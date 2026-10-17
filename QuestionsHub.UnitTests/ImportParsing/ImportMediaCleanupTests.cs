using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure;
using QuestionsHub.Blazor.Infrastructure.Import;
using QuestionsHub.Blazor.Infrastructure.Media;
using QuestionsHub.UnitTests.TestInfrastructure;
using Xunit;

namespace QuestionsHub.UnitTests.ImportParsing;

public sealed class ImportMediaCleanupTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "qh-compensation-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailureAfterMediaCopy_AlwaysCompensates_EvenIfRollbackFails(bool cancel, bool rollbackFails)
    {
        var assets = Path.Combine(_folder, "assets");
        var handouts = Path.Combine(_folder, "handouts");
        Directory.CreateDirectory(assets);
        Directory.CreateDirectory(handouts);
        await File.WriteAllBytesAsync(Path.Combine(assets, "image.png"), [1, 2, 3]);
        await File.WriteAllTextAsync(Path.Combine(handouts, "existing.png"), "keep");
        using var cancellation = new CancellationTokenSource();
        var fault = new FailQuestionSave(cancellation, cancel, handouts);
        var options = new DbContextOptionsBuilder<QuestionsHubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ReplaceService<IDbContextTransactionManager, CleanupTransactionManager>()
            .AddInterceptors(fault).Options;
        await using var db = new TestableQuestionsHubDbContext(options);
        var transactions = (CleanupTransactionManager)db.GetService<IDbContextTransactionManager>();
        transactions.ThrowOnRollback = rollbackFails;
        var factory = new InMemoryDbContextFactory();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var importer = new PackageDbImporter(db, Options.Create(new MediaUploadOptions { UploadsPath = _folder }),
            new AuthorService(factory), new TagService(factory, cache), NullLogger<PackageDbImporter>.Instance);
        var parsed = new ParseResult
        {
            Title = "test",
            Tours = [new TourDto { Number = "1", Questions = [new QuestionDto
            {
                Number = "1", Text = "Q", Answer = "A", HandoutAssetFileName = "image.png", CommentAssetFileName = "image.png"
            }] }]
        };

        var action = () => importer.Import(parsed, "owner", Guid.NewGuid(), assets, cancellation.Token);
        if (cancel) await action.Should().ThrowAsync<OperationCanceledException>();
        else await action.Should().ThrowAsync<DatabaseImportException>();
        fault.SawCopiedMedia.Should().BeTrue();
        transactions.RollbackCalled.Should().BeTrue();
        transactions.RollbackTokenWasIndependent.Should().BeTrue();
        Directory.GetFiles(handouts).Select(Path.GetFileName).Should().Equal("existing.png");
        File.Exists(Path.Combine(assets, "image.png")).Should().BeTrue();
    }

    private sealed class FailQuestionSave(CancellationTokenSource cancellation, bool cancel, string handouts) : SaveChangesInterceptor
    {
        public bool SawCopiedMedia { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Question>().Any(e => e.State == EntityState.Added))
            {
                SawCopiedMedia = Directory.GetFiles(handouts).Length == 2;
                if (cancel)
                {
                    cancellation.Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                throw new DbUpdateException("connection failed after copying media");
            }
            return ValueTask.FromResult(result);
        }
    }

    // InMemory normally ignores transactions, including rollback tokens. This test double makes
    // cancellation and a broken rollback observable without requiring a database server.
    public sealed class CleanupTransactionManager : IDbContextTransactionManager
    {
        public bool ThrowOnRollback { get; set; }
        public bool RollbackCalled { get; private set; }
        public bool RollbackTokenWasIndependent { get; private set; }
        public IDbContextTransaction? CurrentTransaction { get; private set; }
        public IDbContextTransaction BeginTransaction() => CurrentTransaction = new CleanupTransaction(this);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.FromResult(BeginTransaction());
        public void CommitTransaction() => CurrentTransaction!.Commit();
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => CurrentTransaction!.CommitAsync(cancellationToken);
        public void RollbackTransaction() => CurrentTransaction!.Rollback();
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => CurrentTransaction!.RollbackAsync(cancellationToken);
        public void ResetState() => CurrentTransaction = null;
        public Task ResetStateAsync(CancellationToken cancellationToken = default) { ResetState(); return Task.CompletedTask; }

        private sealed class CleanupTransaction(CleanupTransactionManager owner) : IDbContextTransaction
        {
            public Guid TransactionId { get; } = Guid.NewGuid();
            public void Commit() { }
            public Task CommitAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
            public void Rollback() => RollbackAsync().GetAwaiter().GetResult();
            public Task RollbackAsync(CancellationToken cancellationToken = default)
            {
                owner.RollbackCalled = true;
                owner.RollbackTokenWasIndependent = cancellationToken.CanBeCanceled && !cancellationToken.IsCancellationRequested;
                cancellationToken.ThrowIfCancellationRequested();
                if (owner.ThrowOnRollback) throw new IOException("rollback connection lost");
                return Task.CompletedTask;
            }
            public void Dispose() => owner.ResetState();
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
