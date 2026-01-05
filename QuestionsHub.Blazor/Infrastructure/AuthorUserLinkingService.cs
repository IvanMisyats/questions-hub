using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Service for managing the link between Author and ApplicationUser entities.
/// </summary>
public class AuthorUserLinkingService
{
    private readonly IDbContextFactory<QuestionsHubDbContext> _dbContextFactory;

    public AuthorUserLinkingService(IDbContextFactory<QuestionsHubDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    /// <summary>
    /// Links an Author to a User.
    /// </summary>
    /// <param name="authorId">The ID of the author to link.</param>
    /// <param name="userId">The ID of the user to link.</param>
    /// <returns>True if linking was successful, false otherwise.</returns>
    public async Task<LinkResult> LinkAuthorToUserAsync(int authorId, string userId)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var author = await context.Authors
            .FirstOrDefaultAsync(a => a.Id == authorId);

        if (author == null)
        {
            return new LinkResult(false, "Автора не знайдено.");
        }

        var user = await context.Users
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return new LinkResult(false, "Користувача не знайдено.");
        }

        // Check if user is already linked to another author
        var existingAuthorLink = await context.Authors
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Id != authorId);

        if (existingAuthorLink != null)
        {
            return new LinkResult(false, $"Користувач вже пов'язаний з автором '{existingAuthorLink.FullName}'.");
        }

        // Check if author is already linked to another user
        if (author.UserId != null && author.UserId != userId)
        {
            return new LinkResult(false, "Автор вже пов'язаний з іншим користувачем. Спочатку від'єднайте.");
        }

        author.UserId = userId;
        await context.SaveChangesAsync();

        return new LinkResult(true, "Автора успішно пов'язано з користувачем.");
    }

    /// <summary>
    /// Unlinks an Author from its User.
    /// </summary>
    /// <param name="authorId">The ID of the author to unlink.</param>
    /// <returns>True if unlinking was successful, false otherwise.</returns>
    public async Task<LinkResult> UnlinkAuthorFromUserAsync(int authorId)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var author = await context.Authors
            .FirstOrDefaultAsync(a => a.Id == authorId);

        if (author == null)
        {
            return new LinkResult(false, "Автора не знайдено.");
        }

        if (author.UserId == null)
        {
            return new LinkResult(false, "Автор не пов'язаний з жодним користувачем.");
        }

        author.UserId = null;
        await context.SaveChangesAsync();

        return new LinkResult(true, "Зв'язок автора з користувачем успішно видалено.");
    }

    /// <summary>
    /// Gets all users available for linking (not yet linked to any author).
    /// </summary>
    /// <returns>List of users that can be linked to an author.</returns>
    public async Task<List<UserForLinking>> GetAvailableUsersForLinkingAsync()
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
    public async Task<UserForLinking?> GetLinkedUserAsync(int authorId)
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
/// Result of a link/unlink operation.
/// </summary>
public record LinkResult(bool Success, string Message);

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

