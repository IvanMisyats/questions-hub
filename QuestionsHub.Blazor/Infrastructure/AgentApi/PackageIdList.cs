using System.Globalization;

namespace QuestionsHub.Blazor.Infrastructure.AgentApi;

/// <summary>Parses the package allowlist typed into the token form.</summary>
public static class PackageIdList
{
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\n', '\r'];

    /// <summary>
    /// Parses "512, 513 #514" into ids. Returns null when nothing is given or any entry is not a
    /// positive integer (an optional leading '#' is accepted).
    /// </summary>
    public static List<int>? Parse(string? text)
    {
        var parts = (text ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var ids = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            var digits = part.StartsWith('#') ? part[1..] : part;
            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                return null;
            ids.Add(id);
        }

        return ids.Count > 0 ? ids : null;
    }
}
