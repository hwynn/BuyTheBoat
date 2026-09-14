namespace MyMoneyForecast.Domain;

// The default Allocation Plan proposed for a newly-created outflow, the outflow
// itself with its active span prepared (ActiveFrom set when it starts in the
// future, so the plan fits inside it — persist THIS, not the original), plus an
// optional one-off starting earmark to cover a first occurrence the plan can't
// accumulate for in time.
public sealed record ProposedAllocationPlan(FinancialPattern Outflow, EarMarkPattern Plan, ManualEarmark? StartingEarmark);

// Proposes a default Allocation Plan (an EarMarkPattern) for a newly-created
// outflow — a bill, a one-time goal, or any expected transaction with a
// negative Amount.
//
// ONE function for both goals and bills — a one-time goal is just a
// FinancialPattern with a single occurrence, so it falls out of the same
// logic (billOccurrences = 1). The proposer only ever fills in a DatePattern
// and an Amount; everything downstream (the FundJar, its MilestoneAmount,
// the shortfall) is identical no matter which case fires, so the two shapes
// add no new machinery. Supersedes OneTimeGoalFactory's fixed-Monthly
// installment default.
//
// Two shapes:
//   A (single periodic income) — align contributions to the income's rhythm,
//     spreading the amount so the plan's total over the window equals the
//     bill's total consumption over it. If the first bill occurrence lands
//     before the first paycheck-contribution, a starting earmark front-loads
//     that first occurrence (branch B).
//   C (no single usable income — none, or more than one stream) — a
//     genuinely recurring outflow reserves the full amount on its own
//     cadence, starting from the as-of date (front-loaded, no starting
//     earmark needed). A SINGLE-occurrence outflow instead spreads the
//     amount evenly across the remaining time by default — mirroring
//     OneTimeGoalFactory's own long-standing installment default — a
//     one-time goal or one-off bill should never lock up a large, long-dated
//     amount all at once. The one exception is a transfer's withdrawal
//     (`spreadEvenlyWithNoIncome: false`): a transfer stays plain and
//     immediate, with no adaptive behavior.
//
// Divide-by-zero is structurally impossible: incomeOccurrences is a divisor
// only inside shape A, which is entered only when exactly one income stream
// has at least one occurrence in the window; the spread-evenly shape divides
// by an occurrence count that's always >= 1 by construction (the installment
// pattern's own Start is always its first occurrence).
//
// Documented v1 simplifications (defensible defaults, refine later):
//  - "More than one income stream" is treated as shape C rather than an
//    arbitrary post-income/pre-bill point; the front-loaded shape is a safe,
//    if conservative, default and the user can edit it.
//  - An income stream that ends before the bill does is not handled: the
//    plan is bounded by min(income.Until, bill.Until), so occurrences past
//    the income's end simply aren't funded and the shortfall reports them.
//
// ProposeSameSchedule/ProposeSameAmount (2026-08-14): two further methods for
// re-proposing a plan against an EXISTING EarMarkPattern rather than
// building one from scratch — planning/25's Item G (suggestions) is meant to
// offer these alongside Propose's own default. Deliberately narrower
// freedom than a user's own edits get (planning/26): these should always
// still be genuinely ideal, so both return null — "don't offer this" —
// whenever their own result wouldn't be, or would be indistinguishable from
// Propose's own default. Shared alignment fix (AlignedSchedule, below):
// copying a reference pattern's own Frequency/Interval/ByDay/ByMonthDay at a
// NEW Start silently loses phase whenever the reference relied on RFC
// 5545's implicit "omitted BYDAY defaults to DTSTART's own weekday" — found
// 2026-08-14 as a live, confirmed bug in ProposePaced itself (below), not
// just a risk for these two new methods. Both take carriedOverJarBalance as
// its own parameter rather than reading existingPlan.StartingAllocation —
// the author's own explicit direction: whatever plan gets created here must
// carry the previous plan's own real, current funds forward, and
// StartingAllocation is a static field frozen at whenever the existing plan
// was first created, not a live reading. Same "carry the funds forward"
// field BreakOffFactory already reads off the forecast for its own default
// Propose-based successor (BreakOffRequest.CarriedOverJarBalance) — these
// two methods now expect the same real number from their own caller.
//
// IsPacedAgainst/FindPlansPacedAgainst (2026-08-17): a first, DETECTION-ONLY
// piece of planning's own "loose association" thread (redesign/memory's own
// project_next_phase.md, 2026-08-16/17 blocks) — finding which of a
// household's EXISTING plans were paced against a given income, since
// nothing in EarMarkPatternOptions stores that link. Built as the exact
// converse of ProposePaced's own forward construction: every occurrence of
// a genuinely-paced plan lands on one of the income's own real paydays, by
// construction, so checking for that live is the natural inverse. A
// deliberately narrow first pass, not a settled definition — flagged in
// both methods' own doc comments and in project_next_phase.md as my own
// grounded heuristic. UPDATE 2026-08-17, same day: the suggestion/cascade
// this was built to support is now real too —
// FinancePatternSaveConfirmation.PerformPaycheckAssociationCascadeIfApplicable
// re-Proposes each invalidated plan (via this same class's own Propose) once
// the user opts in through EditingHistoryConfirmationWindow's own new row.
// These two detection methods are still exactly the heuristic described
// above; only the orchestration around them changed.
public static class AllocationPlanProposer
{
    /// <summary>[CALC] The income streams worth pacing a plan against over a window — positive-amount patterns that ACTUALLY have a payday in [from, to]. The date filter is load-bearing, not cosmetic: once any income is ever broken off, its truncated predecessor row keeps a positive Amount forever (a break-off truncates, never deletes), so a bare "Amount > 0" count sees it as a second, phantom income stream for every future plan — and since pacing only kicks in for exactly ONE clear income, that phantom silently collapses the paced shape to the even/front-loaded fallback. Requiring a real payday in the window drops the dead predecessor (it stopped paying before the window) while keeping genuine concurrent earners.</summary>
    /// <param name="allPatterns">Every pattern to scan for income.</param>
    /// <param name="from">Start of the window a plan would pace its contributions across.</param>
    /// <param name="to">End of that window.</param>
    public static IReadOnlyList<FinancialPattern> ActiveIncomeStreams(
        IReadOnlyList<FinancialPattern> allPatterns, DateOnly from, DateOnly to) =>
        allPatterns
            .Where(pattern => pattern.Amount > 0m && pattern.DatePattern.GetOccurrences(from, to).Count > 0)
            .ToList();

