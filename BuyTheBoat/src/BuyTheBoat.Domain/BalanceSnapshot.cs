namespace BuyTheBoat.Domain;

/// <summary>
/// Everything known about one day for one account: "This holds all transactions and earmarks occuring on a specific day. This shows how much money we have on a specific day. This holds all the fund jars, showing how much money we have set aside for various goals at this date." (class documentation.ods)
/// There's one snapshot per date with at least one event (adjust_snapshots, 3.13.a3), plus one dateless initial snapshot per page.
/// It answers the core questions for that day: how much money there will be (ExpectedAmount), how much is free (ExpectedFreeAmount), and what's set aside for what and whether each goal is on track (FundJars).
/// </summary>
public sealed record BalanceSnapshot
{
    /// <summary>
    /// Null for the page's initial snapshot, which the cascade starts from; it's kept outside BalanceRecord and has amounts and jars but no events.
    /// "One and only one balance snapshot in a time period has no snapshot date."
    /// </summary>
    public required DateOnly? SnapshotDate { get; init; }

    /// <summary>
    /// 10.2: the real balance after the day's actual transactions, "the first value a cascade sets."
    /// Only actual transactions can tell it, so it's null for every future date; the initial snapshot holds the user-entered balance.
    /// </summary>
    public required decimal? FullAmount { get; init; }

    /// <summary>10.3: the previous snapshot's amount plus today's expected transactions.</summary>
    public required decimal? ExpectedAmount { get; init; }

    /// <summary>
    /// 3.13.4.a1: the day's "free to spend" answer, ExpectedAmount minus every jar's ExpectedAmount.
    /// Also minus the page's CurrentUnpaidExpected, as in 1.2.3.5.a1, since bills still owed from before the as-of date aren't free money.
    /// </summary>
    public required decimal? ExpectedFreeAmount { get; init; }

    /// <summary>
    /// DIVERGENCE(expected-unpaid), 3.13.4.a2: how much the user would owe on this day.
    /// Every bill is assumed paid in full, so a bill the balance can't cover drives ExpectedAmount below zero; that shortfall, plus any CurrentUnpaidExpected not yet paid off, is what's owed.
    /// Incoming money pays this down before anything else, so it is max(0, CurrentUnpaidExpected - ExpectedAmount).
    /// </summary>
    public decimal ExpectedUnpaidAmount { get; init; }

    /// <summary>Always exactly one safety cushion jar with a null FinanceId (9.5.a1); every other finance id appears once.</summary>
    public required IReadOnlyList<FundJar> FundJars { get; init; }

    /// <summary>
    /// Always empty until actual-transaction import exists.
    /// ASSUMED-PAIRING(import): the forecast acts as if these existed and matched ExpectedTransactions exactly.
    /// </summary>
    public required IReadOnlyList<ActualTransaction> ActualTransactions { get; init; }

    public required IReadOnlyList<ExpectedTransaction> ExpectedTransactions { get; init; }

    public required IReadOnlyList<EarMarkEvent> EarMarkEvents { get; init; }

    /// <summary>
    /// Did this day's spending overdraw free funds, so jars had to be drained to cover it (DeallocationCalculator.IsDeallocationDay)?
    /// Display code uses it to tell a negative isolated earmark that covered a shortfall apart from a planned goal payout.
    /// </summary>
    public bool IsDeallocationDay { get; init; }
}
