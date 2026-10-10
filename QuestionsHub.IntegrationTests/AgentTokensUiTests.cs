using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.IntegrationTests.Infrastructure;
using Xunit;

namespace QuestionsHub.IntegrationTests;

/// <summary>
/// Server-rendered (prerendered) token UI: who sees the profile section and whose tokens are listed.
/// Interactive create/revoke go through <see cref="PersonalAccessTokenService"/>, covered by unit tests.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AgentTokensUiTests(PostgresFixture database)
{
    private const string SectionTitle = "Токени доступу для агентів";

    private QuestionsHubAppFactory CreateFactory()
    {
        database.SkipIfUnavailable();
        return new QuestionsHubAppFactory(database);
    }

    private static async Task<string> CreateToken(QuestionsHubAppFactory factory, string userId, string name)
    {
        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<PersonalAccessTokenService>()
            .Create(userId, new CreateTokenRequest(name, TokenScope.Read, 30, null));
        result.Success.Should().BeTrue(result.ErrorMessage);
        return result.RawToken!;
    }

    [SkippableFact]
    public async Task Profile_Editor_SeesSectionWithOwnTokensOnly()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var other = await TestBrowser.CreateUser(factory, "Editor");
        await CreateToken(factory, editor.Id, "Мій агент");
        await CreateToken(factory, other.Id, "Чужий агент");
        using var browser = TestBrowser.Create(factory);
        await TestBrowser.Login(browser, editor.Email, editor.Password);

        var html = await TestBrowser.GetDecodedHtml(browser, "/Account/Profile");

        html.Should().Contain(SectionTitle)
            .And.Contain("Мій агент")
            .And.NotContain("Чужий агент")
            .And.Contain("Усі мої пакети")
            .And.NotContain("Усі пакети сайту");
    }

    [SkippableFact]
    public async Task Profile_PlainUser_DoesNotSeeSection()
    {
        await using var factory = CreateFactory();
        var user = await TestBrowser.CreateUser(factory, role: null);
        using var browser = TestBrowser.Create(factory);
        await TestBrowser.Login(browser, user.Email, user.Password);

        var html = await TestBrowser.GetDecodedHtml(browser, "/Account/Profile");

        html.Should().Contain("Редагувати профіль").And.NotContain(SectionTitle);
    }

    [SkippableFact]
    public async Task Profile_Admin_IsOfferedAllSitePackages()
    {
        await using var factory = CreateFactory();
        using var browser = TestBrowser.Create(factory);
        await TestBrowser.Login(browser, TestBrowser.AdminEmail, TestBrowser.AdminPassword);

        var html = await TestBrowser.GetDecodedHtml(browser, "/Account/Profile");

        html.Should().Contain(SectionTitle).And.Contain("Усі пакети сайту");
    }

    [SkippableFact]
    public async Task AdminPage_ListsEveryUsersTokens()
    {
        await using var factory = CreateFactory();
        var first = await TestBrowser.CreateUser(factory, "Editor");
        var second = await TestBrowser.CreateUser(factory, "Editor");
        await CreateToken(factory, first.Id, "Агент першого");
        await CreateToken(factory, second.Id, "Агент другого");
        using var browser = TestBrowser.Create(factory);
        await TestBrowser.Login(browser, TestBrowser.AdminEmail, TestBrowser.AdminPassword);

        var html = await TestBrowser.GetDecodedHtml(browser, "/admin/api-keys");

        html.Should().Contain("Токени агентів").And.Contain("Агент першого").And.Contain("Агент другого");
    }

    [SkippableFact]
    public async Task AdminPage_IsDeniedToEditors()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        using var browser = TestBrowser.Create(factory);
        await TestBrowser.Login(browser, editor.Email, editor.Password);

        using var response = await browser.GetAsync("/admin/api-keys");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Account/AccessDenied");
    }

    [SkippableFact]
    public async Task Profile_EncodesNames_AndNeverShowsSecretsOrHashes()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        const string hostileName = "<script>alert(1)</script>";
        var raw = await CreateToken(factory, editor.Id, hostileName);
        using var browser = TestBrowser.Create(factory);
        await TestBrowser.Login(browser, editor.Email, editor.Password);

        var html = await browser.GetStringAsync("/Account/Profile"); // not decoded on purpose

        html.Should().NotContain(hostileName)
            .And.NotContain(raw)
            .And.NotContain(PersonalAccessTokenService.HashToken(raw))
            .And.Contain(raw[..16], "the prefix identifies the token in the list");
    }
}
