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
}
