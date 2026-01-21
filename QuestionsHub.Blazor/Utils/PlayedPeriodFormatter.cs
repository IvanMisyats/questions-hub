using System.Globalization;

namespace QuestionsHub.Blazor.Utils;

/// <summary>
/// Helper class for formatting played period display.
/// </summary>
public static class PlayedPeriodFormatter
{
    private static readonly CultureInfo UkrainianCulture = new("uk-UA");

    /// <summary>
    /// Formats the played period for display.
    /// </summary>
    /// <param name="from">Start date (or single date)</param>
    /// <param name="to">End date (null for single-day events)</param>
    /// <returns>Formatted string like "січень 2025" or "січень – березень 2025"</returns>
    public static string Format(DateOnly? from, DateOnly? to)
    {
        if (from == null)
            return string.Empty;

        // Single date or same date
        if (to == null || from == to)
            return FormatMonth(from.Value);

        // Same month and year
        if (from.Value.Year == to.Value.Year && from.Value.Month == to.Value.Month)
            return FormatMonth(from.Value);

        // Same year, different months
        if (from.Value.Year == to.Value.Year)
        {
            var fromMonth = from.Value.ToString("MMMM", UkrainianCulture);
            var toMonthYear = FormatMonth(to.Value);
            return $"{fromMonth} – {toMonthYear}";
        }

        // Different years
        return $"{FormatMonth(from.Value)} – {FormatMonth(to.Value)}";
    }

    private static string FormatMonth(DateOnly date)
    {
        return date.ToString("MMMM yyyy", UkrainianCulture);
    }
}
