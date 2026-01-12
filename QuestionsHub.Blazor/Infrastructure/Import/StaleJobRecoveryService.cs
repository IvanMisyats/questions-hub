using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Recovers stale jobs on application startup.
/// Jobs that were running when the app crashed are marked as failed.
/// </summary>
public class StaleJobRecoveryService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StaleJobRecoveryService> _logger;

    public StaleJobRecoveryService(
        IServiceScopeFactory scopeFactory,
        ILogger<StaleJobRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();

        // Find jobs that were running when the app stopped
        var staleJobs = await db.PackageImportJobs
            .Where(j => j.Status == ImportJobStatus.Running)
            .ToListAsync(cancellationToken);

        if (staleJobs.Count == 0)
        {
            _logger.LogInformation("No stale import jobs found");
            return;
        }

        foreach (var job in staleJobs)
        {
            job.Status = ImportJobStatus.Failed;
            job.ErrorMessage = "Обробку було перервано через перезапуск сервера";
            job.FinishedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        _logger.LogWarning("Marked {Count} stale import jobs as failed", staleJobs.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

