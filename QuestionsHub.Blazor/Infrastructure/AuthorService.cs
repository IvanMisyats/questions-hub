using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Service for managing Author entities - creating, finding, and cleaning up orphaned authors.
/// </summary>
public class AuthorService
{
    private readonly IDbContextFactory<QuestionsHubDbContext> _dbContextFactory;

    public AuthorService(IDbContextFactory<QuestionsHubDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    /// <summary>
    /// Gets or creates an author with the specified first and last name.
    /// If an author with the same name exists, returns the existing author.
    /// </summary>
    /// <param name="firstName">Author's first name.</param>
    /// <param name="lastName">Author's last name.</param>
    /// <returns>The existing or newly created author.</returns>
    /// <exception cref="ArgumentException">Thrown when firstName or lastName is null, empty, or whitespace.</exception>
    public async Task<Author> GetOrCreateAuthor(string firstName, string lastName)
    {
        ValidateAuthorName(firstName, lastName);
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await GetOrCreateAuthorInternal(context, firstName.Trim(), lastName.Trim());
    }

    /// <summary>
    /// Gets or creates an author with the specified first and last name using the provided context.
    /// If an author with the same name exists, returns the existing author.
    /// </summary>
    /// <param name="context">Database context to use.</param>
    /// <param name="firstName">Author's first name.</param>
    /// <param name="lastName">Author's last name.</param>
    /// <returns>The existing or newly created author.</returns>
    /// <exception cref="ArgumentException">Thrown when firstName or lastName is null, empty, or whitespace.</exception>
    public async Task<Author> GetOrCreateAuthor(
        QuestionsHubDbContext context,
        string firstName,
        string lastName)
    {
        ValidateAuthorName(firstName, lastName);
        return await GetOrCreateAuthorInternal(context, firstName.Trim(), lastName.Trim());
    }

    private static void ValidateAuthorName(string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new ArgumentException("Ім'я автора не може бути порожнім.", nameof(firstName));
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            throw new ArgumentException("Прізвище автора не може бути порожнім.", nameof(lastName));
        }
    }

    private static async Task<Author> GetOrCreateAuthorInternal(
        QuestionsHubDbContext context,
        string firstName,
        string lastName)
    {
        var existing = await context.Authors
            .FirstOrDefaultAsync(a => a.FirstName == firstName && a.LastName == lastName);

        if (existing != null)
        {
            return existing;
        }

        var author = new Author
        {
            FirstName = firstName,
            LastName = lastName
        };

        context.Authors.Add(author);
        await context.SaveChangesAsync();

        return author;
    }

    /// <summary>
    /// Searches for authors matching the query (first or last name starts with query).
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of matching authors.</returns>
    public async Task<List<Author>> SearchAuthors(string query, int limit = 10, CancellationToken ct = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(ct);

        var trimmedQuery = query.Trim();
        if (string.IsNullOrWhiteSpace(trimmedQuery))
        {
            return [];
        }

        return await context.Authors
            .Where(a => EF.Functions.ILike(a.LastName, trimmedQuery + "%")
                     || EF.Functions.ILike(a.FirstName, trimmedQuery + "%"))
            .OrderBy(a => a.LastName)
            .ThenBy(a => a.FirstName)
            .Take(limit)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Gets a paginated list of authors with their question and package counts.
    /// Uses optimized database queries for better performance.
    /// Sorted by package count descending, then by question count descending.
    /// </summary>
    /// <param name="accessContext">User access context for filtering by access level.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="searchQuery">Optional search query to filter by name.</param>
    /// <returns>Paginated result with author list items.</returns>
    public async Task<AuthorListResult> GetAuthorsWithCountsPaginated(
        PackageAccessContext accessContext,
        int page = 1,
        int pageSize = 25,
        string? searchQuery = null)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        // Build the base query for accessible packages based on access context
        var accessiblePackageIds = GetAccessiblePackageIdsQuery(context, accessContext);

        // Build the query for authors with counts
        var query = context.Authors
            .Select(a => new
            {
                a.Id,
                a.FirstName,
                a.LastName,
                // Count questions from accessible published packages
                QuestionCount = a.Questions.Count(q =>
                    q.Tour.Package.Status == PackageStatus.Published &&
                    accessiblePackageIds.Contains(q.Tour.PackageId)),
                // Count unique packages from tours, blocks, and package-level editors
                PackageCount = a.Tours
                    .Where(t => t.Package.Status == PackageStatus.Published &&
                               accessiblePackageIds.Contains(t.PackageId))
                    .Select(t => t.PackageId)
                    .Union(a.Blocks
                        .Where(b => b.Tour.Package.Status == PackageStatus.Published &&
                                   accessiblePackageIds.Contains(b.Tour.PackageId))
                        .Select(b => b.Tour.PackageId))
                    .Union(a.Packages
                        .Where(p => p.SharedEditors &&
                                   p.Status == PackageStatus.Published &&
                                   accessiblePackageIds.Contains(p.Id))
                        .Select(p => p.Id))
                    .Distinct()
                    .Count()
            });

        // Apply search filter if provided
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var trimmedQuery = searchQuery.Trim();
            query = query.Where(a =>
                EF.Functions.ILike(a.FirstName, trimmedQuery + "%") ||
                EF.Functions.ILike(a.LastName, trimmedQuery + "%") ||
                EF.Functions.ILike(a.FirstName + " " + a.LastName, "%" + trimmedQuery + "%"));
        }

