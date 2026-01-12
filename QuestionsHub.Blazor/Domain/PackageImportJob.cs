namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Represents a background job for importing a package from DOC/DOCX file.
/// </summary>
public class PackageImportJob
{
    public Guid Id { get; set; }

    /// <summary>User who initiated the import.</summary>
    public required string OwnerId { get; set; }

    /// <summary>Navigation property to the owner.</summary>
    public ApplicationUser Owner { get; set; } = null!;

    // Timestamps

    /// <summary>When the job was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When processing started (null if still queued).</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>When processing finished (null if still running).</summary>
    public DateTime? FinishedAt { get; set; }

    // Status tracking

    /// <summary>Current job status.</summary>
    public ImportJobStatus Status { get; set; } = ImportJobStatus.Queued;

    /// <summary>Current processing step (e.g., "Extracting", "Parsing").</summary>
    public string? CurrentStep { get; set; }

    /// <summary>Progress percentage (0-100).</summary>
    public int Progress { get; set; }

    // Retry tracking

    /// <summary>Number of processing attempts.</summary>
    public int Attempts { get; set; }

    /// <summary>When to retry (for failed jobs that are retriable).</summary>
    public DateTime? NextRetryAt { get; set; }

    // Input file info

    /// <summary>Original uploaded file name.</summary>
    public required string InputFileName { get; set; }

    /// <summary>Path to input file relative to uploads root.</summary>
    public required string InputFilePath { get; set; }

    /// <summary>Size of input file in bytes.</summary>
    public long InputFileSizeBytes { get; set; }

    // Processing artifacts

    /// <summary>Path to converted DOCX file (if DOC was converted).</summary>
    public string? ConvertedFilePath { get; set; }

    // Result

    /// <summary>ID of created package (null until import succeeds).</summary>
    public int? PackageId { get; set; }

    /// <summary>Navigation property to the created package.</summary>
    public Package? Package { get; set; }

    // Error info

    /// <summary>Short, user-friendly error message.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Full exception details for debugging.</summary>
    public string? ErrorDetails { get; set; }

    /// <summary>JSON array of warnings encountered during processing.</summary>
    public string? WarningsJson { get; set; }
}

