using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.Api;

public class ApiKeyService
{
    private readonly IDbContextFactory<QuestionsHubDbContext> _dbContextFactory;
    private readonly IMemoryCache _cache;

    private const string CachePrefix = "apikey_";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public ApiKeyService(
        IDbContextFactory<QuestionsHubDbContext> dbContextFactory,
        IMemoryCache cache)
    {
        _dbContextFactory = dbContextFactory;
        _cache = cache;
    }

    /// <summary>
    /// Validates an API key and returns the client if valid and active.
    /// Results are cached for 5 minutes.
    /// </summary>
    public async Task<ApiClient?> Validate(string rawKey)
    {
        var hash = HashKey(rawKey);
        var cacheKey = $"{CachePrefix}{hash}";

        if (_cache.TryGetValue(cacheKey, out ApiClient? cached))
            return cached;

        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var client = await context.ApiClients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.KeyHash == hash && c.IsActive);

        // Only cache valid keys — don't cache nulls to avoid cache poisoning
        // and stale-null races when a key is created after a failed lookup
        if (client != null)
        {
            _cache.Set(cacheKey, client, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheDuration
            });
        }

        return client;
    }

    /// <summary>
    /// Creates a new API client and returns the raw key (shown once).
    /// </summary>
    public async Task<(ApiClient Client, string RawKey)> Create(string name, string? contactEmail = null)
    {
        var rawKey = GenerateKey();
        var hash = HashKey(rawKey);
        var prefix = rawKey[..16]; // "qh_live_" + first 8 hex chars

        var client = new ApiClient
        {
            Name = name,
            KeyHash = hash,
            KeyPrefix = prefix,
            ContactEmail = contactEmail,
            CreatedAt = DateTime.UtcNow
        };

        await using var context = await _dbContextFactory.CreateDbContextAsync();
        context.ApiClients.Add(client);
        await context.SaveChangesAsync();

        return (client, rawKey);
    }

    /// <summary>
    /// Revokes an API key by setting IsActive to false.
    /// Also invalidates the cache entry.
    /// </summary>
    public async Task<bool> Revoke(int clientId)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var client = await context.ApiClients.FindAsync(clientId);

        if (client == null)
            return false;

        client.IsActive = false;
        await context.SaveChangesAsync();

        // Invalidate cache
        _cache.Remove($"{CachePrefix}{client.KeyHash}");

        return true;
    }

    /// <summary>
    /// Gets all API clients ordered by creation date.
    /// </summary>
    public async Task<List<ApiClient>> GetAll()
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.ApiClients
            .AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Updates the LastUsedAt timestamp (fire-and-forget, non-blocking).
    /// </summary>
    public async Task UpdateLastUsed(int clientId)
    {
        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            await context.ApiClients
                .Where(c => c.Id == clientId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastUsedAt, DateTime.UtcNow));
        }
        catch
        {
            // Non-critical — don't let tracking failures break requests
        }
    }

    public static string HashKey(string rawKey)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexStringLower(bytes);
    }

    private static string GenerateKey()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(16);
        return $"qh_live_{Convert.ToHexStringLower(randomBytes)}";
    }
}
