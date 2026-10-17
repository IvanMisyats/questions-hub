using System.Data.Common;
using System.IO.Compression;
using System.Text;
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

/// <summary>
/// The package's commit reports an error although PostgreSQL committed it (a connection lost before the
/// acknowledgement): the import must look again, keep the package's media and end as a linked success.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ImportCommitOutcomeTests(PostgresFixture database) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "qh-commit-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    [SkippableFact]
    public async Task CommittedImport_WhoseCommitThrows_KeepsMedia_AndSucceeds()
    {
        database.SkipIfUnavailable();
        var lostAck = new LoseCommitAcknowledgement();
        await using var factory = new QuestionsHubAppFactory(database, configureServices: services =>
        {
            services.PostConfigure<MediaUploadOptions>(o => o.UploadsPath = _folder);
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(PackageImportBackgroundService)).ToArray())
                services.Remove(descriptor);
            services.AddDbContext<QuestionsHubDbContext>(options => options.AddInterceptors(lostAck));
        });
        var owner = await TestData.AdminId(factory);

        Guid jobId;
        using (var scope = factory.Services.CreateScope())
        {
            var imports = scope.ServiceProvider.GetRequiredService<PackageImportService>();
            using var archive = Qhub();
            jobId = (await imports.Enqueue(owner, "lost-ack.qhub", archive, archive.Length, PackageType.Www)).Id;
            await imports.Process(jobId, default);
        }

        lostAck.Fired.Should().Be(1, "the commit of the package reported an error");
        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        var job = await db.PackageImportJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
        job.Status.Should().Be(ImportJobStatus.Succeeded);
        job.PackageId.Should().NotBeNull();
        var question = await db.Questions.AsNoTracking().SingleAsync(q => q.Tour.PackageId == job.PackageId);
        question.HandoutUrl.Should().StartWith("/media/");
        File.Exists(Path.Combine(_folder, "handouts", Path.GetFileName(question.HandoutUrl!))).Should().BeTrue();
    }

    private static MemoryStream Qhub()
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var json = new StreamWriter(zip.CreateEntry("package.json").Open(), Encoding.UTF8))
                json.Write("""
                    { "formatVersion": "1.2", "title": "Втрачене підтвердження", "tours": [ { "number": "1",
                      "questions": [ { "number": "1", "text": "Питання", "answer": "Відповідь", "handoutAssetFileName": "h.png" } ] } ] }
                    """);
            using var asset = zip.CreateEntry("assets/h.png").Open();
            asset.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);
        }
        stream.Position = 0;
        return stream;
    }

    /// <summary>Throws once after a transaction that saved a package has committed.</summary>
    private sealed class LoseCommitAcknowledgement : DbTransactionInterceptor
    {
        public int Fired;

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<Package>().Any() == true && Interlocked.Exchange(ref Fired, 1) == 0)
                throw new InvalidOperationException("The connection was lost after the commit.");
            return Task.CompletedTask;
        }
    }
}
