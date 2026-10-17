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
    public string[] AllowedExtensions { get; set; } = [".docx", ".qhub"];

    /// <summary>
    /// Maximum time allowed for a single job in minutes.
    /// </summary>
    public int JobTimeoutMinutes { get; set; } = 10;

    /// <summary>
    /// Maximum number of concurrent import jobs.
    /// </summary>
    public int MaxConcurrentJobs { get; set; } = 1;

    /// <summary>Maximum queued, running or retry-pending imports admitted per agent user.</summary>
    public int MaxActiveImportsPerUser { get; set; } = 3;

    // Expanded sizes are bytes; these limits also apply to actual reads.
    public int MaxArchiveEntries { get; set; } = 1024;
    public long MaxExpandedBytes { get; set; } = 100 * 1024 * 1024;
    public long MaxPackageJsonBytes { get; set; } = 4 * 1024 * 1024;
    public long MaxDocxMainXmlBytes { get; set; } = 4 * 1024 * 1024;
    public long MaxDocxOtherXmlBytes { get; set; } = 1024 * 1024;
    public long MaxDocxTotalXmlBytes { get; set; } = 8 * 1024 * 1024;
    public int MaxXmlDepth { get; set; } = 64;
    public int MaxXmlElementsPerPart { get; set; } = 200_000;
    public int MaxStyleInheritanceDepth { get; set; } = 64;
    public long MaxAssetBytes { get; set; } = 50 * 1024 * 1024;
    public long MaxTotalAssetBytes { get; set; } = 100 * 1024 * 1024;
    public long MaxQhubCompressionRatio { get; set; } = 100;
    public long MaxDocxXmlCompressionRatio { get; set; } = 500;


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

