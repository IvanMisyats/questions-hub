using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace QuestionsHub.Blazor.Infrastructure.RateLimiting;

/// <summary>
/// Per-principal request budgets (layer 2). Partition keys are ids of already validated principals
/// (API client id, token id), so attackers cannot mint fresh partitions with made-up credentials —
/// that traffic is bounded by the per-IP policies that run before authentication.
/// Shared by the HTTP middleware and services (e.g. the changeset engine enforces <c>agent.write</c>
/// for both REST and MCP).
/// </summary>
public sealed class ClientRateLimiter : IDisposable
{
    private readonly Dictionary<string, PartitionedRateLimiter<string>> _limiters;

    public ClientRateLimiter(IOptions<RateLimitSettings> options)
    {
        var settings = options.Value;
        _limiters = RateLimitBudgets.All.ToDictionary(
            b => b.Name,
            b =>
            {
                var permitLimit = b.Limit(settings);
                return PartitionedRateLimiter.Create<string, string>(key =>
                    RateLimitPartition.GetSlidingWindowLimiter(key, _ => RateLimitWindows.PerMinute(permitLimit)));
            });
    }

    /// <summary>
    /// Takes one permit from <paramref name="budget"/> for <paramref name="principalId"/>.
    /// Check <see cref="RateLimitLease.IsAcquired"/>. Sliding-window leases carry no retry-after
    /// metadata, so callers fall back to a fixed hint (see <see cref="RateLimitingExtensions.WriteRejection"/>).
    /// </summary>
    public RateLimitLease Acquire(string budget, string principalId)
    {
        if (!_limiters.TryGetValue(budget, out var limiter))
            throw new ArgumentException($"Unknown rate-limit budget '{budget}'.", nameof(budget));

        return limiter.AttemptAcquire(principalId);
    }

    public void Dispose()
    {
        foreach (var limiter in _limiters.Values)
            limiter.Dispose();
    }
}
