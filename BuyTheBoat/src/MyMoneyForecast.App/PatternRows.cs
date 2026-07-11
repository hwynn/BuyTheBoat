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

// One row of the unified timeline — a BalanceSnapshot rendered under its
// display date (the dateless initial snapshot renders as the as-of row).
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

// One fund jar in the selected-day detail pane.
public sealed class JarDetailRow
{
    public required string Jar { get; init; }
    public required decimal Saved { get; init; }
    public required string ShouldHaveSaved { get; init; }
    public required string DueDate { get; init; }
    public required string Status { get; init; }

    // True when this jar was raided on a deallocation day (drives the red row).
    public bool Drained { get; init; }

    public static JarDetailRow From(
        FundJar jar,
        BalanceSnapshot snapshot,
        IReadOnlyDictionary<int, string> jarLabels,
        IReadOnlyDictionary<int, GoalShortfall> shortfallsByFinanceId,
        decimal cushionTarget)
    {
        // A deallocation-day give-back is a negative isolated earmark; on a
        // normal day that same shape is a planned goal payout — so flag red only
        // when the day deallocated AND this jar's net isolated earmark is < 0.
        var drained = snapshot.IsDeallocationDay
            && NetIsolatedEarmark(snapshot, jar.FinanceId) < 0m;

        if (jar.FinanceId is not { } financeId)
        {
            return new JarDetailRow
            {
                Jar = "Safety cushion",
                Saved = jar.ExpectedAmount,
                ShouldHaveSaved = cushionTarget > 0m ? cushionTarget.ToString("C") : "—",
                DueDate = "—",
                Status = CushionStatus(jar.ExpectedAmount, cushionTarget),
                Drained = drained,
            };
        }

        if (shortfallsByFinanceId.TryGetValue(financeId, out var shortfall))
        {
            return new JarDetailRow
            {
                Jar = TimelineRow.JarLabel(financeId, jarLabels),
                Saved = jar.ExpectedAmount,
                ShouldHaveSaved = jar.MilestoneAmount is { } milestone ? milestone.ToString("C") : "—",
                DueDate = shortfall.DueDate.ToString(),
                Status = GoalStatusRow.FormatStatus(shortfall),
                Drained = drained,
            };
        }

        // No savings plan drives this jar — it's a mandatory bill's
        // automatic reservation.
        return new JarDetailRow
        {
            Jar = $"{TimelineRow.JarLabel(financeId, jarLabels)} (Automatic)",
            Saved = jar.ExpectedAmount,
            ShouldHaveSaved = "—",
            DueDate = "—",
            Status = "Automatic bill reserve",
            Drained = drained,
        };
    }

    // Sum of this finance id's isolated (non-repeated) earmark amounts on the
    // day — negative once a deallocation give-back outweighs any scheduled fill.
    private static decimal NetIsolatedEarmark(BalanceSnapshot snapshot, int? financeId) =>
        snapshot.EarMarkEvents
            .Where(earmark => !earmark.RepeatedEarmark && earmark.FinanceId == financeId)
            .Sum(earmark => earmark.ExpectedAmount);

    private static string CushionStatus(decimal saved, decimal target)
    {
        if (target <= 0m)
        {
            return "Not set";
        }

        return saved >= target ? "Funded" : $"Below target by {(target - saved):C}";
    }
}

// One event (expected transaction or earmark event) in the selected-day
// detail pane — everything the day holds, including the system-generated
// implicit events the at-a-glance Events column leaves out.
public sealed class DayEventRow
{
    public required string Event { get; init; }
    public required string Kind { get; init; }
    public required decimal Amount { get; init; }

    public static List<DayEventRow> From(BalanceSnapshot snapshot, IReadOnlyDictionary<int, string> jarLabels)
    {
        var rows = new List<DayEventRow>();

        foreach (var transaction in snapshot.ExpectedTransactions)
        {
            rows.Add(new DayEventRow
            {
                Event = TimelineRow.JarLabel(transaction.FinanceId, jarLabels),
                Kind = transaction.ExpectedAmount >= 0 ? "Expected income" : "Expected expense",
                Amount = transaction.ExpectedAmount,
            });
        }

        foreach (var earmarkEvent in snapshot.EarMarkEvents)
        {
            rows.Add(new DayEventRow
            {
                Event = TimelineRow.JarLabel(earmarkEvent.FinanceId, jarLabels),
                Kind = earmarkEvent.RepeatedEarmark
                    ? "Planned allocation"
                    : earmarkEvent.ExpectedAmount >= 0 ? "Automatic allocation"
                    : snapshot.IsDeallocationDay ? "Deallocation" : "Automatic release",
                Amount = earmarkEvent.ExpectedAmount,
            });
        }

        return rows;
    }
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
