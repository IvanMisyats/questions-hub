using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using QuestionsHub.Blazor.Controllers;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.RateLimiting;

namespace QuestionsHub.Blazor.Infrastructure.AgentApi.Changesets;

/// <summary>A changeset request (see docs/AGENT_API_PLAN.md, "Changeset request").</summary>
/// <param name="RequestId">Client-generated id, required to apply; reuse it when retrying the same request.</param>
/// <param name="ExpectedVersion">Optional content version the changeset was built against; a mismatch is a conflict.</param>
/// <param name="Operations">The raw <c>operations</c> array.</param>
public record ChangesetRequest(Guid? RequestId, string? ExpectedVersion, string? Summary, bool DryRun, JsonElement Operations);

/// <summary>An entity created by the changeset (id null on dry run).</summary>
public record CreatedEntityDto(int? OperationIndex, string Entity, int? Id);

public record ChangesetResponse(
    int? ChangesetId,
    bool DryRun,
    bool Replayed,
    string VersionBefore,
    string? VersionAfter,
    IReadOnlyList<ChangeDto> Changes,
    IReadOnlyList<CreatedEntityDto> Created,
    IReadOnlyList<string> Warnings,
    int TotalQuestions);

/// <summary>One entry of a package's changeset history.</summary>
public record ChangesetSummaryDto(
    int Id,
    DateTime CreatedAt,
    string UserDisplayName,
    string? TokenName,
    string? Summary,
    int OperationCount,
    string VersionBefore,
    string VersionAfter);

public record ChangesetHistoryPage(IReadOnlyList<ChangesetSummaryDto> Changesets, int TotalCount, int Page, int PageSize);

/// <summary>A stored changeset with its request operations and diff.</summary>
public record ChangesetDetailDto(
    ChangesetSummaryDto Changeset,
    JsonElement Operations,
    IReadOnlyList<ChangeDto> Changes,
    IReadOnlyList<string> Warnings,
    int TotalQuestionsAfter);

public enum ChangesetStatus
{
    Ok,
    /// <summary>Malformed or invalid request/operation (nothing saved).</summary>
    Invalid,
    /// <summary>The package does not exist or the token may not edit it.</summary>
    NotFound,
    /// <summary>Version mismatch, request id reused for another body, or a concurrent uniqueness race.</summary>
    Conflict,
    RateLimited,
    /// <summary>The package is busy with another changeset; retry shortly.</summary>
    Busy
}

public record ChangesetResult(
    ChangesetStatus Status,
    ChangesetResponse? Response = null,
    string? Error = null,
    int? OperationIndex = null)
{
    public static ChangesetResult Fail(ChangesetStatus status, string error, int? operationIndex = null) =>
        new(status, null, error, operationIndex);
}

