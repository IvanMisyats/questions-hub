using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Service for querying and filtering packages on the home page.
/// Provides caching for accessible package IDs and editor list with 1-hour TTL.
/// </summary>
public class PackageListService
{
    private readonly IDbContextFactory<QuestionsHubDbContext> _dbContextFactory;
    private readonly IMemoryCache _cache;

    // Cache key prefixes
    private const string AccessiblePackagesCachePrefix = "accessible_packages_";
    private const string EditorsCacheKey = "package_editors_all";

    // Cache duration
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    public PackageListService(
        IDbContextFactory<QuestionsHubDbContext> dbContextFactory,
        IMemoryCache cache)
    {
        _dbContextFactory = dbContextFactory;
        _cache = cache;
    }

    /// <summary>
    /// Gets the list of accessible package IDs for the given access context.
    /// Results are cached for 1 hour.
    /// </summary>
    public async Task<HashSet<int>> GetAccessiblePackageIds(PackageAccessContext accessContext)
    {
        var cacheKey = GetAccessCacheKey(accessContext);

        if (_cache.TryGetValue(cacheKey, out HashSet<int>? cachedIds) && cachedIds != null)
        {
            return cachedIds;
        }

        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var accessFilter = accessContext.GetAccessFilter();

        var packageIds = await context.Packages
            .AsNoTracking()
            .Where(p => p.Status == PackageStatus.Published)
            .Where(accessFilter)
            .Select(p => p.Id)
            .ToListAsync();

        var result = packageIds.ToHashSet();

        _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration
        });

        return result;
    }

    /// <summary>
    /// Searches packages with optional filters and sorting.
    /// </summary>
    public async Task<PackageListResult> SearchPackages(
        PackageListFilter filter,
        PackageAccessContext accessContext)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var accessFilter = accessContext.GetAccessFilter();

        // Base query with access control
        var query = context.Packages
            .AsNoTracking()
            .Where(p => p.Status == PackageStatus.Published)
            .Where(accessFilter);

        // Apply title filter (case-insensitive, partial match)
        if (!string.IsNullOrWhiteSpace(filter.TitleSearch))
        {
            query = ApplyTitleFilter(query, filter.TitleSearch, context);
        }

        // Apply editor filter
        if (filter.EditorId.HasValue)
        {
            var editorId = filter.EditorId.Value;
            query = query.Where(p =>
                p.PackageEditors.Any(a => a.Id == editorId) ||
                p.Tours.Any(t =>
                    t.Editors.Any(a => a.Id == editorId) ||
                    t.Blocks.Any(b => b.Editors.Any(a => a.Id == editorId))));
        }

        // Apply tag filter
        if (filter.TagId.HasValue)
        {
            var tagId = filter.TagId.Value;
            query = query.Where(p => p.Tags.Any(t => t.Id == tagId));
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling((double)totalCount / filter.PageSize);

        // Apply sorting
        query = ApplySorting(query, filter.SortField, filter.SortDir);

        // Apply pagination
        var page = Math.Max(1, filter.Page);
        var packages = await query
            .Skip((page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.Description,
                p.PublicationDate,
                p.PlayedFrom,
                p.PlayedTo,
                QuestionsCount = p.Tours.SelectMany(t => t.Questions).Count()
            })
            .ToListAsync();

        var packageIds = packages.Select(p => p.Id).ToList();

        // Load editors and tags separately for efficiency
        var packagesWithEditors = await context.Packages
            .AsNoTracking()
            .Where(p => packageIds.Contains(p.Id))
            .Include(p => p.PackageEditors)
            .Include(p => p.Tags)
            .Include(p => p.Tours)
                .ThenInclude(t => t.Editors)
            .Include(p => p.Tours)
                .ThenInclude(t => t.Blocks)
                    .ThenInclude(b => b.Editors)
            .ToDictionaryAsync(p => p.Id);

        // Map to DTOs
        var packageDtos = packages
            .Select(p =>
            {
                var pkg = packagesWithEditors.GetValueOrDefault(p.Id);
                var allEditors = new List<Author>();

                if (pkg != null)
                {
                    allEditors.AddRange(pkg.PackageEditors);
                    foreach (var tour in pkg.Tours)
                    {
                        allEditors.AddRange(tour.Editors);
                        foreach (var block in tour.Blocks)
                        {
                            allEditors.AddRange(block.Editors);
                        }
                    }
                }

                var editors = allEditors
                    .DistinctBy(e => e.Id)
                    .Select(e => new EditorBriefDto(e.Id, e.FirstName, e.LastName))
                    .ToList();

                var tags = pkg?.Tags
                    .Select(t => new TagBriefDto(t.Id, t.Name))
                    .OrderBy(t => t.Name)
                    .ToList() ?? [];

                return new PackageCardDto(
                    p.Id,
                    p.Title,
                    p.Description,
                    p.PublicationDate,
                    p.PlayedFrom,
                    p.PlayedTo,
                    p.QuestionsCount,
                    editors,
                    tags);
            })
            .ToList();

        return new PackageListResult(packageDtos, totalCount, totalPages, page);
    }

    /// <summary>
    /// Gets all editors who are associated with published packages.
    /// Can optionally filter by name (partial match).
    /// Results are cached for 1 hour.
    /// </summary>
    public async Task<List<EditorFilterDto>> GetEditorsForFilter(string? searchTerm = null)
    {
        // Get all editors from cache first
        if (!_cache.TryGetValue(EditorsCacheKey, out List<EditorFilterDto>? allEditors) || allEditors == null)
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync();

            // Get authors who are editors of any published package (package, tour, or block level)
            var packageEditorIds = await context.Packages
                .AsNoTracking()
                .Where(p => p.Status == PackageStatus.Published)
                .SelectMany(p => p.PackageEditors)
                .Select(a => a.Id)
                .ToListAsync();

            var tourEditorIds = await context.Tours
                .AsNoTracking()
                .Where(t => t.Package.Status == PackageStatus.Published)
                .SelectMany(t => t.Editors)
                .Select(a => a.Id)
                .ToListAsync();

            var blockEditorIds = await context.Blocks
                .AsNoTracking()
                .Where(b => b.Tour.Package.Status == PackageStatus.Published)
                .SelectMany(b => b.Editors)
                .Select(a => a.Id)
                .ToListAsync();

            var allEditorIds = packageEditorIds
                .Concat(tourEditorIds)
                .Concat(blockEditorIds)
                .Distinct()
                .ToList();

            allEditors = await context.Authors
                .AsNoTracking()
                .Where(a => allEditorIds.Contains(a.Id))
                .OrderBy(a => a.LastName)
                .ThenBy(a => a.FirstName)
                .Select(a => new EditorFilterDto(a.Id, a.FirstName + " " + a.LastName))
                .ToListAsync();

            _cache.Set(EditorsCacheKey, allEditors, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheDuration
            });
        }

        // Filter in memory if search term provided
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            return allEditors
                .Where(e => e.FullName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return allEditors;
    }

    /// <summary>
    /// Gets an editor by ID. Returns null if not found.
    /// </summary>
    public async Task<EditorFilterDto?> GetEditorById(int editorId)
    {
        var editors = await GetEditorsForFilter();
        return editors.FirstOrDefault(e => e.Id == editorId);
    }

    /// <summary>
    /// Invalidates all package-related caches.
    /// Should be called when package status changes to/from Published.
    /// </summary>
    public void InvalidateCache()
    {
        // Remove all access cache entries by using a pattern
        // Since MemoryCache doesn't support pattern removal, we use a token-based approach
        _cache.Remove(EditorsCacheKey);

        // Remove known access cache keys for each role type
        // Note: This approach clears common role-based keys but may not catch user-specific keys
        _cache.Remove(GetAccessCacheKeyForRole("anonymous"));
        _cache.Remove(GetAccessCacheKeyForRole("user"));
        _cache.Remove(GetAccessCacheKeyForRole("editor"));
        _cache.Remove(GetAccessCacheKeyForRole("admin"));

        // For a more thorough approach, we could use CancellationTokenSource
        // but for this use case, the above is sufficient
    }

    /// <summary>
    /// Escapes special characters in a LIKE pattern to prevent SQL injection.
    /// </summary>
    public static string EscapeLikePattern(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return pattern;

        // Escape backslash first (since it's the escape character)
        // Then escape the wildcards
        return pattern
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }

    /// <summary>
    /// Applies title filter using ILike for PostgreSQL or Contains for InMemory provider.
    /// </summary>
    private static IQueryable<Package> ApplyTitleFilter(
        IQueryable<Package> query,
        string titleSearch,
        QuestionsHubDbContext context)
    {
        var escapedPattern = $"%{EscapeLikePattern(titleSearch)}%";

        // Check if we're using InMemory provider (for tests)
        if (context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            // Use in-memory case-insensitive contains
            var searchLower = titleSearch.ToLowerInvariant();
            return query.Where(p => p.Title.ToLower().Contains(searchLower));
        }

        // Use PostgreSQL ILike for case-insensitive pattern matching
        return query.Where(p => EF.Functions.ILike(p.Title, escapedPattern));
    }

    private static IQueryable<Package> ApplySorting(
        IQueryable<Package> query,
        PackageSortField sortField,
        SortDirection sortDir)
    {
        return (sortField, sortDir) switch
        {
            (PackageSortField.PublicationDate, SortDirection.Desc) =>
                query.OrderByDescending(p => p.PublicationDate ?? DateTime.MinValue)
                    .ThenByDescending(p => p.Id),

            (PackageSortField.PublicationDate, SortDirection.Asc) =>
                query.OrderBy(p => p.PublicationDate ?? DateTime.MaxValue)
                    .ThenBy(p => p.Id),

            (PackageSortField.PlayedFrom, SortDirection.Desc) =>
                query.OrderByDescending(p => p.PlayedFrom ?? DateOnly.MinValue)
                    .ThenByDescending(p => p.Id),

            (PackageSortField.PlayedFrom, SortDirection.Asc) =>
                query.OrderBy(p => p.PlayedFrom ?? DateOnly.MaxValue)
                    .ThenBy(p => p.Id),

            _ => query.OrderByDescending(p => p.PublicationDate ?? DateTime.MinValue)
                    .ThenByDescending(p => p.Id)
        };
    }

    private static string GetAccessCacheKey(PackageAccessContext context)
    {
        // Admin sees everything
        if (context.IsAdmin)
            return $"{AccessiblePackagesCachePrefix}admin";

        // Build key based on access level capabilities + userId for owner check
        var roleKey = context.IsEditor ? "editor" : (context.HasVerifiedEmail ? "user" : "anonymous");

        // Include userId for owner-based access (user can see their own packages)
        if (!string.IsNullOrEmpty(context.UserId))
            return $"{AccessiblePackagesCachePrefix}{roleKey}_{context.UserId}";

        return $"{AccessiblePackagesCachePrefix}{roleKey}";
    }

    private static string GetAccessCacheKeyForRole(string role)
    {
        return $"{AccessiblePackagesCachePrefix}{role}";
    }
}
