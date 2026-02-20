using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Components;

namespace QuestionsHub.Blazor.Infrastructure.Search;

/// <summary>
/// Helper for safely rendering highlighted search results.
/// Supports both server-side ts_headline() marks and client-side accent-insensitive highlighting.
/// </summary>
public static partial class HighlightSanitizer
{
    // Placeholder tokens that won't appear in normal text
    private const string MarkOpenPlaceholder = "\0MARK_OPEN\0";
    private const string MarkClosePlaceholder = "\0MARK_CLOSE\0";

    /// <summary>
    /// Sanitizes highlighted text, keeping only &lt;mark&gt; tags safe while escaping other HTML.
    /// If no highlights found from ts_headline, applies client-side accent-insensitive highlighting.
    /// </summary>
    /// <param name="highlightedText">Text containing &lt;mark&gt; tags from ts_headline()</param>
    /// <param name="fallbackText">Original text to use if highlighted version has no marks</param>
    /// <param name="searchQuery">Optional search query for client-side highlighting fallback</param>
    /// <returns>MarkupString with safe HTML</returns>
    public static MarkupString Sanitize(string? highlightedText, string? fallbackText = null, string? searchQuery = null)
    {
        var textToUse = highlightedText ?? fallbackText ?? "";

        // If ts_headline produced highlights, use them
        if (HasHighlights(highlightedText))
        {
            return SanitizeWithMarks(highlightedText!);
        }

        // If we have a search query, try client-side highlighting on the original text
        if (!string.IsNullOrEmpty(searchQuery) && !string.IsNullOrEmpty(fallbackText))
        {
            var highlighted = ApplyClientSideHighlighting(fallbackText, searchQuery);
            if (HasHighlights(highlighted))
            {
                return SanitizeWithMarks(highlighted);
            }
        }

        // No highlighting possible - just escape and return
        var escaped = HttpUtility.HtmlEncode(textToUse);
        return new MarkupString(escaped.Replace("\n", "<br/>"));
    }

    /// <summary>
    /// Sanitizes text that already contains &lt;mark&gt; tags.
    /// </summary>
    private static MarkupString SanitizeWithMarks(string text)
    {
        // Step 1: Replace <mark> and </mark> with placeholders
        var result = text
            .Replace("<mark>", MarkOpenPlaceholder)
            .Replace("</mark>", MarkClosePlaceholder);

        // Step 2: HTML encode everything (including any malicious tags)
        result = HttpUtility.HtmlEncode(result);

        // Step 3: Restore <mark> tags from placeholders
        result = result
            .Replace(HttpUtility.HtmlEncode(MarkOpenPlaceholder), "<mark>")
            .Replace(HttpUtility.HtmlEncode(MarkClosePlaceholder), "</mark>");

        // Step 4: Preserve line breaks
        result = result.Replace("\n", "<br/>");

        return new MarkupString(result);
    }

    /// <summary>
    /// Applies accent-insensitive highlighting to text based on search query.
    /// Handles AND, OR, phrase, and exclusion operators.
    /// </summary>
    private static string ApplyClientSideHighlighting(string text, string searchQuery)
    {
        var words = ParseSearchTerms(searchQuery);
        if (words.Count == 0) return text;

        var result = text;
        foreach (var word in words)
        {
            result = HighlightWordAccentInsensitive(result, word);
        }
        return result;
    }

    /// <summary>
    /// Parses search query into individual terms, handling operators.
    /// </summary>
    private static List<string> ParseSearchTerms(string query)
    {
        var terms = new List<string>();

        // Extract quoted phrases first
        var phraseMatches = PhraseRegex().Matches(query);
        foreach (Match match in phraseMatches)
        {
            terms.Add(match.Groups[1].Value);
        }

        // Remove phrases from query
        var remaining = PhraseRegex().Replace(query, " ");

        // Split by whitespace and OR, filter out operators and exclusions
        var words = remaining.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            // Skip operators and exclusions
            if (word.Equals("OR", StringComparison.OrdinalIgnoreCase) ||
                word.StartsWith('-') ||
                word.Length < 2)
            {
                continue;
            }
            terms.Add(word);
        }

        return terms;
    }

    /// <summary>
    /// Highlights a word in text using accent-insensitive matching.
    /// Preserves the original text's case and accents.
    /// </summary>
    private static string HighlightWordAccentInsensitive(string text, string word)
    {
        if (string.IsNullOrEmpty(word)) return text;

        var normalizedWord = RemoveAccents(word.ToLowerInvariant());
        var result = new StringBuilder();
        var i = 0;

        while (i < text.Length)
        {
            // Try to match word at current position
            var matchLength = TryMatchWord(text, i, normalizedWord);
            if (matchLength > 0)
            {
                // Found a match - wrap in <mark> tags
                result.Append("<mark>");
                result.Append(text.AsSpan(i, matchLength));
                result.Append("</mark>");
                i += matchLength;
            }
            else
            {
                result.Append(text[i]);
                i++;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Tries to match a normalized word at the given position.
    /// Returns the length of the match in original text, or 0 if no match.
    /// </summary>
    private static int TryMatchWord(string text, int start, string normalizedWord)
    {
        var textPos = start;
        var wordPos = 0;

        while (wordPos < normalizedWord.Length && textPos < text.Length)
        {
            var textChar = text[textPos];
            var normalizedTextChar = RemoveAccents(char.ToLowerInvariant(textChar).ToString());

            // Handle combining characters (accents) in original text
            if (normalizedTextChar.Length == 0 || IsCombiningMark(textChar))
            {
                textPos++;
                continue;
            }

            if (normalizedTextChar.Length > 0 && normalizedTextChar[0] == normalizedWord[wordPos])
            {
                textPos++;
                wordPos++;
            }
            else
            {
                return 0; // No match
            }
        }

        // Check if we matched the whole word
        if (wordPos == normalizedWord.Length)
        {
            // Include any trailing combining marks
            while (textPos < text.Length && IsCombiningMark(text[textPos]))
            {
                textPos++;
            }
            return textPos - start;
        }

        return 0;
    }

    /// <summary>
    /// Removes diacritical marks (accents) from text.
    /// </summary>
    private static string RemoveAccents(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Checks if a character is a combining mark (accent/diacritical).
    /// </summary>
    private static bool IsCombiningMark(char c)
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(c);
        return category == UnicodeCategory.NonSpacingMark ||
               category == UnicodeCategory.SpacingCombiningMark ||
               category == UnicodeCategory.EnclosingMark;
    }

    /// <summary>
    /// Checks if the text contains any highlights.
    /// </summary>
    public static bool HasHighlights(string? text) =>
        !string.IsNullOrEmpty(text) && text.Contains("<mark>");

    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex PhraseRegex();
}

