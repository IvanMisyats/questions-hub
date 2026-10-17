using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Media;
using QuestionsHub.UnitTests.TestInfrastructure;
using Xunit;

namespace QuestionsHub.UnitTests;

/// <summary>MediaService.DeleteIfUnreferenced: a file several questions share stays until the last one lets go.</summary>
public sealed class MediaServiceTests : IDisposable
{
    private readonly string _uploads = Path.Combine(Path.GetTempPath(), "qh-media-" + Guid.NewGuid().ToString("N"));
    private readonly InMemoryDbContextFactory _db = new();

    public void Dispose()
    {
        using (var context = _db.CreateDbContext())
            context.Database.EnsureDeleted();
        if (Directory.Exists(_uploads))
            Directory.Delete(_uploads, recursive: true);
    }

    [Fact]
    public async Task DeleteIfUnreferenced_KeepsAFileAnotherQuestionUses()
    {
        var media = new MediaService(Options.Create(new MediaUploadOptions { UploadsPath = _uploads, HandoutsFolder = "handouts" }),
            _db, NullLogger<MediaService>.Instance);
        var path = Path.Combine(_uploads, "handouts", "shared.png");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        int otherId;
        await using (var context = _db.CreateDbContext())
        {
            var tour = new Tour { Number = "1", Questions =
            [
                new Question { Number = "1", Text = "A", Answer = "A" },
                new Question { Number = "2", Text = "B", Answer = "B", CommentAttachmentUrl = "/media/shared.png" }
            ] };
            context.Packages.Add(new Package { Title = "P", Tours = [tour] });
            await context.SaveChangesAsync();
            otherId = tour.Questions[1].Id;
        }

        // The first question let go of the file (its URL is already saved as null); the second still uses it.
        (await media.DeleteIfUnreferenced("/media/shared.png")).Should().BeFalse();
        File.Exists(path).Should().BeTrue();

        await using (var context = _db.CreateDbContext())
        {
            (await context.Questions.FindAsync(otherId))!.CommentAttachmentUrl = null;
            await context.SaveChangesAsync();
        }
        (await media.DeleteIfUnreferenced("/media/shared.png")).Should().BeTrue();
        File.Exists(path).Should().BeFalse();
    }
}
