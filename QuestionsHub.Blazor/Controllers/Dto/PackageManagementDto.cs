using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Controllers.Dto;

// ==================== Request DTOs ====================

/// <summary>
/// DTO for creating or updating a package.
/// </summary>
public record PackageUpsertDto(
    string Title,
    string? Description,
    string? Preamble,
    List<string> Editors,
    DateOnly? PlayedAt,
    PackageStatus Status
);

/// <summary>
/// DTO for creating or updating a tour.
/// </summary>
public record TourUpsertDto(
    string Number,
    List<string> Editors,
    string? Preamble,
    string? Comment
);

/// <summary>
/// DTO for creating or updating a question.
/// </summary>
public record QuestionUpsertDto(
    string Number,
    string Text,
    string Answer,
    string? HostInstructions,
    string? HandoutText,
    string? HandoutUrl,
    string? AcceptedAnswers,
    string? RejectedAnswers,
    string? Comment,
    string? CommentAttachmentUrl,
    string? Source,
    List<string> Authors
);

// ==================== Response DTOs ====================

/// <summary>
/// DTO for package list item (summary view).
/// </summary>
public record PackageListItemDto(
    int Id,
    string Title,
    DateOnly? PlayedAt,
    int TourCount,
    int QuestionCount,
    PackageStatus Status,
    string? OwnerName
);

/// <summary>
/// DTO for package details including tours and questions.
/// </summary>
public record PackageDetailDto(
    int Id,
    string Title,
    string? Description,
    string? Preamble,
    List<string> Editors,
    DateOnly? PlayedAt,
    PackageStatus Status,
    string? OwnerId,
    string? OwnerName,
    List<TourDetailDto> Tours
);

/// <summary>
/// DTO for tour details including questions.
/// </summary>
public record TourDetailDto(
    int Id,
    string Number,
    List<string> Editors,
    string? Preamble,
    string? Comment,
    int QuestionCount,
    List<QuestionDetailDto> Questions
);

/// <summary>
/// DTO for question details.
/// </summary>
public record QuestionDetailDto(
    int Id,
    int OrderIndex,
    string Number,
    string Text,
    string Answer,
    string? HostInstructions,
    string? HandoutText,
    string? HandoutUrl,
    string? AcceptedAnswers,
    string? RejectedAnswers,
    string? Comment,
    string? CommentAttachmentUrl,
    string? Source,
    List<string> Authors
);

// ==================== Response wrappers ====================

/// <summary>
/// DTO for created entity response.
/// </summary>
public record CreatedEntityDto(int Id);

