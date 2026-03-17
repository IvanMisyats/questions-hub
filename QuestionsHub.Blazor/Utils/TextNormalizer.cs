using System.Globalization;
using System.Text;

namespace QuestionsHub.Blazor.Utils;

/// <summary>
/// Provides text normalization utilities for consistent data storage and search.
/// </summary>
public static class TextNormalizer
{
    /// <summary>
    /// The standard Ukrainian apostrophe character (U+02BC - Modifier Letter Apostrophe).
    /// This is the correct typographic apostrophe for Ukrainian language.
    /// </summary>
    public const char UkrainianApostrophe = '\u02BC'; // ʼ

    /// <summary>
    /// Characters that should be replaced with the standard Ukrainian apostrophe.
    /// </summary>
    private static readonly char[] ApostropheLikeCharacters =
    [
        '\'',     // U+0027 - Apostrophe (ASCII)
        '\u2019', // ' - Right Single Quotation Mark
        '\u02C8'  // ˈ - Modifier Letter Vertical Line
    ];

    /// <summary>
    /// Normalizes apostrophe-like characters to the standard Ukrainian apostrophe (U+02BC).
    /// </summary>
    /// <param name="text">Text to normalize.</param>
    /// <returns>Normalized text, or null if input is null.</returns>
    public static string? NormalizeApostrophes(string? text)
    {
        if (text == null)
            return null;

        foreach (var apostrophe in ApostropheLikeCharacters)
        {
            text = text.Replace(apostrophe, UkrainianApostrophe);
        }

        return text;
    }

    /// <summary>
    /// Normalizes text by replacing special whitespace and dash characters.
    /// Replaces non-breaking spaces with regular spaces and various dashes with hyphens.
    /// </summary>
    /// <param name="text">Text to normalize.</param>
    /// <returns>Normalized and trimmed text, or null if input is null.</returns>
    public static string? NormalizeWhitespaceAndDashes(string? text)
    {
        if (text == null)
            return null;

        // Replace non-breaking space with regular space
        text = text.Replace('\u00A0', ' ');

        // Replace en dash and em dash with hyphen
        text = text.Replace('–', '-').Replace('—', '-');

        return text.Trim();
    }

    /// <summary>
    /// Applies full text normalization: whitespace, dashes, and apostrophes.
    /// Use this for most text fields during import and save operations.
    /// </summary>
    /// <param name="text">Text to normalize.</param>
    /// <returns>Fully normalized and trimmed text, or null if input is null.</returns>
    public static string? Normalize(string? text)
    {
        if (text == null)
            return null;

        text = NormalizeWhitespaceAndDashes(text);
        text = NormalizeApostrophes(text);

        return text?.Trim();
    }

    /// <summary>
    /// Normalizes text without apostrophe normalization.
    /// Use this for Source fields which may contain URLs where apostrophes are intentional.
    /// </summary>
    /// <param name="text">Text to normalize.</param>
    /// <returns>Normalized text (whitespace and dashes only), or null if input is null.</returns>
    public static string? NormalizeExcludingApostrophes(string? text)
    {
        return NormalizeWhitespaceAndDashes(text);
    }

    /// <summary>
    /// Normalizes text for full-text search matching. Mirrors PostgreSQL's qh_normalize function:
    /// removes accents, lowercases, normalizes ґ→г, and converts apostrophe variants to ASCII apostrophe.
    /// Use this to pre-normalize search queries in C# instead of calling qh_normalize in SQL.
    /// </summary>
    /// <param name="text">Text to normalize for FTS.</param>
    /// <returns>Normalized text ready for FTS matching.</returns>
    public static string NormalizeForFts(string text)
    {
        text = RemoveAccents(text);
        text = text.ToLowerInvariant();
        text = text.Replace('ґ', 'г');

        // Match PostgreSQL unaccent behavior: apostrophe variants → ASCII apostrophe
        foreach (var c in ApostropheLikeCharacters)
            text = text.Replace(c, '\'');
        text = text.Replace(UkrainianApostrophe, '\'');

        return text;
    }

    /// <summary>
    /// Removes explicitly present combining marks (e.g., stress marks) from text.
    /// Unlike Unicode FormD decomposition, this does NOT decompose precomposed characters
    /// like й (U+0439) or ї (U+0457) — those are distinct Cyrillic letters, not accented variants.
    /// This matches PostgreSQL's unaccent behavior for Ukrainian text.
    /// </summary>
    /// <param name="text">Text to process.</param>
    /// <returns>Text with combining marks removed.</returns>
    public static string RemoveAccents(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
