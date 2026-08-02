using System.Globalization;

namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Rules about Своя гра question values («вартість»).
///
/// A theme's questions are normally valued 10, 20, 30, 40, 50 — derived entirely from position.
/// Reserve and shoot-out questions, the substitutes printed after the last theme, are instead
/// valued as a <em>range</em> («10-30», «40-50»): they may replace any question in that band, so
/// their value is chosen rather than positional. That distinction is the only structural mark a
/// reserve theme carries, so it is what both renumbering and the ≠5 warnings key off.
/// </summary>
public static class ShvagerValues
{
    /// <summary>
    /// Whether a value was chosen rather than derived from position — anything that is not a plain
    /// integer. A blank or placeholder value is not a pin: freshly created questions arrive with
    /// «0» and must be numbered normally.
    /// </summary>
    public static bool IsPinned(string? number)
        => !string.IsNullOrWhiteSpace(number)
           && !int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    /// <summary>
    /// Whether a theme holds reserve questions, judged by its values alone.
    ///
    /// One pinned value is enough: real reserve themes mix the two forms («10-20», «30», «40-50»),
    /// and a theme whose values are all plain is an ordinary theme — one that genuinely deserves a
    /// warning when it does not hold five questions. An empty theme is never a reserve theme; it is
    /// a parse defect, and the caller still needs to hear about it.
    /// </summary>
    public static bool IsReserveTheme(IEnumerable<string?> questionValues)
        => questionValues.Any(IsPinned);
}
