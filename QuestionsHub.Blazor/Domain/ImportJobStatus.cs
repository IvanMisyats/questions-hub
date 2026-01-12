namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Represents the status of a package import job.
/// </summary>
public enum ImportJobStatus
{
    /// <summary>Job is waiting to be processed.</summary>
    Queued = 0,

    /// <summary>Job is currently being processed.</summary>
    Running = 1,

    /// <summary>Job completed successfully, package was created.</summary>
    Succeeded = 2,

    /// <summary>Job failed (see ErrorMessage for details).</summary>
    Failed = 3,

    /// <summary>Job was cancelled by user or system.</summary>
    Cancelled = 4
}

