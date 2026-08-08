using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MyMoneyForecast.App;

// The Earmark form's Summary region — a narrative sentence, a small line
// chart, and an aside, replacing what used to be separate Goal detail /
// Current jar state / proposed-plan boxes. Compose the actual wording with
// PlanHealthMessages before calling Load; this control only lays it out and
// draws the chart, it doesn't decide what anything says.
//
// Known, deliberate gaps:
//  - TextAt (below) approximates right-alignment from string length instead
//    of measuring the rendered text — fine at the font size/amounts used so
//    far.
//  - Savings-plan mode shows the SAVED plan's real health, not a live
//    hypothetical of whatever amount/schedule is currently typed but
//    unsaved — that would need re-running the whole forecast with the
//    in-progress values substituted in.
//  - ActualTrajectory stays split at Today and always will under this
//    architecture: TransactionLogBookFactory only ever cascades day-by-day
//    balances forward from AsOfDate — no historical BalanceRecord exists
//    before today — so before Today it's a straight-line placeholder (one
//    real point at each end, nothing real in between). MilestoneTrajectory
//    has no such limit and spans the whole plan, Start through the due
//    date, since it's pure pattern math with no dependency on real
//    transaction history.
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
        IReadOnlyList<(DateOnly Date, decimal MilestoneAmount)> MilestoneTrajectory,
        IReadOnlyList<DateOnly> PeakDates,
        DateOnly? HighlightDate,
        IReadOnlyList<(DateOnly Date, decimal Amount)> ProposedTrajectory,
        decimal? AdditionAmount);

    /// <summary>[UI] Shows a plain one-line placeholder instead of the chart/aside — for whenever there's genuinely nothing to summarize yet (no goal picked, no linked plan). The "Summary" GroupBox itself stays visible either way; only its content changes, so the region never just reads as accidentally blank.</summary>
    /// <param name="message">The placeholder text to show.</param>
    public void Clear(string message)
    {
        NarrativeText.Text = message;
        AsideText.Text = string.Empty;
        AsideSecondaryText.Visibility = Visibility.Collapsed;
        _chart = null;
        DrawChart();
    }

    /// <summary>[UI] Fills in the Summary region's narrative, chart, and aside. Pass already-composed wording (PlanHealthMessages) — this method only lays content out, it doesn't decide what anything says.</summary>
    /// <param name="narrative">The composed narrative sentence for the top of the region.</param>
    /// <param name="start">When the plan's savings window begins.</param>
    /// <param name="asOfDate">Today's date, for the chart's Today marker.</param>
    /// <param name="dueDate">The goal's due date.</param>
    /// <param name="startAmount">What was already saved when the plan started.</param>
    /// <param name="goalAmount">The full amount needed.</param>
    /// <param name="actualTrajectory">Real (Date, ExpectedAmount) samples from today through the due date or forecast horizon, whichever comes first — ordered. Empty when there's no forecast yet. Today-onward only; see this class's own header comment for why the segment before Today stays an approximation.</param>
    /// <param name="milestoneTrajectory">Real (Date, MilestoneAmount) samples across the WHOLE plan, Start through the due date — no Today split, since this one doesn't need real transaction history. Empty when there's no plan. Drawn for every goal with a plan, one-time or repeating.</param>
    /// <param name="asideLine">The aside's first line.</param>
    /// <param name="asideSecondaryLine">The aside's optional second line, hidden when null or empty.</param>
    /// <param name="peakDates">Which of the goal's own occurrence dates to label on the chart with their own gridline, most-recent-first from Start — empty for a one-time goal, which already has its single real due date labeled separately. Caller decides how many; this control just draws whatever list it's given.</param>
    /// <param name="highlightDate">One of peakDates (or Start-of-window's own first upcoming occurrence) to mark in the same color as the first-payment warning text, when that warning is showing — so the reader can tell which gridline it's about instead of guessing. Null when no such warning is showing.</param>
    /// <param name="proposedTrajectory">Savings-plan mode's own "proposed — rough, live estimate" line: whatever's currently typed in Amount/Recurrence, computed via TransactionLogBookFactory.ComputeMilestoneTrajectory, no forecast needed. Pass empty in One-off mode (nothing there is proposing a new rate) — DrawChart falls back to drawing the real, saved ActualTrajectory instead; the two are never drawn together.</param>
    /// <param name="additionAmount">One-off mode's own "+ $X today" line: the signed amount the currently-typed one-off would add, drawn as ActualTrajectory shifted by this much from Today onward. Null in Savings-plan mode, or whenever nothing valid is typed yet.</param>
    public void Load(
        string narrative,
        DateOnly start, DateOnly asOfDate, DateOnly dueDate,
        decimal startAmount, decimal goalAmount,
        IReadOnlyList<(DateOnly Date, decimal ActualAmount)> actualTrajectory,
        IReadOnlyList<(DateOnly Date, decimal MilestoneAmount)> milestoneTrajectory,
        string asideLine, string? asideSecondaryLine = null,
        IReadOnlyList<DateOnly>? peakDates = null, DateOnly? highlightDate = null,
        IReadOnlyList<(DateOnly Date, decimal Amount)>? proposedTrajectory = null, decimal? additionAmount = null)
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

        _chart = new ChartData(
            start, asOfDate, dueDate, startAmount, goalAmount, actualTrajectory, milestoneTrajectory, peakDates ?? [], highlightDate,
            proposedTrajectory ?? [], additionAmount);
        DrawChart();
    }

    /// <summary>[UI] Simple, honest chart: a flat goal line, a straight line from the starting point to today (see the class-level TODO for why it isn't the real stepped trajectory yet), and a dashed "today" marker. Coordinates scale to whatever size the Canvas actually gets laid out at, so this re-runs on every SizeChanged, not just on Load.</summary>
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

        // Due-date-for-X-axis-purposes is whatever the caller passed
        // (typically the goal's own Until) — for an indefinitely-repeating
        // bill that can sit well beyond the forecast's own HorizonEndDate, in
        // which case the trajectory below simply stops partway across the
        // plot and the rest reads as genuinely-unknown-yet, not squeezed to
        // fit. Honest gap, not a bug.
        var totalDays = Math.Max(1, (chart.DueDate.ToDateTime(TimeOnly.MinValue) - chart.Start.ToDateTime(TimeOnly.MinValue)).TotalDays);
        double X(DateOnly date) => leftMargin + plotWidth *
            (date.ToDateTime(TimeOnly.MinValue) - chart.Start.ToDateTime(TimeOnly.MinValue)).TotalDays / totalDays;

        var actualMax = chart.ActualTrajectory.Count == 0 ? 0m : chart.ActualTrajectory.Max(p => p.ActualAmount);
        var milestoneMax = chart.MilestoneTrajectory.Count == 0 ? 0m : chart.MilestoneTrajectory.Max(p => p.MilestoneAmount);
        var proposedMax = chart.ProposedTrajectory.Count == 0 ? 0m : chart.ProposedTrajectory.Max(p => p.Amount);
        var additionMax = chart.AdditionAmount is { } addForMax ? actualMax + addForMax : 0m;
        var maxAmount = new[] { chart.GoalAmount, chart.StartAmount, actualMax, milestoneMax, proposedMax, additionMax }
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
        ChartCanvas.Children.Add(TextAt($"{chart.GoalAmount:C0} goal", width - rightMargin, goalY - 12, right: true, width));
        legendEntries.Add(("Goal", Brushes.Gray, true));

        // Savings-plan mode's own "proposed — rough, live estimate" line and
        // One-off mode's own "Actual" line are mutually exclusive, never
        // drawn together — the further down toward an isolated earmark
        // event this form is looking, the more "what's actually saved right
        // now" matters over "what would this proposed rate produce," which
        // is exactly what decides which one a given call to Load even has
        // data for (EarmarkFormPanel.UpdateSummary only ever builds a
        // ProposedTrajectory in Savings-plan mode). Reuses the same
        // SteelBlue solid treatment for both, since they occupy the same
        // visual role (this savings plan's own progress) and are never on
        // screen at the same time to be confused with each other.
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
            // Split at Today (see this class's own header comment for why).
            // Before Today: the older straight-line placeholder, one real
            // point at each end. Today onward: real, walked, per-occurrence
            // data off the already-saved forecast, turned into a proper
            // step/staircase (Stepped below) rather than smoothed into a
            // ramp, so a plan whose actual balance resets at every release
            // actually reads as resetting.
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

        // "Milestone" — no Today split (see this class's own header comment
        // for why: pure pattern math, real for the whole window). Reuses
        // Summary B's own established "second line" violet treatment.
        // Prepends (Start, 0): MilestoneAmount is the running sum of the
        // plan's own repeated contributions alone, with no notion of a
        // starting-allocation bonus riding on top of it, so the line always
        // begins at 0 regardless of what the real samples' own first date
        // happens to be. Drawn for every goal with a plan, one-time or
        // repeating (see Load's own doc comment).
        if (chart.MilestoneTrajectory.Count > 0)
        {
            var milestonePoints = new List<Point> { new(X(chart.Start), Y(0m)) };
            milestonePoints.AddRange(chart.MilestoneTrajectory.Select(p => new Point(X(p.Date), Y(p.MilestoneAmount))));
            ChartCanvas.Children.Add(new Polyline
            {
                Points = new PointCollection(Stepped(milestonePoints)),
                Stroke = Brushes.MediumPurple,
                StrokeThickness = 2,
                StrokeDashArray = [3, 2],
            });
            legendEntries.Add(("Milestone (committed plan)", Brushes.MediumPurple, true));
        }

        // One-off mode's own extra line — the ActualTrajectory shape,
        // shifted by the currently-typed one-off's own amount, from Today
        // onward. Deliberately a plain parallel shift, not a recomputation
        // through the real floor/deallocation rules: it doesn't re-solve
        // the regular contributions, since changing the plan itself is a
        // different action from adding a one-off. AdditionAmount already
        // carries GetOneOffLiveDelta's own scope limit (0 for a backdated
        // date before this goal's most recent release), so this simply
        // won't offset in that one case rather than draw something wrong.
        if (chart.AdditionAmount is { } addition && chart.ActualTrajectory.Count > 0)
        {
            ChartCanvas.Children.Add(new Polyline
            {
                Points = new PointCollection(Stepped(chart.ActualTrajectory.Select(p => new Point(X(p.Date), Y(p.ActualAmount + addition))).ToList())),
                Stroke = AdditionBrush,
                StrokeThickness = 2,
                StrokeDashArray = [6, 4],
            });
            legendEntries.Add(($"With today's addition ({(addition >= 0 ? "+" : string.Empty)}{addition:C0})", AdditionBrush, true));
        }

        // A labeled gridline at each period boundary the caller supplies.
        // Empty for a one-time goal, which keeps the plain
        // "Due {chart.DueDate}" label below instead — it already names a
        // real, single due date, nothing to pick out of a row of repeats.
        // WarningBrush matches StartingShortfallWarningText's own #FF9A4F08
        // (EarmarkFormPanel.xaml) — the same warning color already used for
        // the first-payment warning text this gridline is meant to be found
        // from.
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

        // Built from whichever lines actually got drawn above
        // (legendEntries), not a fixed set — Savings-plan mode and One-off
        // mode never show the same combination (see the Proposed/Actual
        // mutual-exclusion comment above), so a fixed legend would either
        // omit or fabricate an entry depending on mode.
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

    /// <summary>[CALC] Turns a chronological list of (x, y) samples into a proper step — hold flat at the previous value until the next date, THEN jump — not a smoothed ramp between real data points. A 2-point line has nothing to step, so it passes through unchanged (the Start-to-Today placeholder segment uses that path deliberately, staying a plain diagonal rather than a fabricated step).</summary>
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

    /// <summary>[UI] TODO: approximates right-alignment from string length instead of measuring the rendered TextBlock — see the class-level TODO. Both branches clamp to [0, canvasWidth - the text's own estimated width], so neither edge can run off the canvas regardless of where x falls — "Today" in particular sits at the chart's own left edge for any freshly-created plan (Start == today), where an unclamped left coordinate would go negative and WPF simply wouldn't draw it.</summary>
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
