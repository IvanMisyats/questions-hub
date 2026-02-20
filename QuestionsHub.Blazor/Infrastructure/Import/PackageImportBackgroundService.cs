using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Background service that processes package import jobs.
/// </summary>
public class PackageImportBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PackageImportOptions _options;
    private readonly ILogger<PackageImportBackgroundService> _logger;

    private readonly SemaphoreSlim _concurrencyLimit;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

    public PackageImportBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<PackageImportOptions> options,
        ILogger<PackageImportBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _concurrencyLimit = new SemaphoreSlim(_options.MaxConcurrentJobs);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Package import background service started (max concurrent: {MaxConcurrent})",
            _options.MaxConcurrentJobs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _concurrencyLimit.WaitAsync(stoppingToken);

                var job = await TryDequeueJob(stoppingToken);
                if (job != null)
                {
                    // Process in background, release semaphore when done
                    _ = ProcessJobWithTimeout(job, stoppingToken)
                        .ContinueWith(_ => _concurrencyLimit.Release(), TaskScheduler.Default);
                }
                else
                {
                    _concurrencyLimit.Release();
                    await Task.Delay(_pollingInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in import worker loop");
                _concurrencyLimit.Release();
                await Task.Delay(_pollingInterval, stoppingToken);
            }
        }

        _logger.LogInformation("Package import background service stopped");
    }

    private async Task<PackageImportJob?> TryDequeueJob(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();

        var now = DateTime.UtcNow;

        // Find next queued job OR job ready for retry
        var job = await db.PackageImportJobs
            .Where(j => j.Status == ImportJobStatus.Queued ||
                        (j.Status == ImportJobStatus.Failed &&
                         j.Attempts < _options.MaxRetryAttempts &&
                         j.NextRetryAt != null &&
                         j.NextRetryAt <= now))
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (job == null) return null;

        // Claim it
        job.Status = ImportJobStatus.Running;
        job.StartedAt = now;
        job.Attempts++;
        job.CurrentStep = "Validating";
        job.Progress = 0;
        job.ErrorMessage = null;
        job.ErrorDetails = null;

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Dequeued import job: {JobId}, attempt {Attempt}",
            job.Id, job.Attempts);

        return job;
    }

    private async Task ProcessJobWithTimeout(PackageImportJob job, CancellationToken stoppingToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        cts.CancelAfter(TimeSpan.FromMinutes(_options.JobTimeoutMinutes));

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var importService = scope.ServiceProvider.GetRequiredService<PackageImportService>();

            await importService.Process(job.Id, cts.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning("Job {JobId} timed out after {Timeout} minutes",
                job.Id, _options.JobTimeoutMinutes);

            await MarkJobFailed(
                job.Id,
                $"Перевищено час очікування обробки ({_options.JobTimeoutMinutes} хвилин)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing job {JobId}", job.Id);
            await MarkJobFailed(job.Id, "Неочікувана помилка при обробці");
        }
    }

    private async Task MarkJobFailed(Guid jobId, string errorMessage)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();

            var job = await db.PackageImportJobs.FindAsync(jobId);
            if (job != null && job.Status == ImportJobStatus.Running)
            {
                job.Status = ImportJobStatus.Failed;
                job.ErrorMessage = errorMessage;
                job.FinishedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark job {JobId} as failed", jobId);
        }
    }
}

