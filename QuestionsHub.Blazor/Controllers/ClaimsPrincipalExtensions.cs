using System.Security.Claims;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure;

namespace QuestionsHub.Blazor.Controllers;

/// <summary>
/// Extension methods for ClaimsPrincipal to handle user identity and package access control.
/// Uses PackageAccessContext internally for consistent access control logic.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    extension(ClaimsPrincipal user)
    {
        /// <summary>
        /// Gets the user ID from the claims principal.
        /// </summary>
        public string? GetUserId()
        {
            return user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        /// <summary>
        /// Creates a PackageAccessContext from the claims principal.
        /// Note: HasVerifiedEmail is set to true because API controllers require authentication,
        /// and we assume authenticated API users have verified emails. For stricter checking,
        /// the controller should query the database for EmailConfirmed status.
        /// </summary>
        public PackageAccessContext ToAccessContext()
        {
            return new PackageAccessContext(
                IsAdmin: user.IsInRole("Admin"),
                IsEditor: user.IsInRole("Editor"),
                HasVerifiedEmail: user.Identity?.IsAuthenticated ?? false,
                UserId: user.GetUserId()
            );
        }

        /// <summary>
        /// Checks if the user can access the specified package for editing.
        /// Uses the same logic as PackageAccessContext for consistency.
        /// Admins can access all packages, Editors can access their own packages.
        /// </summary>
        public bool CanAccessPackage(Package package)
        {
            var context = user.ToAccessContext();

            // For edit operations: must be admin or owner with editor role
            if (context.IsAdmin)
                return true;

            if (context.IsEditor && package.OwnerId == context.UserId)
                return true;

            return false;
        }
    }
}

