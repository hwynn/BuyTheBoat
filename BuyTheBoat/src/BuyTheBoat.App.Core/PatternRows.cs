using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

// Display-only row shapes for MainWindow's grids, kept out of BuyTheBoat.Domain since how things read in a list is a UI concern.

/// <summary>
/// A List subtab row that can be the item open in its Form subtab.
/// IsOpenInForm and IsDirty change after the row is created, so they raise PropertyChanged for the row style's DataTriggers.
/// </summary>
public abstract class FormListRow : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isOpenInForm;
    public bool IsOpenInForm
    {
        get => _isOpenInForm;
        set
        {
            if (value == _isOpenInForm)
            {
                return;
            }

            _isOpenInForm = value;
            RaisePropertyChanged(nameof(IsOpenInForm));
        }
    }

    // True only while this row is open in the form and the form has unsaved changes.
    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (value == _isDirty)
            {
                return;
            }

            _isDirty = value;
            RaisePropertyChanged(nameof(IsDirty));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>[UI] Tells the grid one of this row's shown values changed, so the cell redraws without rebuilding the list.</summary>
    protected void RaisePropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
}
/// <summary>One row of the Bills &amp; Paychecks list: a bill or paycheck shown once for its whole break-off chain, with the latest segment's values.</summary>
public sealed class FinancialPatternRow(FinancialPattern pattern, string accountName, IReadOnlyList<FinancialPattern>? segments = null) : FormListRow
{
    public FinancialPattern Pattern { get; } = pattern;

    /// <summary>Every segment of the chain this row stands for (same Source), oldest first; just Pattern when there's one.</summary>
    public IReadOnlyList<FinancialPattern> Segments { get; } = segments ?? [pattern];
    public int FinanceId => Pattern.FinanceId;
    public string Source => Pattern.Source;
    public string? Description => Pattern.Description;
    // The account this pattern is filed under — a display value looked up from
    // storage, not a property of the domain pattern.
    public string Account { get; } = accountName;
    public decimal Amount => Pattern.Amount;
    public bool Mandatory => Pattern.Mandatory;
    public int Priority => Pattern.Priority;
    public string Repeats => Pattern.DatePattern.ToRruleString();
}

/// <summary>
/// One row of the Allocations list: a goal shown once, standing for every plan it has across its break-off segments and any running at the same time.
/// Amount, Already Saved and Repeats describe Pattern together with the plans running alongside it.
/// </summary>
public sealed class EarMarkPatternRow(EarMarkPattern pattern, FinancialPattern? goal, IReadOnlyList<EarMarkPattern>? plans = null) : FormListRow
{
    /// <summary>The goal's latest plan, which opens when the row has no other plans to choose from.</summary>
    public EarMarkPattern Pattern { get; } = pattern;

    /// <summary>Every plan this row stands for, oldest first; just Pattern when there's one.</summary>
    public IReadOnlyList<EarMarkPattern> Plans { get; } = plans ?? [pattern];

    public string Goal => goal?.Description is { Length: > 0 } description
        ? description
        : goal?.Source ?? $"(finance id {Pattern.FinanceId} — goal not found)";

    /// <summary>Pattern and the plans running at the same time as it.</summary>
    private IReadOnlyList<EarMarkPattern> RunningTogether => Plans
        .Where(plan => plan.FinanceId == Pattern.FinanceId && plan.DatePattern.ActiveSpansOverlap(Pattern.DatePattern))
        .ToList();

    public decimal Amount => RunningTogether.Sum(plan => plan.Amount);
    public decimal StartingAllocation => RunningTogether.Sum(plan => plan.StartingAllocation);
    public string Repeats => RunningTogether.Count == 1
        ? Pattern.DatePattern.ToRruleString()
        : $"{RunningTogether.Count} plans at once";
}

