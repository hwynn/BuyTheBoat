using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// Flattened, display-only shapes for the grids in MainWindow — kept out of
// MyMoneyForecast.Domain deliberately, since "how a goal's description should
// read in a list" is a UI concern, not a domain one.
public sealed class FinancialPatternRow(FinancialPattern pattern, string accountName)
{
    public FinancialPattern Pattern { get; } = pattern;
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

public sealed class EarMarkPatternRow(EarMarkPattern pattern, FinancialPattern? goal)
{
    public EarMarkPattern Pattern { get; } = pattern;
    public string Goal => goal?.Description is { Length: > 0 } description
        ? description
        : goal?.Source ?? $"(finance id {Pattern.FinanceId} — goal not found)";
    public decimal Amount => Pattern.Amount;
    public decimal StartingAllocation => Pattern.StartingAllocation;
    public string Repeats => Pattern.DatePattern.ToRruleString();
}

// A synthesized stand-in for a mandatory bill's automatic funding
// (TransactionLogBookFactory's automatically funded expense earmarking) — deliberately not
// backed by any persisted row. Exposes the same property names
// EarMarkPatternsGrid's columns already bind to so it can sit in the same
// DataGrid alongside real EarMarkPatternRows; WPF's binding resolves
// properties by name at runtime regardless of the two types sharing no base
// class. Has no Edit/Delete affordance — it reflects the bill's own
// existence and disappears the moment the bill is deleted or gets a real
// explicit earmark instead.
public sealed class AutomaticBillEarmarkRow(FinancialPattern bill)
{
    public FinancialPattern Bill { get; } = bill;
    public string Goal => $"{(string.IsNullOrWhiteSpace(Bill.Description) ? Bill.Source : Bill.Description)} (Automatic)";
    public decimal Amount => Bill.Amount;
    public decimal StartingAllocation => 0m;
    public string Repeats => Bill.DatePattern.ToRruleString();
}

// One row of the forecast rendered under its display date. Since the calendar
// redesign this type feeds ONLY the spreadsheet export's Timeline sheet (the
// on-screen overview is MonthRow/DayCellRow below) — kept because the exporter
// mirrors the pre-calendar tabular reading of a day, and JarLabel is shared.
public sealed class TimelineRow(TimelineEntry entry, IReadOnlyDictionary<int, string> jarLabels)
{
    public BalanceSnapshot Snapshot { get; } = entry.Snapshot;

    public DateOnly Date => entry.Date;
    public decimal Balance => Snapshot.ExpectedAmount ?? 0m;
    public string Reserved => FormatReserved(Snapshot, jarLabels);
    public decimal FreeBalance => Snapshot.ExpectedFreeAmount ?? 0m;

    // Drives the red "Free Balance" cell — a light-touch warning for any day
    // free goes negative (a debt day, an over-committed reservation, or the
    // as-of day if commitments already exceed the balance). All three are worth
    // surfacing; the red is per-row rather than a nagging popup.
    public bool FreeBalanceNegative => FreeBalance < 0m;

    public string Events => FormatEvents(Snapshot, jarLabels);

    // Internal (not private): ForecastSpreadsheetExporter reuses these so the
    // exported file's columns read identically to the on-screen grid.

    /// <summary>[CALC] Zero-balance jars (a goal not yet started, a bill just paid, the still-empty safety cushion) are filtered out — otherwise most days would read as a wall of "$0.00" entries, exactly the noise this column exists to cut through.</summary>
    /// <param name="snapshot">The day's balance snapshot, for its fund jars.</param>
    /// <param name="jarLabels">Finance id → display label, for naming each jar.</param>
    internal static string FormatReserved(BalanceSnapshot snapshot, IReadOnlyDictionary<int, string> jarLabels) =>
        string.Join(", ", snapshot.FundJars
            .Where(jar => jar.ExpectedAmount > 0m)
            .Select(jar => $"{JarLabel(jar.FinanceId, jarLabels)} {jar.ExpectedAmount:C}"));

