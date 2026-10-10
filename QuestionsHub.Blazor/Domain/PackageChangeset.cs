namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Audit record of a changeset an agent applied to a package (dry runs are not stored).
/// Holds who did it (user and token, with name snapshots), the request (for replaying retries
/// idempotently) and the resulting diff with real ids and full snapshots of deleted entities.
/// </summary>
public class PackageChangeset
{
    public int Id { get; set; }

    public int PackageId { get; set; }
    public Package? Package { get; set; }

    /// <summary>The user the agent acted as; null if that user was later deleted.</summary>
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    /// <summary>The user's name when the changeset was applied.</summary>
    public required string UserDisplayName { get; set; }

    /// <summary>The token used; null if it was later deleted.</summary>
    public int? TokenId { get; set; }
    public PersonalAccessToken? Token { get; set; }

    /// <summary>The token's name when the changeset was applied.</summary>
    public string? TokenName { get; set; }

    /// <summary>Client-generated id; unique per token so a retried request is applied once.</summary>
    public Guid RequestId { get; set; }

    /// <summary>SHA-256 of the canonical request; the same <see cref="RequestId"/> with another body is a conflict.</summary>
    public required string RequestHash { get; set; }

    /// <summary>The agent's description of the change.</summary>
    public string? Summary { get; set; }

    public DateTime CreatedAt { get; set; }

    public int OperationCount { get; set; }

    /// <summary>Content version (fingerprint) before and after the changeset.</summary>
    public required string VersionBefore { get; set; }
    public required string VersionAfter { get; set; }

    public int TotalQuestionsAfter { get; set; }

    /// <summary>The request's operations (jsonb).</summary>
    public required string OperationsJson { get; set; }

    /// <summary>The diff (jsonb): array of change entries with real ids.</summary>
    public required string ChangesJson { get; set; }

    /// <summary>Warnings returned with the result (jsonb array of strings), if any.</summary>
    public string? WarningsJson { get; set; }
}
