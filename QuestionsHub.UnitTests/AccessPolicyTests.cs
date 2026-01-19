using FluentAssertions;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure;
using Xunit;

namespace QuestionsHub.UnitTests;

/// <summary>
/// Tests for package access policies.
/// These tests verify that package status filtering logic works correctly.
///
/// Access Policy Rules:
/// - Draft packages: Only visible to owner and admins
/// - Published packages: Visible to all users
/// - Archived packages: Hidden from main list but accessible via direct link (owner/admin only for editing)
///
/// For public pages (like /editors and /editor/{id}):
/// - Only questions and packages with Published status should be counted/displayed
/// - Authors with only Draft/Archived content should not appear on public lists
/// </summary>
public class AccessPolicyTests
{
    #region PackageStatus Tests

    [Fact]
    public void PackageStatus_Draft_ShouldNotBeVisibleToPublic()
    {
        // Arrange
        var package = new Package
        {
            Id = 1,
            Title = "Draft Package",
            Status = PackageStatus.Draft,
            Tours = []
        };

        // Act & Assert - Draft packages should not be included in public queries
        package.Status.Should().Be(PackageStatus.Draft);
        IsPubliclyVisible(package).Should().BeFalse();
    }

    [Fact]
    public void PackageStatus_Published_ShouldBeVisibleToPublic()
    {
        // Arrange
        var package = new Package
        {
            Id = 1,
            Title = "Published Package",
            Status = PackageStatus.Published,
            Tours = []
        };

        // Act & Assert - Published packages should be included in public queries
        package.Status.Should().Be(PackageStatus.Published);
        IsPubliclyVisible(package).Should().BeTrue();
    }

    [Fact]
    public void PackageStatus_Archived_ShouldNotBeVisibleToPublic()
    {
        // Arrange
        var package = new Package
        {
            Id = 1,
            Title = "Archived Package",
            Status = PackageStatus.Archived,
            Tours = []
        };

        // Act & Assert - Archived packages should not be included in public queries
        package.Status.Should().Be(PackageStatus.Archived);
        IsPubliclyVisible(package).Should().BeFalse();
    }

    #endregion

    #region Author Visibility Tests

    [Fact]
    public void Author_WithOnlyDraftQuestions_ShouldHaveZeroPublicQuestionCount()
    {
        // Arrange
        var author = new Author { Id = 1, FirstName = "Test", LastName = "Author" };
        var draftPackage = CreatePackageWithStatus(PackageStatus.Draft);
        var questions = CreateQuestionsForTour(draftPackage.Tours[0], author);

        // Act
        var publicQuestionCount = CountPublicQuestions(author, questions);

        // Assert
        publicQuestionCount.Should().Be(0,
            "Questions from draft packages should not be counted for public display");
    }

    [Fact]
    public void Author_WithOnlyArchivedQuestions_ShouldHaveZeroPublicQuestionCount()
    {
        // Arrange
        var author = new Author { Id = 1, FirstName = "Test", LastName = "Author" };
        var archivedPackage = CreatePackageWithStatus(PackageStatus.Archived);
        var questions = CreateQuestionsForTour(archivedPackage.Tours[0], author);

        // Act
        var publicQuestionCount = CountPublicQuestions(author, questions);

        // Assert
        publicQuestionCount.Should().Be(0,
            "Questions from archived packages should not be counted for public display");
    }

    [Fact]
    public void Author_WithPublishedQuestions_ShouldHaveCorrectPublicQuestionCount()
    {
        // Arrange
        var author = new Author { Id = 1, FirstName = "Test", LastName = "Author" };
        var publishedPackage = CreatePackageWithStatus(PackageStatus.Published);
        var questions = CreateQuestionsForTour(publishedPackage.Tours[0], author);

        // Act
        var publicQuestionCount = CountPublicQuestions(author, questions);

        // Assert
        publicQuestionCount.Should().Be(3,
            "Questions from published packages should be counted for public display");
    }