    /// <summary>[CALC] The at-a-glance column shows the day's scheduled events (expected transactions and planned allocation installments) — the same set the pre-restructure grid showed. System-generated implicit events (bill automatically fund steps, goal releases) appear in the selected-day detail pane instead, where there's room to label what they are.</summary>
    /// <param name="snapshot">The day's balance snapshot, for its transactions and earmark events.</param>
    /// <param name="jarLabels">Finance id → display label, for naming each event.</param>
    internal static string FormatEvents(BalanceSnapshot snapshot, IReadOnlyDictionary<int, string> jarLabels)
    {
        var parts = new List<string>();

        foreach (var transaction in snapshot.ExpectedTransactions)
        {
            parts.Add($"{JarLabel(transaction.FinanceId, jarLabels)} {(transaction.ExpectedAmount >= 0 ? "+" : string.Empty)}{transaction.ExpectedAmount:C}");
        }

        foreach (var earmarkEvent in snapshot.EarMarkEvents.Where(e => e.RepeatedEarmark))
        {
            // Shown as its effect on free balance (an allocation reduces it),
            // matching how these read before the restructure.
            parts.Add($"{JarLabel(earmarkEvent.FinanceId, jarLabels)} {-earmarkEvent.ExpectedAmount:C}");
        }

        return string.Join(", ", parts);
    }

    internal static string JarLabel(int? financeId, IReadOnlyDictionary<int, string> jarLabels) =>
        financeId is { } id
            ? jarLabels.GetValueOrDefault(id, $"(finance id {id})")
            : "Safety cushion";
}

// ===== The calendar overview (planning/11-ui-design-and-decisions.md §A) =====
//
// The OVERVIEW region's job is showing many days at a glance — every balance
// snapshot in the window, per-day free + TOTAL allocated (never a per-jar
// breakdown), event dots for money in/out/aside, attention tints for days
// worth a closer look, and TODAY visually distinct (§2.g). Rendered as month
// sections of Sun–Sat day cells (the chosen calendar direction, see
// planning/mockups/README.md); per-day detail lives in the selected-day region.

// One month section: a title plus its cells in grid order (leading padding
// cells align day 1 under its weekday; the UniformGrid fills left-to-right).
public sealed class MonthRow
{
    public required string Title { get; init; }               // "July 2026"
    public required string MonthAutomationId { get; init; }   // "Month_2026-07"
    public required IReadOnlyList<DayCellRow> Cells { get; init; }
}

// One account's money-flow marker in a day cell: the account's first letter
// plus an arrow — ↑ money in, ↓ money out, • no change (planning/11 §C.2,
// which replaced an unreadable "status square"). The letter and glyph carry
// the meaning, so it reads without relying on color (§C.1).
public sealed class AccountFlowCell
{
    public required string Letter { get; init; }
    public required string Glyph { get; init; }
    public required string Kind { get; init; } // "In" | "Out" | "None"
}

// One calendar day cell — the "rich month calendar" (planning/11 §B): two
// LABELED numbers (Total + Free), the day's top event by name, an explicit
// event count top-right, a per-account flow strip, and a ⚠ + words warning.
// Everything a trigger reads is precomputed at construction so recycled
// containers rebind cheaply during scroll.
//
// Deliberate deviation from this file's otherwise-immutable rows: selection
// changes AFTER creation (clicking day B must un-highlight day A), and the
// cell template's DataTrigger needs a change notification for that — so this
// one class implements INotifyPropertyChanged, with IsSelected as its single
// settable property.
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

    // The day's highest-priority expected transaction, by name — replaces the
    // old "set aside" figure, which was internal jargon (§C.5).
    public string TopEventText { get; } = string.Empty;

    // Spelled out rather than dots — there is room to just say it (§C.6).
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

    /// <summary>[CALC] An event-less day: present but faint, with nothing to report (§2.I.d).</summary>
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

