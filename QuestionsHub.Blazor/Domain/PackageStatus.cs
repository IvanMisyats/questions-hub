namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Represents the visibility status of a package.
/// </summary>
public enum PackageStatus
{
    /// <summary>Not visible to anyone except owner/admin.</summary>
    Draft,

    /// <summary>Visible to all users in search and browsing.</summary>
    Published,

    /// <summary>Hidden from main list but accessible via direct link.</summary>
    Archived
}

