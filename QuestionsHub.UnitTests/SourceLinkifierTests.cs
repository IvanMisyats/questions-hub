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
    public void Linkify_TextWithMarkTags_PreservesHighlights()
    {
        // Arrange - mark tags within the URL text (not breaking the URL structure)
        var text = "Source: example.com/<mark>page</mark>";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert
        result.Value.Should().Contain("<mark>page</mark>");
        result.Value.Should().Contain("<a href=");
    }

    [Fact]
    public void Linkify_MarkTagsBreakingUrl_DoesNotLinkify()
    {
        // Arrange - mark tags break the URL structure
        var text = "Source: <mark>example</mark>.com/page";

        // Act
        var result = SourceLinkifier.Linkify(text);

        // Assert - URL is broken by mark tags, so it shouldn't be linkified
        result.Value.Should().Contain("<mark>example</mark>");
        result.Value.Should().NotContain("<a href=");
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
        // Arrange - simulate sanitized HTML from HighlightSanitizer
        var sanitizedHtml = new Microsoft.AspNetCore.Components.MarkupString(
            "1. Гейлі А. &#171;Готель&#187;, Харків, &#171;Клуб Сімейного Дозвілля&#187;, 2017<br/>2. https://ru.wikipedia.org/wiki/Матч_с_гробом");

        // Act
        var result = SourceLinkifier.Linkify(sanitizedHtml);

        // Assert - encoded characters should NOT be double-encoded
        result.Value.Should().Contain("&#171;Готель&#187;");
        result.Value.Should().NotContain("&amp;#171;");
        result.Value.Should().Contain("<a href=\"https://ru.wikipedia.org/wiki/Матч_с_гробом\" target=\"_blank\" rel=\"noopener noreferrer\">https://ru.wikipedia.org/wiki/Матч_с_гробом</a>");
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
        // Arrange - sanitized HTML with line breaks
        var sanitizedHtml = new Microsoft.AspNetCore.Components.MarkupString(
            "First line<br/>Second line with https://example.com");

        // Act
        var result = SourceLinkifier.Linkify(sanitizedHtml);

        // Assert - line breaks should be preserved
        result.Value.Should().Contain("<br/>");
        result.Value.Should().Contain("First line<br/>Second line");
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
}
