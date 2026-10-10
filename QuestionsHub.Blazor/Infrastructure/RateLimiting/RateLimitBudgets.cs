namespace QuestionsHub.Blazor.Infrastructure.RateLimiting;

/// <summary>
/// Names of the pre-authentication, per-IP rate-limiting policies (ASP.NET rate-limiter middleware).
/// </summary>
public static class RateLimitPolicies
{
    public const string IpPublicApi = "ip_public_api";
    public const string IpManage = "ip_manage";
    public const string IpAuth = "ip_auth";
}

/// <summary>
/// Post-authentication budgets, keyed by a claim of the validated principal.
/// </summary>
public static class RateLimitBudgets
{
    public const string AppGeneral = "app.general";
    public const string AppDetail = "app.detail";
    public const string AppSearch = "app.search";
    public const string AgentRead = "agent.read";
    public const string AgentWrite = "agent.write";

    /// <summary>Claim carrying the API client id (set by <c>ApiKeyAuthenticationHandler</c>).</summary>
    public const string ApiClientIdClaim = Api.ApiKeyAuthenticationOptions.ClientIdClaim;

    /// <summary>Claim carrying the personal access token id.</summary>
    public const string TokenIdClaim = AgentApi.AgentClaims.TokenId;

    /// <summary>All budgets with the claim that identifies the principal and the configured limit.</summary>
    public static IReadOnlyList<(string Name, string ClaimType, Func<RateLimitSettings, int> Limit)> All { get; } =
    [
        (AppGeneral, ApiClientIdClaim, s => s.AppGeneralPerMinute),
        (AppDetail, ApiClientIdClaim, s => s.AppDetailPerMinute),
        (AppSearch, ApiClientIdClaim, s => s.AppSearchPerMinute),
        (AgentRead, TokenIdClaim, s => s.AgentReadPerMinute),
        (AgentWrite, TokenIdClaim, s => s.AgentWritePerMinute),
    ];

    /// <summary>The claim type identifying the principal for <paramref name="budget"/>.</summary>
    public static string ClaimTypeFor(string budget)
    {
        foreach (var b in All)
        {
            if (b.Name == budget)
                return b.ClaimType;
        }

        throw new ArgumentException($"Unknown rate-limit budget '{budget}'.", nameof(budget));
    }
}
