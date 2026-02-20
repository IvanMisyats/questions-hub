using System.ComponentModel.DataAnnotations.Schema;

namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Represents a tour (round) within a package.
/// </summary>
public class Tour
{
    public int Id { get; set; }

    /// <summary>Physical order of the tour within the package (0-based). Source of truth for ordering.</summary>
    public int OrderIndex { get; set; }

    /// <summary>Tour type: Regular, Warmup, or Shootout.</summary>
    public TourType Type { get; set; } = TourType.Regular;

    /// <summary>Whether this is a warmup tour.</summary>
    [NotMapped]
    public bool IsWarmup => Type == TourType.Warmup;

    /// <summary>Whether this is a shootout (Перестрілка) tour.</summary>
    [NotMapped]
    public bool IsShootout => Type == TourType.Shootout;

    /// <summary>Whether this is a special (non-regular) tour.</summary>
    [NotMapped]
    public bool IsSpecial => Type != TourType.Regular;

    /// <summary>Tour number for display (e.g., "1", "2"). For main tours, assigned sequentially. For warmup, may be "0" or empty.</summary>
    public required string Number { get; set; }

    /// <summary>Tour editors/authors.</summary>
    public List<Author> Editors { get; set; } = [];

    /// <summary>Preamble - information from editors about the tour, usually contains list of testers (Преамбула).</summary>
    public string? Preamble { get; set; }

    /// <summary>Commentary for the entire tour.</summary>
    public string? Comment { get; set; }

    // Navigation properties
    public int PackageId { get; set; }
    public Package Package { get; set; } = null!;
    public List<Question> Questions { get; set; } = [];
    public List<Block> Blocks { get; set; } = [];

    /// <summary>Whether this tour has blocks.</summary>
    [NotMapped]
    public bool HasBlocks => Blocks.Count > 0;

    /// <summary>
    /// Gets all editors for the tour. If tour has blocks, returns all unique editors from all blocks.
    /// Otherwise, returns the tour's own editors.
    /// </summary>
    [NotMapped]
    public IEnumerable<Author> AllEditors => HasBlocks
        ? Blocks.SelectMany(b => b.Editors).DistinctBy(a => a.Id)
        : Editors;
}
