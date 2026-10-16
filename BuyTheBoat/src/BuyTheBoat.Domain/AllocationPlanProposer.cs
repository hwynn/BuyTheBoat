namespace BuyTheBoat.Domain;

/// <summary>
/// A proposed Allocation Plan: the expense with its active span prepared, the plan itself, and an optional one-off starting earmark for a first occurrence the plan can't save for in time.
/// Save this Outflow, not the original: its ActiveFrom is set when the expense starts in the future, so the plan fits inside it.
/// </summary>
public sealed record ProposedAllocationPlan(FinancialPattern Outflow, EarMarkPattern Plan, ManualEarmark? StartingEarmark);

/// <summary>
/// Proposes a default Allocation Plan (an EarMarkPattern) for a new expense: a bill, a one-time goal, or any expected transaction with a negative Amount.
/// Also re-proposes a plan against an existing one (ProposeSameSchedule, ProposeSameAmount), and detects which plans are paced against an income (IsPacedAgainst, FindPlansPacedAgainst).
/// </summary>
/// <remarks>
/// Goals and bills share one function, since a one-time goal is just a pattern with a single occurrence.
/// The "Create One-Time Goal" window doesn't use this; it keeps OneTimeGoalFactory's even installments.
///
/// Shape A (exactly one income stream): contributions land on each payday, sized so the plan's total over the window equals the bill's total over it.
/// If the first bill occurrence lands before the first payday contribution, a starting earmark covers it (branch B).
/// Shape C (no income, or more than one stream): a repeating expense reserves its full amount each cycle, starting from the as-of date, so no starting earmark is needed.
/// A single-occurrence expense spreads its amount evenly over the remaining time instead, so a large, far-off amount isn't locked up all at once.
/// A transfer's withdrawal (spreadEvenlyWithNoIncome: false) reserves its amount immediately instead.
///
/// Known simplifications:
/// - More than one income stream is treated as shape C; it's a safe, if conservative, default the user can edit.
/// - An income stream that ends before the bill does isn't handled: the plan stops at the earlier end date, so later occurrences aren't funded and the shortfall reports them.
///
/// The save confirmation offers ProposeSameSchedule and ProposeSameAmount on a break-off, alongside Propose's default.
/// Each returns null when its result wouldn't be ideal, or would match Propose's default.
/// Both take carriedOverJarBalance rather than reading existingPlan.StartingAllocation, which is frozen when the plan was created; the caller reads the jar's real balance off the forecast, as for BreakOffRequest.CarriedOverJarBalance.
///
/// IsPacedAgainst and FindPlansPacedAgainst find which existing plans were paced against an income, since no stored field records that link.
/// They're a deliberately narrow heuristic; FinancePatternSaveConfirmation.PerformPaycheckAssociationCascadeIfApplicable uses them to re-propose those plans when the user opts in.
/// </remarks>
public static class AllocationPlanProposer
{
    /// <summary>
    /// [CALC] Lists the income streams worth pacing a plan against: positive-amount patterns with at least one payday in [from, to].
    /// The date filter matters: a broken-off income's ended predecessor keeps its positive Amount forever, and counting it would look like a second income stream, which turns off pacing for every later plan.
    /// </summary>
    /// <param name="allPatterns">Every pattern to scan for income.</param>
    /// <param name="from">Start of the window a plan would pace its contributions across.</param>
    /// <param name="to">End of that window.</param>
    public static IReadOnlyList<FinancialPattern> ActiveIncomeStreams(
        IReadOnlyList<FinancialPattern> allPatterns, DateOnly from, DateOnly to) =>
        allPatterns
            .Where(pattern => pattern.Amount > 0m && pattern.DatePattern.GetOccurrences(from, to).Count > 0)
            .ToList();

