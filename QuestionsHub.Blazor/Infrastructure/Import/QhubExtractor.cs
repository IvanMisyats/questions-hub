using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Extracts and converts .qhub (ZIP with package.json + assets/) into a <see cref="ParseResult"/>.
/// </summary>
public class QhubExtractor
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<QhubExtractor> _logger;

    /// <summary>Maximum download size per external asset (20 MB).</summary>
    private const long MaxAssetDownloadBytes = 20 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public QhubExtractor(IHttpClientFactory httpClientFactory, ILogger<QhubExtractor> logger)
    {
        _httpClient = httpClientFactory.CreateClient("QhubAssetDownloader");
        _logger = logger;
    }

    /// <summary>
    /// Extracts a .qhub file and produces a <see cref="ParseResult"/>.
    /// </summary>
    /// <param name="qhubFilePath">Path to the .qhub ZIP archive.</param>
    /// <param name="assetsOutputPath">Folder to write extracted/downloaded assets to.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ParseResult> Extract(string qhubFilePath, string assetsOutputPath, CancellationToken ct)
    {
        Directory.CreateDirectory(assetsOutputPath);

        QhubPackage package;
        using (var zip = ZipFile.OpenRead(qhubFilePath))
        {
            // Find package.json
            var packageJsonEntry = zip.GetEntry("package.json")
                ?? throw new ExtractionException("Файл package.json не знайдено в архіві .qhub");

            // Deserialize
            await using var stream = packageJsonEntry.Open();
            package = await JsonSerializer.DeserializeAsync<QhubPackage>(stream, JsonOptions, ct)
                ?? throw new ExtractionException("Не вдалося прочитати package.json");

            // Extract local assets from assets/ folder
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileName = Path.GetFileName(entry.FullName);
                if (string.IsNullOrEmpty(fileName))
                    continue; // skip directory entries

                var destPath = Path.Combine(assetsOutputPath, fileName);
                entry.ExtractToFile(destPath, overwrite: true);
            }
        }

        return await MapToParseResult(package, assetsOutputPath, ct);
    }

    /// <summary>
    /// Extracts from a stream (for testing or direct stream processing).
    /// </summary>
    public async Task<ParseResult> Extract(Stream qhubStream, string assetsOutputPath, CancellationToken ct)
    {
        Directory.CreateDirectory(assetsOutputPath);

        QhubPackage package;
        using (var zip = new ZipArchive(qhubStream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var packageJsonEntry = zip.GetEntry("package.json")
                ?? throw new ExtractionException("Файл package.json не знайдено в архіві .qhub");

            await using var stream = packageJsonEntry.Open();
            package = await JsonSerializer.DeserializeAsync<QhubPackage>(stream, JsonOptions, ct)
                ?? throw new ExtractionException("Не вдалося прочитати package.json");

            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileName = Path.GetFileName(entry.FullName);
                if (string.IsNullOrEmpty(fileName))
                    continue;

                var destPath = Path.Combine(assetsOutputPath, fileName);
                entry.ExtractToFile(destPath, overwrite: true);
            }
        }

        return await MapToParseResult(package, assetsOutputPath, ct);
    }

    private async Task<ParseResult> MapToParseResult(QhubPackage package, string assetsOutputPath, CancellationToken ct)
    {
        var warnings = new List<string>();

        // Validate format version
        if (string.IsNullOrWhiteSpace(package.FormatVersion))
        {
            warnings.Add("Відсутнє поле formatVersion");
        }
        else if (package.FormatVersion != "1.0")
        {
            warnings.Add($"Невідома версія формату: {package.FormatVersion}. Очікувалося 1.0");
        }

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
            var tourDto = await MapTour(qhubTour, i, assetsOutputPath, warnings, ct);
            tours.Add(tourDto);
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
        List<string> warnings, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(qhubTour.Number))
        {
            warnings.Add($"Тур {index + 1}: відсутній номер туру");
        }

        var hasQuestions = qhubTour.Questions is { Count: > 0 };
        var hasBlocks = qhubTour.Blocks is { Count: > 0 };

        if (!hasQuestions && !hasBlocks)
        {
            warnings.Add($"Тур {qhubTour.Number ?? (index + 1).ToString()}: не містить запитань");
        }

        var tourDto = new TourDto
        {
            Number = qhubTour.Number ?? (index + 1).ToString(),
            OrderIndex = index,
            IsWarmup = qhubTour.IsWarmup ?? false,
            Editors = qhubTour.Editors ?? [],
            Preamble = qhubTour.Preamble
        };

        // Map blocks
        if (hasBlocks)
        {
            for (var i = 0; i < qhubTour.Blocks!.Count; i++)
            {
                var qhubBlock = qhubTour.Blocks[i];
                var blockDto = await MapBlock(qhubBlock, i, tourDto.Number, assetsOutputPath, warnings, ct);
                tourDto.Blocks.Add(blockDto);
            }
        }

        // Map direct questions (outside blocks)
        if (hasQuestions)
        {
            for (var i = 0; i < qhubTour.Questions!.Count; i++)
            {
                var qhubQuestion = qhubTour.Questions[i];
                var questionDto = await MapQuestion(qhubQuestion, i, tourDto.Number, assetsOutputPath, warnings, ct);
                tourDto.Questions.Add(questionDto);
            }
        }

        return tourDto;
    }

    private async Task<BlockDto> MapBlock(
        QhubBlock qhubBlock, int index, string tourNumber, string assetsOutputPath,
        List<string> warnings, CancellationToken ct)
    {
        if (qhubBlock.Questions == null || qhubBlock.Questions.Count == 0)
        {
            warnings.Add($"Тур {tourNumber}, блок {index + 1}: не містить запитань");
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
                    qhubQuestion, i, $"{tourNumber}/блок {index + 1}", assetsOutputPath, warnings, ct);
                blockDto.Questions.Add(questionDto);
            }
        }

        return blockDto;
    }

    private async Task<QuestionDto> MapQuestion(
        QhubQuestion qhubQuestion, int index, string context, string assetsOutputPath,
        List<string> warnings, CancellationToken ct)
    {
        var questionLabel = $"Тур {context}, запитання {qhubQuestion.Number ?? (index + 1).ToString()}";

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
            assetsOutputPath, questionLabel, "роздатка", warnings, ct);

        // Resolve comment asset
        var commentAssetFileName = await ResolveAsset(
            qhubQuestion.CommentAssetFileName,
            qhubQuestion.CommentAssetUrl,
            assetsOutputPath, questionLabel, "коментар", warnings, ct);

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
            Comment = NullIfEmpty(qhubQuestion.Comment),
            CommentAssetFileName = commentAssetFileName,
            Source = NullIfEmpty(qhubQuestion.Source),
            Authors = qhubQuestion.Authors ?? []
        };
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
        List<string> warnings,
        CancellationToken ct)
    {
        // Local file takes precedence
        if (!string.IsNullOrWhiteSpace(localFileName))
        {
            var localPath = Path.Combine(assetsOutputPath, localFileName);
            if (File.Exists(localPath))
                return localFileName;

            warnings.Add($"{questionLabel}: файл {assetType} '{localFileName}' не знайдено в архіві");
        }

        // Try external URL
        if (!string.IsNullOrWhiteSpace(externalUrl))
        {
            return await DownloadAsset(externalUrl, assetsOutputPath, questionLabel, assetType, warnings, ct);
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
        List<string> warnings,
        CancellationToken ct)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                warnings.Add($"{questionLabel}: некоректне посилання на {assetType}: {url}");
                return null;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                warnings.Add($"{questionLabel}: не вдалося завантажити {assetType} ({(int)response.StatusCode}): {url}");
                return null;
            }

            // Check content length
            if (response.Content.Headers.ContentLength > MaxAssetDownloadBytes)
            {
                warnings.Add($"{questionLabel}: файл {assetType} завеликий ({response.Content.Headers.ContentLength / 1024 / 1024} МБ): {url}");
                return null;
            }

            // Determine file name from URL
            var extension = GetExtensionFromUrl(url, response.Content.Headers.ContentType?.MediaType);
            var fileName = $"dl_{Guid.NewGuid():N}{extension}";
            var destPath = Path.Combine(assetsOutputPath, fileName);

            // Download with size limit
            await using var responseStream = await response.Content.ReadAsStreamAsync(cts.Token);
            await using var fileStream = File.Create(destPath);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await responseStream.ReadAsync(buffer, cts.Token)) > 0)
            {
                totalRead += bytesRead;
                if (totalRead > MaxAssetDownloadBytes)
                {
                    // Clean up oversized file
                    fileStream.Close();
                    File.Delete(destPath);
                    warnings.Add($"{questionLabel}: файл {assetType} перевищив ліміт 20 МБ: {url}");
                    return null;
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token);
            }

            _logger.LogDebug("Downloaded asset {Url} -> {FileName} ({Size} bytes)", url, fileName, totalRead);
            return fileName;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            warnings.Add($"{questionLabel}: таймаут завантаження {assetType}: {url}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            warnings.Add($"{questionLabel}: помилка завантаження {assetType}: {url} ({ex.Message})");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download asset {Url}", url);
            warnings.Add($"{questionLabel}: помилка завантаження {assetType}: {url}");
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

    private static DateOnly? ParseDate(string? dateStr, string fieldName, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        if (DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;

        warnings.Add($"Некоректний формат дати {fieldName}: '{dateStr}'. Очікувався YYYY-MM-DD.");
        return null;
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