    /// <summary>[CALC] Proposes a default Allocation Plan for a newly-created outflow — paced against a single clear income stream when one exists, or front-loaded/spread otherwise. See this class's own header for the three shapes.</summary>
    /// <param name="outflow">The newly-created bill, goal, or transfer leg needing a plan.</param>
    /// <param name="allPatterns">Every other pattern, to look for a single clear income stream to pace against.</param>
    /// <param name="asOfDate">Today, or the forecast's as-of date — where the plan starts contributing from.</param>
    /// <param name="spreadEvenlyWithNoIncome">Whether a single-occurrence outflow with no clear income spreads evenly across the remaining time (the default) or reserves the full amount immediately — pass false for a transfer's withdrawal, which stays plain with no adaptive behavior.</param>
    /// <param name="carriedOverJarBalance">What an existing jar already holds, if this proposal is replacing a plan with real history rather than starting one from scratch — same field, same meaning, as ProposeSameSchedule/ProposeSameAmount's own parameter of this name. Defaults to 0m (every ordinary "brand-new outflow" caller is unaffected). Added 2026-08-15 specifically so Item G's own "Recommended" candidate preview stops understating what actually gets saved: BreakOffFactory.BreakOff already overrides the chosen plan's StartingAllocation with the real carried-over balance regardless of which candidate is picked, but the candidate the picker itself showed the user, before this fix, never reflected that — reading $0 there even when a real glut existed. See planning/26's own "the glut case" for why protecting that balance matters.</param>
    /// <param name="startingEarmarkCeiling">The most a proposed starting (front-load) earmark may set aside, from AffordabilityCeiling.ForStartingEarmark; null leaves it uncapped (the full first-occurrence amount), the default for callers not doing an affordability-sized proposal.</param>
    /// <param name="ongoingRateCeiling">The most the plan's per-cycle contribution may reserve, from AffordabilityCeiling.For (the range ceiling, as room FOR this plan); when the paced/spread rate would exceed it the plan is held to it instead and knowingly underfunds the outflow. Null leaves the rate uncapped. A transfer's own immediate single contribution is never capped — it stays plain regardless. Same conservative same-day reading the scaling caps use: one cycle's contribution against the window's tightest free point, not a full cumulative solve.</param>
    public static ProposedAllocationPlan Propose(
        FinancialPattern outflow,
        IReadOnlyList<FinancialPattern> allPatterns,
        DateOnly asOfDate,
        bool spreadEvenlyWithNoIncome = true,
        decimal carriedOverJarBalance = 0m,
        decimal? startingEarmarkCeiling = null,
        decimal? ongoingRateCeiling = null)
    {
        if (outflow.Amount >= 0m)
        {
            throw new ArgumentException(
                "An Allocation Plan is only proposed for an outflow (negative Amount).",
                nameof(outflow));
        }

        // Stretch the outflow's active span back to the as-of date when it starts
        // in the future, so a plan that begins accumulating today fits inside it
        // (an outflow already covering today keeps a null ActiveFrom). The
        // prepared outflow is returned so the caller persists it, not the
        // original; occurrences are untouched.
        var preparedOutflow = outflow.DatePattern.ActiveStart > asOfDate
            ? outflow.WithActiveFrom(asOfDate)
            : outflow;

        var billAmount = Math.Abs(preparedOutflow.Amount);
        var billUntil = preparedOutflow.DatePattern.Until;

        var incomePatterns = ActiveIncomeStreams(allPatterns, asOfDate, billUntil);

        if (incomePatterns.Count == 1)
        {
            var income = incomePatterns[0];
            var planUntil = income.DatePattern.Until < billUntil ? income.DatePattern.Until : billUntil;
            var paydayCount = income.DatePattern.GetOccurrences(asOfDate, planUntil).Count;
            var billOccurrenceCount = preparedOutflow.DatePattern.GetOccurrences(asOfDate, planUntil).Count;

            if (paydayCount > 0 && billOccurrenceCount > 0)
            {
                return WithStartingAllocation(
                    ProposePaced(preparedOutflow, income, billAmount, planUntil, paydayCount, billOccurrenceCount, asOfDate, startingEarmarkCeiling, ongoingRateCeiling),
                    carriedOverJarBalance);
            }
        }

        return WithStartingAllocation(
            ProposeFrontLoaded(preparedOutflow, billAmount, billUntil, asOfDate, spreadEvenlyWithNoIncome, ongoingRateCeiling),
            carriedOverJarBalance);
    }

