using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.Blazor.Infrastructure.Export;

namespace QuestionsHub.Blazor.Controllers;

/// <summary>
/// API controller for exporting packages in .qhub format.
/// </summary>
[Route("api/packages")]
[ApiController]
[Authorize(Roles = "Editor,Admin")]
public class PackageExportController : ControllerBase
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

        var package = await PackageGraph.LoadSnapshot(db, db.Packages.AsNoTracking(), id, ct);

        if (package == null)
            return NotFound();

        if (!User.CanAccessPackage(package))
            return Forbid();

        _logger.LogInformation("Exporting package {PackageId} ({Title}) as .qhub", id, package.Title);

        var stream = await _exporter.Export(package, ct);

        var fileName = ExportFileName.FromTitle(package.Title) + ".qhub";
        return File(stream, "application/zip", fileName);
    }
}
