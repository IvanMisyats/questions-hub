using FluentAssertions;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Results;
using Xunit;

namespace QuestionsHub.UnitTests.Results;

public class QuestionStatsMapperTests
{
    #region Helpers

    /// <summary>Builds a WWW package; question ids are assigned sequentially across tours (1, 2, …).</summary>
    private static Package BuildPackage(params (TourType Type, string Number, int QuestionCount)[] tours)
    {
        var package = new Package { Title = "Пакет", Type = PackageType.Www };
        var questionId = 1;
        foreach (var (type, number, count) in tours)
        {
            var tour = new Tour { Number = number, Type = type, OrderIndex = package.Tours.Count };
            for (var i = 0; i < count; i++)
            {
                tour.Questions.Add(new Question
                {
                    Id = questionId++,
                    OrderIndex = i,
                    Number = (i + 1).ToString(),
                    Text = "текст",
                    Answer = "відповідь"
                });
            }

            package.Tours.Add(tour);
        }

        return package;
    }

    private static PlatformResults BuildResults(
        List<PlatformQuestion> questions,
        List<PlatformTeam> teams,
        List<int>? tourSizes = null)
    {
        return new PlatformResults
        {
            Questions = questions,
            Teams = teams,
            TourSizes = tourSizes ?? [questions.Count],
            RawPayload = "{}"
        };
    }

    private static List<PlatformQuestion> Regular(int count)
    {
        return Enumerable.Range(1, count)
            .Select(n => new PlatformQuestion(n.ToString(), PlatformQuestionKind.Regular))
            .ToList();
    }

    private static PlatformTeam Team(string name, params PlatformAnswerState[] answers)
    {
        return new PlatformTeam { Name = name, Answers = answers.Length > 0 ? answers : null };
    }

    #endregion

    [Fact]
    public void Map_ExactRegularMatch_MapsPositionally()
    {
        var package = BuildPackage((TourType.Regular, "1", 3));
        var results = BuildResults(Regular(3),
            [Team("А", PlatformAnswerState.Correct, PlatformAnswerState.Wrong, PlatformAnswerState.NoAnswer)]);

        var mapping = QuestionStatsMapper.Map(package, results);

        mapping.StatsMapped.Should().BeTrue();
        mapping.Warnings.Should().BeEmpty();
        mapping.Teams.Should().ContainSingle().Which.ResultsByQuestion.Should().Equal(
            new Dictionary<int, int> { [1] = 1, [2] = 0, [3] = 0 });
    }

    [Fact]
    public void Map_QuestionCountMismatch_DegradesToStandingsOnly()
    {
        var package = BuildPackage((TourType.Regular, "1", 3));
        var results = BuildResults(Regular(4),
            [Team("А", PlatformAnswerState.Correct, PlatformAnswerState.Correct, PlatformAnswerState.Correct, PlatformAnswerState.Correct)]);

        var mapping = QuestionStatsMapper.Map(package, results);

        mapping.StatsMapped.Should().BeFalse();
        mapping.Warnings.Should().ContainSingle(w => w.Contains("не збігається"));
        mapping.Teams.Should().ContainSingle().Which.ResultsByQuestion.Should().BeNull();
    }

    [Fact]
    public void Map_PlatformWarmup_SkippedInMapping()
    {
        var package = BuildPackage((TourType.Regular, "1", 2));
        var questions = new List<PlatformQuestion>
        {
            new("0", PlatformQuestionKind.Warmup),
            new("1", PlatformQuestionKind.Regular),
            new("2", PlatformQuestionKind.Regular)
        };
        var results = BuildResults(questions,
            [Team("А", PlatformAnswerState.Correct, PlatformAnswerState.Wrong, PlatformAnswerState.Correct)],
            tourSizes: [3]);

        var mapping = QuestionStatsMapper.Map(package, results);

        mapping.StatsMapped.Should().BeTrue();
        // Warm-up answer (Correct) is ignored; regular questions map to package ids 1, 2
        mapping.Teams[0].ResultsByQuestion.Should().Equal(new Dictionary<int, int> { [1] = 0, [2] = 1 });
    }

