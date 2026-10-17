using System.IO.Compression;
using System.Security.Claims;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.Blazor.Infrastructure.Import;
using QuestionsHub.Blazor.Infrastructure.Media;
using QuestionsHub.Blazor.Infrastructure.RateLimiting;
using QuestionsHub.UnitTests.TestInfrastructure;
using Xunit;

namespace QuestionsHub.UnitTests.ImportParsing;

public sealed partial class ImportLifecycleTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "qh-lifecycle-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private static PackageImportJob Job() => new()
    {
        Id = Guid.NewGuid(), OwnerId = "owner", InputFileName = "test.qhub", InputFilePath = "absent.qhub",
        Status = ImportJobStatus.Running, Attempts = 1
    };

    [Theory]
    [InlineData(ImportJobStatus.Queued, false, 0, "queued")]
    [InlineData(ImportJobStatus.Running, true, 1, "running")]
    [InlineData(ImportJobStatus.Succeeded, true, 1, "succeeded")]
    [InlineData(ImportJobStatus.Failed, false, 1, "failed")]
    [InlineData(ImportJobStatus.Failed, true, 1, "retrying")]
    [InlineData(ImportJobStatus.Failed, true, 3, "failed")]
    [InlineData(ImportJobStatus.Cancelled, true, 1, "failed")]
    public void Status_UsesWorkerRetryEligibility(ImportJobStatus status, bool scheduled, int attempts, string expected)
    {
        var imports = new PackageImportService(null!, Options.Create(new PackageImportOptions()),
            Options.Create(new MediaUploadOptions()), NullLogger<PackageImportService>.Instance);
        var service = new AgentImportService(imports, null!, Options.Create(new PackageImportOptions()), NullLogger<AgentImportService>.Instance);
        var job = Job();
        job.Status = status;
        job.NextRetryAt = scheduled ? DateTime.UtcNow.AddHours(1) : null;
        job.Attempts = attempts;
        job.Type = PackageType.Shvager;
        service.Map(job).Status.Should().Be(expected);
        service.Map(job).RequestedGameType.Should().Be("shvager");
        service.Map(job).Warnings.Should().BeEmpty();
        new[] { job }.AsQueryable().Any(ImportJobLifecycle.Unfinished(3))
            .Should().Be(expected is "queued" or "running" or "retrying");
        ImportJobLifecycle.IsUnfinished(job, 3).Should().Be(expected is "queued" or "running" or "retrying");
        job.WarningsJson = "[\"Попередження\"]";
        service.Map(job).Warnings.Should().Equal("Попередження");
    }

    [Fact]
    public void ClaimAndFailure_ClearStaleLifecycleFields()
    {
        var job = Job();
        job.NextRetryAt = DateTime.UtcNow;
        job.FinishedAt = DateTime.UtcNow;
        job.ErrorMessage = "old";
        job.ErrorDetails = "private";
        ImportJobLifecycle.Claim(job, DateTime.UtcNow);
        job.NextRetryAt.Should().BeNull();
        job.FinishedAt.Should().BeNull();
        job.ErrorMessage.Should().BeNull();
        job.ErrorDetails.Should().BeNull();
        job.Attempts.Should().Be(2);
        job.NextRetryAt = DateTime.UtcNow;
        ImportJobLifecycle.Fail(job, "failed");
        job.NextRetryAt.Should().BeNull();
        job.FinishedAt.Should().NotBeNull();
        job.Status.Should().Be(ImportJobStatus.Failed);
    }

    private ServiceProvider Provider(SaveChangesInterceptor? interceptor = null)
    {
        var services = new ServiceCollection();
        var dbOptions = new DbContextOptionsBuilder<QuestionsHubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        if (interceptor != null)
            dbOptions = new DbContextOptionsBuilder<QuestionsHubDbContext>(dbOptions).AddInterceptors(interceptor).Options;
        services.AddScoped<QuestionsHubDbContext>(_ => new TestableQuestionsHubDbContext(dbOptions));
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton<IDbContextFactory<QuestionsHubDbContext>>(new InMemoryDbContextFactory());
        services.AddScoped<AuthorService>();
        services.AddScoped<TagService>();
        services.AddScoped<PackageDbImporter>();
        services.AddScoped<PackageImportService>();
        services.AddScoped<QhubExtractor>();
        services.AddScoped<DocxExtractor>();
        services.AddScoped<PackageParser>();
        services.AddHttpClient();
        services.Configure<MediaUploadOptions>(o => o.UploadsPath = _folder);
        services.Configure<PackageImportOptions>(_ => { });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Enqueue_RemovesWrittenInput_WhenDatabaseSaveFails()
    {
        using var provider = Provider(new FailSave());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PackageImportService>();
        using var stream = new MemoryStream([1, 2, 3]);
        var action = () => service.Enqueue("owner", "test.qhub", stream, stream.Length);
        await action.Should().ThrowAsync<DbUpdateException>();
        Directory.GetDirectories(Path.Combine(_folder, "jobs")).Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_IsPersistedWithIndependentToken_AndRethrown()
    {
        using var cancellation = new CancellationTokenSource();
        var interceptor = new CancelProgress(cancellation);
        using var provider = Provider(interceptor);
        var job = Job();
        using (var seed = provider.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            db.Add(job);
            await db.SaveChangesAsync();
        }
        using var scope = provider.CreateScope();
        var action = () => scope.ServiceProvider.GetRequiredService<PackageImportService>().Process(job.Id, cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
        using var check = provider.CreateScope();
        var saved = await check.ServiceProvider.GetRequiredService<QuestionsHubDbContext>().PackageImportJobs.SingleAsync();
        saved.Status.Should().Be(ImportJobStatus.Failed);
        saved.ErrorMessage.Should().Be("Обробку було скасовано");
        saved.FinishedAt.Should().NotBeNull();
        saved.NextRetryAt.Should().BeNull();
    }

    [Fact]
    public async Task StaleRecovery_ClearsRetrySchedule()
    {
        using var provider = Provider();
        var job = Job();
        job.NextRetryAt = DateTime.UtcNow.AddMinutes(1);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            db.Add(job);
            await db.SaveChangesAsync();
        }
        var recovery = new StaleJobRecoveryService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<StaleJobRecoveryService>.Instance);
        await recovery.StartAsync(default);
        using var check = provider.CreateScope();
        var saved = await check.ServiceProvider.GetRequiredService<QuestionsHubDbContext>().PackageImportJobs.SingleAsync();
        saved.NextRetryAt.Should().BeNull();
        saved.Status.Should().Be(ImportJobStatus.Failed);
        saved.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RecentAndActiveJobs_AreOwnerScoped_AndUiKeepsPollingRetries()
    {
        using var provider = Provider();
        var jobs = Enumerable.Range(0, 24).Select(i =>
        {
            var job = Job();
            job.Status = ImportJobStatus.Succeeded;
            job.CreatedAt = DateTime.UtcNow.AddMinutes(-i);
            return job;
        }).ToArray();
        var retry = Job();
        retry.Status = ImportJobStatus.Failed;
        retry.NextRetryAt = DateTime.UtcNow.AddHours(1);
        var foreign = Job();
        foreign.OwnerId = "other";
        foreign.CreatedAt = DateTime.UtcNow.AddDays(1);
        using (var seed = provider.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            db.AddRange(jobs);
            db.AddRange(retry, foreign);
            await db.SaveChangesAsync();
        }
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PackageImportService>();
        (await service.GetRecentJobsForUser("owner")).Select(j => j.Id).Should().Equal(jobs.Take(20).Select(j => j.Id));
        (await service.GetActiveJobsForUser("owner")).Select(j => j.Id).Should().Equal(retry.Id);
        (await service.GetActiveJobs()).Select(j => j.Id).Should().BeEquivalentTo([retry.Id, foreign.Id]);
        service.IsRetryPending(retry).Should().BeTrue();
        service.IsUnfinished(retry).Should().BeTrue();
        service.IsUnfinished(jobs[0]).Should().BeFalse();

        var agentService = new AgentImportService(service, null!, Options.Create(new PackageImportOptions()), NullLogger<AgentImportService>.Instance);
        var agent = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "owner"), new Claim(ClaimTypes.Role, "Admin")]));
        (await agentService.List(agent)).Imports.Select(j => j.JobId).Should().Equal(jobs.Take(20).Select(j => j.Id));
        ((ClaimsIdentity)agent.Identity!).AddClaim(new Claim(AgentClaims.Packages, "1"));
        var action = () => agentService.List(agent);
        (await action.Should().ThrowAsync<AgentImportException>()).Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Worker_ClaimsDueRetry_ClearsDates_AndPersistsTimeout()
    {
        using var provider = Provider(new WaitForTimeout());
        var job = Job();
        job.Status = ImportJobStatus.Failed;
        job.NextRetryAt = DateTime.UtcNow.AddMinutes(-1);
        job.FinishedAt = DateTime.UtcNow.AddMinutes(-2);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            db.Add(job);
            await db.SaveChangesAsync();
        }
        using var worker = new PackageImportBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PackageImportOptions { JobTimeoutMinutes = 0 }), NullLogger<PackageImportBackgroundService>.Instance);
        var claimed = (await worker.TryDequeueJob(default))!;
        claimed.Id.Should().Be(job.Id);
        claimed.NextRetryAt.Should().BeNull();
        claimed.FinishedAt.Should().BeNull();
        claimed.Attempts.Should().Be(2);
        await worker.ProcessJobWithTimeout(claimed, default);
        using var check = provider.CreateScope();
        var saved = await check.ServiceProvider.GetRequiredService<QuestionsHubDbContext>().PackageImportJobs.SingleAsync();
        saved.Status.Should().Be(ImportJobStatus.Failed);
        saved.ErrorMessage.Should().StartWith("Перевищено час очікування");
        saved.NextRetryAt.Should().BeNull();
        saved.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Worker_UnrelatedCancellation_IsAnUnexpectedError_NotATimeout()
    {
        // An OperationCanceledException the job's token did not cause (e.g. an HttpClient timeout deep in the
        // pipeline) is an ordinary failure: neither "timed out" nor "cancelled".
        using var provider = Provider(new ThrowCancellation());
        var job = Job();
        job.Status = ImportJobStatus.Queued;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            db.Add(job);
            await db.SaveChangesAsync();
        }
        using var worker = new PackageImportBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PackageImportOptions()), NullLogger<PackageImportBackgroundService>.Instance);

        await worker.ProcessJobWithTimeout((await worker.TryDequeueJob(default))!, default);

        using var check = provider.CreateScope();
        var saved = await check.ServiceProvider.GetRequiredService<QuestionsHubDbContext>().PackageImportJobs.SingleAsync();
        saved.Status.Should().Be(ImportJobStatus.Failed);
        saved.ErrorMessage.Should().Be("Неочікувана помилка при обробці");
    }

    [Fact]
    public async Task Worker_MarkJobFailed_KeepsAScheduledRetry()
    {
        using var provider = Provider();
        var job = Job();
        job.Status = ImportJobStatus.Failed;
        job.NextRetryAt = DateTime.UtcNow.AddMinutes(2);
        job.ErrorMessage = "retriable";
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            db.Add(job);
            await db.SaveChangesAsync();
        }
        using var worker = new PackageImportBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PackageImportOptions()), NullLogger<PackageImportBackgroundService>.Instance);

        await worker.MarkJobFailed(job.Id, "Перевищено час очікування обробки");

        using var check = provider.CreateScope();
        var saved = await check.ServiceProvider.GetRequiredService<QuestionsHubDbContext>().PackageImportJobs.SingleAsync();
        saved.NextRetryAt.Should().NotBeNull();
        saved.ErrorMessage.Should().Be("retriable");
    }

    [Fact]
    public async Task AgentUpload_TooSlow_Is408_AndFreesItsSlot()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();
        var imports = scope.ServiceProvider.GetRequiredService<PackageImportService>();
        using var limiter = new ClientRateLimiter(Options.Create(new RateLimitSettings()));
        var options = Options.Create(new PackageImportOptions { MaxActiveImportsPerUser = 1 });
        var service = new AgentImportService(imports, limiter, options, NullLogger<AgentImportService>.Instance);
        var agent = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "slow-" + Guid.NewGuid().ToString("N")),
            new Claim(AgentClaims.Scope, nameof(TokenScope.ReadWrite))]));
        var previous = AgentImportService.UploadTimeout;
        AgentImportService.UploadTimeout = TimeSpan.FromMilliseconds(50);
        try
        {
            var slow = () => service.Enqueue(agent, async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException("unreachable");
            });
            (await slow.Should().ThrowAsync<AgentImportException>()).Which.StatusCode.Should().Be(408);

            // The slot of the timed-out upload is free again: the next one is admitted (and read).
            var read = false;
            var next = () => service.Enqueue(agent, _ =>
            {
                read = true;
                throw new AgentImportException(400, "stop here");
            });
            (await next.Should().ThrowAsync<AgentImportException>()).Which.StatusCode.Should().Be(400);
            read.Should().BeTrue();
        }
        finally
        {
            AgentImportService.UploadTimeout = previous;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnqueueAndProcess_BothFormats_CreateOwnedDraft(bool docx)
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PackageImportService>();
        using var input = new MemoryStream();
        if (docx)
        {
            using var document = WordprocessingDocument.Create(input, WordprocessingDocumentType.Document, true);
            var main = document.AddMainDocumentPart();
            string[] paragraphs = ["Пакет API", "Тур 1", "Питання 1. Запитання?", "Відповідь: Відповідь."];
            main.Document = new Document(new Body(paragraphs.Select(text => new Paragraph(new Run(new Text(text))))));
            main.Document.Save();
        }
        else
        {
            using var archive = new ZipArchive(input, ZipArchiveMode.Create, true);
            using var writer = new StreamWriter(archive.CreateEntry("package.json").Open());
            writer.Write("""{"formatVersion":"1.2","gameType":"www","title":"Імпорт API","tours":[{"number":"1","questions":[{"number":"1","text":"Запитання","answer":"Відповідь"}]}]}""");
        }
        input.Position = 0;
        var job = await service.Enqueue("owner", docx ? "package.docx" : "package.qhub", input, input.Length);
        using var worker = new PackageImportBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PackageImportOptions()), NullLogger<PackageImportBackgroundService>.Instance);
        var claimed = (await worker.TryDequeueJob(default))!;
        claimed.Id.Should().Be(job.Id);
        await worker.ProcessJobWithTimeout(claimed, default);
        using var check = provider.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        var saved = await db.PackageImportJobs.SingleAsync();
        saved.Status.Should().Be(ImportJobStatus.Succeeded, saved.ErrorDetails);
        saved.NextRetryAt.Should().BeNull();
        saved.FinishedAt.Should().NotBeNull();
        var package = await db.Packages.SingleAsync();
        saved.PackageId.Should().Be(package.Id);
        package.OwnerId.Should().Be("owner");
        package.Status.Should().Be(PackageStatus.Draft);
        package.TotalQuestions.Should().Be(1);
    }

    private sealed class WaitForTimeout : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<PackageImportJob>()
                .Any(e => e.Entity.Status == ImportJobStatus.Running && e.Entity.CurrentStep == "Extracting"))
                await Task.Delay(Timeout.Infinite, cancellationToken);
            return result;
        }
    }

    /// <summary>Throws an OperationCanceledException of its own once, when the job reaches "Extracting".</summary>
    private sealed class ThrowCancellation : SaveChangesInterceptor
    {
        private bool _thrown;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_thrown && eventData.Context!.ChangeTracker.Entries<PackageImportJob>().Any(e => e.Entity.CurrentStep == "Extracting"))
            {
                _thrown = true;
                throw new OperationCanceledException("not the job's token");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) => throw new DbUpdateException("injected failure");
    }

    private sealed class CancelProgress(CancellationTokenSource source) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!source.IsCancellationRequested && eventData.Context!.ChangeTracker.Entries<PackageImportJob>()
                .Any(e => e.Entity.CurrentStep == "Extracting"))
            {
                source.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return ValueTask.FromResult(result);
        }
    }
}
