using System.Text.RegularExpressions;

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
    // А-я covers standard Cyrillic; ІіЇїЄєҐґ adds Ukrainian-specific letters outside that range
    [GeneratedRegex(@"^\s*([ПпДдТтЧчШшСсВв][''ʼА-яІіЇїЄєҐґ]+)\s+[Тт][Уу][Рр]\s*$")]
    public static partial Regex OrdinalTourStart();

    // Matches reversed format: "Тур перший", "Тур другий", etc. (1-9)
    [GeneratedRegex(@"^\s*[Тт][Уу][Рр]\s+([ПпДдТтЧчШшСсВв][''ʼА-яІіЇїЄєҐґ]+)\s*$")]
    public static partial Regex TourOrdinalStart();

    // Matches: "1 Тур", "2 тур", "3 ТУР" (number before word "Тур")
    [GeneratedRegex(@"^\s*(\d+)\s+(?:ТУР|Тур|тур|Tour)[\.:,]?\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex NumberTourStart();

    // Matches: "Тур №1", "ТУР №2", "Тур № 3" (with № sign)
    [GeneratedRegex(@"^\s*(?:ТУР|Тур|Tour)\s*№\s*(\d+)[\.:,]?\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex TourNumberSignStart();

    // Matches: "Тур №1 — Назва", "ТУР №2. Автори" (with № sign and preamble after separator)
    [GeneratedRegex(@"^\s*(?:ТУР|Тур|Tour)\s*№\s*(\d+)[\.:,]?\s+[-–—.]?\s*(.+)$", RegexOptions.IgnoreCase)]
    public static partial Regex TourNumberSignStartWithPreamble();

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

    // Shootout (Перестрілка) detection
    [GeneratedRegex(@"^\s*Перестрілка\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex ShootoutTourStart();

    [GeneratedRegex(@"^\s*[-–—]\s*Перестрілка\s*[-–—]\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex ShootoutTourStartDashed();

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
    // Also matches: "Зараховується: ..." as a synonym
    // Separator can be colon (:) or dot with optional whitespace (.)
    [GeneratedRegex(@"^\s*(?:Залік(?:и)?|Зараховується)(?:\s*\([^)]+\))?\s*(?::|[.]\s?)\s*(.*)$", RegexOptions.IgnoreCase)]
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
