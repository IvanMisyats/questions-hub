using System.Security.Claims;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Controllers;

/// <summary>
/// Extension methods for ClaimsPrincipal to handle user identity and package access control.
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
        /// Checks if the user can access the specified package.
        /// Admins can access all packages, other users can only access their own.
        /// </summary>
        public bool CanAccessPackage(Package package)
        {
            if (user.IsInRole("Admin"))
            {
                return true;
            }

            var userId = user.GetUserId();
            return package.OwnerId == userId;
        }
    }
}

