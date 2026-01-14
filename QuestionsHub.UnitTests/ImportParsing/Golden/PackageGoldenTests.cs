using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QuestionsHub.Blazor.Infrastructure.Import;
using QuestionsHub.UnitTests.Assertions;
using QuestionsHub.UnitTests.TestInfrastructure;
using Xunit;

namespace QuestionsHub.UnitTests.ImportParsing.Golden;

/// <summary>
/// End-to-end golden tests: DOCX -> Extract -> Parse -> Compare to expected JSON.
/// These tests verify the complete import pipeline produces expected results.
/// </summary>
public class PackageGoldenTests : IDisposable
{
    private readonly DocxExtractor _extractor;
    private readonly PackageParser _parser;
    private readonly string _tempDirectory;

    public PackageGoldenTests()
    {
        _extractor = new DocxExtractor(NullLogger<DocxExtractor>.Instance);
        _parser = new PackageParser(NullLogger<PackageParser>.Instance);
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"QuestionsHub_GoldenTests_{Guid.NewGuid():N}");
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
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Tests all packages that have corresponding expected JSON files.
    /// </summary>
    [Theory]
    [MemberData(nameof(GetGoldenTestData))]
    public async Task Import_Package_MatchesExpected(string packageFileName, string expectedFileName)
    {
        if (packageFileName == "__NO_FILES__") return;

        // Arrange
        var packagePath = TestFiles.GetPackagePath(packageFileName);
        var expectedPath = TestFiles.GetExpectedPath(expectedFileName);

        var jobId = Guid.NewGuid();
        var assetsPath = Path.Combine(_tempDirectory, jobId.ToString("N"));

        // Act - Extract
        var extraction = await _extractor.Extract(packagePath, jobId, assetsPath, CancellationToken.None);

        // Act - Parse
        var result = _parser.Parse(extraction.Blocks, extraction.Assets);

        // Assert
        result.ShouldMatchExpected(expectedPath);
    }

    /// <summary>
    /// Tests that all available packages produce valid structure (even without expected files).
    /// </summary>
    [Theory]
    [MemberData(nameof(GetAllPackageData))]
    public async Task Import_Package_ProducesValidStructure(string packageFileName)
    {
        if (packageFileName == "__NO_FILES__") return;

        // Arrange
        var packagePath = TestFiles.GetPackagePath(packageFileName);

        var jobId = Guid.NewGuid();
        var assetsPath = Path.Combine(_tempDirectory, jobId.ToString("N"));

        // Act
        var extraction = await _extractor.Extract(packagePath, jobId, assetsPath, CancellationToken.None);
        var result = _parser.Parse(extraction.Blocks, extraction.Assets);

        // Assert - basic structural validation
        result.ShouldBeValidPackage();
    }

    /// <summary>
    /// Helper to generate expected JSON file for a package.
    /// Use this when creating new golden test cases.
    /// </summary>
    [Theory(Skip = "Manual test for generating expected files")]
    [InlineData("simple_package.docx")]
    public async Task GenerateExpectedFile(string packageFileName)
    {
        // Arrange
        var packagePath = TestFiles.GetPackagePath(packageFileName);
        var expectedPath = TestFiles.GetExpectedPathForPackage(packageFileName);

        var jobId = Guid.NewGuid();
        var assetsPath = Path.Combine(_tempDirectory, jobId.ToString("N"));

        // Act
        var extraction = await _extractor.Extract(packagePath, jobId, assetsPath, CancellationToken.None);
        var result = _parser.Parse(extraction.Blocks, extraction.Assets);

        // Save as expected
        Directory.CreateDirectory(Path.GetDirectoryName(expectedPath)!);
        JsonComparer.SaveAsExpected(result, expectedPath);

        // Output path for manual verification
        Console.WriteLine($"Generated expected file: {expectedPath}");
    }

    public static IEnumerable<object[]> GetGoldenTestData()
    {
        var pairs = TestFiles.GetGoldenTestPairs().ToList();
        if (pairs.Count == 0)
        {
            yield return new object[] { "__NO_FILES__", "__NO_FILES__" };
        }
        else
        {
            foreach (var (packageFile, expectedFile) in pairs)
            {
                yield return new object[] { packageFile, expectedFile };
            }
        }
    }

    public static IEnumerable<object[]> GetAllPackageData()
    {
        var files = TestFiles.GetAllPackageFiles().ToList();
        if (files.Count == 0)
        {
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

