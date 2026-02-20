namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Defines how question numbers are assigned within a package.
/// </summary>
public enum QuestionNumberingMode
{
    /// <summary>
    /// Questions are numbered sequentially across all main tours (1, 2, 3, ...).
    /// Warmup tour questions are numbered separately (1..k).
    /// </summary>
    Global,

    /// <summary>
    /// Questions are numbered sequentially within each tour (1, 2, ... per tour).
    /// </summary>
    PerTour,

    /// <summary>
    /// Question numbers are not auto-assigned. Users can edit them manually.
    /// Used for special cases like hexadecimal numbering (e.g., "IT-cup").
    /// </summary>
    Manual
}
