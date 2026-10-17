using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Infrastructure.Export;

namespace QuestionsHub.Blazor.Infrastructure.AgentApi;

/// <summary>The outcome of <see cref="AgentPackageExporter.Export"/>.</summary>
public abstract record AgentExportResult
{
    /// <summary>The package does not exist or is outside the agent's rights.</summary>
    public sealed record NotFound : AgentExportResult;

    /// <summary>The client's copy (<c>If-None-Match</c>) is current.</summary>
    public sealed record NotModified(string ETag) : AgentExportResult;

    /// <summary>
    /// The archive, positioned at its start. The caller owns <paramref name="Content"/> and must dispose it
    /// (MVC does after streaming a file result): disposing deletes the temp file and frees its slot.
    /// </summary>
    public sealed record Archive(Stream Content, string ETag, string FileName, int MissingAssets) : AgentExportResult;
}

/// <summary>
/// <c>.qhub</c> exports for the agent API (backups): the package the agent can edit, a weak ETag over its
/// content (<see cref="QhubExporter.ComputeETag"/>) so unchanged packages answer 304 without reading media,
/// and the archive built in a temp file rather than in memory.
/// </summary>
public sealed class AgentPackageExporter(
    IDbContextFactory<QuestionsHubDbContext> contextFactory,
    QhubExporter exporter,
    ILogger<AgentPackageExporter> logger)
{
    // The graph gate is held only while loading and building; the archive then streams without it. This
    // bounds the temp files that exist at once (each can hold a package's media) until they are disposed.
    // Never waited for while the graph gate is held: a free slot or 503, so waiting exports cannot keep
    // ordinary package reads (and cheap 304s) out of the graph gate.
    private static readonly SemaphoreSlim OpenArchives = new(2, 2);

    /// <exception cref="AgentApiBusyException">Too many package reads or exports are running.</exception>
    public async Task<AgentExportResult> Export(
        ClaimsPrincipal agent, int packageId, IList<EntityTagHeaderValue> ifNoneMatch, CancellationToken ct = default)
    {
        await AgentPackageReader.EnterGraphRead(ct);
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(ct);
            var package = await PackageGraph.LoadSnapshot(context, AgentPackageReader.EditablePackages(context, agent), packageId, ct);

            // EditablePackages applied the rights; CanEditAsAgent is its in-memory twin, kept as a guard.
            if (package == null || !agent.CanEditAsAgent(package))
                return new AgentExportResult.NotFound();

            var manifest = exporter.BuildManifest(package);
            var etag = QhubExporter.ComputeETag(manifest);
            if (Matches(ifNoneMatch, etag))
                return new AgentExportResult.NotModified(etag);

            if (!OpenArchives.Wait(0, CancellationToken.None))
                throw new AgentApiBusyException();

            // The lease is handed to the file only once the file exists, so a failed creation releases the
            // slot here and a half-constructed file (finalized later) has nothing to release.
            var lease = new SlotLease(OpenArchives);
            ArchiveFile file;
            try
            {
                file = new ArchiveFile();
            }
            catch
            {
                lease.Dispose();
                throw;
            }

            file.Lease = lease;
            try
            {
                var missing = await exporter.WriteArchive(manifest, file, ct);
                file.Position = 0;
                logger.LogInformation("Agent token {TokenId} exported package {PackageId} ({Bytes} bytes, {Missing} missing media files)",
                    agent.GetTokenId(), packageId, file.Length, missing);
                return new AgentExportResult.Archive(file, etag, ExportFileName.FromTitle(package.Title) + ".qhub", missing);
            }
            catch
            {
                await file.DisposeAsync();
                throw;
            }
        }
        finally
        {
            AgentPackageReader.ExitGraphRead();
        }
    }

    /// <summary>Weak comparison against an <c>If-None-Match</c> list; <c>*</c> matches any existing package.</summary>
    internal static bool Matches(IList<EntityTagHeaderValue> ifNoneMatch, string etag)
    {
        if (ifNoneMatch.Count == 0)
            return false;

        var current = EntityTagHeaderValue.Parse(etag);
        return ifNoneMatch.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(current, useStrongComparison: false));
    }

    /// <summary>One permit of a semaphore, released at most once.</summary>
    private sealed class SlotLease(SemaphoreSlim slot) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                slot.Release();
        }
    }

    /// <summary>A temp file that deletes itself and ends its <see cref="Lease"/> when disposed (or finalized).</summary>
    private sealed class ArchiveFile() : FileStream(
        Path.Combine(Path.GetTempPath(), $"qh-export-{Guid.NewGuid():N}.qhub"),
        FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
        FileOptions.Asynchronous | FileOptions.DeleteOnClose)
    {
        public SlotLease? Lease { get; set; }

        protected override void Dispose(bool disposing)
        {
            try
            {
                base.Dispose(disposing);
            }
            finally
            {
                Lease?.Dispose();
            }
        }

        public override async ValueTask DisposeAsync()
        {
            try
            {
                await base.DisposeAsync();
            }
            finally
            {
                Lease?.Dispose();
            }
        }
    }
}
