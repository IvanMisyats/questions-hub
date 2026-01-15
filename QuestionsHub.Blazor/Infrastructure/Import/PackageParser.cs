using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

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

    public PackageParser(ILogger<PackageParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses extracted document blocks into a package structure.
    /// </summary>
    public ParseResult Parse(List<DocBlock> blocks, List<AssetReference> assets)
    {
        var result = new ParseResult();
        var currentSection = ParserSection.PackageHeader;
        TourDto? currentTour = null;
        QuestionDto? currentQuestion = null;
        var headerBlocks = new List<string>();

        // Assets that appear BEFORE a question starts (standalone handout blocks, etc.)
        var pendingAssets = new List<(AssetReference Asset, ParserSection Section)>();

        // Author ranges like "Автор запитань 1-18: ..."
        var authorRanges = new List<AuthorRangeRule>();

        // Numbering mode heuristic
        var numberingMode = NumberingMode.Unknown;

        // Sequence validation
        int? expectedNextQuestionInTour = null;
        int? expectedNextQuestionGlobal = null;

        _logger.LogInformation("Parsing {BlockCount} blocks", blocks.Count);

        foreach (var block in blocks)
        {
            var text = NormalizeText(block.Text);
            if (string.IsNullOrWhiteSpace(text) && block.Assets.Count == 0)
                continue;

            var questionCreatedInThisBlock = false;

            // Split block by newlines to handle soft line breaks
            var lines = text.Split('\n');

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex].Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Tour start
                if (TryParseTourStart(line, out var tourNumber))
                {
                    if (currentQuestion != null && currentTour != null)
                    {
                        FinalizeQuestion(currentQuestion, authorRanges, result);
                        currentTour.Questions.Add(currentQuestion);
                    }

                    if (currentTour == null && headerBlocks.Count > 0)
                    {
                        ParsePackageHeader(headerBlocks, result);
                    }

                    currentTour = new TourDto { Number = tourNumber };
                    result.Tours.Add(currentTour);
                    currentQuestion = null;
                    currentSection = ParserSection.TourHeader;

                    // Reset per-tour expected number (global continues)
                    expectedNextQuestionInTour = null;

                    _logger.LogDebug("Found tour: {TourNumber}", tourNumber);
                    continue;
                }

                // Author range lines often appear in header (before first question)
                if (currentQuestion == null)
                {
                    var rangeMatch = ParserPatterns.AuthorRangeLabel().Match(line);
                    if (rangeMatch.Success)
                    {
                        var from = int.Parse(rangeMatch.Groups[1].Value);
                        var to = int.Parse(rangeMatch.Groups[2].Value);
                        var authorsText = rangeMatch.Groups[3].Value.Trim();
                        var authors = ParseAuthorList(authorsText);

                        if (authors.Count > 0)
                            authorRanges.Add(new AuthorRangeRule(from, to, authors));

                        continue;
                    }
                }

                // Question start
                if (TryParseQuestionStart(line, out var questionNumber, out var remainingText))
                {
                    if (!IsValidNextQuestionNumber(
                            questionNumber,
                            ref expectedNextQuestionInTour,
                            ref expectedNextQuestionGlobal,
                            ref numberingMode))
                    {
                        // Not a valid sequential question number -> treat as regular text
                        if (currentTour != null)
                        {
                            var (detectedSection, detectedRemainder) = DetectLabel(line);
                            if (detectedSection != null)
                            {
                                currentSection = detectedSection.Value;
                                line = detectedRemainder;
                            }

                            if (!string.IsNullOrWhiteSpace(line))
                                AppendToSection(currentSection, line, currentQuestion, currentTour, result);
                        }
                        else
                        {
                            headerBlocks.Add(line);
                        }

                        continue;
                    }

                    // Save previous question
                    if (currentQuestion != null && currentTour != null)
                    {
                        FinalizeQuestion(currentQuestion, authorRanges, result);
                        currentTour.Questions.Add(currentQuestion);
                    }

                    // If no tour, create default
                    if (currentTour == null)
                    {
                        if (headerBlocks.Count > 0)
                            ParsePackageHeader(headerBlocks, result);

                        currentTour = new TourDto { Number = "1" };
                        result.Tours.Add(currentTour);
                        result.Warnings.Add("Тур не знайдено, створено тур за замовчуванням");
                    }

                    currentQuestion = new QuestionDto { Number = questionNumber };
                    currentSection = ParserSection.QuestionText;
                    questionCreatedInThisBlock = true;

                    // Apply pending assets that appeared before the question
                    foreach (var (asset, section) in pendingAssets)
                        AssociateAsset(asset, section, currentQuestion);
                    pendingAssets.Clear();

                    // DO NOT swallow remaining lines in the paragraph (labels may follow)
                    if (!string.IsNullOrWhiteSpace(remainingText))
                    {
                        // Remaining text may start with bracketed handout
                        if (TryExtractBracketedHandout(remainingText, out var handoutInRemaining, out var textAfterHandout))
                        {
                            if (!string.IsNullOrWhiteSpace(handoutInRemaining))
                                currentQuestion.HandoutText = AppendText(currentQuestion.HandoutText, handoutInRemaining);

                            if (!string.IsNullOrWhiteSpace(textAfterHandout))
                                currentQuestion.Text = AppendText(currentQuestion.Text, textAfterHandout);
                        }
                        else
                        {
                            currentQuestion.Text = AppendText(currentQuestion.Text, remainingText);
                        }
                    }

                    _logger.LogDebug("Found question: {QuestionNumber}", questionNumber);

                    // Continue parsing next lines of the same block
                    continue;
                }

                // Package header collection
                if (currentTour == null)
                {
                    headerBlocks.Add(line);
                    continue;
                }

                // Host instructions [Ведучому: ...]
                if (currentQuestion != null && TryExtractHostInstructions(line, out var hostInstructions, out var afterInstructions))
                {
                    currentQuestion.HostInstructions = AppendText(currentQuestion.HostInstructions, hostInstructions);

                    if (!string.IsNullOrWhiteSpace(afterInstructions))
                        currentQuestion.Text = AppendText(currentQuestion.Text, afterInstructions);

                    continue;
                }

                // Bracketed handout [Роздатка: ...] Question text...
                if (TryExtractBracketedHandout(line, out var handoutText, out var afterHandout))
                {
                    currentSection = ParserSection.Handout;

                    if (currentQuestion != null && !string.IsNullOrWhiteSpace(handoutText))
                        currentQuestion.HandoutText = AppendText(currentQuestion.HandoutText, handoutText);

                    if (!string.IsNullOrWhiteSpace(afterHandout))
                    {
                        if (currentQuestion != null)
                            currentQuestion.Text = AppendText(currentQuestion.Text, afterHandout);

                        currentSection = ParserSection.QuestionText;
                    }

                    continue;
                }

                // Label transitions
                var (newSection, labelRemainder) = DetectLabel(line);
                if (newSection != null)
                {
                    currentSection = newSection.Value;
                    line = labelRemainder;
                }

                // Append line to current section
                if (!string.IsNullOrWhiteSpace(line))
                    AppendToSection(currentSection, line, currentQuestion, currentTour, result);
            }

            // Associate block assets with the FINAL section detected in this block.
            foreach (var asset in block.Assets)
            {
                var isStandaloneHandoutBlock = currentSection == ParserSection.Handout && !questionCreatedInThisBlock;

                if (isStandaloneHandoutBlock || currentQuestion == null)
                {
                    pendingAssets.Add((asset, currentSection));
                }
                else
                {
                    AssociateAsset(asset, currentSection, currentQuestion);
                }
            }
        }

        // Save final question
        if (currentQuestion != null && currentTour != null)
        {
            FinalizeQuestion(currentQuestion, authorRanges, result);
            currentTour.Questions.Add(currentQuestion);
        }

        // Parse header if no tours found
        if (result.Tours.Count == 0 && headerBlocks.Count > 0)
        {
            ParsePackageHeader(headerBlocks, result);
        }

        CalculateConfidence(result);

        _logger.LogInformation(
            "Parsed {TourCount} tours, {QuestionCount} questions, confidence: {Confidence:P0}",
            result.Tours.Count, result.TotalQuestions, result.Confidence);

        return result;
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

        var match = ParserPatterns.TourStart().Match(text);
        if (match.Success)
        {
            tourNumber = match.Groups[1].Value;
            return true;
        }

        match = ParserPatterns.TourStartDashed().Match(text);
        if (match.Success)
        {
            tourNumber = match.Groups[1].Value;
            return true;
        }

        return false;
    }

    private static bool TryParseQuestionStart(string text, out string questionNumber, out string remainingText)
    {
        questionNumber = "";
        remainingText = "";

        var match = ParserPatterns.QuestionStartWithText().Match(text);
        if (match.Success)
        {
            questionNumber = match.Groups[1].Value;
            remainingText = match.Groups[2].Value.Trim();
            return true;
        }

        match = ParserPatterns.QuestionStartNumberOnly().Match(text);
        if (match.Success)
        {
            questionNumber = match.Groups[1].Value;
            return true;
        }

        match = ParserPatterns.QuestionStartNamed().Match(text);
        if (match.Success)
        {
            questionNumber = match.Groups[1].Value;
            return true;
        }

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

    private static (ParserSection? Section, string Remainder) DetectLabel(string text)
    {
        var match = ParserPatterns.AnswerLabel().Match(text);
        if (match.Success) return (ParserSection.Answer, match.Groups[1].Value.Trim());

        match = ParserPatterns.AcceptedLabel().Match(text);
        if (match.Success) return (ParserSection.AcceptedAnswers, match.Groups[1].Value.Trim());

        match = ParserPatterns.RejectedLabel().Match(text);
        if (match.Success) return (ParserSection.RejectedAnswers, match.Groups[1].Value.Trim());

        match = ParserPatterns.CommentLabel().Match(text);
        if (match.Success) return (ParserSection.Comment, match.Groups[1].Value.Trim());

        match = ParserPatterns.SourceLabel().Match(text);
        if (match.Success) return (ParserSection.Source, match.Groups[1].Value.Trim());

        match = ParserPatterns.AuthorLabel().Match(text);
        if (match.Success) return (ParserSection.Authors, match.Groups[1].Value.Trim());

        match = ParserPatterns.HandoutMarker().Match(text);
        if (match.Success) return (ParserSection.Handout, match.Groups[1].Value.Trim());

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
