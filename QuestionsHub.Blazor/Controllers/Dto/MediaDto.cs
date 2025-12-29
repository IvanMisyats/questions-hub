namespace QuestionsHub.Blazor.Controllers.Dto;

/// <summary>
/// Response DTO for successful media upload.
/// </summary>
public record MediaUploadResponseDto(
    string FileName,
    string Url
);

/// <summary>
/// Response DTO for media upload error.
/// </summary>
public record MediaUploadErrorDto(
    string Error
);

/// <summary>
/// Specifies the target field for media attachment.
/// </summary>
public enum MediaAttachmentTarget
{
    /// <summary>Question handout (роздатковий матеріал).</summary>
    Handout,

    /// <summary>Comment attachment (ілюстрація до коментаря).</summary>
    Comment
}

