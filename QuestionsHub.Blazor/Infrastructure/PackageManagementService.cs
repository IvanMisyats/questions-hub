using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Service for managing package structure: tours, blocks, and questions.
/// Handles reordering, adding, and deleting entities with proper renumbering.
/// </summary>
public class PackageManagementService(
    IDbContextFactory<QuestionsHubDbContext> dbContextFactory,
    PackageRenumberingService renumberingService)
{
    #region Result Types

    /// <summary>
    /// Result of a package management operation.
    /// </summary>
    public record OperationResult(bool Success, string? ErrorMessage = null)
    {
        public static OperationResult Ok() => new(true);
        public static OperationResult Fail(string error) => new(false, error);
    }

    /// <summary>
    /// Result of creating a new entity.
    /// </summary>
    public record CreateResult<T>(bool Success, T? Entity, string? ErrorMessage = null)
    {
        public static CreateResult<T> Ok(T entity) => new(true, entity);
        public static CreateResult<T> Fail(string error) => new(false, default, error);
    }

    /// <summary>
    /// DTO for question order information.
    /// </summary>
    public record QuestionOrderItem(int QuestionId, int? BlockId);

    #endregion

    #region Tour Operations

    /// <summary>
    /// Reorders tours within a package based on the provided order.
    /// </summary>
    public async Task<OperationResult> ReorderTours(int packageId, int[] tourIds)
    {
        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync();

            var tours = await context.Tours
                .Where(t => t.PackageId == packageId)
                .ToListAsync();

            for (int i = 0; i < tourIds.Length; i++)
            {
                var tour = tours.FirstOrDefault(t => t.Id == tourIds[i]);
                if (tour != null)
                {
                    tour.OrderIndex = i;
                }
            }

            await context.SaveChangesAsync();
            await renumberingService.RenumberPackage(packageId);

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Creates a new tour in a package.
    /// </summary>
    public async Task<CreateResult<Tour>> CreateTour(int packageId)
    {
        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync();

            var existingTours = await context.Tours
                .Where(t => t.PackageId == packageId)
                .ToListAsync();

            var maxOrderIndex = existingTours.Count > 0
                ? existingTours.Max(t => t.OrderIndex)
                : -1;
            var newOrderIndex = maxOrderIndex + 1;

            var mainTourCount = existingTours.Count(t => !t.IsWarmup);
            var newNumber = (mainTourCount + 1).ToString(CultureInfo.InvariantCulture);

            var tour = new Tour
            {
                PackageId = packageId,
                Number = newNumber,
                OrderIndex = newOrderIndex,
                IsWarmup = false,
                Editors = [],
                Questions = [],
                Blocks = []
            };

            context.Tours.Add(tour);
            await context.SaveChangesAsync();

            return CreateResult<Tour>.Ok(tour);
        }
        catch (Exception ex)
        {
            return CreateResult<Tour>.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Deletes a tour and all its questions.
    /// </summary>
    public async Task<OperationResult> DeleteTour(int tourId)
    {
        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync();

            var tour = await context.Tours
                .Include(t => t.Questions)
                .FirstOrDefaultAsync(t => t.Id == tourId);

            if (tour == null)
                return OperationResult.Fail("Tour not found");

            var questionCount = tour.Questions.Count;
            var packageId = tour.PackageId;

            var package = await context.Packages.FindAsync(packageId);
            if (package != null)
            {
                package.TotalQuestions -= questionCount;
            }

            context.Tours.Remove(tour);
            await context.SaveChangesAsync();

            await renumberingService.RenumberPackage(packageId);

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Sets or unsets a tour as warmup, moving it to the first position if set.
    /// </summary>
    public async Task<OperationResult> SetWarmup(int tourId, bool isWarmup)
    {
        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync();

            var tour = await context.Tours.FindAsync(tourId);
            if (tour == null)
                return OperationResult.Fail("Tour not found");

            var packageId = tour.PackageId;

            var allTours = await context.Tours
                .Where(t => t.PackageId == packageId)
                .ToListAsync();

            // Unset warmup on all tours first
            foreach (var t in allTours)
            {
                t.IsWarmup = false;
            }

            if (isWarmup)
            {
                tour.IsWarmup = true;
                tour.OrderIndex = 0;

                // Shift other tours
                var otherTours = allTours
                    .Where(t => t.Id != tourId)
                    .OrderBy(t => t.OrderIndex)
                    .ToList();

                for (int i = 0; i < otherTours.Count; i++)
                {
                    otherTours[i].OrderIndex = i + 1;
                }
            }

            await context.SaveChangesAsync();
            await renumberingService.RenumberPackage(packageId);

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    #endregion

    #region Question Operations

    /// <summary>
    /// Creates a new question in a tour.
    /// </summary>
    public async Task<CreateResult<Question>> CreateQuestion(int tourId, int? blockId = null, List<Author>? authors = null)
    {
        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync();

            var tour = await context.Tours
                .Include(t => t.Questions)
                .FirstOrDefaultAsync(t => t.Id == tourId);

            if (tour == null)
                return CreateResult<Question>.Fail("Tour not found");

            var newOrderIndex = tour.Questions.Count;

            var question = new Question
            {
                TourId = tourId,
                BlockId = blockId,
                OrderIndex = newOrderIndex,
                Number = "0", // Temporary, will be set by renumbering
                Text = "",
                Answer = ""
            };

            context.Questions.Add(question);

            // Handle authors
            if (authors != null && authors.Count > 0)
            {
                foreach (var author in authors)
                {
                    var dbAuthor = await context.Authors.FindAsync(author.Id);
                    if (dbAuthor != null)
                    {
                        question.Authors.Add(dbAuthor);
                    }
                }
            }

            var package = await context.Packages.FindAsync(tour.PackageId);
            if (package != null)
            {
                package.TotalQuestions++;
            }

            await context.SaveChangesAsync();
            await renumberingService.RenumberPackage(tour.PackageId);

            // Reload from a fresh context to get the updated number after renumbering
            await using var reloadContext = await dbContextFactory.CreateDbContextAsync();
            var reloadedQuestion = await reloadContext.Questions
                .Include(q => q.Authors)
                .FirstOrDefaultAsync(q => q.Id == question.Id);

            return CreateResult<Question>.Ok(reloadedQuestion!);
        }
        catch (Exception ex)
        {
            return CreateResult<Question>.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Deletes a question.
    /// </summary>
    public async Task<OperationResult> DeleteQuestion(int questionId)
    {
        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync();

            var question = await context.Questions
                .Include(q => q.Tour)
                .FirstOrDefaultAsync(q => q.Id == questionId);

            if (question == null)
                return OperationResult.Fail("Question not found");

            var packageId = question.Tour.PackageId;

            var package = await context.Packages.FindAsync(packageId);
            if (package != null)
            {
                package.TotalQuestions--;
            }

            context.Questions.Remove(question);
            await context.SaveChangesAsync();

            await renumberingService.RenumberPackage(packageId);

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Moves a question to a new position, potentially across tours or blocks.
    /// </summary>
    public async Task<OperationResult> MoveQuestion(
        int questionId,
        int fromTourId,
        int toTourId,
        int newIndex,
        int? fromBlockId = null,
        int? toBlockId = null)
    {
        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync();

            var question = await context.Questions.FindAsync(questionId);
            if (question == null)
                return OperationResult.Fail("Question not found");

            var fromTour = await context.Tours
                .Include(t => t.Questions)
                .FirstOrDefaultAsync(t => t.Id == fromTourId);

            if (fromTour == null)
                return OperationResult.Fail("Source tour not found");

            var toTour = fromTourId == toTourId
                ? fromTour
                : await context.Tours
                    .Include(t => t.Questions)
                    .FirstOrDefaultAsync(t => t.Id == toTourId);

            if (toTour == null)
                return OperationResult.Fail("Target tour not found");

            var isSameTour = fromTourId == toTourId;
            var isSameBlock = fromBlockId == toBlockId;

            if (isSameTour && isSameBlock)
            {
                ReorderWithinSameContainer(fromTour.Questions, question, newIndex, fromBlockId);
            }
            else
            {
                MoveToNewContainer(fromTour, toTour, question, newIndex, fromBlockId, toBlockId);
            }

            await context.SaveChangesAsync();
            await renumberingService.RenumberPackage(fromTour.PackageId);

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Reorders questions within tours based on the provided order.
    /// Supports moving questions between tours.
    /// </summary>
    public async Task<OperationResult> ReorderQuestions(
        int packageId,
        int toTourId,
        List<QuestionOrderItem> toTourOrder,
        int? fromTourId = null,
        List<QuestionOrderItem>? fromTourOrder = null)
    {
        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync();

            // Get all questions for the package
            var packageTours = await context.Tours
                .Where(t => t.PackageId == packageId)
                .Include(t => t.Questions)
                .ToListAsync();

            var toTour = packageTours.FirstOrDefault(t => t.Id == toTourId);
            if (toTour == null)
                return OperationResult.Fail("Target tour not found");

            // Process target tour order
            ApplyQuestionOrder(packageTours, toTour, toTourOrder);

            // Process source tour if different
            if (fromTourId.HasValue && fromTourId.Value != toTourId && fromTourOrder != null)
            {
                var fromTour = packageTours.FirstOrDefault(t => t.Id == fromTourId.Value);
                if (fromTour != null)
                {
                    ApplyQuestionOrder(packageTours, fromTour, fromTourOrder);
                }
            }

            await context.SaveChangesAsync();
            await renumberingService.RenumberPackage(packageId);

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    #endregion

    #region Block Operations

    /// <summary>
    /// Reorders blocks within a tour.
    /// </summary>
    public async Task<OperationResult> ReorderBlocks(int tourId, int[] blockIds)
    {
        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync();

            var blocks = await context.Blocks
                .Where(b => b.TourId == tourId)
                .ToListAsync();

            for (int i = 0; i < blockIds.Length; i++)
            {
                var block = blocks.FirstOrDefault(b => b.Id == blockIds[i]);
                if (block != null)
                {
                    block.OrderIndex = i;
                }
            }

            await context.SaveChangesAsync();

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    #endregion

    #region Private Helpers

    private static void ReorderWithinSameContainer(
        ICollection<Question> tourQuestions,
        Question question,
        int newIndex,
        int? blockId)
    {
        var questions = tourQuestions
            .Where(q => q.BlockId == blockId)
            .OrderBy(q => q.OrderIndex)
            .ToList();

        var oldIndex = questions.IndexOf(question);
        if (oldIndex < 0 || oldIndex == newIndex)
            return;

        questions.RemoveAt(oldIndex);

        if (newIndex > questions.Count)
            newIndex = questions.Count;

        questions.Insert(newIndex, question);

        for (int i = 0; i < questions.Count; i++)
        {
            questions[i].OrderIndex = i;
        }
    }

    private static void MoveToNewContainer(
        Tour fromTour,
        Tour toTour,
        Question question,
        int newIndex,
        int? fromBlockId,
        int? toBlockId)
    {
        var isSameTour = fromTour.Id == toTour.Id;

        if (!isSameTour)
        {
            question.TourId = toTour.Id;
        }

        question.BlockId = toBlockId;

        // Reindex source container (excluding moved question)
        var sourceQuestions = fromTour.Questions
            .Where(q => q.BlockId == fromBlockId && q.Id != question.Id)
            .OrderBy(q => q.OrderIndex)
            .ToList();

        for (int i = 0; i < sourceQuestions.Count; i++)
        {
            sourceQuestions[i].OrderIndex = i;
        }

        // Reindex target container (insert moved question at new position)
        var targetQuestions = toTour.Questions
            .Where(q => q.BlockId == toBlockId && q.Id != question.Id)
            .OrderBy(q => q.OrderIndex)
            .ToList();

        if (newIndex > targetQuestions.Count)
            newIndex = targetQuestions.Count;

        targetQuestions.Insert(newIndex, question);

        for (int i = 0; i < targetQuestions.Count; i++)
        {
            targetQuestions[i].OrderIndex = i;
        }
    }

    private static void ApplyQuestionOrder(
        List<Tour> allTours,
        Tour targetTour,
        List<QuestionOrderItem> order)
    {
        for (int i = 0; i < order.Count; i++)
        {
            var item = order[i];

            // Try to find the question in the target tour first
            var question = targetTour.Questions.FirstOrDefault(q => q.Id == item.QuestionId);

            // If not found, search in other tours (question is being moved)
            if (question == null)
            {
                foreach (var otherTour in allTours.Where(t => t.Id != targetTour.Id))
                {
                    question = otherTour.Questions.FirstOrDefault(q => q.Id == item.QuestionId);
                    if (question != null)
                    {
                        // Move question to target tour
                        question.TourId = targetTour.Id;
                        break;
                    }
                }
            }

            if (question != null)
            {
                question.OrderIndex = i;
                question.BlockId = item.BlockId;
            }
        }
    }

    #endregion
}
