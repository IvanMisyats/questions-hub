namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Represents a single question in the game.
/// </summary>
public class Question
{
    public int Id { get; set; }

    /// <summary>Physical order of the question within the tour (0-based).</summary>
    public int OrderIndex { get; set; }

    /// <summary>Question number for display (e.g., "1", "2", "F" for hexadecimal, "0" for warm-up).</summary>
    public required string Number { get; set; }

    /// <summary>The main question text.</summary>
    public required string Text { get; set; }

    /// <summary>Text of handout material for the question.</summary>
    public string? HandoutText { get; set; }

    /// <summary>URL to handout image/attachment.</summary>
    public string? HandoutUrl { get; set; }

    /// <summary>The correct answer.</summary>
    public required string Answer { get; set; }

    /// <summary>Alternative accepted answers (залік).</summary>
    public string? AcceptedAnswers { get; set; }

    /// <summary>Answers that are explicitly not accepted.</summary>
    public string? RejectedAnswers { get; set; }

    /// <summary>Commentary explaining the answer.</summary>
    public string? Comment { get; set; }

    /// <summary>URL to commentary attachment (image for answer).</summary>
    public string? CommentAttachmentUrl { get; set; }

    /// <summary>Source references for the question facts.</summary>
    public string? Source { get; set; }

    /// <summary>Question authors.</summary>
    public List<string> Authors { get; set; } = [];

    // Navigation properties
    public int TourId { get; set; }
    public Tour Tour { get; set; } = null!;
}

/// <summary>
/// Represents a tour (round) within a package.
/// </summary>
public class Tour
{
    public int Id { get; set; }

    /// <summary>Tour number/name (e.g., "1", "2", "Фінал").</summary>
    public required string Number { get; set; }

    /// <summary>Tour title (optional).</summary>
    public string? Title { get; set; }

    /// <summary>Tour editors/authors.</summary>
    public List<string> Editors { get; set; } = [];

    /// <summary>Commentary for the entire tour.</summary>
    public string? Comment { get; set; }

    // Navigation properties
    public int PackageId { get; set; }
    public Package Package { get; set; } = null!;
    public List<Question> Questions { get; set; } = [];
}

/// <summary>
/// Represents a question package (tournament package).
/// </summary>
public class Package
{
    public int Id { get; set; }

    /// <summary>Package title.</summary>
    public required string Title { get; set; }

    /// <summary>Package editors/authors.</summary>
    public List<string> Editors { get; set; } = [];

    /// <summary>Date when the package was played.</summary>
    public DateOnly? PlayedAt { get; set; }

    /// <summary>Package description or notes.</summary>
    public string? Description { get; set; }

    /// <summary>Total number of questions in the package.</summary>
    public int TotalQuestions { get; set; }

    /// <summary>Package visibility status.</summary>
    public PackageStatus Status { get; set; } = PackageStatus.Draft;

    /// <summary>The ID of the user who owns this package.</summary>
    public string? OwnerId { get; set; }

    /// <summary>The user who owns this package.</summary>
    public ApplicationUser? Owner { get; set; }

    // Navigation properties
    public List<Tour> Tours { get; set; } = [];
}
