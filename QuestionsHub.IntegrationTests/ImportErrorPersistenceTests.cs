using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Import;
using QuestionsHub.Blazor.Infrastructure.Media;
using QuestionsHub.IntegrationTests.Infrastructure;
using Xunit;

namespace QuestionsHub.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ImportErrorPersistenceTests(PostgresFixture database) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "qh-error-db-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LongImportError_LeavesRunningState_AndPreservesFullDetails(bool retry)
    {
        database.SkipIfUnavailable();
        var message = new string('Ж', 5000);
        var fault = new LongImportError(message, retry);
        await using var factory = new QuestionsHubAppFactory(database, configureServices: services =>
        {
            services.PostConfigure<MediaUploadOptions>(o => o.UploadsPath = _folder);
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(PackageImportBackgroundService)).ToArray())
                services.Remove(descriptor);
            services.AddDbContext<QuestionsHubDbContext>(options => options.AddInterceptors(fault));
        });
        var owner = await TestData.AdminId(factory);
        var job = new PackageImportJob
        {
            Id = Guid.NewGuid(), OwnerId = owner, Status = ImportJobStatus.Running,
            InputFileName = "test.qhub", InputFilePath = "absent.qhub", Attempts = 1, CreatedAt = DateTime.UtcNow
        };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            db.Add(job);
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<PackageImportService>().Process(job.Id, default);
        }

        using var check = factory.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        var saved = await checkDb.PackageImportJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
        saved.Status.Should().Be(ImportJobStatus.Failed);
        saved.ErrorMessage.Should().HaveLength(1000).And.EndWith("…");
        saved.ErrorDetails.Should().Contain(message);
        saved.NextRetryAt.HasValue.Should().Be(retry);
        saved.FinishedAt.HasValue.Should().Be(!retry);
    }

    private sealed class LongImportError(string message, bool retry) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<PackageImportJob>().Any(e =>
                e.Entity.Status == ImportJobStatus.Running && e.Entity.CurrentStep == "Extracting"))
            {
                if (retry) throw new LLMException(message);
                throw new ExtractionException(message);
            }
            return ValueTask.FromResult(result);
        }
    }
}
