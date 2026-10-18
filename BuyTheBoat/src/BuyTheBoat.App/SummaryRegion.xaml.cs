using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BuyTheBoat.App;

/// <summary>
/// The Summary region on the Earmark and Expense forms: a narrative sentence, a small line chart, and an aside covering the goal, the jar today, and the proposed plan.
/// Callers write the wording with PlanHealthMessages before calling Load; this control only lays it out and draws the chart.
/// </summary>
/// <remarks>
/// Known gaps:
/// - TextAt estimates right-alignment from string length rather than measuring the text, which is fine at the sizes used.
/// - The actual-balance line before Today is a straight-line placeholder, since the forecast has no balances before today.
/// </remarks>
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
        decimal StartAmount, decimal GoalAmount,
        IReadOnlyList<(DateOnly Date, decimal ActualAmount)> ActualTrajectory,
        IReadOnlyList<DateOnly> PeakDates,
        DateOnly? HighlightDate,
        IReadOnlyList<(DateOnly Date, decimal Amount)> ProposedTrajectory,
        decimal? AdditionAmount);

    /// <summary>[UI] Shows a one-line placeholder instead of the chart and aside when there's nothing to summarize yet; the "Summary" box stays visible, so it never looks accidentally blank.</summary>
    /// <param name="message">The placeholder text to show.</param>
    public void Clear(string message)
    {
        NarrativeText.Text = message;
        AsideStack.Visibility = Visibility.Collapsed;
        _chart = null;
        DrawChart();
    }

    /// <summary>[UI] Fills in the Summary region's narrative, chart, and aside. Pass already-composed wording (PlanHealthMessages) — this method only lays content out, it doesn't decide what anything says.</summary>
    /// <param name="narrative">The composed narrative sentence for the top of the region.</param>
    /// <param name="start">When the plan's savings window begins.</param>
    /// <param name="asOfDate">Today's date, for the chart's Today marker.</param>
    /// <param name="dueDate">Where the chart's x-axis ends — normally the goal's due date, but the caller (EarmarkFormPanel) caps it at a ~5-year window for a far-off repeating goal so the early activity stays legible; the narrative still names the real due date.</param>
    /// <param name="startAmount">What was already saved when the plan started.</param>
    /// <param name="goalAmount">The full amount needed.</param>
    /// <param name="actualTrajectory">Real (Date, ExpectedAmount) samples from today through the due date or forecast horizon, whichever comes first — ordered. Empty when there's no forecast yet. Today-onward only; see this class's own header comment for why the segment before Today stays an approximation.</param>
    /// <param name="jarStateLine">The "Fund jar, today" region's line — where the jar stands right now.</param>
    /// <param name="trajectoryLine">The "Toward the goal" region's line — the long-run picture. Null/empty hides that whole labeled region (label + separator + text).</param>
    /// <param name="firstPaymentLine">The first-payment warning, shown UNDER the aside (no label of its own), separated by space. Null/empty hides it — e.g. the Expense form routes this to its own status indicator instead, so it passes null here.</param>
    /// <param name="peakDates">Which of the goal's own occurrence dates to label on the chart with their own gridline, most-recent-first from Start — empty for a one-time goal, which already has its single real due date labeled separately. Caller decides how many; this control just draws whatever list it's given.</param>
    /// <param name="highlightDate">One of peakDates (or Start-of-window's own first upcoming occurrence) to mark in the same color as the first-payment warning text, when that warning is showing — so the reader can tell which gridline it's about instead of guessing. Null when no such warning is showing.</param>
    /// <param name="proposedTrajectory">Savings-plan mode's own "proposed — rough, live estimate" line: the projected jar ExpectedAmount for whatever's currently typed in Amount/Recurrence/Starting-point, computed via TransactionLogBookFactory.ComputeExpectedTrajectory, no forecast needed. Pass empty in One-off mode (nothing there is proposing a new rate) — DrawChart falls back to drawing the real, saved ActualTrajectory instead; the two are never drawn together.</param>
    /// <param name="additionAmount">One-off mode's own "+ X today" line: the signed amount the currently-typed one-off would add, drawn as ActualTrajectory shifted by this much from Today onward. Null in Savings-plan mode, or whenever nothing valid is typed yet.</param>
    public void Load(
        string narrative,
        DateOnly start, DateOnly asOfDate, DateOnly dueDate,
        decimal startAmount, decimal goalAmount,
        IReadOnlyList<(DateOnly Date, decimal ActualAmount)> actualTrajectory,
        string jarStateLine, string? trajectoryLine = null, string? firstPaymentLine = null,
        IReadOnlyList<DateOnly>? peakDates = null, DateOnly? highlightDate = null,
        IReadOnlyList<(DateOnly Date, decimal Amount)>? proposedTrajectory = null, decimal? additionAmount = null)
    {
        AsideStack.Visibility = Visibility.Visible;
        NarrativeText.Text = narrative;
        AsideText.Text = jarStateLine;

        var hasTrajectory = !string.IsNullOrEmpty(trajectoryLine);
        TrajectorySeparator.Visibility = hasTrajectory ? Visibility.Visible : Visibility.Collapsed;
        TrajectoryLabel.Visibility = hasTrajectory ? Visibility.Visible : Visibility.Collapsed;
        TrajectoryText.Visibility = hasTrajectory ? Visibility.Visible : Visibility.Collapsed;
        TrajectoryText.Text = trajectoryLine ?? string.Empty;

        var hasFirstPayment = !string.IsNullOrEmpty(firstPaymentLine);
        FirstPaymentText.Visibility = hasFirstPayment ? Visibility.Visible : Visibility.Collapsed;
        FirstPaymentText.Text = firstPaymentLine ?? string.Empty;

        _chart = new ChartData(
            start, asOfDate, dueDate, startAmount, goalAmount, actualTrajectory, peakDates ?? [], highlightDate,
            proposedTrajectory ?? [], additionAmount);
        DrawChart();
    }

    /// <summary>
    /// [UI] Draws the chart: a flat goal line, the jar's actual line (straight before today, stepped from today on), the proposed-plan or one-off line if any, the date markers, and the legend.
    /// Coordinates scale to the canvas size, so this runs again on every SizeChanged.
    /// </summary>
    private void DrawChart()
    {
        ChartCanvas.Children.Clear();
        LegendPanel.Children.Clear();
        if (_chart is not { } chart || ChartCanvas.ActualWidth < 10 || ChartCanvas.ActualHeight < 10)
        {
            return;
        }

        var width = ChartCanvas.ActualWidth;
        var height = ChartCanvas.ActualHeight;
        const double leftMargin = 6, rightMargin = 6, topMargin = 16, bottomMargin = 20;
        var plotWidth = width - leftMargin - rightMargin;
        var plotHeight = height - topMargin - bottomMargin;

        // The x-axis ends at the caller's DueDate, which may be capped (EarmarkFormPanel caps a far-off repeating goal at 5 years); past the forecast horizon, the actual line stops partway.
        var totalDays = Math.Max(1, (chart.DueDate.ToDateTime(TimeOnly.MinValue) - chart.Start.ToDateTime(TimeOnly.MinValue)).TotalDays);
        double X(DateOnly date) => leftMargin + plotWidth *
            (date.ToDateTime(TimeOnly.MinValue) - chart.Start.ToDateTime(TimeOnly.MinValue)).TotalDays / totalDays;

        var actualMax = chart.ActualTrajectory.Count == 0 ? 0m : chart.ActualTrajectory.Max(p => p.ActualAmount);
        var proposedMax = chart.ProposedTrajectory.Count == 0 ? 0m : chart.ProposedTrajectory.Max(p => p.Amount);
        var additionMax = chart.AdditionAmount is { } addForMax ? actualMax + addForMax : 0m;
        var maxAmount = new[] { chart.GoalAmount, chart.StartAmount, actualMax, proposedMax, additionMax }
            .Max(amount => (double)amount) * 1.05;
        double Y(decimal amount) => topMargin + plotHeight - plotHeight * ((double)amount / Math.Max(1, maxAmount));
        var legendEntries = new List<(string Label, Brush Color, bool Dashed)>();

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
        ChartCanvas.Children.Add(TextAt($"{chart.GoalAmount:N0} goal", width - rightMargin, goalY - 12, right: true, width));
        legendEntries.Add(("Goal", Brushes.Gray, true));

        // The "proposed" line, only passed in Savings-plan mode, so it never appears with One-off mode's line; both use solid SteelBlue.
        // It's the projected jar balance rather than the milestone, so a starting balance or glut carries past each release.
        if (chart.ProposedTrajectory.Count > 0)
        {
            ChartCanvas.Children.Add(new Polyline
            {
                Points = new PointCollection(Stepped(chart.ProposedTrajectory.Select(p => new Point(X(p.Date), Y(p.Amount))).ToList())),
                Stroke = Brushes.SteelBlue,
                StrokeThickness = 2,
            });
            legendEntries.Add(("Savings plan (proposed — rough, live estimate)", Brushes.SteelBlue, false));
        }
        else if (chart.ActualTrajectory.Count > 0)
        {
            // Before Today, a straight-line placeholder; from Today on, the forecast's real balances, stepped rather than smoothed so a balance that resets at each release looks like it.
            var todayPoint = chart.ActualTrajectory[0];
            ChartCanvas.Children.Add(new Polyline
            {
                Points = [new Point(X(chart.Start), Y(chart.StartAmount)), new Point(X(chart.AsOfDate), Y(todayPoint.ActualAmount))],
                Stroke = Brushes.SteelBlue,
                StrokeThickness = 2,
            });
            ChartCanvas.Children.Add(new Polyline
            {
                Points = new PointCollection(Stepped(chart.ActualTrajectory.Select(p => new Point(X(p.Date), Y(p.ActualAmount))).ToList())),
                Stroke = Brushes.SteelBlue,
                StrokeThickness = 2,
            });
            legendEntries.Add(("Fund jar (actual)", Brushes.SteelBlue, false));
        }

        // One-off mode's extra line: the actual line shifted by the one-off's effect on today's jar (AdditionAmount, from a what-if forecast), from Today on.
        // A plain shift, not a full recomputation; null when the one-off has no effect, so nothing's drawn.
        if (chart.AdditionAmount is { } addition && chart.ActualTrajectory.Count > 0)
        {
            ChartCanvas.Children.Add(new Polyline
            {
                Points = new PointCollection(Stepped(chart.ActualTrajectory.Select(p => new Point(X(p.Date), Y(p.ActualAmount + addition))).ToList())),
                Stroke = AdditionBrush,
                StrokeThickness = 2,
                StrokeDashArray = [6, 4],
            });
            legendEntries.Add(($"With today's addition ({(addition >= 0 ? "+" : string.Empty)}{addition:N0})", AdditionBrush, true));
        }

        // Draws a labeled gridline at each date the caller supplies; a one-time goal has none and gets a plain "Due" label below.
        // WarningBrush matches StartingShortfallWarningText's color, so the first-payment warning leads the eye here.
        foreach (var peakDate in chart.PeakDates)
        {
            var peakX = X(peakDate);
            var isHighlighted = peakDate == chart.HighlightDate;
            var lineBrush = isHighlighted ? WarningBrush : Brushes.LightGray;
            ChartCanvas.Children.Add(new Line
            {
                X1 = peakX, Y1 = topMargin, X2 = peakX, Y2 = topMargin + plotHeight,
                Stroke = lineBrush, StrokeThickness = 1, StrokeDashArray = [2, 3],
            });
            ChartCanvas.Children.Add(TextAt($"{peakDate:MMM d}", peakX, 2, right: false, width, isHighlighted ? WarningBrush : Brushes.Gray));
        }

        var todayX = X(chart.AsOfDate);
        ChartCanvas.Children.Add(new Line
        {
            X1 = todayX, Y1 = topMargin, X2 = todayX, Y2 = topMargin + plotHeight,
            Stroke = Brushes.DarkGray, StrokeThickness = 1, StrokeDashArray = [2, 2],
        });
        ChartCanvas.Children.Add(TextAt("Today", todayX, topMargin + plotHeight + 4, right: false, width));
        if (chart.PeakDates.Count == 0)
        {
            ChartCanvas.Children.Add(TextAt($"Due {chart.DueDate:MMM d}", width - rightMargin, topMargin + plotHeight + 4, right: true, width));
        }

        // Adds a legend entry for each line actually drawn, since the two modes draw different lines.
        foreach (var (label, color, dashed) in legendEntries)
        {
            var swatch = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
            swatch.Children.Add(new Line
            {
                X1 = 0, Y1 = 0, X2 = 16, Y2 = 0,
                Stroke = color, StrokeThickness = 2,
                StrokeDashArray = dashed ? [3, 2] : null,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0),
            });
            swatch.Children.Add(new TextBlock { Text = label, FontSize = 10.5, Foreground = Brushes.Gray });
            LegendPanel.Children.Add(swatch);
        }
    }

    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x4F, 0x08));
    private static readonly Brush AdditionBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0x6D, 0x00));

    /// <summary>[CALC] Turns (x, y) samples into steps, staying flat until the next date and then jumping; a 2-point line passes through unchanged, which keeps the before-Today placeholder a plain diagonal.</summary>
    /// <param name="points">The chronological samples to step.</param>
    private static IReadOnlyList<Point> Stepped(IReadOnlyList<Point> points)
    {
        if (points.Count < 2)
        {
            return points;
        }

        var stepped = new List<Point>(points.Count * 2 - 1) { points[0] };
        for (var i = 1; i < points.Count; i++)
        {
            stepped.Add(new Point(points[i].X, points[i - 1].Y));
            stepped.Add(points[i]);
        }

        return stepped;
    }

    /// <summary>
    /// [UI] Places a label on the chart, estimating right-alignment from string length (a known gap; see the class remarks).
    /// Keeps the label inside the canvas, so "Today" still shows at the left edge for a plan that starts today.
    /// </summary>
    /// <param name="text">The label text to place.</param>
    /// <param name="x">The anchor x-coordinate.</param>
    /// <param name="y">The top y-coordinate.</param>
    /// <param name="right">Whether the text's right edge, rather than its center, anchors to x.</param>
    /// <param name="canvasWidth">The chart's width, for clamping the text on-canvas.</param>
    /// <param name="foreground">The text color; defaults to gray.</param>
    private static TextBlock TextAt(string text, double x, double y, bool right, double canvasWidth, Brush? foreground = null)
    {
        var block = new TextBlock { Text = text, FontSize = 10, Foreground = foreground ?? Brushes.Gray };
        Canvas.SetTop(block, y);
        var approxWidth = text.Length * 5.5;
        var left = right ? x - approxWidth : x - approxWidth / 2;
        Canvas.SetLeft(block, Math.Max(0, Math.Min(left, canvasWidth - approxWidth)));
        return block;
    }
}
