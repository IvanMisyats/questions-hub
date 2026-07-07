using FluentAssertions;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Results;
using Xunit;

namespace QuestionsHub.UnitTests.Results;

public class StandingsBuilderTests
{
    #region Helpers

    private static Tour BuildTour(TourType type, string number, params int[] questionIds)
    {
        var tour = new Tour { Number = number, Type = type };
        foreach (var (id, index) in questionIds.Select((id, i) => (id, i)))
        {
            tour.Questions.Add(new Question
            {
                Id = id,
                OrderIndex = index,
                Number = (index + 1).ToString(),
                Text = "т",
                Answer = "в"
            });
        }

        return tour;
    }

    private static TeamResult Team(string name, decimal points, string? resultsJson = null, string? town = null)
    {
        return new TeamResult { Name = name, Points = points, Town = town, ResultsByQuestionJson = resultsJson };
    }

    #endregion

    [Fact]
    public void Build_RanksByPointsWithSharedPlaces()
    {
        var rows = StandingsBuilder.Build([], [],
        [
            Team("Четверта", 5),
            Team("Перша", 10),
            Team("Друга", 8),
            Team("Третя", 8)
        ]);

        rows.Select(r => r.Name).Should().Equal("Перша", "Друга", "Третя", "Четверта");
        rows.Select(r => r.Place).Should().Equal("1", "2–3", "2–3", "4");
    }

    [Fact]
    public void Build_ComputesPerTourCells()
    {
        var tours = new[] { BuildTour(TourType.Regular, "1", 1, 2), BuildTour(TourType.Regular, "2", 3, 4) };
        var rows = StandingsBuilder.Build(tours, [],
        [
            Team("А", 3, """{"1":1,"2":0,"3":1,"4":1}""")
        ]);

        rows[0].TourCells.Should().Equal(1, 2);
        rows[0].ShootoutCell.Should().BeNull();
    }

    [Fact]
    public void Build_StandingsOnlyTeam_HasEmptyCells()
    {
        var tours = new[] { BuildTour(TourType.Regular, "1", 1, 2) };
        var rows = StandingsBuilder.Build(tours, [],
        [
            Team("Без статистики", 7, town: "Київ")
        ]);

        rows[0].TourCells.Should().Equal(new int?[] { null });
        rows[0].Points.Should().Be(7);
        rows[0].Town.Should().Be("Київ");
    }

    [Fact]
    public void Build_ShootoutCell_OnlyForParticipants()
    {
        var regular = new[] { BuildTour(TourType.Regular, "1", 1, 2) };
        var shootout = new[] { BuildTour(TourType.Shootout, "П", 5, 6) };
        var rows = StandingsBuilder.Build(regular, shootout,
        [
            Team("Грала", 3, """{"1":1,"2":1,"5":1,"6":0}"""),
            Team("Не грала", 2, """{"1":1,"2":1}""")
        ]);

        rows.Single(r => r.Name == "Грала").ShootoutCell.Should().Be(1);
        rows.Single(r => r.Name == "Не грала").ShootoutCell.Should().BeNull();
    }

    [Fact]
    public void Build_TeamWithOnlyShootoutData_TourCellsStayNull()
    {
        // Regular mapping degraded but shoot-out mapped: tour cells must show «—», not 0
        var regular = new[] { BuildTour(TourType.Regular, "1", 1, 2) };
        var shootout = new[] { BuildTour(TourType.Shootout, "П", 5) };
        var rows = StandingsBuilder.Build(regular, shootout,
        [
            Team("А", 9, """{"5":1}""")
        ]);

        rows[0].TourCells.Should().Equal(new int?[] { null });
        rows[0].ShootoutCell.Should().Be(1);
    }

    [Fact]
    public void Build_CorruptResultsJson_TreatedAsStandingsOnly()
    {
        var tours = new[] { BuildTour(TourType.Regular, "1", 1) };
        var rows = StandingsBuilder.Build(tours, [],
        [
            Team("А", 3, "not valid json")
        ]);

        rows[0].TourCells.Should().Equal(new int?[] { null });
    }
}
