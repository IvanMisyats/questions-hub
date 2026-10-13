using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Controllers.Api.Manage;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.RateLimiting;
using QuestionsHub.Blazor.Infrastructure.Results;

namespace QuestionsHub.Blazor.Infrastructure.AgentApi;

/// <summary>An agent results call failed; <see cref="StatusCode"/> is the HTTP status the REST API answers with.</summary>
public sealed class AgentResultsException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// Tournament-results sources through the agent API, shared by the REST controller and the MCP tools:
/// the token's package rights (404 outside them), the per-token write budget shared with changesets,
/// and a projection of <see cref="PackageResultsService"/> — the same service the editor's «Результати»
/// card uses, so attach/load/delete behave exactly as in the UI.
/// </summary>
public sealed partial class AgentResultsService(
    IDbContextFactory<QuestionsHubDbContext> contextFactory,
    PackageResultsService results,
    ClientRateLimiter rateLimiter,
    ILogger<AgentResultsService> logger)
{
    /// <summary>Sources of the package, oldest first.</summary>
    public async Task<ManageResultsSourcesResponse> List(ClaimsPrincipal agent, int packageId, CancellationToken ct = default)
    {
        await CheckAccess(agent, packageId, ct);
        return new((await results.GetSourcesForPackage(packageId)).Select(Map).ToList());
    }

    /// <summary>
    /// Attaches a link and, for Rating/OpenQuiz, loads it synchronously. A failed load stays on the source
    /// (<c>loadError</c>); only an invalid, duplicate or unsupported link is an error (422).
    /// </summary>
    public async Task<ManageResultsSourceDto> Attach(ClaimsPrincipal agent, int packageId, string? url, CancellationToken ct = default)
    {
        await CheckAccess(agent, packageId, ct);
        TakeWritePermit(agent);
        try
        {
            ResultsSource source;
            // Attaching makes hasResults true, which structural changesets check under this lock; holding it
            // also serializes two agents attaching the same link (the duplicate check is not atomic).
            // LoadSource takes the lock itself, after the slow platform fetch.
            using (await PackageWriteLocks.Acquire(packageId, ct))
                source = await results.AttachSource(packageId, url ?? "");
            logger.LogInformation("Agent token {TokenId} attached results source {SourceId} to package {PackageId}",
                agent.GetTokenId(), source.Id, packageId);
            if (source.Platform != ResultsPlatform.Other)
                source = await results.LoadSource(source.Id, ct);
            return Map(source);
        }
        catch (ResultsLoadException ex)
        {
            throw new AgentResultsException(422, ex.Message);
        }
    }

    /// <summary>Refetches a source; a failed reload keeps the previously loaded data and reports <c>loadError</c>.</summary>
    public async Task<ManageResultsSourceDto> Reload(ClaimsPrincipal agent, int packageId, int sourceId, CancellationToken ct = default)
    {
        await CheckSource(agent, packageId, sourceId, ct);
        TakeWritePermit(agent);
        try
        {
            var source = await results.LoadSource(sourceId, ct);
            logger.LogInformation("Agent token {TokenId} reloaded results source {SourceId} in package {PackageId}",
                agent.GetTokenId(), sourceId, packageId);
            return Map(source);
        }
        catch (ResultsLoadException ex)
        {
            throw new AgentResultsException(422, ex.Message);
        }
    }

    /// <summary>Deletes a source with its team results and re-aggregates the package statistics.</summary>
    public async Task Delete(ClaimsPrincipal agent, int packageId, int sourceId, CancellationToken ct = default)
    {
        await CheckSource(agent, packageId, sourceId, ct);
        TakeWritePermit(agent);
        await results.DeleteSource(sourceId);
        logger.LogInformation("Agent token {TokenId} deleted results source {SourceId} from package {PackageId}",
            agent.GetTokenId(), sourceId, packageId);
    }

    private async Task CheckAccess(ClaimsPrincipal agent, int packageId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var package = await db.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == packageId, ct);
        if (package == null || !agent.CanEditAsAgent(package))
            throw new AgentResultsException(404, "Package not found.");
    }

    private async Task CheckSource(ClaimsPrincipal agent, int packageId, int sourceId, CancellationToken ct)
    {
        await CheckAccess(agent, packageId, ct);
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        if (!await db.ResultsSources.AnyAsync(s => s.Id == sourceId && s.PackageId == packageId, ct))
            throw new AgentResultsException(404, "Results source not found.");
    }

    private void TakeWritePermit(ClaimsPrincipal agent)
    {
        if (agent.FindFirst(AgentClaims.Scope)?.Value != nameof(TokenScope.ReadWrite))
            throw new AgentResultsException(403, "The token's scope does not allow changes (read-only token).");
        var tokenId = agent.GetTokenId() ?? throw new InvalidOperationException("Results writes require a personal access token.");
        using var lease = rateLimiter.Acquire(RateLimitBudgets.AgentWrite, tokenId.ToString(CultureInfo.InvariantCulture));
        if (!lease.IsAcquired)
            throw new AgentResultsException(429, "Results rate limit exceeded. Please retry later.");
    }

    private static ManageResultsSourceDto Map(ResultsSource source) => Map(new ResultsSourceInfo(
        source.Id, source.Platform, source.Url, source.Title, source.DisplayUrl, source.LoadedAt,
        source.LoadError, null, source.ResultsAvailableAfter, source.TeamsCount, source.StatsMapped, source.WarningsJson));

    private static ManageResultsSourceDto Map(ResultsSourceInfo source) => new(
        source.Id, JsonNamingPolicy.CamelCase.ConvertName(source.Platform.ToString()), AgentVisibleUrl(source.Platform, source.Url),
        source.DisplayUrl, source.Title, source.LoadedAt, source.TeamsCount, source.StatsMapped,
        string.IsNullOrEmpty(source.WarningsJson) ? [] : JsonSerializer.Deserialize<List<string>>(source.WarningsJson) ?? [],
        source.LoadError, source.ResultsAvailableAfter);

    /// <summary>
    /// The stored link as an agent may see it. An OpenQuiz audience link carries a listen token, a credential
    /// for open-quiz.com outside this token's scope and revocation, so it is masked; once loaded, the public
    /// results link is in <c>displayUrl</c>. Reloads use the stored link server-side.
    /// </summary>
    internal static string AgentVisibleUrl(ResultsPlatform platform, string url)
    {
        if (platform != ResultsPlatform.OpenQuiz)
            return url;

        try
        {
            // Mask the parameter as written: the parser sees the decoded value, the URL may hold it percent-encoded
            return ResultsUrlParser.Parse(platform, url).LinkKind == OpenQuizLinkKind.Aud
                ? TokenParameter().Replace(url, "$1***")
                : url;
        }
        catch (ResultsLoadException)
        {
            // Stored links were validated on attach; never echo one we can no longer classify
            return "***";
        }
    }

    [GeneratedRegex(@"([?&]token=)[^&#]*", RegexOptions.IgnoreCase)]
    private static partial Regex TokenParameter();
}
