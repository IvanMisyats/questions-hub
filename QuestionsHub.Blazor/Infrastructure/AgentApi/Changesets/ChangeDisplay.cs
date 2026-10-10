using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace QuestionsHub.Blazor.Infrastructure.AgentApi.Changesets;

/// <summary>
/// Presents stored changeset diffs in the editor UI (Ukrainian labels and values). Pure functions over
/// <see cref="ChangeDto"/>, tolerant of unexpected JSON shapes (stored data outlives code changes).
/// </summary>
public static partial class ChangeDisplay
{
    private static readonly Dictionary<string, string> FieldLabels = new()
    {
        ["title"] = "Назва", ["description"] = "Опис", ["preamble"] = "Преамбула",
        ["playedFrom"] = "Дата початку", ["playedTo"] = "Дата завершення", ["editors"] = "Редактори",
        ["sharedEditors"] = "Спільні редактори", ["numberingMode"] = "Нумерація", ["tags"] = "Теги",
        ["comment"] = "Коментар", ["name"] = "Назва блоку", ["text"] = "Текст", ["answer"] = "Відповідь",
        ["hostInstructions"] = "Вказівка ведучому", ["handoutText"] = "Роздатка", ["handoutUrl"] = "Роздатка (файл)",
        ["acceptedAnswers"] = "Залік", ["rejectedAnswers"] = "Незалік", ["answerForm"] = "Форма",
        ["commentAttachmentUrl"] = "Ілюстрація до коментаря", ["source"] = "Джерело", ["authors"] = "Автори",
        ["number"] = "Номер", ["orderIndex"] = "Позиція", ["location"] = "Розташування", ["type"] = "Тип туру",
    };

    private static readonly Dictionary<string, string> ValueLabels = new()
    {
        ["global"] = "наскрізна", ["perTour"] = "у кожному турі", ["manual"] = "ручна",
        ["regular"] = "звичайний", ["warmup"] = "розминка", ["shootout"] = "перестрілка",
    };

    /// <summary>Question fields shown in a full snapshot, in reading order.</summary>
    private static readonly string[] QuestionFields =
    [
        "number", "hostInstructions", "handoutText", "handoutUrl", "text", "answer", "acceptedAnswers",
        "rejectedAnswers", "answerForm", "comment", "commentAttachmentUrl", "source", "authors"
    ];

    public static string FieldLabel(string? field) =>
        field != null && FieldLabels.TryGetValue(field, out var label) ? label : field ?? "";

    /// <summary>"Що" column: the field, or «Додано»/«Видалено».</summary>
    public static string What(ChangeDto change) => change.Kind switch
    {
        "added" => "Додано",
        "deleted" => "Видалено",
        _ => FieldLabel(change.Field)
    };

    /// <summary>A field value as text: authors/tags by name, booleans, positions 1-based, enums in Ukrainian.</summary>
    public static string Value(string? field, JsonNode? value)
    {
        switch (value)
        {
            case null:
                return "—";
            case JsonArray items:
                var names = items
                    .Select(item => item is JsonObject o ? o["name"]?.ToString() : item?.ToString())
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();
                return names.Count == 0 ? "—" : string.Join(", ", names);
            case JsonObject location when field == "location":
                return Location(location);
            case JsonObject other:
                return other.ToJsonString();
            case JsonValue flag when flag.TryGetValue<bool>(out var b):
                return b ? "так" : "ні";
            case JsonValue index when field == "orderIndex" && index.TryGetValue<int>(out var i):
                return (i + 1).ToString(CultureInfo.InvariantCulture);
            default:
                var text = value.ToString();
                if (ValueLabels.TryGetValue(text, out var label) && field is "numberingMode" or "type")
                    return label;
                return text.Length == 0 ? "(порожньо)" : text;
        }
    }

    /// <summary>"тур #12, блок #3, позиція 2" — with whatever parts the stored object has.</summary>
    private static string Location(JsonObject location)
    {
        var parts = new List<string>();
        if (location["tourId"] is JsonValue tour)
            parts.Add($"тур #{tour}");
        if (location["blockId"] is JsonValue block)
            parts.Add($"блок #{block}");
        if (location["position"] is JsonValue position && position.TryGetValue<int>(out var p))
            parts.Add($"позиція {(p + 1).ToString(CultureInfo.InvariantCulture)}");
        return parts.Count == 0 ? location.ToJsonString() : string.Join(", ", parts);
    }

