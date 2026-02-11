using System.Text.Json.Serialization;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Root object of the .qhub package.json file.
/// </summary>
public class QhubPackage
{
    [JsonPropertyName("formatVersion")]
    public string? FormatVersion { get; set; }

    [JsonPropertyName("sourceUrl")]
    public string? SourceUrl { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("preamble")]
    public string? Preamble { get; set; }

    [JsonPropertyName("playedFrom")]
    public string? PlayedFrom { get; set; }

    [JsonPropertyName("playedTo")]
    public string? PlayedTo { get; set; }

    [JsonPropertyName("numberingMode")]
    public string? NumberingMode { get; set; }

    [JsonPropertyName("sharedEditors")]
    public bool? SharedEditors { get; set; }

    [JsonPropertyName("editors")]
    public List<string>? Editors { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("tours")]
    public List<QhubTour>? Tours { get; set; }
}

/// <summary>
/// Tour within a .qhub package.
/// </summary>
public class QhubTour
{
    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("isWarmup")]
    public bool? IsWarmup { get; set; }

    [JsonPropertyName("editors")]
    public List<string>? Editors { get; set; }

    [JsonPropertyName("preamble")]
    public string? Preamble { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("questions")]
    public List<QhubQuestion>? Questions { get; set; }

    [JsonPropertyName("blocks")]
    public List<QhubBlock>? Blocks { get; set; }
}

/// <summary>
/// Block within a .qhub tour.
/// </summary>
public class QhubBlock
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("editors")]
    public List<string>? Editors { get; set; }

    [JsonPropertyName("preamble")]
    public string? Preamble { get; set; }

    [JsonPropertyName("questions")]
    public List<QhubQuestion>? Questions { get; set; }
}

/// <summary>
/// Question within a .qhub package.
/// </summary>
public class QhubQuestion
{
    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("hostInstructions")]
    public string? HostInstructions { get; set; }

    [JsonPropertyName("handoutText")]
    public string? HandoutText { get; set; }

    [JsonPropertyName("handoutAssetFileName")]
    public string? HandoutAssetFileName { get; set; }

    [JsonPropertyName("handoutAssetUrl")]
    public string? HandoutAssetUrl { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("answer")]
    public string? Answer { get; set; }

    [JsonPropertyName("acceptedAnswers")]
    public string? AcceptedAnswers { get; set; }

    [JsonPropertyName("rejectedAnswers")]
    public string? RejectedAnswers { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("commentAssetFileName")]
    public string? CommentAssetFileName { get; set; }

    [JsonPropertyName("commentAssetUrl")]
    public string? CommentAssetUrl { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("authors")]
    public List<string>? Authors { get; set; }
}
