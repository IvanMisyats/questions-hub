using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.InMemory;
using QuestionsHub.Blazor.Data;

namespace QuestionsHub.UnitTests.TestInfrastructure;

/// <summary>
/// Factory that creates in-memory database contexts for testing.
/// Each factory instance uses a unique database name.
/// </summary>
public class InMemoryDbContextFactory : IDbContextFactory<QuestionsHubDbContext>
{
    private readonly DbContextOptions<QuestionsHubDbContext> _options;

    public InMemoryDbContextFactory(string? databaseName = null)
    {
        databaseName ??= Guid.NewGuid().ToString();

        _options = new DbContextOptionsBuilder<QuestionsHubDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
    }

    public QuestionsHubDbContext CreateDbContext()
    {
        return new QuestionsHubDbContext(_options);
    }
}
