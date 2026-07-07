namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// One team's result within a results source: standing (points, position) plus optional
/// per-question outcomes mapped to package questions.
/// </summary>
public class TeamResult
{
    public int Id { get; set; }

    public int ResultsSourceId { get; set; }
    public ResultsSource ResultsSource { get; set; } = null!;

    /// <summary>Team name at tournament time.</summary>
    public required string Name { get; set; }

    /// <summary>Team town, when the platform provides it.</summary>
    public string? Town { get; set; }

    /// <summary>Team id on the source platform.</summary>
    public int? ExternalTeamId { get; set; }

    /// <summary>Total points (correct-answer count for ЩДК; can be fractional on OpenQuiz).</summary>
    public decimal Points { get; set; }

    /// <summary>Source-reported position; fractional because tied teams share an averaged position.</summary>
    public decimal? Position { get; set; }

    /// <summary>
    /// Per-question outcomes as a jsonb dict QuestionId → 0|1. Key present = the team counts in
    /// that question's denominator; 1 = correct. Null when stats were not mapped for this source.
    /// </summary>
    public string? ResultsByQuestionJson { get; set; }
}
