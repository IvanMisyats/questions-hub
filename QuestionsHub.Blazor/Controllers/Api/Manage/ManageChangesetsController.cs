using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.Blazor.Infrastructure.AgentApi.Changesets;
using QuestionsHub.Blazor.Infrastructure.RateLimiting;

namespace QuestionsHub.Blazor.Controllers.Api.Manage;

/// <summary>The body of <c>POST /api/v1/manage/packages/{id}/changesets</c> (see docs/API.md, "Agent API").</summary>
public record ChangesetRequestBody(Guid? RequestId, string? ExpectedVersion, string? Summary, bool DryRun, JsonElement Operations);

/// <summary>
/// Agent write API: apply or preview a changeset on a package, and read the package's changeset
/// history. Everything outside the token's editing rights is 404.
/// </summary>
[ApiController]
[Route("api/v1/manage/packages/{packageId:int}/changesets")]
[Authorize(Policy = AgentPolicies.Read)]
[EnableRateLimiting(RateLimitPolicies.IpManage)]
[ClientRateLimit(RateLimitBudgets.AgentRead)]
public class ManageChangesetsController(PackageChangesetService changesets) : ControllerBase
{
    /// <summary>Request body limit (Kestrel); nginx allows 1 MB on the manage location.</summary>
    public const long MaxBodyBytes = Infrastructure.Api.ApiErrorResponses.AgentMaxBodyBytes;

    /// <summary>Applies the changeset atomically, or previews it with <c>dryRun: true</c>.</summary>
    [HttpPost]
    [Authorize(Policy = AgentPolicies.Write)]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Apply(int packageId, [FromBody] ChangesetRequestBody body, CancellationToken ct)
    {
        var request = new ChangesetRequest(body.RequestId, body.ExpectedVersion, body.Summary, body.DryRun, body.Operations);
        var result = await changesets.Execute(User, packageId, request, ct);

        switch (result.Status)
        {
            case ChangesetStatus.Ok:
                return Ok(result.Response);
            case ChangesetStatus.Invalid when result.OperationIndex is { } operationIndex:
                return UnprocessableEntity(new { error = result.Error, operationIndex });
            case ChangesetStatus.Invalid:
                return BadRequest(new { error = result.Error });
            case ChangesetStatus.NotFound:
                return NotFound(new { error = result.Error });
            case ChangesetStatus.Conflict:
                return Conflict(new { error = result.Error });
            case ChangesetStatus.RateLimited:
                Response.Headers.RetryAfter = "60";
                return StatusCode(StatusCodes.Status429TooManyRequests, new { error = result.Error });
            case ChangesetStatus.Busy:
                Response.Headers.RetryAfter = "5";
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = result.Error });
            default:
                throw new InvalidOperationException($"Unexpected changeset status {result.Status}.");
        }
    }

    /// <summary>Applied changesets of the package, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<ChangesetHistoryPage>> History(
        int packageId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!await changesets.CanEdit(User, packageId, ct))
            return NotFound(new { error = "Package not found." });

        return Ok(await changesets.GetHistory(packageId, page, pageSize, ct));
    }

    /// <summary>One applied changeset with its operations and diff.</summary>
    [HttpGet("{changesetId:int}")]
    public async Task<ActionResult<ChangesetDetailDto>> Detail(int packageId, int changesetId, CancellationToken ct)
    {
        if (!await changesets.CanEdit(User, packageId, ct))
            return NotFound(new { error = "Package not found." });

        var detail = await changesets.GetChangeset(packageId, changesetId, ct);
        return detail == null ? NotFound(new { error = "Changeset not found." }) : Ok(detail);
    }
}
