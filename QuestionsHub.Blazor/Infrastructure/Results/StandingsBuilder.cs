using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.Results;

/// <summary>One row of the aggregated standings table.</summary>
public sealed record StandingsRow(
    int RankFrom,
    int RankTo,
    string Name,
    string? Town,
    decimal Points,
    IReadOnlyList<int?> TourCells,
    int? ShootoutCell)
{
    /// <summary>Place for display: «3» or «3–4» when tied.</summary>
    public string Place => RankFrom == RankTo ? RankFrom.ToString() : $"{RankFrom}–{RankTo}";
}

/// <summary>
/// Builds the aggregated standings: all teams from all loaded sources re-ranked by points
/// (ties share a place range), with per-tour correct-answer counts derived from the stored
/// per-question dictionaries.
/// </summary>
public static class StandingsBuilder
{
    /// <summary>Sentinel tour index for shoot-out questions in the id→tour lookup.</summary>
    private const int ShootoutIndex = -1;

    public static List<StandingsRow> Build(
        IReadOnlyList<Tour> regularTours,
        IReadOnlyList<Tour> shootoutTours,
        IReadOnlyList<TeamResult> teams)
    {
        var questionTourIndex = new Dictionary<int, int>();
        for (var i = 0; i < regularTours.Count; i++)
        {
            foreach (var question in regularTours[i].Questions)
            {
                questionTourIndex[question.Id] = i;
            }
        }

        foreach (var question in shootoutTours.SelectMany(t => t.Questions))
        {
            questionTourIndex[question.Id] = ShootoutIndex;
        }

        var unranked = new List<(TeamResult Team, IReadOnlyList<int?> TourCells, int? ShootoutCell)>();
        foreach (var team in teams)
        {
            var byQuestion = ResultsJson.ParseResultsByQuestion(team.ResultsByQuestionJson);
            var tourCells = new int?[regularTours.Count];
            int? shootoutCell = null;

            if (byQuestion != null)
            {
                // Single pass; a tour the team has no data for stays null («—»), not 0
                foreach (var (questionId, outcome) in byQuestion)
                {
                    if (!questionTourIndex.TryGetValue(questionId, out var tourIndex))
                    {
                        continue;
                    }

                    var correct = outcome > 0 ? 1 : 0;
                    if (tourIndex == ShootoutIndex)
                    {
                        shootoutCell = (shootoutCell ?? 0) + correct;
                    }
                    else
                    {
                        tourCells[tourIndex] = (tourCells[tourIndex] ?? 0) + correct;
                    }
                }
            }

            unranked.Add((team, tourCells, shootoutCell));
        }

        var ordered = unranked
            .OrderByDescending(t => t.Team.Points)
            .ThenBy(t => t.Team.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new List<StandingsRow>(ordered.Count);
        var position = 1;
        foreach (var tieGroup in ordered.GroupBy(t => t.Team.Points))
        {
            var count = tieGroup.Count();
            foreach (var (team, tourCells, shootoutCell) in tieGroup)
            {
                rows.Add(new StandingsRow(
                    position, position + count - 1,
                    team.Name, team.Town,
                    team.Points, tourCells, shootoutCell));
            }

            position += count;
        }

        return rows;
    }
}
