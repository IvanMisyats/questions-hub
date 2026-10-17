using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Infrastructure.RateLimiting;
using Xunit;

namespace QuestionsHub.UnitTests;

public sealed class WindowedRateLimitTests
{
    [Theory]
    [InlineData(RateLimitBudgets.AgentImport, 3600)]
    [InlineData(RateLimitBudgets.AgentRead, 60)]
    public async Task Budget_WindowAndRejectionHintAgree(string budget, int seconds)
    {
        var window = RateLimitBudgets.WindowFor(budget);
        window.Should().Be(TimeSpan.FromSeconds(seconds));
        var settings = RateLimitWindows.Create(1, window);
        settings.Window.Should().Be(window);
        settings.SegmentsPerWindow.Should().Be(6);
        using var limiter = new ClientRateLimiter(Options.Create(new RateLimitSettings { AgentImportPerHour = 1, AgentReadPerMinute = 1 }));
        using var first = limiter.Acquire(budget, "one");
        using var denied = limiter.Acquire(budget, "one");
        using var other = limiter.Acquire(budget, "two");
        first.IsAcquired.Should().BeTrue();
        denied.IsAcquired.Should().BeFalse();
        other.IsAcquired.Should().BeTrue();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await RateLimitingExtensions.WriteRejection(context, denied, default, window);
        context.Response.StatusCode.Should().Be(429);
        context.Response.Headers.RetryAfter.ToString().Should().Be(seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ImportBudget_UsesUserClaim()
    {
        RateLimitBudgets.ClaimTypeFor(RateLimitBudgets.AgentImport).Should().Be(ClaimTypes.NameIdentifier);
    }
}
