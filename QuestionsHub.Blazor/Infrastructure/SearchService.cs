using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Utils;

namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Result of a search query containing full question data and metadata.
/// Highlighted versions of text fields contain &lt;mark&gt; tags around matched terms.
/// </summary>
public record SearchResult(
    int QuestionId,
    int TourId,
    int PackageId,
    string PackageTitle,
    string TourNumber,
    string QuestionNumber,
    string Text,
    string Answer,
    string? HandoutText,
    string? HandoutUrl,
    string? AcceptedAnswers,
    string? RejectedAnswers,
    string? Comment,
    string? CommentAttachmentUrl,
    string? Source,
    string TextHighlighted,
    string AnswerHighlighted,
    string? HandoutTextHighlighted,
    string? AcceptedAnswersHighlighted,
    string? RejectedAnswersHighlighted,
    string? CommentHighlighted,
    string? SourceHighlighted,
    double Rank,
    string? Authors,
    bool IsAdult
);

/// <summary>
/// Service for searching questions using PostgreSQL full-text search with trigram fuzzy matching.
/// </summary>
public class SearchService
{
    private readonly IDbContextFactory<QuestionsHubDbContext> _contextFactory;
    private readonly ILogger<SearchService> _logger;

    public SearchService(
        IDbContextFactory<QuestionsHubDbContext> contextFactory,
        ILogger<SearchService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Searches questions using hybrid FTS + trigram matching.
    /// Supports web-style query syntax: AND (default), OR, "phrase", -exclude.
    /// </summary>
    /// <param name="query">Search query string</param>
    /// <param name="accessContext">User access context for filtering by access level</param>
    /// <param name="limit">Maximum number of results (default 50, max 100)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of search results ordered by relevance</returns>
    public async Task<List<SearchResult>> Search(
        string query,
        PackageAccessContext accessContext,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        // Normalize apostrophes in search query to match stored data
        query = TextNormalizer.NormalizeApostrophes(query)!;

        // Clamp limit to reasonable bounds
        limit = Math.Clamp(limit, 1, 100);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Access level values: 0 = All, 1 = RegisteredOnly, 2 = EditorsOnly
        var isAdmin = accessContext.IsAdmin;
        var isEditor = accessContext.IsEditor;
        var hasVerifiedEmail = accessContext.HasVerifiedEmail;
        var userId = accessContext.UserId ?? "";

        var results = await context.Database
            .SqlQuery<SearchResult>($@"
                WITH q AS (
                    SELECT
                        websearch_to_tsquery('ukrainian', public.qh_normalize({query})) AS tsq,
                        public.qh_normalize({query}) AS qnorm
                )
                SELECT
                    qu.""Id"" AS ""QuestionId"",
                    qu.""TourId"",
                    t.""PackageId"",
                    p.""Title"" AS ""PackageTitle"",
                    t.""Number"" AS ""TourNumber"",
                    qu.""Number"" AS ""QuestionNumber"",
                    qu.""Text"",
                    qu.""Answer"",
                    qu.""HandoutText"",
                    qu.""HandoutUrl"",
                    qu.""AcceptedAnswers"",
                    qu.""RejectedAnswers"",
                    qu.""Comment"",
                    qu.""CommentAttachmentUrl"",
                    qu.""Source"",
                    ts_headline('ukrainian', COALESCE(qu.""Text"", ''), q.tsq,
                        'StartSel=<mark>, StopSel=</mark>, HighlightAll=true') AS ""TextHighlighted"",
                    ts_headline('ukrainian', COALESCE(qu.""Answer"", ''), q.tsq,
                        'StartSel=<mark>, StopSel=</mark>, HighlightAll=true') AS ""AnswerHighlighted"",
                    ts_headline('ukrainian', COALESCE(qu.""HandoutText"", ''), q.tsq,
                        'StartSel=<mark>, StopSel=</mark>, HighlightAll=true') AS ""HandoutTextHighlighted"",
                    ts_headline('ukrainian', COALESCE(qu.""AcceptedAnswers"", ''), q.tsq,
                        'StartSel=<mark>, StopSel=</mark>, HighlightAll=true') AS ""AcceptedAnswersHighlighted"",
                    ts_headline('ukrainian', COALESCE(qu.""RejectedAnswers"", ''), q.tsq,
                        'StartSel=<mark>, StopSel=</mark>, HighlightAll=true') AS ""RejectedAnswersHighlighted"",
                    ts_headline('ukrainian', COALESCE(qu.""Comment"", ''), q.tsq,
                        'StartSel=<mark>, StopSel=</mark>, HighlightAll=true') AS ""CommentHighlighted"",
                    ts_headline('ukrainian', COALESCE(qu.""Source"", ''), q.tsq,
                        'StartSel=<mark>, StopSel=</mark>, HighlightAll=true') AS ""SourceHighlighted"",
                    (COALESCE(ts_rank_cd(qu.""SearchVector"", q.tsq), 0) +
                     COALESCE(similarity(qu.""SearchTextNorm"", q.qnorm), 0))::float8 AS ""Rank"",
                    (SELECT string_agg(a.""Id""::text || ':' || a.""FirstName"" || ' ' || a.""LastName"", '|' ORDER BY a.""LastName"", a.""FirstName"")
                     FROM ""QuestionAuthors"" qa
                     JOIN ""Authors"" a ON qa.""AuthorsId"" = a.""Id""
                     WHERE qa.""QuestionsId"" = qu.""Id"") AS ""Authors"",
                    EXISTS (
                        SELECT 1 FROM ""PackageTags"" pt
                        JOIN ""Tags"" tg ON pt.""TagsId"" = tg.""Id""
                        WHERE pt.""PackagesId"" = p.""Id""
                          AND tg.""Name"" = '18+'
                    ) AS ""IsAdult""
                FROM ""Questions"" qu
                JOIN ""Tours"" t ON qu.""TourId"" = t.""Id""
                JOIN ""Packages"" p ON t.""PackageId"" = p.""Id""
                CROSS JOIN q
                WHERE p.""Status"" = 1
                  AND (
                       qu.""SearchVector"" @@ q.tsq
                       OR qu.""SearchTextNorm"" % q.qnorm
                  )
                  AND (
                       {isAdmin} = true
                       OR p.""OwnerId"" = {userId}
                       OR p.""AccessLevel"" = 0
                       OR (p.""AccessLevel"" = 1 AND {hasVerifiedEmail} = true)
                       OR (p.""AccessLevel"" = 2 AND {isEditor} = true)
                  )
                ORDER BY ""Rank"" DESC
                LIMIT {limit}
            ")
            .ToListAsync(cancellationToken);

        _logger.LogDebug("Search for '{Query}' returned {Count} results", query, results.Count);

        return results;
    }
}

