using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Utils;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Parses document blocks of a Своя гра (Shvager) package into structured package data.
/// Sibling of <see cref="PackageParser"/>: consumes the same <see cref="DocBlock"/> stream
/// and emits a <see cref="ParseResult"/> with <see cref="PackageType.Shvager"/>.
///
/// Supported source formats (see docs/shvager.md and the samples in _shvager/):
/// - Named-theme format: «Тема: {Назва} ({Автори})» headers, «Форма:» label, theme list in the header
/// - Bare-title format: theme headers are plain title lines; the «Теми:» list anchors detection;
///   per-question «Автор: Ім'я Прізвище (Місто)» lines; inline «[Малюнок N {url}]» references
/// </summary>
public class ShvagerParser(ILogger<ShvagerParser> logger)
{
    /// <summary>Canonical number of questions per theme.</summary>
    private const int CanonicalThemeSize = 5;

    /// <summary>Maximum length of a line still considered a bare theme title.</summary>
    private const int MaxBareTitleLength = 100;

    /// <summary>Maximum length of a second header line appended to the package title as a subtitle.</summary>
    private const int MaxSubtitleLength = 60;

    private enum Section
    {
        PackageHeader,
        ThemeList,
        ThemeHeader,
        QuestionText,
        Handout,
        Answer,
        AcceptedAnswers,
        RejectedAnswers,
        Form,
        Comment,
        Source,
        Authors
    }

    private sealed record Line(string Text, DocBlock Block, bool IsFirstOfBlock);

    private sealed class Context
    {
        public ParseResult Result { get; } = new() { Type = PackageType.Shvager };
        public Section CurrentSection { get; set; } = Section.PackageHeader;
        public TourDto? CurrentTheme { get; set; }
        public QuestionDto? CurrentQuestion { get; set; }

        /// <summary>Normalized theme titles from the «Теми:» list → (raw title, authors). Consumed on match.</summary>
        public Dictionary<string, (string Title, List<string> Authors)> Anchors { get; } = new();

        /// <summary>
        /// Normalized «core» title (trailing «(Прізвище)» parenthetical stripped) → the anchor's full key.
        /// Lets a bare header match a list entry whose single-surname author stayed glued to the title.
        /// </summary>
        public Dictionary<string, string> AnchorCores { get; } = new();

        /// <summary>
        /// Next expected position number while reading a numbered «Теми:» list (0 = no run started).
        /// A line continuing the 1..N sequence is a list entry even when its number is a question value.
        /// </summary>
        public int ThemeListNextNumber { get; set; }

        /// <summary>Total number of anchors recorded (for the count-mismatch warning).</summary>
        public int AnchorTotal { get; set; }

        /// <summary>Whether a «Теми:» list header was seen (disables the subtitle rule).</summary>
        public bool ThemeListSeen { get; set; }

        /// <summary>Inside a multiline «[Роздатковий матеріал: …]» bracket.</summary>
        public bool InsideHandoutBracket { get; set; }

        /// <summary>How many non-empty header lines were seen (for title/subtitle detection).</summary>
        public int HeaderLinesSeen { get; set; }
    }

    /// <summary>
    /// Parses extracted document blocks into a Своя гра package structure.
    /// </summary>
    public ParseResult Parse(List<DocBlock> blocks, List<AssetReference> assets)
    {
        var ctx = new Context();
        var lines = Flatten(blocks);

        logger.LogInformation("Parsing Shvager package: {BlockCount} blocks, {LineCount} lines", blocks.Count, lines.Count);

        for (var i = 0; i < lines.Count; i++)
        {
            ProcessLine(lines, i, ctx);

            // Associate embedded images from this line's block (rare for Shvager sources)
            if (lines[i].IsFirstOfBlock && lines[i].Block.Assets.Count > 0)
            {
                AssociateBlockAssets(lines[i].Block, ctx);
            }
        }

        FinalizeResult(ctx);

        logger.LogInformation(
            "Parsed Shvager package: {ThemeCount} themes, {QuestionCount} questions, {WarningCount} warnings",
            ctx.Result.Tours.Count, ctx.Result.TotalQuestions, ctx.Result.Warnings.Count);

        return ctx.Result;
    }

    private static List<Line> Flatten(List<DocBlock> blocks)
    {
        var lines = new List<Line>();
        foreach (var block in blocks)
        {
            var text = TextNormalizer.NormalizeWhitespaceAndDashes(block.Text) ?? "";
            var first = true;
            foreach (var raw in text.Split('\n'))
            {
                lines.Add(new Line(raw.Trim(), block, first));
                first = false;
            }
        }
        return lines;
    }

    private void ProcessLine(List<Line> lines, int index, Context ctx)
    {
        var line = lines[index].Text;

        if (string.IsNullOrWhiteSpace(line))
        {
            // Blank lines end list-like sections so later content is not swallowed
            if (ctx.CurrentSection is Section.Source or Section.Authors)
            {
                ctx.CurrentSection = Section.Comment;
            }
            return;
        }

        if (TryProcessHandoutBracketContinuation(line, ctx)) return;
        if (TryProcessThemeStart(lines, index, ctx)) return;
        if (TryProcessQuestionStart(line, ctx)) return;
        if (TryProcessHeaderLine(line, ctx)) return;

        ProcessLabelOrContent(line, ctx);
    }

