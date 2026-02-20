using System.Security.Cryptography;

namespace QuestionsHub.Blazor.Infrastructure.Media;

/// <summary>
/// Type of media content.
/// </summary>
public enum MediaType
{
    Image,
    Video,
    Audio,
    Unknown
}

/// <summary>
/// Security configuration and validation for serving media files.
/// </summary>
public static class MediaSecurityOptions
{
    /// <summary>
    /// Allowed file extensions for media files (images, videos, audio).
    /// </summary>
    public static readonly HashSet<string> AllowedMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".webp",

        // Videos
        ".mp4",
        ".webm",

        // Audio
        ".mp3",
        ".ogg",
        ".wav"
    };

    /// <summary>
    /// Mapping of file extensions to MIME content types.
    /// </summary>
    public static readonly Dictionary<string, string> MediaContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png", "image/png" },
        { ".gif", "image/gif" },
        { ".webp", "image/webp" },

        // Videos
        { ".mp4", "video/mp4" },
        { ".webm", "video/webm" },

        // Audio
        { ".mp3", "audio/mpeg" },
        { ".ogg", "audio/ogg" },
        { ".wav", "audio/wav" }
    };

    /// <summary>
    /// Validates if a file path has an allowed media extension.
    /// </summary>
    /// <param name="path">File path or URL to validate.</param>
    /// <returns>True if the file has an allowed extension, false otherwise.</returns>
    public static bool IsAllowedMediaFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var extension = Path.GetExtension(path);
        return AllowedMediaExtensions.Contains(extension);
    }

    /// <summary>
    /// Gets the MIME content type for a given file extension.
    /// </summary>
    /// <param name="extension">File extension (with or without leading dot).</param>
    /// <returns>MIME type string, or null if not found.</returns>
    public static string? GetContentType(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return null;

        // Ensure extension has leading dot
        if (!extension.StartsWith('.'))
            extension = "." + extension;

        return MediaContentTypes.TryGetValue(extension, out var contentType) ? contentType : null;
    }

    /// <summary>
    /// Determines the media type from a file path or URL.
    /// </summary>
    /// <param name="path">File path or URL.</param>
    /// <returns>MediaType enum value.</returns>
    public static MediaType GetMediaType(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return MediaType.Unknown;

        var extension = Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" => MediaType.Image,
            ".mp4" or ".webm" => MediaType.Video,
            ".mp3" or ".ogg" or ".wav" => MediaType.Audio,
            _ => MediaType.Unknown
        };
    }

    /// <summary>
    /// Gets the MIME content type for a media file path or URL.
    /// </summary>
    /// <param name="path">File path or URL.</param>
    /// <returns>MIME type string, or null if not found.</returns>
    public static string? GetContentTypeFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var extension = Path.GetExtension(path);
        return GetContentType(extension);
    }

    /// <summary>
    /// Gets the file extension for a given MIME content type.
    /// </summary>
    /// <param name="contentType">MIME content type (e.g., "image/png").</param>
    /// <returns>File extension with leading dot, or ".bin" if not recognized.</returns>
    public static string GetExtensionFromContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return ".bin";

        return contentType.ToLowerInvariant() switch
        {
            // Images
            "image/png" => ".png",
            "image/jpeg" => ".jpeg",
            "image/jpg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/tiff" => ".tiff",

            // Videos
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",

            // Audio
            "audio/mpeg" => ".mp3",
            "audio/ogg" => ".ogg",
            "audio/wav" => ".wav",

            _ => ".bin"
        };
    }

    /// <summary>
    /// Generates a cryptographically secure random filename with the given extension.
    /// Uses 32 bytes (256 bits) of entropy, making brute-force impractical.
    /// </summary>
    /// <param name="extension">File extension (with or without leading dot).</param>
    /// <returns>Random filename in format: {64-char-hex}.{extension}</returns>
    public static string GenerateRandomFileName(string extension)
    {
        // Ensure extension has leading dot
        if (!string.IsNullOrEmpty(extension) && !extension.StartsWith('.'))
            extension = "." + extension;

        var randomBytes = RandomNumberGenerator.GetBytes(32);
        var randomName = Convert.ToHexString(randomBytes).ToLowerInvariant();
        return $"{randomName}{extension}";
    }
}

