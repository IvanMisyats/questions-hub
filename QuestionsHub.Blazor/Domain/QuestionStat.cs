using System.ComponentModel.DataAnnotations.Schema;

namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Precomputed per-question statistics aggregated over all loaded results sources of the
/// question's package. Rebuilt from TeamResult.ResultsByQuestionJson on every source
/// load/reload/delete — never updated incrementally.
/// </summary>
public class QuestionStat
{
    /// <summary>Question this stat belongs to (primary key — one row per question).</summary>
    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;

    /// <summary>Number of teams that answered the question correctly.</summary>
    public int CorrectCount { get; set; }

    /// <summary>Number of teams that played the question (the denominator).</summary>
    public int TotalTeams { get; set; }

    /// <summary>Share of teams that answered correctly, in percent.</summary>
    [NotMapped]
    public int Percent => CalcPercent(CorrectCount, TotalTeams);

    /// <summary>The one rounding policy for stat percentages (question card, chart, tooltips).</summary>
    public static int CalcPercent(int correct, int total)
    {
        return total > 0 ? (int)Math.Round(100.0 * correct / total) : 0;
    }
}
