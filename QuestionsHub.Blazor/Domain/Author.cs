namespace QuestionsHub.Blazor.Domain;

/// <summary>
/// Represents an author/editor of questions or tours.
/// </summary>
public class Author
{
    public int Id { get; set; }

    /// <summary>Author's first name (Ім'я).</summary>
    public required string FirstName { get; set; }

    /// <summary>Author's last name (Прізвище).</summary>
    public required string LastName { get; set; }

    /// <summary>Full name for display.</summary>
    public string FullName => $"{FirstName} {LastName}";

    /// <summary>
    /// Optional link to the application user account.
    /// When a user is promoted to Editor, their account is linked to an Author.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Navigation property to the linked user.
    /// </summary>
    public ApplicationUser? User { get; set; }

    // Navigation properties
    public List<Question> Questions { get; set; } = [];
    public List<Tour> Tours { get; set; } = [];
}

