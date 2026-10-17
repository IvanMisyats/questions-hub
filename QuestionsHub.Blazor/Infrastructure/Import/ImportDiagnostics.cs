using System.Collections.ObjectModel;

namespace QuestionsHub.Blazor.Infrastructure.Import;

internal static class ImportDiagnostics
{
    // Bound labels before interpolation: a single large tour name may be repeated for every question.
    // 100 keeps the generated asset names (64 hex digits + extension) whole.
    public static string Label(string? value) => Truncate(value ?? "", 100);

    public static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        var length = maxLength - 1;
        if (length > 0 && char.IsHighSurrogate(value[length - 1])) length--;
        return value[..length] + "…";
    }
}

/// <summary>Retains the first 500 diagnostics and one count of the omitted warnings.</summary>
public sealed class ImportWarnings : Collection<string>
{
    public const int Limit = 500;
    private long _omitted;

    protected override void InsertItem(int index, string item)
    {
        item = ImportDiagnostics.Truncate(item, 512);
        if (Count < Limit)
        {
            base.InsertItem(index, item);
            return;
        }

        // Prepended extraction/type warnings keep their original ordering, displacing the last detail.
        if (index < Limit)
        {
            base.InsertItem(index, item);
            base.RemoveItem(Limit);
        }
        _omitted++;
        var summary = $"… і ще {_omitted} попереджень";
        if (Count == Limit) base.InsertItem(Limit, summary);
        else base.SetItem(Limit, summary);
    }

    public void InsertRange(int index, IEnumerable<string> warnings)
    {
        foreach (var warning in warnings) Insert(Math.Min(index++, Limit), warning);
    }

    protected override void SetItem(int index, string item) =>
        base.SetItem(index, ImportDiagnostics.Truncate(item, 512));

    protected override void ClearItems()
    {
        base.ClearItems();
        _omitted = 0;
    }
}
