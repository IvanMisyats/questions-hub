using System.Text.Json;
using System.Text.Json.Serialization;
using QuestionsHub.Blazor.Infrastructure.Import;

namespace QuestionsHub.UnitTests.TestInfrastructure;

/// <summary>
/// Utilities for comparing ParseResult to expected JSON files.
/// </summary>
public static class JsonComparer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Serializes ParseResult to normalized JSON string for comparison.
    /// </summary>
    public static string ToNormalizedJson(ParseResult result)
    {
        var normalized = NormalizeParseResult(result);
        return JsonSerializer.Serialize(normalized, SerializerOptions);
    }

    /// <summary>
    /// Loads expected result from JSON file.
    /// </summary>
    public static ExpectedPackage LoadExpected(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<ExpectedPackage>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize {filePath}");
    }

    /// <summary>
    /// Saves ParseResult as expected JSON file (for creating new golden files).
    /// </summary>
    public static void SaveAsExpected(ParseResult result, string filePath)
    {
        var json = ToNormalizedJson(result);
        File.WriteAllText(filePath, json);
    }

    private static object NormalizeParseResult(ParseResult result)
    {
        return new
        {
            title = NormalizeString(result.Title),
            preamble = NormalizeString(result.Preamble),
            editors = result.Editors.OrderBy(e => e).ToList(),
            tours = result.Tours
                .OrderBy(t => t.Number)
                .Select(t => new
                {
                    number = t.Number,
                    editors = t.Editors.OrderBy(e => e).ToList(),
                    preamble = NormalizeString(t.Preamble),
                    questions = t.Questions
                        .OrderBy(q => int.TryParse(q.Number, out var n) ? n : 0)
                        .Select(q => new
                        {
                            number = q.Number,
                            text = NormalizeString(q.Text),
                            answer = NormalizeString(q.Answer),
                            acceptedAnswers = NormalizeString(q.AcceptedAnswers),
                            rejectedAnswers = NormalizeString(q.RejectedAnswers),
                            comment = NormalizeString(q.Comment),
                            source = NormalizeString(q.Source),
                            authors = q.Authors.OrderBy(a => a).ToList(),
                            hostInstructions = NormalizeString(q.HostInstructions),
                            handoutText = NormalizeString(q.HandoutText),
                            hasHandoutAsset = q.HandoutAssetFileName != null,
                            hasCommentAsset = q.CommentAssetFileName != null
                        })
                        .ToList()
                })
                .ToList(),
            totalQuestions = result.TotalQuestions,
            confidence = Math.Round(result.Confidence, 2)
        };
    }

    private static string? NormalizeString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // Normalize whitespace and newlines
        return value
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Trim();
    }
}

/// <summary>
/// Expected package structure for JSON comparison.
/// </summary>
public record ExpectedPackage
{
    public string? Title { get; init; }
    public string? Preamble { get; init; }
    public List<string> Editors { get; init; } = [];
    public List<ExpectedTour> Tours { get; init; } = [];
    public int TotalQuestions { get; init; }
    public double Confidence { get; init; }
}

public record ExpectedTour
{
    public required string Number { get; init; }
    public List<string> Editors { get; init; } = [];
    public string? Preamble { get; init; }
    public List<ExpectedQuestion> Questions { get; init; } = [];
}

public record ExpectedQuestion
{
    public required string Number { get; init; }
    public string? Text { get; init; }
    public string? Answer { get; init; }
    public string? AcceptedAnswers { get; init; }
    public string? RejectedAnswers { get; init; }
    public string? Comment { get; init; }
    public string? Source { get; init; }
    public List<string> Authors { get; init; } = [];
    public string? HostInstructions { get; init; }
    public string? HandoutText { get; init; }
    public bool HasHandoutAsset { get; init; }
    public bool HasCommentAsset { get; init; }
}

