namespace QuestionsHub.Blazor.Infrastructure.Results;

/// <summary>Aggregated outcome of one question across a set of teams.</summary>
public readonly record struct QuestionAggregate(int Correct, int Total);

/// <summary>
/// Aggregates per-question correct/total counts from teams' <c>ResultsByQuestionJson</c> dicts.
/// Shared by the persisted recompute (<see cref="PackageResultsService"/>) and the results page's
/// dynamic per-source-selection view, so both use identical semantics.
/// </summary>
public static class QuestionStatsAggregator
{
    public static Dictionary<int, QuestionAggregate> Aggregate(
        IEnumerable<string?> teamResultsJson,
        ISet<int> validQuestionIds)
    {
        var aggregates = new Dictionary<int, QuestionAggregate>();
        foreach (var json in teamResultsJson)
        {
            var byQuestion = ResultsJson.ParseResultsByQuestion(json);
            if (byQuestion == null)
            {
                continue;
            }

            foreach (var (questionId, outcome) in byQuestion)
            {
                // Questions deleted since the load drop out of the aggregate
                if (!validQuestionIds.Contains(questionId))
                {
                    continue;
                }

                var current = aggregates.GetValueOrDefault(questionId);
                aggregates[questionId] = new QuestionAggregate(
                    current.Correct + (outcome > 0 ? 1 : 0),
                    current.Total + 1);
            }
        }

        return aggregates;
    }
}
