using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuestionsHub.Blazor.Infrastructure;
using QuestionsHub.Blazor.Infrastructure.Api;

namespace QuestionsHub.Blazor.Controllers.Api.V1;

[ApiController]
[Route("api/v1")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.Scheme)]
[EnableCors("PublicApi")]
[EnableRateLimiting("api_general")]
public class MetadataController : ControllerBase
{
    private readonly PackageListService _packageListService;
    private readonly TagService _tagService;

    public MetadataController(
        PackageListService packageListService,
        TagService tagService)
    {
        _packageListService = packageListService;
        _tagService = tagService;
    }

    /// <summary>
    /// Get editors for filter dropdowns. Optionally search by name.
    /// </summary>
    [HttpGet("editors")]
    public async Task<IActionResult> GetEditors([FromQuery] string? search = null)
    {
        var editors = await _packageListService.GetEditorsForFilter(search);
        return Ok(new { editors });
    }

    /// <summary>
    /// Get popular tags across published packages.
    /// </summary>
    [HttpGet("tags/popular")]
    public async Task<IActionResult> GetPopularTags([FromQuery] int count = 10)
    {
        count = Math.Clamp(count, 1, 50);
        var tags = await _tagService.GetPopularPublished(count);
        return Ok(new { tags });
    }
}
