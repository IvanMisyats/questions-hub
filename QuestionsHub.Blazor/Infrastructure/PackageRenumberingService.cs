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
                .ThenInclude(t => t.Blocks)
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
        // 1. Ensure special tours are in correct positions (warmup first, shootout last)
        EnforceSpecialTourPositions(package);

        // 2. Renumber tours (display numbers for main tours)
        RenumberTours(package);

        // 3. Renumber questions based on numbering mode
        RenumberQuestions(package);
    }

    /// <summary>
    /// Ensures the warmup tour (if any) has OrderIndex = 0, and the shootout tour (if any) has the highest OrderIndex.
    /// </summary>
    private static void EnforceSpecialTourPositions(Package package)
    {
        var warmupTour = package.Tours.FirstOrDefault(t => t.Type == TourType.Warmup);
        var shootoutTour = package.Tours.FirstOrDefault(t => t.Type == TourType.Shootout);

        // Build ordered list: warmup first, then regular tours by OrderIndex, then shootout last
        var regularTours = package.Tours
            .Where(t => t.Type == TourType.Regular)
            .OrderBy(t => t.OrderIndex)
            .ToList();

        var orderedTours = new List<Tour>();
        if (warmupTour != null) orderedTours.Add(warmupTour);
        orderedTours.AddRange(regularTours);
        if (shootoutTour != null) orderedTours.Add(shootoutTour);

        for (int i = 0; i < orderedTours.Count; i++)
        {
            orderedTours[i].OrderIndex = i;
        }
    }

    /// <summary>
    /// Assigns sequential display numbers to regular tours (1, 2, 3...).
    /// Warmup tour gets "0", shootout tour gets "П" as display numbers.
    /// </summary>
    private static void RenumberTours(Package package)
    {
        var orderedTours = package.Tours.OrderBy(t => t.OrderIndex).ToList();
        int mainTourNumber = 1;

        foreach (var tour in orderedTours)
        {
            switch (tour.Type)
            {
                case TourType.Warmup:
                    tour.Number = "0";
                    break;
                case TourType.Shootout:
                    tour.Number = "П";
                    break;
                default:
                    tour.Number = mainTourNumber.ToString(CultureInfo.InvariantCulture);
                    mainTourNumber++;
                    break;
            }
        }
    }

    /// <summary>
    /// Renumbers questions within the package based on the numbering mode.
    /// When a tour has blocks, questions are ordered by block OrderIndex first,
    /// then by question OrderIndex within each block.
    /// Warmup questions are always numbered independently (1..k).
    /// Shootout questions follow the package numbering mode.
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
            // Sort questions: if tour has blocks, sort by block order first, then by question order
            var orderedQuestions = GetOrderedQuestions(tour);

            // Normalize OrderIndex to be sequential (0, 1, 2...)
            for (int i = 0; i < orderedQuestions.Count; i++)
            {
                orderedQuestions[i].OrderIndex = i;
            }

            if (tour.Type == TourType.Warmup)
            {
                // Warmup questions: numbered 1..k within the warmup tour (always independent)
                for (int i = 0; i < orderedQuestions.Count; i++)
                {
                    orderedQuestions[i].Number = (i + 1).ToString(CultureInfo.InvariantCulture);
                }
            }
            else
            {
                // Regular and shootout tour questions follow the package numbering mode
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
    /// Gets questions for a tour ordered correctly.
    /// If the tour has blocks, orders by block OrderIndex first, then by question OrderIndex within each block.
    /// Questions without a block (orphans) come after all block questions.
    /// </summary>
    private static List<Question> GetOrderedQuestions(Tour tour)
    {
        if (tour.Blocks.Count == 0)
        {
            // No blocks - simple ordering by OrderIndex
            return tour.Questions.OrderBy(q => q.OrderIndex).ToList();
        }

        // Build a dictionary of block OrderIndex for quick lookup
        var blockOrderDict = tour.Blocks.ToDictionary(b => b.Id, b => b.OrderIndex);

        // Sort questions: first by block order (nulls last), then by question order within block
        return tour.Questions
            .OrderBy(q => q.BlockId.HasValue ? blockOrderDict.GetValueOrDefault(q.BlockId.Value, int.MaxValue) : int.MaxValue)
            .ThenBy(q => q.OrderIndex)
            .ToList();
    }

    /// <summary>
    /// Normalizes OrderIndex values for all questions in the package to be sequential within each tour.
    /// Respects block ordering when blocks are present.
    /// </summary>
    private static void NormalizeQuestionOrderIndices(Package package)
    {
        foreach (var tour in package.Tours)
        {
            var orderedQuestions = GetOrderedQuestions(tour);
            for (int i = 0; i < orderedQuestions.Count; i++)
            {
                orderedQuestions[i].OrderIndex = i;
            }
        }
    }

    /// <summary>
    /// Sets the type of a tour (Regular, Warmup, or Shootout).
    /// Ensures at most one warmup and one shootout tour per package.
    /// Warmup is moved to the first position, shootout to the last.
    /// </summary>
    /// <param name="package">The package containing the tour.</param>
    /// <param name="tourId">The tour ID to update.</param>
    /// <param name="newType">The new tour type to set.</param>
    public void SetTourType(Package package, int tourId, TourType newType)
    {
        var tour = package.Tours.FirstOrDefault(t => t.Id == tourId);
        if (tour == null)
            return;

        // Clear the same type from other tours (at most one warmup, one shootout)
        if (newType != TourType.Regular)
        {
            foreach (var t in package.Tours.Where(t => t.Type == newType && t.Id != tourId))
            {
                t.Type = TourType.Regular;
            }
        }

        tour.Type = newType;

        // Renumber everything (positions + numbers)
        RenumberPackageInMemory(package);
    }
}
