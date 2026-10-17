using System.Text.Json;
using FluentAssertions;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Import;
using Xunit;

namespace QuestionsHub.UnitTests.ImportParsing;

/// <summary>People in a .qhub: a two-word string, or (format 1.2) an object for names of several words.</summary>
public class QhubPeopleTests
{
    [Fact]
    public void Encode_KeepsTheStringForm_WhenItParsesBackExactly()
    {
        QhubPeople.Encode(new Author { FirstName = "Олена", LastName = "Коваленко" }).Should().Be("Олена Коваленко");
    }

    [Theory]
    [InlineData("Жан Клод", "Ван Дамм")]
    [InlineData("Іван", "Петренко-Сидоренко (Київ)")]
    [InlineData("Ірина", "Грищенко.")]
    public void Encode_UsesTheExactPair_WhenTheStringWouldNotParseBack(string first, string last)
    {
        var encoded = QhubPeople.Encode(new Author { FirstName = first, LastName = last });

        PackageDbImporter.ParseAuthorName(encoded).Should().Be((first, last));
    }

    [Fact]
    public void Decode_MoreThanOneSeparator_IsNotAName()
    {
        PackageDbImporter.ParseAuthorName($"Мері{QhubPeople.Separator}Джейн{QhubPeople.Separator}Доу").Should().Be((null, null));
    }

    [Fact]
    public void Json_ReadsBothForms_AndWritesObjectsOnlyForExactPairs()
    {
        var question = JsonSerializer.Deserialize<QhubQuestion>(
            """{ "authors": [ "Олена Коваленко", { "firstName": " Жан Клод ", "lastName": "Ван Дамм" } ] }""")!;

        question.Authors.Should().HaveCount(2);
        question.Authors![0].Should().Be("Олена Коваленко");
        PackageDbImporter.ParseAuthorName(question.Authors[1]).Should().Be(("Жан Клод", "Ван Дамм"));

        var json = JsonSerializer.Serialize(question, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        json.Should().Contain("""["Олена Коваленко",{"firstName":"Жан Клод","lastName":"Ван Дамм"}]""");
    }

    [Theory]
    [InlineData("""{ "authors": [ { "firstName": "Жан" } ] }""")]
    [InlineData("""{ "authors": [ 42 ] }""")]
    [InlineData("""{ "authors": [ "Мері\u001fДжейн Доу" ] }""")]
    [InlineData("""{ "authors": [ { "firstName": "Мері\u001fДжейн", "lastName": "Доу" } ] }""")]
    public void Json_RejectsIncompletePeople_AndTheInternalSeparator(string json)
    {
        var act = () => JsonSerializer.Deserialize<QhubQuestion>(json);

        act.Should().Throw<JsonException>();
    }
}
