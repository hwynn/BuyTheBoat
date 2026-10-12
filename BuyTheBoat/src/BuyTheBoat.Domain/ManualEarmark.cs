namespace BuyTheBoat.Domain;

/// <summary>The fields for building a ManualEarmark with ManualEarmark.Create.</summary>
public sealed record ManualEarmarkOptions
{
    public required int FinanceId { get; init; }
    public required DateOnly Date { get; init; }

    /// <summary>Positive moves money into the jar; negative moves it back to free funds.</summary>
    public required decimal Amount { get; init; }
}

/// <summary>
/// A one-off adjustment the user makes: on a chosen day, moves money into a fund jar or back out to free funds, on top of what the jar's savings plan does.
/// At most one per finance id per day (3.13.8.1.a1); the saved data's key enforces this.
/// </summary>
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

    /// <summary>
    /// [CALC] Creates a manual earmark, checking that its date falls within its jar's lifetime.
    /// It doesn't check the jar's balance; catching a withdrawal that would overdraw the jar is the UI's job, since that needs the forecast.
    /// </summary>
    /// <param name="options">The earmark's finance id, date, and amount.</param>
    /// <param name="pattern">The jar's EarMarkPattern — its date span is the jar's lifetime, so a manual earmark can only land on a day the jar exists.</param>
    public static ManualEarmark Create(ManualEarmarkOptions options, EarMarkPattern pattern)
    {
        if (options.FinanceId != pattern.FinanceId)
        {
            throw new ManualEarmarkPatternMismatchException(
                $"FinanceId {options.FinanceId} does not match the earmark pattern's FinanceId {pattern.FinanceId}.",
                nameof(options));
        }

        if (options.Amount == 0m)
        {
            throw new ZeroIsolatedEarmarkException("Amount cannot be zero.", nameof(options));
        }

        // Checks the active span, not just the occurrences, since a jar exists from its ActiveFrom lead-in, before the first occurrence.
        if (!pattern.DatePattern.ActiveSpanContains(options.Date))
        {
            throw new ManualEarmarkOutsidePatternSpanException(
                "A manual earmark must fall within its earmark pattern's date span — the pattern's timeline is the fund jar's lifetime.",
                nameof(options));
        }

        return new ManualEarmark(options);
    }
}
