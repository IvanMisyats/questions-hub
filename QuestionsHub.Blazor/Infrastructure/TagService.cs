using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Service for managing Tag entities - creating, searching, cleaning up orphaned tags,
/// and retrieving popular tags for the home page.
/// </summary>
public class TagService
{
    private readonly IDbContextFactory<QuestionsHubDbContext> _dbContextFactory;
    private readonly IMemoryCache _cache;

    private const string PopularTagsCacheKey = "popular_tags";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    public TagService(IDbContextFactory<QuestionsHubDbContext> dbContextFactory, IMemoryCache cache)
    {
        _dbContextFactory = dbContextFactory;
        _cache = cache;
    }

    /// <summary>
    /// Returns the most popular tags (by number of packages that use them), ordered by usage descending.
    /// </summary>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of popular tags.</returns>
    public async Task<List<Tag>> GetPopular(int limit = 10, CancellationToken ct = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(ct);

        return await context.Tags
            .Where(t => t.Packages.Any())
            .OrderByDescending(t => t.Packages.Count)
            .ThenBy(t => t.Name)
            .Take(limit)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Returns the most popular tags across published packages, ordered by usage count.
    /// Results are cached for 1 hour. Returns TagBriefDto for API/display use.
    /// </summary>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of popular tags with their IDs and names.</returns>
    public async Task<List<TagBriefDto>> GetPopularPublished(int limit = 10, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(PopularTagsCacheKey, out List<TagBriefDto>? cached) && cached != null)
        {
            return cached;
        }

        await using var context = await _dbContextFactory.CreateDbContextAsync(ct);

        var popularTags = await context.Tags
            .AsNoTracking()
            .Where(t => t.Packages.Any(p => p.Status == PackageStatus.Published))
            .Select(t => new
            {
                t.Id,
                t.Name,
                Count = t.Packages.Count(p => p.Status == PackageStatus.Published)
            })
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Name)
            .Take(limit)
            .Select(t => new TagBriefDto(t.Id, t.Name))
            .ToListAsync(ct);

        _cache.Set(PopularTagsCacheKey, popularTags, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration
        });

        return popularTags;
    }

    /// <summary>
    /// Invalidates the popular tags cache.
    /// Should be called when package status changes (publish/unpublish) or tags are modified.
    /// </summary>
    public void InvalidatePopularTagsCache()
    {
        _cache.Remove(PopularTagsCacheKey);
    }

    /// <summary>
    /// Searches for tags whose name contains the query (case-insensitive).
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of matching tags ordered by name.</returns>
    public async Task<List<Tag>> Search(string query, int limit = 10, CancellationToken ct = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(ct);

        var trimmedQuery = query.Trim();
        if (string.IsNullOrWhiteSpace(trimmedQuery))
        {
            return [];
        }

        return await context.Tags
            .Where(t => EF.Functions.ILike(t.Name, "%" + trimmedQuery + "%"))
            .OrderBy(t => t.Name)
            .Take(limit)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Gets or creates a tag with the specified name.
    /// Lookup is case-insensitive. If a tag with the same name (case-insensitive) exists, returns the existing one.
    /// </summary>
    /// <param name="name">Tag name.</param>
    /// <returns>The existing or newly created tag.</returns>
    /// <exception cref="ArgumentException">Thrown when name is null, empty, or whitespace.</exception>
    public async Task<Tag> GetOrCreate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Назва тегу не може бути порожньою.", nameof(name));
        }

        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await GetOrCreateInternal(context, name.Trim());
    }

    /// <summary>
    /// Gets or creates a tag with the specified name using the provided context.
    /// </summary>
    public async Task<Tag> GetOrCreate(QuestionsHubDbContext context, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Назва тегу не може бути порожньою.", nameof(name));
        }

        return await GetOrCreateInternal(context, name.Trim());
    }

    private static async Task<Tag> GetOrCreateInternal(QuestionsHubDbContext context, string name)
    {
        // Case-insensitive lookup
        var existing = await context.Tags
            .FirstOrDefaultAsync(t => EF.Functions.ILike(t.Name, name));

        if (existing != null)
        {
            return existing;
        }

        var tag = new Tag { Name = name };
        context.Tags.Add(tag);
        await context.SaveChangesAsync();

        return tag;
    }

    /// <summary>
    /// Attempts to delete a tag if it is orphaned (no packages reference it).
    /// </summary>
    /// <param name="tagId">The tag ID to check and potentially delete.</param>
    /// <returns>True if the tag was deleted, false if it still has packages.</returns>
    public async Task<bool> TryDeleteIfOrphaned(int tagId)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await TryDeleteIfOrphaned(context, tagId);
    }

    /// <summary>
    /// Attempts to delete a tag if it is orphaned (no packages reference it).
    /// Uses the provided context.
    /// </summary>
    public async Task<bool> TryDeleteIfOrphaned(QuestionsHubDbContext context, int tagId)
    {
        var tag = await context.Tags
            .Include(t => t.Packages)
            .FirstOrDefaultAsync(t => t.Id == tagId);

        if (tag == null)
        {
            return false;
        }

        if (tag.Packages.Count > 0)
        {
            return false;
        }

        context.Tags.Remove(tag);
        await context.SaveChangesAsync();

        return true;
    }
}
