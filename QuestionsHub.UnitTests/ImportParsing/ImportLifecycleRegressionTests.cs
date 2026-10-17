using System.IO.Compression;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Import;
using Xunit;

namespace QuestionsHub.UnitTests.ImportParsing;

public sealed partial class ImportLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LongErrors_AreBounded_AndKeepFullDetails(bool retry)
    {
        var message = new string('Ж', 5000);
        using var provider = Provider(new ThrowLongError(message, retry));
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        var job = Job();
        db.Add(job);
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<PackageImportService>().Process(job.Id, default);

        using var check = provider.CreateScope();
        var saved = await check.ServiceProvider.GetRequiredService<QuestionsHubDbContext>().PackageImportJobs.SingleAsync();
        saved.Status.Should().Be(ImportJobStatus.Failed);
        saved.ErrorMessage.Should().HaveLength(1000).And.EndWith("…");
        saved.ErrorDetails.Should().Contain(message);
        saved.NextRetryAt.HasValue.Should().Be(retry);
    }

    [Fact]
    public async Task Worker_LongError_IsPersisted_WithinModelLimit()
    {
        using var provider = Provider();
        var job = Job();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            db.Add(job);
            await db.SaveChangesAsync();
        }
        using var worker = new PackageImportBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PackageImportOptions()), NullLogger<PackageImportBackgroundService>.Instance);
        var message = new string('E', 3000);
        await worker.MarkJobFailed(job.Id, message);
        using var check = provider.CreateScope();
        var dbCheck = check.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        var saved = await dbCheck.PackageImportJobs.SingleAsync();
        var max = dbCheck.Model.FindEntityType(typeof(PackageImportJob))!.FindProperty(nameof(PackageImportJob.ErrorMessage))!.GetMaxLength();
        saved.ErrorMessage!.Length.Should().Be(max);
        saved.ErrorDetails.Should().Be(message);
        saved.Status.Should().Be(ImportJobStatus.Failed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleAndWorkerRecovery_LinkedJob_IsSucceeded(bool workerRecovery)
    {
        using var provider = Provider();
        var job = Job();
        job.PackageId = 42;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            db.Add(job);
            await db.SaveChangesAsync();
        }
        if (workerRecovery)
        {
            using var worker = new PackageImportBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new PackageImportOptions()), NullLogger<PackageImportBackgroundService>.Instance);
            await worker.MarkJobFailed(job.Id, "timeout");
        }
        else
            await new StaleJobRecoveryService(provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<StaleJobRecoveryService>.Instance).StartAsync(default);

        using var check = provider.CreateScope();
        var saved = await check.ServiceProvider.GetRequiredService<QuestionsHubDbContext>().PackageImportJobs.SingleAsync();
        saved.Status.Should().Be(ImportJobStatus.Succeeded);
        saved.PackageId.Should().Be(42);
        saved.ErrorMessage.Should().BeNull();
        saved.Progress.Should().Be(100);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("save")]
    [InlineData("copy")]
    public async Task PostCommitInterruption_KeepsSucceededJob_WarningsAndPackage(string failure)
    {
        using var cancellation = new CancellationTokenSource();
        var interceptor = new InterruptFinalization(cancellation, failure);
        using var provider = Provider(interceptor);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PackageImportService>();
        using var input = new MemoryStream();
        using (var zip = new ZipArchive(input, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry("package.json").Open()))
            writer.Write("""{"formatVersion":"1.2","title":"Package","tours":[{"number":"1","questions":[{"number":"1","text":"Q"}]}]}""");
        input.Position = 0;
        var job = await service.Enqueue("owner", "package.qhub", input, input.Length);
        // Make the optional original-copy destination unusable.
        if (failure == "copy") await File.WriteAllTextAsync(Path.Combine(_folder, "packages"), "occupied");

        await service.Process(job.Id, cancellation.Token);
        using var check = provider.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        var saved = await db.PackageImportJobs.SingleAsync();
        saved.PackageId.Should().Be((await db.Packages.SingleAsync()).Id);
        saved.Status.Should().Be(ImportJobStatus.Succeeded);
        System.Text.Json.JsonSerializer.Deserialize<string[]>(saved.WarningsJson!)!.Should().Contain(w => w.Contains("відповідь відсутня"));
        saved.ErrorMessage.Should().BeNull();
        saved.NextRetryAt.Should().BeNull();
        interceptor.LinkSavedWithoutCancellation.Should().BeTrue();
        // Replaying a linked job must not create a duplicate package.
        await service.Process(job.Id, default);
        (await db.Packages.CountAsync()).Should().Be(1);
    }

    private sealed class ThrowLongError(string message, bool retry) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<PackageImportJob>().Any(e => e.Entity.CurrentStep == "Extracting" && e.Entity.Status == ImportJobStatus.Running))
            {
                if (retry) throw new LLMException(message);
                throw new ExtractionException(message);
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class InterruptFinalization(CancellationTokenSource source, string failure) : SaveChangesInterceptor
    {
        private int _linkedSaves;
        public bool LinkSavedWithoutCancellation { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<PackageImportJob>().Any(e => e.Entity.PackageId.HasValue))
            {
                _linkedSaves++;
                if (_linkedSaves == 1)
                {
                    LinkSavedWithoutCancellation = !cancellationToken.CanBeCanceled;
                    if (failure == "cancel") source.Cancel();
                }
                if (_linkedSaves == 2 && failure == "save") throw new DbUpdateException("post-commit failure");
            }
            return ValueTask.FromResult(result);
        }
    }
}
