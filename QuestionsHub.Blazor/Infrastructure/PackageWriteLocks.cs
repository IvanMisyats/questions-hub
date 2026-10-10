namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Process-wide coordination of writers whose correctness depends on a package's question layout:
/// agent changesets (structural edits) and tournament-results loading (stats are mapped to questions
/// by position). Holding the lock around "read layout → write" keeps one from acting on a layout the
/// other is changing. Process-wide is enough: the app runs as a single instance. Striped, so the
/// lock table stays bounded whatever ids are requested; unrelated packages may share a stripe.
/// </summary>
public static class PackageWriteLocks
{
    private const int Stripes = 64;
    private static readonly SemaphoreSlim[] Locks = Enumerable.Range(0, Stripes).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    /// <summary>Waits up to <paramref name="timeout"/>; null when the lock was not acquired in time.</summary>
    public static async Task<IDisposable?> TryAcquire(int packageId, TimeSpan timeout, CancellationToken ct = default)
    {
        var semaphore = For(packageId);
        return await semaphore.WaitAsync(timeout, ct) ? new Releaser(semaphore) : null;
    }

    /// <summary>Waits until the lock is free (or <paramref name="ct"/> is cancelled).</summary>
    public static async Task<IDisposable> Acquire(int packageId, CancellationToken ct = default)
    {
        var semaphore = For(packageId);
        await semaphore.WaitAsync(ct);
        return new Releaser(semaphore);
    }

    private static SemaphoreSlim For(int packageId) => Locks[(int)((uint)packageId % Stripes)];

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();
        }
    }
}
