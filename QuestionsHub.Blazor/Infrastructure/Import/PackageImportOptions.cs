namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Configuration options for package import feature.
/// </summary>
public class PackageImportOptions
{
    public const string SectionName = "PackageImport";

    /// <summary>
    /// Maximum allowed file size in bytes.
    /// Default: 50 MB
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>
    /// Allowed file extensions for upload.
    /// </summary>
    public string[] AllowedExtensions { get; set; } = [".docx"];

    /// <summary>
    /// Maximum time allowed for a single job in minutes.
    /// </summary>
    public int JobTimeoutMinutes { get; set; } = 10;

    /// <summary>
    /// Maximum number of concurrent import jobs.
    /// </summary>
    public int MaxConcurrentJobs { get; set; } = 2;


    /// <summary>
    /// Subfolder within uploads path for import jobs.
    /// </summary>
    public string JobsFolder { get; set; } = "jobs";

    /// <summary>
    /// Maximum number of retry attempts for retriable errors.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Validates if the file extension is allowed.
    /// </summary>
    public bool IsExtensionAllowed(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return AllowedExtensions.Contains(extension);
    }

    /// <summary>
    /// Formats file size for display.
    /// </summary>
    public static string FormatFileSize(long bytes)
    {
        string[] sizes = ["Б", "КБ", "МБ", "ГБ"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}

