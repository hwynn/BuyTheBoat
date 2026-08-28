using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

public class AllocationPlanProposerTests
{
    private static readonly DateOnly AsOf = new(2025, 1, 1);

    private static FinancialPattern MonthlyBill(decimal amount, int dayOfMonth, DateOnly start, DateOnly until, int id = 1) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = id,
            Source = $"bill{id}",
            Amount = amount,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [dayOfMonth],
                DtStart = start,
                Until = until,
            }),
        });

    private static FinancialPattern MonthlyIncome(decimal amount, int dayOfMonth, DateOnly start, DateOnly until, int id = 100) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = id,
            Source = $"income{id}",
            Amount = amount,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [dayOfMonth],
                DtStart = start,
                Until = until,
            }),
        });

    private static FinancialPattern BiweeklyIncome(decimal amount, DateOnly start, DateOnly until, int id = 100) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = id,
            Source = $"income{id}",
            Amount = amount,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Weekly,
                Interval = 2,
                DtStart = start,
                Until = until,
            }),
        });

    [Fact]
    public void Rejects_a_non_outflow()
    {
        var income = MonthlyIncome(3000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2027, 1, 1), id: 1);
        Should.Throw<ArgumentException>(() => AllocationPlanProposer.Propose(income, [income], AsOf));
    }

    [Fact]
    public void Single_income_paces_on_the_income_rhythm_and_links_to_the_outflow()
    {
        // Monthly income (25th) + monthly bill (1st): 7 paydays and 7 bill
        // occurrences in the window, so bill x billOccs / incomeOccs is the
        // full bill amount each month.
        var income = MonthlyIncome(3000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2027, 1, 1));
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 8, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill, income], AsOf);

        result.Plan.FinanceId.ShouldBe(bill.FinanceId);
        result.Plan.DatePattern.Frequency.ShouldBe(RecurrenceFrequency.Monthly);
        result.Plan.Amount.ShouldBe(-300m); // negative = into the jar
        result.StartingEarmark.ShouldBeNull(); // the 25th contribution precedes the 1st-of-next-month bill
    }

    [Fact]
    public void Biweekly_income_against_a_monthly_bill_paces_below_the_full_amount()
    {
        // More paydays than bill occurrences, so each contribution is a
        // fraction of the bill — the whole point of pacing. Exact cents depend
        // on the payday count; the invariant is "less than the full amount,
        // still negative (into the jar)".
        var income = BiweeklyIncome(1000m, new DateOnly(2025, 1, 3), new DateOnly(2027, 1, 1));
        var bill = MonthlyBill(-600m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 8, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill, income], AsOf);

        result.Plan.DatePattern.Frequency.ShouldBe(RecurrenceFrequency.Weekly);
        result.Plan.DatePattern.Interval.ShouldBe(2);
        result.Plan.Amount.ShouldBeLessThan(0m);
        result.Plan.Amount.ShouldBeGreaterThan(-600m);
    }

    // Regression (2026-08-27) for the double-counting-income trap: a break-off
    // truncates an income's predecessor row but never deletes it, so it lingers
    // with a positive Amount forever. A bare "Amount > 0" income count then saw
    // TWO income streams (the dead predecessor + the live successor) for every
    // future plan, and — since pacing only fires for exactly one — silently
    // dropped every plan to the front-loaded fallback. Here the live biweekly
    // income should still pace the plan (Weekly/2, a fraction of the bill); under
    // the bug it front-loads to the bill's own Monthly cadence at the full amount.
    [Fact]
    public void A_broken_off_incomes_truncated_predecessor_does_not_derail_pacing()
    {
        var liveIncome = BiweeklyIncome(1000m, new DateOnly(2025, 1, 3), new DateOnly(2027, 1, 1), id: 100);
        // The same paycheck's own earlier version, truncated when it was broken
        // off last year — a positive Amount that stopped paying before AsOf.
        var brokenOffPredecessor = BiweeklyIncome(900m, new DateOnly(2023, 1, 6), new DateOnly(2024, 12, 31), id: 101);
        var bill = MonthlyBill(-600m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 8, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill, liveIncome, brokenOffPredecessor], AsOf);

        result.Plan.DatePattern.Frequency.ShouldBe(RecurrenceFrequency.Weekly); // paced, not front-loaded
        result.Plan.DatePattern.Interval.ShouldBe(2);
        result.Plan.Amount.ShouldBeGreaterThan(-600m); // a fraction per payday, not the full amount
    }

    // Found 2026-08-14: the plan's contributions used to land on Wednesdays
    // (asOfDate's own weekday) instead of the income's real Fridays — a
    // silent phase loss from anchoring Start at asOfDate while copying
    // income's own Frequency/Interval/ByDay (RFC 5545 then ties an omitted
    // ByDay to DTSTART's own weekday, so the SAME implicit rule now resolved
    // against the wrong day). Fixed via AlignedSchedule, which re-anchors at
    // income's own next real payday instead — this test locks the dates in,
    // where the sibling test above only ever checked Amount bounds.
    [Fact]
    public void Biweekly_income_against_a_monthly_bill_lands_contributions_on_real_paydays()
    {
        var income = BiweeklyIncome(1000m, new DateOnly(2025, 1, 3), new DateOnly(2027, 1, 1));
        var bill = MonthlyBill(-600m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 8, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill, income], AsOf);

        var expectedPaydays = income.DatePattern.GetOccurrences(AsOf, bill.DatePattern.Until);
        result.Plan.DatePattern.GetOccurrences().ShouldBe(expectedPaydays);

        // The jar still reads as alive from AsOf onward (so a starting
        // earmark, or the Summary chart's own "since when" reading, isn't
        // stranded before the plan's own first real contribution) even
        // though the first payday itself falls two days later.
        result.Plan.DatePattern.DtStart.ShouldBe(new DateOnly(2025, 1, 3));
        result.Plan.DatePattern.ActiveStart.ShouldBe(AsOf);
    }

    [Fact]
    public void A_bill_due_before_the_first_payday_gets_a_full_starting_earmark()
    {
        // Bill on the 10th (first occurrence Jan 10) with income on the 25th:
        // the first bill lands before the first contribution, so the gap is
        // front-loaded for the whole occurrence amount, dated at the as-of day.
        var income = MonthlyIncome(3000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2027, 1, 1));
        var bill = MonthlyBill(-300m, 10, new DateOnly(2025, 1, 10), new DateOnly(2025, 6, 10));

        var result = AllocationPlanProposer.Propose(bill, [bill, income], AsOf);

        result.StartingEarmark.ShouldNotBeNull();
        result.StartingEarmark!.Amount.ShouldBe(300m);
        result.StartingEarmark.Date.ShouldBe(AsOf);
        result.StartingEarmark.FinanceId.ShouldBe(bill.FinanceId);
    }

    [Fact]
    public void No_income_front_loads_the_full_amount_on_the_bills_cadence()
    {
        // No income at all: the plan reserves the full amount every cycle,
        // starting at the as-of date so the first contribution front-loads.
        // No divide-by-zero, no starting earmark.
        var bill = MonthlyBill(-500m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 6, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill], AsOf);

        result.Plan.Amount.ShouldBe(-500m);
        result.Plan.DatePattern.Frequency.ShouldBe(RecurrenceFrequency.Monthly);
        result.StartingEarmark.ShouldBeNull();
        result.Plan.DatePattern.GetOccurrences(AsOf, result.Plan.DatePattern.Until)[0].ShouldBe(AsOf);
    }

    [Fact]
    public void A_one_off_outflow_with_no_income_spreads_evenly_by_default()
    {
        // planning/18 (C1): a single-occurrence outflow with no clean income
        // must not generate one full contribution per frequency cycle
        // between the as-of date and its due date (that would over-reserve
        // many times over) — and, since Stage 1's revision, it must not
        // reserve the whole amount immediately either. It spreads evenly:
        // $800 over 9 monthly installments (Jan through Sep inclusive).
        var oneOff = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 5,
            Source = "Trip to Japan",
            Amount = -800m,
            Mandatory = false,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                DtStart = new DateOnly(2025, 9, 1),
                Count = 1,
            }),
        });

        var result = AllocationPlanProposer.Propose(oneOff, [oneOff], AsOf);

        result.Plan.Amount.ShouldBe(-88.89m); // 800 / 9, rounded
        result.Plan.DatePattern.GetOccurrences().Count.ShouldBe(9);
        result.StartingEarmark.ShouldBeNull();
    }

    [Fact]
    public void A_near_term_one_off_outflow_still_reduces_to_one_installment()
    {
        // Due within about a month: the spread-evenly pattern (monthly,
        // starting today) has nowhere to put a second installment before the
        // due date, so it reduces to exactly the same shape front-loading
        // would have produced — not a special case, just the same formula
        // landing on one occurrence.
        var oneOff = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 6,
            Source = "Car repair",
            Amount = -800m,
            Mandatory = false,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                DtStart = new DateOnly(2025, 1, 20),
                Count = 1,
            }),
        });

        var result = AllocationPlanProposer.Propose(oneOff, [oneOff], AsOf);

        result.Plan.Amount.ShouldBe(-800m);
        result.Plan.DatePattern.GetOccurrences().Count.ShouldBe(1);
    }

    [Fact]
    public void A_transfer_withdrawal_opts_out_of_spreading_and_still_reserves_the_full_amount_up_front()
    {
        // spreadEvenlyWithNoIncome: false — what MainWindow's transfer
        // creation and TransferBreakOffFactory both actually pass. A
        // transfer's withdrawal stays plain and immediate (planning/18, C1) —
        // the same scenario as the pre-C1 behavior, opted back into
        // explicitly rather than left as the silent default.
        var oneOffTransferWithdrawal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 5,
            Source = "Transfer to Savings",
            Amount = -800m,
            Mandatory = false,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                DtStart = new DateOnly(2025, 9, 1),
                Count = 1,
            }),
        });

        var result = AllocationPlanProposer.Propose(
            oneOffTransferWithdrawal, [oneOffTransferWithdrawal], AsOf, spreadEvenlyWithNoIncome: false);

        result.Plan.Amount.ShouldBe(-800m);
        result.Plan.DatePattern.GetOccurrences(AsOf, result.Plan.DatePattern.Until).Count.ShouldBe(1);
        result.StartingEarmark.ShouldBeNull();
    }

    [Fact]
    public void More_than_one_income_stream_falls_back_to_the_front_loaded_shape()
    {
        var income1 = MonthlyIncome(2000m, 15, new DateOnly(2024, 1, 15), new DateOnly(2027, 1, 1), id: 100);
        var income2 = MonthlyIncome(1000m, 28, new DateOnly(2024, 1, 28), new DateOnly(2027, 1, 1), id: 101);
        var bill = MonthlyBill(-500m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 6, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill, income1, income2], AsOf);

        result.Plan.Amount.ShouldBe(-500m); // full amount, the front-loaded shape
        result.StartingEarmark.ShouldBeNull();
    }

    [Fact]
    public void Income_that_has_already_ended_is_treated_as_no_usable_income()
    {
        // The one income stream ended before the as-of date, so there are no
        // paydays in the window — this must not divide by zero; it falls to the
        // front-loaded shape.
        var income = MonthlyIncome(3000m, 25, new DateOnly(2023, 1, 25), new DateOnly(2024, 12, 1));
        var bill = MonthlyBill(-500m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 6, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill, income], AsOf);

        result.Plan.Amount.ShouldBe(-500m);
    }

    [Fact]
    public void The_plan_never_outruns_the_outflows_end_date()
    {
        // EarMarkPattern.Create enforces Until <= the goal's; a successful
        // proposal proves it, and we assert it directly too.
        var income = MonthlyIncome(3000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2030, 1, 1));
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 8, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill, income], AsOf);

        result.Plan.DatePattern.Until.ShouldBeLessThanOrEqualTo(bill.DatePattern.Until);
    }

    [Fact]
    public void A_one_time_goal_is_just_the_single_occurrence_case()
    {
        // A one-time goal is a FinancialPattern with one occurrence, so the same
        // proposer handles it — billOccurrences = 1.
        var income = MonthlyIncome(4000m, 15, new DateOnly(2024, 1, 15), new DateOnly(2027, 1, 1));
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 7,
            Source = "Trip to Japan",
            Amount = -1200m,
            Mandatory = false,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = new DateOnly(2025, 12, 1),
                Count = 1,
            }),
        });

        var result = AllocationPlanProposer.Propose(goal, [goal, income], AsOf);

        result.Plan.FinanceId.ShouldBe(7);
        result.Plan.Amount.ShouldBeLessThan(0m);
    }

    [Fact]
    public void A_future_dated_outflow_is_returned_stretched_back_to_the_as_of_date()
    {
        // The bill's first occurrence (Feb 1) is after the as-of date, so its plan
        // begins accumulating before it — the returned outflow carries an ActiveFrom
        // at the as-of date so the plan fits (planning/15). Occurrences are untouched.
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 8, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill], AsOf);

        result.Outflow.DatePattern.ToOptions().ActiveFrom.ShouldBe(AsOf);
        result.Outflow.DatePattern.GetOccurrences().ShouldBe(bill.DatePattern.GetOccurrences());
    }

    [Fact]
    public void An_outflow_already_covering_the_as_of_date_keeps_a_null_active_from()
    {
        // Its first occurrence is on the as-of date, so nothing accumulates before
        // it — the sparing rule leaves ActiveFrom null.
        var bill = MonthlyBill(-300m, 1, AsOf, new DateOnly(2025, 8, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill], AsOf);

        result.Outflow.DatePattern.ToOptions().ActiveFrom.ShouldBeNull();
    }

    [Fact]
    public void An_empty_plan_has_no_contributions_and_one_zero_dollar_occurrence_at_the_outflows_end()
    {
        // A declined plan still exists — it just contributes nothing. Its single
        // occurrence sits at the outflow's own end date, at $0.
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 8, 1));

        var result = AllocationPlanProposer.ProposeEmpty(bill, AsOf);

        result.Plan.FinanceId.ShouldBe(bill.FinanceId);
        result.Plan.Amount.ShouldBe(0m);
        result.StartingEarmark.ShouldBeNull();
        result.Plan.DatePattern.Until.ShouldBe(bill.DatePattern.Until);

        var occurrences = result.Plan.DatePattern.GetOccurrences();
        occurrences.Count.ShouldBe(1);
        occurrences[0].ShouldBe(bill.DatePattern.Until);
    }

    [Fact]
    public void An_empty_plan_for_a_future_dated_outflow_stretches_both_the_outflow_and_the_jar_back_to_today()
    {
        // The bill starts Feb 1, after the as-of date, so both the outflow's span
        // and the empty jar reach back to today — the jar is visible now even
        // though nothing is being reserved.
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 8, 1));

        var result = AllocationPlanProposer.ProposeEmpty(bill, AsOf);

        result.Outflow.DatePattern.ToOptions().ActiveFrom.ShouldBe(AsOf);
        result.Plan.DatePattern.ActiveStart.ShouldBe(AsOf);
    }

    [Fact]
    public void An_empty_plans_jar_is_visible_from_today_even_when_the_outflow_already_covers_it()
    {
        // The outflow already covers the as-of date, so the sparing rule leaves
        // its own ActiveFrom null — but the empty plan's single occurrence is at
        // the far-off end date, so it still needs ActiveFrom to put the jar on
        // today's page.
        var bill = MonthlyBill(-300m, 1, AsOf, new DateOnly(2025, 8, 1));

        var result = AllocationPlanProposer.ProposeEmpty(bill, AsOf);

        result.Outflow.DatePattern.ToOptions().ActiveFrom.ShouldBeNull();
        result.Plan.DatePattern.ActiveStart.ShouldBe(AsOf);
    }

    [Fact]
    public void An_empty_plan_rejects_a_non_outflow()
    {
        var income = MonthlyIncome(3000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2027, 1, 1), id: 1);
        Should.Throw<ArgumentException>(() => AllocationPlanProposer.ProposeEmpty(income, AsOf));
    }

    // Mechanism-C follow-on (redesign/planning/26, "the glut case," 2026-08-15)
    // — a detail the author flagged early on ("keep the glut as an up-front
    // earmark event should be a valid option... we might need an optional
    // parameter on the propose functions to handle that") that got set aside
    // while building mechanism C and only surfaced again when asked to track
    // it down. Propose's own candidate never carried a caller-supplied
    // balance forward before this — BreakOffFactory.BreakOff already
    // overrides StartingAllocation with the real one regardless of which
    // Item G candidate gets chosen, but the "Recommended" candidate the
    // picker itself shows the user, before a choice is even made, silently
    // read $0 there. Same class of bug as EarmarkFormLivePreviewTests'
    // findings — a preview that doesn't match what actually gets saved.
    [Fact]
    public void Propose_carries_a_supplied_balance_forward_as_StartingAllocation_paced_shape()
    {
        var income = MonthlyIncome(3000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2027, 1, 1));
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 8, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill, income], AsOf, carriedOverJarBalance: 500m);

        result.Plan.StartingAllocation.ShouldBe(500m);
        result.Plan.Amount.ShouldBe(-300m); // unaffected — same rate as the no-balance case above
    }

    [Fact]
    public void Propose_carries_a_supplied_balance_forward_as_StartingAllocation_front_loaded_shape()
    {
        // No income pattern at all -> front-loaded shape, the other of
        // Propose's two internal paths (both go through WithStartingAllocation).
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 8, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill], AsOf, carriedOverJarBalance: 500m);

        result.Plan.StartingAllocation.ShouldBe(500m);
    }

    [Fact]
    public void Propose_defaults_to_zero_StartingAllocation_exactly_like_before_this_parameter_existed()
    {
        var income = MonthlyIncome(3000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2027, 1, 1));
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 8, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill, income], AsOf); // carriedOverJarBalance omitted

        result.Plan.StartingAllocation.ShouldBe(0m);
    }

    // ---- ProposeSameSchedule ------------------------------------------------

    [Fact]
    public void ProposeSameSchedule_keeps_the_existing_plans_shape_and_solves_for_an_ideal_amount()
    {
        var goal = MonthlyBill(-1000m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 7, 1));
        var existingPlan = BiweeklyPlan(goal, -50m, new DateOnly(2025, 1, 3), goal.DatePattern.Until);

        var result = AllocationPlanProposer.ProposeSameSchedule(goal, existingPlan, carriedOverJarBalance: 0m, [], [goal], AsOf);

        result.ShouldNotBeNull();
        result!.Plan.DatePattern.Frequency.ShouldBe(RecurrenceFrequency.Weekly);
        result.Plan.DatePattern.Interval.ShouldBe(2);
        // Same phase as the existing plan's own Friday cycle — not just "any
        // biweekly schedule."
        existingPlan.DatePattern.GetOccurrences().ShouldContain(result.Plan.DatePattern.DtStart);
        result.Plan.DatePattern.DtStart.ShouldBe(new DateOnly(2025, 1, 3));
        // $6,000 needed (6 monthly releases from the aligned Start through
        // the due date), spread across 13 biweekly occurrences in that same
        // window.
        result.Plan.Amount.ShouldBe(-461.54m);
    }

    [Fact]
    public void ProposeSameSchedule_carries_the_real_jar_balance_forward_not_the_existing_plans_own_stale_StartingAllocation()
    {
        // The existing plan's own StartingAllocation (999m) is a decoy — a
        // stale snapshot from whenever this plan was first created, not what
        // the jar holds today. carriedOverJarBalance (1,200m, enough on its
        // own to fully fund the $1,200/year goal) is what should actually
        // count, and should land unchanged on the new plan's own field too.
        var goal = MonthlyBill(-100m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var existingPlan = BiweeklyPlan(goal, -10m, new DateOnly(2025, 1, 3), goal.DatePattern.Until, startingAllocation: 999m);

        var result = AllocationPlanProposer.ProposeSameSchedule(goal, existingPlan, carriedOverJarBalance: 1200m, [], [goal], AsOf);

        result.ShouldBeNull(); // already fully funded by the REAL balance, regardless of the decoy field
    }

    // Mechanism-C follow-on (redesign/planning/26, "the glut case",
    // 2026-08-15): unlike EarmarkConsolidation, ProposeSameSchedule/
    // ProposeSameAmount needed no code change to protect a glut — the test
    // right above this one already proves carriedOverJarBalance wins over a
    // stale StartingAllocation field; this proves the stronger claim, that
    // it's the REAL, forecast-computed balance (not a hand-picked number)
    // that survives into the new plan's own StartingAllocation untouched,
    // for a jar that's genuinely, verifiably glutted (not just "some decoy
    // beaten by a bigger number").
    [Fact]
    public void ProposeSameSchedule_carries_a_real_forecast_glut_forward_into_the_new_plans_own_StartingAllocation()
    {
        var goal = MonthlyBill(-100m, 1, AsOf, new DateOnly(2025, 12, 1));
        var existingPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = -150m, // over-contributes every month — a real, structural glut
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = AsOf,
                    Until = new DateOnly(2025, 12, 1),
                }),
            },
            goal);

        var forecast = TransactionLogBookFactory.CreateForecast(new ForecastOptions
        {
            FinancialPatterns = [goal],
            EarMarkPatterns = [existingPlan],
            ManualEarmarks = [],
            StartingBalance = 10_000m,
            AsOfDate = AsOf,
            HorizonEndDate = new DateOnly(2025, 3, 31),
        });
        var jar = forecast.GetTimeline(goal.FinanceId).Last(entry => entry.Date <= AsOf).Snapshot.FundJars.Single(j => j.FinanceId == goal.FinanceId);
        jar.HasGlut.ShouldBeTrue(); // a real, checked glut — not assumed

        var result = AllocationPlanProposer.ProposeSameSchedule(goal, existingPlan, jar.ExpectedAmount, [], [goal], AsOf);

        // Ten months still left after AsOf — the $50 glut discounts what's
        // still owed but doesn't fully cover it, so this offers a real
        // candidate rather than declining (ProposeSameSchedule_returns_null_
        // when_already_fully_funded below covers the opposite case).
        result.ShouldNotBeNull();
        result!.Plan.StartingAllocation.ShouldBe(jar.ExpectedAmount); // the real glut, carried forward exactly, not eroded
    }

    [Fact]
    public void ProposeSameSchedule_returns_null_when_already_fully_funded()
    {
        var goal = MonthlyBill(-100m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1));
        var existingPlan = BiweeklyPlan(goal, -10m, new DateOnly(2025, 1, 3), goal.DatePattern.Until);

        var result = AllocationPlanProposer.ProposeSameSchedule(goal, existingPlan, carriedOverJarBalance: 1000m, [], [goal], AsOf);

        result.ShouldBeNull();
    }

    [Fact]
    public void ProposeSameSchedule_returns_null_when_indistinguishable_from_the_default_proposal()
    {
        // Single clear income, no existing plan at all funding this goal yet
        // (nothing banked, no manual earmarks) — the existing plan's own
        // shape happens to already BE what Propose would build fresh.
        var income = MonthlyIncome(3000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2027, 1, 1));
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 8, 1));
        var defaultResult = AllocationPlanProposer.Propose(bill, [bill, income], AsOf);

        var result = AllocationPlanProposer.ProposeSameSchedule(bill, defaultResult.Plan, carriedOverJarBalance: 0m, [], [bill, income], AsOf);

        result.ShouldBeNull();
    }

    // ---- ProposeSameAmount ---------------------------------------------------

    [Fact]
    public void ProposeSameAmount_keeps_the_existing_plans_amount_and_solves_for_an_exact_fit_cadence()
    {
        var goal = MonthlyBill(-1000m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 7, 1));
        var existingPlan = BiweeklyPlan(goal, -50m, new DateOnly(2025, 1, 3), goal.DatePattern.Until);

        var result = AllocationPlanProposer.ProposeSameAmount(goal, existingPlan, carriedOverJarBalance: 0m, [], [goal], AsOf);

        result.ShouldNotBeNull();
        result!.Plan.Amount.ShouldBe(-50m); // unchanged
        result.Plan.DatePattern.Frequency.ShouldBe(RecurrenceFrequency.Daily);
        // $7,000 needed (7 monthly releases from AsOf itself, which IS one of
        // the goal's own occurrence dates here, through the due date) at $50
        // each needs 140 occurrences; spread daily across the 181-day window
        // lands the last one well short of the due date, not pressed against it.
        result.Plan.DatePattern.GetOccurrences().Count.ShouldBe(140);
        result.Plan.DatePattern.Interval.ShouldBe(1);
        result.Plan.DatePattern.ActiveStart.ShouldBe(AsOf);
        result.Plan.DatePattern.Until.ShouldBe(new DateOnly(2025, 5, 20));
    }

    [Fact]
    public void ProposeSameAmount_returns_null_when_already_fully_funded()
    {
        var goal = MonthlyBill(-100m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1));
        var existingPlan = BiweeklyPlan(goal, -10m, new DateOnly(2025, 1, 3), goal.DatePattern.Until);

        var result = AllocationPlanProposer.ProposeSameAmount(goal, existingPlan, carriedOverJarBalance: 1000m, [], [goal], AsOf);

        result.ShouldBeNull();
    }

    [Fact]
    public void ProposeSameAmount_returns_null_when_even_daily_contributions_cant_fit_in_time()
    {
        // $1 per occurrence, due tomorrow, needing $10,000 — no cadence at
        // any frequency can reach that in one day.
        var goal = MonthlyBill(-10_000m, AsOf.AddDays(1).Day, AsOf.AddDays(1), AsOf.AddDays(1));
        var existingPlan = BiweeklyPlan(goal, -1m, goal.DatePattern.ActiveStart, goal.DatePattern.Until);

        var result = AllocationPlanProposer.ProposeSameAmount(goal, existingPlan, carriedOverJarBalance: 0m, [], [goal], AsOf);

        result.ShouldBeNull();
    }

    // ---- IsPacedAgainst / FindPlansPacedAgainst -------------------------------
    // 2026-08-17: these two methods are the detection primitive; the actual
    // cascade that consumes them (FinancePatternSaveConfirmation.
    // PerformPaycheckAssociationCascadeIfApplicable) is covered by its own
    // end-to-end tests instead — FinancePatternSaveConfirmationPaycheckAssociationTests.cs
    // in MyMoneyForecast.App.Tests. See these two methods' own doc comments
    // in AllocationPlanProposer.cs for what "paced against" means here.

    [Fact]
    public void IsPacedAgainst_is_true_for_a_plan_ProposePaced_actually_built()
    {
        var income = MonthlyIncome(3000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2027, 1, 1));
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 8, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill, income], AsOf);

        AllocationPlanProposer.IsPacedAgainst(result.Plan, income).ShouldBeTrue();
    }

    [Fact]
    public void IsPacedAgainst_is_true_for_a_hand_built_plan_whose_occurrences_all_land_on_paydays()
    {
        // Never went through Propose at all — same monthly shape and day as
        // the income, over a window fully inside the income's own, so every
        // one of the plan's own dates genuinely is one of the income's own
        // paydays. Proves this is a real date-coincidence check, not just
        // recognizing Propose's own output by construction.
        var income = MonthlyIncome(3000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2027, 1, 1));
        var goal = MonthlyBill(-300m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 1));
        var plan = MonthlyPlan(goal, -300m, 25, new DateOnly(2025, 1, 25), new DateOnly(2025, 11, 25));

        AllocationPlanProposer.IsPacedAgainst(plan, income).ShouldBeTrue();
    }

    [Fact]
    public void IsPacedAgainst_is_false_when_the_plan_has_its_own_unrelated_schedule()
    {
        var income = MonthlyIncome(3000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2027, 1, 1));
        var goal = MonthlyBill(-300m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 1));
        var plan = MonthlyPlan(goal, -300m, 15, new DateOnly(2025, 1, 15), new DateOnly(2025, 11, 15));

        AllocationPlanProposer.IsPacedAgainst(plan, income).ShouldBeFalse();
    }

    [Fact]
    public void IsPacedAgainst_is_false_for_two_same_shape_streams_paying_on_different_phases()
    {
        // Both biweekly, same Interval — but the plan's own Start is offset
        // one week from the income's, so the two schedules alternate and
        // never land on the same date. A bare shape comparison (Frequency +
        // Interval) would wrongly call this paced; the real date-coincidence
        // check correctly doesn't.
        var income = BiweeklyIncome(1000m, new DateOnly(2025, 1, 3), new DateOnly(2026, 1, 1));
        var goal = MonthlyBill(-300m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 1));
        var plan = BiweeklyPlan(goal, -150m, new DateOnly(2025, 1, 10), new DateOnly(2025, 11, 10));

        AllocationPlanProposer.IsPacedAgainst(plan, income).ShouldBeFalse();
    }

    [Fact]
    public void IsPacedAgainst_is_false_when_the_candidate_income_is_actually_an_outflow()
    {
        var notIncome = MonthlyBill(-3000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2027, 1, 1), id: 2);
        var goal = MonthlyBill(-300m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 1));
        var plan = MonthlyPlan(goal, -300m, 25, new DateOnly(2025, 1, 25), new DateOnly(2025, 11, 25));

        AllocationPlanProposer.IsPacedAgainst(plan, notIncome).ShouldBeFalse();
    }

    [Fact]
    public void IsPacedAgainst_is_false_when_the_plan_has_no_occurrences_of_its_own()
    {
        // Pinned to the 31st but boxed into a window with no 31st in it —
        // legitimate per RecurrenceRule.Create (occurrence count isn't
        // validated at construction), and a real edge case for a check built
        // on "every occurrence must match": vacuously true is the wrong
        // answer for a plan that never actually contributes anything.
        var income = MonthlyIncome(3000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2027, 1, 1));
        var goal = MonthlyBill(-300m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 1));
        var plan = MonthlyPlan(goal, -300m, 31, new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28));

        AllocationPlanProposer.IsPacedAgainst(plan, income).ShouldBeFalse();
    }

    [Fact]
    public void FindPlansPacedAgainst_returns_only_the_plans_that_coincide_with_the_given_income()
    {
        var income = MonthlyIncome(3000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2027, 1, 1));
        var pacedGoal = MonthlyBill(-300m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 1), id: 1);
        var pacedPlan = MonthlyPlan(pacedGoal, -300m, 25, new DateOnly(2025, 1, 25), new DateOnly(2025, 11, 25));
        var unrelatedGoal = MonthlyBill(-50m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 1), id: 2);
        var unrelatedPlan = MonthlyPlan(unrelatedGoal, -50m, 15, new DateOnly(2025, 1, 15), new DateOnly(2025, 11, 15));

        var result = AllocationPlanProposer.FindPlansPacedAgainst(income, [pacedPlan, unrelatedPlan]);

        result.ShouldBe([pacedPlan]);
    }

    [Fact]
    public void FindPlansPacedAgainst_returns_empty_when_none_of_the_plans_match()
    {
        var income = MonthlyIncome(3000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2027, 1, 1));
        var goal = MonthlyBill(-50m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 1));
        var unrelatedPlan = MonthlyPlan(goal, -50m, 15, new DateOnly(2025, 1, 15), new DateOnly(2025, 11, 15));

        AllocationPlanProposer.FindPlansPacedAgainst(income, [unrelatedPlan]).ShouldBeEmpty();
    }

    private static EarMarkPattern BiweeklyPlan(
        FinancialPattern goal, decimal amount, DateOnly start, DateOnly until, decimal startingAllocation = 0m) =>
        EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = amount,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Weekly,
                    Interval = 2,
                    DtStart = start,
                    Until = until,
                }),
                StartingAllocation = startingAllocation,
            },
            goal);

    private static EarMarkPattern MonthlyPlan(
        FinancialPattern goal, decimal amount, int dayOfMonth, DateOnly start, DateOnly until) =>
        EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = amount,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [dayOfMonth],
                    DtStart = start,
                    Until = until,
                }),
            },
            goal);
}
