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
    /// <summary>Claim with the user's email-confirmed flag: <c>"True"</c> or <c>"False"</c> (<see cref="bool.ToString()"/>).</summary>
    public const string EmailConfirmedClaim = "EmailConfirmed";

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

        // Add email confirmed claim for access control decisions without DB roundtrip
        identity.AddClaim(new Claim(EmailConfirmedClaim, user.EmailConfirmed.ToString()));

        return identity;
    }
}

