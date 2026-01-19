using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Centralized service for access control decisions.
/// Provides access to authentication state and creates PackageAccessContext for package visibility checks.
/// For package visibility, use GetPackageAccessContext() and call methods on the returned context.
/// </summary>
public class AccessControlService
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly UserManager<ApplicationUser> _userManager;

    public AccessControlService(
        AuthenticationStateProvider authenticationStateProvider,
        UserManager<ApplicationUser> userManager)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _userManager = userManager;
    }

    /// <summary>
    /// Gets the current authentication state.
    /// </summary>
    private async Task<AuthenticationState> GetAuthenticationState()
    {
        return await _authenticationStateProvider.GetAuthenticationStateAsync();
    }

    /// <summary>
    /// Gets the current user's ID.
    /// </summary>
    public async Task<string?> GetCurrentUserId()
    {
        var authState = await GetAuthenticationState();
        return authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    /// <summary>
    /// Checks if the current user has the Admin role.
    /// </summary>
    public async Task<bool> IsAdmin()
    {
        var authState = await GetAuthenticationState();
        return authState.User.IsInRole("Admin");
    }


    /// <summary>
    /// Gets the package access context for the current user.
    /// Use this to check package visibility: context.CanViewPackage(package)
    /// </summary>
    public async Task<PackageAccessContext> GetPackageAccessContext()
    {
        var authState = await GetAuthenticationState();
        var user = authState.User;
        var isAuthenticated = user.Identity?.IsAuthenticated ?? false;
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // Get verified email status only if authenticated
        var hasVerifiedEmail = false;
        if (isAuthenticated && !string.IsNullOrEmpty(userId))
        {
            var appUser = await _userManager.FindByIdAsync(userId);
            hasVerifiedEmail = appUser?.EmailConfirmed ?? false;
        }

        return new PackageAccessContext(
            IsAdmin: user.IsInRole("Admin"),
            IsEditor: user.IsInRole("Editor"),
            HasVerifiedEmail: hasVerifiedEmail,
            UserId: userId
        );
    }

    /// <summary>
    /// Checks if the current user can view the specified package.
    /// Convenience method - for batch operations, use GetPackageAccessContext() instead.
    /// </summary>
    public async Task<bool> CanViewPackage(Package package)
    {
        var context = await GetPackageAccessContext();
        return context.CanViewPackage(package);
    }

    /// <summary>
    /// Checks if the current user can edit the specified package.
    /// User must be authenticated and have Editor or Admin role.
    /// Admins can edit any package; Editors can only edit their own packages.
    /// </summary>
    public async Task<bool> CanEditPackage(Package package)
    {
        var context = await GetPackageAccessContext();

        // Only authenticated users can edit
        if (string.IsNullOrEmpty(context.UserId))
            return false;

        // Admins can edit all packages
        if (context.IsAdmin)
            return true;

        // Editors can only edit their own packages
        if (context.IsEditor && package.OwnerId == context.UserId)
            return true;

        return false;
    }

    /// <summary>
    /// Checks if the current user can delete the specified package.
    /// Same rules as editing: Admin or package owner with Editor role.
    /// </summary>
    public async Task<bool> CanDeletePackage(Package package)
    {
        return await CanEditPackage(package);
    }
}