/// <summary>
/// A row standing in for a mandatory bill's automatic funding, with no saved row behind it.
/// It has the same property names EarMarkPatternsGrid binds to, so it sits beside real EarMarkPatternRows (the grid binds by name).
/// It has no Edit or Delete, and disappears once the bill is deleted or gets its own savings plan.
/// </summary>
public sealed class AutomaticBillEarmarkRow(FinancialPattern bill)
{
    public FinancialPattern Bill { get; } = bill;
    public string Goal => $"{(string.IsNullOrWhiteSpace(Bill.Description) ? Bill.Source : Bill.Description)} (Automatic)";
    public decimal Amount => Bill.Amount;
    public decimal StartingAllocation => 0m;
    public string Repeats => Bill.DatePattern.ToRruleString();
}

/// <summary>One forecast row under its display date, used only by the spreadsheet export's Timeline sheet; the on-screen overview uses MonthRow and DayCellRow.</summary>
public sealed class TimelineRow(TimelineEntry entry, IReadOnlyDictionary<int, string> jarLabels)
{
    public BalanceSnapshot Snapshot { get; } = entry.Snapshot;

    public DateOnly Date => entry.Date;
    public decimal Balance => Snapshot.ExpectedAmount ?? 0m;
    public string Reserved => FormatReserved(Snapshot, jarLabels);
    public decimal FreeBalance => Snapshot.ExpectedFreeAmount ?? 0m;

    /// <summary>Is free balance negative this day? Turns the "Free Balance" cell red, a quiet per-row warning rather than a popup.</summary>
    public bool FreeBalanceNegative => FreeBalance < 0m;

    public string Events => FormatEvents(Snapshot, jarLabels);

    // Internal (not private): ForecastSpreadsheetExporter reuses these so the
    // exported file's columns read identically to the on-screen grid.

    /// <summary>[CALC] Lists the day's jars, leaving out empty ones so most days don't read as a wall of "0.00".</summary>
    /// <param name="snapshot">The day's balance snapshot, for its fund jars.</param>
    /// <param name="jarLabels">Finance id → display label, for naming each jar.</param>
    internal static string FormatReserved(BalanceSnapshot snapshot, IReadOnlyDictionary<int, string> jarLabels) =>
        string.Join(", ", snapshot.FundJars
            .Where(jar => jar.ExpectedAmount > 0m)
            .Select(jar => $"{JarLabel(jar.FinanceId, jarLabels)} {jar.ExpectedAmount:N2}"));

    /// <summary>
    /// [CALC] Lists the day's scheduled events: expected transactions and planned savings contributions.
    /// Automatic events (cushion refills, deallocation give-backs, goal releases) show in the selected-day pane instead, where there's room to label them.
    /// </summary>
    /// <param name="snapshot">The day's balance snapshot, for its transactions and earmark events.</param>
    /// <param name="jarLabels">Finance id → display label, for naming each event.</param>
    internal static string FormatEvents(BalanceSnapshot snapshot, IReadOnlyDictionary<int, string> jarLabels)
    {
        var parts = new List<string>();

        foreach (var transaction in snapshot.ExpectedTransactions)
        {
            parts.Add($"{JarLabel(transaction.FinanceId, jarLabels)} {(transaction.ExpectedAmount >= 0 ? "+" : string.Empty)}{transaction.ExpectedAmount:N2}");
        }

        foreach (var earmarkEvent in snapshot.EarMarkEvents.Where(e => e.RepeatedEarmark))
        {
            // Shown as its effect on free balance (an allocation reduces it).
            parts.Add($"{JarLabel(earmarkEvent.FinanceId, jarLabels)} {-earmarkEvent.ExpectedAmount:N2}");
        }

        return string.Join(", ", parts);
    }

    internal static string JarLabel(int? financeId, IReadOnlyDictionary<int, string> jarLabels) =>
        financeId is { } id
            ? jarLabels.GetValueOrDefault(id, $"(finance id {id})")
            : "Safety cushion";
}

// ===== The calendar overview =====
//
// The overview rows: many days at a glance, as month sections of Sun–Sat day cells, each with free and total set aside, event markers, warning tints, and today stood out; per-day detail is in the selected-day rows further down.

/// <summary>One month section: a title and its cells in grid order, with leading blank cells so day 1 sits under its weekday.</summary>
public sealed class MonthRow
{
    public required string Title { get; init; }               // "July 2026"
    public required string MonthAutomationId { get; init; }   // "Month_2026-07"
    public required IReadOnlyList<DayCellRow> Cells { get; init; }
}

