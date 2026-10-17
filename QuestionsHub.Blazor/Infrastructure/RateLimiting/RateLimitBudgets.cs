using System.Security.Claims;

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
    public const string AgentExport = "agent.export";
    public const string AgentImport = "agent.import";
    public const string UserFavorites = "user.favorites";

    /// <summary>Claim carrying the API client id (set by <c>ApiKeyAuthenticationHandler</c>).</summary>
    public const string ApiClientIdClaim = Api.ApiKeyAuthenticationOptions.ClientIdClaim;

    /// <summary>Claim carrying the personal access token id.</summary>
    public const string TokenIdClaim = AgentApi.AgentClaims.TokenId;

    /// <summary>
    /// Claim carrying the user id, on both cookie and PAT principals. User-keyed budgets (such as
    /// imports) are shared across all tokens of one user; each named budget has its own partitions.
    /// </summary>
    public const string UserIdClaim = ClaimTypes.NameIdentifier;

    /// <summary>All budgets with the claim that identifies the principal and the configured limit.</summary>
    public static IReadOnlyList<(string Name, string ClaimType, Func<RateLimitSettings, int> Limit, TimeSpan Window)> All { get; } =
    [
        (AppGeneral, ApiClientIdClaim, s => s.AppGeneralPerMinute, TimeSpan.FromMinutes(1)),
        (AppDetail, ApiClientIdClaim, s => s.AppDetailPerMinute, TimeSpan.FromMinutes(1)),
        (AppSearch, ApiClientIdClaim, s => s.AppSearchPerMinute, TimeSpan.FromMinutes(1)),
        (AgentRead, TokenIdClaim, s => s.AgentReadPerMinute, TimeSpan.FromMinutes(1)),
        (AgentWrite, TokenIdClaim, s => s.AgentWritePerMinute, TimeSpan.FromMinutes(1)),
        (AgentExport, TokenIdClaim, s => s.AgentExportPerMinute, TimeSpan.FromMinutes(1)),
        (AgentImport, UserIdClaim, s => s.AgentImportPerHour, TimeSpan.FromHours(1)),
        (UserFavorites, UserIdClaim, s => s.UserFavoritesPerMinute, TimeSpan.FromMinutes(1)),
    ];

    public static TimeSpan WindowFor(string budget) => All.Single(b => b.Name == budget).Window;

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
