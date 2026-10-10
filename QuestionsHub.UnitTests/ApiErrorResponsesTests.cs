using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using QuestionsHub.Blazor.Infrastructure.Api;
using Xunit;

namespace QuestionsHub.UnitTests;

/// <summary>
/// Oversized bodies are rejected by Kestrel while MVC reads them; on the JSON APIs that must be a 413
/// JSON response, not the generic error page's 500. (TestServer does not enforce body limits, so the
/// middleware is exercised directly.)
/// </summary>
public class ApiErrorResponsesTests
{
    private static async Task<HttpContext> Run(string path, RequestDelegate endpoint, BodySizeFeature? limit = null)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseApiErrorResponses();
        app.Run(endpoint);

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (limit != null)
            context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>(limit);
        await app.Build()(context);
        return context;
    }

    /// <summary>Stands in for Kestrel's per-request body limit.</summary>
    private sealed class BodySizeFeature : Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; } = 30_000_000;
    }

    [Fact]
    public async Task McpRequests_GetTheAgentBodyLimit_OtherRequestsKeepTheirs()
    {
        var mcp = new BodySizeFeature();
        await Run("/mcp", _ => Task.CompletedTask, mcp);
        mcp.MaxRequestBodySize.Should().Be(ApiErrorResponses.AgentMaxBodyBytes);

        var other = new BodySizeFeature();
        await Run("/api/v1/packages", _ => Task.CompletedTask, other);
        other.MaxRequestBodySize.Should().Be(30_000_000);
    }

    [Fact]
    public async Task TooLargeBody_OnMcp_Is413Json()
    {
        var context = await Run("/mcp", TooLarge, new BodySizeFeature());

        context.Response.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
    }

    private static readonly RequestDelegate TooLarge =
        _ => throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge);

    [Fact]
    public async Task TooLargeBody_OnTheApi_Is413Json()
    {
        var context = await Run("/api/v1/manage/packages/1/changesets", TooLarge);

        context.Response.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        context.Response.ContentType.Should().StartWith("application/json");
        context.Response.Body.Position = 0;
        (await new StreamReader(context.Response.Body).ReadToEndAsync()).Should().Contain("\"error\"");
    }

    [Fact]
    public async Task TooLargeBody_ElsewhereIsLeftToTheNormalPipeline()
    {
        var act = () => Run("/Account/Login", TooLarge);

        await act.Should().ThrowAsync<BadHttpRequestException>();
    }

    [Fact]
    public async Task OtherBadRequests_AreNotSwallowed()
    {
        var act = () => Run("/api/v1/packages", _ => throw new BadHttpRequestException("Bad", StatusCodes.Status400BadRequest));

        await act.Should().ThrowAsync<BadHttpRequestException>();
    }
}
