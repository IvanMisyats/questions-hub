using QuestionsHub.Blazor.Utils;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Parses document blocks into structured package data.
/// </summary>
public partial class PackageParser
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

        // When in Handout section (entered via label, not brackets) and the block contained
        // image assets that were associated as handout — the image IS the handout content.
        // Transition to QuestionText so subsequent blocks become question text, not handout.
        if (ctx.CurrentSection == ParserSection.Handout &&
            !ctx.InsideMultilineHandoutBracket &&
            block.Assets.Count > 0 &&
            ctx.HasCurrentQuestion &&
            ctx.CurrentQuestion!.HandoutAssetFileName != null)
        {
            ctx.CurrentSection = ParserSection.QuestionText;
        }
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
}