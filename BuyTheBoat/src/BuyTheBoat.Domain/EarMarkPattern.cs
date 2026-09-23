namespace BuyTheBoat.Domain;

// Recurring contributions toward a single goal. FinanceId is not this
// pattern's own identity — it's the FinanceId of the FinancialPattern (the
// goal) being saved for; that's the only link between them
// (planning/01-glossary-of-terms.md#earmarkpattern).
// Deliberately carries no account of its own.
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

    // Whether the user made this savings plan their OWN — opened its form and
    // saved it, or committed a full-save choice about it in a confirmation
    // popup — as opposed to it being an auto-generated default the user never
    // influenced. Drives "don't bother the user about an implicit change if it
    // doesn't alter anything they explicitly did" (see FinancePatternSaveConfirmation's
    // popup gate): a change touching only never-explicitly-created plans is
    // trivial and stays silent. Defaults TRUE on purpose — the safe direction
    // is to over-confirm, never to wrongly silence a warning; the few genuine
    // auto-mint sites (AllocationPlanProposer, OneTimeGoalFactory) set it false,
    // and maintenance rewrites preserve whatever the plan already had.
    public bool ExplicitlyCreated { get; init; } = true;
}

public sealed class EarMarkPattern
{
    public int FinanceId { get; }
    public RecurrenceRule DatePattern { get; }
    public decimal Amount { get; }
    public decimal StartingAllocation { get; }
    public bool ExplicitlyCreated { get; }

    /// <summary>[CALC] Builds an EarMarkPattern from already-validated options.</summary>
    /// <param name="options">The plan's finance id, schedule, amount, and starting allocation.</param>
    private EarMarkPattern(EarMarkPatternOptions options)
    {
        FinanceId = options.FinanceId;
        DatePattern = options.DatePattern;
        Amount = options.Amount;
        StartingAllocation = options.StartingAllocation;
        ExplicitlyCreated = options.ExplicitlyCreated;
    }

    /// <summary>[CALC] Returns this same plan tagged with whether the user explicitly made it their own — used at save sites to record that a form save or popup full-save "adopted" the plan, or that an implicit re-pace should keep the plan's existing standing. Copies the already-validated fields directly (no goal to re-check against), so it never re-runs Create's span validation.</summary>
    /// <param name="explicitlyCreated">True to mark the plan the user's own; false to mark it an untouched auto default.</param>
    public EarMarkPattern WithExplicitlyCreated(bool explicitlyCreated) =>
        ExplicitlyCreated == explicitlyCreated
            ? this
            : new EarMarkPattern(new EarMarkPatternOptions
            {
                FinanceId = FinanceId,
                DatePattern = DatePattern,
                Amount = Amount,
                StartingAllocation = StartingAllocation,
                ExplicitlyCreated = explicitlyCreated,
            });

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
                $"An earmark pattern can't begin allocating before its goal's active span starts ({goal.DatePattern.ActiveStart:MMM d, yyyy}).",
                nameof(options));
        }

        if (options.DatePattern.Until > goal.DatePattern.Until)
        {
            throw new ArgumentException(
                $"An earmark pattern can't still be allocating funds after the goal's own date range ends ({goal.DatePattern.Until:MMM d, yyyy}).",
                nameof(options));
        }

        if (options.StartingAllocation < 0m)
        {
            throw new ArgumentException("StartingAllocation can't be negative.", nameof(options));
        }

        return new EarMarkPattern(options);
    }

    /// <summary>[CALC] Whether two earmark patterns under one goal can be folded into one with no change anyone would notice (the silent join) — same Amount, and recurrence shapes that merge to exactly the union of their occurrences (RecurrenceRule.CanMergeWith). A true result means JoinedWith yields an identical contribution schedule, so there's no user choice worth asking about; a false one is a genuine difference, left for a with-consequence consolidation to resolve. StartingAllocation is not part of the test — the join simply sums the two jars.</summary>
    /// <param name="a">One earmark pattern.</param>
    /// <param name="b">The other earmark pattern — expected to already share a's FinanceId.</param>
    public static bool CanJoinWithoutConsequence(EarMarkPattern a, EarMarkPattern b) =>
        a.Amount == b.Amount && a.DatePattern.CanMergeWith(b.DatePattern);

    /// <summary>[CALC] Folds this earmark pattern and another under the same goal into one covering the union of their spans — same Amount and recurrence shape, its jar the sum of the two StartingAllocations. Call only when CanJoinWithoutConsequence(this, other) holds (it's re-checked, and rejected otherwise): the merged schedule is then identical to running the two separately. Validated against the goal, same as Create.</summary>
    /// <param name="other">The earmark pattern to join with — must share this one's FinanceId and Amount and have a mergeable shape.</param>
    /// <param name="goal">The FinancialPattern both earmark patterns fund — validated against.</param>
    public EarMarkPattern JoinedWith(EarMarkPattern other, FinancialPattern goal)
    {
        if (other.FinanceId != FinanceId)
        {
            throw new ArgumentException(
                $"Can't join earmark patterns under different goals ({FinanceId} vs {other.FinanceId}).",
                nameof(other));
        }

        if (!CanJoinWithoutConsequence(this, other))
        {
            throw new ArgumentException(
                "These two earmark patterns can't be joined without consequence — they need the same Amount and a mergeable shape.",
                nameof(other));
        }

        var mergedStart = DatePattern.ActiveStart < other.DatePattern.ActiveStart ? DatePattern.ActiveStart : other.DatePattern.ActiveStart;
        var mergedUntil = DatePattern.Until > other.DatePattern.Until ? DatePattern.Until : other.DatePattern.Until;

        return Create(
            new EarMarkPatternOptions
            {
                FinanceId = FinanceId,
                DatePattern = DatePattern.ReanchoredToStartOn(mergedStart).WithUntil(mergedUntil),
                Amount = Amount,
                StartingAllocation = StartingAllocation + other.StartingAllocation,
                // Explicit if either side was — merging keeps the user's stake in
                // whichever plan they had already made their own.
                ExplicitlyCreated = ExplicitlyCreated || other.ExplicitlyCreated,
            },
            goal);
    }
}
