using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.Blazor.Infrastructure.RateLimiting;

namespace QuestionsHub.Blazor.Controllers.Api.Manage;

/// <summary>
/// <c>.qhub</c> download of a package the token can edit (any scope), for backups. A weak <c>ETag</c> over
/// the content lets a client re-download only what changed: a matching <c>If-None-Match</c> → 304.
/// </summary>
[ApiController]
[Route("api/v1/manage")]
[Authorize(Policy = AgentPolicies.Read)]
[EnableRateLimiting(RateLimitPolicies.IpManage)]
[ClientRateLimit(RateLimitBudgets.AgentExport)]
public class ManageExportController(AgentPackageExporter exporter) : ControllerBase
{
    /// <summary>Header with the number of local media files that were missing on the server and left out.</summary>
    public const string MissingAssetsHeader = "X-Qhub-Missing-Assets";

    [HttpGet("packages/{id:int}/export")]
    public async Task<IActionResult> Export(int id, CancellationToken ct)
    {
        // With If-Match present, MVC evaluates the preconditions on the built archive (If-Match comes first);
        // only without it may a matching If-None-Match short-circuit to 304 before anything is built.
        var headers = Request.GetTypedHeaders();
        var ifNoneMatch = headers.IfMatch.Count == 0 ? headers.IfNoneMatch : [];

        AgentExportResult result;
        try
        {
            result = await exporter.Export(User, id, ifNoneMatch, ct);
        }
        catch (AgentApiBusyException ex)
        {
            Response.Headers.RetryAfter = "5";
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }

        // Per-user content: shared caches must not keep it, and clients revalidate every time.
        Response.Headers.CacheControl = "private, no-cache";

        switch (result)
        {
            case AgentExportResult.NotModified notModified:
                Response.Headers.ETag = notModified.ETag;
                return StatusCode(StatusCodes.Status304NotModified);

            case AgentExportResult.Archive archive:
                // MVC disposes the stream when it executes the result; this covers the result never running.
                Response.RegisterForDisposeAsync(archive.Content);
                if (archive.MissingAssets > 0)
                    Response.Headers[MissingAssetsHeader] = archive.MissingAssets.ToString(CultureInfo.InvariantCulture);
                return File(archive.Content, "application/zip", archive.FileName,
                    lastModified: null, entityTag: EntityTagHeaderValue.Parse(archive.ETag), enableRangeProcessing: false);

            default:
                return NotFound(new { error = "Package not found." });
        }
    }
}