    /// <summary>[CALC] Holds a proposed per-cycle contribution under an affordability ceiling — returns the ceiling when a positive one is given and the rate would exceed it, the rate itself otherwise (no ceiling, or it already fits). A ceiling of 0 or less never binds, so the returned rate is always positive whenever the rate coming in was.</summary>
    /// <param name="rate">The uncapped per-cycle contribution magnitude (a positive number).</param>
    /// <param name="ceiling">The most that may be reserved per cycle, or null for uncapped.</param>
    private static decimal RateUnderCeiling(decimal rate, decimal? ceiling) =>
        ceiling is decimal cap && cap > 0m && rate > cap ? cap : rate;

    /// <summary>[CALC] Applies a carried-over balance to an already-built proposal's own plan, as its StartingAllocation — a no-op (the exact same instance back) when there's nothing to carry over, so every caller that never passes one gets byte-for-byte the same result as before this parameter existed. Deliberately does NOT touch MaybeStartingEarmark's own separate one-off top-up (ProposePaced's own Shape A can still propose one on top of a nonzero StartingAllocation applied here) — that method only ever reasons about SCHEDULE TIMING (does the plan's own first contribution land late), not about whether a big enough carried-over balance already covers the gap on its own. Narrow, flagged rather than fixed: the two could theoretically double up for a goal that both has a single clear income stream funding it AND is being re-proposed with a real carried-over balance AND has its very next bill due before the next payday — teaching MaybeStartingEarmark about dollar-sufficiency, not just timing, is a bigger change than this fix is about.</summary>
    /// <param name="proposal">The already-built proposal to apply the balance to.</param>
    /// <param name="carriedOverJarBalance">What to carry forward as the plan's own StartingAllocation.</param>
    private static ProposedAllocationPlan WithStartingAllocation(ProposedAllocationPlan proposal, decimal carriedOverJarBalance)
    {
        if (carriedOverJarBalance == 0m)
        {
            return proposal;
        }

        var planWithBalance = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = proposal.Plan.FinanceId,
                DatePattern = proposal.Plan.DatePattern,
                Amount = proposal.Plan.Amount,
                StartingAllocation = carriedOverJarBalance,
                // Auto-proposed, not made by the user — see EarMarkPattern.ExplicitlyCreated.
                ExplicitlyCreated = false,
            },
            proposal.Outflow);

        return proposal with { Plan = planWithBalance };
    }

    /// <summary>[CALC] Proposes a plan that keeps the existing plan's own recurrence shape — frequency, interval, which weekday or month-day — re-anchored to today without losing phase, with a freshly-computed Amount sized to exactly cover what's left of the goal net of what's already banked. Null when there's nothing left to fund, when the existing shape has no occurrence left to re-anchor to inside the goal's own remaining window, or when the result wouldn't be meaningfully different from Propose's own default.</summary>
    /// <param name="outflow">The goal being funded.</param>
    /// <param name="existingPlan">The plan currently in effect, whose recurrence shape is preserved.</param>
    /// <param name="carriedOverJarBalance">What the jar actually holds right now — read by the caller off the live forecast, same as BreakOffFactory's own field of the same name. NOT existingPlan's own StartingAllocation: that's a static field frozen at whenever the plan was first created, while this carries forward everything that's actually happened since (scheduled contributions, releases, prior manual earmarks) — the whole reason this method exists is to replace a plan that's had a real history, so a stale snapshot from its own creation would under- or over-state what's genuinely already banked.</param>
    /// <param name="manualEarmarksForThisGoal">Every manual earmark tied to this goal, dated AFTER asOfDate and on or before the due date — earmarks on or before asOfDate are already folded into carriedOverJarBalance, so counting them again here would double them.</param>
    /// <param name="allPatterns">Every pattern — passed through to Propose for the "is this actually different" check.</param>
    /// <param name="asOfDate">Today, or the forecast's as-of date — where the re-anchored schedule starts from.</param>
    public static ProposedAllocationPlan? ProposeSameSchedule(
        FinancialPattern outflow,
        EarMarkPattern existingPlan,
        decimal carriedOverJarBalance,
        IReadOnlyList<ManualEarmark> manualEarmarksForThisGoal,
        IReadOnlyList<FinancialPattern> allPatterns,
        DateOnly asOfDate)
    {
        var dueDate = outflow.DatePattern.Until;
        var alignedSchedule = AlignedSchedule(existingPlan.DatePattern, asOfDate, dueDate);
        if (alignedSchedule is null || alignedSchedule.DtStart > dueDate)
        {
            return null;
        }

        try
        {
            var scheduleRule = RecurrenceRule.Create(alignedSchedule);
            var occurrenceCount = scheduleRule.GetOccurrences().Count;
            if (occurrenceCount == 0)
            {
                return null;
            }

            var totalReleases = Math.Abs(outflow.Amount)
                * outflow.DatePattern.GetOccurrences(alignedSchedule.DtStart, dueDate).Count;
            var alreadyBanked = carriedOverJarBalance
                + manualEarmarksForThisGoal.Where(manual => manual.Date > asOfDate && manual.Date <= dueDate).Sum(manual => manual.Amount);
            var totalNeeded = Math.Max(0m, totalReleases - alreadyBanked);

            if (totalNeeded == 0m)
            {
                return null;
            }

            var plan = EarMarkPattern.Create(
                new EarMarkPatternOptions
                {
                    FinanceId = outflow.FinanceId,
                    DatePattern = scheduleRule,
                    Amount = -Math.Round(totalNeeded / occurrenceCount, 2),
                    StartingAllocation = carriedOverJarBalance,
                    ExplicitlyCreated = false, // auto-proposed, not made by the user
                },
                outflow);

            return MatchesDefaultProposal(plan, outflow, allPatterns, asOfDate)
                ? null
                : new ProposedAllocationPlan(outflow, plan, null);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>[CALC] Proposes a plan that keeps the existing plan's own Amount unchanged, on a freshly-computed cadence sized to exactly cover what's left of the goal net of what's already banked — evenly-spaced Daily-interval occurrences, not paced to any existing pattern, so there is no recurrence shape to lose phase on. Null when there's nothing left to fund, when even daily contributions of the existing Amount can't reach the target before the goal's own due date, or when the result wouldn't be meaningfully different from Propose's own default.</summary>
    /// <param name="outflow">The goal being funded.</param>
    /// <param name="existingPlan">The plan currently in effect, whose Amount is preserved.</param>
    /// <param name="carriedOverJarBalance">What the jar actually holds right now — see ProposeSameSchedule's own parameter of the same name for why this is not existingPlan.StartingAllocation.</param>
    /// <param name="manualEarmarksForThisGoal">Every manual earmark tied to this goal, dated AFTER asOfDate and on or before the due date — see ProposeSameSchedule's own parameter of the same name for why.</param>
    /// <param name="allPatterns">Every pattern — passed through to Propose for the "is this actually different" check.</param>
    /// <param name="asOfDate">Today, or the forecast's as-of date — where the new schedule starts from.</param>
    public static ProposedAllocationPlan? ProposeSameAmount(
        FinancialPattern outflow,
        EarMarkPattern existingPlan,
        decimal carriedOverJarBalance,
        IReadOnlyList<ManualEarmark> manualEarmarksForThisGoal,
        IReadOnlyList<FinancialPattern> allPatterns,
        DateOnly asOfDate)
    {
        var fixedAmount = Math.Abs(existingPlan.Amount);
        if (fixedAmount == 0m)
        {
            return null; // nothing to solve a cadence for
        }

        var dueDate = outflow.DatePattern.Until;
        var totalReleases = Math.Abs(outflow.Amount) * outflow.DatePattern.GetOccurrences(asOfDate, dueDate).Count;
        var alreadyBanked = carriedOverJarBalance
            + manualEarmarksForThisGoal.Where(manual => manual.Date > asOfDate && manual.Date <= dueDate).Sum(manual => manual.Amount);
        var totalNeeded = Math.Max(0m, totalReleases - alreadyBanked);

        if (totalNeeded == 0m)
        {
            return null;
        }

        var occurrencesNeeded = (int)Math.Ceiling(totalNeeded / fixedAmount);

        RecurrenceRuleOptions schedule;
        if (occurrencesNeeded <= 1)
        {
            schedule = new RecurrenceRuleOptions { Frequency = RecurrenceFrequency.Yearly, DtStart = asOfDate, Count = 1 };
        }
        else
        {
            // Evenly spaced, spanning the whole window — the last of the N
            // occurrences lands on or before dueDate (floor division), never
            // after. Daily/Interval sidesteps AlignedSchedule entirely: it
            // has no implicit day-of-week/month-day to lose phase on.
            var windowDays = dueDate.DayNumber - asOfDate.DayNumber;
            var interval = windowDays / (occurrencesNeeded - 1);
            if (interval < 1)
            {
                return null; // even daily contributions can't fit that many occurrences before the due date
            }

            schedule = new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Daily,
                Interval = interval,
                DtStart = asOfDate,
                Count = occurrencesNeeded,
            };
        }

        try
        {
            var plan = EarMarkPattern.Create(
                new EarMarkPatternOptions
                {
                    FinanceId = outflow.FinanceId,
                    DatePattern = RecurrenceRule.Create(schedule),
                    Amount = -fixedAmount,
                    StartingAllocation = carriedOverJarBalance,
                    ExplicitlyCreated = false, // auto-proposed, not made by the user
                },
                outflow);

            return MatchesDefaultProposal(plan, outflow, allPatterns, asOfDate)
                ? null
                : new ProposedAllocationPlan(outflow, plan, null);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // The empty (declined) plan. When the user removes or declines the proposed
    // Allocation Plan, the outflow STILL gets a real EarMarkPattern — one with no
    // contributions — plus a fund jar visible from today, so the money still
    // reads as unspent, there is a jar to top up, and the shortfall is the full
    // unfunded amount. The plan is a plain Count = 1 rrule at the outflow's
    // Until with Amount = 0; ActiveFrom does the span work so the rrule stays
    // ordinary (no empty-rrule type). The single $0 earmark event it emits at
    // Until is the accepted cost of keeping it plain.
    /// <summary>[CALC] Gives an outflow a real (but empty) savings plan when the proposed one is declined, so its jar still exists and can be topped up by hand.</summary>
    /// <param name="outflow">The outflow to give an empty plan.</param>
    /// <param name="asOfDate">Today, or the forecast's as-of date — the jar becomes visible from here rather than only on the outflow's last day.</param>
    public static ProposedAllocationPlan ProposeEmpty(FinancialPattern outflow, DateOnly asOfDate)
    {
        if (outflow.Amount >= 0m)
        {
            throw new ArgumentException(
                "An Allocation Plan is only created for an outflow (negative Amount).",
                nameof(outflow));
        }

        // Same sparing rule as Propose: stretch the outflow's active span back to
        // today only when it starts in the future, so its jar is visible now.
        var preparedOutflow = outflow.DatePattern.ActiveStart > asOfDate
            ? outflow.WithActiveFrom(asOfDate)
            : outflow;

        var planStart = preparedOutflow.DatePattern.Until;

        var planPattern = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            // Frequency is immaterial for a single occurrence; Yearly matches the
            // proposer's other Count = 1 plan.
            Frequency = RecurrenceFrequency.Yearly,
            DtStart = planStart,
            Count = 1,
            // Reach the jar's span back to today so the jar exists now, not only
            // on the outflow's last day — but only when that day is in the future
            // (ActiveFrom must be <= Start; the same sparing rule again).
            ActiveFrom = planStart > asOfDate ? asOfDate : null,
        });

        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = preparedOutflow.FinanceId,
                DatePattern = planPattern,
                Amount = 0m,
                ExplicitlyCreated = false, // auto-proposed (declined/empty), not made by the user
            },
            preparedOutflow);

        return new ProposedAllocationPlan(preparedOutflow, plan, null);
    }

    // 2026-08-17: the first piece of "loose association" (planning's own
    // paycheck-cascade thread) — detecting an EXISTING plan's association
    // with an income, not proposing a new one. No stored link exists
    // anywhere in EarMarkPatternOptions for this; IsPacedAgainst re-derives
    // it live by checking the same relationship ProposePaced builds going
    // forward. This is a first-pass heuristic (see its own comment below for
    // what it does and doesn't catch), not a settled definition — it exists
    // so a future "your paycheck changed, want to re-pace these bills too?"
    // suggestion has something to detect candidates with. That suggestion
    // itself, and any cascade/orchestration around it, is NOT built here.

    /// <summary>[CALC] Reports whether a plan's own schedule coincides with an income's own paydays — the inverse of ProposePaced's own forward construction, for finding an association no stored link records.</summary>
    /// <param name="plan">The plan to check.</param>
    /// <param name="income">The candidate income stream to check it against.</param>
    public static bool IsPacedAgainst(EarMarkPattern plan, FinancialPattern income)
    {
        if (income.Amount <= 0m)
        {
            return false; // not actually income
        }

        var planDates = plan.DatePattern.GetOccurrences();
        if (planDates.Count == 0)
        {
            return false; // nothing to compare — never "paced" by construction
        }

        // Every one of the plan's own occurrences must land on one of the
        // income's own paydays — the exact converse of AlignedSchedule's own
        // forward construction (copies income's Frequency/Interval/ByDay/
        // ByMonthDay, re-anchored at one of income's own real occurrences).
        // A plan ProposePaced actually built satisfies this by construction,
        // so this also catches a hand-edited plan that still lines up — and
        // correctly misses a same-shape-but-different-phase stream (e.g. two
        // biweekly incomes paying on alternating weeks) that a bare shape
        // comparison alone would not. Requiring EVERY occurrence to match,
        // not just most, is the strictest reading and the safest starting
        // point; how much drift (a skipped date, a one-off manual edit)
        // should still count as "still paced against it" is an open
        // question this doesn't attempt to answer.
        var paydays = income.DatePattern.GetOccurrences(plan.DatePattern.ActiveStart, plan.DatePattern.Until).ToHashSet();
        return planDates.All(paydays.Contains);
    }

    /// <summary>[CALC] Finds which of a household's plans are paced against the given income — IsPacedAgainst applied across every plan, for offering a suggestion after the income itself changes.</summary>
    /// <param name="income">The income stream whose associated plans to find.</param>
    /// <param name="allPlans">Every EarMarkPattern in the household to check.</param>
    public static IReadOnlyList<EarMarkPattern> FindPlansPacedAgainst(FinancialPattern income, IReadOnlyList<EarMarkPattern> allPlans) =>
        allPlans.Where(plan => IsPacedAgainst(plan, income)).ToList();

    /// <summary>[CALC] Shape A: builds a plan with one contribution per payday, sized so the window's contributions equal the window's bill consumption.</summary>
    /// <param name="outflow">The outflow being funded.</param>
    /// <param name="income">The single income stream to pace against.</param>
    /// <param name="billAmount">The outflow's own amount, per occurrence.</param>
    /// <param name="planUntil">When the plan stops — the earlier of the income's and the bill's own end.</param>
    /// <param name="paydayCount">How many paydays fall in the window.</param>
    /// <param name="billOccurrenceCount">How many bill occurrences fall in the window.</param>
    /// <param name="asOfDate">Today, or the forecast's as-of date.</param>
    /// <param name="startingEarmarkCeiling">Cap for the front-load earmark, passed through to MaybeStartingEarmark; null leaves it uncapped.</param>
    /// <param name="ongoingRateCeiling">Cap for the per-payday contribution; when the paced rate would exceed it the plan is held to it and knowingly underfunds. Null leaves it uncapped.</param>
    private static ProposedAllocationPlan ProposePaced(
        FinancialPattern outflow,
        FinancialPattern income,
        decimal billAmount,
        DateOnly planUntil,
        int paydayCount,
        int billOccurrenceCount,
        DateOnly asOfDate,
        decimal? startingEarmarkCeiling,
        decimal? ongoingRateCeiling)
    {
        // Held under the affordability ceiling — a bill we can't fully pace for stays knowingly underfunded
        // (the forecast then reports the shortfall) rather than reserving money that isn't there.
        var perPayday = RateUnderCeiling(Math.Round(billAmount * billOccurrenceCount / paydayCount, 2), ongoingRateCeiling);

        // Fixed 2026-08-14 — was: DtStart = asOfDate here, blindly. Copying
        // income's own Frequency/Interval/ByDay/ByMonthDay but anchoring at
        // asOfDate instead of a real payday silently loses phase whenever
        // income relies on RFC 5545's implicit "omitted BYDAY defaults to
        // DTSTART's own weekday" (true for any interval-anchored income with
        // no explicit ByDay, e.g. a biweekly paycheck) — confirmed live:
        // income paydays Jan 3/17/31 (Fridays) vs. the old code's own plan
        // landing Jan 1/15/29 (Wednesdays), same scenario as
        // Biweekly_income_against_a_monthly_bill_paces_below_the_full_amount.
        // AlignedSchedule (below) re-anchors at income's own next real payday
        // instead. paydayCount > 0 already guarantees one exists on or after
        // asOfDate within [asOfDate, planUntil], so this can't come back null.
        var planPattern = RecurrenceRule.Create(
            AlignedSchedule(income.DatePattern, asOfDate, planUntil)
            ?? throw new InvalidOperationException("Unreachable: Propose already confirmed paydayCount > 0 in this window."));

        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = outflow.FinanceId,
                // Negative = money moves INTO the jar (the pattern's sign
                // convention is "effect on free balance"; the factory flips it).
                DatePattern = planPattern,
                Amount = -perPayday,
                ExplicitlyCreated = false, // auto-proposed, not made by the user
            },
            outflow);

        return new ProposedAllocationPlan(outflow, plan, MaybeStartingEarmark(outflow, plan, billAmount, asOfDate, startingEarmarkCeiling));
    }

    /// <summary>[CALC] Shape C: reserves the full amount every bill cycle, starting at the as-of date so the first contribution reserves the whole amount up front. A single-occurrence outflow instead spreads or reserves immediately, per <paramref name="spreadEvenlyWithNoIncome"/>.</summary>
    /// <param name="outflow">The outflow being funded.</param>
    /// <param name="billAmount">The outflow's own amount, per occurrence.</param>
    /// <param name="billUntil">The outflow's own end date.</param>
    /// <param name="asOfDate">Today, or the forecast's as-of date.</param>
    /// <param name="spreadEvenlyWithNoIncome">For a single-occurrence outflow: whether to spread the amount evenly across the remaining time, or reserve it all immediately.</param>
    /// <param name="ongoingRateCeiling">Cap for the per-cycle contribution — applied to a recurring outflow's own full-amount reservation and to a spread goal's installments, but NOT to a transfer's own immediate single contribution, which stays plain. Null leaves the rate uncapped.</param>
    private static ProposedAllocationPlan ProposeFrontLoaded(
        FinancialPattern outflow,
        decimal billAmount,
        DateOnly billUntil,
        DateOnly asOfDate,
        bool spreadEvenlyWithNoIncome,
        decimal? ongoingRateCeiling)
    {
        // A single-occurrence outflow (a one-time goal, a one-off bill, a
        // one-off transfer) never generates one full contribution per
        // frequency cycle between now and the due date — that would
        // over-reserve many times over. What it does instead differs by
        // kind: a one-time goal or one-off bill spreads the amount evenly
        // across the remaining time, mirroring
        // OneTimeGoalFactory's own long-standing default — reserving a large,
        // long-dated, discretionary amount all at once is wrong for the same
        // reason a boat can't be saved for in one contribution the way a TV
        // can. A transfer's withdrawal is the one exception: it keeps
        // reserving the whole amount immediately (spreadEvenlyWithNoIncome
        // false), since a transfer is a plain, deliberate move with no
        // adaptive behavior. A genuinely recurring outflow with no usable
        // income (the multi-occurrence branch below) is unaffected either
        // way — it already reserves the full amount per cycle.
        var isSingleOccurrence =
            outflow.DatePattern.GetOccurrences(outflow.DatePattern.ActiveStart, billUntil).Count <= 1;

        if (isSingleOccurrence)
        {
            return spreadEvenlyWithNoIncome
                ? ProposeSpreadEvenly(outflow, billAmount, billUntil, asOfDate, ongoingRateCeiling)
                : ProposeImmediateSingleContribution(outflow, billAmount, billUntil, asOfDate);
        }

        var planPattern = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = outflow.DatePattern.Frequency,
            Interval = outflow.DatePattern.Interval,
            DtStart = asOfDate,
            Until = billUntil,
        });

        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = outflow.FinanceId,
                DatePattern = planPattern,
                // Held under the affordability ceiling — a recurring bill we can't fully reserve for stays
                // knowingly underfunded rather than drawing money that isn't there.
                Amount = -RateUnderCeiling(billAmount, ongoingRateCeiling),
                ExplicitlyCreated = false, // auto-proposed, not made by the user
            },
            outflow);

        // The first contribution is at the as-of date (front-loaded), so nothing
        // lands before it — no starting earmark needed.
        return new ProposedAllocationPlan(outflow, plan, null);
    }

    /// <summary>[CALC] Builds a plan with a single contribution at the as-of date, for the whole amount — a transfer's withdrawal keeps this shape, with no adaptive spreading.</summary>
    /// <param name="outflow">The outflow being funded.</param>
    /// <param name="billAmount">The outflow's own amount.</param>
    /// <param name="billUntil">The outflow's own end date.</param>
    /// <param name="asOfDate">Today, or the forecast's as-of date.</param>
    private static ProposedAllocationPlan ProposeImmediateSingleContribution(
        FinancialPattern outflow, decimal billAmount, DateOnly billUntil, DateOnly asOfDate)
    {
        var planPattern = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Yearly,
            // Guard a past-dated one-off: the plan can't end after the
            // outflow it funds (EarMarkPattern.Create / 3.11.2.a2).
            DtStart = asOfDate <= billUntil ? asOfDate : billUntil,
            Count = 1,
        });

        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = outflow.FinanceId,
                DatePattern = planPattern,
                Amount = -billAmount,
                ExplicitlyCreated = false, // auto-proposed, not made by the user
            },
            outflow);

        return new ProposedAllocationPlan(outflow, plan, null);
    }

    /// <summary>[CALC] Builds a plan with monthly installments from the as-of date (or the due date, if it's already past) to the due date — mirrors OneTimeGoalFactory's own default cadence.</summary>
    /// <param name="outflow">The outflow being funded.</param>
    /// <param name="billAmount">The outflow's own amount, split evenly across installments.</param>
    /// <param name="billUntil">The outflow's own end date.</param>
    /// <param name="asOfDate">Today, or the forecast's as-of date.</param>
    /// <param name="ongoingRateCeiling">Cap for each installment; when an even split would exceed it the plan is held to it and knowingly underfunds. Null leaves it uncapped.</param>
    private static ProposedAllocationPlan ProposeSpreadEvenly(
        FinancialPattern outflow, decimal billAmount, DateOnly billUntil, DateOnly asOfDate, decimal? ongoingRateCeiling)
    {
        var start = asOfDate <= billUntil ? asOfDate : billUntil;
        var installmentPattern = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            ByMonthDay = [start.Day],
            DtStart = start,
            Until = billUntil,
        });
        var occurrenceCount = installmentPattern.GetOccurrences().Count;
        // Held under the affordability ceiling — a goal we can't fully spread for stays knowingly
        // underfunded rather than reserving money that isn't there.
        var installmentAmount = RateUnderCeiling(Math.Round(billAmount / occurrenceCount, 2), ongoingRateCeiling);

        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = outflow.FinanceId,
                DatePattern = installmentPattern,
                Amount = -installmentAmount,
                ExplicitlyCreated = false, // auto-proposed, not made by the user
            },
            outflow);

        return new ProposedAllocationPlan(outflow, plan, null);
    }

    /// <summary>[CALC] Returns a starting earmark to front-load the first bill occurrence, if it falls before the plan's own first contribution (shape A, when the bill is due before the next paycheck) — capped at the affordability ceiling when one is given, and null when even a partial front-load can't be afforded (or none is needed).</summary>
    /// <param name="outflow">The outflow being funded.</param>
    /// <param name="plan">The proposed plan.</param>
    /// <param name="billAmount">The outflow's own amount, to front-load.</param>
    /// <param name="asOfDate">Today, or the forecast's as-of date — where the starting earmark is dated.</param>
    /// <param name="ceiling">The most that can be front-loaded on asOfDate; null leaves the full amount uncapped.</param>
    private static ManualEarmark? MaybeStartingEarmark(
        FinancialPattern outflow,
        EarMarkPattern plan,
        decimal billAmount,
        DateOnly asOfDate,
        decimal? ceiling)
    {
        var firstBill = FirstOccurrenceOnOrAfter(outflow.DatePattern, asOfDate);
        if (firstBill is null)
        {
            return null;
        }

        var firstContribution = FirstOccurrenceOnOrAfter(plan.DatePattern, asOfDate);
        if (firstContribution is not null && firstContribution <= firstBill)
        {
            // The plan already contributes on or before the first occurrence.
            return null;
        }

        // Front-load the full first occurrence, but never more than the day can spare; at or below zero
        // (an already-over-committed day) even a partial front-load can't be placed.
        var amount = ceiling is decimal cap ? Math.Min(billAmount, cap) : billAmount;
        if (amount <= 0m)
        {
            return null;
        }

        return ManualEarmark.Create(
            new ManualEarmarkOptions
            {
                FinanceId = outflow.FinanceId,
                Date = asOfDate,
                Amount = amount,
            },
            plan);
    }

    /// <summary>[CALC] Returns a rule's first occurrence on or after a date, or null if it has none.</summary>
    /// <param name="rule">The rule to search.</param>
    /// <param name="from">The earliest date to consider.</param>
    private static DateOnly? FirstOccurrenceOnOrAfter(RecurrenceRule rule, DateOnly from)
    {
        var occurrences = rule.GetOccurrences(from, rule.Until);
        return occurrences.Count > 0 ? occurrences[0] : null;
    }

    /// <summary>[CALC] Re-anchors a reference pattern's own recurrence shape at a new start date without losing phase — finds the reference's own next real occurrence on or after the desired date and uses THAT as the new Start, rather than the raw desired date itself. Safe even when the reference's ByDay/ByMonthDay was never set explicitly: RFC 5545 then implicitly ties an omitted BYDAY to DTSTART's own weekday, which a raw new Start would silently change out from under it (see ProposePaced's own header comment for the confirmed bug this fixes). Also the only safe way to re-anchor an Interval > 1 Weekly rule at all, even with an EXPLICIT ByDay: confirmed empirically 2026-08-17 (RecurrenceRuleTests.Explicit_byday_alone_does_not_protect_an_intervals_own_week_phase_when_start_is_pinned_elsewhere) that "every Nth week" is counted from DTSTART's own calendar week regardless of ByDay, so a raw new Start can land a whole interval-step off even when the weekday itself is spelled out. Extends the reference's own Until first when the caller needs a later window than the reference itself currently reaches — the reference's SHAPE is what's being preserved, not its own current end date. When the aligned occurrence lands after desiredStart, sets ActiveFrom back to desiredStart — same reasoning as Propose's own outflow-preparation step — so the jar still reads as alive (and a starting earmark can still be dated) from desiredStart onward, not only from the first real contribution. Public since 2026-08-17: FinancePatternSaveConfirmation.BuildSuccessorSchedule reuses this directly for a break-off successor's own Weekly schedule, rather than duplicating the logic — see that method's own comment for why a break-off successor needed the exact same fix.</summary>
    /// <param name="reference">The pattern whose recurrence shape (and phase) to preserve.</param>
    /// <param name="desiredStart">Where the new schedule should start from, ideally.</param>
    /// <param name="until">The new schedule's own end date.</param>
    /// <returns>A schedule with the reference's own shape, anchored at the reference's nearest real occurrence on or after desiredStart — or null when the reference has no such occurrence within the window.</returns>
    public static RecurrenceRuleOptions? AlignedSchedule(RecurrenceRule reference, DateOnly desiredStart, DateOnly until)
    {
        var extendedReference = until > reference.Until ? reference.WithUntil(until) : reference;
        if (FirstOccurrenceOnOrAfter(extendedReference, desiredStart) is not { } alignedStart)
        {
            return null;
        }

        return new RecurrenceRuleOptions
        {
            Frequency = reference.Frequency,
            Interval = reference.Interval,
            ByDay = reference.ByDay,
            ByMonthDay = reference.ByMonthDay,
            DtStart = alignedStart,
            ActiveFrom = alignedStart > desiredStart ? desiredStart : null,
            Until = until,
        };
    }

    /// <summary>[CALC] Whether a candidate plan is functionally identical to what Propose would build fresh for the same outflow — same Amount and the same recurrence shape. A candidate this close to the default isn't a meaningfully different option to offer alongside it.</summary>
    /// <param name="candidate">The plan to compare.</param>
    /// <param name="outflow">The outflow to compare it against Propose's own default for.</param>
    /// <param name="allPatterns">Every pattern, passed through to Propose unchanged.</param>
    /// <param name="asOfDate">Today, or the forecast's as-of date, passed through to Propose unchanged.</param>
    private static bool MatchesDefaultProposal(
        EarMarkPattern candidate, FinancialPattern outflow, IReadOnlyList<FinancialPattern> allPatterns, DateOnly asOfDate)
    {
        var defaultPlan = Propose(outflow, allPatterns, asOfDate).Plan;
        return candidate.Amount == defaultPlan.Amount
            && candidate.DatePattern.Frequency == defaultPlan.DatePattern.Frequency
            && candidate.DatePattern.Interval == defaultPlan.DatePattern.Interval
            && candidate.DatePattern.ActiveStart == defaultPlan.DatePattern.ActiveStart
            && candidate.DatePattern.Until == defaultPlan.DatePattern.Until
            && candidate.DatePattern.ByDay.SequenceEqual(defaultPlan.DatePattern.ByDay)
            && candidate.DatePattern.ByMonthDay.SequenceEqual(defaultPlan.DatePattern.ByMonthDay);
    }
}
