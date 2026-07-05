namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Defines the game type of a package. Set at creation/import and never changed afterwards.
/// </summary>
public enum PackageType
{
    /// <summary>Що?Де?Коли? — team game; tours of sequentially numbered questions.</summary>
    Www = 0,

    /// <summary>Своя гра (Швагер) — buzzer game; themes of 5 questions valued 10–50.</summary>
    Shvager = 1
}

/// <summary>
/// Display helpers for <see cref="PackageType"/>.
/// </summary>
public static class PackageTypeExtensions
{
    /// <summary>Full Ukrainian display name (e.g., for tabs and filters).</summary>
    public static string DisplayName(this PackageType type) => type switch
    {
        PackageType.Shvager => "Своя гра",
        _ => "Що?Де?Коли?"
    };

    /// <summary>Short Ukrainian display name (e.g., for badges).</summary>
    public static string ShortName(this PackageType type) => type switch
    {
        PackageType.Shvager => "Своя гра",
        _ => "ЩДК"
    };
}