/// <summary>One account's money-flow marker in a day cell: its first letter plus ↑ for money in, ↓ for money out, or • for no change, readable without relying on color.</summary>
public sealed class AccountFlowCell
{
    public required string Letter { get; init; }
    public required string Glyph { get; init; }
    public required string Kind { get; init; } // "In" | "Out" | "None"
}

/// <summary>
/// One calendar day cell: labeled Total and Free, the day's top event, an event count, a per-account flow strip, and a ⚠ warning with words.
/// Everything its triggers read is worked out up front, so recycled cells rebind cheaply while scrolling.
/// IsSelected changes after creation (clicking another day un-highlights this one), so it raises PropertyChanged.
/// </summary>
public sealed class DayCellRow : System.ComponentModel.INotifyPropertyChanged
{
    public DateOnly? Date { get; }

    public bool IsPadding => Date is null;
    public string DayNumber { get; } = string.Empty;

    // "Active": the day has something to show, so it can be selected.
    public bool HasSnapshot { get; }

    public string TotalText { get; } = string.Empty;
    public string FreeText { get; } = string.Empty;
    public bool FreeNegative { get; }

    // The day's highest-priority expected transaction, by name.
    public string TopEventText { get; } = string.Empty;

    // Spelled out rather than dots — there is room to just say it.
    public string EventCountText { get; } = string.Empty;

    public IReadOnlyList<AccountFlowCell> Flows { get; } = [];

    public bool ShowWarning { get; }
    public string WarningText { get; } = string.Empty;

    public bool IsToday { get; }
    public bool NeedsAttention { get; }

    public string AutomationId { get; } = string.Empty;
    public string AutomationName { get; } = string.Empty;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value == _isSelected)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>[CALC] A leading grid slot before day 1 — renders as nothing.</summary>
    public static DayCellRow Padding() => new();

    private DayCellRow()
    {
    }

    /// <summary>[CALC] An event-less day: present but faint, with nothing to report.</summary>
    /// <param name="date">The day this cell represents.</param>
    public DayCellRow(DateOnly date)
    {
        Date = date;
        DayNumber = date.Day.ToString();
        IsToday = date == DateOnly.FromDateTime(DateTime.Today);
        AutomationId = $"Day_{date:yyyy-MM-dd}";
        AutomationName = date.ToString("MMMM d, yyyy");
    }

    public DayCellRow(
        DateOnly date,
        decimal total,
        decimal free,
        string topEvent,
        int eventCount,
        IReadOnlyList<AccountFlowCell> flows,
        string warningText,
        bool needsAttention)
    {
        Date = date;
        DayNumber = date.Day.ToString();
        IsToday = date == DateOnly.FromDateTime(DateTime.Today);
        AutomationId = $"Day_{date:yyyy-MM-dd}";
        HasSnapshot = true;

        TotalText = total.ToString("N0");
        FreeText = free.ToString("N0");
        FreeNegative = free < 0m;

        TopEventText = topEvent;
        EventCountText = eventCount switch
        {
            <= 0 => "No events",
            1 => "1 event",
            _ => $"{eventCount} events",
        };
        Flows = flows;

        WarningText = warningText;
        ShowWarning = warningText.Length > 0;
        NeedsAttention = needsAttention;

        AutomationName = $"{date:MMMM d, yyyy} — total {TotalText}, free {FreeText}"
            + (ShowWarning ? $", {warningText}" : string.Empty);
    }
}

// ===== The selected-day region =====
//
// The selected-day rows: every transaction (left pane), every jar with its "am I on track?" health (right pane, shaped per ExpenseKind by JarDetailRow), and the day's free amount.

/// <summary>Everything the selected-day rows need besides the snapshot, built once per selection in MainWindow.</summary>
public sealed class DayDetailContext
{
    public required DateOnly Date { get; init; }
    public required IReadOnlyDictionary<int, string> JarLabels { get; init; }
    public required IReadOnlyDictionary<int, GoalShortfall> ShortfallsByFinanceId { get; init; }
    public required IReadOnlyDictionary<int, FinancialPattern> PatternsById { get; init; }
    public required IReadOnlySet<int> EarmarkedIds { get; init; }
    public required decimal CushionTarget { get; init; }

