namespace QuestionsHub.Blazor.Infrastructure.Results;

/// <summary>Classification of a platform question for stats mapping.</summary>
public enum PlatformQuestionKind
{
    Regular = 0,
    Warmup = 1,
    Shootout = 2
}

/// <summary>Outcome of one platform question for one team.</summary>
public enum PlatformAnswerState
{
    /// <summary>Played the question, answer not accepted.</summary>
    Wrong = 0,

    /// <summary>Answer accepted.</summary>
    Correct = 1,

    /// <summary>No answer submitted (OpenQuiz: question key absent from Details).</summary>
    NoAnswer = 2,

    /// <summary>Excluded from statistics (Rating: removed question 'X' or pending controversial '?').</summary>
    Excluded = 3
}

/// <summary>A question as the platform reports it, in play order.</summary>
public sealed record PlatformQuestion(string Name, PlatformQuestionKind Kind);

/// <summary>One team's result as the platform reports it.</summary>
public sealed class PlatformTeam
{
    /// <summary>Team name at tournament time.</summary>
    public required string Name { get; init; }

    public string? Town { get; init; }

    public int? ExternalTeamId { get; init; }

    /// <summary>Total points; fractional values possible on OpenQuiz.</summary>
    public decimal Points { get; init; }

    /// <summary>Source-reported position; tied teams share an averaged position.</summary>
    public decimal? Position { get; init; }

    /// <summary>
    /// Outcome per platform question, aligned with <see cref="PlatformResults.Questions"/>.
    /// Null when the team has no per-question data (standings-only team).
    /// </summary>
    public IReadOnlyList<PlatformAnswerState>? Answers { get; init; }
}

/// <summary>Parsed tournament results fetched from a platform.</summary>
public sealed class PlatformResults
{
    /// <summary>Platform questions in play order.</summary>
    public required IReadOnlyList<PlatformQuestion> Questions { get; init; }

    public required IReadOnlyList<PlatformTeam> Teams { get; init; }

    /// <summary>Question counts per platform tour, in play order (used for sanity warnings).</summary>
    public required IReadOnlyList<int> TourSizes { get; init; }

    /// <summary>Canonical public link to the platform's results page (for external display).</summary>
    public string? DisplayUrl { get; init; }

    /// <summary>Tournament/quiz title as reported by the platform (for the source-icon tooltip).</summary>
    public string? Title { get; init; }

    /// <summary>Raw platform payload, persisted so results can be re-parsed without re-fetching.</summary>
    public required string RawPayload { get; init; }

    /// <summary>User-facing warnings collected while parsing (uk-UA).</summary>
    public List<string> Warnings { get; init; } = [];
}

/// <summary>
/// Results could not be loaded. The message is user-facing (uk-UA) and safe to show
/// to the package manager.
/// </summary>
public class ResultsLoadException(string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>When set, the platform hides the results until this moment (Rating hideResultsTo).</summary>
    public DateTime? EmbargoedUntil { get; init; }
}
