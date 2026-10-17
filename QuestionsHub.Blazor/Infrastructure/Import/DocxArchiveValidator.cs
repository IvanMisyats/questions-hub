using System.IO.Compression;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Bounds ZIP expansion and XML complexity before OpenXML builds any DOM, in two steps:
/// <see cref="Validate"/> before the package is opened (every entry: sizes, ratios, and a streaming scan of
/// each <c>.xml</c>/<c>.rels</c> entry — what System.IO.Packaging parses itself), and
/// <see cref="ValidateLoadedParts"/> after it is opened but before the extractor touches a DOM: the parts the
/// extractor loads, found the way OpenXML finds them (relationships and content types, whatever their names
/// or casing), get their own byte, depth and element limits.
/// </summary>
internal static class DocxArchiveValidator
{
    /// <summary>What <see cref="Validate"/> learned, for <see cref="ValidateLoadedParts"/>.</summary>
    /// <param name="Entries">Compressed length of each entry and whether it was scanned and counted as XML, by name (case-insensitive like OPC part names).</param>
    /// <param name="XmlBytes">Bytes of the XML entries read so far, towards <see cref="PackageImportOptions.MaxDocxTotalXmlBytes"/>.</param>
    public sealed record Precheck(IReadOnlyDictionary<string, (long CompressedLength, bool IsXml)> Entries, long XmlBytes);

    public static async Task<Precheck> Validate(Stream input, PackageImportOptions options, CancellationToken ct)
    {
        ExtractionLimits.CheckCentralDirectory(input, options);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        ExtractionLimits.CheckArchive(zip, options);

        // Which part is the main document is only known once OpenXML resolves the relationships, so every
        // XML entry may be as large as the larger of the two limits here; ValidateLoadedParts is exact.
        var xmlEntryLimit = Math.Max(options.MaxDocxMainXmlBytes, options.MaxDocxOtherXmlBytes);
        var expanded = new ExtractionBudget(options.MaxExpandedBytes, "розпакований архів");
        var xmlTotal = XmlTotal(options);
        var declaredXml = XmlTotal(options);
        var entries = new Dictionary<string, (long, bool)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            if (IsXml(entry))
                declaredXml.Add(entry.Length);
            ExtractionLimits.EntryBudget(entry, IsXml(entry) ? xmlEntryLimit : options.MaxAssetBytes,
                IsXml(entry) ? options.MaxDocxXmlCompressionRatio : null);
            entries.TryAdd(entry.FullName, (entry.CompressedLength, IsXml(entry)));
        }

        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var isXml = IsXml(entry);
            var budget = ExtractionLimits.EntryBudget(entry, isXml ? xmlEntryLimit : options.MaxAssetBytes,
                isXml ? options.MaxDocxXmlCompressionRatio : null);
            await using var stream = new LimitedExtractionStream(entry.Open(), ct,
                isXml ? [expanded, xmlTotal, budget] : [expanded, budget]);
            if (isXml)
                ScanXml(stream, entry.FullName, options, ct);

            // Count any trailing bytes, even if a reader implementation stops early.
            await stream.CopyToAsync(Stream.Null, ct);
        }

        return new Precheck(entries, xmlTotal.Used);
    }

    /// <summary>
    /// Scans the parts <see cref="DocxExtractor"/> loads as a DOM — the main document, styles and numbering —
    /// before any of them is loaded. Getting a part object and its stream does not build its DOM. A part whose
    /// entry was not scanned as XML before (another extension) also counts towards the XML total and gets the
    /// XML compression-ratio limit here.
    /// </summary>
    public static void ValidateLoadedParts(WordprocessingDocument document, Precheck precheck, PackageImportOptions options, CancellationToken ct)
    {
        if (document.MainDocumentPart is not { } main)
            return;

        var xmlTotal = XmlTotal(options);
        xmlTotal.Add(precheck.XmlBytes);
        ScanPart(main, options.MaxDocxMainXmlBytes);
        if (main.StyleDefinitionsPart is { } styles)
            ScanPart(styles, options.MaxDocxOtherXmlBytes);
        if (main.NumberingDefinitionsPart is { } numbering)
            ScanPart(numbering, options.MaxDocxOtherXmlBytes);

        void ScanPart(OpenXmlPart part, long limit)
        {
            var name = part.Uri.OriginalString;
            var budgets = new List<ExtractionBudget> { new(limit, name) };
            var known = precheck.Entries.TryGetValue(Uri.UnescapeDataString(name.TrimStart('/')), out var entry);
            if (!known || !entry.IsXml)
            {
                // Not counted as XML yet; without its entry (an unexpected name form) only the byte limit applies.
                budgets.Insert(0, xmlTotal);
                if (known)
                    budgets.Add(new ExtractionBudget(ExtractionLimits.RatioLimit(entry.CompressedLength, options.MaxDocxXmlCompressionRatio),
                        $"розмір/ступінь стиснення '{ImportDiagnostics.Label(name)}'"));
            }

            using var stream = new LimitedExtractionStream(part.GetStream(FileMode.Open, FileAccess.Read), ct, [.. budgets]);
            ScanXml(stream, name, options, ct);
        }
    }

    /// <summary>Reads the XML through, enforcing depth and element count; DTDs are refused.</summary>
    private static void ScanXml(Stream stream, string name, PackageImportOptions options, CancellationToken ct)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            CloseInput = false
        });
        long elements = 0;
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element)
                continue;
            if (++elements > options.MaxXmlElementsPerPart || reader.Depth + 1 > options.MaxXmlDepth)
                throw new ExtractionException($"Перевищено ліміт глибини або кількості елементів XML: '{ImportDiagnostics.Label(name)}'");
        }
    }

    private static ExtractionBudget XmlTotal(PackageImportOptions options) =>
        new(options.MaxDocxTotalXmlBytes, "усі XML-частини DOCX");

    private static bool IsXml(ZipArchiveEntry entry) =>
        entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
        || entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);
}
