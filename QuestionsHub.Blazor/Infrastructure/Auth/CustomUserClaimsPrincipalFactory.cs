using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.Auth;

/// <summary>
/// Custom claims principal factory that adds FirstName and LastName claims.
/// This allows LoginDisplay to get user name from claims instead of querying the database,
/// which avoids DbContext concurrency issues in Blazor Server.
/// </summary>
public class CustomUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
{
    public CustomUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        // Add custom claims for first and last name
        identity.AddClaim(new Claim("FirstName", user.FirstName ?? ""));
        identity.AddClaim(new Claim("LastName", user.LastName ?? ""));

        return identity;
    }
}

