using System.Globalization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Favorites;
using QuestionsHub.UnitTests.TestInfrastructure;
using Xunit;

namespace QuestionsHub.UnitTests;

public class FavoriteServiceTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory = new();
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));
    private readonly FavoriteService _service;

    private static readonly int[] BothYearsNewestFirst = [2026, 2025];

    public FavoriteServiceTests()
    {
        _service = new FavoriteService(_dbFactory, _clock);
    }

    public void Dispose()
    {
        using var context = _dbFactory.CreateDbContext();
        context.Database.EnsureDeleted();
    }

    #region Add

    [Fact]
    public async Task Add_EachKind_StoresTheItemAndItsPackage()
    {
        var user = await CreateUser();
        var pkg = await CreatePackage();

        (await _service.Add(user, FavoriteKind.Question, pkg.QuestionId)).Should().Be(AddFavoriteOutcome.Added);
        (await _service.Add(user, FavoriteKind.Tour, pkg.TourId)).Should().Be(AddFavoriteOutcome.Added);
        (await _service.Add(user, FavoriteKind.Block, pkg.BlockId)).Should().Be(AddFavoriteOutcome.Added);
        (await _service.Add(user, FavoriteKind.Package, pkg.PackageId)).Should().Be(AddFavoriteOutcome.Added);

        var rows = await LoadFavorites();
        rows.Should().HaveCount(4).And.OnlyContain(f => f.PackageId == pkg.PackageId && f.UserId == user.UserId);
        rows.Select(f => f.Kind).Should().BeEquivalentTo(Enum.GetValues<FavoriteKind>());
        rows.Single(f => f.Kind == FavoriteKind.Question).QuestionId.Should().Be(pkg.QuestionId);
        rows.Single(f => f.Kind == FavoriteKind.Tour).TourId.Should().Be(pkg.TourId);
        rows.Single(f => f.Kind == FavoriteKind.Block).BlockId.Should().Be(pkg.BlockId);
        rows.Should().OnlyContain(f => f.CreatedAt == _clock.Now.UtcDateTime);
    }

    [Fact]
    public async Task Add_Twice_KeepsOneRow()
    {
        var user = await CreateUser();
        var pkg = await CreatePackage();

        await _service.Add(user, FavoriteKind.Question, pkg.QuestionId);
        (await _service.Add(user, FavoriteKind.Question, pkg.QuestionId)).Should().Be(AddFavoriteOutcome.Added);

        (await LoadFavorites()).Should().ContainSingle();
    }

    [Theory]
    [InlineData(FavoriteKind.Question)]
    [InlineData(FavoriteKind.Tour)]
    [InlineData(FavoriteKind.Block)]
    [InlineData(FavoriteKind.Package)]
    public async Task Add_MissingItem_ReturnsNotFound(FavoriteKind kind)
    {
        var user = await CreateUser();
        await CreatePackage();

        (await _service.Add(user, kind, 999_999)).Should().Be(AddFavoriteOutcome.NotFound);
        (await LoadFavorites()).Should().BeEmpty();
    }

    [Fact]
    public async Task Add_SomeoneElsesDraft_ReturnsNotFound()
    {
        var user = await CreateUser();
        var pkg = await CreatePackage(status: PackageStatus.Draft, ownerId: "someone-else");

        (await _service.Add(user, FavoriteKind.Question, pkg.QuestionId)).Should().Be(AddFavoriteOutcome.NotFound);
        (await _service.Add(user, FavoriteKind.Package, pkg.PackageId)).Should().Be(AddFavoriteOutcome.NotFound);
    }

    [Fact]
    public async Task Add_OwnDraft_IsAllowed()
    {
        var user = await CreateUser();
        var pkg = await CreatePackage(status: PackageStatus.Draft, ownerId: user.UserId);

        (await _service.Add(user, FavoriteKind.Question, pkg.QuestionId)).Should().Be(AddFavoriteOutcome.Added);
    }

    [Fact]
    public async Task Add_EditorsOnlyPackage_ReturnsNotFoundForNonEditor()
    {
        var user = await CreateUser();
        var pkg = await CreatePackage(accessLevel: PackageAccessLevel.EditorsOnly);

        (await _service.Add(user, FavoriteKind.Tour, pkg.TourId)).Should().Be(AddFavoriteOutcome.NotFound);
        (await _service.Add(user with { IsEditor = true }, FavoriteKind.Tour, pkg.TourId)).Should().Be(AddFavoriteOutcome.Added);
    }

    [Fact]
    public async Task Add_AtTheCap_ReturnsLimitReached_ButAnExistingFavoriteStillSucceeds()
    {
        var user = await CreateUser();
        var pkg = await CreatePackage();
        await _service.Add(user, FavoriteKind.Package, pkg.PackageId);
        await SeedFavorites(user.UserId!, pkg.PackageId, FavoriteService.MaxFavoritesPerUser - 1);

        (await _service.Add(user, FavoriteKind.Question, pkg.QuestionId)).Should().Be(AddFavoriteOutcome.LimitReached);
        (await _service.Add(user, FavoriteKind.Package, pkg.PackageId)).Should().Be(AddFavoriteOutcome.Added);
    }

    [Fact]
    public async Task Add_Anonymous_Throws()
    {
        var anonymous = new PackageAccessContext(false, false, false, null);

        var act = () => _service.Add(anonymous, FavoriteKind.Package, 1);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region Remove

    [Fact]
    public async Task Remove_Package_KeepsFavoritesInsideIt()
    {
        var user = await CreateUser();
        var pkg = await CreatePackage();
        await _service.Add(user, FavoriteKind.Package, pkg.PackageId);
        await _service.Add(user, FavoriteKind.Question, pkg.QuestionId);

        await _service.Remove(user.UserId!, FavoriteKind.Package, pkg.PackageId);

        (await LoadFavorites()).Should().ContainSingle().Which.Kind.Should().Be(FavoriteKind.Question);
    }

    [Fact]
    public async Task Remove_OnlyTouchesTheCallersFavorite()
    {
        var user = await CreateUser();
        var other = await CreateUser();
        var pkg = await CreatePackage();
        await _service.Add(user, FavoriteKind.Question, pkg.QuestionId);
        await _service.Add(other, FavoriteKind.Question, pkg.QuestionId);

        await _service.Remove(user.UserId!, FavoriteKind.Question, pkg.QuestionId);

        (await LoadFavorites()).Should().ContainSingle().Which.UserId.Should().Be(other.UserId);
    }

    [Fact]
    public async Task Remove_NotAFavorite_DoesNothing()
    {
        var user = await CreateUser();

        var act = () => _service.Remove(user.UserId!, FavoriteKind.Block, 42);

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Lookups

    [Fact]
    public async Task GetLookupForPackage_ReturnsEveryKind_ForThatUserAndPackageOnly()
    {
        var user = await CreateUser();
        var other = await CreateUser();
        var pkg = await CreatePackage();
        var otherPkg = await CreatePackage();
        await _service.Add(user, FavoriteKind.Question, pkg.QuestionId);
        await _service.Add(user, FavoriteKind.Tour, pkg.TourId);
        await _service.Add(user, FavoriteKind.Block, pkg.BlockId);
        await _service.Add(user, FavoriteKind.Package, pkg.PackageId);
        await _service.Add(user, FavoriteKind.Question, otherPkg.QuestionId);
        await _service.Add(other, FavoriteKind.Question, pkg.SecondQuestionId);

        var lookup = await _service.GetLookupForPackage(user.UserId!, pkg.PackageId);

        lookup.IsAnonymous.Should().BeFalse();
        lookup.Has(FavoriteKind.Question, pkg.QuestionId).Should().BeTrue();
        lookup.Has(FavoriteKind.Tour, pkg.TourId).Should().BeTrue();
        lookup.Has(FavoriteKind.Block, pkg.BlockId).Should().BeTrue();
        lookup.Has(FavoriteKind.Package, pkg.PackageId).Should().BeTrue();
        lookup.Has(FavoriteKind.Question, otherPkg.QuestionId).Should().BeFalse();
        lookup.Has(FavoriteKind.Question, pkg.SecondQuestionId).Should().BeFalse();
    }

    [Fact]
    public async Task GetLookupForQuestions_ReturnsOnlyTheRequestedFavorites()
    {
        var user = await CreateUser();
        var pkg = await CreatePackage();
        var otherPkg = await CreatePackage();
        await _service.Add(user, FavoriteKind.Question, pkg.QuestionId);
        await _service.Add(user, FavoriteKind.Question, otherPkg.QuestionId);
        await _service.Add(user, FavoriteKind.Tour, pkg.TourId);

        var lookup = await _service.GetLookupForQuestions(user.UserId!, [pkg.QuestionId, pkg.SecondQuestionId]);

        lookup.Has(FavoriteKind.Question, pkg.QuestionId).Should().BeTrue();
        lookup.Has(FavoriteKind.Question, pkg.SecondQuestionId).Should().BeFalse();
        lookup.Has(FavoriteKind.Question, otherPkg.QuestionId).Should().BeFalse();
        lookup.Has(FavoriteKind.Tour, pkg.TourId).Should().BeFalse();
    }

    [Fact]
    public async Task GetLookupForQuestions_NoIds_ReturnsAnEmptyLookup()
    {
        var lookup = await _service.GetLookupForQuestions("user", []);

        lookup.IsAnonymous.Should().BeFalse();
        lookup.Has(FavoriteKind.Question, 1).Should().BeFalse();
    }

    [Fact]
    public async Task ViewerLookups_AnonymousGetsLoginHearts_UnverifiedGetsNone_VerifiedGetsTheirFavorites()
    {
        var user = await CreateUser();
        var pkg = await CreatePackage();
        await _service.Add(user, FavoriteKind.Question, pkg.QuestionId);
        var anonymous = new PackageAccessContext(false, false, false, null);
        var unverified = user with { HasVerifiedEmail = false };

        (await _service.GetLookupForPackage(anonymous, pkg.PackageId)).Should().BeSameAs(FavoriteLookup.Anonymous);
        (await _service.GetLookupForQuestions(anonymous, [pkg.QuestionId])).Should().BeSameAs(FavoriteLookup.Anonymous);
        (await _service.GetLookupForPackage(unverified, pkg.PackageId)).Should().BeNull();
        (await _service.GetLookupForQuestions(unverified, [pkg.QuestionId])).Should().BeNull();
        (await _service.GetLookupForPackage(user, pkg.PackageId))!.Has(FavoriteKind.Question, pkg.QuestionId).Should().BeTrue();
        (await _service.GetLookupForQuestions(user, [pkg.QuestionId]))!.Has(FavoriteKind.Question, pkg.QuestionId).Should().BeTrue();
    }

    [Fact]
    public void Lookup_AnonymousIsSharedAndReadOnly()
    {
        FavoriteLookup.Anonymous.IsAnonymous.Should().BeTrue();
        FavoriteLookup.Anonymous.Has(FavoriteKind.Question, 1).Should().BeFalse();
        FluentActions.Invoking(() => FavoriteLookup.Anonymous.Add(FavoriteKind.Question, 1))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void KindSegments_RoundTrip_AndRejectAnythingElse()
    {
        foreach (var kind in Enum.GetValues<FavoriteKind>())
            FavoriteKindSegments.Parse(kind.ToSegment()).Should().Be(kind);

        FavoriteKindSegments.Parse("Package").Should().BeNull();
        FavoriteKindSegments.Parse("0").Should().BeNull();
        FavoriteKindSegments.Parse("").Should().BeNull();
    }

    #endregion

    #region GetPage

    [Fact]
    public async Task GetPage_ReturnsNewestFirst_WithCountsPerKind_AndQuestionDetails()
    {
        var user = await CreateUser();
        var pkg = await CreatePackage();
        await _service.Add(user, FavoriteKind.Question, pkg.QuestionId);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _service.Add(user, FavoriteKind.Question, pkg.SecondQuestionId);
        await _service.Add(user, FavoriteKind.Tour, pkg.TourId);
        await _service.Add(user, FavoriteKind.Package, pkg.PackageId);

        var page = await _service.GetPage(user, FavoriteKind.Question, new FavoriteFilter(), 1);

        page.Items.Select(f => f.QuestionId).Should().Equal(pkg.SecondQuestionId, pkg.QuestionId);
        page.CountOf(FavoriteKind.Question).Should().Be(2);
        page.CountOf(FavoriteKind.Tour).Should().Be(1);
        page.CountOf(FavoriteKind.Block).Should().Be(0);
        page.CountOf(FavoriteKind.Package).Should().Be(1);

        var question = page.Items.Single(f => f.QuestionId == pkg.QuestionId).Question!;
        question.Tour.Package.Title.Should().Be("Пакет");
        question.Tour.Package.Tags.Should().ContainSingle(t => t.Name == "18+");
        question.Authors.Should().ContainSingle(a => a.LastName == "Автор");
    }

    [Theory]
    [InlineData(FavoriteKind.Tour)]
    [InlineData(FavoriteKind.Block)]
    [InlineData(FavoriteKind.Package)]
    public async Task GetPage_OtherKinds_LoadThePackage(FavoriteKind kind)
    {
        var user = await CreateUser();
        var pkg = await CreatePackage();
        await _service.Add(user, kind, kind switch
        {
            FavoriteKind.Tour => pkg.TourId,
            FavoriteKind.Block => pkg.BlockId,
            _ => pkg.PackageId
        });

        var page = await _service.GetPage(user, kind, new FavoriteFilter(), 1);

        var item = page.Items.Should().ContainSingle().Subject;
        item.Package.Title.Should().Be("Пакет");
        if (kind == FavoriteKind.Tour)
            item.Tour!.Number.Should().Be("1");
        if (kind == FavoriteKind.Block)
            item.Block!.Tour.Id.Should().Be(pkg.TourId);
    }

    [Fact]
    public async Task GetPage_Blocks_NumbersUnnamedBlocksAcrossThePackage()
    {
        var user = await CreateUser();
        int namedId, unnamedId;
        using (var context = _dbFactory.CreateDbContext())
        {
            var named = new Block { OrderIndex = 0, Name = "Блок про космос" };
            var unnamed = new Block { OrderIndex = 0 };
            var package = new Package
            {
                Title = "Пакет з блоками",
                Status = PackageStatus.Published,
                Tours =
                [
                    new Tour { Number = "2", OrderIndex = 0, Blocks = [unnamed] },
                    new Tour { Number = "1", OrderIndex = 1, Blocks = [new Block { OrderIndex = 0 }, named] }
                ]
            };
            context.Packages.Add(package);
            await context.SaveChangesAsync();
            (namedId, unnamedId) = (named.Id, unnamed.Id);
        }
        await _service.Add(user, FavoriteKind.Block, namedId);
        await _service.Add(user, FavoriteKind.Block, unnamedId);

        var page = await _service.GetPage(user, FavoriteKind.Block, new FavoriteFilter(), 1);

        page.Items.Should().HaveCount(2);
        // Tour «1» comes first on the page (ЩДК order by number), so the block of tour «2» is the third
        page.BlockNumbers[unnamedId].Should().Be(3);
        page.Items.Single(f => f.BlockId == namedId).Block!.GetDisplayName(page.BlockNumbers[namedId]).Should().Be("Блок про космос");
    }

    [Fact]
    public async Task GetPage_IsPaged()
    {
        var user = await CreateUser();
        var pkg = await CreatePackage();
        await SeedFavorites(user.UserId!, pkg.PackageId, FavoriteService.PageSize + 5);

        var first = await _service.GetPage(user, FavoriteKind.Question, new FavoriteFilter(), 1);
        var second = await _service.GetPage(user, FavoriteKind.Question, new FavoriteFilter(), 2);

        first.Items.Should().HaveCount(FavoriteService.PageSize);
        second.Items.Should().HaveCount(5);
        first.Items.Select(f => f.Id).Should().NotIntersectWith(second.Items.Select(f => f.Id));
        first.CountOf(FavoriteKind.Question).Should().Be(FavoriteService.PageSize + 5);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(200_000_000)]
    public async Task GetPage_HugePageNumber_ReturnsAnEmptyPage(int pageNumber)
    {
        var user = await CreateUser();
        var pkg = await CreatePackage();
        await _service.Add(user, FavoriteKind.Question, pkg.QuestionId);

        var page = await _service.GetPage(user, FavoriteKind.Question, new FavoriteFilter(), pageNumber);

        page.Items.Should().BeEmpty();
        page.CountOf(FavoriteKind.Question).Should().Be(1);
    }

    [Fact]
    public async Task GetPage_HidesFavoritesOfPackagesTheUserCanNoLongerView()
    {
        var user = await CreateUser();
        var visible = await CreatePackage();
        var hidden = await CreatePackage();
        await _service.Add(user, FavoriteKind.Question, visible.QuestionId);
        await _service.Add(user, FavoriteKind.Question, hidden.QuestionId);
        await SetStatus(hidden.PackageId, PackageStatus.Draft);

        var page = await _service.GetPage(user, FavoriteKind.Question, new FavoriteFilter(), 1);

        page.Items.Should().ContainSingle().Which.QuestionId.Should().Be(visible.QuestionId);
        page.CountOf(FavoriteKind.Question).Should().Be(1);
        (await LoadFavorites()).Should().HaveCount(2, "the hidden favorite is kept, in case access returns");
    }

    [Fact]
    public async Task GetPage_FiltersByGameType()
    {
        var user = await CreateUser();
        var www = await CreatePackage();
        var shvager = await CreatePackage(type: PackageType.Shvager);
        await _service.Add(user, FavoriteKind.Package, www.PackageId);
        await _service.Add(user, FavoriteKind.Package, shvager.PackageId);

        var page = await _service.GetPage(user, FavoriteKind.Package, new FavoriteFilter(Type: PackageType.Shvager), 1);

        page.Items.Should().ContainSingle().Which.PackageId.Should().Be(shvager.PackageId);
        page.CountOf(FavoriteKind.Package).Should().Be(1);
    }

    [Fact]
    public async Task GetPage_FiltersByPlayedYear_FallingBackToPublicationYear()
    {
        var user = await CreateUser();
        var played2025 = await CreatePackage(playedFrom: new DateOnly(2025, 5, 1));
        var spans2025To2026 = await CreatePackage(playedFrom: new DateOnly(2025, 11, 1), playedTo: new DateOnly(2026, 2, 1));
        var published2026 = await CreatePackage(publicationDate: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        var undated = await CreatePackage();
        foreach (var pkg in new[] { played2025, spans2025To2026, published2026, undated })
            await _service.Add(user, FavoriteKind.Package, pkg.PackageId);

        var in2026 = await _service.GetPage(user, FavoriteKind.Package, new FavoriteFilter(Year: 2026), 1);
        var in2025 = await _service.GetPage(user, FavoriteKind.Package, new FavoriteFilter(Year: 2025), 1);

        in2026.Items.Select(f => f.PackageId).Should().BeEquivalentTo([spans2025To2026.PackageId, published2026.PackageId]);
        in2025.Items.Select(f => f.PackageId).Should().BeEquivalentTo([played2025.PackageId, spans2025To2026.PackageId]);
        in2026.Years.Should().Equal(BothYearsNewestFirst, "the year list ignores the year filter");
    }

    [Fact]
    public async Task GetPage_YearList_FollowsTheTypeFilter()
    {
        var user = await CreateUser();
        var www = await CreatePackage(playedFrom: new DateOnly(2024, 1, 1));
        var shvager = await CreatePackage(type: PackageType.Shvager, playedFrom: new DateOnly(2026, 1, 1));
        await _service.Add(user, FavoriteKind.Package, www.PackageId);
        await _service.Add(user, FavoriteKind.Package, shvager.PackageId);

        var page = await _service.GetPage(user, FavoriteKind.Package, new FavoriteFilter(Type: PackageType.Shvager), 1);

        page.Years.Should().Equal(2026);
    }

    [Theory]
    [InlineData("2025-05-01", null, null, new[] { 2025 })]
    [InlineData("2024-11-01", "2026-02-01", null, new[] { 2024, 2025, 2026 })]
    [InlineData("2026-02-01", "2025-01-01", null, new[] { 2026 })]
    [InlineData(null, null, "2023-06-01", new[] { 2023 })]
    [InlineData("2025-05-01", null, "2023-06-01", new[] { 2025 })]
    [InlineData(null, null, null, new int[0])]
    public void PlayedYears_UsesThePlayedPeriod_ThenThePublicationYear(
        string? from, string? to, string? published, int[] expected)
    {
        var years = FavoriteService.PlayedYears(
            from == null ? null : DateOnly.Parse(from, CultureInfo.InvariantCulture),
            to == null ? null : DateOnly.Parse(to, CultureInfo.InvariantCulture),
            published == null ? null : DateTime.SpecifyKind(DateTime.Parse(published, CultureInfo.InvariantCulture), DateTimeKind.Utc));

        years.Should().Equal(expected);
    }

    #endregion

    #region Helpers

    private sealed record SeededPackage(int PackageId, int TourId, int BlockId, int QuestionId, int SecondQuestionId);

    private async Task<PackageAccessContext> CreateUser()
    {
        using var context = _dbFactory.CreateDbContext();
        var user = new ApplicationUser
        {
            FirstName = "Ірина",
            LastName = "Тестова",
            UserName = Guid.NewGuid().ToString(),
            EmailConfirmed = true
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return new PackageAccessContext(IsAdmin: false, IsEditor: false, HasVerifiedEmail: true, UserId: user.Id);
    }

    private async Task<SeededPackage> CreatePackage(
        PackageStatus status = PackageStatus.Published,
        PackageAccessLevel accessLevel = PackageAccessLevel.All,
        string? ownerId = null,
        PackageType type = PackageType.Www,
        DateOnly? playedFrom = null,
        DateOnly? playedTo = null,
        DateTime? publicationDate = null)
    {
        using var context = _dbFactory.CreateDbContext();
        var author = new Author { FirstName = "Тест", LastName = "Автор" };
        var block = new Block { OrderIndex = 0 };
        var question = new Question { Number = "1", Text = "Текст", Answer = "Відповідь", Block = block, Authors = [author] };
        var second = new Question { Number = "2", Text = "Текст 2", Answer = "Відповідь 2", OrderIndex = 1 };
        var tour = new Tour { Number = "1", Blocks = [block], Questions = [question, second] };
        var package = new Package
        {
            Title = "Пакет",
            Status = status,
            AccessLevel = accessLevel,
            OwnerId = ownerId,
            Type = type,
            PlayedFrom = playedFrom,
            PlayedTo = playedTo,
            PublicationDate = publicationDate,
            Tours = [tour],
            Tags = [new Tag { Name = "18+" }]
        };
        context.Packages.Add(package);
        await context.SaveChangesAsync();
        return new SeededPackage(package.Id, tour.Id, block.Id, question.Id, second.Id);
    }

    private async Task SeedFavorites(string userId, int packageId, int count)
    {
        using var context = _dbFactory.CreateDbContext();
        var start = _clock.Now.UtcDateTime;
        context.Favorites.AddRange(Enumerable.Range(0, count).Select(i => new Favorite
        {
            UserId = userId,
            PackageId = packageId,
            QuestionId = 1_000_000 + i,
            CreatedAt = start.AddSeconds(-i)
        }));
        await context.SaveChangesAsync();
    }

    private async Task SetStatus(int packageId, PackageStatus status)
    {
        using var context = _dbFactory.CreateDbContext();
        var package = await context.Packages.SingleAsync(p => p.Id == packageId);
        package.Status = status;
        await context.SaveChangesAsync();
    }

    private async Task<List<Favorite>> LoadFavorites()
    {
        using var context = _dbFactory.CreateDbContext();
        return await context.Favorites.AsNoTracking().ToListAsync();
    }

    #endregion
}
