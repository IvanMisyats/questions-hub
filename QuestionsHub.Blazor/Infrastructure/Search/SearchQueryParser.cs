using System.Text;
using System.Text.RegularExpressions;

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
    /// Builds a tsquery string with :* prefix operator on each term.
    /// Uses 'simple' config semantics (no morphological normalization).
    /// Supports AND (default), OR, and negation (-word).
    /// Quoted phrases are included as phrase matches (&lt;-&gt;) without prefix.
    /// </summary>
    /// <param name="query">Raw (already normalized) search query from the user</param>
    /// <returns>
    /// A tsquery string suitable for to_tsquery('simple', ...), or null if no valid terms.
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

                    sb.Append($"!'{EscapeTsquery(token.Text)}':*");
                    needsOperator = true;
                    nextOperator = "&";
                    break;

                case TokenType.Word:
                    if (token.Text.Length < MinTermLength)
                        break;

                    if (needsOperator)
                        sb.Append($" {nextOperator} ");

                    sb.Append($"'{EscapeTsquery(token.Text)}':*");
                    needsOperator = true;
                    nextOperator = "&";
                    break;
            }
        }

        var result = sb.ToString();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    /// <summary>
    /// Appends a phrase as proximity-matched terms (word1 &lt;-&gt; word2) with :* on the last word.
    /// </summary>
    private static void AppendPhrase(StringBuilder sb, string phrase)
    {
        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return;

        if (words.Length == 1)
        {
            sb.Append($"'{EscapeTsquery(words[0])}':*");
            return;
        }

        sb.Append('(');
        for (var i = 0; i < words.Length; i++)
        {
            if (i > 0)
                sb.Append(" <-> ");

            sb.Append($"'{EscapeTsquery(words[i])}'");
            // Add prefix operator only to the last word in phrase
            if (i == words.Length - 1)
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
    /// Escapes single quotes in tsquery literals.
    /// </summary>
    private static string EscapeTsquery(string text) =>
        text.Replace("'", "''");

    internal enum TokenType { Word, Phrase, Or, Negation }

    internal record SearchToken(TokenType Type, string Text);
}
