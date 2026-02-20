using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Controllers.Dto;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Infrastructure;
using QuestionsHub.Blazor.Infrastructure.Media;

namespace QuestionsHub.Blazor.Controllers;

/// <summary>
/// API controller for media file uploads and management.
/// Requires Editor or Admin role for all operations.
/// </summary>
[Route("api/media")]
[ApiController]
[Authorize(Roles = "Editor,Admin")]
public class MediaController : ControllerBase
{
    private readonly MediaService _mediaService;
    private readonly QuestionsHubDbContext _context;
    private readonly ILogger<MediaController> _logger;

    public MediaController(
        MediaService mediaService,
        QuestionsHubDbContext context,
        ILogger<MediaController> logger)
    {
        _mediaService = mediaService;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Upload media for a question's handout or comment attachment.
    /// Replaces existing media if present (simple deletion strategy).
    /// </summary>
    /// <param name="questionId">Question ID to attach media to.</param>
    /// <param name="target">Target field: 'handout' or 'comment'.</param>
    /// <param name="file">The file to upload.</param>
    [HttpPost("questions/{questionId:int}/{target}")]
    [RequestSizeLimit(100 * 1024 * 1024)] // 100 MB limit at request level
    public async Task<IActionResult> UploadQuestionMedia(
        int questionId,
        string target,
        IFormFile file)
    {
        // Parse target
        if (!TryParseTarget(target, out var attachmentTarget))
        {
            return BadRequest(new MediaUploadErrorDto("Невірний тип вкладення. Допустимі: handout, comment"));
        }

        // Get question and verify access
        var question = await _context.Questions
            .Include(q => q.Tour)
                .ThenInclude(t => t.Package)
            .FirstOrDefaultAsync(q => q.Id == questionId);

        if (question == null)
        {
            return NotFound(new MediaUploadErrorDto("Запитання не знайдено"));
        }

        if (!User.CanAccessPackage(question.Tour.Package))
        {
            return Forbid();
        }

        // Validate file
        var (isValid, errorMessage) = _mediaService.ValidateFile(file.FileName, file.Length);
        if (!isValid)
        {
            return BadRequest(new MediaUploadErrorDto(errorMessage!));
        }

        // Remember existing media URL for deletion after successful upload
        var existingUrl = attachmentTarget == MediaAttachmentTarget.Handout
            ? question.HandoutUrl
            : question.CommentAttachmentUrl;

        // Upload new file first
        await using var stream = file.OpenReadStream();
        var result = await _mediaService.UploadAsync(stream, file.FileName);

        if (!result.Success)
        {
            return StatusCode(500, new MediaUploadErrorDto(result.ErrorMessage!));
        }

        // Update question with new URL
        if (attachmentTarget == MediaAttachmentTarget.Handout)
        {
            question.HandoutUrl = result.RelativeUrl;
        }
        else
        {
            question.CommentAttachmentUrl = result.RelativeUrl;
        }

        await _context.SaveChangesAsync();

        // Delete old file only after new URL is saved to DB
        if (!string.IsNullOrEmpty(existingUrl))
        {
            _mediaService.Delete(existingUrl);
            _logger.LogInformation(
                "Deleted old {Target} media for question {QuestionId}: {Url}",
                target, questionId, existingUrl);
        }

        _logger.LogInformation(
            "Uploaded {Target} media for question {QuestionId}: {Url}",
            target, questionId, result.RelativeUrl);

        return Ok(new MediaUploadResponseDto(result.FileName!, result.RelativeUrl!));
    }

    /// <summary>
    /// Delete media from a question's handout or comment attachment.
    /// </summary>
    /// <param name="questionId">Question ID.</param>
    /// <param name="target">Target field: 'handout' or 'comment'.</param>
    [HttpDelete("questions/{questionId:int}/{target}")]
    public async Task<IActionResult> DeleteQuestionMedia(int questionId, string target)
    {
        // Parse target
        if (!TryParseTarget(target, out var attachmentTarget))
        {
            return BadRequest(new MediaUploadErrorDto("Невірний тип вкладення. Допустимі: handout, comment"));
        }

        // Get question and verify access
        var question = await _context.Questions
            .Include(q => q.Tour)
                .ThenInclude(t => t.Package)
            .FirstOrDefaultAsync(q => q.Id == questionId);

        if (question == null)
        {
            return NotFound(new MediaUploadErrorDto("Запитання не знайдено"));
        }

        if (!User.CanAccessPackage(question.Tour.Package))
        {
            return Forbid();
        }

        // Get existing URL
        var existingUrl = attachmentTarget == MediaAttachmentTarget.Handout
            ? question.HandoutUrl
            : question.CommentAttachmentUrl;

        if (string.IsNullOrEmpty(existingUrl))
        {
            return NoContent(); // Nothing to delete
        }

        // Delete file
        _mediaService.Delete(existingUrl);

        // Update question
        if (attachmentTarget == MediaAttachmentTarget.Handout)
        {
            question.HandoutUrl = null;
        }
        else
        {
            question.CommentAttachmentUrl = null;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Deleted {Target} media for question {QuestionId}: {Url}",
            target, questionId, existingUrl);

        return NoContent();
    }

    // ==================== Helper Methods ====================

    private static bool TryParseTarget(string target, out MediaAttachmentTarget result)
    {
        result = target.ToLowerInvariant() switch
        {
            "handout" => MediaAttachmentTarget.Handout,
            "comment" => MediaAttachmentTarget.Comment,
            _ => default
        };

        return target.ToLowerInvariant() is "handout" or "comment";
    }
}


