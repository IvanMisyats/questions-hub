using System.Buffers.Binary;
using System.IO.Compression;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>A budget shared by sequential reads in a single extraction.</summary>
internal sealed class ExtractionBudget(long limit, string description)
{
    private readonly string _description = ImportDiagnostics.Label(description);
    public long Used { get; private set; }

    public void Check(long bytes)
    {
        // Subtract rather than sum untrusted ZIP lengths (which may overflow).
        if (bytes < 0 || limit < 0 || bytes > limit - Used)
            throw new ExtractionLimitException(this, $"Перевищено ліміт: {_description} ({PackageImportOptions.FormatFileSize(limit)})");
    }

    public void Add(long bytes)
    {
        Check(bytes);
        Used += bytes;
    }
}

/// <summary>An <see cref="ExtractionBudget"/> was exceeded; <see cref="Budget"/> tells which one.</summary>
internal sealed class ExtractionLimitException(ExtractionBudget budget, string message) : ExtractionException(message)
{
    public ExtractionBudget Budget { get; } = budget;
}

/// <summary>Counts both synchronous XML reads and asynchronous copies/deserialization.</summary>
internal sealed class LimitedExtractionStream(Stream inner, CancellationToken cancellation,
    params ExtractionBudget[] budgets) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    // Budgets are charged in order and the first exceeded one throws, so put the budget whose violation
    // must win (the aggregate one) first: bytes it was charged stay counted when a later one trips.
    private int Count(int bytes)
    {
        foreach (var budget in budgets) budget.Add(bytes);
        return bytes;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        cancellation.ThrowIfCancellationRequested();
        return Count(inner.Read(buffer));
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellation.ThrowIfCancellationRequested();
        return Count(await inner.ReadAsync(buffer, cancellationToken));
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal static class ExtractionLimits
{
    /// <summary>Central-directory bytes allowed per permitted entry (records are ~50 bytes plus the name).</summary>
    private const long MaxDirectoryBytesPerEntry = 1024;

    private const int EndRecordSize = 22;
    private const int Zip64LocatorSize = 20;
    private const int Zip64RecordSize = 56;

    /// <summary>
    /// Bounds the central directory from the end-of-central-directory record, before <see cref="ZipArchive"/>
    /// materializes an object per directory record (a 50 MiB upload could hold about a million). The entry
    /// counts the runtime will use are bounded (it enumerates no more than it expects), and so are the bytes
    /// between the directory's offset and the end record. Strict about ZIP64: when the end record holds a
    /// sentinel that makes the runtime follow the ZIP64 locator, the locator and the ZIP64 record must be
    /// readable and their values are checked, wherever the record lies; otherwise the archive is rejected.
    /// The end record is searched backwards from the end, as the runtime does; an archive without one is left
    /// to ZipArchive to reject. Restores the stream position.
    /// </summary>
    public static void CheckCentralDirectory(Stream stream, PackageImportOptions options)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("The archive stream must be seekable.", nameof(stream));

        var origin = stream.Position;
        try
        {
            var tailLength = (int)Math.Min(stream.Length, EndRecordSize + ushort.MaxValue);
            var tailStart = stream.Length - tailLength;
            var tail = ReadAt(stream, tailStart, tailLength);

            var index = -1;
            for (var i = tailLength - EndRecordSize; i >= 0 && index < 0; i--)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == 0x06054b50)
                    index = i;
            }
            if (index < 0)
                return;

            var end = tail.AsSpan(index, EndRecordSize);
            var endRecordPosition = tailStart + index;
            ulong entriesOnDisk = BinaryPrimitives.ReadUInt16LittleEndian(end[8..]);
            ulong entries = BinaryPrimitives.ReadUInt16LittleEndian(end[10..]);
            ulong directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(end[16..]);

            // The fields that make the runtime (ZipArchive.TryReadZip64EndOfCentralDirectory) look for the ZIP64
            // record; it then uses the record's count and offset. Any other sentinel leaves the ordinary values
            // in use, and those are what gets bounded.
            var zip64 = BinaryPrimitives.ReadUInt16LittleEndian(end[4..]) == ushort.MaxValue
                || entries == ushort.MaxValue
                || directoryOffset == uint.MaxValue;
            if (zip64)
            {
                if (endRecordPosition < Zip64LocatorSize || stream.Length < Zip64RecordSize)
                    throw UnsupportedLayout();
                var locator = ReadAt(stream, endRecordPosition - Zip64LocatorSize, Zip64LocatorSize);
                if (BinaryPrimitives.ReadUInt32LittleEndian(locator) != 0x07064b50)
                    throw UnsupportedLayout();
                var recordPosition = BinaryPrimitives.ReadUInt64LittleEndian(locator.AsSpan(8));
                if (recordPosition > (ulong)(stream.Length - Zip64RecordSize))
                    throw UnsupportedLayout();
                var record = ReadAt(stream, (long)recordPosition, Zip64RecordSize);
                if (BinaryPrimitives.ReadUInt32LittleEndian(record) != 0x06064b50)
                    throw UnsupportedLayout();
                entriesOnDisk = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(24));
                entries = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(32));
                directoryOffset = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(48));
            }

            var maxEntries = (ulong)Math.Max(options.MaxArchiveEntries, 0);
            if (entries > maxEntries || entriesOnDisk > maxEntries
                || (directoryOffset <= (ulong)endRecordPosition
                    && (ulong)endRecordPosition - directoryOffset > maxEntries * MaxDirectoryBytesPerEntry))
                throw TooManyEntries(options);
        }
        finally
        {
            stream.Position = origin;
        }
    }

    private static byte[] ReadAt(Stream stream, long position, int count)
    {
        var buffer = new byte[count];
        stream.Position = position;
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static ExtractionException TooManyEntries(PackageImportOptions options) =>
        new($"Архів містить забагато файлів (ліміт {options.MaxArchiveEntries})");

    private static ExtractionException UnsupportedLayout() =>
        new("Непідтримувана структура архіву (ZIP64 без коректного запису)");

    public static void CheckArchive(ZipArchive zip, PackageImportOptions options)
    {
        if (zip.Entries.Count > options.MaxArchiveEntries)
            throw TooManyEntries(options);

        var total = new ExtractionBudget(options.MaxExpandedBytes, "розпакований архів");
        foreach (var entry in zip.Entries) total.Add(entry.Length);
    }

    public static ExtractionBudget EntryBudget(ZipArchiveEntry entry, long limit, long? ratio = null)
    {
        var budget = new ExtractionBudget(limit, entry.FullName);
        budget.Check(entry.Length);
        if (ratio.HasValue)
        {
            budget = new ExtractionBudget(Math.Min(limit, RatioLimit(entry.CompressedLength, ratio.Value)),
                $"розмір/ступінь стиснення '{ImportDiagnostics.Label(entry.FullName)}'");
            budget.Check(entry.Length);
        }
        return budget;
    }

    /// <summary>The expanded size a compression ratio allows; saturates instead of overflowing (forged ZIP64 lengths, large settings).</summary>
    public static long RatioLimit(long compressedLength, long ratio) =>
        ratio <= 0 || compressedLength < 0 ? 0
        : compressedLength > long.MaxValue / ratio ? long.MaxValue
        : compressedLength * ratio;

    public static async Task CopyToFile(Stream input, string path, CancellationToken ct)
    {
        var created = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            await using var output = File.Create(path);
            created = true;
            await input.CopyToAsync(output, ct);
        }
        catch
        {
            if (created) File.Delete(path);
            throw;
        }
    }
}
