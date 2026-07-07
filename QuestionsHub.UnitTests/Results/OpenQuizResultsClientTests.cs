using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Infrastructure.Results;
using Xunit;

namespace QuestionsHub.UnitTests.Results;

public class OpenQuizResultsClientTests
{
    private static OpenQuizResultsClient CreateClient(FakeResultsHandler handler)
    {
        return new OpenQuizResultsClient(
            new FakeHttpClientFactory(handler),
            Options.Create(new ResultsOptions()));
    }

    [Fact]
    public async Task Load_ResultsLink_ParsesTeamsAndQuestions()
    {
        var handler = new FakeResultsHandler()
            .Add("/static/25795-TOKEN25795/results.json", ResultsFixtures.Read("openquiz_results_25795.json"));
        var client = CreateClient(handler);

        var results = await client.Load("25795", "TOKEN25795", OpenQuizLinkKind.Results);

        results.Teams.Should().HaveCount(44);
        results.Questions.Should().HaveCount(65);
        results.Questions.Count(q => q.Kind == PlatformQuestionKind.Regular).Should().Be(60);
        results.Questions.Where(q => q.Kind == PlatformQuestionKind.Shootout)
            .Select(q => q.Name).Should().Equal("101", "102", "103", "104", "105");
        results.TourSizes.Should().Equal(12, 12, 12, 12, 12, 5);
        results.DisplayUrl.Should().Be("https://www.open-quiz.com/results.html?who=res&quiz=25795&token=TOKEN25795");
    }

    [Fact]
    public async Task Load_ResultsLink_ParsesStandingsAndAnswers()
    {
        var handler = new FakeResultsHandler()
            .Add("/static/25795-TOKEN25795/results.json", ResultsFixtures.Read("openquiz_results_25795.json"));
        var client = CreateClient(handler);

        var results = await client.Load("25795", "TOKEN25795", OpenQuizLinkKind.Results);

        var winner = results.Teams[0];
        winner.Name.Should().Be("Мінус один");
        winner.Points.Should().Be(47.0m);
        winner.Position.Should().Be(1m);
        winner.ExternalTeamId.Should().Be(1);

        // Q1 golden value: 42 of 44 teams answered correctly
        results.Teams.Count(t => t.Answers != null && t.Answers[0] == PlatformAnswerState.Correct).Should().Be(42);

        // Shoot-out (index of "101" onward): only 2 teams participated, the rest have NoAnswer
        var firstShootoutIndex = results.Questions.ToList().FindIndex(q => q.Kind == PlatformQuestionKind.Shootout);
        results.Teams.Count(t => t.Answers != null && t.Answers[firstShootoutIndex] != PlatformAnswerState.NoAnswer)
            .Should().Be(2);
        results.Teams.Count(t => t.Answers != null && t.Answers[firstShootoutIndex] == PlatformAnswerState.Correct)
            .Should().Be(2);
    }

    [Fact]
    public async Task Load_ResultsLink_UsesFallbackTitleFromUrl()
    {
        var handler = new FakeResultsHandler()
            .Add("/static/25795-TOKEN25795/results.json", ResultsFixtures.Read("openquiz_results_25795.json"));
        var client = CreateClient(handler);

        var results = await client.Load("25795", "TOKEN25795", OpenQuizLinkKind.Results, "Весняний Minor-турнір 2026");

        results.Title.Should().Be("Весняний Minor-турнір 2026");
    }

    [Fact]
    public async Task Load_AudLink_UsesQuizNameFromApiOverFallback()
    {
        var handler = new FakeResultsHandler()
            .Add("/api/ISecurityApi/login", ResultsFixtures.Read("openquiz_login_ok.json"))
            .Add("/api/IAudApi/getQuiz", ResultsFixtures.Read("openquiz_getquiz_ok.json"))
            .Add("/static/25795-FAKE_RESULTS_TOKEN/results.json", ResultsFixtures.Read("openquiz_results_25795.json"));
        var client = CreateClient(handler);

        var results = await client.Load("25795", "LISTEN_TOKEN", OpenQuizLinkKind.Aud, "ignored fallback");

        results.Title.Should().Be("Весняний Minor-турнір 2026");
    }

