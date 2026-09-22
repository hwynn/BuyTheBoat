using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

public class EarMarkPatternTests
{
    // A one-time goal is a single-occurrence FinancialPattern — its own
    // DatePattern.Start and .Until both land on the due date itself.
    private static FinancialPattern BoatGoal() => FinancialPattern.Create(new FinancialPatternOptions
    {
        FinanceId = 33777,
        Source = "Boat Dealer",
        DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Yearly,
            DtStart = new DateOnly(2025, 1, 1),
            Count = 1,
            // Saving started three years before the due date, so the goal's
            // active span reaches back that far (its ActiveFrom).
            ActiveFrom = new DateOnly(2022, 1, 1),
        }),
        Amount = -5000m,
    });

    [Fact]
    public void Finance_id_must_match_the_goal_patterns_finance_id()
    {
        var goal = BoatGoal();

        Should.Throw<ArgumentException>(() => EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 999, // does not match goal.FinanceId
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Weekly,
                    DtStart = new DateOnly(2024, 6, 1),
                    Until = new DateOnly(2025, 1, 1),
                }),
                Amount = -200m,
            },
            goal));
    }

    [Fact]
    public void Saving_can_start_well_before_the_goals_own_date_range()
    {
        // The whole point of an earmark pattern — saving in advance for a
        // single-occurrence goal. It is allowed because the goal carries an
        // ActiveFrom reaching back to the save-start day, so the
        // earmark stays within the goal's active span even though it begins
        // before the goal's own single occurrence.
        var goal = BoatGoal(); // single occurrence on 2025-01-01

        var earmark = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    DtStart = new DateOnly(2022, 1, 1), // three years before the goal's own start
                    ByMonthDay = [1],
                    Until = new DateOnly(2025, 1, 1),
                }),
                Amount = -138.89m,
            },
            goal);

        earmark.DatePattern.ActiveStart.ShouldBeLessThan(goal.DatePattern.DtStart);
    }

    [Fact]
    public void Saving_cannot_still_be_happening_after_the_goals_due_date()
    {
        var goal = BoatGoal(); // single occurrence on 2025-01-01

        Should.Throw<ArgumentException>(() => EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Weekly,
                    DtStart = new DateOnly(2024, 6, 1),
                    Until = new DateOnly(2025, 2, 1), // after the goal's own due date
                }),
                Amount = -200m,
            },
            goal));
    }

    [Fact]
    public void Starting_allocation_defaults_to_zero_for_a_brand_new_jar()
    {
        var goal = BoatGoal();

        var earmark = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    DtStart = new DateOnly(2024, 1, 1),
                    ByMonthDay = [1],
                    Until = new DateOnly(2025, 1, 1),
                }),
                Amount = -100m,
            },
            goal);

        earmark.StartingAllocation.ShouldBe(0m);
    }

    [Fact]
    public void Starting_allocation_cannot_be_negative()
    {
        var goal = BoatGoal();

        Should.Throw<ArgumentException>(() => EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    DtStart = new DateOnly(2024, 1, 1),
                    ByMonthDay = [1],
                    Until = new DateOnly(2025, 1, 1),
                }),
                Amount = -100m,
                StartingAllocation = -1m,
            },
            goal));
    }

    [Fact]
    public void Saving_cannot_start_before_the_goals_active_span()
    {
        // The restored front half of 3.11.2.a2: an earmark reaching back before
        // the goal's active span (its ActiveFrom lead-in) is rejected. BoatGoal's
        // ActiveFrom is 2022-01-01, so an earmark starting in 2021 is too early.
        var goal = BoatGoal();

        Should.Throw<ArgumentException>(() => EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    DtStart = new DateOnly(2021, 1, 1), // before the goal's ActiveFrom (2022-01-01)
                    ByMonthDay = [1],
                    Until = new DateOnly(2025, 1, 1),
                }),
                Amount = -100m,
            },
            goal));
    }

    // ---- M1: joining two plans "without consequence" ----

    private static FinancialPattern WideGoal() => FinancialPattern.Create(new FinancialPatternOptions
    {
        FinanceId = 7,
        Source = "Rent",
        Amount = -1000m,
        DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            ByMonthDay = [1],
            DtStart = new DateOnly(2025, 1, 1),
            Until = new DateOnly(2025, 12, 31),
        }),
    });

    private static EarMarkPattern MonthlyPlan(FinancialPattern goal, decimal amount, DateOnly start, DateOnly until, decimal startingAllocation = 0m) =>
        EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = amount,
                StartingAllocation = startingAllocation,
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
    public void Two_plans_whose_occurrences_run_straight_on_at_the_same_rate_can_join()
    {
        var goal = WideGoal();
        var first = MonthlyPlan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 1));
        var second = MonthlyPlan(goal, -100m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 1));

        EarMarkPattern.CanJoinWithoutConsequence(first, second).ShouldBeTrue();
    }

    [Fact]
    public void Joining_spans_their_union_and_adds_the_two_jars_together()
    {
        var goal = WideGoal();
        var first = MonthlyPlan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 1), startingAllocation: 100m);
        var second = MonthlyPlan(goal, -100m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 1), startingAllocation: 50m);

        var joined = first.JoinedWith(second, goal);

        joined.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 1, 1));
        joined.DatePattern.Until.ShouldBe(new DateOnly(2025, 6, 1));
        joined.DatePattern.GetOccurrences().Count.ShouldBe(6); // Jan..Jun, exactly the two blocks, nothing added
        joined.StartingAllocation.ShouldBe(150m); // both jars combined
    }

    [Fact]
    public void A_gap_the_merge_would_fill_blocks_a_silent_join()
    {
        // First ends Mar, second doesn't pick up until May — a merged rule would
        // add an Apr contribution neither plan had, so this isn't consequence-free.
        var goal = WideGoal();
        var first = MonthlyPlan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 1));
        var second = MonthlyPlan(goal, -100m, new DateOnly(2025, 5, 1), new DateOnly(2025, 7, 1));

        EarMarkPattern.CanJoinWithoutConsequence(first, second).ShouldBeFalse();
    }

    [Fact]
    public void A_different_contribution_amount_blocks_a_silent_join()
    {
        var goal = WideGoal();
        var first = MonthlyPlan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 1));
        var second = MonthlyPlan(goal, -120m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 1));

        EarMarkPattern.CanJoinWithoutConsequence(first, second).ShouldBeFalse();
    }

    [Fact]
    public void A_different_recurrence_shape_blocks_a_silent_join()
    {
        var goal = WideGoal();
        var monthly = MonthlyPlan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 1));
        var weekly = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Weekly,
                    DtStart = new DateOnly(2025, 4, 1),
                    Until = new DateOnly(2025, 6, 1),
                }),
            },
            goal);

        EarMarkPattern.CanJoinWithoutConsequence(monthly, weekly).ShouldBeFalse();
    }

    [Fact]
    public void The_same_shape_on_a_different_phase_blocks_a_silent_join()
    {
        // Both are every-other-day, but one lands on odd days and the other on
        // even — merging would shift every one of the second plan's dates.
        var goal = WideGoal();
        var oddDays = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = -10m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Daily,
                    Interval = 2,
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 1, 9),
                }),
            },
            goal);
        var evenDays = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = -10m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Daily,
                    Interval = 2,
                    DtStart = new DateOnly(2025, 1, 2),
                    Until = new DateOnly(2025, 1, 10),
                }),
            },
            goal);

        EarMarkPattern.CanJoinWithoutConsequence(oddDays, evenDays).ShouldBeFalse();
    }

    [Fact]
    public void Joining_two_plans_that_cant_merge_cleanly_is_rejected()
    {
        var goal = WideGoal();
        var first = MonthlyPlan(goal, -100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 1));
        var second = MonthlyPlan(goal, -100m, new DateOnly(2025, 5, 1), new DateOnly(2025, 7, 1)); // Apr gap

        Should.Throw<ArgumentException>(() => first.JoinedWith(second, goal));
    }
}
