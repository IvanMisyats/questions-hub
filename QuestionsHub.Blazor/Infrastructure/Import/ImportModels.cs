using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Represents a block of content extracted from a DOCX document.
/// </summary>
public record DocBlock
{
    public int Index { get; init; }
    public required string Text { get; init; }
    public string? StyleId { get; init; }
    public bool IsBold { get; init; }
    public bool IsItalic { get; init; }
    public bool IsHeading { get; init; }

    /// <summary>
    /// Font size in half-points (e.g., 24 = 12pt). Null if not explicitly set.
    /// </summary>
    public int? FontSizeHalfPoints { get; init; }

    /// <summary>
    /// Font size in points. Returns null if not explicitly set.
    /// </summary>
    public double? FontSizePoints => FontSizeHalfPoints.HasValue ? FontSizeHalfPoints.Value / 2.0 : null;

    public List<AssetReference> Assets { get; init; } = [];
}

/// <summary>
/// Represents an image or other asset extracted from a document.
/// </summary>
public record AssetReference
{
    /// <summary>File name in the assets folder (e.g., "a1b2c3d4_img_001.png").</summary>
    public required string FileName { get; init; }

    /// <summary>Relative URL for serving the file (e.g., "/media/a1b2c3d4_img_001.png").</summary>
    public required string RelativeUrl { get; init; }

    /// <summary>MIME content type (e.g., "image/png").</summary>
    public required string ContentType { get; init; }

    /// <summary>File size in bytes.</summary>
    public long SizeBytes { get; init; }
}

/// <summary>
/// Result of DOCX extraction.
/// </summary>
public record ExtractionResult(
    List<DocBlock> Blocks,
    List<AssetReference> Assets,
    List<string> Warnings
);

/// <summary>
/// Parsed package structure.
/// </summary>
public class ParseResult
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Preamble { get; set; }
    public List<string> Editors { get; set; } = [];
    public List<TourDto> Tours { get; set; } = [];
    public List<string> Warnings { get; set; } = [];

    /// <summary>Parser confidence score (0.0 - 1.0).</summary>
    public double Confidence { get; set; } = 1.0;

    /// <summary>Detected numbering mode for the package.</summary>
    public QuestionNumberingMode NumberingMode { get; set; } = QuestionNumberingMode.Global;

    /// <summary>Total number of questions across all tours.</summary>
    public int TotalQuestions => Tours.Sum(t => t.Questions.Count);
}

/// <summary>
/// Parsed tour data.
/// </summary>
public class TourDto
{
    public required string Number { get; set; }

    /// <summary>Order index within the package (0-based).</summary>
    public int OrderIndex { get; set; }

    /// <summary>Whether this tour is a warmup tour.</summary>
    public bool IsWarmup { get; set; }

    public List<string> Editors { get; set; } = [];
    public string? Preamble { get; set; }
    public List<QuestionDto> Questions { get; set; } = [];
}

/// <summary>
/// Parsed question data.
/// </summary>
public class QuestionDto
{
    public required string Number { get; set; }
    public string? HostInstructions { get; set; }
    public string? HandoutText { get; set; }
    public string? HandoutAssetFileName { get; set; }
    public string Text { get; set; } = "";
    public string Answer { get; set; } = "";
    public string? AcceptedAnswers { get; set; }
    public string? RejectedAnswers { get; set; }
    public string? Comment { get; set; }
    public string? CommentAssetFileName { get; set; }
    public string? Source { get; set; }
    public List<string> Authors { get; set; } = [];

    /// <summary>Whether the question has a valid answer.</summary>
    public bool HasAnswer => !string.IsNullOrWhiteSpace(Answer);

    /// <summary>Whether the question has text.</summary>
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}

