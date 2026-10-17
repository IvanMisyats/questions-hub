using System.Collections.Concurrent;
using System.Linq.Expressions;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// The states of an import job, defined once for queries (expressions) and in memory (the same expressions,
/// compiled once per setting): retry-pending, unfinished, and due for the worker; plus the field updates of
/// claiming and finally failing a job.
/// </summary>
internal static class ImportJobLifecycle
{
    private static readonly ConcurrentDictionary<int, (Func<PackageImportJob, bool> RetryPending, Func<PackageImportJob, bool> Unfinished)> Compiled = new();

    /// <summary>Failed with a retry scheduled and attempts left: the worker will run it again.</summary>
    public static Expression<Func<PackageImportJob, bool>> RetryPending(int maxAttempts) =>
        job => job.Status == ImportJobStatus.Failed && job.NextRetryAt != null && job.Attempts < maxAttempts;

    /// <summary>Queued, running or retry-pending (admission, the API's status, the UI's polling).</summary>
    public static Expression<Func<PackageImportJob, bool>> Unfinished(int maxAttempts) =>
        job => job.Status == ImportJobStatus.Queued || job.Status == ImportJobStatus.Running
            || (job.Status == ImportJobStatus.Failed && job.NextRetryAt != null && job.Attempts < maxAttempts);

    /// <summary>What the worker picks up at <paramref name="now"/>: queued, or retry-pending and due.</summary>
    public static Expression<Func<PackageImportJob, bool>> Due(int maxAttempts, DateTime now) =>
        job => job.Status == ImportJobStatus.Queued
            || (job.Status == ImportJobStatus.Failed && job.NextRetryAt != null && job.Attempts < maxAttempts && job.NextRetryAt <= now);

    public static bool IsRetryPending(PackageImportJob job, int maxAttempts) => For(maxAttempts).RetryPending(job);

    public static bool IsUnfinished(PackageImportJob job, int maxAttempts) => For(maxAttempts).Unfinished(job);

    private static (Func<PackageImportJob, bool> RetryPending, Func<PackageImportJob, bool> Unfinished) For(int maxAttempts) =>
        Compiled.GetOrAdd(maxAttempts, max => (RetryPending(max).Compile(), Unfinished(max).Compile()));

    public static void Claim(PackageImportJob job, DateTime now)
    {
        job.Status = ImportJobStatus.Running;
        job.StartedAt = now;
        job.NextRetryAt = null;
        job.FinishedAt = null;
        job.Attempts++;
        job.CurrentStep = "Validating";
        job.Progress = 0;
        job.ErrorMessage = null;
        job.ErrorDetails = null;
    }

    public static void Fail(PackageImportJob job, string message)
    {
        // Recovery and timeout reporting must never fail or retry an already committed import.
        if (job.PackageId.HasValue)
        {
            Succeed(job);
            return;
        }
        job.Status = ImportJobStatus.Failed;
        SetError(job, message, job.ErrorDetails);
        job.FinishedAt = DateTime.UtcNow;
        job.NextRetryAt = null;
    }

    public static void SetError(PackageImportJob job, string message, string? details = null)
    {
        // PackageImportJobs.ErrorMessage is varchar(1000); ErrorDetails is unrestricted text.
        job.ErrorMessage = ImportDiagnostics.Truncate(message, 1000);
        job.ErrorDetails = message.Length > 1000 && (details == null || !details.Contains(message, StringComparison.Ordinal))
            ? message + (details == null ? "" : "\n\n" + details)
            : details;
    }

    public static void Succeed(PackageImportJob job)
    {
        job.Status = ImportJobStatus.Succeeded;
        job.NextRetryAt = null;
        job.FinishedAt = DateTime.UtcNow;
        job.CurrentStep = null;
        job.Progress = 100;
        job.ErrorMessage = null;
        job.ErrorDetails = null;
    }
}
