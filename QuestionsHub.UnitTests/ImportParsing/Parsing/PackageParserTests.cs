using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QuestionsHub.Blazor.Infrastructure.Import;
using Xunit;

namespace QuestionsHub.UnitTests.ImportParsing.Parsing;

/// <summary>
/// Unit tests for PackageParser using synthetic DocBlocks.
/// These tests focus on parser logic without needing real DOCX files.
/// </summary>
public class PackageParserTests
{
    private readonly PackageParser _parser;

    public PackageParserTests()
    {
        _parser = new PackageParser(NullLogger<PackageParser>.Instance);
    }

    #region Tour Detection

    [Theory]
    [InlineData("ТУР 1", "1")]
    [InlineData("Тур 2", "2")]
    [InlineData("Tour 3", "3")]
    [InlineData("  ТУР 1  ", "1")]
    public void Parse_TourStart_DetectsTour(string tourLine, string expectedNumber)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block(tourLine),
            Block("1. Питання тесту"),
            Block("Відповідь: Відповідь тесту")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(1);
        result.Tours[0].Number.Should().Be(expectedNumber);
    }

    [Theory]
    [InlineData("- ТУР 1 -", "1")]
    [InlineData("– Тур 2 –", "2")]
    [InlineData("— ТУР 3 —", "3")]
    public void Parse_DashedTourStart_DetectsTour(string tourLine, string expectedNumber)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block(tourLine),
            Block("1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(1);
        result.Tours[0].Number.Should().Be(expectedNumber);
    }

    [Fact]
    public void Parse_MultipleTours_ParsesAll()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання 1"),
            Block("Відповідь: Відповідь 1"),
            Block("ТУР 2"),
            Block("1. Питання 2"),
            Block("Відповідь: Відповідь 2"),
            Block("ТУР 3"),
            Block("1. Питання 3"),
            Block("Відповідь: Відповідь 3")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(3);
        result.Tours[0].Number.Should().Be("1");
        result.Tours[1].Number.Should().Be("2");
        result.Tours[2].Number.Should().Be("3");
    }

    #endregion

    #region Question Detection

    [Theory]
    [InlineData("1. Текст питання", "1", "Текст питання")]
    [InlineData("1.  Питання з пробілами  ", "1", "Питання з пробілами")]
    public void Parse_QuestionWithText_ExtractsNumberAndText(string line, string expectedNumber, string expectedText)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block(line),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions.Should().HaveCount(1);
        var question = result.Tours[0].Questions[0];
        question.Number.Should().Be(expectedNumber);
        question.Text.Should().Contain(expectedText.Trim());
    }

    [Fact]
    public void Parse_QuestionNumberOnly_ContinuesWithNextBlock()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1."),
            Block("Текст питання на наступному рядку"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];
        question.Number.Should().Be("1");
        question.Text.Should().Contain("Текст питання на наступному рядку");
    }

    [Theory]
    [InlineData("Запитання 1")]
    [InlineData("Питання 1.")]
    [InlineData("Запитання 1:")]
    [InlineData("Питання 1:")]
    public void Parse_NamedQuestionStart_DetectsQuestion(string line)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block(line),
            Block("Текст питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions.Should().NotBeEmpty();
    }

    [Fact]
    public void Parse_QuestionWithColonSeparator_ParsesFullStructure()
    {
        // Arrange - format from real packages: "Запитання N:" on separate line
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання 1:"),
            Block("У вже згаданій серії «Надприродного» ВІН парадоксально нападає на героя."),
            Block("Відповідь: Махатма Ґанді"),
            Block("Залік: за прізвищем"),
            Block("Коментар: перевертень з епізоду із попереднього запитання перетворився на вегетаріанця."),
            Block("Джерела: Надприродне сезон 5, серія 5")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(1);
        result.Tours[0].Questions.Should().HaveCount(1);

        var question = result.Tours[0].Questions[0];
        question.Number.Should().Be("1");
        question.Text.Should().Contain("Надприродного");
        question.Answer.Should().Be("Махатма Ґанді");
        question.AcceptedAnswers.Should().Be("за прізвищем");
        question.Comment.Should().Contain("перевертень");
        question.Source.Should().Contain("Надприродне сезон 5");
    }

    [Fact]
    public void Parse_MultipleQuestionsWithColonSeparator_ParsesAll()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання 1:"),
            Block("Перше питання тексту"),
            Block("Відповідь: Перша відповідь"),
            Block("Запитання 2:"),
            Block("Друге питання тексту"),
            Block("Відповідь: Друга відповідь"),
            Block("Питання 3:"),
            Block("Третє питання тексту"),
            Block("Відповідь: Третя відповідь")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions.Should().HaveCount(3);
        result.Tours[0].Questions[0].Number.Should().Be("1");
        result.Tours[0].Questions[1].Number.Should().Be("2");
        result.Tours[0].Questions[2].Number.Should().Be("3");
    }

    #endregion

    #region Answer Parsing

    [Theory]
    [InlineData("Відповідь: Київ", "Київ")]
    [InlineData("Відповідь:Київ", "Київ")]
    [InlineData("  Відповідь:   Харків  ", "Харків")]
    [InlineData("ВІДПОВІДЬ: Львів", "Львів")]
    public void Parse_AnswerLabel_ExtractsAnswer(string answerLine, string expectedAnswer)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання"),
            Block(answerLine)
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];
        question.Answer.Should().Be(expectedAnswer);
        question.HasAnswer.Should().BeTrue();
    }

    [Fact]
    public void Parse_MultiLineAnswer_ConcatenatesText()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Частина 1"),
            Block("Частина 2 відповіді")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];
        question.Answer.Should().Contain("Частина 1");
        question.Answer.Should().Contain("Частина 2 відповіді");
    }

    #endregion

    #region Accepted/Rejected Answers

    [Theory]
    [InlineData("Залік: варіант1, варіант2", "варіант1, варіант2")]
    [InlineData("Залік: варіант1 [необов'язкова частина]; варіант2", "варіант1 [необов'язкова частина]; варіант2")]
    [InlineData("Залік:прийнятна відповідь", "прийнятна відповідь")]
    public void Parse_AcceptedLabel_ExtractsAcceptedAnswers(string line, string expected)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест"),
            Block(line)
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions[0].AcceptedAnswers.Should().Contain(expected);
    }

    [Theory]
    [InlineData("Незалік: неправильно", "неправильно")]
    [InlineData("Не залік: варіант", "варіант")]
    [InlineData("Не приймається: відповідь", "відповідь")]
    public void Parse_RejectedLabel_ExtractsRejectedAnswers(string line, string expected)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест"),
            Block(line)
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions[0].RejectedAnswers.Should().Contain(expected);
    }

    [Theory]
    [InlineData("Заліки: Симиренки", "Симиренки")]
    [InlineData("Залік (не оголошувати): за прізвищем", "за прізвищем")]
    public void Parse_AcceptedLabel_Variants_AreDetected(string line, string expected)
    {
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест"),
            Block(line)
        };

        var result = _parser.Parse(blocks, []);

        result.Tours[0].Questions[0].AcceptedAnswers.Should().Contain(expected);
    }

    [Fact]
    public void Parse_Zaliki_DoesNotPolluteAnswer()
    {
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Київ"),
            Block("Заліки: Київ; столиця")
        };

        var result = _parser.Parse(blocks, []);

        var q = result.Tours[0].Questions[0];
        q.Answer.Should().Be("Київ");
        q.AcceptedAnswers.Should().Contain("Київ; столиця");
    }

    #endregion

    #region Comment and Source

    [Theory]
    [InlineData("Коментар: Цікавий факт", "Цікавий факт")]
    [InlineData("КОМЕНТАР: Інформація", "Інформація")]
    public void Parse_CommentLabel_ExtractsComment(string line, string expected)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест"),
            Block(line)
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions[0].Comment.Should().Contain(expected);
    }

    [Theory]
    [InlineData("Джерело: Вікіпедія", "Вікіпедія")]
    [InlineData("Джерела: книга, сайт", "книга, сайт")]
    public void Parse_SourceLabel_ExtractsSource(string line, string expected)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест"),
            Block(line)
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions[0].Source.Should().Contain(expected);
    }

    [Fact]
    public void Parse_MultiLineSource_ConcatenatesText()
    {
        // Arrange - multi-line sources appear as continuation blocks
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест"),
            Block("Джерело: site 1"),
            Block("site 2"),
            Block("site 3")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var source = result.Tours[0].Questions[0].Source;
        source.Should().Contain("site 1");
        source.Should().Contain("site 2");
        source.Should().Contain("site 3");
    }

    #endregion

    #region Authors

    [Theory]
    [InlineData("Автор: Іван Петренко", new[] { "Іван Петренко" })]
    [InlineData("Автори: Іван, Марія", new[] { "Іван", "Марія" })]
    [InlineData("Автор: Іван та Марія", new[] { "Іван", "Марія" })]
    [InlineData("Автори: Іван; Марія; Петро", new[] { "Іван", "Марія", "Петро" })]
    public void Parse_AuthorLabel_ExtractsAuthors(string line, string[] expectedAuthors)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест"),
            Block(line)
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions[0].Authors.Should().BeEquivalentTo(expectedAuthors);
    }

    #endregion

    #region Host Instructions

    [Theory]
    [InlineData("[Ведучому: читати повільно] Текст", "читати повільно")]
    [InlineData("[Ведучому-(ій): наголос на слові] Текст", "наголос на слові")]
    [InlineData("[Вказівка ведучому: пауза 5 секунд] Текст", "пауза 5 секунд")]
    public void Parse_HostInstructions_ExtractsInstructions(string line, string expectedInstruction)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання"),
            Block(line),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions[0].HostInstructions.Should().Contain(expectedInstruction);
    }

    #endregion

    #region Handout

    [Theory]
    [InlineData("Роздатка: Таблиця даних", "Таблиця даних")]
    [InlineData("Роздатковий матеріал: Текст для гравців", "Текст для гравців")]
    public void Parse_HandoutMarker_ExtractsHandoutText(string line, string expected)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання"),
            Block(line),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions[0].HandoutText.Should().Contain(expected);
    }

    [Fact]
    public void Parse_BracketedHandoutMarker_ExtractsHandoutTextAndQuestionText()
    {
        // Arrange - Bracketed handout with question text after closing bracket
        // This is the format: 1. [Роздатковий матеріал: handout text] question text
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. [Роздатковий матеріал: ] Текст питання після роздатки"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];
        question.Text.Should().Contain("Текст питання після роздатки");
        question.Text.Should().NotContain("Роздатковий матеріал");
        question.Text.Should().NotContain("[");
        question.Text.Should().NotContain("]");
    }

    [Theory]
    [InlineData("[Роздатка: Таблиця] Питання", "Таблиця", "Питання")]
    [InlineData("[Роздатковий матеріал: Схема] Який результат?", "Схема", "Який результат?")]
    [InlineData("[Роздатка: ] Питання без тексту роздатки", "", "Питання без тексту роздатки")]
    public void Parse_BracketedHandoutWithText_SeparatesHandoutAndQuestion(
        string line, string expectedHandout, string expectedQuestionText)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. " + line),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];
        question.Text.Should().Contain(expectedQuestionText);
        question.Text.Should().NotContain("[Роздат");

        if (!string.IsNullOrEmpty(expectedHandout))
        {
            question.HandoutText.Should().Contain(expectedHandout);
        }
    }

    [Fact]
    public void Parse_MultilineBracketedHandoutSimple_ExtractsQuestionText()
    {
        // Arrange - Simplest multiline case: opening and closing in separate blocks
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("[Роздатка:"),
            Block("]"),
            Block("Текст після роздатки."),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];
        question.Text.Should().Contain("Питання");
        question.Text.Should().Contain("Текст після роздатки.");
    }

    [Fact]
    public void Parse_MultilineBracketedHandoutWithNamedQuestion_ExtractsQuestionText()
    {
        // Arrange - Using "Запитання N:" format like the real test
        var imageAsset = new AssetReference
        {
            FileName = "image001.png",
            RelativeUrl = "/media/image001.png",
            ContentType = "image/png"
        };

        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання 1:"),
            Block("[Роздатковий матеріал:"),
            Block("", [imageAsset]),
            Block("]"),
            Block("Текст після роздатки."),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];
        question.Text.Should().Be("Текст після роздатки.");
        question.HandoutAssetFileName.Should().Be("image001.png");
    }

    [Fact]
    public void Parse_MultilineBracketedHandout_ClosingBracketAndTextInSameBlock()
    {
        // Arrange - Closing bracket and question text in same block (separated by newlines)
        var imageAsset = new AssetReference
        {
            FileName = "image001.png",
            RelativeUrl = "/media/image001.png",
            ContentType = "image/png"
        };

        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання 1:"),
            Block("[Роздатковий матеріал:"),
            Block("", [imageAsset]),
            Block(" \n  ] \nТекст після роздатки."),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];
        question.Text.Should().Be("Текст після роздатки.");
        question.HandoutAssetFileName.Should().Be("image001.png");
    }

    [Fact]
    public void Parse_MultilineBracketedHandoutWithImageAsset_ExtractsCleanQuestionText()
    {
        // Arrange - Multiline bracketed handout with image asset spanning multiple lines
        // Format:
        // Запитання 1:
        // [Роздатковий матеріал:
        // <imageAsset>
        //   ]
        // Перед вами одна з робіт проєкту...
        var imageAsset = new AssetReference
        {
            FileName = "image001.png",
            RelativeUrl = "/media/image001.png",
            ContentType = "image/png"
        };

        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання 1:"),
            Block("[Роздатковий матеріал:"),
            Block("",[imageAsset]),
            Block(" \n  ] \nПеред вами одна з робіт проєкту, автори якого займаються приверненням уваги до досліджень ЙОГО. Назвіть ЙОГО."),
            Block("Відповідь: глобальне потепління.")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];

        // Question text should NOT contain the bracketed handout markers or empty brackets
        question.Text.Should().Be("Перед вами одна з робіт проєкту, автори якого займаються приверненням уваги до досліджень ЙОГО. Назвіть ЙОГО.");
        question.Text.Should().NotContain("[Роздатковий матеріал");
        question.Text.Should().NotContain("]");

        // The image asset should be associated with the handout
        question.HandoutAssetFileName.Should().Be("image001.png");

        // Handout text should be empty (only image was in the handout)
        question.HandoutText.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Parse_MultilineBracketedHandoutWithTextAndImage_ExtractsHandoutTextAndQuestionText()
    {
        // Arrange - Multiline bracketed handout with both text and image
        var imageAsset = new AssetReference
        {
            FileName = "image001.png",
            RelativeUrl = "/media/image001.png",
            ContentType = "image/png"
        };

        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. [Роздатка: Опис зображення\n  ] \nЯке місто зображено?",
                [imageAsset]),
            Block("Відповідь: Київ")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];
        question.Text.Should().Be("Яке місто зображено?");
        question.HandoutText.Should().Be("Опис зображення");
        question.HandoutAssetFileName.Should().Be("image001.png");
    }

    #endregion

    #region Package Header

    [Fact]
    public void Parse_PackageHeader_ExtractsTitleAndEditors()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Назва пакету питань"),
            Block("Редактор: Іван Петренко"),
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Title.Should().Be("Назва пакету питань");
        result.Editors.Should().Contain("Іван Петренко");
    }

    [Fact]
    public void Parse_TwoHeaderBlocksWithNoFontInfo_TakesFirstAsTitle()
    {
        // Arrange - Basic case with no font size info
        var blocks = new List<DocBlock>
        {
            Block("Перший блок"),
            Block("Другий блок"),
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Title.Should().Be("Перший блок");
        result.Preamble.Should().Contain("Другий блок");
    }

    [Fact]
    public void Parse_PackageWithPreamble_ExtractsPreamble()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Назва пакету"),
            Block("Опис пакету або вступ"),
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Preamble.Should().Contain("Опис пакету або вступ");
    }

    [Fact]
    public void Parse_MultiLineTitleWithSameFontSize_CombinesTitleBlocks()
    {
        // Arrange - Two blocks with same font size (18pt = 36 half-points)
        // Block 3 has smaller font (14pt = 28 half-points) - should be editor, not title
        var blocks = new List<DocBlock>
        {
            BlockWithFont("Благодійний турнір:", 18),
            BlockWithFont("«Синхрон синів маминої подруги»", 18),
            BlockWithFont("Редактори: Антон Куперман (Одеса), Марков Владислав (Львів-Запоріжжя)", 14),
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Title.Should().Be("Благодійний турнір: «Синхрон синів маминої подруги»");
        result.Editors.Should().Contain("Антон Куперман (Одеса)");
        result.Editors.Should().Contain("Марков Владислав (Львів-Запоріжжя)");
    }

    [Fact]
    public void Parse_TitleWithIncreasingThenDecreasingFontSize_IncludesLargerFontBlocks()
    {
        // Arrange - pt 15, then pt 18, then pt 15
        // Title should include first two blocks (up to and including largest font)
        var blocks = new List<DocBlock>
        {
            BlockWithFont("Перша частина заголовка", 15),
            BlockWithFont("ГОЛОВНА ЧАСТИНА", 18),
            BlockWithFont("Опис турніру", 15),
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Title.Should().Be("Перша частина заголовка ГОЛОВНА ЧАСТИНА");
        result.Preamble.Should().Contain("Опис турніру");
    }

    [Fact]
    public void Parse_TitleWithTitleStyle_UsesStyleBasedDetection()
    {
        // Arrange - Blocks with Title style take precedence
        var blocks = new List<DocBlock>
        {
            BlockWithFont("Заголовок з Title стилем", 14, "Title"),
            BlockWithFont("Підзаголовок", 18), // Larger font but no Title style
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Title.Should().Be("Заголовок з Title стилем");
    }

    [Fact]
    public void Parse_TitleWithHeadingStyle_UsesStyleBasedDetection()
    {
        // Arrange - Blocks with Heading style
        var blocks = new List<DocBlock>
        {
            BlockWithFont("Заголовок", 16, "Heading1"),
            BlockWithFont("Підзаголовок", 14, "Heading2"),
            BlockWithFont("Редактор: Іван", 12),
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Title.Should().Be("Заголовок Підзаголовок");
    }

    [Fact]
    public void Parse_TitleLimitedToThreeBlocks()
    {
        // Arrange - Four blocks with same large font, only first 3 should be in title
        var blocks = new List<DocBlock>
        {
            BlockWithFont("Частина 1", 18),
            BlockWithFont("Частина 2", 18),
            BlockWithFont("Частина 3", 18),
            BlockWithFont("Частина 4", 18), // Should NOT be included
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Title.Should().Be("Частина 1 Частина 2 Частина 3");
        result.Preamble.Should().Contain("Частина 4");
    }

    [Fact]
    public void Parse_SingleBlockTitle_WorksWithFontSize()
    {
        // Arrange - Single title block followed by smaller font
        var blocks = new List<DocBlock>
        {
            BlockWithFont("Назва турніру", 18),
            BlockWithFont("Опис турніру", 12),
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Title.Should().Be("Назва турніру");
        result.Preamble.Should().Contain("Опис турніру");
    }

    #endregion

    #region Confidence Calculation

    [Fact]
    public void Parse_AllQuestionsHaveTextAndAnswer_HighConfidence()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання 1"),
            Block("Відповідь: Відповідь 1"),
            Block("2. Питання 2"),
            Block("Відповідь: Відповідь 2")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Confidence.Should().BeGreaterOrEqualTo(0.9);
    }

    [Fact]
    public void Parse_QuestionsMissingAnswers_LowerConfidence()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання 1"),
            Block("Відповідь: Відповідь 1"),
            Block("2. Питання 2 без відповіді")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Confidence.Should().BeLessThan(1.0);
    }

    [Fact]
    public void Parse_NoTours_ZeroConfidence()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Просто текст"),
            Block("Ще текст")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Confidence.Should().Be(0);
        result.Tours.Should().BeEmpty();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Parse_SequentialQuestionNumbers_ParsesAll()
    {
        // Arrange - questions must be numbered sequentially (1, 2, 3...)
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Перше питання"),
            Block("Відповідь: Перша"),
            Block("2. Друге питання"),
            Block("Відповідь: Друга"),
            Block("3. Третє питання"),
            Block("Відповідь: Третя")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions.Should().HaveCount(3);
        result.Tours[0].Questions[0].Number.Should().Be("1");
        result.Tours[0].Questions[1].Number.Should().Be("2");
        result.Tours[0].Questions[2].Number.Should().Be("3");
    }

    [Fact]
    public void Parse_NonSequentialQuestionNumber_IgnoredAsText()
    {
        // Arrange - "5." is not sequential after tour start, so it's treated as text
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("5. Це не буде питанням")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert - no questions parsed because 5 is not sequential
        result.Tours[0].Questions.Should().BeEmpty();
    }

    [Fact]
    public void Parse_EmptyBlocks_ReturnsEmptyResult()
    {
        // Arrange & Act
        var result = _parser.Parse([], []);

        // Assert
        result.Tours.Should().BeEmpty();
        result.Confidence.Should().Be(0);
    }

    [Fact]
    public void Parse_OnlyWhitespaceBlocks_ReturnsEmptyResult()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("   "),
            Block("\n\n"),
            Block("\t")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().BeEmpty();
    }

    [Fact]
    public void Parse_QuestionWithAllFields_ExtractsEverything()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("[Ведучому: читати повільно]"),
            Block("Роздатка: Таблиця"),
            Block("1. Яка столиця України?"),
            Block("Відповідь: Київ"),
            Block("Залік: столиця"),
            Block("Незалік: Харків"),
            Block("Коментар: Найбільше місто"),
            Block("Джерело: Вікіпедія"),
            Block("Автор: Тест Тестович")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.Text.Should().Contain("столиця України");
        q.Answer.Should().Be("Київ");
        q.AcceptedAnswers.Should().Contain("столиця");
        q.RejectedAnswers.Should().Contain("Харків");
        q.Comment.Should().Contain("Найбільше місто");
        q.Source.Should().Contain("Вікіпедія");
        q.Authors.Should().Contain("Тест Тестович");
    }


    #endregion

    #region Asset Association

    [Fact]
    public void Parse_AssetInCommentSection_AssignsToCommentAsset()
    {
        // Arrange - Asset is in a block with Comment label
        var commentAsset = new AssetReference
        {
            FileName = "comment_image.png",
            RelativeUrl = "/media/comment_image.png",
            ContentType = "image/png",
            SizeBytes = 1024
        };

        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання тесту"),
            Block("Відповідь: Тест"),
            Block("Коментар: Пояснення", [commentAsset])
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];
        question.CommentAssetFileName.Should().Be("comment_image.png");
        question.HandoutAssetFileName.Should().BeNull();
    }

    [Fact]
    public void Parse_AssetInHandoutSection_AssignsToHandoutAsset()
    {
        // Arrange - Asset is in a block with Handout marker
        var handoutAsset = new AssetReference
        {
            FileName = "handout_image.png",
            RelativeUrl = "/media/handout_image.png",
            ContentType = "image/png",
            SizeBytes = 2048
        };

        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Роздатка:", [handoutAsset]),
            Block("1. Питання тесту"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];
        question.HandoutAssetFileName.Should().Be("handout_image.png");
        question.CommentAssetFileName.Should().BeNull();
    }

    [Fact]
    public void Parse_AssetInQuestionTextSection_AssignsToHandoutAsset()
    {
        // Arrange - Asset is in a block with question text (before answer sections)
        var imageAsset = new AssetReference
        {
            FileName = "question_image.png",
            RelativeUrl = "/media/question_image.png",
            ContentType = "image/png",
            SizeBytes = 512
        };

        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання з картинкою", [imageAsset]),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert - Images in question text are treated as handouts
        var question = result.Tours[0].Questions[0];
        question.HandoutAssetFileName.Should().Be("question_image.png");
        question.CommentAssetFileName.Should().BeNull();
    }

    [Fact]
    public void Parse_MultiLineBlockWithCommentAsset_AssignsToCommentAsset()
    {
        // Regression test: Asset in a multi-line block should be associated
        // with the LAST section in the block, not the first
        var commentAsset = new AssetReference
        {
            FileName = "inline_comment.png",
            RelativeUrl = "/media/inline_comment.png",
            ContentType = "image/png",
            SizeBytes = 1024
        };

        // Simulate a block with multiple sections (soft line breaks in Word)
        var multiLineBlock = "1. Питання\nВідповідь: Тест\nКоментар: Пояснення з картинкою";

        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block(multiLineBlock, [commentAsset])
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert - The asset should be associated with Comment (last section), not QuestionText (first)
        var question = result.Tours[0].Questions[0];
        question.CommentAssetFileName.Should().Be("inline_comment.png");
        question.HandoutAssetFileName.Should().BeNull();
    }

    [Fact]
    public void Parse_BothHandoutAndCommentAssets_AssignsSeparately()
    {
        // Arrange
        var handoutAsset = new AssetReference
        {
            FileName = "handout.png",
            RelativeUrl = "/media/handout.png",
            ContentType = "image/png",
            SizeBytes = 1024
        };
        var commentAsset = new AssetReference
        {
            FileName = "comment.png",
            RelativeUrl = "/media/comment.png",
            ContentType = "image/png",
            SizeBytes = 2048
        };

        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Роздатка: Таблиця", [handoutAsset]),
            Block("1. Питання"),
            Block("Відповідь: Тест"),
            Block("Коментар: Пояснення", [commentAsset])
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];
        question.HandoutAssetFileName.Should().Be("handout.png");
        question.CommentAssetFileName.Should().Be("comment.png");
    }

    #endregion
    #region Helpers

    private static DocBlock Block(string text, int index = 0) => new()
    {
        Index = index,
        Text = text
    };

    private static DocBlock Block(string text, List<AssetReference> assets) => new()
    {
        Index = 0,
        Text = text,
        Assets = assets
    };

    /// <summary>
    /// Creates a DocBlock with font size in points.
    /// </summary>
    private static DocBlock BlockWithFont(string text, double fontSizePoints, string? styleId = null) => new()
    {
        Index = 0,
        Text = text,
        FontSizeHalfPoints = (int)(fontSizePoints * 2),
        StyleId = styleId
    };

    #endregion
}

