using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Controllers.Api.V1.Dto;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure;
using QuestionsHub.Blazor.Infrastructure.Api;

namespace QuestionsHub.Blazor.Controllers.Api.V1;

[ApiController]
[Route("api/v1/packages")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.Scheme)]
[EnableCors("PublicApi")]
[EnableRateLimiting("api_general")]
public class PackagesController : ControllerBase
{
    private readonly PackageListService _packageListService;
    private readonly IDbContextFactory<QuestionsHubDbContext> _dbContextFactory;
    private readonly IConfiguration _configuration;

    private static readonly PackageAccessContext AnonymousContext = new(
        IsAdmin: false, IsEditor: false, HasVerifiedEmail: false, UserId: null);

    public PackagesController(
        PackageListService packageListService,
        IDbContextFactory<QuestionsHubDbContext> dbContextFactory,
        IConfiguration configuration)
    {
        _packageListService = packageListService;
        _dbContextFactory = dbContextFactory;
        _configuration = configuration;
    }

    /// <summary>
    /// Browse/filter published public packages with pagination.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PackageListResult>> List(
        [FromQuery] string? search = null,
        [FromQuery] int? editor = null,
        [FromQuery] int? tag = null,
        [FromQuery] PackageSortField sort = PackageSortField.PublicationDate,
        [FromQuery] SortDirection dir = SortDirection.Desc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);

        var filter = new PackageListFilter(
            TitleSearch: search,
            EditorId: editor,
            TagId: tag,
            SortField: sort,
            SortDir: dir,
            Page: page,
            PageSize: pageSize);

        var result = await _packageListService.SearchPackages(filter, AnonymousContext);
        return Ok(result);
    }

    /// <summary>
    /// Get full package detail with tours, blocks, and questions.
    /// Returns 404 for non-existent or non-public packages.
    /// </summary>
    [HttpGet("{id:int}")]
    [EnableRateLimiting("api_detail")]
    public async Task<ActionResult<ApiPackageDetailDto>> Detail(int id)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var package = await context.Packages
            .AsNoTracking()
            .Where(p => p.Id == id
                        && p.Status == PackageStatus.Published
                        && p.AccessLevel == PackageAccessLevel.All)
            .Include(p => p.Tags)
            .Include(p => p.PackageEditors)
            .Include(p => p.Tours.OrderBy(t => t.OrderIndex))
                .ThenInclude(t => t.Editors)
            .Include(p => p.Tours)
                .ThenInclude(t => t.Blocks.OrderBy(b => b.OrderIndex))
                    .ThenInclude(b => b.Editors)
            .Include(p => p.Tours)
                .ThenInclude(t => t.Blocks)
                    .ThenInclude(b => b.Questions.OrderBy(q => q.OrderIndex))
                        .ThenInclude(q => q.Authors)
            .Include(p => p.Tours)
                .ThenInclude(t => t.Questions.OrderBy(q => q.OrderIndex))
                    .ThenInclude(q => q.Authors)
            .AsSplitQuery()
            .FirstOrDefaultAsync();

        if (package == null)
            return NotFound(new { error = "Package not found." });

        var siteBaseUrl = _configuration["SiteUrl"] ?? "https://questions.com.ua";
        var isAdult = TagService.IsAdultContent(package.Tags);

        var dto = MapPackageToDto(package, siteBaseUrl, isAdult);
        return Ok(dto);
    }

    private static ApiPackageDetailDto MapPackageToDto(Package package, string siteBaseUrl, bool isAdult)
    {
        var editors = package.Editors
            .Select(a => new ApiAuthorDto(a.Id, a.FirstName, a.LastName))
            .ToList();

        var tags = package.Tags
            .Select(t => new ApiTagDto(t.Id, t.Name))
            .OrderBy(t => t.Name)
            .ToList();

        var numberingMode = package.NumberingMode switch
        {
            QuestionNumberingMode.Global => "global",
            QuestionNumberingMode.PerTour => "perTour",
            QuestionNumberingMode.Manual => "manual",
            _ => "global"
        };

        var tours = package.Tours
            .OrderBy(t => t.OrderIndex)
            .Select(t => MapTourToDto(t, siteBaseUrl))
            .ToList();

        return new ApiPackageDetailDto(
            Id: package.Id,
            Title: package.Title,
            Description: package.Description,
            Preamble: package.Preamble,
            PlayedFrom: package.PlayedFrom,
            PlayedTo: package.PlayedTo,
            PublicationDate: package.PublicationDate,
            QuestionsCount: package.TotalQuestions,
            NumberingMode: numberingMode,
            Editors: editors,
            Tags: tags,
            IsAdult: isAdult,
            Tours: tours);
    }

    private static ApiTourDto MapTourToDto(Tour tour, string siteBaseUrl)
    {
        var tourType = tour.Type switch
        {
            TourType.Regular => "regular",
            TourType.Warmup => "warmup",
            TourType.Shootout => "shootout",
            _ => "regular"
        };

        var tourEditors = tour.AllEditors
            .Select(a => new ApiAuthorDto(a.Id, a.FirstName, a.LastName))
            .ToList();

        var blocks = tour.Blocks
            .OrderBy(b => b.OrderIndex)
            .Select((b, i) => MapBlockToDto(b, i + 1, siteBaseUrl))
            .ToList();

        // Questions directly on the tour (not in any block)
        var directQuestions = tour.Questions
            .Where(q => q.BlockId == null)
            .OrderBy(q => q.OrderIndex)
            .Select(q => MapQuestionToDto(q, siteBaseUrl))
            .ToList();

        return new ApiTourDto(
            Id: tour.Id,
            Number: tour.Number,
            Type: tourType,
            Preamble: tour.Preamble,
            Comment: tour.Comment,
            Editors: tourEditors,
            Blocks: blocks,
            Questions: directQuestions);
    }

    private static ApiBlockDto MapBlockToDto(Block block, int blockNumber, string siteBaseUrl)
    {
        var blockEditors = block.Editors
            .Select(a => new ApiAuthorDto(a.Id, a.FirstName, a.LastName))
            .ToList();

        var questions = block.Questions
            .OrderBy(q => q.OrderIndex)
            .Select(q => MapQuestionToDto(q, siteBaseUrl))
            .ToList();

        return new ApiBlockDto(
            Id: block.Id,
            Name: block.GetDisplayName(blockNumber),
            Preamble: block.Preamble,
            Editors: blockEditors,
            Questions: questions);
    }

    private static ApiQuestionDto MapQuestionToDto(Question question, string siteBaseUrl)
    {
        var authors = question.Authors
            .Select(a => new ApiAuthorDto(a.Id, a.FirstName, a.LastName))
            .ToList();

        return new ApiQuestionDto(
            Id: question.Id,
            Number: question.Number,
            HostInstructions: question.HostInstructions,
            Text: question.Text,
            Answer: question.Answer,
            HandoutText: question.HandoutText,
            HandoutUrl: ToAbsoluteUrl(question.HandoutUrl, siteBaseUrl),
            AcceptedAnswers: question.AcceptedAnswers,
            RejectedAnswers: question.RejectedAnswers,
            Comment: question.Comment,
            CommentAttachmentUrl: ToAbsoluteUrl(question.CommentAttachmentUrl, siteBaseUrl),
            Source: question.Source,
            Authors: authors);
    }

    internal static string? ToAbsoluteUrl(string? relativeUrl, string siteBaseUrl)
    {
        if (string.IsNullOrEmpty(relativeUrl))
            return null;

        // Already absolute
        if (relativeUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            relativeUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return relativeUrl;

        return siteBaseUrl.TrimEnd('/') + "/" + relativeUrl.TrimStart('/');
    }
}