    // ==================== Theme detection ====================

    private bool TryProcessThemeStart(List<Line> lines, int index, Context ctx)
    {
        var line = lines[index].Text;

        // Lines inside the «Теми:» list need their own handling: entries become anchors
        if (ctx.CurrentSection == Section.ThemeList && ctx.CurrentTheme == null)
        {
            return TryProcessThemeListLine(lines, index, ctx);
        }

        // «Тема: Назва (Автори)» / «Тема 1. Назва» — explicit header
        if (TryMatchThemeHeader(line, out var headerTitle))
        {
            var (title, authors) = SplitTitleAndAuthors(headerTitle);

            // Prefer the anchor's author list when the header itself carries none
            if (authors.Count == 0 && ctx.Anchors.TryGetValue(NormalizeTitle(title), out var known))
            {
                authors = known.Authors;
            }

            StartTheme(ctx, title, authors);
            return true;
        }

        // Headers may carry a list-style number («5. ПОЛІТИКИ…») — strip it for matching,
        // but never from question-value lines («10. …»)
        var candidate = line;
        if (!IsQuestionStart(line))
        {
            var numberMatch = ParserPatterns.ListNumberPrefix().Match(line);
            if (numberMatch.Success)
            {
                candidate = numberMatch.Groups[1].Value.Trim();
            }
        }

        // A line matching a known anchor is the real theme header
        var normalized = NormalizeTitle(candidate);
        if (ctx.Anchors.TryGetValue(normalized, out var anchor))
        {
            ctx.Anchors.Remove(normalized); // consume: each anchor starts at most one theme
            StartTheme(ctx, anchor.Title, anchor.Authors);
            return true;
        }

        // Also try with a trailing «(Автори)» stripped: «Змії (Іван Петренко)» matches anchor «Змії»
        var (bareTitle, bareAuthors) = SplitTitleAndAuthors(candidate);
        if (bareAuthors.Count > 0)
        {
            var bareNormalized = NormalizeTitle(bareTitle);
            if (ctx.Anchors.TryGetValue(bareNormalized, out var bareAnchor))
            {
                ctx.Anchors.Remove(bareNormalized);
                StartTheme(ctx, bareAnchor.Title, bareAuthors);
                return true;
            }
        }

        // A header may append an explanatory parenthetical the «Теми:» list entry lacks
        // («В. В. (ініціальна тема…)» ↔ anchor «В. В.»; «-ван- / -гог- (…)» ↔ «-ван- / -гог-»).
        // Match the anchor on the core title but keep the full header (with the explanation)
        // as the theme title, like the other themes whose parenthetical was short enough to
        // pass the bare-title check directly.
        var core = StripTrailingParenthetical(candidate);
        if (!string.Equals(core, candidate, StringComparison.Ordinal))
        {
            var coreNormalized = NormalizeTitle(core);
            if (ctx.Anchors.TryGetValue(coreNormalized, out var coreAnchor))
            {
                ctx.Anchors.Remove(coreNormalized);
                var (fullTitle, _) = SplitTitleAndAuthors(candidate);
                StartTheme(ctx, fullTitle, coreAnchor.Authors);
                return true;
            }
        }

        // The «Теми:» list entry kept a «(Прізвище)» single-surname author glued into its title
        // (LooksLikeAuthorList needs a full «Ім'я Прізвище»). Match the bare header against that
        // anchor's core so themes whose title is separated from «10.» by a preamble are still found,
        // without relying on the before-«10.» lookahead.
        if (TryConsumeAnchorByCore(ctx, candidate, out var byCoreTitle, out var byCoreAuthors))
        {
            StartTheme(ctx, byCoreTitle, byCoreAuthors);
            return true;
        }

        // Fallback: a plain line immediately followed by a value-10 question is a theme title.
        // Judge plausibility on the core title (list number already stripped into `candidate`,
        // trailing explanatory parenthetical removed) so long descriptive headers still pass.
        if (IsPlausibleBareTitle(core) && NextQuestionValueIs10(lines, index)
            && (ctx.CurrentTheme == null || ctx.CurrentTheme.Questions.Count > 0))
        {
            var (title, authors) = SplitTitleAndAuthors(candidate);
            StartTheme(ctx, title, authors);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Processes a line while inside the «Теми:» list. Entries (optionally numbered «1.», with or
    /// without the «Тема:» prefix) become anchors. The real content is detected by a duplicate title
    /// or by a title line standing right before the first «10.» question.
    /// </summary>
    private bool TryProcessThemeListLine(List<Line> lines, int index, Context ctx)
    {
        var raw = lines[index].Text;

        // A line continuing the list's 1..N numbering is a list entry — even when its number is a
        // question value. Entry «10.» must be recorded as position 10, not read as a value-10
        // question (which would abandon the rest of the list and start a spurious theme).
        if (TryTakeSequentialListEntry(raw, ctx, out var seqTitle))
        {
            var (t, au) = SplitTitleAndAuthors(seqTitle);
            AddAnchor(ctx, t, au);
            AppendPreamble(ctx, raw);
            return true;
        }

        // A genuine question line («10. …») means the list is over — an untitled theme follows
        if (IsQuestionStart(raw))
        {
            ctx.CurrentSection = Section.PackageHeader;
            return false;
        }

        // Strip the list-entry number: «1.\tАвіація» → «Авіація»
        var line = raw;
        var numberMatch = ParserPatterns.ListNumberPrefix().Match(line);
        if (numberMatch.Success)
        {
            line = numberMatch.Groups[1].Value.Trim();
        }

        // Extract the candidate title (with or without a «Тема:» / «Тема N.» prefix)
        var isThemePrefixed = TryMatchThemeHeader(line, out var candidate);
        if (!isThemePrefixed)
        {
            candidate = line;
        }

        var (title, authors) = SplitTitleAndAuthors(candidate);
        var normalized = NormalizeTitle(title);

        // A duplicate of a known anchor means the list is over and the real content begins
        if (ctx.Anchors.TryGetValue(normalized, out var known))
        {
            ctx.Anchors.Remove(normalized);
            StartTheme(ctx, known.Title, known.Authors.Count > 0 ? known.Authors : authors);
            return true;
        }

        // Same, but the anchor kept a glued «(Прізвище)» surname the bare header lacks
        // («-РОН-» header ↔ «-РОН- (Каунін)» list entry).
        if (TryConsumeAnchorByCore(ctx, candidate, out var coreTitle, out var coreAuthors))
        {
            StartTheme(ctx, coreTitle, coreAuthors);
            return true;
        }

        // A title standing right before the first «10.» question is the real first theme
        if (NextQuestionValueIs10(lines, index))
        {
            StartTheme(ctx, title, authors);
            return true;
        }

        // Otherwise it is a list entry — record the anchor
        if (isThemePrefixed || IsPlausibleBareTitle(line))
        {
            AddAnchor(ctx, title, authors);
            AppendPreamble(ctx, raw);
            return true;
        }

        // Not a list entry — the list is over; return to plain header handling
        ctx.CurrentSection = Section.PackageHeader;
        return false;
    }

    private void StartTheme(Context ctx, string title, List<string> authors)
    {
        FinalizeQuestion(ctx);
        FinalizeTheme(ctx);

        var theme = new TourDto
        {
            Number = (ctx.Result.Tours.Count + 1).ToString(),
            Title = title,
            OrderIndex = ctx.Result.Tours.Count,
            Type = TourType.Regular,
            Editors = [.. authors]
        };

        ctx.Result.Tours.Add(theme);
        ctx.CurrentTheme = theme;
        ctx.CurrentQuestion = null;
        ctx.CurrentSection = Section.ThemeHeader;

        logger.LogDebug("Theme started: {Title}", title);
    }

    private static void AddAnchor(Context ctx, string title, List<string> authors)
    {
        var key = NormalizeTitle(title);
        if (ctx.Anchors.TryAdd(key, (title, authors)))
        {
            ctx.AnchorTotal++;

            // Index the anchor by its «core» title too (trailing parenthetical stripped), so a bare
            // header matches a list entry whose «(Прізвище)» single surname stayed glued to the title.
            var core = StripTrailingParenthetical(title);
            if (!string.Equals(core, title, StringComparison.Ordinal))
            {
                ctx.AnchorCores.TryAdd(NormalizeTitle(core), key);
            }
        }
    }

    /// <summary>
    /// While reading a numbered «Теми:» list, recognizes a line that continues the 1..N sequence and
    /// returns its title (list number stripped). This keeps entry «10.» — whose number is also a
    /// question value — from being mistaken for the first value-10 question.
    /// </summary>
    private static bool TryTakeSequentialListEntry(string raw, Context ctx, out string title)
    {
        title = "";

        var match = ParserPatterns.ListEntryNumbered().Match(raw);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var number)) return false;

        // Start a run only at «1.» (so a stray number doesn't latch the sequence); afterwards the
        // number must be exactly the next position.
        if (ctx.ThemeListNextNumber == 0)
        {
            if (number != 1) return false;
        }
        else if (number != ctx.ThemeListNextNumber)
        {
            return false;
        }

        ctx.ThemeListNextNumber = number + 1;
        title = match.Groups[2].Value.Trim();
        return true;
    }

    /// <summary>
    /// Matches a bare theme header against an anchor indexed by its «core» title (the list entry kept
    /// a glued «(Прізвище)» surname). On success removes the anchor and returns the clean header title
    /// plus the header's own authors, falling back to the anchor's authors.
    /// </summary>
    private static bool TryConsumeAnchorByCore(Context ctx, string candidate, out string title, out List<string> authors)
    {
        var (candTitle, candAuthors) = SplitTitleAndAuthors(candidate);

        // Try the candidate's clean title first, then the candidate verbatim.
        foreach (var lookup in new[] { NormalizeTitle(candTitle), NormalizeTitle(candidate) })
        {
            if (ctx.AnchorCores.TryGetValue(lookup, out var fullKey)
                && ctx.Anchors.TryGetValue(fullKey, out var anchor))
            {
                ctx.Anchors.Remove(fullKey);
                ctx.AnchorCores.Remove(lookup);
                title = candTitle;
                authors = candAuthors.Count > 0 ? candAuthors : anchor.Authors;
                return true;
            }
        }

        title = "";
        authors = [];
        return false;
    }

    /// <summary>
    /// Matches an explicit theme header: «Тема: Назва», «Тема. Назва» or the numbered
    /// variant «Тема 1. Назва» / «Тема №2: Назва». The document's own number is ignored —
    /// themes are numbered sequentially by position.
    /// </summary>
    private static bool TryMatchThemeHeader(string line, out string title)
    {
        var numbered = ParserPatterns.ShvagerNumberedThemeStart().Match(line);
        if (numbered.Success)
        {
            title = numbered.Groups[1].Value;
            return true;
        }

        var plain = ParserPatterns.ShvagerThemeStart().Match(line);
        if (plain.Success)
        {
            title = plain.Groups[1].Value;
            return true;
        }

        title = "";
        return false;
    }

    private static bool IsPlausibleBareTitle(string line)
    {
        if (line.Length > MaxBareTitleLength) return false;
        if (line.Contains("http", StringComparison.OrdinalIgnoreCase)) return false;
        if (line.EndsWith(':')) return false;
        if (ParserPatterns.ShvagerQuestionStart().IsMatch(line)) return false;
        if (DetectLabel(line).Section != null) return false;
        return true;
    }

    private static bool NextQuestionValueIs10(List<Line> lines, int index)
    {
        for (var i = index + 1; i < lines.Count; i++)
        {
            var text = lines[i].Text;
            if (string.IsNullOrWhiteSpace(text)) continue;

            // An author designation may sit between the theme title and its first question
            // («Миші» / «Автор: Тарас Вахрів» / «10. …», or the parenthetical «(Авторка – …)»).
            // Skip it so the title line is still recognized as standing before the «10.» question.
            if (TryMatchThemeAuthorLine(text, out _)) continue;

            var match = ParserPatterns.ShvagerQuestionStart().Match(text);
            return match.Success && match.Groups[1].Value == "10";
        }
        return false;
    }

    /// <summary>
    /// Recognizes a stand-alone theme-author line and returns its author list. Two forms appear
    /// in real headers between the title and the first question:
    ///   • label form        — «Автор: Ім'я Прізвище», «Авторка: …»
    ///   • parenthetical form — «(Авторка – Ім'я Прізвище)»
    /// Only matches when the name part looks like a person-name list, so a title that merely
    /// starts with «Автор…» or carries a non-name parenthetical is not mistaken for an author line.
    /// </summary>
    private static bool TryMatchThemeAuthorLine(string line, out List<string> authors)
    {
        var label = ParserPatterns.AuthorLabel().Match(line);
        if (label.Success && LooksLikeAuthorList(label.Groups[1].Value))
        {
            authors = PackageParser.ParseAuthorList(label.Groups[1].Value);
            return true;
        }

        var paren = ParserPatterns.ShvagerParentheticalAuthor().Match(line);
        if (paren.Success && LooksLikeAuthorList(paren.Groups[1].Value))
        {
            authors = PackageParser.ParseAuthorList(paren.Groups[1].Value);
            return true;
        }

        authors = [];
        return false;
    }

    /// <summary>Whether the line starts a question (a valid value or value range + separator).</summary>
    private static bool IsQuestionStart(string line)
    {
        var match = ParserPatterns.ShvagerQuestionStart().Match(line);
        if (!match.Success) return false;

        var parts = match.Groups[1].Value
            .Split(['-', '–', '—'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.All(p => int.TryParse(p, out var v) && v % 10 == 0 && v is >= 10 and <= 100);
    }

    /// <summary>
    /// Strips a single sentence-ending period, preserving trailing ellipses
    /// (titles like «Каркасна ...С...Я...Н...» must keep their dots).
    /// </summary>
    private static string TrimSentencePeriod(string text)
        => text.EndsWith('.') && !text.EndsWith("..", StringComparison.Ordinal)
            ? text[..^1]
            : text;

    /// <summary>
    /// Returns the title with a single trailing «(…)» group removed, whatever it contains
    /// — used to match a header carrying an explanatory parenthetical against a bare «Теми:»
    /// anchor, and to measure a long descriptive header by its core length. Returns the input
    /// unchanged when there is no trailing group or nothing precedes it.
    /// </summary>
    private static string StripTrailingParenthetical(string text)
    {
        var match = ParserPatterns.TitleWithTrailingParens().Match(text);
        if (match.Success)
        {
            var head = match.Groups[1].Value.Trim();
            if (head.Length > 0) return head;
        }
        return text;
    }

    /// <summary>
    /// Splits «Назва (Автор1, Автор2)» into the title and the author list.
    /// Parenthesized text is treated as authors only when it looks like person names.
    /// </summary>
    private static (string Title, List<string> Authors) SplitTitleAndAuthors(string text)
    {
        text = TrimSentencePeriod(text.Trim());

        var match = ParserPatterns.TitleWithTrailingParens().Match(text);
        if (match.Success)
        {
            var inner = match.Groups[2].Value.Trim();
            if (LooksLikeAuthorList(inner))
            {
                var title = match.Groups[1].Value.Trim();
                if (title.Length > 0)
                {
                    return (title, PackageParser.ParseAuthorList(inner));
                }
            }
        }

        return (text, []);
    }

    private static bool LooksLikeAuthorList(string text)
    {
        if (text.Any(char.IsDigit)) return false;

        // Every comma/та/і-separated entry must look like «Ім'я Прізвище» (2-3 capitalized words)
        var entries = PackageParser.ParseAuthorList(text);
        if (entries.Count == 0) return false;

        return entries.All(entry =>
        {
            var words = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length is 2 or 3 && words.All(w => w.Length > 1 && char.IsUpper(w[0]));
        });
    }

    private static string NormalizeTitle(string title)
    {
        var noAccents = TextNormalizer.RemoveAccents(title.Trim().TrimEnd('.', '…'));
        return TextNormalizer.NormalizeApostrophes(noAccents)!.ToLowerInvariant();
    }

    // ==================== Question detection ====================

    private bool TryProcessQuestionStart(string line, Context ctx)
    {
        var match = ParserPatterns.ShvagerQuestionStart().Match(line);
        if (!match.Success) return false;

        // "10" or a reserve-theme range like "30-50"; every part must be a value (multiple of 10)
        var parts = match.Groups[1].Value
            .Split(['-', '–', '—'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var values = new List<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var parsed) || parsed % 10 != 0 || parsed < 10 || parsed > 100)
                return false;
            values.Add(parsed);
        }

        var value = values[0];
        var displayNumber = string.Join("-", values);

        if (ctx.CurrentTheme == null)
        {
            // Question before any theme header — recover with an untitled theme
            ctx.Result.Warnings.Add($"Запитання за {value} з'явилося до першої теми — створено тему без назви");
            StartTheme(ctx, "", []);
        }
        else if (ctx.CurrentTheme.Questions.Count > 0 && value <= LastQuestionStartValue(ctx.CurrentTheme))
        {
            // Within a theme, question values strictly increase (10 → 20 → 30 → 40 → 50). A value
            // that does not exceed the previous question's means the current theme is complete and
            // a new one began — recover even when its title line was not recognized (e.g. a long
            // descriptive header, or a title separated from its first question by a preamble).
            ctx.Result.Warnings.Add(
                $"Тема «{ctx.CurrentTheme.Title}»: запитання за {value} йде після запитання за більшу вартість — розпочато нову тему без назви");
            StartTheme(ctx, "", []);
        }

        FinalizeQuestion(ctx);

        var theme = ctx.CurrentTheme!;
        var expectedValue = (theme.Questions.Count + 1) * 10;
        if (value != expectedValue)
        {
            ctx.Result.Warnings.Add(
                $"Тема «{theme.Title}»: очікувалося запитання за {expectedValue}, знайдено за {value}");
        }

        var question = new QuestionDto
        {
            Number = displayNumber
        };

        theme.Questions.Add(question);
        ctx.CurrentQuestion = question;
        ctx.CurrentSection = Section.QuestionText;

        var remainder = match.Groups[2].Value.Trim();
        if (!string.IsNullOrEmpty(remainder))
        {
            // Full pipeline: the value line may carry a handout bracket or inline labels
            ProcessLabelOrContent(remainder, ctx);
        }

        return true;
    }

    /// <summary>
    /// The starting value of a theme's last question (the low end of a «30-50» range),
    /// used to detect the value reset that marks a new theme.
    /// </summary>
    private static int LastQuestionStartValue(TourDto theme)
    {
        var first = theme.Questions[^1].Number
            .Split(['-', '–', '—'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return int.TryParse(first, out var v) ? v : 0;
    }

    // ==================== Package header ====================

    private bool TryProcessHeaderLine(string line, Context ctx)
    {
        // Header handling applies only before the first theme
        if (ctx.CurrentTheme != null) return false;

        // «Теми:» opens the theme list
        if (ParserPatterns.ShvagerThemeListHeader().IsMatch(line))
        {
            ctx.CurrentSection = Section.ThemeList;
            ctx.ThemeListSeen = true;
            AppendPreamble(ctx, line);
            return true;
        }

        ctx.HeaderLinesSeen++;

        // First non-empty line is the package title
        if (ctx.HeaderLinesSeen == 1)
        {
            ctx.Result.Title = TextNormalizer.NormalizeApostrophes(TrimSentencePeriod(line));
            return true;
        }

        // «Редактор Ім'я Прізвище» (with optional prefix like «Коло 1.») → package editors
        var editorsMatch = ParserPatterns.ShvagerHeaderEditors().Match(line);
        if (editorsMatch.Success && LooksLikeAuthorList(editorsMatch.Groups[2].Value))
        {
            ctx.Result.SharedEditors = true;
            ctx.Result.PackageEditors.AddRange(PackageParser.ParseAuthorList(editorsMatch.Groups[2].Value));

            var prefix = editorsMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(prefix))
            {
                ctx.Result.Title = $"{ctx.Result.Title}. {prefix}";
            }
            return true;
        }

        // A short second line is a subtitle appended to the title
        // (never after a «Теми:» list, never a label line, never an editor-like line —
        // even one whose author part failed to parse belongs in the preamble, not the title)
        if (ctx.HeaderLinesSeen == 2 && !ctx.ThemeListSeen && line.Length <= MaxSubtitleLength
            && !line.Contains("http", StringComparison.OrdinalIgnoreCase) && !line.EndsWith(':')
            && DetectLabel(line).Section == null
            && !ParserPatterns.ShvagerHeaderEditors().IsMatch(line))
        {
            ctx.Result.Title = $"{ctx.Result.Title}. {TextNormalizer.NormalizeApostrophes(TrimSentencePeriod(line))}";
            return true;
        }

        AppendPreamble(ctx, line);
        return true;
    }

    private static void AppendPreamble(Context ctx, string line)
    {
        var normalized = TextNormalizer.NormalizeApostrophes(line)!;
        ctx.Result.Preamble = string.IsNullOrWhiteSpace(ctx.Result.Preamble)
            ? normalized
            : ctx.Result.Preamble + "\n" + normalized;
    }

    // ==================== Labels and content ====================

    private static readonly (Func<System.Text.RegularExpressions.Regex> GetPattern, Section Section)[] LabelPatterns =
    [
        (ParserPatterns.AnswerLabel, Section.Answer),
        (ParserPatterns.AcceptedLabel, Section.AcceptedAnswers),
        (ParserPatterns.RejectedLabel, Section.RejectedAnswers),
        (ParserPatterns.FormLabel, Section.Form),
        (ParserPatterns.CommentLabel, Section.Comment),
        (ParserPatterns.SourceLabel, Section.Source),
        (ParserPatterns.AuthorLabel, Section.Authors),
        (ParserPatterns.HandoutMarker, Section.Handout)
    ];

    private static (Section? Section, string Remainder) DetectLabel(string text)
    {
        foreach (var (getPattern, section) in LabelPatterns)
        {
            var match = getPattern().Match(text);
            if (match.Success)
                return (section, match.Groups[1].Value.Trim());
        }

        return (null, text);
    }

    private void ProcessLabelOrContent(string line, Context ctx)
    {
        // A theme header (before any question) may carry an author designation on its own line →
        // theme authors. «Редактор: X» plus the two «Автор» forms handled by TryMatchThemeAuthorLine
        // («Автор: Тарас Вахрів», «(Авторка – Вікторія Маландіна)»).
        if (ctx.CurrentQuestion == null && ctx.CurrentTheme != null)
        {
            var editorsMatch = ParserPatterns.EditorsLabel().Match(line);
            if (editorsMatch.Success && LooksLikeAuthorList(editorsMatch.Groups[1].Value))
            {
                ctx.CurrentTheme.Editors.AddRange(PackageParser.ParseAuthorList(editorsMatch.Groups[1].Value));
                return;
            }

            if (TryMatchThemeAuthorLine(line, out var themeAuthors))
            {
                ctx.CurrentTheme.Editors.AddRange(themeAuthors);
                return;
            }
        }

        // «[Ведучому: …]» bracket — its content is the host instruction
        if (ctx.CurrentQuestion != null && TryProcessHostInstructionsBracket(line, ctx)) return;

        // «[Роздатковий матеріал: …]» bracket — its content is the handout text
        if (ctx.CurrentQuestion != null && TryProcessHandoutBracket(line, ctx)) return;

        var (section, remainder) = DetectLabel(line);
        if (section != null)
        {
            if (ctx.CurrentQuestion == null)
            {
                // Labels outside a question (e.g. in a theme preamble) are kept as plain text
                AppendToCurrentSection(line, ctx);
                return;
            }

            ctx.CurrentSection = section.Value;
            line = remainder;
        }

        if (string.IsNullOrWhiteSpace(line)) return;

        // Inline labels mid-line: «Відповідь: Індіра Ганді. Залік: за прізвищем.»
        var inlineIndex = FindInlineLabelStart(line);
        if (inlineIndex > 0 && ctx.CurrentQuestion != null)
        {
            var before = line[..inlineIndex].Trim();
            if (before.Length > 0)
            {
                AppendToCurrentSection(before, ctx);
            }

            ProcessLabelOrContent(line[inlineIndex..], ctx);
            return;
        }

        AppendToCurrentSection(line, ctx);
    }

    /// <summary>
    /// Finds the start of an inline label within content. Only Залік/Незалік/Форма may
    /// appear mid-line (other labels must be at line start — phrases like «дайте
    /// відповідь:» in question text must not be treated as labels).
    /// </summary>
    private static int FindInlineLabelStart(string text)
    {
        string[] inlineKeywords =
        [
            "Залік:",
            "Заліки:",
            "Зараховується:",
            "Незалік:",
            "Не залік:",
            "Не приймається:",
            "Форма:"
        ];

        var minIndex = int.MaxValue;
        foreach (var keyword in inlineKeywords)
        {
            var index = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (index > 0 && index < minIndex)
            {
                minIndex = index;
            }
        }

        return minIndex == int.MaxValue ? -1 : minIndex;
    }

    // ==================== Host instructions ====================

    /// <summary>
    /// Handles «[Ведучому: текст]» host-instruction brackets. The bracket content becomes the
    /// question's host instruction; any text after the closing bracket continues as question text.
    /// Mirrors the Що?Де?Коли? parser — single-line brackets only.
    /// </summary>
    private bool TryProcessHostInstructionsBracket(string line, Context ctx)
    {
        var match = ParserPatterns.HostInstructionsBracket().Match(line);
        if (!match.Success) return false;

        var question = ctx.CurrentQuestion!;
        var instructions = match.Groups[1].Value.Trim();
        if (instructions.Length > 0)
        {
            question.HostInstructions = Append(question.HostInstructions, Normalize(instructions));
        }

        ctx.CurrentSection = Section.QuestionText;

        var rest = match.Groups[2].Value.Trim();
        if (rest.Length > 0)
        {
            ProcessLabelOrContent(rest, ctx);
        }
        return true;
    }

    // ==================== Bracketed handouts ====================

    /// <summary>
    /// Handles «[Роздатковий матеріал: текст]» (single line) and the opening of a
    /// multiline bracket. Content inside the bracket becomes the handout text; content
    /// after the closing bracket continues as question text.
    /// </summary>
    private bool TryProcessHandoutBracket(string line, Context ctx)
    {
        var question = ctx.CurrentQuestion!;

        var singleLine = ParserPatterns.HandoutMarkerBracket().Match(line);
        if (singleLine.Success)
        {
            var handout = singleLine.Groups[1].Value.Trim();
            if (handout.Length > 0)
            {
                question.HandoutText = Append(question.HandoutText, Normalize(handout));
            }

            ctx.CurrentSection = Section.QuestionText;

            var rest = singleLine.Groups[2].Value.Trim();
            if (rest.Length > 0)
            {
                ProcessLabelOrContent(rest, ctx);
            }
            return true;
        }

        var multilineOpen = ParserPatterns.HandoutMarkerBracketOpen().Match(line);
        if (multilineOpen.Success && !line.Contains(']'))
        {
            ctx.InsideHandoutBracket = true;
            var handout = multilineOpen.Groups[1].Value.Trim();
            if (handout.Length > 0)
            {
                question.HandoutText = Append(question.HandoutText, Normalize(handout));
            }
            return true;
        }

        // A bare «[…]» bracket after a stand-alone «Роздатка:» marker — the marker and the bracket
        // sit on separate lines («10. Роздатка:» / «[» / «url» / «]» / question text). The current
        // section is already Handout; wrap the bracket content as handout and let the closing «]»
        // hand control back to the question text, otherwise everything after «]» is swallowed.
        if (ctx.CurrentSection == Section.Handout && line.StartsWith('['))
        {
            var closeIndex = line.IndexOf(']');
            if (closeIndex < 0)
            {
                ctx.InsideHandoutBracket = true;
                var opened = line[1..].Trim();
                if (opened.Length > 0)
                {
                    question.HandoutText = Append(question.HandoutText, Normalize(opened));
                }
                return true;
            }

            var inside = line[1..closeIndex].Trim();
            if (inside.Length > 0)
            {
                question.HandoutText = Append(question.HandoutText, Normalize(inside));
            }

            ctx.CurrentSection = Section.QuestionText;

            var rest = line[(closeIndex + 1)..].Trim();
            if (rest.Length > 0)
            {
                ProcessLabelOrContent(rest, ctx);
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Consumes lines inside a multiline «[Роздатковий матеріал: …» bracket until the
    /// closing «]»; content after the bracket continues as question text.
    /// </summary>
    private bool TryProcessHandoutBracketContinuation(string line, Context ctx)
    {
        if (!ctx.InsideHandoutBracket) return false;

        var question = ctx.CurrentQuestion;
        var closeIndex = line.IndexOf(']');

        if (closeIndex < 0)
        {
            if (question != null)
            {
                question.HandoutText = Append(question.HandoutText, Normalize(line));
            }
            return true;
        }

        var inside = line[..closeIndex].Trim();
        if (question != null && inside.Length > 0)
        {
            question.HandoutText = Append(question.HandoutText, Normalize(inside));
        }

        ctx.InsideHandoutBracket = false;
        ctx.CurrentSection = Section.QuestionText;

        var rest = line[(closeIndex + 1)..].Trim();
        if (rest.Length > 0)
        {
            ProcessLabelOrContent(rest, ctx);
        }
        return true;
    }

    private void AppendToCurrentSection(string text, Context ctx)
    {
        var question = ctx.CurrentQuestion;

        if (question == null)
        {
            if (ctx.CurrentTheme != null)
            {
                ctx.CurrentTheme.Preamble = Append(ctx.CurrentTheme.Preamble, text);
            }
            else
            {
                AppendPreamble(ctx, text);
            }
            return;
        }

        switch (ctx.CurrentSection)
        {
            case Section.QuestionText:
                question.Text = Append(question.Text, ExtractPictureBrackets(text, question, ctx))!;
                break;
            case Section.Handout:
                question.HandoutText = Append(question.HandoutText, Normalize(text));
                break;
            case Section.Answer:
                question.Answer = Append(question.Answer, Normalize(text))!;
                break;
            case Section.AcceptedAnswers:
                question.AcceptedAnswers = Append(question.AcceptedAnswers, Normalize(text));
                break;
            case Section.RejectedAnswers:
                question.RejectedAnswers = Append(question.RejectedAnswers, Normalize(text));
                break;
            case Section.Form:
                question.Form = Append(question.Form, Normalize(text));
                break;
            case Section.Comment:
                question.Comment = Append(question.Comment, Normalize(text));
                break;
            case Section.Source:
                // Sources are not apostrophe-normalized: they may contain URLs
                question.Source = Append(question.Source, text);
                break;
            case Section.Authors:
                question.Authors.AddRange(PackageParser.ParseAuthorList(text));
                break;
            default:
                question.Text = Append(question.Text, ExtractPictureBrackets(text, question, ctx))!;
                break;
        }
    }

    /// <summary>
    /// Moves inline «[Малюнок N {url}]» references from question text into the handout text.
    /// </summary>
    private static string Normalize(string text) => TextNormalizer.NormalizeApostrophes(text)!;

    private string ExtractPictureBrackets(string text, QuestionDto question, Context ctx)
    {
        var matches = ParserPatterns.PictureBracket().Matches(text);
        if (matches.Count == 0) return Normalize(text);

        foreach (var match in matches.Cast<System.Text.RegularExpressions.Match>())
        {
            var reference = match.Groups[1].Value.Trim();
            question.HandoutText = Append(question.HandoutText, reference);
            ctx.Result.Warnings.Add(
                $"Запитання за {question.Number}: посилання «[{reference}]» перенесено до роздаткового матеріалу");
        }

        var cleaned = ParserPatterns.PictureBracket().Replace(text, "").Trim();
        return Normalize(cleaned);
    }

    private static string? Append(string? existing, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return existing;
        return string.IsNullOrWhiteSpace(existing) ? text : existing + "\n" + text;
    }

    // ==================== Assets ====================

    private void AssociateBlockAssets(DocBlock block, Context ctx)
    {
        foreach (var asset in block.Assets)
        {
            var question = ctx.CurrentQuestion;
            if (question == null)
            {
                ctx.Result.Warnings.Add($"Зображення {asset.FileName} поза запитанням — пропущено");
                continue;
            }

            var isAnswerRelated = ctx.CurrentSection is Section.Answer or Section.AcceptedAnswers
                or Section.RejectedAnswers or Section.Form or Section.Comment or Section.Source or Section.Authors;

            if (isAnswerRelated)
            {
                if (question.CommentAssetFileName == null)
                    question.CommentAssetFileName = asset.FileName;
                else
                    ctx.Result.Warnings.Add($"Запитання за {question.Number}: додаткове зображення {asset.FileName} пропущено");
            }
            else
            {
                if (question.HandoutAssetFileName == null)
                {
                    question.HandoutAssetFileName = asset.FileName;

                    // A bare «Роздатковий матеріал:» label (no inline text) announces this image and
                    // leaves the section in Handout. The image *is* the handout, so text that follows
                    // is the question, not more handout — revert so it is not swallowed by HandoutText.
                    if (ctx.CurrentSection == Section.Handout && question.HandoutText == null)
                    {
                        ctx.CurrentSection = Section.QuestionText;
                    }
                }
                else
                    ctx.Result.Warnings.Add($"Запитання за {question.Number}: додаткове зображення {asset.FileName} пропущено");
            }
        }
    }

    // ==================== Finalization ====================

    private void FinalizeQuestion(Context ctx)
    {
        var question = ctx.CurrentQuestion;
        if (question == null) return;

        if (!question.HasAnswer)
        {
            ctx.Result.Warnings.Add(
                $"Тема «{ctx.CurrentTheme?.Title}», запитання за {question.Number}: не знайдено відповідь");
        }

        ctx.CurrentQuestion = null;
    }

    private static void FinalizeTheme(Context ctx)
    {
        var theme = ctx.CurrentTheme;
        if (theme == null) return;

        if (theme.Questions.Count != CanonicalThemeSize)
        {
            ctx.Result.Warnings.Add(
                $"Тема «{theme.Title}»: {theme.Questions.Count} запитань замість {CanonicalThemeSize}");
        }

        ctx.CurrentTheme = null;
    }

    private void FinalizeResult(Context ctx)
    {
        FinalizeQuestion(ctx);
        FinalizeTheme(ctx);

        var result = ctx.Result;

        if (ctx.AnchorTotal > 0 && ctx.AnchorTotal != result.Tours.Count)
        {
            result.Warnings.Add(
                $"У списку «Теми:» {ctx.AnchorTotal} тем, а в пакеті знайдено {result.Tours.Count}");
        }

        // Author cascade: most packages have a single global author, so per-question
        // «Автор:» lines are usually absent. A question without an explicit author
        // inherits the theme authors, or the package-level editor when the theme has none.
        foreach (var theme in result.Tours)
        {
            var fallback = theme.Editors.Count > 0 ? theme.Editors : result.PackageEditors;
            if (fallback.Count == 0) continue;

            foreach (var question in theme.Questions.Where(q => q.Authors.Count == 0))
            {
                question.Authors.AddRange(fallback);
            }
        }

        result.Preamble = string.IsNullOrWhiteSpace(result.Preamble) ? null : result.Preamble.Trim();

        foreach (var theme in result.Tours)
        {
            theme.Preamble = string.IsNullOrWhiteSpace(theme.Preamble) ? null : theme.Preamble.Trim();
        }
    }
}
