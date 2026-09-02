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

// What ScaleToMeetGoal worked out — the re-rated plans, plus the two totals it
// compared to decide whether re-rating was even called for. The caller reads
// the totals to decide whether to OFFER the adjustment (they already fund the
// goal → nothing to correct → no question worth asking) and which way it's off
// (contributing less than needed = underfunded, more = overfunded), then uses
// ScaledPlans only if the user takes the offer.
public sealed record MeetGoalScalingResult
{
    public required IReadOnlyList<EarMarkPattern> ScaledPlans { get; init; }

    // What the plans are scheduled to contribute over the goal's window at their
    // current amounts, and what they'd need to contribute together to exactly
    // fund the goal net of what's already banked. Equal (to the cent) means the
    // plans already meet the goal and ScaledPlans is unchanged.
    public required decimal CurrentTotal { get; init; }
    public required decimal NeededTotal { get; init; }

    // Whether an affordability ceiling bound the result below what would meet the goal, so ScaledPlans
    // deliberately underfunds it. False when no ceiling was given, or the goal fit under it. Not required:
    // defaults to false, so the no-ceiling path reads as uncapped.
    public bool CappedToAffordability { get; init; }
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

    /// <summary>[CALC] Re-rates several EarMarkPatterns for one goal so that, kept separate, they TOGETHER exactly fund it — net of what's already banked — while keeping the same proportions between them. Used when a goal breaks off into a new segment at a different amount and the user keeps its plans separate: left as-is they'd over- or under-fund the new amount, and this offers the correction. Scales every plan's Amount by one shared ratio (needed ÷ current), which both hits the funded total and preserves each plan's relative share, so it's a genuine proportional split. The window and the "already banked" accounting mirror EarmarkConsolidation.Consolidate's MeetGoal exactly (releases over the goal's occurrences, minus summed StartingAllocation) — the difference is only that the total is split back across the plans instead of folded into one. DatePattern, StartingAllocation, and FinanceId are carried over untouched; a plan contributing nothing over the window (no occurrences) leaves everything unchanged rather than dividing by zero. When an affordability ceiling is given and meeting the goal would exceed it, the combined contribution is held to the ceiling instead — CappedToAffordability flags it, and the goal stays knowingly underfunded.</summary>
    /// <param name="goal">The goal these plans fund — its Amount and occurrences set the target.</param>
    /// <param name="plans">The separate plans to re-rate; their summed StartingAllocation is the "already banked" credit.</param>
    /// <param name="affordabilityCeiling">The most the plans' combined per-cycle contribution may be, from AffordabilityCeiling.For; when meeting the goal would exceed it the plans are held to it instead. Null leaves the result uncapped. See the body note for the two simplifications this makes.</param>
    public static MeetGoalScalingResult ScaleToMeetGoal(FinancialPattern goal, IReadOnlyList<EarMarkPattern> plans, decimal? affordabilityCeiling = null)
    {
        // Same window Consolidate uses: from the earliest plan's own start (never
        // before the goal's own), through the goal's end.
        var earliestStart = plans.Min(plan => plan.DatePattern.ActiveStart);
        var start = earliestStart > goal.DatePattern.ActiveStart ? earliestStart : goal.DatePattern.ActiveStart;
        var end = goal.DatePattern.Until;

        var releaseCount = goal.DatePattern.GetOccurrences(start, end).Count;
        var totalReleases = Math.Abs(goal.Amount) * releaseCount;
        var alreadyBanked = plans.Sum(plan => plan.StartingAllocation);
        var neededTotal = Math.Max(0m, totalReleases - alreadyBanked);

        // What the plans are set to contribute now, at full cadence (skipped and
        // manual earmarks left out, same as Consolidate's KeepCurrentPace total),
        // so the ratio is measured against the plans' own scheduled rate.
        var currentTotal = plans.Sum(plan =>
            Math.Abs(plan.Amount) * plan.DatePattern.WithExcludedDates([]).GetOccurrences(start, end).Count);

        var ratio = currentTotal == 0m ? 1m : neededTotal / currentTotal;

        // Hold the plans' combined per-cycle contribution under the affordability ceiling (resolving the
        // old income-feasibility TODO with the free-funds ceiling, which supersedes the single-income idea).
        // Two deliberate simplifications: (1) it treats the plans as if they all draw on the same day — the
        // combined per-cycle sum vs the ceiling — so it never over-commits but can be conservative when their
        // cadences don't actually coincide; (2) the ceiling must be computed as room FOR these plans (a
        // forecast without their current contributions), since the cap compares the full combined
        // contribution, not just the increase. A ceiling of 0 or less (an already-over-committed window) is
        // out of scope — reducing existing commitments is a different operation than re-rating.
        var cappedToAffordability = false;
        if (affordabilityCeiling is decimal ceiling && ceiling > 0m)
        {
            var combinedPerCycle = plans.Sum(plan => Math.Abs(plan.Amount));
            if (combinedPerCycle > 0m && ceiling / combinedPerCycle < ratio)
            {
                ratio = ceiling / combinedPerCycle;
                cappedToAffordability = true;
            }
        }

        // Apply the final ratio (goal-meeting, or the affordability cap when that binds) to every plan,
        // keeping their relative shares.
        var scaled = plans
            .Select(plan => EarMarkPattern.Create(
                new EarMarkPatternOptions
                {
                    FinanceId = plan.FinanceId,
                    DatePattern = plan.DatePattern,
                    Amount = Math.Round(plan.Amount * ratio, 2),
                    StartingAllocation = plan.StartingAllocation,
                },
                goal))
            .ToList();

        return new MeetGoalScalingResult
        {
            ScaledPlans = scaled,
            CurrentTotal = currentTotal,
            NeededTotal = neededTotal,
            CappedToAffordability = cappedToAffordability,
        };
    }
}
