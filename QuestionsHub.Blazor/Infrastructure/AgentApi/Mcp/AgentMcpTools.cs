using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using ModelContextProtocol;
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
/// The full contract (operations, response shape) is in docs/API.md, "Agent API".
/// </summary>
[McpServerToolType]
public sealed class AgentMcpTools(
    IHttpContextAccessor httpContextAccessor,
    AgentPackageReader reader,
    PackageChangesetService changesets)
{
    private ClaimsPrincipal Agent =>
        httpContextAccessor.HttpContext?.User ?? throw new McpException("No request context.");

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
    [Description("Authors whose first or last name starts with the text (up to 20), to reference them by id in a changeset.")]
    public async Task<List<ManageAuthorDto>> SearchAuthors(string search, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(search) ? throw new McpException("'search' is required.") : await reader.SearchAuthors(search, ct);

    [McpServerTool(Name = "search_tags", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Tags matching the text (up to 20).")]
    public async Task<List<ManageTagDto>> SearchTags(string search, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(search) ? throw new McpException("'search' is required.") : await reader.SearchTags(search, ct);

    // Idempotent: a preview changes nothing, and an apply retried with the same requestId replays.
    [McpServerTool(Name = "apply_changeset", Destructive = true, Idempotent = true, UseStructuredContent = true)]
    [Description("""
        Applies (or, with dryRun true — the default — previews) an ordered list of edit operations to a package, atomically.
        Operations: updatePackage, setPackageEditors, setSharedEditors, setNumberingMode, setTags, updateTour, setTourEditors,
        updateBlock, setBlockEditors, updateQuestion, setQuestionAuthors, addQuestion, deleteQuestion, moveQuestion, addTour,
        deleteTour, moveTour, setTourType — e.g. { "op": "updateQuestion", "questionId": 501, "set": { "text": "…", "comment": null } }
        ('set': absent key = unchanged, null = clear). Workflow: get_package → preview with expectedVersion = its version → check
        the diff → apply with dryRun false, the same expectedVersion and a new requestId (a UUID; reuse it only to retry the very
        same request). Needs a readWrite token. Every applied changeset is recorded in the package history.
        """)]
    public async Task<ChangesetResponse> ApplyChangeset(
        int packageId,
        [Description("The operations array.")] JsonElement operations,
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

        var result = await changesets.Execute(agent, packageId, new ChangesetRequest(id, expectedVersion, summary, dryRun, operations), ct);
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
