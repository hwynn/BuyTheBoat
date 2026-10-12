namespace BuyTheBoat.Domain;

/// <summary>A goal whose Amount changed, its previous Amount, and the surviving plans to scale (see EarmarkScaling.Scale).</summary>
public sealed record ScaleRequest
{
    public required FinancialPattern Goal { get; init; }
    public required decimal PreviousGoalAmount { get; init; }
    public required IReadOnlyList<EarMarkPattern> SurvivingPlans { get; init; }
}

/// <summary>
/// What ScaleToMeetGoal worked out: the re-rated plans, plus the two totals it compared.
/// The caller uses the totals to decide whether to offer the change at all and which way the plans are off, then uses ScaledPlans only if the user accepts.
/// </summary>
public sealed record MeetGoalScalingResult
{
    public required IReadOnlyList<EarMarkPattern> ScaledPlans { get; init; }

    /// <summary>What the plans are scheduled to contribute over the goal's window at their current amounts.</summary>
    public required decimal CurrentTotal { get; init; }

    /// <summary>What the plans together need to contribute to fund the goal after what's already saved; equal to CurrentTotal means ScaledPlans is unchanged.</summary>
    public required decimal NeededTotal { get; init; }

    /// <summary>Whether an affordability ceiling held the plans below what would meet the goal, so ScaledPlans knowingly underfunds it.</summary>
    public bool CappedToAffordability { get; init; }
}

/// <summary>
/// The upcoming contribution dates two "skip some events" choices would drop to use up an overfunded plan's surplus.
/// SkipNext is the soonest one; SkipStretch is a run of the soonest ones that fits within the surplus; neither drops the plan below the goal.
/// </summary>
public sealed record SurplusSkipPlan
{
    public required IReadOnlyList<DateOnly> SkipNext { get; init; }
    public required IReadOnlyList<DateOnly> SkipStretch { get; init; }
}

/// <summary>
/// Re-rates savings plans in place when a goal's amount changes, without moving any dates, and works out which contributions an overfunded plan could skip.
/// StartingAllocation is never changed: it's money already saved, so a bill getting bigger or smaller only changes what's saved from here on.
/// </summary>
public static class EarmarkScaling
{
    /// <summary>
    /// [CALC] For one overfunded plan, the upcoming contribution dates two "skip some events" choices would drop: the soonest one (SkipNext), and a run of the soonest ones that uses up the surplus (SkipStretch).
    /// Only whole contributions that fit within the surplus are dropped, so neither pushes the plan below its goal.
    /// Null when the plan isn't overfunded by at least one whole contribution, so the caller can leave the question out; dates are the plan's occurrences after asOfDate.
    /// </summary>
    /// <param name="goal">The goal the plan funds — its Amount and occurrences set what's needed.</param>
    /// <param name="plan">The overfunded plan whose upcoming contributions might be skipped.</param>
    /// <param name="asOfDate">Today; only occurrences strictly after it count as upcoming/skippable.</param>
    public static SurplusSkipPlan? SurplusSkip(FinancialPattern goal, EarMarkPattern plan, DateOnly asOfDate)
    {
        var perContribution = Math.Abs(plan.Amount);
        if (perContribution == 0m)
        {
            return null;
        }

        // The same window as ScaleToMeetGoal: from the plan's start (no earlier than the goal's) through the goal's end.
        var start = plan.DatePattern.ActiveStart > goal.DatePattern.ActiveStart ? plan.DatePattern.ActiveStart : goal.DatePattern.ActiveStart;
        var end = goal.DatePattern.Until;

        var neededTotal = Math.Max(0m, Math.Abs(goal.Amount) * goal.DatePattern.GetOccurrences(start, end).Count - plan.StartingAllocation);
        var currentTotal = perContribution * plan.DatePattern.WithExcludedDates([]).GetOccurrences(start, end).Count;
        var surplus = currentTotal - neededTotal;

        // How many whole contributions fit inside the surplus, rounded down so skipping them never drops the plan below the goal.
        var maxSkippable = (int)Math.Floor(surplus / perContribution);
        if (maxSkippable < 1)
        {
            return null;
        }

        var upcoming = plan.DatePattern.WithExcludedDates([]).GetOccurrences(asOfDate, end)
            .Where(date => date > asOfDate)
            .ToList();
        if (upcoming.Count == 0)
        {
            return null;
        }

        var stretchCount = Math.Min(maxSkippable, upcoming.Count);
        return new SurplusSkipPlan
        {
            SkipNext = [upcoming[0]],
            SkipStretch = upcoming.Take(stretchCount).ToList(),
        };
    }