    /// <summary>
    /// [CALC] Proposes a default Allocation Plan for a new expense: paced against a single clear income stream when there is one, otherwise front-loaded or spread.
    /// The class remarks describe the shapes.
    /// </summary>
    /// <param name="outflow">The new bill, goal, or transfer leg needing a plan.</param>
    /// <param name="allPatterns">Every other pattern, to look for a single clear income stream to pace against.</param>
    /// <param name="asOfDate">Today, or the forecast's as-of date — where the plan starts contributing from.</param>
    /// <param name="spreadEvenlyWithNoIncome">Whether a single-occurrence expense with no clear income spreads evenly over the remaining time (the default) or reserves the full amount at once; pass false for a transfer's withdrawal.</param>
    /// <param name="carriedOverJarBalance">
    /// What an existing jar already holds, when this proposal replaces a plan with real history; it becomes the plan's StartingAllocation.
    /// Defaults to 0 for a brand-new expense.
    /// It lets a plan-picker preview show the same balance BreakOffFactory.BreakOff will actually save.
    /// </param>
    /// <param name="startingEarmarkCeiling">The most a proposed starting earmark may set aside (AffordabilityCeiling.ForStartingEarmark); null leaves it uncapped.</param>
    /// <param name="ongoingRateCeiling">
    /// The most the plan's per-cycle contribution may reserve (AffordabilityCeiling.For); a higher paced or spread rate is held to it, knowingly underfunding the expense.
    /// Null leaves it uncapped, and a transfer's immediate single contribution is never capped.
    /// It compares one cycle's contribution against the window's tightest free point, not a full cumulative solve.
    /// </param>
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

        // Moves the expense's active span back to the as-of date when it starts in the future, so a plan that starts saving today fits inside it.
        // Its occurrences are untouched.
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

    /// <summary>
    /// [CALC] Holds a per-cycle contribution under an affordability ceiling: returns the ceiling when the rate exceeds a positive one, otherwise the rate.
    /// A ceiling of 0 or less never applies, so a positive rate always stays positive.
    /// </summary>
    /// <param name="rate">The uncapped per-cycle contribution magnitude (a positive number).</param>
    /// <param name="ceiling">The most that may be reserved per cycle, or null for uncapped.</param>
    private static decimal RateUnderCeiling(decimal rate, decimal? ceiling) =>
        ceiling is decimal cap && cap > 0m && rate > cap ? cap : rate;

    /// <summary>[CALC] Sets a carried-over balance as the proposal's plan StartingAllocation; returns the proposal unchanged when there's nothing to carry over.</summary>
    /// <remarks>
    /// Known gap: MaybeStartingEarmark only checks timing, not whether the carried-over balance already covers the first bill.
    /// So a re-proposed plan with one income stream, a real carried-over balance, and a bill due before the next payday can get both.
    /// Teaching MaybeStartingEarmark about amounts is a bigger change.
    /// </remarks>
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

    /// <summary>
    /// [CALC] Proposes a plan that keeps the existing plan's recurrence shape (frequency, interval, weekday or month-day), re-anchored to today without losing phase, with an Amount that exactly covers what's left of the goal after what's already saved.
    /// Null when nothing is left to fund, when the shape has no occurrence left before the due date, or when the result would match Propose's default.
    /// </summary>
    /// <param name="outflow">The goal being funded.</param>
    /// <param name="existingPlan">The plan currently in effect, whose recurrence shape is preserved.</param>
    /// <param name="carriedOverJarBalance">
    /// What the jar holds right now, read by the caller off the forecast.
    /// Not existingPlan.StartingAllocation, which was frozen when the plan was created and misses everything since.
    /// </param>
    /// <param name="manualEarmarksForThisGoal">Every manual earmark for this goal dated after asOfDate and on or before the due date; earlier ones are already in carriedOverJarBalance.</param>
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

    /// <summary>
    /// [CALC] Proposes a plan that keeps the existing plan's Amount, on a new evenly spaced daily-interval schedule that exactly covers what's left of the goal after what's already saved.
    /// Null when nothing is left to fund, when even daily contributions can't get there by the due date, or when the result would match Propose's default.
    /// </summary>
    /// <param name="outflow">The goal being funded.</param>
    /// <param name="existingPlan">The plan currently in effect, whose Amount is preserved.</param>
    /// <param name="carriedOverJarBalance">What the jar holds right now; see ProposeSameSchedule's parameter of the same name.</param>
    /// <param name="manualEarmarksForThisGoal">Every manual earmark for this goal dated after asOfDate and on or before the due date; see ProposeSameSchedule's parameter of the same name.</param>
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
            // Spaces the occurrences evenly across the window so the last one lands on or before dueDate.
            // A daily interval has no weekday or month-day to lose phase on, so AlignedSchedule isn't needed.
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

