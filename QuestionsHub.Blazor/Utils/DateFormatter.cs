using System.Globalization;

namespace QuestionsHub.Blazor.Utils;

/// <summary>
/// Helper class for formatting dates in Ukrainian locale.
/// </summary>
public static class DateFormatter
{
    private static readonly CultureInfo UkrainianCulture = new("uk-UA");

    /// <summary>
    /// Formats a date as "d MMMM yyyy" in Ukrainian (e.g., "22 січня 2026").
    /// </summary>
    public static string FormatDate(DateTime date)
    {
        return date.ToString("d MMMM yyyy", UkrainianCulture);
    }

    /// <summary>
    /// Formats a nullable date. Returns empty string if null.
    /// </summary>
    public static string FormatDate(DateTime? date)
    {
        return date.HasValue ? FormatDate(date.Value) : string.Empty;
    }
}