    // Finance ids whose manual withdrawal got floored on THIS day (only what
    // the jar held actually moved) — flagged in place.
    public IReadOnlySet<int> FlooredFinanceIds { get; init; } = new HashSet<int>();
}

/// <summary>
/// One jar in the selected-day pane, shown by its ExpenseKind: a bill shows the amount due and whether it can be paid in full now; a one-time goal shows its milestone and how far off it's due; the cushion shows its target.
/// StatusKind sets the pill color in XAML: "FullyCovered" (strongest green), "OnTrack" (green), "Behind" (amber), or "Neutral".
/// </summary>
public sealed class JarDetailRow
{
    // Which account this jar belongs to — the selected-day panes group by it so
    // each account's story stays together (grouped two-pane).
    public AccountGroupKey? Account { get; set; }

    public required string Jar { get; init; }
    public required string AmountText { get; init; }
    public required string StatusKind { get; init; }
    public required string StatusText { get; init; }
    public required string SubText { get; init; }
    public double ProgressFraction { get; init; }
    public bool ShowProgress { get; init; }

    // True when this jar was raided on a deallocation day (drives the red row).
    public bool Drained { get; init; }

    // True when a manual withdrawal on this day exceeded what the jar held and
    // got floored — drives the amber warning on the sub line.
    public bool Floored { get; init; }

    // The readjustment on-ramp: a behind goal invites restructuring its plan.
    // Inert for now — clicking it doesn't open the plan on the Earmark tab yet.
    public bool ShowAdjustNudge { get; init; }

    public static JarDetailRow From(FundJar jar, BalanceSnapshot snapshot, DayDetailContext context)
    {
        // Red only on a deallocation day, since on a normal day a negative isolated earmark is a planned payout.
        var drained = snapshot.IsDeallocationDay
            && NetIsolatedEarmark(snapshot, jar.FinanceId) < 0m;
        var saved = jar.ExpectedAmount;

        if (jar.FinanceId is not { } financeId)
        {
            return Cushion(saved, context.CushionTarget, drained);
        }

        var floored = context.FlooredFinanceIds.Contains(financeId);
        var label = TimelineRow.JarLabel(financeId, context.JarLabels);
        if (!context.PatternsById.TryGetValue(financeId, out var pattern))
        {
            // Shouldn't happen (every jar's finance id has a pattern), but a
            // label + amount beats a crash if data drifts.
            return new JarDetailRow
            {
                Jar = label, AmountText = saved.ToString("N2"), StatusKind = "Neutral",
                StatusText = string.Empty, SubText = string.Empty, Drained = drained, Floored = floored,
            };
        }

        var kind = ExpenseKindClassifier.Classify(pattern, context.EarmarkedIds.Contains(financeId));
        return kind switch
        {
            ExpenseKind.Bill => Bill(label, pattern, jar, saved, context, drained, floored),
            ExpenseKind.OneTimeGoal => Goal(label, pattern, jar, saved, context, drained, floored, oneTime: true),
            ExpenseKind.RepeatingGoal => Goal(label, pattern, jar, saved, context, drained, floored, oneTime: false),
            _ => new JarDetailRow
            {
                Jar = label, AmountText = saved.ToString("N2"), StatusKind = "Neutral",
                StatusText = string.Empty, SubText = string.Empty, Drained = drained, Floored = floored,
            },
        };
    }

    /// <summary>[CALC] The floored warning leads the sub line so it can't be missed.</summary>
    /// <param name="subText">The sub-line text to prepend the warning to.</param>
    /// <param name="floored">Whether a manual withdrawal exceeded this jar and got floored.</param>
    private static string WithFlooredWarning(string subText, bool floored) =>
        floored ? $"⚠ a manual withdrawal exceeded this jar — only what it held moved · {subText}" : subText;

