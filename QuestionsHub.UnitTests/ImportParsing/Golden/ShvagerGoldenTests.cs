using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Import;
using QuestionsHub.UnitTests.TestInfrastructure;
using Xunit;

namespace QuestionsHub.UnitTests.ImportParsing.Golden;

/// <summary>
/// End-to-end tests for Своя гра imports over the real sample files from _shvager/:
/// DOCX -> DocxExtractor -> ShvagerParser -> structural assertions.
/// </summary>
public class ShvagerGoldenTests : IDisposable
{
    private readonly DocxExtractor _extractor;
    private readonly ShvagerParser _parser;
    private readonly string _tempDirectory;

    public ShvagerGoldenTests()
    {
        _extractor = new DocxExtractor(NullLogger<DocxExtractor>.Instance);
        _parser = new ShvagerParser(NullLogger<ShvagerParser>.Instance);
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"QuestionsHub_ShvagerGolden_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    private async Task<ParseResult> ParseSample(string fileName)
    {
        var path = TestFiles.GetShvagerPath(fileName);
        var assetsPath = Path.Combine(_tempDirectory, Guid.NewGuid().ToString("N"));

        var extraction = await _extractor.Extract(path, assetsPath, CancellationToken.None);
        return _parser.Parse(extraction.Blocks, extraction.Assets);
    }

    [Fact]
    public async Task Import_NamedThemesSample2026_ParsesAllThemes()
    {
        var result = await ParseSample("shvager_2026_named_themes.docx");

        result.Type.Should().Be(PackageType.Shvager);
        result.Title.Should().Be("Швагер-ліга 2026 Весна. Фінальний раунд");

        // 9 main themes + 2 reserve themes (range-valued replacement questions) at the end
        result.Tours.Should().HaveCount(11);
        result.Tours.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.Title));
        result.Tours.Should().OnlyContain(t => t.Type == TourType.Regular);
        result.Tours.Should().OnlyContain(t => t.Blocks.Count == 0);

        // The 9 main themes are canonical: 5 questions valued 10..50, all with text and answers
        foreach (var theme in result.Tours.Take(9))
        {
            theme.Questions.Select(q => q.Number).Should().Equal("10", "20", "30", "40", "50");
            theme.Questions.Should().OnlyContain(q => q.HasAnswer && q.HasText);
        }

        // Reserve themes carry range-valued questions like «10-20», «30-50»
        result.Tours.Skip(9).Should().OnlyContain(t =>
            t.Questions.Count > 0 && t.Questions.All(q => q.Number.Contains('-') && q.HasAnswer));

        // First theme: «Жереб» by Євген Шляхов
        var zhereb = result.Tours[0];
        zhereb.Title.Should().Be("Жереб");
        zhereb.Editors.Should().ContainSingle().Which.Should().Be("Євген Шляхов");

        // Question 10 of «Жереб»: answer, zalik, comment, source, author
        var q10 = zhereb.Questions[0];
        q10.Answer.Should().Contain("крашанки");
        q10.AcceptedAnswers.Should().Contain("яйця");
        q10.Comment.Should().Contain("Великодня");
        q10.Source.Should().Contain("трансляція матчу");
        q10.Authors.Should().ContainSingle().Which.Should().Be("Євген Шляхов");

        // Question 20: «Форма» field
        var q20 = zhereb.Questions[1];
        q20.Form.Should().Contain("двома словами");

        // Theme preamble/package preamble carries the testers list and the theme list
        result.Preamble.Should().Contain("Допомогли покращити запитання");
        result.Preamble.Should().Contain("Теми:");
    }

    [Fact]
    public async Task Import_BareTitlesSample2021_ParsesAllThemes()
    {
        var result = await ParseSample("shvager_2021_bare_titles.docx");

        result.Type.Should().Be(PackageType.Shvager);
        result.Title.Should().StartWith("Швагер-ліга 2021 Літо");

        // Package-level editor: «Редактор Євген Шляхов»
        result.SharedEditors.Should().BeTrue();
        result.PackageEditors.Should().ContainSingle().Which.Should().Be("Євген Шляхов");

        // 8 themes from the «Теми:» list
        result.Tours.Should().HaveCount(8);
        result.Tours[0].Title.Should().Be("Авіація");
        result.Tours.Should().OnlyContain(t => t.Questions.Count == 5);

        foreach (var theme in result.Tours)
        {
            theme.Questions.Select(q => q.Number).Should().Equal("10", "20", "30", "40", "50");
        }

        // Authors are per-question with the city stripped
        var aviation = result.Tours[0];
        aviation.Questions[0].Authors.Should().ContainSingle().Which.Should().Be("Євген Шляхов");
        aviation.Questions[0].Answer.Should().Contain("Мрія");

        // «[Малюнок 1 https://drive.google.com/...]» moved from question text to handout
        var allQuestions = result.Tours.SelectMany(t => t.Questions).ToList();
        var handoutQuestion = allQuestions.FirstOrDefault(q =>
            q.HandoutText != null && q.HandoutText.Contains("drive.google.com"));
        handoutQuestion.Should().NotBeNull("the sample contains a [Малюнок N url] reference");
        handoutQuestion!.Text.Should().NotContain("Малюнок");
    }

    [Fact]
    public async Task Import_BareTitlesSample2021_OnlyExpectedWarnings()
    {
        var result = await ParseSample("shvager_2021_bare_titles.docx");

        // The only expected warning is the informational «[Малюнок N url]» relocation
        result.Warnings.Should().ContainSingle()
            .Which.Should().Contain("перенесено до роздаткового матеріалу");
    }

    [Fact]
    public async Task Import_NamedThemesSample2026_WarningsOnlyAboutReserveThemes()
    {
        var result = await ParseSample("shvager_2026_named_themes.docx");

        // Structural warnings may only concern the reserve themes and the list-count mismatch —
        // never the 9 main themes
        result.Warnings.Should().OnlyContain(w =>
            w.Contains("Природа Північної Америки")
            || w.Contains("корейське кіно")
            || w.Contains("У списку «Теми:»"));
    }
}
