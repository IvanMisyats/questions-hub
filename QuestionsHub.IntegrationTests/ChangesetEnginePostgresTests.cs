using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.Blazor.Infrastructure.AgentApi.Changesets;
using QuestionsHub.IntegrationTests.Infrastructure;
using Xunit;

namespace QuestionsHub.IntegrationTests;

/// <summary>
/// Engine behaviour that only a real PostgreSQL shows: unsaved entities keep id 0 (temporary keys
/// live in the change tracker), generated ids after saving, and Cyrillic case-insensitive matching.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChangesetEnginePostgresTests(PostgresFixture database)
{
    private QuestionsHubAppFactory CreateFactory()
    {
        database.SkipIfUnavailable();
        return new QuestionsHubAppFactory(database);
    }

    private static async Task<(ChangesetEngine Engine, Package Package)> Open(QuestionsHubDbContext context, int packageId)
    {
        var package = await context.Packages.WithEditableGraph().FirstAsync(p => p.Id == packageId);
        var engine = new ChangesetEngine(context, package, new PackageRenumberingService(new SingleContextFactory(context)));
        return (engine, package);
    }

    private static async Task Apply(ChangesetEngine engine, string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        await engine.Apply(ChangesetParser.Parse(document.RootElement));
    }

    [SkippableFact]
    public async Task SharedEditors_CopiesEveryNewUnsavedAuthor()
    {
        await using var factory = CreateFactory();
        var seeded = await TestData.CreatePackage(factory, ownerId: null);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        var (engine, package) = await Open(context, seeded.Id);
        var tour1 = package.Tours.OrderBy(t => t.OrderIndex).First();
        var block = package.Tours.SelectMany(t => t.Blocks).Single();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await Apply(engine, $$"""
            [ { "op": "setTourEditors", "tourId": {{tour1.Id}}, "authors": [ { "firstName": "Перший", "lastName": "{{suffix}}" } ] },
              { "op": "setBlockEditors", "blockId": {{block.Id}}, "authors": [ { "firstName": "Другий", "lastName": "{{suffix}}" } ] },
              { "op": "setSharedEditors", "value": true } ]
            """);

        package.PackageEditors.Should().HaveCount(2, "both new authors still have id 0 and must not be merged");
        package.PackageEditors.Should().OnlyContain(a => a.Id == 0);

        await context.SaveChangesAsync();
        var dtos = ChangeFormatter.ToDtos(engine.Changes, package, context);
        dtos.Single(c => c.Entity == "package" && c.Field == "editors").After!.AsArray()
            .Select(a => a!["id"]).Should().OnlyContain(id => id != null, "ids are real once saved");
    }

    [SkippableFact]
    public async Task DryRunDiff_ReportsNewAuthorsWithoutIds()
    {
        await using var factory = CreateFactory();
        var seeded = await TestData.CreatePackage(factory, ownerId: null);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        var (engine, package) = await Open(context, seeded.Id);
        var question = package.Tours.SelectMany(t => t.Questions).First();

        await Apply(engine, $$"""[ { "op": "setQuestionAuthors", "questionId": {{question.Id}}, "authors": [ { "firstName": "Без", "lastName": "Id{{Guid.NewGuid():N}}" } ] } ]""");

        var dtos = ChangeFormatter.ToDtos(engine.Changes, package, context);
        dtos.Single().After!.AsArray().Single()!["id"].Should().BeNull();
    }

    [SkippableFact]
    public async Task Tags_MatchCyrillicCaseInsensitively_InPostgres()
    {
        await using var factory = CreateFactory();
        var seeded = await TestData.CreatePackage(factory, ownerId: null);
        var tagName = $"Кубок{Guid.NewGuid():N}"[..20];
        using (var seedScope = factory.Services.CreateScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            seedContext.Tags.Add(new Tag { Name = tagName });
            await seedContext.SaveChangesAsync();
        }

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        var (engine, package) = await Open(context, seeded.Id);

        await Apply(engine, $$"""[ { "op": "setTags", "tags": [ "{{tagName.ToUpperInvariant()}}" ] } ]""");
        await context.SaveChangesAsync();

        package.Tags.Should().ContainSingle().Which.Name.Should().Be(tagName, "the existing tag is reused");
        (await context.Tags.CountAsync(t => t.Name == tagName)).Should().Be(1);
        (await context.Tags.AnyAsync(t => t.Name == tagName.ToUpperInvariant())).Should().BeFalse("no upper-case duplicate was created");
    }

    /// <summary>The renumbering service only needs a factory for its DB-based methods; the engine uses the in-memory one.</summary>
    private sealed class SingleContextFactory(QuestionsHubDbContext context) : IDbContextFactory<QuestionsHubDbContext>
    {
        public QuestionsHubDbContext CreateDbContext() => context;
    }
}
