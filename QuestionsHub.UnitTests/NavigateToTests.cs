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
        var calls = AppSourceFiles.All(".razor", ".cs")
            .SelectMany(f => NavigateToCall().Matches(f.Text).Select(m => $"{f.Path}: {m.Value}"))
            .ToList();

        calls.Should().NotBeEmpty("the scan must find the app's NavigateTo calls");
        calls.Where(c => !c.Contains("forceLoad: true", StringComparison.Ordinal)).Should().BeEmpty();
    }

    /// <summary><c>NavigateTo(…)</c> with its whole argument list (balanced parentheses).</summary>
    [GeneratedRegex(@"\bNavigateTo\((?>[^()]+|\((?<depth>)|\)(?<-depth>))*(?(depth)(?!))\)")]
    private static partial Regex NavigateToCall();
}
