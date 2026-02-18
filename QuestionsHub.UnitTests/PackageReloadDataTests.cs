using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using QuestionsHub.Blazor.Domain;
using QuestionsHub.UnitTests.TestInfrastructure;

using Xunit;

namespace QuestionsHub.UnitTests;

/// <summary>
/// Regression tests that verify the EF Include chain used by ReloadPackageData()
/// loads all navigation properties (Tags, PackageEditors) so they are not lost
/// after operations like adding a question or reordering tours.
/// </summary>
public class PackageReloadDataTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory;

    public PackageReloadDataTests()
    {
        _dbFactory = new InMemoryDbContextFactory();
    }

    public void Dispose()
    {
        using var context = _dbFactory.CreateDbContext();
        context.Database.EnsureDeleted();
    }

    /// <summary>
    /// Reproduces the exact Include chain used by ReloadPackageData() in ManagePackageDetail.razor.
    /// If this method's query diverges from the actual code, the test serves as a reminder to keep them in sync.
    /// </summary>
    private Package? ReloadPackage(int packageId)
    {
        using var context = _dbFactory.CreateDbContext();
        return context.Packages
            .Include(p => p.PackageEditors)
            .Include(p => p.Tags)
            .Include(p => p.Tours)
                .ThenInclude(t => t.Editors)
            .Include(p => p.Tours)
                .ThenInclude(t => t.Blocks)
                    .ThenInclude(b => b.Editors)
            .Include(p => p.Tours)
                .ThenInclude(t => t.Questions)
                    .ThenInclude(q => q.Authors)
            .AsNoTracking()
            .FirstOrDefault(p => p.Id == packageId);
    }

    [Fact]
    public void ReloadPackage_PreservesTags()
    {
        // Arrange: create a package with tags
        int packageId;
        using (var context = _dbFactory.CreateDbContext())
        {
            var tag1 = new Tag { Name = "ЛУК" };
            var tag2 = new Tag { Name = "2025" };
            context.Tags.AddRange(tag1, tag2);

            var package = new Package
            {
                Title = "Test Package",
                Tags = [tag1, tag2]
            };
            context.Packages.Add(package);
            context.SaveChanges();
            packageId = package.Id;
        }

        // Act: reload like ReloadPackageData() does
        var reloaded = ReloadPackage(packageId);

        // Assert: tags must be present
        reloaded.Should().NotBeNull();
        reloaded!.Tags.Should().HaveCount(2);
        reloaded.Tags.Select(t => t.Name).Should().BeEquivalentTo(["ЛУК", "2025"]);
    }

    [Fact]
    public void ReloadPackage_PreservesPackageEditors()
    {
        // Arrange: create a package with shared editors
        int packageId;
        using (var context = _dbFactory.CreateDbContext())
        {
            var editor1 = new Author { FirstName = "Іван", LastName = "Петренко" };
            var editor2 = new Author { FirstName = "Олена", LastName = "Коваленко" };
            context.Authors.AddRange(editor1, editor2);

            var package = new Package
            {
                Title = "Test Package",
                SharedEditors = true,
                PackageEditors = [editor1, editor2]
            };
            context.Packages.Add(package);
            context.SaveChanges();
            packageId = package.Id;
        }

        // Act: reload like ReloadPackageData() does
        var reloaded = ReloadPackage(packageId);

        // Assert: package editors must be present
        reloaded.Should().NotBeNull();
        reloaded!.PackageEditors.Should().HaveCount(2);
        reloaded.PackageEditors.Select(e => e.LastName)
            .Should().BeEquivalentTo(["Петренко", "Коваленко"]);
    }

    [Fact]
    public void ReloadPackage_PreservesTagsAndEditorsAfterAddingQuestion()
    {
        // Arrange: create a package with tags, editors, a tour, and a question
        int packageId;
        int tourId;
        using (var context = _dbFactory.CreateDbContext())
        {
            var tag = new Tag { Name = "Кубок" };
            var editor = new Author { FirstName = "Марія", LastName = "Шевченко" };
            context.Tags.Add(tag);
            context.Authors.Add(editor);

            var tour = new Tour
            {
                Number = "1",
                OrderIndex = 0,
                Questions = [],
                Editors = [],
                Blocks = []
            };

            var package = new Package
            {
                Title = "Test Package",
                SharedEditors = true,
                PackageEditors = [editor],
                Tags = [tag],
                Tours = [tour]
            };
            context.Packages.Add(package);
            context.SaveChanges();
            packageId = package.Id;
            tourId = tour.Id;
        }

        // Simulate adding a question (like CreateQuestionAsync does)
        using (var context = _dbFactory.CreateDbContext())
        {
            var question = new Question
            {
                TourId = tourId,
                OrderIndex = 0,
                Number = "1",
                Text = "Тестове запитання",
                Answer = "Відповідь",
                Authors = []
            };
            context.Questions.Add(question);

            var dbPackage = context.Packages.Find(packageId)!;
            dbPackage.TotalQuestions++;
            context.SaveChanges();
        }

        // Act: reload like ReloadPackageData() does after adding question
        var reloaded = ReloadPackage(packageId);

        // Assert: tags and editors must still be present
        reloaded.Should().NotBeNull();
        reloaded!.Tags.Should().HaveCount(1);
        reloaded.Tags[0].Name.Should().Be("Кубок");
        reloaded.PackageEditors.Should().HaveCount(1);
        reloaded.PackageEditors[0].LastName.Should().Be("Шевченко");

        // Also verify the question was added
        reloaded.Tours.Should().HaveCount(1);
        reloaded.Tours[0].Questions.Should().HaveCount(1);
        reloaded.TotalQuestions.Should().Be(1);
    }
}
