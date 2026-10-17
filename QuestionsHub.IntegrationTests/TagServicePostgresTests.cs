using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Infrastructure;
using QuestionsHub.IntegrationTests.Infrastructure;
using Xunit;

namespace QuestionsHub.IntegrationTests;

/// <summary>TagService.GetOrCreate on PostgreSQL: names are matched literally, case-insensitively (imports use it).</summary>
[Collection(IntegrationCollection.Name)]
public sealed class TagServicePostgresTests(PostgresFixture database)
{
    [SkippableFact]
    public async Task GetOrCreate_MatchesLiterally_NotAsAPattern()
    {
        database.SkipIfUnavailable();
        await using var factory = new QuestionsHubAppFactory(database);
        using var scope = factory.Services.CreateScope();
        var tags = scope.ServiceProvider.GetRequiredService<TagService>();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var years = await tags.GetOrCreate(db, $"100 років {suffix}");
        var underscore = await tags.GetOrCreate(db, $"a_b {suffix}");

        (await tags.GetOrCreate(db, $"100% {suffix}")).Id.Should().NotBe(years.Id);
        (await tags.GetOrCreate(db, $"a%b {suffix}")).Id.Should().NotBe(underscore.Id);
        (await tags.GetOrCreate(db, $"aXb {suffix}")).Id.Should().NotBe(underscore.Id);
        (await tags.GetOrCreate(db, $"100 РОКІВ {suffix}")).Id.Should().Be(years.Id, "the match is case-insensitive, Cyrillic included");
    }
}