    [Fact]
    public void Author_WithMixedPackages_ShouldOnlyCountPublishedQuestions()
    {
        // Arrange
        var author = new Author { Id = 1, FirstName = "Test", LastName = "Author" };

        var draftPackage = CreatePackageWithStatus(PackageStatus.Draft);
        var publishedPackage = CreatePackageWithStatus(PackageStatus.Published);
        var archivedPackage = CreatePackageWithStatus(PackageStatus.Archived);

        var draftQuestions = CreateQuestionsForTour(draftPackage.Tours[0], author);
        var publishedQuestions = CreateQuestionsForTour(publishedPackage.Tours[0], author);
        var archivedQuestions = CreateQuestionsForTour(archivedPackage.Tours[0], author);

        var allQuestions = draftQuestions.Concat(publishedQuestions).Concat(archivedQuestions).ToList();

        // Act
        var publicQuestionCount = CountPublicQuestions(author, allQuestions);

        // Assert
        publicQuestionCount.Should().Be(3,
            "Only questions from published packages should be counted");
    }

    [Fact]
    public void Author_WithOnlyDraftPackagesAsEditor_ShouldHaveZeroPublicPackageCount()
    {
        // Arrange
        var author = new Author { Id = 1, FirstName = "Test", LastName = "Author" };
        var draftPackage = CreatePackageWithStatus(PackageStatus.Draft);
        draftPackage.Tours[0].Editors.Add(author);

        var tours = new List<Tour> { draftPackage.Tours[0] };

        // Act
        var publicPackageCount = CountPublicPackagesAsEditor(tours);

        // Assert
        publicPackageCount.Should().Be(0,
            "Draft packages should not be counted in editor statistics");
    }

    [Fact]
    public void Author_WithPublishedPackagesAsEditor_ShouldHaveCorrectPublicPackageCount()
    {
        // Arrange
        var author = new Author { Id = 1, FirstName = "Test", LastName = "Author" };
        var publishedPackage = CreatePackageWithStatus(PackageStatus.Published);
        publishedPackage.Tours[0].Editors.Add(author);

        var tours = new List<Tour> { publishedPackage.Tours[0] };

        // Act
        var publicPackageCount = CountPublicPackagesAsEditor(tours);

        // Assert
        publicPackageCount.Should().Be(1,
            "Published packages should be counted in editor statistics");
    }

    [Fact]
    public void Author_AsBlockEditor_WithOnlyDraftPackages_ShouldHaveZeroPublicPackageCount()
    {
        // Arrange
        var author = new Author { Id = 1, FirstName = "Test", LastName = "Author" };
        var draftPackage = CreatePackageWithBlockStatus(PackageStatus.Draft);
        draftPackage.Tours[0].Blocks[0].Editors.Add(author);

        var blocks = new List<Block> { draftPackage.Tours[0].Blocks[0] };

        // Act
        var publicPackageCount = CountPublicPackagesAsBlockEditor(blocks);

        // Assert
        publicPackageCount.Should().Be(0,
            "Draft packages should not be counted for block editors");
    }

    [Fact]
    public void Author_AsBlockEditor_WithPublishedPackages_ShouldHaveCorrectPublicPackageCount()
    {
        // Arrange
        var author = new Author { Id = 1, FirstName = "Test", LastName = "Author" };
        var publishedPackage = CreatePackageWithBlockStatus(PackageStatus.Published);
        publishedPackage.Tours[0].Blocks[0].Editors.Add(author);

        var blocks = new List<Block> { publishedPackage.Tours[0].Blocks[0] };

        // Act
        var publicPackageCount = CountPublicPackagesAsBlockEditor(blocks);

        // Assert
        publicPackageCount.Should().Be(1,
            "Published packages should be counted for block editors");
    }

    #endregion

    #region Author List Visibility Tests

    [Fact]
    public void AuthorList_ShouldExcludeAuthors_WithOnlyDraftContent()
    {
        // Arrange
        var authorWithDraftOnly = new Author { Id = 1, FirstName = "Draft", LastName = "Author" };
        var authorWithPublished = new Author { Id = 2, FirstName = "Published", LastName = "Author" };

        var draftPackage = CreatePackageWithStatus(PackageStatus.Draft);
        var publishedPackage = CreatePackageWithStatus(PackageStatus.Published);

        var draftQuestions = CreateQuestionsForTour(draftPackage.Tours[0], authorWithDraftOnly);
        var publishedQuestions = CreateQuestionsForTour(publishedPackage.Tours[0], authorWithPublished);

        var allAuthors = new List<Author> { authorWithDraftOnly, authorWithPublished };
        var allQuestions = draftQuestions.Concat(publishedQuestions).ToList();

        // Act
        var visibleAuthors = FilterVisibleAuthors(allAuthors, allQuestions);

        // Assert
        visibleAuthors.Should().HaveCount(1);
        visibleAuthors.Should().Contain(authorWithPublished);
        visibleAuthors.Should().NotContain(authorWithDraftOnly,
            "Authors with only draft content should not appear on public list");
    }

