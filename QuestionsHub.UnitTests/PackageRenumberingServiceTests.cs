using System.Globalization;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure;
using Xunit;

namespace QuestionsHub.UnitTests;

public class PackageRenumberingServiceTests
{
    private static PackageRenumberingService CreateService()
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

    private static Tour CreateTour(int id, int orderIndex, TourType type = TourType.Regular)
    {
        return new Tour
        {
            Id = id,
            OrderIndex = orderIndex,
            Number = (orderIndex + 1).ToString(CultureInfo.InvariantCulture),
            Type = type,
            Questions = []
        };
    }

    private static Question CreateQuestion(int id, int orderIndex, string number = "")
    {
        return new Question
        {
            Id = id,
            OrderIndex = orderIndex,
            Number = string.IsNullOrEmpty(number) ? (orderIndex + 1).ToString(CultureInfo.InvariantCulture) : number,
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

        var warmupTour = CreateTour(1, 0, type: TourType.Warmup);
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

        var warmupTour = CreateTour(1, 0, type: TourType.Warmup);
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

        var warmupTour = CreateTour(2, 1, type: TourType.Warmup); // Warmup at index 1
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
        service.SetTourType(package, 2, TourType.Warmup);

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

        var tour1 = CreateTour(1, 0, type: TourType.Warmup);
        var tour2 = CreateTour(2, 1);
        var tour3 = CreateTour(3, 2);

        package.Tours.Add(tour1);
        package.Tours.Add(tour2);
        package.Tours.Add(tour3);

        // Act - Set tour3 as warmup (should unset tour1)
        service.SetTourType(package, 3, TourType.Warmup);

        // Assert - Only tour3 should be warmup
        Assert.False(tour1.IsWarmup);
        Assert.False(tour2.IsWarmup);
        Assert.True(tour3.IsWarmup);
    }

    #endregion

    #region Warmup Tour With Existing Questions Tests

    [Fact]
    public void RenumberPackageInMemory_GlobalWithWarmupFirst_MainQuestionsStartAt1()
    {
        // This test reproduces a defect where:
        // 1. Package has 1 tour with 31 questions (numbered 1-31)
        // 2. User adds new tour at first position with 1 question
        // 3. User marks this tour as warmup
        // 4. Expected: warmup question = 1, main tour questions = 1-31
        // Defect: the warmup question was getting number 32 instead of 1

        // Arrange
        var service = CreateService();
        var package = CreatePackage(QuestionNumberingMode.Global);

        // Simulate existing main tour with 31 questions
        var mainTour = CreateTour(1, 1);
        for (int i = 0; i < 31; i++)
        {
            mainTour.Questions.Add(CreateQuestion(i + 1, i, (i + 1).ToString(CultureInfo.InvariantCulture)));
        }

        // Simulate new warmup tour at first position with 1 question
        // The question was initially assigned number 32 (last question + 1)
        var warmupTour = CreateTour(2, 0, type: TourType.Warmup);
        warmupTour.Questions.Add(CreateQuestion(32, 0, "32"));

        package.Tours.Add(mainTour);
        package.Tours.Add(warmupTour);

        // Act
        service.RenumberPackageInMemory(package);

        // Assert
        // Warmup tour should have number "0"
        Assert.Equal("0", warmupTour.Number);
        Assert.Equal("1", mainTour.Number);

        // Warmup question should be renumbered to 1
        Assert.Equal("1", warmupTour.Questions.First().Number);

        // Main tour questions should be 1-31
        var mainQuestions = mainTour.Questions.OrderBy(q => q.OrderIndex).ToList();
        for (int i = 0; i < 31; i++)
        {
            Assert.Equal((i + 1).ToString(CultureInfo.InvariantCulture), mainQuestions[i].Number);
        }
    }

    [Fact]
    public void RenumberPackageInMemory_NewTourDraggedToFirst_RenumbersAllQuestionsFrom1()
    {
        // This test reproduces a defect where:
        // 1. Package has 1 tour with 12 questions (numbered 1-12)
        // 2. User adds new tour, adds 3 questions (numbered 13, 14, 15 initially)
        // 3. User drags this new tour to first position
        // 4. Expected: new tour questions = 1-3, original tour questions = 4-15

        // Arrange
        var service = CreateService();
        var package = CreatePackage(QuestionNumberingMode.Global);

        // New tour (now at first position) with 3 questions
        var newTour = CreateTour(2, 0);
        newTour.Questions.Add(CreateQuestion(13, 0, "13"));
        newTour.Questions.Add(CreateQuestion(14, 1, "14"));
        newTour.Questions.Add(CreateQuestion(15, 2, "15"));

        // Original tour (now at second position) with 12 questions
        var originalTour = CreateTour(1, 1);
        for (int i = 0; i < 12; i++)
        {
            originalTour.Questions.Add(CreateQuestion(i + 1, i, (i + 1).ToString(CultureInfo.InvariantCulture)));
        }

        package.Tours.Add(newTour);
        package.Tours.Add(originalTour);

        // Act
        service.RenumberPackageInMemory(package);

        // Assert
        Assert.Equal("1", newTour.Number);
        Assert.Equal("2", originalTour.Number);

        // New tour questions should be 1-3
        var newTourQuestions = newTour.Questions.OrderBy(q => q.OrderIndex).ToList();
        Assert.Equal("1", newTourQuestions[0].Number);
        Assert.Equal("2", newTourQuestions[1].Number);
        Assert.Equal("3", newTourQuestions[2].Number);

        // Original tour questions should be 4-15
        var originalTourQuestions = originalTour.Questions.OrderBy(q => q.OrderIndex).ToList();
        for (int i = 0; i < 12; i++)
        {
            Assert.Equal((i + 4).ToString(CultureInfo.InvariantCulture), originalTourQuestions[i].Number);
        }
    }

    [Fact]
    public void RenumberPackageInMemory_AddQuestionToWarmupTour_NumbersStartAt1()
    {
        // When adding questions to warmup tour, they should be numbered
        // independently starting from 1, not continue from main tour questions

        // Arrange
        var service = CreateService();
        var package = CreatePackage(QuestionNumberingMode.Global);

        // Warmup tour with questions (initially numbered incorrectly as 32, 33)
        var warmupTour = CreateTour(1, 0, type: TourType.Warmup);
        warmupTour.Questions.Add(CreateQuestion(32, 0, "32"));
        warmupTour.Questions.Add(CreateQuestion(33, 1, "33"));

        // Main tour with 31 questions
        var mainTour = CreateTour(2, 1);
        for (int i = 0; i < 31; i++)
        {
            mainTour.Questions.Add(CreateQuestion(i + 1, i, (i + 1).ToString(CultureInfo.InvariantCulture)));
        }

        package.Tours.Add(warmupTour);
        package.Tours.Add(mainTour);

        // Act
        service.RenumberPackageInMemory(package);

        // Assert
        var warmupQuestions = warmupTour.Questions.OrderBy(q => q.OrderIndex).ToList();
        Assert.Equal("1", warmupQuestions[0].Number);
        Assert.Equal("2", warmupQuestions[1].Number);

        // Main tour questions should still be 1-31
        var mainQuestions = mainTour.Questions.OrderBy(q => q.OrderIndex).ToList();
        for (int i = 0; i < 31; i++)
        {
            Assert.Equal((i + 1).ToString(CultureInfo.InvariantCulture), mainQuestions[i].Number);
        }
    }

    [Fact]
    public void RenumberPackageInMemory_MultipleToursWithWarmup_CorrectGlobalNumbering()
    {
        // Test with warmup + multiple main tours to ensure global numbering works correctly

        // Arrange
        var service = CreateService();
        var package = CreatePackage(QuestionNumberingMode.Global);

        // Warmup tour with 2 questions
        var warmupTour = CreateTour(1, 0, type: TourType.Warmup);
        warmupTour.Questions.Add(CreateQuestion(100, 0, "100"));
        warmupTour.Questions.Add(CreateQuestion(101, 1, "101"));

        // Main tour 1 with 5 questions
        var mainTour1 = CreateTour(2, 1);
        for (int i = 0; i < 5; i++)
        {
            mainTour1.Questions.Add(CreateQuestion(i + 1, i));
        }

        // Main tour 2 with 3 questions
        var mainTour2 = CreateTour(3, 2);
        for (int i = 0; i < 3; i++)
        {
            mainTour2.Questions.Add(CreateQuestion(i + 6, i));
        }

        package.Tours.Add(warmupTour);
        package.Tours.Add(mainTour1);
        package.Tours.Add(mainTour2);

        // Act
        service.RenumberPackageInMemory(package);

        // Assert
        // Warmup questions: 1, 2
        var warmupQuestions = warmupTour.Questions.OrderBy(q => q.OrderIndex).ToList();
        Assert.Equal("1", warmupQuestions[0].Number);
        Assert.Equal("2", warmupQuestions[1].Number);

        // Main tour 1 questions: 1-5 (global numbering starts at 1)
        var main1Questions = mainTour1.Questions.OrderBy(q => q.OrderIndex).ToList();
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal((i + 1).ToString(CultureInfo.InvariantCulture), main1Questions[i].Number);
        }

        // Main tour 2 questions: 6-8 (continues global numbering)
        var main2Questions = mainTour2.Questions.OrderBy(q => q.OrderIndex).ToList();
        Assert.Equal("6", main2Questions[0].Number);
        Assert.Equal("7", main2Questions[1].Number);
        Assert.Equal("8", main2Questions[2].Number);
    }

    #endregion
}
