using FluentAssertions;
using QuestionsHub.Blazor.Infrastructure;
using Xunit;

namespace QuestionsHub.UnitTests;

/// <summary>
/// The footer's protected links: values are written into the page as reversed Base64 and decoded by
/// wwwroot/protected-links.js (atob + reverse), so Protect must stay plain ASCII Base64 of the reversed string.
/// </summary>
public class SiteLinksTests
{
    [Theory]
    [InlineData("admin@questions.com.ua")]
    [InlineData("https://t.me/+abcDEF-123_xyz")]
    public void Protect_RoundTrips_AndHidesTheValue(string value)
    {
        var protectedValue = SiteLinks.Protect(value);

        protectedValue.Should().NotContain(value);
        protectedValue.Should().NotContain("@").And.NotContain("t.me");
        SiteLinks.Unprotect(protectedValue).Should().Be(value);
    }

    [Fact]
    public void Protect_IsBase64OfTheReversedString()
    {
        // "abc" reversed is "cba", whose Base64 is "Y2Jh" — what atob(...).split('').reverse() undoes
        SiteLinks.Protect("abc").Should().Be("Y2Jh");
    }

    [Fact]
    public void DiscussionChat_DecodesToATelegramInviteLink()
    {
        SiteLinks.Unprotect(SiteLinks.DiscussionChatProtected)
            .Should().MatchRegex(@"^https://t\.me/\+[A-Za-z0-9_-]+$");
    }
}
