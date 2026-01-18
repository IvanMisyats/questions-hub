using System.Globalization;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure;
using QuestionsHub.UnitTests.TestInfrastructure;

using Xunit;

namespace QuestionsHub.UnitTests;

public class PackageManagementServiceTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory;
    private readonly PackageManagementService _service;

    public PackageManagementServiceTests()
    {
        _dbFactory = new InMemoryDbContextFactory();
        var renumberingService = new PackageRenumberingService(_dbFactory);
        _service = new PackageManagementService(_dbFactory, renumberingService);
    }

    public void Dispose()
    {
        using var context = _dbFactory.CreateDbContext();
        context.Database.EnsureDeleted();
    }

    #region Helper Methods

    private async Task<Package> CreatePackage(
        QuestionNumberingMode numberingMode = QuestionNumberingMode.Global,
        int tourCount = 0,
        int questionsPerTour = 0)
    {
        using var context = _dbFactory.CreateDbContext();

        var package = new Package
        {
            Title = "Test Package",
            NumberingMode = numberingMode,
            TotalQuestions = tourCount * questionsPerTour,
            Tours = []
        };

        for (int t = 0; t < tourCount; t++)
        {
            var tour = new Tour
            {
                Number = (t + 1).ToString(CultureInfo.InvariantCulture),
                OrderIndex = t,
                IsWarmup = false,
                Questions = [],
                Editors = [],
                Blocks = []
            };

            for (int q = 0; q < questionsPerTour; q++)
            {
                var globalIndex = t * questionsPerTour + q;
                tour.Questions.Add(new Question
                {
                    Number = (globalIndex + 1).ToString(CultureInfo.InvariantCulture),
                    OrderIndex = q,
                    Text = $"Question {globalIndex + 1}",
                    Answer = $"Answer {globalIndex + 1}"
                });
            }

            package.Tours.Add(tour);
        }

        context.Packages.Add(package);
        await context.SaveChangesAsync();

        return package;
    }

    private async Task<Package> GetPackageWithToursAndQuestions(int packageId)
    {
        using var context = _dbFactory.CreateDbContext();
        return (await context.Packages
            .Include(p => p.Tours)
            .ThenInclude(t => t.Questions)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == packageId))!;
    }

    #endregion

    #region Tour Reordering Tests

    [Fact]
    public async Task ReorderTours_ReverseOrder_UpdatesOrderIndices()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 3, questionsPerTour: 2);
        var originalTourIds = package.Tours.OrderBy(t => t.OrderIndex).Select(t => t.Id).ToArray();
        var reversedTourIds = originalTourIds.Reverse().ToArray();

        // Act
        var result = await _service.ReorderTours(package.Id, reversedTourIds);

        // Assert
        result.Success.Should().BeTrue();

        var reloaded = await GetPackageWithToursAndQuestions(package.Id);
        var reorderedTours = reloaded.Tours.OrderBy(t => t.OrderIndex).ToList();

        reorderedTours[0].Id.Should().Be(reversedTourIds[0]);
        reorderedTours[1].Id.Should().Be(reversedTourIds[1]);
        reorderedTours[2].Id.Should().Be(reversedTourIds[2]);
    }

    [Fact]
    public async Task ReorderTours_WithQuestions_RenumbersQuestionsCorrectly()
    {
        // Arrange - Package with 2 tours, 3 questions each (1-3, 4-6)
        var package = await CreatePackage(tourCount: 2, questionsPerTour: 3);
        var originalTourIds = package.Tours.OrderBy(t => t.OrderIndex).Select(t => t.Id).ToArray();
        var reversedTourIds = originalTourIds.Reverse().ToArray();

        // Act - Reverse tour order (second tour becomes first)
        var result = await _service.ReorderTours(package.Id, reversedTourIds);

        // Assert
        result.Success.Should().BeTrue();

        var reloaded = await GetPackageWithToursAndQuestions(package.Id);
        var firstTour = reloaded.Tours.OrderBy(t => t.OrderIndex).First();
        var secondTour = reloaded.Tours.OrderBy(t => t.OrderIndex).Last();

        // First tour (originally second) should now have questions 1-3
        firstTour.Questions.OrderBy(q => q.OrderIndex)
            .Select(q => q.Number)
            .Should().BeEquivalentTo(["1", "2", "3"]);

        // Second tour (originally first) should now have questions 4-6
        secondTour.Questions.OrderBy(q => q.OrderIndex)
            .Select(q => q.Number)
            .Should().BeEquivalentTo(["4", "5", "6"]);
    }

    #endregion

    #region Tour Creation Tests

    [Fact]
    public async Task CreateTour_EmptyPackage_CreatesFirstTour()
    {
        // Arrange
        var package = await CreatePackage();

        // Act
        var result = await _service.CreateTour(package.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.Entity.Should().NotBeNull();
        result.Entity!.Number.Should().Be("1");
        result.Entity.OrderIndex.Should().Be(0);
        result.Entity.IsWarmup.Should().BeFalse();
    }

    [Fact]
    public async Task CreateTour_ExistingTours_AppendsToEnd()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 2);

        // Act
        var result = await _service.CreateTour(package.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.Entity.Should().NotBeNull();
        result.Entity!.Number.Should().Be("3");
        result.Entity.OrderIndex.Should().Be(2);
    }

    [Fact]
    public async Task CreateTour_WithWarmupTour_CountsOnlyMainTours()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 2);
        var firstTourId = package.Tours.First(t => t.OrderIndex == 0).Id;
        await _service.SetWarmup(firstTourId, true);

        // Act
        var result = await _service.CreateTour(package.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.Entity!.Number.Should().Be("2"); // Only 1 main tour exists, so new is #2
    }

    #endregion

    #region Tour Deletion Tests

    [Fact]
    public async Task DeleteTour_WithQuestions_UpdatesPackageTotalQuestions()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 2, questionsPerTour: 5);
        var tourToDelete = package.Tours.First();

        // Act
        var result = await _service.DeleteTour(tourToDelete.Id);

        // Assert
        result.Success.Should().BeTrue();

        var reloaded = await GetPackageWithToursAndQuestions(package.Id);
        reloaded.Tours.Should().HaveCount(1);
        reloaded.TotalQuestions.Should().Be(5); // 10 - 5 = 5
    }

    [Fact]
    public async Task DeleteTour_RenumbersSurvivingTour()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 2, questionsPerTour: 3);
        var firstTour = package.Tours.First(t => t.OrderIndex == 0);

        // Act - Delete first tour
        var result = await _service.DeleteTour(firstTour.Id);

        // Assert
        result.Success.Should().BeTrue();

        var reloaded = await GetPackageWithToursAndQuestions(package.Id);
        var survivingTour = reloaded.Tours.Single();

        survivingTour.Number.Should().Be("1");
        survivingTour.Questions.OrderBy(q => q.OrderIndex)
            .Select(q => q.Number)
            .Should().BeEquivalentTo(["1", "2", "3"]);
    }

    #endregion

    #region Warmup Tour Tests

    [Fact]
    public async Task SetWarmup_True_MovesTourToFirstPosition()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 3);
        var lastTour = package.Tours.First(t => t.OrderIndex == 2);

        // Act
        var result = await _service.SetWarmup(lastTour.Id, true);

        // Assert
        result.Success.Should().BeTrue();

        var reloaded = await GetPackageWithToursAndQuestions(package.Id);
        var warmupTour = reloaded.Tours.First(t => t.IsWarmup);

        warmupTour.Id.Should().Be(lastTour.Id);
        warmupTour.OrderIndex.Should().Be(0);
        warmupTour.Number.Should().Be("0");
    }

    [Fact]
    public async Task SetWarmup_WithQuestions_WarmupQuestionsNumberedFrom1()
    {
        // Arrange - Package with 1 tour with 31 questions
        var package = await CreatePackage(tourCount: 1, questionsPerTour: 31);

        // Add a second tour and set it as warmup
        var createResult = await _service.CreateTour(package.Id);
        var newTourId = createResult.Entity!.Id;

        // Add a question to the new tour
        await _service.CreateQuestion(newTourId);

        // Act - Set new tour as warmup
        var result = await _service.SetWarmup(newTourId, true);

        // Assert
        result.Success.Should().BeTrue();

        var reloaded = await GetPackageWithToursAndQuestions(package.Id);
        var warmupTour = reloaded.Tours.First(t => t.IsWarmup);
        var mainTour = reloaded.Tours.First(t => !t.IsWarmup);

        // Warmup question should be numbered 1
        warmupTour.Questions.Single().Number.Should().Be("1");

        // Main tour questions should still be 1-31
        mainTour.Questions.OrderBy(q => q.OrderIndex)
            .Select(q => q.Number)
            .Should().BeEquivalentTo(
                Enumerable.Range(1, 31).Select(i => i.ToString(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task SetWarmup_False_UnsetsWarmupAndRenumbers()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 2, questionsPerTour: 2);
        var firstTour = package.Tours.First(t => t.OrderIndex == 0);
        await _service.SetWarmup(firstTour.Id, true);

        // Act
        var result = await _service.SetWarmup(firstTour.Id, false);

        // Assert
        result.Success.Should().BeTrue();

        var reloaded = await GetPackageWithToursAndQuestions(package.Id);
        reloaded.Tours.Should().NotContain(t => t.IsWarmup);

        // All tours should have number "1" and "2"
        reloaded.Tours.OrderBy(t => t.OrderIndex)
            .Select(t => t.Number)
            .Should().BeEquivalentTo(["1", "2"]);
    }

    [Fact]
    public async Task SetWarmup_OnDifferentTour_UnsetsExistingWarmup()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 3);
        var tour1 = package.Tours.First(t => t.OrderIndex == 0);
        var tour3 = package.Tours.First(t => t.OrderIndex == 2);

        await _service.SetWarmup(tour1.Id, true);

        // Act - Set tour3 as warmup
        var result = await _service.SetWarmup(tour3.Id, true);

        // Assert
        result.Success.Should().BeTrue();

        var reloaded = await GetPackageWithToursAndQuestions(package.Id);
        var warmupTours = reloaded.Tours.Where(t => t.IsWarmup).ToList();

        warmupTours.Should().HaveCount(1);
        warmupTours.Single().Id.Should().Be(tour3.Id);
    }

    #endregion

    #region Question Creation Tests

    [Fact]
    public async Task CreateQuestion_EmptyTour_CreatesQuestionWithNumber1()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 1);
        var tour = package.Tours.First();

        // Act
        var result = await _service.CreateQuestion(tour.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.Entity.Should().NotBeNull();
        result.Entity!.Number.Should().Be("1");
        result.Entity.OrderIndex.Should().Be(0);
    }

    [Fact]
    public async Task CreateQuestion_ExistingQuestions_AppendsWithCorrectNumber()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 1, questionsPerTour: 5);
        var tour = package.Tours.First();

        // Act
        var result = await _service.CreateQuestion(tour.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.Entity!.Number.Should().Be("6");
        result.Entity.OrderIndex.Should().Be(5);
    }

    [Fact]
    public async Task CreateQuestion_InWarmupTour_NumbersFrom1Independently()
    {
        // Arrange - Create package with main tour having 10 questions
        var package = await CreatePackage(tourCount: 1, questionsPerTour: 10);

        // Add warmup tour
        var warmupResult = await _service.CreateTour(package.Id);
        await _service.SetWarmup(warmupResult.Entity!.Id, true);

        // Act - Add question to warmup tour
        var result = await _service.CreateQuestion(warmupResult.Entity.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.Entity!.Number.Should().Be("1"); // Not 11!
    }

    [Fact]
    public async Task CreateQuestion_InSecondTour_GlobalNumbering()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 2, questionsPerTour: 5);
        var secondTour = package.Tours.First(t => t.OrderIndex == 1);

        // Act
        var result = await _service.CreateQuestion(secondTour.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.Entity!.Number.Should().Be("11"); // 10 existing + 1
    }

    [Fact]
    public async Task CreateQuestion_InBlock_SetsBlockId()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 1);
        var tour = package.Tours.First();

        using var context = _dbFactory.CreateDbContext();
        var block = new Block
        {
            TourId = tour.Id,
            Name = "Block 1",
            OrderIndex = 0
        };
        context.Blocks.Add(block);
        await context.SaveChangesAsync();

        // Act
        var result = await _service.CreateQuestion(tour.Id, block.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.Entity!.BlockId.Should().Be(block.Id);
    }

    [Fact]
    public async Task CreateQuestion_UpdatesPackageTotalQuestions()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 1, questionsPerTour: 5);
        var tour = package.Tours.First();

        // Act
        await _service.CreateQuestion(tour.Id);

        // Assert
        var reloaded = await GetPackageWithToursAndQuestions(package.Id);
        reloaded.TotalQuestions.Should().Be(6);
    }

    #endregion

    #region Question Deletion Tests

    [Fact]
    public async Task DeleteQuestion_UpdatesPackageTotalQuestions()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 1, questionsPerTour: 5);
        var question = package.Tours.First().Questions.First();

        // Act
        var result = await _service.DeleteQuestion(question.Id);

        // Assert
        result.Success.Should().BeTrue();

        var reloaded = await GetPackageWithToursAndQuestions(package.Id);
        reloaded.TotalQuestions.Should().Be(4);
    }

    [Fact]
    public async Task DeleteQuestion_RenumbersRemainingQuestions()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 1, questionsPerTour: 5);
        var firstQuestion = package.Tours.First().Questions.First(q => q.OrderIndex == 0);

        // Act
        var result = await _service.DeleteQuestion(firstQuestion.Id);

        // Assert
        result.Success.Should().BeTrue();

        var reloaded = await GetPackageWithToursAndQuestions(package.Id);
        var tour = reloaded.Tours.First();

        tour.Questions.Should().HaveCount(4);
        tour.Questions.OrderBy(q => q.OrderIndex)
            .Select(q => q.Number)
            .Should().BeEquivalentTo(["1", "2", "3", "4"]);
    }

    #endregion

    #region Question Moving Tests

    [Fact]
    public async Task MoveQuestion_WithinSameTour_ReordersCorrectly()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 1, questionsPerTour: 5);
        var tour = package.Tours.First();
        var questionToMove = tour.Questions.First(q => q.OrderIndex == 0);

        // Act - Move first question to position 3 (0-indexed)
        var result = await _service.MoveQuestion(
            questionToMove.Id, tour.Id, tour.Id, 3);

        // Assert
        result.Success.Should().BeTrue();

        var reloaded = await GetPackageWithToursAndQuestions(package.Id);
        var reorderedQuestions = reloaded.Tours.First().Questions.OrderBy(q => q.OrderIndex).ToList();

        // Original Q1 should now be at position 3
        reorderedQuestions[3].Id.Should().Be(questionToMove.Id);
    }

    [Fact]
    public async Task MoveQuestion_BetweenTours_UpdatesTourIdAndReorders()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 2, questionsPerTour: 3);
        var tour1 = package.Tours.First(t => t.OrderIndex == 0);
        var tour2 = package.Tours.First(t => t.OrderIndex == 1);
        var questionToMove = tour1.Questions.First(q => q.OrderIndex == 0);

        // Act - Move first question from tour1 to tour2 at position 0
        var result = await _service.MoveQuestion(
            questionToMove.Id, tour1.Id, tour2.Id, 0);

        // Assert
        result.Success.Should().BeTrue();

        var reloaded = await GetPackageWithToursAndQuestions(package.Id);
        var reloadedTour1 = reloaded.Tours.First(t => t.Id == tour1.Id);
        var reloadedTour2 = reloaded.Tours.First(t => t.Id == tour2.Id);

        reloadedTour1.Questions.Should().HaveCount(2);
        reloadedTour2.Questions.Should().HaveCount(4);
        reloadedTour2.Questions.Should().Contain(q => q.Id == questionToMove.Id);
    }

    [Fact]
    public async Task MoveQuestion_BetweenTours_RenumbersCorrectly()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 2, questionsPerTour: 3);
        var tour1 = package.Tours.First(t => t.OrderIndex == 0);
        var tour2 = package.Tours.First(t => t.OrderIndex == 1);
        var questionToMove = tour1.Questions.First(q => q.OrderIndex == 0);

        // Act - Move first question from tour1 to beginning of tour2
        var result = await _service.MoveQuestion(
            questionToMove.Id, tour1.Id, tour2.Id, 0);

        // Assert
        result.Success.Should().BeTrue();

        var reloaded = await GetPackageWithToursAndQuestions(package.Id);
        var reloadedTour1 = reloaded.Tours.First(t => t.Id == tour1.Id);
        var reloadedTour2 = reloaded.Tours.First(t => t.Id == tour2.Id);

        // Tour 1: now has 2 questions, numbered 1-2
        reloadedTour1.Questions.OrderBy(q => q.OrderIndex)
            .Select(q => q.Number)
            .Should().BeEquivalentTo(["1", "2"]);

        // Tour 2: now has 4 questions, numbered 3-6
        reloadedTour2.Questions.OrderBy(q => q.OrderIndex)
            .Select(q => q.Number)
            .Should().BeEquivalentTo(["3", "4", "5", "6"]);
    }

    [Fact]
    public async Task MoveQuestion_ToWarmupTour_NumbersIndependently()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 2, questionsPerTour: 3);
        var mainTour = package.Tours.First(t => t.OrderIndex == 0);
        var warmupTour = package.Tours.First(t => t.OrderIndex == 1);
        await _service.SetWarmup(warmupTour.Id, true);

        // Reload to get updated warmup tour
        var reloaded = await GetPackageWithToursAndQuestions(package.Id);
        mainTour = reloaded.Tours.First(t => !t.IsWarmup);
        warmupTour = reloaded.Tours.First(t => t.IsWarmup);

        var questionToMove = mainTour.Questions.First(q => q.OrderIndex == 0);

        // Act - Move question from main tour to warmup
        var result = await _service.MoveQuestion(
            questionToMove.Id, mainTour.Id, warmupTour.Id, 0);

        // Assert
        result.Success.Should().BeTrue();

        reloaded = await GetPackageWithToursAndQuestions(package.Id);
        var reloadedWarmup = reloaded.Tours.First(t => t.IsWarmup);
        var reloadedMain = reloaded.Tours.First(t => !t.IsWarmup);

        // Warmup now has 4 questions, numbered 1-4
        reloadedWarmup.Questions.OrderBy(q => q.OrderIndex)
            .Select(q => q.Number)
            .Should().BeEquivalentTo(["1", "2", "3", "4"]);

        // Main tour has 2 questions, numbered 1-2
        reloadedMain.Questions.OrderBy(q => q.OrderIndex)
            .Select(q => q.Number)
            .Should().BeEquivalentTo(["1", "2"]);
    }

    #endregion

    #region Question Reordering Tests

    [Fact]
    public async Task ReorderQuestions_WithinSingleTour_ReordersCorrectly()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 1, questionsPerTour: 3);
        var tour = package.Tours.First();
        var questions = tour.Questions.OrderBy(q => q.OrderIndex).ToList();

        // Reverse the order
        var newOrder = questions
            .Reverse<Question>()
            .Select(q => new PackageManagementService.QuestionOrderItem(q.Id, null))
            .ToList();

        // Act
        var result = await _service.ReorderQuestions(package.Id, tour.Id, newOrder);

        // Assert
        result.Success.Should().BeTrue();

        var reloaded = await GetPackageWithToursAndQuestions(package.Id);
        var reorderedQuestions = reloaded.Tours.First().Questions.OrderBy(q => q.OrderIndex).ToList();

        reorderedQuestions[0].Id.Should().Be(questions[2].Id);
        reorderedQuestions[1].Id.Should().Be(questions[1].Id);
        reorderedQuestions[2].Id.Should().Be(questions[0].Id);
    }

    [Fact]
    public async Task ReorderQuestions_MoveBetweenTours_TransfersCorrectly()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 2, questionsPerTour: 2);
        var tour1 = package.Tours.First(t => t.OrderIndex == 0);
        var tour2 = package.Tours.First(t => t.OrderIndex == 1);

        var q1 = tour1.Questions.First(q => q.OrderIndex == 0);
        var q2 = tour1.Questions.First(q => q.OrderIndex == 1);
        var q3 = tour2.Questions.First(q => q.OrderIndex == 0);
        var q4 = tour2.Questions.First(q => q.OrderIndex == 1);

        // Move q1 to tour2, keeping q3 and q4 in tour2
        var tour2Order = new List<PackageManagementService.QuestionOrderItem>
        {
            new(q1.Id, null),
            new(q3.Id, null),
            new(q4.Id, null)
        };

        var tour1Order = new List<PackageManagementService.QuestionOrderItem>
        {
            new(q2.Id, null)
        };

        // Act
        var result = await _service.ReorderQuestions(
            package.Id, tour2.Id, tour2Order, tour1.Id, tour1Order);

        // Assert
        result.Success.Should().BeTrue();

        var reloaded = await GetPackageWithToursAndQuestions(package.Id);
        var reloadedTour1 = reloaded.Tours.First(t => t.Id == tour1.Id);
        var reloadedTour2 = reloaded.Tours.First(t => t.Id == tour2.Id);

        reloadedTour1.Questions.Should().HaveCount(1);
        reloadedTour2.Questions.Should().HaveCount(3);
        reloadedTour2.Questions.Should().Contain(q => q.Id == q1.Id);
    }

    #endregion

    #region Block Reordering Tests

    [Fact]
    public async Task ReorderBlocks_ReverseOrder_UpdatesOrderIndices()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 1);
        var tour = package.Tours.First();

        using var context = _dbFactory.CreateDbContext();
        var block1 = new Block { TourId = tour.Id, Name = "Block 1", OrderIndex = 0 };
        var block2 = new Block { TourId = tour.Id, Name = "Block 2", OrderIndex = 1 };
        var block3 = new Block { TourId = tour.Id, Name = "Block 3", OrderIndex = 2 };
        context.Blocks.AddRange(block1, block2, block3);
        await context.SaveChangesAsync();

        var reversedBlockIds = new[] { block3.Id, block2.Id, block1.Id };

        // Act
        var result = await _service.ReorderBlocks(tour.Id, reversedBlockIds);

        // Assert
        result.Success.Should().BeTrue();

        using var verifyContext = _dbFactory.CreateDbContext();
        var reorderedBlocks = await verifyContext.Blocks
            .Where(b => b.TourId == tour.Id)
            .OrderBy(b => b.OrderIndex)
            .ToListAsync();

        reorderedBlocks[0].Id.Should().Be(block3.Id);
        reorderedBlocks[1].Id.Should().Be(block2.Id);
        reorderedBlocks[2].Id.Should().Be(block1.Id);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ReorderTours_InvalidPackageId_ReturnsSuccess()
    {
        // Note: With in-memory database, non-existent package just means no tours found
        // Act
        var result = await _service.ReorderTours(99999, [1, 2, 3]);

        // Assert - Still succeeds, just no tours updated
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CreateQuestion_InvalidTourId_ReturnsFail()
    {
        // Act
        var result = await _service.CreateQuestion(99999);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Tour not found");
    }

    [Fact]
    public async Task DeleteQuestion_InvalidQuestionId_ReturnsFail()
    {
        // Act
        var result = await _service.DeleteQuestion(99999);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Question not found");
    }

    [Fact]
    public async Task MoveQuestion_InvalidQuestionId_ReturnsFail()
    {
        // Arrange
        var package = await CreatePackage(tourCount: 1);
        var tour = package.Tours.First();

        // Act
        var result = await _service.MoveQuestion(99999, tour.Id, tour.Id, 0);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Question not found");
    }

    [Fact]
    public async Task SetWarmup_InvalidTourId_ReturnsFail()
    {
        // Act
        var result = await _service.SetWarmup(99999, true);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Tour not found");
    }

    #endregion

    #region Regression Tests for Original Defect

    [Fact]
    public async Task Scenario_ImportPackageThenAddWarmupTour_QuestionsNumberedCorrectly()
    {
        // This is the exact scenario from the original bug report:
        // 1. Import package with 1 tour containing 31 questions
        // 2. Add new tour, drag to first position
        // 3. Add question and mark as warmup
        // Expected: warmup question = 1, main tour questions = 1-31

        // Arrange - Step 1: Create package simulating import
        var package = await CreatePackage(tourCount: 1, questionsPerTour: 31);

        // Step 2: Add new tour
        var newTourResult = await _service.CreateTour(package.Id);
        var newTourId = newTourResult.Entity!.Id;

        // Step 3: Drag to first position (reorder)
        var reloaded = await GetPackageWithToursAndQuestions(package.Id);
        var newOrder = new[] { newTourId }.Concat(
            reloaded.Tours.Where(t => t.Id != newTourId).Select(t => t.Id)).ToArray();
        await _service.ReorderTours(package.Id, newOrder);

        // Step 4: Add question to new tour
        var questionResult = await _service.CreateQuestion(newTourId);

        // Step 5: Mark as warmup
        await _service.SetWarmup(newTourId, true);

        // Assert
        var final = await GetPackageWithToursAndQuestions(package.Id);
        var warmupTour = final.Tours.First(t => t.IsWarmup);
        var mainTour = final.Tours.First(t => !t.IsWarmup);

        // Warmup question should be 1
        warmupTour.Questions.Single().Number.Should().Be("1");

        // Main tour questions should be 1-31
        mainTour.Questions.OrderBy(q => q.OrderIndex)
            .Select(q => q.Number)
            .Should().BeEquivalentTo(
                Enumerable.Range(1, 31).Select(i => i.ToString(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task Scenario_AddTourWithQuestionsThenDragFirst_QuestionsRenumbered()
    {
        // Scenario:
        // 1. Package has 1 tour with 12 questions (1-12)
        // 2. Add new tour, add 3 questions
        // 3. Drag new tour to first position
        // Expected: new tour questions = 1-3, original tour = 4-15

        // Arrange - Step 1
        var package = await CreatePackage(tourCount: 1, questionsPerTour: 12);

        // Step 2: Add new tour with 3 questions
        var newTourResult = await _service.CreateTour(package.Id);
        var newTourId = newTourResult.Entity!.Id;
        await _service.CreateQuestion(newTourId);
        await _service.CreateQuestion(newTourId);
        await _service.CreateQuestion(newTourId);

        // Step 3: Drag to first position
        var reloaded = await GetPackageWithToursAndQuestions(package.Id);
        var originalTourId = reloaded.Tours.First(t => t.Id != newTourId).Id;
        var newOrder = new[] { newTourId, originalTourId };
        await _service.ReorderTours(package.Id, newOrder);

        // Assert
        var final = await GetPackageWithToursAndQuestions(package.Id);
        var firstTour = final.Tours.OrderBy(t => t.OrderIndex).First();
        var secondTour = final.Tours.OrderBy(t => t.OrderIndex).Last();

        // First tour (new) should have questions 1-3
        firstTour.Questions.OrderBy(q => q.OrderIndex)
            .Select(q => q.Number)
            .Should().BeEquivalentTo(["1", "2", "3"]);

        // Second tour (original) should have questions 4-15
        secondTour.Questions.OrderBy(q => q.OrderIndex)
            .Select(q => q.Number)
            .Should().BeEquivalentTo(
                Enumerable.Range(4, 12).Select(i => i.ToString(CultureInfo.InvariantCulture)));
    }

    #endregion
}
