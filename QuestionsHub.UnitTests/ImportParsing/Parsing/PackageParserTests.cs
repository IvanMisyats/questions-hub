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
    [InlineData("ТУР 2.", "2")]
    [InlineData("Тур 3.", "3")]
    [InlineData("Тур 1:", "1")]
    [InlineData("ТУР 4:", "4")]
    [InlineData("Тур: 1", "1")]
    [InlineData("ТУР: 2", "2")]
    [InlineData("Tour: 3", "3")]
    [InlineData("  Тур: 1  ", "1")]
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

    /// <summary>
    /// Tests parsing of a tour block that contains tour header, preamble, and question in one block.
    /// Real case format:
    /// - Тур 3 -
    ///
    /// Редактор: Володимир Островський (Київ)
    ///
    /// Запитання 25. Для низки закладів у Новій Зеландії...
    /// </summary>
    [Fact]
    public void Parse_TourWithPreambleAndQuestionInSameBlock_ParsesQuestion()
    {
        // Arrange - single block with tour, editor, and question on different lines
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання 1. Перше питання"),
            Block("Відповідь: Перша"),
            Block("- Тур 2 - \n\nРедактор: Тестовий Редактор (Київ)\n\nЗапитання 2. Для низки закладів випустили спеціальний наклад."),
            Block("Відповідь: Happy Meal."),
            Block("Залік: Хеппі Міл"),
            Block("Коментар: Роальд Даль відомий своїми книжками."),
            Block("Джерело: https://example.com"),
            Block("Автор: Тестовий Автор (Київ)")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(2);
        result.Tours[1].Number.Should().Be("2");
        result.Tours[1].Questions.Should().HaveCount(1);

        var question = result.Tours[1].Questions[0];
        question.Number.Should().Be("2");
        question.Text.Should().Contain("Для низки закладів випустили спеціальний наклад");
        question.Answer.Should().Be("Happy Meal.");
        question.AcceptedAnswers.Should().Be("Хеппі Міл");
    }

    /// <summary>
    /// Tests parsing of a tour with global question numbering where the question is in the same block as tour header.
    /// In real packages, sometimes Tour 3 starts with question 25 (after 2 tours of 12 questions each).
    /// </summary>
    [Fact]
    public void Parse_TourWithGlobalNumberingAndQuestionInSameBlock_ParsesQuestion()
    {
        // Arrange - Tour 2 with global numbering (question 2 following question 1)
        // Using minimal example: 1 question per tour
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("Запитання 1. Питання 1"),
            Block("Відповідь: В1"),
            // Tour 2 with embedded question 2 in same block
            Block("- Тур 2 - \n\nРедактор: Володимир Островський (Київ)\n\nЗапитання 2. Для низки закладів у Новій Зеландії 2019 року випустили спеціальний наклад."),
            Block("Відповідь: Happy Meal."),
            Block("Залік: Хеппі Міл"),
            Block("Коментар: Роальд Даль відомий своїми дитячими книжками."),
            Block("Джерело: https://www.independent.co.uk/life-style/food-and-drink/mcdonalds-roald-dahl-free-book.html"),
            Block("Автор: Володимир Островський (Київ)")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(2);
        result.Tours[1].Number.Should().Be("2");
        result.Tours[1].Questions.Should().HaveCount(1);

        var question = result.Tours[1].Questions[0];
        question.Number.Should().Be("2");
        question.Text.Should().Contain("Для низки закладів у Новій Зеландії");
        question.Answer.Should().Be("Happy Meal.");
    }

    /// <summary>
    /// Demonstrates that when question numbering jumps unexpectedly (e.g., from 1 to 25),
    /// the parser will treat the question as preamble because it doesn't match expected numbering.
    /// This is the root cause of the user's issue with "Запитання 25" not being parsed.
    /// </summary>
    [Fact]
    public void Parse_QuestionWithUnexpectedNumber_TreatedAsPreamble()
    {
        // Arrange - Question 25 after Question 1 is invalid numbering
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("Запитання 1. Питання 1"),
            Block("Відповідь: В1"),
            Block("- Тур 2 - \n\nРедактор: Тестовий\n\nЗапитання 25. Це питання не буде розпізнано"),
            Block("Відповідь: Якась відповідь")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert - Question 25 was not parsed because numbering is not sequential
        // The text goes to tour preamble instead
        result.Tours.Should().HaveCount(2);
        result.Tours[1].Questions.Should().BeEmpty();
        result.Tours[1].Preamble.Should().Contain("Запитання 25");
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

    [Fact]
    public void Parse_NumberedSourceList_NotParsedAsQuestions()
    {
        // Arrange - Numbered source list should not be parsed as questions
        // Real case: "Джерело:\n1. https://...\n2. https://..."
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання 1. Перше питання"),
            Block("Відповідь: Перша"),
            Block("Джерело:"),
            Block("1.\thttps://paleontologyworld.com/dinosaurs"),
            Block("2.\thttps://en.wikipedia.org/wiki/The_Last_Flower"),
            Block("3.\thttps://www.youtube.com/watch?v=X1RrEAroZbw"),
            Block("Автор: Костянтин Ільїн (Одеса)."),
            Block("Запитання 2."),
            Block("Друге питання"),
            Block("Відповідь: Друга")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions.Should().HaveCount(2);

        var question1 = result.Tours[0].Questions[0];
        question1.Number.Should().Be("1");
        question1.Source.Should().Contain("paleontologyworld.com");
        question1.Source.Should().Contain("wikipedia.org");
        question1.Source.Should().Contain("youtube.com");
        question1.Authors.Should().Contain("Костянтин Ільїн (Одеса)");

        var question2 = result.Tours[0].Questions[1];
        question2.Number.Should().Be("2");
        question2.Text.Should().Contain("Друге питання");
    }

    [Fact]
    public void Parse_NamedFormatRequiresConsistency_NumberedNotAllowedAfterNamed()
    {
        // Arrange - If first question uses "Запитання N" format,
        // subsequent questions must also use that format
        // This prevents "2. " in sources from being parsed as questions
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання 1. Перше питання"),
            Block("Коментар: Коментар 1. Частина коментаря 2. Ще частина"),
            Block("Відповідь: одинадцята."),
            Block("Залік: 11."),
            Block("Джерело:"),
            Block("1. https://first-source.com"),
            Block("2. https://second-source.com"),
            Block("Автор: Тестовий автор"),
            Block("Запитання 2."),
            Block("Друге питання про щось"),
            Block("Відповідь: тринога.")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions.Should().HaveCount(2);

        var question1 = result.Tours[0].Questions[0];
        question1.Number.Should().Be("1");
        question1.Text.Should().Contain("Перше питання");
        question1.Comment.Should().Contain("Коментар 1. Частина коментаря 2.");
        question1.Source.Should().Contain("https://first-source.com");
        question1.Source.Should().Contain("https://second-source.com");

        var question2 = result.Tours[0].Questions[1];
        question2.Number.Should().Be("2");
        question2.Text.Should().Contain("Друге питання");
    }

    [Fact]
    public void Parse_NumberedFormatAllowsNumberedQuestions()
    {
        // Arrange - If using "N." format from the start, numbered sources
        // should not be parsed as questions (context-based validation)
        // The Author line marks the end of a question's metadata
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Перше питання"),
            Block("Відповідь: Перша"),
            Block("Джерело:"),
            Block("1. https://example.com"),
            Block("Автор: Тест"),
            Block("2. Друге питання"),
            Block("Відповідь: Друга")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions.Should().HaveCount(2);
        result.Tours[0].Questions[0].Source.Should().Contain("https://example.com");
        result.Tours[0].Questions[1].Number.Should().Be("2");
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

    [Fact]
    public void Parse_InlineZalikOnSameLineAsAnswer_ExtractsBoth()
    {
        // Arrange - "Залік:" appears on the same line as the answer
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: «Сяйво». Залік: The Shining.")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.Answer.Should().Be("«Сяйво».");
        q.AcceptedAnswers.Should().Be("The Shining.");
    }

    [Fact]
    public void Parse_MultipleInlineLabels_ExtractsAll()
    {
        // Arrange - Multiple labels on the same line
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: answer. Залік: accepted. Коментар: comment text")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.Answer.Should().Be("answer.");
        q.AcceptedAnswers.Should().Be("accepted.");
        q.Comment.Should().Be("comment text");
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
    [InlineData("Джерело(а): link1, link2", "link1, link2")]
    [InlineData("Джерел(а): посилання1, посилання2", "посилання1, посилання2")]
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
    [InlineData("Авторка: Галина Синєока (Львів)", new[] { "Галина Синєока (Львів)" })]
    [InlineData("Авторки: Марія, Олена", new[] { "Марія", "Олена" })]
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
    [InlineData("[Ведучим: читати чітко] Текст", "читати чітко")]
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

    /// <summary>
    /// Tests that host instructions on the same line as question number are correctly extracted.
    /// Real case: "6. [Ведучому: не оголошувати лапки в жодному з питань!]"
    /// </summary>
    [Fact]
    public void Parse_HostInstructionsOnQuestionLine_ExtractsInstructions()
    {
        // Arrange - host instructions immediately after question number
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. [Ведучому: не оголошувати лапки в жодному з питань!]"),
            Block("Бліц, після якого, можливо, ви захочете вбити ведучого."),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(1);
        result.Tours[0].Questions.Should().HaveCount(1);

        var question = result.Tours[0].Questions[0];
        question.Number.Should().Be("1");
        question.HostInstructions.Should().Contain("не оголошувати лапки в жодному з питань!");
        question.Text.Should().Contain("Бліц");
        question.Text.Should().NotContain("Ведучому");
        question.Text.Should().NotContain("[");
    }

    /// <summary>
    /// Tests host instructions followed by question text on the same line as question number.
    /// </summary>
    [Fact]
    public void Parse_HostInstructionsWithTextOnQuestionLine_ExtractsBoth()
    {
        // Arrange - host instructions with text after the closing bracket
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. [Ведучому: читати повільно] Перше питання про щось цікаве."),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(1);
        result.Tours[0].Questions.Should().HaveCount(1);

        var question = result.Tours[0].Questions[0];
        question.Number.Should().Be("1");
        question.HostInstructions.Should().Contain("читати повільно");
        question.Text.Should().Contain("Перше питання про щось цікаве");
        question.Text.Should().NotContain("Ведучому");
        question.Text.Should().NotContain("[");
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
    public void Parse_BracketedHandoutOnSeparateLine_QuestionTextOnNextLine()
    {
        // Arrange - Bug reproduction: handout is on its own line, question text on next line
        // The handout section should be closed after the closing bracket,
        // so the following text goes into questionText, not handoutText
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання 1."),
            Block("[Роздатковий матеріал: ass murderer]"),
            Block("Герої фільму «Іштар» ховаються від зловмисників у типовій для регіону крамниці. Що ми замінили словами «ass murderer»?"),
            Block("Відповідь: Rug Dealer")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];
        question.HandoutText.Should().Be("ass murderer");
        question.Text.Should().Be("Герої фільму «Іштар» ховаються від зловмисників у типовій для регіону крамниці. Що ми замінили словами «ass murderer»?");
        question.Answer.Should().Be("Rug Dealer");
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

    [Fact]
    public void Parse_MultilineBracketedHandout_ClosingBracketOnSameLineAsHandoutText()
    {
        // Arrange - Case where closing bracket is on the same line as handout text (not at start)
        // Format: [Роздатка:\nZoozeum] Текст питання
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. [Роздатка:\nZoozeum] Яке слово утворено?"),
            Block("Відповідь: зоопарк")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];
        question.HandoutText.Should().Be("Zoozeum");
        question.Text.Should().Be("Яке слово утворено?");
    }

    [Fact]
    public void Parse_MultilineBracketedHandout_ClosingBracketOnSameLineAsHandoutTextNoQuestionText()
    {
        // Arrange - Case where closing bracket is on the same line as handout text, no text after bracket
        // Format: [Роздатка:\nZoozeum]\nТекст питання
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. [Роздатка:\nZoozeum]\nЯке слово утворено?"),
            Block("Відповідь: зоопарк")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];
        question.HandoutText.Should().Be("Zoozeum");
        question.Text.Should().Be("Яке слово утворено?");
    }

    /// <summary>
    /// Edge case: Handout marker followed by brackets containing image asset on same line.
    /// Format:
    /// Запитання 18.
    /// Роздатковий матеріал:
    /// [ (imageAsset) ]
    /// Question text
    /// </summary>
    [Fact]
    public void Parse_HandoutMarkerWithBracketsContainingImage_ShouldExtractImageAndQuestionText()
    {
        // Arrange
        var imageAsset = new AssetReference
        {
            FileName = "image001.png",
            RelativeUrl = "/media/image001.png",
            ContentType = "image/png"
        };

        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("Запитання 1."),
            Block("Роздатковий матеріал:"),
            Block("[ ]", [imageAsset]),
            Block("У 1882 році ПЕРШИЙ, подорожуючи США, відвідав ДРУГОГО. На роздатковому матеріалі – епізод п'єси про цю зустріч. Назвіть ПЕРШОГО і ДРУГОГО."),
            Block("Відповідь: Оскар Вайлд і Волт Вітмен")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions.Should().HaveCount(1);
        var question = result.Tours[0].Questions[0];

        question.Number.Should().Be("1");
        question.HandoutAssetFileName.Should().Be("image001.png");
        question.Text.Should().Be("У 1882 році ПЕРШИЙ, подорожуючи США, відвідав ДРУГОГО. На роздатковому матеріалі - епізод п'єси про цю зустріч. Назвіть ПЕРШОГО і ДРУГОГО.");
        question.Text.Should().NotContain("[");
        question.Text.Should().NotContain("]");
        question.Answer.Should().Be("Оскар Вайлд і Волт Вітмен");
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
    public void Parse_PackageHeader_ExtractsFeminineEditorForm()
    {
        // Arrange - Test "Редакторка" (feminine form of editor)
        var blocks = new List<DocBlock>
        {
            Block("Назва пакету"),
            Block("Редакторка: Марія Коваленко"),
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Editors.Should().Contain("Марія Коваленко");
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
    #region Issue Reproduction Tests

    /// <summary>
    /// Issue 1: Image from handout goes to comment. Image from comment is lost.
    /// When a question has an image in the handout section (inside [Роздатковий матеріал: ... ])
    /// and another image in the comment section, the handout image is incorrectly associated
    /// with the comment, and the actual comment image is lost.
    ///
    /// Real case from Запитання 1:
    /// [Роздатковий матеріал:
    ///  &lt;image Asset&gt;
    /// ]
    /// Question text...
    /// Коментар: ...
    ///  &lt;image Asset&gt;
    /// </summary>
    [Fact]
    public void Parse_HandoutImageAndCommentImage_ShouldAssignCorrectly()
    {
        // Arrange
        var handoutAsset = new AssetReference
        {
            FileName = "handout_pringle.png",
            RelativeUrl = "/media/handout_pringle.png",
            ContentType = "image/png",
            SizeBytes = 1024
        };

        var commentAsset = new AssetReference
        {
            FileName = "comment_pringles.png",
            RelativeUrl = "/media/comment_pringles.png",
            ContentType = "image/png",
            SizeBytes = 2048
        };

        // Simulating the structure from the real document
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання 1."),
            Block("[Роздатковий матеріал:"),
            Block("", [handoutAsset]),  // Image inside handout brackets
            Block("]"),
            Block("Ця споруда отримала прізвисько через схожість з НИМИ. ЇХНЬОГО маскота порівнюють з маскотом \"Монополії\". Назвіть ЇХ."),
            Block("Коментар: через схожість форми лондонський велодром називають The Pringle. Маскотами є чоловіки з пишними вусами."),
            Block("Відповідь: Pringles."),
            Block("Залік: Прінглс; інші варіанти транслітерації."),
            Block("", [commentAsset]),  // Image in comment
            Block("Джерело:\n1.\thttps://www.theguardian.com/sport/2011/feb/22/pass-notes-the-pringle\n2.\thttps://www.monopolyland.com/monopoly-man-vs-pringles-man"),
            Block("Автор: Сергій Черкасов, Костянтин Ільїн (Одеса).")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions.Should().HaveCount(1);
        var question = result.Tours[0].Questions[0];

        // The handout image should be in HandoutAssetFileName
        question.HandoutAssetFileName.Should().Be("handout_pringle.png");

        // The comment image should be in CommentAssetFileName
        question.CommentAssetFileName.Should().Be("comment_pringles.png");
    }

    /// <summary>
    /// Issue 2: Answer was not parsed when using Latin "i" instead of Cyrillic "і".
    /// "Вiдповiдь: Карлсон." uses Latin 'i' (U+0069) instead of Cyrillic 'і' (U+0456).
    /// The answer was incorrectly appended to the comment.
    /// </summary>
    [Fact]
    public void Parse_AnswerWithLatinI_ShouldParseCorrectly()
    {
        // Arrange - Using Latin 'i' (common OCR/typing error)
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання 1."),
            Block("ІКС — це заміна. У тисяча дев'ятсот тридцятих роках відома американка змінила світ високої моди."),
            Block("Коментар: Раніше піджаки та спідниці продавалися разом."),
            Block("Вiдповiдь: Карлсон."),  // Latin 'i' instead of Cyrillic 'і'
            Block("Джерело: https://example.com"),
            Block("Автор: Тестовий автор")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions.Should().HaveCount(1);
        var question = result.Tours[0].Questions[0];

        // The answer should be parsed correctly despite Latin 'i'
        question.Answer.Should().Be("Карлсон.");

        // Comment should NOT contain the answer
        question.Comment.Should().NotContain("Карлсон");
        question.Comment.Should().Be("Раніше піджаки та спідниці продавалися разом.");
    }

    /// <summary>
    /// Issue 3: Question inside a block that starts with tour header goes to preamble.
    /// When a tour header and question appear in the same block (with line breaks between),
    /// the question should still be parsed, not treated as preamble.
    ///
    /// Real case:
    /// - Тур 3 -
    ///
    /// Редактор: Володимир Островський (Київ)
    ///
    /// Запитання 25. Для низки закладів у Новій Зеландії...
    /// </summary>
    [Fact]
    public void Parse_TourWithQuestionInSameBlock_ShouldParseQuestion()
    {
        // Arrange - Previous tours establish global numbering context
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("Запитання 1. Перше питання"),
            Block("Відповідь: Перша"),

            // Tour 3 with embedded question in the same block (using global numbering)
            // For simplicity, using question 2 after question 1
            Block("- Тур 2 -\n\nРедактор: Володимир Островський (Київ)\n\nЗапитання 2. Для низки закладів у Новій Зеландії 2019 року випустили спеціальний наклад скорочених версій книг."),
            Block("Відповідь: Happy Meal."),
            Block("Залік: Хеппі Міл"),
            Block("Коментар: Роальд Даль відомий своїми дитячими книжками."),
            Block("Джерело: https://www.independent.co.uk/life-style/food-and-drink/mcdonalds-roald-dahl-free-book.html"),
            Block("Автор: Володимир Островський (Київ)")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(2);
        result.Tours[1].Number.Should().Be("2");
        result.Tours[1].Questions.Should().HaveCount(1);

        var question = result.Tours[1].Questions[0];
        question.Number.Should().Be("2");
        question.Text.Should().Contain("Для низки закладів у Новій Зеландії");
        question.Answer.Should().Be("Happy Meal.");
    }

    /// <summary>
    /// Generic rule: Image asset appearing after "Коментар", "Вiдповiдь" or "Джерело"
    /// should be attached to the comment, not the handout.
    /// </summary>
    [Fact]
    public void Parse_ImageAfterAnswerLabel_ShouldGoToComment()
    {
        // Arrange
        var imageAsset = new AssetReference
        {
            FileName = "answer_illustration.png",
            RelativeUrl = "/media/answer_illustration.png",
            ContentType = "image/png",
            SizeBytes = 1024
        };

        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання 1. Яке місто зображено?"),
            Block("Відповідь: Київ"),
            Block("", [imageAsset]),  // Image after answer
            Block("Джерело: https://example.com"),
            Block("Автор: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];

        // Image after answer section should be in comment, not handout
        question.CommentAssetFileName.Should().Be("answer_illustration.png");
        question.HandoutAssetFileName.Should().BeNull();
    }

    /// <summary>
    /// Generic rule: Image asset appearing before any of "Коментар", "Вiдповiдь" or "Джерело"
    /// (but after question number) should belong to the question handout.
    /// </summary>
    [Fact]
    public void Parse_ImageBeforeAnswerLabel_ShouldGoToHandout()
    {
        // Arrange
        var imageAsset = new AssetReference
        {
            FileName = "question_illustration.png",
            RelativeUrl = "/media/question_illustration.png",
            ContentType = "image/png",
            SizeBytes = 1024
        };

        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання 1. Яке місто зображено?"),
            Block("", [imageAsset]),  // Image before answer
            Block("Відповідь: Київ"),
            Block("Джерело: https://example.com"),
            Block("Автор: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];

        // Image before answer section should be in handout
        question.HandoutAssetFileName.Should().Be("question_illustration.png");
        question.CommentAssetFileName.Should().BeNull();
    }

    /// <summary>
    /// Generic rule: No images should go to the next question.
    /// All pending assets should be flushed to the current question before starting a new one.
    /// </summary>
    [Fact]
    public void Parse_PendingAssetsBeforeNewQuestion_ShouldFlushToCurrentQuestion()
    {
        // Arrange
        var question1Asset = new AssetReference
        {
            FileName = "q1_image.png",
            RelativeUrl = "/media/q1_image.png",
            ContentType = "image/png",
            SizeBytes = 1024
        };

        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання 1. Перше питання"),
            Block("Відповідь: Перша"),
            Block("Коментар: Коментар до першого"),
            Block("", [question1Asset]),  // Image at end of question 1
            Block("Запитання 2. Друге питання"),  // New question starts
            Block("Відповідь: Друга")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions.Should().HaveCount(2);

        // The image should belong to question 1, not question 2
        var question1 = result.Tours[0].Questions[0];
        var question2 = result.Tours[0].Questions[1];

        question1.CommentAssetFileName.Should().Be("q1_image.png");
        question2.HandoutAssetFileName.Should().BeNull();
        question2.CommentAssetFileName.Should().BeNull();
    }

    /// <summary>
    /// Edge case: Handout text is surrounded with standalone [] brackets on separate lines.
    /// Format:
    /// Роздатковий матеріал:
    /// [
    /// Nissan IV
    /// ]
    /// Question text...
    /// </summary>
    [Fact]
    public void Parse_HandoutWithStandaloneBrackets_ShouldParseCorrectly()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання 1."),
            Block("Роздатковий матеріал:"),
            Block("["),
            Block("Nissan IV"),
            Block("]"),
            Block("Перед вами назва концепт-кару. Назвіть ІКС односкладовим словом."),
            Block("Відповідь: плющ"),
            Block("Коментар: IV - це слово ivy, тобто плющ."),
            Block("Джерело: https://example.com"),
            Block("Автор: Тестовий автор")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions.Should().HaveCount(1);
        var question = result.Tours[0].Questions[0];

        question.HandoutText.Should().Contain("Nissan IV");
        question.Text.Should().Contain("Перед вами назва концепт-кару");
        question.Text.Should().NotContain("[");
        question.Text.Should().NotContain("]");
        question.Answer.Should().Be("плющ");
        question.Comment.Should().Contain("IV - це слово ivy");
    }

    /// <summary>
    /// Edge case: Russian "Комментарий" used instead of Ukrainian "Коментар".
    /// </summary>
    [Theory]
    [InlineData("Комментарий: Це коментар російською", "Це коментар російською")]
    [InlineData("КОММЕНТАРИЙ: ВЕЛИКИМИ ЛІТЕРАМИ", "ВЕЛИКИМИ ЛІТЕРАМИ")]
    public void Parse_RussianCommentLabel_ShouldParseCorrectly(string line, string expectedComment)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання 1. Тестове питання"),
            Block("Відповідь: Тест"),
            Block(line)
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions[0].Comment.Should().Be(expectedComment);
    }

    /// <summary>
    /// Edge case: Russian "Источник" or "Источники" used instead of Ukrainian "Джерело".
    /// </summary>
    [Theory]
    [InlineData("Источник: https://example.com", "https://example.com")]
    [InlineData("Источники: site1, site2", "site1, site2")]
    [InlineData("ИСТОЧНИК: ВЕЛИКИМИ", "ВЕЛИКИМИ")]
    public void Parse_RussianSourceLabel_ShouldParseCorrectly(string line, string expectedSource)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання 1. Тестове питання"),
            Block("Відповідь: Тест"),
            Block(line)
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions[0].Source.Should().Be(expectedSource);
    }

    /// <summary>
    /// Edge case: Russian "Ответ" used instead of Ukrainian "Відповідь".
    /// </summary>
    [Theory]
    [InlineData("Ответ: Київ", "Київ")]
    [InlineData("ОТВЕТ: ВЕЛИКИМИ", "ВЕЛИКИМИ")]
    public void Parse_RussianAnswerLabel_ShouldParseCorrectly(string line, string expectedAnswer)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання 1. Тестове питання"),
            Block(line)
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions[0].Answer.Should().Be(expectedAnswer);
    }

    /// <summary>
    /// Full example with Russian labels mixed with Ukrainian content.
    /// </summary>
    [Fact]
    public void Parse_MixedRussianAndUkrainianLabels_ShouldParseCorrectly()
    {
        // Arrange - Based on real example from ParsingIssues.txt
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання 1."),
            Block("Роздатковий матеріал:"),
            Block("["),
            Block("Nissan IV"),
            Block("]"),
            Block("Перед вами назва концепт-кару, при виробництві кузову якого хотіли використовувати генномодифікований ІКС. Назвіть ІКС односкладовим словом."),
            Block("Ответ: плющ"),  // Russian answer
            Block("Комментарий: \"IV\" - це у даному випадку не \"4\", а слово \"ivy\", тобто плющ."),  // Russian comment
            Block("Источник: https://www.topspeed.com/cars/nissan/2010-nissan-iv-concept-ar100423.html"),  // Russian source
            Block("Автор: Олексій Чирков")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions.Should().HaveCount(1);
        var question = result.Tours[0].Questions[0];

        question.HandoutText.Should().Contain("Nissan IV");
        question.Text.Should().Contain("концепт-кару");
        question.Answer.Should().Be("плющ");
        question.Comment.Should().Contain("ivy");
        question.Source.Should().Contain("topspeed.com");
        question.Authors.Should().Contain("Олексій Чирков");
    }

    /// <summary>
    /// Edge case: Question number followed by handout marker on same line, then standalone brackets.
    /// Format: "1. Роздатковий матеріал:" followed by "[", content, "]" on separate lines.
    /// The question text should NOT include the handout marker or brackets.
    /// </summary>
    [Fact]
    public void Parse_NumberedQuestionWithInlineHandoutMarkerAndBrackets_ShouldParseCorrectly()
    {
        // Arrange - Based on real example from user (using question 1 for simpler test)
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Роздатковий матеріал:"),
            Block("["),
            Block("The Land Beyond The Forest"),
            Block("]"),
            Block("Перед вами назва книги, яка мала великий вплив на письменника кінця дев'ятнадцятого сторіччя. Назвіть цього письменника."),
            Block("Відповідь: Брем Стокер"),
            Block("Залік: Стокер"),
            Block("Коментар: земля за лісом - так буквально перекладається слово \"Трансильванія\"."),
            Block("Джерело: https://books.google.com/books/about/The_Land_Beyond_the_Forest.html"),
            Block("Автор: Олексій Чирков")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions.Should().HaveCount(1);
        var question = result.Tours[0].Questions[0];

        question.Number.Should().Be("1");
        question.HandoutText.Should().Be("The Land Beyond The Forest");
        question.Text.Should().Be("Перед вами назва книги, яка мала великий вплив на письменника кінця дев'ятнадцятого сторіччя. Назвіть цього письменника.");
        question.Text.Should().NotContain("Роздатковий матеріал");
        question.Text.Should().NotContain("[");
        question.Text.Should().NotContain("]");
        question.Answer.Should().Be("Брем Стокер");
        question.AcceptedAnswers.Should().Be("Стокер");
    }

    #endregion

    #region Warmup Question Label (Bold)

    /// <summary>
    /// When a bold "Розминочне питання" label appears before any tours,
    /// it should create an implicit warmup tour.
    /// </summary>
    [Fact]
    public void Parse_BoldWarmupQuestionLabel_CreatesWarmupTour()
    {
        // Arrange - Package with bold warmup question label before tours
        var blocks = new List<DocBlock>
        {
            Block("Кубок «Післязавтра»"),
            Block("Редакторська група: Микола Гнідь"),
            BoldBlock("Розминочне питання"),
            Block("0. При формуванні цього пакету розділи документу називалися коло перше?"),
            Block("Відповідь: Чемпіони"),
            Block("Тур 1"),
            Block("1. Звичайне питання першого туру"),
            Block("Відповідь: Відповідь 1")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(2);

        // First tour should be warmup
        var warmupTour = result.Tours[0];
        warmupTour.IsWarmup.Should().BeTrue();
        warmupTour.Number.Should().Be("0");
        warmupTour.Questions.Should().HaveCount(1);
        warmupTour.Questions[0].Number.Should().Be("0");
        warmupTour.Questions[0].Text.Should().Contain("При формуванні цього пакету");

        // Second tour should be regular
        var tour1 = result.Tours[1];
        tour1.IsWarmup.Should().BeFalse();
        tour1.Number.Should().Be("1");
        tour1.Questions.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("Розминочне питання")]
    [InlineData("Розминкове питання")]
    [InlineData("Розминка")]
    [InlineData("  Розминочне питання  ")]
    [InlineData("РОЗМИНОЧНЕ ПИТАННЯ")]
    public void Parse_BoldWarmupQuestionLabelVariants_CreatesWarmupTour(string labelText)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Package Title"),
            BoldBlock(labelText),
            Block("0. Warmup question text"),
            Block("Відповідь: Answer"),
            Block("Тур 1"),
            Block("1. Regular question"),
            Block("Відповідь: Answer 1")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(2);
        result.Tours[0].IsWarmup.Should().BeTrue();
        result.Tours[0].Questions.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_NonBoldWarmupQuestionLabel_DoesNotCreateWarmupTour()
    {
        // Arrange - Same text but NOT bold - should not be treated as warmup tour marker
        // When not bold, "Розминочне питання" is just regular text in the header,
        // and the parser will create a default non-warmup tour for the "0." question
        var blocks = new List<DocBlock>
        {
            Block("Package Title"),
            Block("Розминочне питання"),  // Not bold - just header text!
            Block("Тур 1"),
            Block("1. Regular question"),
            Block("Відповідь: Answer 1")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert - Only one tour (Тур 1), no implicit warmup because label wasn't bold
        result.Tours.Should().HaveCount(1);
        result.Tours[0].IsWarmup.Should().BeFalse();
        result.Tours[0].Number.Should().Be("1");
        result.Tours[0].Questions.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_BoldWarmupQuestionLabelAfterTour_DoesNotCreateWarmupTour()
    {
        // Arrange - Bold label but AFTER a tour already exists
        var blocks = new List<DocBlock>
        {
            Block("Package Title"),
            Block("Тур 1"),
            Block("1. First question"),
            Block("Відповідь: Answer 1"),
            BoldBlock("Розминочне питання"),  // Bold but after tour - should be ignored
            Block("Some text"),
            Block("Тур 2"),
            Block("2. Second question"),
            Block("Відповідь: Answer 2")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert - Two regular tours, no warmup
        result.Tours.Should().HaveCount(2);
        result.Tours[0].IsWarmup.Should().BeFalse();
        result.Tours[1].IsWarmup.Should().BeFalse();
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
    /// Creates a bold DocBlock.
    /// </summary>
    private static DocBlock BoldBlock(string text, int index = 0) => new()
    {
        Index = index,
        Text = text,
        IsBold = true
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

