namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Represents an API client that authenticates via API key.
/// Keys are stored as SHA-256 hashes. The raw key is shown once at creation.
/// </summary>
public class ApiClient
{
    public int Id { get; set; }

    /// <summary>Human-readable client name (e.g., "Mobile App v1").</summary>
    public required string Name { get; set; }

    /// <summary>SHA-256 hash of the API key.</summary>
    public required string KeyHash { get; set; }

    /// <summary>First 8 characters of the raw key, for identification in logs.</summary>
    public required string KeyPrefix { get; set; }

    /// <summary>Whether this client is allowed to make requests.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When the client was registered.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Last time a request was made with this key (updated periodically, not every request).</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>Contact email of the developer who owns this key.</summary>
    public string? ContactEmail { get; set; }
}
