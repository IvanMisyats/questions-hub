using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.AgentApi.Changesets;

/// <summary>Kinds of entities a change can target (the <c>entity</c> value in diffs).</summary>
public static class ChangeEntities
{
    public const string Package = "package";
    public const string Tour = "tour";
    public const string Block = "block";
    public const string Question = "question";
}

/// <summary>
/// One entry of a changeset diff, holding a reference to the changed entity rather than its id:
/// entities created by the changeset only get their database id when it is saved, so the id is
/// read when the record is turned into a <see cref="ChangeDto"/> (null while unsaved, e.g. dry run).
/// </summary>
public sealed class ChangeRecord
{
    /// <summary>The operation that caused the change; null for side effects such as renumbering.</summary>
    public required int? OperationIndex { get; init; }

    public required string Entity { get; init; }

    /// <summary>The changed <see cref="Package"/>, <see cref="Tour"/>, <see cref="Block"/> or <see cref="Question"/>.</summary>
    public required object Target { get; init; }

    /// <summary>The changed field (camelCase API name); null for added/deleted entities.</summary>
    public string? Field { get; init; }

    /// <summary>Old value: a string, int, bool, date, or a list of <see cref="Author"/>/<see cref="Tag"/>.</summary>
    public object? Before { get; init; }

    public object? After { get; init; }

    /// <summary>"added" or "deleted"; null for a field change.</summary>
    public string? Kind { get; init; }

    /// <summary>
    /// Label fixed when the target was deleted (set on every record of an entity that ends up deleted);
    /// otherwise computed from the final state.
    /// </summary>
    public string? FixedLabel { get; set; }

    /// <summary>
    /// Full content of a deleted entity (for recovery), frozen at deletion time but rendered at
    /// formatting time, so authors created by the same changeset get their real ids once saved.
    /// </summary>
    public Func<Func<object, int, int?>, JsonNode>? Snapshot { get; init; }
}

/// <summary>A diff entry as returned to the agent and stored in the audit log.</summary>
/// <param name="Id">Database id of the entity; null for entities created by an unsaved (dry-run) changeset.</param>
public record ChangeDto(
    int? OperationIndex,
    string Entity,
    int? Id,
    string Label,
    string? Field,
    JsonNode? Before,
    JsonNode? After,
    string? Kind,
    JsonNode? Snapshot);

/// <summary>Turns <see cref="ChangeRecord"/>s into <see cref="ChangeDto"/>s with human-readable labels.</summary>
public static class ChangeFormatter
{
    /// <summary>
    /// Builds the diff. Entities still in the <see cref="EntityState.Added"/> state (dry run: never
    /// saved) are reported without an id — providers differ in whether they pre-assign one.
    /// </summary>
    public static List<ChangeDto> ToDtos(IEnumerable<ChangeRecord> records, Package package, DbContext context)
    {
        int? Id(object entity, int id) => id > 0 && context.Entry(entity).State != EntityState.Added ? id : null;

        return records.Select(r => new ChangeDto(
                r.OperationIndex,
                r.Entity,
                IdOf(r.Target, Id),
                r.FixedLabel ?? Label(r.Target, package),
                r.Field,
                ToNode(r.Before, Id),
                ToNode(r.After, Id),
                r.Kind,
                // Added entities: their final state (with ids once saved); deleted: as they were.
                r.Kind == "added" ? EntitySnapshots.Of(r.Target, package, Id) : r.Snapshot?.Invoke(Id)))
            .ToList();
    }

    /// <summary>"Тур 2, запитання 14" / "Тема 3, 40" / "Тур 1, блок «Назва»" / "Пакет".</summary>
    public static string Label(object target, Package package)
    {
        var shvager = package.Type == PackageType.Shvager;
        return target switch
        {
            Package => "Пакет",
            Tour tour => TourLabel(tour, shvager),
            Block block => $"{TourLabel(TourOf(block, package), shvager)}, блок {BlockName(block)}",
            Question question => shvager
                ? $"{TourLabel(TourOf(question, package), shvager)}, {question.Number}"
                : $"{TourLabel(TourOf(question, package), shvager)}, запитання {question.Number}",
            _ => target.GetType().Name
        };
    }

    private static string TourLabel(Tour? tour, bool shvager) => tour == null
        ? "?"
        : (shvager ? "Тема " : "Тур ") + tour.Number;

    private static string BlockName(Block block) =>
        string.IsNullOrWhiteSpace(block.Name) ? $"№{block.OrderIndex + 1}" : $"«{block.Name}»";

    private static Tour? TourOf(Block block, Package package) =>
        package.Tours.FirstOrDefault(t => t.Blocks.Contains(block));

    private static Tour? TourOf(Question question, Package package) =>
        package.Tours.FirstOrDefault(t => t.Questions.Contains(question));

    private static int? IdOf(object target, Func<object, int, int?> id) => target switch
    {
        Package p => id(p, p.Id),
        Tour t => id(t, t.Id),
        Block b => id(b, b.Id),
        Question q => id(q, q.Id),
        _ => null
    };

    private static JsonNode? ToNode(object? value, Func<object, int, int?> id) => value switch
    {
        null => null,
        JsonNode node => node.DeepClone(),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        DateOnly d => JsonValue.Create(d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
        IEnumerable<Author> authors => new JsonArray(authors
            .Select(a => (JsonNode)new JsonObject
            {
                ["id"] = id(a, a.Id),
                ["name"] = a.FullName
            })
            .ToArray()),
        IEnumerable<Tag> tags => new JsonArray(tags
            .Select(t => (JsonNode)new JsonObject
            {
                ["id"] = id(t, t.Id),
                ["name"] = t.Name
            })
            .ToArray()),
        _ => JsonValue.Create(value.ToString())
    };
}

/// <summary>
/// Full JSON content of questions and tours, stored for deleted entities (enough to restore them)
/// and returned for added ones.
/// </summary>
public static class EntitySnapshots
{
    /// <summary>Id resolver for entities that already exist in the database.</summary>
    public static int? ExistingId(object entity, int id) => id > 0 ? id : null;

