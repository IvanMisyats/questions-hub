using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace QuestionsHub.Blazor.Infrastructure.Results;

/// <summary>
/// Loads tournament results and per-question masks from the rating.chgk.info API.
/// All reads are anonymous; results may be embargoed until the tournament's hideResultsTo date.
/// </summary>
public class RatingResultsClient(
    IHttpClientFactory httpClientFactory,
    IOptions<ResultsOptions> options)
{
    /// <summary>
    /// Loads results for the given tournament id.
    /// Throws <see cref="ResultsLoadException"/> with a user-facing message on any failure.
    /// </summary>
    public async Task<PlatformResults> Load(string tournamentId, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient(PackageResultsServiceExtensions.HttpClientName);
        var apiBase = options.Value.RatingApiBaseUrl.TrimEnd('/');

        var infoBody = await GetString(client,
            $"{apiBase}/tournaments/{tournamentId}",
            notFoundMessage: $"Турнір {tournamentId} не знайдено на rating.chgk.info.",
            cancellationToken);
        var resultsBody = await GetString(client,
            $"{apiBase}/tournaments/{tournamentId}/results?includeMasksAndControversials=1",
            notFoundMessage: $"Результати турніру {tournamentId} не знайдено на rating.chgk.info.",
            cancellationToken);

        try
        {
            return ParsePayload(tournamentId, infoBody, resultsBody);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            // An unexpected payload shape must surface as a stored load error, not crash the caller
            throw new ResultsLoadException("rating.chgk.info повернув несподівані дані — спробуйте пізніше.", ex);
        }
    }

    private PlatformResults ParsePayload(string tournamentId, string infoBody, string resultsBody)
    {
        using var infoDoc = ParseJson(infoBody);
        using var resultsDoc = ParseJson(resultsBody);

        var hideResultsTo = ParseHideResultsTo(infoDoc.RootElement);
        var tourSizes = ParseTourSizes(infoDoc.RootElement);
        var title = ParseString(infoDoc.RootElement, "longName") ?? ParseString(infoDoc.RootElement, "name");

        if (resultsDoc.RootElement.ValueKind != JsonValueKind.Array || resultsDoc.RootElement.GetArrayLength() == 0)
        {
            throw new ResultsLoadException($"На rating.chgk.info немає результатів для турніру {tournamentId}.");
        }

        var rawTeams = resultsDoc.RootElement.EnumerateArray().ToList();

        // While the embargo lasts, the team list is visible but masks/positions/points are all null.
        var hasUsableData = rawTeams.Any(t =>
            !IsNull(t, "position") || !IsNull(t, "questionsTotal") || !IsNull(t, "mask"));
        if (!hasUsableData)
        {
            if (hideResultsTo is { } embargo && embargo.UtcDateTime > DateTime.UtcNow)
            {
                // Format the platform's own (offset-local) date — the UTC date can be a day earlier
                throw new ResultsLoadException(
                    $"Результати приховано платформою до {embargo.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} — спробуйте оновити після цієї дати.")
                {
                    EmbargoedUntil = embargo.UtcDateTime
                };
            }

            throw new ResultsLoadException($"Результати турніру {tournamentId} недоступні (порожні дані).");
        }

        var totalQuestions = tourSizes.Sum();
        if (totalQuestions == 0)
        {
            totalQuestions = rawTeams
                .Select(t => t.TryGetProperty("mask", out var m) && m.ValueKind == JsonValueKind.String
                    ? m.GetString()!.Length
                    : 0)
                .DefaultIfEmpty(0)
                .Max();
        }

        var warnings = new List<string>();
        var hasPendingControversials = false;
        var teams = new List<PlatformTeam>(rawTeams.Count);

        foreach (var raw in rawTeams)
        {
            var (name, town, externalTeamId) = ParseTeamIdentity(raw);
            var mask = raw.TryGetProperty("mask", out var maskProp) && maskProp.ValueKind == JsonValueKind.String
                ? maskProp.GetString()
                : null;

            IReadOnlyList<PlatformAnswerState>? answers = null;
            if (mask != null)
            {
                if (mask.Length == totalQuestions)
                {
                    var states = new PlatformAnswerState[mask.Length];
                    for (var i = 0; i < mask.Length; i++)
                    {
                        states[i] = mask[i] switch
                        {
                            '1' => PlatformAnswerState.Correct,
                            '0' => PlatformAnswerState.Wrong,
                            _ => PlatformAnswerState.Excluded
                        };
                        hasPendingControversials |= mask[i] == '?';
                    }

                    answers = states;
                }
                else
                {
                    warnings.Add($"Маска відповідей команди «{name}» має довжину {mask.Length} замість {totalQuestions} — команду пропущено в статистиці.");
                }
            }

            var questionsTotal = raw.TryGetProperty("questionsTotal", out var qt) && qt.ValueKind == JsonValueKind.Number
                ? qt.GetInt32()
                : (int?)null;

            teams.Add(new PlatformTeam
            {
                Name = name,
                Town = town,
                ExternalTeamId = externalTeamId,
                Points = questionsTotal
                         ?? answers?.Count(a => a == PlatformAnswerState.Correct)
                         ?? 0,
                Position = raw.TryGetProperty("position", out var pos) && pos.ValueKind == JsonValueKind.Number
                    ? pos.GetDecimal()
                    : null,
                Answers = answers
            });
        }

        if (hasPendingControversials)
        {
            warnings.Add("У результатах є нерозглянуті спірні відповіді — статистика може змінитися після їх розгляду.");
        }

        var questions = Enumerable.Range(1, totalQuestions)
            .Select(n => new PlatformQuestion(n.ToString(CultureInfo.InvariantCulture), PlatformQuestionKind.Regular))
            .ToList();

        return new PlatformResults
        {
            Questions = questions,
            Teams = teams,
            TourSizes = tourSizes,
            DisplayUrl = $"{options.Value.RatingSiteBaseUrl.TrimEnd('/')}/tournament/{tournamentId}",
            Title = title,
            RawPayload = $"{{\"tournament\":{infoBody},\"results\":{resultsBody}}}",
            Warnings = warnings
        };
    }

    private static (string Name, string? Town, int? ExternalTeamId) ParseTeamIdentity(JsonElement raw)
    {
        string? name = null;
        string? town = null;
        int? id = null;

        if (raw.TryGetProperty("team", out var team) && team.ValueKind == JsonValueKind.Object)
        {
            if (team.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
            {
                name = n.GetString();
            }

            if (team.TryGetProperty("town", out var t) && t.ValueKind == JsonValueKind.Object
                && t.TryGetProperty("name", out var tn) && tn.ValueKind == JsonValueKind.String)
            {
                town = tn.GetString();
            }

            if (team.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number)
            {
                id = i.GetInt32();
            }
        }

        if (string.IsNullOrWhiteSpace(name)
            && raw.TryGetProperty("current", out var current) && current.ValueKind == JsonValueKind.Object
            && current.TryGetProperty("name", out var cn) && cn.ValueKind == JsonValueKind.String)
        {
            name = cn.GetString();
        }

        return (string.IsNullOrWhiteSpace(name) ? $"Команда {id}" : name!, town, id);
    }

    private static DateTimeOffset? ParseHideResultsTo(JsonElement info)
    {
        if (info.TryGetProperty("hideResultsTo", out var prop)
            && prop.ValueKind == JsonValueKind.String
            && prop.TryGetDateTimeOffset(out var value))
        {
            return value;
        }

        return null;
    }

    /// <summary>An HTTP 200 with a non-JSON body (maintenance page, WAF challenge) must surface as a load error.</summary>
    private static JsonDocument ParseJson(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new ResultsLoadException("rating.chgk.info повернув некоректні дані — спробуйте пізніше.", ex);
        }
    }

    private static List<int> ParseTourSizes(JsonElement info)
    {
        var sizes = new List<int>();
        if (info.TryGetProperty("questionQty", out var qty) && qty.ValueKind == JsonValueKind.Object)
        {
            sizes.AddRange(qty.EnumerateObject()
                .Select(p => (Tour: int.TryParse(p.Name, CultureInfo.InvariantCulture, out var t) ? t : int.MaxValue,
                    Count: p.Value.ValueKind == JsonValueKind.Number ? p.Value.GetInt32() : 0))
                .OrderBy(p => p.Tour)
                .Select(p => p.Count));
        }

        return sizes;
    }

    private static string? ParseString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static bool IsNull(JsonElement element, string property)
    {
        return !element.TryGetProperty(property, out var prop) || prop.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
    }

    private static async Task<string> GetString(HttpClient client, string url, string notFoundMessage, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await client.SendAsync(request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new ResultsLoadException(notFoundMessage);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ResultsLoadException($"rating.chgk.info повернув помилку (HTTP {(int)response.StatusCode}).");
            }

            // The body read can also fail mid-stream — it must stay inside the network catch
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            throw new ResultsLoadException("Не вдалося з'єднатися з rating.chgk.info — спробуйте пізніше.", ex);
        }
    }
}
