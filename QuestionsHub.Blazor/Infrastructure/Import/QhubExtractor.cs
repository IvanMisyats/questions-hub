using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Options;

using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Media;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Extracts and converts .qhub (ZIP with package.json + assets/) into a <see cref="ParseResult"/>.
/// </summary>
public class QhubExtractor
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<QhubExtractor> _logger;

    private readonly PackageImportOptions _options;

    private sealed class AssetContext(PackageImportOptions options)
    {
        public ExtractionBudget Budget { get; } = new(options.MaxTotalAssetBytes, "усі медіафайли");
        public Dictionary<string, string?> Downloads { get; } = new(StringComparer.Ordinal);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public QhubExtractor(IHttpClientFactory httpClientFactory, ILogger<QhubExtractor> logger, IOptions<PackageImportOptions>? options = null)
    {
        _httpClient = httpClientFactory.CreateClient("QhubAssetDownloader");
        _logger = logger;
        _options = options?.Value ?? new PackageImportOptions();
    }

    /// <summary>
    /// Extracts a .qhub file and produces a <see cref="ParseResult"/>.
    /// </summary>
    /// <param name="qhubFilePath">Path to the .qhub ZIP archive.</param>
    /// <param name="assetsOutputPath">Folder to write extracted/downloaded assets to.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ParseResult> Extract(string qhubFilePath, string assetsOutputPath, CancellationToken ct)
    {
        await using var input = File.OpenRead(qhubFilePath);
        return await Extract(input, assetsOutputPath, ct);
    }

    /// <summary>Extracts from a stream without closing the caller's stream.</summary>
    public async Task<ParseResult> Extract(Stream qhubStream, string assetsOutputPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(assetsOutputPath);
        var assetContext = new AssetContext(_options);
        QhubPackage? package = null;
        ExtractionLimits.CheckCentralDirectory(qhubStream, _options);
        using (var zip = new ZipArchive(qhubStream, ZipArchiveMode.Read, leaveOpen: true))
        {
            ExtractionLimits.CheckArchive(zip, _options);
            var packageEntry = zip.GetEntry("package.json")
                ?? throw new ExtractionException("Файл package.json не знайдено в архіві .qhub");
            var declaredAssets = new ExtractionBudget(_options.MaxTotalAssetBytes, "усі медіафайли");
            foreach (var entry in zip.Entries)
            {
                var isAsset = IsAsset(entry);
                ExtractionLimits.EntryBudget(entry, entry == packageEntry ? _options.MaxPackageJsonBytes
                    : isAsset ? _options.MaxAssetBytes : _options.MaxExpandedBytes, _options.MaxQhubCompressionRatio);
                if (isAsset) declaredAssets.Add(entry.Length);
            }

            var expanded = new ExtractionBudget(_options.MaxExpandedBytes, "розпакований архів");
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var isAsset = IsAsset(entry);
                var entryBudget = ExtractionLimits.EntryBudget(entry, entry == packageEntry ? _options.MaxPackageJsonBytes
                    : isAsset ? _options.MaxAssetBytes : _options.MaxExpandedBytes, _options.MaxQhubCompressionRatio);
                await using var stream = new LimitedExtractionStream(entry.Open(), ct,
                    isAsset ? [entryBudget, expanded, assetContext.Budget] : [entryBudget, expanded]);
                if (entry == packageEntry)
                {
                    package = await JsonSerializer.DeserializeAsync<QhubPackage>(stream, JsonOptions, ct)
                        ?? throw new ExtractionException("Не вдалося прочитати package.json");
                    await stream.CopyToAsync(Stream.Null, ct);
                }
                else if (isAsset)
                {
                    var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(assetsOutputPath)) + Path.DirectorySeparatorChar;
                    var destination = Path.GetFullPath(Path.Combine(assetsOutputPath, Path.GetFileName(entry.FullName)));
                    if (!destination.StartsWith(target, StringComparison.Ordinal))
                        throw new ExtractionException($"Виявлено спробу обходу шляху: '{ImportDiagnostics.Label(entry.FullName)}'");
                    await ExtractionLimits.CopyToFile(stream, destination, ct);
                }
                else
                {
                    // Ignored entries still consume the expanded-byte budget.
                    await stream.CopyToAsync(Stream.Null, ct);
                }
            }
        }
        return await MapToParseResult(package!, assetsOutputPath, assetContext, ct);
    }

    private static bool IsAsset(ZipArchiveEntry entry) =>
        entry.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrEmpty(Path.GetFileName(entry.FullName));

    private async Task<ParseResult> MapToParseResult(QhubPackage package, string assetsOutputPath, AssetContext assetContext, CancellationToken ct)
    {
        var warnings = new ImportWarnings();

        // Validate format version
        if (string.IsNullOrWhiteSpace(package.FormatVersion))
        {
            warnings.Add("Відсутнє поле formatVersion");
        }
        else if (package.FormatVersion is not ("1.0" or "1.1" or "1.2"))
        {
            warnings.Add($"Невідома версія формату: {ImportDiagnostics.Label(package.FormatVersion)}. Очікувалося 1.0, 1.1 або 1.2");
        }

        // Parse game type (added in format 1.1; absent means Що?Де?Коли?)
        var gameType = ParseGameType(package.GameType, warnings);

        // Validate required fields
        if (string.IsNullOrWhiteSpace(package.Title))
        {
            warnings.Add("Назва пакету відсутня");
        }

        if (package.Tours == null || package.Tours.Count == 0)
        {
            throw new ExtractionException("Пакет не містить жодного туру");
        }

        // Parse numbering mode
        var numberingMode = ParseNumberingMode(package.NumberingMode);

        // Parse dates
        var playedFrom = ParseDate(package.PlayedFrom, "playedFrom", warnings);
        var playedTo = ParseDate(package.PlayedTo, "playedTo", warnings);

        // Map tours
        var tours = new List<TourDto>();
        for (var i = 0; i < package.Tours.Count; i++)
        {
            var qhubTour = package.Tours[i];
            var tourDto = await MapTour(qhubTour, i, assetsOutputPath, warnings, assetContext, ct);
            tours.Add(tourDto);
        }

        // Своя гра invariants: no special tours, no blocks
        if (gameType == PackageType.Shvager)
        {
            if (tours.Any(t => t.Blocks.Count > 0))
            {
                throw new ExtractionException("Пакет Своєї гри не може містити блоки");
            }

            foreach (var tour in tours.Where(t => t.Type != TourType.Regular))
            {
                warnings.Add($"Тема {ImportDiagnostics.Label(tour.Number)}: розминка/перестрілка не підтримуються у Своїй грі — тип скинуто");
                tour.Type = TourType.Regular;
            }
        }

        // Determine editors
        var sharedEditors = package.SharedEditors ?? false;
        var packageEditors = package.Editors ?? [];

        // If sharedEditors is true but no package editors, use tour editors as hint
        if (sharedEditors && packageEditors.Count == 0)
        {
            var allEditors = tours
                .SelectMany(t => t.Editors)
                .Distinct()
                .ToList();
            if (allEditors.Count > 0)
            {
                packageEditors = allEditors;
                warnings.Add("SharedEditors=true, але редактори пакету не вказані. Використано редакторів турів.");
            }
        }

        return new ParseResult
        {
            Type = gameType,
            Title = package.Title,
            Description = package.Description,
            Preamble = package.Preamble,
            SourceUrl = package.SourceUrl,
            NumberingMode = numberingMode,
            Tours = tours,
            Warnings = warnings,
            Confidence = 1.0,
            PlayedFrom = playedFrom,
            PlayedTo = playedTo,
            SharedEditors = sharedEditors,
            PackageEditors = packageEditors,
            Tags = package.Tags ?? [],
            // For .qhub, Editors field is used for tour-level editors only (not package)
            Editors = sharedEditors ? [] : tours.SelectMany(t => t.Editors).Distinct().ToList()
        };
    }

    private async Task<TourDto> MapTour(
        QhubTour qhubTour, int index, string assetsOutputPath,
        ImportWarnings warnings, AssetContext assetContext, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(qhubTour.Number))
        {
            warnings.Add($"Тур {index + 1}: відсутній номер туру");
        }

        var hasQuestions = qhubTour.Questions is { Count: > 0 };
        var hasBlocks = qhubTour.Blocks is { Count: > 0 };

        if (qhubTour.Questions != null && qhubTour.Blocks != null)
            throw new ExtractionException($"Тур {ImportDiagnostics.Label(qhubTour.Number ?? (index + 1).ToString())}: не може одночасно містити запитання (questions) і блоки (blocks)");

        if (!hasQuestions && !hasBlocks)
        {
            warnings.Add($"Тур {ImportDiagnostics.Label(qhubTour.Number ?? (index + 1).ToString())}: не містить запитань");
        }

        var tourDto = new TourDto
        {
            Number = qhubTour.Number ?? (index + 1).ToString(),
            Title = qhubTour.Title,
            OrderIndex = index,
            Type = qhubTour.IsShootout == true ? TourType.Shootout
                 : qhubTour.IsWarmup == true ? TourType.Warmup
                 : TourType.Regular,
            Editors = qhubTour.Editors ?? [],
            Preamble = qhubTour.Preamble,
            Comment = qhubTour.Comment
        };

        // Map blocks
        if (hasBlocks)
        {
            for (var i = 0; i < qhubTour.Blocks!.Count; i++)
            {
                var qhubBlock = qhubTour.Blocks[i];
                var blockDto = await MapBlock(qhubBlock, i, tourDto.Number, assetsOutputPath, warnings, assetContext, ct);
                tourDto.Blocks.Add(blockDto);
            }
        }

        // Map direct questions (outside blocks)
        if (hasQuestions)
        {
            for (var i = 0; i < qhubTour.Questions!.Count; i++)
            {
                var qhubQuestion = qhubTour.Questions[i];
                var questionDto = await MapQuestion(qhubQuestion, i, tourDto.Number, assetsOutputPath, warnings, assetContext, ct);
                tourDto.Questions.Add(questionDto);
            }
        }

        return tourDto;
    }

    private async Task<BlockDto> MapBlock(
        QhubBlock qhubBlock, int index, string tourNumber, string assetsOutputPath,
        ImportWarnings warnings, AssetContext assetContext, CancellationToken ct)
    {
        if (qhubBlock.Questions == null || qhubBlock.Questions.Count == 0)
        {
            warnings.Add($"Тур {ImportDiagnostics.Label(tourNumber)}, блок {index + 1}: не містить запитань");
        }

        var blockDto = new BlockDto
        {
            Name = qhubBlock.Name,
            OrderIndex = index,
            Editors = qhubBlock.Editors ?? [],
            Preamble = qhubBlock.Preamble
        };

        if (qhubBlock.Questions != null)
        {
            for (var i = 0; i < qhubBlock.Questions.Count; i++)
            {
                var qhubQuestion = qhubBlock.Questions[i];
                var questionDto = await MapQuestion(
                    qhubQuestion, i, $"{ImportDiagnostics.Label(tourNumber)}/блок {index + 1}", assetsOutputPath, warnings, assetContext, ct);
                blockDto.Questions.Add(questionDto);
            }
        }

        return blockDto;
    }

    private async Task<QuestionDto> MapQuestion(
        QhubQuestion qhubQuestion, int index, string context, string assetsOutputPath,
        ImportWarnings warnings, AssetContext assetContext, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var questionLabel = $"Тур {ImportDiagnostics.Label(context)}, запитання {ImportDiagnostics.Label(qhubQuestion.Number ?? (index + 1).ToString())}";

        if (string.IsNullOrWhiteSpace(qhubQuestion.Number))
        {
            warnings.Add($"{questionLabel}: відсутній номер запитання");
        }

        if (string.IsNullOrWhiteSpace(qhubQuestion.Text))
        {
            warnings.Add($"{questionLabel}: текст запитання відсутній");
        }

        if (string.IsNullOrWhiteSpace(qhubQuestion.Answer))
        {
            warnings.Add($"{questionLabel}: відповідь відсутня");
        }

        // Resolve handout asset
        var handoutAssetFileName = await ResolveAsset(
            qhubQuestion.HandoutAssetFileName,
            qhubQuestion.HandoutAssetUrl,
            assetsOutputPath, questionLabel, "роздатка", warnings, assetContext, ct);

        // Resolve comment asset
        var commentAssetFileName = await ResolveAsset(
            qhubQuestion.CommentAssetFileName,
            qhubQuestion.CommentAssetUrl,
            assetsOutputPath, questionLabel, "коментар", warnings, assetContext, ct);

        return new QuestionDto
        {
            Number = qhubQuestion.Number ?? (index + 1).ToString(),
            HostInstructions = NullIfEmpty(qhubQuestion.HostInstructions),
            HandoutText = NullIfEmpty(qhubQuestion.HandoutText),
            HandoutAssetFileName = handoutAssetFileName,
            Text = qhubQuestion.Text ?? "",
            Answer = qhubQuestion.Answer ?? "",
            AcceptedAnswers = NullIfEmpty(qhubQuestion.AcceptedAnswers),
            RejectedAnswers = NullIfEmpty(qhubQuestion.RejectedAnswers),
            Form = NullIfEmpty(qhubQuestion.Form),
            Comment = NullIfEmpty(qhubQuestion.Comment),
            CommentAssetFileName = commentAssetFileName,
            Source = NullIfEmpty(qhubQuestion.Source),
            Authors = qhubQuestion.Authors ?? []
        };
    }

    /// <summary>
    /// Parses the gameType field ("www" / "shvager", case-insensitive). Absent → Що?Де?Коли?.
    /// </summary>
    private static PackageType ParseGameType(string? gameType, ImportWarnings warnings)
    {
        if (string.IsNullOrWhiteSpace(gameType))
            return PackageType.Www;

        if (gameType.Equals("www", StringComparison.OrdinalIgnoreCase))
            return PackageType.Www;

        if (gameType.Equals("shvager", StringComparison.OrdinalIgnoreCase))
            return PackageType.Shvager;

        warnings.Add($"Невідомий тип гри: {ImportDiagnostics.Label(gameType)}. Очікувалося www або shvager. Використано www");
        return PackageType.Www;
    }

    /// <summary>
    /// Resolves an asset: prefers local file, falls back to downloading external URL.
    /// </summary>
    /// <returns>Local file name in the assets folder, or null if not available.</returns>
    private async Task<string?> ResolveAsset(
        string? localFileName,
        string? externalUrl,
        string assetsOutputPath,
        string questionLabel,
        string assetType,
        ImportWarnings warnings,
        AssetContext assetContext,
        CancellationToken ct)
    {
        // Local file takes precedence. The name comes from package.json: only a bare file name of
        // an allowed media type (no paths, no SVG or other active content)
        if (!string.IsNullOrWhiteSpace(localFileName) && !MediaSecurityOptions.IsAllowedMediaFileName(localFileName))
        {
            warnings.Add($"{questionLabel}: недопустима назва або тип файлу {assetType}: '{ImportDiagnostics.Label(localFileName)}'");
        }
        else if (!string.IsNullOrWhiteSpace(localFileName))
        {
            var localPath = Path.Combine(assetsOutputPath, localFileName);
            if (File.Exists(localPath))
                return localFileName;

            warnings.Add($"{questionLabel}: файл {assetType} '{ImportDiagnostics.Label(localFileName)}' не знайдено в архіві");
        }

        // Try external URL
        if (!string.IsNullOrWhiteSpace(externalUrl))
        {
            ct.ThrowIfCancellationRequested();
            if (!assetContext.Downloads.TryGetValue(externalUrl, out var fileName))
            {
                fileName = await DownloadAsset(externalUrl, assetsOutputPath, questionLabel, assetType, warnings, assetContext, ct);
                assetContext.Downloads.Add(externalUrl, fileName);
            }
            return fileName;
        }

        return null;
    }

    /// <summary>
    /// Downloads an external asset to the assets folder.
    /// </summary>
    private async Task<string?> DownloadAsset(
        string url,
        string assetsOutputPath,
        string questionLabel,
        string assetType,
        ImportWarnings warnings,
        AssetContext assetContext,
        CancellationToken ct)
    {
        // One oversized or slow external file is skipped with a warning, as a missing one is; exceeding the
        // budget of all media, or cancelling the job, fails the import.
        var assetBudget = new ExtractionBudget(_options.MaxAssetBytes, "один медіафайл");
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                warnings.Add($"{questionLabel}: некоректне посилання на {assetType}: {ImportDiagnostics.Label(url)}");
                return null;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                warnings.Add($"{questionLabel}: не вдалося завантажити {assetType} ({(int)response.StatusCode}): {ImportDiagnostics.Label(url)}");
                return null;
            }

            // A declared length over the per-file limit skips the file before anything is read.
            if (response.Content.Headers.ContentLength is long length)
            {
                assetBudget.Check(length);
                assetContext.Budget.Check(length);
            }

            // Determine file name from URL
            var extension = GetExtensionFromUrl(url, response.Content.Headers.ContentType?.MediaType);
            if (!MediaSecurityOptions.AllowedMediaExtensions.Contains(extension))
            {
                warnings.Add($"{questionLabel}: непідтримуваний тип файлу {assetType} ({ImportDiagnostics.Label(extension)}): {ImportDiagnostics.Label(url)}");
                return null;
            }

            var fileName = $"dl_{Guid.NewGuid():N}{extension}";
            var destPath = Path.Combine(assetsOutputPath, fileName);

            await using var responseStream = new LimitedExtractionStream(
                await response.Content.ReadAsStreamAsync(cts.Token), cts.Token, assetContext.Budget, assetBudget);
            await ExtractionLimits.CopyToFile(responseStream, destPath, cts.Token);

            _logger.LogDebug("Downloaded asset {Url} -> {FileName} ({Size} bytes)", url, fileName, assetBudget.Used);
            return fileName;
        }
        catch (ExtractionLimitException ex) when (ex.Budget == assetBudget)
        {
            warnings.Add($"{questionLabel}: файл {assetType} перевищує ліміт {PackageImportOptions.FormatFileSize(_options.MaxAssetBytes)}: {ImportDiagnostics.Label(url)}");
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            warnings.Add($"{questionLabel}: таймаут завантаження {assetType}: {ImportDiagnostics.Label(url)}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            warnings.Add($"{questionLabel}: помилка завантаження {assetType}: {ImportDiagnostics.Label(url)} ({ImportDiagnostics.Label(ex.Message)})");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ExtractionException)
        {
            _logger.LogWarning(ex, "Failed to download asset {Url}", url);
            warnings.Add($"{questionLabel}: помилка завантаження {assetType}: {ImportDiagnostics.Label(url)}");
            return null;
        }
    }

    private static string GetExtensionFromUrl(string url, string? contentType)
    {
        // Try to get extension from URL path
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (!string.IsNullOrEmpty(ext) && ext.Length <= 5)
                return ext;
        }
        catch
        {
            // Ignore URL parsing errors
        }

        // Fall back to content type
        return contentType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "audio/mpeg" => ".mp3",
            "audio/ogg" => ".ogg",
            "audio/wav" => ".wav",
            _ => ".bin"
        };
    }

    private static QuestionNumberingMode ParseNumberingMode(string? mode)
    {
        return mode switch
        {
            "Global" => QuestionNumberingMode.Global,
            "PerTour" => QuestionNumberingMode.PerTour,
            "Manual" => QuestionNumberingMode.Manual,
            null => QuestionNumberingMode.Global,
            _ => QuestionNumberingMode.Global
        };
    }

    private static DateOnly? ParseDate(string? dateStr, string fieldName, ImportWarnings warnings)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        if (DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;

        warnings.Add($"Некоректний формат дати {fieldName}: '{ImportDiagnostics.Label(dateStr)}'. Очікувався YYYY-MM-DD.");
        return null;
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
