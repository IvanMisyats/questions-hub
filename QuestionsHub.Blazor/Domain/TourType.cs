namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Defines the type of tour within a package.
/// </summary>
public enum TourType
{
    /// <summary>Regular (main) tour with sequentially numbered questions.</summary>
    Regular = 0,

    /// <summary>Warmup tour (Розминка). At most one per package. Always first.</summary>
    Warmup = 1,

    /// <summary>Shootout tour (Перестрілка). At most one per package. Always last.</summary>
    Shootout = 2
}
