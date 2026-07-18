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

    #region MergeAuthors Tests

    /// <summary>Creates a published package with a single question authored by the given authors; returns the question id.</summary>
    private async Task<int> CreateQuestionAuthoredBy(params int[] authorIds)
    {
        using var context = _dbFactory.CreateDbContext();

        var authors = new List<Author>();
        foreach (var id in authorIds)
        {
            authors.Add((await context.Authors.FindAsync(id))!);
        }

        var question = new Question
        {
            Number = "1",
            OrderIndex = 0,
            Text = "Q",
            Answer = "A",
            Authors = authors
        };
        var tour = new Tour { Number = "1", OrderIndex = 0, Questions = [question], Editors = [], Blocks = [] };
        var package = new Package
        {
            Title = "Package",
            Status = PackageStatus.Published,
            AccessLevel = PackageAccessLevel.All,
            NumberingMode = QuestionNumberingMode.Global,
            Tours = [tour]
        };

        context.Packages.Add(package);
        await context.SaveChangesAsync();

        return question.Id;
    }

    /// <summary>Creates a published package with a single tour edited by the given authors; returns the tour id.</summary>
    private async Task<int> CreateTourEditedBy(params int[] authorIds)
    {
        using var context = _dbFactory.CreateDbContext();

        var editors = new List<Author>();
        foreach (var id in authorIds)
        {
            editors.Add((await context.Authors.FindAsync(id))!);
        }

        var tour = new Tour { Number = "1", OrderIndex = 0, Questions = [], Editors = editors, Blocks = [] };
        var package = new Package
        {
            Title = "Package",
            Status = PackageStatus.Published,
            AccessLevel = PackageAccessLevel.All,
            NumberingMode = QuestionNumberingMode.Global,
            Tours = [tour]
        };

        context.Packages.Add(package);
        await context.SaveChangesAsync();

        return tour.Id;
    }

    /// <summary>Creates a published package with a single block edited by the given authors; returns the block id.</summary>
    private async Task<int> CreateBlockEditedBy(params int[] authorIds)
    {
        using var context = _dbFactory.CreateDbContext();

        var editors = new List<Author>();
        foreach (var id in authorIds)
        {
            editors.Add((await context.Authors.FindAsync(id))!);
        }

        var block = new Block { OrderIndex = 0, Editors = editors, Questions = [] };
        var tour = new Tour { Number = "1", OrderIndex = 0, Questions = [], Editors = [], Blocks = [block] };
        var package = new Package
        {
            Title = "Package",
            Status = PackageStatus.Published,
            AccessLevel = PackageAccessLevel.All,
            NumberingMode = QuestionNumberingMode.Global,
            Tours = [tour]
        };

        context.Packages.Add(package);
        await context.SaveChangesAsync();

        return block.Id;
    }

    /// <summary>Creates a published package with shared package-level editors; returns the package id.</summary>
    private async Task<int> CreatePackageEditedBy(params int[] authorIds)
    {
        using var context = _dbFactory.CreateDbContext();

        var editors = new List<Author>();
        foreach (var id in authorIds)
        {
            editors.Add((await context.Authors.FindAsync(id))!);
        }

        var package = new Package
        {
            Title = "Package",
            Status = PackageStatus.Published,
            AccessLevel = PackageAccessLevel.All,
            NumberingMode = QuestionNumberingMode.Global,
            SharedEditors = true,
            PackageEditors = editors,
            Tours = []
        };

        context.Packages.Add(package);
        await context.SaveChangesAsync();

        return package.Id;
    }

    /// <summary>Creates a user and links it to the given author; returns the user id.</summary>
    private async Task<string> LinkUserToAuthor(int authorId, string firstName, string lastName)
    {
        using var context = _dbFactory.CreateDbContext();

        var user = new ApplicationUser
        {
            UserName = $"{firstName}.{lastName}@example.com",
            Email = $"{firstName}.{lastName}@example.com",
            FirstName = firstName,
            LastName = lastName
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var author = await context.Authors.FindAsync(authorId);
        author!.UserId = user.Id;
        await context.SaveChangesAsync();

        return user.Id;
    }

    [Fact]
    public async Task MergeAuthors_ReassignsQuestionAuthorship_AndDeletesSource()
    {
        // Arrange
        var source = await CreateAuthor("Іван", "Петеренко"); // typo
        var target = await CreateAuthor("Іван", "Петренко");  // correct
        var questionId = await CreateQuestionAuthoredBy(source.Id);

        // Act
        var result = await _service.MergeAuthors(source.Id, target.Id);

        // Assert
        result.Success.Should().BeTrue();

        using var context = _dbFactory.CreateDbContext();
        var question = await context.Questions.Include(q => q.Authors).FirstAsync(q => q.Id == questionId);
        question.Authors.Should().ContainSingle().Which.Id.Should().Be(target.Id);
        (await context.Authors.FindAsync(source.Id)).Should().BeNull("source author is deleted after merge");
    }

    [Fact]
    public async Task MergeAuthors_ReassignsTourEditorRole()
    {
        var source = await CreateAuthor("Олена", "Шевченкo"); // typo (latin o)
        var target = await CreateAuthor("Олена", "Шевченко");
        var tourId = await CreateTourEditedBy(source.Id);

        var result = await _service.MergeAuthors(source.Id, target.Id);

        result.Success.Should().BeTrue();
        using var context = _dbFactory.CreateDbContext();
        var tour = await context.Tours.Include(t => t.Editors).FirstAsync(t => t.Id == tourId);
        tour.Editors.Should().ContainSingle().Which.Id.Should().Be(target.Id);
    }

    [Fact]
    public async Task MergeAuthors_ReassignsBlockEditorRole()
    {
        var source = await CreateAuthor("Петро", "Бондаренкo");
        var target = await CreateAuthor("Петро", "Бондаренко");
        var blockId = await CreateBlockEditedBy(source.Id);

        var result = await _service.MergeAuthors(source.Id, target.Id);

        result.Success.Should().BeTrue();
        using var context = _dbFactory.CreateDbContext();
        var block = await context.Blocks.Include(b => b.Editors).FirstAsync(b => b.Id == blockId);
        block.Editors.Should().ContainSingle().Which.Id.Should().Be(target.Id);
    }

    [Fact]
    public async Task MergeAuthors_ReassignsPackageEditorRole()
    {
        var source = await CreateAuthor("Марія", "Коваленкo");
        var target = await CreateAuthor("Марія", "Коваленко");
        var packageId = await CreatePackageEditedBy(source.Id);

        var result = await _service.MergeAuthors(source.Id, target.Id);

        result.Success.Should().BeTrue();
        using var context = _dbFactory.CreateDbContext();
        var package = await context.Packages.Include(p => p.PackageEditors).FirstAsync(p => p.Id == packageId);
        package.PackageEditors.Should().ContainSingle().Which.Id.Should().Be(target.Id);
    }

    [Fact]
    public async Task MergeAuthors_DeduplicatesSharedQuestion()
    {
        // Arrange: a single question authored by BOTH the source and the target.
        var source = await CreateAuthor("Андрій", "Мельникk"); // typo
        var target = await CreateAuthor("Андрій", "Мельник");
        var questionId = await CreateQuestionAuthoredBy(source.Id, target.Id);

        // Act
        var result = await _service.MergeAuthors(source.Id, target.Id);

        // Assert: exactly one authorship link remains (target), no duplicate.
        result.Success.Should().BeTrue();
        using var context = _dbFactory.CreateDbContext();
        var question = await context.Questions.Include(q => q.Authors).FirstAsync(q => q.Id == questionId);
        question.Authors.Should().ContainSingle().Which.Id.Should().Be(target.Id);
        (await context.Authors.FindAsync(source.Id)).Should().BeNull();
    }

    [Fact]
    public async Task MergeAuthors_TransfersUserLink_WhenTargetHasNone()
    {
        var source = await CreateAuthor("Сергій", "Іваненкo");
        var target = await CreateAuthor("Сергій", "Іваненко");
        var userId = await LinkUserToAuthor(source.Id, "Сергій", "Іваненко");

        var result = await _service.MergeAuthors(source.Id, target.Id);

        result.Success.Should().BeTrue();
        using var context = _dbFactory.CreateDbContext();
        var kept = await context.Authors.FindAsync(target.Id);
        kept!.UserId.Should().Be(userId, "the survivor should inherit the deleted author's user link");
        (await context.Authors.FindAsync(source.Id)).Should().BeNull();
    }

    [Fact]
    public async Task MergeAuthors_BlocksMerge_WhenBothLinkedToUsers()
    {
        var source = await CreateAuthor("Роман", "Литвиненкo");
        var target = await CreateAuthor("Роман", "Литвиненко");
        await LinkUserToAuthor(source.Id, "Роман", "Литвиненко-А");
        await LinkUserToAuthor(target.Id, "Роман", "Литвиненко-Б");
        var questionId = await CreateQuestionAuthoredBy(source.Id);

        var result = await _service.MergeAuthors(source.Id, target.Id);

        // Assert: refused, and nothing changed.
        result.Success.Should().BeFalse();
        using var context = _dbFactory.CreateDbContext();
        (await context.Authors.FindAsync(source.Id)).Should().NotBeNull("merge was blocked, source is untouched");
        (await context.Authors.FindAsync(target.Id)).Should().NotBeNull();
        var question = await context.Questions.Include(q => q.Authors).FirstAsync(q => q.Id == questionId);
        question.Authors.Should().ContainSingle().Which.Id.Should().Be(source.Id, "authorship was not reassigned");
    }

    [Fact]
    public async Task MergeAuthors_SameAuthor_Fails()
    {
        var author = await CreateAuthor("Юлія", "Кравченко");

        var result = await _service.MergeAuthors(author.Id, author.Id);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task MergeAuthors_MissingSource_Fails()
    {
        var target = await CreateAuthor("Наталія", "Козак");

        var result = await _service.MergeAuthors(99999, target.Id);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task MergeAuthors_MissingTarget_Fails()
    {
        var source = await CreateAuthor("Василь", "Ткаченко");

        var result = await _service.MergeAuthors(source.Id, 99999);

        result.Success.Should().BeFalse();
    }

    #endregion
}
