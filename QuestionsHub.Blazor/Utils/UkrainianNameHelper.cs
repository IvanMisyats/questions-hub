using System.Text.RegularExpressions;

namespace QuestionsHub.Blazor.Utils;

/// <summary>
/// Provides heuristic conversion of Ukrainian names from genitive case (родовий відмінок)
/// to nominative case (називний відмінок).
///
/// This is used for parsing patterns like "Блок Станіслава Мерляна" → "Станіслав Мерлян"
/// and "у редакції Едуарда Голуба" → "Едуард Голуб".
///
/// The rules are heuristic and may not cover all edge cases. Names that cannot be
/// confidently converted are returned as-is.
/// </summary>
public static partial class UkrainianNameHelper
{
    /// <summary>
    /// Regex to match "у редакції ..." / "в редакції ..." pattern in author strings.
    /// Captures the text after "у/в редакції" which contains the editor name(s).
    /// </summary>
    [GeneratedRegex(@"\s+[ув]\s+редакції\s+", RegexOptions.IgnoreCase)]
    private static partial Regex RedakciyaPattern();

    /// <summary>
    /// Regex to match "за ідеєю ..." pattern in author strings.
    /// Captures the text after "за ідеєю" which contains the idea author name(s).
    /// </summary>
    [GeneratedRegex(@"\s+за\s+ідеєю\s+", RegexOptions.IgnoreCase)]
    private static partial Regex ZaIdeyeyuPattern();

    /// <summary>
    /// Regex to strip city in parentheses from author name (e.g., "(Одеса)", "(Київ)", "(Харків - Берлін)").
    /// </summary>
    [GeneratedRegex(@"\s*\([^)]*\)\s*")]
    private static partial Regex CityInParentheses();

    /// <summary>
    /// Splits an author string on "у редакції" / "в редакції" / "за ідеєю" patterns,
    /// converts genitive names to nominative, and strips city parentheses.
    /// Returns a list of clean author names in nominative case.
    ///
    /// Example:
    /// "Станіслав Мерлян (Одеса) у редакції Едуарда Голуба (Київ)"
    /// → ["Станіслав Мерлян", "Едуард Голуб"]
    /// </summary>
    public static List<string> SplitAndNormalizeAuthors(string authorText)
    {
        var results = new List<string>();

        // Split on "у/в редакції" and "за ідеєю" patterns
        var parts = RedakciyaPattern().Split(authorText);
        var expanded = new List<string>();
        foreach (var part in parts)
        {
            expanded.AddRange(ZaIdeyeyuPattern().Split(part));
        }

        for (var i = 0; i < expanded.Count; i++)
        {
            var part = expanded[i].Trim().TrimEnd('.', ',', ';');
            if (string.IsNullOrWhiteSpace(part))
                continue;

            // Strip city parentheses
            var cleanName = StripCity(part);
            if (string.IsNullOrWhiteSpace(cleanName))
                continue;

            // The first part (index 0) is already in nominative case (the main author).
            // Subsequent parts (after "у редакції" / "за ідеєю") are in genitive case.
            if (i > 0)
            {
                cleanName = ConvertFullNameToNominative(cleanName);
            }

            if (!string.IsNullOrWhiteSpace(cleanName))
                results.Add(cleanName);
        }

        return results;
    }

