using System.Text.Json;
using System.Text.Json.Serialization;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// People (authors, editors) in a <c>.qhub</c>: normally the string <c>"Ім'я Прізвище"</c>; since format
/// 1.2 also <c>{"firstName": …, "lastName": …}</c> for a name that the string form cannot carry — a first
/// or last name of several words, or one that the importer's name parsing would change. Inside the import
/// pipeline (which passes people as strings) such a name travels as first name, <see cref="Separator"/>,
/// last name; DOCX parsers never produce that character.
/// </summary>
public static class QhubPeople
{
    /// <summary>ASCII unit separator: splits an exact first and last name inside the pipeline.</summary>
    public const char Separator = '\u001F';

    /// <summary>How <paramref name="author"/> is written: the plain full name when it parses back exactly.</summary>
    public static string Encode(Author author) =>
        PackageDbImporter.ParseAuthorName(author.FullName) == (author.FirstName, author.LastName)
            ? author.FullName
            : $"{author.FirstName}{Separator}{author.LastName}";

    /// <summary>
    /// An exact first/last pair from <see cref="Encode"/> or the object form; null for a plain string. Anything
    /// but exactly one separator decodes to an empty pair, which the importer rejects as a name.
    /// </summary>
    public static (string FirstName, string LastName)? Decode(string name)
    {
        var separator = name.IndexOf(Separator);
        if (separator < 0)
            return null;
        if (name.IndexOf(Separator, separator + 1) >= 0)
            return ("", "");

        return (name[..separator].Trim(), name[(separator + 1)..].Trim());
    }

    /// <summary>A name read from a file must not contain the internal separator.</summary>
    internal static string Checked(string? name) =>
        name != null && name.Contains(Separator)
            ? throw new JsonException("A person's name contains a control character (U+001F).")
            : name!;
}

/// <summary>Reads and writes a people list in both forms (see <see cref="QhubPeople"/>).</summary>
public sealed class QhubPeopleConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected an array of people.");

        var people = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    people.Add(QhubPeople.Checked(reader.GetString()));
                    break;
                case JsonTokenType.StartObject:
                    string? first = null, last = null;
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        var property = reader.GetString();
                        reader.Read();
                        var value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                            reader.Skip();
                        if (property == "firstName") first = value;
                        else if (property == "lastName") last = value;
                    }
                    if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last))
                        throw new JsonException("A person object needs both 'firstName' and 'lastName'.");
                    people.Add($"{QhubPeople.Checked(first).Trim()}{QhubPeople.Separator}{QhubPeople.Checked(last).Trim()}");
                    break;
                default:
                    throw new JsonException("A person is a string or an object with 'firstName' and 'lastName'.");
            }
        }

        return people;
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var name in value)
        {
            if (QhubPeople.Decode(name) is { } person)
            {
                writer.WriteStartObject();
                writer.WriteString("firstName", person.FirstName);
                writer.WriteString("lastName", person.LastName);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteStringValue(name);
            }
        }
        writer.WriteEndArray();
    }
}
