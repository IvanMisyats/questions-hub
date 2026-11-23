using System.Globalization;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Infrastructure.Results;
using Xunit;

namespace QuestionsHub.UnitTests.Results;

public class RatingResultsClientTests
{
    private static RatingResultsClient CreateClient(FakeResultsHandler handler)
    {
        return new RatingResultsClient(
            new FakeHttpClientFactory(handler),
            Options.Create(new ResultsOptions()));
    }

    private static FakeResultsHandler HandlerFor13097()
    {
        return new FakeResultsHandler()
            .Add("/tournaments/13097/results", ResultsFixtures.Read("rating_results_13097.json"))
            .Add("/tournaments/13097", ResultsFixtures.Read("rating_tournament_13097.json"));
    }

    [Fact]
    public async Task Load_Tournament13097_ParsesTeamsAndQuestions()
    {
        var client = CreateClient(HandlerFor13097());

        var results = await client.Load("13097");

        results.Teams.Should().HaveCount(17);
        results.Questions.Should().HaveCount(36);
        results.Questions.Should().OnlyContain(q => q.Kind == PlatformQuestionKind.Regular);
        results.TourSizes.Should().Equal(12, 12, 12);
        results.Warnings.Should().BeEmpty();
        results.DisplayUrl.Should().Be("https://rating.chgk.info/tournament/13097");
        results.RawPayload.Should().Contain("\"tournament\":").And.Contain("\"results\":");
    }

    [Fact]
    public async Task Load_Tournament13097_ParsesLongNameAsTitle()
    {
        var client = CreateClient(HandlerFor13097());

        var results = await client.Load("13097");

        results.Title.Should().Be("Синхронный турнир «Толстолобик-2» / Синхронний турнір «Товстолобик-2»");
    }

    [Fact]
    public async Task Load_NoLongName_FallsBackToName()
    {
        const string info = """{"id":1,"name":"Короткий турнір","questionQty":{"1":3},"hideResultsTo":"2000-01-01T00:00:00+00:00"}""";
        const string results = """[{"team":{"id":1,"name":"А","town":null},"mask":"101","questionsTotal":2,"position":1.0}]""";
        var handler = new FakeResultsHandler()
            .Add("/tournaments/1/results", results)
            .Add("/tournaments/1", info);

        var loaded = await CreateClient(handler).Load("1");

        loaded.Title.Should().Be("Короткий турнір");
    }

    [Fact]
    public async Task Load_Tournament13097_ParsesTeamStanding()
    {
        var client = CreateClient(HandlerFor13097());

        var results = await client.Load("13097");

        var team = results.Teams[0];
        team.Name.Should().Be("X-promt");
        team.Town.Should().Be("Рига");
        team.ExternalTeamId.Should().Be(4032);
        team.Points.Should().Be(14);
        team.Position.Should().Be(2.5m);
        team.Answers.Should().NotBeNull().And.HaveCount(36);
    }

    [Fact]
    public async Task Load_Tournament13097_PerQuestionCorrectCountsMatchGoldenValues()
    {
        var client = CreateClient(HandlerFor13097());

        var results = await client.Load("13097");

        CorrectCount(results, questionIndex: 0).Should().Be(3);   // Q1
        CorrectCount(results, questionIndex: 10).Should().Be(14); // Q11 — easiest
        CorrectCount(results, questionIndex: 5).Should().Be(0);   // Q6 — answered by no one
    }

