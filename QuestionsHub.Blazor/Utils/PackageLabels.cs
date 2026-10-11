using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Utils;

/// <summary>
/// Names of tours and blocks as the package page shows them. The favorites page uses the same helpers,
/// so an item is called the same everywhere.
/// </summary>
public static class PackageLabels
{
    /// <summary>«Тема N. Назва» (Своя гра), «Розминка», «Перестрілка» or «Тур N».</summary>
    public static string TourName(Tour tour, PackageType type) =>
        type == PackageType.Shvager ? tour.ThemeDisplayName
        : tour.IsWarmup ? "Розминка"
        : tour.IsShootout ? "Перестрілка"
        : $"Тур {tour.Number}";

    /// <summary>
    /// 1-based block numbers across the whole package (unnamed blocks show «Блок N»), keyed by block id.
    /// <paramref name="toursInDisplayOrder"/> must be ordered like the page (<see cref="TourOrdering.ForDisplay"/>).
    /// </summary>
    public static Dictionary<int, int> BlockNumbers(IEnumerable<Tour> toursInDisplayOrder)
    {
        var numbers = new Dictionary<int, int>();
        foreach (var block in toursInDisplayOrder.SelectMany(t => t.Blocks.OrderBy(b => b.OrderIndex)))
            numbers[block.Id] = numbers.Count + 1;

        return numbers;
    }
}
