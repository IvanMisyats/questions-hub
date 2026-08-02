using FluentAssertions;
using QuestionsHub.Blazor.Domain;
using Xunit;

namespace QuestionsHub.UnitTests;

public class ShvagerValuesTests
{
    [Theory]
    [InlineData("10-30")]
    [InlineData("40-50")]
    [InlineData("10 - 30")]
    public void IsPinned_RangeValue_True(string number) =>
        ShvagerValues.IsPinned(number).Should().BeTrue();

    [Theory]
    [InlineData("10")]
    [InlineData("50")]
    [InlineData("0")]      // freshly created questions arrive with «0» — a placeholder, not a pin
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsPinned_PositionalOrBlankValue_False(string? number) =>
        ShvagerValues.IsPinned(number).Should().BeFalse();

    [Fact]
    public void IsReserveTheme_AllValuesRanges_True() =>
        ShvagerValues.IsReserveTheme(["10-30", "40-50"]).Should().BeTrue();

    [Fact]
    public void IsReserveTheme_OneRangeAmongPlainValues_True() =>
        ShvagerValues.IsReserveTheme(["10-20", "30", "40-50"]).Should().BeTrue();

    [Fact]
    public void IsReserveTheme_AllValuesPlain_False() =>
        ShvagerValues.IsReserveTheme(["10", "20", "40"]).Should().BeFalse();

    [Fact]
    public void IsReserveTheme_NoQuestions_False()
    {
        // An empty theme is a parse defect, not a reserve theme — the caller must still warn.
        ShvagerValues.IsReserveTheme([]).Should().BeFalse();
    }
}