    /// <summary>
    /// Converts a full name (first name + last name) from genitive to nominative case.
    /// Handles both "FirstName LastName" and single-word names.
    /// </summary>
    public static string ConvertFullNameToNominative(string genitiveFullName)
    {
        var parts = genitiveFullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            0 => genitiveFullName,
            1 => ConvertToNominative(parts[0]),
            // For "FirstName LastName" format: convert each part separately
            // First part = first name (genitive), Second part = last name (genitive)
            2 => $"{ConvertFirstNameToNominative(parts[0])} {ConvertLastNameToNominative(parts[1])}",
            // For longer names, convert first and last, keep middle parts
            _ => string.Join(" ", parts.Select((p, idx) =>
                idx == 0 ? ConvertFirstNameToNominative(p) :
                idx == parts.Length - 1 ? ConvertLastNameToNominative(p) :
                ConvertToNominative(p)))
        };
    }

    /// <summary>
    /// Converts a Ukrainian first name (ім'я) from genitive to nominative case.
    /// Applies rules specific to first names.
    /// </summary>
    public static string ConvertFirstNameToNominative(string genitive)
    {
        if (string.IsNullOrWhiteSpace(genitive))
            return genitive;

        var name = genitive.Trim();

        // Common Ukrainian male first name patterns (genitive → nominative):
        // -а → remove (consonant stem): Станіслава → Станіслав, Едуарда → Едуард, Антона → Антон
        // -я → -й: Андрія → Андрій, Сергія → Сергій, Олексія → Олексій, Дмитрія → Дмитрій
        // But: Ігоря → Ігор (remove -я after р)
        // -і → -ь or -а: not common for first names
        // -а after soft consonant: Іллі → Ілля (rarely in this pattern)
        // -і → -я: Дарʼї → Дарʼя (feminine genitive)
        // -и → -а: Катерини → Катерина (feminine genitive)

        // Female names: -и → -а (Катерини → Катерина, Марини → Марина, Ірини → Ірина)
        // Female names: -і → -я (Дарʼї → Дарʼя, Наталії → Наталія, Вікторії → Вікторія)
        // Female names: -и → -а (Олени → Олена)

        return ConvertToNominative(name);
    }

    /// <summary>
    /// Converts a Ukrainian last name (прізвище) from genitive to nominative case.
    /// Applies rules specific to last names.
    /// </summary>
    public static string ConvertLastNameToNominative(string genitive)
    {
        if (string.IsNullOrWhiteSpace(genitive))
            return genitive;

        var name = genitive.Trim();

        // Adjectival surnames ending in -ого/-ього → -ий/-ій
        // Стаханового → Стахановий, Сільвестрового → Сільвестровий
        // But these are rare; more common: -ського → -ський, -цького → -цький
        if (name.EndsWith("ського", StringComparison.Ordinal))
            return name[..^6] + "ський";
        if (name.EndsWith("цького", StringComparison.Ordinal))
            return name[..^6] + "цький";
        if (name.EndsWith("зького", StringComparison.Ordinal))
            return name[..^6] + "зький";

        // Feminine adjectival: -ської → -ська, -цької → -цька
        if (name.EndsWith("ської", StringComparison.Ordinal))
            return name[..^5] + "ська";
        if (name.EndsWith("цької", StringComparison.Ordinal))
            return name[..^5] + "цька";

        // Common surname patterns (genitive → nominative):
        return ConvertToNominative(name);
    }

    /// <summary>
    /// General genitive → nominative conversion for a single Ukrainian word.
    /// Uses heuristic suffix rules ordered from most specific to least specific.
    /// </summary>
    public static string ConvertToNominative(string genitive)
    {
        if (string.IsNullOrWhiteSpace(genitive) || genitive.Length < 3)
            return genitive;

        var name = genitive.Trim();

        // Preserve original case pattern
        var isCapitalized = char.IsUpper(name[0]);

        // === Specific multi-character suffix rules (most specific first) ===

        // -ові → -о (Дмитрові → Дмитро) - rare in genitive, more dative
        // -еві → -е (rare)

        // -ії → -ія (Наталії → Наталія, Вікторії → Вікторія)
        if (name.EndsWith("ії", StringComparison.Ordinal))
            return name[..^2] + "ія";

        // -ої → -а (feminine adjectival: Кочемирової → Кочемирова)
        // But also: -ої → -а for some patterns - skip, too ambiguous

        // -ього → -ій (adjectival: Сільвестрового is unlikely, but just in case)
        if (name.EndsWith("ього", StringComparison.Ordinal))
            return name[..^4] + "ій";

        // -ого → -ий (adjectival masculine genitive)
        if (name.EndsWith("ого", StringComparison.Ordinal))
            return name[..^3] + "ий";

        // -ої → -а (feminine adjectival, but ambiguous - skip for now)

        // === Two-character suffix rules ===

        // -ці → -ка (Монастирьовці is not a name pattern we need)
        // -ки → -ка (Авторки → Авторка — not a name)

        // -ів → remove or change (Грищуків → Грищук, but this is plural genitive, not what we need)

        // Names ending in -ні → -нь? No, too rare.

        // -ка → skip, nominative form (Олександрівка is a place)

        // -ві → -ва and -ви → -ва: Реві/Реви → Рева (surnames ending in -а, genitive -и/-і)
        if (name.EndsWith("ві", StringComparison.Ordinal) || name.EndsWith("ви", StringComparison.Ordinal))
            return name[..^2] + "ва";

        // -ні → -нь (rare for names) or -на
        // Careful: Ненашевій → not a standard genitive
        // Skip this rule as it's ambiguous

        // -ої → -а (Малої → Мала — but this is adjectival, handled above)

        // -ьї → -ья / -ʼї → -ʼя (Дарʼї → Дарʼя, Наталії handled above)
        if (name.EndsWith("ʼї", StringComparison.Ordinal) ||
            name.EndsWith("'ї", StringComparison.Ordinal))
            return name[..^1] + "я";

        // === Single-character suffix rules (least specific) ===

        // -і → -ь (for soft-stem masculines: Ігорі → Ігорь... no, Ігоря → Ігор)
        // -і can also be → -а (Реві → Рева — handled above as -ві → -ва)
        // -і at end after consonant → -ь (Стасі → Стась) — rare for our use case
        // Skip bare -і rule as it's too ambiguous

        // -и → -а (feminine: Катерини → Катерина, Олени → Олена, Ірини → Ірина, Марини → Марина)
        if (name.EndsWith("ини", StringComparison.Ordinal))
            return name[..^1] + "а";
        if (name.EndsWith("ени", StringComparison.Ordinal))
            return name[..^1] + "а";
        // Generic -и → -а for other feminine names (Кікуни → Кікуна? No)
        // Too ambiguous for generic rule

        // -я → remove я and check:
        // After р: Ігоря → Ігор (remove -я)
        // After soft consonant: Сергія → Сергій (-ія → -ій)
        // Андрія → Андрій, Олексія → Олексій, Дмитрія → Дмитрій
        if (name.EndsWith("ія", StringComparison.Ordinal))
            return name[..^2] + "ій";

        if (name.EndsWith("ря", StringComparison.Ordinal))
            return name[..^1]; // Ігоря → Ігор

        if (name.EndsWith("ця", StringComparison.Ordinal))
            return name[..^2] + "ць"; // Стрільця → Стрілець... not common for names

        // -я after vowel+consonant: just remove -я
        // Дмитра is -а not -я, handled below

        // -а → remove (masculine consonant-stem names):
        // Станіслава → Станіслав, Едуарда → Едуард, Антона → Антон
        // Мерляна → Мерлян, Голуба → Голуб, Купермана → Куперман
        // Моісєєва → Моісєєв (this actually ends in -а after в)
        // Also feminine names: Олександра → Олександр (masculine)... but Олександра can be a fem name in nominative
        // For our purposes (block authors are typically male), removing -а is a safe default
        if (name.EndsWith('а'))
        {
            var stem = name[..^1];
            // Make sure the stem ends in a consonant (not a vowel)
            if (stem.Length > 0 && IsUkrainianConsonantLetter(stem[^1]))
                return stem;
        }

        // -у → remove (some masculine patterns): Дмитру → Дмитр... no, that's dative
        // Skip - dative case, not genitive

        // If no rule matched, return as-is
        return name;
    }

    /// <summary>
    /// Strips city names in parentheses from an author string.
    /// E.g., "Станіслав Мерлян (Одеса)" → "Станіслав Мерлян"
    /// </summary>
    public static string StripCity(string name)
    {
        return CityInParentheses().Replace(name, " ").Trim();
    }

    /// <summary>
    /// Checks if a character is a Ukrainian consonant letter.
    /// </summary>
    private static bool IsUkrainianConsonantLetter(char c)
    {
        // Ukrainian consonants (both cases)
        const string consonants = "бвгґджзйклмнпрстфхцчшщБВГҐДЖЗЙКЛМНПРСТФХЦЧШЩ";
        return consonants.Contains(c);
    }
}
