using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Infrastructure.Export;

namespace QuestionsHub.Blazor.Controllers;

/// <summary>
/// API controller for exporting packages in .qhub format.
/// </summary>
[Route("api/packages")]
[ApiController]
[Authorize(Roles = "Editor,Admin")]
public partial class PackageExportController : ControllerBase
{
    private readonly IDbContextFactory<QuestionsHubDbContext> _dbContextFactory;
    private readonly QhubExporter _exporter;
    private readonly ILogger<PackageExportController> _logger;

    public PackageExportController(
        IDbContextFactory<QuestionsHubDbContext> dbContextFactory,
        QhubExporter exporter,
        ILogger<PackageExportController> logger)
    {
        _dbContextFactory = dbContextFactory;
        _exporter = exporter;
        _logger = logger;
    }

    /// <summary>
    /// Exports a package in .qhub format (ZIP archive with package.json + assets/).
    /// </summary>
    [HttpGet("{id:int}/export")]
    public async Task<IActionResult> ExportPackage(int id, CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var package = await db.Packages
            .Include(p => p.PackageEditors)
            .Include(p => p.Tags)
            .Include(p => p.Tours)
                .ThenInclude(t => t.Editors)
            .Include(p => p.Tours)
                .ThenInclude(t => t.Blocks)
                    .ThenInclude(b => b.Editors)
            .Include(p => p.Tours)
                .ThenInclude(t => t.Blocks)
                    .ThenInclude(b => b.Questions)
                        .ThenInclude(q => q.Authors)
            .Include(p => p.Tours)
                .ThenInclude(t => t.Questions)
                    .ThenInclude(q => q.Authors)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (package == null)
            return NotFound();

        if (!User.CanAccessPackage(package))
            return Forbid();

        _logger.LogInformation("Exporting package {PackageId} ({Title}) as .qhub", id, package.Title);

        var stream = await _exporter.Export(package, ct);

        var fileName = SanitizeFileName(package.Title) + ".qhub";
        return File(stream, "application/zip", fileName);
    }

    /// <summary>
    /// Sanitizes a string for use as a file name.
    /// Removes invalid characters and limits length.
    /// </summary>
    private static string SanitizeFileName(string title)
    {
        // Remove characters that are invalid in file names
        var sanitized = InvalidFileNameCharsRegex().Replace(title, "_");

        // Collapse multiple underscores
        sanitized = MultipleUnderscoresRegex().Replace(sanitized, "_").Trim('_');

        // Limit length
        if (sanitized.Length > 100)
            sanitized = sanitized[..100].TrimEnd('_');

        return string.IsNullOrWhiteSpace(sanitized) ? "package" : sanitized;
    }

    [GeneratedRegex(@"[<>:""/\\|?*\x00-\x1F]")]
    private static partial Regex InvalidFileNameCharsRegex();

    [GeneratedRegex(@"_+")]
    private static partial Regex MultipleUnderscoresRegex();
}
