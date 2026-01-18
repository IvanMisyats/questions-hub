using System.Globalization;
using System.Text.RegularExpressions;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Regex patterns for parsing Ukrainian question packages.
/// </summary>
public static partial class ParserPatterns
{
    // Tour detection
    [GeneratedRegex(@"^\s*(?:ТУР|Тур|Tour)\s+(\d+)\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex TourStart();

    [GeneratedRegex(@"^\s*[-–—]\s*(?:ТУР|Тур)\s+(\d+)\s*[-–—]\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex TourStartDashed();

    // Warmup tour detection: "Розминка", "Warmup", "Тур 0", "Розминковий тур"
    [GeneratedRegex(@"^\s*(?:Розминка|Warmup|Розминковий\s+тур)\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex WarmupTourStart();

    [GeneratedRegex(@"^\s*[-–—]\s*(?:Розминка|Warmup)\s*[-–—]\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex WarmupTourStartDashed();

    [GeneratedRegex(@"^\s*(?:ТУР|Тур|Tour)\s+0\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex TourZeroStart();

    // Question detection
    [GeneratedRegex(@"^\s*(\d+)\.\s+(.*)$")]
    public static partial Regex QuestionStartWithText();

    [GeneratedRegex(@"^\s*(\d+)\.\s*$")]
    public static partial Regex QuestionStartNumberOnly();

    // Matches: "Запитання 1", "Питання 1.", "Запитання 1:"
    [GeneratedRegex(@"^\s*(?:Запитання|Питання)\s+(\d+)[\.:]?\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex QuestionStartNamed();

    // Matches: "Запитання 1. text", "Питання 1: text" (named format with text after)
    [GeneratedRegex(@"^\s*(?:Запитання|Питання)\s+(\d+)[\.:]?\s+(.+)$", RegexOptions.IgnoreCase)]
    public static partial Regex QuestionStartNamedWithText();

    // Labels (field markers)
    [GeneratedRegex(@"^\s*Відповідь\s*:\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex AnswerLabel();

    // Matches: "Залік: ...", "Заліки: ...", "Залік (не оголошувати): ..."
    [GeneratedRegex(@"^\s*Залік(?:и)?(?:\s*\([^)]+\))?\s*:\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex AcceptedLabel();

    [GeneratedRegex(@"^\s*(?:Незалік|Не\s*залік|Не\s*приймається)\s*:\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex RejectedLabel();

    [GeneratedRegex(@"^\s*Коментар\s*:\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex CommentLabel();

    [GeneratedRegex(@"^\s*(?:Джерело|Джерела|Джерело\(а\))\s*:\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex SourceLabel();

    // Matches: "Автор:", "Автори:", "Автора:", "Авторка:", "Авторки:"
    [GeneratedRegex(@"^\s*Автор(?:а|и|ка|ки)?\s*:\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex AuthorLabel();

    // Author ranges in header: "Автор запитань 1-18: ...", "Автора запитань 19–36: ...", "Авторка запитань 1-12: ..."
    [GeneratedRegex(@"^\s*Автор(?:а|ка)?\s+запитань\s+(\d+)\s*[-–—]\s*(\d+)\s*:\s*(.+)$", RegexOptions.IgnoreCase)]
    public static partial Regex AuthorRangeLabel();

    // Special markers
    // Matches: [Ведучому: ...], [Ведучому -(ій): ...], [Вказівка ведучому: ...]
    // Captures the instruction text inside brackets and any text after the closing bracket
    [GeneratedRegex(@"^\s*\[(?:Ведучому|Вказівка\s*ведучому)[^:]*:\s*([^\]]+)\]\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex HostInstructionsBracket();

    [GeneratedRegex(@"^\s*(?:Роздатка|Роздатковий\s*матеріал)\s*[:\.]?\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex HandoutMarker();

    // Matches: [Роздатка: ...], [Роздатковий матеріал: ...]
    // Captures the handout text inside brackets and any text after the closing bracket
    [GeneratedRegex(@"^\s*\[(?:Роздатка|Роздатковий\s*матеріал)\s*[:\.]?\s*([^\]]*)\]\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex HandoutMarkerBracket();

    // Matches opening of a multiline bracketed handout (no closing bracket on same line)
    // Captures any text after the colon as handout content
    [GeneratedRegex(@"^\s*\[(?:Роздатка|Роздатковий\s*матеріал)\s*[:\.]?\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex HandoutMarkerBracketOpen();

    // Matches a closing bracket with optional text after it (for multiline handouts)
    // Captures any text after the bracket as question text
    [GeneratedRegex(@"^\s*\]\s*(.*)$")]
    public static partial Regex HandoutMarkerBracketClose();

    // Editors in header: "Редактор:", "Редактори:", "Редактор туру:", "Редакторка:", "Редакторки:"
    [GeneratedRegex(@"^\s*(?:Редактор(?:и|ка|ки)?(?:\s*туру)?)\s*:\s*(.+)$", RegexOptions.IgnoreCase)]
    public static partial Regex EditorsLabel();
}

/// <summary>
/// Parser sections for state machine.
/// </summary>
public enum ParserSection
{
    PackageHeader,
    TourHeader,
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

        public bool HasCurrentQuestion => CurrentQuestion != null;
        public bool HasCurrentTour => CurrentTour != null;
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

        foreach (var block in blocks)
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
    /// Processes a single document block (paragraph).
    /// </summary>
    private void ProcessBlock(DocBlock block, ParserContext ctx)
    {
        var text = NormalizeText(block.Text);
        if (string.IsNullOrWhiteSpace(text) && block.Assets.Count == 0)
            return;

        ctx.QuestionCreatedInCurrentBlock = false;
        ctx.CurrentBlock = block;

        foreach (var line in SplitIntoLines(text))
        {
            ProcessLine(line, ctx);
        }

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
                ctx.CurrentQuestion!.Text = AppendText(ctx.CurrentQuestion.Text, afterBracket);
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
                ctx.CurrentQuestion!.HandoutText = AppendText(ctx.CurrentQuestion.HandoutText, beforeBracket);
            }

            // Text after the bracket is question text
            var afterBracket = line[(bracketIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(afterBracket) && ctx.HasCurrentQuestion)
            {
                ctx.CurrentQuestion!.Text = AppendText(ctx.CurrentQuestion.Text, afterBracket);
            }

            return true;
        }

        // Line is still inside the handout bracket - add to handout text (unless it's just whitespace)
        if (!string.IsNullOrWhiteSpace(line) && ctx.HasCurrentQuestion)
        {
            ctx.CurrentQuestion!.HandoutText = AppendText(ctx.CurrentQuestion.HandoutText, line);
        }

        return true;
    }

    /// <summary>
    /// Attempts to parse and handle a tour start line (including warmup tours).
    /// </summary>
    private bool TryProcessTourStart(string line, ParserContext ctx)
    {
        var isWarmup = false;
        string tourNumber;

        // Check for warmup tour first
        if (TryParseWarmupTourStart(line))
        {
            isWarmup = true;
            tourNumber = "0";
        }
        else if (!TryParseTourStart(line, out tourNumber))
        {
            return false;
        }

        SaveCurrentQuestion(ctx);
        EnsurePackageHeaderParsed(ctx);

        var orderIndex = ctx.Result.Tours.Count;
        ctx.CurrentTour = new TourDto
        {
            Number = tourNumber,
            OrderIndex = orderIndex,
            IsWarmup = isWarmup
        };
        ctx.Result.Tours.Add(ctx.CurrentTour);
        ctx.CurrentQuestion = null;
        ctx.CurrentSection = ParserSection.TourHeader;
        ctx.ExpectedNextQuestionInTour = null;

        _logger.LogDebug("Found tour: {TourNumber}, IsWarmup: {IsWarmup}", tourNumber, isWarmup);
        return true;
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

        SaveCurrentQuestion(ctx);
        EnsureDefaultTourExists(ctx);

        ctx.CurrentQuestion = new QuestionDto { Number = questionNumber };
        ctx.CurrentSection = ParserSection.QuestionText;
        ctx.QuestionCreatedInCurrentBlock = true;

        ApplyPendingAssets(ctx);
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

        ctx.CurrentQuestion!.HostInstructions = AppendText(ctx.CurrentQuestion.HostInstructions, instructions);

        if (!string.IsNullOrWhiteSpace(afterInstructions))
            ctx.CurrentQuestion.Text = AppendText(ctx.CurrentQuestion.Text, afterInstructions);

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
            ctx.CurrentSection = ParserSection.Handout;

            if (ctx.HasCurrentQuestion && !string.IsNullOrWhiteSpace(handoutText))
                ctx.CurrentQuestion!.HandoutText = AppendText(ctx.CurrentQuestion.HandoutText, handoutText);

            if (!string.IsNullOrWhiteSpace(afterHandout))
            {
                if (ctx.HasCurrentQuestion)
                    ctx.CurrentQuestion!.Text = AppendText(ctx.CurrentQuestion.Text, afterHandout);

                ctx.CurrentSection = ParserSection.QuestionText;
            }

            return true;
        }

        // Try multiline bracket opening (no closing bracket on this line)
        if (TryExtractMultilineHandoutOpening(line, out var openingHandoutText))
        {
            ctx.CurrentSection = ParserSection.Handout;
            ctx.InsideMultilineHandoutBracket = true;

            if (ctx.HasCurrentQuestion && !string.IsNullOrWhiteSpace(openingHandoutText))
                ctx.CurrentQuestion!.HandoutText = AppendText(ctx.CurrentQuestion.HandoutText, openingHandoutText);

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
        }

        if (string.IsNullOrWhiteSpace(line))
            return;

        // Check if there's another label inline (e.g., "answer text. Залік: accepted")
        var inlineLabelIndex = FindInlineLabelStart(line);
        if (inlineLabelIndex > 0)
        {
            // Process the text before the inline label
            var beforeLabel = line[..inlineLabelIndex].Trim();
            if (!string.IsNullOrWhiteSpace(beforeLabel))
                AppendToSection(ctx.CurrentSection, beforeLabel, ctx.CurrentQuestion, ctx.CurrentTour!, ctx.Result);

            // Recursively process the inline label and its content
            var labelAndAfter = line[inlineLabelIndex..];
            ProcessLabelOrContent(labelAndAfter, ctx);
        }
        else
        {
            AppendToSection(ctx.CurrentSection, line, ctx.CurrentQuestion, ctx.CurrentTour!, ctx.Result);
        }
    }

    /// <summary>
    /// Finds the start index of an inline label in text.
    /// Returns -1 if no inline label is found, or the index of the label start.
    /// </summary>
    private static int FindInlineLabelStart(string text)
    {
        var minIndex = int.MaxValue;

        // Label keywords that can appear inline (without ^ anchor)
        // These are the prefixes we look for to split inline labels
        string[] labelKeywords =
        [
            "Відповідь:",
            "Залік:",
            "Заліки:",
            "Незалік:",
            "Не залік:",
            "Не приймається:",
            "Коментар:",
            "Джерело:",
            "Джерела:",
            "Автор:",
            "Автори:",
            "Авторка:",
            "Авторки:",
            "Роздатка:",
            "Роздатковий матеріал:"
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

        // Try single-line bracketed handout
        if (TryExtractBracketedHandout(remainingText, out var handout, out var afterHandout))
        {
            ctx.CurrentSection = ParserSection.Handout;

            if (!string.IsNullOrWhiteSpace(handout))
                question.HandoutText = AppendText(question.HandoutText, handout);

            if (!string.IsNullOrWhiteSpace(afterHandout))
            {
                question.Text = AppendText(question.Text, afterHandout);
                ctx.CurrentSection = ParserSection.QuestionText;
            }

            return;
        }

        // Try multiline bracketed handout opening
        if (TryExtractMultilineHandoutOpening(remainingText, out var openingHandout))
        {
            ctx.CurrentSection = ParserSection.Handout;
            ctx.InsideMultilineHandoutBracket = true;

            if (!string.IsNullOrWhiteSpace(openingHandout))
                question.HandoutText = AppendText(question.HandoutText, openingHandout);

            return;
        }

        // Regular question text
        question.Text = AppendText(question.Text, remainingText);
    }

    /// <summary>
    /// Associates block assets with the current question or adds them to pending assets.
    /// </summary>
    private static void AssociateBlockAssets(List<AssetReference> assets, ParserContext ctx)
    {
        foreach (var asset in assets)
        {
            // If we have a current question, associate the asset directly
            if (ctx.HasCurrentQuestion)
            {
                // When inside a multiline handout bracket, force association with handout section
                var section = ctx.InsideMultilineHandoutBracket ? ParserSection.Handout : ctx.CurrentSection;
                AssociateAsset(asset, section, ctx.CurrentQuestion);
            }
            else
            {
                // No question yet - add to pending assets for later association
                ctx.PendingAssets.Add((asset, ctx.CurrentSection));
            }
        }
    }

    /// <summary>
    /// Applies pending assets to the current question.
    /// </summary>
    private static void ApplyPendingAssets(ParserContext ctx)
    {
        foreach (var (asset, section) in ctx.PendingAssets)
            AssociateAsset(asset, section, ctx.CurrentQuestion);

        ctx.PendingAssets.Clear();
    }

    /// <summary>
    /// Saves the current question to the current tour if both exist.
    /// </summary>
    private void SaveCurrentQuestion(ParserContext ctx)
    {
        if (ctx.CurrentQuestion != null && ctx.CurrentTour != null)
        {
            FinalizeQuestion(ctx.CurrentQuestion, ctx.AuthorRanges, ctx.Result);
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
    private void FinalizeParsingResult(ParserContext ctx)
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

        CalculateConfidence(ctx.Result);
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
    /// Splits text into non-empty, trimmed lines.
    /// </summary>
    private static IEnumerable<string> SplitIntoLines(string text)
    {
        return text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line));
    }

    private static string NormalizeText(string text)
    {
        text = text.Replace('\u00A0', ' ');
        text = text.Replace('–', '-').Replace('—', '-');
        text = text.Replace("\u0301", "");
        return text.Trim();
    }

    private static bool TryParseTourStart(string text, out string tourNumber)
    {
        tourNumber = "";

        if (!TryMatchFirst(text, out var match, ParserPatterns.TourStart(), ParserPatterns.TourStartDashed()))
            return false;

        tourNumber = match.Groups[1].Value;
        return true;
    }

    private static bool TryParseWarmupTourStart(string text)
    {
        return ParserPatterns.WarmupTourStart().IsMatch(text) ||
               ParserPatterns.WarmupTourStartDashed().IsMatch(text) ||
               ParserPatterns.TourZeroStart().IsMatch(text);
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
                    tour.Preamble = AppendText(tour.Preamble, text);
                }
            }
            return;
        }

        switch (section)
        {
            case ParserSection.QuestionText:
                question.Text = AppendText(question.Text, text);
                break;
            case ParserSection.HostInstructions:
                question.HostInstructions = AppendText(question.HostInstructions, text);
                break;
            case ParserSection.Handout:
                question.HandoutText = AppendText(question.HandoutText, text);
                break;
            case ParserSection.Answer:
                question.Answer = AppendText(question.Answer, text);
                break;
            case ParserSection.AcceptedAnswers:
                question.AcceptedAnswers = AppendText(question.AcceptedAnswers, text);
                break;
            case ParserSection.RejectedAnswers:
                question.RejectedAnswers = AppendText(question.RejectedAnswers, text);
                break;
            case ParserSection.Comment:
                question.Comment = AppendText(question.Comment, text);
                break;
            case ParserSection.Source:
                question.Source = AppendText(question.Source, text);
                break;
            case ParserSection.Authors:
                var authors = ParseAuthorList(text);
                question.Authors.AddRange(authors);
                break;
        }
    }

    private static void AssociateAsset(AssetReference asset, ParserSection section, QuestionDto? question)
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
            question.CommentAssetFileName ??= asset.FileName;
        else
            question.HandoutAssetFileName ??= asset.FileName;
    }

    private static void ParsePackageHeader(List<DocBlock> headerBlocks, ParseResult result)
    {
        if (headerBlocks.Count == 0) return;

        // Determine title blocks based on font size and style
        var titleBlocks = DetermineTitleBlocks(headerBlocks);
        result.Title = string.Join(" ", titleBlocks.Select(b => NormalizeText(b.Text)));

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
                preambleLines.Add(text);
            }
        }

        if (preambleLines.Count > 0)
            result.Preamble = string.Join("\n", preambleLines);
    }

    /// <summary>
    /// Determines which header blocks should be included in the title based on:
    /// 1. Blocks with "Title" or "Heading" style (take consecutive styled blocks)
    /// 2. Blocks up to and including the block with the largest font size
    /// Limited to maximum 3 blocks.
    /// </summary>
    private static List<DocBlock> DetermineTitleBlocks(List<DocBlock> headerBlocks)
    {
        if (headerBlocks.Count == 0)
            return [];

        // Take at most first 3 blocks for title consideration
        var candidates = headerBlocks.Take(3).ToList();

        // Strategy 1: Look for consecutive blocks with Title/Heading style
        var titleStyleBlocks = candidates
            .TakeWhile(b => b.StyleId?.Contains("Title", StringComparison.OrdinalIgnoreCase) == true
                         || b.StyleId?.Contains("Heading", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        if (titleStyleBlocks.Count > 0)
            return titleStyleBlocks;

        // Strategy 2: Use font size to determine title extent
        // Find the maximum font size among candidates
        var maxFontSize = candidates
            .Where(b => b.FontSizeHalfPoints.HasValue)
            .Select(b => b.FontSizeHalfPoints!.Value)
            .DefaultIfEmpty(0)
            .Max();

        if (maxFontSize == 0)
        {
            // No font size info - just return the first non-empty block
            return candidates.Take(1).ToList();
        }

        // Find the index of the last block with max font size
        var lastMaxFontIndex = -1;
        for (var i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].FontSizeHalfPoints == maxFontSize)
                lastMaxFontIndex = i;
        }

        if (lastMaxFontIndex >= 0)
        {
            // Include all blocks up to and including the last max font block
            return candidates.Take(lastMaxFontIndex + 1).ToList();
        }

        // Fallback: just return the first block
        return candidates.Take(1).ToList();
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
            .Select(s => s.Trim().TrimEnd('.', ',', ';'))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static string AppendText(string? existing, string newText)
    {
        if (string.IsNullOrWhiteSpace(existing))
            return newText;
        return existing + "\n" + newText;
    }
}
