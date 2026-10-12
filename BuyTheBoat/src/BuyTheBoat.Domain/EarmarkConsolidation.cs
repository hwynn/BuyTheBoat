namespace BuyTheBoat.Domain;

/// <summary>
/// How much the consolidated plan should total, the first of the two consolidation choices.
/// MeetGoal covers exactly what the goal needs after what's already saved.
/// KeepCurrentPace keeps the surviving plans' current scheduled pace (at full cadence, ignoring manual and skipped earmarks), which may under- or over-fund the goal: the user's call.
/// </summary>
public enum ConsolidationSizing
{
    MeetGoal,
    KeepCurrentPace,
}

/// <summary>
/// How the consolidated plan's contributions are spread over its window, the second choice.
/// AcrossPaydays paces them to a single clear income, or falls back to Evenly when there isn't one; Evenly spreads them across the goal's occurrences.
/// </summary>
public enum ConsolidationSpread
{
    AcrossPaydays,
    Evenly,
}

public sealed record ConsolidationRequest
{
    public required FinancialPattern Goal { get; init; }
    public required IReadOnlyList<EarMarkPattern> SurvivingPlans { get; init; }
    public required IReadOnlyList<ManualEarmark> ManualEarmarksForThisGoal { get; init; }
    public required IReadOnlyList<FinancialPattern> AllPatterns { get; init; }

    /// <summary>The first consolidation choice; meets the goal by default.</summary>
    public ConsolidationSizing Sizing { get; init; } = ConsolidationSizing.MeetGoal;

    /// <summary>The second consolidation choice; paces to income by default.</summary>
    public ConsolidationSpread Spread { get; init; } = ConsolidationSpread.AcrossPaydays;

    /// <summary>
    /// Today's jar, read by the caller off the current forecast, since this factory has no forecast of its own.
    /// Null means there's no glut to protect.
    /// </summary>
    public FundJar? CurrentJar { get; init; }
}

public sealed record ConsolidationResult
{
    public required EarMarkPattern ConsolidatedPlan { get; init; }
    public required DateOnly Start { get; init; }
    public required DateOnly End { get; init; }
}

