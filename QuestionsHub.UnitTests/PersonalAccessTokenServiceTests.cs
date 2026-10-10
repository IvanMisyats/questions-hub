using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.UnitTests.TestInfrastructure;
using Xunit;

namespace QuestionsHub.UnitTests;

public class PersonalAccessTokenServiceTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory = new();
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly PersonalAccessTokenService _service;

    public PersonalAccessTokenServiceTests()
    {
        _service = new PersonalAccessTokenService(_dbFactory, _clock);
    }

    public void Dispose()
    {
        using var context = _dbFactory.CreateDbContext();
        context.Database.EnsureDeleted();
    }

    #region Helpers

    private async Task<string> CreateUser(params string[] roles)
    {
        using var context = _dbFactory.CreateDbContext();
        var user = new ApplicationUser
        {
            FirstName = "Ірина",
            LastName = "Тестова",
            UserName = Guid.NewGuid().ToString(),
            EmailConfirmed = true
        };
        context.Users.Add(user);

        foreach (var roleName in roles)
        {
            var role = await context.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
            if (role == null)
            {
                role = new IdentityRole(roleName) { NormalizedName = roleName.ToUpperInvariant() };
                context.Roles.Add(role);
            }

            context.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id });
        }

        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task SetRoles(string userId, params string[] roles)
    {
        using var context = _dbFactory.CreateDbContext();
        context.UserRoles.RemoveRange(context.UserRoles.Where(ur => ur.UserId == userId));
        foreach (var roleName in roles)
        {
            var role = await context.Roles.FirstAsync(r => r.Name == roleName);
            context.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = role.Id });
        }

        await context.SaveChangesAsync();
    }

    private async Task SetEmailConfirmed(string userId, bool confirmed)
    {
        using var context = _dbFactory.CreateDbContext();
        var user = await context.Users.SingleAsync(u => u.Id == userId);
        user.EmailConfirmed = confirmed;
        await context.SaveChangesAsync();
    }

    private async Task<int> CreatePackage(string? ownerId)
    {
        using var context = _dbFactory.CreateDbContext();
        var package = new Package { Title = "Пакет", OwnerId = ownerId };
        context.Packages.Add(package);
        await context.SaveChangesAsync();
        return package.Id;
    }

    private async Task<(PersonalAccessToken Token, string Raw)> CreateToken(
        string userId, TokenScope scope = TokenScope.ReadWrite, int validDays = 30, IReadOnlyCollection<int>? packageIds = null)
    {
        var result = await _service.Create(userId, new CreateTokenRequest("Claude", scope, validDays, packageIds));
        result.Success.Should().BeTrue(result.ErrorMessage);
        return (result.Token!, result.RawToken!);
    }

    #endregion

    #region Create

    [Fact]
    public async Task Create_ReturnsRawTokenOnce_AndStoresOnlyItsHash()
    {
        var userId = await CreateUser("Editor");

        var (token, raw) = await CreateToken(userId, TokenScope.Read, validDays: 7);

        raw.Should().StartWith("qh_pat_").And.HaveLength(39);
        token.TokenPrefix.Should().Be(raw[..16]);
        token.Scope.Should().Be(TokenScope.Read);
        token.ExpiresAt.Should().Be(_clock.Now.UtcDateTime.AddDays(7));

        using var context = _dbFactory.CreateDbContext();
        var stored = await context.PersonalAccessTokens.SingleAsync();
        stored.TokenHash.Should().Be(PersonalAccessTokenService.HashToken(raw)).And.NotContain(raw[16..]);
    }

    [Theory]
    [InlineData("User")]
    [InlineData(null)]
    public async Task Create_ForNonEditor_Fails(string? role)
    {
        var userId = role == null ? await CreateUser() : await CreateUser(role);

        var result = await _service.Create(userId, new CreateTokenRequest("Claude", TokenScope.Read, 30, null));

        result.Success.Should().BeFalse();
        result.RawToken.Should().BeNull();
    }

    [Theory]
    [InlineData("", 30)]
    [InlineData("   ", 30)]
    [InlineData("Claude", 0)]
    [InlineData("Claude", 366)]
    public async Task Create_WithInvalidNameOrExpiry_Fails(string name, int validDays)
    {
        var userId = await CreateUser("Editor");

        var result = await _service.Create(userId, new CreateTokenRequest(name, TokenScope.Read, validDays, null));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Create_WithUndefinedScope_Fails()
    {
        var userId = await CreateUser("Editor");

        var result = await _service.Create(userId, new CreateTokenRequest("Claude", (TokenScope)2, 30, null));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Create_ForUnconfirmedEmail_Fails()
    {
        var userId = await CreateUser("Editor");
        await SetEmailConfirmed(userId, false);

        var result = await _service.Create(userId, new CreateTokenRequest("Claude", TokenScope.Read, 30, null));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Create_Concurrently_NeverExceedsTheCap()
    {
        var userId = await CreateUser("Editor");
        for (var i = 0; i < PersonalAccessTokenService.MaxActiveTokensPerUser - 1; i++)
            await CreateToken(userId);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            _service.Create(userId, new CreateTokenRequest("Claude", TokenScope.Read, 30, null))));

        results.Count(r => r.Success).Should().Be(1);
    }

    [Fact]
    public async Task Create_WithEmptyAllowlist_Fails()
    {
        var userId = await CreateUser("Editor");

        var result = await _service.Create(userId, new CreateTokenRequest("Claude", TokenScope.Read, 30, []));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Create_EditorRestrictedToSomeoneElsesPackage_Fails()
    {
        var editorId = await CreateUser("Editor");
        var otherId = await CreateUser("Editor");
        var own = await CreatePackage(editorId);
        var foreign = await CreatePackage(otherId);

        var result = await _service.Create(editorId, new CreateTokenRequest("Claude", TokenScope.Read, 30, [own, foreign]));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain(foreign.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Create_AdminRestrictedToAnyExistingPackage_Succeeds_ButNotToMissingOne()
    {
        var adminId = await CreateUser("Admin");
        var foreign = await CreatePackage(await CreateUser("Editor"));

        var (token, _) = await CreateToken(adminId, packageIds: [foreign, foreign]);
        token.PackageIds.Should().Equal(foreign);

        var missing = await _service.Create(adminId, new CreateTokenRequest("Claude", TokenScope.Read, 30, [999_999]));
        missing.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Create_EleventhActiveToken_Fails_ButRevokedAndExpiredDoNotCount()
    {
        var userId = await CreateUser("Editor");
        for (var i = 0; i < PersonalAccessTokenService.MaxActiveTokensPerUser; i++)
            await CreateToken(userId, validDays: 1);

        (await _service.Create(userId, new CreateTokenRequest("Claude", TokenScope.Read, 30, null)))
            .Success.Should().BeFalse();

        _clock.Advance(TimeSpan.FromDays(2)); // all ten expired
        (await _service.Create(userId, new CreateTokenRequest("Claude", TokenScope.Read, 30, null)))
            .Success.Should().BeTrue();
    }

    #endregion

    #region Validate

    [Fact]
    public async Task Validate_ActiveToken_ReturnsUserAndCurrentRoles()
    {
        var userId = await CreateUser("Editor");
        var (token, raw) = await CreateToken(userId);

        var validated = await _service.Validate(raw);

        validated.Should().NotBeNull();
        validated!.Token.Id.Should().Be(token.Id);
        validated.User.Id.Should().Be(userId);
        validated.Roles.Should().Equal("Editor");
        validated.IsAdmin.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("qh_pat_short")]
    [InlineData("qh_live_0123456789abcdef0123456789abcdef")]
    [InlineData("qh_pat_0123456789abcdef0123456789abcdef")] // well-formed but unknown
    public async Task Validate_UnknownOrMalformed_ReturnsNull(string raw)
    {
        await CreateToken(await CreateUser("Editor"));

        (await _service.Validate(raw)).Should().BeNull();
    }

    [Fact]
    public async Task Validate_ExpiredToken_ReturnsNull()
    {
        var (_, raw) = await CreateToken(await CreateUser("Editor"), validDays: 1);

        _clock.Advance(TimeSpan.FromDays(1));

        (await _service.Validate(raw)).Should().BeNull();
    }

    [Fact]
    public async Task Validate_RevokedToken_ReturnsNull()
    {
        var userId = await CreateUser("Editor");
        var (token, raw) = await CreateToken(userId);

        (await _service.Revoke(token.Id, userId)).Should().BeTrue();

        (await _service.Validate(raw)).Should().BeNull();
    }

    [Fact]
    public async Task Validate_LockedOutUser_ReturnsNull()
    {
        var userId = await CreateUser("Editor");
        var (_, raw) = await CreateToken(userId);

        using (var context = _dbFactory.CreateDbContext())
        {
            var user = await context.Users.SingleAsync(u => u.Id == userId);
            user.LockoutEnd = _clock.Now.AddMinutes(15);
            await context.SaveChangesAsync();
        }

        (await _service.Validate(raw)).Should().BeNull();
    }

    [Fact]
    public async Task Validate_EmailNoLongerConfirmed_ReturnsNull()
    {
        var userId = await CreateUser("Editor");
        var (_, raw) = await CreateToken(userId);

        await SetEmailConfirmed(userId, false);

        (await _service.Validate(raw)).Should().BeNull();
    }

    [Fact]
    public async Task Validate_EditorDemotedToUser_ReturnsNull()
    {
        var userId = await CreateUser("Editor", "User");
        var (_, raw) = await CreateToken(userId);

        await SetRoles(userId, "User");

        (await _service.Validate(raw)).Should().BeNull();
    }

    [Fact]
    public async Task Validate_AdminDemotedToEditor_LosesAdminImmediately()
    {
        var userId = await CreateUser("Admin", "Editor");
        var (_, raw) = await CreateToken(userId);
        (await _service.Validate(raw))!.IsAdmin.Should().BeTrue();

        await SetRoles(userId, "Editor");

        var validated = await _service.Validate(raw);
        validated.Should().NotBeNull();
        validated!.IsAdmin.Should().BeFalse();
        validated.Roles.Should().Equal("Editor");
    }

    [Fact]
    public async Task Validate_RecordsLastUse_AtMostOncePerMinute()
    {
        var (token, raw) = await CreateToken(await CreateUser("Editor"));

        async Task<DateTime?> LastUsed()
        {
            using var context = _dbFactory.CreateDbContext();
            return (await context.PersonalAccessTokens.SingleAsync(t => t.Id == token.Id)).LastUsedAt;
        }

        await _service.Validate(raw);
        var first = await LastUsed();
        first.Should().Be(_clock.Now.UtcDateTime);

        _clock.Advance(TimeSpan.FromSeconds(30));
        await _service.Validate(raw);
        (await LastUsed()).Should().Be(first);

        _clock.Advance(TimeSpan.FromSeconds(30));
        await _service.Validate(raw);
        (await LastUsed()).Should().Be(_clock.Now.UtcDateTime);
    }

    #endregion

    #region Revoke / list

    [Fact]
    public async Task Revoke_OthersToken_FailsForEditor_SucceedsForAdmin()
    {
        var ownerId = await CreateUser("Editor");
        var editorId = await CreateUser("Editor");
        var adminId = await CreateUser("Admin");
        var (token, raw) = await CreateToken(ownerId);

        (await _service.Revoke(token.Id, editorId)).Should().BeFalse();
        (await _service.Validate(raw)).Should().NotBeNull();

        (await _service.Revoke(token.Id, adminId)).Should().BeTrue();
        (await _service.Validate(raw)).Should().BeNull();
    }

    [Fact]
    public async Task Revoke_ByDemotedAdmin_Fails()
    {
        var ownerId = await CreateUser("Editor");
        var formerAdminId = await CreateUser("Admin", "Editor");
        var (token, raw) = await CreateToken(ownerId);

        await SetRoles(formerAdminId, "Editor");

        (await _service.Revoke(token.Id, formerAdminId)).Should().BeFalse();
        (await _service.Validate(raw)).Should().NotBeNull();
    }

    [Fact]
    public async Task Revoke_MissingToken_ReturnsFalse()
    {
        (await _service.Revoke(12345, await CreateUser("Admin"))).Should().BeFalse();
    }

    [Fact]
    public async Task GetAll_OnlyForCurrentAdmins()
    {
        var adminId = await CreateUser("Admin");
        var editorId = await CreateUser("Editor");
        await CreateToken(editorId);

        (await _service.GetAll(adminId)).Should().HaveCount(1);
        (await _service.GetAll(editorId)).Should().BeNull();

        await SetRoles(adminId, "Editor");
        (await _service.GetAll(adminId)).Should().BeNull();
    }

    [Fact]
    public async Task GetForUser_ReturnsOnlyTheUsersTokens_NewestFirst()
    {
        var userId = await CreateUser("Editor");
        var (older, _) = await CreateToken(userId);
        _clock.Advance(TimeSpan.FromMinutes(1));
        var (newer, _) = await CreateToken(userId);
        await CreateToken(await CreateUser("Editor"));

        var tokens = await _service.GetForUser(userId);

        tokens.Select(t => t.Id).Should().Equal(newer.Id, older.Id);
    }

    #endregion
}
