using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure;
using QuestionsHub.Blazor.Infrastructure.Favorites;
using QuestionsHub.IntegrationTests.Infrastructure;
using Xunit;

namespace QuestionsHub.IntegrationTests;

/// <summary>
/// Favorites behaviour only a real PostgreSQL shows: cascades through the app's delete paths (which do
/// not load the graph), the unique indexes and check constraint, and SQL translation of the page queries.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class FavoritesPostgresTests(PostgresFixture database)
{
    private QuestionsHubAppFactory CreateFactory()
    {
        database.SkipIfUnavailable();
        return new QuestionsHubAppFactory(database);
    }

    private sealed record Seeded(Package Package, Tour Tour1, Tour Tour2, Block Block, Question Question1, Question Question2, Question BlockQuestion);

    private static async Task<(Seeded Seeded, PackageAccessContext Admin)> SeedWithEveryKindFavorited(QuestionsHubAppFactory factory)
    {
        var package = await TestData.CreatePackage(factory, ownerId: null, status: PackageStatus.Published);
        var tours = package.Tours.OrderBy(t => t.OrderIndex).ToList();
        var tour1Questions = tours[0].Questions.OrderBy(q => q.OrderIndex).ToList();
        var seeded = new Seeded(package, tours[0], tours[1], tours[1].Blocks.Single(),
            tour1Questions[0], tour1Questions[1], tours[1].Questions.Single());

        var admin = new PackageAccessContext(IsAdmin: true, IsEditor: false, HasVerifiedEmail: true, UserId: await TestData.AdminId(factory));
        using var scope = factory.Services.CreateScope();
        var favorites = scope.ServiceProvider.GetRequiredService<FavoriteService>();
        await favorites.Add(admin, FavoriteKind.Package, package.Id);
        await favorites.Add(admin, FavoriteKind.Tour, seeded.Tour1.Id);
        await favorites.Add(admin, FavoriteKind.Tour, seeded.Tour2.Id);
        await favorites.Add(admin, FavoriteKind.Block, seeded.Block.Id);
        await favorites.Add(admin, FavoriteKind.Question, seeded.Question1.Id);
        await favorites.Add(admin, FavoriteKind.Question, seeded.Question2.Id);
        await favorites.Add(admin, FavoriteKind.Question, seeded.BlockQuestion.Id);

        return (seeded, admin);
    }

    private static async Task<List<Favorite>> FavoritesOf(QuestionsHubAppFactory factory, int packageId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        return await db.Favorites.AsNoTracking().Where(f => f.PackageId == packageId).ToListAsync();
    }

    [SkippableFact]
    public async Task DeletePackage_CascadesEveryFavoriteInIt()
    {
        await using var factory = CreateFactory();
        var (seeded, _) = await SeedWithEveryKindFavorited(factory);
        (await FavoritesOf(factory, seeded.Package.Id)).Should().HaveCount(7);

        // Same EF delete as PackageService.DeletePackage (which needs a Razor auth scope): tours and
        // questions are tracked, blocks and favorites are left to the database cascades.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            var package = await db.Packages
                .Include(p => p.Tours).ThenInclude(t => t.Questions)
                .SingleAsync(p => p.Id == seeded.Package.Id);
            db.Packages.Remove(package);
            await db.SaveChangesAsync();
        }

        (await FavoritesOf(factory, seeded.Package.Id)).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task DeleteTour_CascadesTheTourItsBlockAndItsQuestions()
    {
        await using var factory = CreateFactory();
        var (seeded, _) = await SeedWithEveryKindFavorited(factory);

        using (var scope = factory.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<PackageManagementService>().DeleteTour(seeded.Tour2.Id);
            result.Success.Should().BeTrue();
        }

        var left = await FavoritesOf(factory, seeded.Package.Id);
        left.Select(f => (f.Kind, f.QuestionId ?? f.TourId ?? f.PackageId)).Should().BeEquivalentTo(new[]
        {
            (FavoriteKind.Package, seeded.Package.Id),
            (FavoriteKind.Tour, seeded.Tour1.Id),
            (FavoriteKind.Question, seeded.Question1.Id),
            (FavoriteKind.Question, seeded.Question2.Id)
        });
    }

    [SkippableFact]
    public async Task DeleteQuestion_CascadesOnlyThatQuestion()
    {
        await using var factory = CreateFactory();
        var (seeded, _) = await SeedWithEveryKindFavorited(factory);

        using (var scope = factory.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<PackageManagementService>().DeleteQuestion(seeded.Question1.Id);
            result.Success.Should().BeTrue();
        }

        var left = await FavoritesOf(factory, seeded.Package.Id);
        left.Should().HaveCount(6).And.NotContain(f => f.QuestionId == seeded.Question1.Id);
    }

    [SkippableFact]
    public async Task DeleteBlock_CascadesTheBlockButKeepsItsQuestions()
    {
        await using var factory = CreateFactory();
        var (seeded, _) = await SeedWithEveryKindFavorited(factory);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            await db.Blocks.Where(b => b.Id == seeded.Block.Id).ExecuteDeleteAsync();
        }

        var left = await FavoritesOf(factory, seeded.Package.Id);
        left.Should().HaveCount(6).And.NotContain(f => f.BlockId == seeded.Block.Id);
        left.Should().Contain(f => f.QuestionId == seeded.BlockQuestion.Id, "Block → Question is SET NULL");
    }

    [SkippableFact]
    public async Task Database_RejectsDuplicatesAndRowsWithTwoTargets()
    {
        await using var factory = CreateFactory();
        var (seeded, admin) = await SeedWithEveryKindFavorited(factory);
        var now = DateTime.UtcNow;

        async Task<string?> Insert(Favorite favorite)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            db.Favorites.Add(favorite);
            try
            {
                await db.SaveChangesAsync();
                return null;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg)
            {
                return pg.ConstraintName;
            }
        }

        (await Insert(new Favorite { UserId = admin.UserId!, PackageId = seeded.Package.Id, QuestionId = seeded.Question1.Id, CreatedAt = now }))
            .Should().Be("IX_Favorites_QuestionId_UserId");
        (await Insert(new Favorite { UserId = admin.UserId!, PackageId = seeded.Package.Id, TourId = seeded.Tour1.Id, CreatedAt = now }))
            .Should().Be("IX_Favorites_TourId_UserId");
        (await Insert(new Favorite { UserId = admin.UserId!, PackageId = seeded.Package.Id, BlockId = seeded.Block.Id, CreatedAt = now }))
            .Should().Be("IX_Favorites_BlockId_UserId");
        (await Insert(new Favorite { UserId = admin.UserId!, PackageId = seeded.Package.Id, CreatedAt = now }))
            .Should().Be("IX_Favorites_PackageId_UserId_PackageOnly");
        (await Insert(new Favorite { UserId = admin.UserId!, PackageId = seeded.Package.Id, QuestionId = seeded.Question1.Id, TourId = seeded.Tour1.Id, CreatedAt = now }))
            .Should().Be("CK_Favorites_SingleTarget");
    }

    [SkippableFact]
    public async Task Add_Concurrently_KeepsOneRow()
    {
        await using var factory = CreateFactory();
        var package = await TestData.CreatePackage(factory, ownerId: null, status: PackageStatus.Published);
        var question = package.Tours.SelectMany(t => t.Questions).First();
        var admin = new PackageAccessContext(true, false, true, await TestData.AdminId(factory));

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<FavoriteService>().Add(admin, FavoriteKind.Question, question.Id);
        }));

        outcomes.Should().OnlyContain(o => o == AddFavoriteOutcome.Added);
        (await FavoritesOf(factory, package.Id)).Should().ContainSingle();
    }

    [SkippableFact]
    public async Task PageAndLookupQueries_TranslateToSql()
    {
        await using var factory = CreateFactory();
        var (seeded, admin) = await SeedWithEveryKindFavorited(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            await db.Packages.Where(p => p.Id == seeded.Package.Id).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.PlayedFrom, new DateOnly(2025, 12, 20))
                .SetProperty(p => p.PlayedTo, new DateOnly(2026, 1, 10)));
        }

        using var serviceScope = factory.Services.CreateScope();
        var favorites = serviceScope.ServiceProvider.GetRequiredService<FavoriteService>();
        // Another user's view: the filter and EXISTS subquery must translate for non-admins too
        var user = admin with { IsAdmin = false };

        var page = await favorites.GetPage(user, FavoriteKind.Question, new FavoriteFilter(PackageType.Www, 2026), 1);
        page.Items.Should().HaveCount(3);
        page.Items.Should().OnlyContain(f => f.Question!.Tour.Package.Id == seeded.Package.Id);
        page.CountOf(FavoriteKind.Tour).Should().Be(2);
        page.CountOf(FavoriteKind.Block).Should().Be(1);
        page.CountOf(FavoriteKind.Package).Should().Be(1);
        page.Years.Should().Contain([2025, 2026]);

        foreach (var kind in new[] { FavoriteKind.Tour, FavoriteKind.Block, FavoriteKind.Package })
            (await favorites.GetPage(user, kind, new FavoriteFilter(), 1)).Items.Should().NotBeEmpty();

        (await favorites.GetPage(user, FavoriteKind.Question, new FavoriteFilter(Year: 2024), 1)).Items.Should().BeEmpty();

        var lookup = await favorites.GetLookupForQuestions(user.UserId!, [seeded.Question1.Id, seeded.BlockQuestion.Id, int.MaxValue]);
        lookup.Has(FavoriteKind.Question, seeded.Question1.Id).Should().BeTrue();
        lookup.Has(FavoriteKind.Question, seeded.BlockQuestion.Id).Should().BeTrue();

        var packageLookup = await favorites.GetLookupForPackage(user.UserId!, seeded.Package.Id);
        packageLookup.Has(FavoriteKind.Package, seeded.Package.Id).Should().BeTrue();
        packageLookup.Has(FavoriteKind.Block, seeded.Block.Id).Should().BeTrue();
    }
}
