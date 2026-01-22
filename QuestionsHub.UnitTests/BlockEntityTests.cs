using FluentAssertions;
using QuestionsHub.Blazor.Domain;
using Xunit;

namespace QuestionsHub.UnitTests;

/// <summary>
/// Tests for the Block entity and related Tour/Package computed properties.
/// </summary>
public class BlockEntityTests
{
    [Fact]
    public void Block_DisplayName_ReturnsName_WhenNameIsSet()
    {
        // Arrange
        var block = new Block
        {
            Name = "Блок редактора Іванова",
            Editors = []
        };

        // Act & Assert
        block.DisplayName.Should().Be("Блок редактора Іванова");
    }

    [Fact]
    public void Block_DisplayName_ReturnsDefault_WhenNameIsEmpty()
    {
        // Arrange
        var block = new Block
        {
            Name = null,
            Editors =
            [
                new Author { Id = 1, FirstName = "Іван", LastName = "Місяць" }
            ]
        };

        // Act & Assert
        block.DisplayName.Should().Be("Блок");
    }

    [Fact]
    public void Block_GetDisplayName_ReturnsName_WhenNameIsSet()
    {
        // Arrange
        var block = new Block
        {
            Name = "Мій блок",
            Editors = []
        };

        // Act & Assert
        block.GetDisplayName(5).Should().Be("Мій блок");
    }

    [Fact]
    public void Block_GetDisplayName_ReturnsBlockWithNumber_WhenNameIsEmpty()
    {
        // Arrange
        var block = new Block
        {
            Name = null,
            Editors =
            [
                new Author { Id = 1, FirstName = "Іван", LastName = "Місяць" }
            ]
        };

        // Act & Assert
        block.GetDisplayName(1).Should().Be("Блок 1");
        block.GetDisplayName(3).Should().Be("Блок 3");
        block.GetDisplayName(10).Should().Be("Блок 10");
    }

    [Fact]
    public void Block_GetDisplayName_ReturnsBlockWithNumber_WhenNameIsWhitespace()
    {
        // Arrange
        var block = new Block
        {
            Name = "   ",
            Editors = []
        };

        // Act & Assert
        block.GetDisplayName(2).Should().Be("Блок 2");
    }

    [Fact]
    public void Tour_HasBlocks_ReturnsFalse_WhenNoBlocks()
    {
        // Arrange
        var tour = new Tour
        {
            Number = "1",
            Blocks = []
        };

        // Act & Assert
        tour.HasBlocks.Should().BeFalse();
    }

    [Fact]
    public void Tour_HasBlocks_ReturnsTrue_WhenHasBlocks()
    {
        // Arrange
        var tour = new Tour
        {
            Number = "1",
            Blocks =
            [
                new Block { Id = 1, OrderIndex = 0 }
            ]
        };

        // Act & Assert
        tour.HasBlocks.Should().BeTrue();
    }

    [Fact]
    public void Tour_AllEditors_ReturnsTourEditors_WhenNoBlocks()
    {
        // Arrange
        var editor1 = new Author { Id = 1, FirstName = "Іван", LastName = "Перший" };
        var editor2 = new Author { Id = 2, FirstName = "Петро", LastName = "Другий" };

        var tour = new Tour
        {
            Number = "1",
            Editors = [editor1, editor2],
            Blocks = []
        };

        // Act
        var allEditors = tour.AllEditors.ToList();

        // Assert
        allEditors.Should().HaveCount(2);
        allEditors.Should().Contain(editor1);
        allEditors.Should().Contain(editor2);
    }

