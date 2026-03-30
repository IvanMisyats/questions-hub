using FluentAssertions;
using QuestionsHub.Blazor.Controllers.Api.V1;
using Xunit;
using PackagesCtrl = QuestionsHub.Blazor.Controllers.Api.V1.PackagesController;

namespace QuestionsHub.UnitTests;

public class ApiDtoMappingTests
{
    [Theory]
    [InlineData("1:Іван Петренко|2:Олена Коваленко", 2)]
    [InlineData("1:Іван Петренко", 1)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void ParseAuthors_HandlesVariousInputs(string? input, int expectedCount)
    {
        var result = SearchController.ParseAuthors(input);
        result.Should().HaveCount(expectedCount);
    }

    [Fact]
    public void ParseAuthors_ParsesCorrectly()
    {
        var result = SearchController.ParseAuthors("42:Іван Петренко|99:Олена Коваленко");

        result.Should().HaveCount(2);
        result[0].Id.Should().Be(42);
        result[0].FirstName.Should().Be("Іван");
        result[0].LastName.Should().Be("Петренко");
        result[1].Id.Should().Be(99);
        result[1].FirstName.Should().Be("Олена");
        result[1].LastName.Should().Be("Коваленко");
    }

    [Fact]
    public void ParseAuthors_SkipsMalformedEntries()
    {
        var result = SearchController.ParseAuthors("1:Valid Name|bad_entry|:no_id|3:");

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(1);
    }

    [Theory]
    [InlineData(null, "https://example.com", null)]
    [InlineData("", "https://example.com", null)]
    [InlineData("/media/image.jpg", "https://example.com", "https://example.com/media/image.jpg")]
    [InlineData("/media/image.jpg", "https://example.com/", "https://example.com/media/image.jpg")]
    [InlineData("https://cdn.example.com/image.jpg", "https://example.com", "https://cdn.example.com/image.jpg")]
    [InlineData("http://other.com/img.png", "https://example.com", "http://other.com/img.png")]
    public void ToAbsoluteUrl_ConvertsCorrectly(string? input, string baseUrl, string? expected)
    {
        var result = PackagesCtrl.ToAbsoluteUrl(input, baseUrl);
        result.Should().Be(expected);
    }
}
