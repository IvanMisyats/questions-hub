using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuestionsHub.Blazor.Controllers.Api.Manage;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.AgentApi.Changesets;
using QuestionsHub.IntegrationTests.Infrastructure;
using Xunit;

namespace QuestionsHub.IntegrationTests;

/// <summary>The changeset endpoints over HTTP: the agent workflow and the error contract.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class ManageChangesetsHttpTests(PostgresFixture database)
{
    private QuestionsHubAppFactory CreateFactory(params (string Key, int Value)[] limits)
    {
        database.SkipIfUnavailable();
        var settings = limits.ToDictionary(
            l => $"RateLimits:{l.Key}",
            l => (string?)l.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new QuestionsHubAppFactory(database, settings);
    }

    private static HttpClient Client(QuestionsHubAppFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, int packageId, string json) =>
        client.PostAsync($"/api/v1/manage/packages/{packageId}/changesets", new StringContent(json, Encoding.UTF8, "application/json"));

    private static async Task<int> FirstQuestionId(QuestionsHubAppFactory factory, int packageId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        return await db.Questions.Where(q => q.Tour.PackageId == packageId && q.Tour.OrderIndex == 0)
            .OrderBy(q => q.OrderIndex).Select(q => q.Id).FirstAsync();
    }

    private static async Task<JsonNode> Json(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    [SkippableFact]
    public async Task AgentWorkflow_Read_Preview_Apply_Retry_History()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        using var client = Client(factory, await TestData.CreateToken(factory, editor.Id, packageIds: [package.Id]));
        var questionId = await FirstQuestionId(factory, package.Id);

        // 1. Read the package and its version
        var read = await client.GetFromJsonAsync<ManagePackageDto>($"/api/v1/manage/packages/{package.Id}");

        // 2. Preview
        var operations = $$"""[ { "op": "updateQuestion", "questionId": {{questionId}}, "set": { "text": "Виправлено агентом" } } ]""";
        using var preview = await Post(client, package.Id, $$"""{ "dryRun": true, "expectedVersion": "{{read!.Version}}", "operations": {{operations}} }""");
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewBody = await Json(preview);
        previewBody["dryRun"]!.GetValue<bool>().Should().BeTrue();
        previewBody.AsObject().ContainsKey("changesetId").Should().BeTrue("properties are always present");
        previewBody["changesetId"].Should().BeNull();
        previewBody["versionAfter"].Should().BeNull();
        previewBody["changes"]!.AsArray().Single()!.AsObject().ContainsKey("snapshot").Should().BeTrue();
        previewBody["changes"]!.AsArray().Single()!["after"]!.GetValue<string>().Should().Be("Виправлено агентом");

        // 3. Apply (same version, a request id for safe retries)
        var requestId = Guid.NewGuid();
        var applyJson = $$"""{ "requestId": "{{requestId}}", "expectedVersion": "{{read.Version}}", "summary": "Лист редактора", "operations": {{operations}} }""";
        using var apply = await Post(client, package.Id, applyJson);
        apply.StatusCode.Should().Be(HttpStatusCode.OK);
        var applied = await apply.Content.ReadFromJsonAsync<ChangesetResponse>();
        applied!.ChangesetId.Should().NotBeNull();
        applied.Replayed.Should().BeFalse();

        // 4. A retried request (e.g. after a timeout) returns the same result, nothing is applied twice
        using var retry = await Post(client, package.Id, applyJson);
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        (await retry.Content.ReadFromJsonAsync<ChangesetResponse>())!.Replayed.Should().BeTrue();

        // 5. The package reflects it; the version moved on
        var after = await client.GetFromJsonAsync<ManagePackageDto>($"/api/v1/manage/packages/{package.Id}");
        after!.Tours[0].Questions[0].Text.Should().Be("Виправлено агентом");
        after.Version.Should().Be(applied.VersionAfter);

        // 6. History
        var history = await client.GetFromJsonAsync<ChangesetHistoryPage>($"/api/v1/manage/packages/{package.Id}/changesets");
        history!.Changesets.Should().ContainSingle().Which.Summary.Should().Be("Лист редактора");
        var detail = await client.GetFromJsonAsync<ChangesetDetailDto>(
            $"/api/v1/manage/packages/{package.Id}/changesets/{applied.ChangesetId}");
        detail!.Changes.Should().ContainSingle(c => c.Field == "text");
        detail.Operations.GetArrayLength().Should().Be(1);
    }

    [SkippableFact]
    public async Task ErrorContract()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var other = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        var foreign = await TestData.CreatePackage(factory, other.Id);
        using var client = Client(factory, await TestData.CreateToken(factory, editor.Id));
        using var reader = Client(factory, await TestData.CreateToken(factory, editor.Id, TokenScope.Read));
        var questionId = await FirstQuestionId(factory, package.Id);
        var valid = $$"""{ "requestId": "{{Guid.NewGuid()}}", "operations": [ { "op": "updateQuestion", "questionId": {{questionId}}, "set": { "comment": "x" } } ] }""";

        // Read-only token → 403; no token → 401
        (await Post(reader, package.Id, valid)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.CreateClient().PostAsync($"/api/v1/manage/packages/{package.Id}/changesets",
            new StringContent(valid, Encoding.UTF8, "application/json"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Request-level problems → 400
        (await Post(client, package.Id, """{ "operations": [ { "op": "setSharedEditors", "value": true } ] }""")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "apply needs a requestId");
        (await Post(client, package.Id, """{ "requestId": "6f1c4b6e-0d3a-4b8e-9d2f-1a2b3c4d5e6f", "operations": [] }""")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        (await Post(client, package.Id, """{ "operations": [ """)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Operation problems → 422 with the index
        using var invalid = await Post(client, package.Id, $$"""
            { "dryRun": true, "operations": [
                { "op": "updateQuestion", "questionId": {{questionId}}, "set": { "comment": "ok" } },
                { "op": "updateQuestion", "questionId": {{questionId}}, "set": { "text": null } } ] }
            """);
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Json(invalid))["operationIndex"]!.GetValue<int>().Should().Be(1);

        // Someone else's package → 404, for writes and history alike
        (await Post(client, foreign.Id, valid)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/manage/packages/{foreign.Id}/changesets")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Stale version → 409
        (await Post(client, package.Id, $$"""{ "dryRun": true, "expectedVersion": "{{new string('0', 64)}}", "operations": [ { "op": "setSharedEditors", "value": true } ] }"""))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [SkippableFact]
    public async Task WriteBudget_Returns429WithRetryAfter()
    {
        await using var factory = CreateFactory(("AgentWritePerMinute", 1));
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        using var client = Client(factory, await TestData.CreateToken(factory, editor.Id));
        var json = """{ "dryRun": true, "operations": [ { "op": "updatePackage", "set": { "description": "Опис" } } ] }""";

        (await Post(client, package.Id, json)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var limited = await Post(client, package.Id, json);

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task BindingErrors_AreJsonWithDetails()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        using var client = Client(factory, await TestData.CreateToken(factory, editor.Id));

        foreach (var body in new[]
        {
            """{ "operations": [ """,
            """{ "dryRun": "yes", "operations": [ { "op": "setSharedEditors", "value": true } ] }""",
            """{ "requestId": "abc", "operations": [ { "op": "setSharedEditors", "value": true } ] }""",
        })
        {
            using var response = await Post(client, package.Id, body);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/json", body);
            var json = await Json(response);
            json["error"]!.GetValue<string>().Should().NotBeNullOrEmpty();
            json["details"].Should().NotBeNull();
        }

        using var noOperations = await Post(client, package.Id, """{ "dryRun": true }""");
        noOperations.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(noOperations))["error"]!.GetValue<string>().Should().Contain("operations");

        using var wrongType = await client.PostAsync($"/api/v1/manage/packages/{package.Id}/changesets",
            new StringContent("operations=x", Encoding.UTF8, "application/x-www-form-urlencoded"));
        wrongType.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [SkippableFact]
    public async Task History_FollowsTheTokensRights()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        var otherPackage = await TestData.CreatePackage(factory, editor.Id);
        using var writer = Client(factory, await TestData.CreateToken(factory, editor.Id));
        using var reader = Client(factory, await TestData.CreateToken(factory, editor.Id, TokenScope.Read));
        using var restricted = Client(factory, await TestData.CreateToken(factory, editor.Id, packageIds: [otherPackage.Id]));

        using var apply = await Post(writer, package.Id, $$"""{ "requestId": "{{Guid.NewGuid()}}", "operations": [ { "op": "updatePackage", "set": { "description": "Опис" } } ] }""");
        var changesetId = (await apply.Content.ReadFromJsonAsync<ChangesetResponse>())!.ChangesetId;
        var url = $"/api/v1/manage/packages/{package.Id}/changesets";

        (await reader.GetFromJsonAsync<ChangesetHistoryPage>(url))!.Changesets.Should().ContainSingle("read tokens can see history");
        (await restricted.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound, "outside the token's package list");
        (await reader.GetAsync($"/api/v1/manage/packages/{otherPackage.Id}/changesets/{changesetId}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "the changeset belongs to another package");
        (await reader.GetAsync($"{url}/999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var beyond = await reader.GetFromJsonAsync<ChangesetHistoryPage>($"{url}?page=2147483647&pageSize=1000");
        beyond!.Changesets.Should().BeEmpty();
        beyond.PageSize.Should().Be(100);
        beyond.TotalCount.Should().Be(1);
    }

    [SkippableFact]
    public async Task Posts_AlsoCountAgainstTheReadBudget()
    {
        await using var factory = CreateFactory(("AgentReadPerMinute", 1));
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        using var client = Client(factory, await TestData.CreateToken(factory, editor.Id));
        var json = """{ "dryRun": true, "operations": [ { "op": "updatePackage", "set": { "description": "Опис" } } ] }""";

        (await Post(client, package.Id, json)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Post(client, package.Id, json)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
