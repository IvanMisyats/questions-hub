using System.ComponentModel.DataAnnotations.Schema;

namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Represents a question package (tournament package).
/// </summary>
public class Package
{
    public int Id { get; set; }

    /// <summary>Package title.</summary>
    public required string Title { get; set; }

    /// <summary>Start date when the package was played (or single date for one-day events).</summary>
    public DateOnly? PlayedFrom { get; set; }

    /// <summary>End date when the package was played (null for single-day events).</summary>
    public DateOnly? PlayedTo { get; set; }

    /// <summary>URL where the package was originally obtained (e.g., from .qhub import).</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Package description or notes.</summary>
    public string? Description { get; set; }

    /// <summary>Preamble - information from editors about the package, usually contains list of testers (Преамбула).</summary>
    public string? Preamble { get; set; }

    /// <summary>Total number of questions in the package.</summary>
    public int TotalQuestions { get; set; }

    /// <summary>Package visibility status.</summary>
    public PackageStatus Status { get; set; } = PackageStatus.Draft;

    /// <summary>Access control level - determines who can view the package.</summary>
    public PackageAccessLevel AccessLevel { get; set; } = PackageAccessLevel.All;

    /// <summary>How question numbers are assigned in this package.</summary>
    public QuestionNumberingMode NumberingMode { get; set; } = QuestionNumberingMode.Global;

    /// <summary>When true, all tours share the same editors defined at package level.</summary>
    public bool SharedEditors { get; set; }

    /// <summary>Date and time when the package was published (UTC).</summary>
    public DateTime? PublicationDate { get; set; }

    /// <summary>The ID of the user who owns this package.</summary>
    public string? OwnerId { get; set; }

    /// <summary>The user who owns this package.</summary>
    public ApplicationUser? Owner { get; set; }

    // Navigation properties
    public List<Tour> Tours { get; set; } = [];

    /// <summary>Editors assigned directly to the package (used when SharedEditors is true).</summary>
    public List<Author> PackageEditors { get; set; } = [];

    /// <summary>Tags associated with this package.</summary>
    public List<Tag> Tags { get; set; } = [];

    /// <summary>
    /// Gets the effective editors for the package.
    /// When SharedEditors is true, returns PackageEditors.
    /// Otherwise, returns all unique editors from all tours (computed, not stored in DB).
    /// </summary>
    [NotMapped]
    public IEnumerable<Author> Editors => SharedEditors
        ? PackageEditors
        : Tours.SelectMany(t => t.AllEditors).DistinctBy(a => a.Id);
}
