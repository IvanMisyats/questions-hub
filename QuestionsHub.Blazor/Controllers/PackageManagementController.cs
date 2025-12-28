using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Controllers.Dto;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Controllers;

/// <summary>
/// API controller for managing packages, tours, and questions.
/// Requires Editor or Admin role for all operations.
/// Editors can only manage their own packages; Admins can manage all.
/// </summary>
[Route("api/manage")]
[ApiController]
[Authorize(Roles = "Editor,Admin")]
public class PackageManagementController : ControllerBase
{
    private readonly QuestionsHubDbContext _context;

    public PackageManagementController(QuestionsHubDbContext context)
    {
        _context = context;
    }

    // ==================== Package Endpoints ====================

    /// <summary>
    /// Get list of packages. Editors see only their own; Admins see all.
    /// </summary>
    [HttpGet("packages")]
    public async Task<ActionResult<List<PackageListItemDto>>> GetPackages()
    {
        var userId = GetUserId();
        var isAdmin = User.IsInRole("Admin");

        var query = _context.Packages
            .Include(p => p.Owner)
            .Include(p => p.Tours)
            .AsQueryable();

        if (!isAdmin)
        {
            query = query.Where(p => p.OwnerId == userId);
        }

        var packages = await query
            .OrderByDescending(p => p.PlayedAt)
            .ThenBy(p => p.Title)
            .Select(p => new PackageListItemDto(
                p.Id,
                p.Title,
                p.PlayedAt,
                p.Tours.Count,
                p.TotalQuestions,
                p.Status,
                p.Owner != null ? p.Owner.FullName : null
            ))
            .ToListAsync();

        return Ok(packages);
    }

    /// <summary>
    /// Get package details with all tours and questions.
    /// </summary>
    [HttpGet("packages/{id:int}")]
    public async Task<ActionResult<PackageDetailDto>> GetPackage(int id)
    {
        var package = await _context.Packages
            .Include(p => p.Owner)
            .Include(p => p.Tours)
                .ThenInclude(t => t.Questions)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (package == null)
        {
            return NotFound();
        }

        if (!CanAccessPackage(package))
        {
            return Forbid();
        }

        var dto = MapToPackageDetailDto(package);
        return Ok(dto);
    }