    /// <summary>
    /// Freezes a question that is about to be deleted. Its content is copied now; author ids are
    /// resolved when rendered (an author created by this changeset has no id until it is saved).
    /// </summary>
    public static Func<Func<object, int, int?>, JsonNode> FreezeQuestion(Question question, Tour tour)
    {
        var copy = CopyQuestion(question);
        var tourCopy = new Tour { Id = tour.Id, Number = tour.Number };
        return id => Question(copy, tourCopy, Frozen(id));
    }

    /// <summary>Freezes a tour that is about to be deleted, with its blocks and the given questions.</summary>
    public static Func<Func<object, int, int?>, JsonNode> FreezeTour(Tour tour, IEnumerable<Question> questions)
    {
        var copy = new Tour
        {
            Id = tour.Id,
            OrderIndex = tour.OrderIndex,
            Number = tour.Number,
            Type = tour.Type,
            Title = tour.Title,
            Preamble = tour.Preamble,
            Comment = tour.Comment,
            Editors = tour.Editors.ToList(),
            Blocks = tour.Blocks.Select(b => new Block
            {
                Id = b.Id, OrderIndex = b.OrderIndex, Name = b.Name, Preamble = b.Preamble, Editors = b.Editors.ToList()
            }).ToList(),
            Questions = questions.Select(CopyQuestion).ToList()
        };
        return id => Tour(copy, Frozen(id));
    }

    /// <summary>The frozen copies are detached; only their authors are live entities whose ids may be pending.</summary>
    private static Func<object, int, int?> Frozen(Func<object, int, int?> id) =>
        (entity, value) => entity is Author ? id(entity, value) : ExistingId(entity, value);

    private static Question CopyQuestion(Question q) => new()
    {
        Id = q.Id,
        TourId = q.TourId,
        BlockId = q.BlockId,
        OrderIndex = q.OrderIndex,
        Number = q.Number,
        HostInstructions = q.HostInstructions,
        Text = q.Text,
        HandoutText = q.HandoutText,
        HandoutUrl = q.HandoutUrl,
        Answer = q.Answer,
        AcceptedAnswers = q.AcceptedAnswers,
        RejectedAnswers = q.RejectedAnswers,
        AnswerForm = q.AnswerForm,
        Comment = q.Comment,
        CommentAttachmentUrl = q.CommentAttachmentUrl,
        Source = q.Source,
        Authors = q.Authors.ToList()
    };

    public static JsonObject? Of(object target, Package package, Func<object, int, int?> id) => target switch
    {
        Question question => Question(question, package.Tours.FirstOrDefault(t => t.Questions.Contains(question)), id),
        Tour tour => Tour(tour, id),
        _ => null
    };

    public static JsonObject Question(Question question, Tour? tour, Func<object, int, int?> id) => new()
    {
        ["id"] = id(question, question.Id),
        ["tourId"] = tour != null ? id(tour, tour.Id) : question.TourId,
        ["blockId"] = question.BlockId,
        ["orderIndex"] = question.OrderIndex,
        ["number"] = question.Number,
        ["hostInstructions"] = question.HostInstructions,
        ["text"] = question.Text,
        ["handoutText"] = question.HandoutText,
        ["handoutUrl"] = question.HandoutUrl,
        ["answer"] = question.Answer,
        ["acceptedAnswers"] = question.AcceptedAnswers,
        ["rejectedAnswers"] = question.RejectedAnswers,
        ["answerForm"] = question.AnswerForm,
        ["comment"] = question.Comment,
        ["commentAttachmentUrl"] = question.CommentAttachmentUrl,
        ["source"] = question.Source,
        ["authors"] = Authors(question.Authors, id)
    };

    public static JsonObject Tour(Tour tour, Func<object, int, int?> id) => new()
    {
        ["id"] = id(tour, tour.Id),
        ["orderIndex"] = tour.OrderIndex,
        ["number"] = tour.Number,
        ["type"] = AgentApi.AgentApiNames.Of(tour.Type),
        ["title"] = tour.Title,
        ["preamble"] = tour.Preamble,
        ["comment"] = tour.Comment,
        ["editors"] = Authors(tour.Editors, id),
        ["blocks"] = new JsonArray(tour.Blocks.OrderBy(b => b.OrderIndex).Select(b => (JsonNode)new JsonObject
        {
            ["id"] = id(b, b.Id),
            ["orderIndex"] = b.OrderIndex,
            ["name"] = b.Name,
            ["preamble"] = b.Preamble,
            ["editors"] = Authors(b.Editors, id)
        }).ToArray()),
        ["questions"] = new JsonArray(tour.Questions.OrderBy(q => q.OrderIndex)
            .Select(q => (JsonNode)Question(q, tour, id)).ToArray())
    };

    private static JsonArray Authors(IEnumerable<Author> authors, Func<object, int, int?> id) =>
        new(authors.OrderBy(a => a.LastName).ThenBy(a => a.FirstName)
            .Select(a => (JsonNode)new JsonObject { ["id"] = id(a, a.Id), ["name"] = a.FullName }).ToArray());
}
