using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure;
using QuestionsHub.Blazor.Infrastructure.Import;
using QuestionsHub.UnitTests.TestInfrastructure;
using Xunit;

namespace QuestionsHub.UnitTests.ImportParsing;

/// <summary>
/// End-to-end integration tests: .qhub ZIP → QhubExtractor → PackageDbImporter → DB entities.
/// </summary>
public class QhubImportIntegrationTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory;
    private readonly AuthorService _authorService;
    private readonly TagService _tagService;
    private readonly MemoryCache _cache;
    private readonly MediaUploadOptions _mediaOptions;
    private readonly string _tempDir;
    private readonly string _assetsDir;

    private const string OwnerId = "integration-test-owner";

    public QhubImportIntegrationTests()
    {
        _dbFactory = new InMemoryDbContextFactory();
        _authorService = new AuthorService(_dbFactory);
        _cache = new MemoryCache(new MemoryCacheOptions());
        _tagService = new TagService(_dbFactory, _cache);

        _tempDir = Path.Combine(Path.GetTempPath(), "qhub_integration_" + Guid.NewGuid().ToString("N"));
        _assetsDir = Path.Combine(_tempDir, "assets");
        Directory.CreateDirectory(_assetsDir);

        _mediaOptions = new MediaUploadOptions
        {
            UploadsPath = _tempDir,
            HandoutsFolder = "handouts"
        };
    }

    public void Dispose()
    {
        _cache.Dispose();
        using var context = _dbFactory.CreateDbContext();
        context.Database.EnsureDeleted();
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

    private PackageDbImporter CreateImporter()
    {
        var db = _dbFactory.CreateDbContext();
        return new PackageDbImporter(
            db, _mediaOptions, _authorService, _tagService,
            NullLogger<PackageDbImporter>.Instance);
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

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, byte[] Content, string ContentType)> _responses = new();

        public void AddResponse(string url, byte[] content, string contentType = "image/png",
            HttpStatusCode status = HttpStatusCode.OK)
        {
            _responses[url] = (status, content, contentType);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
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
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    #endregion

    [Fact]
    public async Task FullPipeline_MinimalPackage_CreatesEntitiesInDatabase()
    {
        // Arrange: minimal .qhub
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Мінімальний пакет",
            ["tours"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["number"] = "1",
                    ["questions"] = new List<object>
                    {
                        new Dictionary<string, object?>
                        {
                            ["number"] = "1",
                            ["text"] = "Що таке Що?Де?Коли?",
                            ["answer"] = "Інтелектуальна гра"
                        }
                    }
                }
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        // Act: Extract
        var parseResult = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        // Act: Import
        var importer = CreateImporter();
        var package = await importer.Import(parseResult, OwnerId, Guid.NewGuid(), _assetsDir, CancellationToken.None);

        // Assert
        using var db = _dbFactory.CreateDbContext();
        var loaded = db.Packages.Include(p => p.Tours).ThenInclude(t => t.Questions).First();

        loaded.Title.Should().Be("Мінімальний пакет");
        loaded.Status.Should().Be(PackageStatus.Draft);
        loaded.Tours.Should().HaveCount(1);
        loaded.Tours[0].Number.Should().Be("1");
        loaded.Tours[0].Questions.Should().HaveCount(1);
        loaded.Tours[0].Questions[0].Text.Should().Be("Що таке Що?Де?Коли?");
        loaded.Tours[0].Questions[0].Answer.Should().Be("Інтелектуальна гра");
    }

    [Fact]
    public async Task FullPipeline_CompletePackage_AllFieldsMapped()
    {
        // Arrange: rich .qhub with all fields
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Кубок Києва 2025",
            ["description"] = "Міжнародний турнір",
            ["preamble"] = "Вітаємо учасників",
            ["sourceUrl"] = "https://example.com/kubak",
            ["playedFrom"] = "2025-06-01",
            ["playedTo"] = "2025-06-02",
            ["numberingMode"] = "PerTour",
            ["sharedEditors"] = true,
            ["editors"] = new List<string> { "Іван Місяць" },
            ["tours"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["number"] = "0",
                    ["isWarmup"] = true,
                    ["questions"] = new List<object>
                    {
                        new Dictionary<string, object?>
                        {
                            ["number"] = "1",
                            ["text"] = "Розминка",
                            ["answer"] = "Так"
                        }
                    }
                },
                new Dictionary<string, object?>
                {
                    ["number"] = "1",
                    ["preamble"] = "Тестувала: Марія Іваненко",
                    ["editors"] = new List<string> { "Андрій Пундор" },
                    ["questions"] = new List<object>
                    {
                        new Dictionary<string, object?>
                        {
                            ["number"] = "1",
                            ["hostInstructions"] = "Роздайте матеріали",
                            ["handoutText"] = "Див. роздатку",
                            ["text"] = "Назвіть столицю",
                            ["answer"] = "Київ",
                            ["acceptedAnswers"] = "Столиця",
                            ["rejectedAnswers"] = "Харків",
                            ["comment"] = "Київ — столиця України",
                            ["source"] = "Загальновідомо",
                            ["authors"] = new List<string> { "Іван Місяць" }
                        },
                        new Dictionary<string, object?>
                        {
                            ["number"] = "2",
                            ["text"] = "Друге запитання",
                            ["answer"] = "Відповідь 2"
                        }
                    }
                }
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        // Act
        var parseResult = await extractor.Extract(zip, _assetsDir, CancellationToken.None);
        var importer = CreateImporter();
        var package = await importer.Import(parseResult, OwnerId, Guid.NewGuid(), _assetsDir, CancellationToken.None);

        // Assert package
        package.Title.Should().Be("Кубок Києва 2025");
        package.Description.Should().Be("Міжнародний турнір");
        package.Preamble.Should().Be("Вітаємо учасників");
        package.SourceUrl.Should().Be("https://example.com/kubak");
        package.PlayedFrom.Should().Be(new DateOnly(2025, 6, 1));
        package.PlayedTo.Should().Be(new DateOnly(2025, 6, 2));
        package.NumberingMode.Should().Be(QuestionNumberingMode.PerTour);
        package.SharedEditors.Should().BeTrue();
        package.TotalQuestions.Should().Be(3);

        // Assert structure
        using var db = _dbFactory.CreateDbContext();
        var tours = db.Tours.Include(t => t.Questions).Include(t => t.Editors)
            .OrderBy(t => t.OrderIndex).ToList();

        tours.Should().HaveCount(2);

        // Warmup tour
        tours[0].Number.Should().Be("0");
        tours[0].IsWarmup.Should().BeTrue();
        tours[0].Questions.Should().HaveCount(1);

        // Main tour
        tours[1].Number.Should().Be("1");
        tours[1].IsWarmup.Should().BeFalse();
        tours[1].Preamble.Should().Be("Тестувала: Марія Іваненко");
        tours[1].Editors.Should().HaveCount(1);
        tours[1].Questions.Should().HaveCount(2);

        // Detailed question check
        var q1 = tours[1].Questions.OrderBy(q => q.OrderIndex).First();
        q1.HostInstructions.Should().Be("Роздайте матеріали");
        q1.HandoutText.Should().Be("Див. роздатку");
        q1.Text.Should().Be("Назвіть столицю");
        q1.Answer.Should().Be("Київ");
        q1.AcceptedAnswers.Should().Be("Столиця");
        q1.RejectedAnswers.Should().Be("Харків");
        q1.Comment.Should().Be("Київ — столиця України");
        q1.Source.Should().Be("Загальновідомо");

        // Question authors
        var q1WithAuthors = db.Questions.Include(q => q.Authors)
            .First(q => q.Number == "1" && q.TourId == tours[1].Id);
        q1WithAuthors.Authors.Should().HaveCount(1);
        q1WithAuthors.Authors[0].FirstName.Should().Be("Іван");
    }

    [Fact]
    public async Task FullPipeline_WithBlocks_CreatesBlockHierarchy()
    {
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет з блоками",
            ["tours"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["number"] = "1",
                    ["blocks"] = new List<object>
                    {
                        new Dictionary<string, object?>
                        {
                            ["name"] = "Фізики",
                            ["editors"] = new List<string> { "Іван Місяць" },
                            ["preamble"] = "Блок 1 преамбула",
                            ["questions"] = new List<object>
                            {
                                new Dictionary<string, object?> { ["number"] = "1", ["text"] = "B1Q1", ["answer"] = "A1" },
                                new Dictionary<string, object?> { ["number"] = "2", ["text"] = "B1Q2", ["answer"] = "A2" }
                            }
                        },
                        new Dictionary<string, object?>
                        {
                            ["name"] = "Лірики",
                            ["questions"] = new List<object>
                            {
                                new Dictionary<string, object?> { ["number"] = "3", ["text"] = "B2Q1", ["answer"] = "A3" }
                            }
                        }
                    }
                }
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        // Act
        var parseResult = await extractor.Extract(zip, _assetsDir, CancellationToken.None);
        var importer = CreateImporter();
        await importer.Import(parseResult, OwnerId, Guid.NewGuid(), _assetsDir, CancellationToken.None);

        // Assert
        using var db = _dbFactory.CreateDbContext();
        var blocks = db.Blocks.Include(b => b.Questions).Include(b => b.Editors)
            .OrderBy(b => b.OrderIndex).ToList();

        blocks.Should().HaveCount(2);

        blocks[0].Name.Should().Be("Фізики");
        blocks[0].Preamble.Should().Be("Блок 1 преамбула");
        blocks[0].Editors.Should().HaveCount(1);
        blocks[0].Questions.Should().HaveCount(2);

        blocks[1].Name.Should().Be("Лірики");
        blocks[1].Questions.Should().HaveCount(1);

        // Block questions should have globally sequential order
        var allQuestions = db.Questions.OrderBy(q => q.OrderIndex).ToList();
        allQuestions.Select(q => q.OrderIndex).Should().BeEquivalentTo(new[] { 0, 1, 2 });
        allQuestions.All(q => q.BlockId != null).Should().BeTrue();
    }

    [Fact]
    public async Task FullPipeline_WithLocalAssets_ExtractsAndImports()
    {
        // Arrange: .qhub with a local image asset
        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // PNG header
        var assets = new Dictionary<string, byte[]> { ["handout1.png"] = imageData };

        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет з картинками",
            ["tours"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["number"] = "1",
                    ["questions"] = new List<object>
                    {
                        new Dictionary<string, object?>
                        {
                            ["number"] = "1",
                            ["text"] = "Що зображено?",
                            ["answer"] = "Кіт",
                            ["handoutAssetFileName"] = "handout1.png"
                        }
                    }
                }
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg, assets);

        // Act
        var parseResult = await extractor.Extract(zip, _assetsDir, CancellationToken.None);
        var importer = CreateImporter();
        await importer.Import(parseResult, OwnerId, Guid.NewGuid(), _assetsDir, CancellationToken.None);

        // Assert: asset was extracted and question has handout URL
        using var db = _dbFactory.CreateDbContext();
        var q = db.Questions.First();
        q.HandoutUrl.Should().Be("/media/handout1.png");
        q.Text.Should().Be("Що зображено?");

        // Asset file exists in handouts folder
        File.Exists(Path.Combine(_tempDir, "handouts", "handout1.png")).Should().BeTrue();
    }

    [Fact]
    public async Task FullPipeline_WithExternalAssetUrl_DownloadsAndImports()
    {
        // Arrange: .qhub with external URL
        var handler = new FakeHttpHandler();
        var imageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG header
        handler.AddResponse("https://example.com/photo.jpg", imageData, "image/jpeg");

        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "1.0",
            ["title"] = "Пакет з URL",
            ["tours"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["number"] = "1",
                    ["questions"] = new List<object>
                    {
                        new Dictionary<string, object?>
                        {
                            ["number"] = "1",
                            ["text"] = "Що на фото?",
                            ["answer"] = "Пейзаж",
                            ["handoutAssetUrl"] = "https://example.com/photo.jpg"
                        }
                    }
                }
            }
        };

        var extractor = CreateExtractor(handler);
        using var zip = CreateQhubZip(pkg);

        // Act
        var parseResult = await extractor.Extract(zip, _assetsDir, CancellationToken.None);
        var importer = CreateImporter();
        await importer.Import(parseResult, OwnerId, Guid.NewGuid(), _assetsDir, CancellationToken.None);

        // Assert: question has handout URL pointing to downloaded file
        using var db = _dbFactory.CreateDbContext();
        var q = db.Questions.First();
        q.HandoutUrl.Should().StartWith("/media/dl_");
        q.HandoutUrl.Should().EndWith(".jpg");
    }

    [Fact]
    public async Task FullPipeline_WithWarnings_WarningsAreCollected()
    {
        // Arrange: .qhub with issues
        var pkg = new Dictionary<string, object?>
        {
            ["formatVersion"] = "2.0", // wrong version → warning
            ["tours"] = new List<object> // no title → warning
            {
                new Dictionary<string, object?>
                {
                    ["number"] = "1",
                    ["questions"] = new List<object>
                    {
                        new Dictionary<string, object?>
                        {
                            ["number"] = "1",
                            ["text"] = "Запитання"
                            // no answer → warning
                        }
                    }
                }
            }
        };

        var extractor = CreateExtractor();
        using var zip = CreateQhubZip(pkg);

        // Act
        var parseResult = await extractor.Extract(zip, _assetsDir, CancellationToken.None);

        // Assert: warnings are collected
        parseResult.Warnings.Should().HaveCountGreaterOrEqualTo(3);
        parseResult.Warnings.Should().Contain(w => w.Contains("версія формату"));
        parseResult.Warnings.Should().Contain(w => w.Contains("Назва пакету"));
        parseResult.Warnings.Should().Contain(w => w.Contains("відповідь"));

        // Import should still succeed (warnings don't block import)
        var importer = CreateImporter();
        var package = await importer.Import(parseResult, OwnerId, Guid.NewGuid(), _assetsDir, CancellationToken.None);
        package.Should().NotBeNull();
    }
}
