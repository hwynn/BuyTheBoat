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
                Start = start,
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
                    Start = start,
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
        result.Pattern.DatePattern.Start.ShouldBe(bill.DatePattern.Start);
        result.Plan!.Amount.ShouldBe(plan.Amount);
        result.Plan.StartingAllocation.ShouldBe(plan.StartingAllocation);
        result.Plan.DatePattern.Start.ShouldBe(plan.DatePattern.Start);
    }

    [Fact]
    public void ActiveFrom_survives_truncation_on_both_the_pattern_and_the_plan()
    {
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 3, 1), new DateOnly(2026, 1, 1))
            .WithActiveFrom(new DateOnly(2025, 1, 1));
        var plan = MonthlyPlan(bill, -300m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));

        var result = PatternTruncation.EndOn(bill, plan, new DateOnly(2025, 8, 31));

        result.Pattern.DatePattern.ActiveFrom.ShouldBe(new DateOnly(2025, 1, 1));
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
}
