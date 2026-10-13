using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using QuestionsHub.Blazor.Controllers.Api.Manage;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.Blazor.Infrastructure.AgentApi.Mcp;
using QuestionsHub.Blazor.Infrastructure.RateLimiting;
using QuestionsHub.Blazor.Infrastructure.Results;
using QuestionsHub.UnitTests.TestInfrastructure;
using Xunit;

namespace QuestionsHub.UnitTests.Results;

public sealed class AgentResultsTests : IDisposable
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly InMemoryDbContextFactory _db = new();
    private readonly ClientRateLimiter _limiter = new(Options.Create(new RateLimitSettings { AgentWritePerMinute = 5 }));

    public void Dispose()
    {
        _limiter.Dispose();
        using var db = _db.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    private static ClaimsPrincipal Agent(string owner = "owner", string? allowlist = null, bool write = true)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, owner), new(ClaimTypes.Role, "Editor"),
            new(AgentClaims.TokenId, "7"), new(AgentClaims.Scope, write ? "ReadWrite" : "Read")
        };
        if (allowlist != null)
            claims.Add(new(AgentClaims.Packages, allowlist));
        return new(new ClaimsIdentity(claims, PersonalAccessTokenAuthenticationOptions.Scheme));
    }

    private async Task<int> Seed(PackageType type = PackageType.Www)
    {
        using var db = _db.CreateDbContext();
        var package = new Package { Title = "Пакет", OwnerId = "owner", Type = type };
        for (var t = 0; t < 3; t++)
        {
            var tour = new Tour { OrderIndex = t, Number = (t + 1).ToString(CultureInfo.InvariantCulture) };
            for (var q = 0; q < 12; q++)
                tour.Questions.Add(new Question { OrderIndex = q, Number = (q + 1).ToString(CultureInfo.InvariantCulture), Text = "Текст", Answer = "Відповідь" });
            package.Tours.Add(tour);
        }
        db.Packages.Add(package);
        await db.SaveChangesAsync();
        return package.Id;
    }

    private AgentResultsService Service(HttpMessageHandler? handler = null)
    {
        var http = new FakeHttpClientFactory(handler ?? new FakeResultsHandler());
        var options = Options.Create(new ResultsOptions());
        var results = new PackageResultsService(_db, new RatingResultsClient(http, options),
            new OpenQuizResultsClient(http, options), options, NullLogger<PackageResultsService>.Instance);
        return new(_db, results, _limiter, NullLogger<AgentResultsService>.Instance);
    }

    private static ManageResultsController Controller(AgentResultsService service, ClaimsPrincipal? agent = null) => new(service)
    {
        ControllerContext = new() { HttpContext = new DefaultHttpContext { User = agent ?? Agent() } }
    };

    [Fact]
    public async Task Attach_RejectsLinksLongerThanTheColumns_With422()
    {
        var id = await Seed();
        var service = Service();

        var tooLongUrl = await FluentActions.Awaiting(() => service.Attach(Agent(), id, "https://example.com/" + new string('a', 2000)))
            .Should().ThrowAsync<AgentResultsException>();
        tooLongUrl.Which.StatusCode.Should().Be(422);
        tooLongUrl.Which.Message.Should().Be("Посилання задовге.");

        var tooLongId = await FluentActions.Awaiting(() => service.Attach(Agent(), id, new string('1', 51)))
            .Should().ThrowAsync<AgentResultsException>();
        tooLongId.Which.StatusCode.Should().Be(422);
        (await service.List(Agent(), id)).Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenQuizAudienceToken_IsMaskedForAgents()
    {
        var id = await Seed();
        const string listenToken = "d4-HwroGR7MjFIWyEqBeOTzlOAv7Bw8Z0uGTuESF8Bo";
        const string audience = "https://www.open-quiz.com/app/index.html?who=aud&quiz=25795&token=" + listenToken;
        const string results = "https://www.open-quiz.com/app/index.html?who=res&quiz=25795&token=PublicResultsToken";
        using (var db = _db.CreateDbContext())
        {
            db.ResultsSources.AddRange(
                new ResultsSource { PackageId = id, Platform = ResultsPlatform.OpenQuiz, Url = audience, ExternalId = "25795" },
                new ResultsSource { PackageId = id, Platform = ResultsPlatform.OpenQuiz, Url = results, ExternalId = "25795" });
            await db.SaveChangesAsync();
        }

        var sources = (await Service().List(Agent(write: false), id)).Sources;

        sources[0].Url.Should().NotContain(listenToken).And.EndWith("token=***");
        sources[1].Url.Should().Be(results, "a public results link is not a credential");
        AgentResultsService.AgentVisibleUrl(ResultsPlatform.OpenQuiz,
                "https://www.open-quiz.com/app/index.html?who=aud&token=%41BCdef&quiz=25795")
            .Should().Be("https://www.open-quiz.com/app/index.html?who=aud&token=***&quiz=25795", "a percent-encoded token is masked too");
    }

    [Fact]
    public async Task Reload_OfASourceDeletedDuringTheFetch_Is422_NotACrash()
    {
        var id = await Seed();
        var source = await Service(Rating()).Attach(Agent(), id, "13097");
        var deleteDuringFetch = new RunOnFirstRequest(Rating(), async () =>
        {
            using var db = _db.CreateDbContext();
            db.ResultsSources.Remove(await db.ResultsSources.SingleAsync(s => s.Id == source.Id));
            await db.SaveChangesAsync();
        });

        var reload = await FluentActions.Awaiting(() => Service(deleteDuringFetch).Reload(Agent(), id, source.Id))
            .Should().ThrowAsync<AgentResultsException>();

        reload.Which.StatusCode.Should().Be(422);
        reload.Which.Message.Should().Be("Джерело результатів не знайдено.");
    }

    [Fact]
    public async Task FailedReload_OfASourceDeletedDuringTheFetch_Is422_NotACrash()
    {
        var id = await Seed();
        var source = await Service(Rating()).Attach(Agent(), id, "13097");
        var deleteThenFail = new RunOnFirstRequest(new FakeResultsHandler(), async () =>
        {
            using var db = _db.CreateDbContext();
            db.ResultsSources.Remove(await db.ResultsSources.SingleAsync(s => s.Id == source.Id));
            await db.SaveChangesAsync();
        });

        var reload = await FluentActions.Awaiting(() => Service(deleteThenFail).Reload(Agent(), id, source.Id))
            .Should().ThrowAsync<AgentResultsException>();

        reload.Which.StatusCode.Should().Be(422);
        reload.Which.Message.Should().Be("Джерело результатів не знайдено.");
    }

    /// <summary>Runs an action (e.g. a concurrent delete) when the platform is first called, then forwards.</summary>
    private sealed class RunOnFirstRequest(HttpMessageHandler inner, Func<Task> onFirstRequest) : DelegatingHandler(inner)
    {
        private int _calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                await onFirstRequest();
            return await base.SendAsync(request, cancellationToken);
        }
    }

    private static FakeResultsHandler Rating() => new FakeResultsHandler()
        .Add("/tournaments/13097/results", ResultsFixtures.Read("rating_results_13097.json"))
        .Add("/tournaments/13097", ResultsFixtures.Read("rating_tournament_13097.json"));

    [Fact]
    public async Task Attach_LoadsRating_ReloadAndDeleteUseTheSameService()
    {
        var id = await Seed();
        var service = Service(Rating());
        var controller = Controller(service);
        var response = (ObjectResult)await controller.Attach(id, new("13097"), default);
        response.StatusCode.Should().Be(201);
        var source = (ManageResultsSourceDto)response.Value!;
        source.Platform.Should().Be("rating");
        source.TeamsCount.Should().Be(17);
        source.StatsMapped.Should().BeTrue();
        source.LoadedAt.Should().NotBeNull();
        source.LoadError.Should().BeNull();
        var reloaded = (ObjectResult)await controller.Reload(id, source.Id, default);
        reloaded.StatusCode.Should().Be(200);
        ((ManageResultsSourceDto)reloaded.Value!).TeamsCount.Should().Be(17);
        (await controller.Delete(id, source.Id, default)).Should().BeOfType<NoContentResult>();
        (await service.List(Agent(), id)).Sources.Should().BeEmpty();
    }

    [Theory]
    [InlineData("other", null)]
    [InlineData("owner", "999")]
    public async Task EveryOperation_ConcealsPackagesOutsideRights(string owner, string? allowlist)
    {
        var id = await Seed();
        var controller = Controller(Service(), Agent(owner, allowlist));
        ((ObjectResult)await controller.List(id, default)).StatusCode.Should().Be(404);
        ((ObjectResult)await controller.Attach(id, new("13097"), default)).StatusCode.Should().Be(404);
        ((ObjectResult)await controller.Reload(id, 1, default)).StatusCode.Should().Be(404);
        ((ObjectResult)await controller.Delete(id, 1, default)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task MissingPackage_AndForeignOrMissingSource_Are404()
    {
        var id = await Seed();
        var other = await Seed();
        var service = Service();
        var source = await service.Attach(Agent(), other, "https://example.com/results");
        var controller = Controller(service);
        ((ObjectResult)await controller.List(999, default)).StatusCode.Should().Be(404);
        foreach (var sourceId in new[] { source.Id, 999 })
        {
            ((ObjectResult)await controller.Reload(id, sourceId, default)).StatusCode.Should().Be(404);
            ((ObjectResult)await controller.Delete(id, sourceId, default)).StatusCode.Should().Be(404);
        }
        (await service.List(Agent(), other)).Sources.Should().ContainSingle();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("ftp://example.com/results")]
    [InlineData("https://open-quiz.com/results.html?quiz=123")]
    [InlineData(null)]
    public async Task InvalidAttach_Returns422WithServiceMessage(string? url)
    {
        var id = await Seed();
        var response = (ObjectResult)await Controller(Service()).Attach(id, new(url), default);
        response.StatusCode.Should().Be(422);
        var error = JsonSerializer.SerializeToElement(response.Value).GetProperty("error").GetString();
        Action parse = () => ResultsUrlParser.Detect(url!);
        parse.Should().Throw<ResultsLoadException>().WithMessage(error);
    }

    [Fact]
    public async Task Duplicate_NonWww_AndOtherReload_Return422()
    {
        var id = await Seed();
        var shvager = await Seed(PackageType.Shvager);
        var controller = Controller(Service());
        var source = (ManageResultsSourceDto)((ObjectResult)await controller.Attach(id, new("https://example.com"), default)).Value!;
        ((ObjectResult)await controller.Attach(id, new("https://example.com"), default)).StatusCode.Should().Be(422);
        ((ObjectResult)await controller.Attach(shvager, new("13097"), default)).StatusCode.Should().Be(422);
        ((ObjectResult)await controller.Reload(id, source.Id, default)).StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task LoadFailure_Is201_AndReloadFailurePreservesData()
    {
        var id = await Seed();
        var failed = (ObjectResult)await Controller(Service()).Attach(id, new("13097"), default);
        failed.StatusCode.Should().Be(201);
        var source = (ManageResultsSourceDto)failed.Value!;
        source.LoadError.Should().NotBeNullOrEmpty();
        var loaded = await Service(Rating()).Reload(Agent(), id, source.Id);
        var reloaded = await Service().Reload(Agent(), id, source.Id);
        reloaded.LoadError.Should().NotBeNullOrEmpty();
        reloaded.LoadedAt.Should().Be(loaded.LoadedAt);
        reloaded.TeamsCount.Should().Be(17);
    }

    [Fact]
    public async Task Embargo_IsReturnedInDto()
    {
        var id = await Seed();
        var handler = new FakeResultsHandler()
            .Add("/tournaments/99999/results", ResultsFixtures.Read("rating_results_embargoed.json"))
            .Add("/tournaments/99999", ResultsFixtures.Read("rating_tournament_embargoed.json"));
        var source = await Service(handler).Attach(Agent(), id, "99999");
        source.LoadError.Should().NotBeNullOrEmpty();
        source.ResultsAvailableAfter.Should().Be(new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task List_OrdersSources_ParsesWarnings_AndOmitsPrivateFields()
    {
        var id = await Seed();
        using (var db = _db.CreateDbContext())
        {
            db.ResultsSources.AddRange(
                new ResultsSource { Id = 9, PackageId = id, Url = "https://example.com/9", Platform = ResultsPlatform.OpenQuiz, WarningsJson = "[\"warning\"]", RawPayload = "secret", LoadErrorDetail = "secret" },
                new ResultsSource { Id = 2, PackageId = id, Url = "https://example.com/2" });
            await db.SaveChangesAsync();
        }
        var list = await Service().List(Agent(), id);
        list.Sources.Select(s => s.Id).Should().Equal(2, 9);
        list.Sources[0].Warnings.Should().BeEmpty();
        list.Sources[1].Warnings.Should().Equal("warning");
        list.Sources[1].Platform.Should().Be("openQuiz");
        var json = JsonSerializer.Serialize(list, WebJson);
        json.Should().Contain("\"sources\"").And.NotContain("rawPayload").And.NotContain("loadErrorDetail").And.NotContain("secret");
    }

    [Fact]
    public async Task WritesShareBudgetAcrossRestAndMcp_AndReturnRetryAfter()
    {
        var id = await Seed();
        var service = Service();
        var controller = Controller(service);
        var source = await service.Attach(Agent(), id, "13097");
        var accessor = new HttpContextAccessor { HttpContext = controller.HttpContext };
        var mcp = new AgentMcpTools(accessor, null!, null!, service);
        for (var i = 0; i < 4; i++)
            await mcp.ReloadResultsSource(id, source.Id);
        var response = (ObjectResult)await controller.Delete(id, source.Id, default);
        response.StatusCode.Should().Be(429);
        controller.Response.Headers.RetryAfter.ToString().Should().Be("60");
        var act = () => mcp.AttachResultsSource(id, "13098");
        await act.Should().ThrowAsync<McpException>().WithMessage("Results rate limit exceeded. Please retry later.");
    }

    [Fact]
    public async Task Mcp_ReadOnlyRejectsAllWrites_AndTranslatesErrors()
    {
        var id = await Seed();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = Agent(write: false) } };
        var mcp = new AgentMcpTools(accessor, null!, null!, Service());
        Func<Task>[] writes = [() => mcp.AttachResultsSource(id, "13097"), () => mcp.ReloadResultsSource(id, 1), () => mcp.DeleteResultsSource(id, 1)];
        foreach (var write in writes)
            await write.Should().ThrowAsync<McpException>().WithMessage("*read-only token*");
        (await mcp.ListResultsSources(id)).Sources.Should().BeEmpty();
        var missing = () => mcp.ListResultsSources(999);
        await missing.Should().ThrowAsync<McpException>().WithMessage("Package not found.");
    }
}
