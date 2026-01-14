namespace QuestionsHub.UnitTests.TestInfrastructure;

/// <summary>
/// Helper for resolving paths to test data files.
/// </summary>
public static class TestFiles
{
    private static readonly string TestDataRoot = Path.Combine(
        AppContext.BaseDirectory, "TestData");

    public static string PackagesDir => Path.Combine(TestDataRoot, "Packages");
    public static string ExpectedDir => Path.Combine(TestDataRoot, "Expected");

    public static string GetPackagePath(string fileName) =>
        Path.Combine(PackagesDir, fileName);

    public static string GetExpectedPath(string fileName) =>
        Path.Combine(ExpectedDir, fileName);

    /// <summary>
    /// Gets expected JSON path for a given package file.
    /// Example: "simple_package.docx" -> "Expected/simple_package.json"
    /// </summary>
    public static string GetExpectedPathForPackage(string packageFileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(packageFileName);
        return Path.Combine(ExpectedDir, $"{baseName}.json");
    }

    /// <summary>
    /// Lists all .docx files in the Packages directory.
    /// </summary>
    public static IEnumerable<string> GetAllPackageFiles()
    {
        if (!Directory.Exists(PackagesDir))
            return [];

        return Directory.GetFiles(PackagesDir, "*.docx")
            .Select(Path.GetFileName)!;
    }

    /// <summary>
    /// Gets pairs of (package, expected) files for golden tests.
    /// Only returns pairs where both files exist.
    /// </summary>
    public static IEnumerable<(string PackageFile, string ExpectedFile)> GetGoldenTestPairs()
    {
        foreach (var packageFile in GetAllPackageFiles())
        {
            var expectedPath = GetExpectedPathForPackage(packageFile);
            if (File.Exists(expectedPath))
            {
                yield return (packageFile, Path.GetFileName(expectedPath));
            }
        }
    }
}

