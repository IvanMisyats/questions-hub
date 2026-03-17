using System.Text;
using System.Text.RegularExpressions;
using QuestionsHub.Blazor.Utils;

namespace QuestionsHub.Blazor.Infrastructure.Search;

/// <summary>
/// Parses user search queries and builds PostgreSQL tsquery expressions with prefix matching support.
/// </summary>
public static partial class SearchQueryParser
{
    /// <summary>
    /// Minimum term length for prefix matching. Shorter terms are ignored.
    /// </summary>
    private const int MinTermLength = 2;

    /// <summary>
    /// Characters treated as apostrophes. PostgreSQL's unaccent converts U+02BC to U+0027,
    /// which would break tsquery syntax if left inside lexeme quotes. We split words at these
    /// characters and use the adjacency operator, mirroring how PostgreSQL's text parser tokenizes.
    /// </summary>
    private static readonly char[] ApostropheChars = ['\'', '\u02BC'];

    /// <summary>
    /// Builds a pre-normalized tsquery string with :* prefix operator on each term.
    /// Each term is normalized (lowercase, accent-stripped, ґ→г) to match the SearchVector,
    /// so the result can be passed directly to to_tsquery('simple', ...) without qh_normalize.
    /// Supports AND (default), OR, and negation (-word).
    /// Quoted phrases are included as phrase matches (&lt;-&gt;) without prefix.
    /// </summary>
    /// <param name="query">Search query with apostrophes already unified to U+02BC</param>
    /// <returns>
    /// A pre-normalized tsquery string suitable for to_tsquery('simple', ...), or null if no valid terms.
    /// Example: "'сепул':* &amp; 'антарктида':*" or "'амундсен':* | 'скотт':*"
    /// </returns>
    public static string? BuildPrefixTsquery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var tokens = Tokenize(query);
        if (tokens.Count == 0)
            return null;

        var sb = new StringBuilder();
        var needsOperator = false;
        var nextOperator = "&"; // default is AND

        foreach (var token in tokens)
        {
            switch (token.Type)
            {
                case TokenType.Or:
                    nextOperator = "|";
                    break;

                case TokenType.Phrase:
                    if (token.Text.Length < MinTermLength)
                        break;

                    if (needsOperator)
                        sb.Append($" {nextOperator} ");

                    AppendPhrase(sb, token.Text);
                    needsOperator = true;
                    nextOperator = "&";
                    break;

                case TokenType.Negation:
                    if (token.Text.Length < MinTermLength)
                        break;

                    if (needsOperator)
                        sb.Append(" & ");

                    sb.Append('!');
                    AppendTerm(sb, token.Text, prefix: true);
                    needsOperator = true;
                    nextOperator = "&";
                    break;

                case TokenType.Word:
                    if (token.Text.Length < MinTermLength)
                        break;

                    if (needsOperator)
                        sb.Append($" {nextOperator} ");

                    AppendTerm(sb, token.Text, prefix: true);
                    needsOperator = true;
                    nextOperator = "&";
                    break;
            }
        }

        var result = sb.ToString();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    /// <summary>
    /// Appends a single term, splitting at apostrophes into adjacent parts if needed.
    /// Each part is FTS-normalized (lowercase, accents removed, ґ→г) so the resulting tsquery
    /// matches the SearchVector without needing SQL-side qh_normalize.
    /// </summary>
    private static void AppendTerm(StringBuilder sb, string word, bool prefix)
    {
        var parts = word.Split(ApostropheChars, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        if (parts.Length == 1)
        {
            sb.Append($"'{EscapeTsquery(NormalizePart(parts[0]))}'");
            if (prefix) sb.Append(":*");
            return;
        }

        sb.Append('(');
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
                sb.Append(" <-> ");

            sb.Append($"'{EscapeTsquery(NormalizePart(parts[i]))}'");
            if (prefix && i == parts.Length - 1)
                sb.Append(":*");
        }
        sb.Append(')');
    }

    /// <summary>
    /// Appends a phrase as proximity-matched terms (word1 &lt;-&gt; word2) with :* on the last word.
    /// Words containing apostrophes are flattened into the adjacency chain.
    /// Each part is FTS-normalized.
    /// </summary>
    private static void AppendPhrase(StringBuilder sb, string phrase)
    {
        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return;

        // Flatten all words, splitting each at apostrophes, normalizing each part
        var allParts = new List<string>();
        foreach (var word in words)
        {
            var parts = word.Split(ApostropheChars, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
                allParts.Add(NormalizePart(part));
        }

        if (allParts.Count == 0) return;

        if (allParts.Count == 1)
        {
            sb.Append($"'{EscapeTsquery(allParts[0])}':*");
            return;
        }

        sb.Append('(');
        for (var i = 0; i < allParts.Count; i++)
        {
            if (i > 0)
                sb.Append(" <-> ");

            sb.Append($"'{EscapeTsquery(allParts[i])}'");
            if (i == allParts.Count - 1)
                sb.Append(":*");
        }
        sb.Append(')');
    }

    /// <summary>
    /// Tokenizes the search query into words, phrases, operators, and negations.
    /// </summary>
    internal static List<SearchToken> Tokenize(string query)
    {
        var tokens = new List<SearchToken>();
        var i = 0;

        while (i < query.Length)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(query[i]))
            {
                i++;
                continue;
            }

            // Quoted phrase
            if (query[i] == '"')
            {
                var end = query.IndexOf('"', i + 1);
                if (end > i + 1)
                {
                    var phrase = query[(i + 1)..end].Trim();
                    if (phrase.Length >= MinTermLength)
                        tokens.Add(new SearchToken(TokenType.Phrase, phrase));
                    i = end + 1;
                }
                else
                {
                    // Unclosed quote — treat rest as a phrase
                    var phrase = query[(i + 1)..].Trim();
                    if (phrase.Length >= MinTermLength)
                        tokens.Add(new SearchToken(TokenType.Phrase, phrase));
                    break;
                }
                continue;
            }

            // Extract word
            var wordStart = i;
            while (i < query.Length && !char.IsWhiteSpace(query[i]) && query[i] != '"')
                i++;

            var word = query[wordStart..i];

            // Check for OR operator
            if (word.Equals("OR", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(new SearchToken(TokenType.Or, word));
                continue;
            }

            // Check for negation
            if (word.StartsWith('-') && word.Length > 1)
            {
                tokens.Add(new SearchToken(TokenType.Negation, word[1..]));
                continue;
            }

            if (word.Length >= MinTermLength)
                tokens.Add(new SearchToken(TokenType.Word, word));
        }

        // Remove trailing OR (invalid)
        while (tokens.Count > 0 && tokens[^1].Type == TokenType.Or)
            tokens.RemoveAt(tokens.Count - 1);

        // Remove leading OR (invalid)
        while (tokens.Count > 0 && tokens[0].Type == TokenType.Or)
            tokens.RemoveAt(0);

        return tokens;
    }

    /// <summary>
    /// FTS-normalizes a term part: lowercase, remove accents, ґ→г.
    /// Mirrors the relevant parts of PostgreSQL's qh_normalize for individual words.
    /// </summary>
    private static string NormalizePart(string part)
    {
        part = TextNormalizer.RemoveAccents(part);
        part = part.ToLowerInvariant();
        part = part.Replace('ґ', 'г');
        return part;
    }

    /// <summary>
    /// Escapes single quotes in tsquery literals.
    /// </summary>
    private static string EscapeTsquery(string text) =>
        text.Replace("'", "''");

    internal enum TokenType { Word, Phrase, Or, Negation }

    internal record SearchToken(TokenType Type, string Text);
}
