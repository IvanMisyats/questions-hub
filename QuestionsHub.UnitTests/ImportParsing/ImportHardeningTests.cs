using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Infrastructure.Import;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;

namespace QuestionsHub.UnitTests.ImportParsing;

public sealed class ImportHardeningTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "qh-limits-" + Guid.NewGuid().ToString("N"));
    private string Assets => Path.Combine(_temp, "assets");
    private const string MainXml = "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Text</w:t></w:r></w:p></w:body></w:document>";

    public ImportHardeningTests() => Directory.CreateDirectory(_temp);
    public void Dispose() => Directory.Delete(_temp, recursive: true);

    [Fact]
    public void Defaults_MatchCalibratedLimits()
    {
        var o = new PackageImportOptions();
        o.MaxArchiveEntries.Should().Be(1024);
        o.MaxFileSizeBytes.Should().Be(50 * 1024 * 1024);
        o.MaxExpandedBytes.Should().Be(100 * 1024 * 1024);
        o.MaxPackageJsonBytes.Should().Be(4 * 1024 * 1024);
        o.MaxDocxMainXmlBytes.Should().Be(4 * 1024 * 1024);
        o.MaxDocxOtherXmlBytes.Should().Be(1024 * 1024);
        o.MaxDocxTotalXmlBytes.Should().Be(8 * 1024 * 1024);
        o.MaxXmlDepth.Should().Be(64);
        o.MaxXmlElementsPerPart.Should().Be(200_000);
        o.MaxStyleInheritanceDepth.Should().Be(64);
        o.MaxAssetBytes.Should().Be(50 * 1024 * 1024);
        o.MaxTotalAssetBytes.Should().Be(100 * 1024 * 1024);
        o.MaxQhubCompressionRatio.Should().Be(100);
        o.MaxDocxXmlCompressionRatio.Should().Be(500);
        o.MaxConcurrentJobs.Should().Be(1);
    }

    [Theory]
    [InlineData("entries")]
    [InlineData("expanded")]
    [InlineData("json")]
    [InlineData("asset")]
    [InlineData("assets")]
    public async Task Qhub_DeclaredLimits_AreRejected(string limit)
    {
        var options = new PackageImportOptions();
        switch (limit)
        {
            case "entries": options.MaxArchiveEntries = 2; break;
            case "expanded": options.MaxExpandedBytes = Manifest().Length + 7; break;
            case "json": options.MaxPackageJsonBytes = Manifest().Length - 1; break;
            case "asset": options.MaxAssetBytes = 3; break;
            case "assets": options.MaxTotalAssetBytes = 7; break;
        }
        using var zip = Archive(("package.json", Manifest()), ("assets/a.png", new byte[4]), ("assets/b.png", new byte[4]));
        using var client = new HttpClient(new Handler());
        var act = () => Qhub(options, client).Extract(zip, Assets, CancellationToken.None);
        await act.Should().ThrowAsync<ExtractionException>();
        Directory.GetFiles(Assets).Should().BeEmpty();
    }

    [Theory]
    [InlineData("json")]
    [InlineData("asset")]
    [InlineData("assets")]
    [InlineData("expanded")]
    public async Task Qhub_StoredEntriesWithLyingLengths_AreCounted_AndPartialFilesRemoved(string limit)
    {
        var options = new PackageImportOptions();
        var name = limit == "json" ? "package.json" : limit == "expanded" ? "ignored.bin" : "assets/a.png";
        var bytes = limit == "json" ? Manifest() : new byte[150_000];
        using var zip = name == "package.json" ? Archive((name, bytes)) : Archive(("package.json", Manifest()), (name, bytes));
        ForgeUncompressedLength(zip, name, 1);
        switch (limit)
        {
            case "json": options.MaxPackageJsonBytes = bytes.Length - 1; break;
            case "asset": options.MaxAssetBytes = 140_000; break;
            case "assets": options.MaxTotalAssetBytes = 140_000; break;
            case "expanded": options.MaxExpandedBytes = 140_000; break;
        }
        using var client = new HttpClient(new Handler());
        var act = () => Qhub(options, client).Extract(zip, Assets, CancellationToken.None);
        await act.Should().ThrowAsync<ExtractionException>();
        Directory.GetFiles(Assets).Should().BeEmpty();
    }

    [Fact]
    public async Task Qhub_CompressionRatio_IsConfigurable_AndChecked()
    {
        using var zip = Archive(CompressionLevel.SmallestSize, ("package.json", Manifest()), ("ignored.bin", new byte[20_000]));
        using var client = new HttpClient(new Handler());
        var act = () => Qhub(new() { MaxQhubCompressionRatio = 10 }, client).Extract(zip, Assets, CancellationToken.None);
        await act.Should().ThrowAsync<ExtractionException>().WithMessage("*стиснення*");
    }

    [Fact]
    public async Task Qhub_ExactLimits_AreAccepted()
    {
        var manifest = Manifest();
        using var zip = Archive(("package.json", manifest), ("assets/a.png", new byte[4]));
        using var client = new HttpClient(new Handler());
        var options = new PackageImportOptions
        {
            MaxArchiveEntries = 2, MaxPackageJsonBytes = manifest.Length, MaxExpandedBytes = manifest.Length + 4,
            MaxAssetBytes = 4, MaxTotalAssetBytes = 4, MaxQhubCompressionRatio = 1
        };
        var result = await Qhub(options, client).Extract(zip, Assets, CancellationToken.None);
        result.Title.Should().Be("Test");
        File.ReadAllBytes(Path.Combine(Assets, "a.png")).Should().HaveCount(4);
    }

    [Fact]
    public async Task Qhub_ForgedDeflateLength_IsTruncatedByRuntime()
    {
        using var zip = Archive(CompressionLevel.SmallestSize, ("package.json", Manifest()), ("assets/a.png", new byte[10_000]));
        ForgeUncompressedLength(zip, "assets/a.png", 1);
        using var client = new HttpClient(new Handler());
        await Qhub(new() { MaxAssetBytes = 2 }, client).Extract(zip, Assets, CancellationToken.None);
        // DeflateStream is capped by ZipArchive's declared length. Stored entries, tested
        // separately above, expose the compressed length and require actual-byte counters.
        File.ReadAllBytes(Path.Combine(Assets, "a.png")).Should().HaveCount(1);
    }

    [Fact]
    public async Task Qhub_Zip64DeclaredTotals_CannotOverflow()
    {
        using var zip = Zip64WithHugeLengths();
        using var client = new HttpClient(new Handler());
        var act = () => Qhub(new() { MaxExpandedBytes = long.MaxValue }, client).Extract(zip, Assets, CancellationToken.None);
        await act.Should().ThrowAsync<ExtractionException>().WithMessage("*розпакований архів*");
    }

    [Fact]
    public async Task Qhub_CancellationDuringAssetCopy_Propagates_AndRemovesPartialFile()
    {
        using var cts = new CancellationTokenSource();
        using var archive = Archive(("assets/a.png", new byte[20_000]), ("package.json", Manifest()));
        var partialObserved = false;
        using var input = new CancellingArchiveStream(archive.ToArray(), () =>
        {
            partialObserved = new FileInfo(Path.Combine(Assets, "a.png")).Length > 0;
            cts.Cancel();
        });
        using var client = new HttpClient(new Handler());
        var act = () => Qhub(new(), client).Extract(input, Assets, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        partialObserved.Should().BeTrue();
        Directory.GetFiles(Assets).Should().BeEmpty();
    }

    [Fact]
    public async Task Downloads_AreDeduplicated_AndShareTheLocalAssetBudget()
    {
        var handler = new Handler { Content = () => new ByteArrayContent(new byte[4]) };
        using var client = new HttpClient(handler);
        using var zip = Archive(("package.json", Manifest("https://example.org/a.png", "https://example.org/a.png")),
            ("assets/local.png", new byte[4]));
        var result = await Qhub(new() { MaxTotalAssetBytes = 8 }, client).Extract(zip, Assets, CancellationToken.None);
        handler.Requests.Should().Be(1);
        var question = result.Tours[0].Questions[0];
        question.HandoutAssetFileName.Should().Be(question.CommentAssetFileName);
        Directory.GetFiles(Assets).Should().HaveCount(2);
    }

    [Fact]
    public async Task FailedDownloads_AreAttemptedOnlyOnce()
    {
        var handler = new Handler { Status = HttpStatusCode.NotFound };
        using var client = new HttpClient(handler);
        using var zip = Archive(("package.json", Manifest("https://example.org/a.png", "https://example.org/a.png")));
        var result = await Qhub(new(), client).Extract(zip, Assets, CancellationToken.None);
        handler.Requests.Should().Be(1);
        result.Warnings.Should().Contain(w => w.Contains("404"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DownloadLimits_RemovePartialFiles_OneFileIsSkipped_TheTotalFails(bool aggregate)
    {
        var partialObserved = false;
        var handler = new Handler
        {
            Content = () => new StreamContent(new ChunkedStream(new byte[12], read =>
            {
                if (read == 2) partialObserved = Directory.GetFiles(Assets, "dl_*").Length == 1;
            }))
        };
        using var client = new HttpClient(handler);
        using var zip = Archive(("package.json", Manifest("https://example.org/a.png")), ("assets/local.png", new byte[4]));
        var options = aggregate ? new PackageImportOptions { MaxTotalAssetBytes = 9 }
            : new PackageImportOptions { MaxAssetBytes = 5 };
        var act = () => Qhub(options, client).Extract(zip, Assets, CancellationToken.None);
        if (aggregate)
        {
            await act.Should().ThrowAsync<ExtractionException>();
        }
        else
        {
            var result = await act();
            result.Warnings.Should().Contain(w => w.Contains("перевищує ліміт") && w.Contains("https://example.org/a.png"));
            result.Tours[0].Questions[0].HandoutAssetFileName.Should().BeNull();
        }
        partialObserved.Should().BeTrue();
        Directory.GetFiles(Assets).Select(Path.GetFileName).Should().Equal("local.png");
    }

    [Fact]
    public async Task Download_DeclaredContentLength_IsCheckedBeforeWriting()
    {
        var handler = new Handler { Content = () => new ByteArrayContent(new byte[8]) };
        using var client = new HttpClient(handler);
        using var zip = Archive(("package.json", Manifest("https://example.org/a.png")));
        var result = await Qhub(new() { MaxAssetBytes = 7 }, client).Extract(zip, Assets, CancellationToken.None);
        result.Warnings.Should().Contain(w => w.Contains("перевищує ліміт"));
        Directory.GetFiles(Assets).Should().BeEmpty();
    }

    [Fact]
    public async Task Download_CancellationDuringCopy_Propagates_AndRemovesPartialFile()
    {
        using var cts = new CancellationTokenSource();
        var handler = new Handler
        {
            Content = () => new StreamContent(new ChunkedStream(new byte[12], read => { if (read == 2) cts.Cancel(); }))
        };
        using var client = new HttpClient(handler);
        using var zip = Archive(("package.json", Manifest("https://example.org/a.png")));
        var act = () => Qhub(new(), client).Extract(zip, Assets, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFiles(Assets).Should().BeEmpty();
    }

    [Fact]
    public async Task Download_TimeoutOfOneFile_IsAWarning_AndRemovesPartialFile()
    {
        // A cancellation the job did not ask for is the per-download timeout (or HttpClient's).
        var handler = new Handler
        {
            Content = () => new StreamContent(new ChunkedStream(new byte[12], _ => throw new OperationCanceledException()))
        };
        using var client = new HttpClient(handler);
        using var zip = Archive(("package.json", Manifest("https://example.org/a.png")));
        var result = await Qhub(new(), client).Extract(zip, Assets, CancellationToken.None);
        result.Warnings.Should().Contain(w => w.Contains("таймаут"));
        Directory.GetFiles(Assets).Should().BeEmpty();
    }

    [Fact]
    public void CentralDirectory_Zip64TriggeredByADiskField_IsFollowed()
    {
        // The ordinary record claims one entry, but its disk number is the ZIP64 sentinel, so the runtime
        // follows the locator to a ZIP64 record that declares half a million entries.
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            WriteZip64Record(w, entries: 500_000);
            WriteZip64Locator(w, recordOffset: 0);
            WriteEndRecord(w, disk: ushort.MaxValue, entries: 1, commentLength: 0);
        }

        var act = () => ExtractionLimits.CheckCentralDirectory(stream, new PackageImportOptions());
        act.Should().Throw<ExtractionException>().WithMessage("*забагато файлів*");
    }

    [Fact]
    public void CentralDirectory_Zip64RecordInsideTheComment_IsFollowed()
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            WriteZip64Locator(w, recordOffset: 20 + 22);
            WriteEndRecord(w, disk: 0, entries: ushort.MaxValue, commentLength: 56);
            WriteZip64Record(w, entries: 500_000);
        }

        var act = () => ExtractionLimits.CheckCentralDirectory(stream, new PackageImportOptions());
        act.Should().Throw<ExtractionException>().WithMessage("*забагато файлів*");
    }

    [Fact]
    public void CentralDirectory_SizeOnlySentinel_KeepsTheOrdinaryValues()
    {
        // A directory-size sentinel alone does not make the runtime read ZIP64, so the ordinary count
        // (60 000) is what it enumerates — the ZIP64 record claiming one entry must not hide it.
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            WriteZip64Record(w, entries: 1);
            WriteZip64Locator(w, recordOffset: 0);
            w.Write(0x06054b50u); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)60_000); w.Write((ushort)60_000);
            w.Write(uint.MaxValue); w.Write(0u); w.Write((ushort)0);
        }

        var act = () => ExtractionLimits.CheckCentralDirectory(stream, new PackageImportOptions());
        act.Should().Throw<ExtractionException>().WithMessage("*забагато файлів*");
    }

    [Fact]
    public void CentralDirectory_Zip64SentinelWithoutAValidRecord_IsRejected()
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            WriteEndRecord(w, disk: 0, entries: ushort.MaxValue, commentLength: 0);

        var act = () => ExtractionLimits.CheckCentralDirectory(stream, new PackageImportOptions());
        act.Should().Throw<ExtractionException>().WithMessage("*ZIP64*");
    }

    [Fact]
    public async Task CentralDirectory_LongestComment_IsAccepted()
    {
        using var zip = Archive(("package.json", Manifest()));
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Update, leaveOpen: true))
            archive.Comment = new string('x', ushort.MaxValue);
        zip.Position = 0;

        var result = await Qhub(new(), new HttpClient(new Handler())).Extract(zip, Assets, CancellationToken.None);
        result.Tours.Should().ContainSingle();
    }

    [Fact]
    public async Task Download_OneReadOverBothBudgets_FailsOnTheTotal()
    {
        // 4 local bytes leave 4 of the 8-byte total; one 12-byte read exceeds that and the 5-byte per-file
        // limit at once. The total must win, or the import would carry on with more than its total read.
        var handler = new Handler { Content = () => new StreamContent(new ChunkedStream(new byte[12], _ => { }, chunk: 12)) };
        using var client = new HttpClient(handler);
        using var zip = Archive(("package.json", Manifest("https://example.org/a.png")), ("assets/local.png", new byte[4]));
        var act = () => Qhub(new() { MaxTotalAssetBytes = 8, MaxAssetBytes = 5 }, client).Extract(zip, Assets, CancellationToken.None);
        await act.Should().ThrowAsync<ExtractionException>().WithMessage("*усі медіафайли*");
    }

    private static void WriteZip64Record(BinaryWriter w, ulong entries)
    {
        w.Write(0x06064b50u); w.Write(44ul); w.Write((ushort)45); w.Write((ushort)45);
        w.Write(0u); w.Write(0u); w.Write(entries); w.Write(entries); w.Write(0ul); w.Write(0ul);
    }

    private static void WriteZip64Locator(BinaryWriter w, ulong recordOffset)
    {
        w.Write(0x07064b50u); w.Write(0u); w.Write(recordOffset); w.Write(1u);
    }

    private static void WriteEndRecord(BinaryWriter w, ushort disk, ushort entries, ushort commentLength)
    {
        w.Write(0x06054b50u); w.Write(disk); w.Write((ushort)0); w.Write(entries); w.Write(entries);
        w.Write(0u); w.Write(0u); w.Write(commentLength);
    }

    [Fact]
    public async Task Qhub_OversizedCentralDirectory_IsRejectedBeforeOpening()
    {
        // One entry, so the entry count passes; its 3 000-character name makes the directory larger than
        // MaxArchiveEntries × 1 KiB, which is what a directory of a million tiny records looks like too.
        using var zip = Archive(("package.json", Manifest()), ("assets/" + new string('a', 3000) + ".png", new byte[1]));
        var act = () => Qhub(new() { MaxArchiveEntries = 2 }, new HttpClient(new Handler())).Extract(zip, Assets, CancellationToken.None);
        await act.Should().ThrowAsync<ExtractionException>().WithMessage("*забагато файлів*");
        zip.Position.Should().Be(0);
    }

    [Fact]
    public async Task Docx_OversizedCentralDirectory_IsRejectedBeforeOpening()
    {
        var path = Save(Archive(("word/document.xml", Encoding.UTF8.GetBytes(MainXml)),
            ("word/media/" + new string('a', 3000) + ".png", new byte[1])));
        var act = () => Docx(new() { MaxArchiveEntries = 2 }).Extract(path, Assets, CancellationToken.None);
        await act.Should().ThrowAsync<ExtractionException>().WithMessage("*забагато файлів*");
    }

    [Theory]
    [InlineData("entries")]
    [InlineData("expanded")]
    [InlineData("xmlTotal")]
    [InlineData("elements")]
    [InlineData("depth")]
    [InlineData("ratio")]
    public async Task Docx_PreScanLimits_AreEnforcedBeforeOpenXml(string limit)
    {
        var options = new PackageImportOptions();
        switch (limit)
        {
            case "entries": options.MaxArchiveEntries = 1; break;
            case "expanded": options.MaxExpandedBytes = 10; break;
            case "xmlTotal": options.MaxDocxTotalXmlBytes = Encoding.UTF8.GetByteCount(MainXml); break;
            case "elements": options.MaxXmlElementsPerPart = 4; break;
            case "depth": options.MaxXmlDepth = 4; break;
            case "ratio": options.MaxDocxXmlCompressionRatio = 1; break;
        }
        using var zip = Archive(CompressionLevel.SmallestSize,
            ("word/document.xml", Encoding.UTF8.GetBytes(MainXml)), ("extra.xml", "<r/>"u8.ToArray()));
        var act = () => Docx(options).Extract(Save(zip), Assets, CancellationToken.None);
        await act.Should().ThrowAsync<ExtractionException>().WithMessage("*ліміт*");
    }

    [Theory]
    [InlineData("[Content_Types].xml")]
    [InlineData("word/_rels/document.xml.rels")]
    [InlineData("custom/unused.xml")]
    public async Task Docx_AllXmlParts_AreScanned_EvenIfUnused(string name)
    {
        using var zip = Archive(("word/document.xml", Encoding.UTF8.GetBytes(MainXml)),
            (name, "<a><b><c><d><e><f/></e></d></c></b></a>"u8.ToArray()));
        var act = () => Docx(new() { MaxXmlDepth = 5 }).Extract(Save(zip), Assets, CancellationToken.None);
        await act.Should().ThrowAsync<ExtractionException>().WithMessage("*глибини*");
    }

    [Fact]
    public async Task Docx_Dtd_IsProhibited()
    {
        using var zip = Archive(("word/document.xml", "<!DOCTYPE r [<!ENTITY x 'test'>]><r>&x;</r>"u8.ToArray()));
        var act = () => Docx(new()).Extract(Save(zip), Assets, CancellationToken.None);
        await act.Should().ThrowAsync<ExtractionException>().WithMessage("*DTD*");
    }

    [Theory]
    [InlineData("xmlEntry")]
    [InlineData("xmlTotal")]
    [InlineData("expanded")]
    public async Task Docx_LyingStoredLengths_UseActualBytes(string limit)
    {
        var xml = Encoding.UTF8.GetBytes("<r>" + new string('x', 1000) + "</r>");
        var name = limit == "xmlEntry" ? "word/document.xml" : limit == "expanded" ? "ignored.bin" : "unused.xml";
        using var zip = Archive((name, xml));
        ForgeUncompressedLength(zip, name, 1);
        var options = new PackageImportOptions();
        switch (limit)
        {
            case "xmlEntry": options.MaxDocxMainXmlBytes = options.MaxDocxOtherXmlBytes = 999; break;
            case "xmlTotal": options.MaxDocxTotalXmlBytes = 999; break;
            case "expanded": options.MaxExpandedBytes = 999; break;
        }
        var act = () => Docx(options).Extract(Save(zip), Assets, CancellationToken.None);
        await act.Should().ThrowAsync<ExtractionException>().WithMessage("*ліміт*");
    }

    [Fact]
    public async Task Docx_MainPartFoundByItsRelationship_AnyNameOrCase_GetsTheMainLimit()
    {
        // The entry is custom/MAIN.xml, the relationship says custom/main.xml: OPC part names are
        // case-insensitive, so OpenXML opens it, and its limit is the main part's, not the other parts'.
        var main = Main(new string('x', 2000));
        var accepted = await Docx(new() { MaxDocxMainXmlBytes = 4000, MaxDocxOtherXmlBytes = 1000 })
            .Extract(Save(Package(main, mainEntry: "custom/MAIN.xml")), Assets, CancellationToken.None);
        accepted.Blocks.Should().ContainSingle().Which.Text.Should().StartWith("xxx");

        var act = () => Docx(new() { MaxDocxMainXmlBytes = 1500, MaxDocxOtherXmlBytes = 4000 })
            .Extract(Save(Package(main, mainEntry: "custom/MAIN.xml")), Assets, CancellationToken.None);
        (await act.Should().ThrowAsync<ExtractionException>().WithMessage("*ліміт*")).Which.Message.Should().ContainEquivalentOf("custom/main.xml");
    }

    [Fact]
    public async Task Docx_StylesPartWithAnotherExtension_IsScannedBeforeItsDomLoads()
    {
        // styles.bin is not scanned as XML by name, but OpenXML resolves it through its relationship and
        // content type and would load it as a DOM for the paragraph's style.
        var styles = "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
            + string.Concat(Enumerable.Repeat("<a>", 10)) + string.Concat(Enumerable.Repeat("</a>", 10)) + "</w:styles>";
        var path = Save(Package(Main("Text", style: "S1"), mainEntry: "custom/main.xml", stylesEntry: "custom/styles.bin", styles: styles));

        var act = () => Docx(new() { MaxXmlDepth = 8 }).Extract(path, Assets, CancellationToken.None);
        (await act.Should().ThrowAsync<ExtractionException>().WithMessage("*глибини*")).Which.Message.Should().Contain("styles.bin");
    }

    [Fact]
    public async Task Docx_MainPartWithAnotherExtension_CountsTowardsTheXmlTotal()
    {
        var main = Main(new string('x', 3000));
        var act = () => Docx(new() { MaxDocxTotalXmlBytes = 2000 })
            .Extract(Save(Package(main, mainEntry: "custom/main.bin")), Assets, CancellationToken.None);
        await act.Should().ThrowAsync<ExtractionException>().WithMessage("*усі XML-частини*");
    }

    [Fact]
    public async Task Docx_MainPartWithAnotherExtension_GetsTheXmlCompressionRatio()
    {
        var main = Main(new string('x', 3000));
        var act = () => Docx(new() { MaxDocxXmlCompressionRatio = 2 })
            .Extract(Save(Package(main, mainEntry: "custom/main.bin", compression: CompressionLevel.SmallestSize)), Assets, CancellationToken.None);
        await act.Should().ThrowAsync<ExtractionException>().WithMessage("*ступінь стиснення*main.bin*");
    }

    [Fact]
    public async Task Docx_StylesPart_GetsTheOtherPartLimit()
    {
        var styles = "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
            + new string(' ', 2000) + "</w:styles>";
        var path = Save(Package(Main("Text"), stylesEntry: "word/styles.xml", styles: styles));

        var act = () => Docx(new() { MaxDocxOtherXmlBytes = 1000 }).Extract(path, Assets, CancellationToken.None);
        (await act.Should().ThrowAsync<ExtractionException>().WithMessage("*ліміт*")).Which.Message.Should().Contain("styles.xml");
    }

    private static string Main(string text, string? style = null) =>
        "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p>"
        + (style == null ? "" : $"<w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr>")
        + $"<w:r><w:t>{text}</w:t></w:r></w:p></w:body></w:document>";

    /// <summary>A hand-built OPC package, so part names, casing and content types can be anything.</summary>
    private static MemoryStream Package(string mainXml, string mainEntry = "word/document.xml",
        string? stylesEntry = null, string? styles = null, CompressionLevel compression = CompressionLevel.NoCompression)
    {
        const string relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string officeDocument = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var mainFolder = mainEntry[..(mainEntry.LastIndexOf('/') + 1)];
        var overrides = $"<Override PartName=\"/{mainEntry}\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>";
        var entries = new List<(string, byte[])>
        {
            ("_rels/.rels", Encoding.UTF8.GetBytes($"<Relationships xmlns=\"{relationships}\"><Relationship Id=\"rId1\" Type=\"{officeDocument}/officeDocument\" Target=\"{mainEntry.ToLowerInvariant()}\"/></Relationships>")),
            (mainEntry, Encoding.UTF8.GetBytes(mainXml)),
        };
        if (stylesEntry != null)
        {
            overrides += $"<Override PartName=\"/{stylesEntry}\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>";
            entries.Add(($"{mainFolder}_rels/{mainEntry[mainFolder.Length..]}.rels", Encoding.UTF8.GetBytes(
                $"<Relationships xmlns=\"{relationships}\"><Relationship Id=\"rId1\" Type=\"{officeDocument}/styles\" Target=\"{stylesEntry[mainFolder.Length..]}\"/></Relationships>")));
            entries.Add((stylesEntry, Encoding.UTF8.GetBytes(styles!)));
        }
        entries.Insert(0, ("[Content_Types].xml", Encoding.UTF8.GetBytes(
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
            + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" + overrides + "</Types>")));
        return Archive(compression, [.. entries]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Docx_StyleCyclesAndDepth_Terminate(bool cycle)
    {
        var path = CreateDocx(main =>
        {
            main.AddNewPart<StyleDefinitionsPart>().Styles = new Styles(
                new Style(new BasedOn { Val = "B" }) { StyleId = "A" },
                new Style(new BasedOn { Val = cycle ? "A" : "C" }) { StyleId = "B" },
                new Style(new StyleRunProperties(new FontSize { Val = "36" })) { StyleId = "C" });
            main.Document.Body!.Append(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "A" }), new Run(new Text("Test"))));
        });
        var result = await Docx(new() { MaxStyleInheritanceDepth = cycle ? 64 : 2 }).Extract(path, Assets, CancellationToken.None);
        result.Blocks.Single().FontSizeHalfPoints.Should().BeNull();
    }

    [Fact]
    public async Task Docx_NormalStyleInheritance_IsPreserved()
    {
        var path = CreateDocx(main =>
        {
            main.AddNewPart<StyleDefinitionsPart>().Styles = new Styles(
                new Style(new BasedOn { Val = "B" }) { StyleId = "A" },
                new Style(new StyleRunProperties(new FontSize { Val = "36" })) { StyleId = "B" });
            main.Document.Body!.Append(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "A" }), new Run(new Text("Test"))));
        });
        var result = await Docx(new()).Extract(path, Assets, CancellationToken.None);
        result.Blocks.Single().FontSizeHalfPoints.Should().Be(36);
    }

    [Fact]
    public async Task Docx_RepeatedImage_IsOneFile_ButEachOccurrenceCanBeAssociated()
    {
        var path = CreateDocx(main =>
        {
            var id = AddImage(main, 4);
            main.Document.Body!.Append(new Paragraph(new Run(new Text("Тур 1"))),
                new Paragraph(new Run(new Text("1. [Роздатка: зображення] Текст"), Drawing(id), new Break(),
                    new Text("Відповідь: Так"), new Break(), new Text("Коментар: Текст"), Drawing(id))),
                new Paragraph(new Run(new Text("2. Інший текст"), Drawing(id))),
                new Paragraph(new Run(new Text("Відповідь: Так"))));
        });
        var result = await Docx(new() { MaxTotalAssetBytes = 4 }).Extract(path, Assets, CancellationToken.None);
        result.Assets.Should().ContainSingle();
        Directory.GetFiles(Assets).Should().ContainSingle();
        result.Blocks.SelectMany(b => b.Assets).Should().HaveCount(3);
        var parsed = new PackageParser(NullLogger<PackageParser>.Instance).Parse(result.Blocks, result.Assets);
        var questions = parsed.Tours.Single().Questions;
        questions.Should().HaveCount(2);
        questions[0].HandoutAssetFileName.Should().Be(result.Assets[0].FileName);
        questions[0].CommentAssetFileName.Should().Be(result.Assets[0].FileName);
        questions[1].HandoutAssetFileName.Should().Be(result.Assets[0].FileName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Docx_ImageLimits_Propagate(bool aggregate)
    {
        var path = CreateDocx(main => main.Document.Body!.Append(new Paragraph(new Run(
            Drawing(AddImage(main, 4)), Drawing(AddImage(main, 4))))));
        var options = aggregate ? new PackageImportOptions { MaxTotalAssetBytes = 7 }
            : new PackageImportOptions { MaxAssetBytes = 3 };
        var act = () => Docx(options).Extract(path, Assets, CancellationToken.None);
        await act.Should().ThrowAsync<ExtractionException>();
        Directory.GetFiles(Assets).Length.Should().Be(aggregate ? 1 : 0);
    }

    [Fact]
    public async Task Docx_CancellationInsideImageExtraction_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var path = CreateDocx(main =>
        {
            var id = AddImage(main, 4);
            main.Document.Body!.Append(new Paragraph(new Run(Drawing(id), Drawing(id))));
        });
        var extractor = new DocxExtractor(new CancelOnImageLogger(cts));
        var act = () => extractor.Extract(path, Assets, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFiles(Assets).Should().ContainSingle();
    }

    [Fact]
    public async Task Docx_Numbering_TracksEachListAndLevel_AcrossUnnumberedParagraphs()
    {
        var path = CreateDocx(main =>
        {
            main.AddNewPart<NumberingDefinitionsPart>().Numbering = new Numbering(
                new AbstractNum(
                    new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1." }) { LevelIndex = 0 },
                    new Level(new NumberingFormat { Val = NumberFormatValues.LowerLetter }, new LevelText { Val = "%2)" }) { LevelIndex = 1 }) { AbstractNumberId = 0 },
                new NumberingInstance(new AbstractNumId { Val = 0 }) { NumberID = 1 },
                new NumberingInstance(new AbstractNumId { Val = 0 }) { NumberID = 2 });
            foreach (var (id, level) in new[] { (1, 0), (1, 1), (2, 0), (0, 0), (1, 0), (1, 1), (2, 0) })
                main.Document.Body!.Append(new Paragraph(new ParagraphProperties(new NumberingProperties(
                    new NumberingLevelReference { Val = level }, new NumberingId { Val = id })), new Run(new Text("Text"))));
        });
        var result = await Docx(new()).Extract(path, Assets, CancellationToken.None);
        result.Blocks.Select(b => b.Text).Should().Equal("1.\tText", "a)\tText", "1.\tText", "Text", "2.\tText", "b)\tText", "2.\tText");
    }

    private static QhubExtractor Qhub(PackageImportOptions options, HttpClient client) =>
        new(new ClientFactory(client), NullLogger<QhubExtractor>.Instance, Options.Create(options));
    private static DocxExtractor Docx(PackageImportOptions options) => new(NullLogger<DocxExtractor>.Instance, Options.Create(options));

    private static byte[] Manifest(string? handout = null, string? comment = null) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        formatVersion = "1.0", title = "Test", tours = new[] { new { number = "1", questions = new[] {
            new { number = "1", text = "Text", answer = "Answer", handoutAssetUrl = handout, commentAssetUrl = comment } } } }
    });

    private static MemoryStream Archive(params (string Name, byte[] Bytes)[] entries) => Archive(CompressionLevel.NoCompression, entries);

    private static MemoryStream Archive(CompressionLevel compression, params (string Name, byte[] Bytes)[] entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, bytes) in entries)
            {
                using var output = zip.CreateEntry(name, compression).Open();
                output.Write(bytes);
            }
        stream.Position = 0;
        return stream;
    }

    private static void ForgeUncompressedLength(MemoryStream stream, string name, uint length)
    {
        var bytes = stream.GetBuffer().AsSpan(0, (int)stream.Length);
        for (var i = 0; i < bytes.Length - 46; i++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[i..]) != 0x02014b50) continue;
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(i + 28)..]);
            if (Encoding.UTF8.GetString(bytes.Slice(i + 46, nameLength)) != name) continue;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[(i + 24)..], length);
            var localOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[(i + 42)..]);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[(localOffset + 22)..], length);
            return;
        }
        throw new InvalidOperationException("Entry not found");
    }

    private static MemoryStream Zip64WithHugeLengths()
    {
        // Two empty stored entries whose ZIP64 extra fields claim long.MaxValue each.
        // No payload is read: the declared total must fail before any entry is opened.
        var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        var offsets = new List<uint>();
        foreach (var name in new[] { "a", "b" })
        {
            offsets.Add((uint)stream.Position);
            writer.Write(0x04034b50u);
            writer.Write((ushort)45);
            writer.Write(new byte[12]); // flags, method, time/date, CRC
            writer.Write(0u); // compressed length
            writer.Write(uint.MaxValue);
            writer.Write((ushort)1); // filename length
            writer.Write((ushort)12); // ZIP64 extra length
            writer.Write(Encoding.ASCII.GetBytes(name));
            writer.Write((ushort)1); writer.Write((ushort)8); writer.Write(long.MaxValue);
        }
        var centralOffset = (uint)stream.Position;
        for (var i = 0; i < 2; i++)
        {
            writer.Write(0x02014b50u);
            writer.Write((ushort)45); writer.Write((ushort)45);
            writer.Write(new byte[12]);
            writer.Write(0u); writer.Write(uint.MaxValue);
            writer.Write((ushort)1); writer.Write((ushort)12);
            writer.Write(new byte[10]); // comment length, disk, internal/external attributes
            writer.Write(offsets[i]);
            writer.Write((byte)('a' + i));
            writer.Write((ushort)1); writer.Write((ushort)8); writer.Write(long.MaxValue);
        }
        var centralLength = (uint)stream.Position - centralOffset;
        writer.Write(0x06054b50u); writer.Write(0u);
        writer.Write((ushort)2); writer.Write((ushort)2);
        writer.Write(centralLength); writer.Write(centralOffset); writer.Write((ushort)0);
        stream.Position = 0;
        return stream;
    }

    private string Save(MemoryStream zip)
    {
        var path = Path.Combine(_temp, Guid.NewGuid().ToString("N") + ".docx");
        File.WriteAllBytes(path, zip.ToArray());
        return path;
    }

    private string CreateDocx(Action<MainDocumentPart> configure)
    {
        var path = Path.Combine(_temp, Guid.NewGuid().ToString("N") + ".docx");
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        configure(main);
        return path;
    }

    private static string AddImage(MainDocumentPart main, int length)
    {
        var image = main.AddImagePart(ImagePartType.Png);
        using var stream = new MemoryStream(new byte[length]);
        image.FeedData(stream);
        return main.GetIdOfPart(image);
    }

    private static Drawing Drawing(string id) => new(new DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline(
        new A.Graphic(new A.GraphicData(new DocumentFormat.OpenXml.Drawing.Pictures.Picture(
            new DocumentFormat.OpenXml.Drawing.Pictures.BlipFill(new A.Blip { Embed = id })))
        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })));

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class Handler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public Func<HttpContent> Content { get; init; } = () => new ByteArrayContent([]);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = Content() });
        }
    }

    private sealed class ChunkedStream(byte[] bytes, Action<int> onRead, int chunk = 2) : Stream
    {
        private int _position;
        private int _reads;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            onRead(++_reads);
            var count = Math.Min(chunk, Math.Min(buffer.Length, bytes.Length - _position));
            bytes.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = Read(buffer.Span);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(count);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CancellingArchiveStream(byte[] bytes, Action cancel) : MemoryStream(bytes)
    {
        private int _reads;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (++_reads == 2) cancel();
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, 4096)], cancellationToken);
        }
    }

    private sealed class CancelOnImageLogger(CancellationTokenSource cts) : ILogger<DocxExtractor>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).StartsWith("Extracted image:", StringComparison.Ordinal)) cts.Cancel();
        }
    }
}
