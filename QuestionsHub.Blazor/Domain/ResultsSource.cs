namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// A tournament-results source attached to a package: one row per play of the package on a
/// platform. For Rating/OpenQuiz the results are loaded into the local DB; for Other the link
/// is only displayed.
/// </summary>
public class ResultsSource
{
    public int Id { get; set; }

    public int PackageId { get; set; }
    public Package Package { get; set; } = null!;

    /// <summary>Platform this source points to. Other means a bare external link.</summary>
    public ResultsPlatform Platform { get; set; } = ResultsPlatform.Other;

    /// <summary>URL as entered by the package manager.</summary>
    public required string Url { get; set; }

    /// <summary>Parsed platform identifier (Rating tournament id / OpenQuiz quiz id).</summary>
    public string? ExternalId { get; set; }

    /// <summary>Tournament/quiz title fetched from the platform (shown as the source-icon tooltip).</summary>
    public string? Title { get; set; }

    /// <summary>Canonical public link shown to site visitors (for OpenQuiz built from the results token; for Other equals Url).</summary>
    public string? DisplayUrl { get; set; }

    /// <summary>When the results were last successfully loaded (UTC). Null = never loaded.</summary>
    public DateTime? LoadedAt { get; set; }

    /// <summary>When a load was last attempted (UTC), successful or not.</summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>Error message of the last failed load attempt; null after a successful load.</summary>
    public string? LoadError { get; set; }

    /// <summary>Technical detail of the last failed load (exception text) for diagnostics.</summary>
    public string? LoadErrorDetail { get; set; }

    /// <summary>Rating results embargo (hideResultsTo): results are unavailable until this moment (UTC).</summary>
    public DateTime? ResultsAvailableAfter { get; set; }

    /// <summary>Number of teams in the loaded results.</summary>
    public int? TeamsCount { get; set; }

    /// <summary>Whether per-question stats were successfully mapped to the package questions.</summary>
    public bool StatsMapped { get; set; }

    /// <summary>JSON array of load/mapping warnings for the package manager.</summary>
    public string? WarningsJson { get; set; }

    /// <summary>Raw platform response (jsonb) — kept so results can be re-parsed without re-fetching.</summary>
    public string? RawPayload { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public List<TeamResult> TeamResults { get; set; } = [];
}
