using System.Security.Claims;
using FluentAssertions;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using Xunit;

namespace QuestionsHub.UnitTests;

public class AgentAccessTests
{
    private static ClaimsPrincipal Agent(string userId, string role, string? packages = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Role, role),
            new(AgentClaims.TokenId, "7"),
        };
        if (packages != null)
            claims.Add(new Claim(AgentClaims.Packages, packages));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, PersonalAccessTokenAuthenticationOptions.Scheme));
    }

    private static Package PackageOwnedBy(int id, string? ownerId) => new() { Id = id, Title = "P", OwnerId = ownerId };

    [Fact]
    public void Editor_CanEditOwnPackagesOnly()
    {
        var editor = Agent("u1", "Editor");

        editor.CanEditAsAgent(PackageOwnedBy(1, "u1")).Should().BeTrue();
        editor.CanEditAsAgent(PackageOwnedBy(2, "u2")).Should().BeFalse();
        editor.CanEditAsAgent(PackageOwnedBy(3, null)).Should().BeFalse();
    }

    [Fact]
    public void UnrestrictedAdmin_CanEditAnyPackage()
    {
        Agent("admin", "Admin").CanEditAsAgent(PackageOwnedBy(2, "u2")).Should().BeTrue();
    }

    [Fact]
    public void Allowlist_NarrowsEvenAnAdmin()
    {
        var admin = Agent("admin", "Admin", packages: "5,9");

        admin.CanEditAsAgent(PackageOwnedBy(5, "u2")).Should().BeTrue();
        admin.CanEditAsAgent(PackageOwnedBy(6, "u2")).Should().BeFalse();
    }

    [Fact]
    public void Allowlist_NeverWidensAnEditor()
    {
        var editor = Agent("u1", "Editor", packages: "5");

        editor.CanEditAsAgent(PackageOwnedBy(5, "u2")).Should().BeFalse();
    }

    [Fact]
    public void Claims_RoundTrip()
    {
        var agent = Agent("u1", "Editor", packages: "9,5");

        agent.GetTokenId().Should().Be(7);
        agent.GetPackageAllowlist().Should().BeEquivalentTo([5, 9]);
        Agent("u1", "Editor").GetPackageAllowlist().Should().BeNull();
        new ClaimsPrincipal(new ClaimsIdentity()).GetTokenId().Should().BeNull();
    }
}
