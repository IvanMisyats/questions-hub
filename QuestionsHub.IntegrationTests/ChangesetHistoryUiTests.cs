using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.Blazor.Infrastructure.AgentApi.Changesets;
using QuestionsHub.IntegrationTests.Infrastructure;
using Xunit;

namespace QuestionsHub.IntegrationTests;

/// <summary>
/// The «Історія змін (агенти)» panel on the package manage page (server-rendered). The diff of a
/// changeset loads on demand in the interactive page; its formatting is unit-tested (ChangeDisplay).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChangesetHistoryUiTests(PostgresFixture database)
{
    private const string PanelTitle = "Історія змін (агенти)";

    private QuestionsHubAppFactory CreateFactory()
    {
        database.SkipIfUnavailable();
        return new QuestionsHubAppFactory(database);
    }

    private static async Task<ClaimsPrincipal> Agent(QuestionsHubAppFactory factory, string userId)
    {
        var raw = await TestData.CreateToken(factory, userId);
        using var scope = factory.Services.CreateScope();
        var validated = await scope.ServiceProvider.GetRequiredService<PersonalAccessTokenService>().Validate(raw);
        return PersonalAccessTokenAuthenticationHandler.CreatePrincipal(validated!);
    }

    private static async Task ApplyChangeset(QuestionsHubAppFactory factory, ClaimsPrincipal agent, int packageId, string summary)
    {
        int questionId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            questionId = db.Questions.Where(q => q.Tour.PackageId == packageId).OrderBy(q => q.Id).Select(q => q.Id).First();
        }

        using var document = JsonDocument.Parse($$"""
            [ { "op": "updateQuestion", "questionId": {{questionId}}, "set": { "text": "Текст від агента" } },
              { "op": "deleteQuestion", "questionId": {{questionId}} } ]
            """);
        using var serviceScope = factory.Services.CreateScope();
        var result = await serviceScope.ServiceProvider.GetRequiredService<PackageChangesetService>().Execute(agent, packageId,
            new ChangesetRequest(Guid.NewGuid(), null, summary, false, document.RootElement.Clone()));
        result.Status.Should().Be(ChangesetStatus.Ok, result.Error);
    }

    [SkippableFact]
    public async Task ManagePage_ListsAgentChangesets_ToTheOwnerAndAdmins()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        using var browser = TestBrowser.Create(factory);
        await TestBrowser.Login(browser, editor.Email, editor.Password);

        (await TestBrowser.GetDecodedHtml(browser, $"/manage/package/{package.Id}"))
            .Should().NotContain(PanelTitle, "nothing to show before an agent changed the package");

        await ApplyChangeset(factory, await Agent(factory, editor.Id), package.Id, "Правки з листа");

        (await TestBrowser.GetDecodedHtml(browser, $"/manage/package/{package.Id}"))
            .Should().Contain(PanelTitle)
            .And.Contain("Правки з листа")
            .And.Contain("«test»", "the token name is shown")
            .And.Contain("2 операції")
            .And.Contain("Показати зміни");

        using var admin = TestBrowser.Create(factory);
        await TestBrowser.Login(admin, TestBrowser.AdminEmail, TestBrowser.AdminPassword);
        (await TestBrowser.GetDecodedHtml(admin, $"/manage/package/{package.Id}")).Should().Contain("Правки з листа");
    }

    [SkippableFact]
    public async Task ManagePage_OfSomeoneElsesPackage_ShowsNoHistory()
    {
        await using var factory = CreateFactory();
        var owner = await TestBrowser.CreateUser(factory, "Editor");
        var outsider = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, owner.Id);
        await ApplyChangeset(factory, await Agent(factory, owner.Id), package.Id, "Приватний підсумок");
        using var browser = TestBrowser.Create(factory);
        await TestBrowser.Login(browser, outsider.Email, outsider.Password);

        var html = await TestBrowser.GetDecodedHtml(browser, $"/manage/package/{package.Id}");

        html.Should().NotContain(PanelTitle).And.NotContain("Приватний підсумок");
    }

    [SkippableFact]
    public async Task AgentSuppliedText_IsEncoded()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        await ApplyChangeset(factory, await Agent(factory, editor.Id), package.Id, "<img src=x onerror=alert(1)>");
        using var browser = TestBrowser.Create(factory);
        await TestBrowser.Login(browser, editor.Email, editor.Password);

        var raw = await browser.GetStringAsync($"/manage/package/{package.Id}"); // not decoded on purpose

        raw.Should().NotContain("<img src=x").And.Contain("&lt;img src=x");
    }
}
