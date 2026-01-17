using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Service responsible for renumbering questions and tours within a package.
/// Implements the single source of truth for ordering via OrderIndex fields.
/// </summary>
public class PackageRenumberingService(IDbContextFactory<QuestionsHubDbContext> dbContextFactory)
{
    /// <summary>
    /// Renumbers all tours and questions within a package according to the package's numbering mode.
    /// </summary>
    /// <param name="packageId">The ID of the package to renumber.</param>
    /// <returns>True if renumbering was successful, false if package was not found.</returns>
    public async Task<bool> RenumberPackage(int packageId)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();

        var package = await context.Packages
            .Include(p => p.Tours)
                .ThenInclude(t => t.Questions)
            .FirstOrDefaultAsync(p => p.Id == packageId);

        if (package == null)
            return false;

        RenumberPackageInMemory(package);

        await context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Renumbers all tours and questions within a package in memory.
    /// Useful when you have a tracked package entity and want to avoid an extra DB load.
    /// Call SaveChanges on the context after calling this method.
    /// </summary>
    public void RenumberPackageInMemory(Package package)
    {
        // 1. Ensure warmup tour is first if it exists
        EnforceWarmupFirst(package);

        // 2. Renumber tours (display numbers for main tours)
        RenumberTours(package);

        // 3. Renumber questions based on numbering mode
        RenumberQuestions(package);
    }

    /// <summary>
    /// Ensures the warmup tour (if any) has OrderIndex = 0 and all other tours are shifted.
    /// </summary>
    private static void EnforceWarmupFirst(Package package)
    {
        var warmupTour = package.Tours.FirstOrDefault(t => t.IsWarmup);
        if (warmupTour == null)
            return;

        // If warmup already at index 0, nothing to do
        if (warmupTour.OrderIndex == 0)
            return;

        // Move warmup to front by adjusting all OrderIndex values
        var orderedTours = package.Tours.OrderBy(t => t.OrderIndex).ToList();
        orderedTours.Remove(warmupTour);
        orderedTours.Insert(0, warmupTour);

        for (int i = 0; i < orderedTours.Count; i++)
        {
            orderedTours[i].OrderIndex = i;
        }
    }

    /// <summary>
    /// Assigns sequential display numbers to main tours (1, 2, 3...).
    /// Warmup tour gets "0" as its display number.
    /// </summary>
    private static void RenumberTours(Package package)
    {
        var orderedTours = package.Tours.OrderBy(t => t.OrderIndex).ToList();
        int mainTourNumber = 1;

        foreach (var tour in orderedTours)
        {
            if (tour.IsWarmup)
            {
                tour.Number = "0";
            }
            else
            {
                tour.Number = mainTourNumber.ToString(CultureInfo.InvariantCulture);
                mainTourNumber++;
            }
        }
    }

    /// <summary>
    /// Renumbers questions within the package based on the numbering mode.
    /// </summary>
    private static void RenumberQuestions(Package package)
    {
        if (package.NumberingMode == QuestionNumberingMode.Manual)
        {
            // Manual mode: don't change Question.Number, but still ensure OrderIndex consistency
            NormalizeQuestionOrderIndices(package);
            return;
        }

        var orderedTours = package.Tours.OrderBy(t => t.OrderIndex).ToList();
        int globalQuestionNumber = 1;

        foreach (var tour in orderedTours)
        {
            // Sort questions by OrderIndex within the tour
            var orderedQuestions = tour.Questions.OrderBy(q => q.OrderIndex).ToList();

            // Normalize OrderIndex to be sequential (0, 1, 2...)
            for (int i = 0; i < orderedQuestions.Count; i++)
            {
                orderedQuestions[i].OrderIndex = i;
            }

            if (tour.IsWarmup)
            {
                // Warmup questions: numbered 1..k within the warmup tour
                for (int i = 0; i < orderedQuestions.Count; i++)
                {
                    orderedQuestions[i].Number = (i + 1).ToString(CultureInfo.InvariantCulture);
                }
            }
            else
            {
                // Main tour questions
                if (package.NumberingMode == QuestionNumberingMode.Global)
                {
                    // Global: continue numbering from where we left off
                    for (int i = 0; i < orderedQuestions.Count; i++)
                    {
                        orderedQuestions[i].Number = globalQuestionNumber.ToString(CultureInfo.InvariantCulture);
                        globalQuestionNumber++;
                    }
                }
                else // PerTour
                {
                    // PerTour: start from 1 for each tour
                    for (int i = 0; i < orderedQuestions.Count; i++)
                    {
                        orderedQuestions[i].Number = (i + 1).ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Normalizes OrderIndex values for all questions in the package to be sequential within each tour.
    /// </summary>
    private static void NormalizeQuestionOrderIndices(Package package)
    {
        foreach (var tour in package.Tours)
        {
            var orderedQuestions = tour.Questions.OrderBy(q => q.OrderIndex).ToList();
            for (int i = 0; i < orderedQuestions.Count; i++)
            {
                orderedQuestions[i].OrderIndex = i;
            }
        }
    }

    /// <summary>
    /// Sets a tour as the warmup tour. Ensures only one warmup exists in the package.
    /// Also moves the warmup tour to the first position (OrderIndex = 0).
    /// </summary>
    /// <param name="package">The package containing the tour.</param>
    /// <param name="tourId">The tour ID to set as warmup.</param>
    /// <param name="isWarmup">Whether to set or unset the warmup flag.</param>
    public void SetWarmupTour(Package package, int tourId, bool isWarmup)
    {
        // First, unset warmup on all tours
        foreach (var tour in package.Tours)
        {
            tour.IsWarmup = false;
        }

        if (!isWarmup)
        {
            // Just unsetting - renumber to restore normal order
            RenumberPackageInMemory(package);
            return;
        }

        // Set the specified tour as warmup
        var warmupTour = package.Tours.FirstOrDefault(t => t.Id == tourId);
        if (warmupTour == null)
            return;

        warmupTour.IsWarmup = true;

        // Move warmup tour to the front
        var orderedTours = package.Tours.OrderBy(t => t.OrderIndex).ToList();
        orderedTours.Remove(warmupTour);
        orderedTours.Insert(0, warmupTour);

        for (int i = 0; i < orderedTours.Count; i++)
        {
            orderedTours[i].OrderIndex = i;
        }

        // Renumber everything
        RenumberPackageInMemory(package);
    }
}