    /// <summary>
    /// [CALC] Scales every surviving plan's Amount by the same ratio the goal's Amount just changed by, for a goal whose amount changed but whose dates and shape didn't, when the user keeps its plans separate.
    /// Dates, StartingAllocation, and FinanceId are untouched, so there's nothing to revalidate and no jar hand-off.
    /// When an affordability ceiling is given, the ratio is held down so the combined contribution fits under it, knowingly underfunding the goal.
    /// That cap isn't flagged to the user, since this is an automatic re-rate, not a suggestion.
    /// </summary>
    /// <param name="request">The goal's new Amount (via Goal), its previous Amount, and every surviving plan to scale.</param>
    /// <param name="affordabilityCeiling">The most the plans' combined per-cycle contribution may be (AffordabilityCeiling.For, measured as room for these plans); null, 0, or less means no cap.</param>
    /// <returns>One freshly-scaled EarMarkPattern per surviving plan, in the same order — same (FinanceId, Start) as before, so saving these is an in-place update, not a delete-and-recreate.</returns>
    public static IReadOnlyList<EarMarkPattern> Scale(ScaleRequest request, decimal? affordabilityCeiling = null)
    {
        if (request.PreviousGoalAmount == 0m)
        {
            throw new ArgumentException(
                "Can't compute a scaling ratio from a previous amount of zero.", nameof(request));
        }

        var ratio = request.Goal.Amount / request.PreviousGoalAmount;

        // Lowers the ratio if the combined per-cycle amount would exceed the ceiling, as ScaleToMeetGoal does.
        if (affordabilityCeiling is decimal ceiling && ceiling > 0m)
        {
            var combinedPerCycle = request.SurvivingPlans.Sum(plan => Math.Abs(plan.Amount));
            if (combinedPerCycle > 0m && ceiling / combinedPerCycle < ratio)
            {
                ratio = ceiling / combinedPerCycle;
            }
        }

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

    /// <summary>
    /// [CALC] Re-rates several plans for one goal so that, kept separate, together they fund it after what's already saved, keeping their proportions to each other.
    /// Used when a goal breaks off into a new segment at a different amount and the user keeps its plans separate; this offers the correction.
    /// Every plan's Amount is scaled by one shared ratio (needed ÷ current); the window matches EarmarkConsolidation's MeetGoal, but "already saved" here is only the plans' StartingAllocation.
    /// A set of plans with no contributions in the window is left unchanged.
    /// When an affordability ceiling is given, the combined contribution is held to it, and CappedToAffordability says so.
    /// </summary>
    /// <param name="goal">The goal these plans fund — its Amount and occurrences set the target.</param>
    /// <param name="plans">The separate plans to re-rate; their summed StartingAllocation is the "already banked" credit.</param>
    /// <param name="affordabilityCeiling">The most the plans' combined per-cycle contribution may be (AffordabilityCeiling.For); null, 0, or less means no cap.</param>
    public static MeetGoalScalingResult ScaleToMeetGoal(FinancialPattern goal, IReadOnlyList<EarMarkPattern> plans, decimal? affordabilityCeiling = null)
    {
        // The same window as Consolidate: from the earliest plan's start (no earlier than the goal's) through the goal's end.
        var earliestStart = plans.Min(plan => plan.DatePattern.ActiveStart);
        var start = earliestStart > goal.DatePattern.ActiveStart ? earliestStart : goal.DatePattern.ActiveStart;
        var end = goal.DatePattern.Until;

        var releaseCount = goal.DatePattern.GetOccurrences(start, end).Count;
        var totalReleases = Math.Abs(goal.Amount) * releaseCount;
        var alreadyBanked = plans.Sum(plan => plan.StartingAllocation);
        var neededTotal = Math.Max(0m, totalReleases - alreadyBanked);

        // What the plans are scheduled to contribute at full cadence, ignoring skips and manual earmarks, like Consolidate's KeepCurrentPace total.
        var currentTotal = plans.Sum(plan =>
            Math.Abs(plan.Amount) * plan.DatePattern.WithExcludedDates([]).GetOccurrences(start, end).Count);

        var ratio = currentTotal == 0m ? 1m : neededTotal / currentTotal;

        // Lowers the ratio if the combined per-cycle amount would exceed the ceiling.
        // This treats the plans as all drawing on the same day, so it never over-commits but can be cautious when their schedules don't line up.
        // The ceiling must be measured as room for these plans (a forecast without them), since it's compared against the full combined contribution.
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

        // Applies the final ratio to every plan, keeping their relative shares.
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
