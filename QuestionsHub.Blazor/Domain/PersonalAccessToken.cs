namespace QuestionsHub.Blazor.Domain;

/// <summary>What a personal access token may do.</summary>
public enum TokenScope
{
    /// <summary>Read packages the user can edit.</summary>
    Read = 0,

    /// <summary>Read and apply changesets.</summary>
    ReadWrite = 1
}

/// <summary>
/// A personal access token lets an agent act on behalf of its user through the agent API
/// (<c>/api/v1/manage</c>, <c>/mcp</c>). Only the SHA-256 hash is stored; the raw token is shown once.
/// The token never grants more than the user currently has (roles are re-checked on every request)
/// and can be narrowed to a package allowlist.
/// </summary>
public class PersonalAccessToken
{
    public int Id { get; set; }

    /// <summary>The user the agent acts as.</summary>
    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }

    /// <summary>Human-readable label, e.g. "Claude — пакет 512".</summary>
    public required string Name { get; set; }

    /// <summary>SHA-256 hex of the raw token.</summary>
    public required string TokenHash { get; set; }

    /// <summary>First characters of the raw token, for recognising it in lists.</summary>
    public required string TokenPrefix { get; set; }

    public TokenScope Scope { get; set; }

    /// <summary>Packages the token is limited to; null means every package the user can edit.</summary>
    public List<int>? PackageIds { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>The token stops working after this moment (UTC).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Last successful use (updated at most once a minute).</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>When the token was revoked; null while it is not.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Not revoked and not expired at <paramref name="now"/>.</summary>
    public bool IsActive(DateTime now) => RevokedAt == null && ExpiresAt > now;
}
