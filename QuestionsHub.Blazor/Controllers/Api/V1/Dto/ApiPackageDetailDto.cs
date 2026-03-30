namespace QuestionsHub.Blazor.Controllers.Api.V1.Dto;

public record ApiPackageDetailDto(
    int Id,
    string Title,
    string? Description,
    string? Preamble,
    DateOnly? PlayedFrom,
    DateOnly? PlayedTo,
    DateTime? PublicationDate,
    int QuestionsCount,
    string NumberingMode,
    List<ApiAuthorDto> Editors,
    List<ApiTagDto> Tags,
    bool IsAdult,
    List<ApiTourDto> Tours
);

public record ApiTourDto(
    int Id,
    string Number,
    string Type,
    string? Preamble,
    string? Comment,
    List<ApiAuthorDto> Editors,
    List<ApiBlockDto> Blocks,
    List<ApiQuestionDto> Questions
);

public record ApiBlockDto(
    int Id,
    string Name,
    string? Preamble,
    List<ApiAuthorDto> Editors,
    List<ApiQuestionDto> Questions
);

public record ApiQuestionDto(
    int Id,
    string Number,
    string? HostInstructions,
    string Text,
    string Answer,
    string? HandoutText,
    string? HandoutUrl,
    string? AcceptedAnswers,
    string? RejectedAnswers,
    string? Comment,
    string? CommentAttachmentUrl,
    string? Source,
    List<ApiAuthorDto> Authors
);

public record ApiAuthorDto(int Id, string FirstName, string LastName);

public record ApiTagDto(int Id, string Name);
