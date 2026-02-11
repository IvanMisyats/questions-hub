using System.Globalization;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Import;

namespace QuestionsHub.Blazor.Infrastructure.Export;

/// <summary>
/// Exports a <see cref="Package"/> entity to a .qhub ZIP archive (package.json + assets/).
/// Reverse of <see cref="QhubExtractor"/>.
/// </summary>
public class QhubExporter
{
    private readonly MediaUploadOptions _mediaOptions;
    private readonly ILogger<QhubExporter> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public QhubExporter(MediaUploadOptions mediaOptions, ILogger<QhubExporter> logger)
    {
        _mediaOptions = mediaOptions;
        _logger = logger;
    }

    /// <summary>
    /// Exports the package to a .qhub ZIP archive (MemoryStream).
    /// The package must be fully loaded with Tours, Blocks, Questions, Authors, Editors, Tags.
    /// </summary>
    public async Task<MemoryStream> Export(Package package, CancellationToken ct = default)
    {
        var assetFiles = new List<(string FileName, string PhysicalPath)>();

        var qhubPackage = MapToQhubPackage(package, assetFiles);

        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Write package.json
            var jsonEntry = zip.CreateEntry("package.json", CompressionLevel.Optimal);
            await using (var jsonStream = jsonEntry.Open())
            {
                await JsonSerializer.SerializeAsync(jsonStream, qhubPackage, JsonOptions, ct);
            }

            // Copy local asset files into assets/ folder
            foreach (var (fileName, physicalPath) in assetFiles)
            {
                if (!File.Exists(physicalPath))
                {
                    _logger.LogWarning("Asset file not found during export: {Path}", physicalPath);
                    continue;
                }

                var assetEntry = zip.CreateEntry($"assets/{fileName}", CompressionLevel.Optimal);
                await using var source = File.OpenRead(physicalPath);
                await using var dest = assetEntry.Open();
                await source.CopyToAsync(dest, ct);
            }
        }

        ms.Position = 0;
        return ms;
    }

    private QhubPackage MapToQhubPackage(Package package, List<(string FileName, string PhysicalPath)> assetFiles)
    {
        var editors = package.SharedEditors && package.PackageEditors.Count > 0
            ? package.PackageEditors.Select(a => a.FullName).ToList()
            : null;

        var tags = package.Tags.Count > 0
            ? package.Tags.Select(t => t.Name).ToList()
            : null;

        return new QhubPackage
        {
            FormatVersion = "1.0",
            SourceUrl = NullIfEmpty(package.SourceUrl),
            Title = package.Title,
            Description = NullIfEmpty(package.Description),
            Preamble = NullIfEmpty(package.Preamble),
            PlayedFrom = package.PlayedFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PlayedTo = package.PlayedTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            NumberingMode = package.NumberingMode == QuestionNumberingMode.Global ? null : package.NumberingMode.ToString(),
            SharedEditors = package.SharedEditors ? true : null,
            Editors = editors,
            Tags = tags,
            Tours = package.Tours
                .OrderBy(t => t.OrderIndex)
                .Select(t => MapTour(t, assetFiles))
                .ToList()
        };
    }

    private QhubTour MapTour(Tour tour, List<(string FileName, string PhysicalPath)> assetFiles)
    {
        var editors = tour.Editors.Count > 0
            ? tour.Editors.Select(a => a.FullName).ToList()
            : null;

        var hasBlocks = tour.Blocks.Count > 0;

        return new QhubTour
        {
            Number = tour.Number,
            IsWarmup = tour.IsWarmup ? true : null,
            Editors = editors,
            Preamble = NullIfEmpty(tour.Preamble),
            Comment = NullIfEmpty(tour.Comment),
            Questions = !hasBlocks && tour.Questions.Count > 0
                ? tour.Questions
                    .OrderBy(q => q.OrderIndex)
                    .Select(q => MapQuestion(q, assetFiles))
                    .ToList()
                : null,
            Blocks = hasBlocks
                ? tour.Blocks
                    .OrderBy(b => b.OrderIndex)
                    .Select(b => MapBlock(b, assetFiles))
                    .ToList()
                : null
        };
    }

    private QhubBlock MapBlock(Block block, List<(string FileName, string PhysicalPath)> assetFiles)
    {
        var editors = block.Editors.Count > 0
            ? block.Editors.Select(a => a.FullName).ToList()
            : null;

        return new QhubBlock
        {
            Name = NullIfEmpty(block.Name),
            Editors = editors,
            Preamble = NullIfEmpty(block.Preamble),
            Questions = block.Questions
                .OrderBy(q => q.OrderIndex)
                .Select(q => MapQuestion(q, assetFiles))
                .ToList()
        };
    }

    private QhubQuestion MapQuestion(Question question, List<(string FileName, string PhysicalPath)> assetFiles)
    {
        var (handoutAssetFileName, handoutAssetUrl) = ResolveAssetForExport(question.HandoutUrl, assetFiles);
        var (commentAssetFileName, commentAssetUrl) = ResolveAssetForExport(question.CommentAttachmentUrl, assetFiles);

        var authors = question.Authors.Count > 0
            ? question.Authors.Select(a => a.FullName).ToList()
            : null;

        return new QhubQuestion
        {
            Number = question.Number,
            HostInstructions = NullIfEmpty(question.HostInstructions),
            HandoutText = NullIfEmpty(question.HandoutText),
            HandoutAssetFileName = handoutAssetFileName,
            HandoutAssetUrl = handoutAssetUrl,
            Text = question.Text,
            Answer = question.Answer,
            AcceptedAnswers = NullIfEmpty(question.AcceptedAnswers),
            RejectedAnswers = NullIfEmpty(question.RejectedAnswers),
            Comment = NullIfEmpty(question.Comment),
            CommentAssetFileName = commentAssetFileName,
            CommentAssetUrl = commentAssetUrl,
            Source = NullIfEmpty(question.Source),
            Authors = authors
        };
    }

    /// <summary>
    /// Determines whether a media URL is a local file (to include in assets/) or an external URL.
    /// Local URLs look like "/media/filename.ext".
    /// External URLs start with "http://" or "https://".
    /// </summary>
    private (string? AssetFileName, string? AssetUrl) ResolveAssetForExport(
        string? mediaUrl,
        List<(string FileName, string PhysicalPath)> assetFiles)
    {
        if (string.IsNullOrWhiteSpace(mediaUrl))
            return (null, null);

        // External URL — store as-is
        if (mediaUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            mediaUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return (null, mediaUrl);
        }

        // Local file — extract filename and resolve physical path
        var fileName = Path.GetFileName(mediaUrl);
        if (string.IsNullOrEmpty(fileName))
            return (null, null);

        var physicalPath = Path.Combine(
            _mediaOptions.UploadsPath,
            _mediaOptions.HandoutsFolder,
            fileName);

        // Avoid duplicates in the asset list
        if (!assetFiles.Any(a => a.FileName == fileName))
        {
            assetFiles.Add((fileName, physicalPath));
        }

        return (fileName, null);
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
