using FluentAssertions;
using QuestionsHub.Blazor.Infrastructure.Import;
using QuestionsHub.UnitTests.TestInfrastructure;

namespace QuestionsHub.UnitTests.Assertions;

/// <summary>
/// Fluent assertion extensions for ParseResult validation.
/// </summary>
public static class ParseResultAssertions
{
    /// <summary>
    /// Asserts that the ParseResult matches the expected structure from a JSON file.
    /// </summary>
    public static void ShouldMatchExpected(this ParseResult actual, string expectedJsonPath)
    {
        var expected = JsonComparer.LoadExpected(expectedJsonPath);

        actual.Title.Should().Be(expected.Title, "package title should match");
        actual.TotalQuestions.Should().Be(expected.TotalQuestions, "total question count should match");
        actual.Tours.Should().HaveCount(expected.Tours.Count, "tour count should match");

        for (var i = 0; i < expected.Tours.Count; i++)
        {
            var actualTour = actual.Tours[i];
            var expectedTour = expected.Tours[i];

            actualTour.Number.Should().Be(expectedTour.Number, $"tour {i} number should match");
            actualTour.Questions.Should().HaveCount(expectedTour.Questions.Count,
                $"tour {expectedTour.Number} question count should match");

            for (var j = 0; j < expectedTour.Questions.Count; j++)
            {
                AssertQuestion(actualTour.Questions[j], expectedTour.Questions[j],
                    $"tour {expectedTour.Number}, question {j}");
            }
        }
    }

    /// <summary>
    /// Asserts that the ParseResult has a valid structure (has tours, questions with text and answers).
    /// </summary>
    public static void ShouldBeValidPackage(this ParseResult actual)
    {
        actual.Tours.Should().NotBeEmpty("package should have at least one tour");
        actual.TotalQuestions.Should().BeGreaterThan(0, "package should have at least one question");
        actual.Confidence.Should().BeGreaterThan(0.5, "confidence should be reasonable");

        foreach (var tour in actual.Tours)
        {
            tour.Questions.Should().NotBeEmpty($"tour {tour.Number} should have questions");

            foreach (var question in tour.Questions)
            {
                question.HasText.Should().BeTrue($"question {question.Number} should have text");
                question.HasAnswer.Should().BeTrue($"question {question.Number} should have an answer");
            }
        }
    }

    /// <summary>
    /// Asserts that a specific question has the expected content.
    /// </summary>
    public static void ShouldHaveQuestion(this ParseResult actual, string tourNumber, string questionNumber,
        Action<QuestionDto> assertions)
    {
        var tour = actual.Tours.FirstOrDefault(t => t.Number == tourNumber);
        tour.Should().NotBeNull($"tour {tourNumber} should exist");

        var question = tour!.Questions.FirstOrDefault(q => q.Number == questionNumber);
        question.Should().NotBeNull($"question {questionNumber} in tour {tourNumber} should exist");

        assertions(question!);
    }

    private static void AssertQuestion(QuestionDto actual, ExpectedQuestion expected, string context)
    {
        actual.Number.Should().Be(expected.Number, $"{context}: number");

        if (expected.Text != null)
        {
            NormalizeText(actual.Text).Should().Contain(NormalizeText(expected.Text), $"{context}: text");
        }

        if (expected.Answer != null)
        {
            NormalizeText(actual.Answer).Should().Be(NormalizeText(expected.Answer), $"{context}: answer");
        }

        if (expected.AcceptedAnswers != null)
        {
            NormalizeText(actual.AcceptedAnswers).Should().Be(NormalizeText(expected.AcceptedAnswers),
                $"{context}: accepted answers");
        }

        if (expected.RejectedAnswers != null)
        {
            NormalizeText(actual.RejectedAnswers).Should().Be(NormalizeText(expected.RejectedAnswers),
                $"{context}: rejected answers");
        }

        if (expected.Comment != null)
        {
            NormalizeText(actual.Comment).Should().Contain(NormalizeText(expected.Comment),
                $"{context}: comment");
        }

        if (expected.Source != null)
        {
            NormalizeText(actual.Source).Should().Contain(NormalizeText(expected.Source),
                $"{context}: source");
        }

        if (expected.Authors.Count > 0)
        {
            actual.Authors.Should().BeEquivalentTo(expected.Authors, $"{context}: authors");
        }

        if (expected.HostInstructions != null)
        {
            NormalizeText(actual.HostInstructions).Should().Contain(NormalizeText(expected.HostInstructions),
                $"{context}: host instructions");
        }

        if (expected.HandoutText != null)
        {
            NormalizeText(actual.HandoutText).Should().Contain(NormalizeText(expected.HandoutText),
                $"{context}: handout text");
        }

        actual.HandoutAssetFileName.Should().Match(
            _ => expected.HasHandoutAsset == (actual.HandoutAssetFileName != null),
            $"{context}: has handout asset");

        actual.CommentAssetFileName.Should().Match(
            _ => expected.HasCommentAsset == (actual.CommentAssetFileName != null),
            $"{context}: has comment asset");
    }

    private static string? NormalizeText(string? text)
    {
        return text?.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
    }
}

