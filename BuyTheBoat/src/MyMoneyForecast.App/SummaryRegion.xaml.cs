using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MyMoneyForecast.App;

// The Earmark form's Summary region (planning/22 §6c) — a narrative sentence,
// a small line chart, and an aside, replacing what used to be separate Goal
// detail / Current jar state / proposed-plan boxes (see settled-designs.html
// for what this is meant to look like laid out). Compose the actual wording
// with PlanHealthMessages before calling Load; this control only lays it out
// and draws the chart, it doesn't decide what anything says — matches the
// "region communicates X" job planning/22 §6 records for the Summary region:
// what this savings plan is, the plan behind it, and how the trajectory
// looks against the goal.
//
// TODO (2026-08-05, iterative-build pass): this is a working first cut, not
// the finished chart. Known gaps, left as TODOs rather than blocking on them:
//  - The "actual" line is a straight start-to-today segment, not the real
//    day-by-day trajectory (the stepped/sawtooth shape the mockups show).
//    Drawing the real one needs the full BalanceRecord threaded in, which
//    this control deliberately doesn't require yet so it can exist before
//    that plumbing does.
//  - No milestone line yet (planning/22 §6c's other confirmed addition) and
//    no one-off-mode addition line (Summary B's own extra line) — both
//    settled in content, neither wired into this control's Load signature.
//  - Text label positioning (TextAt) approximates right-alignment from
//    string length instead of measuring the rendered text — fine at the
//    font size/amounts used so far, but a real fix once this is visible in
//    the actual app.
//  - Not wired into any host window yet — CreateEarMarkPatternWindow has no
//    ForecastResult/PlanHealthState to feed it; that plumbing is its own
//    task, not done here.
public partial class SummaryRegion : UserControl
{
    private ChartData? _chart;

    public SummaryRegion()
    {
        InitializeComponent();
        SizeChanged += (_, _) => DrawChart();
    }

    private sealed record ChartData(
        DateOnly Start, DateOnly AsOfDate, DateOnly DueDate,
        decimal StartAmount, decimal TodayAmount, decimal GoalAmount);

    /// <summary>[UI] Fills in the Summary region's narrative, chart, and aside. Pass already-composed wording (PlanHealthMessages) — this method only lays content out, it doesn't decide what anything says.</summary>
    public void Load(
        string narrative,
        DateOnly start, DateOnly asOfDate, DateOnly dueDate,
        decimal startAmount, decimal todayAmount, decimal goalAmount,
        string asideLine, string? asideSecondaryLine = null)
    {
        NarrativeText.Text = narrative;
        AsideText.Text = asideLine;
        if (string.IsNullOrEmpty(asideSecondaryLine))
        {
            AsideSecondaryText.Visibility = Visibility.Collapsed;
        }
        else
        {
            AsideSecondaryText.Text = asideSecondaryLine;
            AsideSecondaryText.Visibility = Visibility.Visible;
        }

        _chart = new ChartData(start, asOfDate, dueDate, startAmount, todayAmount, goalAmount);
        DrawChart();
    }

    // Simple, honest chart: a flat goal line, a straight line from the
    // starting point to today (see the class-level TODO for why it isn't the
    // real stepped trajectory yet), and a dashed "today" marker. Coordinates
    // scale to whatever size the Canvas actually gets laid out at, so this
    // re-runs on every SizeChanged, not just on Load.
    private void DrawChart()
    {
        ChartCanvas.Children.Clear();
        if (_chart is not { } chart || ChartCanvas.ActualWidth < 10 || ChartCanvas.ActualHeight < 10)
        {
            return;
        }

        var width = ChartCanvas.ActualWidth;
        var height = ChartCanvas.ActualHeight;
        const double leftMargin = 6, rightMargin = 6, topMargin = 16, bottomMargin = 20;
        var plotWidth = width - leftMargin - rightMargin;
        var plotHeight = height - topMargin - bottomMargin;

        var totalDays = Math.Max(1, (chart.DueDate.ToDateTime(TimeOnly.MinValue) - chart.Start.ToDateTime(TimeOnly.MinValue)).TotalDays);
        double X(DateOnly date) => leftMargin + plotWidth *
            (date.ToDateTime(TimeOnly.MinValue) - chart.Start.ToDateTime(TimeOnly.MinValue)).TotalDays / totalDays;

        var maxAmount = Math.Max((double)chart.GoalAmount, (double)Math.Max(chart.StartAmount, chart.TodayAmount)) * 1.05;
        double Y(decimal amount) => topMargin + plotHeight - plotHeight * ((double)amount / Math.Max(1, maxAmount));

        ChartCanvas.Children.Add(new Line
        {
            X1 = leftMargin, Y1 = topMargin + plotHeight, X2 = width - rightMargin, Y2 = topMargin + plotHeight,
            Stroke = Brushes.LightGray, StrokeThickness = 1,
        });

        var goalY = Y(chart.GoalAmount);
        ChartCanvas.Children.Add(new Line
        {
            X1 = leftMargin, Y1 = goalY, X2 = width - rightMargin, Y2 = goalY,
            Stroke = Brushes.Gray, StrokeThickness = 1, StrokeDashArray = [4, 3],
        });
        ChartCanvas.Children.Add(TextAt($"{chart.GoalAmount:C0} goal", width - rightMargin, goalY - 12, right: true));

        ChartCanvas.Children.Add(new Polyline
        {
            Points = [new Point(X(chart.Start), Y(chart.StartAmount)), new Point(X(chart.AsOfDate), Y(chart.TodayAmount))],
            Stroke = Brushes.SteelBlue,
            StrokeThickness = 2,
        });

        var todayX = X(chart.AsOfDate);
        ChartCanvas.Children.Add(new Line
        {
            X1 = todayX, Y1 = topMargin, X2 = todayX, Y2 = topMargin + plotHeight,
            Stroke = Brushes.DarkGray, StrokeThickness = 1, StrokeDashArray = [2, 2],
        });
        ChartCanvas.Children.Add(TextAt("Today", todayX, topMargin + plotHeight + 4, right: false));
        ChartCanvas.Children.Add(TextAt($"Due {chart.DueDate:MMM d}", width - rightMargin, topMargin + plotHeight + 4, right: true));
    }

    // TODO (2026-08-05): approximates right-alignment from string length
    // instead of measuring the rendered TextBlock — see the class-level TODO.
    private static TextBlock TextAt(string text, double x, double y, bool right)
    {
        var block = new TextBlock { Text = text, FontSize = 10, Foreground = Brushes.Gray };
        Canvas.SetTop(block, y);
        Canvas.SetLeft(block, right ? Math.Max(0, x - text.Length * 5.5) : x - text.Length * 2.5);
        return block;
    }
}
