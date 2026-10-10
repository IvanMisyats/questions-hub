using FluentAssertions;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using Xunit;

namespace QuestionsHub.UnitTests;

public class PackageIdListTests
{
    [Theory]
    [InlineData("512", new[] { 512 })]
    [InlineData("512, 513", new[] { 512, 513 })]
    [InlineData(" 512;513  514 ", new[] { 512, 513, 514 })]
    [InlineData("#512,#513", new[] { 512, 513 })]
    [InlineData("512,\n513", new[] { 512, 513 })]
    public void Parse_ValidLists(string text, int[] expected)
    {
        PackageIdList.Parse(text).Should().Equal(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ; ")]
    [InlineData("512, abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("12.5")]
    [InlineData("99999999999")]
    public void Parse_InvalidOrEmpty_ReturnsNull(string? text)
    {
        PackageIdList.Parse(text).Should().BeNull();
    }
}
