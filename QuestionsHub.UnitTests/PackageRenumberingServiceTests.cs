using FluentAssertions;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure;
using Xunit;

namespace QuestionsHub.UnitTests;

public class PackageRenumberingServiceTests
{
    private PackageRenumberingService CreateService()
    {
        // The service can work with in-memory packages using RenumberPackageInMemory
        return new PackageRenumberingService(null!);
    }

    #region Helper Methods

    private static Package CreatePackage(QuestionNumberingMode numberingMode = QuestionNumberingMode.Global)
    {
        return new Package
        {
            Id = 1,
            Title = "Test Package",
            NumberingMode = numberingMode,
            Tours = []
        };
    }

    private static Tour CreateTour(int id, int orderIndex, bool isWarmup = false)
    {
        return new Tour
        {
            Id = id,
            OrderIndex = orderIndex,
            Number = (orderIndex + 1).ToString(),
            IsWarmup = isWarmup,
            Questions = []
        };
    }

    private static Question CreateQuestion(int id, int orderIndex, string number = "")
    {
        return new Question
        {
            Id = id,
            OrderIndex = orderIndex,
            Number = string.IsNullOrEmpty(number) ? (orderIndex + 1).ToString() : number,
            Text = $"Question {id}",
            Answer = $"Answer {id}"
        };
    }

    #endregion

    #region Global Numbering Tests

    [Fact]
    public void RenumberPackageInMemory_GlobalMode_NumbersQuestionsSequentially()
    {
        // Arrange
        var service = CreateService();
        var package = CreatePackage(QuestionNumberingMode.Global);

        var tour1 = CreateTour(1, 0);
        tour1.Questions.Add(CreateQuestion(1, 0));
        tour1.Questions.Add(CreateQuestion(2, 1));
        tour1.Questions.Add(CreateQuestion(3, 2));

        var tour2 = CreateTour(2, 1);
        tour2.Questions.Add(CreateQuestion(4, 0));
        tour2.Questions.Add(CreateQuestion(5, 1));

        package.Tours.Add(tour1);
        package.Tours.Add(tour2);

        // Act
        service.RenumberPackageInMemory(package);

        // Assert
        Assert.Equal("1", tour1.Number);
        Assert.Equal("2", tour2.Number);

        Assert.Equal("1", tour1.Questions.First(q => q.Id == 1).Number);
        Assert.Equal("2", tour1.Questions.First(q => q.Id == 2).Number);
        Assert.Equal("3", tour1.Questions.First(q => q.Id == 3).Number);
        Assert.Equal("4", tour2.Questions.First(q => q.Id == 4).Number);
        Assert.Equal("5", tour2.Questions.First(q => q.Id == 5).Number);
    }

    [Fact]
    public void RenumberPackageInMemory_GlobalWithWarmup_StartsMainTourQuestionsAt1()
    {
        // Arrange
        var service = CreateService();
        var package = CreatePackage(QuestionNumberingMode.Global);

        var warmupTour = CreateTour(1, 0, isWarmup: true);
        warmupTour.Questions.Add(CreateQuestion(1, 0));
        warmupTour.Questions.Add(CreateQuestion(2, 1));

        var mainTour = CreateTour(2, 1);
        mainTour.Questions.Add(CreateQuestion(3, 0));
        mainTour.Questions.Add(CreateQuestion(4, 1));

        package.Tours.Add(warmupTour);
        package.Tours.Add(mainTour);

        // Act
        service.RenumberPackageInMemory(package);

        // Assert
        Assert.Equal("0", warmupTour.Number);
        Assert.Equal("1", mainTour.Number);

        // Warmup questions numbered 1..k
        Assert.Equal("1", warmupTour.Questions.First(q => q.Id == 1).Number);
        Assert.Equal("2", warmupTour.Questions.First(q => q.Id == 2).Number);

        // Main tour questions start at 1 (global, but warmup doesn't count)
        Assert.Equal("1", mainTour.Questions.First(q => q.Id == 3).Number);
        Assert.Equal("2", mainTour.Questions.First(q => q.Id == 4).Number);
    }

    #endregion

    #region PerTour Numbering Tests

    [Fact]
    public void RenumberPackageInMemory_PerTourMode_RestartsNumberingPerTour()
    {
        // Arrange
        var service = CreateService();
        var package = CreatePackage(QuestionNumberingMode.PerTour);

        var tour1 = CreateTour(1, 0);
        tour1.Questions.Add(CreateQuestion(1, 0));
        tour1.Questions.Add(CreateQuestion(2, 1));
        tour1.Questions.Add(CreateQuestion(3, 2));

        var tour2 = CreateTour(2, 1);
        tour2.Questions.Add(CreateQuestion(4, 0));
        tour2.Questions.Add(CreateQuestion(5, 1));

        package.Tours.Add(tour1);
        package.Tours.Add(tour2);

        // Act
        service.RenumberPackageInMemory(package);

        // Assert
        Assert.Equal("1", tour1.Questions.First(q => q.Id == 1).Number);
        Assert.Equal("2", tour1.Questions.First(q => q.Id == 2).Number);
        Assert.Equal("3", tour1.Questions.First(q => q.Id == 3).Number);
        // PerTour: restart at 1 for second tour
        Assert.Equal("1", tour2.Questions.First(q => q.Id == 4).Number);
        Assert.Equal("2", tour2.Questions.First(q => q.Id == 5).Number);
    }

