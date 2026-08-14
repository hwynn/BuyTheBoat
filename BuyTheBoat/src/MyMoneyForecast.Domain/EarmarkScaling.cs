namespace MyMoneyForecast.Domain;

// Item F's "amount changed, no date shift" row (planning/25) — the smallest
// of its four mechanisms: when a goal's own Amount changes but neither its
// start_date nor its recurrence shape does, and the user chooses to keep
// several surviving EarMarkPatterns separate rather than fold them into one
// (EarmarkConsolidation.Consolidate), each surviving plan's own Amount can
// simply scale by the same ratio the goal's own Amount just changed by.
// Nothing else needs to move: since dates are untouched, there's no
// boundary revalidation, no new finance_id, no jar-balance handoff — the
// "genuinely harder mechanism" keeping plans separate needs for a start_date
// or recurrence-shape change (each surviving plan re-dated individually)
// simply doesn't arise here.
//
// StartingAllocation is deliberately left alone. It's already-realized
// money — a historical fact, not an ongoing rate — so a bill getting bigger
// or smaller shouldn't retroactively change what already accumulated, only
// what accumulates from here on.
public sealed record ScaleRequest
{
    public required FinancialPattern Goal { get; init; }
    public required decimal PreviousGoalAmount { get; init; }
    public required IReadOnlyList<EarMarkPattern> SurvivingPlans { get; init; }
}

public static class EarmarkScaling
{
    /// <summary>[CALC] Scales every surviving EarMarkPattern's own Amount by the same ratio the goal's own Amount just changed by — DatePattern, StartingAllocation, and FinanceId all carried over untouched.</summary>
    /// <param name="request">The goal's new Amount (via Goal), its previous Amount, and every surviving plan to scale.</param>
    /// <returns>One freshly-scaled EarMarkPattern per surviving plan, in the same order — same (FinanceId, Start) as before, so saving these is an in-place update, not a delete-and-recreate.</returns>
    public static IReadOnlyList<EarMarkPattern> Scale(ScaleRequest request)
    {
        if (request.PreviousGoalAmount == 0m)
        {
            throw new ArgumentException(
                "Can't compute a scaling ratio from a previous amount of zero.", nameof(request));
        }

        var ratio = request.Goal.Amount / request.PreviousGoalAmount;

        return request.SurvivingPlans
            .Select(plan => EarMarkPattern.Create(
                new EarMarkPatternOptions
                {
                    FinanceId = plan.FinanceId,
                    DatePattern = plan.DatePattern,
                    Amount = Math.Round(plan.Amount * ratio, 2),
                    StartingAllocation = plan.StartingAllocation,
                },
                request.Goal))
            .ToList();
    }
}
