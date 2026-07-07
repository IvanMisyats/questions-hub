using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.Results;

/// <summary>
/// Projection of <see cref="ResultsSource"/> for display — excludes the heavy RawPayload jsonb
/// so list/header queries never drag platform payloads across the wire.
/// </summary>
public sealed record ResultsSourceInfo(
    int Id,
    ResultsPlatform Platform,
    string Url,
    string? Title,
    string? DisplayUrl,
    DateTime? LoadedAt,
    string? LoadError,
    string? LoadErrorDetail,
    DateTime? ResultsAvailableAfter,
    int? TeamsCount,
    bool StatsMapped,
    string? WarningsJson)
{
    /// <summary>
    /// Whether the public link may be shown to site visitors: external-only links always;
    /// platform links once the platform confirmed the tournament (successful load or embargo).
    /// </summary>
    public bool ShowExternalLink => !string.IsNullOrEmpty(DisplayUrl)
        && (Platform == ResultsPlatform.Other || LoadedAt != null || ResultsAvailableAfter != null);
}
