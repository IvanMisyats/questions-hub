using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace QuestionsHub.Blazor.Infrastructure.Results;

/// <summary>
/// Loads quiz results from open-quiz.com. Results live in a static results.json addressed by
/// a results token; an audience (aud) link's listen token is first exchanged for the results
/// token via the platform's Fable.Remoting API (login → getQuiz).
/// </summary>
public class OpenQuizResultsClient(
    IHttpClientFactory httpClientFactory,
    IOptions<ResultsOptions> options)
{
    /// <summary>
    /// Loads results for the given quiz id and link token.
    /// Throws <see cref="ResultsLoadException"/> with a user-facing message on any failure.
    /// </summary>
    public async Task<PlatformResults> Load(string quizId, string token, OpenQuizLinkKind linkKind, string? fallbackTitle = null, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient(PackageResultsServiceExtensions.HttpClientName);
        var baseUrl = options.Value.OpenQuizBaseUrl.TrimEnd('/');

        var resultsToken = token;
        // res-links carry no authoritative name; fall back to the quizName from the URL.
        // aud-links get the real name from the getQuiz API.
        var quizName = fallbackTitle;
        if (linkKind == OpenQuizLinkKind.Aud)
        {
            string? apiName;
            (resultsToken, apiName) = await ExchangeAudToken(client, baseUrl, quizId, token, cancellationToken);
            quizName = apiName ?? fallbackTitle;
        }

        var resultsUrl = $"{baseUrl}/static/{quizId}-{resultsToken}/results.json?nocache={DateTime.UtcNow.Ticks}";
        var body = await GetResultsJson(client, resultsUrl, cancellationToken);

        try
        {
            return ParseResultsPayload(body, quizId, resultsToken, quizName, baseUrl);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or JsonException)
        {
            // An unexpected payload shape must land in the source's LoadError, not crash the caller
            throw new ResultsLoadException("Open-quiz.com повернув некоректні дані результатів.", ex);
        }
    }

    private static PlatformResults ParseResultsPayload(string body, string quizId, string resultsToken, string? quizName, string baseUrl)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var questions = new List<PlatformQuestion>();
        var questionKeys = new List<string>();
        var tourSizes = new List<int>();
        var currentTourSize = 0;

        if (root.TryGetProperty("Questions", out var questionsProp) && questionsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var q in questionsProp.EnumerateArray())
            {
                var name = q.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString() ?? ""
                    : "";
                questions.Add(new PlatformQuestion(name, ClassifyQuestion(name)));

                var key = q.GetProperty("Key");
                questionKeys.Add(BuildDetailsKey(key.GetProperty("TourIdx").GetInt32(), key.GetProperty("QwIdx").GetInt32()));

                currentTourSize++;
                if (q.TryGetProperty("EOT", out var eot) && eot.ValueKind == JsonValueKind.True)
                {
                    tourSizes.Add(currentTourSize);
                    currentTourSize = 0;
                }
            }
        }

        if (currentTourSize > 0)
        {
            tourSizes.Add(currentTourSize);
        }

        if (!root.TryGetProperty("Teams", out var teamsProp) || teamsProp.ValueKind != JsonValueKind.Array
            || teamsProp.GetArrayLength() == 0)
        {
            throw new ResultsLoadException("На open-quiz.com немає результатів для цієї гри (жодної команди).");
        }

        var teams = new List<PlatformTeam>(teamsProp.GetArrayLength());
        foreach (var t in teamsProp.EnumerateArray())
        {
            teams.Add(ParseTeam(t, questionKeys));
        }

        var displayUrl = $"{baseUrl}/results.html?who=res&quiz={quizId}&token={resultsToken}";
        if (!string.IsNullOrEmpty(quizName))
        {
            displayUrl += $"&quizName={Uri.EscapeDataString(quizName)}";
        }

        return new PlatformResults
        {
            Questions = questions,
            Teams = teams,
            TourSizes = tourSizes,
            DisplayUrl = displayUrl,
            Title = string.IsNullOrWhiteSpace(quizName) ? null : quizName,
            RawPayload = body
        };
    }

    private static PlatformQuestionKind ClassifyQuestion(string name)
    {
        if (name == "0")
        {
            return PlatformQuestionKind.Warmup;
        }

        // Shoot-out questions are conventionally numbered 100+ ("101".."105").
        // Non-numeric names ("8.1" multi-question slips) are regular.
        return int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 100
            ? PlatformQuestionKind.Shootout
            : PlatformQuestionKind.Regular;
    }

    /// <summary>Details map keys are stringified compact JSON of the question key, e.g. {"TourIdx":0,"QwIdx":0}.</summary>
    private static string BuildDetailsKey(int tourIdx, int qwIdx)
    {
        return $"{{\"TourIdx\":{tourIdx.ToString(CultureInfo.InvariantCulture)},\"QwIdx\":{qwIdx.ToString(CultureInfo.InvariantCulture)}}}";
    }

    private static PlatformTeam ParseTeam(JsonElement t, List<string> questionKeys)
    {
        var name = t.TryGetProperty("TeamName", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString() ?? ""
            : "";
        var externalTeamId = t.TryGetProperty("TeamId", out var id) && id.ValueKind == JsonValueKind.Number
            ? id.GetInt32()
            : (int?)null;
        var points = t.TryGetProperty("Points", out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetDecimal()
            : 0m;

        decimal? position = null;
        if (t.TryGetProperty("PlaceFrom", out var from) && from.ValueKind == JsonValueKind.Number
            && t.TryGetProperty("PlaceTo", out var to) && to.ValueKind == JsonValueKind.Number)
        {
            position = (from.GetDecimal() + to.GetDecimal()) / 2m;
        }

        IReadOnlyList<PlatformAnswerState>? answers = null;
        if (t.TryGetProperty("Details", out var details) && details.ValueKind == JsonValueKind.Object
            && details.EnumerateObject().Any())
        {
            var states = new PlatformAnswerState[questionKeys.Count];
            for (var i = 0; i < questionKeys.Count; i++)
            {
                if (details.TryGetProperty(questionKeys[i], out var answer))
                {
                    var correct = answer.TryGetProperty("Result", out var result)
                                  && result.ValueKind == JsonValueKind.Number
                                  && result.GetDecimal() > 0;
                    states[i] = correct ? PlatformAnswerState.Correct : PlatformAnswerState.Wrong;
                }
                else
                {
                    states[i] = PlatformAnswerState.NoAnswer;
                }
            }

            answers = states;
        }

        return new PlatformTeam
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Команда {externalTeamId}" : name,
            ExternalTeamId = externalTeamId,
            Points = points,
            Position = position,
            Answers = answers
        };
    }

    /// <summary>Logs in with the aud (listen) token and reads the quiz's results token.</summary>
    private static async Task<(string ResultsToken, string? QuizName)> ExchangeAudToken(
        HttpClient client, string baseUrl, string quizId, string listenToken, CancellationToken cancellationToken)
    {
        var loginBody = JsonSerializer.Serialize(new object[]
        {
            new { Token = "", Arg = new { AudUser = new { QuizId = int.Parse(quizId, CultureInfo.InvariantCulture), Token = listenToken } } }
        });
        using var loginDoc = await PostRpc(client, $"{baseUrl}/api/ISecurityApi/login", loginBody,
            "Не вдалося авторизуватися на open-quiz.com — перевірте посилання.", cancellationToken);
        var loginOk = GetOk(loginDoc, "Не вдалося авторизуватися на open-quiz.com — перевірте посилання.");
        var jwt = loginOk.TryGetProperty("Token", out var jwtProp) && jwtProp.ValueKind == JsonValueKind.String
            ? jwtProp.GetString()
            : null;
        if (string.IsNullOrEmpty(jwt))
        {
            throw new ResultsLoadException("Не вдалося авторизуватися на open-quiz.com — перевірте посилання.");
        }

        var getQuizBody = JsonSerializer.Serialize(new object[] { new { Token = jwt, Arg = (object?)null } });
        using var quizDoc = await PostRpc(client, $"{baseUrl}/api/IAudApi/getQuiz", getQuizBody,
            "Не вдалося отримати дані гри з open-quiz.com.", cancellationToken);
        var ok = GetOk(quizDoc, "Не вдалося отримати дані гри з open-quiz.com.");

        var resultsToken = ok.TryGetProperty("RT", out var rt) && rt.ValueKind == JsonValueKind.String
            ? rt.GetString()
            : null;
        if (string.IsNullOrEmpty(resultsToken))
        {
            throw new ResultsLoadException("Open-quiz.com не повернув токен результатів для цієї гри.");
        }

        var quizName = ok.TryGetProperty("QN", out var qn) && qn.ValueKind == JsonValueKind.String
            ? qn.GetString()
            : null;
        return (resultsToken, quizName);
    }

    private static JsonElement GetOk(JsonDocument rpcResponse, string errorMessage)
    {
        var root = rpcResponse.RootElement;
        if (root.TryGetProperty("Status", out var status) && status.ValueKind == JsonValueKind.String
            && status.GetString() == "Executed"
            && root.TryGetProperty("Value", out var value) && value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("Ok", out var ok))
        {
            return ok;
        }

        throw new ResultsLoadException(errorMessage);
    }

    private static async Task<JsonDocument> PostRpc(
        HttpClient client, string url, string body, string errorMessage, CancellationToken cancellationToken)
    {
        string content;
        try
        {
            using var response = await client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ResultsLoadException($"{errorMessage} (HTTP {(int)response.StatusCode})");
            }

            // The body read can also fail mid-stream — it must stay inside the network catch
            content = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            throw new ResultsLoadException("Не вдалося з'єднатися з open-quiz.com — спробуйте пізніше.", ex);
        }

        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new ResultsLoadException(errorMessage, ex);
        }
    }

    private static async Task<string> GetResultsJson(HttpClient client, string url, CancellationToken cancellationToken)
    {
        string body;
        try
        {
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ResultsLoadException(
                    $"Не вдалося завантажити результати з open-quiz.com (HTTP {(int)response.StatusCode}) — перевірте посилання.");
            }

            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            throw new ResultsLoadException("Не вдалося з'єднатися з open-quiz.com — спробуйте пізніше.", ex);
        }

        try
        {
            using var _ = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new ResultsLoadException("Open-quiz.com повернув некоректні дані результатів.", ex);
        }

        return body;
    }
}