    /// <summary>
    /// [CALC] Gives an expense a real but empty savings plan when the proposed one is declined, so its jar still exists from today and can be topped up by hand.
    /// The shortfall then shows the full unfunded amount.
    /// </summary>
    /// <remarks>
    /// The plan is a plain Count = 1 rule at the expense's Until with Amount = 0, and ActiveFrom stretches it back to today, so no special empty-plan type is needed.
    /// Its single $0 earmark event at Until is the accepted cost of keeping it plain.
    /// </remarks>
    /// <param name="outflow">The expense to give an empty plan.</param>
    /// <param name="asOfDate">Today, or the forecast's as-of date — the jar becomes visible from here rather than only on the expense's last day.</param>
    public static ProposedAllocationPlan ProposeEmpty(FinancialPattern outflow, DateOnly asOfDate)
    {
        if (outflow.Amount >= 0m)
        {
            throw new ArgumentException(
                "An Allocation Plan is only created for an outflow (negative Amount).",
                nameof(outflow));
        }

        // Moves the expense's active span back to today when it starts in the future, as Propose does, so its jar is visible now.
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
            // Stretches the plan back to today so the jar exists now, only when its one day is in the future (ActiveFrom must be on or before Start).
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

    /// <summary>[CALC] Reports whether every one of a plan's occurrences lands on one of an income's paydays, the reverse of how ProposePaced builds a plan, since no stored link records which income a plan was paced against.</summary>
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

        // Every plan occurrence must land on a payday.
        // This catches a hand-edited plan that still lines up, and misses a same-shape income on a different phase (e.g. two biweekly paychecks on alternating weeks).
        // Requiring every occurrence is the strictest reading; how much drift should still count is an open question.
        var paydays = income.DatePattern.GetOccurrences(plan.DatePattern.ActiveStart, plan.DatePattern.Until).ToHashSet();
        return planDates.All(paydays.Contains);
    }

    /// <summary>[CALC] Finds which of a household's plans are paced against the given income — IsPacedAgainst applied across every plan, for offering a suggestion after the income itself changes.</summary>
    /// <param name="income">The income stream whose associated plans to find.</param>
    /// <param name="allPlans">Every EarMarkPattern in the household to check.</param>
    public static IReadOnlyList<EarMarkPattern> FindPlansPacedAgainst(FinancialPattern income, IReadOnlyList<EarMarkPattern> allPlans) =>
        allPlans.Where(plan => IsPacedAgainst(plan, income)).ToList();

    /// <summary>[CALC] Shape A: builds a plan with one contribution per payday, sized so the window's contributions equal the window's bill consumption.</summary>
    /// <param name="outflow">The expense being funded.</param>
    /// <param name="income">The single income stream to pace against.</param>
    /// <param name="billAmount">The expense's amount, per occurrence.</param>
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
        // Held under the affordability ceiling; a bill that can't be fully paced stays knowingly underfunded, and the forecast reports the shortfall.
        var perPayday = RateUnderCeiling(Math.Round(billAmount * billOccurrenceCount / paydayCount, 2), ongoingRateCeiling);

