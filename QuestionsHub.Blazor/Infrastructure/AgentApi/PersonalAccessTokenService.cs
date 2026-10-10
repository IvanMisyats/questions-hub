using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.AgentApi;

/// <summary>What the caller asked for when creating a token.</summary>
/// <param name="PackageIds">Packages to restrict the token to; null = every package the user can edit.</param>
public record CreateTokenRequest(string Name, TokenScope Scope, int ValidDays, IReadOnlyCollection<int>? PackageIds);

/// <summary>Outcome of <see cref="PersonalAccessTokenService.Create"/>; the raw token is only ever returned here.</summary>
public record CreateTokenResult(bool Success, PersonalAccessToken? Token, string? RawToken, string? ErrorMessage)
{
    public static CreateTokenResult Fail(string error) => new(false, null, null, error);
}

/// <summary>A token that passed validation, with the user's current roles.</summary>
public record ValidatedToken(PersonalAccessToken Token, ApplicationUser User, IReadOnlyList<string> Roles)
{
    public bool IsAdmin => Roles.Contains("Admin");
}

/// <summary>
/// Creates, validates and revokes personal access tokens for the agent API.
/// Validation reads the token, user and roles from the database on every call (no cache), so
/// revocation, expiry, lockout and role changes take effect on the next request.
/// </summary>
public class PersonalAccessTokenService(
    IDbContextFactory<QuestionsHubDbContext> dbContextFactory,
    TimeProvider timeProvider)
{
    public const string TokenPrefix = "qh_pat_";
    public const int MaxActiveTokensPerUser = 10;
    public const int MaxValidDays = 365;
    public const int MaxPackagesPerToken = 50;
    public const int MaxNameLength = 100;

    private const int RandomHexLength = 32;
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(1);
    private static readonly string[] AgentRoles = ["Admin", "Editor"];

    /// <summary>
    /// Serializes count-then-insert in <see cref="Create"/> so concurrent requests cannot exceed
    /// <see cref="MaxActiveTokensPerUser"/>. Process-wide is enough: the app runs as a single
    /// instance, and token creation is a rare, human-initiated action.
    /// </summary>
    private static readonly SemaphoreSlim CreateLock = new(1, 1);

    /// <summary>
    /// Creates a token for <paramref name="userId"/>. Only editors and admins may hold tokens; an
    /// editor may only restrict a token to packages they own.
    /// </summary>
    public async Task<CreateTokenResult> Create(string userId, CreateTokenRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > MaxNameLength)
            return CreateTokenResult.Fail($"Назва токена має містити від 1 до {MaxNameLength} символів.");

        if (request.ValidDays < 1 || request.ValidDays > MaxValidDays)
            return CreateTokenResult.Fail($"Термін дії — від 1 до {MaxValidDays} днів.");

        if (!Enum.IsDefined(request.Scope))
            return CreateTokenResult.Fail("Некоректний рівень доступу токена.");

        var packageIds = request.PackageIds?.Distinct().Order().ToList();
        if (packageIds != null)
        {
            if (packageIds.Count == 0)
                return CreateTokenResult.Fail("Вкажіть хоча б один пакет або дозвольте доступ до всіх пакетів.");
            if (packageIds.Count > MaxPackagesPerToken)
                return CreateTokenResult.Fail($"Не більше {MaxPackagesPerToken} пакетів на токен.");
            if (packageIds.Any(id => id <= 0))
                return CreateTokenResult.Fail("Некоректний ідентифікатор пакета.");
        }

        await CreateLock.WaitAsync();
        try
        {
            return await CreateLocked(userId, request, name, packageIds);
        }
        finally
        {
            CreateLock.Release();
        }
    }

    private async Task<CreateTokenResult> CreateLocked(
        string userId, CreateTokenRequest request, string name, List<int>? packageIds)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();

        var user = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is not { EmailConfirmed: true })
            return CreateTokenResult.Fail("Токени доступні лише користувачам з підтвердженою електронною поштою.");

        var roles = await GetRoles(context, userId);
        if (!roles.Any(AgentRoles.Contains))
            return CreateTokenResult.Fail("Токени доступні лише редакторам.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var activeCount = await context.PersonalAccessTokens
            .CountAsync(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now);
        if (activeCount >= MaxActiveTokensPerUser)
            return CreateTokenResult.Fail($"Можна мати не більше {MaxActiveTokensPerUser} активних токенів. Відкличте непотрібні.");

        if (packageIds != null)
        {
            var isAdmin = roles.Contains("Admin");
            var allowed = await context.Packages
                .Where(p => packageIds.Contains(p.Id) && (isAdmin || p.OwnerId == userId))
                .Select(p => p.Id)
                .ToListAsync();

            var unavailable = packageIds.Except(allowed).ToList();
            if (unavailable.Count > 0)
                return CreateTokenResult.Fail($"Пакети не знайдено або вони вам не належать: {string.Join(", ", unavailable)}.");
        }

        var rawToken = GenerateRawToken();
        var token = new PersonalAccessToken
        {
            UserId = userId,
            Name = name,
            TokenHash = HashToken(rawToken),
            TokenPrefix = rawToken[..16],
            Scope = request.Scope,
            PackageIds = packageIds,
            CreatedAt = now,
            ExpiresAt = now.AddDays(request.ValidDays)
        };

        context.PersonalAccessTokens.Add(token);
        await context.SaveChangesAsync();

        return new CreateTokenResult(true, token, rawToken, null);
    }

    /// <summary>
    /// Returns the token and its user when the raw token is known, not revoked, not expired, the
    /// user has a confirmed email (as password login requires), is not locked out and currently
    /// holds the Editor or Admin role; otherwise null.
    /// </summary>
    public async Task<ValidatedToken?> Validate(string rawToken)
    {
        // Cheap format check first: no database round trip for obvious garbage.
        if (rawToken.Length != TokenPrefix.Length + RandomHexLength
            || !rawToken.StartsWith(TokenPrefix, StringComparison.Ordinal))
            return null;

        var hash = HashToken(rawToken);
        var now = timeProvider.GetUtcNow();

        await using var context = await dbContextFactory.CreateDbContextAsync();
        var token = await context.PersonalAccessTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (token?.User == null || !token.IsActive(now.UtcDateTime))
            return null;

        if (!token.User.EmailConfirmed || token.User.LockoutEnd > now)
            return null;

        var roles = await GetRoles(context, token.UserId);
        if (!roles.Any(AgentRoles.Contains))
            return null;

        // Throttled bookkeeping. Concurrent requests may each write (last one wins, possibly a few
        // milliseconds older) — harmless for a "last used" hint, so no conditional update.
        if (token.LastUsedAt == null || now.UtcDateTime - token.LastUsedAt.Value >= LastUsedResolution)
        {
            token.LastUsedAt = now.UtcDateTime;
            await context.SaveChangesAsync();
        }

        return new ValidatedToken(token, token.User, roles);
    }

    /// <summary>
    /// Revokes a token. Users may revoke their own tokens; users who are admins <em>right now</em>
    /// (roles re-read from the database, so a long-open admin page loses the right on demotion)
    /// may revoke anyone's. Returns false when the token does not exist or the caller may not revoke it.
    /// </summary>
    public async Task<bool> Revoke(int tokenId, string requestingUserId)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        var token = await context.PersonalAccessTokens.FindAsync(tokenId);

        if (token == null)
            return false;

        if (token.UserId != requestingUserId && !(await GetRoles(context, requestingUserId)).Contains("Admin"))
            return false;

        token.RevokedAt ??= timeProvider.GetUtcNow().UtcDateTime;
        await context.SaveChangesAsync();
        return true;
    }

    /// <summary>The user's tokens, newest first (including revoked and expired ones).</summary>
    public async Task<List<PersonalAccessToken>> GetForUser(string userId)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        return await context.PersonalAccessTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Every user's tokens with their owners, newest first (admin overview), or null when
    /// <paramref name="requestingUserId"/> is not currently an admin.
    /// </summary>
    public async Task<List<PersonalAccessToken>?> GetAll(string requestingUserId)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        if (!(await GetRoles(context, requestingUserId)).Contains("Admin"))
            return null;

        return await context.PersonalAccessTokens
            .AsNoTracking()
            .Include(t => t.User)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public static string HashToken(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));

    private static string GenerateRawToken() =>
        TokenPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(RandomHexLength / 2));

    private static Task<List<string>> GetRoles(QuestionsHubDbContext context, string userId) =>
        context.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(context.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToListAsync();
}
