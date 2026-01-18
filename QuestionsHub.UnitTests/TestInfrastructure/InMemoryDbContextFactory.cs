﻿using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using QuestionsHub.Blazor.Data;

namespace QuestionsHub.UnitTests.TestInfrastructure;

/// <summary>
/// Factory that creates in-memory database contexts for testing.
/// Each factory instance uses a unique database name.
/// Uses a shared root for consistent data across context instances.
/// </summary>
public class InMemoryDbContextFactory : IDbContextFactory<QuestionsHubDbContext>
{
    private readonly DbContextOptions<QuestionsHubDbContext> _options;
    private readonly InMemoryDatabaseRoot _databaseRoot;

    public InMemoryDbContextFactory(string? databaseName = null)
    {
        databaseName ??= Guid.NewGuid().ToString();
        _databaseRoot = new InMemoryDatabaseRoot();

        _options = new DbContextOptionsBuilder<QuestionsHubDbContext>()
            .UseInMemoryDatabase(databaseName, _databaseRoot)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    public QuestionsHubDbContext CreateDbContext()
    {
        return new TestableQuestionsHubDbContext(_options);
    }
}

/// <summary>
/// A testable version of QuestionsHubDbContext that ignores PostgreSQL-specific properties
/// not supported by the InMemory provider (e.g., NpgsqlTsVector for full-text search).
/// </summary>
public class TestableQuestionsHubDbContext : QuestionsHubDbContext
{
    public TestableQuestionsHubDbContext(DbContextOptions<QuestionsHubDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Ignore PostgreSQL-specific properties not supported by InMemory provider
        builder.Entity<Blazor.Domain.Question>(entity =>
        {
            entity.Ignore(q => q.SearchVector);
            entity.Ignore(q => q.SearchTextNorm);
        });
    }
}
