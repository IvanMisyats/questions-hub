using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.IntegrationTests.Infrastructure;
using Xunit;

namespace QuestionsHub.IntegrationTests;

/// <summary>The MCP endpoint (/mcp) driven by the official MCP client, as an agent would use it.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class AgentMcpTests(PostgresFixture database)
{
    private QuestionsHubAppFactory CreateFactory(params (string Key, int Value)[] limits)
    {
        database.SkipIfUnavailable();
        var settings = limits.ToDictionary(
            l => $"RateLimits:{l.Key}",
            l => (string?)l.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new QuestionsHubAppFactory(database, settings);
    }

    private static async Task<McpClient> Connect(QuestionsHubAppFactory factory, string? token)
    {
        var http = factory.CreateClient();
        if (token != null)
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, NullLoggerFactory.Instance, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private static async Task<(bool IsError, JsonNode? Json, string Text)> Call(
        McpClient client, string tool, Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(tool, arguments);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text));
        JsonNode? json = null;
        if (result.IsError != true)
            json = JsonNode.Parse(text);
        return (result.IsError == true, json, text);
    }

    [SkippableFact]
    public async Task Tools_AreListed_WithTheirSafetyHints()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        await using var client = await Connect(factory, await TestData.CreateToken(factory, editor.Id));

        var tools = await client.ListToolsAsync();

        tools.Select(t => t.Name).Should().BeEquivalentTo(
            "whoami", "list_packages", "get_package", "search_authors", "search_tags",
            "apply_changeset", "list_changesets", "get_changeset");
        tools.Single(t => t.Name == "apply_changeset").ProtocolTool.Annotations!.DestructiveHint.Should().BeTrue();
        tools.Single(t => t.Name == "get_package").ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue();
    }

    [SkippableFact]
    public async Task AgentWorkflow_OverMcp()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        await using var client = await Connect(factory, await TestData.CreateToken(factory, editor.Id, packageIds: [package.Id]));

        var me = await Call(client, "whoami", []);
        me.Json!["token"]!["scope"]!.GetValue<string>().Should().Be("readWrite");
        me.Text.Should().Contain("Олена", "Cyrillic is not escaped in tool results (token cost, readability)");
        me.Json["token"]!["packageIds"]!.AsArray().Single()!.GetValue<int>().Should().Be(package.Id);

        var list = await Call(client, "list_packages", []);
        list.Json!["packages"]!.AsArray().Single()!["id"]!.GetValue<int>().Should().Be(package.Id);

        var structured = await client.CallToolAsync("get_package", new Dictionary<string, object?> { ["packageId"] = package.Id });
        structured.StructuredContent.Should().NotBeNull("results are also returned as structured content");

        var read = await Call(client, "get_package", new() { ["packageId"] = package.Id });
        var version = read.Json!["version"]!.GetValue<string>();
        var questionId = read.Json["tours"]![0]!["questions"]![0]!["id"]!.GetValue<int>();
        var operations = JsonDocument.Parse($$"""
            [ { "op": "updateQuestion", "questionId": {{questionId}}, "set": { "text": "Виправлено через MCP" } } ]
            """).RootElement;

        // Default is a preview
        var preview = await Call(client, "apply_changeset", new()
        {
            ["packageId"] = package.Id, ["operations"] = operations, ["expectedVersion"] = version
        });
        preview.IsError.Should().BeFalse(preview.Text);
        preview.Json!["dryRun"]!.GetValue<bool>().Should().BeTrue();

        var applied = await Call(client, "apply_changeset", new()
        {
            ["packageId"] = package.Id, ["operations"] = operations, ["expectedVersion"] = version,
            ["dryRun"] = false, ["requestId"] = Guid.NewGuid().ToString(), ["summary"] = "MCP"
        });
        applied.IsError.Should().BeFalse(applied.Text);
        var changesetId = applied.Json!["changesetId"]!.GetValue<int>();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            (await db.Questions.AsNoTracking().FirstAsync(q => q.Id == questionId)).Text.Should().Be("Виправлено через MCP");
        }

        var history = await Call(client, "list_changesets", new() { ["packageId"] = package.Id });
        history.Json!["changesets"]!.AsArray().Single()!["summary"]!.GetValue<string>().Should().Be("MCP");
        var detail = await Call(client, "get_changeset", new() { ["packageId"] = package.Id, ["changesetId"] = changesetId });
        detail.Json!["changes"]!.AsArray().Should().ContainSingle();
    }

    [SkippableFact]
    public async Task Errors_AreToolErrors_AndRightsMatchTheRestApi()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var other = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        var foreign = await TestData.CreatePackage(factory, other.Id);
        await using var reader = await Connect(factory, await TestData.CreateToken(factory, editor.Id, TokenScope.Read));
        await using var writer = await Connect(factory, await TestData.CreateToken(factory, editor.Id));
        var operations = JsonDocument.Parse("""[ { "op": "updatePackage", "set": { "description": "x" } } ]""").RootElement;

        var readOnly = await Call(reader, "apply_changeset", new() { ["packageId"] = package.Id, ["operations"] = operations });
        readOnly.IsError.Should().BeTrue();
        readOnly.Text.Should().Contain("read-only");

        (await Call(writer, "get_package", new() { ["packageId"] = foreign.Id })).IsError.Should().BeTrue();
        (await Call(writer, "apply_changeset", new() { ["packageId"] = foreign.Id, ["operations"] = operations })).Text
            .Should().Contain("not found");

        var invalid = await Call(writer, "apply_changeset", new()
        {
            ["packageId"] = package.Id,
            ["operations"] = JsonDocument.Parse("""[ { "op": "updatePackage", "set": { "title": "" } } ]""").RootElement
        });
        invalid.IsError.Should().BeTrue();
        invalid.Text.Should().Contain("Operation 0: 'title' cannot be empty.");

        var noRequestId = await Call(writer, "apply_changeset", new() { ["packageId"] = package.Id, ["operations"] = operations, ["dryRun"] = false });
        noRequestId.Text.Should().Contain("requestId");
    }

    [SkippableFact]
    public async Task WithoutAToken_TheEndpointRefuses()
    {
        await using var factory = CreateFactory();

        var act = () => Connect(factory, token: null);

        await act.Should().ThrowAsync<Exception>();
        using var http = factory.CreateClient();
        using var response = await http.PostAsync("/mcp", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task RevokedToken_StopsWorking_BetweenCalls()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var raw = await TestData.CreateToken(factory, editor.Id);
        await using var client = await Connect(factory, raw);
        (await Call(client, "whoami", [])).IsError.Should().BeFalse();

        using (var scope = factory.Services.CreateScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<QuestionsHub.Blazor.Infrastructure.AgentApi.PersonalAccessTokenService>();
            var token = (await tokens.GetForUser(editor.Id)).Single();
            (await tokens.Revoke(token.Id, editor.Id)).Should().BeTrue();
        }

        var act = () => client.CallToolAsync("whoami", new Dictionary<string, object?>()).AsTask();
        await act.Should().ThrowAsync<Exception>("every MCP request is authenticated again");
    }

    [SkippableFact]
    public async Task ReadBudget_IsSharedBetweenRestAndMcp()
    {
        await using var factory = CreateFactory(("AgentReadPerMinute", 2));
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var raw = await TestData.CreateToken(factory, editor.Id);
        using var rest = factory.CreateClient();
        rest.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", raw);

        (await rest.GetAsync("/api/v1/manage/me")).StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        (await rest.GetAsync("/api/v1/manage/me")).StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var connect = () => Connect(factory, raw);
        await connect.Should().ThrowAsync<Exception>("the token's budget is spent, whichever API spent it");
    }
}
