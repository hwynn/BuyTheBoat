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
// Design: planning/14 "Revision 2026-07-24" (F22). ONE function for both goals
// and bills — a one-time goal is just a FinancialPattern with a single
// occurrence, so it falls out of the same logic (billOccurrences = 1). The
// proposer only ever fills in a DatePattern and an Amount; everything
// downstream (the FundJar, its MilestoneAmount via 3.13.5.4.a1, the shortfall)
// is identical no matter which case fires, so the two shapes add NO new
// machinery. Supersedes OneTimeGoalFactory's fixed-Monthly installment default.
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
//     earmark needed). A SINGLE-occurrence outflow (planning/18, C1) instead
//     spreads the amount evenly across the remaining time by default —
//     mirroring OneTimeGoalFactory's own long-standing installment default,
//     which this genuinely supersedes now, not just for the paced case — a
//     one-time goal or one-off bill should never lock up a large, long-dated
//     amount all at once. The one exception is a transfer's withdrawal
//     (`spreadEvenlyWithNoIncome: false`): a transfer stays plain and
//     immediate, with no adaptive behavior, by the author's own ruling.
//
// Divide-by-zero (F22) is structurally impossible: incomeOccurrences is a
// divisor only inside shape A, which is entered only when exactly one income
// stream has at least one occurrence in the window; the new spread-evenly
// shape divides by an occurrence count that's always >= 1 by construction
// (the installment pattern's own Start is always its first occurrence).
//
// Documented v1 simplifications (defensible defaults, refine later):
//  - "More than one income stream" is treated as shape C rather than the
//    "arbitrary post-income/pre-bill point" the design sketches; the front-
//    loaded shape is a safe, if conservative, default and the user can edit it.
//  - Shape A anchors the plan's DatePattern.Start at asOfDate and copies the
//    income's recurrence fields. For calendar-anchored income (monthly on a
//    day, weekly on a weekday) that lands exactly on paydays; for interval-
//    anchored income (e.g. biweekly) the plan may be phase-shifted from the
//    real paydays by a few days — the per-cycle AMOUNT is unaffected, which is
//    what governs adequacy.
//  - F16 (an income stream that ends before the bill does) is not handled: the
//    plan is bounded by min(income.Until, bill.Until), so occurrences past the
//    income's end simply aren't funded and the shortfall reports them.
public static class AllocationPlanProposer
{
    public static ProposedAllocationPlan Propose(
        FinancialPattern outflow,
        IReadOnlyList<FinancialPattern> allPatterns,
        DateOnly asOfDate,
        // planning/18 (C1): a single-occurrence outflow with no clean income
        // to pace against spreads evenly across the remaining time by
        // default (a one-time goal or one-off bill) — but a transfer's
        // withdrawal must keep reserving immediately, in full: transfers are
        // deliberately plain, with no adaptive behavior (author, 2026-07-30
        // — "no shortcuts beyond making it easy to create a transfer once or
        // on a schedule"). Callers proposing a transfer's withdrawal pass
        // false.
        bool spreadEvenlyWithNoIncome = true)
    {
        if (outflow.Amount >= 0m)
        {
            throw new ArgumentException(
                "An Allocation Plan is only proposed for an outflow (negative Amount).",
                nameof(outflow));
        }

        // Stretch the outflow's active span back to the as-of date when it starts
        // in the future, so a plan that begins accumulating today fits inside it
        // (planning/15, ActiveFrom + the sparing rule — an outflow already covering
        // today keeps a null ActiveFrom). The prepared outflow is returned so the
        // caller persists it, not the original; occurrences are untouched.
        var preparedOutflow = outflow.DatePattern.Start > asOfDate
            ? outflow.WithActiveFrom(asOfDate)
            : outflow;

        var billAmount = Math.Abs(preparedOutflow.Amount);
        var billUntil = preparedOutflow.DatePattern.Until;

        var incomePatterns = allPatterns.Where(pattern => pattern.Amount > 0m).ToList();

        if (incomePatterns.Count == 1)
        {
            var income = incomePatterns[0];
            var planUntil = income.DatePattern.Until < billUntil ? income.DatePattern.Until : billUntil;
            var paydayCount = income.DatePattern.GetOccurrences(asOfDate, planUntil).Count;
            var billOccurrenceCount = preparedOutflow.DatePattern.GetOccurrences(asOfDate, planUntil).Count;

            if (paydayCount > 0 && billOccurrenceCount > 0)
            {
                return ProposePaced(preparedOutflow, income, billAmount, planUntil, paydayCount, billOccurrenceCount, asOfDate);
            }
        }

        return ProposeFrontLoaded(preparedOutflow, billAmount, billUntil, asOfDate, spreadEvenlyWithNoIncome);
    }

