using FluentAssertions;
using QuestionsHub.Blazor.Utils;
using Xunit;

namespace QuestionsHub.UnitTests;

public class TextNormalizerTests
{
    #region NormalizeApostrophes

    [Fact]
    public void NormalizeApostrophes_ReturnsNull_WhenInputIsNull()
    {
        var result = TextNormalizer.NormalizeApostrophes(null);
        result.Should().BeNull();
    }

    [Fact]
    public void NormalizeApostrophes_ReturnsEmpty_WhenInputIsEmpty()
    {
        var result = TextNormalizer.NormalizeApostrophes("");
        result.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeApostrophes_PreservesTextWithoutApostrophes()
    {
        var result = TextNormalizer.NormalizeApostrophes("Текст без апострофів");
        result.Should().Be("Текст без апострофів");
    }

    [Fact]
    public void NormalizeApostrophes_ReplacesAsciiApostrophe()
    {
        // U+0027 (ASCII apostrophe)
        var result = TextNormalizer.NormalizeApostrophes("м'яч");
        result.Should().Be("мʼяч");
    }

    [Fact]
    public void NormalizeApostrophes_ReplacesRightSingleQuotationMark()
    {
        // U+2019 (Right Single Quotation Mark)
        var result = TextNormalizer.NormalizeApostrophes("м'яч");
        result.Should().Be("мʼяч");
    }

    [Fact]
    public void NormalizeApostrophes_ReplacesModifierLetterVerticalLine()
    {
        // U+02C8 (Modifier Letter Vertical Line)
        var result = TextNormalizer.NormalizeApostrophes("мˈяч");
        result.Should().Be("мʼяч");
    }

    [Fact]
    public void NormalizeApostrophes_PreservesUkrainianApostrophe()
    {
        // U+02BC should remain unchanged
        var result = TextNormalizer.NormalizeApostrophes("мʼяч");
        result.Should().Be("мʼяч");
    }

    [Fact]
    public void NormalizeApostrophes_ReplacesMultipleApostropheTypes()
    {
        // Mix of all types
        var result = TextNormalizer.NormalizeApostrophes("м'яч п'ять об'єкт");
        result.Should().Be("мʼяч пʼять обʼєкт");
    }

    [Fact]
    public void NormalizeApostrophes_HandlesComplexUkrainianText()
    {
        // Input has ASCII apostrophe (U+0027)
        var input = "Сім'я О'Ніла живе в Києві";
        var result = TextNormalizer.NormalizeApostrophes(input);

        // Apostrophe is normalized but case is preserved
        result.Should().NotBeNull();
        result.Should().Contain("\u02BC"); // Contains Ukrainian apostrophe
        result.Should().NotContain("'"); // No ASCII apostrophe
        result!.Length.Should().Be(input.Length); // Same length (only apostrophe replaced)
    }

    #endregion

    #region NormalizeWhitespaceAndDashes

    [Fact]
    public void NormalizeWhitespaceAndDashes_ReturnsNull_WhenInputIsNull()
    {
        var result = TextNormalizer.NormalizeWhitespaceAndDashes(null);
        result.Should().BeNull();
    }

    [Fact]
    public void NormalizeWhitespaceAndDashes_ReplacesNonBreakingSpace()
    {
        var result = TextNormalizer.NormalizeWhitespaceAndDashes("text\u00A0with\u00A0spaces");
        result.Should().Be("text with spaces");
    }

    [Fact]
    public void NormalizeWhitespaceAndDashes_ReplacesEnDash()
    {
        var result = TextNormalizer.NormalizeWhitespaceAndDashes("1–10");
        result.Should().Be("1-10");
    }

    [Fact]
    public void NormalizeWhitespaceAndDashes_ReplacesEmDash()
    {
        var result = TextNormalizer.NormalizeWhitespaceAndDashes("слово—слово");
        result.Should().Be("слово-слово");
    }

    [Fact]
    public void NormalizeWhitespaceAndDashes_TrimsWhitespace()
    {
        var result = TextNormalizer.NormalizeWhitespaceAndDashes("  text  ");
        result.Should().Be("text");
    }

    #endregion

    #region Normalize (full)

    [Fact]
    public void Normalize_ReturnsNull_WhenInputIsNull()
    {
        var result = TextNormalizer.Normalize(null);
        result.Should().BeNull();
    }

    [Fact]
    public void Normalize_AppliesAllNormalizations()
    {
        // Non-breaking space, em-dash, and ASCII apostrophe
        var result = TextNormalizer.Normalize("  м'яч\u00A0—\u00A0гра  ");
        result.Should().Be("мʼяч - гра");
    }

    [Fact]
    public void Normalize_HandlesComplexInput()
    {
        var result = TextNormalizer.Normalize("П'ять\u00A0об'єктів — всі м'які");
        result.Should().Be("Пʼять обʼєктів - всі мʼякі");
    }

    #endregion

    #region NormalizeExcludingApostrophes

    [Fact]
    public void NormalizeExcludingApostrophes_ReturnsNull_WhenInputIsNull()
    {
        var result = TextNormalizer.NormalizeExcludingApostrophes(null);
        result.Should().BeNull();
    }

    [Fact]
    public void NormalizeExcludingApostrophes_PreservesApostrophes()
    {
        // Should NOT replace apostrophes - useful for URLs
        var result = TextNormalizer.NormalizeExcludingApostrophes("https://example.com/page?name=O'Neil");
        result.Should().Be("https://example.com/page?name=O'Neil");
    }

    [Fact]
    public void NormalizeExcludingApostrophes_NormalizesWhitespaceAndDashes()
    {
        var result = TextNormalizer.NormalizeExcludingApostrophes("  text\u00A0—\u00A0url  ");
        result.Should().Be("text - url");
    }

    #endregion
}
