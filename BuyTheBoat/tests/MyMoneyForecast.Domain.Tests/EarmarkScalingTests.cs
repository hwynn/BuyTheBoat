using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

public class EarmarkScalingTests
{
    private static readonly DateOnly Start = new(2025, 1, 1);
    private static readonly DateOnly Until = new(2026, 1, 1);

    private static FinancialPattern Goal(decimal amount, int id = 1) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = id,
            Source = $"goal{id}",
            Amount = amount,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                Start = Start,
                Until = Until,
            }),
        });

    private static EarMarkPattern Plan(FinancialPattern goal, decimal amount, int dayOfMonth, decimal startingAllocation = 0m) =>
        EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [dayOfMonth],
                    Start = Start,
                    Until = Until,
                }),
                Amount = amount,
                StartingAllocation = startingAllocation,
            },
            goal);

    [Fact]
    public void Scales_every_surviving_plan_by_the_same_ratio_the_goal_changed_by()
    {
        var oldGoal = Goal(-300m);
        var newGoal = Goal(-400m); // 4/3 of the old amount
        var planA = Plan(oldGoal, -180m, 1);
        var planB = Plan(oldGoal, -120m, 2);

        var scaled = EarmarkScaling.Scale(new ScaleRequest
        {
            Goal = newGoal,
            PreviousGoalAmount = oldGoal.Amount,
            SurvivingPlans = [planA, planB],
        });

        scaled.Count.ShouldBe(2);
        scaled.Single(p => p.DatePattern.ByMonthDay[0] == 1).Amount.ShouldBe(-240m); // -180 x 4/3
        scaled.Single(p => p.DatePattern.ByMonthDay[0] == 2).Amount.ShouldBe(-160m); // -120 x 4/3
    }

    [Fact]
    public void Scaling_down_works_the_same_way()
    {
        var oldGoal = Goal(-400m);
        var newGoal = Goal(-300m); // 3/4 of the old amount
        var plan = Plan(oldGoal, -240m, 1);

        var scaled = EarmarkScaling.Scale(new ScaleRequest
        {
            Goal = newGoal,
            PreviousGoalAmount = oldGoal.Amount,
            SurvivingPlans = [plan],
        });

        scaled.Single().Amount.ShouldBe(-180m); // -240 x 3/4
    }

    [Fact]
    public void StartingAllocation_is_carried_over_untouched_not_scaled()
    {
        var oldGoal = Goal(-300m);
        var newGoal = Goal(-450m); // 1.5x
        var plan = Plan(oldGoal, -100m, 1, startingAllocation: 200m);

        var scaled = EarmarkScaling.Scale(new ScaleRequest
        {
            Goal = newGoal,
            PreviousGoalAmount = oldGoal.Amount,
            SurvivingPlans = [plan],
        });

        scaled.Single().Amount.ShouldBe(-150m); // the rate scales...
        scaled.Single().StartingAllocation.ShouldBe(200m); // ...but already-realized money doesn't
    }

    [Fact]
    public void The_schedule_and_finance_id_are_carried_over_untouched()
    {
        var oldGoal = Goal(-300m);
        var newGoal = Goal(-400m);
        var plan = Plan(oldGoal, -180m, 15);

        var scaled = EarmarkScaling.Scale(new ScaleRequest
        {
            Goal = newGoal,
            PreviousGoalAmount = oldGoal.Amount,
            SurvivingPlans = [plan],
        });

        var result = scaled.Single();
        result.FinanceId.ShouldBe(plan.FinanceId);
        result.DatePattern.Start.ShouldBe(plan.DatePattern.Start); // same (FinanceId, Start) key
        result.DatePattern.Until.ShouldBe(plan.DatePattern.Until);
        result.DatePattern.ByMonthDay.ShouldBe(plan.DatePattern.ByMonthDay);
    }

    [Fact]
    public void Rounds_the_scaled_amount_to_two_decimal_places()
    {
        var oldGoal = Goal(-300m);
        var newGoal = Goal(-400m); // 4/3 ratio, doesn't divide evenly against every input
        var plan = Plan(oldGoal, -100m, 1); // -100 x 4/3 = -133.333...

        var scaled = EarmarkScaling.Scale(new ScaleRequest
        {
            Goal = newGoal,
            PreviousGoalAmount = oldGoal.Amount,
            SurvivingPlans = [plan],
        });

        scaled.Single().Amount.ShouldBe(-133.33m);
    }

    [Fact]
    public void Throws_when_the_previous_amount_is_zero()
    {
        var newGoal = Goal(-400m);
        var plan = Plan(Goal(-300m), -180m, 1);

        Should.Throw<ArgumentException>(() => EarmarkScaling.Scale(new ScaleRequest
        {
            Goal = newGoal,
            PreviousGoalAmount = 0m,
            SurvivingPlans = [plan],
        }));
    }
}
