using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Import;
using Xunit;

namespace QuestionsHub.UnitTests.ImportParsing;

/// <summary>
/// Unit tests for QhubExtractor — parses .qhub (ZIP + package.json) into ParseResult.
/// </summary>
public class QhubExtractorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _assetsDir;

    public QhubExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "qhub_tests_" + Guid.NewGuid().ToString("N"));
        _assetsDir = Path.Combine(_tempDir, "assets");
        Directory.CreateDirectory(_assetsDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    #region Helpers

    private static QhubExtractor CreateExtractor(HttpMessageHandler? handler = null)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("QhubAssetDownloader")
            .ConfigurePrimaryHttpMessageHandler(() => handler ?? new FakeHttpHandler());
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        return new QhubExtractor(factory, NullLogger<QhubExtractor>.Instance);
    }

    private static MemoryStream CreateQhubZip(object packageJson, Dictionary<string, byte[]>? assets = null)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("package.json");
            using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
            {
                var json = JsonSerializer.Serialize(packageJson, new JsonSerializerOptions { WriteIndented = true });
                writer.Write(json);
            }

            if (assets != null)
            {
                foreach (var (name, data) in assets)
                {
                    var assetEntry = zip.CreateEntry($"assets/{name}");
                    using var stream = assetEntry.Open();
                    stream.Write(data, 0, data.Length);
                }
            }
        }
        ms.Position = 0;
        return ms;
    }

    private static object MinimalPackageJson(
        string title = "Тестовий пакет",
        string? numberingMode = null,
        List<object>? tours = null)
    {
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = title,
            ["tours"] = tours ?? new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["number"] = "1",
                    ["questions"] = new List<object>
                    {
                        new Dictionary<string, object?>
                        {
                            ["number"] = "1",
                            ["text"] = "Тестове запитання",
                            ["answer"] = "Тестова відповідь"
                        }
                    }
                }
            }
        };

        if (numberingMode != null)
            pkg["numberingMode"] = numberingMode;

        return pkg;
    }

    private static Dictionary<string, object?> MakeTour(
        string number,
        List<object>? questions = null,
        List<object>? blocks = null,
        bool? isWarmup = null,
        List<string>? editors = null,
        string? preamble = null)
    {
        var tour = new Dictionary<string, object?>
        {
            ["number"] = number
        };

        if (questions != null) tour["questions"] = questions;
        if (blocks != null) tour["blocks"] = blocks;
        if (isWarmup == true) tour["isWarmup"] = true;
        if (editors != null) tour["editors"] = editors;
        if (preamble != null) tour["preamble"] = preamble;

        return tour;
    }

    private static Dictionary<string, object?> MakeQuestion(
        string number,
        string text = "Запитання",
        string answer = "Відповідь",
        string? handoutAssetFileName = null,
        string? handoutAssetUrl = null,
        string? commentAssetFileName = null,
        string? commentAssetUrl = null,
        string? source = null,
        List<string>? authors = null)
    {
        var q = new Dictionary<string, object?>
        {
            ["number"] = number,
            ["text"] = text,
            ["answer"] = answer
        };

        if (handoutAssetFileName != null) q["handoutAssetFileName"] = handoutAssetFileName;
        if (handoutAssetUrl != null) q["handoutAssetUrl"] = handoutAssetUrl;
        if (commentAssetFileName != null) q["commentAssetFileName"] = commentAssetFileName;
        if (commentAssetUrl != null) q["commentAssetUrl"] = commentAssetUrl;
        if (source != null) q["source"] = source;
        if (authors != null) q["authors"] = authors;

        return q;
    }

    private static Dictionary<string, object?> MakeBlock(
        List<object> questions,
        string? name = null,
        List<string>? editors = null,
        string? preamble = null)
    {
        var b = new Dictionary<string, object?>
        {
            ["questions"] = questions
        };

        if (name != null) b["name"] = name;
        if (editors != null) b["editors"] = editors;
        if (preamble != null) b["preamble"] = preamble;

        return b;
    }

    /// <summary>A fake HTTP handler that returns configured responses.</summary>
    private class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, byte[] Content, string ContentType)> _responses = new();
        private readonly HttpStatusCode _defaultStatus = HttpStatusCode.NotFound;

        public void AddResponse(string url, byte[] content, string contentType = "image/png",
            HttpStatusCode status = HttpStatusCode.OK)
        {
            _responses[url] = (status, content, contentType);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (_responses.TryGetValue(url, out var resp))
            {
                var response = new HttpResponseMessage(resp.Status)
                {
                    Content = new ByteArrayContent(resp.Content)
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(resp.ContentType);
                response.Content.Headers.ContentLength = resp.Content.Length;
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(_defaultStatus));
        }
    }

    #endregion

    #region Basic Extraction

    [Fact]
    public async Task Extract_MinimalPackage_ReturnsParsedResult()
    {
        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(MinimalPackageJson());

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Title.Should().Be("Тестовий пакет");
        result.Tours.Should().HaveCount(1);
        result.Tours[0].Number.Should().Be("1");
        result.Tours[0].Questions.Should().HaveCount(1);
        result.Tours[0].Questions[0].Text.Should().Be("Тестове запитання");
        result.Tours[0].Questions[0].Answer.Should().Be("Тестова відповідь");
        result.Confidence.Should().Be(1.0);
    }

    [Fact]
    public async Task Extract_PackageWithAllFields_MapsCorrectly()
    {
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["sourceUrl"] = "https://example.com/source",
            ["title"] = "Повний пакет",
            ["description"] = "Опис пакету",
            ["preamble"] = "Преамбула",
            ["playedFrom"] = "2025-03-15",
            ["playedTo"] = "2025-03-16",
            ["numberingMode"] = "PerTour",
            ["sharedEditors"] = true,
            ["editors"] = new List<string> { "Іван Місяць", "Андрій Пундор" },
            ["tags"] = new List<string> { "2025", "ЛУК" },
            ["tours"] = new List<object>
            {
                MakeTour("1", questions: new List<object>
                {
                    MakeQuestion("1", "Текст", "Відповідь", authors: new List<string> { "Іван Місяць" })
                })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Title.Should().Be("Повний пакет");
        result.Description.Should().Be("Опис пакету");
        result.Preamble.Should().Be("Преамбула");
        result.SourceUrl.Should().Be("https://example.com/source");
        result.PlayedFrom.Should().Be(new DateOnly(2025, 3, 15));
        result.PlayedTo.Should().Be(new DateOnly(2025, 3, 16));
        result.NumberingMode.Should().Be(QuestionNumberingMode.PerTour);
        result.SharedEditors.Should().BeTrue();
        result.PackageEditors.Should().BeEquivalentTo(new[] { "Іван Місяць", "Андрій Пундор" });
        result.Tags.Should().BeEquivalentTo(new[] { "2025", "ЛУК" });
    }

    #endregion

    #region Validation & Warnings

    [Fact]
    public async Task Extract_MissingPackageJson_ThrowsExtractionException()
    {
        var extractor = CreateExtractor();
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("readme.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("not package.json");
        }
        ms.Position = 0;

        var act = () => extractor.Extract(ms, _assetsDir, CancellationToken.None);

        await act.Should().ThrowAsync<ExtractionException>()
            .WithMessage("*package.json*");
    }

    [Fact]
    public async Task Extract_NoTours_ThrowsExtractionException()
    {
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Порожній пакет",
            ["tours"] = new List<object>()
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var act = () => extractor.Extract(zip, _assetsDir, CancellationToken.None);

        await act.Should().ThrowAsync<ExtractionException>()
            .WithMessage("*туру*");
    }

    [Fact]
    public async Task Extract_WrongFormatVersion_AddsWarning()
    {
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "2.0",
            ["title"] = "Пакет",
            ["tours"] = new List<object>
            {
                MakeTour("1", questions: new List<object>
                {
                    MakeQuestion("1")
                })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Warnings.Should().Contain(w => w.Contains("версія формату") && w.Contains("2.0"));
    }

    [Fact]
    public async Task Extract_MissingFormatVersion_AddsWarning()
    {
        var pkg = new Dictionary<string, object?>
        {
            ["title"] = "Пакет",
            ["tours"] = new List<object>
            {
                MakeTour("1", questions: new List<object> { MakeQuestion("1") })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Warnings.Should().Contain(w => w.Contains("formatVersion"));
    }

    [Fact]
    public async Task Extract_MissingTitle_AddsWarning()
    {
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["tours"] = new List<object>
            {
                MakeTour("1", questions: new List<object> { MakeQuestion("1") })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Warnings.Should().Contain(w => w.Contains("Назва пакету"));
    }

    [Fact]
    public async Task Extract_MissingQuestionText_AddsWarning()
    {
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["tours"] = new List<object>
            {
                MakeTour("1", questions: new List<object>
                {
                    new Dictionary<string, object?> { ["number"] = "1", ["answer"] = "Відповідь" }
                })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Warnings.Should().Contain(w => w.Contains("текст запитання"));
    }

    [Fact]
    public async Task Extract_MissingAnswer_AddsWarning()
    {
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["tours"] = new List<object>
            {
                MakeTour("1", questions: new List<object>
                {
                    new Dictionary<string, object?> { ["number"] = "1", ["text"] = "Запитання" }
                })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Warnings.Should().Contain(w => w.Contains("відповідь"));
    }

    [Fact]
    public async Task Extract_InvalidDate_AddsWarning()
    {
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["playedFrom"] = "not-a-date",
            ["tours"] = new List<object>
            {
                MakeTour("1", questions: new List<object> { MakeQuestion("1") })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Warnings.Should().Contain(w => w.Contains("дати") && w.Contains("not-a-date"));
        result.PlayedFrom.Should().BeNull();
    }

    [Fact]
    public async Task Extract_EmptyTour_AddsWarning()
    {
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["tours"] = new List<object>
            {
                MakeTour("1") // no questions, no blocks
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Warnings.Should().Contain(w => w.Contains("Тур 1") && w.Contains("запитань"));
    }

    #endregion

    #region Numbering Modes

    [Theory]
    [InlineData("Global", QuestionNumberingMode.Global)]
    [InlineData("PerTour", QuestionNumberingMode.PerTour)]
    [InlineData("Manual", QuestionNumberingMode.Manual)]
    [InlineData(null, QuestionNumberingMode.Global)]
    public async Task Extract_NumberingMode_DetectedCorrectly(string? mode, QuestionNumberingMode expected)
    {
        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(MinimalPackageJson(numberingMode: mode));

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.NumberingMode.Should().Be(expected);
    }

    #endregion

    #region Tours, Blocks, Warmup

    [Fact]
    public async Task Extract_MultipleTours_MapsCorrectly()
    {
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["tours"] = new List<object>
            {
                MakeTour("1", questions: new List<object>
                {
                    MakeQuestion("1"), MakeQuestion("2")
                }),
                MakeTour("2", questions: new List<object>
                {
                    MakeQuestion("1"), MakeQuestion("2"), MakeQuestion("3")
                })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Tours.Should().HaveCount(2);
        result.Tours[0].OrderIndex.Should().Be(0);
        result.Tours[1].OrderIndex.Should().Be(1);
        result.Tours[0].Questions.Should().HaveCount(2);
        result.Tours[1].Questions.Should().HaveCount(3);
        result.TotalQuestions.Should().Be(5);
    }

    [Fact]
    public async Task Extract_WarmupTour_SetsIsWarmup()
    {
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["tours"] = new List<object>
            {
                MakeTour("0", isWarmup: true, questions: new List<object> { MakeQuestion("1") }),
                MakeTour("1", questions: new List<object> { MakeQuestion("1") })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Tours[0].IsWarmup.Should().BeTrue();
        result.Tours[1].IsWarmup.Should().BeFalse();
    }

    [Fact]
    public async Task Extract_TourWithBlocks_MapsBlocksCorrectly()
    {
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["tours"] = new List<object>
            {
                MakeTour("1", blocks: new List<object>
                {
                    MakeBlock(
                        questions: new List<object> { MakeQuestion("1"), MakeQuestion("2") },
                        name: "Фізики",
                        editors: new List<string> { "Іван Місяць" },
                        preamble: "Преамбула блоку"),
                    MakeBlock(
                        questions: new List<object> { MakeQuestion("3") },
                        name: "Лірики")
                })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Tours[0].Blocks.Should().HaveCount(2);
        result.Tours[0].Blocks[0].Name.Should().Be("Фізики");
        result.Tours[0].Blocks[0].Editors.Should().Contain("Іван Місяць");
        result.Tours[0].Blocks[0].Preamble.Should().Be("Преамбула блоку");
        result.Tours[0].Blocks[0].OrderIndex.Should().Be(0);
        result.Tours[0].Blocks[0].Questions.Should().HaveCount(2);
        result.Tours[0].Blocks[1].Name.Should().Be("Лірики");
        result.Tours[0].Blocks[1].OrderIndex.Should().Be(1);
        result.Tours[0].Blocks[1].Questions.Should().HaveCount(1);
        result.TotalQuestions.Should().Be(3);
    }

    [Fact]
    public async Task Extract_TourEditors_MapsCorrectly()
    {
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["tours"] = new List<object>
            {
                MakeTour("1",
                    editors: new List<string> { "Іван Місяць", "Андрій Пундор" },
                    preamble: "Тестувала: Марія Іваненко",
                    questions: new List<object> { MakeQuestion("1") })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Tours[0].Editors.Should().BeEquivalentTo(new[] { "Іван Місяць", "Андрій Пундор" });
        result.Tours[0].Preamble.Should().Be("Тестувала: Марія Іваненко");
    }

    #endregion

    #region Shared Editors

    [Fact]
    public async Task Extract_SharedEditors_MapsPackageEditors()
    {
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["sharedEditors"] = true,
            ["editors"] = new List<string> { "Іван Місяць" },
            ["tours"] = new List<object>
            {
                MakeTour("1", questions: new List<object> { MakeQuestion("1") })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.SharedEditors.Should().BeTrue();
        result.PackageEditors.Should().Contain("Іван Місяць");
    }

    [Fact]
    public async Task Extract_SharedEditorsNoPackageEditors_FallsBackToTourEditors()
    {
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["sharedEditors"] = true,
            ["tours"] = new List<object>
            {
                MakeTour("1",
                    editors: new List<string> { "Іван Місяць" },
                    questions: new List<object> { MakeQuestion("1") })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.SharedEditors.Should().BeTrue();
        result.PackageEditors.Should().Contain("Іван Місяць");
        result.Warnings.Should().Contain(w => w.Contains("SharedEditors"));
    }

    #endregion

    #region Question Fields

    [Fact]
    public async Task Extract_QuestionAllFields_MapsCorrectly()
    {
        var question = new Dictionary<string, object?>
        {
            ["number"] = "7",
            ["hostInstructions"] = "Не читати відразу",
            ["handoutText"] = "Текст роздатки",
            ["text"] = "Основне запитання",
            ["answer"] = "Правильна відповідь",
            ["acceptedAnswers"] = "За змістом",
            ["rejectedAnswers"] = "Неправильні",
            ["comment"] = "Пояснення до відповіді",
            ["source"] = "https://uk.wikipedia.org",
            ["authors"] = new List<string> { "Іван Місяць" }
        };

        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["tours"] = new List<object>
            {
                MakeTour("1", questions: new List<object> { question })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        var q = result.Tours[0].Questions[0];
        q.Number.Should().Be("7");
        q.HostInstructions.Should().Be("Не читати відразу");
        q.HandoutText.Should().Be("Текст роздатки");
        q.Text.Should().Be("Основне запитання");
        q.Answer.Should().Be("Правильна відповідь");
        q.AcceptedAnswers.Should().Be("За змістом");
        q.RejectedAnswers.Should().Be("Неправильні");
        q.Comment.Should().Be("Пояснення до відповіді");
        q.Source.Should().Be("https://uk.wikipedia.org");
        q.Authors.Should().Contain("Іван Місяць");
    }

    [Fact]
    public async Task Extract_QuestionEmptyOptionalFields_SetsNulls()
    {
        var question = new Dictionary<string, object?>
        {
            ["number"] = "1",
            ["text"] = "Запитання",
            ["answer"] = "Відповідь",
            ["hostInstructions"] = "",
            ["comment"] = "  ",
            ["source"] = ""
        };

        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["tours"] = new List<object>
            {
                MakeTour("1", questions: new List<object> { question })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        var q = result.Tours[0].Questions[0];
        q.HostInstructions.Should().BeNull();
        q.Comment.Should().BeNull();
        q.Source.Should().BeNull();
    }

    #endregion

    #region Local Asset Extraction

    [Fact]
    public async Task Extract_LocalAsset_ExtractsToAssetsFolder()
    {
        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // fake PNG header
        var assets = new Dictionary<string, byte[]> { ["handout_q1.png"] = imageData };

        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["tours"] = new List<object>
            {
                MakeTour("1", questions: new List<object>
                {
                    MakeQuestion("1", handoutAssetFileName: "handout_q1.png")
                })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg, assets);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Tours[0].Questions[0].HandoutAssetFileName.Should().Be("handout_q1.png");
        File.Exists(Path.Combine(_assetsDir, "handout_q1.png")).Should().BeTrue();
    }

    [Fact]
    public async Task Extract_MissingLocalAsset_AddsWarning()
    {
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["tours"] = new List<object>
            {
                MakeTour("1", questions: new List<object>
                {
                    MakeQuestion("1", handoutAssetFileName: "nonexistent.png")
                })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Tours[0].Questions[0].HandoutAssetFileName.Should().BeNull();
        result.Warnings.Should().Contain(w => w.Contains("nonexistent.png"));
    }

    [Fact]
    public async Task Extract_LocalAssetTakesPrecedenceOverUrl()
    {
        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var assets = new Dictionary<string, byte[]> { ["handout.png"] = imageData };

        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["tours"] = new List<object>
            {
                MakeTour("1", questions: new List<object>
                {
                    MakeQuestion("1",
                        handoutAssetFileName: "handout.png",
                        handoutAssetUrl: "https://example.com/image.png")
                })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg, assets);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        // Local file should be used, URL ignored
        result.Tours[0].Questions[0].HandoutAssetFileName.Should().Be("handout.png");
    }

    #endregion

    #region External URL Download

    [Fact]
    public async Task Extract_ExternalUrl_DownloadsAsset()
    {
        var handler = new FakeHttpHandler();
        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        handler.AddResponse("https://example.com/image.png", imageData, "image/png");

        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["tours"] = new List<object>
            {
                MakeTour("1", questions: new List<object>
                {
                    MakeQuestion("1", handoutAssetUrl: "https://example.com/image.png")
                })
            }
        };

        var extractor = CreateExtractor(handler);
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Tours[0].Questions[0].HandoutAssetFileName.Should().NotBeNull();
        result.Tours[0].Questions[0].HandoutAssetFileName.Should().EndWith(".png");

        var downloadedFile = Path.Combine(_assetsDir, result.Tours[0].Questions[0].HandoutAssetFileName!);
        File.Exists(downloadedFile).Should().BeTrue();
        (await File.ReadAllBytesAsync(downloadedFile)).Should().BeEquivalentTo(imageData);
    }

    [Fact]
    public async Task Extract_ExternalUrlDownloadFails_AddsWarning()
    {
        var handler = new FakeHttpHandler();
        handler.AddResponse("https://example.com/missing.png", Array.Empty<byte>(), "text/html", HttpStatusCode.NotFound);

        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["tours"] = new List<object>
            {
                MakeTour("1", questions: new List<object>
                {
                    MakeQuestion("1", handoutAssetUrl: "https://example.com/missing.png")
                })
            }
        };

        var extractor = CreateExtractor(handler);
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Tours[0].Questions[0].HandoutAssetFileName.Should().BeNull();
        result.Warnings.Should().Contain(w => w.Contains("404") || w.Contains("не вдалося"));
    }

    [Fact]
    public async Task Extract_ExternalUrlInvalidUri_AddsWarning()
    {
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["tours"] = new List<object>
            {
                MakeTour("1", questions: new List<object>
                {
                    MakeQuestion("1", handoutAssetUrl: "not-a-valid-url")
                })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Tours[0].Questions[0].HandoutAssetFileName.Should().BeNull();
        result.Warnings.Should().Contain(w => w.Contains("некоректне посилання"));
    }

    [Fact]
    public async Task Extract_CommentExternalUrl_DownloadsAsset()
    {
        var handler = new FakeHttpHandler();
        var imageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG header
        handler.AddResponse("https://example.com/comment.jpg", imageData, "image/jpeg");

        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["tours"] = new List<object>
            {
                MakeTour("1", questions: new List<object>
                {
                    MakeQuestion("1", commentAssetUrl: "https://example.com/comment.jpg")
                })
            }
        };

        var extractor = CreateExtractor(handler);
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Tours[0].Questions[0].CommentAssetFileName.Should().NotBeNull();
        result.Tours[0].Questions[0].CommentAssetFileName.Should().EndWith(".jpg");
    }

    #endregion

    #region Tags

    [Fact]
    public async Task Extract_Tags_MapsCorrectly()
    {
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет",
            ["tags"] = new List<string> { "2025", "ЛУК", "синхрон" },
            ["tours"] = new List<object>
            {
                MakeTour("1", questions: new List<object> { MakeQuestion("1") })
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Tags.Should().BeEquivalentTo(new[] { "2025", "ЛУК", "синхрон" });
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task Extract_JsonWithComments_ParsesSuccessfully()
    {
        // package.json with JSONC comments should work (AllowTrailingCommas + ReadCommentHandling)
        var jsonWithComments = """
        {
            // This is a comment
            "formatVersion": "1.0",
            "title": "Пакет з коментарями",
            "tours": [
                {
                    "number": "1",
                    "questions": [
                        {
                            "number": "1",
                            "text": "Запитання",
                            "answer": "Відповідь",
                        }
                    ]
                }
            ],
        }
        """;

        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("package.json");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(jsonWithComments);
        }
        ms.Position = 0;

        var extractor = CreateExtractor();

        var result = await extractor.Extract(ms, _assetsDir, CancellationToken.None);

        result.Title.Should().Be("Пакет з коментарями");
    }

    [Fact]
    public async Task Extract_NoWarnings_ForValidPackage()
    {
        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(MinimalPackageJson());

        var result = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        result.Warnings.Should().BeEmpty();
    }

    #endregion
}
