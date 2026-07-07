using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.Results;

/// <summary>Kind of an OpenQuiz link determines how the results token is obtained.</summary>
public enum OpenQuizLinkKind
{
    None = 0,

    /// <summary>Public results link — the token in the URL is the results token.</summary>
    Results = 1,

    /// <summary>Audience link — the token is a listen token, exchanged for the results token via the aud API.</summary>
    Aud = 2
}

/// <summary>Outcome of parsing a user-entered results URL.</summary>
public sealed record ParsedResultsUrl(
    ResultsPlatform Platform,
    string Url,
    string? ExternalId,
    string? Token,
    OpenQuizLinkKind LinkKind,
    string? Title = null);

/// <summary>
/// Parses user-entered links to tournament results into platform identifiers.
/// The platform is chosen explicitly by the user; parsing validates that the link
/// matches the chosen platform and extracts the id/token needed for loading.
/// </summary>
public static partial class ResultsUrlParser
{
    [GeneratedRegex(@"^/tournaments?/(\d+)(/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex RatingTournamentPath();

    // OpenQuiz capability tokens are base64url; anything else would end up interpolated
    // into request paths and public links, so reject it outright.
    [GeneratedRegex(@"^[A-Za-z0-9_-]+$")]
    private static partial Regex OpenQuizToken();

    /// <summary>
    /// Detects the platform from the link itself and parses it: rating.chgk.info URLs (or a
    /// bare tournament id) → Rating, open-quiz.com URLs → OpenQuiz, any other http(s) URL →
    /// Other. A malformed link of a recognized platform (e.g. an open-quiz.com URL without a
    /// token) is an error, not «Other».
    /// Throws <see cref="ResultsLoadException"/> with a user-facing message.
    /// </summary>
    public static ParsedResultsUrl Detect(string input)
    {
        var trimmed = input?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            throw new ResultsLoadException("Посилання не може бути порожнім.");
        }

        if (trimmed.All(char.IsAsciiDigit))
        {
            return ParseRating(trimmed);
        }

        if (TryCreateHttpUri(trimmed, out var uri))
        {
            if (IsHost(uri, "rating.chgk.info"))
            {
                return ParseRating(trimmed);
            }

            if (IsHost(uri, "open-quiz.com"))
            {
                return ParseOpenQuiz(trimmed);
            }
        }

        return ParseOther(trimmed);
    }

    /// <summary>
    /// Parses <paramref name="input"/> for the given platform.
    /// Throws <see cref="ResultsLoadException"/> with a user-facing message when the link is not recognized.
    /// </summary>
    public static ParsedResultsUrl Parse(ResultsPlatform platform, string input)
    {
        var trimmed = input?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            throw new ResultsLoadException("Посилання не може бути порожнім.");
        }

        return platform switch
        {
            ResultsPlatform.Rating => ParseRating(trimmed),
            ResultsPlatform.OpenQuiz => ParseOpenQuiz(trimmed),
            _ => ParseOther(trimmed)
        };
    }

    private static ParsedResultsUrl ParseRating(string input)
    {
        // Bare tournament id is accepted ("13097")
        if (input.All(char.IsAsciiDigit))
        {
            return new ParsedResultsUrl(ResultsPlatform.Rating,
                $"https://rating.chgk.info/tournament/{input}", input, null, OpenQuizLinkKind.None);
        }

        if (TryCreateHttpUri(input, out var uri) && IsHost(uri, "rating.chgk.info"))
        {
            var match = RatingTournamentPath().Match(uri.AbsolutePath);
            if (match.Success)
            {
                return new ParsedResultsUrl(ResultsPlatform.Rating, input, match.Groups[1].Value, null, OpenQuizLinkKind.None);
            }
        }

        throw new ResultsLoadException(
            "Не вдалося розпізнати посилання на rating.chgk.info — очікується https://rating.chgk.info/tournament/{id} або номер турніру.");
    }

    private static ParsedResultsUrl ParseOpenQuiz(string input)
    {
        if (TryCreateHttpUri(input, out var uri) && IsHost(uri, "open-quiz.com"))
        {
            var query = QueryHelpers.ParseQuery(uri.Query);
            var quizId = query.TryGetValue("quiz", out var quizValues) ? quizValues.ToString() : "";
            var token = query.TryGetValue("token", out var tokenValues) ? tokenValues.ToString() : "";
            var who = query.TryGetValue("who", out var whoValues) ? whoValues.ToString() : "";
            // The public results link often carries the quiz name; used as a title fallback for
            // res-links (aud-links get the authoritative name from the getQuiz API at load time).
            var quizName = query.TryGetValue("quizName", out var nameValues) ? nameValues.ToString() : "";

            if (quizId.Length is > 0 and <= 9 && quizId.All(char.IsAsciiDigit) && OpenQuizToken().IsMatch(token))
            {
                var kind = who.Equals("aud", StringComparison.OrdinalIgnoreCase)
                    ? OpenQuizLinkKind.Aud
                    : OpenQuizLinkKind.Results;
                return new ParsedResultsUrl(ResultsPlatform.OpenQuiz, input, quizId, token, kind,
                    Title: string.IsNullOrWhiteSpace(quizName) ? null : quizName);
            }
        }

        throw new ResultsLoadException(
            "Не вдалося розпізнати посилання на open-quiz.com — очікується посилання на результати з параметрами quiz і token.");
    }

    private static ParsedResultsUrl ParseOther(string input)
    {
        if (!TryCreateHttpUri(input, out _))
        {
            throw new ResultsLoadException("Некоректне посилання — очікується повна адреса (https://…).");
        }

        return new ParsedResultsUrl(ResultsPlatform.Other, input, null, null, OpenQuizLinkKind.None);
    }

    private static bool TryCreateHttpUri(string input, out Uri uri)
    {
        return Uri.TryCreate(input, UriKind.Absolute, out uri!)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static bool IsHost(Uri uri, string host)
    {
        return uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase);
    }
}
