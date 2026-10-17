using System.IO.Compression;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuestionsHub.Blazor.Infrastructure.Import;
using Xunit;

namespace QuestionsHub.UnitTests.ImportParsing;

public sealed class ImportDiagnosticsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "qh-diagnostics-" + Guid.NewGuid().ToString("N"));

    public ImportDiagnosticsTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, true);

    private async Task<ParseResult> Extract(object tour)
    {
        using var provider = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var extractor = new QhubExtractor(provider.GetRequiredService<IHttpClientFactory>(), NullLogger<QhubExtractor>.Instance);
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry("package.json", CompressionLevel.NoCompression).Open()))
            writer.Write(JsonSerializer.Serialize(new { formatVersion = "1.2", title = "Package", tours = new[] { tour } }));
        stream.Position = 0;
        return await extractor.Extract(stream, Path.Combine(_folder, "assets"), default);
    }

    [Fact]
    public async Task Qhub_WarningFlood_IsBounded_AndDoesNotTruncateData()
    {
        var number = new string('Т', 100_000);
        var result = await Extract(new
        {
            number,
            questions = Enumerable.Range(0, 1000).Select(i => new { number = i.ToString() }).ToArray()
        });
        result.Tours[0].Number.Should().Be(number);
        result.TotalQuestions.Should().Be(1000);
        result.Warnings.Should().HaveCount(501);
        result.Warnings[^1].Should().Be("… і ще 1500 попереджень");
        result.Warnings.Should().OnlyContain(w => w.Length < 200);
        result.Warnings[0].Should().Contain(new string('Т', 59) + "…");
        // Debug artifacts continue to serialize and deserialize a simple warnings array.
        JsonDocument.Parse(JsonSerializer.Serialize(result)).RootElement.GetProperty("Warnings")[500].GetString().Should().Be(result.Warnings[^1]);
    }

    [Fact]
    public async Task Qhub_QuestionNumber_FileName_AndUrl_AreShortDiagnostics()
    {
        var large = new string('x', 100_000);
        var result = await Extract(new { number = "1", questions = new[] { new
        {
            number = large, handoutAssetFileName = large + ".png", handoutAssetUrl = large
        } } });
        result.Warnings.Should().HaveCount(4);
        result.Warnings.Should().OnlyContain(w => w.Length < 300);
        result.Tours[0].Questions[0].Number.Should().Be(large);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Qhub_MixedTour_IsRejected_EvenWithEmptyArrays(bool empty)
    {
        var action = () => Extract(new
        {
            number = "Фінал",
            questions = empty ? [] : new[] { new { number = "1", text = "Q", answer = "A" } },
            blocks = empty ? [] : new[] { new { name = "A", questions = new[] { new { number = "2", text = "Q", answer = "A" } } } }
        });
        var error = (await action.Should().ThrowAsync<ExtractionException>()).Which;
        error.UserMessage.Should().Contain("Фінал").And.Contain("questions").And.Contain("blocks");
    }

    [Fact]
    public void DocxParser_WarningFlood_IsBounded()
    {
        var blocks = new List<DocBlock> { new() { Index = 0, Text = "Тестовий пакет" }, new() { Index = 1, Text = "Тур 1" } };
        for (var i = 1; i <= 1000; i++)
            blocks.Add(new DocBlock { Index = i, Text = $"Питання {i}. Текст запитання?" });
        var result = new PackageParser(NullLogger<PackageParser>.Instance).Parse(blocks, []);
        result.TotalQuestions.Should().Be(1000);
        result.Warnings.Should().HaveCount(501);
        result.Warnings[^1].Should().StartWith("… і ще ");
    }

    [Theory]
    [InlineData("%1.", "1.")]
    [InlineData(null, null)]
    public async Task Docx_NumberingPrefix_IsBounded_AndNormalPrefixIsUnchanged(string? pattern, string? expected)
    {
        var path = Path.Combine(_folder, "numbered.docx");
        using (var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            var numbering = main.AddNewPart<NumberingDefinitionsPart>();
            numbering.Numbering = new Numbering(
                new AbstractNum(new Level(new StartNumberingValue { Val = 1 },
                    new NumberingFormat { Val = NumberFormatValues.Decimal },
                    new LevelText { Val = pattern ?? new string('N', 20_000) }) { LevelIndex = 0 }) { AbstractNumberId = 1 },
                new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 });
            main.Document = new Document(new Body(Enumerable.Range(0, 100).Select(_ => new Paragraph(
                new ParagraphProperties(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 })),
                new Run(new Text("text"))))));
            main.Document.Save();
        }
        var extractor = new DocxExtractor(NullLogger<DocxExtractor>.Instance);
        var result = await extractor.Extract(path, Path.Combine(_folder, "assets"), default);
        result.Blocks.Should().HaveCount(100);
        result.Blocks.Should().OnlyContain(b => b.Text.Length <= 32 + 1 + 4);
        result.Blocks[0].Text.Should().Be((expected ?? new string('N', 31) + "…") + "\ttext");
    }

    [Fact]
    public void WarningLimit_PrependingAndAdding_KeepCountAndOrder()
    {
        var warnings = new ImportWarnings();
        for (var i = 0; i < 510; i++) warnings.Add(i.ToString());
        warnings.InsertRange(0, ["extract 1", "extract 2"]);
        warnings.Add("last");
        warnings.Should().HaveCount(501);
        warnings.Take(3).Should().Equal("extract 1", "extract 2", "0");
        warnings[^1].Should().Be("… і ще 13 попереджень");
    }

    [Fact]
    public void ExtractionBudget_LongEntryName_IsShortInLimitError()
    {
        using var stream = new MemoryStream();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        var entry = zip.CreateEntry(new string('x', 10_000));
        // A direct budget also covers callers using part names instead of ZIP entries.
        var budget = new ExtractionBudget(1, entry.FullName);
        var action = () => budget.Add(2);
        action.Should().Throw<ExtractionException>().Which.UserMessage.Length.Should().BeLessThan(150);
    }
}
