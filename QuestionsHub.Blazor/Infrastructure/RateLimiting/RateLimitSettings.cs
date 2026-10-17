namespace QuestionsHub.Blazor.Infrastructure.RateLimiting;

/// <summary>
/// Request budgets, bound from the <c>RateLimits</c> configuration section.
/// "Ip*" limits are pre-authentication admission per client IP; the others are per validated
/// principal (API client, personal access token or signed-in user). See docs/AGENT_API_PLAN.md.
/// </summary>
public class RateLimitSettings
{
    public const string SectionName = "RateLimits";

    /// <summary>Public read API (<c>/api/v1/packages</c>, <c>/search</c>, <c>/editors</c>, <c>/tags</c>), per IP.</summary>
    public int IpPublicApiPerMinute { get; set; } = 60;

    /// <summary>Agent API (<c>/api/v1/manage</c>, <c>/mcp</c>), per IP.</summary>
    public int IpManagePerMinute { get; set; } = 150;

    /// <summary>Login/registration endpoints (<c>/api/Auth</c>), per IP.</summary>
    public int IpAuthPerMinute { get; set; } = 5;

    /// <summary>Public API list/metadata endpoints, per API key.</summary>
    public int AppGeneralPerMinute { get; set; } = 60;

    /// <summary>Public API package detail, per API key.</summary>
    public int AppDetailPerMinute { get; set; } = 30;

    /// <summary>Public API search, per API key.</summary>
    public int AppSearchPerMinute { get; set; } = 20;

    /// <summary>Agent reads (every manage/MCP request), per personal access token.</summary>
    public int AgentReadPerMinute { get; set; } = 120;

    /// <summary>Agent changesets (apply and dry-run) and results writes, per personal access token.</summary>
    public int AgentWritePerMinute { get; set; } = 20;

    /// <summary>Agent <c>.qhub</c> exports (304s included), per personal access token; replaces the read budget there.</summary>
    public int AgentExportPerMinute { get; set; } = 60;

    /// <summary>Agent uploads per hour, shared across every token belonging to one user.</summary>
    public int AgentImportPerHour { get; set; } = 10;

    /// <summary>Adding/removing favorites (<c>/api/favorites</c>), per signed-in user.</summary>
    public int UserFavoritesPerMinute { get; set; } = 60;
}