    /// <summary>[CALC] A regular bill: show the amount due; styling distinguishes "on track vs. the milestone" (green) from "could pay the whole bill right now" (strongest green). Precise due date, month as a word.</summary>
    /// <param name="label">The jar's display label.</param>
    /// <param name="pattern">The bill's financial pattern, for its amount and due date.</param>
    /// <param name="jar">The bill's fund jar, for its milestone.</param>
    /// <param name="saved">What the jar currently holds.</param>
    /// <param name="context">The selected day's shared context (dates, labels, etc.).</param>
    /// <param name="drained">Whether this jar was raided on a deallocation day.</param>
    /// <param name="floored">Whether a manual withdrawal exceeded this jar and got floored.</param>
    private static JarDetailRow Bill(
        string label, FinancialPattern pattern, FundJar jar, decimal saved,
        DayDetailContext context, bool drained, bool floored)
    {
        var amountDue = Math.Abs(pattern.Amount);
        var nextDue = pattern.DatePattern.GetOccurrences(context.Date).FirstOrDefault();

        string statusKind, statusText;
        if (saved >= amountDue)
        {
            (statusKind, statusText) = ("FullyCovered", "Fully covered");
        }
        else if (jar.MilestoneAmount is { } milestone && saved < milestone)
        {
            (statusKind, statusText) = ("Behind", $"Behind {milestone - saved:N0}");
        }
        else
        {
            // At or above the savings plan's milestone, though not yet the
            // full amount.
            (statusKind, statusText) = ("OnTrack", "On track");
        }

        return new JarDetailRow
        {
            Jar = label,
            AmountText = saved.ToString("N2"),
            StatusKind = statusKind,
            StatusText = statusText,
            SubText = WithFlooredWarning(
                nextDue == default
                    ? $"amount due {amountDue:N0} · no upcoming due date"
                    : $"amount due {amountDue:N0} · due {nextDue:MMMM d}",
                floored),
            ProgressFraction = amountDue > 0m ? Math.Min(1.0, (double)(saved / amountDue)) : 0.0,
            ShowProgress = true,
            Drained = drained,
            Floored = floored,
        };
    }

    /// <summary>[CALC] A goal with a savings plan: progress toward the MILESTONE (am I on track setting money aside), not the full amount. One-time goals get a relative due summary when far out; repeating ones a precise date.</summary>
    /// <param name="label">The jar's display label.</param>
    /// <param name="pattern">The goal's financial pattern, for its due date and amount.</param>
    /// <param name="jar">The goal's fund jar, for its milestone.</param>
    /// <param name="saved">What the jar currently holds.</param>
    /// <param name="context">The selected day's shared context (dates, shortfalls, etc.).</param>
    /// <param name="drained">Whether this jar was raided on a deallocation day.</param>
    /// <param name="floored">Whether a manual withdrawal exceeded this jar and got floored.</param>
    /// <param name="oneTime">Whether this is a one-time goal rather than a repeating one.</param>
    private static JarDetailRow Goal(
        string label, FinancialPattern pattern, FundJar jar, decimal saved,
        DayDetailContext context, bool drained, bool floored, bool oneTime)
    {
        var behind = context.ShortfallsByFinanceId.TryGetValue(pattern.FinanceId, out var shortfall)
            && shortfall.ShortfallAmount > 0m;
        var milestone = jar.MilestoneAmount ?? 0m;
        var goalAmount = Math.Abs(pattern.Amount);

        var dueText = oneTime
            ? DueDateText.Relative(pattern.DatePattern.Until, context.Date)
            : pattern.DatePattern.GetOccurrences(context.Date).FirstOrDefault() is var next && next != default
                ? $"next due {next:MMMM d}"
                : $"ends {pattern.DatePattern.Until:MMMM d, yyyy}";

        return new JarDetailRow
        {
            Jar = label,
            AmountText = saved.ToString("N2"),
            StatusKind = behind ? "Behind" : "OnTrack",
            StatusText = behind ? $"Behind {shortfall!.ShortfallAmount:N0}" : "On track",
            SubText = WithFlooredWarning($"milestone {milestone:N0} · goal {goalAmount:N0} · {dueText}", floored),
            ProgressFraction = milestone > 0m
                ? Math.Min(1.0, (double)(saved / milestone))
                : saved > 0m ? 1.0 : 0.0,
            ShowProgress = true,
            Drained = drained,
            Floored = floored,
            ShowAdjustNudge = behind,
        };
    }

