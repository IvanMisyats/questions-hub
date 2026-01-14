using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QuestionsHub.Blazor.Infrastructure.Import;
using QuestionsHub.UnitTests.TestInfrastructure;
using Xunit;

namespace QuestionsHub.UnitTests.ImportParsing.DocxExtraction;

/// <summary>
/// Tests for DocxExtractor using real DOCX test files.
/// These tests verify that DOCX content is correctly extracted into DocBlocks.
/// </summary>
public class DocxExtractorTests : IDisposable
{
    private readonly DocxExtractor _extractor;
    private readonly string _tempDirectory;

    public DocxExtractorTests()
    {
        _extractor = new DocxExtractor(NullLogger<DocxExtractor>.Instance);
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"QuestionsHub_Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }

    [Theory]
    [MemberData(nameof(GetAvailablePackageFiles))]
    public async Task Extract_RealPackage_ReturnsBlocks(string packageFileName)
    {
        // Skip if this is the placeholder "no files" marker
        if (packageFileName == "__NO_FILES__")
        {
            return; // Gracefully skip
        }

        // Arrange
        var packagePath = TestFiles.GetPackagePath(packageFileName);

        var jobId = Guid.NewGuid();
        var assetsPath = Path.Combine(_tempDirectory, jobId.ToString("N"));

        // Act
        var result = await _extractor.Extract(packagePath, jobId, assetsPath, CancellationToken.None);

        // Assert
        result.Blocks.Should().NotBeEmpty("DOCX should contain text blocks");
        result.Warnings.Should().NotBeNull();
    }

    [Theory]
    [MemberData(nameof(GetAvailablePackageFiles))]
    public async Task Extract_RealPackage_BlocksHaveText(string packageFileName)
    {
        if (packageFileName == "__NO_FILES__") return;

        // Arrange
        var packagePath = TestFiles.GetPackagePath(packageFileName);

        var jobId = Guid.NewGuid();
        var assetsPath = Path.Combine(_tempDirectory, jobId.ToString("N"));

        // Act
        var result = await _extractor.Extract(packagePath, jobId, assetsPath, CancellationToken.None);

        // Assert
        result.Blocks.Should().AllSatisfy(block =>
        {
            // Block should have text or assets
            var hasContent = !string.IsNullOrWhiteSpace(block.Text) || block.Assets.Count > 0;
            hasContent.Should().BeTrue("block should have text or assets");
        });
    }

    [Theory]
    [MemberData(nameof(GetAvailablePackageFiles))]
    public async Task Extract_RealPackage_ContainsTourMarkers(string packageFileName)
    {
        if (packageFileName == "__NO_FILES__") return;

        // Arrange
        var packagePath = TestFiles.GetPackagePath(packageFileName);

        var jobId = Guid.NewGuid();
        var assetsPath = Path.Combine(_tempDirectory, jobId.ToString("N"));

        // Act
        var result = await _extractor.Extract(packagePath, jobId, assetsPath, CancellationToken.None);

        // Assert - at least one block should match tour pattern
        var tourPattern = ParserPatterns.TourStart();
        var tourPatternDashed = ParserPatterns.TourStartDashed();

        var hasTourBlock = result.Blocks.Any(b =>
            tourPattern.IsMatch(b.Text) || tourPatternDashed.IsMatch(b.Text));

        hasTourBlock.Should().BeTrue("package should contain at least one tour marker");
    }

    [Theory]
    [MemberData(nameof(GetAvailablePackageFiles))]
    public async Task Extract_RealPackage_ContainsAnswerMarkers(string packageFileName)
    {
        if (packageFileName == "__NO_FILES__") return;

        // Arrange
        var packagePath = TestFiles.GetPackagePath(packageFileName);

        var jobId = Guid.NewGuid();
        var assetsPath = Path.Combine(_tempDirectory, jobId.ToString("N"));

        // Act
        var result = await _extractor.Extract(packagePath, jobId, assetsPath, CancellationToken.None);

        // Assert
        var answerPattern = ParserPatterns.AnswerLabel();
        var hasAnswerBlock = result.Blocks.Any(b => answerPattern.IsMatch(b.Text));

        hasAnswerBlock.Should().BeTrue("package should contain at least one answer marker");
    }

    [Fact]
    public async Task Extract_NonExistentFile_ThrowsException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDirectory, "non_existent.docx");
        var jobId = Guid.NewGuid();
        var assetsPath = Path.Combine(_tempDirectory, jobId.ToString("N"));

        // Act
        var act = () => _extractor.Extract(nonExistentPath, jobId, assetsPath, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact(Skip = "Requires test DOCX files - add files to TestData/Packages/")]
    public async Task Extract_CancellationRequested_ThrowsOperationCanceled()
    {
        // Arrange
        var packageFiles = TestFiles.GetAllPackageFiles().ToList();
        if (packageFiles.Count == 0)
            return; // No files to test with

        var packagePath = TestFiles.GetPackagePath(packageFiles[0]);
        var jobId = Guid.NewGuid();
        var assetsPath = Path.Combine(_tempDirectory, jobId.ToString("N"));
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = () => _extractor.Extract(packagePath, jobId, assetsPath, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    public static IEnumerable<object[]> GetAvailablePackageFiles()
    {
        var files = TestFiles.GetAllPackageFiles().ToList();
        if (files.Count == 0)
        {
            // Return placeholder that tests will skip
            yield return new object[] { "__NO_FILES__" };
        }
        else
        {
            foreach (var file in files)
            {
                yield return new object[] { file };
            }
        }
    }
}

