namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Represents a block within a tour. A tour can optionally have blocks,
/// where each block has its own editors and preamble.
/// When a tour has blocks, questions belong to blocks rather than directly to the tour.
/// </summary>
public class Block
{
    public int Id { get; set; }

    /// <summary>Order of the block within the tour (0-based).</summary>
    public int OrderIndex { get; set; }

    /// <summary>Optional block name. If empty, displays as "Блок {index}".</summary>
    public string? Name { get; set; }

    /// <summary>Preamble - information from editors about the block (Преамбула).</summary>
    public string? Preamble { get; set; }

    /// <summary>Block editors/authors.</summary>
    public List<Author> Editors { get; set; } = [];

    // Navigation properties
    public int TourId { get; set; }
    public Tour Tour { get; set; } = null!;
    public List<Question> Questions { get; set; } = [];

    /// <summary>
    /// Gets the display name for the block using the provided 1-based block number.
    /// If Name is set, returns the name. Otherwise, returns "Блок {blockNumber}".
    /// </summary>
    /// <param name="blockNumber">1-based block number within the package.</param>
    public string GetDisplayName(int blockNumber) => !string.IsNullOrWhiteSpace(Name)
        ? Name
        : $"Блок {blockNumber}";

    /// <summary>
    /// Gets the display name for the block.
    /// If Name is set, returns the name. Otherwise, returns "Блок" (use GetDisplayName for numbered blocks).
    /// </summary>
    public string DisplayName => !string.IsNullOrWhiteSpace(Name)
        ? Name
        : "Блок";
}