    /// <summary>
    /// Create a new package.
    /// </summary>
    [HttpPost("packages")]
    public async Task<ActionResult<CreatedEntityDto>> CreatePackage([FromBody] PackageUpsertDto dto)
    {
        var userId = GetUserId();

        var package = new Package
        {
            Title = dto.Title,
            Description = dto.Description,
            Editors = dto.Editors,
            PlayedAt = dto.PlayedAt,
            Status = dto.Status,
            OwnerId = userId,
            TotalQuestions = 0
        };

        _context.Packages.Add(package);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetPackage), new { id = package.Id }, new CreatedEntityDto(package.Id));
    }

    /// <summary>
    /// Update package properties.
    /// </summary>
    [HttpPut("packages/{id:int}")]
    public async Task<IActionResult> UpdatePackage(int id, [FromBody] PackageUpsertDto dto)
    {
        var package = await _context.Packages.FindAsync(id);

        if (package == null)
        {
            return NotFound();
        }

        if (!CanAccessPackage(package))
        {
            return Forbid();
        }

        package.Title = dto.Title;
        package.Description = dto.Description;
        package.Editors = dto.Editors;
        package.PlayedAt = dto.PlayedAt;
        package.Status = dto.Status;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Delete a package and all its tours and questions.
    /// </summary>
    [HttpDelete("packages/{id:int}")]
    public async Task<IActionResult> DeletePackage(int id)
    {
        var package = await _context.Packages
            .Include(p => p.Tours)
                .ThenInclude(t => t.Questions)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (package == null)
        {
            return NotFound();
        }

        if (!CanAccessPackage(package))
        {
            return Forbid();
        }

        _context.Packages.Remove(package);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // ==================== Tour Endpoints ====================

    /// <summary>
    /// Add a new tour to a package.
    /// </summary>
    [HttpPost("packages/{packageId:int}/tours")]
    public async Task<ActionResult<CreatedEntityDto>> CreateTour(int packageId, [FromBody] TourUpsertDto dto)
    {
        var package = await _context.Packages.FindAsync(packageId);

        if (package == null)
        {
            return NotFound();
        }

        if (!CanAccessPackage(package))
        {
            return Forbid();
        }

        var tour = new Tour
        {
            PackageId = packageId,
            Number = dto.Number,
            Title = dto.Title,
            Editors = dto.Editors,
            Comment = dto.Comment
        };

        _context.Tours.Add(tour);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetPackage), new { id = packageId }, new CreatedEntityDto(tour.Id));
    }

    /// <summary>
    /// Update tour properties.
    /// </summary>
    [HttpPut("tours/{id:int}")]
    public async Task<IActionResult> UpdateTour(int id, [FromBody] TourUpsertDto dto)
    {
        var tour = await _context.Tours
            .Include(t => t.Package)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tour == null)
        {
            return NotFound();
        }

        if (!CanAccessPackage(tour.Package))
        {
            return Forbid();
        }

        tour.Number = dto.Number;
        tour.Title = dto.Title;
        tour.Editors = dto.Editors;
        tour.Comment = dto.Comment;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Delete a tour and all its questions.
    /// </summary>
    [HttpDelete("tours/{id:int}")]
    public async Task<IActionResult> DeleteTour(int id)
    {
        var tour = await _context.Tours
            .Include(t => t.Package)
            .Include(t => t.Questions)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tour == null)
        {
            return NotFound();
        }

        if (!CanAccessPackage(tour.Package))
        {
            return Forbid();
        }

        // Update package TotalQuestions
        tour.Package.TotalQuestions -= tour.Questions.Count;

        _context.Tours.Remove(tour);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // ==================== Question Endpoints ====================

    /// <summary>
    /// Add a new question to a tour.
    /// </summary>
    [HttpPost("tours/{tourId:int}/questions")]
    public async Task<ActionResult<CreatedEntityDto>> CreateQuestion(int tourId, [FromBody] QuestionUpsertDto dto)
    {
        var tour = await _context.Tours
            .Include(t => t.Package)
            .Include(t => t.Questions)
            .FirstOrDefaultAsync(t => t.Id == tourId);

        if (tour == null)
        {
            return NotFound();
        }

        if (!CanAccessPackage(tour.Package))
        {
            return Forbid();
        }

        // Calculate next OrderIndex (0-based)
        var nextOrderIndex = tour.Questions.Count > 0
            ? tour.Questions.Max(q => q.OrderIndex) + 1
            : 0;

        var question = new Question
        {
            TourId = tourId,
            OrderIndex = nextOrderIndex,
            Number = dto.Number,
            Text = dto.Text,
            Answer = dto.Answer,
            HandoutText = dto.HandoutText,
            HandoutUrl = dto.HandoutUrl,
            AcceptedAnswers = dto.AcceptedAnswers,
            RejectedAnswers = dto.RejectedAnswers,
            Comment = dto.Comment,
            CommentAttachmentUrl = dto.CommentAttachmentUrl,
            Source = dto.Source,
            Authors = dto.Authors
        };

        _context.Questions.Add(question);

        // Update package TotalQuestions
        tour.Package.TotalQuestions++;

        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetPackage), new { id = tour.PackageId }, new CreatedEntityDto(question.Id));
    }

    /// <summary>
    /// Update question properties.
    /// </summary>
    [HttpPut("questions/{id:int}")]
    public async Task<IActionResult> UpdateQuestion(int id, [FromBody] QuestionUpsertDto dto)
    {
        var question = await _context.Questions
            .Include(q => q.Tour)
                .ThenInclude(t => t.Package)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (question == null)
        {
            return NotFound();
        }

        if (!CanAccessPackage(question.Tour.Package))
        {
            return Forbid();
        }

        question.Number = dto.Number;
        question.Text = dto.Text;
        question.Answer = dto.Answer;
        question.HandoutText = dto.HandoutText;
        question.HandoutUrl = dto.HandoutUrl;
        question.AcceptedAnswers = dto.AcceptedAnswers;
        question.RejectedAnswers = dto.RejectedAnswers;
        question.Comment = dto.Comment;
        question.CommentAttachmentUrl = dto.CommentAttachmentUrl;
        question.Source = dto.Source;
        question.Authors = dto.Authors;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Delete a question.
    /// </summary>
    [HttpDelete("questions/{id:int}")]
    public async Task<IActionResult> DeleteQuestion(int id)
    {
        var question = await _context.Questions
            .Include(q => q.Tour)
                .ThenInclude(t => t.Package)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (question == null)
        {
            return NotFound();
        }

        if (!CanAccessPackage(question.Tour.Package))
        {
            return Forbid();
        }

        // Update package TotalQuestions
        question.Tour.Package.TotalQuestions--;

        _context.Questions.Remove(question);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // ==================== Helper Methods ====================

    private string? GetUserId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    private bool CanAccessPackage(Package package)
    {
        if (User.IsInRole("Admin"))
        {
            return true;
        }

        var userId = GetUserId();
        return package.OwnerId == userId;
    }

    private static PackageDetailDto MapToPackageDetailDto(Package package)
    {
        return new PackageDetailDto(
            package.Id,
            package.Title,
            package.Description,
            package.Editors,
            package.PlayedAt,
            package.Status,
            package.OwnerId,
            package.Owner?.FullName,
            package.Tours
                .OrderBy(t => t.Number)
                .Select(t => new TourDetailDto(
                    t.Id,
                    t.Number,
                    t.Title,
                    t.Editors,
                    t.Comment,
                    t.Questions.Count,
                    t.Questions
                        .OrderBy(q => q.OrderIndex)
                        .Select(q => new QuestionDetailDto(
                            q.Id,
                            q.OrderIndex,
                            q.Number,
                            q.Text,
                            q.Answer,
                            q.HandoutText,
                            q.HandoutUrl,
                            q.AcceptedAnswers,
                            q.RejectedAnswers,
                            q.Comment,
                            q.CommentAttachmentUrl,
                            q.Source,
                            q.Authors
                        ))
                        .ToList()
                ))
                .ToList()
        );
    }
}

