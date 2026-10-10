using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuestionsHub.Blazor.Controllers;
using QuestionsHub.Blazor.Infrastructure.AgentApi;

namespace QuestionsHub.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only endpoint guarded by <see cref="AgentPolicies.Write"/>, so the write policy can be tested
/// over HTTP before the real write endpoints exist. Loaded into the test host as an application part.
/// </summary>
[ApiController]
[Route("__test/agent")]
public class AgentPolicyProbeController : ControllerBase
{
    [HttpPost("write")]
    [Authorize(Policy = AgentPolicies.Write)]
    public IActionResult Write() => Ok(new { userId = User.GetUserId() });
}
