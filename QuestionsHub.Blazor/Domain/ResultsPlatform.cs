namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Platform a tournament-results source comes from.
/// </summary>
public enum ResultsPlatform
{
    /// <summary>Arbitrary external link (Google Docs etc.). Never loaded — only opened in a new tab.</summary>
    Other = 0,

    /// <summary>rating.chgk.info — official rating site with a public API.</summary>
    Rating = 1,

    /// <summary>open-quiz.com — results come from the platform's static results.json.</summary>
    OpenQuiz = 2
}
