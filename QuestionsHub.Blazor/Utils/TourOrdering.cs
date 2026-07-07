using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Utils;

/// <summary>
/// The single definition of ЩДК tour display order: numeric Number ascending (warmup «0»
/// first), non-numeric (e.g. «П» shootout) last. The stats mapper attaches per-question
/// statistics positionally in this order, so every consumer (package page, results page,
/// mapper) MUST use this helper — a divergent copy silently misattaches stats.
/// </summary>
public static class TourOrdering
{
    public static List<Tour> OrderWww(IEnumerable<Tour> tours)
    {
        return tours
            .OrderBy(t => int.TryParse(t.Number, out var num) ? num : int.MaxValue)
            .ThenBy(t => t.Number, StringComparer.Ordinal)
            .ToList();
    }
}
