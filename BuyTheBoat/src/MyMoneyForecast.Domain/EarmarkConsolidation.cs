namespace MyMoneyForecast.Domain;

// Folds several EarMarkPatterns sharing one finance_id into ONE, IN PLACE —
// the retroactive-correction-side counterpart to BreakOffFactory's own
// multi-plan consolidation (planning/25, Item F's own "in place" case:
// "a genuinely new mechanism, not yet designed... nothing built so far
// collapses multiple plans without also creating a new one to collapse them
// into"). Unlike a break-off, there is no new finance_id and no cut date —
// Item E's "correct it everywhere" applies to the whole history, not just
// going forward, so the consolidated plan spans from the earliest surviving
// plan's own start through the goal's own end.
//
// The total the one new plan needs to contribute, author-derived
// (2026-08-13): call the amount already sitting in the jar before the
// earliest surviving plan's own start A, the total the goal will consume
// between start and end B, and the target balance right after the last of
// those releases washes out C. Conservation gives contributions = B + C - A.
// C is not a free choice — 3.13.5.4.a1's reset-at-release rule means the
// milestone resets to zero the instant a release lands, and "fully funded"
// means exactly that: what accumulated exactly cancels what got spent. So
// C = 0 always, and the formula collapses to B - A.
//
// A, in turn, needs no day-by-day forecast read at all — GoalShortfall's own
// AmountAllocatedByDueDate already establishes that summing StartingAllocation
// (a stored field on each surviving plan) plus any ManualEarmark already
// dated on or before the window's end is the right, complete accounting of
// "already banked," with no double-count against the scheduled contributions
// being replaced (those are exactly what this fold tears down and rebuilds
// as one pattern, so they must NOT also count as "already banked"). Because
// this needs no forecast read, it carries none of NarrowSurvivingPlanIfNeeded's
// own "can't safely read a balance from before the as-of date" limitation —
// this mechanism is meant to reach into the past, and can.
//
// The StartingAllocation share of A carries forward onto the new plan
// itself, not just into sizing its (smaller) Amount — otherwise that
// already-banked money stops being counted anywhere the moment the old
// plans' rows are deleted, leaving GoalShortfall short by exactly that much
// even though the money never actually went anywhere (found 2026-08-14, via
// a save-then-rebuild-the-forecast test in FinancePatternSaveConfirmationTests).
//
// A also gained a glut term, 2026-08-15 (redesign/planning/26, "the glut
// case"): A as originally derived only ever covered money OUTSIDE the
// schedule being replaced (StartingAllocation + manual earmarks) — correct
// for its own "no double-count against the contributions being replaced"
// reasoning, but blind to a real risk that reasoning didn't anticipate. If a
// surviving plan has been over-contributing (a genuine, deliberate glut —
// FundJar.HasGlut), that surplus is REAL money sitting in the jar TODAY,
// beyond what the plan's own "on pace" trajectory (MilestoneAmount) would
// read — and this fold was about to silently un-count it: the new schedule's
// total is computed purely from B - A with no memory of that surplus, so the
// old plans' real over-funding would simply vanish once their rows are
// deleted and replaced by a schedule sized as if the jar had only ever been
// exactly on pace. CurrentJar.GlutSurplus (0 when not glutted, or the jar's
// balance floored below MilestoneAmount) is added to A precisely to prevent
// that — the new plan asks for exactly that much less, protecting the
// surplus instead of erasing it.
public sealed record ConsolidationRequest
{
    public required FinancialPattern Goal { get; init; }
    public required IReadOnlyList<EarMarkPattern> SurvivingPlans { get; init; }
    public required IReadOnlyList<ManualEarmark> ManualEarmarksForThisGoal { get; init; }
    public required IReadOnlyList<FinancialPattern> AllPatterns { get; init; }

    // The live jar as of today (read by the caller off the current forecast
    // — a pure domain function has no forecast of its own, same reasoning as
    // BreakOffRequest.CarriedOverJarBalance). Null is tolerated (treated as
    // "nothing glutted to protect") rather than required, matching how
    // JarBalanceOn's own callers already default to 0m for a day the
    // forecast's timeline doesn't cover yet.
    public FundJar? CurrentJar { get; init; }
}

public sealed record ConsolidationResult
{
    public required EarMarkPattern ConsolidatedPlan { get; init; }
    public required DateOnly Start { get; init; }
    public required DateOnly End { get; init; }
}

