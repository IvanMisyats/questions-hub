namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Represents a tag (label) that can be applied to packages.
/// Tags are user-created and shared across packages.
/// </summary>
public class Tag
{
    public int Id { get; set; }

    /// <summary>Tag name (e.g., "2025", "ЛУК", "кубок"). Stored as-is, uniqueness is case-insensitive.</summary>
    public required string Name { get; set; }

    // Navigation properties
    public List<Package> Packages { get; set; } = [];
}
