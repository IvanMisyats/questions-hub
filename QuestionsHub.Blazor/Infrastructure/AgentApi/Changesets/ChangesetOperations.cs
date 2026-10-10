using System.Globalization;
using System.Text.Json;

namespace QuestionsHub.Blazor.Infrastructure.AgentApi.Changesets;

/// <summary>
/// A changeset request is rejected as a whole; <see cref="OperationIndex"/> points at the
/// operation that failed (null for request-level problems). Messages are shown to agents.
/// </summary>
public sealed class ChangesetValidationException(int? operationIndex, string message) : Exception(message)
{
    public int? OperationIndex { get; } = operationIndex;
}

/// <summary>An author given by id, or by exact first and last name (reused if it exists, created otherwise).</summary>
public sealed record AuthorRef(int? Id, string? FirstName, string? LastName);

/// <summary>Base of every changeset operation; <see cref="Index"/> is its position in the request.</summary>
public abstract record ChangesetOperation(int Index);

public sealed record UpdatePackageOp(int Index, FieldSet Set) : ChangesetOperation(Index);
public sealed record SetPackageEditorsOp(int Index, IReadOnlyList<AuthorRef> Authors) : ChangesetOperation(Index);
public sealed record SetSharedEditorsOp(int Index, bool Value) : ChangesetOperation(Index);
public sealed record SetNumberingModeOp(int Index, string Mode) : ChangesetOperation(Index);
public sealed record SetTagsOp(int Index, IReadOnlyList<string> Tags) : ChangesetOperation(Index);

public sealed record UpdateTourOp(int Index, int TourId, FieldSet Set) : ChangesetOperation(Index);
public sealed record SetTourEditorsOp(int Index, int TourId, IReadOnlyList<AuthorRef> Authors) : ChangesetOperation(Index);

public sealed record UpdateBlockOp(int Index, int BlockId, FieldSet Set) : ChangesetOperation(Index);
public sealed record SetBlockEditorsOp(int Index, int BlockId, IReadOnlyList<AuthorRef> Authors) : ChangesetOperation(Index);

public sealed record UpdateQuestionOp(int Index, int QuestionId, FieldSet Set) : ChangesetOperation(Index);
public sealed record SetQuestionAuthorsOp(int Index, int QuestionId, IReadOnlyList<AuthorRef> Authors) : ChangesetOperation(Index);

// Structural operations. Positions are 0-based within the target container (a tour's questions
// outside blocks, or one block); null appends. Entities created in a changeset cannot be referenced
// by later operations of the same changeset (they have no id yet) — addTour takes its questions inline.

/// <param name="Authors">Null prefills like the editor (block editors, else tour editors, else shared package editors).</param>
public sealed record AddQuestionOp(int Index, int TourId, int? BlockId, int? Position, FieldSet? Set, IReadOnlyList<AuthorRef>? Authors)
    : ChangesetOperation(Index);
public sealed record DeleteQuestionOp(int Index, int QuestionId) : ChangesetOperation(Index);
public sealed record MoveQuestionOp(int Index, int QuestionId, int TourId, int? BlockId, int? Position) : ChangesetOperation(Index);

/// <summary>A question created inline by <see cref="AddTourOp"/>.</summary>
public sealed record NewQuestion(FieldSet? Set, IReadOnlyList<AuthorRef>? Authors);

public sealed record AddTourOp(int Index, int? Position, string? Type, FieldSet? Set, IReadOnlyList<AuthorRef>? Editors,
    IReadOnlyList<NewQuestion> Questions) : ChangesetOperation(Index);
public sealed record DeleteTourOp(int Index, int TourId) : ChangesetOperation(Index);
public sealed record MoveTourOp(int Index, int TourId, int Position) : ChangesetOperation(Index);
public sealed record SetTourTypeOp(int Index, int TourId, string Type) : ChangesetOperation(Index);

/// <summary>
/// The <c>set</c> object of an update operation. An absent key leaves the field unchanged; an
/// explicit JSON <c>null</c> clears it. Keys are matched case-insensitively.
/// </summary>
public sealed class FieldSet
{
    private readonly int _operationIndex;
    private readonly Dictionary<string, JsonElement> _values;

