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
                DtStart = Start,
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
                    DtStart = Start,
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
        result.DatePattern.ActiveStart.ShouldBe(plan.DatePattern.ActiveStart); // same (FinanceId, Start) key
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

    // ScaleToMeetGoal — the "keep them separate but correct the funding" split.
    // A month-end Until so every day-of-month gives the same 12 occurrences,
    // keeping the proportional math exact.
    private static readonly DateOnly MonthEndUntil = new(2025, 12, 28);

    private static FinancialPattern GoalFundedBy(decimal amount) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "goal",
            Amount = amount,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = Start,
                Until = MonthEndUntil,
            }),
        });

    private static EarMarkPattern PlanOn(FinancialPattern goal, decimal amount, int day, decimal startingAllocation = 0m) =>
        EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [day],
                    DtStart = Start,
                    Until = MonthEndUntil,
                }),
                Amount = amount,
                StartingAllocation = startingAllocation,
            },
            goal);

    [Fact]
    public void ScaleToMeetGoal_raises_underfunded_plans_proportionally_to_exactly_fund_the_goal()
    {
        // Goal consumes 100 x 12 = 1,200. The two plans put in 30x12 + 50x12 =
        // 960 — short. Raised by 1,200/960 = 1.25, keeping their 3:5 split.
        var goal = GoalFundedBy(-100m);
        var planA = PlanOn(goal, -30m, 5);
        var planB = PlanOn(goal, -50m, 10);

        var result = EarmarkScaling.ScaleToMeetGoal(goal, [planA, planB]);

        result.CurrentTotal.ShouldBe(960m);
        result.NeededTotal.ShouldBe(1200m);
        result.ScaledPlans.Single(p => p.DatePattern.ByMonthDay[0] == 5).Amount.ShouldBe(-37.50m); // -30 x 1.25
        result.ScaledPlans.Single(p => p.DatePattern.ByMonthDay[0] == 10).Amount.ShouldBe(-62.50m); // -50 x 1.25
    }

    [Fact]
    public void ScaleToMeetGoal_lowers_overfunded_plans_the_same_proportional_way()
    {
        // 75x12 + 50x12 = 1,500 against a 1,200 goal — over. Scaled by 0.8.
        var goal = GoalFundedBy(-100m);
        var planA = PlanOn(goal, -75m, 5);
        var planB = PlanOn(goal, -50m, 10);

        var result = EarmarkScaling.ScaleToMeetGoal(goal, [planA, planB]);

        result.CurrentTotal.ShouldBe(1500m);
        result.NeededTotal.ShouldBe(1200m);
        result.ScaledPlans.Single(p => p.DatePattern.ByMonthDay[0] == 5).Amount.ShouldBe(-60m); // -75 x 0.8
        result.ScaledPlans.Single(p => p.DatePattern.ByMonthDay[0] == 10).Amount.ShouldBe(-40m); // -50 x 0.8
    }

    [Fact]
    public void ScaleToMeetGoal_counts_banked_money_as_already_contributed_so_the_plans_ask_for_less()
    {
        // Same 50x12 + 50x12 = 1,200 the goal needs — but 240 is already banked,
        // so the ongoing ask drops to 960 and the plans scale down to 0.8.
        var goal = GoalFundedBy(-100m);
        var planA = PlanOn(goal, -50m, 5, startingAllocation: 240m);
        var planB = PlanOn(goal, -50m, 10);

        var result = EarmarkScaling.ScaleToMeetGoal(goal, [planA, planB]);

        result.CurrentTotal.ShouldBe(1200m);
        result.NeededTotal.ShouldBe(960m); // 1,200 goal less 240 already banked
        result.ScaledPlans.ShouldAllBe(p => p.Amount == -40m); // both -50 x 0.8
        result.ScaledPlans.Single(p => p.DatePattern.ByMonthDay[0] == 5).StartingAllocation.ShouldBe(240m); // banked money left untouched
    }

    [Fact]
    public void ScaleToMeetGoal_leaves_plans_that_already_fund_the_goal_unchanged()
    {
        // 50x12 + 50x12 = 1,200, exactly the goal — nothing to correct.
        var goal = GoalFundedBy(-100m);
        var planA = PlanOn(goal, -50m, 5);
        var planB = PlanOn(goal, -50m, 10);

        var result = EarmarkScaling.ScaleToMeetGoal(goal, [planA, planB]);

        result.CurrentTotal.ShouldBe(result.NeededTotal); // already met — the caller won't offer the adjustment
        result.ScaledPlans.ShouldAllBe(p => p.Amount == -50m);
    }

    [Fact]
    public void ScaleToMeetGoal_ignores_a_plans_skipped_occurrences_so_they_dont_move_the_ratio()
    {
        // A plan's skips are one-off deviations, not its ongoing rate: the shared
        // ratio is measured at full cadence, so a plan carrying skips comes out
        // with the exact same new amount as the identical plan carrying none — and
        // so does every OTHER plan sharing the goal. (Mirrors Consolidate's
        // KeepCurrentPace_counts_skipped_dates rule.)
        var goal = GoalFundedBy(-100m);
        var planA = PlanOn(goal, -30m, 5);
        var planB = PlanOn(goal, -50m, 10);

        // The same planA, but with two of its own occurrences skipped.
        var planAWithSkips = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = planA.Amount,
                DatePattern = planA.DatePattern.WithExcludedDates([new DateOnly(2025, 3, 5), new DateOnly(2025, 4, 5)]),
            },
            goal);

        var withoutSkips = EarmarkScaling.ScaleToMeetGoal(goal, [planA, planB]);
        var withSkips = EarmarkScaling.ScaleToMeetGoal(goal, [planAWithSkips, planB]);

        withSkips.CurrentTotal.ShouldBe(withoutSkips.CurrentTotal); // the skip doesn't lower the measured rate...
        withSkips.NeededTotal.ShouldBe(withoutSkips.NeededTotal);   // ...nor the target...
        // ...so neither the skipped plan nor its neighbor budges.
        withSkips.ScaledPlans.Single(p => p.DatePattern.ByMonthDay[0] == 5).Amount
            .ShouldBe(withoutSkips.ScaledPlans.Single(p => p.DatePattern.ByMonthDay[0] == 5).Amount);
        withSkips.ScaledPlans.Single(p => p.DatePattern.ByMonthDay[0] == 10).Amount
            .ShouldBe(withoutSkips.ScaledPlans.Single(p => p.DatePattern.ByMonthDay[0] == 10).Amount);
    }

    [Fact]
    public void ScaleToMeetGoal_holds_the_combined_contribution_under_the_affordability_ceiling()
    {
        var goal = GoalFundedBy(-100m);
        var plan = PlanOn(goal, -80m, 5);

        // Meeting the goal wants -100/cycle, but only 90 can be spared → held to 90, goal knowingly underfunded.
        var result = EarmarkScaling.ScaleToMeetGoal(goal, [plan], affordabilityCeiling: 90m);

        result.ScaledPlans.Single().Amount.ShouldBe(-90m);
        result.CappedToAffordability.ShouldBeTrue();
    }

    [Fact]
    public void ScaleToMeetGoal_is_uncapped_when_the_ceiling_covers_meeting_the_goal()
    {
        var goal = GoalFundedBy(-100m);
        var plan = PlanOn(goal, -80m, 5);

        var result = EarmarkScaling.ScaleToMeetGoal(goal, [plan], affordabilityCeiling: 150m);

        result.ScaledPlans.Single().Amount.ShouldBe(-100m); // ceiling above the -100 needed → meets the goal
        result.CappedToAffordability.ShouldBeFalse();
    }

    [Fact]
    public void ScaleToMeetGoal_capping_keeps_the_plans_relative_shares()
    {
        var goal = GoalFundedBy(-100m);
        var planA = PlanOn(goal, -30m, 5);
        var planB = PlanOn(goal, -50m, 10);

        // 80/cycle now, 100 needed; ceiling 60 binds → both cut to a 60 total, 3:5 preserved.
        var result = EarmarkScaling.ScaleToMeetGoal(goal, [planA, planB], affordabilityCeiling: 60m);

        result.ScaledPlans.Single(p => p.DatePattern.ByMonthDay[0] == 5).Amount.ShouldBe(-22.50m);
        result.ScaledPlans.Single(p => p.DatePattern.ByMonthDay[0] == 10).Amount.ShouldBe(-37.50m);
        result.CappedToAffordability.ShouldBeTrue();
    }

    // Scale (the amount-only cross-boundary re-rate) — same affordability cap, but silent (no result flag).

    [Fact]
    public void Scale_holds_the_combined_contribution_under_the_affordability_ceiling()
    {
        var oldGoal = Goal(-300m);
        var newGoal = Goal(-600m); // doubles → ratio 2
        var planA = Plan(oldGoal, -180m, 1);
        var planB = Plan(oldGoal, -120m, 2); // combined 300/cycle

        // Doubling wants 600/cycle, but only 450 can be spared → ratio held to 1.5, shares (3:2) preserved.
        var scaled = EarmarkScaling.Scale(new ScaleRequest
        {
            Goal = newGoal,
            PreviousGoalAmount = oldGoal.Amount,
            SurvivingPlans = [planA, planB],
        }, affordabilityCeiling: 450m);

        scaled.Single(p => p.DatePattern.ByMonthDay[0] == 1).Amount.ShouldBe(-270m); // -180 x 1.5
        scaled.Single(p => p.DatePattern.ByMonthDay[0] == 2).Amount.ShouldBe(-180m); // -120 x 1.5
    }

    [Fact]
    public void Scale_is_uncapped_when_the_ceiling_covers_the_new_amount()
    {
        var oldGoal = Goal(-300m);
        var newGoal = Goal(-600m);
        var planA = Plan(oldGoal, -180m, 1);
        var planB = Plan(oldGoal, -120m, 2);

        // Ceiling above the 600 the doubled goal wants → the full 2x scale goes through.
        var scaled = EarmarkScaling.Scale(new ScaleRequest
        {
            Goal = newGoal,
            PreviousGoalAmount = oldGoal.Amount,
            SurvivingPlans = [planA, planB],
        }, affordabilityCeiling: 700m);

        scaled.Single(p => p.DatePattern.ByMonthDay[0] == 1).Amount.ShouldBe(-360m); // -180 x 2
        scaled.Single(p => p.DatePattern.ByMonthDay[0] == 2).Amount.ShouldBe(-240m); // -120 x 2
    }
}
