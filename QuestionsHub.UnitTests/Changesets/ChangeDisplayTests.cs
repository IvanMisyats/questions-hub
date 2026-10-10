using System.Text.Json.Nodes;
using FluentAssertions;
using QuestionsHub.Blazor.Infrastructure.AgentApi.Changesets;
using Xunit;

namespace QuestionsHub.UnitTests.Changesets;

public class ChangeDisplayTests
{
    private static JsonNode? Node(string json) => JsonNode.Parse(json);

    [Theory]
    [InlineData("text", "\"Новий текст\"", "Новий текст")]
    [InlineData("text", "\"\"", "(порожньо)")]
    [InlineData("comment", "null", "—")]
    [InlineData("authors", """[ { "id": 3, "name": "Марія Шевченко" }, { "id": null, "name": "Нова Авторка" } ]""", "Марія Шевченко, Нова Авторка")]
    [InlineData("authors", "[]", "—")]
    [InlineData("sharedEditors", "true", "так")]
    [InlineData("orderIndex", "0", "1")]
    [InlineData("numberingMode", "\"perTour\"", "у кожному турі")]
    [InlineData("type", "\"warmup\"", "розминка")]
    [InlineData("text", "\"manual\"", "manual")]
    [InlineData("location", """{ "tourId": 12, "blockId": null, "position": 1 }""", "тур #12, позиція 2")]
    [InlineData("location", """{ "tourId": 12, "blockId": 5, "position": 0 }""", "тур #12, блок #5, позиція 1")]
    [InlineData("location", """{ "tourId": 12, "position": "first" }""", "тур #12")]
    [InlineData("location", """{ "unexpected": true }""", "{\"unexpected\":true}")]
    public void Value_FormatsEachKindOfValue(string field, string json, string expected)
    {
        var node = json == "null" ? null : Node(json);
        ChangeDisplay.Value(field, node).Should().Be(expected);
    }

    [Fact]
    public void What_NamesTheFieldOrTheKind()
    {
        ChangeDisplay.What(new ChangeDto(0, "question", 1, "", "acceptedAnswers", null, null, null, null)).Should().Be("Залік");
        ChangeDisplay.What(new ChangeDto(0, "tour", 1, "", null, null, null, "deleted", null)).Should().Be("Видалено");
        ChangeDisplay.What(new ChangeDto(0, "question", 1, "", "somethingNew", null, null, null, null)).Should().Be("somethingNew");
    }

    [Fact]
    public void Snapshots_ShowEverythingNeededToRestore()
    {
        var tour = Node("""
            { "id": 7, "title": "Тема", "questions": [
                { "id": 1, "number": "10", "text": "Питання", "answer": "Відповідь", "comment": "", "source": "Вікі",
                  "authors": [ { "id": 2, "name": "Анна Коваль" } ] },
                { "id": 2, "number": "20", "text": "Друге", "answer": "Б", "authors": [] } ] }
            """);

        ChangeDisplay.Summary(tour).Should().Be("Тема (2 запитання)");
        var questions = ChangeDisplay.TourQuestions(tour);
        questions.Should().HaveCount(2);
        ChangeDisplay.QuestionLines(questions[0]).Should().Equal(
            ("Номер", "10"), ("Текст", "Питання"), ("Відповідь", "Відповідь"), ("Джерело", "Вікі"), ("Автори", "Анна Коваль"));

        ChangeDisplay.Summary(questions[1]).Should().Be("Друге");
        ChangeDisplay.TourQuestions(questions[1]).Should().BeEmpty();
        ChangeDisplay.QuestionLines(null).Should().BeEmpty();
        ChangeDisplay.Summary(Node("\"not an object\"")).Should().BeEmpty();
    }

    [Fact]
    public void TourSnapshot_ShowsTheToursOwnFieldsAndBlocks()
    {
        var tour = Node("""
            { "id": 7, "type": "warmup", "number": "0", "title": null, "preamble": "Тестери", "comment": "",
              "editors": [ { "id": 1, "name": "Анна Коваль" } ],
              "blocks": [ { "id": 3, "name": "Блок А", "preamble": null, "editors": [ { "id": 2, "name": "Петро Мельник" } ] } ],
              "questions": [] }
            """);

        ChangeDisplay.TourLines(tour).Should().Equal(
            ("Тип туру", "розминка"), ("Номер", "0"), ("Преамбула", "Тестери"), ("Редактори", "Анна Коваль"),
            ("Блок", "Блок А; редактори: Петро Мельник"), ("Запитання", "немає"));
        ChangeDisplay.TourLines(Node("""{ "id": 1, "text": "питання" }""")).Should().BeEmpty("question snapshots have no tour lines");
    }

    [Theory]
    [InlineData("Question 501: 'answer' is empty.", "Запитання 501: поле «Відповідь» порожнє.")]
    [InlineData("A new question has empty 'text'.", "Нове запитання без тексту.")]
    [InlineData("New author 'Олена Коваленко' will be created (no author with exactly this name exists).", "Створено нового автора: Олена Коваленко.")]
    [InlineData("New tag 'Історія' will be created.", "Створено новий тег: Історія.")]
    [InlineData("Something else.", "Something else.")]
    public void Warnings_AreShownInUkrainianWhenKnown(string warning, string expected)
    {
        ChangeDisplay.Warning(warning).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "1 зміна")]
    [InlineData(3, "3 зміни")]
    [InlineData(5, "5 змін")]
    [InlineData(11, "11 змін")]
    [InlineData(21, "21 зміна")]
    [InlineData(112, "112 змін")]
    public void Plural_FollowsUkrainianRules(int count, string expected)
    {
        ChangeDisplay.Plural(count, "зміна", "зміни", "змін").Should().Be(expected);
    }
}
