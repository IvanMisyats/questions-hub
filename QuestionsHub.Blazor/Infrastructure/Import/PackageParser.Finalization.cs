using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Utils;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Finalization, numbering detection, and header parsing methods.
/// </summary>
public partial class PackageParser
{
    /// <summary>
    /// Finalizes the parsing result: saves the last question, parses remaining header, and calculates confidence.
    /// </summary>
    private static void FinalizeParsingResult(ParserContext ctx)
    {
        SaveCurrentQuestion(ctx);

        if (ctx.Result.Tours.Count == 0 && ctx.HeaderBlocks.Count > 0)
            ParsePackageHeader(ctx.HeaderBlocks, ctx.Result);

        // Ensure special tours are in correct positions
        EnsureSpecialTourPositions(ctx.Result);

        // Detect and set numbering mode
        DetectNumberingMode(ctx);

        // Assign OrderIndex values
        AssignTourOrderIndices(ctx.Result);

        // Trim leading/trailing blank lines from all question text fields
        TrimQuestionBlankLines(ctx.Result);

        CalculateConfidence(ctx.Result);
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
    /// Ensures special tours are in correct positions: warmup first, shootout last.
    /// </summary>
    private static void EnsureSpecialTourPositions(ParseResult result)
    {
        var warmupTour = result.Tours.FirstOrDefault(t => t.Type == TourType.Warmup);
        var shootoutTour = result.Tours.FirstOrDefault(t => t.Type == TourType.Shootout);

        // Remove special tours from their current positions
        if (warmupTour != null) result.Tours.Remove(warmupTour);
        if (shootoutTour != null) result.Tours.Remove(shootoutTour);

        // Re-insert: warmup at front, shootout at end
        if (warmupTour != null) result.Tours.Insert(0, warmupTour);
        if (shootoutTour != null) result.Tours.Add(shootoutTour);
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
}
