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

    // Navigation properties
    public List<Question> Questions { get; set; } = [];
    public List<Tour> Tours { get; set; } = [];
}