    [Fact]
    public void RenumberPackageInMemory_PerTourWithWarmup_WarmupAndMainToursBothStartAt1()
    {
        // Arrange
        var service = CreateService();
        var package = CreatePackage(QuestionNumberingMode.PerTour);

        var warmupTour = CreateTour(1, 0, isWarmup: true);
        warmupTour.Questions.Add(CreateQuestion(1, 0));
        warmupTour.Questions.Add(CreateQuestion(2, 1));

        var mainTour = CreateTour(2, 1);
        mainTour.Questions.Add(CreateQuestion(3, 0));
        mainTour.Questions.Add(CreateQuestion(4, 1));

        package.Tours.Add(warmupTour);
        package.Tours.Add(mainTour);

        // Act
        service.RenumberPackageInMemory(package);

        // Assert
        Assert.Equal("1", warmupTour.Questions.First(q => q.Id == 1).Number);
        Assert.Equal("2", warmupTour.Questions.First(q => q.Id == 2).Number);
        Assert.Equal("1", mainTour.Questions.First(q => q.Id == 3).Number);
        Assert.Equal("2", mainTour.Questions.First(q => q.Id == 4).Number);
    }

    #endregion

    #region Manual Mode Tests

    [Fact]
    public void RenumberPackageInMemory_ManualMode_DoesNotChangeQuestionNumbers()
    {
        // Arrange
        var service = CreateService();
        var package = CreatePackage(QuestionNumberingMode.Manual);

        var tour = CreateTour(1, 0);
        tour.Questions.Add(CreateQuestion(1, 0, "A"));
        tour.Questions.Add(CreateQuestion(2, 1, "B"));
        tour.Questions.Add(CreateQuestion(3, 2, "F"));

        package.Tours.Add(tour);

        // Act
        service.RenumberPackageInMemory(package);

        // Assert - Question numbers should be unchanged
        Assert.Equal("A", tour.Questions.First(q => q.Id == 1).Number);
        Assert.Equal("B", tour.Questions.First(q => q.Id == 2).Number);
        Assert.Equal("F", tour.Questions.First(q => q.Id == 3).Number);

        // But tour number should still be updated
        Assert.Equal("1", tour.Number);
    }

    [Fact]
    public void RenumberPackageInMemory_ManualMode_StillNormalizesOrderIndices()
    {
        // Arrange
        var service = CreateService();
        var package = CreatePackage(QuestionNumberingMode.Manual);

        var tour = CreateTour(1, 0);
        tour.Questions.Add(CreateQuestion(1, 5, "X")); // Gaps in OrderIndex
        tour.Questions.Add(CreateQuestion(2, 10, "Y"));
        tour.Questions.Add(CreateQuestion(3, 15, "Z"));

        package.Tours.Add(tour);

        // Act
        service.RenumberPackageInMemory(package);

        // Assert - OrderIndex should be normalized to 0, 1, 2
        var orderedQuestions = tour.Questions.OrderBy(q => q.OrderIndex).ToList();
        Assert.Equal(0, orderedQuestions[0].OrderIndex);
        Assert.Equal(1, orderedQuestions[1].OrderIndex);
        Assert.Equal(2, orderedQuestions[2].OrderIndex);
    }

    #endregion

    #region Warmup Tour Positioning Tests

    [Fact]
    public void RenumberPackageInMemory_WarmupNotFirst_MovesWarmupToFront()
    {
        // Arrange
        var service = CreateService();
        var package = CreatePackage(QuestionNumberingMode.Global);

        var mainTour1 = CreateTour(1, 0);
        mainTour1.Questions.Add(CreateQuestion(1, 0));

        var warmupTour = CreateTour(2, 1, isWarmup: true); // Warmup at index 1
        warmupTour.Questions.Add(CreateQuestion(2, 0));

        var mainTour2 = CreateTour(3, 2);
        mainTour2.Questions.Add(CreateQuestion(3, 0));

        package.Tours.Add(mainTour1);
        package.Tours.Add(warmupTour);
        package.Tours.Add(mainTour2);

        // Act
        service.RenumberPackageInMemory(package);

        // Assert - Warmup should now be at OrderIndex 0
        Assert.Equal(0, warmupTour.OrderIndex);
        Assert.True(mainTour1.OrderIndex > 0);
        Assert.True(mainTour2.OrderIndex > 0);
    }

    [Fact]
    public void SetWarmupTour_WhenSet_MakesTourWarmupAndMovesToFront()
    {
        // Arrange
        var service = CreateService();
        var package = CreatePackage(QuestionNumberingMode.Global);

        var mainTour1 = CreateTour(1, 0);
        mainTour1.Questions.Add(CreateQuestion(1, 0));

        var mainTour2 = CreateTour(2, 1);
        mainTour2.Questions.Add(CreateQuestion(2, 0));

        package.Tours.Add(mainTour1);
        package.Tours.Add(mainTour2);

        // Act - Set mainTour2 as warmup
        service.SetWarmupTour(package, 2, true);

        // Assert
        Assert.False(mainTour1.IsWarmup);
        Assert.True(mainTour2.IsWarmup);
        Assert.Equal(0, mainTour2.OrderIndex);
        Assert.Equal(1, mainTour1.OrderIndex);
    }

    [Fact]
    public void SetWarmupTour_OnlyOneWarmupAllowed()
    {
        // Arrange
        var service = CreateService();
        var package = CreatePackage(QuestionNumberingMode.Global);

        var tour1 = CreateTour(1, 0, isWarmup: true);
        var tour2 = CreateTour(2, 1);
        var tour3 = CreateTour(3, 2);

        package.Tours.Add(tour1);
        package.Tours.Add(tour2);
        package.Tours.Add(tour3);

        // Act - Set tour3 as warmup (should unset tour1)
        service.SetWarmupTour(package, 3, true);

        // Assert - Only tour3 should be warmup
        Assert.False(tour1.IsWarmup);
        Assert.False(tour2.IsWarmup);
        Assert.True(tour3.IsWarmup);
    }

    #endregion
}
