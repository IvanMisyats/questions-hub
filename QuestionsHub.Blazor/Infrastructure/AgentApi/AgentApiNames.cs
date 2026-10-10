using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.AgentApi;

/// <summary>
/// The camelCase names the agent API uses for enums, in both directions (responses and changeset
/// requests). Parsing is case-insensitive.
/// </summary>
public static class AgentApiNames
{
    public static string Of(PackageStatus status) => status switch
    {
        PackageStatus.Draft => "draft",
        PackageStatus.Published => "published",
        PackageStatus.Archived => "archived",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    public static string Of(PackageAccessLevel level) => level switch
    {
        PackageAccessLevel.All => "all",
        PackageAccessLevel.RegisteredOnly => "registeredOnly",
        PackageAccessLevel.EditorsOnly => "editorsOnly",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
    };

    public static string Of(QuestionNumberingMode mode) => mode switch
    {
        QuestionNumberingMode.Global => "global",
        QuestionNumberingMode.PerTour => "perTour",
        QuestionNumberingMode.Manual => "manual",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };

    public static string Of(TourType type) => type switch
    {
        TourType.Regular => "regular",
        TourType.Warmup => "warmup",
        TourType.Shootout => "shootout",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public static string Of(PackageType type) => type switch
    {
        PackageType.Www => "www",
        PackageType.Shvager => "shvager",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public static string Of(TokenScope scope) => scope switch
    {
        TokenScope.Read => "read",
        TokenScope.ReadWrite => "readWrite",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
    };

    public static bool TryParse(string? value, out PackageStatus status) => TryParse(value, Of, out status);

    public static bool TryParse(string? value, out QuestionNumberingMode mode) => TryParse(value, Of, out mode);

    public static bool TryParse(string? value, out TourType type) => TryParse(value, Of, out type);

    private static bool TryParse<T>(string? value, Func<T, string> name, out T result) where T : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (string.Equals(name(candidate), value, StringComparison.OrdinalIgnoreCase))
            {
                result = candidate;
                return true;
            }
        }

        result = default;
        return false;
    }
}