    private static JarDetailRow Cushion(decimal saved, decimal target, bool drained)
    {
        string statusKind, statusText;
        if (target <= 0m)
        {
            (statusKind, statusText) = ("Neutral", "Not set");
        }
        else if (saved >= target)
        {
            (statusKind, statusText) = ("FullyCovered", "Funded");
        }
        else
        {
            (statusKind, statusText) = ("Behind", $"Below target by {target - saved:N0}");
        }

        return new JarDetailRow
        {
            Jar = "Safety cushion",
            AmountText = saved.ToString("N2"),
            StatusKind = statusKind,
            StatusText = statusText,
            SubText = target > 0m ? $"target {target:N0}" : "set a cushion amount above to reserve a buffer",
            ProgressFraction = target > 0m ? Math.Min(1.0, (double)(saved / target)) : 0.0,
            ShowProgress = target > 0m,
            Drained = drained,
        };
    }

    /// <summary>[CALC] Sum of this finance id's isolated (non-repeated) earmark amounts on the day — negative once a deallocation give-back outweighs any scheduled fill.</summary>
    /// <param name="snapshot">The day's balance snapshot, for its earmark events.</param>
    /// <param name="financeId">Which jar to sum isolated earmarks for.</param>
    private static decimal NetIsolatedEarmark(BalanceSnapshot snapshot, int? financeId) =>
        snapshot.EarMarkEvents
            .Where(earmark => !earmark.RepeatedEarmark && earmark.FinanceId == financeId)
            .Sum(earmark => earmark.ExpectedAmount);
}

/// <summary>Describes a due date: a far-off one as "~8 months away", and a close one (within ~2 months) as a written-out date.</summary>
internal static class DueDateText
{
    internal static string Relative(DateOnly due, DateOnly from)
    {
        var days = due.DayNumber - from.DayNumber;
        if (days < 0)
        {
            return "due date passed";
        }

        if (days <= 62)
        {
            return $"due {due:MMMM d}";
        }

        var months = (int)Math.Round(days / 30.44);
        return months < 24
            ? $"~{months} months away ({due:MMM yyyy})"
            : $"~{(int)Math.Round(months / 12.0)} years away ({due:MMM yyyy})";
    }
}

/// <summary>
/// How the selected-day panes group their rows: the account, its balance that day, how short it is, and whether another account could cover the gap (the shortfall ladder).
/// A record, so grouping works by value, and the group header can show the balance, the shortfall narrative, and the "move money in" button.
/// </summary>
/// <remarks>
/// CanCoverElsewhere true: the money is in another account (DonorName names it when one account covers the whole gap), so the "move money in" button shows.
/// CanCoverElsewhere false: no other account can cover it, so there's no button.
/// IsThin: free funds are low but not negative (below one cushion's worth), a milder warning beside "short"; a dipped cushion always reads as thin too, since a cushion only drops when a deallocation drains it to avoid going short.
/// </remarks>
public sealed record AccountGroupKey(
    int AccountId, string Name, decimal Balance, decimal Shortfall, bool CanCoverElsewhere = false, string? DonorName = null,
    bool IsThin = false)
{
    public bool IsShort => Shortfall > 0m;

    // How much money the account actually holds on the day, shown right after the
    // name — the header's own total, above the jar breakdown below it.
    public string BalanceText => Balance.ToString("N0");
    public bool BalanceNegative => Balance < 0m;

    // Rung 2 only: a transfer can actually close the gap, so the lever appears.
    public bool ShowCoverButton => IsShort && CanCoverElsewhere;

    // "Short 2,000" — the how-bad, shown in red to the right of the balance.
    public string ShortText => $"Short {Shortfall:N0}";

    // The lever's label, direction-explicit ("in" = into this account): the app
    // pre-fills only the destination and amount, so the user picks the source.
    public string MoveInText => $"Move {Shortfall:N0} in →";

    // Line two of the header: which rung, and where the money is (rung 2) or that
    // there is none to be had (rung 3). Empty when the account isn't short.
    public string RungText => !IsShort
        ? string.Empty
        : CanCoverElsewhere
            ? DonorName is { } donor
                ? $"In another account — the money's in {donor}."
                : "In another account — the money's in your other accounts."
            : "No other account can cover it.";

    /// <summary>
    /// [CALC] The account's group header as one line for screen readers: name and balance, any "short" or "funds running low" warning, then the shortfall narrative, in the same order as on screen.
    /// MainWindow.axaml binds this as the account header's screen-reader name, so the header reads as one line.
    /// </summary>
    public string AccessibleSummary
    {
        get
        {
            var summary = $"{Name}, balance {BalanceText}";
            if (IsShort) summary += $", {ShortText}";
            if (IsThin) summary += ", funds running low";
            if (IsShort && RungText.Length > 0) summary += $". {RungText}";
            return summary;
        }
    }
}

