using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Centralized service for all access control decisions.
/// Use this service to check permissions instead of duplicating logic in pages.
/// </summary>
public class AccessControlService
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;

    public AccessControlService(AuthenticationStateProvider authenticationStateProvider)
    {
        _authenticationStateProvider = authenticationStateProvider;
    }

    /// <summary>
    /// Gets the current authentication state.
    /// </summary>
    public async Task<AuthenticationState> GetAuthenticationState()
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
    /// Checks if the current user is authenticated.
    /// </summary>
    public async Task<bool> IsAuthenticated()
    {
        var authState = await GetAuthenticationState();
        return authState.User.Identity?.IsAuthenticated ?? false;
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
    /// Checks if the current user has the Editor role.
    /// </summary>
    public async Task<bool> IsEditor()
    {
        var authState = await GetAuthenticationState();
        return authState.User.IsInRole("Editor");
    }

    /// <summary>
    /// Checks if the current user can view the specified package.
    /// Published and Archived packages are visible to everyone.
    /// Draft packages are only visible to the owner or Admin.
    /// </summary>
    public async Task<bool> CanViewPackage(Package package)
    {
        // Published packages are visible to everyone
        if (package.Status == PackageStatus.Published)
            return true;

        // Archived packages are accessible via direct link
        if (package.Status == PackageStatus.Archived)
            return true;

        // Draft packages require authentication
        if (!await IsAuthenticated())
            return false;

        // Admins can see all packages
        if (await IsAdmin())
            return true;

        // Owners can see their own draft packages
        var userId = await GetCurrentUserId();
        return package.OwnerId == userId;
    }

    /// <summary>
    /// Checks if the current user can edit the specified package.
    /// User must be authenticated and have Editor or Admin role.
    /// Admins can edit any package; Editors can only edit their own packages.
    /// </summary>
    public async Task<bool> CanEditPackage(Package package)
    {
        // Only authenticated users can edit
        if (!await IsAuthenticated())
            return false;

        var authState = await GetAuthenticationState();
        var user = authState.User;

        // User must be in Editor or Admin role
        if (!user.IsInRole("Editor") && !user.IsInRole("Admin"))
            return false;

        // Admins can edit all packages
        if (user.IsInRole("Admin"))
            return true;

        // Editors can only edit their own packages
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return package.OwnerId == userId;
    }

    /// <summary>
    /// Checks if the current user can delete the specified package.
    /// Same rules as editing: Admin or package owner.
    /// </summary>
    public async Task<bool> CanDeletePackage(Package package)
    {
        return await CanEditPackage(package);
    }
}
