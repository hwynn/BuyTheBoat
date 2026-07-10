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
            Start = new DateOnly(2025, 1, 1),
            Count = 1,
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
                    Start = new DateOnly(2024, 6, 1),
                    Until = new DateOnly(2025, 1, 1),
                }),
                Amount = -200m,
            },
            goal));
    }

    [Fact]
    public void Saving_can_start_well_before_the_goals_own_date_range()
    {
        // This is the whole point of an earmark pattern — saving in advance
        // for a single-occurrence goal. Confirmed with the user (2026-07-07)
        // that this must be allowed: earmark patterns aren't tied to the
        // timing of any specific triggering event, including their own goal.
        var goal = BoatGoal(); // single occurrence on 2025-01-01

        var earmark = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    Start = new DateOnly(2022, 1, 1), // three years before the goal's own start
                    ByMonthDay = [1],
                    Until = new DateOnly(2025, 1, 1),
                }),
                Amount = -138.89m,
            },
            goal);

        earmark.DatePattern.Start.ShouldBeLessThan(goal.DatePattern.Start);
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
                    Start = new DateOnly(2024, 6, 1),
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
                    Start = new DateOnly(2024, 1, 1),
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
                    Start = new DateOnly(2024, 1, 1),
                    ByMonthDay = [1],
                    Until = new DateOnly(2025, 1, 1),
                }),
                Amount = -100m,
                StartingAllocation = -1m,
            },
            goal));
    }
}
