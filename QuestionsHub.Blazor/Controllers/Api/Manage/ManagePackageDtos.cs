namespace QuestionsHub.Blazor.Controllers.Api.Manage;

// Response shapes of the agent read API (/api/v1/manage). Enum values are camelCase strings
// (AgentApiNames); ids, orderIndex and blockId are included so changesets can address everything.

public record ManageAuthorDto(int Id, string FirstName, string LastName);

public record ManageTagDto(int Id, string Name);

public record ManagePackageListItemDto(
    int Id,
    string Title,
    string GameType,
    string Status,
    string AccessLevel,
    int TotalQuestions,
    int ToursCount,
    DateOnly? PlayedFrom,
    DateOnly? PlayedTo,
    DateTime? PublicationDate,
    string? OwnerName,
    bool HasResults);

public record ManagePackageListResponse(
    IReadOnlyList<ManagePackageListItemDto> Packages,
    int TotalCount,
    int Page,
    int PageSize);

/// <param name="Version">Content fingerprint; pass it as <c>expectedVersion</c> when applying a changeset.</param>
/// <param name="HasResults">Tournament results are attached — structural changes are rejected for such packages.</param>
public record ManagePackageDto(
    int Id,
    string Title,
    string GameType,
    string Status,
    string AccessLevel,
    string NumberingMode,
    bool SharedEditors,
    string? Description,
    string? Preamble,
    DateOnly? PlayedFrom,
    DateOnly? PlayedTo,
    DateTime? PublicationDate,
    int TotalQuestions,
    bool HasResults,
    string Version,
    IReadOnlyList<ManageAuthorDto> Editors,
    IReadOnlyList<ManageTagDto> Tags,
    IReadOnlyList<ManageTourDto> Tours);

/// <param name="Questions">Questions not in any block; questions in blocks are listed under <c>Blocks</c>.</param>
public record ManageTourDto(
    int Id,
    int OrderIndex,
    string Number,
    string Type,
    string? Title,
    string? Preamble,
    string? Comment,
    IReadOnlyList<ManageAuthorDto> Editors,
    IReadOnlyList<ManageBlockDto> Blocks,
    IReadOnlyList<ManageQuestionDto> Questions);

public record ManageBlockDto(
    int Id,
    int OrderIndex,
    string? Name,
    string? Preamble,
    IReadOnlyList<ManageAuthorDto> Editors,
    IReadOnlyList<ManageQuestionDto> Questions);

public record ManageQuestionDto(
    int Id,
    int OrderIndex,
    int? BlockId,
    string Number,
    string? HostInstructions,
    string Text,
    string? HandoutText,
    string? HandoutUrl,
    string Answer,
    string? AcceptedAnswers,
    string? RejectedAnswers,
    string? AnswerForm,
    string? Comment,
    string? CommentAttachmentUrl,
    string? Source,
    IReadOnlyList<ManageAuthorDto> Authors);
