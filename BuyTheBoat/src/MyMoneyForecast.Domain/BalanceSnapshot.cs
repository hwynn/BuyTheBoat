namespace MyMoneyForecast.Domain;

// Everything known about one specific day, for one account. Per class
// documentation.ods (Properties sheet, purpose statement): "This holds all
// transactions and earmarks occuring on a specific day. This shows how much
// money we have on a specific day. This holds all the fund jars, showing how
// much money we have set aside for various goals at this date."
//
// One snapshot exists per date that has at least one event (the
// adjust_snapshots rule, 3.13.a3), plus exactly one dateless initial
// snapshot per page. This is the per-day answer container for the project's
// core questions: ExpectedAmount (how much money will I have),
// ExpectedFreeAmount (how much of it is actually free — Q1), and FundJars
// (what's allocated to what, and — against MilestoneAmount — whether each
// goal is on track: Q2/Q3).
public sealed record BalanceSnapshot
{
    // null = this is the page's initial snapshot: the seed the cascade
    // starts from, kept OUTSIDE BalanceRecord, carrying no events — only
    // amounts and jars ("One and only one balance snapshot in a time period
    // has no snapshot date").
    public required DateOnly? SnapshotDate { get; init; }

    // 10.2: the real balance after the day's actual transactions — "the
    // first value a cascade sets." Computable only from ActualTransactions,
    // so per the documented rule it is null for every future date; the
    // initial snapshot's value is the user-entered balance.
    public required decimal? FullAmount { get; init; }

    // 10.3: the previous snapshot's amount plus today's expected
    // transactions.
    public required decimal? ExpectedAmount { get; init; }

    // 3.13.4.a1: ExpectedAmount minus the sum of every jar's ExpectedAmount.
    // This is the day's "free to spend" answer.
    public required decimal? ExpectedFreeAmount { get; init; }

    // Always contains exactly one FinanceId=null jar (9.5.a1 — the safety
    // cushion) and otherwise unique finance ids.
    public required IReadOnlyList<FundJar> FundJars { get; init; }

    // Always empty until actual-transaction import exists.
    // ASSUMED-PAIRING(import): the engine currently acts as if these existed
    // and matched ExpectedTransactions exactly.
    public required IReadOnlyList<ActualTransaction> ActualTransactions { get; init; }

    public required IReadOnlyList<ExpectedTransaction> ExpectedTransactions { get; init; }

    public required IReadOnlyList<EarMarkEvent> EarMarkEvents { get; init; }

    // True when this day's spending overdrew free funds and jars had to be
    // drained to cover it (DeallocationCalculator.IsDeallocationDay returned
    // true). Lets display layers distinguish a deallocation raid — a negative
    // isolated earmark here means "pulled out to cover a shortfall" — from a
    // planned goal payout on a normal day (also a negative isolated earmark).
    // Not `required`: the dateless initial snapshot is never a deallocation day.
    public bool IsDeallocationDay { get; init; }
}