        // Filter to only authors with accessible content
        query = query.Where(a => a.QuestionCount > 0 || a.PackageCount > 0);

        // Get total count for pagination
        var totalCount = await query.CountAsync();

        // Sort by package count first, then by question count (both descending)
        var orderedQuery = query
            .OrderByDescending(a => a.PackageCount)
            .ThenByDescending(a => a.QuestionCount)
            .ThenBy(a => a.LastName)
            .ThenBy(a => a.FirstName);

        // Apply pagination - fetch one extra item to detect if there's a next page
        var items = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .ToListAsync();

        var hasNextPage = items.Count > pageSize;
        var resultItems = items
            .Take(pageSize)
            .Select(a => new AuthorListItem
            {
                Id = a.Id,
                FirstName = a.FirstName,
                LastName = a.LastName,
                QuestionCount = a.QuestionCount,
                PackageCount = a.PackageCount
            })
            .ToList();

        return new AuthorListResult(resultItems, hasNextPage, totalCount);
    }

    /// <summary>
    /// Builds a queryable of package IDs that are accessible to the user.
    /// Uses PackageAccessContext.GetAccessFilter() for consistency with other pages.
    /// </summary>
    private static IQueryable<int> GetAccessiblePackageIdsQuery(
        QuestionsHubDbContext context,
        PackageAccessContext accessContext)
    {
        var accessFilter = accessContext.GetAccessFilter();
        return context.Packages
            .Where(accessFilter)
            .Select(p => p.Id);
    }

    // ==================== Author Statistics Methods ====================

    /// <summary>
    /// Gets statistics for a specific author: question count and package count.
    /// Question count includes all questions where the author is listed as a question author
    /// in any accessible published package.
    /// Package count includes packages where the author is an editor at any level.
    /// </summary>
    /// <param name="authorId">The author ID.</param>
    /// <param name="accessContext">User access context for filtering by access level.</param>
    /// <returns>Author statistics or null if author not found.</returns>
    public async Task<AuthorStatistics?> GetAuthorStatistics(int authorId, PackageAccessContext accessContext)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var author = await context.Authors
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == authorId);

        if (author == null)
        {
            return null;
        }

        var accessiblePackageIds = GetAccessiblePackageIdsQuery(context, accessContext);

        // Count questions from all accessible published packages where author is a question author
        var questionCount = await context.Questions
            .AsNoTracking()
            .Where(q => q.Authors.Any(a => a.Id == authorId))
            .Where(q => q.Tour.Package.Status == PackageStatus.Published)
            .Where(q => accessiblePackageIds.Contains(q.Tour.PackageId))
            .CountAsync();

        // Count unique packages where author is an editor (tour, block, or package level)
        var packageCount = await context.Tours
            .AsNoTracking()
            .Where(t => t.Editors.Any(e => e.Id == authorId))
            .Where(t => t.Package.Status == PackageStatus.Published)
            .Where(t => accessiblePackageIds.Contains(t.PackageId))
            .Select(t => t.PackageId)
            .Union(
                context.Blocks
                    .AsNoTracking()
                    .Where(b => b.Editors.Any(e => e.Id == authorId))
                    .Where(b => b.Tour.Package.Status == PackageStatus.Published)
                    .Where(b => accessiblePackageIds.Contains(b.Tour.PackageId))
                    .Select(b => b.Tour.PackageId)
            )
            .Union(
                context.Packages
                    .AsNoTracking()
                    .Where(p => p.SharedEditors && p.PackageEditors.Any(e => e.Id == authorId))
                    .Where(p => p.Status == PackageStatus.Published)
                    .Where(p => accessiblePackageIds.Contains(p.Id))
                    .Select(p => p.Id)
            )
            .Distinct()
            .CountAsync();

        return new AuthorStatistics(questionCount, packageCount);
    }

    /// <summary>
    /// Gets the list of packages where the author is an editor (at tour, block, or package level).
    /// </summary>
    /// <param name="authorId">The author ID.</param>
    /// <param name="accessContext">User access context for filtering by access level.</param>
    /// <returns>List of packages with detailed editor information.</returns>
    public async Task<List<AuthorPackageListItem>> GetAuthorPackages(int authorId, PackageAccessContext accessContext)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var accessiblePackageIds = GetAccessiblePackageIdsQuery(context, accessContext);

        // Query tours where author is a tour editor
        var tourData = await context.Tours
            .AsNoTracking()
            .Where(t => t.Package.Status == PackageStatus.Published)
            .Where(t => accessiblePackageIds.Contains(t.PackageId))
            .Where(t => t.Editors.Any(e => e.Id == authorId))
            .Select(t => new
            {
                t.Id,
                t.Number,
                t.PackageId,
                PackageTitle = t.Package.Title
            })
            .ToListAsync();

        // Query blocks where author is a block editor
        var blockData = await context.Blocks
            .AsNoTracking()
            .Where(b => b.Tour.Package.Status == PackageStatus.Published)
            .Where(b => accessiblePackageIds.Contains(b.Tour.PackageId))
            .Where(b => b.Editors.Any(e => e.Id == authorId))
            .Select(b => new
            {
                b.Id,
                b.OrderIndex,
                TourId = b.Tour.Id,
                TourNumber = b.Tour.Number,
                b.Tour.PackageId,
                PackageTitle = b.Tour.Package.Title
            })
            .ToListAsync();

        // Query packages where author is a package-level editor (SharedEditors packages)
        var globalEditorPackages = await context.Packages
            .AsNoTracking()
            .Where(p => p.Status == PackageStatus.Published)
            .Where(p => accessiblePackageIds.Contains(p.Id))
            .Where(p => p.SharedEditors && p.PackageEditors.Any(e => e.Id == authorId))
            .Select(p => new { p.Id, p.Title })
            .ToListAsync();

        // Build packages list
        var globalEditorPackageIds = globalEditorPackages.Select(p => p.Id).ToHashSet();

        var tourItems = tourData
            .Where(t => !globalEditorPackageIds.Contains(t.PackageId))
            .Select(t => new
            {
                t.PackageId,
                t.PackageTitle,
                TourId = t.Id,
                TourNumber = (string?)t.Number,
                BlockId = (int?)null,
                BlockOrderIndex = (int?)null,
                IsGlobalEditor = false
            });

        var blockItems = blockData
            .Where(b => !globalEditorPackageIds.Contains(b.PackageId))
            .Select(b => new
            {
                b.PackageId,
                b.PackageTitle,
                b.TourId,
                TourNumber = (string?)b.TourNumber,
                BlockId = (int?)b.Id,
                BlockOrderIndex = (int?)b.OrderIndex,
                IsGlobalEditor = false
            });

        var globalEditorPackageItems = globalEditorPackages
            .Select(p => new
            {
                PackageId = p.Id,
                PackageTitle = p.Title,
                TourId = 0,
                TourNumber = (string?)null,
                BlockId = (int?)null,
                BlockOrderIndex = (int?)null,
                IsGlobalEditor = true
            });

        return tourItems
            .Union(blockItems)
            .Union(globalEditorPackageItems)
            .GroupBy(x => new { x.PackageId, x.PackageTitle })
            .Select(g => new AuthorPackageListItem
            {
                PackageId = g.Key.PackageId,
                PackageTitle = g.Key.PackageTitle,
                IsGlobalEditor = g.Any(x => x.IsGlobalEditor),
                Tours = g
                    .Where(x => !x.IsGlobalEditor && x.TourNumber != null)
                    .GroupBy(x => new { x.TourId, x.TourNumber })
                    .Select(tg => new AuthorTourListItem
                    {
                        TourId = tg.Key.TourId,
                        TourNumber = tg.Key.TourNumber!,
                        Blocks = tg
                            .Where(x => x.BlockId.HasValue)
                            .OrderBy(x => x.BlockOrderIndex)
                            .Select((x, index) => new AuthorBlockListItem
                            {
                                BlockId = x.BlockId!.Value,
                                BlockNumber = index + 1
                            })
                            .ToList()
                    })
                    .OrderBy(t => int.TryParse(t.TourNumber, out var num) ? num : int.MaxValue)
                    .ThenBy(t => t.TourNumber)
                    .ToList()
            })
            .OrderByDescending(p => p.PackageId)
            .ToList();
    }


    /// <summary>
    /// Attempts to delete an author if they are orphaned (no questions, no tours, and not linked to a user).
    /// </summary>
    /// <param name="authorId">The author ID to check and potentially delete.</param>
    /// <returns>True if the author was deleted, false if they still have relationships.</returns>
    public async Task<bool> TryDeleteAuthorIfOrphaned(int authorId)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await TryDeleteAuthorIfOrphaned(context, authorId);
    }

    /// <summary>
    /// Attempts to delete an author if they are orphaned (no questions, no tours, no blocks, and not linked to a user).
    /// Uses the provided context.
    /// </summary>
    /// <param name="context">Database context to use.</param>
    /// <param name="authorId">The author ID to check and potentially delete.</param>
    /// <returns>True if the author was deleted, false if they still have relationships.</returns>
    public async Task<bool> TryDeleteAuthorIfOrphaned(QuestionsHubDbContext context, int authorId)
    {
        var author = await context.Authors
            .Include(a => a.Questions)
            .Include(a => a.Tours)
            .Include(a => a.Blocks)
            .FirstOrDefaultAsync(a => a.Id == authorId);

        if (author == null)
        {
            return false;
        }

        // Don't delete if author has any questions, tours, blocks, or is linked to a user
        if (author.Questions.Count > 0 || author.Tours.Count > 0 || author.Blocks.Count > 0 || author.UserId != null)
        {
            return false;
        }

        context.Authors.Remove(author);
        await context.SaveChangesAsync();

        return true;
    }

    // ==================== Author-User Linking Methods ====================

    /// <summary>
    /// Links an Author to a User.
    /// </summary>
    /// <param name="authorId">The ID of the author to link.</param>
    /// <param name="userId">The ID of the user to link.</param>
    /// <returns>Result indicating success or failure with a message.</returns>
    public async Task<AuthorOperationResult> LinkAuthorToUser(int authorId, string userId)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var author = await context.Authors
            .FirstOrDefaultAsync(a => a.Id == authorId);

        if (author == null)
        {
            return new AuthorOperationResult(false, "Автора не знайдено.");
        }

        var user = await context.Users
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return new AuthorOperationResult(false, "Користувача не знайдено.");
        }

        // Check if user is already linked to another author
        var existingAuthorLink = await context.Authors
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Id != authorId);

        if (existingAuthorLink != null)
        {
            return new AuthorOperationResult(false, $"Користувач вже пов'язаний з автором '{existingAuthorLink.FullName}'.");
        }

        // Check if author is already linked to another user
        if (author.UserId != null && author.UserId != userId)
        {
            return new AuthorOperationResult(false, "Автор вже пов'язаний з іншим користувачем. Спочатку від'єднайте.");
        }

        author.UserId = userId;
        await context.SaveChangesAsync();

        return new AuthorOperationResult(true, "Автора успішно пов'язано з користувачем.");
    }

    /// <summary>
    /// Unlinks an Author from its User.
    /// </summary>
    /// <param name="authorId">The ID of the author to unlink.</param>
    /// <returns>Result indicating success or failure with a message.</returns>
    public async Task<AuthorOperationResult> UnlinkAuthorFromUser(int authorId)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var author = await context.Authors
            .FirstOrDefaultAsync(a => a.Id == authorId);

        if (author == null)
        {
            return new AuthorOperationResult(false, "Автора не знайдено.");
        }

        if (author.UserId == null)
        {
            return new AuthorOperationResult(false, "Автор не пов'язаний з жодним користувачем.");
        }

        author.UserId = null;
        await context.SaveChangesAsync();

        return new AuthorOperationResult(true, "Зв'язок автора з користувачем успішно видалено.");
    }

    /// <summary>
    /// Gets all users available for linking (not yet linked to any author).
    /// </summary>
    /// <returns>List of users that can be linked to an author.</returns>
    public async Task<List<UserForLinking>> GetAvailableUsersForLinking()
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        // Get all users who are not yet linked to any author
        var linkedUserIds = await context.Authors
            .Where(a => a.UserId != null)
            .Select(a => a.UserId)
            .ToListAsync();

        var availableUsers = await context.Users
            .Where(u => !linkedUserIds.Contains(u.Id))
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .Select(u => new UserForLinking
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                FullName = u.FirstName + " " + u.LastName,
                City = u.City
            })
            .ToListAsync();

        return availableUsers;
    }

    /// <summary>
    /// Gets the linked user for an author.
    /// </summary>
    /// <param name="authorId">The author ID.</param>
    /// <returns>The linked user info, or null if not linked.</returns>
    public async Task<UserForLinking?> GetLinkedUser(int authorId)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var author = await context.Authors
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.Id == authorId);

        if (author?.User == null)
        {
            return null;
        }

        return new UserForLinking
        {
            Id = author.User.Id,
            Email = author.User.Email ?? string.Empty,
            FullName = author.User.FullName,
            City = author.User.City
        };
    }
}

