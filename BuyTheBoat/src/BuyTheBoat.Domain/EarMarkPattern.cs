namespace BuyTheBoat.Domain;

/// <summary>The fields for building an EarMarkPattern with EarMarkPattern.Create.</summary>
public sealed record EarMarkPatternOptions
{
    public required int FinanceId { get; init; }
    public required RecurrenceRule DatePattern { get; init; }
    public required decimal Amount { get; init; }

    /// <summary>
    /// How much is already in the jar before any of DatePattern's occurrences, so an already-funded goal doesn't start from $0 just because it's new to the app.
    /// Defaults to 0.
    /// </summary>
    public decimal StartingAllocation { get; init; }

    /// <summary>
    /// Did the user make this savings plan their own, by saving its form or choosing a full save for it in a confirmation popup, rather than leave an automatic default untouched?
    /// FinancePatternSaveConfirmation stays silent about an automatic change that only touches plans the user never made their own.
    /// Defaults to true, since asking too often is safer than wrongly staying silent; AllocationPlanProposer and OneTimeGoalFactory set it false, and other rewrites keep the plan's existing value.
    /// </summary>
    public bool ExplicitlyCreated { get; init; } = true;
}

/// <summary>
/// Recurring contributions toward a single goal (planning/01-glossary-of-terms.md#earmarkpattern).
/// FinanceId is the goal's FinanceId, not the plan's own identity; it's the only link between them, and the plan has no account of its own.
/// </summary>
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

    /// <summary>
    /// [CALC] Returns this plan marked with whether the user made it their own, for save code recording a form save or popup full save, or an automatic re-pace keeping the existing value.
    /// It copies the already-validated fields, so Create's span checks don't run again.
    /// </summary>
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
            throw new EarMarkPatternGoalMismatchException(
                $"FinanceId {options.FinanceId} does not match the goal pattern's FinanceId {goal.FinanceId}.",
                nameof(options));
        }

        // DIVERGENCE(active-from): enforces 3.11.2.a2 against the goal's active span, so a plan can't begin before the goal's ActiveStart or run past its Until.
        if (options.DatePattern.ActiveStart < goal.DatePattern.ActiveStart)
        {
            throw new EarMarkPatternOutsideGoalSpanException(
                $"An earmark pattern can't begin allocating before its goal's active span starts ({goal.DatePattern.ActiveStart:MMM d, yyyy}).",
                nameof(options));
        }

        if (options.DatePattern.Until > goal.DatePattern.Until)
        {
            throw new EarMarkPatternOutsideGoalSpanException(
                $"An earmark pattern can't still be allocating funds after the goal's own date range ends ({goal.DatePattern.Until:MMM d, yyyy}).",
                nameof(options));
        }

        if (options.StartingAllocation < 0m)
        {
            throw new ArgumentException("StartingAllocation can't be negative.", nameof(options));
        }

        return new EarMarkPattern(options);
    }

    /// <summary>
    /// [CALC] Whether two plans under one goal can be folded into one with no change anyone would notice (the silent join): same Amount, and schedules that merge to exactly the union of their occurrences (RecurrenceRule.CanMergeWith).
    /// When true, JoinedWith gives an identical contribution schedule, so there's nothing to ask the user; when false, a consolidation with consequences handles it.
    /// StartingAllocation isn't compared, since the join adds the two together.
    /// </summary>
    /// <param name="a">One earmark pattern.</param>
    /// <param name="b">The other earmark pattern — expected to already share a's FinanceId.</param>
    public static bool CanJoinWithoutConsequence(EarMarkPattern a, EarMarkPattern b) =>
        a.Amount == b.Amount && a.DatePattern.CanMergeWith(b.DatePattern);

    /// <summary>
    /// [CALC] Folds this plan and another under the same goal into one covering both spans, with the same Amount and shape and the two StartingAllocations added together.
    /// Throws unless CanJoinWithoutConsequence(this, other) holds, so the merged schedule matches running the two separately; it's checked against the goal like Create.
    /// </summary>
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
                // The user's own if either plan was.
                ExplicitlyCreated = ExplicitlyCreated || other.ExplicitlyCreated,
            },
            goal);
    }
}
