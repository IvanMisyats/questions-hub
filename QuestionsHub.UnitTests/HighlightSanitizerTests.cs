using FluentAssertions;
using QuestionsHub.Blazor.Infrastructure.Search;
using Xunit;

namespace QuestionsHub.UnitTests;

public class HighlightSanitizerTests
{
    [Fact]
    public void Sanitize_WithServerHighlights_PreservesMarks()
    {
        var highlighted = "текст з <mark>підсвіткою</mark> тут";

        var result = HighlightSanitizer.Sanitize(highlighted);

        result.Value.Should().Contain("<mark>підсвіткою</mark>");
    }

    [Fact]
    public void Sanitize_WithServerHighlights_EscapesOtherHtml()
    {
        var highlighted = "<script>alert('xss')</script> <mark>safe</mark>";

        var result = HighlightSanitizer.Sanitize(highlighted);

        result.Value.Should().NotContain("<script>");
        result.Value.Should().Contain("<mark>safe</mark>");
    }

    [Fact]
    public void Sanitize_NoHighlights_FallbackHighlightsPrefix()
    {
        var original = "Знайшли сепульки та сепулькарії";
        var noHighlights = "Знайшли сепульки та сепулькарії";

        var result = HighlightSanitizer.Sanitize(noHighlights, original, "сепул");

        result.Value.Should().Contain("<mark>сепул</mark>ьки");
        result.Value.Should().Contain("<mark>сепул</mark>ькарії");
    }

    [Fact]
    public void Sanitize_NoHighlights_FallbackHighlightsAccentedText()
    {
        var original = "Університет Ру\u0301прехта у місті";
        var noHighlights = original;

        var result = HighlightSanitizer.Sanitize(noHighlights, original, "Рупрехт");

        result.Value.Should().Contain("<mark>");
    }

    [Fact]
    public void Sanitize_NoHighlights_PrefixMatchesWordStart()
    {
        var original = "Гайдельберг — старовинне місто";
        var noHighlights = original;

        var result = HighlightSanitizer.Sanitize(noHighlights, original, "Гайдельб");

        result.Value.Should().Contain("<mark>Гайдельб</mark>ерг");
    }

    [Fact]
    public void Sanitize_Null_ReturnsEmptyMarkup()
    {
        var result = HighlightSanitizer.Sanitize(null);

        result.Value.Should().BeEmpty();
    }

    [Fact]
    public void Sanitize_NoSearchQuery_ReturnsEscapedText()
    {
        var text = "plain text <b>not bold</b>";

        var result = HighlightSanitizer.Sanitize(text);

        result.Value.Should().NotContain("<b>");
        result.Value.Should().Contain("&lt;b&gt;");
    }

    [Fact]
    public void Sanitize_PreservesLineBreaks()
    {
        var text = "line1\nline2";

        var result = HighlightSanitizer.Sanitize(text);

        result.Value.Should().Contain("<br/>");
    }

    [Fact]
    public void Sanitize_MergesAdjacentMarksAtApostrophe()
    {
        var highlighted = "<mark>п</mark>'<mark>яний</mark> текст";

        var result = HighlightSanitizer.Sanitize(highlighted);

        result.Value.Should().Contain("<mark>п&#39;яний</mark>");
        result.Value.Should().NotContain("</mark>&#39;<mark>");
    }

    [Fact]
    public void Sanitize_ClientSideFallback_HighlightsApostropheWord()
    {
        // Stored text uses U+02BC, search query uses U+0027 (keyboard apostrophe)
        var original = "текст п\u02BCяний кіт";
        var noHighlights = original;

        var result = HighlightSanitizer.Sanitize(noHighlights, original, "п'яний");

        result.Value.Should().Contain("<mark>");
    }

    [Fact]
    public void HasHighlights_WithMarks_ReturnsTrue()
    {
        HighlightSanitizer.HasHighlights("text <mark>word</mark>").Should().BeTrue();
    }

    [Fact]
    public void HasHighlights_WithoutMarks_ReturnsFalse()
    {
        HighlightSanitizer.HasHighlights("plain text").Should().BeFalse();
    }

    [Fact]
    public void HasHighlights_Null_ReturnsFalse()
    {
        HighlightSanitizer.HasHighlights(null).Should().BeFalse();
    }

    [Fact]
    public void HasHighlights_Empty_ReturnsFalse()
    {
        HighlightSanitizer.HasHighlights("").Should().BeFalse();
    }
}
