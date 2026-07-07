using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Utils;

/// <summary>Display helpers for tournament-results sources (uk-UA labels, badge styling).</summary>
public static class ResultsDisplay
{
    public static string PlatformName(ResultsPlatform platform) => platform switch
    {
        ResultsPlatform.Rating => "Рейтинг МАК",
        ResultsPlatform.OpenQuiz => "OpenQuiz",
        _ => "Інше"
    };

    public static string PlatformBadgeClass(ResultsPlatform platform) => platform switch
    {
        ResultsPlatform.Rating => "bg-primary",
        ResultsPlatform.OpenQuiz => "bg-info text-dark",
        _ => "bg-secondary"
    };
}
