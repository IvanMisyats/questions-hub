using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuestionsHub.Blazor.Controllers.Api.Manage;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.IntegrationTests.Infrastructure;
using Xunit;

namespace QuestionsHub.IntegrationTests;

/// <summary>The agent read API: which packages a token sees and the shape of the editable tree.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class ManageReadTests(PostgresFixture database)
{
    private QuestionsHubAppFactory CreateFactory()
    {
        database.SkipIfUnavailable();
        return new QuestionsHubAppFactory(database);
    }

    private static HttpClient Client(QuestionsHubAppFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<ManagePackageListResponse> List(HttpClient client, string query = "")
    {
        using var response = await client.GetAsync($"/api/v1/manage/packages{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ManagePackageListResponse>())!;
    }

    [SkippableFact]
    public async Task List_Editor_SeesOwnPackagesOnly_NewestFirst()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var other = await TestBrowser.CreateUser(factory, "Editor");
        var first = await TestData.CreatePackage(factory, editor.Id);
        var second = await TestData.CreatePackage(factory, editor.Id);
        await TestData.CreatePackage(factory, other.Id);
        using var client = Client(factory, await TestData.CreateToken(factory, editor.Id));

        var result = await List(client);

        result.Packages.Select(p => p.Id).Should().Equal(second.Id, first.Id);
        result.TotalCount.Should().Be(2);
        var item = result.Packages[0];
        item.Status.Should().Be("draft");
        item.GameType.Should().Be("www");
        item.ToursCount.Should().Be(2);
        item.TotalQuestions.Should().Be(3);
        item.HasResults.Should().BeFalse();
    }

    [SkippableFact]
    public async Task List_Admin_SeesEveryonesPackages_UnlessAllowlisted()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var allowed = await TestData.CreatePackage(factory, editor.Id);
        var notAllowed = await TestData.CreatePackage(factory, editor.Id);
        var adminId = await TestData.AdminId(factory);

        using var unrestricted = Client(factory, await TestData.CreateToken(factory, adminId));
        (await List(unrestricted, "?pageSize=50")).Packages.Select(p => p.Id)
            .Should().Contain([allowed.Id, notAllowed.Id]);

        using var restricted = Client(factory, await TestData.CreateToken(factory, adminId, packageIds: [allowed.Id]));
        var result = await List(restricted);
        result.Packages.Select(p => p.Id).Should().Equal(allowed.Id);
        result.TotalCount.Should().Be(1);
    }

    [SkippableFact]
    public async Task List_FiltersByStatus()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        await TestData.CreatePackage(factory, editor.Id, PackageStatus.Draft);
        var published = await TestData.CreatePackage(factory, editor.Id, PackageStatus.Published);
        using var client = Client(factory, await TestData.CreateToken(factory, editor.Id));

        (await List(client, "?status=Published")).Packages.Select(p => p.Id).Should().Equal(published.Id);

        using var invalid = await client.GetAsync("/api/v1/manage/packages?status=deleted");
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Get_OwnDraft_ReturnsTheEditableTree()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        using var client = Client(factory, await TestData.CreateToken(factory, editor.Id));

        var dto = await client.GetFromJsonAsync<ManagePackageDto>($"/api/v1/manage/packages/{package.Id}");

        dto!.Id.Should().Be(package.Id);
        dto.Status.Should().Be("draft");
        dto.NumberingMode.Should().Be("global");
        dto.Version.Should().MatchRegex("^[0-9a-f]{64}$");
        dto.Tours.Should().HaveCount(2);

        var tour1 = dto.Tours[0];
        tour1.Type.Should().Be("regular");
        tour1.Editors.Should().ContainSingle();
        tour1.Questions.Select(q => (q.OrderIndex, q.Number, q.Text)).Should().Equal(
            (0, "1", "Перше питання"), (1, "2", "Друге питання"));
        tour1.Questions[1].Source.Should().Be("Джерело");
        tour1.Questions[0].Authors.Should().ContainSingle();

        var tour2 = dto.Tours[1];
        tour2.Questions.Should().BeEmpty("block questions are listed under their block");
        tour2.Blocks.Should().ContainSingle();
        tour2.Blocks[0].Name.Should().Be("Блок А");
        tour2.Blocks[0].Questions.Should().ContainSingle()
            .Which.BlockId.Should().Be(tour2.Blocks[0].Id);
    }

    [SkippableFact]
    public async Task Get_NotEditable_Is404()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var other = await TestBrowser.CreateUser(factory, "Editor");
        var foreign = await TestData.CreatePackage(factory, other.Id, PackageStatus.Published);
        var own = await TestData.CreatePackage(factory, editor.Id);
        var outsideAllowlist = await TestData.CreatePackage(factory, editor.Id);
        var adminId = await TestData.AdminId(factory);

        using var editorClient = Client(factory, await TestData.CreateToken(factory, editor.Id));
        (await editorClient.GetAsync($"/api/v1/manage/packages/{foreign.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await editorClient.GetAsync("/api/v1/manage/packages/999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var restrictedAdmin = Client(factory, await TestData.CreateToken(factory, adminId, packageIds: [own.Id]));
        (await restrictedAdmin.GetAsync($"/api/v1/manage/packages/{own.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await restrictedAdmin.GetAsync($"/api/v1/manage/packages/{outsideAllowlist.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task Get_Version_IsStable_AndChangesWhenContentChanges()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        using var client = Client(factory, await TestData.CreateToken(factory, editor.Id));
        var url = $"/api/v1/manage/packages/{package.Id}";

        var first = (await client.GetFromJsonAsync<ManagePackageDto>(url))!.Version;
        (await client.GetFromJsonAsync<ManagePackageDto>(url))!.Version.Should().Be(first);

        // An edit made elsewhere (e.g. in the editor UI)
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            var question = await db.Questions.FirstAsync(q => q.Tour.PackageId == package.Id);
            question.Comment = "Новий коментар";
            await db.SaveChangesAsync();
        }

        (await client.GetFromJsonAsync<ManagePackageDto>(url))!.Version.Should().NotBe(first);
    }

    [SkippableFact]
    public async Task Lookups_FindAuthorsAndTags_AndRequireSearch()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            db.Authors.Add(new Author { FirstName = "Ярема", LastName = "Пошуковий" });
            db.Tags.Add(new Tag { Name = "пошуковийтег" });
            await db.SaveChangesAsync();
        }
        using var client = Client(factory, await TestData.CreateToken(factory, editor.Id));

        var authors = await client.GetFromJsonAsync<List<ManageAuthorDto>>("/api/v1/manage/authors?search=Пошуков");
        authors.Should().Contain(a => a.FirstName == "Ярема" && a.LastName == "Пошуковий");

        var tags = await client.GetFromJsonAsync<List<ManageTagDto>>("/api/v1/manage/tags?search=пошуковий");
        tags.Should().Contain(t => t.Name == "пошуковийтег");

        (await client.GetAsync("/api/v1/manage/authors")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Get_MapsEditorsTagsMediaAndResults_AndMatchesTrackedFingerprint()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            var tracked = await db.Packages.WithEditableGraph().FirstAsync(p => p.Id == package.Id);
            tracked.SharedEditors = true;
            tracked.PackageEditors.Add(new Author { FirstName = "Пакетний", LastName = Guid.NewGuid().ToString("N")[..12] });
            tracked.Tags.Add(new Tag { Name = $"тег-{Guid.NewGuid():N}"[..20] });
            tracked.Tours.OrderBy(t => t.OrderIndex).First().Questions.OrderBy(q => q.OrderIndex).First().HandoutUrl = "/media/handout_test.png";
            db.ResultsSources.Add(new ResultsSource { PackageId = package.Id, Url = "https://rating.example/1" });
            await db.SaveChangesAsync();
        }
        using var client = Client(factory, await TestData.CreateToken(factory, editor.Id));

        var dto = (await client.GetFromJsonAsync<ManagePackageDto>($"/api/v1/manage/packages/{package.Id}"))!;

        dto.SharedEditors.Should().BeTrue();
        dto.Editors.Should().ContainSingle(a => a.FirstName == "Пакетний");
        dto.Tags.Should().ContainSingle(t => t.Name.StartsWith("тег-"));
        dto.HasResults.Should().BeTrue();
        dto.Tours[1].Blocks[0].Editors.Should().ContainSingle();
        dto.Tours[0].Questions[0].HandoutUrl.Should().StartWith("http").And.EndWith("/media/handout_test.png");
        (await List(client)).Packages.Single().HasResults.Should().BeTrue();

        // The changeset engine fingerprints a tracked graph; it must agree with the read API.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            var tracked = await db.Packages.WithEditableGraph().FirstAsync(p => p.Id == package.Id);
            PackageGraph.Fingerprint(tracked).Should().Be(dto.Version);
        }
    }

    [SkippableFact]
    public async Task OwnerlessPackage_IsAdminOnly()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var ownerless = await TestData.CreatePackage(factory, ownerId: null);
        using var editorClient = Client(factory, await TestData.CreateToken(factory, editor.Id));
        using var adminClient = Client(factory, await TestData.CreateToken(factory, await TestData.AdminId(factory)));

        (await editorClient.GetAsync($"/api/v1/manage/packages/{ownerless.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await adminClient.GetAsync($"/api/v1/manage/packages/{ownerless.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task RestrictedEditorToken_SeesOnlyItsAllowlist()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var allowed = await TestData.CreatePackage(factory, editor.Id);
        var other = await TestData.CreatePackage(factory, editor.Id);
        using var client = Client(factory, await TestData.CreateToken(factory, editor.Id, packageIds: [allowed.Id]));

        (await List(client)).Packages.Select(p => p.Id).Should().Equal(allowed.Id);
        (await client.GetAsync($"/api/v1/manage/packages/{other.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task List_Paging_IsSafe()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var first = await TestData.CreatePackage(factory, editor.Id);
        var second = await TestData.CreatePackage(factory, editor.Id);
        using var client = Client(factory, await TestData.CreateToken(factory, editor.Id));

        (await List(client, "?page=2&pageSize=1")).Packages.Select(p => p.Id).Should().Equal(first.Id);
        (await List(client, "?page=1&pageSize=1")).Packages.Select(p => p.Id).Should().Equal(second.Id);

        var beyond = await List(client, "?page=2147483647&pageSize=50");
        beyond.Packages.Should().BeEmpty();
        beyond.TotalCount.Should().Be(2);

        (await List(client, "?pageSize=100000")).PageSize.Should().Be(50);
        (await List(client, "?page=-3")).Page.Should().Be(1);
    }
}