    [Fact]
    public void Tour_AllEditors_ReturnsBlockEditors_WhenHasBlocks()
    {
        // Arrange
        var tourEditor = new Author { Id = 1, FirstName = "Тур", LastName = "Редактор" };
        var blockEditor1 = new Author { Id = 2, FirstName = "Блок", LastName = "Перший" };
        var blockEditor2 = new Author { Id = 3, FirstName = "Блок", LastName = "Другий" };

        var tour = new Tour
        {
            Number = "1",
            Editors = [tourEditor], // Should be ignored when blocks exist
            Blocks =
            [
                new Block { Id = 1, OrderIndex = 0, Editors = [blockEditor1] },
                new Block { Id = 2, OrderIndex = 1, Editors = [blockEditor2] }
            ]
        };

        // Act
        var allEditors = tour.AllEditors.ToList();

        // Assert
        allEditors.Should().HaveCount(2);
        allEditors.Should().Contain(blockEditor1);
        allEditors.Should().Contain(blockEditor2);
        allEditors.Should().NotContain(tourEditor);
    }

    [Fact]
    public void Tour_AllEditors_ReturnsDistinctEditors_WhenSameEditorInMultipleBlocks()
    {
        // Arrange
        var sharedEditor = new Author { Id = 1, FirstName = "Спільний", LastName = "Редактор" };
        var uniqueEditor = new Author { Id = 2, FirstName = "Унікальний", LastName = "Редактор" };

        var tour = new Tour
        {
            Number = "1",
            Editors = [],
            Blocks =
            [
                new Block { Id = 1, OrderIndex = 0, Editors = [sharedEditor] },
                new Block { Id = 2, OrderIndex = 1, Editors = [sharedEditor, uniqueEditor] }
            ]
        };

        // Act
        var allEditors = tour.AllEditors.ToList();

        // Assert
        allEditors.Should().HaveCount(2);
        allEditors.Should().Contain(sharedEditor);
        allEditors.Should().Contain(uniqueEditor);
    }

    [Fact]
    public void Package_Editors_IncludesBlockEditors_WhenTourHasBlocks()
    {
        // Arrange
        var tourEditor = new Author { Id = 1, FirstName = "Тур", LastName = "Редактор" };
        var blockEditor = new Author { Id = 2, FirstName = "Блок", LastName = "Редактор" };

        var package = new Package
        {
            Title = "Тестовий пакет",
            Tours =
            [
                new Tour
                {
                    Number = "1",
                    Editors = [tourEditor],
                    Blocks = []
                },
                new Tour
                {
                    Number = "2",
                    Editors = [],
                    Blocks =
                    [
                        new Block { Id = 1, OrderIndex = 0, Editors = [blockEditor] }
                    ]
                }
            ]
        };

        // Act
        var packageEditors = package.Editors.ToList();

        // Assert
        packageEditors.Should().HaveCount(2);
        packageEditors.Should().Contain(tourEditor);
        packageEditors.Should().Contain(blockEditor);
    }

    [Fact]
    public void Package_Editors_ReturnsOnlyBlockEditors_WhenTourHasBlocksAndTourEditors()
    {
        // Arrange - when a tour has blocks, Tour.AllEditors should return block editors, not tour editors
        var tourEditor = new Author { Id = 1, FirstName = "Тур", LastName = "Редактор" };
        var blockEditor = new Author { Id = 2, FirstName = "Блок", LastName = "Редактор" };

        var package = new Package
        {
            Title = "Тестовий пакет",
            Tours =
            [
                new Tour
                {
                    Number = "1",
                    Editors = [tourEditor], // This should be ignored because tour has blocks
                    Blocks =
                    [
                        new Block { Id = 1, OrderIndex = 0, Editors = [blockEditor] }
                    ]
                }
            ]
        };

        // Act
        var packageEditors = package.Editors.ToList();

        // Assert - tour editor should be ignored, only block editor should be included
        packageEditors.Should().HaveCount(1);
        packageEditors.Should().Contain(blockEditor);
        packageEditors.Should().NotContain(tourEditor);
    }

    [Fact]
    public void Package_Editors_ReturnsDistinctEditors_WhenSameEditorInMultipleBlocks()
    {
        // Arrange
        var sharedEditor = new Author { Id = 1, FirstName = "Спільний", LastName = "Редактор" };

        var package = new Package
        {
            Title = "Тестовий пакет",
            Tours =
            [
                new Tour
                {
                    Number = "1",
                    Editors = [],
                    Blocks =
                    [
                        new Block { Id = 1, OrderIndex = 0, Editors = [sharedEditor] },
                        new Block { Id = 2, OrderIndex = 1, Editors = [sharedEditor] }
                    ]
                }
            ]
        };

        // Act
        var packageEditors = package.Editors.ToList();

        // Assert - same editor in multiple blocks should appear only once
        packageEditors.Should().HaveCount(1);
        packageEditors.Should().Contain(sharedEditor);
    }

