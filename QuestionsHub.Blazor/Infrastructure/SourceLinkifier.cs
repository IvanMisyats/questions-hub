using System.Collections.Frozen;
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
    /// Characters that end a sentence rather than a URL. Trimmed off the right edge of a match.
    /// Closing brackets are handled separately (see <see cref="TrimTrailingPunctuation"/>) because
    /// they can legitimately belong to the URL, as in .../wiki/Cat_(disambiguation).
    /// </summary>
    private const string TrailingPunctuation = ".,;:!?*…\"'’”»‘“„";

    /// <summary>
    /// Top-level domains accepted for a bare host written without a scheme and without a "www."
    /// prefix. Without this gate, ordinary prose linkifies: "the end.Next sentence" (a missing space
    /// after a period), "photo.jpg", "Vol.II". A scheme or a "www." prefix is treated as intent by
    /// the author and skips the check, so exotic TLDs still work when written explicitly.
    /// </summary>
    private static readonly FrozenSet<string> KnownTlds = (
        // ccTLDs (ISO 3166-1 alpha-2 plus ac/eu/su/uk)
        "ac ad ae af ag ai al am ao aq ar as at au aw ax az " +
        "ba bb bd be bf bg bh bi bj bm bn bo bq br bs bt bw by bz " +
        "ca cc cd cf cg ch ci ck cl cm cn co cr cu cv cw cx cy cz " +
        "de dj dk dm do dz ec ee eg eh er es et eu fi fj fk fm fo fr " +
        "ga gb gd ge gf gg gh gi gl gm gn gp gq gr gs gt gu gw gy " +
        "hk hm hn hr ht hu id ie il im in io iq ir is it je jm jo jp " +
        "ke kg kh ki km kn kp kr kw ky kz la lb lc li lk lr ls lt lu lv ly " +
        "ma mc md me mg mh mk ml mm mn mo mp mq mr ms mt mu mv mw mx my mz " +
        "na nc ne nf ng ni nl no np nr nu nz om " +
        "pa pe pf pg ph pk pl pm pn pr ps pt pw py qa re ro rs ru rw " +
        "sa sb sc sd se sg sh si sj sk sl sm sn so sr ss st su sv sx sy sz " +
        "tc td tf tg th tj tk tl tm tn to tr tt tv tw tz ua ug uk um us uy uz " +
        "va vc ve vg vi vn vu wf ws ye yt za zm zw " +
        // gTLDs seen in question sources
        "com org net int edu gov mil arpa " +
        "info biz name pro asia cat coop aero museum jobs mobi tel travel post " +
        "app art blog cloud club dev digital email fun gallery game games group guru " +
        "life link live media news one online page photo photos press pub shop site " +
        "software space store studio team tech today top tube video website wiki work world xyz zone")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

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
            if (!TrySplitMatch(match.Value, htmlEncoded: false, out var display, out var suffix, out var href))
            {
                return match.Value;
            }

            // Wrap with placeholders: LINK_OPEN{href}LINK_MIDDLE{text}LINK_CLOSE
            return $"{LinkOpenPlaceholder}{href}{LinkMiddlePlaceholder}{display}{LinkClosePlaceholder}{suffix}";
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
            if (!TrySplitMatch(match.Value, htmlEncoded: true, out var display, out var suffix, out var href))
            {
                return match.Value;
            }

            // Directly create link tags (no placeholders needed since text is already encoded)
            return $"<a href=\"{HttpUtility.HtmlAttributeEncode(href)}\" target=\"_blank\" rel=\"noopener noreferrer\">{display}</a>{suffix}";
        });
    }

    /// <summary>
    /// Splits a regex match into the URL itself, the punctuation that trailed it, and the href to
    /// link to. Returns false when what was matched is not a URL worth linkifying.
    /// </summary>
    /// <param name="matched">The raw regex match.</param>
    /// <param name="htmlEncoded">True when the match came from HTML-encoded text, so that trailing
    /// entities such as &amp;#187; are recognised as punctuation and the href is built from the
    /// decoded URL (encoding it again would turn &amp;amp; into &amp;amp;amp;).</param>
    /// <param name="display">The URL as it should be shown, in the same encoding as the input.</param>
    /// <param name="suffix">The trailing punctuation, to be re-emitted outside the link.</param>
    /// <param name="href">The absolute URL for the href attribute, always decoded.</param>
    private static bool TrySplitMatch(string matched, bool htmlEncoded, out string display, out string suffix, out string href)
    {
        display = TrimTrailingPunctuation(matched, htmlEncoded);
        suffix = matched[display.Length..];
        href = string.Empty;

        var url = htmlEncoded ? HttpUtility.HtmlDecode(display) : display;
        if (!IsValidUrl(url))
        {
            return false;
        }

        href = EnsureProtocol(url);
        return true;
    }

    /// <summary>
    /// Strips sentence punctuation that the greedy match pulled in past the end of the URL.
    /// A closing bracket is kept when the URL contains a matching opener, so
    /// ".../wiki/Cat_(disambiguation)" survives while "(https://ibb.co/mVgbHSbP)." does not.
    /// </summary>
    private static string TrimTrailingPunctuation(string url, bool htmlEncoded)
    {
        while (url.Length > 0)
        {
            var last = url[^1];
            var tokenLength = 1;

            // In encoded text the final character may be written as an entity, e.g. "&#187;" for »
            if (htmlEncoded)
            {
                var entity = TrailingHtmlEntityRegex().Match(url);
                if (entity.Success)
                {
                    var decoded = HttpUtility.HtmlDecode(entity.Value);
                    if (decoded.Length != 1)
                    {
                        break;
                    }
                    last = decoded[0];
                    tokenLength = entity.Length;
                }
            }

            var opener = last switch { ')' => '(', ']' => '[', '}' => '{', _ => '\0' };
            if (opener != '\0')
            {
                if (CountChar(url, opener) >= CountChar(url, last))
                {
                    break; // balanced — the bracket belongs to the URL
                }
            }
            else if (!TrailingPunctuation.Contains(last))
            {
                break;
            }

            url = url[..^tokenLength];
        }

        return url;
    }

    private static int CountChar(string text, char c)
    {
        var count = 0;
        foreach (var ch in text)
        {
            if (ch == c) count++;
        }
        return count;
    }

    /// <summary>
    /// Validates if a matched string is a valid URL worth linkifying.
    /// The host must be absolute and dotted; a host written without a scheme and without a "www."
    /// prefix must additionally end in a known TLD (see <see cref="KnownTlds"/>).
    /// </summary>
    private static bool IsValidUrl(string url)
    {
        if (!Uri.TryCreate(EnsureProtocol(url), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var host = uri.Host;
        var lastDot = host.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == host.Length - 1)
        {
            return false; // "localhost", "example." and similar are not worth linking
        }

        if (HasProtocol(url) || host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return KnownTlds.Contains(host[(lastDot + 1)..]);
    }

    private static bool HasProtocol(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ensures URL has a protocol prefix for the href attribute.
    /// </summary>
    private static string EnsureProtocol(string url) => HasProtocol(url) ? url : "https://" + url;

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

    // URL detection. Two alternatives, both guarded on the left by (?<![\w@/]) so a match cannot
    // start in the middle of a word, right after an e-mail's "@", or inside a path segment:
    //
    //   1. Explicit scheme — greedy to the first space or quote; trailing punctuation is trimmed
    //      afterwards by TrimTrailingPunctuation.
    //   2. Bare host — every label must contain a letter, which is what stops a source list's
    //      numbering from being swallowed as a subdomain ("1.www.site.com" → "www.site.com").
    //      Past the host the match may continue only through "/", "?" or "#", so a host followed
    //      by prose punctuation ("tsn.ua.", "example.com).") ends at the TLD.
    //
    // The scheme alternative is listed first so "1.http://site.com" links to the scheme'd URL
    // rather than to the nonsense host "1.http".
    [GeneratedRegex("""(?<![\w@/])(?:https?://[^\s<>"«»“”„‘’]+|(?:[a-zA-Z0-9-]*[a-zA-Z][a-zA-Z0-9-]*\.)+[a-zA-Z]{2,24}(?![\w-])(?::\d{2,5})?(?:[/?#][^\s<>"«»“”„‘’]*)?)""", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    // A single HTML entity at the very end of the string: "&#187;", "&#x2019;", "&quot;"
    [GeneratedRegex("""&(?:#\d{1,7}|#[xX][0-9a-fA-F]{1,6}|[a-zA-Z][a-zA-Z0-9]{1,31});$""")]
    private static partial Regex TrailingHtmlEntityRegex();
}
