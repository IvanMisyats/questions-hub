namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Represents the access control level for a package.
/// Determines which users can view the package content.
/// </summary>
public enum PackageAccessLevel
{
    /// <summary>
    /// Everyone can access (default).
    /// Package is visible to all users including anonymous.
    /// </summary>
    All = 0,

    /// <summary>
    /// Only registered users with verified email can access.
    /// Requires authentication and email confirmation.
    /// </summary>
    RegisteredOnly = 1,

    /// <summary>
    /// Only users with Editor role can access.
    /// Requires Editor or Admin role.
    /// </summary>
    EditorsOnly = 2
}
