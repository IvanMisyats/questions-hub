using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Utils;

namespace QuestionsHub.Blazor.Infrastructure.Favorites;

public enum AddFavoriteOutcome
{
    /// <summary>The item is now a favorite (including when it already was).</summary>
    Added,

    /// <summary>The item does not exist or the user cannot view its package.</summary>
    NotFound,

    /// <summary>The user already has <see cref="FavoriteService.MaxFavoritesPerUser"/> favorites.</summary>
    LimitReached
}

/// <summary>Filters of the favorites page.</summary>
/// <param name="Year">Played year: the package's <c>PlayedFrom</c>..<c>PlayedTo</c>, or the publication year when it has no played date.</param>
public record FavoriteFilter(PackageType? Type = null, int? Year = null);

/// <summary>One page of the favorites list.</summary>
/// <param name="Items">Favorites of the requested kind, newest first, with what the page needs to render them.</param>
/// <param name="Counts">Visible favorites per kind under the current filters.</param>
/// <param name="Years">Played years of the visible favorites under the type filter, newest first.</param>
/// <param name="BlockNumbers">Package-wide numbers of the unnamed blocks on the page («Блок N»), by block id.</param>
public record FavoritePage(
    IReadOnlyList<Favorite> Items,
    IReadOnlyDictionary<FavoriteKind, int> Counts,
    IReadOnlyList<int> Years,
    IReadOnlyDictionary<int, int> BlockNumbers)
{
    public int CountOf(FavoriteKind kind) => Counts.GetValueOrDefault(kind);
}

