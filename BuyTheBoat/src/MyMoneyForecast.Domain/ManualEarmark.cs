namespace MyMoneyForecast.Domain;

// A user-created one-off allocation adjustment: on a chosen day, move money
// into a fund jar (positive) or back out to the free balance (negative), on
// top of whatever the jar's EarMarkPattern does automatically. The documented
// isolated-earmark machinery (planning/09-manual-earmarks.md): the amount is
// the user's explicitly-given number (8.4.a2 / 3.13.8.5.a1), at most one per
// finance id per day (3.13.8.1.a1 — the persistence key enforces the merge
// rule), and it becomes an isolated EarMarkEvent with ExplicitAmount set.
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

    private ManualEarmark(ManualEarmarkOptions options)
    {
        FinanceId = options.FinanceId;
        Date = options.Date;
        Amount = options.Amount;
    }

    // `pattern` is the jar's EarMarkPattern: its date span IS the jar's
    // lifetime (author ruling, 3.13.8.a1/a2), so a manual earmark can only
    // land on a day the jar exists. Balance-dependent checks (a withdrawal
    // can't exceed the jar's balance that day) are the UI's job — they depend
    // on the currently-computed forecast, not on anything stable enough to
    // enforce here.
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

        if (options.Date < pattern.DatePattern.Start || options.Date > pattern.DatePattern.Until)
        {
            throw new ArgumentException(
                "A manual earmark must fall within its earmark pattern's date span — the pattern's timeline is the fund jar's lifetime.",
                nameof(options));
        }

        return new ManualEarmark(options);
    }
}