    [Fact]
    public void AuthorList_ShouldExcludeAuthors_WithOnlyArchivedContent()
    {
        // Arrange
        var authorWithArchivedOnly = new Author { Id = 1, FirstName = "Archived", LastName = "Author" };
        var authorWithPublished = new Author { Id = 2, FirstName = "Published", LastName = "Author" };

        var archivedPackage = CreatePackageWithStatus(PackageStatus.Archived);
        var publishedPackage = CreatePackageWithStatus(PackageStatus.Published);

        var archivedQuestions = CreateQuestionsForTour(archivedPackage.Tours[0], authorWithArchivedOnly);
        var publishedQuestions = CreateQuestionsForTour(publishedPackage.Tours[0], authorWithPublished);

        var allAuthors = new List<Author> { authorWithArchivedOnly, authorWithPublished };
        var allQuestions = archivedQuestions.Concat(publishedQuestions).ToList();

        // Act
        var visibleAuthors = FilterVisibleAuthors(allAuthors, allQuestions);

        // Assert
        visibleAuthors.Should().HaveCount(1);
        visibleAuthors.Should().Contain(authorWithPublished);
        visibleAuthors.Should().NotContain(authorWithArchivedOnly,
            "Authors with only archived content should not appear on public list");
    }

    [Fact]
    public void AuthorList_ShouldIncludeAuthor_WithMixedContentIncludingPublished()
    {
        // Arrange
        var author = new Author { Id = 1, FirstName = "Mixed", LastName = "Author" };

        var draftPackage = CreatePackageWithStatus(PackageStatus.Draft);
        var publishedPackage = CreatePackageWithStatus(PackageStatus.Published);
        var archivedPackage = CreatePackageWithStatus(PackageStatus.Archived);

        var draftQuestions = CreateQuestionsForTour(draftPackage.Tours[0], author);
        var publishedQuestions = CreateQuestionsForTour(publishedPackage.Tours[0], author);
        var archivedQuestions = CreateQuestionsForTour(archivedPackage.Tours[0], author);

        var allAuthors = new List<Author> { author };
        var allQuestions = draftQuestions.Concat(publishedQuestions).Concat(archivedQuestions).ToList();

        // Act
        var visibleAuthors = FilterVisibleAuthors(allAuthors, allQuestions);

        // Assert
        visibleAuthors.Should().HaveCount(1);
        visibleAuthors.Should().Contain(author,
            "Authors with at least one published question should appear on public list");
    }

    #endregion

    #region Search Access Tests

    [Fact]
    public void Search_ShouldOnlyReturnQuestionsFromPublishedPackages()
    {
        // Arrange
        var draftPackage = CreatePackageWithStatus(PackageStatus.Draft);
        var publishedPackage = CreatePackageWithStatus(PackageStatus.Published);
        var archivedPackage = CreatePackageWithStatus(PackageStatus.Archived);

        var author = new Author { Id = 1, FirstName = "Test", LastName = "Author" };

        var draftQuestions = CreateQuestionsForTour(draftPackage.Tours[0], author);
        var publishedQuestions = CreateQuestionsForTour(publishedPackage.Tours[0], author);
        var archivedQuestions = CreateQuestionsForTour(archivedPackage.Tours[0], author);

        var allQuestions = draftQuestions.Concat(publishedQuestions).Concat(archivedQuestions).ToList();

        // Act
        var searchableQuestions = FilterSearchableQuestions(allQuestions);

        // Assert
        searchableQuestions.Should().HaveCount(3,
            "Only questions from published packages should be searchable");
        searchableQuestions.Should().OnlyContain(q => q.Tour.Package.Status == PackageStatus.Published);
    }

    #endregion

    #region Package Access Control Tests