/// <summary>
/// Folds several savings plans sharing one finance id into one, in place, for the "combine them" paths (a break-off's combine choice, and a schedule change that crosses plan boundaries).
/// There's no new finance id and no cut date; the combined plan runs from the earliest surviving plan's start through the goal's end.
/// </summary>
/// <remarks>
/// Sizing: with A the money already saved, B what the goal will pay out over the window, and C the balance left right after the last payout, contributions = B + C - A.
/// C is always 0, since 3.13.5.4.a1 resets the milestone to zero at each payout, so contributions = B - A.
/// A is the surviving plans' StartingAllocation plus manual earmarks dated by the window's end, as in GoalShortfall.AmountAllocatedByDueDate; the scheduled contributions being replaced don't count.
/// So no forecast read is needed, and the window can reach into the past.
/// A also includes CurrentJar.GlutSurplus, money a plan saved beyond its milestone, so that surplus isn't erased when the old plans are replaced.
/// The StartingAllocation and glut parts of A carry forward as the new plan's StartingAllocation, so GoalShortfall still counts that money once the old plans are deleted.
/// </remarks>
public static class EarmarkConsolidation
{
    /// <summary>
    /// [CALC] Folds every surviving savings plan for one goal into one new plan, from the earliest surviving plan's start through the goal's end, sized to cover what the goal will pay out after what's already saved (including any glut surplus).
    /// Paces to a single clear income stream like AllocationPlanProposer's paced shape, or spreads evenly across the goal's occurrences otherwise.
    /// When an affordability ceiling is given, the per-cycle contribution is held to it, so the plan stays knowingly underfunded rather than reserving money that isn't there.
    /// That cap isn't flagged to the user, since consolidation is an automatic correction, not a suggestion.
    /// </summary>
    /// <param name="request">The goal, every surviving plan, the manual earmarks already dated for it, every pattern (for income-stream detection), and the live jar to check for a glut to protect.</param>
    /// <param name="affordabilityCeiling">The most the per-cycle contribution may be (AffordabilityCeiling.For); null, 0, or less means no cap.</param>
    /// <returns>The one consolidated plan, plus the window it was sized against.</returns>
    public static ConsolidationResult Consolidate(ConsolidationRequest request, decimal? affordabilityCeiling = null)
    {
        if (request.SurvivingPlans.Count == 0)
        {
            throw new ArgumentException(
                "At least one surviving plan is required to consolidate.", nameof(request));
        }

        // Starts no earlier than the goal: after a goal's start moves later, the earliest surviving plan can begin before it, which EarMarkPattern.Create rejects.
        // This doesn't change the money math, since the goal's occurrences never fall before its own start anyway.
        var earliestSurvivingStart = request.SurvivingPlans.Min(plan => plan.DatePattern.ActiveStart);
        var start = earliestSurvivingStart > request.Goal.DatePattern.ActiveStart
            ? earliestSurvivingStart
            : request.Goal.DatePattern.ActiveStart;
        var end = request.Goal.DatePattern.Until;

        var releaseCount = request.Goal.DatePattern.GetOccurrences(start, end).Count;
        var totalReleases = Math.Abs(request.Goal.Amount) * releaseCount;

        // A = StartingAllocation + manual earmarks + glut surplus (see the class remarks).
        // The <= end filter is defensive: the caller passes every manual earmark for this finance id, and an old segment's row could still be in storage.
        var alreadyBanked = request.SurvivingPlans.Sum(plan => plan.StartingAllocation)
            + request.ManualEarmarksForThisGoal
                .Where(manual => manual.Date <= end)
                .Sum(manual => manual.Amount)
            + (request.CurrentJar?.GlutSurplus ?? 0m);

        // The sizing choice sets the total: MeetGoal covers B - A, and KeepCurrentPace keeps what the surviving plans were already scheduled to contribute.
        // Either way, the money already saved still carries forward as StartingAllocation below.
        var total = request.Sizing == ConsolidationSizing.KeepCurrentPace
            ? CurrentScheduledTotal(request.SurvivingPlans, start, end)
            : Math.Max(0m, totalReleases - alreadyBanked);

        var (schedule, perOccurrence) = BuildSchedule(request.Goal, request.AllPatterns, request.Spread, start, end, total);

        // Holds the per-cycle contribution under the affordability ceiling; the money already saved isn't capped.
        if (affordabilityCeiling is decimal ceiling && ceiling > 0m && perOccurrence > ceiling)
        {
            perOccurrence = ceiling;
        }

        var consolidatedPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = request.Goal.FinanceId,
                DatePattern = schedule,
                // Negative means money into the jar: EarMarkPattern.Amount is its effect on free funds.
                Amount = -perOccurrence,
                // Carries the surviving plans' saved money and any glut forward, since it was subtracted from total above and the old plans are about to be deleted.
                // Seeding the glut here while lowering the rate by the same amount spends it down evenly, reaching 0 on schedule.
                StartingAllocation = request.SurvivingPlans.Sum(plan => plan.StartingAllocation)
                    + (request.CurrentJar?.GlutSurplus ?? 0m),
            },
            request.Goal);

        return new ConsolidationResult { ConsolidatedPlan = consolidatedPlan, Start = start, End = end };
    }

    /// <summary>
    /// [CALC] Picks the consolidated plan's schedule and per-occurrence amount for the chosen spread.
    /// AcrossPaydays paces to the income's schedule when exactly one income stream pays in the window (AllocationPlanProposer's shape A), otherwise spreads evenly.
    /// Evenly always spreads across the goal's occurrences (shape C, without its single-occurrence case).
    /// </summary>
    /// <param name="goal">The goal being funded.</param>
    /// <param name="allPatterns">Every pattern, to look for a single clear income stream to pace against.</param>
    /// <param name="spread">Which way to spread the contributions — the second strategy choice.</param>
    /// <param name="start">The consolidated plan's own start.</param>
    /// <param name="end">The consolidated plan's own end — the goal's own Until.</param>
    /// <param name="total">The total the plan's contributions must sum to.</param>
    private static (RecurrenceRule Schedule, decimal PerOccurrence) BuildSchedule(
        FinancialPattern goal, IReadOnlyList<FinancialPattern> allPatterns, ConsolidationSpread spread, DateOnly start, DateOnly end, decimal total)
    {
        if (spread == ConsolidationSpread.AcrossPaydays)
        {
            // Only income that pays in the window counts (see AllocationPlanProposer.ActiveIncomeStreams).
            var incomePatterns = AllocationPlanProposer.ActiveIncomeStreams(allPatterns, start, end);

            if (incomePatterns.Count == 1)
            {
                var income = incomePatterns[0];
                var planUntil = income.DatePattern.Until < end ? income.DatePattern.Until : end;
                var paydayCount = income.DatePattern.GetOccurrences(start, planUntil).Count;

                if (paydayCount > 0)
                {
                    var pacedSchedule = RecurrenceRule.Create(new RecurrenceRuleOptions
                    {
                        Frequency = income.DatePattern.Frequency,
                        Interval = income.DatePattern.Interval,
                        ByDay = income.DatePattern.ByDay,
                        ByMonthDay = income.DatePattern.ByMonthDay,
                        DtStart = start,
                        Until = planUntil,
                    });
                    return (pacedSchedule, Math.Round(total / paydayCount, 2));
                }
            }
        }

        // Evenly, or AcrossPaydays with no usable income to pace against.
        var occurrenceCount = Math.Max(1, goal.DatePattern.GetOccurrences(start, end).Count);
        var spreadSchedule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = goal.DatePattern.Frequency,
            Interval = goal.DatePattern.Interval,
            DtStart = start,
            Until = end,
        });
        return (spreadSchedule, Math.Round(total / occurrenceCount, 2));
    }

    /// <summary>
    /// [CALC] What the surviving plans are scheduled to contribute over the window at full cadence, the KeepCurrentPace total.
    /// Counts every date the schedule would produce, including skipped ones, and ignores manual earmarks, so neither a skip nor a one-off lowers the pace.
    /// </summary>
    /// <param name="plans">The surviving plans being folded.</param>
    /// <param name="start">The consolidation window's start.</param>
    /// <param name="end">The consolidation window's end.</param>
    private static decimal CurrentScheduledTotal(IReadOnlyList<EarMarkPattern> plans, DateOnly start, DateOnly end) =>
        plans.Sum(plan => Math.Abs(plan.Amount)
            * plan.DatePattern.WithExcludedDates([]).GetOccurrences(start, end).Count);
}
