using System.Globalization;
using QuestionsHub.Blazor.Utils;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Question detection and processing methods.
/// </summary>
public partial class PackageParser
{
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
        // But allow through if the number matches the expected next question
        // (e.g., after an empty "Джерело:" label, the next question should still be detected)
        if (detectedFormat == QuestionFormat.Numbered &&
            ctx.CurrentSection == ParserSection.Source &&
            !IsExpectedNextQuestionNumber(questionNumber, ctx))
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
}
