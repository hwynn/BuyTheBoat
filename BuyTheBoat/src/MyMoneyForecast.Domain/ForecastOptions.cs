namespace MyMoneyForecast.Domain;

// Input to TransactionLogBookFactory.CreateForecast. HorizonEndDate is a
// plain input rather than a constant baked into the engine — the UI defaults
// it to 3 months out but imposes no ceiling; the engine itself stays
// horizon-agnostic.
public sealed record ForecastOptions
{
    public required IReadOnlyList<FinancialPattern> FinancialPatterns { get; init; }
    public required IReadOnlyList<EarMarkPattern> EarMarkPatterns { get; init; }

    // User-created one-off jar adjustments (planning/09-manual-earmarks.md).
    // Not `required`: defaults to none, so existing callers are unaffected.
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

    // The per-account breakdown (planning/10 item 4). When provided, the engine
    // builds one AccountTransactionPage per entry — each its own silo with its
    // own balance, cushion, and patterns — instead of a single "Primary" page.
    // When null/empty, the flat fields above drive one combined account exactly
    // as before, so the single-account API and its tests are unaffected.
    public IReadOnlyList<AccountForecastInput>? Accounts { get; init; }
}

// One account's slice of a forecast: its own seed balance and cushion, and the
// patterns/earmarks/manual adjustments filed under it. A pattern belongs to an
// account by being in this list — the pattern itself still carries no account
// (planning/10 item 2-A); the caller does the filing.
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
