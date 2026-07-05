using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuestionsHub.Blazor.Controllers.Api.V1.Dto;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Api;
using QuestionsHub.Blazor.Infrastructure.Search;

namespace QuestionsHub.Blazor.Controllers.Api.V1;

[ApiController]
[Route("api/v1/search")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.Scheme)]
[EnableCors("PublicApi")]
[EnableRateLimiting("api_search")]
public class SearchController : ControllerBase
{
    private readonly SearchService _searchService;
    private readonly IConfiguration _configuration;

    private static readonly PackageAccessContext AnonymousContext = new(
        IsAdmin: false, IsEditor: false, HasVerifiedEmail: false, UserId: null);

    public SearchController(SearchService searchService, IConfiguration configuration)
    {
        _searchService = searchService;
        _configuration = configuration;
    }

    /// <summary>
    /// Full-text search across published public questions.
    /// Supports AND (default), OR, "phrase", -exclude syntax.
    /// </summary>
    /// <param name="q">Search query.</param>
    /// <param name="limit">Maximum number of results (1-100).</param>
    /// <param name="type">Optional game-type filter: "www" or "shvager".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet]
    public async Task<ActionResult<ApiSearchResponse>> Search(
        [FromQuery] string? q = null,
        [FromQuery] int limit = 50,
        [FromQuery] PackageType? type = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "Query parameter 'q' is required." });

        if (type.HasValue && !Enum.IsDefined(type.Value))
            return BadRequest(new { error = "Invalid 'type' value. Allowed: www, shvager." });

        limit = Math.Clamp(limit, 1, 100);

        var results = await _searchService.Search(q, AnonymousContext, limit, type, cancellationToken);

        var siteBaseUrl = _configuration["SiteUrl"] ?? "https://questions.com.ua";

        var dtos = results.Select(r => new ApiSearchResultDto(
            QuestionId: r.QuestionId,
            TourId: r.TourId,
            PackageId: r.PackageId,
            PackageTitle: r.PackageTitle,
            GameType: ToGameTypeString(r.PackageType),
            TourNumber: r.TourNumber,
            TourTitle: r.TourTitle,
            QuestionNumber: r.QuestionNumber,
            Text: r.Text,
            Answer: r.Answer,
            HandoutText: r.HandoutText,
            HandoutUrl: PackagesController.ToAbsoluteUrl(r.HandoutUrl, siteBaseUrl),
            AcceptedAnswers: r.AcceptedAnswers,
            RejectedAnswers: r.RejectedAnswers,
            AnswerForm: r.AnswerForm,
            Comment: r.Comment,
            CommentAttachmentUrl: PackagesController.ToAbsoluteUrl(r.CommentAttachmentUrl, siteBaseUrl),
            Source: r.Source,
            TextHighlighted: r.TextHighlighted,
            AnswerHighlighted: r.AnswerHighlighted,
            HandoutTextHighlighted: r.HandoutTextHighlighted,
            AcceptedAnswersHighlighted: r.AcceptedAnswersHighlighted,
            RejectedAnswersHighlighted: r.RejectedAnswersHighlighted,
            CommentHighlighted: r.CommentHighlighted,
            SourceHighlighted: r.SourceHighlighted,
            Authors: ParseAuthors(r.Authors),
            IsAdult: r.IsAdult,
            Rank: r.Rank
        )).ToList();

        return Ok(new ApiSearchResponse(q, dtos.Count, dtos));
    }

    /// <summary>
    /// Converts a <see cref="PackageType"/> to its public API string representation.
    /// </summary>
    internal static string ToGameTypeString(PackageType type) => type switch
    {
        PackageType.Shvager => "shvager",
        _ => "www"
    };

    /// <summary>
    /// Parses the pipe-separated "id:name" author string from SearchResult into structured DTOs.
    /// </summary>
    internal static List<ApiAuthorDto> ParseAuthors(string? authorsString)
    {
        if (string.IsNullOrEmpty(authorsString))
            return [];

        var result = new List<ApiAuthorDto>();
        foreach (var entry in authorsString.Split('|'))
        {
            var colonIdx = entry.IndexOf(':');
            if (colonIdx <= 0 || colonIdx >= entry.Length - 1)
                continue;

            if (!int.TryParse(entry.AsSpan(0, colonIdx), out var id))
                continue;

            var fullName = entry[(colonIdx + 1)..];
            var spaceIdx = fullName.IndexOf(' ');
            if (spaceIdx > 0)
            {
                result.Add(new ApiAuthorDto(id, fullName[..spaceIdx], fullName[(spaceIdx + 1)..]));
            }
            else
            {
                result.Add(new ApiAuthorDto(id, fullName, ""));
            }
        }

        return result;
    }
}