    [Fact]
    public async Task Load_EmbargoedTournament_ThrowsWithEmbargoDate()
    {
        var handler = new FakeResultsHandler()
            .Add("/tournaments/99999/results", ResultsFixtures.Read("rating_results_embargoed.json"))
            .Add("/tournaments/99999", ResultsFixtures.Read("rating_tournament_embargoed.json"));
        var client = CreateClient(handler);

        var act = () => client.Load("99999");

        var exception = (await act.Should().ThrowAsync<ResultsLoadException>()
                .WithMessage("*приховано*"))
            .Which;
        exception.EmbargoedUntil.Should().Be(new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Load_EmptyResults_Throws()
    {
        var handler = new FakeResultsHandler()
            .Add("/tournaments/13919/results", "[]")
            .Add("/tournaments/13919", ResultsFixtures.Read("rating_tournament_13097.json"));
        var client = CreateClient(handler);

        var act = () => client.Load("13919");

        await act.Should().ThrowAsync<ResultsLoadException>().WithMessage("*немає результатів*");
    }

    [Fact]
    public async Task Load_Http200WithNonJsonBody_ThrowsLoadException()
    {
        // A maintenance/WAF page served with 200 must surface as a stored load error,
        // not as a JsonException crashing the caller
        var handler = new FakeResultsHandler()
            .Add("/tournaments/5/results", "<html>maintenance</html>")
            .Add("/tournaments/5", "<html>maintenance</html>");
        var client = CreateClient(handler);

        var act = () => client.Load("5");

        await act.Should().ThrowAsync<ResultsLoadException>().WithMessage("*некоректні дані*");
    }

    [Fact]
    public async Task Load_UnknownTournament_ThrowsNotFound()
    {
        var client = CreateClient(new FakeResultsHandler());

        var act = () => client.Load("1");

        await act.Should().ThrowAsync<ResultsLoadException>().WithMessage("*не знайдено*");
    }

    [Fact]
    public async Task Load_MaskLengthMismatch_SkipsTeamStatsWithWarning()
    {
        const string info = """{"id":1,"questionQty":{"1":3},"hideResultsTo":"2000-01-01T00:00:00+00:00"}""";
        const string results = """
            [
              {"team":{"id":1,"name":"Довга","town":null},"mask":"10","questionsTotal":1,"position":1.0},
              {"team":{"id":2,"name":"Точна","town":null},"mask":"101","questionsTotal":2,"position":2.0}
            ]
            """;
        var handler = new FakeResultsHandler()
            .Add("/tournaments/1/results", results)
            .Add("/tournaments/1", info);
        var client = CreateClient(handler);

        var loaded = await client.Load("1");

        loaded.Teams[0].Answers.Should().BeNull();
        loaded.Teams[1].Answers.Should().Equal(
            PlatformAnswerState.Correct, PlatformAnswerState.Wrong, PlatformAnswerState.Correct);
        loaded.Warnings.Should().ContainSingle(w => w.Contains("Довга"));
    }

    [Fact]
    public async Task Load_PendingControversialsAndRemovedQuestions_ExcludedWithWarning()
    {
        const string info = """{"id":2,"questionQty":{"1":4},"hideResultsTo":"2000-01-01T00:00:00+00:00"}""";
        const string results = """
            [{"team":{"id":1,"name":"Команда","town":null},"mask":"1?X0","questionsTotal":1,"position":1.0}]
            """;
        var handler = new FakeResultsHandler()
            .Add("/tournaments/2/results", results)
            .Add("/tournaments/2", info);
        var client = CreateClient(handler);

        var loaded = await client.Load("2");

        loaded.Teams[0].Answers.Should().Equal(
            PlatformAnswerState.Correct, PlatformAnswerState.Excluded,
            PlatformAnswerState.Excluded, PlatformAnswerState.Wrong);
        loaded.Warnings.Should().ContainSingle(w => w.Contains("спірні"));
    }

    [Fact]
    public async Task Load_Tournament13097_ParsesDates()
    {
        var results = await CreateClient(HandlerFor13097()).Load("13097");

        results.Dates.Should().Be(new TournamentDates(new DateOnly(2026, 5, 21), new DateOnly(2026, 5, 28)));
    }

    [Theory]
    // Organizer's Kyiv midnight is the previous UTC evening (EET +2 in winter, EEST +3 in summer)
    [InlineData("2015-01-09T22:00:00+00:00", "2015-01-11T21:55:00+00:00", "2015-01-10", "2015-01-11")]
    [InlineData("2015-04-02T21:00:00+00:00", "2015-04-05T20:55:00+00:00", "2015-04-03", "2015-04-05")]
    // Platform-local offsets are converted, not truncated (Moscow midnight is 23:00 in winter Kyiv)
    [InlineData("2024-03-10T00:00:00+03:00", "2024-03-10T23:00:00+03:00", "2024-03-09", "2024-03-10")]
    // One-day tournament: no end date
    [InlineData("2024-06-01T09:00:00+00:00", "2024-06-01T18:00:00+00:00", "2024-06-01", null)]
    // End before start (bad data): start only
    [InlineData("2024-06-05T09:00:00+00:00", "2024-06-01T18:00:00+00:00", "2024-06-05", null)]
    public async Task Load_DatesConvertedToKyivDates(string dateStart, string dateEnd, string expectedFrom, string? expectedTo)
    {
        var info = $$"""{"id":1,"questionQty":{"1":3},"dateStart":"{{dateStart}}","dateEnd":"{{dateEnd}}"}""";
        var handler = new FakeResultsHandler()
            .Add("/tournaments/1/results", """[{"team":{"id":1,"name":"А","town":null},"mask":"101","questionsTotal":2,"position":1.0}]""")
            .Add("/tournaments/1", info);

        var loaded = await CreateClient(handler).Load("1");

        loaded.Dates.Should().Be(new TournamentDates(
            DateOnly.Parse(expectedFrom, CultureInfo.InvariantCulture), expectedTo == null ? null : DateOnly.Parse(expectedTo, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task Load_NoDateStart_NoDates()
    {
        var handler = new FakeResultsHandler()
            .Add("/tournaments/1/results", """[{"team":{"id":1,"name":"А","town":null},"mask":"101","questionsTotal":2,"position":1.0}]""")
            .Add("/tournaments/1", """{"id":1,"questionQty":{"1":3},"dateStart":null,"dateEnd":"2024-06-01T18:00:00+00:00"}""");

        var loaded = await CreateClient(handler).Load("1");

        loaded.Dates.Should().BeNull();
    }

    [Fact]
    public async Task Load_EmbargoedTournament_ExceptionCarriesDates()
    {
        var handler = new FakeResultsHandler()
            .Add("/tournaments/1/results", """[{"team":{"id":1,"name":"А","town":null},"mask":null,"questionsTotal":null,"position":null}]""")
            .Add("/tournaments/1", """{"id":1,"questionQty":{"1":3},"hideResultsTo":"2099-01-01T00:00:00+00:00","dateStart":"2024-06-01T09:00:00+00:00","dateEnd":"2024-06-03T18:00:00+00:00"}""");

        var act = () => CreateClient(handler).Load("1");

        var exception = (await act.Should().ThrowAsync<ResultsLoadException>()).Which;
        exception.EmbargoedUntil.Should().NotBeNull();
        exception.Dates.Should().Be(new TournamentDates(new DateOnly(2024, 6, 1), new DateOnly(2024, 6, 3)));
    }

    private static int CorrectCount(PlatformResults results, int questionIndex)
    {
        return results.Teams.Count(t => t.Answers != null && t.Answers[questionIndex] == PlatformAnswerState.Correct);
    }
}
