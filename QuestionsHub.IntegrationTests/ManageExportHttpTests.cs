using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Controllers.Api.Manage;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Media;
using QuestionsHub.IntegrationTests.Infrastructure;
using Xunit;

namespace QuestionsHub.IntegrationTests;

/// <summary><c>GET /api/v1/manage/packages/{id}/export</c>: the archive, its ETag/304 and the token's rights.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class ManageExportHttpTests(PostgresFixture database)
{
    private static HttpClient Client(QuestionsHubAppFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<HttpResponseMessage> Export(HttpClient client, int packageId, EntityTagHeaderValue? ifNoneMatch = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/manage/packages/{packageId}/export");
        if (ifNoneMatch != null)
            request.Headers.IfNoneMatch.Add(ifNoneMatch);
        return await client.SendAsync(request);
    }

    private static async Task Update(QuestionsHubAppFactory factory, int packageId, Action<Question> change)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        var question = await db.Questions.Where(q => q.Tour.PackageId == packageId).OrderBy(q => q.Id).FirstAsync();
        change(question);
        await db.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task Export_ReturnsTheArchive_ThenNotModified_UntilTheContentChanges()
    {
        database.SkipIfUnavailable();
        await using var factory = new QuestionsHubAppFactory(database);
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        var media = factory.Services.GetRequiredService<IOptions<MediaUploadOptions>>().Value;
        var picture = $"export-{Guid.NewGuid():N}.png";
        await File.WriteAllBytesAsync(Path.Combine(media.UploadsPath, media.HandoutsFolder, picture), [1, 2, 3]);
        await Update(factory, package.Id, q => q.HandoutUrl = "/media/" + picture);
        using var client = Client(factory, await TestData.CreateToken(factory, editor.Id, TokenScope.Read));

        using var first = await Export(client, package.Id);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        first.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("Тестовий пакет.qhub");
        first.Headers.ETag!.IsWeak.Should().BeTrue();
        first.Headers.CacheControl!.Private.Should().BeTrue();
        first.Headers.CacheControl.NoCache.Should().BeTrue();
        first.Headers.Contains(ManageExportController.MissingAssetsHeader).Should().BeFalse();
        using (var zip = new ZipArchive(await first.Content.ReadAsStreamAsync(), ZipArchiveMode.Read))
        {
            zip.GetEntry("assets/" + picture).Should().NotBeNull();
            using var json = await JsonDocument.ParseAsync(zip.GetEntry("package.json")!.Open());
            var tours = json.RootElement.GetProperty("tours");
            tours[0].GetProperty("questions")[0].GetProperty("handoutAssetFileName").GetString().Should().Be(picture);
            tours[1].GetProperty("blocks")[0].GetProperty("questions")[0].GetProperty("text").GetString()
                .Should().Be("Питання в блоці");
        }

        using var unchanged = await Export(client, package.Id, first.Headers.ETag);
        unchanged.StatusCode.Should().Be(HttpStatusCode.NotModified);
        unchanged.Headers.ETag.Should().Be(first.Headers.ETag);

        await Update(factory, package.Id, q => q.Text = "Змінене питання");
        using var changed = await Export(client, package.Id, first.Headers.ETag);
        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        changed.Headers.ETag.Should().NotBe(first.Headers.ETag);

        File.Delete(Path.Combine(media.UploadsPath, media.HandoutsFolder, picture));
        using var missing = await Export(client, package.Id, changed.Headers.ETag);
        missing.StatusCode.Should().Be(HttpStatusCode.OK);
        missing.Headers.GetValues(ManageExportController.MissingAssetsHeader).Should().Equal("1");

        // A failing If-Match wins over a matching If-None-Match (RFC 9110 §13.2.2): 412, not 304.
        using var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/manage/packages/{package.Id}/export");
        conditional.Headers.IfMatch.Add(new EntityTagHeaderValue("\"other\""));
        conditional.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
        using var precondition = await client.SendAsync(conditional);
        precondition.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
    }

    [SkippableFact]
    public async Task Export_IsLimitedToThePackagesTheTokenCanEdit()
    {
        database.SkipIfUnavailable();
        await using var factory = new QuestionsHubAppFactory(database);
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var other = await TestBrowser.CreateUser(factory, "Editor");
        var allowed = await TestData.CreatePackage(factory, editor.Id);
        var notAllowed = await TestData.CreatePackage(factory, editor.Id);
        var foreign = await TestData.CreatePackage(factory, other.Id, PackageStatus.Published);

        using var unrestricted = Client(factory, await TestData.CreateToken(factory, editor.Id));
        using (var response = await Export(unrestricted, foreign.Id))
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using (var response = await Export(unrestricted, int.MaxValue))
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var restricted = Client(factory, await TestData.CreateToken(factory, editor.Id, packageIds: [allowed.Id]));
        using (var response = await Export(restricted, allowed.Id))
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var response = await Export(restricted, notAllowed.Id))
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var admin = Client(factory, await TestData.CreateToken(factory, await TestData.AdminId(factory), TokenScope.Read));
        using (var response = await Export(admin, foreign.Id))
            response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
