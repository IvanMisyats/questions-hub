namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Represents the current user's access permissions for filtering packages.
/// This context is used to determine package visibility based on access level.
/// Contains pure business logic with no dependencies on infrastructure.
/// </summary>
/// <param name="IsAdmin">Whether the user has the Admin role</param>
/// <param name="IsEditor">Whether the user has the Editor role</param>
/// <param name="HasVerifiedEmail">Whether the user has a verified email address</param>
/// <param name="UserId">The current user's ID (null if anonymous)</param>
public record PackageAccessContext(
    bool IsAdmin,
    bool IsEditor,
    bool HasVerifiedEmail,
    string? UserId)
{
    /// <summary>
    /// Checks if the user can access the given package based on its access level.
    /// Does not consider package status - use CanViewPackage for full visibility check.
    /// </summary>
    /// <param name="package">The package to check access for.</param>
    /// <returns>True if the user can access the package based on access level.</returns>
    public bool CanAccessPackage(Package package)
    {
        // Admin always has access
        if (IsAdmin)
            return true;

        // Owner always has access to their own packages
        if (!string.IsNullOrEmpty(UserId) && package.OwnerId == UserId)
            return true;

        return package.AccessLevel switch
        {
            PackageAccessLevel.All => true,
            PackageAccessLevel.RegisteredOnly => HasVerifiedEmail,
            PackageAccessLevel.EditorsOnly => IsEditor,
            _ => true
        };
    }

    /// <summary>
    /// Checks if the user can view the package considering both status and access level.
    /// This is the main method for determining package visibility.
    /// </summary>
    /// <param name="package">The package to check visibility for.</param>
    /// <returns>True if the user can view the package.</returns>
    public bool CanViewPackage(Package package)
    {
        // Admin can see all packages
        if (IsAdmin)
            return true;

        // Draft packages: only owner can view
        if (package.Status == PackageStatus.Draft)
            return !string.IsNullOrEmpty(UserId) && package.OwnerId == UserId;

        // Published and Archived packages: check access level
        // Owner can always see their own packages
        if (!string.IsNullOrEmpty(UserId) && package.OwnerId == UserId)
            return true;

        // Check access level for non-owners
        return CanAccessPackage(package);
    }
}