    [Fact]
    public void Map_PackageWarmupTour_NeverGetsStats()
    {
        var package = BuildPackage((TourType.Warmup, "0", 1), (TourType.Regular, "1", 2));
        var results = BuildResults(Regular(2),
            [Team("А", PlatformAnswerState.Correct, PlatformAnswerState.Correct)]);

        var mapping = QuestionStatsMapper.Map(package, results);

        // Warm-up question has id 1; only regular ids 2 and 3 are mapped
        mapping.Teams[0].ResultsByQuestion.Should().Equal(new Dictionary<int, int> { [2] = 1, [3] = 1 });
    }

    [Fact]
    public void Map_ShootoutMatched_UsesOwnDenominator()
    {
        var package = BuildPackage((TourType.Regular, "1", 2), (TourType.Shootout, "П", 2));
        var questions = Regular(2);
        questions.Add(new PlatformQuestion("101", PlatformQuestionKind.Shootout));
        questions.Add(new PlatformQuestion("102", PlatformQuestionKind.Shootout));
        var results = BuildResults(questions,
            [
                Team("Грала перестрілку", PlatformAnswerState.Correct, PlatformAnswerState.Wrong,
                    PlatformAnswerState.Correct, PlatformAnswerState.Wrong),
                Team("Не грала", PlatformAnswerState.Correct, PlatformAnswerState.Wrong,
                    PlatformAnswerState.NoAnswer, PlatformAnswerState.NoAnswer)
            ],
            tourSizes: [2, 2]);

        var mapping = QuestionStatsMapper.Map(package, results);

        mapping.StatsMapped.Should().BeTrue();
        mapping.Teams[0].ResultsByQuestion.Should().Equal(new Dictionary<int, int> { [1] = 1, [2] = 0, [3] = 1, [4] = 0 });
        // NoAnswer on shoot-out questions = did not participate → out of the denominator
        mapping.Teams[1].ResultsByQuestion.Should().Equal(new Dictionary<int, int> { [1] = 1, [2] = 0 });
    }

    [Fact]
    public void Map_ShootoutMismatch_RegularStillMapped()
    {
        var package = BuildPackage((TourType.Regular, "1", 2), (TourType.Shootout, "П", 1));
        var questions = Regular(2);
        questions.Add(new PlatformQuestion("101", PlatformQuestionKind.Shootout));
        questions.Add(new PlatformQuestion("102", PlatformQuestionKind.Shootout));
        var results = BuildResults(questions,
            [
                Team("А", PlatformAnswerState.Correct, PlatformAnswerState.Wrong,
                    PlatformAnswerState.Correct, PlatformAnswerState.Correct)
            ],
            tourSizes: [2, 2]);

        var mapping = QuestionStatsMapper.Map(package, results);

        mapping.StatsMapped.Should().BeTrue();
        mapping.Warnings.Should().ContainSingle(w => w.Contains("Перестрілка"));
        mapping.Teams[0].ResultsByQuestion.Should().Equal(new Dictionary<int, int> { [1] = 1, [2] = 0 });
    }

    [Fact]
    public void Map_HundredQuestionPackage_FoldsMisclassifiedShootoutBack()
    {
        // Questions named "100"+ are classified Shootout by the OpenQuiz heuristic, but this
        // package genuinely has 100 regular questions and no shoot-out tour
        var package = BuildPackage((TourType.Regular, "1", 100));
        var questions = Regular(99);
        questions.Add(new PlatformQuestion("100", PlatformQuestionKind.Shootout));
        var answers = Enumerable.Repeat(PlatformAnswerState.Correct, 100).ToArray();
        var results = BuildResults(questions, [Team("А", answers)], tourSizes: [100]);

        var mapping = QuestionStatsMapper.Map(package, results);

        mapping.StatsMapped.Should().BeTrue();
        mapping.Warnings.Should().BeEmpty();
        mapping.Teams[0].ResultsByQuestion.Should().HaveCount(100);
        mapping.Teams[0].ResultsByQuestion![100].Should().Be(1); // the folded-back question
    }

