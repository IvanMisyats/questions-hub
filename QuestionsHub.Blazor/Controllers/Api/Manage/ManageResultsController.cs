using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.Blazor.Infrastructure.RateLimiting;

namespace QuestionsHub.Blazor.Controllers.Api.Manage;

/// <summary>The body of <c>POST /api/v1/manage/packages/{id}/results</c>.</summary>
public record AttachResultsSourceRequest(string? Url);

/// <summary>
/// Agent API for a package's tournament-results sources: list, attach (with a synchronous load), reload
/// and delete — the operations of the editor's «Результати» card. Everything outside the token's editing
/// rights is 404; a failed platform load is reported in the returned source, not as an HTTP error.
/// </summary>

[ApiController]
[Route("api/v1/manage/packages/{packageId:int}/results")]
[Authorize(Policy = AgentPolicies.Read)]
[EnableRateLimiting(RateLimitPolicies.IpManage)]
[ClientRateLimit(RateLimitBudgets.AgentRead)]
public class ManageResultsController(AgentResultsService results) : ControllerBase
{
    /// <summary>Sources of the package, oldest first.</summary>
    [HttpGet]
    public Task<IActionResult> List(int packageId, CancellationToken ct) =>
        Execute(async () => Ok(await results.List(User, packageId, ct)));

    /// <summary>Attaches a results link and loads it (Rating, OpenQuiz); 201 with the source.</summary>
    [HttpPost]
    [Authorize(Policy = AgentPolicies.Write)]
    [RequestSizeLimit(Infrastructure.Api.ApiErrorResponses.AgentMaxBodyBytes)]
    public Task<IActionResult> Attach(int packageId, [FromBody] AttachResultsSourceRequest body, CancellationToken ct) =>
        Execute(async () => StatusCode(StatusCodes.Status201Created, await results.Attach(User, packageId, body.Url, ct)));

    /// <summary>Refetches a source from its platform.</summary>
    [HttpPost("{sourceId:int}/reload")]
    [Authorize(Policy = AgentPolicies.Write)]
    public Task<IActionResult> Reload(int packageId, int sourceId, CancellationToken ct) =>
        Execute(async () => Ok(await results.Reload(User, packageId, sourceId, ct)));

    /// <summary>Deletes a source and re-aggregates the package statistics.</summary>
    [HttpDelete("{sourceId:int}")]
    [Authorize(Policy = AgentPolicies.Write)]
    public Task<IActionResult> Delete(int packageId, int sourceId, CancellationToken ct) => Execute(async () =>
    {
        await results.Delete(User, packageId, sourceId, ct);
        return NoContent();
    });

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (AgentResultsException ex)
        {
            if (ex.StatusCode == StatusCodes.Status429TooManyRequests)
                Response.Headers.RetryAfter = "60";
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }
}