    // The empty (declined) plan. When the user removes or declines the proposed
    // Allocation Plan, the outflow STILL gets a real EarMarkPattern — one with no
    // contributions — plus a fund jar visible from today, so the money still
    // reads as unspent, there is a jar to top up, and the shortfall is the full
    // unfunded amount (planning/15, ActiveFrom resolution). The plan is a plain
    // Count = 1 rrule at the outflow's Until with Amount = 0; ActiveFrom does the
    // span work so the rrule stays ordinary (no empty-rrule type). The single $0
    // earmark event it emits at Until is the accepted cost of keeping it plain.
    /// <summary>[CALC] Gives an outflow a real (but empty) savings plan when the proposed one is declined, so its jar still exists and can be topped up by hand.</summary>
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
        var preparedOutflow = outflow.DatePattern.Start > asOfDate
            ? outflow.WithActiveFrom(asOfDate)
            : outflow;

        var planStart = preparedOutflow.DatePattern.Until;

        var planPattern = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            // Frequency is immaterial for a single occurrence; Yearly matches the
            // proposer's other Count = 1 plan.
            Frequency = RecurrenceFrequency.Yearly,
            Start = planStart,
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
            },
            preparedOutflow);

        return new ProposedAllocationPlan(preparedOutflow, plan, null);
    }

    // Shape A: one contribution per payday, sized so the window's contributions
    // equal the window's bill consumption — bill × billOccs ÷ incomeOccs.
    private static ProposedAllocationPlan ProposePaced(
        FinancialPattern outflow,
        FinancialPattern income,
        decimal billAmount,
        DateOnly planUntil,
        int paydayCount,
        int billOccurrenceCount,
        DateOnly asOfDate)
    {
        var perPayday = Math.Round(billAmount * billOccurrenceCount / paydayCount, 2);

        var planPattern = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = income.DatePattern.Frequency,
            Interval = income.DatePattern.Interval,
            ByDay = income.DatePattern.ByDay,
            ByMonthDay = income.DatePattern.ByMonthDay,
            Start = asOfDate,
            Until = planUntil,
        });

        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = outflow.FinanceId,
                // Negative = money moves INTO the jar (the pattern's sign
                // convention is "effect on free balance"; the factory flips it).
                DatePattern = planPattern,
                Amount = -perPayday,
            },
            outflow);

        return new ProposedAllocationPlan(outflow, plan, MaybeStartingEarmark(outflow, plan, billAmount, asOfDate));
    }

    // Shape C: the full amount every bill cycle, offset from the bill's own
    // days (same Frequency/Interval, NOT its ByDay/ByMonthDay) so a contribution
    // never lands on a release day, and starting at asOfDate so the first
    // contribution reserves the whole amount up front — branch B, expressed as
    // an ordinary EarMarkPattern rather than a computed curve.
    private static ProposedAllocationPlan ProposeFrontLoaded(
        FinancialPattern outflow,
        decimal billAmount,
        DateOnly billUntil,
        DateOnly asOfDate,
        bool spreadEvenlyWithNoIncome)
    {
        // A single-occurrence outflow (a one-time goal, a one-off bill, a
        // one-off transfer) never generates one full contribution per
        // frequency cycle between now and the due date — that would
        // over-reserve many times over. What it does instead differs by kind
        // (planning/18, C1): a one-time goal or one-off bill spreads the
        // amount evenly across the remaining time, mirroring
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
            outflow.DatePattern.GetOccurrences(outflow.DatePattern.Start, billUntil).Count <= 1;

        if (isSingleOccurrence)
        {
            return spreadEvenlyWithNoIncome
                ? ProposeSpreadEvenly(outflow, billAmount, billUntil, asOfDate)
                : ProposeImmediateSingleContribution(outflow, billAmount, billUntil, asOfDate);
        }

        var planPattern = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = outflow.DatePattern.Frequency,
            Interval = outflow.DatePattern.Interval,
            Start = asOfDate,
            Until = billUntil,
        });

        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = outflow.FinanceId,
                DatePattern = planPattern,
                Amount = -billAmount,
            },
            outflow);

        // The first contribution is at the as-of date (front-loaded), so nothing
        // lands before it — no starting earmark needed.
        return new ProposedAllocationPlan(outflow, plan, null);
    }

    // A single contribution at the as-of date, for the whole amount — today's
    // original single-occurrence shape, kept as-is for a transfer's
    // withdrawal (planning/18, C1).
    private static ProposedAllocationPlan ProposeImmediateSingleContribution(
        FinancialPattern outflow, decimal billAmount, DateOnly billUntil, DateOnly asOfDate)
    {
        var planPattern = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Yearly,
            // Guard a past-dated one-off: the plan can't end after the
            // outflow it funds (EarMarkPattern.Create / 3.11.2.a2).
            Start = asOfDate <= billUntil ? asOfDate : billUntil,
            Count = 1,
        });

        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = outflow.FinanceId,
                DatePattern = planPattern,
                Amount = -billAmount,
            },
            outflow);

        return new ProposedAllocationPlan(outflow, plan, null);
    }

    // Monthly installments from the as-of date (or the due date, if it's
    // already past) to the due date — mirrors
    // OneTimeGoalFactory.BuildSavingsDatePattern's own default cadence. A
    // goal due within about a month reduces to a single installment on its
    // own, the same number front-loading would have produced, arrived at the
    // same way rather than as a special case.
    private static ProposedAllocationPlan ProposeSpreadEvenly(
        FinancialPattern outflow, decimal billAmount, DateOnly billUntil, DateOnly asOfDate)
    {
        var start = asOfDate <= billUntil ? asOfDate : billUntil;
        var installmentPattern = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            ByMonthDay = [start.Day],
            Start = start,
            Until = billUntil,
        });
        var occurrenceCount = installmentPattern.GetOccurrences().Count;
        var installmentAmount = Math.Round(billAmount / occurrenceCount, 2);

        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = outflow.FinanceId,
                DatePattern = installmentPattern,
                Amount = -installmentAmount,
            },
            outflow);

        return new ProposedAllocationPlan(outflow, plan, null);
    }

    // A starting earmark is needed only when the first bill occurrence falls
    // before the plan's first contribution (shape A, when the bill is due
    // before the next paycheck). It front-loads that first occurrence's amount,
    // dated at asOfDate — which is within the plan's span (the plan starts at
    // asOfDate), so ManualEarmark's span check passes.
    private static ManualEarmark? MaybeStartingEarmark(
        FinancialPattern outflow,
        EarMarkPattern plan,
        decimal billAmount,
        DateOnly asOfDate)
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

        return ManualEarmark.Create(
            new ManualEarmarkOptions
            {
                FinanceId = outflow.FinanceId,
                Date = asOfDate,
                Amount = billAmount,
            },
            plan);
    }

    private static DateOnly? FirstOccurrenceOnOrAfter(RecurrenceRule rule, DateOnly from)
    {
        var occurrences = rule.GetOccurrences(from, rule.Until);
        return occurrences.Count > 0 ? occurrences[0] : null;
    }
}
