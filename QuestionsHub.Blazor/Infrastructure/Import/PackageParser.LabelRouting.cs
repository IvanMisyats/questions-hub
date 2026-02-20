using System.Text.RegularExpressions;
using QuestionsHub.Blazor.Utils;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Label detection and content routing methods.
/// </summary>
public partial class PackageParser
{
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
            "Зараховується:",
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
}
