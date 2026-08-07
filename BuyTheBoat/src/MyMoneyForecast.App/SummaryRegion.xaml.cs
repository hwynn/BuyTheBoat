using System.Linq;
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
//  - No one-off-mode addition line yet (Summary B's own extra line, showing
//    how a proposed one-off would move the picture) — settled in content,
//    not wired into this control's Load signature.
//  - No "proposed — live estimate" third line (settled-designs.html) — the
//    line computed from whatever's currently typed but unsaved in the form,
//    as opposed to the already-saved plan this control does draw. Same
//    live-hypothetical gap UpdateSummary's own comment already flags;
//    deliberately not built this pass either.
//  - Text label positioning (TextAt) approximates right-alignment from
//    string length instead of measuring the rendered text — fine at the
//    font size/amounts used so far, but a real fix once this is visible in
//    the actual app.
//
// WIRED 2026-08-07 (EarmarkFormPanel.UpdateSummary): reads real
// PlanHealthState/FundJar data via PlanHealthMessages, in both Savings-plan
// and One-off mode. One known, deliberate gap carried over from there, not
// this control's own: Savings-plan mode shows the SAVED plan's real health,
// not a live hypothetical of whatever amount/schedule is currently typed
// but unsaved — that would need re-running the whole forecast with the
// in-progress values substituted in, a bigger task than this pass. Item
// 23-D's "everything derived reads live" holds for the Starting-point
// region and this region's own narrative sentence (both read straight off
// the form's current fields); it does NOT yet hold for the forecast-derived
// health figures specifically — flagged, not silently glossed over.
//
// REVISED 2026-08-07 (author's own report: no visible milestone line for a
// plan that's clearly contributing regularly): the "actual" and "milestone"
// lines are now real, walked, stepped trajectories — not one point
// extrapolated backward as a straight segment — matching the genuine
// reset-and-climb shape settled-designs.html's own SVGs show for a
// repeating pattern, and the steady step-up shape they show for a one-time
// goal.
//
// REVISED AGAIN 2026-08-07 (same conversation, author's own follow-up: "it's
// fine if expected amount in the past is simplified... but we really should
// be able to see a proper sawtooth pattern for the milestone amount in days
// before today"): the two lines are NOT symmetric, and shouldn't be treated
// as one "trajectory" concept. ActualTrajectory stays split at Today and
// always will be under this architecture — TransactionLogBookFactory only
// ever cascades day-by-day balances forward from AsOfDate (planning/22 §7's
// own "backward-history gap" — no historical BalanceRecord exists before
// today), so before Today it's still the older straight-line placeholder
// (one real point at each end, nothing real in between). MilestoneTrajectory
// has NO such limit and now spans the whole plan, Start through the due
// date: it's pure pattern math with no dependency on real transaction
// history (TransactionLogBookFactory.ComputeMilestoneTrajectory's own header
// comment has the full reasoning) — there was never an architectural reason
// to withhold its pre-Today portion, only that nothing had computed it yet.
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
        IReadOnlyList<(DateOnly Date, decimal MilestoneAmount)> MilestoneTrajectory);

    /// <summary>[UI] Shows a plain one-line placeholder instead of the chart/aside — for whenever there's genuinely nothing to summarize yet (no goal picked, no linked plan). The "Summary" GroupBox itself stays visible either way; only its content changes, so the region never just reads as accidentally blank.</summary>
    public void Clear(string message)
    {
        NarrativeText.Text = message;
        AsideText.Text = string.Empty;
        AsideSecondaryText.Visibility = Visibility.Collapsed;
        _chart = null;
        DrawChart();
    }

    /// <summary>[UI] Fills in the Summary region's narrative, chart, and aside. Pass already-composed wording (PlanHealthMessages) — this method only lays content out, it doesn't decide what anything says.</summary>
    /// <param name="actualTrajectory">Real (Date, ExpectedAmount) samples from today through the due date or forecast horizon, whichever comes first — ordered. Empty when there's no forecast yet. Today-onward only; see this class's own header comment for why the segment before Today stays an approximation.</param>
    /// <param name="milestoneTrajectory">Real (Date, MilestoneAmount) samples across the WHOLE plan, Start through the due date — no Today split, since this one doesn't need real transaction history. Empty when there's no plan. Drawn for every goal with a plan, one-time or repeating (settled-designs.html, superseding planning/22 §6c's older repeating-only description).</param>
    public void Load(
        string narrative,
        DateOnly start, DateOnly asOfDate, DateOnly dueDate,
        decimal startAmount, decimal goalAmount,
        IReadOnlyList<(DateOnly Date, decimal ActualAmount)> actualTrajectory,
        IReadOnlyList<(DateOnly Date, decimal MilestoneAmount)> milestoneTrajectory,
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

        _chart = new ChartData(start, asOfDate, dueDate, startAmount, goalAmount, actualTrajectory, milestoneTrajectory);
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
        var maxAmount = Math.Max((double)chart.GoalAmount, Math.Max((double)chart.StartAmount, Math.Max((double)actualMax, (double)milestoneMax))) * 1.05;
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

        // "Actual" — split at Today (see this class's own header comment for
        // why). Before Today: the older straight-line placeholder, one real
        // point at each end. Today onward: real, walked, per-occurrence data
        // off the already-saved forecast, turned into a proper step/
        // staircase (Stepped below) rather than smoothed into a ramp, so a
        // plan whose actual balance resets at every release actually reads
        // as resetting.
        if (chart.ActualTrajectory.Count > 0)
        {
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
        }

        var todayX = X(chart.AsOfDate);
        ChartCanvas.Children.Add(new Line
        {
            X1 = todayX, Y1 = topMargin, X2 = todayX, Y2 = topMargin + plotHeight,
            Stroke = Brushes.DarkGray, StrokeThickness = 1, StrokeDashArray = [2, 2],
        });
        ChartCanvas.Children.Add(TextAt("Today", todayX, topMargin + plotHeight + 4, right: false));
        ChartCanvas.Children.Add(TextAt($"Due {chart.DueDate:MMM d}", width - rightMargin, topMargin + plotHeight + 4, right: true));
    }

    // Turns a chronological list of (x, y) samples into a proper step —
    // hold flat at the previous value until the next date, THEN jump —
    // matching the mockups' own SVG path style (alternating H/V segments),
    // not a smoothed ramp between real data points. A 2-point line has
    // nothing to step, so it passes through unchanged (the Start-to-Today
    // placeholder segment uses that path deliberately, staying a plain
    // diagonal rather than a fabricated step).
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
