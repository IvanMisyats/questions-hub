using System.Web;
using Microsoft.AspNetCore.Components;

namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Helper for safely rendering highlighted search results.
/// Only allows &lt;mark&gt; tags from ts_headline(), escapes everything else.
/// </summary>
public static class HighlightSanitizer
{
    // Placeholder tokens that won't appear in normal text
    private const string MarkOpenPlaceholder = "\0MARK_OPEN\0";
    private const string MarkClosePlaceholder = "\0MARK_CLOSE\0";

    /// <summary>
    /// Sanitizes highlighted text, keeping only &lt;mark&gt; tags safe while escaping other HTML.
    /// Returns a MarkupString safe for Blazor rendering.
    /// </summary>
    /// <param name="highlightedText">Text containing &lt;mark&gt; tags from ts_headline()</param>
    /// <param name="fallbackText">Fallback text if highlighted is null/empty</param>
    /// <returns>MarkupString with safe HTML</returns>
    public static MarkupString Sanitize(string? highlightedText, string? fallbackText = null)
    {
        // If no highlighted text, escape and return fallback
        if (string.IsNullOrEmpty(highlightedText))
        {
            return new MarkupString(HttpUtility.HtmlEncode(fallbackText ?? ""));
        }

        // Step 1: Replace <mark> and </mark> with placeholders
        var text = highlightedText
            .Replace("<mark>", MarkOpenPlaceholder)
            .Replace("</mark>", MarkClosePlaceholder);

        // Step 2: HTML encode everything (including any malicious tags)
        text = HttpUtility.HtmlEncode(text);

        // Step 3: Restore <mark> tags from placeholders
        text = text
            .Replace(HttpUtility.HtmlEncode(MarkOpenPlaceholder), "<mark>")
            .Replace(HttpUtility.HtmlEncode(MarkClosePlaceholder), "</mark>");

        // Step 4: Preserve line breaks for pre-line CSS
        text = text.Replace("\n", "<br/>");

        return new MarkupString(text);
    }

    /// <summary>
    /// Checks if the highlighted text actually contains any highlights.
    /// </summary>
    public static bool HasHighlights(string? text) =>
        !string.IsNullOrEmpty(text) && text.Contains("<mark>");
}

