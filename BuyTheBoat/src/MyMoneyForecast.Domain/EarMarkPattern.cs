namespace MyMoneyForecast.Domain;

// Recurring contributions toward a single goal. FinanceId is not this
// pattern's own identity — it's the FinanceId of the FinancialPattern (the
// goal) being saved for; that's the only link between them
// (redesign/01-glossary-of-terms.md#earmarkpattern).
public sealed record EarMarkPatternOptions
{
    public required int FinanceId { get; init; }
    public required RecurrenceRule DatePattern { get; init; }
    public required decimal Amount { get; init; }

    // How much is already sitting in this jar before any of DatePattern's own
    // occurrences run — e.g. an already-funded retirement goal shouldn't
    // forecast as starting from $0 just because it's new to this app. Not
    // `required`: every existing caller (OneTimeGoalFactory, tests) creates a
    // jar starting from scratch, so 0m is the right default rather than a
    // migration burden.
    public decimal StartingAllocation { get; init; }
}

public sealed class EarMarkPattern
{
    public int FinanceId { get; }
    public RecurrenceRule DatePattern { get; }
    public decimal Amount { get; }
    public decimal StartingAllocation { get; }

    private EarMarkPattern(EarMarkPatternOptions options)
    {
        FinanceId = options.FinanceId;
        DatePattern = options.DatePattern;
        Amount = options.Amount;
        StartingAllocation = options.StartingAllocation;
    }

    // `goal` is the FinancialPattern this earmark pattern is saving toward.
    //
    // Enforces only the back half of 3.11.2.a2 — an earmark can't still be
    // allocating funds after the goal's own date range ends. The front half
    // ("can't start before the goal's own start") turned out not to hold in
    // practice: saving in advance for a single-occurrence goal (the whole
    // point of an earmark pattern) means the earmark's Start is *supposed* to
    // be well before the goal's, since a one-time goal's own DatePattern is
    // just its single due date. Confirmed with the user (2026-07-07) that
    // earmark patterns aren't meant to be tied to a specific triggering
    // event's timing at all — this is the one piece of that coupling that
    // was left, and it didn't survive contact with the real use case.
    public static EarMarkPattern Create(EarMarkPatternOptions options, FinancialPattern goal)
    {
        if (options.FinanceId != goal.FinanceId)
        {
            throw new ArgumentException(
                $"FinanceId {options.FinanceId} does not match the goal pattern's FinanceId {goal.FinanceId}.",
                nameof(options));
        }

        if (options.DatePattern.Until > goal.DatePattern.Until)
        {
            throw new ArgumentException(
                "An earmark pattern can't still be allocating funds after the goal's own date range ends.",
                nameof(options));
        }

        if (options.StartingAllocation < 0m)
        {
            throw new ArgumentException("StartingAllocation can't be negative.", nameof(options));
        }

        return new EarMarkPattern(options);
    }
}
