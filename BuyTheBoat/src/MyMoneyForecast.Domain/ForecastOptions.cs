namespace MyMoneyForecast.Domain;

// Input to TransactionLogBookFactory.CreateForecast. HorizonEndDate is a
// plain input rather than a constant baked into the engine — the UI defaults
// it to 3 months out but imposes no ceiling; the engine itself stays
// horizon-agnostic.
public sealed record ForecastOptions
{
    public required IReadOnlyList<FinancialPattern> FinancialPatterns { get; init; }
    public required IReadOnlyList<EarMarkPattern> EarMarkPatterns { get; init; }
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
}
