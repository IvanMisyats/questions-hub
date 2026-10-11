using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.IntegrationTests.Infrastructure;
using Xunit;

namespace QuestionsHub.IntegrationTests;

/// <summary>Heart buttons in the server-rendered pages: state per viewer, on every surface.</summary>
[Collection(IntegrationCollection.Name)]
public sealed partial class FavoritesUiTests(PostgresFixture database)
{
    private QuestionsHubAppFactory CreateFactory()
    {
        database.SkipIfUnavailable();
        return new QuestionsHubAppFactory(database);
    }

    /// <summary>Heart buttons in <paramref name="html"/>: (kind, id, filled, login-only).</summary>
    private static List<(string Kind, int Id, bool IsFavorite, bool IsAnonymous)> Hearts(string html) =>
        HeartButton().Matches(html)
            .Select(m => (
                m.Groups["kind"].Value,
                int.Parse(m.Groups["id"].Value, CultureInfo.InvariantCulture),
                m.Groups["class"].Value.Split(' ').Contains("is-fav"),
                m.Value.Contains("data-fav-anon=\"true\"")))
            .ToList();

    [SkippableFact]
    public async Task PackagePage_Anonymous_GetsLoginHeartsOnEveryItem()
    {
        await using var factory = CreateFactory();
        var package = await TestData.CreatePackage(factory, ownerId: null, status: PackageStatus.Published);
        using var browser = TestBrowser.Create(factory);

        var hearts = Hearts(await browser.GetStringAsync($"/package/{package.Id}"));

        hearts.Select(h => h.Kind).Should().BeEquivalentTo(["package", "tour", "tour", "block", "question", "question", "question"]);
        hearts.Should().OnlyContain(h => h.IsAnonymous && !h.IsFavorite);
    }

    [SkippableFact]
    public async Task SignedIn_SeesTheirFavoritesFilled_OnThePackageQuestionAndSearchPages()
    {
        await using var factory = CreateFactory();
        var package = await TestData.CreatePackage(factory, ownerId: null, status: PackageStatus.Published);
        var tour2 = package.Tours.Single(t => t.Blocks.Count > 0);
        var block = tour2.Blocks.Single();
        var question = tour2.Questions.Single();
        var other = package.Tours.Single(t => t.Blocks.Count == 0).Questions.First();

        // A word only this question contains, so the search finds it among other tests' packages
        var word = "фаворит" + new string(Guid.NewGuid().ToString("N")[..8].Select(c => (char)('а' + c % 26)).ToArray());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            await db.Questions.Where(q => q.Id == question.Id).ExecuteUpdateAsync(s => s.SetProperty(q => q.Text, $"Питання про {word}"));
        }

