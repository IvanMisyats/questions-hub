using FluentAssertions;
using QuestionsHub.Blazor.Infrastructure.Results;
using Xunit;

namespace QuestionsHub.UnitTests.Results;

public class DifficultyChartModelTests
{
    private static ChartGroup Group(string label, params (int Id, int Correct, int Total)[] questions)
    {
        return new ChartGroup(label, questions
            .Select((q, i) => new ChartQuestion(q.Id, (i + 1).ToString(), q.Correct, q.Total))
            .ToList());
    }

    [Fact]
    public void Build_ComputesBarPercentAndHeight()
    {
        var model = DifficultyChartModel.Build([Group("Тур 1", (1, 5, 10))]);

        var bar = model.Bars.Should().ContainSingle().Which;
        bar.Percent.Should().Be(50);
        bar.Height.Should().Be(90); // 50% of the 180px plot
        (bar.Y + bar.Height).Should().Be(model.PlotBottom);
        bar.Tooltip.Should().Be("Запитання 1: 5/10 (50%)");
    }

    [Fact]
    public void Build_ZeroCorrect_GetsVisibleSliver()
    {
        var model = DifficultyChartModel.Build([Group("Тур 1", (1, 0, 17))]);

        var bar = model.Bars[0];
        bar.Percent.Should().Be(0);
        bar.Height.Should().Be(2);
    }

    [Fact]
    public void Build_NoData_ZeroHeightWithTooltip()
    {
        var model = DifficultyChartModel.Build([Group("Тур 1", (1, 0, 0))]);

        model.Bars[0].Height.Should().Be(0);
        model.Bars[0].Tooltip.Should().Contain("немає даних");
    }

    [Fact]
    public void Build_BandsSpanTheirGroupsAndAlternate()
    {
        var model = DifficultyChartModel.Build(
        [
            Group("Тур 1", (1, 1, 2), (2, 1, 2), (3, 1, 2)),
            Group("Тур 2", (4, 1, 2), (5, 1, 2))
        ]);

        model.Bands.Should().HaveCount(2);
        model.Bands[0].Width.Should().Be(66); // 3 slots × 22
        model.Bands[1].Width.Should().Be(44); // 2 slots × 22
        model.Bands[1].X.Should().Be(model.Bands[0].X + model.Bands[0].Width);
        model.Bands[0].Alternate.Should().BeFalse();
        model.Bands[1].Alternate.Should().BeTrue();
    }

    [Fact]
    public void Build_AxisLabelStep_ScalesWithQuestionCount()
    {
        var small = DifficultyChartModel.Build([Group("Тур 1",
            Enumerable.Range(1, 36).Select(i => (i, 1, 2)).ToArray())]);
        small.Bars.Should().OnlyContain(b => b.ShowAxisLabel); // step 1

        var large = DifficultyChartModel.Build([Group("Тур 1",
            Enumerable.Range(1, 60).Select(i => (i, 1, 2)).ToArray())]);
        large.Bars.Count(b => b.ShowAxisLabel).Should().Be(30); // step 2
        large.Bars[0].ShowAxisLabel.Should().BeTrue();
        large.Bars[1].ShowAxisLabel.Should().BeFalse();
    }

    [Fact]
    public void Build_GridLines_CoverPercentScale()
    {
        var model = DifficultyChartModel.Build([Group("Тур 1", (1, 1, 2))]);

        model.GridLines.Select(g => g.Percent).Should().Equal(0, 25, 50, 75, 100);
        model.GridLines.Single(g => g.Percent == 0).Y.Should().Be(model.PlotBottom);
    }
}
