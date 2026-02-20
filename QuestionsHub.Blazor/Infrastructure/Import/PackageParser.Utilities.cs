using System.Text.RegularExpressions;
using QuestionsHub.Blazor.Utils;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Shared utility and helper methods.
/// </summary>
public partial class PackageParser
{
    /// <summary>
    /// Splits text into lines, trimming each line individually.
    /// Preserves blank lines to maintain paragraph structure.
    /// </summary>
    private static IEnumerable<string> SplitIntoLines(string text)
    {
        return text.Split('\n')
            .Select(line => line.Trim());
    }

    /// <summary>
    /// Splits text into non-empty, trimmed lines.
    /// Use this when blank lines should be ignored (e.g., for structural parsing).
    /// </summary>
    private static IEnumerable<string> SplitIntoNonEmptyLines(string text)
    {
        return text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line));
    }

    private static string NormalizeText(string text)
    {
        return TextNormalizer.NormalizeWhitespaceAndDashes(text) ?? "";
    }

    /// <summary>
    /// Tries to match text against multiple regex patterns, returning the first successful match.
    /// </summary>
    private static bool TryMatchFirst(string text, out Match match, params Regex[] patterns)
    {
        foreach (var pattern in patterns)
        {
            match = pattern.Match(text);
            if (match.Success)
                return true;
        }

        match = Match.Empty;
        return false;
    }

    private static bool TryExtractHostInstructions(string text, out string instructions, out string remainingText)
    {
        instructions = "";
        remainingText = "";

        var match = ParserPatterns.HostInstructionsBracket().Match(text);
        if (!match.Success) return false;

        instructions = match.Groups[1].Value.Trim();
        remainingText = match.Groups[2].Value.Trim();
        return true;
    }

    private static bool TryExtractBracketedHandout(string text, out string handoutText, out string remainingText)
    {
        handoutText = "";
        remainingText = "";

        var match = ParserPatterns.HandoutMarkerBracket().Match(text);
        if (!match.Success) return false;

        handoutText = match.Groups[1].Value.Trim();
        remainingText = match.Groups[2].Value.Trim();
        return true;
    }

    /// <summary>
    /// Tries to extract the opening of a multiline bracketed handout (no closing bracket on this line).
    /// </summary>
    private static bool TryExtractMultilineHandoutOpening(string text, out string handoutText)
    {
        handoutText = "";

        // Don't match if the closing bracket is on the same line (single-line case)
        if (text.Contains(']'))
            return false;

        var match = ParserPatterns.HandoutMarkerBracketOpen().Match(text);
        if (!match.Success)
            return false;

        handoutText = match.Groups[1].Value.Trim();
        return true;
    }

    private static List<string> ParseAuthorList(string text)
    {
        return text
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(s => s.Split([" та ", " і ", " and "], StringSplitOptions.RemoveEmptyEntries))
            .Select(s => StripAccents(s.Trim().TrimEnd('.', ',', ';')))
            .Select(s => TextNormalizer.NormalizeApostrophes(s)!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .SelectMany(SplitAuthorOnRedakciya)
            .ToList();
    }

    /// <summary>
    /// Splits a single author entry on "у редакції" / "в редакції" / "за ідеєю" patterns,
    /// converts genitive names to nominative, and strips city parentheses.
    /// E.g., "Станіслав Мерлян (Одеса) у редакції Едуарда Голуба (Київ)"
    /// → ["Станіслав Мерлян", "Едуард Голуб"]
    /// </summary>
    private static IEnumerable<string> SplitAuthorOnRedakciya(string author)
    {
        var parts = UkrainianNameHelper.SplitAndNormalizeAuthors(author);
        return parts.Where(p => !string.IsNullOrWhiteSpace(p));
    }

    /// <summary>
    /// Removes combining acute accent marks from text.
    /// Used for author and editor names to ensure consistent matching.
    /// </summary>
    private static string StripAccents(string text)
    {
        return text.Replace("\u0301", "");
    }

    private static string AppendText(string? existing, string newText)
    {
        if (string.IsNullOrWhiteSpace(existing))
            return newText;
        return existing + "\n" + newText;
    }

    /// <summary>
    /// Appends a blank line to the current section's content.
    /// Used to preserve paragraph breaks in multiline content.
    /// </summary>
    private static string AppendBlankLine(string? existing)
    {
        if (string.IsNullOrEmpty(existing))
            return "";
        return existing + "\n";
    }

    /// <summary>
    /// Trims leading and trailing blank lines from text while preserving internal blank lines.
    /// </summary>
    private static string TrimBlankLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var lines = text.Split('\n');
        var startIndex = 0;
        var endIndex = lines.Length - 1;

        // Find first non-empty line
        while (startIndex <= endIndex && string.IsNullOrWhiteSpace(lines[startIndex]))
            startIndex++;

        // Find last non-empty line
        while (endIndex >= startIndex && string.IsNullOrWhiteSpace(lines[endIndex]))
            endIndex--;

        if (startIndex > endIndex)
            return string.Empty;

        return string.Join("\n", lines.Skip(startIndex).Take(endIndex - startIndex + 1));
    }
}
