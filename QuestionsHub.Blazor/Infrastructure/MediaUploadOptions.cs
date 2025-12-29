namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Configuration options for media file uploads.
/// </summary>
public class MediaUploadOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "MediaUpload";

    /// <summary>
    /// Maximum file size for image uploads in bytes.
    /// Default: 10 MB
    /// </summary>
    public long MaxImageSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Maximum file size for video uploads in bytes.
    /// Default: 50 MB
    /// </summary>
    public long MaxVideoSizeBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>
    /// Maximum file size for audio uploads in bytes.
    /// Default: 10 MB
    /// </summary>
    public long MaxAudioSizeBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>
    /// Base path for media uploads folder.
    /// In development, relative to project root.
    /// In production, typically /app/media.
    /// </summary>
    public string MediaPath { get; set; } = string.Empty;

    /// <summary>
    /// Subfolder within media path for uploaded files.
    /// Uploaded files are stored in {MediaPath}/{UploadsFolder}/
    /// </summary>
    public string UploadsFolder { get; set; } = "uploads";

    /// <summary>
    /// Gets the maximum allowed file size for a given media type.
    /// </summary>
    public long GetMaxSizeForMediaType(MediaType mediaType) => mediaType switch
    {
        MediaType.Image => MaxImageSizeBytes,
        MediaType.Video => MaxVideoSizeBytes,
        MediaType.Audio => MaxAudioSizeBytes,
        _ => MaxImageSizeBytes // Default to image size for unknown types
    };

    /// <summary>
    /// Formats a file size in bytes to a human-readable string.
    /// </summary>
    public static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}

