using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Components;

namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Helper for converting URLs in source text to clickable links.
/// Preserves existing HTML markup like &lt;mark&gt; tags from search highlights.
/// </summary>
public static partial class SourceLinkifier
{
    // Placeholder tokens that won't appear in normal text
    private const string MarkOpenPlaceholder = "\0MARK_OPEN\0";
    private const string MarkClosePlaceholder = "\0MARK_CLOSE\0";
    private const string LinkOpenPlaceholder = "\0LINK_OPEN\0";
    private const string LinkMiddlePlaceholder = "\0LINK_MIDDLE\0";
    private const string LinkClosePlaceholder = "\0LINK_CLOSE\0";

    /// <summary>
    /// Linkifies URLs in raw source text while preserving HTML safety.
    /// Used for displaying question sources from the database.
    /// Detects:
    /// - URLs starting with http:// or https://
    /// - Domain-only URLs (e.g., en.wikipedia.org/some%20page)
    /// </summary>
    /// <param name="text">Plain text potentially containing URLs</param>
    /// <returns>MarkupString with clickable links</returns>
    public static MarkupString Linkify(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new MarkupString(string.Empty);
        }

        // Step 1: Detect and wrap URLs with placeholders
        var result = ReplaceUrlsWithPlaceholders(text);

        // Step 2: HTML encode everything (including any malicious content)
        result = HttpUtility.HtmlEncode(result);

        // Step 3: Restore <a> tags from placeholders
        result = RestoreLinkTagsFromPlaceholders(result);

        // Step 4: Preserve line breaks
        result = result.Replace("\n", "<br/>");

        return new MarkupString(result);
    }

    /// <summary>
    /// Linkifies URLs in already-sanitized HTML (from MarkupString).
    /// Use this when the text has already been HTML-encoded (e.g., from HighlightSanitizer).
    /// Preserves &lt;mark&gt; tags from search highlighting.
    /// Detects:
    /// - URLs starting with http:// or https://
    /// - Domain-only URLs (e.g., en.wikipedia.org/some%20page)
    /// </summary>
    /// <param name="sanitizedHtml">MarkupString containing pre-sanitized HTML with optional &lt;mark&gt; tags</param>
    /// <returns>MarkupString with clickable links and preserved highlights</returns>
    public static MarkupString Linkify(MarkupString sanitizedHtml)
    {
        var htmlContent = sanitizedHtml.Value;
        
        if (string.IsNullOrEmpty(htmlContent))
        {
            return new MarkupString(string.Empty);
        }

        // Step 1: Replace <mark> tags with placeholders to preserve them
        var result = htmlContent
            .Replace("<mark>", MarkOpenPlaceholder)
            .Replace("</mark>", MarkClosePlaceholder);

        // Step 2: Replace <br/> and <br> tags with newlines to preserve them
        result = result.Replace("<br/>", "\n").Replace("<br>", "\n");

        // Step 3: Detect and wrap URLs with link tags (text is already encoded)
        result = CreateLinksInEncodedHtml(result);

        // Step 4: Restore <mark> tags from placeholders
        result = result
            .Replace(MarkOpenPlaceholder, "<mark>")
            .Replace(MarkClosePlaceholder, "</mark>");

        // Step 5: Restore line breaks
        result = result.Replace("\n", "<br/>");

        return new MarkupString(result);
    }

    /// <summary>
    /// Detects URLs in text and wraps them with link placeholders.
    /// Shared logic for URL detection and validation.
    /// </summary>
    private static string ReplaceUrlsWithPlaceholders(string text)
    {
        return UrlRegex().Replace(text, match =>
        {
            var url = match.Value;
            
            if (!IsValidUrl(url))
            {
                return url;
            }
            
            var href = EnsureProtocol(url);
            
            // Wrap with placeholders: LINK_OPEN{href}LINK_MIDDLE{text}LINK_CLOSE
            return $"{LinkOpenPlaceholder}{href}{LinkMiddlePlaceholder}{url}{LinkClosePlaceholder}";
        });
    }

    /// <summary>
    /// Detects URLs in already HTML-encoded text and wraps them with link tags.
    /// This handles text where special characters are already encoded (e.g., from HighlightSanitizer).
    /// </summary>
    private static string CreateLinksInEncodedHtml(string encodedHtml)
    {
        return UrlRegex().Replace(encodedHtml, match =>
        {
            var url = match.Value;
            
            if (!IsValidUrl(url))
            {
                return url;
            }
            
            var href = EnsureProtocol(url);
            
            // Directly create link tags (no placeholders needed since text is already encoded)
            return $"<a href=\"{HttpUtility.HtmlAttributeEncode(href)}\" target=\"_blank\" rel=\"noopener noreferrer\">{url}</a>";
        });
    }

    /// <summary>
    /// Validates if a matched string is a valid URL worth linkifying.
    /// </summary>
    private static bool IsValidUrl(string url)
    {
        return url.Contains('.') && url.Length >= 5;
    }

    /// <summary>
    /// Ensures URL has a protocol prefix for the href attribute.
    /// </summary>
    private static string EnsureProtocol(string url)
    {
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }
        return "https://" + url;
    }

    /// <summary>
    /// Restores link tags from encoded placeholders.
    /// </summary>
    private static string RestoreLinkTagsFromPlaceholders(string text)
    {
        return text
            .Replace(HttpUtility.HtmlEncode(LinkOpenPlaceholder), "<a href=\"")
            .Replace(HttpUtility.HtmlEncode(LinkMiddlePlaceholder), "\" target=\"_blank\" rel=\"noopener noreferrer\">")
            .Replace(HttpUtility.HtmlEncode(LinkClosePlaceholder), "</a>");
    }

    [GeneratedRegex(@"(?:https?://)?(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}(?:[^\s<>]*)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
}
