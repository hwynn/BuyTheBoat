namespace MyMoneyForecast.Domain;

// Input to TransactionLogBookFactory.CreateForecast. HorizonEndDate is a
// plain input rather than a constant baked into the engine — the UI defaults
// it to 3 months out but imposes no ceiling; the engine itself stays
// horizon-agnostic.
public sealed record ForecastOptions
{
    public required IReadOnlyList<FinancialPattern> FinancialPatterns { get; init; }
    public required IReadOnlyList<EarMarkPattern> EarMarkPatterns { get; init; }

    // User-created one-off jar adjustments. Not `required`: defaults to
    // none, so existing callers are unaffected.
    public IReadOnlyList<ManualEarmark> ManualEarmarks { get; init; } = [];
    public required decimal StartingBalance { get; init; }
    public required DateOnly AsOfDate { get; init; }
    public required DateOnly HorizonEndDate { get; init; }

    // How much the user wants held in the safety cushion (the finance_id = null
    // jar, always priority 0 / drained first). Occupies free funds so the
    // reported free figure reflects what's comfortable to spend, not every last
    // dollar. Not `required` — defaults to 0 (cushion inert), so existing
    // callers/tests are unaffected. Firm target: reserved in full even when the
    // balance can't cover it (free then goes negative, like an unaffordable
    // bill).
    public decimal IdealSafetyCushion { get; init; }

    // The finance ids of patterns that are one half of a transfer. Storage
    // knows this (a TransferId column); the domain FinancialPattern
    // deliberately does not carry it, so the caller passes the set in.
    //
    // Used for one thing: a transfer's withdrawal reserves in the account it
    // leaves, because per-account solvency is the point of accounts — but the
    // household view must not count it as set aside, since the household is
    // not down a cent. Empty means "no transfers", which is the correct
    // behaviour for every caller that doesn't have any.
    public IReadOnlySet<int> TransferWithdrawalFinanceIds { get; init; } = new HashSet<int>();

    // The per-account breakdown. When provided, the engine builds one
    // AccountTransactionPage per entry — each its own silo with its own
    // balance, cushion, and patterns — instead of a single "Primary" page.
    // When null/empty, the flat fields above drive one combined account
    // exactly as before, so the single-account API and its tests are
    // unaffected.
    public IReadOnlyList<AccountForecastInput>? Accounts { get; init; }
}

// One account's slice of a forecast: its own seed balance and cushion, and the
// patterns/earmarks/manual adjustments filed under it. A pattern belongs to an
// account by being in this list — the pattern itself still carries no account.
public sealed record AccountForecastInput
{
    public required int AccountId { get; init; }
    public required string Name { get; init; }
    public required decimal StartingBalance { get; init; }
    public decimal IdealSafetyCushion { get; init; }
    public required IReadOnlyList<FinancialPattern> FinancialPatterns { get; init; }
    public required IReadOnlyList<EarMarkPattern> EarMarkPatterns { get; init; }
    public IReadOnlyList<ManualEarmark> ManualEarmarks { get; init; } = [];
}