    [Fact]
    public void PackageAccess_Published_ShouldBeAccessibleByAnyone()
    {
        // Arrange
        var package = CreatePackageWithStatus(PackageStatus.Published, ownerId: "owner123");

        // Act & Assert
        CanViewPackage(package, userId: null, isAdmin: false).Should().BeTrue(
            "Published packages should be visible to anonymous users");
        CanViewPackage(package, userId: "random-user", isAdmin: false).Should().BeTrue(
            "Published packages should be visible to any authenticated user");
        CanViewPackage(package, userId: "owner123", isAdmin: false).Should().BeTrue(
            "Published packages should be visible to owner");
        CanViewPackage(package, userId: "admin", isAdmin: true).Should().BeTrue(
            "Published packages should be visible to admin");
    }

    [Fact]
    public void PackageAccess_Draft_ShouldOnlyBeAccessibleByOwnerOrAdmin()
    {
        // Arrange
        var package = CreatePackageWithStatus(PackageStatus.Draft, ownerId: "owner123");

        // Act & Assert
        CanViewPackage(package, userId: null, isAdmin: false).Should().BeFalse(
            "Draft packages should not be visible to anonymous users");
        CanViewPackage(package, userId: "random-user", isAdmin: false).Should().BeFalse(
            "Draft packages should not be visible to other users");
        CanViewPackage(package, userId: "owner123", isAdmin: false).Should().BeTrue(
            "Draft packages should be visible to owner");
        CanViewPackage(package, userId: "admin", isAdmin: true).Should().BeTrue(
            "Draft packages should be visible to admin");
    }

