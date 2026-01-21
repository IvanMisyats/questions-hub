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
    /// Linkifies URLs in the source text while preserving HTML safety and existing markup.
    /// Detects:
    /// - URLs starting with http:// or https://
    /// - Domain-only URLs (e.g., en.wikipedia.org/some%20page)
    /// </summary>
    /// <param name="text">Text potentially containing URLs and &lt;mark&gt; tags</param>
    /// <returns>MarkupString with clickable links and preserved highlights</returns>
    public static MarkupString Linkify(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new MarkupString(string.Empty);
        }

        // Step 1: Replace <mark> tags with placeholders to preserve them
        var result = text
            .Replace("<mark>", MarkOpenPlaceholder)
            .Replace("</mark>", MarkClosePlaceholder);

        // Step 2: Detect and wrap URLs with placeholders
        result = LinkifyUrls(result);

        // Step 3: HTML encode everything (including any malicious tags)
        result = HttpUtility.HtmlEncode(result);

        // Step 4: Restore <mark> tags from placeholders
        result = result
            .Replace(HttpUtility.HtmlEncode(MarkOpenPlaceholder), "<mark>")
            .Replace(HttpUtility.HtmlEncode(MarkClosePlaceholder), "</mark>");

        // Step 5: Restore <a> tags from placeholders
        result = result
            .Replace(HttpUtility.HtmlEncode(LinkOpenPlaceholder), "<a href=\"")
            .Replace(HttpUtility.HtmlEncode(LinkMiddlePlaceholder), "\" target=\"_blank\" rel=\"noopener noreferrer\">")
            .Replace(HttpUtility.HtmlEncode(LinkClosePlaceholder), "</a>");

        // Step 6: Preserve line breaks
        result = result.Replace("\n", "<br/>");

        return new MarkupString(result);
    }

    /// <summary>
    /// Linkifies URLs in already-sanitized HTML (from MarkupString).
    /// Use this when the text has already been HTML-encoded (e.g., from HighlightSanitizer).
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

        // Step 2: Replace <br/> tags with placeholders to preserve them
        result = result.Replace("<br/>", "\n");

        // Step 3: Detect and wrap URLs with placeholders
        // Note: URLs in already-encoded HTML will have HTML entities, so we need to handle that
        result = LinkifyUrlsInEncodedHtml(result);

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
    /// Pattern explanation:
    /// 1. (?:https?://) - Optional http:// or https://
    /// 2. (?:[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?\.)+[a-zA-Z]{2,} - Domain (e.g., en.wikipedia.org)
    /// 3. (?:[^\s<>]*) - Path and query string (anything except whitespace and HTML tags)
    /// The domain part requires at least one dot and a valid TLD
    /// </summary>
    private static string LinkifyUrls(string text)
    {
        return UrlRegex().Replace(text, match =>
        {
            var url = match.Value;
            
            // Skip if it's just a single word (no dots or too short)
            if (!url.Contains('.') || url.Length < 5)
            {
                return url;
            }
            
            // Ensure URL has protocol for the href attribute
            var href = url;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                href = "https://" + url;
            }
            
            // Wrap with placeholders: LINK_OPEN{href}LINK_MIDDLE{text}LINK_CLOSE
            return $"{LinkOpenPlaceholder}{href}{LinkMiddlePlaceholder}{url}{LinkClosePlaceholder}";
        });
    }

    /// <summary>
    /// Detects URLs in already HTML-encoded text and wraps them with link tags.
    /// This handles text where special characters are already encoded (e.g., from HighlightSanitizer).
    /// </summary>
    private static string LinkifyUrlsInEncodedHtml(string encodedHtml)
    {
        return UrlRegex().Replace(encodedHtml, match =>
        {
            var url = match.Value;
            
            // Skip if it's just a single word (no dots or too short)
            if (!url.Contains('.') || url.Length < 5)
            {
                return url;
            }
            
            // Ensure URL has protocol for the href attribute
            var href = url;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                href = "https://" + url;
            }
            
            // Directly create link tags (no placeholders needed since text is already encoded)
            return $"<a href=\"{HttpUtility.HtmlAttributeEncode(href)}\" target=\"_blank\" rel=\"noopener noreferrer\">{url}</a>";
        });
    }

    [GeneratedRegex(@"(?:https?://)?(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}(?:[^\s<>]*)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
}
