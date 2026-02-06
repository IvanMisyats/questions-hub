using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure;
using QuestionsHub.UnitTests.TestInfrastructure;
using Xunit;

namespace QuestionsHub.UnitTests;

public class TagServiceTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory;
    private readonly MemoryCache _cache;
    private readonly TagService _service;

    public TagServiceTests()
    {
        _dbFactory = new InMemoryDbContextFactory();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _service = new TagService(_dbFactory, _cache);
    }

    public void Dispose()
    {
        _cache.Dispose();
        using var context = _dbFactory.CreateDbContext();
        context.Database.EnsureDeleted();
    }

    #region Helper Methods

    private async Task<Tag> CreateTag(string name)
    {
        using var context = _dbFactory.CreateDbContext();
        var tag = new Tag { Name = name };
        context.Tags.Add(tag);
        await context.SaveChangesAsync();
        return tag;
    }

    private async Task<Package> CreatePackageWithTags(string title, params string[] tagNames)
    {
        return await CreatePackageWithTags(title, PackageStatus.Published, tagNames);
    }

    private async Task<Package> CreatePackageWithTags(
        string title, PackageStatus status, params string[] tagNames)
    {
        using var context = _dbFactory.CreateDbContext();

        var tags = new List<Tag>();
        foreach (var name in tagNames)
        {
            var existing = await context.Tags.FirstOrDefaultAsync(t => t.Name == name);
            if (existing != null)
            {
                tags.Add(existing);
            }
            else
            {
                var tag = new Tag { Name = name };
                context.Tags.Add(tag);
                await context.SaveChangesAsync();
                tags.Add(tag);
            }
        }

        var package = new Package
        {
            Title = title,
            Status = status,
            Tags = tags,
            Tours =
            [
                new Tour
                {
                    Number = "1",
                    OrderIndex = 0,
                    Questions =
                    [
                        new Question { Number = "1", Text = "Q1", Answer = "A1", OrderIndex = 0 }
                    ]
                }
            ]
        };
        context.Packages.Add(package);
        await context.SaveChangesAsync();
        return package;
    }

    #endregion

    // Note: Search tests are not included because EF.Functions.ILike is not supported
    // by the InMemory provider. Search functionality uses PostgreSQL ILike and should
    // be tested via integration tests against a real database.

    #region GetOrCreate Tests

    [Fact]
    public async Task GetOrCreate_CreatesNewTag_WhenNotExists()
    {
        var tag = await _service.GetOrCreate("Новий тег");

        tag.Should().NotBeNull();
        tag.Name.Should().Be("Новий тег");
        tag.Id.Should().BeGreaterThan(0);

        // Verify it's persisted
        using var context = _dbFactory.CreateDbContext();
        var dbTag = await context.Tags.FindAsync(tag.Id);
        dbTag.Should().NotBeNull();
        dbTag!.Name.Should().Be("Новий тег");
    }

    [Fact]
    public async Task GetOrCreate_TrimsWhitespace()
    {
        var tag = await _service.GetOrCreate("  тег  ");

        tag.Name.Should().Be("тег");
    }

    [Fact]
    public async Task GetOrCreate_ThrowsForEmptyName()
    {
        var act = async () => await _service.GetOrCreate("");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*порожньою*");
    }

    [Fact]
    public async Task GetOrCreate_ThrowsForWhitespaceName()
    {
        var act = async () => await _service.GetOrCreate("   ");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*порожньою*");
    }

    #endregion

    #region TryDeleteIfOrphaned Tests

    [Fact]
    public async Task TryDeleteIfOrphaned_DeletesTag_WhenNoPackagesReference()
    {
        var tag = await CreateTag("Orphan");

        var deleted = await _service.TryDeleteIfOrphaned(tag.Id);

        deleted.Should().BeTrue();

        using var context = _dbFactory.CreateDbContext();
        var dbTag = await context.Tags.FindAsync(tag.Id);
        dbTag.Should().BeNull();
    }

    [Fact]
    public async Task TryDeleteIfOrphaned_KeepsTag_WhenPackagesReference()
    {
        await CreatePackageWithTags("Package 1", "Кубок");

        using var context = _dbFactory.CreateDbContext();
        var tag = await context.Tags.FirstAsync(t => t.Name == "Кубок");

        var deleted = await _service.TryDeleteIfOrphaned(tag.Id);

        deleted.Should().BeFalse();

        // Tag still exists
        using var checkContext = _dbFactory.CreateDbContext();
        var stillExists = await checkContext.Tags.FindAsync(tag.Id);
        stillExists.Should().NotBeNull();
    }

    [Fact]
    public async Task TryDeleteIfOrphaned_ReturnsFalse_WhenTagNotFound()
    {
        var deleted = await _service.TryDeleteIfOrphaned(999);

        deleted.Should().BeFalse();
    }

    [Fact]
    public async Task TryDeleteIfOrphaned_DeletesTag_AfterLastPackageRemovesIt()
    {
        // Create a package with the tag
        var package = await CreatePackageWithTags("Only Package", "унікальний");

        // Remove the tag from the package
        using (var context = _dbFactory.CreateDbContext())
        {
            var dbPackage = await context.Packages
                .Include(p => p.Tags)
                .FirstAsync(p => p.Id == package.Id);
            dbPackage.Tags.Clear();
            await context.SaveChangesAsync();
        }

        // Now the tag is orphaned
        using (var context = _dbFactory.CreateDbContext())
        {
            var tag = await context.Tags.FirstAsync(t => t.Name == "унікальний");
            var deleted = await _service.TryDeleteIfOrphaned(tag.Id);
            deleted.Should().BeTrue();
        }

        // Tag no longer exists
        using (var checkContext = _dbFactory.CreateDbContext())
        {
            var tags = await checkContext.Tags.ToListAsync();
            tags.Should().BeEmpty();
        }
    }

    #endregion

    #region GetPopular Tests

    [Fact]
    public async Task GetPopular_ReturnsTagsOrderedByPackageCount()
    {
        // "popular" used in 2 packages, "medium" in 1, "orphan" in 0
        await CreatePackageWithTags("Pkg1", "popular", "medium");
        await CreatePackageWithTags("Pkg2", "popular");
        await CreateTag("orphan");

        var result = await _service.GetPopular(10);

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("popular");
        result[1].Name.Should().Be("medium");
    }

    [Fact]
    public async Task GetPopular_ExcludesOrphanedTags()
    {
        await CreateTag("orphan");
        await CreatePackageWithTags("Pkg1", "used");

        var result = await _service.GetPopular(10);

        result.Should().ContainSingle().Which.Name.Should().Be("used");
    }

    [Fact]
    public async Task GetPopular_RespectsLimit()
    {
        await CreatePackageWithTags("Pkg1", "a", "b", "c");

        var result = await _service.GetPopular(2);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPopular_ReturnsEmpty_WhenNoTagsExist()
    {
        var result = await _service.GetPopular(10);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPopular_OrdersByNameForSameCount()
    {
        // All tags have 1 package each – should be ordered alphabetically
        await CreatePackageWithTags("Pkg1", "banana");
        await CreatePackageWithTags("Pkg2", "apple");
        await CreatePackageWithTags("Pkg3", "cherry");

        var result = await _service.GetPopular(10);

        result.Select(t => t.Name).Should().BeInAscendingOrder();
    }

    #endregion

    #region Context Overload Tests

    [Fact]
    public async Task GetOrCreate_WithContext_CreatesTag()
    {
        using var context = _dbFactory.CreateDbContext();

        var tag = await _service.GetOrCreate(context, "Контекстний тег");

        tag.Should().NotBeNull();
        tag.Name.Should().Be("Контекстний тег");
    }

    [Fact]
    public async Task TryDeleteIfOrphaned_WithContext_DeletesOrphanedTag()
    {
        var tag = await CreateTag("Контекстний orphan");

        using var context = _dbFactory.CreateDbContext();
        var deleted = await _service.TryDeleteIfOrphaned(context, tag.Id);

        deleted.Should().BeTrue();
    }

    #endregion

    #region GetPopularPublished Tests

    [Fact]
    public async Task GetPopularPublished_ReturnsTagsOrderedByUsageCount()
    {
        await CreatePackageWithTags("Pkg1", "рідкісний");
        await CreatePackageWithTags("Pkg2", "популярний");
        await CreatePackageWithTags("Pkg3", "популярний");
        await CreatePackageWithTags("Pkg4", "популярний");
        await CreatePackageWithTags("Pkg5", "середній");
        await CreatePackageWithTags("Pkg6", "середній");

        var result = await _service.GetPopularPublished(10);

        result.Should().HaveCount(3);
        result[0].Name.Should().Be("популярний");
        result[1].Name.Should().Be("середній");
        result[2].Name.Should().Be("рідкісний");
    }

    [Fact]
    public async Task GetPopularPublished_OnlyCountsPublishedPackages()
    {
        var tag = await CreateTag("тестовий");

        await CreatePackageWithTags("Published", PackageStatus.Published, "тестовий");
        await CreatePackageWithTags("Draft", PackageStatus.Draft, "тестовий");

        var result = await _service.GetPopularPublished(10);

        result.Should().ContainSingle();
        result[0].Name.Should().Be("тестовий");
    }

    [Fact]
    public async Task GetPopularPublished_ExcludesTagsWithOnlyDraftPackages()
    {
        await CreatePackageWithTags("Draft Only", PackageStatus.Draft, "чернетковий");

        var result = await _service.GetPopularPublished(10);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPopularPublished_RespectsCountLimit()
    {
        for (int i = 0; i < 15; i++)
        {
            await CreatePackageWithTags($"Pkg{i}", $"tag{i:D2}");
        }

        var result = await _service.GetPopularPublished(10);

        result.Should().HaveCount(10);
    }

    [Fact]
    public async Task GetPopularPublished_IsCached()
    {
        await CreatePackageWithTags("Cached Package", "cached");

        // First call populates cache
        var result1 = await _service.GetPopularPublished(10);
        result1.Should().ContainSingle();

        // Add more data - should not appear due to caching
        await CreatePackageWithTags("New Package", "new");

        var result2 = await _service.GetPopularPublished(10);
        result2.Should().HaveCount(1); // Still cached
    }

    [Fact]
    public async Task InvalidatePopularTagsCache_ClearsCachedTags()
    {
        await CreatePackageWithTags("Package", "invalidated");

        // Populate cache
        var result1 = await _service.GetPopularPublished(10);
        result1.Should().ContainSingle();

        // Add more data
        await CreatePackageWithTags("Package 2", "новий");

        // Invalidate
        _service.InvalidatePopularTagsCache();

        // Should return fresh data
        var result2 = await _service.GetPopularPublished(10);
        result2.Should().HaveCount(2);
    }

    #endregion
}