        TotalText = total.ToString("C0");
        FreeText = free.ToString("C0");
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

// ===== The selected-day region (planning/11-ui-design-and-decisions.md §A) =====
//
// Leverages the full BalanceSnapshot: every transaction listed (left pane),
// every fund jar with per-type Q3 health (right pane), and the day's free
// amount. Q3 "am I on track?" takes a different shape per ExpenseKind — that's
// what JarDetailRow renders.

// Everything the selected-day rows need beyond the snapshot itself, built once
// per selection in MainWindow.
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

// One fund jar in the selected-day detail pane, rendered per its ExpenseKind
// (§3.III): a bill shows amount due + "can I pay it in full now" styling; a
// one-time goal shows its milestone and a relative due summary; the cushion
// shows its target. StatusKind drives pill colors in XAML: "FullyCovered"
// (strongest green) / "OnTrack" (green) / "Behind" (amber) / "Neutral".
public sealed class JarDetailRow
{
    // Which account this jar belongs to — the selected-day panes group by it so
    // each account's story stays together (grouped two-pane, planning/11).
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

    // The Q4 on-ramp (§3.III.a): a behind goal invites restructuring its plan.
    // Inert affordance for now — the restructure flow is future design work.
    public bool ShowAdjustNudge { get; init; }

    public static JarDetailRow From(FundJar jar, BalanceSnapshot snapshot, DayDetailContext context)
    {
        // A deallocation-day give-back is a negative isolated earmark; on a
        // normal day that same shape is a planned goal payout — so flag red only
        // when the day deallocated AND this jar's net isolated earmark is < 0.
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
                Jar = label, AmountText = saved.ToString("C"), StatusKind = "Neutral",
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
                Jar = label, AmountText = saved.ToString("C"), StatusKind = "Neutral",
                StatusText = string.Empty, SubText = string.Empty, Drained = drained, Floored = floored,
            },
        };
    }

    /// <summary>[CALC] The floored warning leads the sub line so it can't be missed.</summary>
    /// <param name="subText">The sub-line text to prepend the warning to.</param>
    /// <param name="floored">Whether a manual withdrawal exceeded this jar and got floored.</param>
    private static string WithFlooredWarning(string subText, bool floored) =>
        floored ? $"⚠ a manual withdrawal exceeded this jar — only what it held moved · {subText}" : subText;

    /// <summary>[CALC] §3.III.c — a regular bill: show the amount due; styling distinguishes "on track vs. the milestone" (green) from "could pay the whole bill right now" (strongest green). Precise due date, month as a word.</summary>
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
            (statusKind, statusText) = ("Behind", $"Behind {milestone - saved:C0}");
        }
        else
        {
            // Auto-reserved bills track their accrual target by construction;
            // explicitly-earmarked ones land here when at/above milestone.
            (statusKind, statusText) = ("OnTrack", "On track");
        }

        return new JarDetailRow
        {
            Jar = label,
            AmountText = saved.ToString("C"),
            StatusKind = statusKind,
            StatusText = statusText,
            SubText = WithFlooredWarning(
                nextDue == default
                    ? $"amount due {amountDue:C0} · no upcoming due date"
                    : $"amount due {amountDue:C0} · due {nextDue:MMMM d}",
                floored),
            ProgressFraction = amountDue > 0m ? Math.Min(1.0, (double)(saved / amountDue)) : 0.0,
            ShowProgress = true,
            Drained = drained,
            Floored = floored,
        };
    }

    /// <summary>[CALC] §3.III.a/b — a goal with a savings plan: progress toward the MILESTONE (am I on track setting money aside), not the full amount. One-time goals get a relative due summary when far out; repeating ones a precise date.</summary>
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
            AmountText = saved.ToString("C"),
            StatusKind = behind ? "Behind" : "OnTrack",
            StatusText = behind ? $"Behind {shortfall!.ShortfallAmount:C0}" : "On track",
            SubText = WithFlooredWarning($"milestone {milestone:C0} · goal {goalAmount:C0} · {dueText}", floored),
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
            (statusKind, statusText) = ("Behind", $"Below target by {target - saved:C0}");
        }

        return new JarDetailRow
        {
            Jar = "Safety cushion",
            AmountText = saved.ToString("C"),
            StatusKind = statusKind,
            StatusText = statusText,
            SubText = target > 0m ? $"target {target:C0}" : "set a cushion amount above to reserve a buffer",
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

// §3.III.a: a far-off goal reads as a summary ("~8 months away"); an actual
// formatted date (month as a word) only once it's close (within ~2 months).
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

// One event in the selected-day "What happened today" pane — every transaction
// and earmark event the day holds (§3.I), ordered so a paycheck sits directly
// above the allocations it funds (§3.III.d), with deallocation pulls last.
// ChipKind drives chip colors in XAML: "In" / "Out" / "Aside" / "Release" / "Pull".
// The grouping key for the selected-day panes: the account and its balance on the
// day being shown, plus how short it is and — when short — whether the gap is
// coverable from another account (the three-rung shortfall ladder, planning/22).
// Grouping by this record (records give value equality, so grouping still works)
// rather than a bare name lets the group header carry the account's own balance,
// the shortfall narrative, and the "move money in" lever — philosophy 1: a
// problem the app surfaces comes with a fix.
//
//   rung 2 — CanCoverElsewhere true:  the money exists, just in another account
//            (DonorName names it when one account alone covers the whole gap).
//            The lever is shown; a transfer can genuinely fix it.
//   rung 3 — CanCoverElsewhere false: no other account can cover it. No lever —
//            offering "move money in" when none can be would be a lie.
// IsThin: free funds are low but not yet negative (below one cushion's worth) —
// the milder free-funds warning shown beside "short" on the header. It also stands
// in for a dipped safety cushion: a cushion only drops below target when a
// deallocation drains it to avoid going short, which leaves free near zero, so a
// reserve dip always reads as thin anyway (folded in rather than shown apart).
public sealed record AccountGroupKey(
    int AccountId, string Name, decimal Balance, decimal Shortfall, bool CanCoverElsewhere = false, string? DonorName = null,
    bool IsThin = false)
{
    public bool IsShort => Shortfall > 0m;

    // How much money the account actually holds on the day, shown right after the
    // name — the header's own total, above the jar breakdown below it.
    public string BalanceText => Balance.ToString("C0");
    public bool BalanceNegative => Balance < 0m;

    // Rung 2 only: a transfer can actually close the gap, so the lever appears.
    public bool ShowCoverButton => IsShort && CanCoverElsewhere;

    // "Short $2,000" — the how-bad, shown in red to the right of the balance.
    public string ShortText => $"Short {Shortfall:C0}";

    // The lever's label, direction-explicit ("in" = into this account): the app
    // pre-fills only the destination and amount, so the user picks the source.
    public string MoveInText => $"Move {Shortfall:C0} in →";

    // Line two of the header: which rung, and where the money is (rung 2) or that
    // there is none to be had (rung 3). Empty when the account isn't short.
    public string RungText => !IsShort
        ? string.Empty
        : CanCoverElsewhere
            ? DonorName is { } donor
                ? $"In another account — the money's in {donor}."
                : "In another account — the money's in your other accounts."
            : "No other account can cover it.";
}

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

    // Allocations render indented under the day's income (§3.III.d "how much
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
                    AmountText = $"+{transaction.ExpectedAmount:C}", IsPositive = true,
                });
            }
            else
            {
                var mandatory = context.PatternsById.TryGetValue(transaction.FinanceId, out var pattern)
                    && pattern.Mandatory;
                expenses.Add(new DayEventRow
                {
                    Event = label, Chip = mandatory ? "BILL" : "EXPENSE", ChipKind = "Out",
                    AmountText = transaction.ExpectedAmount.ToString("C"),
                });
            }
        }

        foreach (var earmark in snapshot.EarMarkEvents)
        {
            var label = TimelineRow.JarLabel(earmark.FinanceId, context.JarLabels);
            if (!earmark.RepeatedEarmark && earmark.ExplicitAmount is { } explicitAmount && explicitAmount != 0m)
            {
                // The user's own adjustment — a nonzero
                // ExplicitAmount is what distinguishes it from system events.
                // On a deallocation day the merged give-back rides along in
                // ExpectedAmount; the jar rows + day chip tell that story.
                manual.Add(new DayEventRow
                {
                    Event = label, Chip = "MANUAL", ChipKind = "Manual",
                    AmountText = earmark.ExpectedAmount >= 0m
                        ? $"+{earmark.ExpectedAmount:C}"
                        : earmark.ExpectedAmount.ToString("C"),
                });
            }
            else if (earmark.RepeatedEarmark || earmark.ExpectedAmount >= 0m)
            {
                asides.Add(new DayEventRow
                {
                    Event = label, Chip = "SET ASIDE", ChipKind = "Aside",
                    AmountText = earmark.ExpectedAmount.ToString("C"),
                });
            }
            else if (snapshot.IsDeallocationDay)
            {
                pulls.Add(new DayEventRow
                {
                    Event = label, Chip = "PULLED", ChipKind = "Pull",
                    AmountText = earmark.ExpectedAmount.ToString("C"), IsPull = true,
                });
            }
            else
            {
                // A goal bought as planned: its jar releases the spent money.
                releases.Add(new DayEventRow
                {
                    Event = label, Chip = "RELEASED", ChipKind = "Release",
                    AmountText = earmark.ExpectedAmount.ToString("C"),
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

// One row of the Allocations tab's "Manual adjustments" grid.
public sealed class ManualEarmarkRow(ManualEarmark earmark, string target)
{
    public ManualEarmark Earmark { get; } = earmark;
    public DateOnly Date => Earmark.Date;
    public string Target { get; } = target;
    public decimal Amount => Earmark.Amount;
}

// Per-goal savings status, reused by the spreadsheet export's Goals sheet
// and the detail pane's Status column.
public sealed class GoalStatusRow(GoalShortfall status)
{
    public string Goal => status.Label;
    public DateOnly DueDate => status.DueDate;
    public decimal AmountNeeded => status.AmountNeeded;
    public decimal AmountAllocatedByDueDate => status.AmountAllocatedByDueDate;
    public string Status => FormatStatus(status);

    internal static string FormatStatus(GoalShortfall status) => status.ShortfallAmount > 0m
        ? $"Short by {status.ShortfallAmount:C}"
        : "On track";
}

// One row of the Accounts tab. Cushion is shown blank rather than "$0.00" when
// the user hasn't given the account one — philosophy 2: don't surface an
// internal zero as if it were a setting they chose.
public sealed class AccountRow(Account account)
{
    public Account Account { get; } = account;
    public string Name => Account.Name;
    public decimal Balance => Account.Balance;
    public string Cushion => Account.IdealSafetyCushion == 0m
        ? "—"
        : Account.IdealSafetyCushion.ToString("C");
}

// One row of the Transfers tab. From/To are account names (resolved by the
// caller); the two underlying patterns never appear here — a transfer reads as one
// thing.
public sealed class TransferRow(Transfer transfer, string fromName, string toName)
{
    public Transfer Transfer { get; } = transfer;
    public string From { get; } = fromName;
    public string To { get; } = toName;
    public decimal Amount => Transfer.Amount;
    public string Repeats => Transfer.DatePattern.ToRruleString();
}

// One entry in the overview's account filter (planning/11 §C.3). A null
// AccountId is the "All accounts" household roll-up.
public sealed record AccountFilterOption(int? AccountId, string Name);
