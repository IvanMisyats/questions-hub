using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.Blazor.Infrastructure.RateLimiting;

namespace QuestionsHub.Blazor.Controllers.Api.Manage;

/// <summary>
/// Agent read API: packages the token can edit (drafts included), their full editable tree, and
/// author/tag lookups. A package outside the user's editing rights or the token's allowlist is 404.
/// </summary>
[ApiController]
[Route("api/v1/manage")]
[Authorize(Policy = AgentPolicies.Read)]
[EnableRateLimiting(RateLimitPolicies.IpManage)]
[ClientRateLimit(RateLimitBudgets.AgentRead)]
public class ManagePackagesController(AgentPackageReader reader) : ControllerBase
{
    /// <summary>Packages the token can edit, newest first.</summary>
    /// <param name="status">Optional filter: draft, published or archived.</param>
    [HttpGet("packages")]
    public async Task<ActionResult<ManagePackageListResponse>> List(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!AgentPackageReader.TryParseStatus(status, out var statusFilter))
            return BadRequest(new { error = AgentPackageReader.InvalidStatusError });

        return Ok(await reader.ListPackages(User, statusFilter, page, pageSize, ct));
    }

    /// <summary>The full editable tree of a package, with its content <c>version</c>.</summary>
    [HttpGet("packages/{id:int}")]
    public async Task<ActionResult<ManagePackageDto>> Get(int id, CancellationToken ct)
    {
        ManagePackageDto? package;
        try
        {
            package = await reader.GetPackage(User, id, ct);
        }
        catch (AgentApiBusyException ex)
        {
            Response.Headers.RetryAfter = "5";
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }

        return package == null ? NotFound(new { error = "Package not found." }) : Ok(package);
    }

    /// <summary>Authors whose first or last name starts with <paramref name="search"/>.</summary>
    [HttpGet("authors")]
    public async Task<ActionResult<IReadOnlyList<ManageAuthorDto>>> Authors([FromQuery] string? search, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(search))
            return BadRequest(new { error = "Query parameter 'search' is required." });

        return Ok(await reader.SearchAuthors(search, ct));
    }

    /// <summary>Tags matching <paramref name="search"/>.</summary>
    [HttpGet("tags")]
    public async Task<ActionResult<IReadOnlyList<ManageTagDto>>> Tags([FromQuery] string? search, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(search))
            return BadRequest(new { error = "Query parameter 'search' is required." });

        return Ok(await reader.SearchTags(search, ct));
    }
}
