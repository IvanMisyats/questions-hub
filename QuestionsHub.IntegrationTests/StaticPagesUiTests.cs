using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.IntegrationTests.Infrastructure;
using Xunit;

namespace QuestionsHub.IntegrationTests;

/// <summary>
/// Public pages are plain server-rendered HTML: they never load Blazor's script (which older Safari
/// cannot parse), and what used to need a live circuit is links, GET forms and POST forms.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed partial class StaticPagesUiTests(PostgresFixture database)
{
    /// <summary>The script tag (the import map in the head lists the module too, but loads nothing).</summary>
    private const string BlazorScript = "<script src=\"_framework/blazor.web.js\"";

    private QuestionsHubAppFactory CreateFactory()
    {
        database.SkipIfUnavailable();
        return new QuestionsHubAppFactory(database);
    }

    [SkippableFact]
    public async Task PublicPages_Anonymous_AreCompleteWithoutBlazorScript()
    {
        await using var factory = CreateFactory();
        var package = await TestData.CreatePackage(factory, ownerId: null, status: PackageStatus.Published);
        var question = package.Tours.First().Questions.First();
        var authorId = question.Authors.Single().Id;
        using var browser = TestBrowser.Create(factory);

        foreach (var url in new[]
                 {
                     "/", $"/package/{package.Id}", $"/question/{question.Id}", $"/editor/{authorId}",
                     $"/editor/{authorId}?view=questions", $"/package/{package.Id}/results", "/editors",
                     "/search/%D0%BF%D0%B8%D1%82%D0%B0%D0%BD%D0%BD%D1%8F"
                 })
        {
            var html = await TestBrowser.GetDecodedHtml(browser, url);

            html.Should().NotContain(BlazorScript, url)
                .And.NotContain("<blazor-ssr", $"{url} must not stream its content after a placeholder")
                .And.NotContain("Завантаження...", url);
        }
    }

    [SkippableFact]
    public async Task BlazorScript_IsLoadedForEditors_WithoutEnhancedNavigation()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        using var browser = TestBrowser.Create(factory);
        await TestBrowser.Login(browser, editor.Email, editor.Password);

        var html = await browser.GetStringAsync("/");

        html.Should().Contain(BlazorScript).And.Contain("disableDomPreservation: true");
    }

    [SkippableFact]
    public async Task Profile_PlainUser_SavesCityAndTeam_WithoutBlazorScript()
    {
        await using var factory = CreateFactory();
        var user = await TestBrowser.CreateUser(factory, role: null);
        using var browser = TestBrowser.Create(factory);
        await TestBrowser.Login(browser, user.Email, user.Password);

        var page = await TestBrowser.GetDecodedHtml(browser, "/Account/Profile");
        page.Should().NotContain(BlazorScript).And.Contain("name=\"Model.City\"").And.Contain("name=\"Model.Team\"");

        var html = await Post(browser, "/Account/Profile", "ProfileForm", new()
        {
            ["Model.City"] = "Житомир",
            ["Model.Team"] = "Ластівки",
        });

        html.Should().Contain("Профіль успішно оновлено!")
            .And.Contain("value=\"Олена\"", "the read-only name is shown again after the submit");
        using var scope = factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>()
            .Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        saved.City.Should().Be("Житомир");
        saved.Team.Should().Be("Ластівки");
        saved.FirstName.Should().Be("Олена");
    }

    [SkippableFact]
    public async Task EditorProfile_Questions_ShowMoreIsALinkToTheNextQuestion()
    {
        await using var factory = CreateFactory();
        var (authorId, questionIds) = await CreateAuthorWithQuestions(factory, 25);
        using var browser = TestBrowser.Create(factory);

        var firstPage = await TestBrowser.GetDecodedHtml(browser, $"/editor/{authorId}?view=questions");

        QuestionAnchors(firstPage).Should().Equal(questionIds.Take(20));
        firstPage.Should().Contain($"href=\"/editor/{authorId}?view=questions&shown=40#question-{questionIds[20]}\"");

        var secondPage = await TestBrowser.GetDecodedHtml(browser, $"/editor/{authorId}?view=questions&shown=40");

        QuestionAnchors(secondPage).Should().Equal(questionIds);
        secondPage.Should().NotContain("Показати більше");
    }

    [SkippableFact]
    public async Task EditorProfile_DefaultsToPackages_AndCapsShownCount()
    {
        await using var factory = CreateFactory();
        var (authorId, _) = await CreateAuthorWithQuestions(factory, 3);
        using var browser = TestBrowser.Create(factory);

        foreach (var shown in new[] { "-5", "abc", "99999999999" })
        {
            var html = await TestBrowser.GetDecodedHtml(browser, $"/editor/{authorId}?shown={shown}");

            html.Should().Contain("Список пакетів").And.NotContain("Список питань");
            QuestionAnchors(html).Should().BeEmpty();
        }
    }

    [SkippableFact]
    public async Task PackageResults_SourceCheckboxes_FilterThroughTheQueryString()
    {
        await using var factory = CreateFactory();
        var package = await TestData.CreatePackage(factory, ownerId: null, status: PackageStatus.Published);
        int first, second;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            var a = Source(package.Id, "Перше джерело", "Команда Альфа");
            var b = Source(package.Id, "Друге джерело", "Команда Бета");
            db.ResultsSources.AddRange(a, b);
            await db.SaveChangesAsync();
            (first, second) = (a.Id, b.Id);
        }

        using var browser = TestBrowser.Create(factory);
        var url = $"/package/{package.Id}/results";

        var all = await TestBrowser.GetDecodedHtml(browser, url);
        all.Should().Contain("Команда Альфа").And.Contain("Команда Бета")
            .And.Contain("data-autosubmit").And.NotContain(BlazorScript);

        var onlySecond = await TestBrowser.GetDecodedHtml(browser, $"{url}?sources=selected&source={second}");
        onlySecond.Should().Contain("Команда Бета").And.NotContain("Команда Альфа");
        onlySecond.Should().MatchRegex($"value=\"{second}\"\\s+checked").And.NotMatchRegex($"value=\"{first}\"\\s+checked");

        foreach (var noneTicked in new[] { $"{url}?sources=selected", $"{url}?sources=selected&source=junk" })
        {
            var none = await TestBrowser.GetDecodedHtml(browser, noneTicked);
            none.Should().Contain("Оберіть хоча б одне джерело результатів.").And.NotContain("Команда Альфа");
        }
    }

    [SkippableFact]
    public async Task EditorProfile_Admin_LinksAndUnlinksAUserThroughForms()
    {
        await using var factory = CreateFactory();
        var (authorId, _) = await CreateAuthorWithQuestions(factory, 1);
        var user = await TestBrowser.CreateUser(factory, role: null);
        using var browser = TestBrowser.Create(factory);
        await TestBrowser.Login(browser, TestBrowser.AdminEmail, TestBrowser.AdminPassword);
        var url = $"/editor/{authorId}";

        // Success redirects back to the page (post/redirect/get), so a refresh doesn't re-post a vanished form
        using (var link = await PostRaw(browser, url, "link-user", new() { ["LinkUserId"] = user.Id }))
            ExpectRedirectTo(link, url);
        (await AuthorUserId(factory, authorId)).Should().Be(user.Id);
        (await TestBrowser.GetDecodedHtml(browser, url)).Should().Contain(user.Email).And.Contain("Від'єднати");

        using (var unlink = await PostRaw(browser, url, "unlink-user", new()))
            ExpectRedirectTo(unlink, url);
        (await AuthorUserId(factory, authorId)).Should().BeNull();
        (await TestBrowser.GetDecodedHtml(browser, url)).Should().Contain("Автор не пов'язаний з жодним користувачем.");
    }

    [SkippableFact]
    public async Task EditorProfile_Admin_MergesIntoASearchedAuthor_AndLandsOnIt()
    {
        await using var factory = CreateFactory();
        var (sourceId, questionIds) = await CreateAuthorWithQuestions(factory, 2);
        var (targetId, _) = await CreateAuthorWithQuestions(factory, 1);
        var targetLastName = await AuthorLastName(factory, targetId);
        using var browser = TestBrowser.Create(factory);
        await TestBrowser.Login(browser, TestBrowser.AdminEmail, TestBrowser.AdminPassword);
        var url = $"/editor/{sourceId}?merge={Uri.EscapeDataString(targetLastName)}";

        var search = await TestBrowser.GetDecodedHtml(browser, url);
        search.Should().Contain($"name=\"MergeTargetId\" value=\"{targetId}\"").And.Contain("data-confirm=");

        using var response = await PostRaw(browser, url, "merge-author", new() { ["MergeTargetId"] = targetId.ToString(CultureInfo.InvariantCulture) });

        ExpectRedirectTo(response, $"/editor/{targetId}");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        (await db.Authors.AnyAsync(a => a.Id == sourceId)).Should().BeFalse();
        (await db.Questions.Where(q => questionIds.Contains(q.Id)).AllAsync(q => q.Authors.Any(a => a.Id == targetId)))
            .Should().BeTrue();
    }

    [SkippableFact]
    public async Task EditorProfile_NonAdmin_CannotPostTheMergeForm()
    {
        await using var factory = CreateFactory();
        var (sourceId, _) = await CreateAuthorWithQuestions(factory, 1);
        var (targetId, _) = await CreateAuthorWithQuestions(factory, 1);
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        using var browser = TestBrowser.Create(factory);
        await TestBrowser.Login(browser, editor.Email, editor.Password);
        var url = $"/editor/{sourceId}?merge={Uri.EscapeDataString(await AuthorLastName(factory, targetId))}";

        using var response = await PostRaw(browser, url, "merge-author", new() { ["MergeTargetId"] = targetId.ToString(CultureInfo.InvariantCulture) });

        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect);
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>().Authors.AnyAsync(a => a.Id == sourceId))
            .Should().BeTrue("the merge form is rendered for admins only, so the post is not dispatched");
    }

    /// <summary>Submits the server-rendered form named <paramref name="formName"/> and returns the decoded response HTML.</summary>
    private static async Task<string> Post(HttpClient browser, string url, string formName, Dictionary<string, string> fields)
    {
        using var response = await PostRaw(browser, url, formName, fields);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static void ExpectRedirectTo(HttpResponseMessage response, string path)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().EndWith(path);
    }

    private static async Task<HttpResponseMessage> PostRaw(
        HttpClient browser, string url, string formName, Dictionary<string, string> fields)
    {
        fields["_handler"] = formName;
        fields["__RequestVerificationToken"] = await TestBrowser.GetAntiforgeryToken(browser, url);
        using var form = new FormUrlEncodedContent(fields);
        return await browser.PostAsync(url, form);
    }

    /// <summary>A published package with one tour of <paramref name="count"/> questions by a new author, in display order.</summary>
    private static async Task<(int AuthorId, List<int> QuestionIds)> CreateAuthorWithQuestions(QuestionsHubAppFactory factory, int count)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        var author = new Author { FirstName = "Автор", LastName = "Профіль" + Guid.NewGuid().ToString("N")[..10] };
        var questions = Enumerable.Range(0, count)
            .Select(i => new Question
            {
                OrderIndex = i, Number = (i + 1).ToString(CultureInfo.InvariantCulture), Text = $"Питання {i + 1}", Answer = "Відповідь", Authors = [author]
            })
            .ToList();
        var package = new Package
        {
            Title = "Пакет автора " + author.LastName,
            Status = PackageStatus.Published,
            TotalQuestions = count,
            PackageEditors = [],
            Tours = [new Tour { OrderIndex = 0, Number = "1", Editors = [author], Questions = questions }]
        };
        db.Packages.Add(package);
        await db.SaveChangesAsync();
        return (author.Id, questions.Select(q => q.Id).ToList());
    }

    private static ResultsSource Source(int packageId, string title, string teamName) => new()
    {
        PackageId = packageId,
        Url = "https://example.com/" + Guid.NewGuid().ToString("N"),
        Title = title,
        LoadedAt = DateTime.UtcNow,
        TeamsCount = 1,
        CreatedAt = DateTime.UtcNow,
        TeamResults = [new TeamResult { Name = teamName, Points = 10, Position = 1 }]
    };

    private static async Task<string?> AuthorUserId(QuestionsHubAppFactory factory, int authorId)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>()
            .Authors.AsNoTracking().SingleAsync(a => a.Id == authorId)).UserId;
    }

    private static async Task<string> AuthorLastName(QuestionsHubAppFactory factory, int authorId)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>()
            .Authors.AsNoTracking().SingleAsync(a => a.Id == authorId)).LastName;
    }

    /// <summary>Question ids of the "show more" anchors in the page, in order.</summary>
    private static List<int> QuestionAnchors(string html) =>
        QuestionAnchor().Matches(html).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();

    [GeneratedRegex("class=\"anchor-target\" id=\"question-(\\d+)\"")]
    private static partial Regex QuestionAnchor();
}
