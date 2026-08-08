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
        if (lastDay < pattern.DatePattern.Start)
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
            },
            truncatedPattern);

        return new TruncatedPattern(truncatedPattern, truncatedPlan);
    }
}