        // Anchors at the income's next real payday rather than asOfDate, so the plan lands on the days the income actually pays (see AlignedSchedule).
        // paydayCount > 0 guarantees that payday exists, so AlignedSchedule can't return null here.
        var planPattern = RecurrenceRule.Create(
            AlignedSchedule(income.DatePattern, asOfDate, planUntil)
            ?? throw new InvalidOperationException("Unreachable: Propose already confirmed paydayCount > 0 in this window."));

        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = outflow.FinanceId,
                DatePattern = planPattern,
                // Negative means money into the jar: EarMarkPattern.Amount is its effect on free funds.
                Amount = -perPayday,
                ExplicitlyCreated = false, // auto-proposed, not made by the user
            },
            outflow);

        return new ProposedAllocationPlan(outflow, plan, MaybeStartingEarmark(outflow, plan, billAmount, asOfDate, startingEarmarkCeiling));
    }

    /// <summary>
    /// [CALC] Shape C: reserves the full amount each bill cycle, starting at the as-of date.
    /// A single-occurrence expense instead spreads its amount or reserves it at once, per <paramref name="spreadEvenlyWithNoIncome"/>.
    /// </summary>
    /// <param name="outflow">The expense being funded.</param>
    /// <param name="billAmount">The expense's amount, per occurrence.</param>
    /// <param name="billUntil">The expense's end date.</param>
    /// <param name="asOfDate">Today, or the forecast's as-of date.</param>
    /// <param name="spreadEvenlyWithNoIncome">For a single-occurrence expense: whether to spread the amount evenly over the remaining time, or reserve it all at once.</param>
    /// <param name="ongoingRateCeiling">Cap for each contribution of a repeating expense or a spread goal; a transfer's immediate single contribution is never capped, and null leaves it uncapped.</param>
    private static ProposedAllocationPlan ProposeFrontLoaded(
        FinancialPattern outflow,
        decimal billAmount,
        DateOnly billUntil,
        DateOnly asOfDate,
        bool spreadEvenlyWithNoIncome,
        decimal? ongoingRateCeiling)
    {
        // Does the expense happen only once (a one-time goal, one-off bill, or one-off transfer)?
        // Then reserving the full amount every cycle until it's due would reserve it many times over, so it's spread or reserved once instead.
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
                // Held under the affordability ceiling; a repeating bill that can't be fully reserved stays knowingly underfunded.
                Amount = -RateUnderCeiling(billAmount, ongoingRateCeiling),
                ExplicitlyCreated = false, // auto-proposed, not made by the user
            },
            outflow);

        // The first contribution is on the as-of date, so no starting earmark is needed.
        return new ProposedAllocationPlan(outflow, plan, null);
    }

    /// <summary>[CALC] Builds a plan with a single contribution at the as-of date, for the whole amount — a transfer's withdrawal keeps this shape, with no adaptive spreading.</summary>
    /// <param name="outflow">The expense being funded.</param>
    /// <param name="billAmount">The expense's amount.</param>
    /// <param name="billUntil">The expense's end date.</param>
    /// <param name="asOfDate">Today, or the forecast's as-of date.</param>
    private static ProposedAllocationPlan ProposeImmediateSingleContribution(
        FinancialPattern outflow, decimal billAmount, DateOnly billUntil, DateOnly asOfDate)
    {
        var planPattern = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Yearly,
            // Guard a past-dated one-off: the plan can't end after the
            // expense it funds (EarMarkPattern.Create / 3.11.2.a2).
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
    /// <param name="outflow">The expense being funded.</param>
    /// <param name="billAmount">The expense's amount, split evenly across installments.</param>
    /// <param name="billUntil">The expense's end date.</param>
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
        // Held under the affordability ceiling; a goal that can't be fully spread stays knowingly underfunded.
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

    /// <summary>
    /// [CALC] Returns a starting earmark covering the first bill occurrence when it falls before the plan's first contribution (shape A, with a bill due before the next paycheck).
    /// It's capped at the affordability ceiling when one is given; null when none is needed or the day can't spare anything.
    /// </summary>
    /// <param name="outflow">The expense being funded.</param>
    /// <param name="plan">The proposed plan.</param>
    /// <param name="billAmount">The expense's amount, to cover.</param>
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

        // Covers the full first occurrence, capped at what the day can spare; none at all when the day can't spare anything.
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

    /// <summary>
    /// [CALC] Copies a reference pattern's recurrence shape to a new start without losing phase, by starting at the reference's next real occurrence on or after desiredStart rather than desiredStart itself.
    /// When that occurrence is after desiredStart, ActiveFrom is set to desiredStart, so the jar is active (and a starting earmark can be dated) from desiredStart on.
    /// The reference's Until is extended first if the caller needs a later window; only its shape is kept.
    /// </summary>
    /// <remarks>
    /// A raw new start breaks phase in two ways.
    /// With no explicit ByDay, RFC 5545 ties the weekday to DTSTART, so moving the start moves the weekday.
    /// An Interval > 1 weekly rule counts its weeks from DTSTART's week even with an explicit ByDay, so it can land a whole step off (RecurrenceRuleTests covers this).
    /// FinancePatternSaveConfirmation.BuildSuccessorSchedule also uses this for a break-off successor's schedule.
    /// </remarks>
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

    /// <summary>
    /// [CALC] Whether a candidate plan matches what Propose would build fresh for the same expense: same Amount and recurrence shape.
    /// A candidate that close isn't worth offering alongside the default.
    /// </summary>
    /// <param name="candidate">The plan to compare.</param>
    /// <param name="outflow">The expense whose default proposal to compare against.</param>
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
