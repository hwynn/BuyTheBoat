namespace BuyTheBoat.Domain;

public sealed record TruncatedPattern(FinancialPattern Pattern, EarMarkPattern? Plan);

/// <summary>
/// Ends a pattern, and its savings plan if it has one, on a chosen date: either when it simply stops, or as the first half of a break-off before a successor starts the next day.
/// The jar needs no separate handling: a jar was never a real transfer of money, so once the plan ends, whatever it held reads as free funds.
/// </summary>
public static class PatternTruncation
{
    /// <summary>
    /// [CALC] Stops a bill or paycheck, and the savings plan behind it if it has one, on a chosen date, with nothing continuing after.
    /// Whatever was already saved becomes free money again from that date on.
    /// </summary>
    /// <param name="pattern">The bill or paycheck to stop.</param>
    /// <param name="plan">Its savings plan, if it has one.</param>
    /// <param name="lastDay">The last day the pattern (and its plan) should still be active.</param>
    /// <returns>The pattern and its plan, both ending on <paramref name="lastDay"/> — the plan only if one existed, and never pushed later than it already ended.</returns>
    public static TruncatedPattern EndOn(FinancialPattern pattern, EarMarkPattern? plan, DateOnly lastDay)
    {
        if (lastDay < pattern.DatePattern.ActiveStart)
        {
            throw new ArgumentException(
                "The end date can't be before the pattern's own start.",
                nameof(lastDay));
        }

        var truncatedPattern = pattern.WithUntil(lastDay);

        if (plan is null)
        {
            return new TruncatedPattern(truncatedPattern, null);
        }

        // The plan ends on lastDay, or keeps its own end if that's already earlier.
        var planUntil = plan.DatePattern.Until < lastDay ? plan.DatePattern.Until : lastDay;

        var truncatedPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = plan.FinanceId,
                DatePattern = plan.DatePattern.WithUntil(planUntil),
                Amount = plan.Amount,
                StartingAllocation = plan.StartingAllocation,
                // Trimming isn't a user edit, so the plan keeps its existing value.
                ExplicitlyCreated = plan.ExplicitlyCreated,
            },
            truncatedPattern);

        return new TruncatedPattern(truncatedPattern, truncatedPlan);
    }

    /// <summary>
    /// [CALC] Moves a savings plan's start later, the front-end counterpart to EndOn, adding what it held just before that date to its StartingAllocation.
    /// It uses ReanchoredToStartOn, so the dates it lands on from the new start onward stay the same.
    /// Manual earmarks dated before the new start are the caller's job, since this has no access to saved data.
    /// </summary>
    /// <param name="plan">The plan to trim.</param>
    /// <param name="goal">The goal it funds — validated against, same as EarMarkPattern.Create.</param>
    /// <param name="newStart">The plan's new active-span start. Must be strictly after its current start.</param>
    /// <param name="absorbedBalance">What the plan held just before newStart, read by the caller off the forecast; added to the plan's existing StartingAllocation.</param>
    /// <returns>A plan with the same finance id, rate, and recurrence shape, its active span starting from newStart, its StartingAllocation increased by absorbedBalance.</returns>
    public static EarMarkPattern StartOn(EarMarkPattern plan, FinancialPattern goal, DateOnly newStart, decimal absorbedBalance)
    {
        if (newStart <= plan.DatePattern.ActiveStart)
        {
            throw new ArgumentException(
                "The new start must be after the plan's own current start — there has to be at least one day of history to absorb.",
                nameof(newStart));
        }

        return EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = plan.FinanceId,
                DatePattern = plan.DatePattern.ReanchoredToStartOn(newStart),
                Amount = plan.Amount,
                StartingAllocation = plan.StartingAllocation + absorbedBalance,
                // Not a user edit, so the plan keeps its existing value.
                ExplicitlyCreated = plan.ExplicitlyCreated,
            },
            goal);
    }
}
