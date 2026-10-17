using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Controllers;
using QuestionsHub.Blazor.Controllers.Api.Manage;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Import;
using QuestionsHub.Blazor.Infrastructure.RateLimiting;

namespace QuestionsHub.Blazor.Infrastructure.AgentApi;

/// <summary>An agent import call failed; <see cref="StatusCode"/> is the HTTP status, <see cref="RetryAfterSeconds"/> the hint for a 429.</summary>
public sealed class AgentImportException(int statusCode, string message, int? retryAfterSeconds = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

/// <summary>
/// Imports through the agent API, shared by REST and MCP: the token's user's own jobs only (admins too),
/// tokens without a package allowlist only. An upload is admitted before its body is read — at most
/// <see cref="PackageImportOptions.MaxActiveImportsPerUser"/> unfinished jobs and uploads in flight per
/// user, then one permit of the hourly <c>agent.import</c> budget — and enqueues the same job as the UI.
/// </summary>
public sealed class AgentImportService(
    PackageImportService imports,
    ClientRateLimiter rateLimiter,
    IOptions<PackageImportOptions> options,
    ILogger<AgentImportService> logger)
{
    /// <summary>How long a client may take to send an upload's body.</summary>
    internal static TimeSpan UploadTimeout { get; set; } = TimeSpan.FromMinutes(10);

    // Uploads admitted but not yet saved as jobs, per user. The lock only covers the admission check
    // (a count query), never the upload itself. Single-instance app, so process-wide is enough.
    private static readonly Dictionary<string, int> InFlight = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim AdmissionLock = new(1, 1);

    public async Task<ManageImportsResponse> List(ClaimsPrincipal agent, CancellationToken ct = default)
    {
        CheckAccess(agent);
        var jobs = await imports.GetRecentJobsForUser(agent.GetUserId()!, ct);
        return new(jobs.Select(Map).ToArray());
    }

    public async Task<ManageImportJobDto> Get(ClaimsPrincipal agent, Guid jobId, CancellationToken ct = default)
    {
        CheckAccess(agent);
        var job = await imports.GetJobForUser(agent.GetUserId()!, jobId, ct);
        return job == null ? throw new AgentImportException(404, "Import not found.") : Map(job);
    }

    /// <summary>The callback reads the body; it runs only after authorization and admission.</summary>
    public async Task<ManageImportJobDto> Enqueue(
        ClaimsPrincipal agent, Func<CancellationToken, Task<ImportUpload>> readUpload, CancellationToken ct = default)
    {
        CheckAccess(agent);
        if (agent.FindFirst(AgentClaims.Scope)?.Value != nameof(TokenScope.ReadWrite))
            throw new AgentImportException(403, "The token's scope does not allow changes (read-only token).");
        var userId = agent.GetUserId()!;

        await Admit(userId, ct);
        try
        {
            using (var permit = rateLimiter.Acquire(RateLimitBudgets.AgentImport, userId))
            {
                if (!permit.IsAcquired)
                    throw new AgentImportException(429, "Import rate limit exceeded. Please retry later.",
                        (int)RateLimitBudgets.WindowFor(RateLimitBudgets.AgentImport).TotalSeconds);
            }

            ImportUpload upload;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(UploadTimeout);
                try
                {
                    upload = await readUpload(timeout.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new AgentImportException(408, "The upload took too long.");
                }
            }

            await using var stream = upload.Content;
            var job = await imports.Enqueue(userId, upload.FileName, stream, upload.Length, upload.Type, ct);
            logger.LogInformation("Agent token {TokenId} enqueued import {JobId}", agent.GetTokenId(), job.Id);
            return Map(job);
        }
        finally
        {
            await Release(userId);
        }
    }

    /// <summary>Reserves an unfinished-job slot for the upload: saved jobs plus uploads in flight.</summary>
    private async Task Admit(string userId, CancellationToken ct)
    {
        await AdmissionLock.WaitAsync(ct);
        try
        {
            var unfinished = await imports.CountUnfinishedJobsForUser(userId, ct) + InFlight.GetValueOrDefault(userId);
            if (unfinished >= options.Value.MaxActiveImportsPerUser)
                throw new AgentImportException(429, "Too many unfinished imports. Please retry later.", 60);
            InFlight[userId] = InFlight.GetValueOrDefault(userId) + 1;
        }
        finally
        {
            AdmissionLock.Release();
        }
    }

    private static async Task Release(string userId)
    {
        await AdmissionLock.WaitAsync(CancellationToken.None);
        try
        {
            if (InFlight.TryGetValue(userId, out var count) && count > 1)
                InFlight[userId] = count - 1;
            else
                InFlight.Remove(userId);
        }
        finally
        {
            AdmissionLock.Release();
        }
    }

    private static void CheckAccess(ClaimsPrincipal agent)
    {
        if (agent.GetPackageAllowlist() != null)
            throw new AgentImportException(403, "Imports need a token that is not limited to specific packages.");
    }

    internal ManageImportJobDto Map(PackageImportJob job) => new(
        job.Id, job.InputFileName, job.Type == PackageType.Shvager ? "shvager" : "www",
        imports.IsRetryPending(job) ? "retrying" : job.Status switch
        {
            ImportJobStatus.Queued => "queued",
            ImportJobStatus.Running => "running",
            ImportJobStatus.Succeeded => "succeeded",
            _ => "failed" // Legacy Cancelled jobs are terminal failures in the agent contract.
        },
        job.CurrentStep, job.Progress, job.CreatedAt, job.StartedAt, job.FinishedAt,
        job.NextRetryAt, job.PackageId, job.ErrorMessage,
        string.IsNullOrEmpty(job.WarningsJson) ? [] : JsonSerializer.Deserialize<string[]>(job.WarningsJson) ?? []);
}

/// <summary>The enqueue path owns and disposes Content once the callback returns.</summary>
public sealed record ImportUpload(string FileName, Stream Content, long Length, PackageType Type);
