namespace BuyTheBoat.Domain;

// A user-created one-off allocation adjustment: on a chosen day, move money
// into a fund jar (positive) or back out to the free balance (negative), on
// top of whatever the jar's EarMarkPattern does automatically. At most one
// per finance id per day (3.13.8.1.a1 — the persistence key enforces the
// merge rule).
public sealed record ManualEarmarkOptions
{
    public required int FinanceId { get; init; }
    public required DateOnly Date { get; init; }

    // Positive moves money into the jar; negative pulls it back to free.
    public required decimal Amount { get; init; }
}

public sealed class ManualEarmark
{
    public int FinanceId { get; }
    public DateOnly Date { get; }
    public decimal Amount { get; }

    /// <summary>[CALC] Builds a ManualEarmark from already-validated options.</summary>
    /// <param name="options">The earmark's finance id, date, and amount.</param>
    private ManualEarmark(ManualEarmarkOptions options)
    {
        FinanceId = options.FinanceId;
        Date = options.Date;
        Amount = options.Amount;
    }

    /// <summary>[CALC] Creates a manual earmark, validating that its date falls within its jar's own lifetime. Doesn't check the jar's balance — a withdrawal that would overdraw the jar is the UI's job to catch, since that depends on the live forecast, not anything this constructor can see.</summary>
    /// <param name="options">The earmark's finance id, date, and amount.</param>
    /// <param name="pattern">The jar's EarMarkPattern — its date span is the jar's lifetime, so a manual earmark can only land on a day the jar exists.</param>
    public static ManualEarmark Create(ManualEarmarkOptions options, EarMarkPattern pattern)
    {
        if (options.FinanceId != pattern.FinanceId)
        {
            throw new ArgumentException(
                $"FinanceId {options.FinanceId} does not match the earmark pattern's FinanceId {pattern.FinanceId}.",
                nameof(options));
        }

        if (options.Amount == 0m)
        {
            throw new ArgumentException("Amount cannot be zero.", nameof(options));
        }

        // ActiveSpanContains (ActiveStart..Until), not the narrower Start..Until:
        // ActiveStart already exists precisely so a jar can be considered alive
        // — and so a manual earmark can land on it — before the pattern's own
        // first occurrence (e.g. a lead-in set by AllocationPlanProposer's own
        // asOfDate handling). Identical behavior to before whenever ActiveFrom
        // is unset (ActiveStart falls back to Start in that case) — this only
        // widens what validates, never narrows it.
        if (!pattern.DatePattern.ActiveSpanContains(options.Date))
        {
            throw new ArgumentException(
                "A manual earmark must fall within its earmark pattern's date span — the pattern's timeline is the fund jar's lifetime.",
                nameof(options));
        }

        return new ManualEarmark(options);
    }
}
