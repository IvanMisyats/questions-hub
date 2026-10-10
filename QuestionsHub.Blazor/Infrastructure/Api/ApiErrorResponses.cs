using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace QuestionsHub.Blazor.Infrastructure.Api;

/// <summary>
/// Keeps every error of the JSON APIs under <c>/api/v1</c> in the documented <c>{ "error": "…" }</c>
/// shape: model-binding failures (otherwise ProblemDetails) and oversized bodies (otherwise an
/// exception that the generic error page would turn into a 500 HTML page).
/// </summary>
public static class ApiErrorResponses
{
    /// <summary>Body limit for agent requests; the REST changeset endpoint declares the same via [RequestSizeLimit].</summary>
    public const long AgentMaxBodyBytes = 512 * 1024;

    public static bool IsJsonApi(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api/v1") || IsMcp(context);

    private static bool IsMcp(HttpContext context) => context.Request.Path.StartsWithSegments("/mcp");

    /// <summary>Model-binding failures on /api/v1 become <c>{ error, details }</c>; elsewhere the default.</summary>
    public static IServiceCollection AddApiErrorResponses(this IServiceCollection services)
    {
        services.PostConfigure<ApiBehaviorOptions>(options =>
        {
            var fallback = options.InvalidModelStateResponseFactory;
            options.InvalidModelStateResponseFactory = context =>
            {
                if (!IsJsonApi(context.HttpContext))
                    return fallback(context);

                var details = context.ModelState
                    .Where(entry => entry.Value is { Errors.Count: > 0 })
                    .ToDictionary(
                        entry => string.IsNullOrEmpty(entry.Key) ? "body" : entry.Key,
                        entry => entry.Value!.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage).ToArray());
                var summary = string.Join("; ", details.Select(d => $"{d.Key}: {d.Value[0]}"));

                return new BadRequestObjectResult(new { error = $"Invalid request — {summary}", details });
            };
        });

        return services;
    }

    /// <summary>
    /// Turns a request body over the endpoint's limit into a 413 JSON response on /api/v1 and /mcp
    /// (Kestrel throws <see cref="BadHttpRequestException"/> while the body is read), and caps /mcp
    /// bodies like the REST changeset endpoint (Kestrel enforces it, chunked bodies included).
    /// Register inside <c>UseExceptionHandler</c> so it sees the exception first.
    /// </summary>
    public static IApplicationBuilder UseApiErrorResponses(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (IsMcp(context) && context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                limit.MaxRequestBodySize = AgentMaxBodyBytes;

            try
            {
                await next(context);
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge
                                                     && IsJsonApi(context) && !context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                await context.Response.WriteAsJsonAsync(new { error = "Request body is too large." });
            }
        });
}
