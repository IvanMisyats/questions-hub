using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.Blazor.Infrastructure.RateLimiting;

namespace QuestionsHub.Blazor.Controllers.Api.Manage;

/// <summary>The agent's identity: who the token acts as and what it may do.</summary>
public record AgentMeResponse(AgentUserDto User, AgentTokenDto Token);

public record AgentUserDto(string Id, string Name, IReadOnlyList<string> Roles);

/// <param name="PackageIds">Packages the token is limited to; null = every package the user can edit.</param>
public record AgentTokenDto(int Id, string Name, string Scope, DateTime ExpiresAt, IReadOnlyList<int>? PackageIds);

[ApiController]
[Route("api/v1/manage")]
[Authorize(Policy = AgentPolicies.Read)]
[EnableRateLimiting(RateLimitPolicies.IpManage)]
[ClientRateLimit(RateLimitBudgets.AgentRead)]
public class ManageMeController : ControllerBase
{
    /// <summary>Who the token acts as, its scope, expiry and package allowlist.</summary>
    [HttpGet("me")]
    public ActionResult<AgentMeResponse> Me() => Ok(Describe(User));

    /// <summary>The agent principal as returned by <c>/me</c> and the MCP <c>whoami</c> tool.</summary>
    public static AgentMeResponse Describe(ClaimsPrincipal agent)
    {
        var user = new AgentUserDto(
            agent.GetUserId()!,
            agent.Identity?.Name ?? "",
            agent.FindAll(ClaimTypes.Role).Select(c => c.Value).Order().ToList());

        var token = new AgentTokenDto(
            agent.GetTokenId()!.Value,
            agent.FindFirst(AgentClaims.TokenName)?.Value ?? "",
            Enum.TryParse<TokenScope>(agent.FindFirst(AgentClaims.Scope)?.Value, out var scope) ? AgentApiNames.Of(scope) : "",
            DateTime.Parse(agent.FindFirst(AgentClaims.ExpiresAt)!.Value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind),
            agent.GetPackageAllowlist()?.Order().ToList());

        return new AgentMeResponse(user, token);
    }
}
