using FluentAssertions;
using QuestionsHub.Blazor.Infrastructure.Search;
using Xunit;

namespace QuestionsHub.UnitTests;

public class SearchQueryParserTests
{
    [Fact]
    public void BuildPrefixTsquery_SingleWord_ReturnsPrefixTsquery()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("сепул");

        result.Should().Be("'сепул':*");
    }

    [Fact]
    public void BuildPrefixTsquery_MultipleWords_JoinsWithAnd()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("Амундсен Антарктида");

        result.Should().Be("'Амундсен':* & 'Антарктида':*");
    }

    [Fact]
    public void BuildPrefixTsquery_OrOperator_JoinsWithOr()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("Амундсен OR Скотт");

        result.Should().Be("'Амундсен':* | 'Скотт':*");
    }

    [Fact]
    public void BuildPrefixTsquery_OrOperator_CaseInsensitive()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("кіт or собака");

        result.Should().Be("'кіт':* | 'собака':*");
    }

    [Fact]
    public void BuildPrefixTsquery_Negation_ProducesNotOperator()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("-Скотт Амундсен");

        result.Should().Be("!'Скотт':* & 'Амундсен':*");
    }

    [Fact]
    public void BuildPrefixTsquery_NegationAfterWord_ProducesAndNot()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("Амундсен -Скотт");

        result.Should().Be("'Амундсен':* & !'Скотт':*");
    }

    [Fact]
    public void BuildPrefixTsquery_QuotedPhrase_UsesPhraseOperator()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("\"Південний полюс\"");

        result.Should().Be("('Південний' <-> 'полюс':*)");
    }

    [Fact]
    public void BuildPrefixTsquery_QuotedPhraseWithOtherWords_CombinesPhraseAndWord()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("\"Південний полюс\" Амундсен");

        result.Should().Be("('Південний' <-> 'полюс':*) & 'Амундсен':*");
    }

    [Fact]
    public void BuildPrefixTsquery_SingleWordPhrase_TreatsAsPrefix()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("\"Амундсен\"");

        result.Should().Be("'Амундсен':*");
    }

    [Fact]
    public void BuildPrefixTsquery_ShortTerms_SkipsTermsShorterThan2Chars()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("я і ти");

        // "я" and "і" are 1 char, skipped. "ти" is 2 chars, kept.
        result.Should().Be("'ти':*");
    }

    [Fact]
    public void BuildPrefixTsquery_AllShortTerms_ReturnsNull()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("я і");

        result.Should().BeNull();
    }

    [Fact]
    public void BuildPrefixTsquery_Null_ReturnsNull()
    {
        var result = SearchQueryParser.BuildPrefixTsquery(null);

        result.Should().BeNull();
    }

    [Fact]
    public void BuildPrefixTsquery_EmptyString_ReturnsNull()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("");

        result.Should().BeNull();
    }

    [Fact]
    public void BuildPrefixTsquery_WhitespaceOnly_ReturnsNull()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("   ");

        result.Should().BeNull();
    }

    [Fact]
    public void BuildPrefixTsquery_TrailingOr_IgnoresTrailingOr()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("Амундсен OR");

        result.Should().Be("'Амундсен':*");
    }

    [Fact]
    public void BuildPrefixTsquery_LeadingOr_IgnoresLeadingOr()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("OR Амундсен");

        result.Should().Be("'Амундсен':*");
    }

    [Fact]
    public void BuildPrefixTsquery_MultipleOrOperators_ChainsOrCorrectly()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("кіт OR собака OR риба");

        result.Should().Be("'кіт':* | 'собака':* | 'риба':*");
    }

    [Fact]
    public void BuildPrefixTsquery_MixedAndOr_MixesOperatorsCorrectly()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("Амундсен Скотт OR Нансен");

        // "Амундсен" AND "Скотт" OR "Нансен"
        result.Should().Be("'Амундсен':* & 'Скотт':* | 'Нансен':*");
    }

    [Fact]
    public void BuildPrefixTsquery_SingleQuoteInWord_EscapesQuote()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("O'Brien");

        result.Should().Be("'O''Brien':*");
    }

    [Fact]
    public void BuildPrefixTsquery_UnclosedQuote_TreatsRestAsPhrase()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("\"Південний полюс");

        result.Should().Be("('Південний' <-> 'полюс':*)");
    }

    [Fact]
    public void BuildPrefixTsquery_ExtraWhitespace_HandlesGracefully()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("  Амундсен   Скотт  ");

        result.Should().Be("'Амундсен':* & 'Скотт':*");
    }

    [Fact]
    public void BuildPrefixTsquery_NegationOnly_ProducesNegation()
    {
        var result = SearchQueryParser.BuildPrefixTsquery("-Скотт");

        result.Should().Be("!'Скотт':*");
    }

    [Fact]
    public void BuildPrefixTsquery_ShortNegation_SkipsIt()
    {
        // "-а" has text "а" which is 1 char, should be skipped
        var result = SearchQueryParser.BuildPrefixTsquery("-а книга");

        result.Should().Be("'книга':*");
    }

    [Fact]
    public void Tokenize_SimpleWords_ReturnsWordTokens()
    {
        var tokens = SearchQueryParser.Tokenize("hello world");

        tokens.Should().HaveCount(2);
        tokens[0].Type.Should().Be(SearchQueryParser.TokenType.Word);
        tokens[0].Text.Should().Be("hello");
        tokens[1].Type.Should().Be(SearchQueryParser.TokenType.Word);
        tokens[1].Text.Should().Be("world");
    }

    [Fact]
    public void Tokenize_QuotedPhrase_ReturnsPhraseToken()
    {
        var tokens = SearchQueryParser.Tokenize("\"exact phrase\"");

        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(SearchQueryParser.TokenType.Phrase);
        tokens[0].Text.Should().Be("exact phrase");
    }

    [Fact]
    public void Tokenize_OrOperator_ReturnsOrToken()
    {
        var tokens = SearchQueryParser.Tokenize("cat OR dog");

        tokens.Should().HaveCount(3);
        tokens[1].Type.Should().Be(SearchQueryParser.TokenType.Or);
    }

    [Fact]
    public void Tokenize_Negation_ReturnsNegationToken()
    {
        var tokens = SearchQueryParser.Tokenize("-excluded");

        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(SearchQueryParser.TokenType.Negation);
        tokens[0].Text.Should().Be("excluded");
    }

    [Fact]
    public void Tokenize_BareDash_SkippedAsShort()
    {
        // Just "-" alone — it's a negation with empty text, which is skipped
        var tokens = SearchQueryParser.Tokenize("- word");

        // "-" is treated as negation with empty text "" (length < 2, skipped by BuildPrefixTsquery)
        // But Tokenize itself uses Length > 1 for the word check before becoming negation
        // "-" has length 1 after removing '-', which means word.Length > 1 fails
        // So it would be treated as a regular word of length 1 (skipped by min length)
        tokens.Should().HaveCount(1);
        tokens[0].Text.Should().Be("word");
    }
}
