using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuestionsHub.Blazor.Controllers;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Api;
using QuestionsHub.Blazor.Infrastructure.Favorites;
using QuestionsHub.IntegrationTests.Infrastructure;
using Xunit;

namespace QuestionsHub.IntegrationTests;

/// <summary>
/// The heart endpoints over HTTP: cookie + antiforgery only, idempotent, access-checked, capped and rate-limited.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class FavoritesHttpTests(PostgresFixture database)
{
    private const string AntiforgeryHeader = "RequestVerificationToken";

    private QuestionsHubAppFactory CreateFactory(IDictionary<string, string?>? settings = null)
    {
        database.SkipIfUnavailable();
        return new QuestionsHubAppFactory(database, settings);
    }

    private static async Task<(HttpClient Browser, TestUser User, string Token)> SignIn(QuestionsHubAppFactory factory)
    {
        var user = await TestBrowser.CreateUser(factory, role: null);
        var browser = TestBrowser.Create(factory);
        await TestBrowser.Login(browser, user.Email, user.Password);
        return (browser, user, await TestBrowser.GetAntiforgeryToken(browser));
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string url, string? antiforgery)
    {
        var request = new HttpRequestMessage(method, url);
        if (antiforgery != null)
            request.Headers.Add(AntiforgeryHeader, antiforgery);
        return client.SendAsync(request);
    }

    private static async Task<int> CountFavorites(QuestionsHubAppFactory factory, string userId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>()
            .Favorites.CountAsync(f => f.UserId == userId);
    }

    [SkippableFact]
    public async Task PutAndDelete_ToggleTheFavorite_AndAreIdempotent()
    {
        await using var factory = CreateFactory();
        var package = await TestData.CreatePackage(factory, ownerId: null, status: PackageStatus.Published);
        var question = package.Tours.SelectMany(t => t.Questions).First();
        var (browser, user, token) = await SignIn(factory);
        var url = $"/api/favorites/question/{question.Id}";

        foreach (var _ in Enumerable.Range(0, 2))
        {
            using var put = await Send(browser, HttpMethod.Put, url, token);
            put.StatusCode.Should().Be(HttpStatusCode.OK);
            (await put.Content.ReadFromJsonAsync<FavoriteStateResponse>())!.IsFavorite.Should().BeTrue();
        }
        (await CountFavorites(factory, user.Id)).Should().Be(1);

        foreach (var _ in Enumerable.Range(0, 2))
        {
            using var delete = await Send(browser, HttpMethod.Delete, url, token);
            delete.StatusCode.Should().Be(HttpStatusCode.OK);
            (await delete.Content.ReadFromJsonAsync<FavoriteStateResponse>())!.IsFavorite.Should().BeFalse();
        }
        (await CountFavorites(factory, user.Id)).Should().Be(0);
    }

    [SkippableFact]
    public async Task EveryKind_CanBeFavorited()
    {
        await using var factory = CreateFactory();
        var package = await TestData.CreatePackage(factory, ownerId: null, status: PackageStatus.Published);
        var tour = package.Tours.First(t => t.Blocks.Count > 0);
        var (browser, user, token) = await SignIn(factory);

        foreach (var url in new[]
                 {
                     $"/api/favorites/package/{package.Id}",
                     $"/api/favorites/tour/{tour.Id}",
                     $"/api/favorites/block/{tour.Blocks.Single().Id}",
                     $"/api/favorites/question/{tour.Questions.Single().Id}"
                 })
        {
            using var response = await Send(browser, HttpMethod.Put, url, token);
            response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        }

        (await CountFavorites(factory, user.Id)).Should().Be(4);
    }

    [SkippableFact]
    public async Task Anonymous_Gets401_NotALoginRedirect()
    {
        await using var factory = CreateFactory();
        var package = await TestData.CreatePackage(factory, ownerId: null, status: PackageStatus.Published);
        using var browser = TestBrowser.Create(factory);

        using var response = await Send(browser, HttpMethod.Put, $"/api/favorites/package/{package.Id}", antiforgery: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AgentTokenAlone_IsNotAccepted()
    {
        await using var factory = CreateFactory();
        var package = await TestData.CreatePackage(factory, ownerId: null, status: PackageStatus.Published);
        var token = await TestData.CreateToken(factory, await TestData.AdminId(factory));
        using var client = TestBrowser.Create(factory);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/favorites/package/{package.Id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task ApiKeyAlone_IsNotAccepted()
    {
        await using var factory = CreateFactory();
        var package = await TestData.CreatePackage(factory, ownerId: null, status: PackageStatus.Published);
        string apiKey;
        using (var scope = factory.Services.CreateScope())
            (_, apiKey) = await scope.ServiceProvider.GetRequiredService<ApiKeyService>().Create("app");
        using var client = TestBrowser.Create(factory);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/favorites/package/{package.Id}");
        request.Headers.Add("X-API-Key", apiKey);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task MissingAntiforgeryToken_IsRejected()
    {
        await using var factory = CreateFactory();
        var package = await TestData.CreatePackage(factory, ownerId: null, status: PackageStatus.Published);
        var (browser, user, _) = await SignIn(factory);

        using var put = await Send(browser, HttpMethod.Put, $"/api/favorites/package/{package.Id}", antiforgery: null);
        using var delete = await Send(browser, HttpMethod.Delete, $"/api/favorites/package/{package.Id}", antiforgery: null);

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        delete.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountFavorites(factory, user.Id)).Should().Be(0);
    }

    [SkippableFact]
    public async Task SomeoneElsesDraft_MissingItem_AndUnknownKind_Get404()
    {
        await using var factory = CreateFactory();
        var draft = await TestData.CreatePackage(factory, ownerId: await TestData.AdminId(factory), status: PackageStatus.Draft);
        var (browser, user, token) = await SignIn(factory);

        foreach (var url in new[]
                 {
                     $"/api/favorites/package/{draft.Id}",
                     $"/api/favorites/question/{draft.Tours.First().Questions.First().Id}",
                     "/api/favorites/question/2147483647",
                     $"/api/favorites/Package/{draft.Id}",
                     $"/api/favorites/0/{draft.Id}"
                 })
        {
            using var response = await Send(browser, HttpMethod.Put, url, token);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, url);
        }

        (await CountFavorites(factory, user.Id)).Should().Be(0);
    }

    [SkippableFact]
    public async Task AtTheCap_Returns409()
    {
        await using var factory = CreateFactory();
        var package = await TestData.CreatePackage(factory, ownerId: null, status: PackageStatus.Published);
        var tour = package.Tours.OrderBy(t => t.OrderIndex).First();
        var target = package.Tours.SelectMany(t => t.Questions).First(q => q.TourId != tour.Id);
        var (browser, user, token) = await SignIn(factory);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
            var max = FavoriteService.MaxFavoritesPerUser;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Questions" ("OrderIndex", "Number", "Text", "Answer", "TourId")
                SELECT 100 + g, g::text, 'Масове питання', 'Відповідь', {tour.Id} FROM generate_series(1, {max}) g
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Favorites" ("UserId", "PackageId", "QuestionId", "CreatedAt")
                SELECT {user.Id}, {package.Id}, "Id", now() FROM "Questions"
                WHERE "TourId" = {tour.Id} AND "Text" = 'Масове питання'
                """);
        }

        using var response = await Send(browser, HttpMethod.Put, $"/api/favorites/question/{target.Id}", token);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain(FavoriteService.MaxFavoritesPerUser.ToString(CultureInfo.InvariantCulture));
    }

    [SkippableFact]
    public async Task PerUserBudget_Returns429()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?> { ["RateLimits:UserFavoritesPerMinute"] = "2" });
        var package = await TestData.CreatePackage(factory, ownerId: null, status: PackageStatus.Published);
        var (browser, _, token) = await SignIn(factory);
        var url = $"/api/favorites/package/{package.Id}";

        var statuses = new List<HttpStatusCode>();
        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete, HttpMethod.Put })
        {
            using var response = await Send(browser, method, url, token);
            statuses.Add(response.StatusCode);
        }

        statuses.Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
    }
}
