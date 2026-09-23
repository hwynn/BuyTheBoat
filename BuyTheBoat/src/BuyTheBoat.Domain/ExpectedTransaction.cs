namespace BuyTheBoat.Domain;

// A single, discrete anticipated money event on a specific date — one
// occurrence of a FinancialPattern (a bill's due date, a payday). Per the
// original model (class documentation.ods, Properties sheet): "An expected
// transaction is a singular, discrete event in a transaction log page. It is
// normally created after the current date... If it has a finance_id, this is
// a repeated expected transaction (even if it only happens once)."
//
// The engine reasons in terms of these instead of raw occurrence counts —
// they are what the user's intentions actually generate, and what real
// bank data will eventually be paired against.
public sealed record ExpectedTransaction
{
    // Which FinancialPattern generated this. Even a one-off goal has one —
    // "so if a user manually creates a new expected transaction, a new
    // financial pattern will have to be created first by the system."
    public required int FinanceId { get; init; }

    public required DateOnly ExpectedDate { get; init; }

    public required decimal ExpectedAmount { get; init; }

    // "if true, we ignore this expected transaction." Nothing sets this yet;
    // it exists so cancellation has a home when editing individual
    // occurrences becomes a feature.
    public bool Cancelled { get; init; }

    // Pairing fields — present but dormant.
    // ASSUMED-PAIRING(fulfillment): with no ActualTransactions to pair
    // against, every expected transaction is treated as if it will be
    // fulfilled exactly as written — these stay null and all downstream math
    // proceeds as though pairing succeeded. When actual-transaction import
    // becomes real, pairing logic belongs here and on AccountTransactionPage
    // (pair_actual_event / unpair_actual_event / ScanForMatchs in the
    // documented model).
    public decimal? PairedAmount { get; init; }
    public DateOnly? PairedActualDate { get; init; }

    // Matching wiggle room ([below, above]), defaults [0,0] per the docs.
    // Only meaningful once pairing against real transactions exists.
    public (decimal Below, decimal Above) AmountTolerance { get; init; } = (0m, 0m);
    public (decimal Below, decimal Above) DateTolerance { get; init; } = (0m, 0m);
}
