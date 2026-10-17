using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.Blazor.Infrastructure.Api;
using QuestionsHub.Blazor.Infrastructure.Import;
using QuestionsHub.Blazor.Infrastructure.RateLimiting;

namespace QuestionsHub.Blazor.Controllers.Api.Manage;

[ApiController]
[Route("api/v1/manage/imports")]
[Authorize(Policy = AgentPolicies.Read)]
[EnableRateLimiting(RateLimitPolicies.IpManage)]
[ClientRateLimit(RateLimitBudgets.AgentRead)]
public sealed class ManageImportsController(AgentImportService imports) : ControllerBase
{
    public const long MaxBodyBytes = 55L * 1024 * 1024;

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct) => Execute(async () => Ok(await imports.List(User, ct)));

    [HttpGet("{jobId:guid}")]
    public Task<IActionResult> Get(Guid jobId, CancellationToken ct) => Execute(async () => Ok(await imports.Get(User, jobId, ct)));

    // The hourly agent.import budget is charged by the service, only for uploads that pass admission:
    // a refused upload does not use it up. The request itself counts against agent.read.
    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    // No action parameters: even binding CancellationToken would create MVC form value providers,
    // which can read multipart data before the service's authorization/admission checks.
    public Task<IActionResult> Enqueue() => Execute(async () =>
    {
        var job = await imports.Enqueue(User, ReadUpload, HttpContext.RequestAborted);
        return AcceptedAtAction(nameof(Get), new { jobId = job.JobId }, job);
    });

    private async Task<ImportUpload> ReadUpload(CancellationToken ct)
    {
        if (Request.ContentLength > MaxBodyBytes)
            throw new BadHttpRequestException("Request body is too large.", StatusCodes.Status413PayloadTooLarge);
        if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var contentType)
            || !contentType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            throw new AgentImportException(400, "Expected multipart/form-data.");

        IFormCollection form;
        try
        {
            form = await Request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = MaxBodyBytes }, ct);
        }
        catch (Exception ex) when (ApiErrorResponses.IsPayloadTooLarge(ex))
        {
            throw; // The JSON error middleware handles wrapped transport and multipart size failures.
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or BadHttpRequestException)
        {
            throw new AgentImportException(400, "Malformed multipart form.");
        }

        if (form.Files.Count != 1 || form.Files[0].Name != "file" || form.Count != 1
            || !form.TryGetValue("gameType", out var types) || types.Count != 1)
            throw new AgentImportException(400, "Provide exactly one file and one gameType field.");
        var type = types[0]?.ToLowerInvariant() switch
        {
            "www" => PackageType.Www,
            "shvager" => PackageType.Shvager,
            _ => throw new AgentImportException(400, "gameType must be www or shvager.")
        };
        var file = form.Files[0];
        return new(file.FileName, file.OpenReadStream(), file.Length, type);
    }

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (AgentImportException ex)
        {
            if (ex.RetryAfterSeconds is { } seconds)
                Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
        catch (ValidationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
    }
}
