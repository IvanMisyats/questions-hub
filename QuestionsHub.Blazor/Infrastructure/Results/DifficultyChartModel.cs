using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.Results;

/// <summary>Input: one question of the difficulty chart.</summary>
public sealed record ChartQuestion(int QuestionId, string Number, int Correct, int Total);

/// <summary>Input: one tour (band) of the difficulty chart.</summary>
public sealed record ChartGroup(string Label, IReadOnlyList<ChartQuestion> Questions);

/// <summary>Computed bar geometry (SVG user units).</summary>
public sealed record ChartBar(
    int QuestionId, string Number, int Correct, int Total, int Percent,
    double X, double Y, double Width, double Height, bool ShowAxisLabel)
{
    public string Tooltip => Total > 0
        ? $"Запитання {Number}: {Correct}/{Total} ({Percent}%)"
        : $"Запитання {Number}: немає даних";
}

/// <summary>Computed tour band geometry.</summary>
public sealed record ChartBand(string Label, double X, double Width, bool Alternate);

/// <summary>Horizontal gridline of the percent scale.</summary>
public sealed record ChartGridLine(int Percent, double Y);

/// <summary>
/// Geometry of the question-difficulty bar chart: one bar per question (height = % of teams
/// that answered correctly), alternating background bands per tour. Pure math — rendered
/// as inline SVG by DifficultyChart.razor.
/// </summary>
public sealed class DifficultyChartModel
{
    private const double SlotWidth = 22;
    private const double BarWidth = 14;
    private const double PlotHeight = 180;
    private const double TopPad = 14;
    private const double BottomPad = 40; // axis labels + tour labels
    private const double LeftPad = 36;
    private const double RightPad = 8;

    public required double Width { get; init; }
    public required double Height { get; init; }
    public required IReadOnlyList<ChartBar> Bars { get; init; }
    public required IReadOnlyList<ChartBand> Bands { get; init; }
    public required IReadOnlyList<ChartGridLine> GridLines { get; init; }

    public double PlotBottom => TopPad + PlotHeight;
    public double AxisLabelY => TopPad + PlotHeight + 16;
    public double BandLabelY => TopPad + PlotHeight + 32;
    public double PlotLeft => LeftPad;

    public static DifficultyChartModel Build(IReadOnlyList<ChartGroup> groups)
    {
        var totalQuestions = groups.Sum(g => g.Questions.Count);
        var labelStep = Math.Max(1, (int)Math.Ceiling(totalQuestions / 36.0));

        var bars = new List<ChartBar>(totalQuestions);
        var bands = new List<ChartBand>(groups.Count);
        var x = LeftPad;
        var barIndex = 0;

        foreach (var (group, groupIndex) in groups.Select((g, i) => (g, i)))
        {
            var bandStart = x;
            foreach (var question in group.Questions)
            {
                var percent = QuestionStat.CalcPercent(question.Correct, question.Total);
                var height = PlotHeight * percent / 100.0;
                if (question.Total > 0 && height < 2)
                {
                    height = 2; // a question answered by no one still shows a visible (red) sliver
                }
                bars.Add(new ChartBar(
                    question.QuestionId, question.Number, question.Correct, question.Total, percent,
                    X: x + (SlotWidth - BarWidth) / 2,
                    Y: TopPad + PlotHeight - height,
                    Width: BarWidth,
                    Height: height,
                    ShowAxisLabel: barIndex % labelStep == 0));
                x += SlotWidth;
                barIndex++;
            }

            bands.Add(new ChartBand(group.Label, bandStart, x - bandStart, Alternate: groupIndex % 2 == 1));
        }

        var gridLines = new[] { 0, 25, 50, 75, 100 }
            .Select(p => new ChartGridLine(p, TopPad + PlotHeight * (100 - p) / 100.0))
            .ToList();

        return new DifficultyChartModel
        {
            Width = x + RightPad,
            Height = TopPad + PlotHeight + BottomPad,
            Bars = bars,
            Bands = bands,
            GridLines = gridLines
        };
    }
}
