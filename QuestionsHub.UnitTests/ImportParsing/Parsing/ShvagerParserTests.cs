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