/// <summary>
/// Applies (or previews) a changeset on behalf of an agent, atomically: one Repeatable Read
/// transaction under the execution strategy loads the graph, checks rights and the expected
/// version, runs the <see cref="ChangesetEngine"/>, saves the mutations (generated ids resolved),
/// then writes the <see cref="PackageChangeset"/> audit row and commits. A retried request with the
/// same request id returns the stored result instead of applying again.
/// </summary>
public class PackageChangesetService(
    IDbContextFactory<QuestionsHubDbContext> dbContextFactory,
    PackageRenumberingService renumbering,
    ClientRateLimiter rateLimiter,
    PackageListService packageListService,
    TagService tagService,
    TimeProvider timeProvider,
    ILogger<PackageChangesetService> logger)
{
    public const int MaxSummaryLength = 500;

    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // At most two graph loads + engine runs at once to bound memory (single-instance app). Per package,
    // changesets serialize with each other and with results loading via PackageWriteLocks.
    private static readonly SemaphoreSlim EngineSlots = new(2, 2);

    public async Task<ChangesetResult> Execute(ClaimsPrincipal agent, int packageId, ChangesetRequest request, CancellationToken ct = default)
    {
        var tokenId = agent.GetTokenId()
            ?? throw new InvalidOperationException("Changesets require a principal authenticated with a personal access token.");

        if (request.Summary?.Length > MaxSummaryLength)
            return ChangesetResult.Fail(ChangesetStatus.Invalid, $"'summary' is longer than {MaxSummaryLength} characters.");
        if (!request.DryRun && (request.RequestId == null || request.RequestId == Guid.Empty))
        {
            return ChangesetResult.Fail(ChangesetStatus.Invalid,
                "'requestId' is required to apply a changeset: generate a UUID and reuse it if you retry the same request.");
        }

        List<ChangesetOperation> operations;
        try
        {
            operations = ChangesetParser.Parse(request.Operations);
        }
        catch (ChangesetValidationException ex)
        {
            return ChangesetResult.Fail(ChangesetStatus.Invalid, ex.Message, ex.OperationIndex);
        }

        // Applies and dry runs share the write budget (both load and process the whole package).
        using (var lease = rateLimiter.Acquire(RateLimitBudgets.AgentWrite, tokenId.ToString(CultureInfo.InvariantCulture)))
        {
            if (!lease.IsAcquired)
                return ChangesetResult.Fail(ChangesetStatus.RateLimited, "Changeset rate limit exceeded. Please retry later.");
        }

        var requestHash = HashRequest(request);

        // Cheap replay check before taking locks: a retried request returns its stored result —
        // but only to a caller who may still edit the package (rights are checked first, every time).
        if (!request.DryRun)
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(ct);
            if (!await CanEdit(context, agent, packageId, ct))
                return NotFoundResult;

            var replay = await FindReplay(context, tokenId, request.RequestId!.Value, requestHash, packageId, ct);
            if (replay != null)
                return Confirmed(replay);
        }

        if (!await EngineSlots.WaitAsync(LockTimeout, ct))
            return ChangesetResult.Fail(ChangesetStatus.Busy, "The server is busy applying other changesets. Retry shortly.");
        try
        {
            // Taken before the transaction starts and held through commit, so the snapshot cannot miss
            // results being loaded concurrently (which would make the structural guard stale).
            using var packageLock = await PackageWriteLocks.TryAcquire(packageId, LockTimeout, ct);
            if (packageLock == null)
                return ChangesetResult.Fail(ChangesetStatus.Busy, "Another change is being applied to this package. Retry shortly.");

            return await ExecuteLocked(agent, tokenId, packageId, request, requestHash, operations, ct);
        }
        finally
        {
            EngineSlots.Release();
        }
    }

    private async Task<ChangesetResult> ExecuteLocked(
        ClaimsPrincipal agent,
        int tokenId,
        int packageId,
        ChangesetRequest request,
        string requestHash,
        List<ChangesetOperation> operations,
        CancellationToken ct)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var strategy = context.Database.CreateExecutionStrategy();

        try
        {
            // The cancellation-aware overload, so cancelling also stops waiting out retry delays.
            var result = await strategy.ExecuteAsync(async attemptCt =>
            {
                // A transient failure re-runs the whole unit from scratch.
                context.ChangeTracker.Clear();
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, attemptCt);

                // Rights first, from two columns: nothing of an inaccessible package is loaded or replayed.
                if (!await CanEdit(context, agent, packageId, attemptCt))
                    return NotFoundResult;

                // An earlier attempt may have committed before its connection dropped.
                if (!request.DryRun)
                {
                    var replay = await FindReplay(context, tokenId, request.RequestId!.Value, requestHash, packageId, attemptCt);
                    if (replay != null)
                        return replay;
                }

                var package = await context.Packages.WithEditableGraph().FirstAsync(p => p.Id == packageId, attemptCt);
                var versionBefore = PackageGraph.Fingerprint(package);
                if (request.ExpectedVersion != null && request.ExpectedVersion != versionBefore)
                {
                    return ChangesetResult.Fail(ChangesetStatus.Conflict,
                        "The package changed since 'expectedVersion'. Re-read it (GET /api/v1/manage/packages/{id}) and rebuild the changeset.");
                }

                var hasResults = await context.ResultsSources.AnyAsync(r => r.PackageId == packageId, attemptCt);
                var engine = new ChangesetEngine(context, package, renumbering, hasResults);
                await engine.Apply(operations, attemptCt);

                if (request.DryRun)
                {
                    var preview = ChangeFormatter.ToDtos(engine.Changes, package, context);
                    return Ok(new ChangesetResponse(null, true, false, versionBefore, null, preview,
                        CreatedOf(preview), engine.Warnings, package.TotalQuestions));
                    // The transaction is rolled back on dispose; nothing was written anyway.
                }

                await context.SaveChangesAsync(attemptCt);

                var changes = ChangeFormatter.ToDtos(engine.Changes, package, context);
                var versionAfter = PackageGraph.Fingerprint(package);
                var changeset = new PackageChangeset
                {
                    PackageId = packageId,
                    UserId = agent.GetUserId(),
                    UserDisplayName = agent.Identity?.Name ?? "",
                    TokenId = tokenId,
                    TokenName = agent.FindFirst(AgentClaims.TokenName)?.Value,
                    RequestId = request.RequestId!.Value,
                    RequestHash = requestHash,
                    Summary = string.IsNullOrWhiteSpace(request.Summary) ? null : request.Summary.Trim(),
                    CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
                    OperationCount = operations.Count,
                    VersionBefore = versionBefore,
                    VersionAfter = versionAfter,
                    TotalQuestionsAfter = package.TotalQuestions,
                    OperationsJson = JsonSerializer.Serialize(request.Operations),
                    ChangesJson = JsonSerializer.Serialize(changes, Json),
                    WarningsJson = engine.Warnings.Count > 0 ? JsonSerializer.Serialize(engine.Warnings, Json) : null
                };
                context.PackageChangesets.Add(changeset);
                await context.SaveChangesAsync(attemptCt);
                await transaction.CommitAsync(attemptCt);

                return Ok(new ChangesetResponse(changeset.Id, false, false, versionBefore, versionAfter, changes,
                    CreatedOf(changes), engine.Warnings, package.TotalQuestions));
            }, ct);

            return Confirmed(result);
        }
        catch (ChangesetValidationException ex)
        {
            return ChangesetResult.Fail(ChangesetStatus.Invalid, ex.Message, ex.OperationIndex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            if (pg.ConstraintName == QuestionsHubDbContext.PackageChangesetRequestIndex)
            {
                // Another request with the same id committed first: report its result.
                await using var fresh = await dbContextFactory.CreateDbContextAsync(ct);
                if (!await CanEdit(fresh, agent, packageId, ct))
                    return NotFoundResult;

                var replay = await FindReplay(fresh, tokenId, request.RequestId!.Value, requestHash, packageId, ct);
                if (replay != null)
                    return Confirmed(replay);
            }

            logger.LogInformation(ex, "Changeset on package {PackageId} lost a uniqueness race ({Constraint})", packageId, pg.ConstraintName);
            return ChangesetResult.Fail(ChangesetStatus.Conflict,
                "Another change created the same author or tag at the same moment. Retry the changeset.");
        }
    }

    /// <summary>The stored result of an earlier request with this id, a conflict if the id was used for another request, or null.</summary>
    private static async Task<ChangesetResult?> FindReplay(
        QuestionsHubDbContext context, int tokenId, Guid requestId, string requestHash, int packageId, CancellationToken ct)
    {
        var existing = await context.PackageChangesets
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TokenId == tokenId && c.RequestId == requestId, ct);

        if (existing == null)
            return null;

        if (existing.RequestHash != requestHash || existing.PackageId != packageId)
        {
            return ChangesetResult.Fail(ChangesetStatus.Conflict,
                "This 'requestId' was already used for a different changeset. Generate a new one for a new request.");
        }

        var changes = JsonSerializer.Deserialize<List<ChangeDto>>(existing.ChangesJson, Json) ?? [];
        var warnings = existing.WarningsJson == null ? [] : JsonSerializer.Deserialize<List<string>>(existing.WarningsJson, Json) ?? [];
        return Ok(new ChangesetResponse(existing.Id, false, true, existing.VersionBefore, existing.VersionAfter,
            changes, CreatedOf(changes), warnings, existing.TotalQuestionsAfter));
    }

    /// <summary>
    /// Whether a signed-in user may edit (and so see the history of) the package, from the user's
    /// <em>current</em> roles in the database — not the session cookie's, which outlive a demotion.
    /// Same rule as the editor: admins any package, editors their own.
    /// </summary>
    public async Task<bool> UserCanEdit(string? userId, int packageId, CancellationToken ct = default)
    {
        if (userId == null)
            return false;

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var roles = await context.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(context.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name)
            .ToListAsync(ct);

        if (roles.Contains("Admin"))
            return await context.Packages.AnyAsync(p => p.Id == packageId, ct);

        return roles.Contains("Editor")
               && await context.Packages.AnyAsync(p => p.Id == packageId && p.OwnerId == userId, ct);
    }

    /// <summary>Whether the agent may edit (and so see the history of) the package.</summary>
    public async Task<bool> CanEdit(ClaimsPrincipal agent, int packageId, CancellationToken ct = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        return await CanEdit(context, agent, packageId, ct);
    }

    /// <summary>
    /// The package's applied changesets, newest first. Callers check access first (agents via
    /// <see cref="CanEdit(ClaimsPrincipal, int, CancellationToken)"/>, the editor UI via its own rules).
    /// </summary>
    public async Task<ChangesetHistoryPage> GetHistory(int packageId, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var query = context.PackageChangesets.AsNoTracking().Where(c => c.PackageId == packageId);
        var totalCount = await query.CountAsync(ct);

        var offset = (long)(page - 1) * pageSize;
        if (offset >= totalCount)
            return new ChangesetHistoryPage([], totalCount, page, pageSize);

        var items = await query
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(c => new ChangesetSummaryDto(c.Id, c.CreatedAt, c.UserDisplayName, c.TokenName, c.Summary,
                c.OperationCount, c.VersionBefore, c.VersionAfter))
            .ToListAsync(ct);

        return new ChangesetHistoryPage(items, totalCount, page, pageSize);
    }

    /// <summary>One changeset of the package with its operations and diff, or null. Callers check access first.</summary>
    public async Task<ChangesetDetailDto?> GetChangeset(int packageId, int changesetId, CancellationToken ct = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var changeset = await context.PackageChangesets.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == changesetId && c.PackageId == packageId, ct);
        return changeset == null ? null : ToDetail(changeset);
    }

    /// <summary>
    /// Summaries of the package's latest changesets, newest first — no JSON payloads, so it is cheap
    /// enough for every editor page load. Callers check access first.
    /// </summary>
    public async Task<List<ChangesetSummaryDto>> GetRecentSummaries(int packageId, int take, CancellationToken ct = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        return await context.PackageChangesets.AsNoTracking()
            .Where(c => c.PackageId == packageId)
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .Take(Math.Clamp(take, 1, 101))
            .Select(c => new ChangesetSummaryDto(c.Id, c.CreatedAt, c.UserDisplayName, c.TokenName, c.Summary,
                c.OperationCount, c.VersionBefore, c.VersionAfter))
            .ToListAsync(ct);
    }

    private static ChangesetDetailDto ToDetail(PackageChangeset changeset)
    {
        var summary = new ChangesetSummaryDto(changeset.Id, changeset.CreatedAt, changeset.UserDisplayName, changeset.TokenName,
            changeset.Summary, changeset.OperationCount, changeset.VersionBefore, changeset.VersionAfter);
        using var operations = JsonDocument.Parse(changeset.OperationsJson);
        return new ChangesetDetailDto(
            summary,
            operations.RootElement.Clone(),
            JsonSerializer.Deserialize<List<ChangeDto>>(changeset.ChangesJson, Json) ?? [],
            changeset.WarningsJson == null ? [] : JsonSerializer.Deserialize<List<string>>(changeset.WarningsJson, Json) ?? [],
            changeset.TotalQuestionsAfter);
    }

    private static ChangesetResult Ok(ChangesetResponse response) => new(ChangesetStatus.Ok, response);

    private static readonly ChangesetResult NotFoundResult = ChangesetResult.Fail(ChangesetStatus.NotFound, "Package not found.");

    /// <summary>Editing rights from two columns, without loading the package graph.</summary>
    private static async Task<bool> CanEdit(QuestionsHubDbContext context, ClaimsPrincipal agent, int packageId, CancellationToken ct)
    {
        var head = await context.Packages
            .Where(p => p.Id == packageId)
            .Select(p => new { p.Id, p.OwnerId })
            .FirstOrDefaultAsync(ct);
        return head != null && agent.CanEditAsAgent(new Package { Id = head.Id, OwnerId = head.OwnerId, Title = "" });
    }

    /// <summary>
    /// After a confirmed apply (fresh or replayed) the cached package lists and popular tags may be
    /// stale. Invalidated unconditionally: cheap, and correct even when an uncertain commit was only
    /// confirmed by the replay check of a retry.
    /// </summary>
    private ChangesetResult Confirmed(ChangesetResult result)
    {
        if (result is { Status: ChangesetStatus.Ok, Response.DryRun: false })
        {
            packageListService.InvalidateCache();
            tagService.InvalidatePopularTagsCache();
        }

        return result;
    }

    private static List<CreatedEntityDto> CreatedOf(IEnumerable<ChangeDto> changes) =>
        changes.Where(c => c.Kind == "added").Select(c => new CreatedEntityDto(c.OperationIndex, c.Entity, c.Id)).ToList();

    /// <summary>
    /// SHA-256 of the request's content in canonical form — whitespace removed and object properties
    /// sorted (array order kept) — so a retry of the same request matches however it is serialized.
    /// </summary>
    public static string HashRequest(ChangesetRequest request)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("expectedVersion", request.ExpectedVersion);
            writer.WriteString("summary", request.Summary?.Trim());
            writer.WritePropertyName("operations");
            WriteCanonical(writer, request.Operations);
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
