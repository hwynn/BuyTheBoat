using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

public class PatternTruncationTests
{
    private static FinancialPattern MonthlyBill(decimal amount, int dayOfMonth, DateOnly start, DateOnly until, int id = 1) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = id,
            Source = $"bill{id}",
            Description = "Electric bill",
            Amount = amount,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [dayOfMonth],
                DtStart = start,
                Until = until,
            }),
        });

    private static EarMarkPattern MonthlyPlan(FinancialPattern goal, decimal amount, DateOnly start, DateOnly until) =>
        EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = amount,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [start.Day],
                    DtStart = start,
                    Until = until,
                }),
            },
            goal);

    [Fact]
    public void Ends_both_the_pattern_and_its_plan_on_the_chosen_date()
    {
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var plan = MonthlyPlan(bill, -300m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));

        var result = PatternTruncation.EndOn(bill, plan, new DateOnly(2025, 6, 30));

        result.Pattern.DatePattern.Until.ShouldBe(new DateOnly(2025, 6, 30));
        result.Plan.ShouldNotBeNull();
        result.Plan!.DatePattern.Until.ShouldBe(new DateOnly(2025, 6, 30));
    }

    [Fact]
    public void Every_other_field_is_preserved_through_truncation()
    {
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var plan = MonthlyPlan(bill, -280m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));

        var result = PatternTruncation.EndOn(bill, plan, new DateOnly(2025, 6, 30));

        result.Pattern.FinanceId.ShouldBe(bill.FinanceId);
        result.Pattern.Source.ShouldBe(bill.Source);
        result.Pattern.Description.ShouldBe(bill.Description);
        result.Pattern.Amount.ShouldBe(bill.Amount);
        result.Pattern.Priority.ShouldBe(bill.Priority);
        result.Pattern.Mandatory.ShouldBe(bill.Mandatory);
        result.Pattern.DatePattern.ActiveStart.ShouldBe(bill.DatePattern.ActiveStart);
        result.Plan!.Amount.ShouldBe(plan.Amount);
        result.Plan.StartingAllocation.ShouldBe(plan.StartingAllocation);
        result.Plan.DatePattern.ActiveStart.ShouldBe(plan.DatePattern.ActiveStart);
    }

    [Fact]
    public void ActiveFrom_survives_truncation_on_both_the_pattern_and_the_plan()
    {
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 3, 1), new DateOnly(2026, 1, 1))
            .WithActiveFrom(new DateOnly(2025, 1, 1));
        var plan = MonthlyPlan(bill, -300m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));

        var result = PatternTruncation.EndOn(bill, plan, new DateOnly(2025, 8, 31));

        result.Pattern.DatePattern.ToOptions().ActiveFrom.ShouldBe(new DateOnly(2025, 1, 1));
    }

    [Fact]
    public void A_plan_that_already_ended_earlier_than_the_new_cutoff_keeps_its_own_earlier_end()
    {
        // The plan's Until is already Apr 1 — earlier than the Jun 30 cutoff — so
        // truncation must never STRETCH it out to match; only ever shorten.
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var plan = MonthlyPlan(bill, -300m, new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 1));

        var result = PatternTruncation.EndOn(bill, plan, new DateOnly(2025, 6, 30));

        result.Plan!.DatePattern.Until.ShouldBe(new DateOnly(2025, 4, 1));
    }

    [Fact]
    public void A_pattern_with_no_plan_truncates_cleanly_with_a_null_plan_result()
    {
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));

        var result = PatternTruncation.EndOn(bill, plan: null, new DateOnly(2025, 6, 30));

        result.Pattern.DatePattern.Until.ShouldBe(new DateOnly(2025, 6, 30));
        result.Plan.ShouldBeNull();
    }

    [Fact]
    public void An_end_date_before_the_patterns_own_start_is_rejected()
    {
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 6, 1), new DateOnly(2026, 1, 1));

        Should.Throw<ArgumentException>(() => PatternTruncation.EndOn(bill, plan: null, new DateOnly(2025, 1, 1)));
    }

    [Fact]
    public void StartOn_moves_the_plans_start_forward_and_absorbs_the_prior_balance()
    {
        var goal = MonthlyBill(-300m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var plan = MonthlyPlan(goal, -300m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));

        var trimmed = PatternTruncation.StartOn(plan, goal, new DateOnly(2025, 6, 15), absorbedBalance: 450m);

        trimmed.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 6, 15));
        trimmed.StartingAllocation.ShouldBe(450m);
    }

    [Fact]
    public void StartOn_adds_the_absorbed_balance_on_top_of_whatever_starting_allocation_already_existed()
    {
        var goal = MonthlyBill(-300m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = -300m,
                StartingAllocation = 100m, // already carrying something in from an earlier break-off
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2026, 1, 1),
                }),
            },
            goal);

        var trimmed = PatternTruncation.StartOn(plan, goal, new DateOnly(2025, 6, 15), absorbedBalance: 450m);

        trimmed.StartingAllocation.ShouldBe(550m); // 100 already there + 450 newly absorbed, not a replacement
    }

    [Fact]
    public void StartOn_leaves_the_recurrence_shape_amount_and_until_unchanged()
    {
        var goal = MonthlyBill(-300m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var plan = MonthlyPlan(goal, -280m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));

        var trimmed = PatternTruncation.StartOn(plan, goal, new DateOnly(2025, 6, 15), absorbedBalance: 0m);

        trimmed.FinanceId.ShouldBe(plan.FinanceId);
        trimmed.Amount.ShouldBe(plan.Amount);
        trimmed.DatePattern.Frequency.ShouldBe(plan.DatePattern.Frequency);
        trimmed.DatePattern.ByMonthDay.ShouldBe(plan.DatePattern.ByMonthDay);
        trimmed.DatePattern.Until.ShouldBe(plan.DatePattern.Until);
    }

    [Fact]
    public void StartOn_replaces_an_existing_lead_in_with_the_new_start()
    {
        var goal = MonthlyBill(-300m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1))
            .WithActiveFrom(new DateOnly(2024, 10, 1));
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = -300m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2026, 1, 1),
                    ActiveFrom = new DateOnly(2024, 10, 1), // was already saving ahead of its own Start
                }),
            },
            goal);

        var trimmed = PatternTruncation.StartOn(plan, goal, new DateOnly(2025, 6, 15), absorbedBalance: 200m);

        // The old Oct-2024 lead-in is gone; the new active span begins at the
        // new start, with a fresh lead-in to the first occurrence (Jul 1) that
        // now lands after it — a phase-preserving re-anchor, not a raw move.
        trimmed.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 6, 15));
        trimmed.DatePattern.ToOptions().ActiveFrom.ShouldBe(new DateOnly(2025, 6, 15));
        trimmed.DatePattern.GetOccurrences()[0].ShouldBe(new DateOnly(2025, 7, 1));
    }

    [Fact]
    public void StartOn_rejects_a_new_start_that_isnt_after_the_plans_own_current_start()
    {
        var goal = MonthlyBill(-300m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var plan = MonthlyPlan(goal, -300m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));

        Should.Throw<ArgumentException>(() => PatternTruncation.StartOn(plan, goal, new DateOnly(2025, 1, 1), absorbedBalance: 0m));
    }
}
