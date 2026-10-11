namespace QuestionsHub.Blazor.Domain;

/// <summary>The URL segment of each <see cref="FavoriteKind"/> in <c>/api/favorites/{kind}/{id}</c>.</summary>
public static class FavoriteKindSegments
{
    public static string ToSegment(this FavoriteKind kind) => kind switch
    {
        FavoriteKind.Question => "question",
        FavoriteKind.Tour => "tour",
        FavoriteKind.Block => "block",
        FavoriteKind.Package => "package",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    /// <summary>The kind for an exact (lower-case) segment, or null.</summary>
    public static FavoriteKind? Parse(string segment) => segment switch
    {
        "question" => FavoriteKind.Question,
        "tour" => FavoriteKind.Tour,
        "block" => FavoriteKind.Block,
        "package" => FavoriteKind.Package,
        _ => null
    };
}
