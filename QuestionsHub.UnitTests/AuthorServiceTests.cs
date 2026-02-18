using System.Globalization;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure;
using QuestionsHub.UnitTests.TestInfrastructure;

using Xunit;

namespace QuestionsHub.UnitTests;

/// <summary>
/// Tests for AuthorService, focusing on author statistics calculation.
/// These tests verify that question counts and package counts are calculated correctly
/// based on different author roles (question author vs editor).
/// </summary>
public class AuthorServiceTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory;
    private readonly AuthorService _service;

    public AuthorServiceTests()
    {
        _dbFactory = new InMemoryDbContextFactory();
        _service = new AuthorService(_dbFactory);
    }

    public void Dispose()
    {
        using var context = _dbFactory.CreateDbContext();
        context.Database.EnsureDeleted();
    }

    #region Helper Methods

    /// <summary>
    /// Creates a package with the specified number of tours and questions.
    /// </summary>
    private async Task<Package> CreatePackage(
        string title = "Test Package",
        PackageStatus status = PackageStatus.Published,
        int tourCount = 1,
        int questionsPerTour = 3,
        Author? tourEditor = null,
        Author? questionAuthor = null)
    {
        using var context = _dbFactory.CreateDbContext();

        var package = new Package
        {
            Title = title,
            Status = status,
            AccessLevel = PackageAccessLevel.All,
            NumberingMode = QuestionNumberingMode.Global,
            TotalQuestions = tourCount * questionsPerTour,
            Tours = []
        };

        for (int t = 0; t < tourCount; t++)
        {
            var tour = new Tour
            {
                Number = (t + 1).ToString(CultureInfo.InvariantCulture),
                OrderIndex = t,
                Questions = [],
                Editors = [],
                Blocks = []
            };

            // Add tour editor if specified
            if (tourEditor != null)
            {
                // Attach or add the editor
                var existingEditor = await context.Authors.FindAsync(tourEditor.Id);
                if (existingEditor != null)
                {
                    tour.Editors.Add(existingEditor);
                }
                else
                {
                    context.Authors.Add(tourEditor);
                    tour.Editors.Add(tourEditor);
                }
            }

            for (int q = 0; q < questionsPerTour; q++)
            {
                var globalIndex = t * questionsPerTour + q;
                var question = new Question
                {
                    Number = (globalIndex + 1).ToString(CultureInfo.InvariantCulture),
                    OrderIndex = q,
                    Text = $"Question {globalIndex + 1}",
                    Answer = $"Answer {globalIndex + 1}",
                    Authors = []
                };

                // Add question author if specified
                if (questionAuthor != null)
                {
                    var existingAuthor = await context.Authors.FindAsync(questionAuthor.Id);
                    if (existingAuthor != null)
                    {
                        question.Authors.Add(existingAuthor);
                    }
                    else
                    {
                        context.Authors.Add(questionAuthor);
                        question.Authors.Add(questionAuthor);
                    }
                }

                tour.Questions.Add(question);
            }

            package.Tours.Add(tour);
        }

        context.Packages.Add(package);
        await context.SaveChangesAsync();

        return package;
    }

    /// <summary>
    /// Creates an author and saves it to the database.
    /// </summary>
    private async Task<Author> CreateAuthor(string firstName, string lastName)
    {
        using var context = _dbFactory.CreateDbContext();

        var author = new Author
        {
            FirstName = firstName,
            LastName = lastName
        };

        context.Authors.Add(author);
        await context.SaveChangesAsync();

        return author;
    }

    /// <summary>
    /// Creates a PackageAccessContext for anonymous user (can access all public packages).
    /// </summary>
    private static PackageAccessContext CreateAnonymousAccessContext()
    {
        return new PackageAccessContext(
            IsAdmin: false,
            IsEditor: false,
            HasVerifiedEmail: false,
            UserId: null);
    }

    /// <summary>
    /// Creates a PackageAccessContext for admin user (can access all packages).
    /// </summary>
    private static PackageAccessContext CreateAdminAccessContext()
    {
        return new PackageAccessContext(
            IsAdmin: true,
            IsEditor: false,
            HasVerifiedEmail: true,
            UserId: "admin-user-id");
    }

    #endregion

    #region GetAuthorStatistics Tests

    [Fact]
    public async Task GetAuthorStatistics_AuthorWithQuestionsButNotEditor_ReturnsCorrectQuestionCount()
    {
        // Arrange
        // Create an author who will be a question author but NOT an editor
        var questionAuthor = await CreateAuthor("Іван", "Петренко");

        // Create a different author who will be the editor
        var editor = await CreateAuthor("Марія", "Коваленко");

        // Create a published package where:
        // - editor is the tour editor
        // - questionAuthor is the author of all questions
        await CreatePackage(
            title: "Package 1",
            tourCount: 1,
            questionsPerTour: 5,
            tourEditor: editor,
            questionAuthor: questionAuthor);

        var accessContext = CreateAnonymousAccessContext();

        // Act
        var stats = await _service.GetAuthorStatistics(questionAuthor.Id, accessContext);

        // Assert
        stats.Should().NotBeNull();
        stats!.QuestionCount.Should().Be(5, "author should have 5 questions as question author");
        stats.PackageCount.Should().Be(0, "author is not an editor of any package");
    }

    [Fact]
    public async Task GetAuthorStatistics_AuthorIsEditorButNotQuestionAuthor_ReturnsCorrectPackageCount()
    {
        // Arrange
        // Create an author who will be an editor but NOT a question author
        var editor = await CreateAuthor("Олена", "Шевченко");

        // Create a different author who will be the question author
        var questionAuthor = await CreateAuthor("Петро", "Бондаренко");

        // Create a published package where:
        // - editor is the tour editor
        // - questionAuthor is the author of all questions
        await CreatePackage(
            title: "Package 1",
            tourCount: 2,
            questionsPerTour: 3,
            tourEditor: editor,
            questionAuthor: questionAuthor);

        var accessContext = CreateAnonymousAccessContext();

        // Act
        var stats = await _service.GetAuthorStatistics(editor.Id, accessContext);

        // Assert
        stats.Should().NotBeNull();
        stats!.QuestionCount.Should().Be(0, "author is not a question author");
        stats.PackageCount.Should().Be(1, "author is editor of 1 package");
    }

    [Fact]
    public async Task GetAuthorStatistics_AuthorIsBothEditorAndQuestionAuthor_ReturnsBothCounts()
    {
        // Arrange
        // Create an author who will be both editor AND question author
        var author = await CreateAuthor("Андрій", "Мельник");

        // Create a published package where author is both editor and question author
        await CreatePackage(
            title: "Package 1",
            tourCount: 1,
            questionsPerTour: 4,
            tourEditor: author,
            questionAuthor: author);

        var accessContext = CreateAnonymousAccessContext();

        // Act
        var stats = await _service.GetAuthorStatistics(author.Id, accessContext);

        // Assert
        stats.Should().NotBeNull();
        stats!.QuestionCount.Should().Be(4, "author has 4 questions");
        stats.PackageCount.Should().Be(1, "author is editor of 1 package");
    }

    [Fact]
    public async Task GetAuthorStatistics_AuthorWithQuestionsInMultiplePackages_CountsAllQuestions()
    {
        // Arrange
        var questionAuthor = await CreateAuthor("Наталія", "Козак");
        var editor = await CreateAuthor("Василь", "Ткаченко");

        // Create multiple packages with the same question author
        await CreatePackage(
            title: "Package 1",
            tourCount: 1,
            questionsPerTour: 3,
            tourEditor: editor,
            questionAuthor: questionAuthor);

        await CreatePackage(
            title: "Package 2",
            tourCount: 1,
            questionsPerTour: 5,
            tourEditor: editor,
            questionAuthor: questionAuthor);

        await CreatePackage(
            title: "Package 3",
            tourCount: 2,
            questionsPerTour: 2,
            tourEditor: editor,
            questionAuthor: questionAuthor);

        var accessContext = CreateAnonymousAccessContext();

        // Act
        var stats = await _service.GetAuthorStatistics(questionAuthor.Id, accessContext);

        // Assert
        stats.Should().NotBeNull();
        stats!.QuestionCount.Should().Be(3 + 5 + 4, "author has questions in 3 packages: 3 + 5 + 4 = 12");
        stats.PackageCount.Should().Be(0, "author is not an editor");
    }

    [Fact]
    public async Task GetAuthorStatistics_NonExistentAuthor_ReturnsNull()
    {
        // Arrange
        var accessContext = CreateAnonymousAccessContext();

        // Act
        var stats = await _service.GetAuthorStatistics(99999, accessContext);

        // Assert
        stats.Should().BeNull();
    }

    [Fact]
    public async Task GetAuthorStatistics_DraftPackage_NotIncludedInCounts()
    {
        // Arrange
        var questionAuthor = await CreateAuthor("Сергій", "Іваненко");
        var editor = await CreateAuthor("Олексій", "Петров");

        // Create a draft package (not published) with question author
        await CreatePackage(
            title: "Draft Package",
            status: PackageStatus.Draft,
            tourCount: 1,
            questionsPerTour: 3,
            tourEditor: editor,
            questionAuthor: questionAuthor);

        var accessContext = CreateAdminAccessContext(); // Even admin shouldn't see draft in stats

        // Act
        var stats = await _service.GetAuthorStatistics(questionAuthor.Id, accessContext);

        // Assert
        stats.Should().NotBeNull();
        stats!.QuestionCount.Should().Be(0, "questions in draft packages should not be counted");
        stats.PackageCount.Should().Be(0, "author is question author, not editor");
    }

    #endregion

    #region GetAuthorPackages Tests

    [Fact]
    public async Task GetAuthorPackages_AuthorIsNotEditor_ReturnsEmptyList()
    {
        // Arrange
        var questionAuthor = await CreateAuthor("Тетяна", "Романенко");
        var editor = await CreateAuthor("Олег", "Сидоренко");

        await CreatePackage(
            title: "Package 1",
            tourCount: 1,
            questionsPerTour: 3,
            tourEditor: editor,
            questionAuthor: questionAuthor);

        var accessContext = CreateAnonymousAccessContext();

        // Act
        var packages = await _service.GetAuthorPackages(questionAuthor.Id, accessContext);

        // Assert
        packages.Should().BeEmpty("author is not an editor of any package");
    }

    [Fact]
    public async Task GetAuthorPackages_AuthorIsTourEditor_ReturnsPackageWithTourInfo()
    {
        // Arrange
        var editor = await CreateAuthor("Дмитро", "Павленко");

        await CreatePackage(
            title: "Test Package",
            tourCount: 2,
            questionsPerTour: 3,
            tourEditor: editor,
            questionAuthor: null);

        var accessContext = CreateAnonymousAccessContext();

        // Act
        var packages = await _service.GetAuthorPackages(editor.Id, accessContext);

        // Assert
        packages.Should().HaveCount(1);
        packages[0].PackageTitle.Should().Be("Test Package");
        packages[0].IsGlobalEditor.Should().BeFalse();
        packages[0].Tours.Should().HaveCount(2);
    }

    #endregion

    #region GetAuthorsWithCountsPaginated Tests - Regression Test

    [Fact]
    public async Task GetAuthorsWithCountsPaginated_AuthorWithQuestionsButNotEditor_IncludedInList()
    {
        // Arrange
        // This is a regression test for the bug where authors who are question authors
        // but not editors were not showing correct question counts
        var questionAuthor = await CreateAuthor("Юлія", "Кравченко");
        var editor = await CreateAuthor("Роман", "Литвиненко");

        await CreatePackage(
            title: "Package 1",
            tourCount: 1,
            questionsPerTour: 7,
            tourEditor: editor,
            questionAuthor: questionAuthor);

        var accessContext = CreateAnonymousAccessContext();

        // Act
        var result = await _service.GetAuthorsWithCountsPaginated(accessContext);

        // Assert
        var authorInList = result.Items.FirstOrDefault(a => a.Id == questionAuthor.Id);
        authorInList.Should().NotBeNull("author with questions should appear in the list");
        authorInList!.QuestionCount.Should().Be(7, "author should have 7 questions");
        authorInList.PackageCount.Should().Be(0, "author is not an editor");
    }

    #endregion
}
