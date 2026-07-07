namespace QuestionsHub.Blazor.Infrastructure.Results;

/// <summary>
/// Configuration for loading tournament results from external platforms.
/// Base URLs are overridable for testing; defaults point at the live platforms.
/// </summary>
public class ResultsOptions
{
    public const string SectionName = "Results";

    /// <summary>Base URL of the rating.chgk.info API.</summary>
    public string RatingApiBaseUrl { get; set; } = "https://api.rating.chgk.info";

    /// <summary>Base URL of the rating.chgk.info site (for public tournament links).</summary>
    public string RatingSiteBaseUrl { get; set; } = "https://rating.chgk.info";

    /// <summary>Base URL of the open-quiz.com platform.</summary>
    public string OpenQuizBaseUrl { get; set; } = "https://www.open-quiz.com";
}