        var user = await TestBrowser.CreateUser(factory, role: null);
        using var browser = TestBrowser.Create(factory);
        await TestBrowser.Login(browser, user.Email, user.Password);
        var token = await TestBrowser.GetAntiforgeryToken(browser, $"/package/{package.Id}");
        foreach (var url in new[]
                 {
                     $"/api/favorites/package/{package.Id}", $"/api/favorites/tour/{tour2.Id}",
                     $"/api/favorites/block/{block.Id}", $"/api/favorites/question/{question.Id}"
                 })
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, url);
            request.Headers.Add("RequestVerificationToken", token);
            (await browser.SendAsync(request)).EnsureSuccessStatusCode();
        }

        var packagePage = Hearts(await browser.GetStringAsync($"/package/{package.Id}"));
        packagePage.Should().OnlyContain(h => !h.IsAnonymous);
        packagePage.Where(h => h.IsFavorite).Select(h => (h.Kind, h.Id)).Should().BeEquivalentTo(new[]
        {
            ("package", package.Id), ("tour", tour2.Id), ("block", block.Id), ("question", question.Id)
        });
        packagePage.Should().Contain(h => h.Kind == "question" && h.Id == other.Id && !h.IsFavorite);

        Hearts(await browser.GetStringAsync($"/question/{question.Id}"))
            .Should().ContainSingle().Which.Should().Be(("question", question.Id, true, false));
        Hearts(await browser.GetStringAsync($"/question/{other.Id}"))
            .Should().ContainSingle().Which.Should().Be(("question", other.Id, false, false));

        Hearts(await browser.GetStringAsync($"/search/{Uri.EscapeDataString(word)}"))
            .Should().ContainSingle().Which.Should().Be(("question", question.Id, true, false));
    }

    [SkippableFact]
    public async Task FavoritesPage_Anonymous_RedirectsToLogin_AndHasNoNavLink()
    {
        await using var factory = CreateFactory();
        using var browser = TestBrowser.Create(factory);

        using var response = await browser.GetAsync("/favorites");
        var home = await browser.GetStringAsync("/");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Account/Login");
        home.Should().NotContain("href=\"favorites\"");
    }

    [SkippableFact]
    public async Task FavoritesPage_ListsEveryKind_WithCountsAndFilters_AndHidesWhatTheUserCanNoLongerSee()
    {
        await using var factory = CreateFactory();
        var title = $"Пакет обраного {Guid.NewGuid():N}";
        var package = await TestData.CreatePackage(factory, ownerId: null, status: PackageStatus.Published, title: title);
        var tour2 = package.Tours.Single(t => t.Blocks.Count > 0);
        var block = tour2.Blocks.Single();
        var question = tour2.Questions.Single();
        var adminId = await TestData.AdminId(factory);
        var hidden = await TestData.CreatePackage(factory, ownerId: adminId, status: PackageStatus.Published);
        var hiddenQuestion = hidden.Tours.SelectMany(t => t.Questions).First();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            await db.Packages.Where(p => p.Id == package.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.PlayedFrom, new DateOnly(2026, 3, 14)));
            var adult = await db.Tags.FirstOrDefaultAsync(t => t.Name == "18+") ?? db.Tags.Add(new Tag { Name = "18+" }).Entity;
            var tracked = await db.Packages.Include(p => p.Tags).SingleAsync(p => p.Id == package.Id);
            tracked.Tags.Add(adult);
            await db.SaveChangesAsync();
        }

        var user = await TestBrowser.CreateUser(factory, role: null);
        using var browser = TestBrowser.Create(factory);
        await TestBrowser.Login(browser, user.Email, user.Password);
        var token = await TestBrowser.GetAntiforgeryToken(browser);
        foreach (var url in new[]
                 {
                     $"/api/favorites/package/{package.Id}", $"/api/favorites/tour/{tour2.Id}",
                     $"/api/favorites/block/{block.Id}", $"/api/favorites/question/{question.Id}",
                     $"/api/favorites/question/{hiddenQuestion.Id}"
                 })
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, url);
            request.Headers.Add("RequestVerificationToken", token);
            (await browser.SendAsync(request)).EnsureSuccessStatusCode();
        }

        // The admin unpublishes the other package: its favorite is kept but no longer shown or counted
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            await db.Packages.Where(p => p.Id == hidden.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PackageStatus.Draft));
        }

        var questions = await TestBrowser.GetDecodedHtml(browser, "/favorites");
        foreach (var (tab, count) in new[] { ("Запитання", 1), ("Тури й теми", 1), ("Блоки", 1), ("Пакети", 1) })
            questions.Should().MatchRegex($@"{tab}\s*<span class=""badge[^""]*"">{count}</span>", tab);
        Hearts(questions).Should().ContainSingle().Which.Should().Be(("question", question.Id, true, false));
        questions.Should().Contain("adult-blur-wrapper blurred", "18+ stays blurred on the favorites page");
        questions.Should().Contain("href=\"favorites\"", "signed-in users get the nav link");

        var tours = await TestBrowser.GetDecodedHtml(browser, "/favorites?tab=tours");
        tours.Should().Contain($"/package/{package.Id}#tour-{tour2.Id}").And.Contain("Тур 2");
        Hearts(tours).Should().ContainSingle().Which.Should().Be(("tour", tour2.Id, true, false));

        var blocks = await TestBrowser.GetDecodedHtml(browser, "/favorites?tab=blocks");
        blocks.Should().Contain($"/package/{package.Id}#block-{block.Id}").And.Contain("Блок А");

        var packages = await TestBrowser.GetDecodedHtml(browser, "/favorites?tab=packages");
        packages.Should().Contain(title);

        (await TestBrowser.GetDecodedHtml(browser, "/favorites?type=shvager")).Should().Contain("За цими фільтрами нічого немає.");
        var in2026 = await TestBrowser.GetDecodedHtml(browser, "/favorites?year=2026");
        Hearts(in2026).Should().ContainSingle().Which.Id.Should().Be(question.Id);
        in2026.Should().Contain("<option value=\"2026\" selected");
        (await TestBrowser.GetDecodedHtml(browser, "/favorites?year=2020")).Should().Contain("За цими фільтрами нічого немає.");

        // Hand-edited URLs: a junk year is ignored, a page past the end says so instead of "nothing here yet"
        Hearts(await TestBrowser.GetDecodedHtml(browser, "/favorites?year=abc")).Should().ContainSingle();
        (await TestBrowser.GetDecodedHtml(browser, "/favorites/9")).Should().Contain("Цієї сторінки немає.");
    }

    [GeneratedRegex("""<button[^>]*class="(?<class>[^"]*\bfav-btn\b[^"]*)"[^>]*data-fav-kind="(?<kind>\w+)" data-fav-id="(?<id>\d+)"[^>]*>""")]
    private static partial Regex HeartButton();
}
