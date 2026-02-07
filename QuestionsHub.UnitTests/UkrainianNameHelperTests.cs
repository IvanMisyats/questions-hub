using FluentAssertions;
using QuestionsHub.Blazor.Utils;
using Xunit;

namespace QuestionsHub.UnitTests;

/// <summary>
/// Tests for UkrainianNameHelper: genitive → nominative conversion and author splitting.
/// </summary>
public class UkrainianNameHelperTests
{
    #region ConvertToNominative — Single Word

    [Theory]
    // Male first names: -а → remove (consonant stem)
    [InlineData("Станіслава", "Станіслав")]
    [InlineData("Едуарда", "Едуард")]
    [InlineData("Антона", "Антон")]
    [InlineData("Єгора", "Єгор")]
    // Male first names: -ія → -ій
    [InlineData("Сергія", "Сергій")]
    [InlineData("Андрія", "Андрій")]
    [InlineData("Олексія", "Олексій")]
    [InlineData("Дмитрія", "Дмитрій")]
    // Male first names: -ря → remove я (Ігоря → Ігор)
    [InlineData("Ігоря", "Ігор")]
    // Female names: -ії → -ія
    [InlineData("Наталії", "Наталія")]
    [InlineData("Вікторії", "Вікторія")]
    // Female names: -ини → -ина, -ени → -ена
    [InlineData("Катерини", "Катерина")]
    [InlineData("Олени", "Олена")]
    [InlineData("Ірини", "Ірина")]
    public void ConvertToNominative_SingleWord_ConvertsCorrectly(string genitive, string expected)
    {
        UkrainianNameHelper.ConvertToNominative(genitive).Should().Be(expected);
    }

    #endregion

    #region ConvertToNominative — Last Names

    [Theory]
    // Last names: -а → remove (consonant stem)
    [InlineData("Мерляна", "Мерлян")]
    [InlineData("Голуба", "Голуб")]
    [InlineData("Купермана", "Куперман")]
    [InlineData("Грищука", "Грищук")]
    // Last names: -ова → -ов pattern (via -а → remove rule)
    [InlineData("Моісєєва", "Моісєєв")]
    [InlineData("Сільвестрова", "Сільвестров")]
    // Last names: -ві → -ва
    [InlineData("Реви", "Рева")]
    // Adjectival surnames: -ського → -ський
    [InlineData("Николаєвського", "Николаєвський")]
    public void ConvertToNominative_LastNames_ConvertsCorrectly(string genitive, string expected)
    {
        UkrainianNameHelper.ConvertLastNameToNominative(genitive).Should().Be(expected);
    }

    #endregion

    #region ConvertFullNameToNominative

    [Theory]
    [InlineData("Станіслава Мерляна", "Станіслав Мерлян")]
    [InlineData("Едуарда Голуба", "Едуард Голуб")]
    [InlineData("Антона Моісєєва", "Антон Моісєєв")]
    [InlineData("Антона Купермана", "Антон Куперман")]
    [InlineData("Олексія Сільвестрова", "Олексій Сільвестров")]
    [InlineData("Сергія Реви", "Сергій Рева")]
    [InlineData("Андрія Грищука", "Андрій Грищук")]
    [InlineData("Ігоря Андрєєва", "Ігор Андрєєв")]
    public void ConvertFullNameToNominative_ConvertsCorrectly(string genitive, string expected)
    {
        UkrainianNameHelper.ConvertFullNameToNominative(genitive).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ConvertFullNameToNominative_EmptyOrWhitespace_ReturnsAsIs(string input)
    {
        UkrainianNameHelper.ConvertFullNameToNominative(input).Should().Be(input);
    }

    #endregion

    #region SplitAndNormalizeAuthors

    [Fact]
    public void SplitAndNormalizeAuthors_WithRedakciya_SplitsAndConverts()
    {
        var result = UkrainianNameHelper.SplitAndNormalizeAuthors(
            "Станіслав Мерлян (Одеса) у редакції Едуарда Голуба (Київ)");

        result.Should().HaveCount(2);
        result[0].Should().Be("Станіслав Мерлян");
        result[1].Should().Be("Едуард Голуб");
    }

    [Fact]
    public void SplitAndNormalizeAuthors_WithVRedakciya_SplitsAndConverts()
    {
        var result = UkrainianNameHelper.SplitAndNormalizeAuthors(
            "Антон Куперман (Одеса) в редакції Сергія Реви (Харків)");

        result.Should().HaveCount(2);
        result[0].Should().Be("Антон Куперман");
        result[1].Should().Be("Сергій Рева");
    }

    [Fact]
    public void SplitAndNormalizeAuthors_WithZaIdeyeyu_SplitsAndConverts()
    {
        var result = UkrainianNameHelper.SplitAndNormalizeAuthors(
            "Олексій Сільвестров (Київ) за ідеєю Андрія Грищука (Львів)");

        result.Should().HaveCount(2);
        result[0].Should().Be("Олексій Сільвестров");
        result[1].Should().Be("Андрій Грищук");
    }

    [Fact]
    public void SplitAndNormalizeAuthors_SimpleAuthorNoCity_ReturnsSingle()
    {
        var result = UkrainianNameHelper.SplitAndNormalizeAuthors("Едуард Голуб");

        result.Should().HaveCount(1);
        result[0].Should().Be("Едуард Голуб");
    }

    [Fact]
    public void SplitAndNormalizeAuthors_SimpleAuthorWithCity_StripsCity()
    {
        var result = UkrainianNameHelper.SplitAndNormalizeAuthors("Едуард Голуб (Київ)");

        result.Should().HaveCount(1);
        result[0].Should().Be("Едуард Голуб");
    }

    [Fact]
    public void SplitAndNormalizeAuthors_CityWithDash_StripsCity()
    {
        var result = UkrainianNameHelper.SplitAndNormalizeAuthors("Антон Моісєєв (Харків - Берлін)");

        result.Should().HaveCount(1);
        result[0].Should().Be("Антон Моісєєв");
    }

    #endregion

    #region StripCity

    [Theory]
    [InlineData("Іван Петренко (Київ)", "Іван Петренко")]
    [InlineData("Іван Петренко (Київ - Львів)", "Іван Петренко")]
    [InlineData("Іван Петренко", "Іван Петренко")]
    [InlineData("(Київ) Іван Петренко", "Іван Петренко")]
    public void StripCity_RemovesCityParentheses(string input, string expected)
    {
        UkrainianNameHelper.StripCity(input).Should().Be(expected);
    }

    #endregion
}