/// <summary>
/// One event in the selected-day "What happened today" pane: every transaction and earmark event that day, ordered so a paycheck sits above the allocations it funds, with deallocation pulls last.
/// ChipKind sets the chip color in XAML: "In", "Out", "Aside", "Release", or "Pull".
/// </summary>
public sealed class DayEventRow
{
    // Which account this event happened in — the panes group by it.
    public AccountGroupKey? Account { get; set; }

    public required string Event { get; init; }
    public required string Chip { get; init; }
    public required string ChipKind { get; init; }
    public required string AmountText { get; init; }
    public bool IsPositive { get; init; }
    public bool IsPull { get; init; }

    // Allocations render indented under the day's income ("how much
    // did I get, and where did it go?").
    public bool Indented { get; init; }

    public static List<DayEventRow> From(BalanceSnapshot snapshot, DayDetailContext context)
    {
        var income = new List<DayEventRow>();
        var asides = new List<DayEventRow>();
        var manual = new List<DayEventRow>();
        var expenses = new List<DayEventRow>();
        var releases = new List<DayEventRow>();
        var pulls = new List<DayEventRow>();

        foreach (var transaction in snapshot.ExpectedTransactions.Where(t => !t.Cancelled))
        {
            var label = TimelineRow.JarLabel(transaction.FinanceId, context.JarLabels);
            if (transaction.ExpectedAmount > 0m)
            {
                income.Add(new DayEventRow
                {
                    Event = label, Chip = "IN", ChipKind = "In",
                    AmountText = $"+{transaction.ExpectedAmount:N2}", IsPositive = true,
                });
            }
            else
            {
                var mandatory = context.PatternsById.TryGetValue(transaction.FinanceId, out var pattern)
                    && pattern.Mandatory;
                expenses.Add(new DayEventRow
                {
                    Event = label, Chip = mandatory ? "BILL" : "EXPENSE", ChipKind = "Out",
                    AmountText = transaction.ExpectedAmount.ToString("N2"),
                });
            }
        }

        foreach (var earmark in snapshot.EarMarkEvents)
        {
            var label = TimelineRow.JarLabel(earmark.FinanceId, context.JarLabels);
            if (!earmark.RepeatedEarmark && earmark.ExplicitAmount is { } explicitAmount && explicitAmount != 0m)
            {
                // The user's own adjustment, told apart from automatic events by a nonzero ExplicitAmount; a merged deallocation give-back shows in the jar rows instead.
                manual.Add(new DayEventRow
                {
                    Event = label, Chip = "MANUAL", ChipKind = "Manual",
                    AmountText = earmark.ExpectedAmount >= 0m
                        ? $"+{earmark.ExpectedAmount:N2}"
                        : earmark.ExpectedAmount.ToString("N2"),
                });
            }
            else if (earmark.RepeatedEarmark || earmark.ExpectedAmount >= 0m)
            {
                asides.Add(new DayEventRow
                {
                    Event = label, Chip = "SET ASIDE", ChipKind = "Aside",
                    AmountText = earmark.ExpectedAmount.ToString("N2"),
                });
            }
            else if (snapshot.IsDeallocationDay)
            {
                pulls.Add(new DayEventRow
                {
                    Event = label, Chip = "PULLED", ChipKind = "Pull",
                    AmountText = earmark.ExpectedAmount.ToString("N2"), IsPull = true,
                });
            }
            else
            {
                // A goal bought as planned: its jar releases the spent money.
                releases.Add(new DayEventRow
                {
                    Event = label, Chip = "RELEASED", ChipKind = "Release",
                    AmountText = earmark.ExpectedAmount.ToString("N2"),
                });
            }
        }

        if (income.Count > 0)
        {
            // Indent set-asides under the paycheck (rows are init-only — rebuild).
            asides = asides
                .Select(row => new DayEventRow
                {
                    Event = row.Event, Chip = row.Chip, ChipKind = row.ChipKind,
                    AmountText = row.AmountText, Indented = true,
                })
                .ToList();
        }

        return [.. income, .. asides, .. manual, .. expenses, .. releases, .. pulls];
    }
}

