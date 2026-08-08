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
//  - Text label positioning (TextAt) approximates right-alignment from
//    string length instead of measuring the rendered text — fine at the
//    font size/amounts used so far, but a real fix once this is visible in
//    the actual app.
//
// BUILT 2026-08-07 (author's own go-ahead): the two lines the TODO above
// used to flag as missing now exist — ProposedTrajectory (Savings-plan
// mode's own "proposed — rough, live estimate" line, settled-designs.html)
// and AdditionAmount (One-off mode's own "+ $X today" line,
// earmark-form-layout-mockups.html's own "Summary B"). Neither is ever
// drawn together with the OTHER mode's own lines — see DrawChart's own
// comments for exactly which combination each mode gets and why.
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
        IReadOnlyList<(DateOnly Date, decimal MilestoneAmount)> MilestoneTrajectory,
        IReadOnlyList<DateOnly> PeakDates,
        DateOnly? HighlightDate,
        IReadOnlyList<(DateOnly Date, decimal Amount)> ProposedTrajectory,
        decimal? AdditionAmount);

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
    /// <param name="peakDates">Which of the goal's own occurrence dates to label on the chart with their own gridline, most-recent-first from Start — empty for a one-time goal, which already has its single real due date labeled separately. Caller decides how many (planning/mockups/settled-designs.html's own "Earmark · 3" section labels 3); this control just draws whatever list it's given.</param>
    /// <param name="highlightDate">One of peakDates (or Start-of-window's own first upcoming occurrence) to mark in the same color as the first-payment warning text, when that warning is showing — so the reader can tell which gridline it's about instead of guessing. Null when no such warning is showing.</param>
    /// <param name="proposedTrajectory">Savings-plan mode's own "proposed — rough, live estimate" line (settled-designs.html): whatever's currently typed in Amount/Recurrence, computed via TransactionLogBookFactory.ComputeMilestoneTrajectory, no forecast needed. Pass empty in One-off mode (nothing there is proposing a new rate) — DrawChart falls back to drawing the real, saved ActualTrajectory instead; the two are never drawn together.</param>
    /// <param name="additionAmount">One-off mode's own "+ $X today" line (earmark-form-layout-mockups.html's own "Summary B" — the extra detail that mode specifically needs): the signed amount the currently-typed one-off would add, drawn as ActualTrajectory shifted by this much from Today onward. Null in Savings-plan mode, or whenever nothing valid is typed yet.</param>
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

    // Simple, honest chart: a flat goal line, a straight line from the
    // starting point to today (see the class-level TODO for why it isn't the
    // real stepped trajectory yet), and a dashed "today" marker. Coordinates
    // scale to whatever size the Canvas actually gets laid out at, so this
    // re-runs on every SizeChanged, not just on Load.
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

        // Author, 2026-08-07: "that third chart line would be incredibly
        // helpful" — Savings-plan mode's own "proposed — rough, live
        // estimate" line (settled-designs.html) and One-off mode's own
        // "Actual" line are mutually exclusive, never drawn together: the
        // hierarchy-level distinction the author raised — the further down
        // toward an isolated earmark event this form is looking, the more
        // "what's actually saved right now" matters over "what would this
        // proposed rate produce" — is exactly what decides which one a
        // given call to Load even has data for (EarmarkFormPanel.UpdateSummary
        // only ever builds a ProposedTrajectory in Savings-plan mode). Reuses
        // the same SteelBlue solid treatment for both — settled-designs.html
        // itself does (compare Earmark · 1's "Savings plan (proposed)" and
        // Earmark · 5's "Fund jar (actual)", both var(--accent)) — since they
        // occupy the same visual role (this savings plan's own progress) and
        // are never on screen at the same time to be confused with each other.
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

        // Author, 2026-08-07: One-off mode's own extra line
        // (earmark-form-layout-mockups.html's own "Summary B" — "the extra
        // context this mode specifically needs") — the ActualTrajectory
        // shape, shifted by the currently-typed one-off's own amount, from
        // Today onward. Deliberately a plain parallel shift, not a
        // recomputation through the real floor/deallocation rules — Summary
        // B's own note: "it doesn't re-solve the regular contributions...
        // changing the plan itself is a different action from adding a
        // one-off." AdditionAmount already carries GetOneOffLiveDelta's own
        // scope limit (0 for a date backdated before this goal's own most
        // recent release — see that method's own TODO), so this simply
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

        // Author, 2026-08-07: "I don't know what dates those actual peaks
        // are on" — planning/mockups/settled-designs.html's own "Earmark ·
        // 3" section already settled this (a labeled gridline at each
        // period boundary), it just never made it into this control. Caller
        // decides which dates and how many (empty for a one-time goal,
        // which keeps the plain "Due {chart.DueDate}" label below instead
        // — it already names a real, single due date, nothing to pick out
        // of a row of repeats). WarningBrush matches
        // StartingShortfallWarningText's own #FF9A4F08 (EarmarkFormPanel.xaml)
        // — the same warning color already used for the first-payment
        // warning text this gridline is meant to be found from.
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

        // Author, 2026-08-07: "I didn't even notice the legend in the
        // settled design. But yes, that would be helpful" — matches
        // settled-designs.html's own .chart-legend row. Built from whichever
        // lines actually got drawn above (legendEntries), not a fixed set —
        // Savings-plan mode and One-off mode never show the same
        // combination (see the Proposed/Actual mutual-exclusion comment
        // above), so a fixed legend would either omit or fabricate an entry
        // depending on mode.
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
    //
    // BUG FOUND AND FIXED (2026-08-07, author's own report: "'Today' —
    // 'To' runs out of frame"): right's own branch clamped its left edge to
    // Math.Max(0, ...) so long text can't run off the canvas's LEFT side;
    // the other branch (used for "Today", and now the peak-date labels
    // too) never had that same clamp. "Today" sits at whatever X the
    // as-of date falls on, which is the chart's own left edge for any
    // freshly-created plan (Start == today) — Canvas.Left came out
    // negative, and WPF simply doesn't draw what's positioned off-canvas;
    // that's the missing "To". Both branches now clamp to
    // [0, canvasWidth - the text's own estimated width], so neither edge
    // can run off the canvas regardless of where x falls.
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
