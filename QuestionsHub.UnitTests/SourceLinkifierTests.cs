using FluentAssertions;
using QuestionsHub.Blazor.Infrastructure;
using Xunit;

namespace QuestionsHub.UnitTests;

public class SourceLinkifierTests
{
    [Fact]
    public void Linkify_EmptyString_ReturnsEmpty()
    {
        // Arrange
        var text = "";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public void Linkify_Null_ReturnsEmpty()
    {
        // Arrange
        string? text = null;

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public void Linkify_TextWithoutUrls_ReturnsEncodedText()
    {
        // Arrange
        var text = "Some source reference without URLs";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().Be("Some source reference without URLs");
    }

    [Fact]
    public void Linkify_HttpUrl_CreatesClickableLink()
    {
        // Arrange
        var text = "Source: http://example.com/page";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().Contain("<a href=\"http://example.com/page\" target=\"_blank\" rel=\"noopener noreferrer\">http://example.com/page</a>");
    }

    [Fact]
    public void Linkify_HttpsUrl_CreatesClickableLink()
    {
        // Arrange
        var text = "https://en.wikipedia.org/wiki/Something";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().Contain("<a href=\"https://en.wikipedia.org/wiki/Something\" target=\"_blank\" rel=\"noopener noreferrer\">https://en.wikipedia.org/wiki/Something</a>");
    }

    [Fact]
    public void Linkify_DomainOnlyUrl_CreatesClickableLinkWithHttps()
    {
        // Arrange
        var text = "Source: en.wikipedia.org/wiki/Page";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().Contain("<a href=\"https://en.wikipedia.org/wiki/Page\" target=\"_blank\" rel=\"noopener noreferrer\">en.wikipedia.org/wiki/Page</a>");
    }

    [Fact]
    public void Linkify_UrlWithEncodedCharacters_CreatesClickableLink()
    {
        // Arrange
        var text = "en.wikipedia.org/wiki/Some%20Page";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().Contain("<a href=\"https://en.wikipedia.org/wiki/Some%20Page\" target=\"_blank\" rel=\"noopener noreferrer\">en.wikipedia.org/wiki/Some%20Page</a>");
    }

    [Fact]
    public void Linkify_MultipleUrls_CreatesMultipleLinks()
    {
        // Arrange
        var text = "Source: https://example.com and also en.wikipedia.org/wiki/Test";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().Contain("<a href=\"https://example.com\" target=\"_blank\" rel=\"noopener noreferrer\">https://example.com</a>");
        result.Value.Should().Contain("<a href=\"https://en.wikipedia.org/wiki/Test\" target=\"_blank\" rel=\"noopener noreferrer\">en.wikipedia.org/wiki/Test</a>");
    }

    [Fact]
    public void Linkify_MaliciousHtml_EscapesHtml()
    {
        // Arrange
        var text = "Source: <script>alert('xss')</script> https://example.com";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().NotContain("<script>");
        result.Value.Should().Contain("&lt;script&gt;");
        result.Value.Should().Contain("<a href=\"https://example.com\" target=\"_blank\" rel=\"noopener noreferrer\">https://example.com</a>");
    }

    [Fact]
    public void Linkify_LineBreaks_PreservesLineBreaks()
    {
        // Arrange
        var text = "Line 1: https://example.com\nLine 2: Another source";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().Contain("<br/>");
    }

    [Fact]
    public void Linkify_UrlInMiddleOfText_CreatesLink()
    {
        // Arrange
        var text = "See details at example.com/path for more information";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().StartWith("See details at ");
        result.Value.Should().Contain("<a href=\"https://example.com/path\" target=\"_blank\" rel=\"noopener noreferrer\">example.com/path</a>");
        result.Value.Should().EndWith(" for more information");
    }

    [Fact]
    public void Linkify_ShortWord_DoesNotCreateLink()
    {
        // Arrange
        var text = "test a.b cd";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().NotContain("<a href=");
        result.Value.Should().Be("test a.b cd");
    }

    [Fact]
    public void Linkify_ComplexUrl_CreatesClickableLink()
    {
        // Arrange
        var text = "https://example.com/path?param=value&other=123#section";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().Contain("<a href=\"https://example.com/path?param=value&amp;other=123#section\"");
    }

    [Fact]
    public void Linkify_RealWorldExample_WikipediaUkrainian()
    {
        // Arrange
        var text = "Вікіпедія uk.wikipedia.org/wiki/%D0%93%D1%80%D0%B0";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().Contain("Вікіпедія ");
        result.Value.Should().Contain("<a href=\"https://uk.wikipedia.org/wiki/%D0%93%D1%80%D0%B0\" target=\"_blank\" rel=\"noopener noreferrer\">uk.wikipedia.org/wiki/%D0%93%D1%80%D0%B0</a>");
    }

    [Fact]
    public void Linkify_LocalhostUrl_IsNotLinkified()
    {
        // Arrange - localhost URLs are not typical in sources and don't have TLDs
        var text = "http://localhost:8080/api/test";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert - localhost URLs without TLD are not linkified
        result.Value.Should().NotContain("<a href=");
        result.Value.Should().Be("http://localhost:8080/api/test");
    }

    [Fact]
    public void Linkify_MarkupString_PreservesEncodedCharacters()
    {
        // Arrange - simulate sanitized HTML from HighlightSanitizer with Ukrainian quotation marks
        var inputHtml = "1. Гейлі А. &#171;Готель&#187;, Харків, &#171;Клуб Сімейного Дозвілля&#187;, 2017<br/>2. https://ru.wikipedia.org/wiki/Матч_с_гробом";
        var sanitizedHtml = new Microsoft.AspNetCore.Components.MarkupString(inputHtml);

        // Act
        var result = SourceLinkifier.Linkify(sanitizedHtml);

        // Assert - encoded characters should NOT be double-encoded
        result.Value.Should().Contain("&#171;Готель&#187;");
        result.Value.Should().NotContain("&amp;#171;");
        
        // Assert - URL should be linkified
        var expectedLink = "<a href=\"https://ru.wikipedia.org/wiki/Матч_с_гробом\" target=\"_blank\" rel=\"noopener noreferrer\">https://ru.wikipedia.org/wiki/Матч_с_гробом</a>";
        result.Value.Should().Contain(expectedLink);
    }

    [Fact]
    public void Linkify_MarkupString_PreservesMarkTags()
    {
        // Arrange - sanitized HTML with search highlights
        var sanitizedHtml = new Microsoft.AspNetCore.Components.MarkupString(
            "Source with <mark>highlighted</mark> text and https://example.com link");

        // Act
        var result = SourceLinkifier.Linkify(sanitizedHtml);

        // Assert - mark tags should be preserved
        result.Value.Should().Contain("<mark>highlighted</mark>");
        result.Value.Should().Contain("<a href=\"https://example.com\" target=\"_blank\" rel=\"noopener noreferrer\">https://example.com</a>");
    }

    [Fact]
    public void Linkify_MarkupString_PreservesLineBreaks()
    {
        // Arrange - sanitized HTML with line breaks (both <br/> and <br>)
        var sanitizedHtml = new Microsoft.AspNetCore.Components.MarkupString(
            "First line<br/>Second line with https://example.com<br>Third line");

        // Act
        var result = SourceLinkifier.Linkify(sanitizedHtml);

        // Assert - line breaks should be preserved
        result.Value.Should().Contain("<br/>");
        result.Value.Should().Contain("First line<br/>Second line");
        result.Value.Should().Contain("Third line");
    }

    [Fact]
    public void Linkify_MarkupString_EmptyString_ReturnsEmpty()
    {
        // Arrange
        var sanitizedHtml = new Microsoft.AspNetCore.Components.MarkupString("");

        // Act
        var result = SourceLinkifier.Linkify(sanitizedHtml);

        // Assert
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public void Linkify_MarkupString_WithMultipleUrls_LinkifiesAll()
    {
        // Arrange - sanitized HTML with multiple URLs
        var sanitizedHtml = new Microsoft.AspNetCore.Components.MarkupString(
            "Visit https://example.com and https://test.org for more info");

        // Act
        var result = SourceLinkifier.Linkify(sanitizedHtml);

        // Assert - both URLs should be linkified
        result.Value.Should().Contain("<a href=\"https://example.com\"");
        result.Value.Should().Contain("<a href=\"https://test.org\"");
    }

    // ==================== Boundary detection ====================

    [Fact]
    public void Linkify_UrlInParenthesesEndingSentence_ExcludesBracketAndPeriod()
    {
        // Arrange - real case from "укр. Simply Red.docx"
        var text = "1. J. Forsyth, «Xueqin and Xakespeare: Reading The Story of the Stone through Hamlet» (https://ibb.co/mVgbHSbP).";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert - neither ")" nor "." belongs to the URL, and both survive outside the link
        result.Value.Should().Contain("<a href=\"https://ibb.co/mVgbHSbP\" target=\"_blank\" rel=\"noopener noreferrer\">https://ibb.co/mVgbHSbP</a>).");
    }

    [Fact]
    public void Linkify_UrlWithBalancedParentheses_KeepsBrackets()
    {
        // Arrange - Wikipedia disambiguation links legitimately end in ")"
        var text = "https://en.wikipedia.org/wiki/Cat_(disambiguation)";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().Contain("<a href=\"https://en.wikipedia.org/wiki/Cat_(disambiguation)\" target=\"_blank\" rel=\"noopener noreferrer\">https://en.wikipedia.org/wiki/Cat_(disambiguation)</a>");
    }

    [Fact]
    public void Linkify_UrlEndingSentence_ExcludesTrailingPeriod()
    {
        // Arrange
        var text = "2. https://en.wikipedia.org/wiki/Simply_Red.";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().Contain(">https://en.wikipedia.org/wiki/Simply_Red</a>.");
        result.Value.Should().NotContain("Simply_Red.\"");
    }

    [Fact]
    public void Linkify_UrlInGuillemets_ExcludesClosingQuote()
    {
        // Arrange
        var text = "«https://example.com/page»";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().Contain("<a href=\"https://example.com/page\" target=\"_blank\" rel=\"noopener noreferrer\">https://example.com/page</a>");
        result.Value.Should().EndWith("&#187;");
    }

    [Fact]
    public void Linkify_BareDomainFollowedByPunctuation_ExcludesPunctuation()
    {
        // Arrange
        var text = "3) www.bbc.co.uk/news; 4) tsn.ua.";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().Contain(">www.bbc.co.uk/news</a>;");
        result.Value.Should().Contain(">tsn.ua</a>.");
    }

    // ==================== List numbering must not be swallowed ====================

    [Fact]
    public void Linkify_NumberedSourceWithoutSpace_ExcludesTheNumber()
    {
        // Arrange - "1." is the source list's numbering, not a subdomain
        var text = "1.www.pravda.com.ua/news/";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().StartWith("1.<a href=\"https://www.pravda.com.ua/news/\"");
        result.Value.Should().Contain(">www.pravda.com.ua/news/</a>");
    }

    [Fact]
    public void Linkify_NumberedSchemeUrlWithoutSpace_ExcludesTheNumber()
    {
        // Arrange - the number must not end up inside the host, nor the scheme inside the path
        var text = "1.http://site.com.ua";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().Be("1.<a href=\"http://site.com.ua\" target=\"_blank\" rel=\"noopener noreferrer\">http://site.com.ua</a>");
    }

    [Fact]
    public void Linkify_HostWithLeadingDigits_IsStillLinkified()
    {
        // Arrange - only an all-digit label is rejected; "24tv" is a real host label
        var text = "1.24tv.ua/sport";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().StartWith("1.<a href=\"https://24tv.ua/sport\"");
    }

    // ==================== False positives in prose ====================

    [Fact]
    public void Linkify_MissingSpaceAfterSentence_DoesNotCreateLink()
    {
        // Arrange - "end.Next" is prose, not a host: "next" is not a known TLD
        var text = "the end.Next sentence starts here";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().Be("the end.Next sentence starts here");
    }

    [Fact]
    public void Linkify_FileName_DoesNotCreateLink()
    {
        // Arrange
        var text = "photo.jpg, Vol.II, Simply Red.docx";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().NotContain("<a href=");
    }

    [Fact]
    public void Linkify_EmailAddress_DoesNotLinkifyDomainPart()
    {
        // Arrange - linking "example.com" out of the middle of an address is worse than no link
        var text = "mail: info@example.com";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().Be("mail: info@example.com");
    }

    // ==================== Encoded (search results) path ====================

    [Fact]
    public void Linkify_MarkupString_UrlFollowedByEncodedGuillemet_ExcludesEntity()
    {
        // Arrange - HighlightSanitizer encodes « and » as numeric entities
        var sanitizedHtml = new Microsoft.AspNetCore.Components.MarkupString(
            "&#171;https://example.com/page&#187;");

        // Act
        var result = SourceLinkifier.Linkify(sanitizedHtml);

        // Assert - the entity is punctuation around the URL, not part of it
        result.Value.Should().Be(
            "&#171;<a href=\"https://example.com/page\" target=\"_blank\" rel=\"noopener noreferrer\">https://example.com/page</a>&#187;");
    }

    [Fact]
    public void Linkify_MarkupString_UrlWithQueryString_DoesNotDoubleEncodeAmpersand()
    {
        // Arrange - the input is already encoded, so "&" arrives as "&amp;"
        var sanitizedHtml = new Microsoft.AspNetCore.Components.MarkupString(
            "https://example.com/s?a=1&amp;b=2");

        // Act
        var result = SourceLinkifier.Linkify(sanitizedHtml);

        // Assert - the href must resolve to "?a=1&b=2", not "?a=1&amp;b=2"
        result.Value.Should().Contain("<a href=\"https://example.com/s?a=1&amp;b=2\"");
        result.Value.Should().NotContain("&amp;amp;");
    }

    [Fact]
    public void Linkify_MarkupString_UrlInParenthesesEndingSentence_ExcludesBracketAndPeriod()
    {
        // Arrange
        var sanitizedHtml = new Microsoft.AspNetCore.Components.MarkupString(
            "1. Forsyth (https://ibb.co/mVgbHSbP).");

        // Act
        var result = SourceLinkifier.Linkify(sanitizedHtml);

        // Assert
        result.Value.Should().EndWith(">https://ibb.co/mVgbHSbP</a>).");
    }
}
