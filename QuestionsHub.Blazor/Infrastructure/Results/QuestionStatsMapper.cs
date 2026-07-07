using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Utils;

namespace QuestionsHub.Blazor.Infrastructure.Results;

/// <summary>Per-team outcome of mapping: dict QuestionId → 0|1, or null for standings-only teams.</summary>
public sealed record TeamStatsMapping(PlatformTeam Team, Dictionary<int, int>? ResultsByQuestion);

/// <summary>Result of mapping platform results onto package questions.</summary>
public sealed class QuestionStatsMappingResult
{
    /// <summary>True when per-question stats were mapped for at least one question group.</summary>
    public required bool StatsMapped { get; init; }

    public required List<TeamStatsMapping> Teams { get; init; }

    /// <summary>User-facing warnings (uk-UA).</summary>
    public required List<string> Warnings { get; init; }
}

/// <summary>
/// Maps platform questions onto package questions positionally, in two independent groups:
/// regular questions and shoot-out questions. Warm-up questions never get stats. A count
/// mismatch degrades that group to standings-only (stats are never misattached), with a warning.
/// </summary>
public static class QuestionStatsMapper
{
    public static QuestionStatsMappingResult Map(Package package, PlatformResults results)
    {
        var warnings = new List<string>();

        var packageRegular = EligibleQuestions(package, TourType.Regular);
        var packageShootout = EligibleQuestions(package, TourType.Shootout);

        var platformRegular = IndexesOfKind(results, PlatformQuestionKind.Regular);
        var platformShootout = IndexesOfKind(results, PlatformQuestionKind.Shootout);

        // The shoot-out classification (names 100+) is a naming heuristic. When the package has
        // no shoot-out tour and the regular counts only match with the "shoot-out" questions
        // folded back in, they were really regular ones — a genuine 100+-question package.
        if (packageShootout.Count == 0 && platformShootout.Count > 0
            && platformRegular.Count != packageRegular.Count
            && platformRegular.Count + platformShootout.Count == packageRegular.Count)
        {
            platformRegular = platformRegular.Concat(platformShootout).Order().ToList();
            platformShootout = [];
        }

        // platform question index -> package question id, built per group
        var indexToQuestionId = new Dictionary<int, int>();
        var shootoutIndexes = new HashSet<int>(platformShootout);

        var regularMapped = TryMapGroup(platformRegular, packageRegular, indexToQuestionId);
        if (!regularMapped)
        {
            warnings.Add(
                $"Кількість запитань на платформі ({platformRegular.Count}) не збігається з кількістю запитань у пакеті ({packageRegular.Count}) — статистику запитань не завантажено.");
        }
        else
        {
            AddTourBreakdownWarning(package, results, [.. platformRegular], warnings);
        }

        var shootoutMapped = false;
        if (platformShootout.Count > 0)
        {
            shootoutMapped = TryMapGroup(platformShootout, packageShootout, indexToQuestionId);
            if (!shootoutMapped)
            {
                warnings.Add(
                    $"Перестрілка: кількість запитань не збігається ({platformShootout.Count} на платформі, {packageShootout.Count} у пакеті) — статистику перестрілки не завантажено.");
            }
        }

        var teams = new List<TeamStatsMapping>(results.Teams.Count);
        foreach (var team in results.Teams)
        {
            teams.Add(new TeamStatsMapping(team, MapTeam(team, indexToQuestionId, shootoutIndexes)));
        }

        var statsMapped = (regularMapped || shootoutMapped) && teams.Any(t => t.ResultsByQuestion is { Count: > 0 });

        return new QuestionStatsMappingResult
        {
            StatsMapped = statsMapped,
            Teams = teams,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Package questions of the given tour type, in display order: tours via the shared
    /// ЩДК sort (must match the package/results pages), questions by OrderIndex.
    /// </summary>
    private static List<Question> EligibleQuestions(Package package, TourType tourType)
    {
        return TourOrdering.OrderWww(package.Tours.Where(t => t.Type == tourType))
            .SelectMany(t => t.Questions.OrderBy(q => q.OrderIndex))
            .ToList();
    }

    private static List<int> IndexesOfKind(PlatformResults results, PlatformQuestionKind kind)
    {
        var indexes = new List<int>();
        for (var i = 0; i < results.Questions.Count; i++)
        {
            if (results.Questions[i].Kind == kind)
            {
                indexes.Add(i);
            }
        }

        return indexes;
    }

    private static bool TryMapGroup(List<int> platformIndexes, List<Question> packageQuestions, Dictionary<int, int> indexToQuestionId)
    {
        if (platformIndexes.Count == 0 || platformIndexes.Count != packageQuestions.Count)
        {
            return false;
        }

        for (var i = 0; i < platformIndexes.Count; i++)
        {
            indexToQuestionId[platformIndexes[i]] = packageQuestions[i].Id;
        }

        return true;
    }

    private static Dictionary<int, int>? MapTeam(PlatformTeam team, Dictionary<int, int> indexToQuestionId, HashSet<int> shootoutIndexes)
    {
        if (team.Answers == null || indexToQuestionId.Count == 0)
        {
            return null;
        }

        var mapped = new Dictionary<int, int>();
        foreach (var (platformIndex, questionId) in indexToQuestionId)
        {
            if (platformIndex >= team.Answers.Count)
            {
                continue;
            }

            var state = team.Answers[platformIndex];
            switch (state)
            {
                case PlatformAnswerState.Correct:
                    mapped[questionId] = 1;
                    break;
                case PlatformAnswerState.Wrong:
                    mapped[questionId] = 0;
                    break;
                case PlatformAnswerState.NoAnswer:
                    // A missing answer counts as a miss for regular questions, but means
                    // "did not participate" for the shoot-out — those teams stay out of its denominator.
                    if (!shootoutIndexes.Contains(platformIndex))
                    {
                        mapped[questionId] = 0;
                    }

                    break;
                case PlatformAnswerState.Excluded:
                default:
                    break; // removed/pending questions stay out of the denominator
            }
        }

        return mapped;
    }

    /// <summary>
    /// Positional mapping trusts play order; when the per-tour breakdown differs between the
    /// platform and the package (same total), flag it so the manager double-checks the numbering.
    /// </summary>
    private static void AddTourBreakdownWarning(Package package, PlatformResults results, HashSet<int> regularIndexes, List<string> warnings)
    {
        var packageSizes = TourOrdering.OrderWww(package.Tours.Where(t => t.Type == TourType.Regular))
            .Select(t => t.Questions.Count)
            .ToList();

        // Platform tour sizes counted over effectively-regular questions only
        // (warm-up drops out; folded-back shoot-out questions count as regular)
        var platformSizes = new List<int>();
        var questionIndex = 0;
        foreach (var tourSize in results.TourSizes)
        {
            var regularInTour = 0;
            for (var i = 0; i < tourSize && questionIndex < results.Questions.Count; i++, questionIndex++)
            {
                if (regularIndexes.Contains(questionIndex))
                {
                    regularInTour++;
                }
            }

            if (regularInTour > 0)
            {
                platformSizes.Add(regularInTour);
            }
        }

        if (platformSizes.Count > 0 && !platformSizes.SequenceEqual(packageSizes))
        {
            warnings.Add(
                $"Розбивка запитань за турами на платформі ({string.Join("+", platformSizes)}) відрізняється від пакета ({string.Join("+", packageSizes)}) — перевірте відповідність нумерації.");
        }
    }
}
