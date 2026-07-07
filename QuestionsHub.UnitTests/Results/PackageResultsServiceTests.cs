using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Results;
using QuestionsHub.UnitTests.TestInfrastructure;
using Xunit;

namespace QuestionsHub.UnitTests.Results;

public class PackageResultsServiceTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory = new();

    public void Dispose()
    {
        using var db = _dbFactory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    #region Helpers

    private PackageResultsService CreateService(FakeResultsHandler handler)
    {
        var httpFactory = new FakeHttpClientFactory(handler);
        var options = Options.Create(new ResultsOptions());
        return new PackageResultsService(
            _dbFactory,
            new RatingResultsClient(httpFactory, options),
            new OpenQuizResultsClient(httpFactory, options),
            options,
            NullLogger<PackageResultsService>.Instance);
    }

    private async Task<int> SeedWwwPackage(params (TourType Type, string Number, int QuestionCount)[] tours)
    {
        await using var db = _dbFactory.CreateDbContext();
        var package = new Package { Title = "Пакет", Type = PackageType.Www };
        foreach (var (type, number, count) in tours)
        {
            var tour = new Tour { Number = number, Type = type, OrderIndex = package.Tours.Count };
            for (var i = 0; i < count; i++)
            {
                tour.Questions.Add(new Question
                {
                    OrderIndex = i,
                    Number = (i + 1).ToString(),
                    Text = "текст",
                    Answer = "відповідь"
                });
            }

            package.Tours.Add(tour);
        }

        db.Packages.Add(package);
        await db.SaveChangesAsync();
        return package.Id;
    }

    /// <summary>Package question ids in stats-mapping order (regular tours by Number, questions by OrderIndex).</summary>
    private async Task<List<int>> GetRegularQuestionIds(int packageId)
    {
        await using var db = _dbFactory.CreateDbContext();
        var tours = await db.Tours
            .Include(t => t.Questions)
            .Where(t => t.PackageId == packageId && t.Type == TourType.Regular)
            .ToListAsync();
        return tours
            .OrderBy(t => int.TryParse(t.Number, out var num) ? num : int.MaxValue)
            .SelectMany(t => t.Questions.OrderBy(q => q.OrderIndex))
            .Select(q => q.Id)
            .ToList();
    }

    private async Task<Dictionary<int, QuestionStat>> GetStats(int packageId)
    {
        await using var db = _dbFactory.CreateDbContext();
        var questionIds = await db.Questions
            .Where(q => q.Tour.PackageId == packageId)
            .Select(q => q.Id)
            .ToListAsync();
        return await db.QuestionStats
            .Where(s => questionIds.Contains(s.QuestionId))
            .ToDictionaryAsync(s => s.QuestionId);
    }

    private static FakeResultsHandler HandlerForRating13097()
    {
        return new FakeResultsHandler()
            .Add("/tournaments/13097/results", ResultsFixtures.Read("rating_results_13097.json"))
            .Add("/tournaments/13097", ResultsFixtures.Read("rating_tournament_13097.json"));
    }

    /// <summary>Synthetic small Rating tournament: one tour, 3 questions, given team masks.</summary>
    private static FakeResultsHandler AddSyntheticRating(FakeResultsHandler handler, int tournamentId, params (string Name, string Mask)[] teams)
    {
        var teamsJson = string.Join(",", teams.Select((t, i) =>
            $$"""{"team":{"id":{{i + 1}},"name":"{{t.Name}}","town":null},"mask":"{{t.Mask}}","questionsTotal":{{t.Mask.Count(c => c == '1')}},"position":{{i + 1}}.0}"""));
        return handler
            .Add($"/tournaments/{tournamentId}/results", $"[{teamsJson}]")
            .Add($"/tournaments/{tournamentId}", $$"""{"id":{{tournamentId}},"questionQty":{"1":3},"hideResultsTo":"2000-01-01T00:00:00+00:00"}""");
    }

    #endregion

    #region AttachSource

    [Fact]
    public async Task AttachSource_RatingUrl_DetectsPlatformAndCreatesSource()
    {
        var packageId = await SeedWwwPackage((TourType.Regular, "1", 3));
        var service = CreateService(new FakeResultsHandler());

        var source = await service.AttachSource(packageId, "https://rating.chgk.info/tournament/13097");

        source.Id.Should().BePositive();
        source.Platform.Should().Be(ResultsPlatform.Rating);
        source.ExternalId.Should().Be("13097");
        source.LoadedAt.Should().BeNull();
        source.DisplayUrl.Should().Be("https://rating.chgk.info/tournament/13097");
        source.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData("https://rating.chgk.info/tournament/13097/statistics")]
    [InlineData("https://rating.chgk.info/tournament/13097/tours")]
    [InlineData("https://rating.chgk.info/tournament/13097")]
    [InlineData("13097")]
    public async Task AttachSource_SameTournamentDifferentUrlForms_RejectedAsDuplicate(string secondUrl)
    {
        var packageId = await SeedWwwPackage((TourType.Regular, "1", 3));
        var service = CreateService(new FakeResultsHandler());
        await service.AttachSource(packageId, "https://rating.chgk.info/tournament/13097");

        var act = () => service.AttachSource(packageId, secondUrl);

        await act.Should().ThrowAsync<ResultsLoadException>().WithMessage("*вже додано*");
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("https://www.open-quiz.com/results.html?quiz=25795")] // OpenQuiz host but no token — error, not «Other»
    public async Task AttachSource_InvalidUrl_Throws(string url)
    {
        var packageId = await SeedWwwPackage((TourType.Regular, "1", 3));
        var service = CreateService(new FakeResultsHandler());

        var act = () => service.AttachSource(packageId, url);

        await act.Should().ThrowAsync<ResultsLoadException>();
    }

    [Fact]
    public async Task AttachSource_ShvagerPackage_Throws()
    {
        int packageId;
        await using (var db = _dbFactory.CreateDbContext())
        {
            var package = new Package { Title = "Своя гра", Type = PackageType.Shvager };
            db.Packages.Add(package);
            await db.SaveChangesAsync();
            packageId = package.Id;
        }

        var service = CreateService(new FakeResultsHandler());

        var act = () => service.AttachSource(packageId, "13097");

        await act.Should().ThrowAsync<ResultsLoadException>().WithMessage("*Що?Де?Коли?*");
    }

    [Fact]
    public async Task AttachSource_OtherPlatform_SetsDisplayUrlImmediately()
    {
        var packageId = await SeedWwwPackage((TourType.Regular, "1", 3));
        var service = CreateService(new FakeResultsHandler());

        var source = await service.AttachSource(packageId, "https://docs.google.com/spreadsheets/d/abc");

        source.DisplayUrl.Should().Be("https://docs.google.com/spreadsheets/d/abc");
        source.ExternalId.Should().BeNull();
    }

    #endregion

    #region LoadSource

    [Fact]
    public async Task LoadSource_Rating13097_LoadsTeamsAndComputesStats()
    {
        var packageId = await SeedWwwPackage(
            (TourType.Regular, "1", 12), (TourType.Regular, "2", 12), (TourType.Regular, "3", 12));
        var service = CreateService(HandlerForRating13097());
        var source = await service.AttachSource(packageId, "13097");

        var loaded = await service.LoadSource(source.Id);

        loaded.LoadedAt.Should().NotBeNull();
        loaded.LoadError.Should().BeNull();
        loaded.TeamsCount.Should().Be(17);
        loaded.StatsMapped.Should().BeTrue();
        loaded.RawPayload.Should().NotBeNullOrEmpty();
        loaded.DisplayUrl.Should().Be("https://rating.chgk.info/tournament/13097");

        var questionIds = await GetRegularQuestionIds(packageId);
        var stats = await GetStats(packageId);
        stats.Should().HaveCount(36);
        stats[questionIds[10]].Should().Match<QuestionStat>(s => s.CorrectCount == 14 && s.TotalTeams == 17); // Q11
        stats[questionIds[5]].Should().Match<QuestionStat>(s => s.CorrectCount == 0 && s.TotalTeams == 17);   // Q6

        await using var db = _dbFactory.CreateDbContext();
        (await db.TeamResults.CountAsync(t => t.ResultsSourceId == source.Id)).Should().Be(17);
    }

    [Fact]
    public async Task LoadSource_QuestionCountMismatch_LoadsStandingsWithoutStats()
    {
        var packageId = await SeedWwwPackage((TourType.Regular, "1", 12), (TourType.Regular, "2", 12));
        var service = CreateService(HandlerForRating13097());
        var source = await service.AttachSource(packageId, "13097");

        var loaded = await service.LoadSource(source.Id);

        loaded.TeamsCount.Should().Be(17);
        loaded.StatsMapped.Should().BeFalse();
        loaded.WarningsJson.Should().Contain("не збігається");

        (await GetStats(packageId)).Should().BeEmpty();
        await using var db = _dbFactory.CreateDbContext();
        var teams = await db.TeamResults.Where(t => t.ResultsSourceId == source.Id).ToListAsync();
        teams.Should().HaveCount(17);
        teams.Should().OnlyContain(t => t.ResultsByQuestionJson == null);
    }

    [Fact]
    public async Task LoadSource_FailedReload_KeepsPreviousResults()
    {
        var packageId = await SeedWwwPackage(
            (TourType.Regular, "1", 12), (TourType.Regular, "2", 12), (TourType.Regular, "3", 12));
        var okService = CreateService(HandlerForRating13097());
        var source = await okService.AttachSource(packageId, "13097");
        await okService.LoadSource(source.Id);
        var loadedAt = (await okService.GetSourcesForPackage(packageId)).Single().LoadedAt;

        var failingService = CreateService(new FakeResultsHandler()); // every request → 404
        var reloaded = await failingService.LoadSource(source.Id);

        reloaded.LoadError.Should().NotBeNullOrEmpty();
        reloaded.LoadedAt.Should().Be(loadedAt); // unchanged

        var questionIds = await GetRegularQuestionIds(packageId);
        var stats = await GetStats(packageId);
        stats[questionIds[10]].CorrectCount.Should().Be(14); // old stats intact
        await using var db = _dbFactory.CreateDbContext();
        (await db.TeamResults.CountAsync(t => t.ResultsSourceId == source.Id)).Should().Be(17);
    }

    [Fact]
    public async Task LoadSource_EmbargoedTournament_StoresEmbargoDateAndNoResults()
    {
        var packageId = await SeedWwwPackage((TourType.Regular, "1", 36));
        var handler = new FakeResultsHandler()
            .Add("/tournaments/99999/results", ResultsFixtures.Read("rating_results_embargoed.json"))
            .Add("/tournaments/99999", ResultsFixtures.Read("rating_tournament_embargoed.json"));
        var service = CreateService(handler);
        var source = await service.AttachSource(packageId, "99999");

        var loaded = await service.LoadSource(source.Id);

        loaded.LoadError.Should().Contain("приховано");
        loaded.ResultsAvailableAfter.Should().Be(new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        loaded.LoadedAt.Should().BeNull();
        loaded.TeamsCount.Should().BeNull();

        await using var db = _dbFactory.CreateDbContext();
        (await db.TeamResults.CountAsync(t => t.ResultsSourceId == source.Id)).Should().Be(0);
    }

    [Fact]
    public async Task LoadSource_TwoSources_StatsSummed_ReloadAndDeleteStayConsistent()
    {
        var packageId = await SeedWwwPackage((TourType.Regular, "1", 3));
        var handler = new FakeResultsHandler();
        AddSyntheticRating(handler, 101, ("Перша", "110"), ("Друга", "010"));
        AddSyntheticRating(handler, 102, ("Третя", "101"));
        var service = CreateService(handler);

        var source1 = await service.AttachSource(packageId, "101");
        var source2 = await service.AttachSource(packageId, "102");
        await service.LoadSource(source1.Id);
        await service.LoadSource(source2.Id);

        var questionIds = await GetRegularQuestionIds(packageId);
        var stats = await GetStats(packageId);
        stats[questionIds[0]].Should().Match<QuestionStat>(s => s.CorrectCount == 2 && s.TotalTeams == 3);
        stats[questionIds[1]].Should().Match<QuestionStat>(s => s.CorrectCount == 2 && s.TotalTeams == 3);
        stats[questionIds[2]].Should().Match<QuestionStat>(s => s.CorrectCount == 1 && s.TotalTeams == 3);

        // Reloading source 1 must not corrupt source 2's contribution (full re-aggregation)
        await service.LoadSource(source1.Id);
        stats = await GetStats(packageId);
        stats[questionIds[0]].Should().Match<QuestionStat>(s => s.CorrectCount == 2 && s.TotalTeams == 3);
        stats[questionIds[2]].Should().Match<QuestionStat>(s => s.CorrectCount == 1 && s.TotalTeams == 3);

        // Deleting source 2 re-aggregates down to source 1 alone
        await service.DeleteSource(source2.Id);
        stats = await GetStats(packageId);
        stats[questionIds[0]].Should().Match<QuestionStat>(s => s.CorrectCount == 1 && s.TotalTeams == 2);
        stats[questionIds[1]].Should().Match<QuestionStat>(s => s.CorrectCount == 2 && s.TotalTeams == 2);
        stats[questionIds[2]].Should().Match<QuestionStat>(s => s.CorrectCount == 0 && s.TotalTeams == 2);
    }

    [Fact]
    public async Task LoadSource_OpenQuizWithShootout_MapsShootoutWithOwnDenominator()
    {
        var packageId = await SeedWwwPackage(
            (TourType.Regular, "1", 12), (TourType.Regular, "2", 12), (TourType.Regular, "3", 12),
            (TourType.Regular, "4", 12), (TourType.Regular, "5", 12), (TourType.Shootout, "П", 5));
        var handler = new FakeResultsHandler()
            .Add("/static/25795-TOKEN/results.json", ResultsFixtures.Read("openquiz_results_25795.json"));
        var service = CreateService(handler);
        var source = await service.AttachSource(packageId,
            "https://www.open-quiz.com/results.html?who=res&quiz=25795&token=TOKEN");

        var loaded = await service.LoadSource(source.Id);

        loaded.TeamsCount.Should().Be(44);
        loaded.StatsMapped.Should().BeTrue();

        var regularIds = await GetRegularQuestionIds(packageId);
        var stats = await GetStats(packageId);
        stats[regularIds[0]].Should().Match<QuestionStat>(s => s.CorrectCount == 42 && s.TotalTeams == 44); // Q1

        await using var db = _dbFactory.CreateDbContext();
        var shootoutIds = await db.Questions
            .Where(q => q.Tour.PackageId == packageId && q.Tour.Type == TourType.Shootout)
            .OrderBy(q => q.OrderIndex)
            .Select(q => q.Id)
            .ToListAsync();
        stats[shootoutIds[0]].Should().Match<QuestionStat>(s => s.CorrectCount == 2 && s.TotalTeams == 2); // "101"
        stats[shootoutIds[3]].Should().Match<QuestionStat>(s => s.CorrectCount == 0 && s.TotalTeams == 2); // "104"
    }

    [Fact]
    public async Task LoadSource_OtherPlatform_Throws()
    {
        var packageId = await SeedWwwPackage((TourType.Regular, "1", 3));
        var service = CreateService(new FakeResultsHandler());
        var source = await service.AttachSource(packageId, "https://example.com/results");

        var act = () => service.LoadSource(source.Id);

        await act.Should().ThrowAsync<ResultsLoadException>().WithMessage("*не завантажується*");
    }

    [Fact]
    public async Task LoadSource_Failure_StoresErrorDetail_ClearedOnSuccessfulReload()
    {
        var packageId = await SeedWwwPackage(
            (TourType.Regular, "1", 12), (TourType.Regular, "2", 12), (TourType.Regular, "3", 12));
        // HTTP 200 with an HTML body → ResultsLoadException with a JsonException inside
        var brokenHandler = new FakeResultsHandler()
            .Add("/tournaments/13097", "<html>maintenance</html>");
        var brokenService = CreateService(brokenHandler);
        var source = await brokenService.AttachSource(packageId, "13097");

        var failed = await brokenService.LoadSource(source.Id);

        failed.LoadError.Should().Contain("некоректні дані");
        failed.LoadErrorDetail.Should().Contain("System.Text.Json");

        var okService = CreateService(HandlerForRating13097());
        var reloaded = await okService.LoadSource(source.Id);

        reloaded.LoadError.Should().BeNull();
        reloaded.LoadErrorDetail.Should().BeNull();
    }

    [Fact]
    public async Task LoadSource_CorruptStoredTeamJson_RecomputeSkipsRowInsteadOfCrashing()
    {
        var packageId = await SeedWwwPackage((TourType.Regular, "1", 3));
        var handler = new FakeResultsHandler();
        AddSyntheticRating(handler, 101, ("Перша", "110"), ("Друга", "010"));
        AddSyntheticRating(handler, 102, ("Третя", "101"));
        var service = CreateService(handler);
        var source1 = await service.AttachSource(packageId, "101");
        await service.LoadSource(source1.Id);

        await using (var db = _dbFactory.CreateDbContext())
        {
            var row = await db.TeamResults.FirstAsync(t => t.Name == "Перша");
            row.ResultsByQuestionJson = "corrupt {";
            await db.SaveChangesAsync();
        }

        var source2 = await service.AttachSource(packageId, "102");
        var loaded = await service.LoadSource(source2.Id);

        loaded.LoadError.Should().BeNull();
        var questionIds = await GetRegularQuestionIds(packageId);
        var stats = await GetStats(packageId);
        // «Перша» (110, corrupted) drops out; «Друга» (010) + «Третя» (101) remain
        stats[questionIds[0]].Should().Match<QuestionStat>(s => s.CorrectCount == 1 && s.TotalTeams == 2);
        stats[questionIds[1]].Should().Match<QuestionStat>(s => s.CorrectCount == 1 && s.TotalTeams == 2);
        stats[questionIds[2]].Should().Match<QuestionStat>(s => s.CorrectCount == 1 && s.TotalTeams == 2);
    }

    #endregion

    #region DeleteSource

    [Fact]
    public async Task DeleteSource_RemovesTeamResultsAndStats()
    {
        var packageId = await SeedWwwPackage(
            (TourType.Regular, "1", 12), (TourType.Regular, "2", 12), (TourType.Regular, "3", 12));
        var service = CreateService(HandlerForRating13097());
        var source = await service.AttachSource(packageId, "13097");
        await service.LoadSource(source.Id);

        await service.DeleteSource(source.Id);

        (await service.GetSourcesForPackage(packageId)).Should().BeEmpty();
        (await GetStats(packageId)).Should().BeEmpty();
        await using var db = _dbFactory.CreateDbContext();
        (await db.TeamResults.CountAsync()).Should().Be(0);
    }

    #endregion
}
