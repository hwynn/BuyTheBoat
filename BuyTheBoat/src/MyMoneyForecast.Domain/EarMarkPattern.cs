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

    /// <summary>[CALC] Builds an EarMarkPattern from already-validated options.</summary>
    /// <param name="options">The plan's finance id, schedule, amount, and starting allocation.</param>
    private EarMarkPattern(EarMarkPatternOptions options)
    {
        FinanceId = options.FinanceId;
        DatePattern = options.DatePattern;
        Amount = options.Amount;
        StartingAllocation = options.StartingAllocation;
    }

    /// <summary>[CALC] Creates a savings plan pattern for a goal, validating that its active span stays within the goal's own — it can't allocate before the goal's active span starts, or after the goal's date range ends.</summary>
    /// <param name="options">The plan's finance id, schedule, amount, and starting allocation.</param>
    /// <param name="goal">The FinancialPattern this earmark pattern is saving toward.</param>
    public static EarMarkPattern Create(EarMarkPatternOptions options, FinancialPattern goal)
    {
        if (options.FinanceId != goal.FinanceId)
        {
            throw new ArgumentException(
                $"FinanceId {options.FinanceId} does not match the goal pattern's FinanceId {goal.FinanceId}.",
                nameof(options));
        }

        // DIVERGENCE(active-from): enforces 3.11.2.a2 in both directions,
        // against the goal's active span — an earmark can't still be
        // allocating after the goal's range ends (Until), and can't begin
        // before the goal's active span starts (its ActiveFrom lead-in if
        // set, else its Start).
        if (options.DatePattern.ActiveStart < goal.DatePattern.ActiveStart)
        {
            throw new ArgumentException(
                "An earmark pattern can't begin allocating before its goal's active span starts.",
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
