using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.Blazor.Infrastructure.Export;
using QuestionsHub.Blazor.Infrastructure.Media;
using Xunit;

namespace QuestionsHub.UnitTests.Export;

/// <summary>
/// <see cref="QhubExporter"/> on a package shaped like the agent API's editable graph: every question in
/// <see cref="Tour.Questions"/>, block questions marked by <see cref="Question.BlockId"/> only.
/// </summary>
public sealed class QhubExporterTests : IDisposable
{
    private readonly string _uploads = Path.Combine(Path.GetTempPath(), "qhub_export_" + Guid.NewGuid().ToString("N"));
    private readonly QhubExporter _exporter;

    public QhubExporterTests()
    {
        Directory.CreateDirectory(Path.Combine(_uploads, "handouts"));
        _exporter = new QhubExporter(
            Options.Create(new MediaUploadOptions { UploadsPath = _uploads, HandoutsFolder = "handouts" }),
            NullLogger<QhubExporter>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_uploads))
            Directory.Delete(_uploads, recursive: true);
    }

    private static Package CreatePackage()
    {
        var olena = new Author { Id = 7, FirstName = "Олена", LastName = "Коваленко" };
        var petro = new Author { Id = 3, FirstName = "Петро", LastName = "Шевченко" };
        var block = new Block { Id = 50, OrderIndex = 0, Name = "Блок А", Editors = [olena, petro] };

        return new Package
        {
            Id = 1,
            Title = "Пакет",
            Tags = [new Tag { Id = 2, Name = "весна" }, new Tag { Id = 1, Name = "архів" }],
            Tours =
            [
                new Tour
                {
                    Id = 10, OrderIndex = 0, Number = "1", Editors = [olena, petro],
                    Questions =
                    [
                        new Question { Id = 101, OrderIndex = 1, Number = "2", Text = "Друге", Answer = "Б", Authors = [olena, petro] },
                        new Question { Id = 100, OrderIndex = 0, Number = "1", Text = "Перше", Answer = "А", HandoutUrl = "/media/pic.png" },
                    ]
                },
                new Tour
                {
                    Id = 11, OrderIndex = 1, Number = "2", Blocks = [block],
                    Questions =
                    [
                        new Question { Id = 102, OrderIndex = 0, Number = "3", Text = "У блоці", Answer = "В", BlockId = block.Id },
                    ]
                }
            ]
        };
    }

    private static JsonElement Json(QhubManifest manifest) => JsonDocument.Parse(manifest.Json).RootElement;

    [Fact]
    public void BuildManifest_TakesBlockQuestionsFromTheTour()
    {
        var json = Json(_exporter.BuildManifest(CreatePackage()));

        var block = json.GetProperty("tours")[1].GetProperty("blocks")[0];
        block.GetProperty("questions").EnumerateArray().Select(q => q.GetProperty("text").GetString())
            .Should().Equal("У блоці");
        json.GetProperty("tours")[0].GetProperty("questions").EnumerateArray().Select(q => q.GetProperty("number").GetString())
            .Should().Equal("1", "2");
    }

    [Fact]
    public void BuildManifest_IsCanonical_WhateverTheOrderOfLoadedCollections()
    {
        var first = CreatePackage();
        var second = CreatePackage();
        second.Tags.Reverse();
        second.Tours[0].Editors.Reverse();
        second.Tours[0].Questions[0].Authors.Reverse();
        second.Tours[1].Blocks[0].Editors.Reverse();

        _exporter.BuildManifest(second).Json.Should().Equal(_exporter.BuildManifest(first).Json);
        var json = Json(_exporter.BuildManifest(first));
        json.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).Should().Equal("архів", "весна");
        json.GetProperty("tours")[0].GetProperty("editors").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("Петро Шевченко", "Олена Коваленко");
    }

    [Fact]
    public void ComputeETag_IsWeakAndStable_AndFollowsTheContent()
    {
        var etag = QhubExporter.ComputeETag(_exporter.BuildManifest(CreatePackage()));

        etag.Should().MatchRegex("^W/\"[0-9a-f]{64}\"$");
        QhubExporter.ComputeETag(_exporter.BuildManifest(CreatePackage())).Should().Be(etag);

        var edited = CreatePackage();
        edited.Tours[1].Questions[0].Comment = "Новий коментар";
        QhubExporter.ComputeETag(_exporter.BuildManifest(edited)).Should().NotBe(etag);
    }

    [Fact]
    public void ComputeETag_ChangesWhenAMediaFileAppearsOrChanges()
    {
        var manifest = _exporter.BuildManifest(CreatePackage());
        var missing = QhubExporter.ComputeETag(manifest);

        var path = Path.Combine(_uploads, "handouts", "pic.png");
        File.WriteAllBytes(path, [1, 2, 3]);
        var present = QhubExporter.ComputeETag(manifest);
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        var changed = QhubExporter.ComputeETag(manifest);

        new[] { missing, present, changed }.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task WriteArchive_PacksManifestAndMedia_AndCountsMissingFiles()
    {
        var manifest = _exporter.BuildManifest(CreatePackage());
        using (var output = new MemoryStream())
        {
            (await _exporter.WriteArchive(manifest, output)).Should().Be(1);
        }

        File.WriteAllBytes(Path.Combine(_uploads, "handouts", "pic.png"), [9, 8, 7]);
        using var archive = new MemoryStream();
        (await _exporter.WriteArchive(manifest, archive)).Should().Be(0);

        archive.Position = 0;
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read);
        zip.Entries.Select(e => e.FullName).Should().BeEquivalentTo("package.json", "assets/pic.png");
        zip.Entries.Should().OnlyContain(e => e.CompressedLength == e.Length,
            "entries are stored, so an export never trips the importer's compression-ratio limit");
        using var json = new MemoryStream();
        await zip.GetEntry("package.json")!.Open().CopyToAsync(json);
        json.ToArray().Should().Equal(manifest.Json);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("*", true)]
    [InlineData("W/\"abc\"", true)]
    [InlineData("\"abc\"", true)]
    [InlineData("W/\"other\"", false)]
    [InlineData("W/\"other\", W/\"abc\"", true)]
    public void IfNoneMatch_UsesWeakComparison(string? header, bool matches)
    {
        var list = header == null ? [] : EntityTagHeaderValue.ParseList([header]);

        AgentPackageExporter.Matches(list, "W/\"abc\"").Should().Be(matches);
    }
}
