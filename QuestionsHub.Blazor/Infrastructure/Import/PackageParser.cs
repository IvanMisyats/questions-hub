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

    [GeneratedRegex(@"^\s*(?:Запитання|Питання)\s+(\d+)\.?\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex QuestionStartNamed();

    // Labels (field markers)
    [GeneratedRegex(@"^\s*Відповідь\s*:\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex AnswerLabel();

    [GeneratedRegex(@"^\s*Залік\s*:\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex AcceptedLabel();

    [GeneratedRegex(@"^\s*(?:Незалік|Не\s*залік|Не\s*приймається)\s*:\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex RejectedLabel();

    [GeneratedRegex(@"^\s*Коментар\s*:\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex CommentLabel();

    [GeneratedRegex(@"^\s*(?:Джерело|Джерела)\s*:\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex SourceLabel();

    [GeneratedRegex(@"^\s*(?:Автор|Автори)\s*:\s*(.*)$", RegexOptions.IgnoreCase)]
    public static partial Regex AuthorLabel();

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

        // Track pending assets that should be associated with the next question
        // This handles cases where handout/images appear BEFORE the question starts
        var pendingAssets = new List<(AssetReference Asset, ParserSection Section)>();

        // Track expected question numbers for sequential validation
        // Questions can be numbered either per-tour (1, 2, 3... then 1, 2, 3 in next tour)
        // or sequentially across package (1-12 in tour 1, 13-24 in tour 2, etc.)
        int? expectedNextQuestionInTour = null;  // Expected if per-tour numbering
        int? expectedNextQuestionGlobal = null;  // Expected if global numbering

        _logger.LogInformation("Parsing {BlockCount} blocks", blocks.Count);

        foreach (var block in blocks)
        {
            var text = NormalizeText(block.Text);
            if (string.IsNullOrWhiteSpace(text) && block.Assets.Count == 0)
                continue;

            // Track if this block created a new question
            // This helps distinguish standalone handout blocks from multi-line blocks with question+handout
            var questionCreatedInThisBlock = false;

            // Split block by newlines to handle multi-line paragraphs with soft line breaks
            var lines = text.Split('\n');

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex].Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Check for tour start
                if (TryParseTourStart(line, out var tourNumber))
                {
                    // Save pending question
                    if (currentQuestion != null && currentTour != null)
                    {
                        FinalizeQuestion(currentQuestion, result);
                        currentTour.Questions.Add(currentQuestion);
                    }

                    // Parse header if this is the first tour
                    if (currentTour == null && headerBlocks.Count > 0)
                    {
                        ParsePackageHeader(headerBlocks, result);
                    }

                    currentTour = new TourDto { Number = tourNumber };
                    result.Tours.Add(currentTour);
                    currentQuestion = null;
                    currentSection = ParserSection.TourHeader;

                    // Reset per-tour numbering expectation for the new tour
                    expectedNextQuestionInTour = null;

                    _logger.LogDebug("Found tour: {TourNumber}", tourNumber);
                    continue;
                }

                // Check for question start
                if (TryParseQuestionStart(line, out var questionNumber, out var remainingText))
                {
                    // Validate that question number is sequential
                    if (!IsValidNextQuestionNumber(questionNumber,
                            ref expectedNextQuestionInTour, ref expectedNextQuestionGlobal))
                    {
                        // Not a valid sequential question number, treat as regular text
                        // This handles cases like "2." in source references
                        if (currentTour != null)
                        {
                            var (detectedSection, detectedRemainder) = DetectLabel(line);
                            if (detectedSection != null)
                            {
                                currentSection = detectedSection.Value;
                                line = detectedRemainder;
                            }

                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                AppendToSection(currentSection, line, currentQuestion, currentTour, result);
                            }
                        }
                        else
                        {
                            headerBlocks.Add(line);
                        }
                        continue;
                    }

                    // Save pending question
                    if (currentQuestion != null && currentTour != null)
                    {
                        FinalizeQuestion(currentQuestion, result);
                        currentTour.Questions.Add(currentQuestion);
                    }

                    // If no tour yet, create default tour
                    if (currentTour == null)
                    {
                        // Parse header first
                        if (headerBlocks.Count > 0)
                        {
                            ParsePackageHeader(headerBlocks, result);
                        }

                        currentTour = new TourDto { Number = "1" };
                        result.Tours.Add(currentTour);
                        result.Warnings.Add("Тур не знайдено, створено тур за замовчуванням");
                    }

                    currentQuestion = new QuestionDto { Number = questionNumber };
                    currentSection = ParserSection.QuestionText;
                    questionCreatedInThisBlock = true;

                    // Apply any pending assets from blocks that appeared before this question
                    foreach (var (asset, section) in pendingAssets)
                    {
                        AssociateAsset(asset, section, currentQuestion);
                    }
                    pendingAssets.Clear();

                    // Do NOT swallow the rest of the lines in the paragraph, because they can contain
                    // labels like "Відповідь:" / "Коментар:" etc.
                    if (!string.IsNullOrWhiteSpace(remainingText))
                    {
                        // Check if remaining text starts with bracketed handout
                        if (TryExtractBracketedHandout(remainingText, out var handoutInRemaining, out var textAfterHandout))
                        {
                            if (!string.IsNullOrWhiteSpace(handoutInRemaining))
                            {
                                currentQuestion.HandoutText = AppendText(currentQuestion.HandoutText, handoutInRemaining);
                            }
                            if (!string.IsNullOrWhiteSpace(textAfterHandout))
                            {
                                currentQuestion.Text = AppendText(currentQuestion.Text, textAfterHandout);
                            }
                        }
                        else
                        {
                            currentQuestion.Text = AppendText(currentQuestion.Text, remainingText);
                        }
                    }

                    _logger.LogDebug("Found question: {QuestionNumber}", questionNumber);

                    // NOTE: Don't associate assets here! The block may contain multiple sections
                    // (e.g., "20. Text\nВідповідь: ...\nКоментар: ... [IMAGE]")
                    // Assets will be associated after label detection for proper section assignment.

                    // Continue parsing remaining lines (lineIndex+1, ...) normally
                    continue;
                }

                // If we're still in package header, collect blocks
                if (currentTour == null)
                {
                    headerBlocks.Add(line);
                    continue;
                }

                // Check for host instructions [Ведучому: ...] - handle specially
                // because they contain both instructions AND remaining question text
                if (currentQuestion != null && TryExtractHostInstructions(line, out var hostInstructions, out var afterInstructions))
                {
                    currentQuestion.HostInstructions = AppendText(currentQuestion.HostInstructions, hostInstructions);

                    // The text after ] is question text
                    if (!string.IsNullOrWhiteSpace(afterInstructions))
                    {
                        currentQuestion.Text = AppendText(currentQuestion.Text, afterInstructions);
                    }
                    continue;
                }

                // Check for bracketed handout [Роздатка: ...] or [Роздатковий матеріал: ...]
                // The text inside brackets is handout, text after is question text
                if (TryExtractBracketedHandout(line, out var handoutText, out var afterHandout))
                {
                    currentSection = ParserSection.Handout;

                    if (currentQuestion != null && !string.IsNullOrWhiteSpace(handoutText))
                    {
                        currentQuestion.HandoutText = AppendText(currentQuestion.HandoutText, handoutText);
                    }

                    // The text after ] is question text, not handout
                    if (!string.IsNullOrWhiteSpace(afterHandout))
                    {
                        // This text belongs to question text section
                        if (currentQuestion != null)
                        {
                            currentQuestion.Text = AppendText(currentQuestion.Text, afterHandout);
                        }
                        currentSection = ParserSection.QuestionText;
                    }
                    continue;
                }

                // Detect label transitions
                var (newSection, labelRemainder) = DetectLabel(line);
                if (newSection != null)
                {
                    currentSection = newSection.Value;
                    line = labelRemainder;
                }

                // Append to current section
                if (!string.IsNullOrWhiteSpace(line))
                {
                    AppendToSection(currentSection, line, currentQuestion, currentTour, result);
                }
            }

            // Associate block assets with the FINAL section detected in this block
            // This ensures images in comment sections are correctly classified
            foreach (var asset in block.Assets)
            {
                // Handout sections that appear in standalone blocks (without question start)
                // belong to the NEXT question. Handouts in the same block as a question
                // belong to that question.
                var isStandaloneHandoutBlock = currentSection == ParserSection.Handout && !questionCreatedInThisBlock;

                if (isStandaloneHandoutBlock || currentQuestion == null)
                {
                    // Queue the asset to be associated with the next question
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
            FinalizeQuestion(currentQuestion, result);
            currentTour.Questions.Add(currentQuestion);
        }

        // Parse header if no tours were found
        if (result.Tours.Count == 0 && headerBlocks.Count > 0)
        {
            ParsePackageHeader(headerBlocks, result);
        }

        // Calculate confidence
        CalculateConfidence(result);

        _logger.LogInformation(
            "Parsed {TourCount} tours, {QuestionCount} questions, confidence: {Confidence:P0}",
            result.Tours.Count, result.TotalQuestions, result.Confidence);

        return result;
    }

    private static string NormalizeText(string text)
    {
        // Replace non-breaking spaces
        text = text.Replace('\u00A0', ' ');
        // Normalize dashes
        text = text.Replace('–', '-').Replace('—', '-');
        // Remove combining acute accent (U+0301) - used in Ukrainian for stress marks
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
    /// Validates that a question number is valid in sequence.
    /// Supports two numbering schemes:
    /// 1. Per-tour: questions numbered 1, 2, 3... in each tour (optionally starting from 0)
    /// 2. Global: questions numbered continuously across tours (1-12, 13-24, etc.)
    /// </summary>
    private static bool IsValidNextQuestionNumber(
        string questionNumberStr,
        ref int? expectedNextQuestionInTour,
        ref int? expectedNextQuestionGlobal)
    {
        if (!int.TryParse(questionNumberStr, out var questionNumber))
            return false;

        // First question in the package - initialize expectations
        if (expectedNextQuestionGlobal == null)
        {
            // First question can start from 0 or 1 typically
            if (questionNumber is 0 or 1)
            {
                expectedNextQuestionGlobal = questionNumber + 1;
                expectedNextQuestionInTour = questionNumber + 1;
                return true;
            }
            return false;
        }

        // First question in a new tour (after tour header)
        if (expectedNextQuestionInTour == null)
        {
            // Either continues global numbering, or resets to 1 (or 0) for per-tour numbering
            if (questionNumber == expectedNextQuestionGlobal)
            {
                // Global numbering continues
                expectedNextQuestionGlobal = questionNumber + 1;
                expectedNextQuestionInTour = questionNumber + 1;
                return true;
            }
            if (questionNumber is 0 or 1)
            {
                // Per-tour numbering resets
                expectedNextQuestionInTour = questionNumber + 1;
                // Keep global tracking for future tours
                expectedNextQuestionGlobal = questionNumber + 1;
                return true;
            }
            return false;
        }

        // Normal case - check if it's the expected next number in either scheme
        if (questionNumber == expectedNextQuestionInTour || questionNumber == expectedNextQuestionGlobal)
        {
            expectedNextQuestionInTour = questionNumber + 1;
            expectedNextQuestionGlobal = questionNumber + 1;
            return true;
        }

        return false;
    }

    private static (ParserSection? Section, string Remainder) DetectLabel(string text)
    {
        var match = ParserPatterns.AnswerLabel().Match(text);
        if (match.Success)
            return (ParserSection.Answer, match.Groups[1].Value.Trim());

        match = ParserPatterns.AcceptedLabel().Match(text);
        if (match.Success)
            return (ParserSection.AcceptedAnswers, match.Groups[1].Value.Trim());

        match = ParserPatterns.RejectedLabel().Match(text);
        if (match.Success)
            return (ParserSection.RejectedAnswers, match.Groups[1].Value.Trim());

        match = ParserPatterns.CommentLabel().Match(text);
        if (match.Success)
            return (ParserSection.Comment, match.Groups[1].Value.Trim());

        match = ParserPatterns.SourceLabel().Match(text);
        if (match.Success)
            return (ParserSection.Source, match.Groups[1].Value.Trim());

        match = ParserPatterns.AuthorLabel().Match(text);
        if (match.Success)
            return (ParserSection.Authors, match.Groups[1].Value.Trim());

        // Note: HostInstructions are handled separately via TryExtractHostInstructions

        match = ParserPatterns.HandoutMarker().Match(text);
        if (match.Success)
            return (ParserSection.Handout, match.Groups[1].Value.Trim());

        return (null, text);
    }

    /// <summary>
    /// Tries to extract host instructions from a line that starts with [Ведучому: ...].
    /// Returns the instruction text and any remaining text after the closing bracket.
    /// </summary>
    private static bool TryExtractHostInstructions(string text, out string instructions, out string remainingText)
    {
        instructions = "";
        remainingText = "";

        var match = ParserPatterns.HostInstructionsBracket().Match(text);
        if (match.Success)
        {
            instructions = match.Groups[1].Value.Trim();
            remainingText = match.Groups[2].Value.Trim();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tries to extract handout from a line that starts with [Роздатка: ...] or [Роздатковий матеріал: ...].
    /// Returns the handout text inside brackets and any text after the closing bracket.
    /// </summary>
    private static bool TryExtractBracketedHandout(string text, out string handoutText, out string remainingText)
    {
        handoutText = "";
        remainingText = "";

        var match = ParserPatterns.HandoutMarkerBracket().Match(text);
        if (match.Success)
        {
            handoutText = match.Groups[1].Value.Trim();
            remainingText = match.Groups[2].Value.Trim();
            return true;
        }

        return false;
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
            // We're in tour header
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

        // Images that appear AFTER answer-related sections are comment attachments
        // Images that appear BEFORE answer-related sections are handouts
        var isAnswerRelatedSection = section is
            ParserSection.Answer or
            ParserSection.AcceptedAnswers or
            ParserSection.RejectedAnswers or
            ParserSection.Comment or
            ParserSection.Source or
            ParserSection.Authors;

        if (isAnswerRelatedSection)
        {
            question.CommentAssetFileName ??= asset.FileName;
        }
        else
        {
            question.HandoutAssetFileName ??= asset.FileName;
        }
    }

    private static void ParsePackageHeader(List<string> headerBlocks, ParseResult result)
    {
        if (headerBlocks.Count == 0) return;

        // First non-empty line is likely the title
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
        {
            result.Preamble = string.Join("\n", preambleLines);
        }
    }

    private static void FinalizeQuestion(QuestionDto question, ParseResult result)
    {
        if (!question.HasText)
        {
            result.Warnings.Add($"Питання {question.Number}: текст питання не знайдено");
        }

        if (!question.HasAnswer)
        {
            result.Warnings.Add($"Питання {question.Number}: відповідь не знайдено");
        }
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

        var questionsWithAnswer = result.Tours
            .SelectMany(t => t.Questions)
            .Count(q => q.HasAnswer);

        var questionsWithText = result.Tours
            .SelectMany(t => t.Questions)
            .Count(q => q.HasText);

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
