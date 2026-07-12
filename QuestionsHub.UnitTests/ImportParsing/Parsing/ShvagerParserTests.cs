using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Import;
using Xunit;

namespace QuestionsHub.UnitTests.ImportParsing.Parsing;

/// <summary>
/// Unit tests for ShvagerParser (Своя гра packages) using synthetic DocBlocks.
/// </summary>
public class ShvagerParserTests
{
    private readonly ShvagerParser _parser;

    public ShvagerParserTests()
    {
        _parser = new ShvagerParser(NullLogger<ShvagerParser>.Instance);
    }

    private static DocBlock Block(string text, int index = 0) => new()
    {
        Index = index,
        Text = text
    };

    private static DocBlock BlockWithAsset(string text, string fileName, int index = 0) => new()
    {
        Index = index,
        Text = text,
        Assets =
        [
            new AssetReference { FileName = fileName, RelativeUrl = "/media/" + fileName, ContentType = "image/jpeg" }
        ]
    };

    private static List<DocBlock> Blocks(params string[] lines) =>
        lines.Select((text, i) => Block(text, i)).ToList();

    private ParseResult Parse(params string[] lines) => _parser.Parse(Blocks(lines), []);

    /// <summary>A complete 5-question theme in the named format.</summary>
    private static string[] Theme(string header, int startId = 1) =>
    [
        header,
        $"10. Питання {startId}?",
        "Відповідь: перша",
        $"20. Питання {startId + 1}?",
        "Відповідь: друга",
        $"30. Питання {startId + 2}?",
        "Відповідь: третя",
        $"40. Питання {startId + 3}?",
        "Відповідь: четверта",
        $"50. Питання {startId + 4}?",
        "Відповідь: п'ята"
    ];

    /// <summary>A 5-question theme whose bare-title header carries an «Автор:» line.</summary>
    private static string[] ThemeWithAuthor(string title, string author, int startId = 1) =>
        [title, $"Автор: {author}", "", .. Theme("", startId)[1..]];

    /// <summary>
    /// A 5-question theme whose header places a preamble line between the «Автор:» line and the
    /// first «10.» question — the shape that defeats the before-«10.» lookahead.
    /// </summary>
    private static string[] ThemeWithAuthorAndPreamble(string title, string author, string preamble, int startId = 1) =>
        [title, $"Автор: {author}", preamble, "", .. Theme("", startId)[1..]];

    #region Theme detection

    [Theory]
    [InlineData("Тема: Жереб")]
    [InlineData("Тема. Жереб")]
    [InlineData("Тема – Жереб")]
    [InlineData("ТЕМА: Жереб")]
    public void Parse_NamedThemeHeader_DetectsTheme(string header)
    {
        var result = Parse(Theme(header));

        result.Type.Should().Be(PackageType.Shvager);
        result.Tours.Should().HaveCount(1);
        result.Tours[0].Title.Should().Be("Жереб");
        result.Tours[0].Number.Should().Be("1");
        result.Tours[0].Type.Should().Be(TourType.Regular);
    }

    [Fact]
    public void Parse_ThemeHeaderWithAuthors_ExtractsThemeEditors()
    {
        var result = Parse(Theme("Тема: Жереб (Євген Шляхов)"));

        result.Tours[0].Title.Should().Be("Жереб");
        result.Tours[0].Editors.Should().ContainSingle().Which.Should().Be("Євген Шляхов");
    }

    [Fact]
    public void Parse_ThemeHeaderWithTwoAuthors_ExtractsBoth()
    {
        var result = Parse(Theme("Тема: Кіно (Костянтин Каунін, Євген Шляхов)"));

        result.Tours[0].Title.Should().Be("Кіно");
        result.Tours[0].Editors.Should().Equal("Костянтин Каунін", "Євген Шляхов");
    }