    [Fact]
    public void Map_ShootoutOnPlatformButNoShootoutTour_RegularStillMapsWhenCountsMatch()
    {
        // Package without a shoot-out tour, platform played one: no fold (regular already
        // matches), regular maps, shoot-out group degrades with a warning
        var package = BuildPackage((TourType.Regular, "1", 2));
        var questions = Regular(2);
        questions.Add(new PlatformQuestion("101", PlatformQuestionKind.Shootout));
        var results = BuildResults(questions,
            [Team("А", PlatformAnswerState.Correct, PlatformAnswerState.Wrong, PlatformAnswerState.Correct)],
            tourSizes: [2, 1]);

        var mapping = QuestionStatsMapper.Map(package, results);

        mapping.StatsMapped.Should().BeTrue();
        mapping.Warnings.Should().ContainSingle(w => w.Contains("Перестрілка"));
        mapping.Teams[0].ResultsByQuestion.Should().Equal(new Dictionary<int, int> { [1] = 1, [2] = 0 });
    }

    [Fact]
    public void Map_ExcludedAnswers_OmittedFromDenominator()
    {
        var package = BuildPackage((TourType.Regular, "1", 2));
        var results = BuildResults(Regular(2),
            [Team("А", PlatformAnswerState.Correct, PlatformAnswerState.Excluded)]);

        var mapping = QuestionStatsMapper.Map(package, results);

        mapping.Teams[0].ResultsByQuestion.Should().Equal(new Dictionary<int, int> { [1] = 1 });
    }

    [Fact]
    public void Map_TeamWithoutAnswers_IsStandingsOnly()
    {
        var package = BuildPackage((TourType.Regular, "1", 2));
        var results = BuildResults(Regular(2), [Team("Без маски")]);

        var mapping = QuestionStatsMapper.Map(package, results);

        mapping.StatsMapped.Should().BeFalse();
        mapping.Teams[0].ResultsByQuestion.Should().BeNull();
    }

    [Fact]
    public void Map_TourBreakdownDiffers_AddsWarning()
    {
        var package = BuildPackage((TourType.Regular, "1", 2), (TourType.Regular, "2", 2));
        var results = BuildResults(Regular(4),
            [Team("А", PlatformAnswerState.Correct, PlatformAnswerState.Correct, PlatformAnswerState.Correct, PlatformAnswerState.Correct)],
            tourSizes: [1, 3]);

        var mapping = QuestionStatsMapper.Map(package, results);

        mapping.StatsMapped.Should().BeTrue();
        mapping.Warnings.Should().ContainSingle(w => w.Contains("Розбивка"));
    }

    [Fact]
    public void Map_WarmupInsidePlatformTour_NoFalseTourWarning()
    {
        // OpenQuiz puts the warm-up inside the first tour: sizes [3, 2] with one warm-up question
        var package = BuildPackage((TourType.Regular, "1", 2), (TourType.Regular, "2", 2));
        var questions = new List<PlatformQuestion>
        {
            new("0", PlatformQuestionKind.Warmup),
            new("1", PlatformQuestionKind.Regular),
            new("2", PlatformQuestionKind.Regular),
            new("3", PlatformQuestionKind.Regular),
            new("4", PlatformQuestionKind.Regular)
        };
        var results = BuildResults(questions,
            [Team("А", PlatformAnswerState.Wrong, PlatformAnswerState.Correct, PlatformAnswerState.Correct, PlatformAnswerState.Correct, PlatformAnswerState.Correct)],
            tourSizes: [3, 2]);

        var mapping = QuestionStatsMapper.Map(package, results);

        mapping.Warnings.Should().BeEmpty();
        mapping.Teams[0].ResultsByQuestion.Should().Equal(new Dictionary<int, int> { [1] = 1, [2] = 1, [3] = 1, [4] = 1 });
    }
}