    /// <summary>One line describing an added/deleted entity (question text, or tour + question count).</summary>
    public static string Summary(JsonNode? snapshot)
    {
        if (snapshot is not JsonObject entity)
            return "";

        if (entity["questions"] is JsonArray questions)
        {
            var name = (entity["title"] ?? entity["preamble"])?.ToString();
            var count = Plural(questions.Count, "запитання", "запитання", "запитань");
            return string.IsNullOrWhiteSpace(name) ? count : $"{name} ({count})";
        }

        return entity["text"]?.ToString() ?? "";
    }

    /// <summary>The full content of a question snapshot as (label, value) lines, empty fields skipped.</summary>
    public static List<(string Label, string Value)> QuestionLines(JsonNode? snapshot)
    {
        var lines = new List<(string, string)>();
        if (snapshot is not JsonObject question)
            return lines;

        foreach (var field in QuestionFields)
        {
            var node = question[field];
            if (node == null || (node is JsonValue v && v.ToString().Length == 0) || (node is JsonArray a && a.Count == 0))
                continue;
            lines.Add((FieldLabel(field), Value(field, node)));
        }

        return lines;
    }

    /// <summary>
    /// The tour's own content in a tour snapshot (type, number, title, preamble, comment, editors, and
    /// each block with its editors) as (label, value) lines; empty for question snapshots.
    /// </summary>
    public static List<(string Label, string Value)> TourLines(JsonNode? snapshot)
    {
        var lines = new List<(string, string)>();
        if (snapshot is not JsonObject tour || tour["questions"] is not JsonArray)
            return lines;

        void Add(string label, string field, JsonNode? node)
        {
            if (node == null || (node is JsonValue v && v.ToString().Length == 0) || (node is JsonArray a && a.Count == 0))
                return;
            lines.Add((label, Value(field, node)));
        }

        Add("Тип туру", "type", tour["type"]);
        Add("Номер", "number", tour["number"]);
        Add("Назва", "title", tour["title"]);
        Add("Преамбула", "preamble", tour["preamble"]);
        Add("Коментар", "comment", tour["comment"]);
        Add("Редактори", "editors", tour["editors"]);

        if (tour["blocks"] is JsonArray blocks)
        {
            foreach (var block in blocks.OfType<JsonObject>())
            {
                var name = block["name"]?.ToString();
                var editors = Value("editors", block["editors"]);
                var preamble = block["preamble"]?.ToString();
                var description = $"{(string.IsNullOrWhiteSpace(name) ? "без назви" : name)}; редактори: {editors}";
                if (!string.IsNullOrWhiteSpace(preamble))
                    description += $"; преамбула: {preamble}";
                lines.Add(("Блок", description));
            }
        }

        if (tour["questions"] is JsonArray { Count: 0 })
            lines.Add(("Запитання", "немає"));

        return lines;
    }

    /// <summary>The questions inside a tour snapshot (empty for question snapshots).</summary>
    public static List<JsonNode?> TourQuestions(JsonNode? snapshot) =>
        snapshot is JsonObject tour && tour["questions"] is JsonArray questions ? questions.ToList() : [];

    /// <summary>Engine warnings are written for agents in English; known ones are shown in Ukrainian.</summary>
    public static string Warning(string warning)
    {
        var empty = EmptyFieldWarning().Match(warning);
        if (empty.Success)
            return $"Запитання {empty.Groups[1].Value}: поле «{FieldLabel(empty.Groups[2].Value)}» порожнє.";
        if (warning.StartsWith("A new question has empty 'text'", StringComparison.Ordinal))
            return "Нове запитання без тексту.";
        var author = NewAuthorWarning().Match(warning);
        if (author.Success)
            return $"Створено нового автора: {author.Groups[1].Value}.";
        var tag = NewTagWarning().Match(warning);
        if (tag.Success)
            return $"Створено новий тег: {tag.Groups[1].Value}.";
        return warning;
    }

    /// <summary>Ukrainian plural: 1 зміна, 2–4 зміни, 5–20 змін, 21 зміна…</summary>
    public static string Plural(int count, string one, string few, string many)
    {
        var lastTwo = count % 100;
        var last = count % 10;
        var noun = lastTwo is >= 11 and <= 14 ? many : last == 1 ? one : last is >= 2 and <= 4 ? few : many;
        return $"{count.ToString(CultureInfo.InvariantCulture)} {noun}";
    }

    [GeneratedRegex(@"^Question (\d+): '(\w+)' is empty\.$")]
    private static partial Regex EmptyFieldWarning();

    [GeneratedRegex(@"^New author '(.+)' will be created")]
    private static partial Regex NewAuthorWarning();

    [GeneratedRegex(@"^New tag '(.+)' will be created\.$")]
    private static partial Regex NewTagWarning();
}
