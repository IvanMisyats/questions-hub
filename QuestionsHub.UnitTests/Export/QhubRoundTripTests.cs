using System.Security.Cryptography;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.Blazor.Infrastructure.Export;
using QuestionsHub.Blazor.Infrastructure.Import;
using QuestionsHub.Blazor.Infrastructure.Media;
using QuestionsHub.UnitTests.TestInfrastructure;
using Xunit;

namespace QuestionsHub.UnitTests.Export;

/// <summary>
/// A <c>.qhub</c> is a backup only if it round-trips: export → <see cref="QhubExtractor"/> →
/// <see cref="PackageDbImporter"/> → export again gives the same <c>package.json</c>. Author/editor order
/// (ids are new) and media file names (fresh random names, compared by content) are normalized.
/// </summary>
public sealed class QhubRoundTripTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly string _uploads = Path.Combine(Path.GetTempPath(), "qhub_roundtrip_" + Guid.NewGuid().ToString("N"));
    private readonly MediaUploadOptions _media;
    private readonly QhubExporter _exporter;

    public QhubRoundTripTests()
    {
        _media = new MediaUploadOptions { UploadsPath = _uploads, HandoutsFolder = "handouts" };
        Directory.CreateDirectory(Path.Combine(_uploads, "handouts"));
        _exporter = new QhubExporter(Options.Create(_media), NullLogger<QhubExporter>.Instance);
    }

    public void Dispose()
    {
        _cache.Dispose();
        using (var context = _dbFactory.CreateDbContext())
            context.Database.EnsureDeleted();
        if (Directory.Exists(_uploads))
            Directory.Delete(_uploads, recursive: true);
    }

    private string Media(string name, byte[] content)
    {
        File.WriteAllBytes(Path.Combine(_uploads, "handouts", name), content);
        return "/media/" + name;
    }

    private static Author Person(int id, string first, string last) => new() { Id = id, FirstName = first, LastName = last };

    private Package Www()
    {
        var ivan = Person(1, "Іван", "Петренко");
        var olena = Person(2, "Олена", "Коваленко");
        var jean = Person(3, "Жан Клод", "Ван Дамм");
        var block = new Block { Id = 30, OrderIndex = 0, Name = "Блок Олени", Preamble = "Про блок", Editors = [olena] };
        return new Package
        {
            Id = 1,
            Title = "Кубок Тесту 2026",
            Description = "Опис",
            Preamble = "Преамбула пакету",
            SourceUrl = "https://example.org/package",
            PlayedFrom = new DateOnly(2026, 3, 1),
            PlayedTo = new DateOnly(2026, 3, 8),
            NumberingMode = QuestionNumberingMode.PerTour,
            SharedEditors = true,
            PackageEditors = [ivan, olena],
            Tags = [new Tag { Id = 1, Name = "синхрон" }, new Tag { Id = 2, Name = "100%" }],
            Tours =
            [
                new Tour
                {
                    Id = 10, OrderIndex = 0, Number = "0", Type = TourType.Warmup, Editors = [ivan],
                    Questions = [new Question { Id = 100, OrderIndex = 0, Number = "1", Text = "Розминка", Answer = "Так", Authors = [ivan] }]
                },
                new Tour
                {
                    Id = 11, OrderIndex = 1, Number = "1", Editors = [ivan, olena], Preamble = "Перед туром", Comment = "Після туру",
                    Questions =
                    [
                        new Question
                        {
                            Id = 101, OrderIndex = 0, Number = "1", HostInstructions = "Читати повільно", Text = "Питання з роздаткою",
                            HandoutText = "Текст роздатки", HandoutUrl = Media("handout.png", [1, 2, 3]),
                            Answer = "Відповідь", AcceptedAnswers = "Варіант.", RejectedAnswers = "Не те.",
                            Comment = "Коментар", CommentAttachmentUrl = Media("comment.jpg", [4, 5]),
                            Source = "https://uk.wikipedia.org", Authors = [olena, ivan, jean]
                        },
                        new Question { Id = 102, OrderIndex = 1, Number = "2", Text = "Без медіа", Answer = "Б" },
                    ]
                },
                new Tour
                {
                    Id = 12, OrderIndex = 2, Number = "2", Blocks = [block],
                    Questions = [new Question { Id = 103, OrderIndex = 0, Number = "1", Text = "У блоці", Answer = "В", BlockId = block.Id, Authors = [olena] }]
                },
                new Tour
                {
                    Id = 13, OrderIndex = 3, Number = "П", Type = TourType.Shootout,
                    Questions = [new Question { Id = 104, OrderIndex = 0, Number = "1", Text = "Перестрілка", Answer = "Г" }]
                }
            ]
        };
    }

    private static Package Shvager()
    {
        var author = Person(5, "Марія", "Шевченко");
        return new Package
        {
            Id = 2,
            Title = "Своя гра 2026",
            Type = PackageType.Shvager,
            Tours =
            [
                new Tour
                {
                    Id = 20, OrderIndex = 0, Number = "1", Title = "Річки", Editors = [author],
                    Questions =
                    [
                        new Question { Id = 200, OrderIndex = 0, Number = "10", Text = "Дніпро?", Answer = "Так", AnswerForm = "Назва річки" },
                        new Question { Id = 201, OrderIndex = 1, Number = "20", Text = "Десна?", Answer = "Так" },
                        new Question { Id = 202, OrderIndex = 2, Number = "10-30", Text = "Запасне", Answer = "Ні" },
                    ]
                }
            ]
        };
    }

    private async Task<Package> RoundTrip(Package original)
    {
        using var archive = await _exporter.Export(original);
        var assets = Path.Combine(_uploads, "job-" + Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        // No network: the packages here have local media only (external URLs are downloaded on import).
        services.AddHttpClient("QhubAssetDownloader").ConfigurePrimaryHttpMessageHandler(() => new NoNetwork());
        var extractor = new QhubExtractor(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(),
            NullLogger<QhubExtractor>.Instance);
        var parsed = await extractor.Extract(archive, assets, CancellationToken.None);

        await using (var db = _dbFactory.CreateDbContext())
        {
            var importer = new PackageDbImporter(db, Options.Create(_media), new AuthorService(_dbFactory),
                new TagService(_dbFactory, _cache), NullLogger<PackageDbImporter>.Instance);
            var imported = await importer.Import(parsed, "owner", Guid.NewGuid(), assets, CancellationToken.None);
            parsed.Warnings.Should().BeEmpty();
            await using var read = _dbFactory.CreateDbContext();
            return await read.Packages.AsNoTracking().WithEditableGraph().SingleAsync(p => p.Id == imported.Id);
        }
    }

    /// <summary>The manifest with people sorted by name and media file names replaced by their content hash.</summary>
    private JsonNode Normalized(Package package)
    {
        var json = JsonNode.Parse(_exporter.BuildManifest(package).Json)!;
        Normalize(json);
        return json;

        void Normalize(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (key, value) in obj.ToList())
                    {
                        if (key is "authors" or "editors" && value is JsonArray people)
                            obj[key] = new JsonArray(people.Select(PersonKey).Order(StringComparer.Ordinal).Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());
                        else if (key.EndsWith("AssetFileName", StringComparison.Ordinal) && value != null)
                            obj[key] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(_uploads, "handouts", value.GetValue<string>()))));
                        else
                            Normalize(value);
                    }
                    break;
                case JsonArray array:
                    foreach (var item in array)
                        Normalize(item);
                    break;
            }
        }
    }

    // A person is "First Last" or, when a name has several words, {firstName, lastName}.
    private static string PersonKey(JsonNode? node) => node is JsonObject person
        ? $"{person["firstName"]!.GetValue<string>()}|{person["lastName"]!.GetValue<string>()}"
        : node!.GetValue<string>();

    [Fact]
    public async Task Www_RoundTripsWithoutLoss()
    {
        var original = Www();

        var imported = await RoundTrip(original);

        JsonNode.DeepEquals(Normalized(imported), Normalized(original)).Should().BeTrue(
            $"expected {Normalized(original).ToJsonString()}\nactual   {Normalized(imported).ToJsonString()}");
        imported.Status.Should().Be(PackageStatus.Draft);
        imported.Tours[1].Questions.SelectMany(q => q.Authors).Should().Contain(a => a.FirstName == "Жан Клод" && a.LastName == "Ван Дамм");
    }

    [Fact]
    public async Task Shvager_RoundTripsWithoutLoss()
    {
        var original = Shvager();

        var imported = await RoundTrip(original);

        JsonNode.DeepEquals(Normalized(imported), Normalized(original)).Should().BeTrue(
            $"expected {Normalized(original).ToJsonString()}\nactual   {Normalized(imported).ToJsonString()}");
        imported.Type.Should().Be(PackageType.Shvager);
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"Unexpected request to {request.RequestUri}");
    }
}
