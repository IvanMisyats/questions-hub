using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace QuestionsHub.UnitTests;

/// <summary>
/// Pages are static server-rendered HTML and work without Blazor's script (Safari before 16.4 cannot
/// parse it). App.razor loads the script only on pages marked <c>@rendermode</c>; a static page with an
/// interactive part asks for it with <c>&lt;BlazorScript /&gt;</c>.
/// </summary>
public partial class StaticRenderingTests
{
    /// <summary>A streamed page shows its placeholder until Blazor's script applies the real content.</summary>
    [Fact]
    public void NoPageStreamsItsContent()
    {
        AppSourceFiles.All(".razor", ".cs")
            .Where(f => f.Text.Contains("StreamRendering", StringComparison.Ordinal))
            .Select(f => f.Path)
            .Should().BeEmpty();
    }

    /// <summary>An interactive component on a static page stays dead unless the page loads the script.</summary>
    [Fact]
    public void InteractiveIslandOnAStaticPage_RendersBlazorScript()
    {
        var islands = AppSourceFiles.All(".razor")
            .Where(f => IslandRenderMode().IsMatch(f.Text) && !PageRenderMode().IsMatch(f.Text))
            .ToList();

        islands.Select(f => f.Path).Should().Contain(p => p.EndsWith("Profile.razor", StringComparison.Ordinal),
            "the scan must find the agent-token panel on the profile");
        islands.Where(f => !f.Text.Contains("<BlazorScript />", StringComparison.Ordinal))
            .Select(f => f.Path)
            .Should().BeEmpty();
    }

    /// <summary><c>&lt;Component @rendermode="…" /&gt;</c>: a render mode set where a component is used.</summary>
    [GeneratedRegex(@"\s@rendermode=")]
    private static partial Regex IslandRenderMode();

    /// <summary>The <c>@rendermode …</c> directive of an interactive page.</summary>
    [GeneratedRegex(@"(?m)^﻿?@rendermode\s")]
    private static partial Regex PageRenderMode();
}
