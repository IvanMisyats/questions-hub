using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace QuestionsHub.UnitTests;

/// <summary>
/// App.razor starts Blazor with enhanced navigation off and the site has no interactive router, so a
/// <c>NavigationManager.NavigateTo</c> without <c>forceLoad: true</c> on an interactive page only changes the
/// address bar: nothing re-renders (e.g. "create package" stayed on the list). In static pages forceLoad is
/// harmless (it is a redirect either way), so every call passes it.
/// </summary>
public partial class NavigateToTests
{
    [Fact]
    public void EveryNavigateTo_PassesForceLoad()
    {
        var appRoot = FindAppRoot();

        var calls = Directory.EnumerateFiles(appRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
            .Where(f => !IsBuildOutput(Path.GetRelativePath(appRoot, f)))
            .SelectMany(f => NavigateToCall().Matches(File.ReadAllText(f))
                .Select(m => $"{Path.GetRelativePath(appRoot, f)}: {m.Value}"))
            .ToList();

        calls.Should().NotBeEmpty("the scan must find the app's NavigateTo calls");
        calls.Where(c => !c.Contains("forceLoad: true", StringComparison.Ordinal)).Should().BeEmpty();
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

    /// <summary><c>NavigateTo(…)</c> with its whole argument list (balanced parentheses).</summary>
    [GeneratedRegex(@"\bNavigateTo\((?>[^()]+|\((?<depth>)|\)(?<-depth>))*(?(depth)(?!))\)")]
    private static partial Regex NavigateToCall();
}