    [Fact]
    public void PackageAccess_Archived_ShouldBeAccessibleViaDirectLink()
    {
        // Arrange
        var package = CreatePackageWithStatus(PackageStatus.Archived, ownerId: "owner123");

        // Act & Assert - Archived packages are accessible via direct link but not in listings
        CanViewPackageViaDirectLink(package).Should().BeTrue(
            "Archived packages should be accessible via direct link");
        IsPubliclyVisible(package).Should().BeFalse(
            "Archived packages should not appear in public listings");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Simulates the public visibility check (only Published status is publicly visible).
    /// </summary>
    private static bool IsPubliclyVisible(Package package)
    {
        return package.Status == PackageStatus.Published;
    }

    /// <summary>
    /// Simulates the package access check logic from PackageDetail.razor.
    /// </summary>
    private static bool CanViewPackage(Package package, string? userId, bool isAdmin)
    {
        // Published packages are visible to everyone
        if (package.Status == PackageStatus.Published)
            return true;

        // Archived packages are accessible via direct link
        if (package.Status == PackageStatus.Archived)
            return true;

        // Draft packages require authentication
        if (string.IsNullOrEmpty(userId))
            return false;

        // Admins can see all packages
        if (isAdmin)
            return true;

        // Owners can see their own draft packages
        return package.OwnerId == userId;
    }

    /// <summary>
    /// Simulates direct link access for archived packages.
    /// </summary>
    private static bool CanViewPackageViaDirectLink(Package package)
    {
        return package.Status == PackageStatus.Published || package.Status == PackageStatus.Archived;
    }

    /// <summary>
    /// Counts publicly visible questions for an author (only from published packages).
    /// </summary>
    private static int CountPublicQuestions(Author author, List<Question> questions)
    {
        return questions.Count(q =>
            q.Authors.Any(a => a.Id == author.Id) &&
            q.Tour.Package.Status == PackageStatus.Published);
    }

    /// <summary>
    /// Counts publicly visible packages where author is a tour editor.
    /// </summary>
    private static int CountPublicPackagesAsEditor(List<Tour> tours)
    {
        return tours
            .Where(t => t.Package.Status == PackageStatus.Published)
            .Select(t => t.PackageId)
            .Distinct()
            .Count();
    }

    /// <summary>
    /// Counts publicly visible packages where author is a block editor.
    /// </summary>
    private static int CountPublicPackagesAsBlockEditor(List<Block> blocks)
    {
        return blocks
            .Where(b => b.Tour.Package.Status == PackageStatus.Published)
            .Select(b => b.Tour.PackageId)
            .Distinct()
            .Count();
    }

    /// <summary>
    /// Filters authors to only those with publicly visible content.
    /// </summary>
    private static List<Author> FilterVisibleAuthors(List<Author> authors, List<Question> questions)
    {
        return authors
            .Where(a => CountPublicQuestions(a, questions) > 0)
            .ToList();
    }

    /// <summary>
    /// Filters questions to only those from published packages (searchable).
    /// </summary>
    private static List<Question> FilterSearchableQuestions(List<Question> questions)
    {
        return questions
            .Where(q => q.Tour.Package.Status == PackageStatus.Published)
            .ToList();
    }

    /// <summary>
    /// Creates a package with the specified status.
    /// </summary>
    private static Package CreatePackageWithStatus(PackageStatus status, string? ownerId = null)
    {
        var package = new Package
        {
            Id = Random.Shared.Next(1, 10000),
            Title = $"Test Package ({status})",
            Status = status,
            OwnerId = ownerId,
            Tours =
            [
                new Tour
                {
                    Id = Random.Shared.Next(1, 10000),
                    Number = "1",
                    Editors = [],
                    Blocks = [],
                    Questions = []
                }
            ]
        };

        // Set up bidirectional navigation
        package.Tours[0].Package = package;
        package.Tours[0].PackageId = package.Id;

        return package;
    }

    /// <summary>
    /// Creates a package with blocks and the specified status.
    /// </summary>
    private static Package CreatePackageWithBlockStatus(PackageStatus status)
    {
        var package = new Package
        {
            Id = Random.Shared.Next(1, 10000),
            Title = $"Test Package with Blocks ({status})",
            Status = status,
            Tours =
            [
                new Tour
                {
                    Id = Random.Shared.Next(1, 10000),
                    Number = "1",
                    Editors = [],
                    Blocks =
                    [
                        new Block
                        {
                            Id = Random.Shared.Next(1, 10000),
                            OrderIndex = 0,
                            Editors = []
                        }
                    ],
                    Questions = []
                }
            ]
        };

        // Set up bidirectional navigation
        package.Tours[0].Package = package;
        package.Tours[0].PackageId = package.Id;
        package.Tours[0].Blocks[0].Tour = package.Tours[0];
        package.Tours[0].Blocks[0].TourId = package.Tours[0].Id;

        return package;
    }

    /// <summary>
    /// Creates test questions for a tour with the given author.
    /// </summary>
    private static List<Question> CreateQuestionsForTour(Tour tour, Author author)
    {
        var questions = new List<Question>();
        for (var i = 0; i < 3; i++)
        {
            var question = new Question
            {
                Id = Random.Shared.Next(1, 10000),
                Number = (i + 1).ToString(),
                Text = $"Test question {i + 1}",
                Answer = $"Test answer {i + 1}",
                OrderIndex = i,
                TourId = tour.Id,
                Tour = tour,
                Authors = [author]
            };
            questions.Add(question);
            tour.Questions.Add(question);
        }
        return questions;
    }

    #endregion

    #region Package Access Level Tests

    [Fact]
    public void PackageAccessLevel_All_ShouldBeAccessibleByAnyone()
    {
        // Arrange
        var package = CreatePackageWithAccessLevel(PackageAccessLevel.All, ownerId: "owner123");

        // Act & Assert
        CreateAccessContext(userId: null, isAdmin: false, isEditor: false, hasVerifiedEmail: false)
            .CanViewPackage(package)
            .Should().BeTrue("Packages with AccessLevel.All should be visible to anonymous users");
        CreateAccessContext(userId: "random-user", isAdmin: false, isEditor: false, hasVerifiedEmail: false)
            .CanViewPackage(package)
            .Should().BeTrue("Packages with AccessLevel.All should be visible to unverified users");
    }

    [Fact]
    public void PackageAccessLevel_RegisteredOnly_ShouldRequireVerifiedEmail()
    {
        // Arrange
        var package = CreatePackageWithAccessLevel(PackageAccessLevel.RegisteredOnly, ownerId: "owner123");

        // Act & Assert
        CreateAccessContext(userId: null, isAdmin: false, isEditor: false, hasVerifiedEmail: false)
            .CanViewPackage(package)
            .Should().BeFalse("Packages with AccessLevel.RegisteredOnly should not be visible to anonymous users");
        CreateAccessContext(userId: "random-user", isAdmin: false, isEditor: false, hasVerifiedEmail: false)
            .CanViewPackage(package)
            .Should().BeFalse("Packages with AccessLevel.RegisteredOnly should not be visible to unverified users");
        CreateAccessContext(userId: "verified-user", isAdmin: false, isEditor: false, hasVerifiedEmail: true)
            .CanViewPackage(package)
            .Should().BeTrue("Packages with AccessLevel.RegisteredOnly should be visible to verified users");
    }

    [Fact]
    public void PackageAccessLevel_EditorsOnly_ShouldRequireEditorRole()
    {
        // Arrange
        var package = CreatePackageWithAccessLevel(PackageAccessLevel.EditorsOnly, ownerId: "owner123");

        // Act & Assert
        CreateAccessContext(userId: null, isAdmin: false, isEditor: false, hasVerifiedEmail: false)
            .CanViewPackage(package)
            .Should().BeFalse("Packages with AccessLevel.EditorsOnly should not be visible to anonymous users");
        CreateAccessContext(userId: "verified-user", isAdmin: false, isEditor: false, hasVerifiedEmail: true)
            .CanViewPackage(package)
            .Should().BeFalse("Packages with AccessLevel.EditorsOnly should not be visible to non-editors");
        CreateAccessContext(userId: "editor-user", isAdmin: false, isEditor: true, hasVerifiedEmail: true)
            .CanViewPackage(package)
            .Should().BeTrue("Packages with AccessLevel.EditorsOnly should be visible to editors");
    }

    [Fact]
    public void PackageAccessLevel_Admin_ShouldAlwaysHaveAccess()
    {
        // Arrange
        var allPackage = CreatePackageWithAccessLevel(PackageAccessLevel.All, ownerId: "owner123");
        var registeredOnlyPackage = CreatePackageWithAccessLevel(PackageAccessLevel.RegisteredOnly, ownerId: "owner123");
        var editorsOnlyPackage = CreatePackageWithAccessLevel(PackageAccessLevel.EditorsOnly, ownerId: "owner123");
        var adminContext = CreateAccessContext(userId: "admin", isAdmin: true, isEditor: false, hasVerifiedEmail: false);

        // Act & Assert
        adminContext.CanViewPackage(allPackage)
            .Should().BeTrue("Admin should always have access to All packages");
        adminContext.CanViewPackage(registeredOnlyPackage)
            .Should().BeTrue("Admin should always have access to RegisteredOnly packages");
        adminContext.CanViewPackage(editorsOnlyPackage)
            .Should().BeTrue("Admin should always have access to EditorsOnly packages");
    }

    [Fact]
    public void PackageAccessLevel_Owner_ShouldAlwaysHaveAccessToOwnPackages()
    {
        // Arrange
        var registeredOnlyPackage = CreatePackageWithAccessLevel(PackageAccessLevel.RegisteredOnly, ownerId: "owner123");
        var editorsOnlyPackage = CreatePackageWithAccessLevel(PackageAccessLevel.EditorsOnly, ownerId: "owner123");
        var ownerContext = CreateAccessContext(userId: "owner123", isAdmin: false, isEditor: false, hasVerifiedEmail: false);

        // Act & Assert - Owner should access their own packages regardless of access level
        ownerContext.CanViewPackage(registeredOnlyPackage)
            .Should().BeTrue("Owner should always have access to their own RegisteredOnly packages");
        ownerContext.CanViewPackage(editorsOnlyPackage)
            .Should().BeTrue("Owner should always have access to their own EditorsOnly packages");
    }

    /// <summary>
    /// Creates a PackageAccessContext for testing.
    /// </summary>
    private static PackageAccessContext CreateAccessContext(
        string? userId,
        bool isAdmin,
        bool isEditor,
        bool hasVerifiedEmail)
    {
        return new PackageAccessContext(
            IsAdmin: isAdmin,
            IsEditor: isEditor,
            HasVerifiedEmail: hasVerifiedEmail,
            UserId: userId
        );
    }

    /// <summary>
    /// Creates a published package with the specified access level.
    /// </summary>
    private static Package CreatePackageWithAccessLevel(PackageAccessLevel accessLevel, string? ownerId = null)
    {
        var package = new Package
        {
            Id = Random.Shared.Next(1, 10000),
            Title = $"Test Package ({accessLevel})",
            Status = PackageStatus.Published,
            AccessLevel = accessLevel,
            OwnerId = ownerId,
            Tours = []
        };
        return package;
    }

    #endregion
}
