namespace MyMoneyForecast.Domain;

public sealed record TruncatedPattern(FinancialPattern Pattern, EarMarkPattern? Plan);

// Ends a pattern — and its savings plan, if it has one — on a chosen date.
// Used both when a pattern simply stops with no continuation, and as the
// predecessor half of a break-off before a successor picks up from the next
// day.
//
// The jar question needs no separate handling: 3.11.2.a2 requires the
// plan's Until to never exceed its goal's, so truncating the plan to match
// is the same operation as truncating the pattern. Whatever was in the jar
// isn't moved or destroyed — a jar was never a real transfer of money — it
// simply stops being computed past LastDay and reads as ordinary free
// balance from then on.
public static class PatternTruncation
{
    /// <summary>[CALC] Stops a bill or paycheck — and the savings plan behind it, if it has one — on a chosen date, with nothing continuing after. Whatever was already saved simply becomes free money again from that date on.</summary>
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

        // Never extend the plan — only ever shorten it to at most LastDay. A
        // plan that already ended earlier than LastDay (the user set an
        // earlier end for the plan itself) keeps its own, earlier end.
        var planUntil = plan.DatePattern.Until < lastDay ? plan.DatePattern.Until : lastDay;

        var truncatedPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = plan.FinanceId,
                DatePattern = plan.DatePattern.WithUntil(planUntil),
                Amount = plan.Amount,
                StartingAllocation = plan.StartingAllocation,
                // Trimming a plan to fit its goal's boundary is upkeep, not user
                // authorship — it keeps whatever standing the plan already had.
                ExplicitlyCreated = plan.ExplicitlyCreated,
            },
            truncatedPattern);

        return new TruncatedPattern(truncatedPattern, truncatedPlan);
    }

    /// <summary>[CALC] Moves a savings plan's active span forward to begin on a chosen date — the front-boundary counterpart to EndOn — absorbing whatever it held immediately before that date into its StartingAllocation. Moved via ReanchoredToStartOn, which keeps its cadence, so the dates it lands on from the new start onward stay the same; any old lead-in reaching further back is replaced by the new start (with a fresh lead-in to the first occurrence when that lands later). Doesn't touch ManualEarmarks dated before the new start — the caller's job, since this is a pure domain method with no repository access.</summary>
    /// <param name="plan">The plan to trim.</param>
    /// <param name="goal">The goal it funds — validated against, same as EarMarkPattern.Create.</param>
    /// <param name="newStart">The plan's new active-span start. Must be strictly after its current start.</param>
    /// <param name="absorbedBalance">Whatever the plan held immediately before newStart — read by the caller off the live forecast, since a pure domain method has no forecast of its own to read (same reasoning as BreakOffRequest.CarriedOverJarBalance). Added on top of whatever StartingAllocation the plan already had.</param>
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
                // Upkeep, not user authorship — keeps the plan's existing standing.
                ExplicitlyCreated = plan.ExplicitlyCreated,
            },
            goal);
    }
}