    [Fact]
    public void Package_Editors_ReturnsDistinctEditors_WhenSameEditorInMultipleTours()
    {
        // Arrange
        var sharedEditor = new Author { Id = 1, FirstName = "Спільний", LastName = "Редактор" };

        var package = new Package
        {
            Title = "Тестовий пакет",
            Tours =
            [
                new Tour
                {
                    Number = "1",
                    Editors = [sharedEditor],
                    Blocks = []
                },
                new Tour
                {
                    Number = "2",
                    Editors = [],
                    Blocks =
                    [
                        new Block { Id = 1, OrderIndex = 0, Editors = [sharedEditor] }
                    ]
                }
            ]
        };

        // Act
        var packageEditors = package.Editors.ToList();

        // Assert - same editor in tour and block should appear only once
        packageEditors.Should().HaveCount(1);
        packageEditors.Should().Contain(sharedEditor);
    }

    [Fact]
    public void Package_Editors_ReturnsEmpty_WhenNoToursHaveEditors()
    {
        // Arrange
        var package = new Package
        {
            Title = "Тестовий пакет",
            Tours =
            [
                new Tour
                {
                    Number = "1",
                    Editors = [],
                    Blocks = []
                }
            ]
        };

        // Act
        var packageEditors = package.Editors.ToList();

        // Assert
        packageEditors.Should().BeEmpty();
    }

    [Fact]
    public void Package_Editors_ReturnsEmpty_WhenBlocksHaveNoEditors()
    {
        // Arrange
        var package = new Package
        {
            Title = "Тестовий пакет",
            Tours =
            [
                new Tour
                {
                    Number = "1",
                    Editors = [],
                    Blocks =
                    [
                        new Block { Id = 1, OrderIndex = 0, Editors = [] }
                    ]
                }
            ]
        };

        // Act
        var packageEditors = package.Editors.ToList();

        // Assert
        packageEditors.Should().BeEmpty();
    }

    [Fact]
    public void Package_Editors_ReturnsPackageEditors_WhenSharedEditorsIsTrue()
    {
        // Arrange
        var packageEditor = new Author { Id = 1, FirstName = "Пакет", LastName = "Редактор" };
        var tourEditor = new Author { Id = 2, FirstName = "Тур", LastName = "Редактор" };

        var package = new Package
        {
            Title = "Тестовий пакет",
            SharedEditors = true,
            PackageEditors = [packageEditor],
            Tours =
            [
                new Tour
                {
                    Number = "1",
                    Editors = [tourEditor],
                    Blocks = []
                }
            ]
        };

        // Act
        var editors = package.Editors.ToList();

        // Assert - should return package editors, not tour editors
        editors.Should().HaveCount(1);
        editors.Should().Contain(packageEditor);
        editors.Should().NotContain(tourEditor);
    }

    [Fact]
    public void Package_Editors_ReturnsTourEditors_WhenSharedEditorsIsFalse()
    {
        // Arrange
        var packageEditor = new Author { Id = 1, FirstName = "Пакет", LastName = "Редактор" };
        var tourEditor = new Author { Id = 2, FirstName = "Тур", LastName = "Редактор" };

        var package = new Package
        {
            Title = "Тестовий пакет",
            SharedEditors = false,
            PackageEditors = [packageEditor],
            Tours =
            [
                new Tour
                {
                    Number = "1",
                    Editors = [tourEditor],
                    Blocks = []
                }
            ]
        };

        // Act
        var editors = package.Editors.ToList();

        // Assert - should return tour editors, not package editors
        editors.Should().HaveCount(1);
        editors.Should().Contain(tourEditor);
        editors.Should().NotContain(packageEditor);
    }
}
