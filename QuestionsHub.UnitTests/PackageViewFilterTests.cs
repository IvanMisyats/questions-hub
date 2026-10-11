using FluentAssertions;
using QuestionsHub.Blazor.Domain;
using Xunit;

namespace QuestionsHub.UnitTests;

/// <summary>
/// <see cref="PackageAccessContext.GetViewFilter"/> is the SQL form of
/// <see cref="PackageAccessContext.CanViewPackage"/>; the two must agree for every combination.
/// </summary>
public class PackageViewFilterTests
{
    private const string OwnerId = "owner";

    public static TheoryData<string, PackageAccessContext> Viewers => new()
    {
        { "anonymous", new PackageAccessContext(false, false, false, null) },
        { "empty user id", new PackageAccessContext(false, false, true, "") },
        { "unverified user", new PackageAccessContext(false, false, false, "user") },
        { "verified user", new PackageAccessContext(false, false, true, "user") },
        { "editor", new PackageAccessContext(false, true, true, "editor") },
        { "owner", new PackageAccessContext(false, true, true, OwnerId) },
        { "admin", new PackageAccessContext(true, false, true, "admin") }
    };

    [Theory]
    [MemberData(nameof(Viewers))]
    public void GetViewFilter_AgreesWithCanViewPackage(string viewer, PackageAccessContext access)
    {
        var filter = access.GetViewFilter().Compile();

        foreach (var status in Enum.GetValues<PackageStatus>())
        foreach (var level in Enum.GetValues<PackageAccessLevel>())
        foreach (var ownerId in new[] { OwnerId, "", null })
        {
            var package = new Package { Title = "Пакет", Status = status, AccessLevel = level, OwnerId = ownerId };

            filter(package).Should().Be(access.CanViewPackage(package),
                $"{viewer} viewing a {status} package with access level {level}, owner {ownerId ?? "none"}");
        }
    }
}
