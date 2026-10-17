namespace QuestionsHub.Blazor.Controllers.Api.Manage;

public record ManageImportJobDto(
    Guid JobId, string FileName, string RequestedGameType, string Status,
    string? CurrentStep, int Progress, DateTime CreatedAt, DateTime? StartedAt,
    DateTime? FinishedAt, DateTime? NextRetryAt, int? PackageId,
    string? ErrorMessage, string[] Warnings);

public record ManageImportsResponse(IReadOnlyList<ManageImportJobDto> Imports);
