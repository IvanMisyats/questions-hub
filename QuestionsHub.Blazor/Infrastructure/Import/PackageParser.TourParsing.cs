using System.Globalization;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Utils;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Tour and block structure detection methods.
/// </summary>
public partial class PackageParser
{
    /// <summary>
    /// Attempts to parse and handle a tour start line (including warmup and shootout tours).
    /// Also handles bold warmup question labels (e.g., "Розминочне питання") that create implicit warmup tours.
    /// </summary>
    private bool TryProcessTourStart(string line, ParserContext ctx)
    {
        var tourType = TourType.Regular;
        string tourNumber;
        string? preamble = null;

        // Check for warmup tour first
        if (TryParseWarmupTourStart(line))
        {
            tourType = TourType.Warmup;
            tourNumber = "0";
        }
        // Check for bold warmup question label (only before any tours exist)
        else if (TryParseWarmupQuestionLabel(line, ctx))
        {
            tourType = TourType.Warmup;
            tourNumber = "0";
        }
        // Check for shootout tour
        else if (TryParseShootoutTourStart(line))
        {
            tourType = TourType.Shootout;
            tourNumber = "П";
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
            Type = tourType,
            Preamble = preamble
        };
        ctx.Result.Tours.Add(ctx.CurrentTour);
        ctx.CurrentBlockDto = null;
        ctx.CurrentQuestion = null;
        ctx.CurrentSection = ParserSection.TourHeader;
        ctx.ExpectedNextQuestionInTour = null;
        ctx.Format = QuestionFormat.Unknown; // Allow each tour to use its own format

        _logger.LogDebug("Found tour: {TourNumber}, Type: {TourType}", tourNumber, tourType);
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

        // Try № sign with preamble: "Тур №1 — Автор", "ТУР №2. Назва"
        var numberSignPreambleMatch = ParserPatterns.TourNumberSignStartWithPreamble().Match(text);
        if (numberSignPreambleMatch.Success)
        {
            tourNumber = numberSignPreambleMatch.Groups[1].Value;
            preamble = numberSignPreambleMatch.Groups[2].Value.Trim();
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

    private static bool TryParseShootoutTourStart(string text)
    {
        return ParserPatterns.ShootoutTourStart().IsMatch(text) ||
               ParserPatterns.ShootoutTourStartDashed().IsMatch(text);
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
}
