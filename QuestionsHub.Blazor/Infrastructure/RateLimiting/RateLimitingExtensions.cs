using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace QuestionsHub.Blazor.Infrastructure.RateLimiting;

/// <summary>
/// Marks an endpoint with the per-principal budget it consumes (layer 2). The most specific
/// attribute wins (an action-level attribute overrides the controller-level one).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class ClientRateLimitAttribute(string budget) : Attribute
{
    public string Budget { get; } = budget;
}

internal static class RateLimitWindows
{
    /// <summary>Sliding one-minute window in 10-second segments, no queueing.</summary>
    public static SlidingWindowRateLimiterOptions PerMinute(int permitLimit) => new()
    {
        Window = TimeSpan.FromMinutes(1),
        SegmentsPerWindow = 6,
        PermitLimit = permitLimit,
        QueueLimit = 0
    };
}

public static class RateLimitingExtensions
{
    private const string RejectionBody = """{"error":"Rate limit exceeded. Please retry later."}""";

    /// <summary>
    /// Registers both rate-limiting layers: per-IP admission policies (run before authentication)
    /// and the per-principal <see cref="ClientRateLimiter"/>.
    /// </summary>
    public static IServiceCollection AddRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitSettings>(configuration.GetSection(RateLimitSettings.SectionName));
        services.AddSingleton<ClientRateLimiter>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitPolicies.IpPublicApi, ctx => IpPartition(ctx, s => s.IpPublicApiPerMinute));
            options.AddPolicy(RateLimitPolicies.IpManage, ctx => IpPartition(ctx, s => s.IpManagePerMinute));
            options.AddPolicy(RateLimitPolicies.IpAuth, ctx => IpPartition(ctx, s => s.IpAuthPerMinute));

            options.OnRejected = (context, cancellationToken) =>
                new ValueTask(WriteRejection(context.HttpContext, context.Lease, cancellationToken));
        });

        return services;
    }

    /// <summary>
    /// Adds the per-principal budget check. Must run after <c>UseAuthorization</c>, which replaces
    /// <c>HttpContext.User</c> with the principal of the endpoint's authentication scheme.
    /// </summary>
    public static IApplicationBuilder UseClientRateLimiting(this IApplicationBuilder app) =>
        app.UseMiddleware<ClientRateLimitingMiddleware>();

    /// <summary>The client IP as seen after forwarded-headers processing.</summary>
    public static string ClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// Writes the standard 429 JSON response with a <c>Retry-After</c> header. Sliding-window
    /// leases do not report when a permit frees up, so the hint falls back to the window length.
    /// </summary>
    public static async Task WriteRejection(HttpContext context, RateLimitLease lease, CancellationToken cancellationToken)
    {
        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue)
            ? retryAfterValue
            : TimeSpan.FromMinutes(1);

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/json";
        context.Response.Headers.RetryAfter =
            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        await context.Response.WriteAsync(RejectionBody, cancellationToken);
    }

    private static RateLimitPartition<string> IpPartition(HttpContext context, Func<RateLimitSettings, int> limit)
    {
        var permitLimit = limit(context.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value);
        return RateLimitPartition.GetSlidingWindowLimiter(ClientIp(context), _ => RateLimitWindows.PerMinute(permitLimit));
    }
}

/// <summary>
/// Layer 2: charges the endpoint's <see cref="ClientRateLimitAttribute"/> budget to the validated
/// principal. Endpoints without the attribute, or requests without the identifying claim (anonymous
/// endpoints — protected ones were already rejected by authorization), pass through.
/// </summary>
public sealed class ClientRateLimitingMiddleware(RequestDelegate next, ClientRateLimiter limiter)
{
    public async Task Invoke(HttpContext context)
    {
        var budget = context.GetEndpoint()?.Metadata.GetMetadata<ClientRateLimitAttribute>()?.Budget;
        var principalId = budget == null
            ? null
            : context.User.FindFirst(RateLimitBudgets.ClaimTypeFor(budget))?.Value;

        if (budget == null || principalId == null)
        {
            await next(context);
            return;
        }

        using var lease = limiter.Acquire(budget, principalId);
        if (!lease.IsAcquired)
        {
            await RateLimitingExtensions.WriteRejection(context, lease, context.RequestAborted);
            return;
        }

        await next(context);
    }
}
