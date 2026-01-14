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

    #endregion
}