/// <summary>
/// A user's private favorites: questions, tours, blocks and packages.
/// Favorites of packages the user can no longer view are kept but never shown.
/// </summary>
public class FavoriteService(
    IDbContextFactory<QuestionsHubDbContext> dbContextFactory,
    TimeProvider timeProvider)
{
    public const int MaxFavoritesPerUser = 5000;
    public const int PageSize = 20;

    /// <summary>Beyond the last possible page; keeps the offset of a hand-typed page number in range.</summary>
    private const int MaxPage = MaxFavoritesPerUser / PageSize + 1;

    /// <summary>
    /// Marks the item as a favorite. Idempotent. The cap is a plain count check: concurrent requests
    /// may overshoot it by a few rows, which is harmless.
    /// </summary>
    public async Task<AddFavoriteOutcome> Add(PackageAccessContext access, FavoriteKind kind, int id)
    {
        var userId = RequireUser(access);
        await using var context = await dbContextFactory.CreateDbContextAsync();

        var packageId = await FindViewablePackageId(context, access, kind, id);
        if (packageId == null)
            return AddFavoriteOutcome.NotFound;

        if (await context.Favorites.AnyAsync(Matches(userId, kind, id)))
            return AddFavoriteOutcome.Added;

        if (await context.Favorites.CountAsync(f => f.UserId == userId) >= MaxFavoritesPerUser)
            return AddFavoriteOutcome.LimitReached;

        context.Favorites.Add(new Favorite
        {
            UserId = userId,
            PackageId = packageId.Value,
            QuestionId = kind == FavoriteKind.Question ? id : null,
            TourId = kind == FavoriteKind.Tour ? id : null,
            BlockId = kind == FavoriteKind.Block ? id : null,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        });

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent request added the same favorite first
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // The item was deleted after we looked it up
            return AddFavoriteOutcome.NotFound;
        }

        return AddFavoriteOutcome.Added;
    }

    /// <summary>Removes the favorite. Idempotent.</summary>
    public async Task Remove(string userId, FavoriteKind kind, int id)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        await using var context = await dbContextFactory.CreateDbContextAsync();

        var rows = await context.Favorites.Where(Matches(userId, kind, id)).ToListAsync();
        if (rows.Count == 0)
            return;

        context.Favorites.RemoveRange(rows);
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent request removed it first
        }
    }

    /// <summary>
    /// The hearts of a package page for this viewer: <see cref="FavoriteLookup.Anonymous"/> for a visitor (no
    /// query), null (no hearts) for a signed-in user without a verified email, otherwise the user's favorites.
    /// </summary>
    public Task<FavoriteLookup?> GetLookupForPackage(PackageAccessContext viewer, int packageId) =>
        ForViewer(viewer, userId => GetLookupForPackage(userId, packageId));

    /// <summary>The hearts of a list of questions for this viewer; see <see cref="GetLookupForPackage(PackageAccessContext, int)"/>.</summary>
    public Task<FavoriteLookup?> GetLookupForQuestions(PackageAccessContext viewer, IEnumerable<int> questionIds) =>
        ForViewer(viewer, userId => GetLookupForQuestions(userId, questionIds));

    private static async Task<FavoriteLookup?> ForViewer(PackageAccessContext viewer, Func<string, Task<FavoriteLookup>> load)
    {
        if (string.IsNullOrEmpty(viewer.UserId))
            return FavoriteLookup.Anonymous;

        return viewer.HasVerifiedEmail ? await load(viewer.UserId) : null;
    }

    /// <summary>Every heart on a package page (the package, its tours, blocks and questions) in one indexed query.</summary>
    public async Task<FavoriteLookup> GetLookupForPackage(string userId, int packageId)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        await using var context = await dbContextFactory.CreateDbContextAsync();

        var rows = await context.Favorites.AsNoTracking()
            .Where(f => f.PackageId == packageId && f.UserId == userId)
            .Select(f => new { f.QuestionId, f.TourId, f.BlockId })
            .ToListAsync();

        var lookup = new FavoriteLookup();
        foreach (var row in rows)
        {
            if (row.QuestionId is { } questionId)
                lookup.Add(FavoriteKind.Question, questionId);
            else if (row.TourId is { } tourId)
                lookup.Add(FavoriteKind.Tour, tourId);
            else if (row.BlockId is { } blockId)
                lookup.Add(FavoriteKind.Block, blockId);
            else
                lookup.Add(FavoriteKind.Package, packageId);
        }

        return lookup;
    }

    /// <summary>Hearts for a list of questions from many packages (search results, editor profile, question page).</summary>
    public async Task<FavoriteLookup> GetLookupForQuestions(string userId, IEnumerable<int> questionIds)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        var lookup = new FavoriteLookup();
        var ids = questionIds.Distinct().ToList();
        if (ids.Count == 0)
            return lookup;

        await using var context = await dbContextFactory.CreateDbContextAsync();

        var favoriteIds = await context.Favorites.AsNoTracking()
            .Where(f => f.UserId == userId && f.QuestionId != null && ids.Contains(f.QuestionId.Value))
            .Select(f => f.QuestionId!.Value)
            .ToListAsync();

        foreach (var id in favoriteIds)
            lookup.Add(FavoriteKind.Question, id);

        return lookup;
    }

    /// <summary>
    /// One page of the user's favorites of <paramref name="kind"/>, newest first, plus the per-kind
    /// counts and the year options. Favorites of packages the user cannot view are skipped.
    /// Queries: years, counts, items (split into three for questions), plus tours/blocks for the block tab.
    /// </summary>
    public async Task<FavoritePage> GetPage(PackageAccessContext access, FavoriteKind kind, FavoriteFilter filter, int page)
    {
        var userId = RequireUser(access);
        await using var context = await dbContextFactory.CreateDbContextAsync();

        var packages = context.Packages.Where(access.GetViewFilter());
        if (filter.Type is { } type)
            packages = packages.Where(p => p.Type == type);

        var years = await LoadYears(Visible(context, userId, packages));

        if (filter.Year is { } year)
            packages = packages.Where(PlayedIn(year));

        var favorites = Visible(context, userId, packages);

        var counts = await favorites
            .GroupBy(f => f.QuestionId != null ? FavoriteKind.Question
                : f.TourId != null ? FavoriteKind.Tour
                : f.BlockId != null ? FavoriteKind.Block
                : FavoriteKind.Package)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Kind, x => x.Count);

        var ofKind = favorites.Where(OfKind(kind));
        IQueryable<Favorite> withDetails = kind switch
        {
            // Split: one joined query would repeat each question's text for every author × tag
            FavoriteKind.Question => ofKind
                .Include(f => f.Question!).ThenInclude(q => q.Authors)
                .Include(f => f.Question!).ThenInclude(q => q.Tour).ThenInclude(t => t.Package).ThenInclude(p => p.Tags)
                .AsSplitQuery(),
            FavoriteKind.Tour => ofKind.Include(f => f.Tour).Include(f => f.Package),
            FavoriteKind.Block => ofKind.Include(f => f.Block!).ThenInclude(b => b.Tour).Include(f => f.Package),
            _ => ofKind.Include(f => f.Package)
        };

        var items = await withDetails
            .OrderByDescending(f => f.CreatedAt)
            .ThenByDescending(f => f.Id)
            .Skip((Math.Clamp(page, 1, MaxPage) - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        var blockNumbers = kind == FavoriteKind.Block
            ? await LoadBlockNumbers(context, items)
            : new Dictionary<int, int>();

        return new FavoritePage(items, counts, years, blockNumbers);
    }

    /// <summary>
    /// Unnamed blocks are shown as «Блок N», numbered across the whole package like on the package page.
    /// One query for the tours and blocks of those packages; none when every block on the page has a name.
    /// </summary>
    private static async Task<Dictionary<int, int>> LoadBlockNumbers(QuestionsHubDbContext context, List<Favorite> items)
    {
        var packages = items
            .Where(f => string.IsNullOrWhiteSpace(f.Block?.Name))
            .Select(f => f.Package)
            .DistinctBy(p => p.Id)
            .ToList();
        if (packages.Count == 0)
            return [];

        var packageIds = packages.Select(p => p.Id).ToList();
        var tours = await context.Tours.AsNoTracking()
            .Where(t => packageIds.Contains(t.PackageId))
            .Select(t => new Tour
            {
                Id = t.Id,
                PackageId = t.PackageId,
                Number = t.Number,
                OrderIndex = t.OrderIndex,
                Type = t.Type,
                Blocks = t.Blocks.Select(b => new Block { Id = b.Id, OrderIndex = b.OrderIndex }).ToList()
            })
            .ToListAsync();

        var numbers = new Dictionary<int, int>();
        foreach (var package in packages)
        {
            var ordered = TourOrdering.ForDisplay(tours.Where(t => t.PackageId == package.Id), package.Type);
            foreach (var (blockId, number) in PackageLabels.BlockNumbers(ordered))
                numbers[blockId] = number;
        }

        return numbers;
    }

    /// <summary>
    /// Years a package was played in: <c>PlayedFrom</c>..<c>PlayedTo</c>, or the publication year
    /// when it has no played date. Must agree with <see cref="PlayedIn"/>.
    /// </summary>
    public static IEnumerable<int> PlayedYears(DateOnly? playedFrom, DateOnly? playedTo, DateTime? publicationDate)
    {
        if (playedFrom is { } from)
        {
            var lastYear = playedTo is { } to && to.Year > from.Year ? to.Year : from.Year;
            for (var year = from.Year; year <= lastYear; year++)
                yield return year;
        }
        else if (publicationDate is { } published)
        {
            yield return published.Year;
        }
    }

    private static Expression<Func<Package, bool>> PlayedIn(int year) => p =>
        p.PlayedFrom != null
            ? p.PlayedFrom.Value.Year == year ||
              (p.PlayedFrom.Value.Year < year && p.PlayedTo != null && p.PlayedTo.Value.Year >= year)
            : p.PublicationDate != null && p.PublicationDate.Value.Year == year;

    private static async Task<IReadOnlyList<int>> LoadYears(IQueryable<Favorite> favorites)
    {
        var periods = await favorites
            .Select(f => new { f.Package.PlayedFrom, f.Package.PlayedTo, f.Package.PublicationDate })
            .Distinct()
            .ToListAsync();

        return periods
            .SelectMany(p => PlayedYears(p.PlayedFrom, p.PlayedTo, p.PublicationDate))
            .Distinct()
            .OrderDescending()
            .ToList();
    }

    /// <summary>The user's favorites whose package is in <paramref name="packages"/>.</summary>
    private static IQueryable<Favorite> Visible(QuestionsHubDbContext context, string userId, IQueryable<Package> packages) =>
        context.Favorites.AsNoTracking()
            .Where(f => f.UserId == userId && packages.Any(p => p.Id == f.PackageId));

    /// <summary>The id of the item's package, when the item exists and the user can view that package.</summary>
    private static Task<int?> FindViewablePackageId(
        QuestionsHubDbContext context, PackageAccessContext access, FavoriteKind kind, int id)
    {
        var packages = kind switch
        {
            FavoriteKind.Question => context.Questions.Where(q => q.Id == id).Select(q => q.Tour.Package),
            FavoriteKind.Tour => context.Tours.Where(t => t.Id == id).Select(t => t.Package),
            FavoriteKind.Block => context.Blocks.Where(b => b.Id == id).Select(b => b.Tour.Package),
            FavoriteKind.Package => context.Packages.Where(p => p.Id == id),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        return packages.Where(access.GetViewFilter()).Select(p => (int?)p.Id).FirstOrDefaultAsync();
    }

    private static Expression<Func<Favorite, bool>> Matches(string userId, FavoriteKind kind, int id) => kind switch
    {
        FavoriteKind.Question => f => f.QuestionId == id && f.UserId == userId,
        FavoriteKind.Tour => f => f.TourId == id && f.UserId == userId,
        FavoriteKind.Block => f => f.BlockId == id && f.UserId == userId,
        FavoriteKind.Package => f => f.PackageId == id && f.UserId == userId &&
                                     f.QuestionId == null && f.TourId == null && f.BlockId == null,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static Expression<Func<Favorite, bool>> OfKind(FavoriteKind kind) => kind switch
    {
        FavoriteKind.Question => f => f.QuestionId != null,
        FavoriteKind.Tour => f => f.TourId != null,
        FavoriteKind.Block => f => f.BlockId != null,
        FavoriteKind.Package => f => f.QuestionId == null && f.TourId == null && f.BlockId == null,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static string RequireUser(PackageAccessContext access) =>
        string.IsNullOrEmpty(access.UserId)
            ? throw new ArgumentException("Favorites need a logged-in user.", nameof(access))
            : access.UserId;
}
