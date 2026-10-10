using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Controllers;
using QuestionsHub.Blazor.Controllers.Api.Manage;
using QuestionsHub.Blazor.Controllers.Api.V1;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.AgentApi;

/// <summary>
/// The agent read side shared by the REST API and the MCP tools: packages the agent can edit (drafts
/// included), their editable tree with its version, and author/tag lookups. Anything outside the
/// agent's rights reads as absent.
/// </summary>
/// <summary>Too many full-package reads are running; retry shortly (REST 503, MCP tool error).</summary>
public sealed class AgentApiBusyException() : Exception("The server is busy reading other packages. Retry shortly.");

public class AgentPackageReader(
    IDbContextFactory<QuestionsHubDbContext> dbContextFactory,
    AuthorService authorService,
    TagService tagService,
    IConfiguration configuration)
{
    public const int MaxPageSize = 50;
    public const int MaxLookupResults = 20;

    // A full package graph is the biggest thing an agent can make the server hold in memory; bound
    // how many are materialized at once (single-instance app, 512 MB container).
    private static readonly SemaphoreSlim GraphReads = new(4, 4);
    private static readonly TimeSpan GraphReadWait = TimeSpan.FromSeconds(10);

    /// <summary>Error for an unknown status filter (see <see cref="TryParseStatus"/>).</summary>
    public const string InvalidStatusError = "Invalid 'status'. Allowed: draft, published, archived.";

    /// <summary>Parses an optional status filter; false for an unknown value.</summary>
    public static bool TryParseStatus(string? status, out PackageStatus? filter)
    {
        filter = null;
        if (status == null)
            return true;
        if (!AgentApiNames.TryParse(status, out PackageStatus parsed))
            return false;
        filter = parsed;
        return true;
    }

    /// <summary>Packages the agent can edit, newest first.</summary>
    public async Task<ManagePackageListResponse> ListPackages(
        ClaimsPrincipal agent, PackageStatus? statusFilter, int page, int pageSize, CancellationToken ct = default)
    {

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var query = EditablePackages(context, agent);
        if (statusFilter != null)
            query = query.Where(p => p.Status == statusFilter);

        var totalCount = await query.CountAsync(ct);

        // long: page * pageSize can overflow int for absurd page numbers
        var offset = (long)(page - 1) * pageSize;
        if (offset >= totalCount)
            return new ManagePackageListResponse([], totalCount, page, pageSize);

        var packages = await query
            .OrderByDescending(p => p.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.Type,
                p.Status,
                p.AccessLevel,
                p.TotalQuestions,
                ToursCount = p.Tours.Count,
                p.PlayedFrom,
                p.PlayedTo,
                p.PublicationDate,
                OwnerFirstName = p.Owner != null ? p.Owner.FirstName : null,
                OwnerLastName = p.Owner != null ? p.Owner.LastName : null,
                HasResults = p.ResultsSources.Any()
            })
            .ToListAsync(ct);

        var items = packages
            .Select(p => new ManagePackageListItemDto(
                p.Id,
                p.Title,
                AgentApiNames.Of(p.Type),
                AgentApiNames.Of(p.Status),
                AgentApiNames.Of(p.AccessLevel),
                p.TotalQuestions,
                p.ToursCount,
                p.PlayedFrom,
                p.PlayedTo,
                p.PublicationDate,
                p.OwnerFirstName == null ? null : $"{p.OwnerFirstName} {p.OwnerLastName}",
                p.HasResults))
            .ToList();

        return new ManagePackageListResponse(items, totalCount, page, pageSize);
    }

    /// <summary>The full editable tree of a package with its content version, or null outside the agent's rights.</summary>
    /// <exception cref="AgentApiBusyException">Too many package graphs are being read right now.</exception>
    public async Task<ManagePackageDto?> GetPackage(ClaimsPrincipal agent, int id, CancellationToken ct = default)
    {
        if (!await GraphReads.WaitAsync(GraphReadWait, ct))
            throw new AgentApiBusyException();
        try
        {
            return await LoadPackage(agent, id, ct);
        }
        finally
        {
            GraphReads.Release();
        }
    }

    private async Task<ManagePackageDto?> LoadPackage(ClaimsPrincipal agent, int id, CancellationToken ct)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(ct);

        // The split query issues one statement per collection; a Repeatable Read snapshot makes them
        // (and the results check) see one consistent state, so the tree and its version never mix
        // before/after halves of a concurrent edit. Retried as a unit under EnableRetryOnFailure.
        var strategy = context.Database.CreateExecutionStrategy();
        var (package, hasResults) = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);

            // Filter by editing rights first: nothing of an inaccessible package is loaded.
            var loaded = await EditablePackages(context, agent)
                .WithEditableGraph()
                .FirstOrDefaultAsync(p => p.Id == id, ct);
            var results = loaded != null && await context.ResultsSources.AnyAsync(s => s.PackageId == id, ct);

            await transaction.CommitAsync(ct);
            return (loaded, results);
        });

        // EditablePackages already applied the rights; CanEditAsAgent is the in-memory twin, kept as
        // a guard against the two drifting apart.
        if (package == null || !agent.CanEditAsAgent(package))
            return null;

        var siteBaseUrl = configuration["SiteUrl"] ?? "https://questions.com.ua";
        return MapPackage(package, hasResults, siteBaseUrl);
    }

    /// <summary>Authors whose first or last name starts with <paramref name="search"/>.</summary>
    public async Task<List<ManageAuthorDto>> SearchAuthors(string search, CancellationToken ct = default)
    {
        var authors = await authorService.SearchAuthors(search.Trim(), MaxLookupResults, ct);
        return authors.Select(a => new ManageAuthorDto(a.Id, a.FirstName, a.LastName)).ToList();
    }

    /// <summary>Tags matching <paramref name="search"/>.</summary>
    public async Task<List<ManageTagDto>> SearchTags(string search, CancellationToken ct = default)
    {
        var tags = await tagService.Search(search.Trim(), MaxLookupResults, ct);
        return tags.Select(t => new ManageTagDto(t.Id, t.Name)).ToList();
    }

    /// <summary>
    /// Packages the principal may edit as an agent — the SQL form of <c>CanEditAsAgent</c>:
    /// admin → all, editor → own; narrowed by the token's allowlist.
    /// </summary>
    private static IQueryable<Package> EditablePackages(QuestionsHubDbContext context, ClaimsPrincipal agent)
    {
        var query = context.Packages.AsNoTracking();

        if (!agent.IsInRole("Admin"))
        {
            var userId = agent.GetUserId();
            query = query.Where(p => p.OwnerId == userId);
        }

        var allowlist = agent.GetPackageAllowlist();
        if (allowlist != null)
        {
            var ids = allowlist.ToList();
            query = query.Where(p => ids.Contains(p.Id));
        }

        return query;
    }

    private static ManagePackageDto MapPackage(Package package, bool hasResults, string siteBaseUrl)
    {
        ManageAuthorDto Author(Author a) => new(a.Id, a.FirstName, a.LastName);
        List<ManageAuthorDto> Authors(IEnumerable<Author> authors) => authors.OrderBy(a => a.Id).Select(Author).ToList();

        ManageQuestionDto Question(Question q) => new(
            q.Id,
            q.OrderIndex,
            q.BlockId,
            q.Number,
            q.HostInstructions,
            q.Text,
            q.HandoutText,
            PackagesController.ToAbsoluteUrl(q.HandoutUrl, siteBaseUrl),
            q.Answer,
            q.AcceptedAnswers,
            q.RejectedAnswers,
            q.AnswerForm,
            q.Comment,
            PackagesController.ToAbsoluteUrl(q.CommentAttachmentUrl, siteBaseUrl),
            q.Source,
            Authors(q.Authors));

        var tours = package.Tours
            .OrderBy(t => t.OrderIndex)
            .Select(t => new ManageTourDto(
                t.Id,
                t.OrderIndex,
                t.Number,
                AgentApiNames.Of(t.Type),
                t.Title,
                t.Preamble,
                t.Comment,
                Authors(t.Editors),
                t.Blocks
                    .OrderBy(b => b.OrderIndex)
                    .Select(b => new ManageBlockDto(
                        b.Id,
                        b.OrderIndex,
                        b.Name,
                        b.Preamble,
                        Authors(b.Editors),
                        PackageGraph.BlockQuestions(t, b).Select(Question).ToList()))
                    .ToList(),
                PackageGraph.RootQuestions(t).Select(Question).ToList()))
            .ToList();

        return new ManagePackageDto(
            package.Id,
            package.Title,
            AgentApiNames.Of(package.Type),
            AgentApiNames.Of(package.Status),
            AgentApiNames.Of(package.AccessLevel),
            AgentApiNames.Of(package.NumberingMode),
            package.SharedEditors,
            package.Description,
            package.Preamble,
            package.PlayedFrom,
            package.PlayedTo,
            package.PublicationDate,
            package.TotalQuestions,
            hasResults,
            PackageGraph.Fingerprint(package),
            Authors(package.PackageEditors),
            package.Tags.OrderBy(t => t.Name).Select(t => new ManageTagDto(t.Id, t.Name)).ToList(),
            tours);
    }
}
