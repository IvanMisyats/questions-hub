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
    /// Tests Ukrainian ordinal tour names: "Перший тур", "Другий тур", etc.
    /// </summary>
    [Theory]
    [InlineData("Перший тур", "1")]
    [InlineData("Другий тур", "2")]
    [InlineData("Третій тур", "3")]
    [InlineData("Четвертий тур", "4")]
    [InlineData("П'ятий тур", "5")]
    [InlineData("Шостий тур", "6")]
    [InlineData("Сьомий тур", "7")]
    [InlineData("Восьмий тур", "8")]
    [InlineData("Дев'ятий тур", "9")]
    [InlineData("ПЕРШИЙ ТУР", "1")]  // Case insensitive
    [InlineData("  Другий тур  ", "2")]  // With whitespace
    public void Parse_OrdinalTourStart_DetectsTour(string tourLine, string expectedNumber)
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
    /// Tests reversed ordinal format: "Тур перший", "Тур другий", etc.
    /// </summary>
    [Theory]
    [InlineData("Тур перший", "1")]
    [InlineData("Тур другий", "2")]
    [InlineData("Тур третій", "3")]
    [InlineData("ТУР ЧЕТВЕРТИЙ", "4")]  // Case insensitive
    [InlineData("тур п'ятий", "5")]
    public void Parse_TourOrdinalStart_DetectsTour(string tourLine, string expectedNumber)
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
    /// Tests that ordinal tours work correctly with multiple tours in a package.
    /// </summary>
    [Fact]
    public void Parse_MultipleOrdinalTours_ParsesAllTours()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Перший тур"),
            Block("1. Питання туру 1"),
            Block("Відповідь: В1"),
            Block("Другий тур"),
            Block("1. Питання туру 2"),
            Block("Відповідь: В2"),
            Block("Третій тур"),
            Block("1. Питання туру 3"),
            Block("Відповідь: В3")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(3);
        result.Tours[0].Number.Should().Be("1");
        result.Tours[0].Questions.Should().HaveCount(1);
        result.Tours[1].Number.Should().Be("2");
        result.Tours[1].Questions.Should().HaveCount(1);
        result.Tours[2].Number.Should().Be("3");
        result.Tours[2].Questions.Should().HaveCount(1);
    }

    /// <summary>
    /// Tests tour start with inline preamble/name: "Тур 1. Фізики", "Тур 2: Лірики".
    /// The text after the number should become the tour preamble.
    /// </summary>
    [Theory]
    [InlineData("Тур 1. Фізики", "1", "Фізики")]
    [InlineData("Тур 2. Лірики", "2", "Лірики")]
    [InlineData("Тур 3: Фізлірики", "3", "Фізлірики")]
    [InlineData("ТУР 1. НАЗВА ТУРУ", "1", "НАЗВА ТУРУ")]
    [InlineData("Tour 2. Physics", "2", "Physics")]
    public void Parse_TourStartWithPreamble_DetectsTourAndPreamble(string tourLine, string expectedNumber, string expectedPreamble)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block(tourLine),
            Block("Запитання №1. Текст питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(1);
        result.Tours[0].Number.Should().Be(expectedNumber);
        result.Tours[0].Preamble.Should().Be(expectedPreamble);
    }

    /// <summary>
    /// Tests multiple tours with inline preambles - a real-world scenario.
    /// </summary>
    [Fact]
    public void Parse_MultipleToursWithPreamble_ParsesAllToursAndPreambles()
    {
        // Arrange - real scenario with named tours
        var blocks = new List<DocBlock>
        {
            Block("Тур 1. Фізики"),
            Block("Запитання №1. Питання туру 1"),
            Block("Відповідь: В1"),
            Block("Тур 2. Лірики"),
            Block("Запитання №1. Питання туру 2"),
            Block("Відповідь: В2"),
            Block("Тур 3. Фізлірики"),
            Block("Запитання №1. Питання туру 3"),
            Block("Відповідь: В3")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(3);
        result.Tours[0].Number.Should().Be("1");
        result.Tours[0].Preamble.Should().Be("Фізики");
        result.Tours[0].Questions.Should().HaveCount(1);
        result.Tours[1].Number.Should().Be("2");
        result.Tours[1].Preamble.Should().Be("Лірики");
        result.Tours[1].Questions.Should().HaveCount(1);
        result.Tours[2].Number.Should().Be("3");
        result.Tours[2].Preamble.Should().Be("Фізлірики");
        result.Tours[2].Questions.Should().HaveCount(1);
    }

    /// <summary>
    /// Tests that tours without inline preamble (just "Тур N") don't have the inline preamble,
    /// but still collect subsequent tour header text.
    /// </summary>
    [Fact]
    public void Parse_TourWithoutInlinePreamble_CollectsSubsequentTextAsPreamble()
    {
        // Arrange - use text that won't match editor pattern
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("Тема туру: Історія"),
            Block("1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(1);
        result.Tours[0].Number.Should().Be("1");
        // The preamble should contain the theme line (collected as tour header text)
        result.Tours[0].Preamble.Should().Contain("Тема туру: Історія");
    }

    /// <summary>
    /// Tests that inline preamble from tour header is different from preamble collected later.
    /// </summary>
    [Fact]
    public void Parse_TourWithInlinePreambleAndSubsequentText_CombinesBoth()
    {
        // Arrange - use text that won't match editor pattern
        var blocks = new List<DocBlock>
        {
            Block("Тур 1. Фізики"),
            Block("Тема: Природничі науки"),
            Block("Запитання №1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(1);
        result.Tours[0].Number.Should().Be("1");
        // The preamble should contain both the inline text and subsequent header text
        result.Tours[0].Preamble.Should().Contain("Фізики");
        result.Tours[0].Preamble.Should().Contain("Тема: Природничі науки");
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

    /// <summary>
    /// Verifies that each tour can use its own question format (Named vs Numbered).
    /// Tour 1 uses "Запитання N" format, Tour 2 uses "N." format - both should parse correctly.
    /// This is a real-world scenario from the Hayfive-7 package.
    /// </summary>
    [Fact]
    public void Parse_MixedFormatAcrossTours_NamedThenNumbered_ParsesBothTours()
    {
        // Arrange - Tour 1 uses "Запитання N", Tour 2 uses "N."
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("Редактор туру: Олексій Файнбурд"),
            Block("Запитання 1"),
            Block("Текст першого питання туру 1"),
            Block("Відповідь: Відповідь 1"),
            Block("Запитання 2"),
            Block("Текст другого питання туру 1"),
            Block("Відповідь: Відповідь 2"),
            Block("Тур 2"),
            Block("Редактор туру: Інший Редактор"),
            Block("1. Текст першого питання туру 2"),
            Block("Відповідь: Відповідь 1 туру 2"),
            Block("2. Текст другого питання туру 2"),
            Block("Відповідь: Відповідь 2 туру 2")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(2);

        // Tour 1 should have 2 questions using Named format
        result.Tours[0].Questions.Should().HaveCount(2);
        result.Tours[0].Questions[0].Number.Should().Be("1");
        result.Tours[0].Questions[0].Text.Should().Contain("першого питання туру 1");
        result.Tours[0].Questions[1].Number.Should().Be("2");

        // Tour 2 should have 2 questions using Numbered format
        result.Tours[1].Questions.Should().HaveCount(2);
        result.Tours[1].Questions[0].Number.Should().Be("1");
        result.Tours[1].Questions[0].Text.Should().Contain("першого питання туру 2");
        result.Tours[1].Questions[1].Number.Should().Be("2");
    }

    /// <summary>
    /// Verifies the reverse case: Tour 1 uses "N." format, Tour 2 uses "Запитання N" format.
    /// </summary>
    [Fact]
    public void Parse_MixedFormatAcrossTours_NumberedThenNamed_ParsesBothTours()
    {
        // Arrange - Tour 1 uses "N.", Tour 2 uses "Запитання N"
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Текст першого питання туру 1"),
            Block("Відповідь: Відповідь 1"),
            Block("2. Текст другого питання туру 1"),
            Block("Відповідь: Відповідь 2"),
            Block("Тур 2"),
            Block("Запитання 1. Текст першого питання туру 2"),
            Block("Відповідь: Відповідь 1 туру 2"),
            Block("Запитання 2. Текст другого питання туру 2"),
            Block("Відповідь: Відповідь 2 туру 2")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(2);

        // Tour 1 should have 2 questions using Numbered format
        result.Tours[0].Questions.Should().HaveCount(2);
        result.Tours[0].Questions[0].Number.Should().Be("1");
        result.Tours[0].Questions[1].Number.Should().Be("2");

        // Tour 2 should have 2 questions using Named format
        result.Tours[1].Questions.Should().HaveCount(2);
        result.Tours[1].Questions[0].Number.Should().Be("1");
        result.Tours[1].Questions[0].Text.Should().Contain("першого питання туру 2");
        result.Tours[1].Questions[1].Number.Should().Be("2");
    }

    /// <summary>
    /// Verifies that three tours with mixed formats all parse correctly.
    /// This matches the real-world scenario from Hayfive-7: Tour 1 "Запитання N", Tours 2-3 "N."
    /// </summary>
    [Fact]
    public void Parse_ThreeToursWithMixedFormats_ParsesAllTours()
    {
        // Arrange - Tour 1 uses "Запитання N", Tours 2 and 3 use "N."
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("Запитання 1. Питання туру 1"),
            Block("Відповідь: В1"),
            Block("Тур 2"),
            Block("1. Питання туру 2"),
            Block("Відповідь: В2"),
            Block("Тур 3"),
            Block("1. Питання туру 3"),
            Block("Відповідь: В3")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(3);
        result.Tours[0].Questions.Should().HaveCount(1);
        result.Tours[1].Questions.Should().HaveCount(1);
        result.Tours[2].Questions.Should().HaveCount(1);

        result.Tours[0].Questions[0].Text.Should().Contain("Питання туру 1");
        result.Tours[1].Questions[0].Text.Should().Contain("Питання туру 2");
        result.Tours[2].Questions[0].Text.Should().Contain("Питання туру 3");
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

    #region Block Detection

    [Theory]
    [InlineData("Блок 1", "1")]
    [InlineData("Блок 2", "2")]
    [InlineData("  Блок 3  ", "3")]
    [InlineData("Блок 1.", "1")]
    [InlineData("Блок 2:", "2")]
    public void Parse_BlockStart_DetectsBlock(string blockLine, string expectedName)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block(blockLine),
            Block("Редактор - Тест Тестович"),
            Block("Запитання 1. Питання тесту"),
            Block("Відповідь: Відповідь тесту")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(1);
        result.Tours[0].Blocks.Should().HaveCount(1);
        result.Tours[0].Blocks[0].Name.Should().Be(expectedName);
    }

    [Fact]
    public void Parse_BlockWithoutNumber_DetectsUnnamedBlock()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("Блок"),
            Block("Редактор: Тест Тестович"),
            Block("Запитання 1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Blocks.Should().HaveCount(1);
        result.Tours[0].Blocks[0].Name.Should().BeNull();
    }

    [Fact]
    public void Parse_MultipleBlocks_ParsesAll()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("Блок 1"),
            Block("Редактор - Анатолій Матвєєвський"),
            Block("Запитання 1. Питання 1"),
            Block("Відповідь: Відповідь 1"),
            Block("Блок 2"),
            Block("Редакторка - Ірина Кречетова"),
            Block("Запитання 2. Питання 2"),
            Block("Відповідь: Відповідь 2")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(1);
        result.Tours[0].Blocks.Should().HaveCount(2);
        result.Tours[0].Blocks[0].Name.Should().Be("1");
        result.Tours[0].Blocks[0].Editors.Should().Contain("Анатолій Матвєєвський");
        result.Tours[0].Blocks[1].Name.Should().Be("2");
        result.Tours[0].Blocks[1].Editors.Should().Contain("Ірина Кречетова");
    }

    [Fact]
    public void Parse_BlockWithQuestions_QuestionsGoToBlock()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("Блок 1"),
            Block("Запитання 1. Питання блоку"),
            Block("Відповідь: Відповідь 1")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions.Should().BeEmpty(); // Questions should be in block, not directly in tour
        result.Tours[0].Blocks.Should().HaveCount(1);
        result.Tours[0].Blocks[0].Questions.Should().HaveCount(1);
        result.Tours[0].Blocks[0].Questions[0].Text.Should().Contain("Питання блоку");
    }

    [Theory]
    [InlineData("Редактор - Анатолій Матвєєвський", "Анатолій Матвєєвський")]
    [InlineData("Редакторка - Ірина Кречетова", "Ірина Кречетова")]
    [InlineData("Редактор блоку: Тест Тестович", "Тест Тестович")]
    [InlineData("Редакторка блоку: Галина Синєока", "Галина Синєока")]
    [InlineData("Редактори блоку: Іван, Марія", "Іван")]
    public void Parse_BlockEditorLabels_ExtractsEditors(string editorLine, string expectedEditor)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("Блок 1"),
            Block(editorLine),
            Block("Запитання 1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Blocks[0].Editors.Should().Contain(expectedEditor);
    }

    [Fact]
    public void Parse_RealWorldBlockExample_ParsesCorrectly()
    {
        // Based on scratch_6.txt sample, adjusted for sequential numbering
        var blocks = new List<DocBlock>
        {
            Block("Асинхронний турнір \"Забобони бонобо\""),
            Block("2024-10-30"),
            Block("Тур 1"),
            Block("Блок 1"),
            Block("Редактор - Анатолій Матвєєвський"),
            Block("Запитання 1."),
            Block("[Роздатковий матеріал: ]"),
            Block("[Ведучому: виділити голосом авТОР, ісТОРІя, повТОРюється, ТОРованим]"),
            Block("Розминкове запитання, відповідь на яке не треба здавати."),
            Block("Відповідь: консервація"),
            Block("Залік: банки з консервацією, закрутки, синонімічні"),
            Block("Блок 2"),
            Block("Редакторка - Ірина Кречетова"),
            Block("Запитання 2."),
            Block("В одному селищі у Мексиці існує щорічний ритуал."),
            Block("Відповідь: перемивають кістки")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Title.Should().Contain("Забобони бонобо");
        result.Tours.Should().HaveCount(1);
        result.Tours[0].Blocks.Should().HaveCount(2);

        var block1 = result.Tours[0].Blocks[0];
        block1.Name.Should().Be("1");
        block1.Editors.Should().Contain("Анатолій Матвєєвський");
        block1.Questions.Should().HaveCount(1);
        block1.Questions[0].Number.Should().Be("1");

        var block2 = result.Tours[0].Blocks[1];
        block2.Name.Should().Be("2");
        block2.Editors.Should().Contain("Ірина Кречетова");
        block2.Questions.Should().HaveCount(1);
        block2.Questions[0].Number.Should().Be("2");
        block2.Questions[0].Answer.Should().Be("перемивають кістки");
    }

    [Fact]
    public void Parse_NewTourResetsBlock()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("Блок 1"),
            Block("Запитання 1. Питання туру 1"),
            Block("Відповідь: Відповідь 1"),
            Block("Тур 2"),
            Block("Запитання 1. Питання туру 2 без блоку"),
            Block("Відповідь: Відповідь 2")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(2);
        result.Tours[0].Blocks.Should().HaveCount(1);
        result.Tours[0].Blocks[0].Questions.Should().HaveCount(1);
        result.Tours[1].Blocks.Should().BeEmpty();
        result.Tours[1].Questions.Should().HaveCount(1); // Question goes directly to tour, not a block
    }

    [Fact]
    public void Parse_BlockOutsideTour_IsIgnored()
    {
        // Arrange - Block before any tour should not create a block
        var blocks = new List<DocBlock>
        {
            Block("Заголовок пакету"),
            Block("Блок 1"), // This should be treated as header text, not a block
            Block("Тур 1"),
            Block("Запитання 1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(1);
        result.Tours[0].Blocks.Should().BeEmpty();
        result.Preamble.Should().Contain("Блок 1");
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
    [InlineData("Запитання №1")]
    [InlineData("Питання №1.")]
    [InlineData("Запитання №1:")]
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

    [Theory]
    [InlineData("Запитання №1. Текст питання", "1", "Текст питання")]
    [InlineData("Питання №1: Інший текст", "1", "Інший текст")]
    [InlineData("Запитання №1. У вже згаданій серії", "1", "У вже згаданій серії")]
    public void Parse_NamedQuestionStartWithNumberSign_ExtractsNumberAndText(string line, string expectedNumber, string expectedText)
    {
        // Arrange - "Запитання №N" format with text after number
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
    public void Parse_QuestionWithNumberSign_ParsesFullStructure()
    {
        // Arrange - format "Запитання №N" (with № symbol before number)
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання №1. У вже згаданій серії «Надприродного» ВІН парадоксально нападає на героя."),
            Block("Відповідь: Махатма Ґанді"),
            Block("Залік: за прізвищем"),
            Block("Коментар: перевертень з епізоду із попереднього запитання перетворився на вегетаріанця."),
            Block("Джерела: Надприродне сезон 5, серія 5"),
            Block("Автор: Тест Тестович")
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
    public void Parse_MultipleQuestionsWithNumberSign_ParsesAll()
    {
        // Arrange - multiple questions using "Запитання №N" format
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("Запитання №1"),
            Block("Перше питання тексту"),
            Block("Відповідь: Перша відповідь"),
            Block("Запитання №2"),
            Block("Друге питання тексту"),
            Block("Відповідь: Друга відповідь"),
            Block("Питання №3"),
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
    [InlineData("Залік: варіант1 [частина]; варіант2", "варіант1 [частина]; варіант2")]
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
    public void Parse_MultipleInlineLabels_ExtractsZalikOnly()
    {
        // Arrange - Multiple labels on the same line
        // Only Залік/Незалік can appear inline; Коментар must be at line start
        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: answer. Залік: accepted. Незалік: rejected."),
            Block("Коментар: comment text")  // Comment on separate line
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.Answer.Should().Be("answer.");
        q.AcceptedAnswers.Should().Be("accepted.");
        q.RejectedAnswers.Should().Be("rejected.");
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
    [InlineData("Автор(и): Петро Сидоренко", new[] { "Петро Сидоренко" })]
    [InlineData("Автор(и): Іван, Марія, Петро", new[] { "Іван", "Марія", "Петро" })]
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

    /// <summary>
    /// Tests author range labels that assign authors to a range of questions in the package header.
    /// </summary>
    [Fact]
    public void Parse_AuthorRangeLabel_AssignsAuthorsToQuestionRange()
    {
        // Arrange - Author range header before questions
        var blocks = new List<DocBlock>
        {
            Block("Автор запитань 1-2: Іван Петренко"),
            Block("ТУР 1"),
            Block("1. Перше питання"),
            Block("Відповідь: Перша"),
            Block("2. Друге питання"),
            Block("Відповідь: Друга")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions.Should().HaveCount(2);
        result.Tours[0].Questions[0].Authors.Should().Contain("Іван Петренко");
        result.Tours[0].Questions[1].Authors.Should().Contain("Іван Петренко");
    }

    /// <summary>
    /// Tests that "Автори запитань" label correctly parses comma-separated list of authors.
    /// </summary>
    [Fact]
    public void Parse_AuthoriZapytanLabel_AssignsMultipleAuthorsToQuestionRange()
    {
        // Arrange - Multiple authors for a range of questions
        var blocks = new List<DocBlock>
        {
            Block("Автори запитань 1-2: Іван Петренко, Марія Коваленко, Петро Сидоренко"),
            Block("ТУР 1"),
            Block("1. Перше питання"),
            Block("Відповідь: Перша"),
            Block("2. Друге питання"),
            Block("Відповідь: Друга")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions.Should().HaveCount(2);
        result.Tours[0].Questions[0].Authors.Should().BeEquivalentTo(
            ["Іван Петренко", "Марія Коваленко", "Петро Сидоренко"]);
        result.Tours[0].Questions[1].Authors.Should().BeEquivalentTo(
            ["Іван Петренко", "Марія Коваленко", "Петро Сидоренко"]);
    }

    /// <summary>
    /// Tests various author range label formats.
    /// </summary>
    [Theory]
    [InlineData("Автор запитань 1-3: Іван Петренко")]
    [InlineData("Автора запитань 1-3: Іван Петренко")]
    [InlineData("Авторка запитань 1-3: Іван Петренко")]
    [InlineData("Автори запитань 1-3: Іван Петренко")]
    [InlineData("Авторки запитань 1-3: Іван Петренко")]
    public void Parse_AuthorRangeLabelVariants_AssignsAuthors(string authorRangeLine)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block(authorRangeLine),
            Block("ТУР 1"),
            Block("1. Питання 1"),
            Block("Відповідь: Відповідь 1"),
            Block("2. Питання 2"),
            Block("Відповідь: Відповідь 2"),
            Block("3. Питання 3"),
            Block("Відповідь: Відповідь 3")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions.Should().HaveCount(3);
        foreach (var question in result.Tours[0].Questions)
        {
            question.Authors.Should().Contain("Іван Петренко");
        }
    }

    #endregion

    #region Host Instructions

    [Theory]
    [InlineData("[Ведучому: читати повільно] Текст", "читати повільно")]
    [InlineData("[Ведучому-(ій): наголос на слові] Текст", "наголос на слові")]
    [InlineData("[Вказівка ведучому: пауза 5 секунд] Текст", "пауза 5 секунд")]
    [InlineData("[Ведучим: читати чітко] Текст", "читати чітко")]
    [InlineData("[Ведучій: наголос на останньому слові] Текст", "наголос на останньому слові")]
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
    [InlineData("Роздатковий матеріял: Текст для команд", "Текст для команд")]
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
    [InlineData("[Роздатковий матеріял: Діаграма] Що зображено?", "Діаграма", "Що зображено?")]
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
        var question = result.Tours[0].Questions[0];

        question.HandoutAssetFileName.Should().Be("image001.png");
        // Apostrophe is normalized to Ukrainian apostrophe (U+02BC)
        question.Text.Should().StartWith("У 1882 році ПЕРШИЙ");
        question.Text.Should().Contain("\u02BC"); // Ukrainian apostrophe
        question.Text.Should().NotContain("[");
        question.Text.Should().NotContain("]");
        question.Answer.Should().Be("Оскар Вайлд і Волт Вітмен");
    }

    [Fact]
    public void Parse_HandoutLabelThenBracketedMultilineContent_SeparatesHandoutAndQuestionText()
    {
        // Arrange - Bug reproduction: "Роздатковий матеріал:" on its own line,
        // then multiline content in [brackets], then question text after closing bracket.
        // Only the bracketed text should be handout; the rest is question text.
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("Запитання 1"),
            Block("Роздатковий матеріал:"),
            Block("[Розчулена музикою спілкування, вона всіма фібрами відчула кілька фальшивих нот.\n<...>\nЩоранку мама давала мені двадцять копійок.]"),
            Block("Пацьорки довгий час потрапляли до українок з Венеції. Заповніть пропуск двома короткими словами."),
            Block("Відповідь: Гра в.")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];
        question.HandoutText.Should().Contain("Розчулена музикою");
        question.HandoutText.Should().Contain("двадцять копійок");
        question.HandoutText.Should().NotContain("[");
        question.HandoutText.Should().NotContain("]");
        question.Text.Should().Contain("Пацьорки довгий час");
        question.Text.Should().Contain("Заповніть пропуск");
        question.Text.Should().NotContain("Розчулена");
        question.Answer.Should().Be("Гра в.");
    }

    [Fact]
    public void Parse_HandoutLabelThenSingleLineBracketedContent_SeparatesHandoutAndQuestionText()
    {
        // Arrange - "Роздатковий матеріал:" then single-line [content] then question text
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("Запитання 1"),
            Block("Роздатковий матеріал:"),
            Block("[Текст роздатки на одному рядку]"),
            Block("Текст запитання після роздатки."),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];
        question.HandoutText.Should().Be("Текст роздатки на одному рядку");
        question.Text.Should().Be("Текст запитання після роздатки.");
        question.Answer.Should().Be("Тест");
    }

    [Fact]
    public void Parse_HandoutLabelThenBracketedMultilineInSameBlock_SeparatesCorrectly()
    {
        // Arrange - All handout content in single block with newlines, closing ] at end of line
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Питання номер один"),
            Block("Роздатковий матеріал:\n[Перший рядок роздатки.\nДругий рядок роздатки.]"),
            Block("Текст запитання."),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];
        question.HandoutText.Should().Contain("Перший рядок роздатки");
        question.HandoutText.Should().Contain("Другий рядок роздатки");
        question.HandoutText.Should().NotContain("[");
        question.HandoutText.Should().NotContain("]");
        question.Text.Should().Contain("Текст запитання.");
    }

    [Fact]
    public void Parse_HandoutLabelThenBracketedMultilineClosingOnOwnLine_SeparatesCorrectly()
    {
        // Arrange - Closing bracket on its own line after content
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("Запитання 1"),
            Block("Роздатковий матеріал:"),
            Block("[Рядок роздатки один.\nРядок роздатки два."),
            Block("]"),
            Block("Текст запитання після роздатки."),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var question = result.Tours[0].Questions[0];
        question.HandoutText.Should().Contain("Рядок роздатки один");
        question.HandoutText.Should().Contain("Рядок роздатки два");
        question.Text.Should().Be("Текст запитання після роздатки.");
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
    public void Parse_TitleWithSimilarFontSizes_IncludesAllSimilarBlocks()
    {
        // Arrange - pt 15, then pt 18, then pt 15
        // All blocks are within 70% threshold of first block, so all should be included
        var blocks = new List<DocBlock>
        {
            BlockWithFont("Перша частина заголовка", 15),
            BlockWithFont("ГОЛОВНА ЧАСТИНА", 18),
            BlockWithFont("Підзаголовок турніру", 15),
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert - all 3 blocks should be included as they have similar font sizes
        result.Title.Should().Be("Перша частина заголовка ГОЛОВНА ЧАСТИНА Підзаголовок турніру");
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

    [Fact]
    public void Parse_TitleWithSignificantFontSizeDrop_StopsAtSmallerBlock()
    {
        // Arrange - Large font (26pt) followed by much smaller font (14pt)
        // 14/26 = 53.8% which is below 70% threshold
        var blocks = new List<DocBlock>
        {
            BlockWithFont("Дзеркало Чемпіонату Грузії 2023 - 1", 26, "Title"),
            BlockWithFont("Липень - серпень 2023", 14, "Title"),
            BlockWithFont("[Шановні ведучі, у квадратних дужках вказівки]", 14, "Title"),
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert - Only first block should be title due to significant font size drop
        result.Title.Should().Be("Дзеркало Чемпіонату Грузії 2023 - 1");
        result.Preamble.Should().Contain("Липень - серпень 2023");
    }

    [Fact]
    public void Parse_TitleStopsAtPreambleMarker()
    {
        // Arrange - Title followed by preamble marker starting with [
        var blocks = new List<DocBlock>
        {
            BlockWithFont("Назва турніру", 18, "Title"),
            BlockWithFont("Підзаголовок", 18, "Title"),
            BlockWithFont("[Шановні ведучі, це інструкції для вас]", 18, "Title"),
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert - Title should stop before preamble marker
        result.Title.Should().Be("Назва турніру Підзаголовок");
        result.Preamble.Should().Contain("[Шановні ведучі, це інструкції для вас]");
    }

    [Fact]
    public void Parse_TitleWithOnlyPreambleMarker_ReturnsEmptyTitle()
    {
        // Arrange - First block is a preamble marker
        var blocks = new List<DocBlock>
        {
            BlockWithFont("[Шановні ведучі, інструкції]", 18, "Title"),
            BlockWithFont("Назва турніру", 18, "Title"),
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert - Title should be empty since first block is preamble marker
        result.Title.Should().BeEmpty();
        result.Preamble.Should().Contain("[Шановні ведучі, інструкції]");
    }

    [Fact]
    public void Parse_FontSizeTakesPriorityOverStyle()
    {
        // Arrange - All blocks have Title style, but font size drops significantly
        // First block 26pt, second block 14pt (54% < 70%)
        var blocks = new List<DocBlock>
        {
            BlockWithFont("Головний заголовок", 26, "Title"),
            BlockWithFont("Другорядний текст", 14, "Title"),
            Block("ТУР 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert - Font size drop should exclude second block despite Title style
        result.Title.Should().Be("Головний заголовок");
        result.Preamble.Should().Contain("Другорядний текст");
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

    /// <summary>
    /// Regression test: Block contains Q1 complete content + "2." (next question) at the end.
    /// The asset in block belongs to Q1's comment, not Q2's handout.
    /// </summary>
    [Fact]
    public void Parse_Q27Q28Scenario_AssetShouldGoToQ27Comment()
    {
        var q1CommentAsset = new AssetReference
        {
            FileName = "q1_comment.png",
            RelativeUrl = "/media/q1_comment.png",
            ContentType = "image/png",
            SizeBytes = 1024
        };

        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання 1"),
            Block("Журналіст повідомляє...\n\n" +
                  "Коментар: він — це дах.\n" +
                  "Відповідь: кабріолет.\n" +
                  "Джерело: https://example.com\n" +
                  "Автор: Тест.\n\n" +
                  "2.", [q1CommentAsset])
        };

        var result = _parser.Parse(blocks, []);

        result.Tours[0].Questions.Should().HaveCount(2);

        var q1 = result.Tours[0].Questions[0];
        var q2 = result.Tours[0].Questions[1];

        q1.CommentAssetFileName.Should().Be("q1_comment.png",
            "asset in block with Q1 content should be Q1's comment asset");
        q2.HandoutAssetFileName.Should().BeNull(
            "Q2 should not have a handout asset from Q1's block");
        q2.CommentAssetFileName.Should().BeNull();
    }

    /// <summary>
    /// Test for backward-merge of asset-only blocks.
    /// When an empty block contains only assets, those assets should be merged
    /// into the previous textual block (fixing DOCX anchoring issues).
    /// </summary>
    [Fact]
    public void Parse_AssetOnlyBlockBetweenQuestions_ShouldMergeBackward()
    {
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
            Block("1. Питання 1\n\nКоментар: Пояснення\nВідповідь: Тест"),
            Block("", [commentAsset]),
            Block("2. Питання 2\nВідповідь: Тест2")
        };

        var result = _parser.Parse(blocks, []);

        result.Tours[0].Questions.Should().HaveCount(2);

        var q1 = result.Tours[0].Questions[0];
        var q2 = result.Tours[0].Questions[1];

        q1.CommentAssetFileName.Should().Be("comment_image.png",
            "asset from empty block should merge backward");
        q2.HandoutAssetFileName.Should().BeNull();
        q2.CommentAssetFileName.Should().BeNull();
    }

    /// <summary>
    /// Test that duplicate comment assets trigger a warning.
    /// </summary>
    [Fact]
    public void Parse_DuplicateCommentAssets_ShouldWarn()
    {
        var asset1 = new AssetReference
        {
            FileName = "first_comment.png",
            RelativeUrl = "/media/first_comment.png",
            ContentType = "image/png",
            SizeBytes = 1024
        };
        var asset2 = new AssetReference
        {
            FileName = "second_comment.png",
            RelativeUrl = "/media/second_comment.png",
            ContentType = "image/png",
            SizeBytes = 2048
        };

        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання\nКоментар: Пояснення", [asset1, asset2])
        };

        var result = _parser.Parse(blocks, []);

        var question = result.Tours[0].Questions[0];
        question.CommentAssetFileName.Should().Be("first_comment.png");
        result.Warnings.Should().Contain(w => w.Contains("second_comment.png") && w.Contains("ignored"));
    }

    /// <summary>
    /// Test that duplicate handout assets trigger a warning.
    /// </summary>
    [Fact]
    public void Parse_DuplicateHandoutAssets_ShouldWarn()
    {
        var asset1 = new AssetReference
        {
            FileName = "first_handout.png",
            RelativeUrl = "/media/first_handout.png",
            ContentType = "image/png",
            SizeBytes = 1024
        };
        var asset2 = new AssetReference
        {
            FileName = "second_handout.png",
            RelativeUrl = "/media/second_handout.png",
            ContentType = "image/png",
            SizeBytes = 2048
        };

        var blocks = new List<DocBlock>
        {
            Block("ТУР 1"),
            Block("1. Питання", [asset1, asset2]),
            Block("Відповідь: Тест")
        };

        var result = _parser.Parse(blocks, []);

        var question = result.Tours[0].Questions[0];
        question.HandoutAssetFileName.Should().Be("first_handout.png");
        result.Warnings.Should().Contain(w => w.Contains("second_handout.png") && w.Contains("ignored"));
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
            Block("Ця споруда отримала прізвисько через схожість з НИМИ. Назвіть ім'я персонажа, в чиєму прізвищі є ВОНИ."),
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
            Block("Відповідь: Відповідь"),
            Block("Коментар: Пояснення", [question1Asset]),
            Block("Запитання 2. Друге питання"),  // New question starts
            Block("Відповідь: Тест2")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(1);
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
            Block("Перед вами назва концепт-кару. Назвіть ім'я персонажа, в чиєму прізвищі є ВОНИ."),
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

        question.HandoutText.Should().Contain("Nissan IV");
        question.Text.Should().Contain("Перед вами назва концепт-кару");
        question.Text.Should().NotContain("[");
        question.Text.Should().NotContain("]");
        question.Answer.Should().Be("Брем Стокер");
        question.AcceptedAnswers.Should().Be("Стокер");
    }

    /// <summary>
    /// Edge case: Russian "Комментарий" used instead of Ukrainian "Коментар".
    /// </summary>
    [Theory]
    [InlineData("Комментарий: Це коментар російською", "Це коментар російською")]
    [InlineData("КОММЕНТАРИЙ: ВЕЛИКИМИ ЛІТЕРАМИ", "ВЕЛИКИМИ ЛІТЕРАМИ")]
    [InlineData("Комментар: Змішаний варіант", "Змішаний варіант")]
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
    /// Edge case: Russian "Источник" or "Источники" used вместо Ukrainian "Джерело".
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
    /// Edge case: Russian "Авторы" (with ы) used instead of Ukrainian "Автори" (with і).
    /// This is critical because if "Авторы:" is not recognized, the parser stays in Source section
    /// and subsequent numbered questions like "18. text" are blocked.
    /// </summary>
    [Theory]
    [InlineData("Авторы: Іван Іванов", "Іван Іванов")]
    [InlineData("Авторы: Богдана Романцова (Київ), Олександр Мерзликін (Кривий Ріг)", "Богдана Романцова (Київ)")]
    public void Parse_RussianAuthorsLabel_ShouldParseCorrectly(string line, string expectedAuthor)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Питання"),
            Block("Відповідь: відповідь"),
            Block(line)
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions[0].Authors.Should().Contain(expectedAuthor);
    }

    /// <summary>
    /// Critical test: When "Авторы:" (Russian) is used, subsequent numbered questions must still be parsed.
    /// This reproduces the Hayfive-7 bug where Q18+ were not parsed because "Авторы:" wasn't recognized.
    /// </summary>
    [Fact]
    public void Parse_RussianAuthorsLabel_ShouldNotBlockNextQuestion()
    {
        // Arrange - Q17 ends with "Авторы:", Q18 starts with "18."
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Питання 1"),
            Block("Відповідь: В1"),
            Block("Джерело: https://example.com"),
            Block("Авторы: Автор Один"),  // Russian "Авторы" with ы
            Block("2. Питання 2"),  // This must be recognized as a new question
            Block("Відповідь: В2"),
            Block("Автор: Автор Два")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert - Both questions should be parsed
        result.Tours[0].Questions.Should().HaveCount(2);
        result.Tours[0].Questions[0].Number.Should().Be("1");
        result.Tours[0].Questions[0].Authors.Should().Contain("Автор Один");
        result.Tours[0].Questions[1].Number.Should().Be("2");
        result.Tours[0].Questions[1].Text.Should().Contain("Питання 2");
    }

    /// <summary>
    /// Edge case: Question number followed by handout marker on same line, then standalone brackets.
    /// Format: "1. Роздатковий матеріал:" followed by "[", content, "]" on separate lines.
    /// The question text should NOT include the handout marker or brackets.
    /// </summary>
    [Fact]
    public void Parse_NumberedQuestionWithInlineHandoutMarkerAndBrackets_ShouldParseCorrectly()
    {
        // Arrange - Based on real example from user (using question 1 for valid sequential numbering in test)
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Роздатковий матеріал:"),
            Block("["),
            Block("Nissan IV"),
            Block("]"),
            Block("Перед вами назва концепт-кару, при виробництві кузову якого хотіли використовувати генномодифікований ІКС. Назвіть ІКС односкладовим словом."),
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

        question.HandoutText.Should().Be("Nissan IV");
        question.Text.Should().StartWith("Перед вами назва концепт-кару");
        question.Text.Should().NotContain("Роздатковий матеріал");
        question.Text.Should().NotContain("[");
        question.Text.Should().NotContain("]");
        question.Answer.Should().Be("Брем Стокер");
        question.AcceptedAnswers.Should().Be("Стокер");
    }

    /// <summary>
    /// Tests that numbered lines in preamble (before any tour) are not parsed as questions.
    /// Example: "1. PayPal: email@example.com" in payment instructions.
    /// </summary>
    [Fact]
    public void Parse_NumberedLinesInPreambleBeforeTour_ShouldNotCreateQuestions()
    {
        // Arrange - numbered list in preamble followed by actual tour and question
        var blocks = new List<DocBlock>
        {
            Block("Турнір \"Тест\""),
            Block("Способи оплати:"),
            Block("1. PayPal: example@gmail.com"),
            Block("2. ПриватБанк: 4731000000000000"),
            Block("3. МоноБанк: 5375000000000000"),
            Block("Тур 1"),
            Block("1. Справжнє питання"),
            Block("Відповідь: Правильна відповідь")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours.Should().HaveCount(1);
        result.Tours[0].Questions.Should().HaveCount(1);
        result.Tours[0].Questions[0].Text.Should().Be("Справжнє питання");
        result.Tours[0].Questions[0].Answer.Should().Be("Правильна відповідь");
    }

    /// <summary>
    /// Tests that "Дайте відповідь:" in question text is not parsed as answer label.
    /// Only labels at line start should be recognized.
    /// </summary>
    [Fact]
    public void Parse_VidpovidInQuestionText_ShouldNotSplitAsAnswerLabel()
    {
        // Arrange - "Дайте відповідь:" is a common phrasing in question text
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1.\nПобачивши варіацію Герти Барб на тему відомого малюнка. Дайте відповідь: що таке ІКС або хто такий ІГРЕК?"),
            Block("Відповідь: капелюх."),
            Block("Залік: удав; інші синонімічні відповіді."),
            Block("Незалік: слон в удаві."),
            Block("Коментар: малюнок Герти відсилає до ілюстрації Екзюпері до «Маленького принца»."),
            Block("Джерело: https://www.facebook.com/example"),
            Block("Автор: Олександр Мерзликін (Кривий Ріг)")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.Text.Should().Contain("Дайте відповідь: що таке ІКС");
        q.Answer.Should().Be("капелюх.");
        q.AcceptedAnswers.Should().Be("удав; інші синонімічні відповіді.");
        q.RejectedAnswers.Should().Be("слон в удаві.");
    }

    /// <summary>
    /// Tests that labels with dot separator (e.g., "Відповідь. ") are recognized.
    /// </summary>
    [Theory]
    [InlineData("Відповідь. капелюх", "капелюх")]
    [InlineData("Відповідь.  два пробіли", "два пробіли")]
    public void Parse_AnswerLabelWithDotSeparator_ExtractsAnswer(string line, string expected)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Питання"),
            Block(line)
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions[0].Answer.Should().Be(expected);
    }

    /// <summary>
    /// Tests that labels with dot separator without whitespace ARE recognized.
    /// E.g., "Відповідь.капелюх" should match as Answer label with value "капелюх".
    /// This enables "Джерело." followed by newline to work correctly.
    /// </summary>
    [Fact]
    public void Parse_AnswerLabelWithDotNoWhitespace_IsRecognized()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Питання"),
            Block("Відповідь.капелюх")  // No space after dot - should still match
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.Answer.Should().Be("капелюх");
        q.Text.Should().Be("Питання");
    }

    /// <summary>
    /// Tests that Source label with dot separator is recognized.
    /// </summary>
    [Theory]
    [InlineData("Джерело. https://example.com", "https://example.com")]
    [InlineData("Джерела. посилання 1; посилання 2", "посилання 1; посилання 2")]
    public void Parse_SourceLabelWithDotSeparator_ExtractsSource(string line, string expected)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест"),
            Block(line)
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions[0].Source.Should().Be(expected);
    }

    /// <summary>
    /// Tests that Author label with dot separator is recognized.
    /// </summary>
    [Fact]
    public void Parse_AuthorLabelWithDotSeparator_ExtractsAuthor()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест"),
            Block("Автор. Іван Петренко")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions[0].Authors.Should().Contain("Іван Петренко");
    }

    /// <summary>
    /// Tests host instructions with dot separator.
    /// </summary>
    [Theory]
    [InlineData("[Ведучому. слова виділити голосом]", "слова виділити голосом")]
    [InlineData("[Ведучому - слова виділити голосом]", "слова виділити голосом")]
    [InlineData("[Ведучому – слова виділити голосом]", "слова виділити голосом")]
    [InlineData("[Ведучому — слова виділити голосом]", "слова виділити голосом")]
    public void Parse_HostInstructionsWithDifferentSeparators_ExtractsInstructions(string line, string expected)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block($"1. {line} Питання тексту")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions[0].HostInstructions.Should().Be(expected);
    }

    /// <summary>
    /// Tests that Залік (accepted answers) with dot separator is recognized.
    /// </summary>
    [Theory]
    [InlineData("Залік. варіант1, варіант2", "варіант1, варіант2")]
    [InlineData("Заліки. варіанти", "варіанти")]
    [InlineData("Залік.варіант без пробілу", "варіант без пробілу")]
    public void Parse_AcceptedLabelWithDotSeparator_ExtractsAcceptedAnswers(string line, string expected)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест"),
            Block(line)
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions[0].AcceptedAnswers.Should().Be(expected);
    }

    /// <summary>
    /// Tests that Незалік (rejected answers) with dot separator is recognized.
    /// </summary>
    [Theory]
    [InlineData("Незалік. неправильно", "неправильно")]
    [InlineData("Не залік. варіант", "варіант")]
    [InlineData("Не приймається. відповідь", "відповідь")]
    public void Parse_RejectedLabelWithDotSeparator_ExtractsRejectedAnswers(string line, string expected)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест"),
            Block(line)
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions[0].RejectedAnswers.Should().Be(expected);
    }

    /// <summary>
    /// Tests that Коментар with dot separator and Коментарі variant are recognized.
    /// </summary>
    [Theory]
    [InlineData("Коментар. Цікавий факт", "Цікавий факт")]
    [InlineData("Коментарі: Інформація", "Інформація")]
    [InlineData("Коментарі. Декілька коментарів", "Декілька коментарів")]
    public void Parse_CommentLabelVariantsWithDotSeparator_ExtractsComment(string line, string expected)
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест"),
            Block(line)
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        result.Tours[0].Questions[0].Comment.Should().Be(expected);
    }

    /// <summary>
    /// Tests that Джерело. followed by newline (content on next line) is properly parsed.
    /// </summary>
    [Fact]
    public void Parse_SourceLabelWithDotAndNewline_ExtractsSource()
    {
        // Arrange - "Джерело." on its own line, content on next line
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест"),
            Block("Джерело."),
            Block("https://example.com"),
            Block("https://another.com")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var source = result.Tours[0].Questions[0].Source;
        source.Should().Contain("https://example.com");
        source.Should().Contain("https://another.com");
    }

    /// <summary>
    /// Tests the complete question format from the reported bug:
    /// all fields use dot separators, Джерело. has content on next lines.
    /// </summary>
    [Fact]
    public void Parse_AllFieldsWithDotSeparator_ParsesCorrectly()
    {
        // Arrange - exact format from the bug report
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. ВОНИ - назва фільму про стосунки на відстані."),
            Block("Відповідь. Листи з фронту"),
            Block("Залік. За згадкою листів та фронту"),
            Block("Джерело."),
            Block("https://www.kino-teatr.ru/kino/movie/asia/159176/annot/"),
            Block("https://murawei.de/r006696.html"),
            Block("Автор. Олексій Жидких")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.Text.Should().Be("ВОНИ - назва фільму про стосунки на відстані.");
        q.Answer.Should().Be("Листи з фронту");
        q.AcceptedAnswers.Should().Be("За згадкою листів та фронту");
        q.Source.Should().Contain("https://www.kino-teatr.ru/kino/movie/asia/159176/annot/");
        q.Source.Should().Contain("https://murawei.de/r006696.html");
        q.Authors.Should().Contain("Олексій Жидких");
    }

    /// <summary>
    /// Tests that all fields with dot separators don't merge into Answer.
    /// This is the core bug being fixed.
    /// </summary>
    [Fact]
    public void Parse_DotSeparators_DoNotMergeIntoAnswer()
    {
        // Arrange
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Питання тексту"),
            Block("Відповідь. Відповідь тексту"),
            Block("Залік. Залік тексту"),
            Block("Незалік. Незалік тексту"),
            Block("Коментар. Коментар тексту"),
            Block("Джерело. Джерело тексту"),
            Block("Автор. Автор тексту")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.Answer.Should().Be("Відповідь тексту");
        q.Answer.Should().NotContain("Залік");
        q.Answer.Should().NotContain("Незалік");
        q.Answer.Should().NotContain("Коментар");
        q.Answer.Should().NotContain("Джерело");
        q.Answer.Should().NotContain("Автор");
        q.AcceptedAnswers.Should().Be("Залік тексту");
        q.RejectedAnswers.Should().Be("Незалік тексту");
        q.Comment.Should().Be("Коментар тексту");
        q.Source.Should().Be("Джерело тексту");
        q.Authors.Should().Contain("Автор тексту");
    }

    #endregion

    #region Multiline Content and Blank Lines

    /// <summary>
    /// Tests that question text with internal blank lines preserves them.
    /// </summary>
    [Fact]
    public void Parse_QuestionTextWithBlankLines_PreservesInternalBlankLines()
    {
        // Arrange - question text has a blank line between paragraphs
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Перший абзац тексту.\n\nДругий абзац після порожнього рядка."),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.Text.Should().Contain("\n\n");
        q.Text.Should().StartWith("Перший абзац тексту.");
        q.Text.Should().EndWith("Другий абзац після порожнього рядка.");
    }

    /// <summary>
    /// Tests that multiline answers from separate blocks are preserved.
    /// </summary>
    [Fact]
    public void Parse_MultilineAnswer_PreservesAllLines()
    {
        // Arrange - answer spans multiple blocks
        // Note: Don't use "2." "3." as they look like question numbers
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Питання"),
            Block("Відповідь:"),
            Block("перша частина відповіді"),
            Block("друга частина відповіді"),
            Block("третя частина відповіді"),
            Block("Автор: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.Answer.Should().Contain("перша частина відповіді");
        q.Answer.Should().Contain("друга частина відповіді");
        q.Answer.Should().Contain("третя частина відповіді");
    }

    /// <summary>
    /// Tests that comment with internal blank lines preserves them.
    /// </summary>
    [Fact]
    public void Parse_CommentWithBlankLines_PreservesInternalBlankLines()
    {
        // Arrange - comment has a blank line between paragraphs
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест"),
            Block("Коментар: Перший абзац коментаря.\n\nДругий абзац коментаря.")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.Comment.Should().Contain("\n\n");
        q.Comment.Should().StartWith("Перший абзац коментаря.");
        q.Comment.Should().EndWith("Другий абзац коментаря.");
    }

    /// <summary>
    /// Tests that multiline accepted answers from separate blocks are preserved.
    /// </summary>
    [Fact]
    public void Parse_MultilineAcceptedAnswers_PreservesAllLines()
    {
        // Arrange - accepted answers span multiple blocks
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест"),
            Block("Залік:"),
            Block("варіант перший"),
            Block("варіант другий"),
            Block("Автор: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.AcceptedAnswers.Should().Contain("варіант перший");
        q.AcceptedAnswers.Should().Contain("варіант другий");
    }

    /// <summary>
    /// Tests that leading and trailing blank lines are trimmed while internal ones are preserved.
    /// </summary>
    [Fact]
    public void Parse_TextWithLeadingTrailingBlankLines_TrimsEdgesOnly()
    {
        // Arrange - text has leading/trailing blank lines but internal ones should stay
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1.\n\nПерший рядок.\n\nДругий рядок.\n\n"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.Text.Should().StartWith("Перший");
        q.Text.Should().EndWith("рядок.");
        q.Text.Should().Contain("\n\n"); // Internal blank line preserved
    }

    /// <summary>
    /// Tests that handout text with blank lines preserves internal structure.
    /// </summary>
    [Fact]
    public void Parse_HandoutTextWithBlankLines_PreservesInternalBlankLines()
    {
        // Arrange - handout has blank line between paragraphs
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. [Роздатка: Перший рядок роздатки.\n\nДругий рядок роздатки.] Текст питання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.HandoutText.Should().Contain("\n\n");
    }

    /// <summary>
    /// Tests source field with multiline content from separate blocks.
    /// </summary>
    [Fact]
    public void Parse_MultilineSource_PreservesAllLines()
    {
        // Arrange - source spans multiple blocks (typical for multiple URLs)
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест"),
            Block("Джерело:"),
            Block("https://example1.com"),
            Block("https://example2.com"),
            Block("Автор: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.Source.Should().Contain("https://example1.com");
        q.Source.Should().Contain("https://example2.com");
    }

    /// <summary>
    /// Tests that blank lines between different sections don't bleed into adjacent sections.
    /// </summary>
    [Fact]
    public void Parse_BlankLinesBetweenSections_DoNotBleedIntoContent()
    {
        // Arrange - blank lines appear between different sections
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Текст питання"),
            Block(""),  // Blank block between text and answer
            Block("Відповідь: Відповідь тексту"),
            Block(""),  // Blank block between answer and comment
            Block("Коментар: Коментар тексту")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.Text.Should().Be("Текст питання");
        q.Answer.Should().Be("Відповідь тексту");
        q.Comment.Should().Be("Коментар тексту");
    }

    /// <summary>
    /// Tests that rejected answers can span multiple lines.
    /// </summary>
    [Fact]
    public void Parse_MultilineRejectedAnswers_PreservesAllLines()
    {
        // Arrange - rejected answers span multiple blocks
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест"),
            Block("Незалік:"),
            Block("неправильний варіант 1"),
            Block("неправильний варіант 2"),
            Block("Автор: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.RejectedAnswers.Should().Contain("неправильний варіант 1");
        q.RejectedAnswers.Should().Contain("неправильний варіант 2");
    }

    /// <summary>
    /// Tests that empty paragraph blocks between question text blocks are preserved as blank lines.
    /// This simulates DOCX structure where each paragraph is a separate block.
    /// </summary>
    [Fact]
    public void Parse_EmptyParagraphBlocksInQuestionText_PreservedAsBlankLines()
    {
        // Arrange - question text spread across blocks with empty block in between
        // This simulates: "Intro paragraph" + empty paragraph + "1. First item" + "2. Second item"
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Увага, тетрабліц. Чотири запитання."),
            Block(""),  // Empty paragraph in DOCX = blank line
            Block("перше підпитання"),
            Block("друге підпитання"),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.Text.Should().Contain("Увага, тетрабліц");
        q.Text.Should().Contain("\n\n"); // Blank line should be preserved
        q.Text.Should().Contain("перше підпитання");
        q.Text.Should().Contain("друге підпитання");
    }

    /// <summary>
    /// Tests that multiple empty paragraph blocks between question text create multiple blank lines.
    /// </summary>
    [Fact]
    public void Parse_MultipleEmptyParagraphBlocks_PreservedAsMultipleBlankLines()
    {
        // Arrange - two empty paragraphs in a row
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Перший абзац."),
            Block(""),  // First empty paragraph
            Block(""),  // Second empty paragraph
            Block("Другий абзац."),
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        // Should have 3 newlines (two blank lines = \n\n\n between paragraphs)
        q.Text.Should().Contain("\n\n\n");
    }

    /// <summary>
    /// Tests that empty paragraph blocks in comment section are preserved as blank lines.
    /// </summary>
    [Fact]
    public void Parse_EmptyParagraphBlocksInComment_PreservedAsBlankLines()
    {
        // Arrange - comment with empty paragraph block between content
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Питання"),
            Block("Відповідь: Тест"),
            Block("Коментар: Перший абзац коментаря."),
            Block(""),  // Empty paragraph
            Block("Другий абзац коментаря."),
            Block("Автор: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.Comment.Should().Contain("Перший абзац коментаря.");
        q.Comment.Should().Contain("\n\n"); // Blank line preserved
        q.Comment.Should().Contain("Другий абзац коментаря.");
    }

    /// <summary>
    /// Tests that empty paragraph blocks in answer section are preserved as blank lines.
    /// </summary>
    [Fact]
    public void Parse_EmptyParagraphBlocksInAnswer_PreservedAsBlankLines()
    {
        // Arrange - multiline answer with empty paragraph block
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Питання"),
            Block("Відповідь:"),
            Block("перша частина"),
            Block(""),  // Empty paragraph
            Block("друга частина"),
            Block("Автор: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.Answer.Should().Contain("перша частина");
        q.Answer.Should().Contain("\n\n"); // Blank line preserved
        q.Answer.Should().Contain("друга частина");
    }

    /// <summary>
    /// Tests that empty paragraph blocks at the end of content are trimmed.
    /// </summary>
    [Fact]
    public void Parse_EmptyParagraphBlocksAtEndOfContent_AreTrimmed()
    {
        // Arrange - empty blocks at the end of question text before answer
        var blocks = new List<DocBlock>
        {
            Block("Тур 1"),
            Block("1. Текст питання"),
            Block(""),  // Empty block before answer - should be trimmed
            Block(""),  // Another empty block - should be trimmed
            Block("Відповідь: Тест")
        };

        // Act
        var result = _parser.Parse(blocks, []);

        // Assert
        var q = result.Tours[0].Questions[0];
        q.Text.Should().Be("Текст питання"); // No trailing blank lines
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