    [Fact]
    public void Parse_TitleWithNonAuthorParens_KeepsParensInTitle()
    {
        var result = Parse(Theme("Тема: Столиці (не тільки держав)"));

        result.Tours[0].Title.Should().Be("Столиці (не тільки держав)");
        result.Tours[0].Editors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_BareTitleBeforeValue10_DetectsTheme()
    {
        // Bare-title format without a «Теми:» list: a short line followed by "10." starts a theme
        var result = Parse(Theme("Авіація"));

        result.Tours.Should().HaveCount(1);
        result.Tours[0].Title.Should().Be("Авіація");
    }

    [Fact]
    public void Parse_ThemeListAnchors_DetectBareTitleThemes()
    {
        var lines = new List<string>
        {
            "Швагер-ліга 2021 Літо",
            "Коло 1. Редактор Євген Шляхов",
            "Теми:",
            "Авіація",
            "Змії",
            ""
        };
        lines.AddRange(Theme("Авіація"));
        lines.AddRange(Theme("Змії", 6));

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Title.Should().Be("Швагер-ліга 2021 Літо. Коло 1");
        result.SharedEditors.Should().BeTrue();
        result.PackageEditors.Should().ContainSingle().Which.Should().Be("Євген Шляхов");
        result.Tours.Should().HaveCount(2);
        result.Tours[0].Title.Should().Be("Авіація");
        result.Tours[1].Title.Should().Be("Змії");
        result.Preamble.Should().Contain("Теми:").And.Contain("Авіація");
    }

    [Fact]
    public void Parse_ThemeListWithNamedEntries_AnchorsCarryAuthors()
    {
        var lines = new List<string>
        {
            "Пакет",
            "Теми:",
            "Тема: Жереб (Євген Шляхов)",
            ""
        };
        lines.AddRange(Theme("Тема: Жереб (Євген Шляхов)"));

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Tours.Should().HaveCount(1);
        result.Tours[0].Title.Should().Be("Жереб");
        result.Tours[0].Editors.Should().ContainSingle().Which.Should().Be("Євген Шляхов");
    }

    [Fact]
    public void Parse_AccentedAnchor_MatchesUnaccentedHeader()
    {
        var lines = new List<string>
        {
            "Пакет",
            "Теми:",
            "Го́ри",
            ""
        };
        lines.AddRange(Theme("Гори"));

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Tours.Should().HaveCount(1);
        result.Tours[0].Title.Should().Be("Го́ри");
    }

    [Fact]
    public void Parse_DateLineInHeader_DoesNotStealTitle()
    {
        // "10.05.2026, Дніпро" must not look like a question, so the first line stays the title
        var lines = new List<string>
        {
            "Швагер-ліга",
            "10.05.2026, Дніпро"
        };
        lines.AddRange(Theme("Тема: Тест"));

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Title.Should().StartWith("Швагер-ліга");
        result.Tours.Should().HaveCount(1);
        result.Tours[0].Title.Should().Be("Тест");
    }

    [Fact]
    public void Parse_BareAnchorHeaderWithAuthors_MatchesAnchorAndTakesAuthors()
    {
        // The «Теми:» list has a bare title, but the real header carries «(Автор)»
        var lines = new List<string>
        {
            "Пакет",
            "Теми:",
            "Змії",
            ""
        };
        lines.AddRange(Theme("Змії (Іван Петренко)"));

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Tours.Should().HaveCount(1);
        result.Tours[0].Title.Should().Be("Змії");
        result.Tours[0].Editors.Should().ContainSingle().Which.Should().Be("Іван Петренко");
    }

    [Fact]
    public void Parse_AnchorTitleInsideComment_DoesNotDuplicateTheme()
    {
        // An anchor is consumed by its theme; the same word later in content must not start a new theme
        var lines = new List<string>
        {
            "Пакет",
            "Теми:",
            "Змії",
            ""
        };
        lines.AddRange(Theme("Змії"));
        // Append an extra content line to the last question's comment that equals the anchor title
        lines.Add("Коментар: тема називалася");
        lines.Add("Змії");

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Tours.Should().HaveCount(1);
        result.Tours[0].Questions.Should().HaveCount(5);
    }

    [Fact]
    public void Parse_QuestionRightAfterThemeList_ExitsListAndCreatesUntitledTheme()
    {
        var result = Parse(
            "Пакет",
            "Теми:",
            "10. Питання без теми?",
            "Відповідь: а");

        result.Title.Should().Be("Пакет");
        result.Tours.Should().HaveCount(1);
        result.Tours[0].Questions.Should().ContainSingle().Which.Number.Should().Be("10");
        result.Warnings.Should().Contain(w => w.Contains("до першої теми"));
    }

    [Fact]
    public void Parse_ReserveThemeValueRange_ParsedAsQuestion()
    {
        var result = Parse(
            "Тема: запасна",
            "30-50. Замінне запитання?",
            "Відповідь: так");

        var q = result.Tours[0].Questions.Should().ContainSingle().Subject;
        q.Number.Should().Be("30-50");
        q.HasAnswer.Should().BeTrue();
    }

    [Theory]
    [InlineData("Тема 1. Каркасна ...С...Я...Н...")]
    [InlineData("Тема №1: Каркасна ...С...Я...Н...")]
    [InlineData("Тема 1 — Каркасна ...С...Я...Н...")]
    public void Parse_NumberedThemeHeader_ExtractsTitleWithoutPrefix(string header)
    {
        var result = Parse(
            header,
            "Пояснення: Всі відповіді містять літери С, Я, Н саме в такому порядку.",
            "10. Питання?",
            "Відповідь: а");

        result.Tours.Should().HaveCount(1);
        result.Tours[0].Title.Should().Be("Каркасна ...С...Я...Н...");
        result.Tours[0].Title.Should().NotStartWith("Тема");
        result.Tours[0].Preamble.Should().Contain("Пояснення: Всі відповіді містять літери");
    }

    [Fact]
    public void Parse_NumberedThemeHeadersInList_AnchorsAndBodyMatch()
    {
        var lines = new List<string>
        {
            "Пакет",
            "Теми:",
            "Тема 1. Каркасна ...С...Я...Н...",
            ""
        };
        lines.AddRange(Theme("Тема 1. Каркасна ...С...Я...Н..."));

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Tours.Should().HaveCount(1);
        result.Tours[0].Title.Should().Be("Каркасна ...С...Я...Н...");
    }

    [Theory]
    [InlineData("Редактор та автор тем - Олександр Мерзликін (Кривий Ріг)")]
    [InlineData("Автор тем: Олександр Мерзликін")]
    [InlineData("Авторка та редакторка тем — Олена Мерзликіна")]
    public void Parse_EditorAuthorHeaderVariants_ParsedAsPackageEditors(string editorLine)
    {
        var lines = new List<string>
        {
            "ШВАҐЕР-ЛІГА - 2021. ЛІТО. ТУР 3",
            editorLine
        };
        lines.AddRange(Theme("Тема: Тест"));

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Title.Should().Be("ШВАҐЕР-ЛІГА - 2021. ЛІТО. ТУР 3");
        result.SharedEditors.Should().BeTrue();
        result.PackageEditors.Should().ContainSingle()
            .Which.Should().MatchRegex("Мерзлик");
    }

    [Fact]
    public void Parse_UnparsableEditorLine_GoesToPreambleNotTitle()
    {
        // An editor-like line whose name part fails validation must not become a subtitle
        var lines = new List<string>
        {
            "Пакет",
            "Редактор дякує всім тестерам пакету"
        };
        lines.AddRange(Theme("Тема: Тест"));

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Title.Should().Be("Пакет");
        result.Preamble.Should().Contain("Редактор дякує");
    }

    [Fact]
    public void Parse_QuestionBeforeAnyTheme_CreatesUntitledThemeWithWarning()
    {
        var result = Parse(
            "10. Питання без теми?",
            "Відповідь: так");

        result.Tours.Should().HaveCount(1);
        result.Tours[0].Title.Should().BeEmpty();
        result.Warnings.Should().Contain(w => w.Contains("до першої теми"));
    }

    [Fact]
    public void Parse_LongDescriptiveHeaderMatchingAnchor_DetectedAsSeparateTheme()
    {
        // Regression: a non-first header whose explanatory parenthetical pushes it past the
        // bare-title length limit («5. В. В. (це звичайна ініціальна тема: …)») must still be
        // matched against its «Теми:» anchor «5. В. В.» and not merged into the previous theme.
        var lines = new List<string>
        {
            "Пакет",
            "Теми:",
            "1. Аква Віта",
            "2. В. В.",
            ""
        };
        lines.AddRange(Theme("1. Аква Віта (про воду або життя)"));
        lines.AddRange(Theme("2. В. В. (це звичайна ініціальна тема: кожна відповідь складається з двох слів, кожне з яких починається на літеру «В»)", 6));

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Tours.Should().HaveCount(2);
        result.Tours[0].Title.Should().Be("Аква Віта (про воду або життя)");
        result.Tours[0].Questions.Should().HaveCount(5);
        result.Tours[1].Title.Should().StartWith("В. В.");
        result.Tours[1].Questions.Should().HaveCount(5);
        result.Warnings.Should().NotContain(w => w.Contains("запитань замість"));
    }

    [Fact]
    public void Parse_LongDescriptiveHeaderWithoutList_DetectedByCoreLength()
    {
        // No «Теми:» list: the bare-title fallback must judge the header by its core length
        // (list number and trailing parenthetical excluded), not the full 100+ char line.
        var lines = new List<string>();
        lines.AddRange(Theme("1. Аква Віта (про воду або життя)"));
        lines.AddRange(Theme("2. -ван- / -гог- (це альтернативно-матрична тема, відповіді в ній належать або до матриці -ван-, або до матриці -гог-; редактор лишив мітку перед кожним запитанням)", 6));

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Tours.Should().HaveCount(2);
        result.Tours[1].Title.Should().StartWith("-ван- / -гог-");
        result.Tours[1].Questions.Should().HaveCount(5);
    }

    [Fact]
    public void Parse_TitleFollowedByAuthorLabelLine_TitleAndThemeEditorDetected()
    {
        // Real header shape: a bare title, then an «Автор: Ім'я Прізвище» line, then the first
        // question. The author line sits between the title and «10.», so the title must still be
        // recognized (not the author line) and the author becomes the theme editor.
        var result = Parse(
            "Миші",
            "Автор: Тарас Вахрів",
            "",
            "10. Питання?",
            "Відповідь: а",
            "20. Друге?",
            "Відповідь: б",
            "30. Третє?",
            "Відповідь: в",
            "40. Четверте?",
            "Відповідь: г",
            "50. П'яте?",
            "Відповідь: ґ");

        result.Tours.Should().HaveCount(1);
        result.Tours[0].Title.Should().Be("Миші");
        result.Tours[0].Editors.Should().ContainSingle().Which.Should().Be("Тарас Вахрів");
        result.Tours[0].Questions.Should().HaveCount(5);
    }

    [Fact]
    public void Parse_TitleFollowedByParentheticalAuthorLine_TitleAndThemeEditorDetected()
    {
        // Real header shape: a bare title, then a parenthetical «(Авторка – Ім'я Прізвище)» author
        // line, then the first question. The parenthetical must not be mistaken for the title, and
        // its name becomes the theme editor.
        var result = Parse(
            "Останівка",
            "(Авторка – Вікторія Маландіна)",
            "",
            "10. Питання?",
            "Відповідь: а",
            "20. Друге?",
            "Відповідь: б",
            "30. Третє?",
            "Відповідь: в",
            "40. Четверте?",
            "Відповідь: г",
            "50. П'яте?",
            "Відповідь: ґ");

        result.Tours.Should().HaveCount(1);
        result.Tours[0].Title.Should().Be("Останівка");
        result.Tours[0].Editors.Should().ContainSingle().Which.Should().Be("Вікторія Маландіна");
        result.Tours[0].Questions.Should().HaveCount(5);
    }

    [Fact]
    public void Parse_ParentheticalAuthorAfterThemeList_TitleAndEditorDetected()
    {
        // Same parenthetical-author header, but reached straight out of the «Теми:» list (the very
        // first body theme). The list carries a single-surname author that does not resolve to a
        // full name, so the authoritative editor comes from the real header's author line.
        var lines = new List<string>
        {
            "Пакет",
            "Теми:",
            "1.\tОстанівка (Маландіна)",
            ""
        };
        lines.AddRange(new[]
        {
            "Останівка",
            "(Авторка – Вікторія Маландіна)",
            "",
            "10. Питання?",
            "Відповідь: а",
            "20. Друге?",
            "Відповідь: б",
            "30. Третє?",
            "Відповідь: в",
            "40. Четверте?",
            "Відповідь: г",
            "50. П'яте?",
            "Відповідь: ґ"
        });

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Tours.Should().HaveCount(1);
        result.Tours[0].Title.Should().Be("Останівка");
        result.Tours[0].Editors.Should().ContainSingle().Which.Should().Be("Вікторія Маландіна");
    }

    [Fact]
    public void Parse_ValueResetWithoutRecognizedHeader_StartsNewUntitledTheme()
    {
        // Backstop: even if a theme header is completely unrecognizable, a value that does not
        // exceed the previous question's (a «10.» after a «50.») starts a new theme so questions
        // are grouped in fives instead of piling onto the previous theme.
        var lines = new List<string>();
        lines.AddRange(Theme("Тема: Перша"));
        // A header that is neither an anchor nor followed directly by «10.» (a preamble sits between)
        lines.Add("Друга тема з нульовим запитанням");
        lines.Add("У цій темі буде нульове запитання за 0 балів.");
        lines.Add("0. Жартівливе нульове запитання.");
        lines.Add("Відповідь: жарт");
        lines.AddRange(Theme("", 6)[1..]); // 10..50 questions with no header line

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Tours.Should().HaveCount(2);
        result.Tours[0].Questions.Should().HaveCount(5);
        result.Tours[1].Questions.Should().HaveCount(5);
        result.Warnings.Should().Contain(w => w.Contains("розпочато нову тему без назви"));
    }

    [Fact]
    public void Parse_NumberedListEntryTen_RecordedAsAnchorNotValue10Question()
    {
        // A numbered «Теми:» list reaching entry «10.»: that entry's number is also a question value,
        // so it must be recognized as list position 10, not the first value-10 question (which would
        // abandon the list, drop the remaining entries and start a spurious theme).
        var lines = new List<string> { "Пакет", "Теми:" };
        for (var i = 1; i <= 11; i++)
        {
            lines.Add($"{i}.\tТема{i} (Прізвище)");
        }
        lines.Add("");
        for (var i = 1; i <= 11; i++)
        {
            lines.AddRange(ThemeWithAuthor($"Тема{i}", "Іван Петренко"));
        }

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Tours.Should().HaveCount(11);
        result.Tours.Select(t => t.Title).Should().Equal(Enumerable.Range(1, 11).Select(i => $"Тема{i}"));
        result.Tours.Should().OnlyContain(t => t.Questions.Count == 5);
        // The full 11-entry list — including entry «10.» — is preserved in the preamble
        result.Preamble.Should().Contain("10.\tТема10 (Прізвище)").And.Contain("11.\tТема11 (Прізвище)");
        result.Warnings.Should().NotContain(w => w.Contains("розпочато нову тему без назви"));
        result.Warnings.Should().NotContain(w => w.Contains("У списку «Теми:»"));
    }

    [Fact]
    public void Parse_ThemeTitleSeparatedFromQuestionsByPreamble_RecoveredViaAnchorCore()
    {
        // The list entry keeps a glued single surname «(Мартиненко)»; the real header is separated
        // from «10.» by a preamble, defeating the before-«10.» lookahead. Anchor-core matching still
        // finds the title (before the fix the theme came out untitled and the title leaked upward).
        var lines = new List<string>
        {
            "Пакет",
            "Теми:",
            "1.\tПомилки (Мартиненко)",
            ""
        };
        lines.AddRange(ThemeWithAuthorAndPreamble(
            "Помилки", "Андрій Мартиненко", "Шоу могли бути не цілком українськими."));

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Tours.Should().ContainSingle();
        result.Tours[0].Title.Should().Be("Помилки");
        result.Tours[0].Editors.Should().ContainSingle().Which.Should().Be("Андрій Мартиненко");
        result.Tours[0].Preamble.Should().Contain("Шоу могли бути");
        result.Tours[0].Questions.Should().HaveCount(5);
    }

    [Fact]
    public void Parse_IncompleteNumberedListWithTenCollision_DetectsAllThemesWithTitles()
    {
        // End-to-end reproduction of «Швагер-супер-кубок 2022»: a numbered «Теми:» list whose 12
        // entries carry a single surname «N.\tНазва (Прізвище)». Entry «10.» collides with a value-10
        // question, and two themes place a preamble between the «Автор:» line and the first «10.».
        var titles = new[]
        {
            "-РОН-", "Кіно", "Польська опера", "Осінні квіти", "Написи", "Прийоми їжі",
            "Помилки", "Не тільки назви телешоу", "Римуй як Сергій Вікторович",
            "Не лише позивні", "Сусіди", "-АНДА-"
        };
        var surnames = new[]
        {
            "Каунін", "Каунін", "Маландіна", "Маландіна", "Шляхов", "Шляхов",
            "Мартиненко", "Мартиненко", "Мерзликін", "Мерзликін", "Вахрів", "Вахрів"
        };

        var lines = new List<string> { "Швагер-супер-кубок 2022", "", "Теми:" };
        for (var i = 0; i < titles.Length; i++)
        {
            lines.Add($"{i + 1}.\t{titles[i]} ({surnames[i]})");
        }
        lines.Add("");

        // Bodies: themes 8 and 9 (0-based 7, 8) carry a preamble between the author line and «10.»
        lines.AddRange(ThemeWithAuthor("-РОН-", "Костянтин Каунін"));
        lines.AddRange(ThemeWithAuthor("Кіно", "Костянтин Каунін"));
        lines.AddRange(ThemeWithAuthor("Польська опера", "Вікторія Маландіна"));
        lines.AddRange(ThemeWithAuthor("Осінні квіти", "Вікторія Маландіна"));
        lines.AddRange(ThemeWithAuthor("Написи", "Євген Шляхов"));
        lines.AddRange(ThemeWithAuthor("Прийоми їжі", "Євген Шляхов"));
        lines.AddRange(ThemeWithAuthor("Помилки", "Андрій Мартиненко"));
        lines.AddRange(ThemeWithAuthorAndPreamble(
            "Не тільки назви телешоу", "Андрій Мартиненко", "Шоу могли бути не цілком українськими."));
        lines.AddRange(ThemeWithAuthorAndPreamble(
            "Римуй як Сергій Вікторович", "Олександр Мерзликін", "У відповіді – заримовані слова."));
        lines.AddRange(ThemeWithAuthor("Не лише позивні", "Олександр Мерзликін"));
        lines.AddRange(ThemeWithAuthor("Сусіди", "Тарас Вахрів"));
        lines.AddRange(ThemeWithAuthor("-АНДА-", "Тарас Вахрів"));

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        // Exactly 12 themes (no spurious extra), each titled and complete
        result.Tours.Should().HaveCount(12);
        result.Tours.Select(t => t.Title).Should().Equal(titles);
        result.Tours.Should().OnlyContain(t => t.Questions.Count == 5);

        // The two preamble-separated themes carry their authors and preamble
        result.Tours[8].Title.Should().Be("Римуй як Сергій Вікторович");
        result.Tours[8].Editors.Should().ContainSingle().Which.Should().Be("Олександр Мерзликін");
        result.Tours[8].Preamble.Should().Contain("заримовані слова");

        // The full 12-entry list stays in the preamble; nothing leaked into question text
        result.Preamble.Should().Contain("12.\t-АНДА- (Вахрів)");

        // None of the boundary/count/answer warnings the broken parse produced
        result.Warnings.Should().NotContain(w => w.Contains("розпочато нову тему без назви"));
        result.Warnings.Should().NotContain(w => w.Contains("У списку «Теми:»"));
        result.Warnings.Should().NotContain(w => w.Contains("не знайдено відповідь"));
        result.Warnings.Should().NotContain(w => w.Contains("запитань замість"));
    }

    #endregion

    #region Question values

    [Fact]
    public void Parse_FiveQuestions_ValuesAssigned()
    {
        var result = Parse(Theme("Тема: Тест"));

        var questions = result.Tours[0].Questions;
        questions.Should().HaveCount(5);
        questions.Select(q => q.Number).Should().Equal("10", "20", "30", "40", "50");
        questions.All(q => q.HasAnswer).Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ValueOutOfOrder_AddsWarning()
    {
        var result = Parse(
            "Тема: Тест",
            "10. Перше?",
            "Відповідь: а",
            "30. Третє без другого?",
            "Відповідь: б");

        result.Tours[0].Questions.Should().HaveCount(2);
        result.Warnings.Should().Contain(w => w.Contains("очікувалося запитання за 20"));
    }

    [Fact]
    public void Parse_ThemeWithLessThanFiveQuestions_AddsWarning()
    {
        var result = Parse(
            "Тема: Коротка",
            "10. Одне?",
            "Відповідь: а");

        result.Warnings.Should().Contain(w => w.Contains("1 запитань замість 5"));
    }

    [Fact]
    public void Parse_QuestionWithoutAnswer_AddsWarning()
    {
        var result = Parse(
            "Тема: Тест",
            "10. Питання без відповіді?",
            "20. Наступне?",
            "Відповідь: є");

        result.Warnings.Should().Contain(w => w.Contains("за 10") && w.Contains("не знайдено відповідь"));
    }

    [Fact]
    public void Parse_ValueWithoutSpaceAfterPeriod_DetectedAsQuestion()
    {
        // Real packages sometimes omit the space after the value's period («30.В дитинстві…»).
        // The question must still be detected, not absorbed as content of the previous one.
        var result = Parse(
            "Тема: Тест",
            "10. Перше?",
            "Відповідь: а",
            "20.Друге без пробілу після крапки?",
            "Відповідь: б");

        result.Tours[0].Questions.Should().HaveCount(2);
        result.Tours[0].Questions[1].Number.Should().Be("20");
        result.Tours[0].Questions[1].Text.Should().Contain("без пробілу");
        result.Tours[0].Questions[1].Answer.Should().Be("б");
        // The value line was not absorbed as content of the previous question
        result.Tours[0].Questions[0].Comment.Should().BeNull();
    }

    [Fact]
    public void Parse_BareTitleBeforeNoSpaceValue10_DetectsThemeAndFirstQuestion()
    {
        // Regression: the second theme «Антарктида» is a bare title whose first question is written
        // «10.У 1957…» with no space after the period. Both must be recognized so the theme is not
        // merged into the previous one and left starting at «20.».
        var lines = new List<string>();
        lines.AddRange(Theme("Паралімпіада"));
        lines.Add("Антарктида");
        lines.Add("10.У 1957 році загинув студент Зиков?");
        lines.Add("Відповідь: Ніжин");
        lines.Add("20. Друге?");
        lines.Add("Відповідь: діра");
        lines.Add("30.Третє без пробілу?");
        lines.Add("Відповідь: гармата");
        lines.Add("40. Четверте?");
        lines.Add("Відповідь: комар");
        lines.Add("50. П'яте?");
        lines.Add("Відповідь: пальма");

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Tours.Should().HaveCount(2);
        result.Tours[1].Title.Should().Be("Антарктида");
        result.Tours[1].Questions.Select(q => q.Number).Should().Equal("10", "20", "30", "40", "50");
        result.Warnings.Should().NotContain(w => w.Contains("без назви"));
    }

    [Theory]
    [InlineData("11. Не кратне десяти")]
    [InlineData("110. Завелике")]
    [InlineData("1932. Рік — не запитання")]
    [InlineData("20.05.1986 сталася аварія.")]
    [InlineData("10:00 за Києвом.")]
    [InlineData("10.1038/s41586-020-2649-2")]
    public void Parse_NonValueNumberedLine_IsNotAQuestion(string line)
    {
        var result = Parse(
            "Тема: Тест",
            "10. Питання?",
            "Коментар: коментар",
            line,
            "20. Друге?",
            "Відповідь: б");

        result.Tours[0].Questions.Should().HaveCount(2);
        result.Tours[0].Questions[0].Comment.Should().NotBeNull("the line must land in the comment, not start a question");
    }

    #endregion

    #region Labels

    [Fact]
    public void Parse_AllLabels_RoutedToFields()
    {
        var result = Parse(
            "Тема: Жереб",
            "10. У відповіді два слова. ВОНА згадується у фразеологізмі.",
            "Відповідь: коротка соломинка",
            "Залік: соломинка",
            "Незалік: довга соломинка",
            "Форма: вона, двома словами",
            "Коментар: фразеологізм — draw the short straw.",
            "Джерело: https://dictionary.cambridge.org/dictionary/english/draw",
            "Автор: Євген Шляхов.");

        var q = result.Tours[0].Questions[0];
        q.Text.Should().Contain("ВОНА згадується");
        q.Answer.Should().Be("коротка соломинка");
        q.AcceptedAnswers.Should().Be("соломинка");
        q.RejectedAnswers.Should().Be("довга соломинка");
        q.Form.Should().Be("вона, двома словами");
        q.Comment.Should().Contain("draw the short straw");
        q.Source.Should().Contain("dictionary.cambridge.org");
        q.Authors.Should().ContainSingle().Which.Should().Be("Євген Шляхов");
    }

    [Fact]
    public void Parse_GlobalEditor_InheritedByQuestionsWithoutExplicitAuthor()
    {
        var lines = new List<string>
        {
            "Пакет",
            "Редактор та автор тем - Олександр Мерзликін (Кривий Ріг)",
            "Тема: Тест",
            "10. Перше питання?",
            "Відповідь: а",
            "20. Друге питання з власним автором?",
            "Відповідь: б",
            "Автор: Іван Петренко"
        };

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        var questions = result.Tours[0].Questions;
        // Question without an explicit author inherits the global editor
        questions[0].Authors.Should().ContainSingle().Which.Should().Be("Олександр Мерзликін");
        // Explicit «Автор:» always wins
        questions[1].Authors.Should().ContainSingle().Which.Should().Be("Іван Петренко");
    }

    [Fact]
    public void Parse_ThemeAuthors_TakePrecedenceOverGlobalEditor()
    {
        var lines = new List<string>
        {
            "Пакет",
            "Редактор Олександр Мерзликін",
            "Тема: Тест (Євген Шляхов)",
            "10. Питання без автора?",
            "Відповідь: а"
        };

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Tours[0].Questions[0].Authors.Should().ContainSingle()
            .Which.Should().Be("Євген Шляхов");
    }

    [Fact]
    public void Parse_NoGlobalOrThemeAuthors_QuestionAuthorsStayEmpty()
    {
        var result = Parse(
            "Тема: Тест",
            "10. Питання?",
            "Відповідь: а");

        result.Tours[0].Questions[0].Authors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_AuthorWithCity_CityStripped()
    {
        var result = Parse(
            "Тема: Тест",
            "10. Питання?",
            "Відповідь: а",
            "Автор: Євген Шляхов (Дніпро)");

        // City is stripped by UkrainianNameHelper during author-list parsing
        result.Tours[0].Questions[0].Authors.Should().ContainSingle()
            .Which.Should().Be("Євген Шляхов");
    }

    [Fact]
    public void Parse_MultilineComment_Preserved()
    {
        var result = Parse(
            "Тема: Тест",
            "10. Питання?",
            "Відповідь: а",
            "Коментар: перший рядок",
            "другий рядок");

        result.Tours[0].Questions[0].Comment.Should().Be("перший рядок\nдругий рядок");
    }

    #endregion

    #region Numbered bare titles

    [Fact]
    public void Parse_NumberedBareTitleInListAndBody_StripsNumberPrefix()
    {
        // Real-world format: the «Теми:» list AND the body headers are numbered «N. Назва»
        var lines = new List<string>
        {
            "Пакет",
            "Теми:",
            "5. ПОЛІТИКИ З П'ЯТИБУКВЕНИМИ ПРІЗВИЩАМИ",
            ""
        };
        lines.AddRange(Theme("5. ПОЛІТИКИ З П'ЯТИБУКВЕНИМИ ПРІЗВИЩАМИ"));

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Tours.Should().HaveCount(1);
        result.Tours[0].Title.Should().Be("ПОЛІТИКИ З П'ЯТИБУКВЕНИМИ ПРІЗВИЩАМИ");
    }

    [Fact]
    public void Parse_NumberedBareTitleWithoutList_StripsNumberPrefix()
    {
        // Fallback path (no «Теми:» list): a numbered title right before «10.»
        var result = Parse(Theme("5. ПОЛІТИКИ З П'ЯТИБУКВЕНИМИ ПРІЗВИЩАМИ"));

        result.Tours.Should().HaveCount(1);
        result.Tours[0].Title.Should().Be("ПОЛІТИКИ З П'ЯТИБУКВЕНИМИ ПРІЗВИЩАМИ");
    }

    #endregion

    #region Inline labels

    [Fact]
    public void Parse_InlineZalik_SplitsAnswerAndAcceptedAnswers()
    {
        var result = Parse(
            "Тема: Тест",
            "10. Хто ця жінка?",
            "Відповідь: Інді́ра Га́нді. Залік: за прізвищем.");

        var q = result.Tours[0].Questions[0];
        q.Answer.Should().Be("Інді́ра Га́нді.");
        q.AcceptedAnswers.Should().Be("за прізвищем.");
    }

    [Fact]
    public void Parse_InlineNezalikAndForma_Split()
    {
        var result = Parse(
            "Тема: Тест",
            "10. Питання?",
            "Відповідь: соломинка. Незалік: трубочка. Форма: одним словом");

        var q = result.Tours[0].Questions[0];
        q.Answer.Should().Be("соломинка.");
        q.RejectedAnswers.Should().Be("трубочка.");
        q.Form.Should().Be("одним словом");
    }

    [Fact]
    public void Parse_ZalikAtLineStart_StillWorks()
    {
        var result = Parse(
            "Тема: Тест",
            "10. Питання?",
            "Відповідь: а",
            "Залік: б");

        var q = result.Tours[0].Questions[0];
        q.Answer.Should().Be("а");
        q.AcceptedAnswers.Should().Be("б");
    }

    #endregion

    #region Bracketed handouts

    [Fact]
    public void Parse_BracketedHandout_OwnLine_ParsedAsHandoutText()
    {
        var result = Parse(
            "Тема: Тест",
            "10. [Роздатковий матеріал: фаресімі ]",
            "Фа́ресімі - ЦЕ.",
            "Відповідь: факсиміле");

        var q = result.Tours[0].Questions[0];
        q.HandoutText.Should().Be("фаресімі");
        q.Text.Should().Be("Фа́ресімі - ЦЕ.");
        q.Text.Should().NotContain("Роздатковий");
    }

    [Fact]
    public void Parse_BracketedHandout_WithTextAfterBracket_SplitsCorrectly()
    {
        var result = Parse(
            "Тема: Тест",
            "10. [Роздатка: слово] Що це?",
            "Відповідь: а");

        var q = result.Tours[0].Questions[0];
        q.HandoutText.Should().Be("слово");
        q.Text.Should().Be("Що це?");
    }

    [Fact]
    public void Parse_BracketedHandout_Multiline_Collected()
    {
        var result = Parse(
            "Тема: Тест",
            "10. [Роздатковий матеріал: перший рядок",
            "другий рядок]",
            "Текст запитання?",
            "Відповідь: а");

        var q = result.Tours[0].Questions[0];
        q.HandoutText.Should().Be("перший рядок\nдругий рядок");
        q.Text.Should().Be("Текст запитання?");
    }

    [Fact]
    public void Parse_HandoutMarkerThenBareBracket_TextAfterCloseIsQuestion()
    {
        // Real shape: «10. Роздатка:» announces a handout, then the content is wrapped in a bare
        // «[ … ]» bracket on following lines (marker and bracket decoupled). The bracket content is
        // the handout; text after the closing «]» is the question, not more handout.
        var result = Parse(
            "Тема: Тест",
            "10. Роздатка:",
            "[",
            "https://ibb.co/zNH9qnt",
            "]",
            "",
            "За легендою, сир мав іншу форму.",
            "Відповідь: Наполеон");

        var q = result.Tours[0].Questions[0];
        q.HandoutText.Should().Be("https://ibb.co/zNH9qnt");
        q.Text.Should().Be("За легендою, сир мав іншу форму.");
        q.Answer.Should().Be("Наполеон");
    }

    [Fact]
    public void Parse_HandoutMarkerThenSingleLineBareBracket_TextAfterCloseIsQuestion()
    {
        // The decoupled bracket may also be single-line: «Роздатка:» then «[url] Текст».
        var result = Parse(
            "Тема: Тест",
            "10. Роздатка:",
            "[слово] Що це?",
            "Відповідь: а");

        var q = result.Tours[0].Questions[0];
        q.HandoutText.Should().Be("слово");
        q.Text.Should().Be("Що це?");
    }

    [Fact]
    public void Parse_HandoutLabelThenImage_QuestionTextNotSwallowedIntoHandout()
    {
        // «20. Роздатковий матеріал:» (empty inline text) announces an image handout. The image is
        // the handout; the text after it is the question, not more handout text.
        var blocks = new List<DocBlock>
        {
            Block("Тема: Тест", 0),
            Block("10. Перше?", 1),
            Block("Відповідь: а", 2),
            Block("20. Роздатковий матеріал:", 3),
            BlockWithAsset(" ", "img1.jpeg", 4),
            Block("В 1969 році після автотрощі ВІН змінив діяльність?", 5),
            Block("Відповідь: Абебе Бікіла", 6)
        };

        var result = _parser.Parse(blocks, []);

        var q = result.Tours[0].Questions[1];
        q.Number.Should().Be("20");
        q.HandoutAssetFileName.Should().Be("img1.jpeg");
        q.Text.Should().Contain("В 1969 році");
        q.HandoutText.Should().BeNull();
        q.Answer.Should().Be("Абебе Бікіла");
    }

    #endregion

    #region Host instructions

    [Fact]
    public void Parse_HostInstructions_OwnLine_ParsedAsHostInstructions()
    {
        var result = Parse(
            "Тема: Тест",
            "10. Текст запитання?",
            "[Ведучому: читати повільно]",
            "Відповідь: а");

        var q = result.Tours[0].Questions[0];
        q.HostInstructions.Should().Be("читати повільно");
        q.Text.Should().Be("Текст запитання?");
        q.Text.Should().NotContain("Ведучому");
    }

    [Fact]
    public void Parse_HostInstructions_OnValueLineWithTextAfter_SplitsCorrectly()
    {
        var result = Parse(
            "Тема: Тест",
            "10. [Ведучому: наголос на перший склад] Що це?",
            "Відповідь: а");

        var q = result.Tours[0].Questions[0];
        q.HostInstructions.Should().Be("наголос на перший склад");
        q.Text.Should().Be("Що це?");
        q.Text.Should().NotContain("Ведучому");
    }

    [Fact]
    public void Parse_HostInstructions_RealExample_ExtractedFromText()
    {
        // The «[Ведучому: …]» text from the imported package that motivated this feature
        var result = Parse(
            "Тема: Тест",
            "10. [Ведучому: читаючи слово, мазоГізму прочитати так, щоб гравці розчули саме мазоГізму, а не мазоХізму] Текст запитання?",
            "Відповідь: а");

        var q = result.Tours[0].Questions[0];
        q.HostInstructions.Should().Contain("мазоГізму").And.Contain("мазоХізму");
        q.Text.Should().Be("Текст запитання?");
        q.Text.Should().NotContain("Ведучому");
    }

    [Fact]
    public void Parse_HostInstructions_VariantLabel_ParsedAsHostInstructions()
    {
        var result = Parse(
            "Тема: Тест",
            "10. Текст?",
            "[Вказівка ведучому: пауза перед відповіддю]",
            "Відповідь: а");

        result.Tours[0].Questions[0].HostInstructions.Should().Be("пауза перед відповіддю");
    }

    [Fact]
    public void Parse_HostInstructions_BeforeAnyQuestion_NotTreatedAsHostInstructions()
    {
        // A «[Ведучому: …]» line before the first question has no question to attach to;
        // it must not crash and must not become a phantom host instruction.
        var result = Parse(
            "Тема: Тест",
            "[Ведучому: загальна вказівка]",
            "10. Текст?",
            "Відповідь: а");

        var q = result.Tours[0].Questions[0];
        q.HostInstructions.Should().BeNull();
        q.Text.Should().Be("Текст?");
    }

    #endregion

    #region Picture brackets

    [Fact]
    public void Parse_PictureBracketWithUrl_MovedToHandout()
    {
        var result = Parse(
            "Тема: Тест",
            "10. [Малюнок 1 https://drive.google.com/file/d/abc/view] Ми закрили підписи. ЯК підписано останній?",
            "Відповідь: Орегон");

        var q = result.Tours[0].Questions[0];
        q.HandoutText.Should().Contain("https://drive.google.com/file/d/abc/view");
        q.Text.Should().NotContain("Малюнок");
        q.Text.Should().Contain("Ми закрили підписи");
        result.Warnings.Should().Contain(w => w.Contains("перенесено до роздаткового матеріалу"));
    }

    #endregion

    #region Package header

    [Fact]
    public void Parse_TitleAndSubtitle_Combined()
    {
        var result = Parse(Theme("Тема: Тест").Prepend("Фінальний раунд").Prepend("Швагер-ліга 2026 Весна").ToArray());

        result.Title.Should().Be("Швагер-ліга 2026 Весна. Фінальний раунд");
    }

    [Fact]
    public void Parse_LongHeaderLines_GoToPreamble()
    {
        var lines = new List<string>
        {
            "Швагер-ліга 2026 Весна",
            "Допомогли покращити запитання: Андрій Гахун, Іван Грабовський, Андрій Данченко, Андрій Задворнов.",
            "Окрема подяка нашим захисникам і захисницям!"
        };
        lines.AddRange(Theme("Тема: Тест"));

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Title.Should().Be("Швагер-ліга 2026 Весна");
        result.Preamble.Should().Contain("Допомогли покращити").And.Contain("Окрема подяка");
    }

    [Fact]
    public void Parse_EditorAuthorOfAllQuestionsHeader_ParsedAsPackageEditors()
    {
        // "Редактор та автор усіх запитань: …" — the scope qualifier is «усіх запитань», not «тем»,
        // and the name carries a city that must be stripped.
        var lines = new List<string>
        {
            "Швагер-ліга",
            "Осінь-2021. Тиждень 4",
            "Редактор та автор усіх запитань: Едуард Голуб (Київ)"
        };
        lines.AddRange(Theme("Тема: Тест"));

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Title.Should().Be("Швагер-ліга. Осінь-2021. Тиждень 4");
        result.SharedEditors.Should().BeTrue();
        result.PackageEditors.Should().ContainSingle().Which.Should().Be("Едуард Голуб");
    }

    [Fact]
    public void Parse_EditorAcknowledgmentLine_NotParsedAsEditors()
    {
        // "Редактор вдячний за тестування: Імена…" is thanks, not an editor line. The enumerated
        // scope qualifier must not swallow «вдячний за тестування», so the names fail validation
        // and the line goes to the preamble.
        var lines = new List<string>
        {
            "Пакет",
            "Редактор вдячний за тестування: Миколі Королю, Роману Немучинському, Ігорю Пальті"
        };
        lines.AddRange(Theme("Тема: Тест"));

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.SharedEditors.Should().BeFalse();
        result.PackageEditors.Should().BeEmpty();
        result.Preamble.Should().Contain("вдячний за тестування");
    }

    [Fact]
    public void Parse_AnchorCountMismatch_AddsWarning()
    {
        var lines = new List<string>
        {
            "Пакет",
            "Теми:",
            "Авіація",
            "Змії",
            ""
        };
        lines.AddRange(Theme("Авіація"));
        // Theme «Змії» is missing from the body

        var result = _parser.Parse(Blocks(lines.ToArray()), []);

        result.Tours.Should().HaveCount(1);
        result.Warnings.Should().Contain(w => w.Contains("2 тем") && w.Contains("знайдено 1"));
    }

    #endregion
}