    public FieldSet(int operationIndex, Dictionary<string, JsonElement> values)
    {
        _operationIndex = operationIndex;
        _values = new Dictionary<string, JsonElement>(values, StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<string> Keys => _values.Keys;

    /// <summary>Rejects keys outside <paramref name="allowed"/> (catches typos instead of silently ignoring them).</summary>
    public void EnsureOnly(params string[] allowed)
    {
        var unknown = _values.Keys.Where(k => !allowed.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
        {
            throw new ChangesetValidationException(_operationIndex,
                $"Unknown field(s) in 'set': {string.Join(", ", unknown)}. Allowed: {string.Join(", ", allowed)}.");
        }
    }

    public bool Has(string key) => _values.ContainsKey(key);

    /// <summary>The string value, or null for JSON null. Throws for other JSON types.</summary>
    public string? GetString(string key)
    {
        var value = _values[key];
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            _ => throw new ChangesetValidationException(_operationIndex, $"'{key}' must be a string or null.")
        };
    }

    /// <summary>A <c>yyyy-MM-dd</c> date, or null for JSON null.</summary>
    public DateOnly? GetDate(string key)
    {
        var text = GetString(key);
        if (text == null)
            return null;

        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new ChangesetValidationException(_operationIndex, $"'{key}' must be a date in yyyy-MM-dd format.");
    }
}

/// <summary>
/// Parses the <c>operations</c> array of a changeset request into typed operations. Operation
/// names and property names are matched case-insensitively; unknown operations and properties are
/// rejected with the index of the offending operation.
/// </summary>
public static class ChangesetParser
{
    public const int MaxOperations = 200;
    public const int MaxAuthorRefs = 20;
    public const int MaxTags = 30;
    public const int MaxNewTourQuestions = 30;

    /// <summary>Every operation the parser accepts (names are case-insensitive), as documented.</summary>
    public static readonly IReadOnlyList<string> OperationNames =
    [
        "updatePackage", "setPackageEditors", "setSharedEditors", "setNumberingMode", "setTags",
        "updateTour", "setTourEditors", "updateBlock", "setBlockEditors", "updateQuestion", "setQuestionAuthors",
        "addQuestion", "deleteQuestion", "moveQuestion", "addTour", "deleteTour", "moveTour", "setTourType"
    ];

    public static List<ChangesetOperation> Parse(JsonElement operations)
    {
        if (operations.ValueKind != JsonValueKind.Array)
            throw new ChangesetValidationException(null, "'operations' must be an array.");

        var count = operations.GetArrayLength();
        if (count == 0)
            throw new ChangesetValidationException(null, "'operations' must contain at least one operation.");
        if (count > MaxOperations)
            throw new ChangesetValidationException(null, $"At most {MaxOperations} operations per changeset.");

        var result = new List<ChangesetOperation>(count);
        var index = 0;
        foreach (var element in operations.EnumerateArray())
        {
            result.Add(ParseOne(index, element));
            index++;
        }

        return result;
    }

    private static ChangesetOperation ParseOne(int index, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new ChangesetValidationException(index, "Each operation must be a JSON object.");

        var props = new OperationProperties(index, element);
        var op = props.RequiredString("op");

        ChangesetOperation parsed = op.ToLowerInvariant() switch
        {
            "updatepackage" => new UpdatePackageOp(index, props.Set()),
            "setpackageeditors" => new SetPackageEditorsOp(index, props.Authors("authors")),
            "setsharededitors" => new SetSharedEditorsOp(index, props.RequiredBool("value")),
            "setnumberingmode" => new SetNumberingModeOp(index, props.RequiredString("mode")),
            "settags" => new SetTagsOp(index, props.Strings("tags", MaxTags)),
            "updatetour" => new UpdateTourOp(index, props.RequiredInt("tourId"), props.Set()),
            "settoureditors" => new SetTourEditorsOp(index, props.RequiredInt("tourId"), props.Authors("authors")),
            "updateblock" => new UpdateBlockOp(index, props.RequiredInt("blockId"), props.Set()),
            "setblockeditors" => new SetBlockEditorsOp(index, props.RequiredInt("blockId"), props.Authors("authors")),
            "updatequestion" => new UpdateQuestionOp(index, props.RequiredInt("questionId"), props.Set()),
            "setquestionauthors" => new SetQuestionAuthorsOp(index, props.RequiredInt("questionId"), props.Authors("authors")),
            "addquestion" => new AddQuestionOp(index, props.RequiredInt("tourId"), props.OptionalInt("blockId"),
                props.OptionalPosition("position"), props.OptionalSet(), props.OptionalAuthors("authors")),
            "deletequestion" => new DeleteQuestionOp(index, props.RequiredInt("questionId")),
            "movequestion" => new MoveQuestionOp(index, props.RequiredInt("questionId"), props.RequiredInt("tourId"),
                props.OptionalInt("blockId"), props.OptionalPosition("position")),
            "addtour" => new AddTourOp(index, props.OptionalPosition("position"), props.OptionalString("type"),
                props.OptionalSet(), props.OptionalAuthors("editors"), props.NewQuestions("questions")),
            "deletetour" => new DeleteTourOp(index, props.RequiredInt("tourId")),
            "movetour" => new MoveTourOp(index, props.RequiredInt("tourId"), props.OptionalPosition("position")
                ?? throw new ChangesetValidationException(index, "Missing required property 'position'.")),
            "settourtype" => new SetTourTypeOp(index, props.RequiredInt("tourId"), props.RequiredString("type")),
            _ => throw new ChangesetValidationException(index,
                $"Unknown operation '{op}'. Operations: {string.Join(", ", OperationNames)}.")
        };

        props.EnsureAllConsumed();
        return parsed;
    }

    /// <summary>Reads an operation's properties, tracking which were used so leftovers can be rejected.</summary>
    private sealed class OperationProperties
    {
        private readonly int _index;
        private readonly Dictionary<string, JsonElement> _properties = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _consumed = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Where a nested object sits in the operation (e.g. "questions[2]"); null for the operation itself.</summary>
        private readonly string? _path;

        public OperationProperties(int index, JsonElement element, string? path = null)
        {
            _index = index;
            _path = path;
            foreach (var property in element.EnumerateObject())
            {
                if (!_properties.TryAdd(property.Name, property.Value))
                    throw new ChangesetValidationException(index, $"Duplicate property '{property.Name}'.");
            }
        }

        private JsonElement? Take(string name)
        {
            _consumed.Add(name);
            return _properties.TryGetValue(name, out var value) ? value : null;
        }

        private JsonElement Required(string name) =>
            Take(name) ?? throw new ChangesetValidationException(_index, $"Missing required property '{name}'.");

        public string RequiredString(string name)
        {
            var value = Required(name);
            return value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!
                : throw new ChangesetValidationException(_index, $"'{name}' must be a non-empty string.");
        }

        /// <summary>A required entity id (positive integer).</summary>
        public int RequiredInt(string name)
        {
            var value = Required(name);
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number > 0
                ? number
                : throw new ChangesetValidationException(_index, $"'{name}' must be a positive integer id.");
        }

        /// <summary>An optional entity id: absent or null → null.</summary>
        public int? OptionalInt(string name)
        {
            var value = Take(name);
            if (value is null || value.Value.ValueKind == JsonValueKind.Null)
                return null;

            return value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetInt32(out var number) && number > 0
                ? number
                : throw new ChangesetValidationException(_index, $"'{name}' must be a positive integer id or null.");
        }

        /// <summary>An optional 0-based position: absent or null → null (append).</summary>
        public int? OptionalPosition(string name)
        {
            var value = Take(name);
            if (value is null || value.Value.ValueKind == JsonValueKind.Null)
                return null;

            return value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetInt32(out var number) && number >= 0
                ? number
                : throw new ChangesetValidationException(_index, $"'{name}' must be a non-negative integer or null.");
        }

        public string? OptionalString(string name)
        {
            var value = Take(name);
            if (value is null || value.Value.ValueKind == JsonValueKind.Null)
                return null;

            return value.Value.ValueKind == JsonValueKind.String
                ? value.Value.GetString()
                : throw new ChangesetValidationException(_index, $"'{name}' must be a string or null.");
        }

        public FieldSet? OptionalSet() =>
            _properties.ContainsKey("set") ? Set() : Consumed("set", (FieldSet?)null);

        public List<AuthorRef>? OptionalAuthors(string name)
        {
            if (!_properties.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
                return Consumed(name, (List<AuthorRef>?)null);

            return Authors(name);
        }

        /// <summary>The inline questions of addTour: an array of {"set"?, "authors"?} objects.</summary>
        public List<NewQuestion> NewQuestions(string name)
        {
            var value = Take(name);
            if (value is null || value.Value.ValueKind == JsonValueKind.Null)
                return [];
            if (value.Value.ValueKind != JsonValueKind.Array)
                throw new ChangesetValidationException(_index, $"'{name}' must be an array.");
            if (value.Value.GetArrayLength() > MaxNewTourQuestions)
                throw new ChangesetValidationException(_index, $"At most {MaxNewTourQuestions} entries in '{name}'.");

            var result = new List<NewQuestion>();
            foreach (var item in value.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    throw new ChangesetValidationException(_index, $"Each entry of '{name}' must be an object with optional 'set' and 'authors'.");

                var nested = new OperationProperties(_index, item, $"{name}[{result.Count}]");
                result.Add(new NewQuestion(nested.OptionalSet(), nested.OptionalAuthors("authors")));
                nested.EnsureAllConsumed();
            }

            return result;
        }

        private T Consumed<T>(string name, T value)
        {
            _consumed.Add(name);
            return value;
        }

        public bool RequiredBool(string name)
        {
            var value = Required(name);
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new ChangesetValidationException(_index, $"'{name}' must be true or false.")
            };
        }

        public FieldSet Set()
        {
            var value = Required("set");
            if (value.ValueKind != JsonValueKind.Object)
                throw new ChangesetValidationException(_index, "'set' must be an object.");

            var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!values.TryAdd(property.Name, property.Value.Clone()))
                    throw new ChangesetValidationException(_index, $"Duplicate field '{property.Name}' in 'set'.");
            }

            if (values.Count == 0)
                throw new ChangesetValidationException(_index, "'set' must contain at least one field.");

            return new FieldSet(_index, values);
        }

        public List<string> Strings(string name, int max)
        {
            var value = Required(name);
            if (value.ValueKind != JsonValueKind.Array)
                throw new ChangesetValidationException(_index, $"'{name}' must be an array of strings.");
            if (value.GetArrayLength() > max)
                throw new ChangesetValidationException(_index, $"At most {max} entries in '{name}'.");

            return value.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String
                    ? item.GetString()!
                    : throw new ChangesetValidationException(_index, $"'{name}' must be an array of strings."))
                .ToList();
        }

