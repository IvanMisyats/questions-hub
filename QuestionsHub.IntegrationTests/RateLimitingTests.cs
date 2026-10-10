using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuestionsHub.Blazor.Infrastructure.Api;
using QuestionsHub.IntegrationTests.Infrastructure;
using Xunit;

namespace QuestionsHub.IntegrationTests;

/// <summary>
/// Two-layer rate limiting over real HTTP: per-IP admission before authentication and per-principal
/// budgets after it (docs/AGENT_API_PLAN.md, "Rate Limiting").
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RateLimitingTests(PostgresFixture database)
{
    private const string GeneralEndpoint = "/api/v1/tags/popular";

    private QuestionsHubAppFactory CreateFactory(params (string Key, int Value)[] limits)
    {
        database.SkipIfUnavailable();

        // Generous defaults so only the limit under test can trip.
        var settings = new Dictionary<string, string?>
        {
            ["RateLimits:IpPublicApiPerMinute"] = "1000",
            ["RateLimits:IpAuthPerMinute"] = "1000",
            ["RateLimits:AppGeneralPerMinute"] = "1000",
            ["RateLimits:AppDetailPerMinute"] = "1000",
            ["RateLimits:AppSearchPerMinute"] = "1000",
        };
        foreach (var (key, value) in limits)
            settings[$"RateLimits:{key}"] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return new QuestionsHubAppFactory(database, settings);
    }

    private static async Task<string> CreateApiKey(QuestionsHubAppFactory factory, string name)
    {
        using var scope = factory.Services.CreateScope();
        var keys = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var (_, rawKey) = await keys.Create(name);
        return rawKey;
    }

    private static async Task<HttpStatusCode> Get(HttpClient client, string url, string? apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (apiKey != null)
            request.Headers.Add(ApiKeyAuthenticationOptions.HeaderName, apiKey);

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [SkippableFact]
    public async Task ApiKeys_HaveSeparateBudgets()
    {
        await using var factory = CreateFactory(("AppGeneralPerMinute", 2));
        var keyA = await CreateApiKey(factory, "A");
        var keyB = await CreateApiKey(factory, "B");
        using var client = factory.CreateClientFrom("203.0.113.10");

        (await Get(client, GeneralEndpoint, keyA)).Should().Be(HttpStatusCode.OK);
        (await Get(client, GeneralEndpoint, keyA)).Should().Be(HttpStatusCode.OK);
        (await Get(client, GeneralEndpoint, keyA)).Should().Be(HttpStatusCode.TooManyRequests);

        // Same IP, different key: a fresh budget (the old limiter was one global bucket).
        (await Get(client, GeneralEndpoint, keyB)).Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Rejection_HasJsonBodyAndRetryAfter()
    {
        await using var factory = CreateFactory(("AppGeneralPerMinute", 1));
        var key = await CreateApiKey(factory, "A");
        using var client = factory.CreateClientFrom("203.0.113.11");
        await Get(client, GeneralEndpoint, key);

        using var request = new HttpRequestMessage(HttpMethod.Get, GeneralEndpoint);
        request.Headers.Add(ApiKeyAuthenticationOptions.HeaderName, key);
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        (await response.Content.ReadAsStringAsync()).Should().Contain("Rate limit exceeded");
    }

    [SkippableFact]
    public async Task ActionBudget_OverridesControllerBudget()
    {
        await using var factory = CreateFactory(("AppDetailPerMinute", 1));
        var key = await CreateApiKey(factory, "A");
        using var client = factory.CreateClientFrom("203.0.113.12");

        (await Get(client, "/api/v1/packages/999999", key)).Should().Be(HttpStatusCode.NotFound);
        (await Get(client, "/api/v1/packages/999999", key)).Should().Be(HttpStatusCode.TooManyRequests);

        // The detail budget is exhausted, the general one (same controller) is not.
        (await Get(client, "/api/v1/packages", key)).Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task InvalidKeys_AreBoundedPerIp()
    {
        await using var factory = CreateFactory(("IpPublicApiPerMinute", 3));
        using var attacker = factory.CreateClientFrom("198.51.100.20");
        using var bystander = factory.CreateClientFrom("198.51.100.21");

        for (var i = 0; i < 3; i++)
        {
            (await Get(attacker, GeneralEndpoint, $"qh_live_{Guid.NewGuid():N}"))
                .Should().Be(HttpStatusCode.Unauthorized);
        }

        // Fresh made-up keys do not buy a fresh budget.
        (await Get(attacker, GeneralEndpoint, $"qh_live_{Guid.NewGuid():N}"))
            .Should().Be(HttpStatusCode.TooManyRequests);
        (await Get(bystander, GeneralEndpoint, $"qh_live_{Guid.NewGuid():N}"))
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AuthLimit_IsPerIp()
    {
        await using var factory = CreateFactory(("IpAuthPerMinute", 2));
        using var first = factory.CreateClientFrom("192.0.2.30");
        using var second = factory.CreateClientFrom("192.0.2.31");

        async Task<HttpStatusCode> Login(HttpClient client)
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["email"] = "nobody@test.local",
                ["password"] = "wrong",
            });
            using var response = await client.PostAsync("/api/Auth/login", form);
            return response.StatusCode;
        }

        (await Login(first)).Should().NotBe(HttpStatusCode.TooManyRequests);
        (await Login(first)).Should().NotBe(HttpStatusCode.TooManyRequests);
        (await Login(first)).Should().Be(HttpStatusCode.TooManyRequests);

        // Was a site-wide bucket: one busy IP locked everybody out of logging in.
        (await Login(second)).Should().NotBe(HttpStatusCode.TooManyRequests);
    }
}
