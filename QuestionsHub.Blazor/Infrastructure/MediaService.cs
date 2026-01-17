namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Result of a media file upload operation.
/// </summary>
public record MediaUploadResult(
    bool Success,
    string? FileName,
    string? RelativeUrl,
    string? ErrorMessage
);

/// <summary>
/// Service for handling media file uploads and management.
/// </summary>
public class MediaService
{
    private readonly MediaUploadOptions _options;
    private readonly ILogger<MediaService> _logger;
    private readonly string _handoutsPath;

    public MediaService(MediaUploadOptions options, ILogger<MediaService> logger)
    {
        _options = options;
        _logger = logger;
        _handoutsPath = Path.Combine(options.UploadsPath, options.HandoutsFolder);

        // Ensure handouts directory exists
        if (!Directory.Exists(_handoutsPath))
        {
            Directory.CreateDirectory(_handoutsPath);
            _logger.LogInformation("Created handouts directory: {HandoutsPath}", _handoutsPath);
        }
    }

    public (bool IsValid, string? ErrorMessage) ValidateFile(string fileName, long fileSize)
    {
        // Check extension
        var extension = Path.GetExtension(fileName);
        if (!MediaSecurityOptions.IsAllowedMediaFile(fileName))
        {
            return (false, $"Недозволений тип файлу: {extension}. Дозволені типи: {string.Join(", ", MediaSecurityOptions.AllowedMediaExtensions)}");
        }

        // Check file size based on media type
        var mediaType = MediaSecurityOptions.GetMediaType(fileName);
        var maxSize = _options.GetMaxSizeForMediaType(mediaType);

        if (fileSize > maxSize)
        {
            return (false, $"Файл занадто великий. Максимальний розмір для {GetMediaTypeName(mediaType)}: {MediaUploadOptions.FormatFileSize(maxSize)}");
        }

        if (fileSize == 0)
        {
            return (false, "Файл порожній");
        }

        return (true, null);
    }

    public async Task<MediaUploadResult> UploadAsync(Stream fileStream, string originalFileName)
    {
        try
        {
            var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
            var newFileName = MediaSecurityOptions.GenerateRandomFileName(extension);

            var filePath = Path.Combine(_handoutsPath, newFileName);

            // Write file to disk
            await using var outputStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            await fileStream.CopyToAsync(outputStream);

            var relativeUrl = $"/media/{newFileName}";

            _logger.LogInformation("Uploaded media file: {FileName} -> {NewFileName}", originalFileName, newFileName);

            return new MediaUploadResult(true, newFileName, relativeUrl, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file: {FileName}", originalFileName);
            return new MediaUploadResult(false, null, null, "Помилка завантаження файлу");
        }
    }

    public bool Delete(string? relativeUrl)
    {
        if (string.IsNullOrEmpty(relativeUrl))
            return false;

        try
        {
            // Extract filename from relative URL
            // Expected format: /media/filename.ext
            const string expectedPrefix = "/media/";
            if (!relativeUrl.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Attempted to delete file outside media folder: {Url}", relativeUrl);
                return false;
            }

            var fileName = relativeUrl[expectedPrefix.Length..];

            // Security: Ensure no path traversal
            if (fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
            {
                _logger.LogWarning("Attempted path traversal in delete: {Url}", relativeUrl);
                return false;
            }

            var filePath = Path.Combine(_handoutsPath, fileName);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("Deleted media file: {FileName}", fileName);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file: {Url}", relativeUrl);
            return false;
        }
    }

    public bool Exists(string? relativeUrl)
    {
        if (string.IsNullOrEmpty(relativeUrl))
            return false;

        try
        {
            const string expectedPrefix = "/media/";
            if (!relativeUrl.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            var fileName = relativeUrl[expectedPrefix.Length..];

            if (fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
                return false;

            var filePath = Path.Combine(_handoutsPath, fileName);
            return File.Exists(filePath);
        }
        catch
        {
            return false;
        }
    }

    private static string GetMediaTypeName(MediaType mediaType) => mediaType switch
    {
        MediaType.Image => "зображень",
        MediaType.Video => "відео",
        MediaType.Audio => "аудіо",
        _ => "файлів"
    };
}

