using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using QuestionsHub.Blazor.Controllers;
using QuestionsHub.Blazor.Controllers.Api.Manage;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.AgentApi.Changesets;

namespace QuestionsHub.Blazor.Infrastructure.AgentApi.Mcp;

/// <summary>
/// MCP tools at <c>/mcp</c> (Streamable HTTP, stateless). The endpoint authorizes with the personal
/// access token scheme like the REST agent API; every tool acts as the token's user and calls the same
/// services, so rules, rights and limits are identical. Errors are tool errors with the API's message.
/// The server describes itself (<see cref="AgentApiReference"/>): instructions on connect, the operation
/// list in <c>apply_changeset</c>, and the full contract from docs/API.md through <c>get_api_reference</c>.
/// </summary>
[McpServerToolType]
public sealed class AgentMcpTools(
    IHttpContextAccessor httpContextAccessor,
    AgentPackageReader reader,
    PackageChangesetService changesets)
{
    private ClaimsPrincipal Agent =>
        httpContextAccessor.HttpContext?.User ?? throw new McpException("No request context.");

    /// <summary>Every operation with its properties; a test keeps it in step with the parser's operation list.</summary>
    internal const string ApplyChangesetDescription = """
        Applies (or, with dryRun true — the default — previews) an ordered list of operations to a package, atomically: all or nothing.
        Each operation is an object with "op" and its properties ('?' = optional). 'set' is an object of fields, each replacing the field's whole value: an absent field stays unchanged, null clears an optional one.
        'authors' / 'editors' are arrays of {"id": n} or {"firstName": "…", "lastName": "…"}; they and 'tags' REPLACE the whole list (send the existing entries too when adding one; [] clears). Positions are 0-based within the tour (or block); omitted = append.
        - updatePackage: set {title, description, preamble, playedFrom, playedTo (yyyy-MM-dd)}
        - setPackageEditors: authors | setSharedEditors: value (bool) | setNumberingMode: mode (global, perTour, manual) | setTags: tags (names)
        - updateTour: tourId, set {title (Своя гра theme name), preamble, comment} | setTourEditors: tourId, authors | setTourType: tourId, type (regular, warmup, shootout)
        - updateBlock: blockId, set {name, preamble} | setBlockEditors: blockId, authors
        - updateQuestion: questionId, set {text, answer, acceptedAnswers, rejectedAnswers, comment, source, handoutText, hostInstructions, answerForm, number (manual numbering only)}
        - setQuestionAuthors: questionId, authors
        - addQuestion: tourId, blockId? (required in a tour with blocks), position?, set?, authors? | deleteQuestion: questionId | moveQuestion: questionId, tourId, blockId?, position?
        - addTour: position?, type?, set?, editors?, questions? (array of {set?, authors?}) | deleteTour: tourId | moveTour: tourId, position
        Example: [{ "op": "updateQuestion", "questionId": 501, "set": { "answer": "Київ", "acceptedAnswers": "Києв", "comment": null } }]
        Workflow: get_package → preview with expectedVersion = its version → check the diff → apply with dryRun false, the same expectedVersion and a new requestId (a UUID; reuse it only to retry the very same request).
        Needs a readWrite token; every applied changeset is recorded in the package history. Rules, limits, the response and errors: get_api_reference.
        """;

    [McpServerTool(Name = "get_api_reference", ReadOnly = true, Idempotent = true)]
    [Description("The complete Agent API reference (markdown): package model and field glossary, every changeset operation with its fields and rules, limits, the response format and errors. Read it before building a non-trivial changeset.")]
    public static string GetApiReference() => AgentApiReference.Text;

    [McpServerTool(Name = "whoami", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Who the token acts as (user, roles) and what it may do (scope 'read' or 'readWrite', expiry, package list or null = all editable packages).")]
    public AgentMeResponse WhoAmI() => ManageMeController.Describe(Agent);

    [McpServerTool(Name = "list_packages", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Packages the token can edit (drafts included), newest first. Page size up to 50.")]
    public async Task<ManagePackageListResponse> ListPackages(
        [Description("Optional filter: draft, published or archived.")] string? status = null,
        [Description("1-based page.")] int page = 1,
        [Description("Packages per page (1–50).")] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!AgentPackageReader.TryParseStatus(status, out var statusFilter))
            throw new McpException(AgentPackageReader.InvalidStatusError);

        return await reader.ListPackages(Agent, statusFilter, page, pageSize, ct);
    }

    [McpServerTool(Name = "get_package", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("The full editable tree of a package: tours (themes), blocks and questions with ids, orderIndex and all fields, plus 'version' to pass as expectedVersion when applying a changeset, and 'hasResults' (structural changes are refused when true).")]
    public async Task<ManagePackageDto> GetPackage(int packageId, CancellationToken ct = default)
    {
        try
        {
            return await reader.GetPackage(Agent, packageId, ct) ?? throw new McpException("Package not found.");
        }
        catch (AgentApiBusyException ex)
        {
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(Name = "search_authors", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Authors matching the text (up to 20), to reference them by id in a changeset: every word must start the first or the last name, so a full name ('Олена Коваленко') or a prefix ('Ковал') works.")]
    public async Task<List<ManageAuthorDto>> SearchAuthors(string search, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(search) ? throw new McpException("'search' is required.") : await reader.SearchAuthors(search, ct);

    [McpServerTool(Name = "search_tags", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Tags matching the text (up to 20).")]
    public async Task<List<ManageTagDto>> SearchTags(string search, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(search) ? throw new McpException("'search' is required.") : await reader.SearchTags(search, ct);

    // Idempotent: a preview changes nothing, and an apply retried with the same requestId replays.
    [McpServerTool(Name = "apply_changeset", Destructive = true, Idempotent = true, UseStructuredContent = true)]
    [Description(ApplyChangesetDescription)]
    public async Task<ChangesetResponse> ApplyChangeset(
        RequestContext<CallToolRequestParams> request,
        [Description("Package id (from list_packages).")] int packageId,
        [Description("The operations, applied in order: each an object with \"op\" and the properties listed in this tool's description.")] McpChangesetOperation?[] operations,
        [Description("true = preview only (default); false = apply.")] bool dryRun = true,
        [Description("UUID, required when applying; reuse it to retry the same request safely.")] string? requestId = null,
        [Description("The package 'version' the changeset was built against; the call fails if the package changed since.")] string? expectedVersion = null,
        [Description("Short description shown in the package history.")] string? summary = null,
        CancellationToken ct = default)
    {
        var agent = Agent;
        if (agent.FindFirst(AgentClaims.Scope)?.Value != nameof(TokenScope.ReadWrite))
            throw new McpException("The token's scope does not allow changes (read-only token).");

        Guid? id = null;
        if (requestId != null)
        {
            if (!Guid.TryParse(requestId, out var parsed))
                throw new McpException("'requestId' must be a UUID.");
            id = parsed;
        }

        // 'operations' is typed only for the input schema. The parser gets the JSON exactly as sent —
        // duplicate keys and property-name casing intact — so validation and replay match the REST API.
        var operationsJson = default(JsonElement);
        if (request.Params?.Arguments is not { } arguments || !arguments.TryGetValue("operations", out operationsJson))
            throw new McpException("'operations' is required.");
        var result = await changesets.Execute(agent, packageId, new ChangesetRequest(id, expectedVersion, summary, dryRun, operationsJson), ct);
        return result.Status switch
        {
            ChangesetStatus.Ok => result.Response!,
            ChangesetStatus.Invalid when result.OperationIndex is { } index => throw new McpException($"Operation {index}: {result.Error}"),
            ChangesetStatus.Busy => throw new McpException($"{result.Error} (retry in a few seconds)"),
            ChangesetStatus.RateLimited => throw new McpException($"{result.Error} (retry in a minute)"),
            _ => throw new McpException(result.Error ?? "The changeset failed.")
        };
    }

    [McpServerTool(Name = "list_changesets", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Changesets applied to a package, newest first (who, which token, summary, operation count, versions).")]
    public async Task<ChangesetHistoryPage> ListChangesets(int packageId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (!await changesets.CanEdit(Agent, packageId, ct))
            throw new McpException("Package not found.");

        return await changesets.GetHistory(packageId, page, pageSize, ct);
    }

    [McpServerTool(Name = "get_changeset", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("One applied changeset: its operations, the full diff and warnings.")]
    public async Task<ChangesetDetailDto> GetChangeset(int packageId, int changesetId, CancellationToken ct = default)
    {
        if (!await changesets.CanEdit(Agent, packageId, ct))
            throw new McpException("Package not found.");

        return await changesets.GetChangeset(packageId, changesetId, ct) ?? throw new McpException("Changeset not found.");
    }
}

/// <summary>
/// One changeset operation as the MCP input schema describes it: "op" plus that operation's own
/// properties, so clients see an array of objects instead of untyped JSON. Only the schema uses it:
/// apply_changeset hands the operations to the parser as raw JSON.
/// </summary>
public sealed class McpChangesetOperation
{
    [Description("The operation, e.g. updateQuestion — every operation and its properties are listed in the apply_changeset description.")]
    public string? Op { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Properties { get; set; }
}
