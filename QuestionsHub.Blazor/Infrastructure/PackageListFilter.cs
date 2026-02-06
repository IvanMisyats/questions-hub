namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Sort field options for package list.
/// </summary>
public enum PackageSortField
{
    /// <summary>Sort by publication date (default).</summary>
    PublicationDate,
    /// <summary>Sort by play start date.</summary>
    PlayedFrom
}

/// <summary>
/// Sort direction options.
/// </summary>
public enum SortDirection
{
    /// <summary>Ascending order (oldest first for dates).</summary>
    Asc,
    /// <summary>Descending order (newest first for dates).</summary>
    Desc
}

/// <summary>
/// Filter and sort options for package list query.
/// </summary>
/// <param name="TitleSearch">Partial title match (case-insensitive). Null or empty means no filter.</param>
/// <param name="EditorId">Filter by editor ID. Null means no filter (all editors).</param>
/// <param name="TagId">Filter by tag ID. Null means no filter (all tags).</param>
/// <param name="SortField">Field to sort by.</param>
/// <param name="SortDir">Sort direction.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Number of items per page.</param>
public record PackageListFilter(
    string? TitleSearch = null,
    int? EditorId = null,
    int? TagId = null,
    PackageSortField SortField = PackageSortField.PublicationDate,
    SortDirection SortDir = SortDirection.Desc,
    int Page = 1,
    int PageSize = 24
);

/// <summary>
/// DTO for package card display on home page.
/// </summary>
public record PackageCardDto(
    int Id,
    string Title,
    string? Description,
    DateTime? PublicationDate,
    DateOnly? PlayedFrom,
    DateOnly? PlayedTo,
    int QuestionsCount,
    List<EditorBriefDto> Editors,
    List<TagBriefDto> Tags
);

/// <summary>
/// Brief editor info for package cards.
/// </summary>
public record EditorBriefDto(int Id, string FirstName, string LastName)
{
    public string FullName => $"{FirstName} {LastName}";
}

/// <summary>
/// Editor info for filter dropdown.
/// </summary>
public record EditorFilterDto(int Id, string FullName);

/// <summary>
/// Brief tag info for package cards and filters.
/// </summary>
public record TagBriefDto(int Id, string Name);

/// <summary>
/// Paginated result of package list query.
/// </summary>
public record PackageListResult(
    List<PackageCardDto> Packages,
    int TotalCount,
    int TotalPages,
    int CurrentPage
);