public static class EarmarkConsolidation
{
    /// <summary>[CALC] Folds every surviving EarMarkPattern for one goal into a single freshly-sized plan, spanning from the earliest surviving plan's own start through the goal's own end — sized so its contributions exactly cover what the goal will consume in that window, net of what's already banked (including any protected glut surplus). Paces to a single clear income stream the same way AllocationPlanProposer's own paced shape does, or spreads evenly across the goal's own occurrences otherwise.</summary>
    /// <param name="request">The goal, every surviving plan, the manual earmarks already dated for it, every pattern (for income-stream detection), and the live jar to check for a glut to protect.</param>
    /// <returns>The one consolidated plan, plus the window it was sized against.</returns>
    public static ConsolidationResult Consolidate(ConsolidationRequest request)
    {
        if (request.SurvivingPlans.Count == 0)
        {
            throw new ArgumentException(
                "At least one surviving plan is required to consolidate.", nameof(request));
        }

        // Clamped forward to the goal's own ActiveStart when it lands
        // later, 2026-08-17 — found while widening ConsolidationNeeded to
        // also force this mechanism for a start_date change, not just a
        // recurrence-shape one: this method was only ever exercised
        // against a goal whose own Start never moved, so the consolidated
        // plan's own Start (the earliest surviving plan's own, unclamped)
        // could land BEFORE the goal's newly-moved-forward one, which
        // EarMarkPattern.Create's own validation rejects outright. Safe to
        // clamp with no effect on the money math: GetOccurrences already
        // self-clamps to the goal's own real Start regardless of how early
        // a lower bound it's asked from, so releaseCount below is
        // identical either way — only the constructed plan's own Start
        // field needed fixing, not anything it's sized against.
        var earliestSurvivingStart = request.SurvivingPlans.Min(plan => plan.DatePattern.ActiveStart);
        var start = earliestSurvivingStart > request.Goal.DatePattern.ActiveStart
            ? earliestSurvivingStart
            : request.Goal.DatePattern.ActiveStart;
        var end = request.Goal.DatePattern.Until;

        var releaseCount = request.Goal.DatePattern.GetOccurrences(start, end).Count;
        var totalReleases = Math.Abs(request.Goal.Amount) * releaseCount;

        // The <= end filter is defensive, not reachable through ordinary use:
        // ManualEarmark.Create already requires a manual earmark's own date to
        // fall inside its earmark pattern's span, and EarMarkPattern.Create
        // requires that span's own Until to never exceed the goal's — so
        // nothing legitimately tied to this goal can be dated past `end`
        // (the goal's own Until) in the first place. Kept anyway: the caller
        // passes every ManualEarmark for this finance_id, not only the ones
        // tied to today's surviving plans, and an earlier, now-superseded
        // segment's own row could in principle still be sitting in storage.
        var alreadyBanked = request.SurvivingPlans.Sum(plan => plan.StartingAllocation)
            + request.ManualEarmarksForThisGoal
                .Where(manual => manual.Date <= end)
                .Sum(manual => manual.Amount)
            + (request.CurrentJar?.GlutSurplus ?? 0m);

        // C = 0 (see this file's own header note) folds straight into this
        // subtraction rather than appearing as its own term.
        var total = Math.Max(0m, totalReleases - alreadyBanked);

        var (schedule, perOccurrence) = BuildSchedule(request.Goal, request.AllPatterns, start, end, total);

        var consolidatedPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = request.Goal.FinanceId,
                DatePattern = schedule,
                // Negative = money moves INTO the jar, same sign convention
                // AllocationPlanProposer's own paced/front-loaded shapes use.
                Amount = -perOccurrence,
                // Carries the surviving plans' own already-banked money
                // forward onto the one plan that replaces them. Without
                // this, that money is discounted correctly when SIZING
                // total above but then never counted again anywhere — the
                // old plans' rows are gone, and a fresh plan with
                // StartingAllocation left at 0 doesn't remember they ever
                // held it — leaving GoalShortfall short by exactly this much
                // even though the money is still real. Found 2026-08-14 via
                // FinancePatternSaveConfirmationTests' own
                // save-then-rebuild-the-forecast shortfall check.
                //
                // GlutSurplus carries forward the SAME way, added 2026-08-15,
                // for the identical reason — it was only just discounted out
                // of `total` above, so it has to land somewhere the rebuilt
                // forecast will still see it, or it's erased the moment the
                // old plans' rows are gone, exactly like StartingAllocation
                // would have been. Confirmed by replaying the actual numbers,
                // not just by the symmetry: seeding the new schedule with the
                // glut AND discounting its own ongoing rate by the same
                // amount together spend the glut down evenly across the
                // window's own remaining occurrences, landing at exactly 0
                // right on schedule — the surplus is drawn on to lighten the
                // ongoing ask, not thrown away
                // (An_existing_glut_is_carried_forward_and_spent_down_evenly_not_erased).
                StartingAllocation = request.SurvivingPlans.Sum(plan => plan.StartingAllocation)
                    + (request.CurrentJar?.GlutSurplus ?? 0m),
            },
            request.Goal);

        return new ConsolidationResult { ConsolidatedPlan = consolidatedPlan, Start = start, End = end };
    }

    /// <summary>[CALC] Picks the consolidated plan's own schedule and per-occurrence amount — paced to a single clear income stream's own rrule when exactly one exists and has occurrences in the window (AllocationPlanProposer's own "shape A"), or spread evenly across the goal's own occurrences otherwise (its "shape C," minus the single-occurrence special case, which doesn't apply here — a goal with multiple surviving plans to fold has already been contributed to more than once).</summary>
    /// <param name="goal">The goal being funded.</param>
    /// <param name="allPatterns">Every pattern, to look for a single clear income stream to pace against.</param>
    /// <param name="start">The consolidated plan's own start.</param>
    /// <param name="end">The consolidated plan's own end — the goal's own Until.</param>
    /// <param name="total">The total the plan's contributions must sum to.</param>
    private static (RecurrenceRule Schedule, decimal PerOccurrence) BuildSchedule(
        FinancialPattern goal, IReadOnlyList<FinancialPattern> allPatterns, DateOnly start, DateOnly end, decimal total)
    {
        var incomePatterns = allPatterns.Where(pattern => pattern.Amount > 0m).ToList();

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
                    Start = start,
                    Until = planUntil,
                });
                return (pacedSchedule, Math.Round(total / paydayCount, 2));
            }
        }

        var occurrenceCount = Math.Max(1, goal.DatePattern.GetOccurrences(start, end).Count);
        var spreadSchedule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = goal.DatePattern.Frequency,
            Interval = goal.DatePattern.Interval,
            Start = start,
            Until = end,
        });
        return (spreadSchedule, Math.Round(total / occurrenceCount, 2));
    }
}
