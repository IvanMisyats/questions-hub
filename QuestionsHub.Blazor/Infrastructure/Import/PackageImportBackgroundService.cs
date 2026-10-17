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

    internal async Task<PackageImportJob?> TryDequeueJob(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();

        var now = DateTime.UtcNow;

        // Find next queued job OR job ready for retry
        var job = await db.PackageImportJobs
            .Where(ImportJobLifecycle.Due(_options.MaxRetryAttempts, now))
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (job == null) return null;

        // Claim it
        ImportJobLifecycle.Claim(job, now);

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Dequeued import job: {JobId}, attempt {Attempt}",
            job.Id, job.Attempts);

        return job;
    }

    internal async Task ProcessJobWithTimeout(PackageImportJob job, CancellationToken stoppingToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        cts.CancelAfter(TimeSpan.FromMinutes(_options.JobTimeoutMinutes));

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var importService = scope.ServiceProvider.GetRequiredService<PackageImportService>();

            await importService.Process(job.Id, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning("Job {JobId} timed out after {Timeout} minutes",
                job.Id, _options.JobTimeoutMinutes);

            await MarkJobFailed(
                job.Id,
                $"Перевищено час очікування обробки ({_options.JobTimeoutMinutes} хвилин)");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await MarkJobFailed(job.Id, "Обробку було скасовано");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing job {JobId}", job.Id);
            await MarkJobFailed(job.Id, "Неочікувана помилка при обробці");
        }
    }

    internal async Task MarkJobFailed(Guid jobId, string errorMessage)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();

            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var job = await db.PackageImportJobs.FindAsync([jobId], cleanup.Token);
            // Process persists cancellation before rethrowing; overwrite that message with the timeout.
            // A retry it scheduled (a retriable error racing the cancellation) is kept.
            if (job is { Status: not ImportJobStatus.Succeeded, NextRetryAt: null })
            {
                ImportJobLifecycle.Fail(job, errorMessage);
                await db.SaveChangesAsync(cleanup.Token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark job {JobId} as failed", jobId);
        }
    }
}