/// <summary>One row of the Allocations tab's "Manual adjustments" grid.</summary>
public sealed class ManualEarmarkRow(ManualEarmark earmark, string target) : FormListRow
{
    public ManualEarmark Earmark { get; } = earmark;
    public DateOnly Date => Earmark.Date;
    public string Target { get; } = target;
    public decimal Amount => Earmark.Amount;
}

/// <summary>One goal's savings status, for the spreadsheet export's Goals sheet and the detail pane's Status column.</summary>
public sealed class GoalStatusRow(GoalShortfall status)
{
    public string Goal => status.Label;
    public DateOnly DueDate => status.DueDate;
    public decimal AmountNeeded => status.AmountNeeded;
    public decimal AmountAllocatedByDueDate => status.AmountAllocatedByDueDate;
    public string Status => FormatStatus(status);

    internal static string FormatStatus(GoalShortfall status) => status.ShortfallAmount > 0m
        ? $"Short by {status.ShortfallAmount:N2}"
        : "On track";
}

/// <summary>
/// One row of the Accounts tab; the cushion shows blank rather than "0.00" when the user hasn't set one.
/// The balance is typed straight into the list, so a saved Account replaces the row in place (ReplaceAccount) rather than rebuilding the list under the box.
/// </summary>
public sealed class AccountRow(Account account) : FormListRow
{
    public Account Account { get; private set; } = account;
    public string Name => Account.Name;
    public decimal Balance => Account.Balance;
    public string BalanceText => Account.Balance.ToString("N2");

    /// <summary>[UI] Shows the account's newly saved balance in this row.</summary>
    public void ReplaceAccount(Account saved)
    {
        Account = saved;
        RaisePropertyChanged(nameof(Balance));
        RaisePropertyChanged(nameof(BalanceText));
    }

    public string Cushion => Account.IdealSafetyCushion == 0m
        ? "—"
        : Account.IdealSafetyCushion.ToString("N2");
}

/// <summary>One row of the Accounts tab's Money flow subtab: an account's average monthly money in and out over the forecast window.</summary>
public sealed record AccountFlowRow(string Name, decimal MonthlyIn, decimal MonthlyOut)
{
    public decimal MonthlyNet => MonthlyIn - MonthlyOut;

    /// <summary>[CALC] True when more leaves this account each month than arrives, which turns its Net red.</summary>
    public bool IsDraining => MonthlyNet < 0m;
}

/// <summary>One row of the Transfers tab, with From and To as account names; its two patterns don't appear, so a transfer reads as one thing.</summary>
public sealed class TransferRow(Transfer transfer, string fromName, string toName) : FormListRow
{
    public Transfer Transfer { get; } = transfer;
    public string From { get; } = fromName;
    public string To { get; } = toName;
    public decimal Amount => Transfer.Amount;
    public string Repeats => Transfer.DatePattern.ToRruleString();
}

/// <summary>One entry in the overview's account filter; a null AccountId is "All accounts".</summary>
public sealed record AccountFilterOption(int? AccountId, string Name);
