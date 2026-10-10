using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using QuestionsHub.Blazor.Controllers.Api.Manage;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.Blazor.Infrastructure.Api;
using QuestionsHub.IntegrationTests.Infrastructure;
using Xunit;

namespace QuestionsHub.IntegrationTests;

/// <summary>
/// The agent API accepts personal access tokens only: no cookies, no API keys, current user rights.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AgentAuthTests(PostgresFixture database)
{
    private const string MeUrl = "/api/v1/manage/me";
    private const string AdminEmail = TestBrowser.AdminEmail;
    private const string AdminPassword = TestBrowser.AdminPassword;

    private QuestionsHubAppFactory CreateFactory(params (string Key, int Value)[] limits)
    {
        database.SkipIfUnavailable();
        var settings = limits.ToDictionary(
            l => $"RateLimits:{l.Key}",
            l => (string?)l.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new QuestionsHubAppFactory(database, settings);
    }

    private static HttpClient CreateBrowser(QuestionsHubAppFactory factory) => TestBrowser.Create(factory);

    private static async Task<string> CreateUser(QuestionsHubAppFactory factory, string role) =>
        (await TestBrowser.CreateUser(factory, role)).Id;

    private static async Task<string> AdminId(QuestionsHubAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.FindByEmailAsync(AdminEmail))!.Id;
    }

    private static async Task<(int Id, string Raw)> CreateToken(
        QuestionsHubAppFactory factory, string userId, TokenScope scope = TokenScope.Read)
    {
        using var serviceScope = factory.Services.CreateScope();
        var tokens = serviceScope.ServiceProvider.GetRequiredService<PersonalAccessTokenService>();
        var result = await tokens.Create(userId, new CreateTokenRequest("test", scope, 30, null));
        result.Success.Should().BeTrue(result.ErrorMessage);
        return (result.Token!.Id, result.RawToken!);
    }

    private static async Task<HttpResponseMessage> GetMe(HttpClient client, string? token = null, string? apiKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MeUrl);
        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (apiKey != null)
            request.Headers.Add(ApiKeyAuthenticationOptions.HeaderName, apiKey);
        return await client.SendAsync(request);
    }

    /// <summary>Logs in as the seeded admin and proves the session cookie authenticates cookie endpoints.</summary>
    private static async Task LoginAsAdmin(HttpClient browser)
    {
        await TestBrowser.Login(browser, AdminEmail, AdminPassword);

        // An editor-only export of a missing package is 404 for the signed-in admin
        // (an anonymous caller is redirected to login).
        using var export = await browser.GetAsync("/api/packages/999999/export");
        export.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<HttpResponseMessage> PostWriteProbe(HttpClient client, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__test/agent/write");
        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    [SkippableFact]
    public async Task NoToken_Returns401WithBearerChallenge()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await GetMe(client);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("Bearer");
        (await response.Content.ReadAsStringAsync()).Should().Contain("qh_pat_");
    }

    [SkippableFact]
    public async Task ValidToken_ReturnsUserAndToken()
    {
        await using var factory = CreateFactory();
        var editorId = await CreateUser(factory, "Editor");
        var (tokenId, raw) = await CreateToken(factory, editorId, TokenScope.ReadWrite);
        using var client = factory.CreateClient();

        using var response = await GetMe(client, raw);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<AgentMeResponse>();
        me!.User.Id.Should().Be(editorId);
        me.User.Roles.Should().Equal("Editor");
        me.Token.Id.Should().Be(tokenId);
        me.Token.Scope.Should().Be("readWrite");
        me.Token.PackageIds.Should().BeNull();
    }

    [SkippableFact]
    public async Task RevokedToken_Returns401_OnTheNextRequest()
    {
        await using var factory = CreateFactory();
        var editorId = await CreateUser(factory, "Editor");
        var (tokenId, raw) = await CreateToken(factory, editorId);
        using var client = factory.CreateClient();
        (await GetMe(client, raw)).StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PersonalAccessTokenService>()
                .Revoke(tokenId, editorId);
        }

        (await GetMe(client, raw)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AdminCookieAlone_IsNotAccepted()
    {
        await using var factory = CreateFactory();
        using var browser = CreateBrowser(factory);
        await LoginAsAdmin(browser);

        (await GetMe(browser)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AdminCookie_PlusInvalidToken_Returns401()
    {
        await using var factory = CreateFactory();
        using var browser = CreateBrowser(factory);
        await LoginAsAdmin(browser);

        (await GetMe(browser, $"qh_pat_{Guid.NewGuid():N}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AdminCookie_PlusEditorToken_ActsAsTheEditorOnly()
    {
        await using var factory = CreateFactory();
        var editorId = await CreateUser(factory, "Editor");
        var (_, raw) = await CreateToken(factory, editorId);
        using var browser = CreateBrowser(factory);
        await LoginAsAdmin(browser);

        using var response = await GetMe(browser, raw);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<AgentMeResponse>();
        me!.User.Id.Should().Be(editorId);
        me.User.Roles.Should().NotContain("Admin");
    }

    [SkippableFact]
    public async Task ApiKey_IsNotAccepted()
    {
        await using var factory = CreateFactory();
        string apiKey;
        using (var scope = factory.Services.CreateScope())
            (_, apiKey) = await scope.ServiceProvider.GetRequiredService<ApiKeyService>().Create("app");
        using var client = factory.CreateClient();

        (await GetMe(client, apiKey: apiKey)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task WritePolicy_ReadToken_Returns403()
    {
        await using var factory = CreateFactory();
        var (_, raw) = await CreateToken(factory, await CreateUser(factory, "Editor"), TokenScope.Read);
        using var client = factory.CreateClient();

        using var response = await PostWriteProbe(client, raw);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("scope");
    }

    [SkippableFact]
    public async Task WritePolicy_ReadWriteToken_IsAllowed()
    {
        await using var factory = CreateFactory();
        var editorId = await CreateUser(factory, "Editor");
        var (_, raw) = await CreateToken(factory, editorId, TokenScope.ReadWrite);
        using var client = factory.CreateClient();

        using var response = await PostWriteProbe(client, raw);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain(editorId);
    }

    [SkippableFact]
    public async Task WritePolicy_AdminCookieAlone_Returns401()
    {
        await using var factory = CreateFactory();
        using var browser = CreateBrowser(factory);
        await LoginAsAdmin(browser);

        (await PostWriteProbe(browser)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AdminToken_ReportsAdminRole()
    {
        await using var factory = CreateFactory();
        var (_, raw) = await CreateToken(factory, await AdminId(factory));
        using var client = factory.CreateClient();

        var me = await (await GetMe(client, raw)).Content.ReadFromJsonAsync<AgentMeResponse>();

        me!.User.Roles.Should().Contain("Admin");
    }

    [SkippableFact]
    public async Task AgentReadBudget_IsPerToken()
    {
        await using var factory = CreateFactory(("AgentReadPerMinute", 2));
        var editorId = await CreateUser(factory, "Editor");
        var (_, first) = await CreateToken(factory, editorId);
        var (_, second) = await CreateToken(factory, editorId);
        using var client = factory.CreateClientFrom("203.0.113.40");

        (await GetMe(client, first)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetMe(client, first)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetMe(client, first)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await GetMe(client, second)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
