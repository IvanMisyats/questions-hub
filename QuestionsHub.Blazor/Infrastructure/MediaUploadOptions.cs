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
    /// Base path for all uploads.
    /// In development, relative to project root.
    /// In production, typically /app/uploads.
    /// </summary>
    public string UploadsPath { get; set; } = string.Empty;

    /// <summary>
    /// Subfolder within uploads path for question handouts and comment attachments.
    /// Files are stored in {UploadsPath}/{HandoutsFolder}/
    /// </summary>
    public string HandoutsFolder { get; set; } = "handouts";

    /// <summary>
    /// Subfolder within uploads path for original package files (docx, pdf).
    /// Files are stored in {UploadsPath}/{PackagesFolder}/
    /// This folder is not publicly accessible.
    /// </summary>
    public string PackagesFolder { get; set; } = "packages";

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

