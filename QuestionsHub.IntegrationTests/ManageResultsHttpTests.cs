using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using QuestionsHub.Blazor.Controllers.Api.Manage;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.IntegrationTests.Infrastructure;
using Xunit;

namespace QuestionsHub.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ManageResultsHttpTests(PostgresFixture database)
{
    [SkippableFact]
    public async Task ResultsRoutes_EnforceScopeAndAllowlist_AndReturnExpectedStatuses()
    {
        database.SkipIfUnavailable();
        await using var factory = new QuestionsHubAppFactory(database);
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        var other = await TestData.CreatePackage(factory, editor.Id);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await TestData.CreateToken(factory, editor.Id, packageIds: [package.Id]));
        var path = $"/api/v1/manage/packages/{package.Id}/results";

        using var outside = await client.GetAsync($"/api/v1/manage/packages/{other.Id}/results");
        outside.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var invalid = await client.PostAsJsonAsync(path, new { url = "invalid" });
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var attached = await client.PostAsJsonAsync(path, new { url = "https://example.com/results" });
        attached.StatusCode.Should().Be(HttpStatusCode.Created);
        var source = (await attached.Content.ReadFromJsonAsync<ManageResultsSourceDto>())!;
        source.Platform.Should().Be("other");
        source.Warnings.Should().BeEmpty();
        var list = await client.GetFromJsonAsync<ManageResultsSourcesResponse>(path);
        list!.Sources.Should().ContainSingle();
        using var reload = await client.PostAsync($"{path}/{source.Id}/reload", null);
        reload.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await TestData.CreateToken(factory, editor.Id, TokenScope.Read));
        using var deniedAttach = await client.PostAsJsonAsync(path, new { url = "https://example.com/other" });
        using var deniedReload = await client.PostAsync($"{path}/{source.Id}/reload", null);
        using var deniedDelete = await client.DeleteAsync($"{path}/{source.Id}");
        deniedAttach.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        deniedReload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        deniedDelete.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await TestData.CreateToken(factory, editor.Id));
        using var deleted = await client.DeleteAsync($"{path}/{source.Id}");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var missing = await client.DeleteAsync($"{path}/{source.Id}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