    [Fact]
    public async Task Load_AudLink_ExchangesTokenThenLoadsResults()
    {
        var handler = new FakeResultsHandler()
            .Add("/api/ISecurityApi/login", ResultsFixtures.Read("openquiz_login_ok.json"))
            .Add("/api/IAudApi/getQuiz", ResultsFixtures.Read("openquiz_getquiz_ok.json"))
            .Add("/static/25795-FAKE_RESULTS_TOKEN/results.json", ResultsFixtures.Read("openquiz_results_25795.json"));
        var client = CreateClient(handler);

        var results = await client.Load("25795", "LISTEN_TOKEN", OpenQuizLinkKind.Aud);

        results.Teams.Should().HaveCount(44);
        handler.RequestBodies[0].Should().Contain("\"QuizId\":25795").And.Contain("LISTEN_TOKEN");
        handler.RequestBodies[1].Should().Contain("FAKE.JWT.TOKEN");
        results.DisplayUrl.Should().StartWith("https://www.open-quiz.com/results.html?who=res&quiz=25795&token=FAKE_RESULTS_TOKEN")
            .And.Contain("quizName=");
    }

    [Fact]
    public async Task Load_QuizWithWarmup_ClassifiesWarmupQuestion()
    {
        var handler = new FakeResultsHandler()
            .Add("/static/26130-T/results.json", ResultsFixtures.Read("openquiz_results_26130.json"));
        var client = CreateClient(handler);

        var results = await client.Load("26130", "T", OpenQuizLinkKind.Results);

        results.Teams.Should().ContainSingle().Which.Name.Should().Be("Finntrolls");
        results.Teams[0].Points.Should().Be(22.0m);
        results.Questions.Should().HaveCount(37);
        results.Questions[0].Kind.Should().Be(PlatformQuestionKind.Warmup);
        results.Questions.Count(q => q.Kind == PlatformQuestionKind.Regular).Should().Be(36);
        results.TourSizes.Should().Equal(7, 6, 6, 6, 6, 6);
    }

    [Fact]
    public async Task Load_LoginFails_Throws()
    {
        var handler = new FakeResultsHandler()
            .Add("/api/ISecurityApi/login", """{"Status":"Executed","Value":{"Error":"Invalid token"}}""");
        var client = CreateClient(handler);

        var act = () => client.Load("25795", "BAD", OpenQuizLinkKind.Aud);

        await act.Should().ThrowAsync<ResultsLoadException>().WithMessage("*авторизуватися*");
    }

    [Fact]
    public async Task Load_ResultsFileMissing_Throws()
    {
        var client = CreateClient(new FakeResultsHandler());

        var act = () => client.Load("1", "T", OpenQuizLinkKind.Results);

        await act.Should().ThrowAsync<ResultsLoadException>().WithMessage("*HTTP 404*");
    }

    [Fact]
    public async Task Load_QuestionWithoutKey_ThrowsLoadException()
    {
        // Unexpected payload shape must become a user-facing load error, not KeyNotFoundException
        var handler = new FakeResultsHandler()
            .Add("/static/1-T/results.json", """{"Questions":[{"Name":"1"}],"Teams":[{"TeamId":1,"TeamName":"А","Points":1.0}]}""");
        var client = CreateClient(handler);

        var act = () => client.Load("1", "T", OpenQuizLinkKind.Results);

        await act.Should().ThrowAsync<ResultsLoadException>().WithMessage("*некоректні дані*");
    }

    [Fact]
    public async Task Load_NoTeams_Throws()
    {
        var handler = new FakeResultsHandler()
            .Add("/static/1-T/results.json", """{"Questions":[],"Teams":[]}""");
        var client = CreateClient(handler);

        var act = () => client.Load("1", "T", OpenQuizLinkKind.Results);

        await act.Should().ThrowAsync<ResultsLoadException>().WithMessage("*жодної команди*");
    }
}
