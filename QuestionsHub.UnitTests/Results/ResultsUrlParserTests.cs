using FluentAssertions;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Results;
using Xunit;

namespace QuestionsHub.UnitTests.Results;

public class ResultsUrlParserTests
{
    #region Rating

    [Theory]
    [InlineData("https://rating.chgk.info/tournament/13097")]
    [InlineData("https://rating.chgk.info/tournament/13097/statistics")]
    [InlineData("http://rating.chgk.info/tournament/13097/")]
    [InlineData("https://api.rating.chgk.info/tournaments/13097")]
    public void Parse_RatingUrls_ExtractsTournamentId(string url)
    {
        var parsed = ResultsUrlParser.Parse(ResultsPlatform.Rating, url);

        parsed.Platform.Should().Be(ResultsPlatform.Rating);
        parsed.ExternalId.Should().Be("13097");
        parsed.LinkKind.Should().Be(OpenQuizLinkKind.None);
        parsed.Url.Should().Be(url);
    }

    [Fact]
    public void Parse_RatingBareId_BuildsCanonicalUrl()
    {
        var parsed = ResultsUrlParser.Parse(ResultsPlatform.Rating, " 13097 ");

        parsed.ExternalId.Should().Be("13097");
        parsed.Url.Should().Be("https://rating.chgk.info/tournament/13097");
    }

    [Theory]
    [InlineData("https://rating.chgk.info/teams/5")]
    [InlineData("https://example.com/tournament/13097")]
    [InlineData("tournament 13097")]
    public void Parse_InvalidRatingInput_Throws(string input)
    {
        var act = () => ResultsUrlParser.Parse(ResultsPlatform.Rating, input);

        act.Should().Throw<ResultsLoadException>().WithMessage("*rating.chgk.info*");
    }

    #endregion

    #region OpenQuiz

    [Fact]
    public void Parse_OpenQuizResultsLink_ReturnsResultsKind()
    {
        const string url = "https://www.open-quiz.com/results.html?who=res&quiz=25795&quizName=%D0%92%D0%B5%D1%81%D0%BD%D1%8F%D0%BD%D0%B8%D0%B9&quizImg=&token=p8JaXXyzdkRusWWzbjRW9yIXs4jjodMI1st0Ndvjq1k&url=https://www.open-quiz.com";

        var parsed = ResultsUrlParser.Parse(ResultsPlatform.OpenQuiz, url);

        parsed.Platform.Should().Be(ResultsPlatform.OpenQuiz);
        parsed.ExternalId.Should().Be("25795");
        parsed.Token.Should().Be("p8JaXXyzdkRusWWzbjRW9yIXs4jjodMI1st0Ndvjq1k");
        parsed.LinkKind.Should().Be(OpenQuizLinkKind.Results);
    }

    [Fact]
    public void Parse_OpenQuizResultsLink_ExtractsQuizNameAsTitle()
    {
        const string url = "https://www.open-quiz.com/results.html?who=res&quiz=25795&quizName=%D0%92%D0%B5%D1%81%D0%BD%D1%8F%D0%BD%D0%B8%D0%B9%20Minor-%D1%82%D1%83%D1%80%D0%BD%D1%96%D1%80%202026&token=p8JaXXyzdkRusWWzbjRW9yIXs4jjodMI1st0Ndvjq1k";

        var parsed = ResultsUrlParser.Parse(ResultsPlatform.OpenQuiz, url);

        parsed.Title.Should().Be("Весняний Minor-турнір 2026");
    }

    [Fact]
    public void Parse_OpenQuizResultsLinkWithoutName_TitleNull()
    {
        const string url = "https://www.open-quiz.com/results.html?who=res&quiz=25795&token=abc";

        var parsed = ResultsUrlParser.Parse(ResultsPlatform.OpenQuiz, url);

        parsed.Title.Should().BeNull();
    }

    [Fact]
    public void Parse_OpenQuizAudLink_ReturnsAudKind()
    {
        const string url = "https://www.open-quiz.com/app/index.html?who=aud&quiz=25795&token=d4-HwroGR7MjFIWyEqBeOTzlOAv7Bw8Z0uGTuESF8Bo";

        var parsed = ResultsUrlParser.Parse(ResultsPlatform.OpenQuiz, url);

        parsed.ExternalId.Should().Be("25795");
        parsed.Token.Should().Be("d4-HwroGR7MjFIWyEqBeOTzlOAv7Bw8Z0uGTuESF8Bo");
        parsed.LinkKind.Should().Be(OpenQuizLinkKind.Aud);
    }

    [Theory]
    [InlineData("https://www.open-quiz.com/results.html?quiz=25795")] // no token
    [InlineData("https://www.open-quiz.com/results.html?token=abc")] // no quiz
    [InlineData("https://example.com/results.html?quiz=25795&token=abc")] // wrong host
    [InlineData("https://www.open-quiz.com/results.html?quiz=25795&token=x%2F..%2F..%2Fapi%2Ffoo")] // path traversal in token
    [InlineData("https://www.open-quiz.com/results.html?quiz=25795&token=a+b")] // non-base64url token
    [InlineData("https://www.open-quiz.com/results.html?quiz=9999999999&token=abc")] // quiz id overflows int
    public void Parse_InvalidOpenQuizInput_Throws(string input)
    {
        var act = () => ResultsUrlParser.Parse(ResultsPlatform.OpenQuiz, input);

        act.Should().Throw<ResultsLoadException>().WithMessage("*open-quiz.com*");
    }

    #endregion

    #region Detect

    [Theory]
    [InlineData("https://rating.chgk.info/tournament/13098/statistics", ResultsPlatform.Rating, "13098")]
    [InlineData("https://rating.chgk.info/tournament/13098", ResultsPlatform.Rating, "13098")]
    [InlineData("https://rating.chgk.info/tournament/13098/tours", ResultsPlatform.Rating, "13098")]
    [InlineData("13098", ResultsPlatform.Rating, "13098")]
    [InlineData("https://www.open-quiz.com/results.html?who=res&quiz=25795&token=abcDEF-_1", ResultsPlatform.OpenQuiz, "25795")]
    [InlineData("https://docs.google.com/spreadsheets/d/abc", ResultsPlatform.Other, null)]
    public void Detect_RecognizesPlatformFromUrl(string url, ResultsPlatform expectedPlatform, string? expectedId)
    {
        var parsed = ResultsUrlParser.Detect(url);

        parsed.Platform.Should().Be(expectedPlatform);
        parsed.ExternalId.Should().Be(expectedId);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("https://rating.chgk.info/teams/5")] // rating host but not a tournament link
    [InlineData("https://www.open-quiz.com/results.html?quiz=25795")] // open-quiz host but no token
    public void Detect_MalformedPlatformLinks_ThrowInsteadOfFallingBackToOther(string url)
    {
        var act = () => ResultsUrlParser.Detect(url);

        act.Should().Throw<ResultsLoadException>();
    }

    #endregion

    #region Other

    [Fact]
    public void Parse_OtherHttpsUrl_Accepted()
    {
        var parsed = ResultsUrlParser.Parse(ResultsPlatform.Other, "https://docs.google.com/spreadsheets/d/abc/edit");

        parsed.Platform.Should().Be(ResultsPlatform.Other);
        parsed.ExternalId.Should().BeNull();
        parsed.Token.Should().BeNull();
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/file")]
    [InlineData("")]
    public void Parse_InvalidOtherInput_Throws(string input)
    {
        var act = () => ResultsUrlParser.Parse(ResultsPlatform.Other, input);

        act.Should().Throw<ResultsLoadException>();
    }

    #endregion
}
