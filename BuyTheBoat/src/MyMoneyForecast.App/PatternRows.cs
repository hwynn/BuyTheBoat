using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// Flattened, display-only shapes for the grids in MainWindow — kept out of
// MyMoneyForecast.Domain deliberately, since "how a goal's description should
// read in a list" is a UI concern, not a domain one.
public sealed class FinancialPatternRow(FinancialPattern pattern)
{
    public FinancialPattern Pattern { get; } = pattern;
    public int FinanceId => Pattern.FinanceId;
    public string Source => Pattern.Source;
    public string? Description => Pattern.Description;
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

// A synthesized stand-in for a mandatory bill's automatic reservation
// (TransactionLogBookFactory's auto-bill earmarking) — deliberately not
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

    // Zero-balance jars (a goal not yet started, a bill just paid, the
    // still-empty safety cushion) are filtered out — otherwise most days
    // would read as a wall of "$0.00" entries, exactly the noise this
    // column exists to cut through.
    internal static string FormatReserved(BalanceSnapshot snapshot, IReadOnlyDictionary<int, string> jarLabels) =>
        string.Join(", ", snapshot.FundJars
            .Where(jar => jar.ExpectedAmount > 0m)
            .Select(jar => $"{JarLabel(jar.FinanceId, jarLabels)} {jar.ExpectedAmount:C}"));

    // The at-a-glance column shows the day's scheduled events (expected
    // transactions and planned allocation installments) — the same set the
    // pre-restructure grid showed. System-generated implicit events (bill
    // auto-reserve steps, goal releases) appear in the selected-day detail
    // pane instead, where there's room to label what they are.
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

// ===== The calendar overview (planning/08-forecast-tab-design-philosophy.md §2) =====
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

// One calendar day cell. Everything a trigger reads is precomputed at
// construction so recycled containers rebind cheaply during scroll.
//
// Deliberate deviation from this file's otherwise-immutable rows: selection
// changes AFTER creation (clicking day B must un-highlight day A), and the
// cell template's DataTrigger needs a change notification for that — so this
// one class implements INotifyPropertyChanged, with IsSelected as its single
// settable property.
public sealed class DayCellRow : System.ComponentModel.INotifyPropertyChanged
{
    public DateOnly? Date { get; }
    public BalanceSnapshot? Snapshot { get; }

    public bool IsPadding => Date is null;
    public string DayNumber { get; }
    public bool HasSnapshot => Snapshot is not null;

    public string FreeText { get; }
    public bool FreeNegative { get; }
    public string AllocatedText { get; }

    public bool HasIncome { get; }
    public bool HasExpense { get; }
    public bool HasAllocation { get; }

    public bool IsToday { get; }
    public bool NeedsAttention { get; }

    public string AutomationId { get; }
    public string AutomationName { get; }

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

    public static DayCellRow Padding() => new(null, null);

    // `flagged` marks a day worth attention for reasons the snapshot itself
    // doesn't carry — today: a floored manual earmark (planning/09).
    public DayCellRow(DateOnly? date, BalanceSnapshot? snapshot, bool flagged = false)
    {
        Date = date;
        Snapshot = snapshot;
        DayNumber = date is { } d ? d.Day.ToString() : string.Empty;
        IsToday = date == DateOnly.FromDateTime(DateTime.Today);
        AutomationId = date is { } id ? $"Day_{id:yyyy-MM-dd}" : string.Empty;

        if (snapshot is null)
        {
            FreeText = string.Empty;
            AllocatedText = string.Empty;
            AutomationName = date?.ToString("MMMM d, yyyy") ?? string.Empty;
            return;
        }

        var free = snapshot.ExpectedFreeAmount ?? 0m;
        FreeText = free.ToString("C0");
        FreeNegative = free < 0m;

        var allocated = snapshot.FundJars.Sum(jar => jar.ExpectedAmount);
        AllocatedText = allocated > 0m ? $"{allocated:C0} set aside" : string.Empty;

        HasIncome = snapshot.ExpectedTransactions.Any(t => !t.Cancelled && t.ExpectedAmount > 0m);
        HasExpense = snapshot.ExpectedTransactions.Any(t => !t.Cancelled && t.ExpectedAmount < 0m);
        HasAllocation = snapshot.EarMarkEvents.Any(e => e.RepeatedEarmark || e.ExpectedAmount > 0m);

        NeedsAttention = snapshot.IsDeallocationDay || FreeNegative || flagged;
        AutomationName = $"{date:MMMM d, yyyy} — free {FreeText}";
    }
}

// ===== The selected-day region (planning/08-forecast-tab-design-philosophy.md §3) =====
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
    // the jar held actually moved) — flagged in place per planning/09.
    public IReadOnlySet<int> FlooredFinanceIds { get; init; } = new HashSet<int>();
}

// One fund jar in the selected-day detail pane, rendered per its ExpenseKind
// (§3.III): a bill shows amount due + "can I pay it in full now" styling; a
// one-time goal shows its milestone and a relative due summary; the cushion
// shows its target. StatusKind drives pill colors in XAML: "FullyCovered"
// (strongest green) / "OnTrack" (green) / "Behind" (amber) / "Neutral".
public sealed class JarDetailRow
{
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
    // got floored (planning/09) — drives the amber warning on the sub line.
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

    // The floored warning leads the sub line so it can't be missed.
    private static string WithFlooredWarning(string subText, bool floored) =>
        floored ? $"⚠ a manual withdrawal exceeded this jar — only what it held moved · {subText}" : subText;

    // §3.III.c — a regular bill: show the amount due; styling distinguishes
    // "on track vs. the milestone" (green) from "could pay the whole bill right
    // now" (strongest green). Precise due date, month as a word.
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

    // §3.III.a/b — a goal with a savings plan: progress toward the MILESTONE
    // (am I on track setting money aside), not the full amount. One-time goals
    // get a relative due summary when far out; repeating ones a precise date.
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

    // Sum of this finance id's isolated (non-repeated) earmark amounts on the
    // day — negative once a deallocation give-back outweighs any scheduled fill.
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
public sealed class DayEventRow
{
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
                // The user's own adjustment (planning/09) — a nonzero
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
