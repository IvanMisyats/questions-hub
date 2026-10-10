using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Utils;

namespace QuestionsHub.Blazor.Infrastructure.AgentApi.Changesets;

/// <summary>
/// Applies changeset operations to a package graph that is <b>tracked</b> by <paramref name="context"/>
/// (loaded with <see cref="PackageGraph.WithEditableGraph"/>), recording a diff of every change.
/// Mirrors the package editor's save rules (normalization, Своя гра cascades, numbering).
/// The engine never saves: new entities are only added to the context, so discarding the context
/// (dry run) or saving it (apply) is the caller's decision.
/// </summary>
/// <param name="hasResults">
/// Tournament results are attached to the package. Their statistics are mapped to questions by
/// position and tour type, so structural operations are rejected for such packages.
/// </param>
public sealed class ChangesetEngine(
    QuestionsHubDbContext context,
    Package package,
    PackageRenumberingService renumbering,
    bool hasResults = false)
{
    private readonly List<ChangeRecord> _changes = [];
    private readonly List<string> _warnings = [];

    // Changeset-local identity maps: a query cannot see entities added but not yet saved.
    private readonly Dictionary<int, Author> _authorsById = [];
    private readonly Dictionary<(string First, string Last), Author> _authorsByName = [];
    private readonly Dictionary<string, Tag> _tagsByName = [];

    private bool _renumber;

    // Entities created by this changeset: reported as "added" and never addressable by id (they
    // have none yet). While filling them in, field changes are not recorded individually.
    private readonly HashSet<object> _added = new(ReferenceEqualityComparer.Instance);
    private bool _silent;

    private bool IsShvager => package.Type == PackageType.Shvager;

    public IReadOnlyList<ChangeRecord> Changes => _changes;
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Title, editors, shared editors or tags changed — cached package lists are stale.</summary>
    public bool PackageListAffected { get; private set; }

    /// <summary>Tags changed — the popular-tags cache is stale.</summary>
    public bool TagsChanged { get; private set; }

    /// <summary>
    /// Applies <paramref name="operations"/> in order, then renumbers (when needed) and recounts
    /// questions. Throws <see cref="ChangesetValidationException"/> on the first invalid operation;
    /// the graph may then be partially modified and must be discarded.
    /// </summary>
    public async Task Apply(IReadOnlyList<ChangesetOperation> operations, CancellationToken ct = default)
    {
        foreach (var operation in operations)
            await ApplyOne(operation, ct);

        Finish();
    }

    private Task ApplyOne(ChangesetOperation operation, CancellationToken ct) => operation switch
    {
        UpdatePackageOp op => Done(() => UpdatePackage(op)),
        SetPackageEditorsOp op => SetPackageEditors(op, ct),
        SetSharedEditorsOp op => Done(() => SetSharedEditors(op)),
        SetNumberingModeOp op => Done(() => SetNumberingMode(op)),
        SetTagsOp op => SetTags(op, ct),
        UpdateTourOp op => Done(() => UpdateTour(op)),
        SetTourEditorsOp op => SetTourEditors(op, ct),
        UpdateBlockOp op => Done(() => UpdateBlock(op)),
        SetBlockEditorsOp op => SetBlockEditors(op, ct),
        UpdateQuestionOp op => Done(() => UpdateQuestion(op)),
        SetQuestionAuthorsOp op => SetQuestionAuthors(op, ct),
        AddQuestionOp op => AddQuestion(op, ct),
        DeleteQuestionOp op => Done(() => DeleteQuestion(op)),
        MoveQuestionOp op => Done(() => MoveQuestion(op)),
        AddTourOp op => AddTour(op, ct),
        DeleteTourOp op => Done(() => DeleteTour(op)),
        MoveTourOp op => Done(() => MoveTour(op)),
        SetTourTypeOp op => Done(() => SetTourType(op)),
        _ => throw new ChangesetValidationException(operation.Index, $"Operation {operation.GetType().Name} is not supported.")
    };

    private static Task Done(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    #region Package

    private void UpdatePackage(UpdatePackageOp op)
    {
        op.Set.EnsureOnly("title", "description", "preamble", "playedFrom", "playedTo");

        if (op.Set.Has("title"))
        {
            var title = op.Set.GetString("title")?.Trim();
            if (string.IsNullOrEmpty(title))
                throw new ChangesetValidationException(op.Index, "'title' cannot be empty.");
            CheckLength<Package>(nameof(Package.Title), title, op.Index, "title");
            if (SetField(op.Index, ChangeEntities.Package, package, "title", package.Title, title, v => package.Title = v!))
                PackageListAffected = true;
        }

        if (op.Set.Has("description"))
        {
            var value = OptionalTrimmed(op.Set.GetString("description"));
            CheckLength<Package>(nameof(Package.Description), value, op.Index, "description");
            SetField(op.Index, ChangeEntities.Package, package, "description", package.Description, value, v => package.Description = v);
        }

        if (op.Set.Has("preamble"))
        {
            var value = OptionalTrimmed(op.Set.GetString("preamble"));
            SetField(op.Index, ChangeEntities.Package, package, "preamble", package.Preamble, value, v => package.Preamble = v);
        }

        if (op.Set.Has("playedFrom"))
            SetValue(op.Index, ChangeEntities.Package, package, "playedFrom", package.PlayedFrom, op.Set.GetDate("playedFrom"), v => package.PlayedFrom = v);

        if (op.Set.Has("playedTo"))
            SetValue(op.Index, ChangeEntities.Package, package, "playedTo", package.PlayedTo, op.Set.GetDate("playedTo"), v => package.PlayedTo = v);
    }

    private async Task SetPackageEditors(SetPackageEditorsOp op, CancellationToken ct)
    {
        var editors = await ResolveAuthors(op.Authors, op.Index, ct);
        if (SetAuthors(op.Index, ChangeEntities.Package, package, "editors", package.PackageEditors, editors))
            PackageListAffected = true;

        // Своя гра: saving the package editors fills every question still without an author whose
        // theme and block have no editors of their own (editor: CascadePackageEditorsToEmptyQuestions) —
        // on every save, even when the editor list itself did not change.
        if (IsShvager && editors.Count > 0)
        {
            foreach (var tour in package.Tours.Where(t => t.Editors.Count == 0))
            {
                foreach (var question in tour.Questions.Where(q => q.Authors.Count == 0))
                {
                    var block = question.BlockId is { } blockId ? tour.Blocks.FirstOrDefault(b => b.Id == blockId) : null;
                    if (block is { Editors.Count: > 0 })
                        continue;

                    SetAuthors(op.Index, ChangeEntities.Question, question, "authors", question.Authors, editors);
                }
            }
        }
    }

    private void SetSharedEditors(SetSharedEditorsOp op)
    {
        if (SetValue(op.Index, ChangeEntities.Package, package, "sharedEditors", package.SharedEditors, op.Value, v => package.SharedEditors = v))
            PackageListAffected = true;

        // Turning shared editors on with none set copies the editors in use (editor: OnSharedEditorsChanged,
        // which does so whenever the box is ticked); a tour with blocks contributes its block editors, as
        // Tour.AllEditors does. Deduplicated by instance: authors added by this changeset have no id yet.
        if (op.Value && package.PackageEditors.Count == 0)
        {
            var inUse = package.Tours
                .SelectMany(t => t.Blocks.Count > 0 ? t.Blocks.SelectMany(b => b.Editors) : t.Editors)
                .Distinct()
                .ToList();
            if (SetAuthors(op.Index, ChangeEntities.Package, package, "editors", package.PackageEditors, inUse))
                PackageListAffected = true;
        }
    }

    private void SetNumberingMode(SetNumberingModeOp op)
    {
        if (IsShvager)
            throw new ChangesetValidationException(op.Index, "Своя гра packages have no numbering mode: values follow theme positions.");
        if (!AgentApiNames.TryParse(op.Mode, out QuestionNumberingMode mode))
            throw new ChangesetValidationException(op.Index, "'mode' must be global, perTour or manual.");

        SetValue(op.Index, ChangeEntities.Package, package, "numberingMode",
            AgentApiNames.Of(package.NumberingMode), AgentApiNames.Of(mode), _ => package.NumberingMode = mode);

        // The editor renumbers on every mode selection, which also repairs inconsistent numbers.
        _renumber = true;
    }

    private async Task SetTags(SetTagsOp op, CancellationToken ct)
    {
        var tags = new List<Tag>();
        foreach (var raw in op.Tags)
        {
            var name = raw.Trim();
            if (name.Length == 0)
                throw new ChangesetValidationException(op.Index, "Tag names cannot be empty.");
            CheckLength<Tag>(nameof(Tag.Name), name, op.Index, "tags");

            var tag = await ResolveTag(name, ct);
            if (!tags.Contains(tag))
                tags.Add(tag);
        }

        var before = package.Tags.ToList();
        if (SameSet(before, tags))
            return;

        ReplaceCollection(package.Tags, tags);
        _changes.Add(new ChangeRecord
        {
            OperationIndex = op.Index, Entity = ChangeEntities.Package, Target = package,
            Field = "tags", Before = before, After = tags.ToList()
        });
        TagsChanged = true;
        PackageListAffected = true;
    }

    #endregion

    #region Tours and blocks

    private void UpdateTour(UpdateTourOp op) => ApplyTourFields(FindTour(op.TourId, op.Index), op.Set, op.Index);

    private void ApplyTourFields(Tour tour, FieldSet fields, int opIndex)
    {
        fields.EnsureOnly("title", "preamble", "comment");

        if (fields.Has("title"))
        {
            var title = OptionalOf(TextNormalizer.Normalize(fields.GetString("title")));
            if (title != null && !IsShvager)
                throw new ChangesetValidationException(opIndex, "'title' is a Своя гра theme name; Що?Де?Коли? tours have no title.");
            CheckLength<Tour>(nameof(Tour.Title), title, opIndex, "title");
            SetField(opIndex, ChangeEntities.Tour, tour, "title", tour.Title, title, v => tour.Title = v);
        }

        if (fields.Has("preamble"))
        {
            var value = OptionalTrimmed(fields.GetString("preamble"));
            SetField(opIndex, ChangeEntities.Tour, tour, "preamble", tour.Preamble, value, v => tour.Preamble = v);
        }

        if (fields.Has("comment"))
        {
            var value = OptionalTrimmed(fields.GetString("comment"));
            CheckLength<Tour>(nameof(Tour.Comment), value, opIndex, "comment");
            SetField(opIndex, ChangeEntities.Tour, tour, "comment", tour.Comment, value, v => tour.Comment = v);
        }
    }

    private async Task SetTourEditors(SetTourEditorsOp op, CancellationToken ct)
    {
        var tour = FindTour(op.TourId, op.Index);
        var editors = await ResolveAuthors(op.Authors, op.Index, ct);
        var hadNoEditors = tour.Editors.Count == 0;

        if (!SetAuthors(op.Index, ChangeEntities.Tour, tour, "editors", tour.Editors, editors))
            return;

        PackageListAffected = true;

        // Своя гра: a theme that had no author until now fills its author-less questions
        // (editor: CascadeThemeAuthorsToEmptyQuestions).
        if (IsShvager && hadNoEditors && editors.Count > 0)
        {
            foreach (var question in tour.Questions.Where(q => q.Authors.Count == 0))
                SetAuthors(op.Index, ChangeEntities.Question, question, "authors", question.Authors, editors);
        }
    }

    private void UpdateBlock(UpdateBlockOp op)
    {
        var block = FindBlock(op.BlockId, op.Index);
        op.Set.EnsureOnly("name", "preamble");

        if (op.Set.Has("name"))
        {
            var value = OptionalTrimmed(op.Set.GetString("name"));
            CheckLength<Block>(nameof(Block.Name), value, op.Index, "name");
            SetField(op.Index, ChangeEntities.Block, block, "name", block.Name, value, v => block.Name = v);
        }

        if (op.Set.Has("preamble"))
        {
            var value = OptionalTrimmed(op.Set.GetString("preamble"));
            SetField(op.Index, ChangeEntities.Block, block, "preamble", block.Preamble, value, v => block.Preamble = v);
        }
    }

    private async Task SetBlockEditors(SetBlockEditorsOp op, CancellationToken ct)
    {
        var block = FindBlock(op.BlockId, op.Index);
        var editors = await ResolveAuthors(op.Authors, op.Index, ct);
        if (SetAuthors(op.Index, ChangeEntities.Block, block, "editors", block.Editors, editors))
            PackageListAffected = true;
    }

    #endregion

    #region Questions

    private void UpdateQuestion(UpdateQuestionOp op) => ApplyQuestionFields(FindQuestion(op.QuestionId, op.Index), op.Set, op.Index);

    private void ApplyQuestionFields(Question question, FieldSet fields, int opIndex)
    {
        fields.EnsureOnly("number", "hostInstructions", "text", "handoutText", "answer",
            "acceptedAnswers", "rejectedAnswers", "answerForm", "comment", "source");

        void Required(string key, Func<string> get, Action<string> set)
        {
            if (!fields.Has(key))
                return;

            var value = TextNormalizer.Normalize(fields.GetString(key))
                ?? throw new ChangesetValidationException(opIndex, $"'{key}' cannot be null (use \"\" to empty it).");
            CheckLength<Question>(char.ToUpperInvariant(key[0]) + key[1..], value, opIndex, key);
            SetField(opIndex, ChangeEntities.Question, question, key, get(), value, v => set(v!));
            if (value.Length == 0)
                _warnings.Add($"Question {question.Id}: '{key}' is empty.");
        }

        void Optional(string key, Func<string?> get, Action<string?> set, Func<string?, string?> normalize)
        {
            if (!fields.Has(key))
                return;

            var value = OptionalOf(normalize(fields.GetString(key)));
            CheckLength<Question>(char.ToUpperInvariant(key[0]) + key[1..], value, opIndex, key);
            SetField(opIndex, ChangeEntities.Question, question, key, get(), value, set);
        }

        if (fields.Has("number"))
        {
            // The editor only lets numbers be typed in Manual mode; elsewhere they follow positions.
            if (IsShvager || package.NumberingMode != QuestionNumberingMode.Manual)
            {
                throw new ChangesetValidationException(opIndex,
                    "'number' can only be set in Що?Де?Коли? packages with manual numbering (setNumberingMode \"manual\" first).");
            }

            // Trimmed like the editor, which also accepts an empty number in Manual mode.
            var number = fields.GetString("number")?.Trim()
                ?? throw new ChangesetValidationException(opIndex, "'number' cannot be null (use an empty string to empty it).");
            CheckLength<Question>(nameof(Question.Number), number, opIndex, "number");
            SetField(opIndex, ChangeEntities.Question, question, "number", question.Number, number, v => question.Number = v!);
        }

        if (fields.Has("hostInstructions") && IsShvager && fields.GetString("hostInstructions") != null)
            throw new ChangesetValidationException(opIndex, "Своя гра questions have no host instructions.");
        if (fields.Has("answerForm") && !IsShvager && fields.GetString("answerForm") != null)
            throw new ChangesetValidationException(opIndex, "'answerForm' («Форма») is a Своя гра field.");

        Required("text", () => question.Text, v => question.Text = v);
        Required("answer", () => question.Answer, v => question.Answer = v);
        Optional("hostInstructions", () => question.HostInstructions, v => question.HostInstructions = v, TextNormalizer.Normalize);
        Optional("handoutText", () => question.HandoutText, v => question.HandoutText = v, TextNormalizer.Normalize);
        Optional("acceptedAnswers", () => question.AcceptedAnswers, v => question.AcceptedAnswers = v, TextNormalizer.Normalize);
        Optional("rejectedAnswers", () => question.RejectedAnswers, v => question.RejectedAnswers = v, TextNormalizer.Normalize);
        Optional("answerForm", () => question.AnswerForm, v => question.AnswerForm = v, TextNormalizer.Normalize);
        Optional("comment", () => question.Comment, v => question.Comment = v, TextNormalizer.Normalize);
        // Sources may hold URLs whose apostrophes are intentional (editor: NormalizeExcludingApostrophes).
        Optional("source", () => question.Source, v => question.Source = v, TextNormalizer.NormalizeExcludingApostrophes);
    }

    private async Task SetQuestionAuthors(SetQuestionAuthorsOp op, CancellationToken ct)
    {
        var question = FindQuestion(op.QuestionId, op.Index);
        var authors = await ResolveAuthors(op.Authors, op.Index, ct);
        SetAuthors(op.Index, ChangeEntities.Question, question, "authors", question.Authors, authors);
    }

    #endregion

    #region Structure

    private void RequireNoResults(int opIndex)
    {
        if (hasResults)
        {
            throw new ChangesetValidationException(opIndex,
                "Tournament results are attached to this package: structural changes (adding, deleting or moving " +
                "questions and tours, tour types) would invalidate the per-question statistics. Make them in the editor.");
        }
    }

    private async Task AddQuestion(AddQuestionOp op, CancellationToken ct)
    {
        RequireNoResults(op.Index);
        var tour = FindTour(op.TourId, op.Index);
        var block = TargetBlock(tour, op.BlockId, op.Index);

        var question = await CreateQuestion(tour, block, op.Set, op.Authors, op.Index, ct);
        Insert(tour, block, question, op.Position, op.Index);
        _renumber = true;
    }

    private void DeleteQuestion(DeleteQuestionOp op)
    {
        RequireNoResults(op.Index);
        var question = FindQuestion(op.QuestionId, op.Index);
        var tour = TourOf(question);

        var label = ChangeFormatter.Label(question, package);
        FixLabels(question, label);
        _changes.Add(new ChangeRecord
        {
            OperationIndex = op.Index, Entity = ChangeEntities.Question, Target = question, Kind = "deleted",
            FixedLabel = label,
            Snapshot = EntitySnapshots.FreezeQuestion(question, tour)
        });

        var siblings = Container(tour, question.BlockId).Where(q => q != question).ToList();
        tour.Questions.Remove(question);
        context.Questions.Remove(question);
        Reindex(siblings, op.Index);
        _renumber = true;
    }

    private void MoveQuestion(MoveQuestionOp op)
    {
        RequireNoResults(op.Index);
        var question = FindQuestion(op.QuestionId, op.Index);
        var source = TourOf(question);
        var target = FindTour(op.TourId, op.Index);
        var block = TargetBlock(target, op.BlockId, op.Index);
        var before = Location(question, source);

        Reindex(Container(source, question.BlockId).Where(q => q != question).ToList(), op.Index);
        if (source != target)
        {
            source.Questions.Remove(question);
            target.Questions.Add(question);
            question.Tour = target;
            question.TourId = target.Id;
        }

        question.Block = block;
        question.BlockId = block?.Id;
        Insert(target, block, question, op.Position, op.Index);

        Record(op.Index, ChangeEntities.Question, question, "location", before, Location(question, target));
        _renumber = true;
    }

    private async Task AddTour(AddTourOp op, CancellationToken ct)
    {
        RequireNoResults(op.Index);
        var type = ParseTourType(op.Type ?? AgentApiNames.Of(TourType.Regular), op.Index);
        if (type != TourType.Regular && package.Tours.Any(t => t.Type == type))
        {
            throw new ChangesetValidationException(op.Index,
                $"The package already has a {AgentApiNames.Of(type)} tour; use setTourType to change which tour it is.");
        }

        var tour = new Tour { Number = "0", Type = type, Package = package, PackageId = package.Id };
        package.Tours.Add(tour);
        context.Tours.Add(tour);
        _added.Add(tour);

        using (Silently())
        {
            if (op.Set != null)
                ApplyTourFields(tour, op.Set, op.Index);
            if (op.Editors != null)
                SetAuthors(op.Index, ChangeEntities.Tour, tour, "editors", tour.Editors, await ResolveAuthors(op.Editors, op.Index, ct));
        }

        var others = package.Tours.Where(t => t != tour).OrderBy(t => t.OrderIndex).ThenBy(t => t.Id).ToList();
        others.Insert(Math.Min(op.Position ?? others.Count, others.Count), tour);
        ReindexTours(others, op.Index);

        _changes.Add(new ChangeRecord { OperationIndex = op.Index, Entity = ChangeEntities.Tour, Target = tour, Kind = "added" });
        PackageListAffected = true;

        foreach (var newQuestion in op.Questions)
        {
            var question = await CreateQuestion(tour, null, newQuestion.Set, newQuestion.Authors, op.Index, ct);
            Insert(tour, null, question, null, op.Index);
        }

        _renumber = true;
    }

    private void DeleteTour(DeleteTourOp op)
    {
        RequireNoResults(op.Index);
        var tour = FindTour(op.TourId, op.Index);

        // Questions added to this tour earlier in the changeset never existed: drop them and their
        // records rather than reporting a creation that is not persisted.
        foreach (var added in tour.Questions.Where(_added.Contains).ToList())
        {
            _changes.RemoveAll(c => ReferenceEquals(c.Target, added));
            tour.Questions.Remove(added);
            context.Questions.Remove(added);
        }

        // Earlier records of entities that disappear with the tour keep the labels they had.
        foreach (var question in tour.Questions)
            FixLabels(question, ChangeFormatter.Label(question, package));
        foreach (var block in tour.Blocks)
            FixLabels(block, ChangeFormatter.Label(block, package));
        var label = ChangeFormatter.Label(tour, package);
        FixLabels(tour, label);

        _changes.Add(new ChangeRecord
        {
            OperationIndex = op.Index, Entity = ChangeEntities.Tour, Target = tour, Kind = "deleted",
            FixedLabel = label,
            Snapshot = EntitySnapshots.FreezeTour(tour, tour.Questions)
        });

        // Explicit removal of the children keeps the in-memory graph honest for renumbering;
        // deleted questions keep their media files (as in the editor).
        foreach (var question in tour.Questions.ToList())
            context.Questions.Remove(question);
        foreach (var block in tour.Blocks.ToList())
            context.Blocks.Remove(block);
        package.Tours.Remove(tour);
        context.Tours.Remove(tour);

        ReindexTours(package.Tours.OrderBy(t => t.OrderIndex).ThenBy(t => t.Id).ToList(), op.Index);
        PackageListAffected = true;
        _renumber = true;
    }

    private void MoveTour(MoveTourOp op)
    {
        RequireNoResults(op.Index);
        var tour = FindTour(op.TourId, op.Index);

        var others = package.Tours.Where(t => t != tour).OrderBy(t => t.OrderIndex).ThenBy(t => t.Id).ToList();
        others.Insert(Math.Min(op.Position, others.Count), tour);
        ReindexTours(others, op.Index);
        _renumber = true;
    }

    /// <summary>
    /// Mirrors the editor's OnTourTypeChanged: at most one warmup and one shootout (another tour of the
    /// same type becomes regular); renumbering then puts the warmup first and the shootout last.
    /// </summary>
    private void SetTourType(SetTourTypeOp op)
    {
        RequireNoResults(op.Index);
        var tour = FindTour(op.TourId, op.Index);
        var type = ParseTourType(op.Type, op.Index);

        if (type != TourType.Regular)
        {
            foreach (var other in package.Tours.Where(t => t != tour && t.Type == type))
            {
                SetValue(op.Index, ChangeEntities.Tour, other, "type", AgentApiNames.Of(other.Type),
                    AgentApiNames.Of(TourType.Regular), _ => other.Type = TourType.Regular);
            }
        }

        SetValue(op.Index, ChangeEntities.Tour, tour, "type", AgentApiNames.Of(tour.Type), AgentApiNames.Of(type), _ => tour.Type = type);
        _renumber = true;
    }

    private TourType ParseTourType(string value, int opIndex)
    {
        if (!AgentApiNames.TryParse(value, out TourType type))
            throw new ChangesetValidationException(opIndex, "'type' must be regular, warmup or shootout.");
        if (IsShvager && type != TourType.Regular)
            throw new ChangesetValidationException(opIndex, "Своя гра themes are always regular (no warmup or shootout).");
        return type;
    }

    /// <summary>
    /// The block a question goes into: required (and owned by the tour) when the tour has blocks,
    /// forbidden otherwise — questions of a tour with blocks belong to blocks.
    /// </summary>
    private static Block? TargetBlock(Tour tour, int? blockId, int opIndex)
    {
        if (blockId == null)
        {
            return tour.Blocks.Count > 0
                ? throw new ChangesetValidationException(opIndex, $"Tour {tour.Id} has blocks: specify 'blockId'.")
                : null;
        }

        return tour.Blocks.FirstOrDefault(b => b.Id == blockId)
            ?? throw new ChangesetValidationException(opIndex, $"Block {blockId} does not belong to tour {tour.Id}.");
    }

    /// <summary>
    /// Creates a question in <paramref name="tour"/> like the editor: placeholder number (renumbering
    /// assigns it), empty text/answer unless given, authors prefilled when not given — block editors in a
    /// block, else tour editors, else package editors when they are shared.
    /// </summary>
    private async Task<Question> CreateQuestion(
        Tour tour, Block? block, FieldSet? set, IReadOnlyList<AuthorRef>? authors, int opIndex, CancellationToken ct)
    {
        var question = new Question { Number = "0", Text = "", Answer = "", Tour = tour, TourId = tour.Id, Block = block, BlockId = block?.Id };
        tour.Questions.Add(question);
        context.Questions.Add(question);
        _added.Add(question);

        var resolved = authors != null
            ? await ResolveAuthors(authors, opIndex, ct)
            : block != null
                ? block.Editors.ToList()
                : tour.Editors.Count > 0
                    ? tour.Editors.ToList()
                    : package.SharedEditors ? package.PackageEditors.ToList() : [];

        using (Silently())
        {
            if (set != null)
                ApplyQuestionFields(question, set, opIndex);
            SetAuthors(opIndex, ChangeEntities.Question, question, "authors", question.Authors, resolved);
        }

        if (question.Text.Length == 0)
            _warnings.Add("A new question has empty 'text'.");

        _changes.Add(new ChangeRecord { OperationIndex = opIndex, Entity = ChangeEntities.Question, Target = question, Kind = "added" });
        return question;
    }

    /// <summary>Gives earlier records of an entity about to be deleted the label it has now.</summary>
    private void FixLabels(object target, string label)
    {
        foreach (var record in _changes.Where(c => ReferenceEquals(c.Target, target) && c.FixedLabel == null))
            record.FixedLabel = label;
    }

    /// <summary>Questions of one container — a tour's questions outside blocks, or one block — in order.</summary>
    private static List<Question> Container(Tour tour, int? blockId) =>
        tour.Questions.Where(q => q.BlockId == blockId).OrderBy(q => q.OrderIndex).ThenBy(q => q.Id).ToList();

    /// <summary>Puts <paramref name="question"/> at <paramref name="position"/> (null = end) of its container.</summary>
    private void Insert(Tour tour, Block? block, Question question, int? position, int opIndex)
    {
        var siblings = Container(tour, block?.Id).Where(q => q != question).ToList();
        siblings.Insert(Math.Min(position ?? siblings.Count, siblings.Count), question);
        Reindex(siblings, opIndex);
    }

    /// <summary>Sets 0..n-1 positions within a container; positions of existing questions are recorded.</summary>
    private void Reindex(List<Question> ordered, int opIndex)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            var question = ordered[i];
            var position = i;
            if (_added.Contains(question))
                question.OrderIndex = position;
            else
                SetValue(opIndex, ChangeEntities.Question, question, "orderIndex", question.OrderIndex, position, v => question.OrderIndex = v);
        }
    }

    private void ReindexTours(List<Tour> ordered, int opIndex)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            var tour = ordered[i];
            var position = i;
            if (_added.Contains(tour))
                tour.OrderIndex = position;
            else
                SetValue(opIndex, ChangeEntities.Tour, tour, "orderIndex", tour.OrderIndex, position, v => tour.OrderIndex = v);
        }
    }

    /// <summary>Where a question is: tour, block and position within its container.</summary>
    private static System.Text.Json.Nodes.JsonObject Location(Question question, Tour tour) => new()
    {
        ["tourId"] = tour.Id,
        ["blockId"] = question.BlockId,
        ["position"] = Container(tour, question.BlockId).IndexOf(question)
    };

    private SilentScope Silently()
    {
        var previous = _silent;
        _silent = true;
        return new SilentScope(this, previous);
    }

    private readonly struct SilentScope(ChangesetEngine engine, bool previous) : IDisposable
    {
        public void Dispose() => engine._silent = previous;
    }

    #endregion

    #region Finishing

    private void Finish()
    {
        if (_renumber)
            Renumber();

        package.TotalQuestions = package.Tours.Sum(t => t.Questions.Count);
    }

    /// <summary>
    /// Renumbers like the editor and records everything renumbering changed — numbers and positions,
    /// including numbers an operation set earlier in this changeset — as side effects (no operation index).
    /// </summary>
    private void Renumber()
    {
        var tours = package.Tours.ToList();
        var questions = tours.SelectMany(t => t.Questions).ToList();
        var tourBefore = tours.ToDictionary(t => t, t => (t.Number, t.OrderIndex));
        var questionBefore = questions.ToDictionary(q => q, q => (q.Number, q.OrderIndex));

        renumbering.RenumberPackageInMemory(package);

        void Record(string entity, object target, string field, object before, object after)
        {
            if (!Equals(before, after))
            {
                _changes.Add(new ChangeRecord
                {
                    OperationIndex = null, Entity = entity, Target = target, Field = field, Before = before, After = after
                });
            }
        }

        foreach (var tour in tours)
        {
            var (number, orderIndex) = tourBefore[tour];
            Record(ChangeEntities.Tour, tour, "number", number, tour.Number);
            Record(ChangeEntities.Tour, tour, "orderIndex", orderIndex, tour.OrderIndex);
        }

        foreach (var question in questions)
        {
            var (number, orderIndex) = questionBefore[question];
            Record(ChangeEntities.Question, question, "number", number, question.Number);
            Record(ChangeEntities.Question, question, "orderIndex", orderIndex, question.OrderIndex);
        }
    }

    #endregion

    #region Lookups and references

    private Tour FindTour(int id, int opIndex) =>
        package.Tours.FirstOrDefault(t => t.Id == id && !_added.Contains(t))
        ?? throw new ChangesetValidationException(opIndex, $"Tour {id} not found in this package.");

    private Block FindBlock(int id, int opIndex) =>
        package.Tours.SelectMany(t => t.Blocks).FirstOrDefault(b => b.Id == id)
        ?? throw new ChangesetValidationException(opIndex, $"Block {id} not found in this package.");

    private Question FindQuestion(int id, int opIndex) =>
        package.Tours.SelectMany(t => t.Questions).FirstOrDefault(q => q.Id == id && !_added.Contains(q))
        ?? throw new ChangesetValidationException(opIndex, $"Question {id} not found in this package.");

    private Tour TourOf(Question question) => package.Tours.First(t => t.Questions.Contains(question));

    /// <summary>
    /// Resolves author references to tracked entities. A name that does not exist yet becomes a new
    /// <see cref="Author"/> added to the context (saved only if the changeset is applied).
    /// </summary>
    private async Task<List<Author>> ResolveAuthors(IReadOnlyList<AuthorRef> refs, int opIndex, CancellationToken ct)
    {
        var result = new List<Author>();
        foreach (var reference in refs)
        {
            var author = reference.Id is { } id
                ? await AuthorById(id, opIndex, ct)
                : await AuthorByName(reference.FirstName, reference.LastName, opIndex, ct);

            if (!result.Contains(author))
                result.Add(author);
        }

        return result;
    }

    private async Task<Author> AuthorById(int id, int opIndex, CancellationToken ct)
    {
        if (_authorsById.TryGetValue(id, out var cached))
            return cached;

        var author = await context.Authors.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new ChangesetValidationException(opIndex, $"Author {id} not found.");
        _authorsById[id] = author;
        return author;
    }

    private async Task<Author> AuthorByName(string? firstName, string? lastName, int opIndex, CancellationToken ct)
    {
        var first = firstName?.Trim() ?? "";
        var last = lastName?.Trim() ?? "";
        if (first.Length == 0 || last.Length == 0)
            throw new ChangesetValidationException(opIndex, "An author needs a non-empty firstName and lastName.");
        CheckLength<Author>(nameof(Author.FirstName), first, opIndex, "firstName");
        CheckLength<Author>(nameof(Author.LastName), last, opIndex, "lastName");

        if (_authorsByName.TryGetValue((first, last), out var cached))
            return cached;

        // Exact match, as AuthorService.GetOrCreateAuthor does (authors are unique by first + last name).
        var author = await context.Authors.FirstOrDefaultAsync(a => a.FirstName == first && a.LastName == last, ct);
        if (author == null)
        {
            author = new Author { FirstName = first, LastName = last };
            context.Authors.Add(author);
            _warnings.Add($"New author '{first} {last}' will be created (no author with exactly this name exists).");
        }
        else
        {
            _authorsById[author.Id] = author;
        }

        _authorsByName[(first, last)] = author;
        return author;
    }

    /// <summary>Case-insensitive match like <see cref="TagService.GetOrCreate(string)"/>, without LIKE patterns.</summary>
    private async Task<Tag> ResolveTag(string name, CancellationToken ct)
    {
        var key = name.ToLowerInvariant();
        if (_tagsByName.TryGetValue(key, out var cached))
            return cached;

        // ToLower() is translated to SQL lower() (ToLowerInvariant() is not translatable by Npgsql);
        // it never runs in .NET, so the culture warning does not apply.
#pragma warning disable CA1304, CA1311
        var tag = await context.Tags.FirstOrDefaultAsync(t => t.Name.ToLower() == key, ct);
#pragma warning restore CA1304, CA1311
        if (tag == null)
        {
            tag = new Tag { Name = name };
            context.Tags.Add(tag);
            _warnings.Add($"New tag '{name}' will be created.");
        }

        _tagsByName[key] = tag;
        return tag;
    }

    #endregion

    #region Change helpers

    /// <summary>Sets a string field when the value differs and records the change. Returns whether it changed.</summary>
    private bool SetField(int opIndex, string entity, object target, string field, string? before, string? after, Action<string?> set)
    {
        if (before == after)
            return false;

        set(after);
        Record(opIndex, entity, target, field, before, after);
        return true;
    }

    private bool SetValue<T>(int opIndex, string entity, object target, string field, T before, T after, Action<T> set)
    {
        if (EqualityComparer<T>.Default.Equals(before, after))
            return false;

        set(after);
        Record(opIndex, entity, target, field, before, after);
        return true;
    }

    /// <summary>Adds a field-change entry, unless a new entity is being filled in.</summary>
    private void Record(int? opIndex, string entity, object target, string field, object? before, object? after)
    {
        if (_silent)
            return;

        _changes.Add(new ChangeRecord
        {
            OperationIndex = opIndex, Entity = entity, Target = target, Field = field, Before = before, After = after
        });
    }

    /// <summary>
    /// Makes a many-to-many author collection equal to <paramref name="authors"/> (as a set) and
    /// records the change. Returns whether it changed.
    /// </summary>
    private bool SetAuthors(int opIndex, string entity, object target, string field, List<Author> collection, List<Author> authors)
    {
        if (SameSet(collection, authors))
            return false;

        var before = collection.ToList();
        ReplaceCollection(collection, authors);
        Record(opIndex, entity, target, field, before, authors.ToList());
        return true;
    }

    /// <summary>Removes and adds only the differences, so unchanged join rows are left alone.</summary>
    private static void ReplaceCollection<T>(List<T> collection, List<T> values) where T : class
    {
        collection.RemoveAll(item => !values.Contains(item));
        foreach (var value in values.Where(v => !collection.Contains(v)))
            collection.Add(value);
    }

    private static bool SameSet<T>(IEnumerable<T> first, IEnumerable<T> second) where T : class =>
        first.ToHashSet(ReferenceEqualityComparer.Instance).SetEquals(second);

    private static string? OptionalTrimmed(string? value) => OptionalOf(value?.Trim());

    /// <summary>Empty or whitespace-only text is stored as null.</summary>
    private static string? OptionalOf(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Rejects values longer than the column allows (from the EF model), instead of failing on save.</summary>
    private void CheckLength<TEntity>(string property, string? value, int opIndex, string apiField)
    {
        if (value == null)
            return;

        var maxLength = context.Model.FindEntityType(typeof(TEntity))?.FindProperty(property)?.GetMaxLength();
        if (maxLength != null && value.Length > maxLength)
            throw new ChangesetValidationException(opIndex, $"'{apiField}' is longer than {maxLength} characters.");
    }

    #endregion
}
