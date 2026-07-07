using System.Text.Json;

namespace QuestionsHub.Blazor.Infrastructure.Results;

/// <summary>
/// Single owner of the TeamResult.ResultsByQuestionJson schema (dict QuestionId → 0|1).
/// All readers share one failure policy: a corrupt row degrades to «no per-question data»
/// instead of crashing the reader.
/// </summary>
public static class ResultsJson
{
    public static Dictionary<int, int>? ParseResultsByQuestion(string? resultsByQuestionJson)
    {
        if (string.IsNullOrEmpty(resultsByQuestionJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<int, int>>(resultsByQuestionJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
