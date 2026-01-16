using System.Globalization;
using System.Text.RegularExpressions;

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

    // Question detection
    [GeneratedRegex(@"^\s*(\d+)\.\s+(.*)$")]
    public static partial Regex QuestionStartWithText();

    [GeneratedRegex(@"^\s*(\d+)\.\s*$")]
    public static partial Regex QuestionStartNumberOnly();

    // Matches: "Запитання 1", "Питання 1.", "Запитання 1:"
    [GeneratedRegex(@"^\s*(?:Запитання|Питання)\s+(\d+)[\.:]?\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex QuestionStartNamed();

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

    [GeneratedRegex(@"^\s*(?:Джерело|Джерела)\s*:\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex SourceLabel();

    // Matches: "Автор:", "Автори:", "Автора:"
    [GeneratedRegex(@"^\s*Автор(?:а|и)?\s*:\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex AuthorLabel();

    // Author ranges in header: "Автор запитань 1-18: ...", "Автора запитань 19–36: ..."
    [GeneratedRegex(@"^\s*Автор(?:а)?\s+запитань\s+(\d+)\s*[-–—]\s*(\d+)\s*:\s*(.+)$", RegexOptions.IgnoreCase)]
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

    // Editors in header
    [GeneratedRegex(@"^\s*(?:Редактори?(?:\s*туру)?)\s*:\s*(.+)$", RegexOptions.IgnoreCase)]
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
        public List<string> HeaderBlocks { get; } = [];
        public List<(AssetReference Asset, ParserSection Section)> PendingAssets { get; } = [];
        public List<AuthorRangeRule> AuthorRanges { get; } = [];
        public NumberingMode Mode { get; set; } = NumberingMode.Unknown;
        public int? ExpectedNextQuestionInTour { get; set; }
        public int? ExpectedNextQuestionGlobal { get; set; }
        public bool QuestionCreatedInCurrentBlock { get; set; }

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
        if (TryProcessTourStart(line, ctx)) return;
        if (TryProcessAuthorRange(line, ctx)) return;
        if (TryProcessQuestionStart(line, ctx)) return;
        if (TryCollectHeaderLine(line, ctx)) return;
        if (TryProcessHostInstructions(line, ctx)) return;
        if (TryProcessBracketedHandout(line, ctx)) return;

        ProcessLabelOrContent(line, ctx);
    }

    /// <summary>
    /// Attempts to parse and handle a tour start line.
    /// </summary>
    private bool TryProcessTourStart(string line, ParserContext ctx)
    {
        if (!TryParseTourStart(line, out var tourNumber))
            return false;

        SaveCurrentQuestion(ctx);
        EnsurePackageHeaderParsed(ctx);

        ctx.CurrentTour = new TourDto { Number = tourNumber };
        ctx.Result.Tours.Add(ctx.CurrentTour);
        ctx.CurrentQuestion = null;
        ctx.CurrentSection = ParserSection.TourHeader;
        ctx.ExpectedNextQuestionInTour = null;

        _logger.LogDebug("Found tour: {TourNumber}", tourNumber);
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
        if (!TryParseQuestionStart(line, out var questionNumber, out var remainingText))
            return false;

        if (!IsValidNextQuestionNumber(questionNumber, ctx))
        {
            ProcessAsRegularContent(line, ctx);
            return true;
        }

        SaveCurrentQuestion(ctx);
        EnsureDefaultTourExists(ctx);

        ctx.CurrentQuestion = new QuestionDto { Number = questionNumber };
        ctx.CurrentSection = ParserSection.QuestionText;
        ctx.QuestionCreatedInCurrentBlock = true;

        ApplyPendingAssets(ctx);
        ProcessRemainingTextAfterQuestionNumber(remainingText, ctx.CurrentQuestion);

        _logger.LogDebug("Found question: {QuestionNumber}", questionNumber);
        return true;
    }

    /// <summary>
    /// Collects lines into header blocks when no tour has been started yet.
    /// </summary>
    private static bool TryCollectHeaderLine(string line, ParserContext ctx)
    {
        if (ctx.HasCurrentTour)
            return false;

        ctx.HeaderBlocks.Add(line);
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
    /// </summary>
    private static bool TryProcessBracketedHandout(string line, ParserContext ctx)
    {
        if (!TryExtractBracketedHandout(line, out var handoutText, out var afterHandout))
            return false;

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

    /// <summary>
    /// Processes a line that may contain a section label or regular content.
    /// </summary>
    private static void ProcessLabelOrContent(string line, ParserContext ctx)
    {
        var (newSection, remainder) = DetectLabel(line);
        if (newSection != null)
        {
            ctx.CurrentSection = newSection.Value;
            line = remainder;
        }

        if (!string.IsNullOrWhiteSpace(line))
            AppendToSection(ctx.CurrentSection, line, ctx.CurrentQuestion, ctx.CurrentTour!, ctx.Result);
    }

    /// <summary>
    /// Handles a line that looked like a question start but failed validation.
    /// </summary>
    private static void ProcessAsRegularContent(string line, ParserContext ctx)
    {
        if (ctx.HasCurrentTour)
        {
            var (detectedSection, detectedRemainder) = DetectLabel(line);
            if (detectedSection != null)
            {
                ctx.CurrentSection = detectedSection.Value;
                line = detectedRemainder;
            }

            if (!string.IsNullOrWhiteSpace(line))
                AppendToSection(ctx.CurrentSection, line, ctx.CurrentQuestion, ctx.CurrentTour!, ctx.Result);
        }
        else
        {
            ctx.HeaderBlocks.Add(line);
        }
    }

    /// <summary>
    /// Processes any remaining text after a question number (e.g., "1. [Роздатка: ...] Question text").
    /// </summary>
    private static void ProcessRemainingTextAfterQuestionNumber(string remainingText, QuestionDto question)
    {
        if (string.IsNullOrWhiteSpace(remainingText))
            return;

        if (TryExtractBracketedHandout(remainingText, out var handout, out var afterHandout))
        {
            if (!string.IsNullOrWhiteSpace(handout))
                question.HandoutText = AppendText(question.HandoutText, handout);

            if (!string.IsNullOrWhiteSpace(afterHandout))
                question.Text = AppendText(question.Text, afterHandout);
        }
        else
        {
            question.Text = AppendText(question.Text, remainingText);
        }
    }

    /// <summary>
    /// Associates block assets with the current question or adds them to pending assets.
    /// </summary>
    private static void AssociateBlockAssets(List<AssetReference> assets, ParserContext ctx)
    {
        foreach (var asset in assets)
        {
            var isStandaloneHandoutBlock = ctx.CurrentSection == ParserSection.Handout && !ctx.QuestionCreatedInCurrentBlock;

            if (isStandaloneHandoutBlock || !ctx.HasCurrentQuestion)
            {
                ctx.PendingAssets.Add((asset, ctx.CurrentSection));
            }
            else
            {
                AssociateAsset(asset, ctx.CurrentSection, ctx.CurrentQuestion);
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

        CalculateConfidence(ctx.Result);
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

    private static bool TryParseQuestionStart(string text, out string questionNumber, out string remainingText)
    {
        questionNumber = "";
        remainingText = "";

        // Try pattern with text first (captures remaining text in group 2)
        var match = ParserPatterns.QuestionStartWithText().Match(text);
        if (match.Success)
        {
            questionNumber = match.Groups[1].Value;
            remainingText = match.Groups[2].Value.Trim();
            return true;
        }

        // Try other patterns (only capture question number)
        if (!TryMatchFirst(text, out match, ParserPatterns.QuestionStartNumberOnly(), ParserPatterns.QuestionStartNamed()))
            return false;

        questionNumber = match.Groups[1].Value;
        return true;
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

    private static void ParsePackageHeader(List<string> headerBlocks, ParseResult result)
    {
        if (headerBlocks.Count == 0) return;

        result.Title = headerBlocks.FirstOrDefault(b => !string.IsNullOrWhiteSpace(b));

        var preambleLines = new List<string>();

        foreach (var block in headerBlocks.Skip(1))
        {
            var editorsMatch = ParserPatterns.EditorsLabel().Match(block);
            if (editorsMatch.Success)
            {
                var editors = ParseAuthorList(editorsMatch.Groups[1].Value);
                result.Editors.AddRange(editors);
            }
            else
            {
                preambleLines.Add(block);
            }
        }

        if (preambleLines.Count > 0)
            result.Preamble = string.Join("\n", preambleLines);
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