        public List<AuthorRef> Authors(string name)
        {
            var value = Required(name);
            if (value.ValueKind != JsonValueKind.Array)
                throw new ChangesetValidationException(_index, $"'{name}' must be an array.");
            if (value.GetArrayLength() > MaxAuthorRefs)
                throw new ChangesetValidationException(_index, $"At most {MaxAuthorRefs} entries in '{name}'.");

            return value.EnumerateArray().Select(ParseAuthor).ToList();
        }

        private AuthorRef ParseAuthor(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new ChangesetValidationException(_index, "Each author must be an object: {\"id\": n} or {\"firstName\", \"lastName\"}.");

            int? id = null;
            string? firstName = null, lastName = null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in item.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                    throw new ChangesetValidationException(_index, $"Duplicate author property '{property.Name}'.");

                switch (property.Name.ToLowerInvariant())
                {
                    case "id" when property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var parsed):
                        id = parsed;
                        break;
                    case "firstname" when property.Value.ValueKind == JsonValueKind.String:
                        firstName = property.Value.GetString();
                        break;
                    case "lastname" when property.Value.ValueKind == JsonValueKind.String:
                        lastName = property.Value.GetString();
                        break;
                    default:
                        throw new ChangesetValidationException(_index, $"Unexpected author property '{property.Name}'.");
                }
            }

            var byId = id != null;
            var byName = firstName != null || lastName != null;
            if (byId == byName)
                throw new ChangesetValidationException(_index, "An author is either {\"id\": n} or {\"firstName\", \"lastName\"}.");

            return new AuthorRef(id, firstName, lastName);
        }

        public void EnsureAllConsumed()
        {
            // Every property the operation reads was requested by now, so _consumed is its full list
            var unknown = _properties.Keys.Where(k => !_consumed.Contains(k)).ToList();
            if (unknown.Count > 0)
            {
                var known = string.Join(", ", _consumed.Where(k => !k.Equals("op", StringComparison.OrdinalIgnoreCase)));
                throw new ChangesetValidationException(_index, _path == null
                    ? $"Unknown property(ies): {string.Join(", ", unknown)}. This operation takes: {known}."
                    : $"Unknown property(ies) in '{_path}': {string.Join(", ", unknown)}. It takes: {known}.");
            }
        }
    }
}
