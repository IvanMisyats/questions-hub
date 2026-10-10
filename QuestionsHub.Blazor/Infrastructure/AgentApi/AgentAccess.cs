using System.Globalization;
using System.Security.Claims;
using QuestionsHub.Blazor.Controllers;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.AgentApi;

/// <summary>Claim types set on principals authenticated with a personal access token.</summary>
public static class AgentClaims
{
    public const string TokenId = "pat_id";
    public const string TokenName = "pat_name";
    public const string Scope = "pat_scope";
    public const string ExpiresAt = "pat_expires_at";

    /// <summary>Comma-separated package ids; absent when the token is unrestricted.</summary>
    public const string Packages = "pat_packages";
}

/// <summary>Authorization policy names for the agent API.</summary>
public static class AgentPolicies
{
    /// <summary>Any valid token of an editor/admin.</summary>
    public const string Read = "AgentRead";

    /// <summary>A valid token with the <see cref="TokenScope.ReadWrite"/> scope.</summary>
    public const string Write = "AgentWrite";
}

/// <summary>
/// Package-level checks for agent principals: the user's own editing rights, narrowed by the token's
/// package allowlist. Failures surface as 404 so a token never learns whether a package exists.
/// </summary>
public static class AgentAccess
{
    extension(ClaimsPrincipal user)
    {
        /// <summary>The token id, or null for principals that did not authenticate with a token.</summary>
        public int? GetTokenId() =>
            int.TryParse(user.FindFirst(AgentClaims.TokenId)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? id
                : null;

        /// <summary>Packages the token is limited to, or null when it is unrestricted.</summary>
        public IReadOnlySet<int>? GetPackageAllowlist()
        {
            var value = user.FindFirst(AgentClaims.Packages)?.Value;
            if (value == null)
                return null;

            return value
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => int.Parse(id, NumberStyles.None, CultureInfo.InvariantCulture))
                .ToHashSet();
        }

        /// <summary>
        /// Whether the principal may edit <paramref name="package"/>: admin, or editor-owner (the same
        /// rule as the package editor UI), and the package is in the token's allowlist if it has one.
        /// </summary>
        public bool CanEditAsAgent(Package package)
        {
            if (!user.CanAccessPackage(package))
                return false;

            var allowlist = user.GetPackageAllowlist();
            return allowlist == null || allowlist.Contains(package.Id);
        }
    }
}
