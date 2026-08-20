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
            // active span reaches back that far (planning/15, ActiveFrom).
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
        // ActiveFrom reaching back to the save-start day (planning/15), so the
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
}