/// <summary>
/// Result of an author operation.
/// </summary>
public record AuthorOperationResult(bool Success, string Message);

/// <summary>
/// DTO for author list display with counts.
/// </summary>
public class AuthorListItem
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FullName => $"{FirstName} {LastName}";
    public int QuestionCount { get; set; }
    public int PackageCount { get; set; }
}

/// <summary>
/// DTO for user information used in linking operations.
/// </summary>
public class UserForLinking
{
    public required string Id { get; set; }
    public required string Email { get; set; }
    public required string FullName { get; set; }
    public string? City { get; set; }
}

/// <summary>
/// Result of author list pagination.
/// </summary>
public class AuthorListResult
{
    public List<AuthorListItem> Items { get; }
    public bool HasNextPage { get; }
    public int TotalCount { get; }

    public AuthorListResult(List<AuthorListItem> items, bool hasNextPage, int totalCount = 0)
    {
        Items = items;
        HasNextPage = hasNextPage;
        TotalCount = totalCount;
    }
}

/// <summary>
/// Statistics for an author, including question count and package count.
/// </summary>
public class AuthorStatistics
{
    public int QuestionCount { get; }
    public int PackageCount { get; }

    public AuthorStatistics(int questionCount, int packageCount)
    {
        QuestionCount = questionCount;
        PackageCount = packageCount;
    }
}

/// <summary>
/// DTO for author package list items, including tours and blocks information.
/// </summary>
public class AuthorPackageListItem
{
    public int PackageId { get; set; }
    public string PackageTitle { get; set; } = "";
    public bool IsGlobalEditor { get; set; }
    public List<AuthorTourListItem> Tours { get; set; } = new();
}

/// <summary>
/// DTO for author tour list items, including blocks information.
/// </summary>
public class AuthorTourListItem
{
    public int TourId { get; set; }
    public string TourNumber { get; set; } = "";
    public List<AuthorBlockListItem> Blocks { get; set; } = new();
}

/// <summary>
/// DTO for author block list items.
/// </summary>
public class AuthorBlockListItem
{
    public int BlockId { get; set; }
    public int BlockNumber { get; set; }
}

