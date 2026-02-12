using System.Globalization;
using System.Text.RegularExpressions;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Utils;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Regex patterns for parsing Ukrainian question packages.
/// </summary>
public static partial class ParserPatterns
{
    // Tour detection
    [GeneratedRegex(@"^\s*(?:ТУР|Тур|Tour)\s+(\d+)[\.:]?\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex TourStart();

    // Matches: "Тур 1. Назва туру", "ТУР 2: Лірики" (tour number followed by name/preamble)
    [GeneratedRegex(@"^\s*(?:ТУР|Тур|Tour)\s+(\d+)[\.:]?\s+(.+)$", RegexOptions.IgnoreCase)]
    public static partial Regex TourStartWithPreamble();

    // Matches: "Тур: 1", "ТУР: 2" (colon before number)
    [GeneratedRegex(@"^\s*(?:ТУР|Тур|Tour)\s*:\s*(\d+)\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex TourStartWithColon();

    [GeneratedRegex(@"^\s*[-–—]\s*(?:ТУР|Тур)\s+(\d+)[\.:]?\s*[-–—]\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex TourStartDashed();

    // Matches Ukrainian ordinal tour names: "Перший тур", "Другий тур", etc. (1-9)
    // Explicitly includes both cases since IgnoreCase may not work reliably with Cyrillic
    // Uses character class [''ʼ] to match different apostrophe variants (', ', ʼ)
    [GeneratedRegex(@"^\s*([ПпДдТтЧчШшСсВв][''ʼА-яі]+)\s+[Тт][Уу][Рр]\s*$")]
    public static partial Regex OrdinalTourStart();

    // Matches reversed format: "Тур перший", "Тур другий", etc. (1-9)
    [GeneratedRegex(@"^\s*[Тт][Уу][Рр]\s+([ПпДдТтЧчШшСсВв][''ʼА-яі]+)\s*$")]
    public static partial Regex TourOrdinalStart();

    // Matches: "1 Тур", "2 тур", "3 ТУР" (number before word "Тур")
    [GeneratedRegex(@"^\s*(\d+)\s+(?:ТУР|Тур|тур|Tour)[\.:,]?\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex NumberTourStart();

    // Matches: "Тур №1", "ТУР №2", "Тур № 3" (with № sign)
    [GeneratedRegex(@"^\s*(?:ТУР|Тур|Tour)\s*№\s*(\d+)[\.:,]?\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex TourNumberSignStart();

    // Matches: "ТУР III", "Тур ІІ", "ТУР ІІІ" (Roman numerals, including Cyrillic І/Х lookalikes)
    // Character class includes both Latin (I, V, X, L, C, D, M) and Cyrillic lookalikes (І/і U+0456, Х/х U+0425/0445)
    [GeneratedRegex(@"^\s*(?:ТУР|Тур|Tour)\s+([IІVXХLCDMіivxхlcdm]+)[\.:,]?\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex TourRomanStart();

    // Matches: "ТУР III. Назва туру", "Тур ІІ: Лірики" (Roman numeral followed by name/preamble)
    [GeneratedRegex(@"^\s*(?:ТУР|Тур|Tour)\s+([IІVXХLCDMіivxхlcdm]+)[\.:,]?\s+(.+)$", RegexOptions.IgnoreCase)]
    public static partial Regex TourRomanStartWithPreamble();

    // Warmup tour detection: "Розминка", "Warmup", "Тур 0", "Розминковий тур"
    [GeneratedRegex(@"^\s*(?:Розминка|Warmup|Розминковий\s+тур)\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex WarmupTourStart();

    [GeneratedRegex(@"^\s*[-–—]\s*(?:Розминка|Warmup)\s*[-–—]\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex WarmupTourStartDashed();

    [GeneratedRegex(@"^\s*(?:ТУР|Тур|Tour)\s+0\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex TourZeroStart();

    // Warmup question label (requires bold formatting): "Розминочне питання", "Розминкове питання", "Розминка"
    // This creates an implicit warmup tour when appearing before any tours
    [GeneratedRegex(@"^\s*(?:Розминочне\s+питання|Розминкове\s+питання|Розминка)\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex WarmupQuestionLabel();

    // Question detection
    [GeneratedRegex(@"^\s*(\d+)\.\s+(.*)$")]
    public static partial Regex QuestionStartWithText();

    [GeneratedRegex(@"^\s*(\d+)\.\s*$")]
    public static partial Regex QuestionStartNumberOnly();

    // Matches: "Запитання 1", "Питання 1.", "Запитання 1:", "Запитання №1", "Питання №1."
    [GeneratedRegex(@"^\s*(?:Запитання|Питання)\s+№?(\d+)[\.:]?\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex QuestionStartNamed();

    // Matches: "Запитання 1. text", "Питання 1: text", "Запитання №1. text" (named format with text after)
    [GeneratedRegex(@"^\s*(?:Запитання|Питання)\s+№?(\d+)[\.:]?\s+(.+)$", RegexOptions.IgnoreCase)]
    public static partial Regex QuestionStartNamedWithText();

    // Labels (field markers)
    // Note: Using character class to match both Cyrillic 'і' (U+0456) and Latin 'i' (U+0069)
    // This handles common typing/OCR errors where Latin 'i' is used instead of Cyrillic 'і'
    // Also supports Russian "Ответ" as alternative to Ukrainian "Відповідь"
    // Separator can be colon (:) or dot with optional whitespace (.) to support dot at end of line
    [GeneratedRegex(@"^\s*(?:В[\u0456\u0069]дпов[\u0456\u0069]дь|Ответ)\s*(?::|[.]\s?)\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex AnswerLabel();

    // Matches: "Залік: ...", "Заліки: ...", "Залік (не оголошувати): ...", "Залік. ..."
    // Separator can be colon (:) or dot with optional whitespace (.)
    [GeneratedRegex(@"^\s*Залік(?:и)?(?:\s*\([^)]+\))?\s*(?::|[.]\s?)\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex AcceptedLabel();

    // Separator can be colon (:) or dot with optional whitespace (.)
    [GeneratedRegex(@"^\s*(?:Незалік|Не\s*залік|Не\s*приймається)\s*(?::|[.]\s?)\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex RejectedLabel();

    // Matches: "Коментар: ...", "Коментарі: ..." (Ukrainian), "Комментарий: ...", "Комментар: ..." (Russian/mixed)
    // Separator can be colon (:) or dot with optional whitespace (.)
    [GeneratedRegex(@"^\s*(?:Коментар|Коментарі|Комментарий|Комментар)\s*(?::|[.]\s?)\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex CommentLabel();

    // Matches: "Джерело: ...", "Джерела: ...", "Джерело(а): ...", "Джерел(а): ..." (Ukrainian)
    // Also: "Источник: ...", "Источники: ..." (Russian)
    // Separator can be colon (:) or dot with optional whitespace (.)
    [GeneratedRegex(@"^\s*(?:Джерело|Джерела|Джерело\(а\)|Джерел\(а\)|Источник|Источники)\s*(?::|[.]\s?)\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex SourceLabel();

    // Matches: "Автор:", "Автори:", "Автора:", "Авторка:", "Авторки:", "Автор(и):", "Авторы:" (Russian)
    // Separator can be colon (:) or dot with optional whitespace (.)
    [GeneratedRegex(@"^\s*Автор(?:а|и|ы|ка|ки|\(и\))?\s*(?::|[.]\s?)\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex AuthorLabel();

    // Author ranges in header: "Автор запитань 1-18: ...", "Автора запитань 19–36: ...", "Авторка запитань 1-12: ...", "Автори запитань 1-6: ...", "Авторы запитань 1-6: ..." (Russian)
    [GeneratedRegex(@"^\s*Автор(?:а|и|ы|ка|ки)?\s+запитань\s+(\d+)\s*[-–—]\s*(\d+)\s*:\s*(.+)$", RegexOptions.IgnoreCase)]
    public static partial Regex AuthorRangeLabel();

    // Special markers
    // Matches: [Ведучому: ...], [Ведучим: ...], [Ведучому -(ій): ...], [Вказівка ведучому: ...], [Ведучій: ...]
    // Separator can be colon, dot, dash, or long dash. Colon can have no whitespace, others need whitespace after.
    // Uses alternation to handle different separator cases:
    // - Colon: [Ведучому: text] (traditional format, no whitespace required)
    // - Dot/dash: [Ведучому. text] or [Ведучому - text] (requires whitespace after)
    // Captures the instruction text inside brackets and any text after the closing bracket
    [GeneratedRegex(@"^\s*\[(?:Ведучому|Ведучим|Ведучій|Вказівка\s*ведучому)[^:]*(?::\s*|[.\-–—]\s+)([^\]]+)\]\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex HostInstructionsBracket();

    [GeneratedRegex(@"^\s*(?:Роздатка|Роздатковий\s*матері[ая]л)\s*[:\.]?\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex HandoutMarker();

    // Matches: [Роздатка: ...], [Роздатковий матеріал: ...], [Роздатковий матеріял: ...]
    // Captures the handout text inside brackets and any text after the closing bracket
    [GeneratedRegex(@"^\s*\[(?:Роздатка|Роздатковий\s*матері[ая]л)\s*[:\.]?\s*([^\]]*)\]\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex HandoutMarkerBracket();

    // Matches opening of a multiline bracketed handout (no closing bracket on same line)
    // Captures any text after the colon as handout content
    [GeneratedRegex(@"^\s*\[(?:Роздатка|Роздатковий\s*матері[ая]л)\s*[:\.]?\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex HandoutMarkerBracketOpen();

    // Matches a closing bracket with optional text after it (for multiline handouts)
    // Captures any text after the bracket as question text
    [GeneratedRegex(@"^\s*\]\s*(.*)$")]
    public static partial Regex HandoutMarkerBracketClose();

    // Editors in header: "Редактор:", "Редактори:", "Редактор туру:", "Редакторка:", "Редакторки:"
    // Also matches dash separators: "Редактор – Name", "Редактор - Name"
    [GeneratedRegex(@"^\s*(?:Редактор(?:и|ка|ки)?(?:\s*туру)?)\s*[-–—:]\s*(.+)$", RegexOptions.IgnoreCase)]
    public static partial Regex EditorsLabel();

    // Block detection: "Блок 1", "Блок", "Блок 2:", etc.
    [GeneratedRegex(@"^\s*Блок(?:\s+(\d+))?[\.:]?\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex BlockStart();

    // Block detection with author name: "Блок Станіслава Мерляна", "Блок Сергія Реви."
    // Matches "Блок" followed by one or more Cyrillic words (author name in genitive case).
    // The name is captured in group 1. Trailing period is optional.
    [GeneratedRegex(@"^\s*Блок\s+([А-ЯІЇЄҐа-яіїєґʼ'']+(?:\s+[А-ЯІЇЄҐа-яіїєґʼ'']+)*)\s*\.?\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex BlockStartWithName();

    // Block editors: "Редактор блоку:", "Редакторка блоку:", "Редактори блоку:", "Редакторки блоку:"
    // Also matches: "Редактор - Name", "Редакторка - Name" (with dash instead of colon)
    [GeneratedRegex(@"^\s*(?:Редактор(?:и|ка|ки)?(?:\s*блоку)?)\s*[-–—:]\s*(.+)$", RegexOptions.IgnoreCase)]
    public static partial Regex BlockEditorsLabel();
}

/// <summary>
/// Parser sections for state machine.
/// </summary>
public enum ParserSection
{
    PackageHeader,
    TourHeader,
    BlockHeader,
    QuestionText,
    HostInstructions,
    Handout,
    Answer,
    AcceptedAnswers,
    RejectedAnswers,
    Comment,
    Source,
    Authors
}

/// <summary>
/// Parses document blocks into structured package data.
/// </summary>
public class PackageParser
{
    private readonly ILogger<PackageParser> _logger;

    private enum NumberingMode
    {
        Unknown,
        PerTour,
        Global
    }

    private enum QuestionFormat
    {
        Unknown,
        Named,      // "Запитання N" or "Питання N"
        Numbered    // "N." or "N. text"
    }

    private sealed record AuthorRangeRule(int From, int To, List<string> Authors);

    /// <summary>
    /// Encapsulates mutable parsing state to reduce parameter passing and improve readability.
    /// </summary>
    private sealed class ParserContext
    {
        public ParseResult Result { get; } = new();
        public ParserSection CurrentSection { get; set; } = ParserSection.PackageHeader;
        public TourDto? CurrentTour { get; set; }
        public BlockDto? CurrentBlockDto { get; set; }
        public QuestionDto? CurrentQuestion { get; set; }
        public List<DocBlock> HeaderBlocks { get; } = [];
        public List<(AssetReference Asset, ParserSection Section)> PendingAssets { get; } = [];
        public List<AuthorRangeRule> AuthorRanges { get; } = [];
        public NumberingMode Mode { get; set; } = NumberingMode.Unknown;
        public QuestionFormat Format { get; set; } = QuestionFormat.Unknown;
        public int? ExpectedNextQuestionInTour { get; set; }
        public int? ExpectedNextQuestionGlobal { get; set; }
        public bool QuestionCreatedInCurrentBlock { get; set; }
        public DocBlock? CurrentBlock { get; set; }

        /// <summary>
        /// Indicates we're inside a multiline bracketed handout [Роздатка: ... ] that spans multiple lines.
        /// </summary>
        public bool InsideMultilineHandoutBracket { get; set; }

        /// <summary>
        /// Tracks asset filenames that have already been associated in the current block.
        /// Reset at the start of each block processing.
        /// </summary>
        public HashSet<string> AssociatedAssetFileNames { get; } = [];

        /// <summary>
        /// Tracks the section before transitioning to answer-related sections within a block.
        /// Used for associating assets with the correct section when processing mixed-content blocks.
        /// </summary>
        public ParserSection SectionBeforeAnswerRelated { get; set; } = ParserSection.QuestionText;

        /// <summary>
        /// Indicates a handout marker was detected in the current block.
        /// When true and transitioning to answer-related sections, assets are associated with handout.
        /// Reset at the start of each block processing.
        /// </summary>
        public bool HandoutMarkerDetectedInCurrentBlock { get; set; }

        /// <summary>
        /// Tracks the previous question when a new question is detected within the same block.
        /// Used at end-of-block processing to associate assets with the correct question.
        /// Reset at the start of each block processing.
        /// </summary>
        public QuestionDto? PreviousQuestionInBlock { get; set; }

        public bool HasCurrentQuestion => CurrentQuestion != null;
        public bool HasCurrentTour => CurrentTour != null;
        public bool HasCurrentBlock => CurrentBlockDto != null;
    }

    public PackageParser(ILogger<PackageParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses extracted document blocks into a package structure.
    /// </summary>
    public ParseResult Parse(List<DocBlock> blocks, List<AssetReference> assets)
    {
        var ctx = new ParserContext();

        _logger.LogInformation("Parsing {BlockCount} blocks", blocks.Count);

        // Pre-process: merge asset-only blocks backward to fix DOCX anchoring issues
        // Images in Word are often placed in empty paragraphs after the text they relate to
        var normalizedBlocks = MergeAssetOnlyBlocksBackward(blocks);

        if (normalizedBlocks.Count != blocks.Count)
        {
            _logger.LogDebug("Merged {MergedCount} asset-only blocks backward",
                blocks.Count - normalizedBlocks.Count);
        }

        foreach (var block in normalizedBlocks)
        {
            ProcessBlock(block, ctx);
        }

        FinalizeParsingResult(ctx);

        _logger.LogInformation(
            "Parsed {TourCount} tours, {QuestionCount} questions, confidence: {Confidence:P0}",
            ctx.Result.Tours.Count, ctx.Result.TotalQuestions, ctx.Result.Confidence);

        return ctx.Result;
    }

    /// <summary>
    /// Merges asset-only blocks (blocks with no meaningful text but with assets) backward
    /// into the previous textual block. This fixes DOCX anchoring issues where images
    /// are placed in empty paragraphs after the text they visually relate to.
    /// </summary>
    private static List<DocBlock> MergeAssetOnlyBlocksBackward(List<DocBlock> blocks)
    {
        var result = new List<DocBlock>();

        foreach (var block in blocks)
        {
            var text = TextNormalizer.NormalizeWhitespaceAndDashes(block.Text);
            var hasText = !string.IsNullOrWhiteSpace(text);

            // If this block has no text but has assets, merge assets to previous block
            if (!hasText && block.Assets.Count > 0 && result.Count > 0)
            {
                result[^1].Assets.AddRange(block.Assets);
                continue; // Drop this asset-only block
            }

            result.Add(block);
        }

        return result;
    }

    /// <summary>
    /// Processes a single document block (paragraph).
    /// </summary>
    private void ProcessBlock(DocBlock block, ParserContext ctx)
    {
        var text = NormalizeText(block.Text);
        
        // Handle empty blocks (blank paragraphs in DOCX) - append blank line to content sections
        if (string.IsNullOrWhiteSpace(text) && block.Assets.Count == 0)
        {
            if (ctx.CurrentQuestion != null)
            {
                if (IsContentSection(ctx.CurrentSection))
                {
                    AppendBlankLineToSection(ctx.CurrentSection, ctx.CurrentQuestion);
                }
                else if (ctx.CurrentSection == ParserSection.Authors)
                {
                    // A blank line after authors ends the author section to prevent
                    // subsequent text (epilogues, preambles) from being parsed as authors.
                    ctx.CurrentSection = ParserSection.Comment;
                }
            }
            return;
        }

        ctx.QuestionCreatedInCurrentBlock = false;
        ctx.CurrentBlock = block;
        ctx.AssociatedAssetFileNames.Clear();
        ctx.HandoutMarkerDetectedInCurrentBlock = false;
        ctx.PreviousQuestionInBlock = null;
        ctx.SectionBeforeAnswerRelated = IsAnswerRelatedSection(ctx.CurrentSection)
            ? ParserSection.QuestionText
            : ctx.CurrentSection;

        foreach (var line in SplitIntoLines(text))
        {
            // Handle blank lines - append to content sections to preserve paragraph structure
            if (string.IsNullOrWhiteSpace(line))
            {
                if (ctx.CurrentQuestion != null)
                {
                    if (IsContentSection(ctx.CurrentSection))
                    {
                        AppendBlankLineToSection(ctx.CurrentSection, ctx.CurrentQuestion);
                    }
                    else if (ctx.CurrentSection == ParserSection.Authors)
                    {
                        // A blank line after authors ends the author section to prevent
                        // subsequent text (epilogues, preambles) from being parsed as authors.
                        ctx.CurrentSection = ParserSection.Comment;
                    }
                }
                continue;
            }

            var sectionBeforeLine = ctx.CurrentSection;
            ProcessLine(line, ctx);

            // When transitioning to an answer-related section AND a handout marker was detected,
            // associate any remaining block assets with the pre-answer section (handout or question text).
            // This ensures assets in blocks with [Роздатковий матеріал: ...] followed by Коментар:
            // are correctly associated with handout, not comment.
            if (ctx.HandoutMarkerDetectedInCurrentBlock &&
                !IsAnswerRelatedSection(sectionBeforeLine) &&
                IsAnswerRelatedSection(ctx.CurrentSection))
            {
                ctx.SectionBeforeAnswerRelated = sectionBeforeLine;
                AssociateBlockAssetsBeforeAnswerSection(block.Assets, ctx);
            }
        }

        // Associate any remaining assets at the end of block processing
        AssociateBlockAssets(block.Assets, ctx);
    }

    /// <summary>
    /// Processes a single line of text within a block.
    /// </summary>
    private void ProcessLine(string line, ParserContext ctx)
    {
        // Handle multiline handout bracket closing first
        if (ctx.InsideMultilineHandoutBracket)
        {
            if (TryProcessMultilineHandoutBracketContent(line, ctx))
                return;
        }

        if (TryProcessTourStart(line, ctx)) return;
        if (TryProcessBlockStart(line, ctx)) return;
        if (TryProcessAuthorRange(line, ctx)) return;
        if (TryProcessQuestionStart(line, ctx)) return;
        if (TryCollectHeaderLine(line, ctx)) return;
        if (TryProcessHostInstructions(line, ctx)) return;
        if (TryProcessBracketedHandout(line, ctx)) return;

        ProcessLabelOrContent(line, ctx);
    }

    /// <summary>
    /// Processes content inside a multiline handout bracket or the closing bracket.
    /// </summary>
    private static bool TryProcessMultilineHandoutBracketContent(string line, ParserContext ctx)
    {
        // Check if this line closes the bracket (bracket at start of line)
        var closeMatch = ParserPatterns.HandoutMarkerBracketClose().Match(line);
        if (closeMatch.Success)
        {
            ctx.InsideMultilineHandoutBracket = false;
            ctx.CurrentSection = ParserSection.QuestionText;

            var afterBracket = closeMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(afterBracket) && ctx.HasCurrentQuestion)
            {
                ctx.CurrentQuestion!.Text = AppendText(ctx.CurrentQuestion.Text, TextNormalizer.NormalizeApostrophes(afterBracket)!);
            }

            return true;
        }

        // Check if this line contains a closing bracket mid-line (e.g., "Zoozeum] question text")
        var bracketIndex = line.IndexOf(']');
        if (bracketIndex >= 0)
        {
            ctx.InsideMultilineHandoutBracket = false;
            ctx.CurrentSection = ParserSection.QuestionText;

            // Text before the bracket is handout text
            var beforeBracket = line[..bracketIndex].Trim();
            if (!string.IsNullOrWhiteSpace(beforeBracket) && ctx.HasCurrentQuestion)
            {
                ctx.CurrentQuestion!.HandoutText = AppendText(ctx.CurrentQuestion.HandoutText, TextNormalizer.NormalizeApostrophes(beforeBracket)!);
            }

            // Text after the bracket is question text
            var afterBracket = line[(bracketIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(afterBracket) && ctx.HasCurrentQuestion)
            {
                ctx.CurrentQuestion!.Text = AppendText(ctx.CurrentQuestion.Text, TextNormalizer.NormalizeApostrophes(afterBracket)!);
            }

            return true;
        }

        // Line is still inside the handout bracket - add to handout text (unless it's just whitespace)
        if (!string.IsNullOrWhiteSpace(line) && ctx.HasCurrentQuestion)
        {
            ctx.CurrentQuestion!.HandoutText = AppendText(ctx.CurrentQuestion.HandoutText, TextNormalizer.NormalizeApostrophes(line)!);
        }

        return true;
    }

    /// <summary>
    /// Attempts to parse and handle a tour start line (including warmup tours).
    /// Also handles bold warmup question labels (e.g., "Розминочне питання") that create implicit warmup tours.
    /// </summary>
    private bool TryProcessTourStart(string line, ParserContext ctx)
    {
        var isWarmup = false;
        string tourNumber;
        string? preamble = null;

        // Check for warmup tour first
        if (TryParseWarmupTourStart(line))
        {
            isWarmup = true;
            tourNumber = "0";
        }
        // Check for bold warmup question label (only before any tours exist)
        else if (TryParseWarmupQuestionLabel(line, ctx))
        {
            isWarmup = true;
            tourNumber = "0";
        }
        else if (!TryParseTourStart(line, out tourNumber, out preamble))
        {
            return false;
        }

        SaveCurrentQuestion(ctx);
        SaveCurrentBlock(ctx);
        EnsurePackageHeaderParsed(ctx);

        var orderIndex = ctx.Result.Tours.Count;
        ctx.CurrentTour = new TourDto
        {
            Number = tourNumber,
            OrderIndex = orderIndex,
            IsWarmup = isWarmup,
            Preamble = preamble
        };
        ctx.Result.Tours.Add(ctx.CurrentTour);
        ctx.CurrentBlockDto = null;
        ctx.CurrentQuestion = null;
        ctx.CurrentSection = ParserSection.TourHeader;
        ctx.ExpectedNextQuestionInTour = null;
        ctx.Format = QuestionFormat.Unknown; // Allow each tour to use its own format

        _logger.LogDebug("Found tour: {TourNumber}, IsWarmup: {IsWarmup}", tourNumber, isWarmup);
        return true;
    }

    /// <summary>
    /// Attempts to parse and handle a block start line (e.g., "Блок 1", "Блок").
    /// Also handles named blocks like "Блок Станіслава Мерляна" where the author name
    /// is in genitive case and gets converted to nominative for the block editor.
    /// Blocks are optional subdivisions within a tour.
    /// </summary>
    private bool TryProcessBlockStart(string line, ParserContext ctx)
    {
        string? blockName = null;
        string? editorNameGenitive = null;

        // Try numbered/unnamed block first: "Блок 1", "Блок"
        var match = ParserPatterns.BlockStart().Match(line);
        if (match.Success)
        {
            blockName = match.Groups[1].Success ? match.Groups[1].Value : null;
        }
        else
        {
            // Try named block: "Блок Станіслава Мерляна", "Блок Сергія Реви."
            var namedMatch = ParserPatterns.BlockStartWithName().Match(line);
            if (!namedMatch.Success)
                return false;

            editorNameGenitive = namedMatch.Groups[1].Value.Trim();
        }

        // Blocks only make sense within a tour
        if (!ctx.HasCurrentTour)
            return false;

        SaveCurrentQuestion(ctx);
        SaveCurrentBlock(ctx);

        var orderIndex = ctx.CurrentTour!.Blocks.Count;

        ctx.CurrentBlockDto = new BlockDto
        {
            Name = blockName,
            OrderIndex = orderIndex
        };

        // For named blocks, convert the genitive author name to nominative and set as editor
        if (editorNameGenitive != null)
        {
            var nominativeName = UkrainianNameHelper.ConvertFullNameToNominative(editorNameGenitive);
            ctx.CurrentBlockDto.Editors.Add(StripAccents(TextNormalizer.NormalizeApostrophes(nominativeName)!));
        }

        ctx.CurrentTour.Blocks.Add(ctx.CurrentBlockDto);
        ctx.CurrentQuestion = null;
        ctx.CurrentSection = ParserSection.BlockHeader;

        _logger.LogDebug("Found block: {BlockName} (editor: {Editor}) in tour {TourNumber}",
            blockName ?? editorNameGenitive ?? "(unnamed)",
            editorNameGenitive != null ? UkrainianNameHelper.ConvertFullNameToNominative(editorNameGenitive) : "(none)",
            ctx.CurrentTour.Number);
        return true;
    }

    /// <summary>
    /// Saves the current block to the current tour if it exists and has questions.
    /// </summary>
    private static void SaveCurrentBlock(ParserContext ctx)
    {
        // Block is already added to the tour when created, so nothing to do here
        // This method exists for symmetry and potential future cleanup
    }

    /// <summary>
    /// Attempts to parse author range headers (e.g., "Автор запитань 1-18: ...").
    /// </summary>
    private static bool TryProcessAuthorRange(string line, ParserContext ctx)
    {
        if (ctx.HasCurrentQuestion)
            return false;

        var match = ParserPatterns.AuthorRangeLabel().Match(line);
        if (!match.Success)
            return false;

        var from = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var to = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var authors = ParseAuthorList(match.Groups[3].Value.Trim());

        if (authors.Count > 0)
            ctx.AuthorRanges.Add(new AuthorRangeRule(from, to, authors));

        return true;
    }

    /// <summary>
    /// Attempts to parse and handle a question start line.
    /// </summary>
    private bool TryProcessQuestionStart(string line, ParserContext ctx)
    {
        if (!TryParseQuestionStart(line, out var questionNumber, out var remainingText, out var detectedFormat))
            return false;

        // Tour-existence guard: question patterns before any tour are treated as preamble/header content.
        // This prevents numbered lines like "1. PayPal: email@example.com" from being parsed as questions.
        if (!ctx.HasCurrentTour)
        {
            return false;
        }

        // Context-based validation: don't allow "N." pattern in Source section
        // This prevents numbered list items in sources from being parsed as questions
        // But allow if it's in Authors section (which typically ends a question)
        if (detectedFormat == QuestionFormat.Numbered &&
            ctx.CurrentSection == ParserSection.Source)
        {
            ProcessAsRegularContent(line, ctx);
            return true;
        }

        // Format consistency validation: if first question used Named format,
        // require subsequent questions to also use Named format
        if (ctx.Format == QuestionFormat.Named && detectedFormat == QuestionFormat.Numbered)
        {
            ProcessAsRegularContent(line, ctx);
            return true;
        }

        if (!IsValidNextQuestionNumber(questionNumber, ctx))
        {
            ProcessAsRegularContent(line, ctx);
            return true;
        }

        // Set format on first question
        if (ctx.Format == QuestionFormat.Unknown)
            ctx.Format = detectedFormat;

        // Flush any pending assets to the CURRENT question before saving it
        // This ensures assets at the end of a question don't leak to the next question
        // (In practice, assets after a question starts are directly associated, not pending)
        FlushPendingAssetsToCurrentQuestion(ctx);

        // When a new question is detected, save reference to current question as previous.
        // This allows end-of-block processing to associate assets with the correct question.
        // The previous question could have been created in a prior block or earlier in this block.
        if (ctx.HasCurrentQuestion)
        {
            ctx.PreviousQuestionInBlock = ctx.CurrentQuestion;
        }

        SaveCurrentQuestion(ctx);
        EnsureDefaultTourExists(ctx);

        ctx.CurrentQuestion = new QuestionDto { Number = questionNumber };
        ctx.CurrentSection = ParserSection.QuestionText;
        ctx.QuestionCreatedInCurrentBlock = true;

        // Reset handout marker tracking for the new question within the same block
        ctx.HandoutMarkerDetectedInCurrentBlock = false;

        // Apply any pending assets from before the first question
        ApplyPendingAssetsToNewQuestion(ctx);

        ProcessRemainingTextAfterQuestionNumber(remainingText, ctx);

        _logger.LogDebug("Found question: {QuestionNumber}", questionNumber);
        return true;
    }

    /// <summary>
    /// Collects blocks into header blocks when no tour has been started yet.
    /// </summary>
    private static bool TryCollectHeaderLine(string line, ParserContext ctx)
    {
        if (ctx.HasCurrentTour)
            return false;

        // Add the current block to header blocks (only add once per block)
        if (ctx.CurrentBlock != null && !ctx.HeaderBlocks.Contains(ctx.CurrentBlock))
            ctx.HeaderBlocks.Add(ctx.CurrentBlock);

        return true;
    }

    /// <summary>
    /// Attempts to extract and process host instructions [Ведучому: ...].
    /// </summary>
    private static bool TryProcessHostInstructions(string line, ParserContext ctx)
    {
        if (!ctx.HasCurrentQuestion)
            return false;

        if (!TryExtractHostInstructions(line, out var instructions, out var afterInstructions))
            return false;

        ctx.CurrentQuestion!.HostInstructions = AppendText(ctx.CurrentQuestion.HostInstructions, TextNormalizer.NormalizeApostrophes(instructions)!);

        if (!string.IsNullOrWhiteSpace(afterInstructions))
            ctx.CurrentQuestion.Text = AppendText(ctx.CurrentQuestion.Text, TextNormalizer.NormalizeApostrophes(afterInstructions)!);

        return true;
    }

    /// <summary>
    /// Attempts to extract and process bracketed handout [Роздатка: ...].
    /// Handles both single-line and multiline (opening) bracketed handouts.
    /// </summary>
    private static bool TryProcessBracketedHandout(string line, ParserContext ctx)
    {
        // Try single-line bracket first (has closing bracket on same line)
        if (TryExtractBracketedHandout(line, out var handoutText, out var afterHandout))
        {
            ctx.HandoutMarkerDetectedInCurrentBlock = true;

            if (ctx.HasCurrentQuestion && !string.IsNullOrWhiteSpace(handoutText))
                ctx.CurrentQuestion!.HandoutText = AppendText(ctx.CurrentQuestion.HandoutText, TextNormalizer.NormalizeApostrophes(handoutText)!);

            if (!string.IsNullOrWhiteSpace(afterHandout) && ctx.HasCurrentQuestion)
                ctx.CurrentQuestion!.Text = AppendText(ctx.CurrentQuestion.Text, TextNormalizer.NormalizeApostrophes(afterHandout)!);

            // Single-line bracketed handout is complete, subsequent lines are question text
            ctx.CurrentSection = ParserSection.QuestionText;

            return true;
        }

        // Try multiline bracket opening (no closing bracket on this line)
        if (TryExtractMultilineHandoutOpening(line, out var openingHandoutText))
        {
            ctx.HandoutMarkerDetectedInCurrentBlock = true;
            ctx.CurrentSection = ParserSection.Handout;
            ctx.InsideMultilineHandoutBracket = true;

            if (ctx.HasCurrentQuestion && !string.IsNullOrWhiteSpace(openingHandoutText))
                ctx.CurrentQuestion!.HandoutText = AppendText(ctx.CurrentQuestion.HandoutText, TextNormalizer.NormalizeApostrophes(openingHandoutText)!);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Processes a line that may contain a section label or regular content.
    /// Handles multiple inline labels on the same line (e.g., "Відповідь: answer. Залік: accepted").
    /// </summary>
    private static void ProcessLabelOrContent(string line, ParserContext ctx)
    {
        var (newSection, remainder) = DetectLabel(line);
        if (newSection != null)
        {
            ctx.CurrentSection = newSection.Value;
            line = remainder;

            // Track when handout marker is detected via label
            if (newSection == ParserSection.Handout)
                ctx.HandoutMarkerDetectedInCurrentBlock = true;
        }

        if (string.IsNullOrWhiteSpace(line))
            return;

        // Handle standalone '[' that starts multiline handout content
        // This handles format like:
        // Роздатковий матеріал:
        // [
        // content
        // ]
        if (ctx.CurrentSection == ParserSection.Handout && line == "[")
        {
            ctx.InsideMultilineHandoutBracket = true;
            return;
        }

        // Handle standalone ']' that closes handout section
        if (ctx.CurrentSection == ParserSection.Handout && line == "]")
        {
            ctx.InsideMultilineHandoutBracket = false;
            ctx.CurrentSection = ParserSection.QuestionText;
            return;
        }

        // Handle '[ content ]' or '[ ]' on same line in handout section
        // This handles format like:
        // Роздатковий матеріал:
        // [ <imageAsset> ]
        // Question text
        if (ctx.CurrentSection == ParserSection.Handout &&
            line.StartsWith('[') && line.EndsWith(']'))
        {
            var content = line[1..^1].Trim();
            if (!string.IsNullOrWhiteSpace(content) && ctx.HasCurrentQuestion)
            {
                ctx.CurrentQuestion!.HandoutText = AppendText(ctx.CurrentQuestion.HandoutText, TextNormalizer.NormalizeApostrophes(content)!);
            }
            // Section stays as Handout - next non-bracket line will be question text
            // The asset association happens after ProcessBlock, so assets will be associated correctly
            ctx.CurrentSection = ParserSection.QuestionText;
            return;
        }

        // Handle '[content...' that opens a multiline bracket scope in handout section
        // This handles format like:
        // Роздатковий матеріал:
        // [Розчулена музикою спiлкування...
        // ...ще рядок тексту...
        // ...яку я купувала в шкiльному буфетi.]
        // Question text
        if (ctx.CurrentSection == ParserSection.Handout &&
            line.StartsWith('[') && !line.EndsWith(']'))
        {
            ctx.InsideMultilineHandoutBracket = true;
            var content = line[1..].Trim();
            if (!string.IsNullOrWhiteSpace(content) && ctx.HasCurrentQuestion)
            {
                ctx.CurrentQuestion!.HandoutText = AppendText(ctx.CurrentQuestion.HandoutText, TextNormalizer.NormalizeApostrophes(content)!);
            }
            return;
        }

        // Handle '...content]' that closes a bracket scope in handout section
        // This handles lines ending with ']' when we're in Handout section
        // (the InsideMultilineHandoutBracket case is handled earlier in ProcessLine)
        if (ctx.CurrentSection == ParserSection.Handout && line.EndsWith(']'))
        {
            var content = line[..^1].Trim();
            if (!string.IsNullOrWhiteSpace(content) && ctx.HasCurrentQuestion)
            {
                ctx.CurrentQuestion!.HandoutText = AppendText(ctx.CurrentQuestion.HandoutText, TextNormalizer.NormalizeApostrophes(content)!);
            }
            ctx.CurrentSection = ParserSection.QuestionText;
            return;
        }

        // Check if there's another label inline (e.g., "answer text. Залік: accepted")
        var inlineLabelIndex = FindInlineLabelStart(line);
        if (inlineLabelIndex > 0)
        {
            // Process the text before the inline label
            var beforeLabel = line[..inlineLabelIndex].Trim();
            if (!string.IsNullOrWhiteSpace(beforeLabel))
                AppendToSection(ctx.CurrentSection, beforeLabel, ctx.CurrentQuestion, ctx.CurrentTour!, ctx.CurrentBlockDto, ctx.Result);

            // Recursively process the inline label and its content
            var labelAndAfter = line[inlineLabelIndex..];
            ProcessLabelOrContent(labelAndAfter, ctx);
        }
        else
        {
            AppendToSection(ctx.CurrentSection, line, ctx.CurrentQuestion, ctx.CurrentTour!, ctx.CurrentBlockDto, ctx.Result);
        }
    }

    /// <summary>
    /// Finds the start index of an inline label in text.
    /// Returns -1 if no inline label is found, or the index of the label start.
    /// Only Залік and Незалік can appear inline (mid-line).
    /// All other labels (Відповідь, Коментар, Джерело, Автор) must appear at line start.
    /// </summary>
    private static int FindInlineLabelStart(string text)
    {
        var minIndex = int.MaxValue;

        // Only Залік/Незалік variants can appear inline within answer text.
        // Other labels (Відповідь, Коментар, Джерело, Автор, Роздатка) must be at line start.
        // This prevents phrases like "Дайте відповідь:" in question text from being parsed as answer labels.
        string[] labelKeywords =
        [
            "Залік:",
            "Заліки:",
            "Незалік:",
            "Не залік:",
            "Не приймається:"
        ];

        foreach (var keyword in labelKeywords)
        {
            var index = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (index > 0 && index < minIndex)
            {
                minIndex = index;
            }
        }

        return minIndex == int.MaxValue ? -1 : minIndex;
    }

    /// <summary>
    /// Handles a line that looked like a question start but failed validation.
    /// </summary>
    private static void ProcessAsRegularContent(string line, ParserContext ctx)
    {
        if (ctx.HasCurrentTour)
        {
            ProcessLabelOrContent(line, ctx);
        }
        else
        {
            // Add the current block to header blocks (only add once per block)
            if (ctx.CurrentBlock != null && !ctx.HeaderBlocks.Contains(ctx.CurrentBlock))
                ctx.HeaderBlocks.Add(ctx.CurrentBlock);
        }
    }

    /// <summary>
    /// Processes any remaining text after a question number (e.g., "1. [Роздатка: ...] Question text").
    /// </summary>
    private static void ProcessRemainingTextAfterQuestionNumber(string remainingText, ParserContext ctx)
    {
        if (string.IsNullOrWhiteSpace(remainingText))
            return;

        var question = ctx.CurrentQuestion!;

        // Try handout marker label (e.g., "1. Роздатковий матеріал:" or "1. Роздатка:")
        var handoutMatch = ParserPatterns.HandoutMarker().Match(remainingText);
        if (handoutMatch.Success)
        {
            ctx.HandoutMarkerDetectedInCurrentBlock = true;
            ctx.CurrentSection = ParserSection.Handout;
            var handoutContent = handoutMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(handoutContent))
                question.HandoutText = AppendText(question.HandoutText, TextNormalizer.NormalizeApostrophes(handoutContent)!);
            return;
        }

        // Try single-line bracketed handout
        if (TryExtractBracketedHandout(remainingText, out var handout, out var afterHandout))
        {
            ctx.HandoutMarkerDetectedInCurrentBlock = true;

            // A complete single-line bracket [Роздатка: text] means handout is done,
            // so section resets to QuestionText. This matches TryProcessBracketedHandout behavior.
            ctx.CurrentSection = ParserSection.QuestionText;

            if (!string.IsNullOrWhiteSpace(handout))
                question.HandoutText = AppendText(question.HandoutText, TextNormalizer.NormalizeApostrophes(handout)!);

            if (!string.IsNullOrWhiteSpace(afterHandout))
                question.Text = AppendText(question.Text, TextNormalizer.NormalizeApostrophes(afterHandout)!);

            return;
        }

        // Try multiline bracketed handout opening
        if (TryExtractMultilineHandoutOpening(remainingText, out var openingHandout))
        {
            ctx.HandoutMarkerDetectedInCurrentBlock = true;
            ctx.CurrentSection = ParserSection.Handout;
            ctx.InsideMultilineHandoutBracket = true;

            if (!string.IsNullOrWhiteSpace(openingHandout))
                question.HandoutText = AppendText(question.HandoutText, TextNormalizer.NormalizeApostrophes(openingHandout)!);

            return;
        }

        // Try host instructions (e.g., "6. [Ведучому: ...] Question text")
        if (TryExtractHostInstructions(remainingText, out var instructions, out var afterInstructions))
        {
            question.HostInstructions = AppendText(question.HostInstructions, TextNormalizer.NormalizeApostrophes(instructions)!);

            if (!string.IsNullOrWhiteSpace(afterInstructions))
                question.Text = AppendText(question.Text, TextNormalizer.NormalizeApostrophes(afterInstructions)!);

            return;
        }

        // Regular question text
        question.Text = AppendText(question.Text, TextNormalizer.NormalizeApostrophes(remainingText)!);
    }

    /// <summary>
    /// Checks if a section is answer-related (Answer, AcceptedAnswers, RejectedAnswers, Comment, Source, Authors).
    /// Assets found in these sections should be associated as comment assets.
    /// </summary>
    private static bool IsAnswerRelatedSection(ParserSection section) =>
        section is ParserSection.Answer or
                   ParserSection.AcceptedAnswers or
                   ParserSection.RejectedAnswers or
                   ParserSection.Comment or
                   ParserSection.Source or
                   ParserSection.Authors;

    /// <summary>
    /// Checks if a section is a content section that can contain multiline text with blank lines.
    /// </summary>
    private static bool IsContentSection(ParserSection section) =>
        section is ParserSection.QuestionText or
                   ParserSection.Handout or
                   ParserSection.HostInstructions or
                   ParserSection.Answer or
                   ParserSection.AcceptedAnswers or
                   ParserSection.RejectedAnswers or
                   ParserSection.Comment or
                   ParserSection.Source;

    /// <summary>
    /// Appends a blank line to the appropriate field of the question based on the current section.
    /// </summary>
    private static void AppendBlankLineToSection(ParserSection section, QuestionDto question)
    {
        switch (section)
        {
            case ParserSection.QuestionText:
                question.Text = AppendBlankLine(question.Text);
                break;
            case ParserSection.Handout:
                question.HandoutText = AppendBlankLine(question.HandoutText);
                break;
            case ParserSection.HostInstructions:
                question.HostInstructions = AppendBlankLine(question.HostInstructions);
                break;
            case ParserSection.Answer:
                question.Answer = AppendBlankLine(question.Answer);
                break;
            case ParserSection.AcceptedAnswers:
                question.AcceptedAnswers = AppendBlankLine(question.AcceptedAnswers);
                break;
            case ParserSection.RejectedAnswers:
                question.RejectedAnswers = AppendBlankLine(question.RejectedAnswers);
                break;
            case ParserSection.Comment:
                question.Comment = AppendBlankLine(question.Comment);
                break;
            case ParserSection.Source:
                question.Source = AppendBlankLine(question.Source);
                break;
        }
    }

    /// <summary>
    /// Associates block assets with handout section when transitioning to answer-related sections.
    /// This ensures assets appearing before answers/comments in the same block are correctly
    /// associated with the handout rather than the comment.
    /// </summary>
    private static void AssociateBlockAssetsBeforeAnswerSection(List<AssetReference> assets, ParserContext ctx)
    {
        foreach (var asset in assets)
        {
            // Skip if already associated
            if (ctx.AssociatedAssetFileNames.Contains(asset.FileName))
                continue;

            if (ctx.HasCurrentQuestion)
            {
                // Force association with pre-answer section (handout or question text)
                var section = ctx.InsideMultilineHandoutBracket
                    ? ParserSection.Handout
                    : ctx.SectionBeforeAnswerRelated;
                AssociateAsset(asset, section, ctx.CurrentQuestion, ctx.Result);
                ctx.AssociatedAssetFileNames.Add(asset.FileName);
            }
            else
            {
                // No question yet - add to pending assets for later association
                ctx.PendingAssets.Add((asset, ctx.SectionBeforeAnswerRelated));
                ctx.AssociatedAssetFileNames.Add(asset.FileName);
            }
        }
    }

    /// <summary>
    /// Associates block assets with the current question or adds them to pending assets.
    /// Skips assets that have already been associated in the current block.
    ///
    /// For multi-question blocks (where QuestionCreatedInCurrentBlock is true and
    /// PreviousQuestionInBlock is set), the logic depends on whether a handout marker
    /// was detected for the current question:
    /// - If handout marker detected: associate with current question's handout
    /// - Otherwise: distribute assets between previous and current questions
    /// </summary>
    private static void AssociateBlockAssets(List<AssetReference> assets, ParserContext ctx)
    {
        // Get unassociated assets
        var unassociatedAssets = assets
            .Where(a => !ctx.AssociatedAssetFileNames.Contains(a.FileName))
            .ToList();

        if (unassociatedAssets.Count == 0)
            return;

        // If we have a current question, associate the assets
        if (ctx.HasCurrentQuestion)
        {
            // When inside a multiline handout bracket, force association with handout section
            var section = ctx.InsideMultilineHandoutBracket ? ParserSection.Handout : ctx.CurrentSection;

            // For multi-question blocks without handout marker for current question:
            // Distribute assets between previous and current questions
            if (ctx.PreviousQuestionInBlock != null && !ctx.HandoutMarkerDetectedInCurrentBlock)
            {
                // First asset goes to previous question (as comment)
                // Remaining assets go to current question (as comment)
                var firstAsset = unassociatedAssets[0];
                AssociateAsset(firstAsset, ParserSection.Comment, ctx.PreviousQuestionInBlock, ctx.Result);
                ctx.AssociatedAssetFileNames.Add(firstAsset.FileName);

                // Associate remaining assets with current question
                foreach (var asset in unassociatedAssets.Skip(1))
                {
                    AssociateAsset(asset, ParserSection.Comment, ctx.CurrentQuestion, ctx.Result);
                    ctx.AssociatedAssetFileNames.Add(asset.FileName);
                }
            }
            else
            {
                // Normal case: associate all assets with current question
                foreach (var asset in unassociatedAssets)
                {
                    AssociateAsset(asset, section, ctx.CurrentQuestion, ctx.Result);
                    ctx.AssociatedAssetFileNames.Add(asset.FileName);
                }
            }
        }
        else
        {
            // No question yet - add to pending assets for later association
            foreach (var asset in unassociatedAssets)
            {
                ctx.PendingAssets.Add((asset, ctx.CurrentSection));
                ctx.AssociatedAssetFileNames.Add(asset.FileName);
            }
        }
    }

    /// <summary>
    /// Flushes pending assets to the current question before transitioning to a new question.
    /// This ensures assets at the end of a question don't leak to the next question.
    /// </summary>
    private static void FlushPendingAssetsToCurrentQuestion(ParserContext ctx)
    {
        if (!ctx.HasCurrentQuestion || ctx.PendingAssets.Count == 0)
            return;

        foreach (var (asset, section) in ctx.PendingAssets)
        {
            // For assets that were pending before we had a question,
            // associate them based on their original section context
            AssociateAsset(asset, section, ctx.CurrentQuestion, ctx.Result);
        }

        ctx.PendingAssets.Clear();
    }

    /// <summary>
    /// Associates remaining unassociated block assets with a specific question.
    /// Called when transitioning to a new question within the same block to ensure
    /// assets belonging to the previous question don't leak to the next one.
    /// Assets are associated as comment assets since question/answer content has been processed.
    /// </summary>
    private static void AssociateRemainingBlockAssetsToQuestion(
        List<AssetReference> assets,
        QuestionDto question,
        ParserContext ctx)
    {
        foreach (var asset in assets)
        {
            // Skip if already associated in this block
            if (ctx.AssociatedAssetFileNames.Contains(asset.FileName))
                continue;

            // Associate as comment asset (since we've processed Q/A content for this question)
            AssociateAsset(asset, ParserSection.Comment, question, ctx.Result);
            ctx.AssociatedAssetFileNames.Add(asset.FileName);
        }
    }

    /// <summary>
    /// Applies pending assets to a newly created question.
    /// These are typically assets that appeared in header/tour sections before any question.
    /// </summary>
    private static void ApplyPendingAssetsToNewQuestion(ParserContext ctx)
    {
        foreach (var (asset, section) in ctx.PendingAssets)
            AssociateAsset(asset, section, ctx.CurrentQuestion, ctx.Result);

        ctx.PendingAssets.Clear();
    }

    /// <summary>
    /// Saves the current question to the current block (if any) or tour.
    /// </summary>
    private static void SaveCurrentQuestion(ParserContext ctx)
    {
        if (ctx.CurrentQuestion == null || ctx.CurrentTour == null)
            return;

        FinalizeQuestion(ctx.CurrentQuestion, ctx.AuthorRanges, ctx.Result);

        // If we have a current block, add question to the block; otherwise add directly to tour
        if (ctx.HasCurrentBlock)
        {
            ctx.CurrentBlockDto!.Questions.Add(ctx.CurrentQuestion);
        }
        else
        {
            ctx.CurrentTour.Questions.Add(ctx.CurrentQuestion);
        }
    }

    /// <summary>
    /// Ensures the package header is parsed before starting a tour.
    /// </summary>
    private static void EnsurePackageHeaderParsed(ParserContext ctx)
    {
        if (ctx.CurrentTour == null && ctx.HeaderBlocks.Count > 0)
            ParsePackageHeader(ctx.HeaderBlocks, ctx.Result);
    }

    /// <summary>
    /// Ensures a default tour exists when a question is found without a tour.
    /// </summary>
    private static void EnsureDefaultTourExists(ParserContext ctx)
    {
        if (ctx.CurrentTour != null)
            return;

        if (ctx.HeaderBlocks.Count > 0)
            ParsePackageHeader(ctx.HeaderBlocks, ctx.Result);

        ctx.CurrentTour = new TourDto { Number = "1" };
        ctx.Result.Tours.Add(ctx.CurrentTour);
        ctx.Result.Warnings.Add("Тур не знайдено, створено тур за замовчуванням");
    }

    /// <summary>
    /// Finalizes the parsing result: saves the last question, parses remaining header, and calculates confidence.
    /// </summary>
    private static void FinalizeParsingResult(ParserContext ctx)
    {
        SaveCurrentQuestion(ctx);

        if (ctx.Result.Tours.Count == 0 && ctx.HeaderBlocks.Count > 0)
            ParsePackageHeader(ctx.HeaderBlocks, ctx.Result);

        // Ensure warmup tour is at the front if present
        EnsureWarmupTourFirst(ctx.Result);

        // Detect and set numbering mode
        DetectNumberingMode(ctx);

        // Assign OrderIndex values
        AssignTourOrderIndices(ctx.Result);

        // Trim leading/trailing blank lines from all question text fields
        TrimQuestionBlankLines(ctx.Result);

        CalculateConfidence(ctx.Result);
    }

    /// <summary>
    /// Trims leading and trailing blank lines from all question text fields.
    /// Preserves internal blank lines for paragraph structure.
    /// </summary>
    private static void TrimQuestionBlankLines(ParseResult result)
    {
        foreach (var tour in result.Tours)
        {
            foreach (var question in tour.Questions)
            {
                question.Text = TrimBlankLines(question.Text);
                question.Answer = TrimBlankLines(question.Answer);
                question.AcceptedAnswers = TrimBlankLines(question.AcceptedAnswers);
                question.RejectedAnswers = TrimBlankLines(question.RejectedAnswers);
                question.Comment = TrimBlankLines(question.Comment);
                question.Source = TrimBlankLines(question.Source);
                question.HandoutText = TrimBlankLines(question.HandoutText);
                question.HostInstructions = TrimBlankLines(question.HostInstructions);
            }
        }
    }

    /// <summary>
    /// Ensures the warmup tour (if any) is positioned first in the tour list.
    /// </summary>
    private static void EnsureWarmupTourFirst(ParseResult result)
    {
        var warmupTour = result.Tours.FirstOrDefault(t => t.IsWarmup);
        if (warmupTour == null)
            return;

        var warmupIndex = result.Tours.IndexOf(warmupTour);
        if (warmupIndex <= 0)
            return; // Already first or not found

        // Move warmup to front
        result.Tours.RemoveAt(warmupIndex);
        result.Tours.Insert(0, warmupTour);
    }

    /// <summary>
    /// Assigns sequential OrderIndex values to tours.
    /// </summary>
    private static void AssignTourOrderIndices(ParseResult result)
    {
        for (int i = 0; i < result.Tours.Count; i++)
        {
            result.Tours[i].OrderIndex = i;
        }
    }

    /// <summary>
    /// Detects the numbering mode based on parsed questions.
    /// </summary>
    private static void DetectNumberingMode(ParserContext ctx)
    {
        var result = ctx.Result;

        // Check if any question has non-numeric number (suggests Manual mode)
        var hasNonNumericNumbers = result.Tours
            .SelectMany(t => t.Questions)
            .Any(q => !int.TryParse(q.Number, out _));

        if (hasNonNumericNumbers)
        {
            result.NumberingMode = QuestionNumberingMode.Manual;
            return;
        }

        // Use the detected mode from parsing
        result.NumberingMode = ctx.Mode switch
        {
            NumberingMode.PerTour => QuestionNumberingMode.PerTour,
            NumberingMode.Global => QuestionNumberingMode.Global,
            _ => QuestionNumberingMode.Global // Default to Global if unknown
        };
    }

    /// <summary>
    /// Validates question numbering using the context's numbering state.
    /// </summary>
    private static bool IsValidNextQuestionNumber(string questionNumberStr, ParserContext ctx)
    {
        var expectedInTour = ctx.ExpectedNextQuestionInTour;
        var expectedGlobal = ctx.ExpectedNextQuestionGlobal;
        var mode = ctx.Mode;

        var result = IsValidNextQuestionNumber(
            questionNumberStr,
            ref expectedInTour,
            ref expectedGlobal,
            ref mode);

        ctx.ExpectedNextQuestionInTour = expectedInTour;
        ctx.ExpectedNextQuestionGlobal = expectedGlobal;
        ctx.Mode = mode;

        return result;
    }

    /// <summary>
    /// Splits text into lines, trimming each line individually.
    /// Preserves blank lines to maintain paragraph structure.
    /// </summary>
    private static IEnumerable<string> SplitIntoLines(string text)
    {
        return text.Split('\n')
            .Select(line => line.Trim());
    }

    /// <summary>
    /// Splits text into non-empty, trimmed lines.
    /// Use this when blank lines should be ignored (e.g., for structural parsing).
    /// </summary>
    private static IEnumerable<string> SplitIntoNonEmptyLines(string text)
    {
        return text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line));
    }

    private static string NormalizeText(string text)
    {
        return TextNormalizer.NormalizeWhitespaceAndDashes(text) ?? "";
    }

    private static bool TryParseTourStart(string text, out string tourNumber, out string? preamble)
    {
        tourNumber = "";
        preamble = null;

        // Try numeric patterns first (without preamble)
        if (TryMatchFirst(text, out var match, ParserPatterns.TourStart(), ParserPatterns.TourStartWithColon(), ParserPatterns.TourStartDashed(), ParserPatterns.NumberTourStart(), ParserPatterns.TourNumberSignStart()))
        {
            tourNumber = match.Groups[1].Value;
            return true;
        }

        // Try pattern with preamble: "Тур 2. Лірики", "Тур 3: Фізлірики"
        var preambleMatch = ParserPatterns.TourStartWithPreamble().Match(text);
        if (preambleMatch.Success)
        {
            tourNumber = preambleMatch.Groups[1].Value;
            preamble = preambleMatch.Groups[2].Value.Trim();
            return true;
        }

        // Try Roman numeral patterns: "ТУР III", "Тур ІІ", "ТУР ІІІ"
        var romanMatch = ParserPatterns.TourRomanStart().Match(text);
        if (romanMatch.Success)
        {
            var romanNumber = RomanToNumber(romanMatch.Groups[1].Value);
            if (romanNumber != null)
            {
                tourNumber = romanNumber;
                return true;
            }
        }

        // Try Roman numeral with preamble: "ТУР III. Назва"
        var romanPreambleMatch = ParserPatterns.TourRomanStartWithPreamble().Match(text);
        if (romanPreambleMatch.Success)
        {
            var romanNumber = RomanToNumber(romanPreambleMatch.Groups[1].Value);
            if (romanNumber != null)
            {
                tourNumber = romanNumber;
                preamble = romanPreambleMatch.Groups[2].Value.Trim();
                return true;
            }
        }

        // Try ordinal patterns (normalize apostrophes for matching)
        var normalizedText = TextNormalizer.NormalizeApostrophes(text) ?? text;

        var ordinalMatch = ParserPatterns.OrdinalTourStart().Match(normalizedText);
        if (ordinalMatch.Success)
        {
            tourNumber = OrdinalToNumber(ordinalMatch.Groups[1].Value);
            return true;
        }

        var tourOrdinalMatch = ParserPatterns.TourOrdinalStart().Match(normalizedText);
        if (tourOrdinalMatch.Success)
        {
            tourNumber = OrdinalToNumber(tourOrdinalMatch.Groups[1].Value);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Converts a Roman numeral string to its decimal string representation.
    /// Handles Cyrillic lookalike characters: І (U+0456) → I, Х (U+0425/0445) → X.
    /// Returns null if the input is not a valid Roman numeral.
    /// </summary>
    private static string? RomanToNumber(string roman)
    {
        // Normalize Cyrillic lookalikes to Latin equivalents
        var normalized = roman
            .Replace('І', 'I')  // Cyrillic І (U+0406) → Latin I
            .Replace('і', 'I')  // Cyrillic і (U+0456) → Latin I
            .Replace('Х', 'X')  // Cyrillic Х (U+0425) → Latin X
            .Replace('х', 'X')  // Cyrillic х (U+0445) → Latin X
            .ToUpperInvariant();

        var values = new Dictionary<char, int>
        {
            ['I'] = 1, ['V'] = 5, ['X'] = 10,
            ['L'] = 50, ['C'] = 100, ['D'] = 500, ['M'] = 1000
        };

        var result = 0;
        var prevValue = 0;

        // Process right to left for subtractive notation (IV=4, IX=9, etc.)
        for (var i = normalized.Length - 1; i >= 0; i--)
        {
            if (!values.TryGetValue(normalized[i], out var value))
                return null; // Invalid character

            if (value < prevValue)
                result -= value;
            else
                result += value;

            prevValue = value;
        }

        // Sanity check: must be positive and within reasonable range
        if (result <= 0 || result > 50)
            return null;

        return result.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Maps Ukrainian ordinal words to their numeric values (1-9).
    /// </summary>
    private static string OrdinalToNumber(string ordinal)
    {
        // Normalize apostrophes and convert to lowercase for matching
        var normalized = TextNormalizer.NormalizeApostrophes(ordinal)?.ToLowerInvariant() ?? ordinal.ToLowerInvariant();

        // Use StartsWith for more robust matching against potential encoding variations
        if (normalized.StartsWith("перш")) return "1";
        if (normalized.StartsWith("друг")) return "2";
        if (normalized.StartsWith("трет")) return "3";
        if (normalized.StartsWith("четв")) return "4";
        if (normalized.StartsWith("п") && normalized.Contains("ят")) return "5";
        if (normalized.StartsWith("шост")) return "6";
        if (normalized.StartsWith("сьом") || normalized.StartsWith("сём")) return "7";
        if (normalized.StartsWith("вось")) return "8";
        if (normalized.StartsWith("дев")) return "9";

        return "1"; // Fallback, should not happen if regex matches
    }

    private static bool TryParseWarmupTourStart(string text)
    {
        return ParserPatterns.WarmupTourStart().IsMatch(text) ||
               ParserPatterns.WarmupTourStartDashed().IsMatch(text) ||
               ParserPatterns.TourZeroStart().IsMatch(text);
    }

    /// <summary>
    /// Checks if the line is a bold warmup question label that creates an implicit warmup tour.
    /// Only matches when: text matches warmup label pattern, block is bold, and no tours exist yet.
    /// </summary>
    private static bool TryParseWarmupQuestionLabel(string text, ParserContext ctx)
    {
        // Only valid before any tours exist
        if (ctx.Result.Tours.Count > 0)
            return false;

        // Must be bold
        if (ctx.CurrentBlock is not { IsBold: true })
            return false;

        return ParserPatterns.WarmupQuestionLabel().IsMatch(text);
    }

    private static bool TryParseQuestionStart(string text, out string questionNumber, out string remainingText, out QuestionFormat format)
    {
        questionNumber = "";
        remainingText = "";
        format = QuestionFormat.Unknown;

        // Try named format with text first (Запитання N. text / Питання N: text)
        var namedWithTextMatch = ParserPatterns.QuestionStartNamedWithText().Match(text);
        if (namedWithTextMatch.Success)
        {
            questionNumber = namedWithTextMatch.Groups[1].Value;
            remainingText = namedWithTextMatch.Groups[2].Value.Trim();
            format = QuestionFormat.Named;
            return true;
        }

        // Try named format without text (Запитання N / Питання N)
        var namedMatch = ParserPatterns.QuestionStartNamed().Match(text);
        if (namedMatch.Success)
        {
            questionNumber = namedMatch.Groups[1].Value;
            format = QuestionFormat.Named;
            return true;
        }

        // Try numbered format with text (N. text)
        var withTextMatch = ParserPatterns.QuestionStartWithText().Match(text);
        if (withTextMatch.Success)
        {
            questionNumber = withTextMatch.Groups[1].Value;
            remainingText = withTextMatch.Groups[2].Value.Trim();
            format = QuestionFormat.Numbered;
            return true;
        }

        // Try numbered format without text (N.)
        var numberOnlyMatch = ParserPatterns.QuestionStartNumberOnly().Match(text);
        if (numberOnlyMatch.Success)
        {
            questionNumber = numberOnlyMatch.Groups[1].Value;
            format = QuestionFormat.Numbered;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tries to match text against multiple regex patterns, returning the first successful match.
    /// </summary>
    private static bool TryMatchFirst(string text, out Match match, params Regex[] patterns)
    {
        foreach (var pattern in patterns)
        {
            match = pattern.Match(text);
            if (match.Success)
                return true;
        }

        match = Match.Empty;
        return false;
    }

    /// <summary>
    /// Validates question numbering. Chooses numbering mode (Global vs PerTour) when a tour boundary is observed.
    /// </summary>
    private static bool IsValidNextQuestionNumber(
        string questionNumberStr,
        ref int? expectedNextQuestionInTour,
        ref int? expectedNextQuestionGlobal,
        ref NumberingMode mode)
    {
        if (!int.TryParse(questionNumberStr, out var qn))
            return false;

        // First question overall
        if (expectedNextQuestionGlobal == null)
        {
            if (qn is 0 or 1)
            {
                expectedNextQuestionGlobal = qn + 1;
                expectedNextQuestionInTour = qn + 1;
                return true;
            }
            return false;
        }

        var isFirstQuestionInTour = expectedNextQuestionInTour == null;

        if (isFirstQuestionInTour)
        {
            // Decide mode if unknown
            if (mode == NumberingMode.Unknown)
            {
                if (qn == expectedNextQuestionGlobal)
                    mode = NumberingMode.Global;
                else if (qn is 0 or 1)
                    mode = NumberingMode.PerTour;
                else
                    return false;
            }

            if (mode == NumberingMode.Global)
            {
                if (qn != expectedNextQuestionGlobal)
                    return false;

                expectedNextQuestionGlobal = qn + 1;
                expectedNextQuestionInTour = qn + 1; // keep in sync for convenience
                return true;
            }

            // Per-tour
            if (qn is 0 or 1)
            {
                expectedNextQuestionInTour = qn + 1;
                // Do NOT mutate expectedNextQuestionGlobal in per-tour mode
                return true;
            }

            return false;
        }

        // Normal within-tour numbering
        if (mode == NumberingMode.Global)
        {
            if (qn != expectedNextQuestionGlobal)
                return false;

            expectedNextQuestionGlobal = qn + 1;
            expectedNextQuestionInTour = qn + 1;
            return true;
        }

        if (mode == NumberingMode.PerTour)
        {
            if (qn != expectedNextQuestionInTour)
                return false;

            expectedNextQuestionInTour = qn + 1;
            return true;
        }

        // Unknown mode (single tour so far): allow either sequence, and lock when unambiguous
        var okInTour = (qn == expectedNextQuestionInTour);
        var okGlobal = (qn == expectedNextQuestionGlobal);

        if (!okInTour && !okGlobal)
            return false;

        if (okGlobal && !okInTour)
            mode = NumberingMode.Global;
        else if (okInTour && !okGlobal)
            mode = NumberingMode.PerTour;

        expectedNextQuestionInTour = qn + 1;
        expectedNextQuestionGlobal = okGlobal ? qn + 1 : expectedNextQuestionGlobal;

        return true;
    }

    /// <summary>
    /// Label patterns mapped to their corresponding parser sections.
    /// </summary>
    private static readonly (Func<Regex> GetPattern, ParserSection Section)[] LabelPatterns =
    [
        (ParserPatterns.AnswerLabel, ParserSection.Answer),
        (ParserPatterns.AcceptedLabel, ParserSection.AcceptedAnswers),
        (ParserPatterns.RejectedLabel, ParserSection.RejectedAnswers),
        (ParserPatterns.CommentLabel, ParserSection.Comment),
        (ParserPatterns.SourceLabel, ParserSection.Source),
        (ParserPatterns.AuthorLabel, ParserSection.Authors),
        (ParserPatterns.HandoutMarker, ParserSection.Handout)
    ];

    private static (ParserSection? Section, string Remainder) DetectLabel(string text)
    {
        foreach (var (getPattern, section) in LabelPatterns)
        {
            var match = getPattern().Match(text);
            if (match.Success)
                return (section, match.Groups[1].Value.Trim());
        }

        return (null, text);
    }

    private static bool TryExtractHostInstructions(string text, out string instructions, out string remainingText)
    {
        instructions = "";
        remainingText = "";

        var match = ParserPatterns.HostInstructionsBracket().Match(text);
        if (!match.Success) return false;

        instructions = match.Groups[1].Value.Trim();
        remainingText = match.Groups[2].Value.Trim();
        return true;
    }

    private static bool TryExtractBracketedHandout(string text, out string handoutText, out string remainingText)
    {
        handoutText = "";
        remainingText = "";

        var match = ParserPatterns.HandoutMarkerBracket().Match(text);
        if (!match.Success) return false;

        handoutText = match.Groups[1].Value.Trim();
        remainingText = match.Groups[2].Value.Trim();
        return true;
    }

    /// <summary>
    /// Tries to extract the opening of a multiline bracketed handout (no closing bracket on this line).
    /// </summary>
    private static bool TryExtractMultilineHandoutOpening(string text, out string handoutText)
    {
        handoutText = "";

        // Don't match if the closing bracket is on the same line (single-line case)
        if (text.Contains(']'))
            return false;

        var match = ParserPatterns.HandoutMarkerBracketOpen().Match(text);
        if (!match.Success)
            return false;

        handoutText = match.Groups[1].Value.Trim();
        return true;
    }

    private static void AppendToSection(
        ParserSection section,
        string text,
        QuestionDto? question,
        TourDto tour,
        BlockDto? block,
        ParseResult result)
    {
        if (question == null)
        {
            if (section == ParserSection.TourHeader)
            {
                var editorsMatch = ParserPatterns.EditorsLabel().Match(text);
                if (editorsMatch.Success)
                {
                    var editors = ParseAuthorList(editorsMatch.Groups[1].Value);
                    tour.Editors.AddRange(editors);
                }
                else
                {
                    tour.Preamble = AppendText(tour.Preamble, TextNormalizer.NormalizeApostrophes(text)!);
                }
            }
            else if (section == ParserSection.BlockHeader && block != null)
            {
                // Try block editors first (with "блоку" in the label or dash separator)
                var blockEditorsMatch = ParserPatterns.BlockEditorsLabel().Match(text);
                if (blockEditorsMatch.Success)
                {
                    var editors = ParseAuthorList(blockEditorsMatch.Groups[1].Value);
                    block.Editors.AddRange(editors);
                }
                // Also try regular editors label for backwards compatibility
                else
                {
                    var editorsMatch = ParserPatterns.EditorsLabel().Match(text);
                    if (editorsMatch.Success)
                    {
                        var editors = ParseAuthorList(editorsMatch.Groups[1].Value);
                        block.Editors.AddRange(editors);
                    }
                    else
                    {
                        block.Preamble = AppendText(block.Preamble, TextNormalizer.NormalizeApostrophes(text)!);
                    }
                }
            }
            return;
        }

        switch (section)
        {
            case ParserSection.QuestionText:
                question.Text = AppendText(question.Text, TextNormalizer.NormalizeApostrophes(text)!);
                break;
            case ParserSection.HostInstructions:
                question.HostInstructions = AppendText(question.HostInstructions, TextNormalizer.NormalizeApostrophes(text)!);
                break;
            case ParserSection.Handout:
                question.HandoutText = AppendText(question.HandoutText, TextNormalizer.NormalizeApostrophes(text)!);
                break;
            case ParserSection.Answer:
                question.Answer = AppendText(question.Answer, TextNormalizer.NormalizeApostrophes(text)!);
                break;
            case ParserSection.AcceptedAnswers:
                question.AcceptedAnswers = AppendText(question.AcceptedAnswers, TextNormalizer.NormalizeApostrophes(text)!);
                break;
            case ParserSection.RejectedAnswers:
                question.RejectedAnswers = AppendText(question.RejectedAnswers, TextNormalizer.NormalizeApostrophes(text)!);
                break;
            case ParserSection.Comment:
                question.Comment = AppendText(question.Comment, TextNormalizer.NormalizeApostrophes(text)!);
                break;
            case ParserSection.Source:
                // Source is not normalized for apostrophes as it may contain URLs
                question.Source = AppendText(question.Source, text);
                break;
            case ParserSection.Authors:
                var authors = ParseAuthorList(text);
                question.Authors.AddRange(authors);
                break;
        }
    }

    private static void AssociateAsset(AssetReference asset, ParserSection section, QuestionDto? question, ParseResult? result = null)
    {
        if (question == null) return;

        // Anything after (or within) answer-related sections counts as comment attachment.
        var isAnswerRelatedSection = section is
            ParserSection.Answer or
            ParserSection.AcceptedAnswers or
            ParserSection.RejectedAnswers or
            ParserSection.Comment or
            ParserSection.Source or
            ParserSection.Authors;

        if (isAnswerRelatedSection)
        {
            if (question.CommentAssetFileName == null)
            {
                question.CommentAssetFileName = asset.FileName;
            }
            else
            {
                // Warn about dropped asset
                result?.Warnings.Add($"Question {question.Number}: extra comment asset ignored: {asset.FileName} (already has: {question.CommentAssetFileName})");
            }
        }
        else
        {
            if (question.HandoutAssetFileName == null)
            {
                question.HandoutAssetFileName = asset.FileName;
            }
            else
            {
                // Warn about dropped asset
                result?.Warnings.Add($"Question {question.Number}: extra handout asset ignored: {asset.FileName} (already has: {question.HandoutAssetFileName})");
            }
        }
    }

    private static void ParsePackageHeader(List<DocBlock> headerBlocks, ParseResult result)
    {
        if (headerBlocks.Count == 0) return;

        // Determine title blocks based on font size and style
        var titleBlocks = DetermineTitleBlocks(headerBlocks);
        result.Title = TextNormalizer.NormalizeApostrophes(
            string.Join(" ", titleBlocks.Select(b => NormalizeText(b.Text))));

        var preambleLines = new List<string>();

        // Process remaining blocks after title
        foreach (var block in headerBlocks.Skip(titleBlocks.Count))
        {
            var text = NormalizeText(block.Text);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var editorsMatch = ParserPatterns.EditorsLabel().Match(text);
            if (editorsMatch.Success)
            {
                var editors = ParseAuthorList(editorsMatch.Groups[1].Value);
                result.Editors.AddRange(editors);
            }
            else
            {
                preambleLines.Add(TextNormalizer.NormalizeApostrophes(text)!);
            }
        }

        if (preambleLines.Count > 0)
            result.Preamble = string.Join("\n", preambleLines);
    }

    /// <summary>
    /// Determines which header blocks should be included in the title based on:
    /// 1. Font size consistency (subsequent blocks must be ≥70% of first block's font size)
    /// 2. Style consistency (if first block has Title/Heading style, subsequent blocks must too)
    /// 3. Preamble markers (stop if text starts with '[' indicating host instructions)
    /// 4. Editors label (stop if text starts with 'Редактор:' pattern)
    /// Limited to maximum 3 blocks.
    /// </summary>
    private static List<DocBlock> DetermineTitleBlocks(List<DocBlock> headerBlocks)
    {
        if (headerBlocks.Count == 0)
            return [];

        // Take at most first 3 blocks for title consideration
        var candidates = headerBlocks.Take(3).ToList();

        var firstBlock = candidates[0];

        // Check if first block itself is a preamble marker or editors label
        if (IsTitleTerminator(firstBlock.Text))
            return [];

        var firstBlockHasTitleStyle = HasTitleOrHeadingStyle(firstBlock);

        // Strategy 1 (Primary): Use font size to determine title extent
        if (firstBlock.FontSizeHalfPoints.HasValue && firstBlock.FontSizeHalfPoints.Value > 0)
        {
            var referenceFontSize = firstBlock.FontSizeHalfPoints.Value;
            const double fontSizeThreshold = 0.70; // 70% threshold

            var result = new List<DocBlock> { firstBlock };

            for (var i = 1; i < candidates.Count; i++)
            {
                var block = candidates[i];

                // Stop if preamble marker or editors label encountered
                if (IsTitleTerminator(block.Text))
                    break;

                // If first block has Title/Heading style, subsequent blocks must also have it
                if (firstBlockHasTitleStyle && !HasTitleOrHeadingStyle(block))
                    break;

                // If block has font size, check if it's within threshold
                if (block.FontSizeHalfPoints.HasValue)
                {
                    var ratio = (double)block.FontSizeHalfPoints.Value / referenceFontSize;
                    if (ratio < fontSizeThreshold)
                        break;
                }

                result.Add(block);
            }

            return result;
        }

        // Strategy 2 (Fallback): Use style-based detection when no font size available
        var titleStyleBlocks = candidates
            .TakeWhile(b => !IsTitleTerminator(b.Text) && HasTitleOrHeadingStyle(b))
            .ToList();

        if (titleStyleBlocks.Count > 0)
            return titleStyleBlocks;

        // Fallback: just return the first block if not a preamble marker
        return [firstBlock];
    }

    /// <summary>
    /// Checks if block has Title or Heading style.
    /// </summary>
    private static bool HasTitleOrHeadingStyle(DocBlock block)
    {
        return block.StyleId?.Contains("Title", StringComparison.OrdinalIgnoreCase) == true
            || block.StyleId?.Contains("Heading", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Checks if text should terminate title extraction.
    /// Returns true for preamble markers and editors labels.
    /// </summary>
    private static bool IsTitleTerminator(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.TrimStart();

        // Preamble marker: text starting with '['
        if (trimmed.StartsWith('['))
            return true;

        // Editors label: "Редактор:", "Редактори:", etc.
        if (ParserPatterns.EditorsLabel().IsMatch(trimmed))
            return true;

        return false;
    }

    private static void FinalizeQuestion(QuestionDto question, List<AuthorRangeRule> authorRanges, ParseResult result)
    {
        // Apply author range defaults (only if question has no explicit authors)
        if (question.Authors.Count == 0 && int.TryParse(question.Number, out var qn))
        {
            var rule = authorRanges.FirstOrDefault(r => qn >= r.From && qn <= r.To);
            if (rule is not null && rule.Authors.Count > 0)
                question.Authors.AddRange(rule.Authors);
        }

        if (!question.HasText)
            result.Warnings.Add($"Питання {question.Number}: текст питання не знайдено");

        if (!question.HasAnswer)
            result.Warnings.Add($"Питання {question.Number}: відповідь не знайдено");
    }

    private static void CalculateConfidence(ParseResult result)
    {
        if (result.Tours.Count == 0)
        {
            result.Confidence = 0;
            return;
        }

        var totalQuestions = result.TotalQuestions;
        if (totalQuestions == 0)
        {
            result.Confidence = 0.2;
            return;
        }

        var allQuestions = result.Tours.SelectMany(t => t.Questions).ToList();

        var questionsWithAnswer = allQuestions.Count(q => q.HasAnswer);
        var questionsWithText = allQuestions.Count(q => q.HasText);

        var answerRatio = (double)questionsWithAnswer / totalQuestions;
        var textRatio = (double)questionsWithText / totalQuestions;

        result.Confidence = (answerRatio * 0.6) + (textRatio * 0.4);
    }

    private static List<string> ParseAuthorList(string text)
    {
        return text
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(s => s.Split([" та ", " і ", " and "], StringSplitOptions.RemoveEmptyEntries))
            .Select(s => StripAccents(s.Trim().TrimEnd('.', ',', ';')))
            .Select(s => TextNormalizer.NormalizeApostrophes(s)!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .SelectMany(SplitAuthorOnRedakciya)
            .ToList();
    }

    /// <summary>
    /// Splits a single author entry on "у редакції" / "в редакції" / "за ідеєю" patterns,
    /// converts genitive names to nominative, and strips city parentheses.
    /// E.g., "Станіслав Мерлян (Одеса) у редакції Едуарда Голуба (Київ)"
    /// → ["Станіслав Мерлян", "Едуард Голуб"]
    /// </summary>
    private static IEnumerable<string> SplitAuthorOnRedakciya(string author)
    {
        var parts = UkrainianNameHelper.SplitAndNormalizeAuthors(author);
        return parts.Where(p => !string.IsNullOrWhiteSpace(p));
    }

    /// <summary>
    /// Removes combining acute accent marks from text.
    /// Used for author and editor names to ensure consistent matching.
    /// </summary>
    private static string StripAccents(string text)
    {
        return text.Replace("\u0301", "");
    }

    private static string AppendText(string? existing, string newText)
    {
        if (string.IsNullOrWhiteSpace(existing))
            return newText;
        return existing + "\n" + newText;
    }

    /// <summary>
    /// Appends a blank line to the current section's content.
    /// Used to preserve paragraph breaks in multiline content.
    /// </summary>
    private static string AppendBlankLine(string? existing)
    {
        if (string.IsNullOrEmpty(existing))
            return "";
        return existing + "\n";
    }

    /// <summary>
    /// Trims leading and trailing blank lines from text while preserving internal blank lines.
    /// </summary>
    private static string TrimBlankLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var lines = text.Split('\n');
        var startIndex = 0;
        var endIndex = lines.Length - 1;

        // Find first non-empty line
        while (startIndex <= endIndex && string.IsNullOrWhiteSpace(lines[startIndex]))
            startIndex++;

        // Find last non-empty line
        while (endIndex >= startIndex && string.IsNullOrWhiteSpace(lines[endIndex]))
            endIndex--;

        if (startIndex > endIndex)
            return string.Empty;

        return string.Join("\n", lines.Skip(startIndex).Take(endIndex - startIndex + 1));
    }
}
