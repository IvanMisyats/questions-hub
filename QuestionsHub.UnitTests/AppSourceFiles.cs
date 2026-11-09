namespace QuestionsHub.UnitTests;

/// <summary>The QuestionsHub.Blazor source files, for tests that guard a convention across the whole app.</summary>
internal static class AppSourceFiles
{
    /// <summary>Each <c>.razor</c>/<c>.cs</c> file outside bin/obj, with its path relative to the project folder.</summary>
    public static IEnumerable<(string Path, string Text)> All(params string[] extensions)
    {
        var appRoot = FindAppRoot();
        return Directory.EnumerateFiles(appRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Any(e => f.EndsWith(e, StringComparison.Ordinal)))
            .Select(f => Path.GetRelativePath(appRoot, f))
            .Where(f => !IsBuildOutput(f))
            .Select(f => (f, File.ReadAllText(Path.Combine(appRoot, f))));
    }

    private static bool IsBuildOutput(string relativePath) =>
        relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) is var parts
        && (parts.Contains("bin") || parts.Contains("obj"));

    /// <summary>The QuestionsHub.Blazor project folder, found by walking up from the test binaries.</summary>
    private static string FindAppRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "QuestionsHub.Blazor");
            if (File.Exists(Path.Combine(candidate, "QuestionsHub.Blazor.csproj")))
                return candidate;
        }

        throw new InvalidOperationException("QuestionsHub.Blazor project folder not found above " + AppContext.BaseDirectory);
    }
}
