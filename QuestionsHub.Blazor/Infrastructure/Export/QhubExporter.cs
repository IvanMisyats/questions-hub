using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.Blazor.Infrastructure.Import;
using QuestionsHub.Blazor.Infrastructure.Media;

namespace QuestionsHub.Blazor.Infrastructure.Export;

/// <summary>
/// Exports a <see cref="Package"/> entity to a .qhub ZIP archive (package.json + assets/).
/// Reverse of <see cref="QhubExtractor"/>.
/// </summary>
public class QhubExporter
{
    private readonly MediaUploadOptions _mediaOptions;
    private readonly ILogger<QhubExporter> _logger;

    // Bump when the archive layout changes in a way package.json does not show, so backups re-download.
    private static readonly byte[] ETagRevision = "qhub-export-1"u8.ToArray();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public QhubExporter(IOptions<MediaUploadOptions> mediaOptions, ILogger<QhubExporter> logger)
    {
        _mediaOptions = mediaOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Exports the package to a .qhub ZIP archive (MemoryStream).
    /// The package must be loaded with <see cref="PackageGraph.WithEditableGraph"/>.
    /// </summary>
    public async Task<MemoryStream> Export(Package package, CancellationToken ct = default)
    {
        var ms = new MemoryStream();
        await WriteArchive(BuildManifest(package), ms, ct);
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// The archive's <c>package.json</c> and the local media files it references. The JSON is canonical:
    /// the same content always produces the same bytes (people ordered by id, tags by name).
    /// The package must be loaded with <see cref="PackageGraph.WithEditableGraph"/>.
    /// </summary>
    public QhubManifest BuildManifest(Package package)
    {
        var assetFiles = new List<(string FileName, string PhysicalPath)>();
        var qhubPackage = MapToQhubPackage(package, assetFiles);
        return new QhubManifest(JsonSerializer.SerializeToUtf8Bytes(qhubPackage, JsonOptions), assetFiles);
    }

    /// <summary>
    /// A weak entity tag for the archive of <paramref name="manifest"/>: SHA-256 over the
    /// <c>package.json</c> bytes and the length and modification time of every local media file (or that
    /// it is missing). Media files get fresh names when replaced, so this changes whenever the exported
    /// content does; it is weak because the ZIP bytes themselves (entry timestamps) differ between builds.
    /// </summary>
    public static string ComputeETag(QhubManifest manifest)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(ETagRevision);
        hash.AppendData(manifest.Json);
        foreach (var (fileName, physicalPath) in manifest.Assets.OrderBy(a => a.FileName, StringComparer.Ordinal))
        {
            var file = new FileInfo(physicalPath);
            var state = file.Exists
                ? string.Create(CultureInfo.InvariantCulture, $"{file.Length}:{file.LastWriteTimeUtc.Ticks}")
                : "missing";
            hash.AppendData(Encoding.UTF8.GetBytes($"\n{fileName}\0{state}"));
        }

        return $"W/\"{Convert.ToHexStringLower(hash.GetHashAndReset())}\"";
    }

    /// <summary>
    /// Writes the ZIP archive (package.json + assets/) to <paramref name="output"/>. Local media files that
    /// no longer exist are skipped and logged; returns how many were skipped.
    /// </summary>
    public async Task<int> WriteArchive(QhubManifest manifest, Stream output, CancellationToken ct = default)
    {
        var missing = 0;
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Every entry is stored, not deflated, so an export always passes the importer's compression-ratio
            // limit (a package of repetitive text can deflate past 100×); package.json is a few hundred KB.
            var jsonEntry = zip.CreateEntry("package.json", CompressionLevel.NoCompression);
            await using (var jsonStream = jsonEntry.Open())
            {
                await jsonStream.WriteAsync(manifest.Json, ct);
            }

            // Copy local asset files into assets/ folder
            foreach (var (fileName, physicalPath) in manifest.Assets)
            {
                if (!File.Exists(physicalPath))
                {
                    _logger.LogWarning("Asset file not found during export: {Path}", physicalPath);
                    missing++;
                    continue;
                }

                // Stored as well (media is mostly compressed already; a WAV of silence would deflate past 100×).
                var assetEntry = zip.CreateEntry($"assets/{fileName}", CompressionLevel.NoCompression);
                await using var source = File.OpenRead(physicalPath);
                await using var dest = assetEntry.Open();
                await source.CopyToAsync(dest, ct);
            }
        }

        return missing;
    }

    private QhubPackage MapToQhubPackage(Package package, List<(string FileName, string PhysicalPath)> assetFiles)
    {
        var editors = package.SharedEditors && package.PackageEditors.Count > 0
            ? package.PackageEditors.OrderBy(a => a.Id).Select(QhubPeople.Encode).ToList()
            : null;

        var tags = package.Tags.Count > 0
            ? package.Tags.Select(t => t.Name).Order(StringComparer.Ordinal).ToList()
            : null;

        return new QhubPackage
        {
            FormatVersion = "1.2",
            GameType = package.Type == PackageType.Shvager ? "shvager" : null,
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
            ? tour.Editors.OrderBy(a => a.Id).Select(QhubPeople.Encode).ToList()
            : null;

        var hasBlocks = tour.Blocks.Count > 0;

        return new QhubTour
        {
            Number = tour.Number,
            Title = NullIfEmpty(tour.Title),
            IsWarmup = tour.IsWarmup ? true : null,
            IsShootout = tour.IsShootout ? true : null,
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
                    .Select(b => MapBlock(tour, b, assetFiles))
                    .ToList()
                : null
        };
    }

    // Block questions are taken from the tour: the editable graph reaches every question through
    // Tour.Questions and does not populate Block.Questions.
    private QhubBlock MapBlock(Tour tour, Block block, List<(string FileName, string PhysicalPath)> assetFiles)
    {
        var editors = block.Editors.Count > 0
            ? block.Editors.OrderBy(a => a.Id).Select(QhubPeople.Encode).ToList()
            : null;

        return new QhubBlock
        {
            Name = NullIfEmpty(block.Name),
            Editors = editors,
            Preamble = NullIfEmpty(block.Preamble),
            Questions = PackageGraph.BlockQuestions(tour, block)
                .Select(q => MapQuestion(q, assetFiles))
                .ToList()
        };
    }

    private QhubQuestion MapQuestion(Question question, List<(string FileName, string PhysicalPath)> assetFiles)
    {
        var (handoutAssetFileName, handoutAssetUrl) = ResolveAssetForExport(question.HandoutUrl, assetFiles);
        var (commentAssetFileName, commentAssetUrl) = ResolveAssetForExport(question.CommentAttachmentUrl, assetFiles);

        var authors = question.Authors.Count > 0
            ? question.Authors.OrderBy(a => a.Id).Select(QhubPeople.Encode).ToList()
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
            Form = NullIfEmpty(question.AnswerForm),
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

/// <summary>The <c>package.json</c> of an export and the local media files (name in assets/, path on disk) it references.</summary>
public sealed record QhubManifest(byte[] Json, IReadOnlyList<(string FileName, string PhysicalPath)> Assets);
