namespace QuestionsHub.Blazor.Controllers.Api.V1.Dto;

public record ApiSearchResponse(
    string Query,
    int Count,
    List<ApiSearchResultDto> Results
);

public record ApiSearchResultDto(
    int QuestionId,
    int TourId,
    int PackageId,
    string PackageTitle,
    string TourNumber,
    string QuestionNumber,
    string Text,
    string Answer,
    string? HandoutText,
    string? HandoutUrl,
    string? AcceptedAnswers,
    string? RejectedAnswers,
    string? Comment,
    string? CommentAttachmentUrl,
    string? Source,
    string TextHighlighted,
    string AnswerHighlighted,
    string? HandoutTextHighlighted,
    string? AcceptedAnswersHighlighted,
    string? RejectedAnswersHighlighted,
    string? CommentHighlighted,
    string? SourceHighlighted,
    List<ApiAuthorDto> Authors,
    bool IsAdult,
    double Rank
);
