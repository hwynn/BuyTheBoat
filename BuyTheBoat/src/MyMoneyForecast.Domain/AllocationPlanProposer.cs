namespace MyMoneyForecast.Domain;

// The default Allocation Plan proposed for a newly-created outflow, plus an
// optional one-off starting earmark to cover a first occurrence the plan
// can't accumulate for in time.
public sealed record ProposedAllocationPlan(EarMarkPattern Plan, ManualEarmark? StartingEarmark);

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
//   C (no single usable income — none, or more than one stream) — reserve the
//     full amount, on the bill's own cadence, starting from the as-of date so
//     the first contribution front-loads. No starting earmark needed.
//
// Divide-by-zero (F22) is structurally impossible: incomeOccurrences is a
// divisor only inside shape A, which is entered only when exactly one income
// stream has at least one occurrence in the window.
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
        DateOnly asOfDate)
    {
        if (outflow.Amount >= 0m)
        {
            throw new ArgumentException(
                "An Allocation Plan is only proposed for an outflow (negative Amount).",
                nameof(outflow));
        }

        var billAmount = Math.Abs(outflow.Amount);
        var billUntil = outflow.DatePattern.Until;

        var incomePatterns = allPatterns.Where(pattern => pattern.Amount > 0m).ToList();

        if (incomePatterns.Count == 1)
        {
            var income = incomePatterns[0];
            var planUntil = income.DatePattern.Until < billUntil ? income.DatePattern.Until : billUntil;
            var paydayCount = income.DatePattern.GetOccurrences(asOfDate, planUntil).Count;
            var billOccurrenceCount = outflow.DatePattern.GetOccurrences(asOfDate, planUntil).Count;

            if (paydayCount > 0 && billOccurrenceCount > 0)
            {
                return ProposePaced(outflow, income, billAmount, planUntil, paydayCount, billOccurrenceCount, asOfDate);
            }
        }

        return ProposeFrontLoaded(outflow, billAmount, billUntil, asOfDate);
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

        return new ProposedAllocationPlan(plan, MaybeStartingEarmark(outflow, plan, billAmount, asOfDate));
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
        DateOnly asOfDate)
    {
        // A single-occurrence outflow (a one-time expense, a one-off transfer)
        // reserves its whole amount once, up front — one contribution at the
        // as-of date. Generating on the outflow's frequency would instead emit
        // one full contribution per cycle between now and the due date,
        // over-reserving many times over. A genuinely recurring outflow with no
        // usable income does reserve the full amount per cycle (each contribution
        // funds the next occurrence).
        var isSingleOccurrence =
            outflow.DatePattern.GetOccurrences(outflow.DatePattern.Start, billUntil).Count <= 1;

        var planPattern = isSingleOccurrence
            ? RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                // Guard a past-dated one-off: the plan can't end after the
                // outflow it funds (EarMarkPattern.Create / 3.11.2.a2).
                Start = asOfDate <= billUntil ? asOfDate : billUntil,
                Count = 1,
            })
            : RecurrenceRule.Create(new RecurrenceRuleOptions
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
        return new ProposedAllocationPlan(plan, null);
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
