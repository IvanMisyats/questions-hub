using Microsoft.AspNetCore.Identity;

namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Represents a user in the QuestionsHub application.
/// Extends IdentityUser with additional profile fields.
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>
    /// User's first name (Ім'я).
    /// Required field.
    /// </summary>
    public required string FirstName { get; set; }

    /// <summary>
    /// User's last name (Прізвище).
    /// Required field.
    /// </summary>
    public required string LastName { get; set; }

    /// <summary>
    /// User's city (Місто).
    /// Optional field.
    /// </summary>
    public string? City { get; set; }

    /// <summary>
    /// User's team name (Команда).
    /// Optional field.
    /// </summary>
    public string? Team { get; set; }

    /// <summary>
    /// Optional link to the Author entity.
    /// When a user is promoted to Editor, an Author is created and linked.
    /// </summary>
    public int? AuthorId { get; set; }

    /// <summary>
    /// Navigation property to the linked Author.
    /// </summary>
    public Author? Author { get; set; }

    /// <summary>
    /// Returns the full name of the user.
    /// </summary>
    public string FullName => $"{FirstName} {LastName}";
}

