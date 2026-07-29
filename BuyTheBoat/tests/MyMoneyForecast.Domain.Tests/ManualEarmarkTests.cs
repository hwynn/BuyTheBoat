using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

public class ManualEarmarkTests
{
    private static (FinancialPattern Goal, EarMarkPattern Pattern) GoalWithPlan()
    {
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Japan trip",
            Amount = -3000m,
            Mandatory = false,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                Start = new DateOnly(2026, 6, 1),
                Count = 1,
                ActiveFrom = new DateOnly(2025, 1, 1), // saving starts before the due date (planning/15)
            }),
        });
        var pattern = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    Start = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 12, 1),
                }),
            },
            goal);
        return (goal, pattern);
    }

    [Fact]
    public void A_manual_earmark_within_the_pattern_span_is_valid_in_both_directions()
    {
        var (_, pattern) = GoalWithPlan();

        ManualEarmark.Create(new ManualEarmarkOptions
        {
            FinanceId = 1, Date = new DateOnly(2025, 6, 15), Amount = 300m,
        }, pattern).Amount.ShouldBe(300m);

        ManualEarmark.Create(new ManualEarmarkOptions
        {
            FinanceId = 1, Date = new DateOnly(2025, 6, 15), Amount = -50m,
        }, pattern).Amount.ShouldBe(-50m);
    }

    [Fact]
    public void The_pattern_span_is_the_jars_lifetime_so_dates_outside_it_are_rejected()
    {
        var (_, pattern) = GoalWithPlan();

        Should.Throw<ArgumentException>(() => ManualEarmark.Create(new ManualEarmarkOptions
        {
            FinanceId = 1, Date = new DateOnly(2024, 12, 31), Amount = 300m, // before Start
        }, pattern));

        Should.Throw<ArgumentException>(() => ManualEarmark.Create(new ManualEarmarkOptions
        {
            FinanceId = 1, Date = new DateOnly(2025, 12, 2), Amount = 300m, // after Until
        }, pattern));
    }

    [Fact]
    public void Zero_amounts_and_mismatched_finance_ids_are_rejected()
    {
        var (_, pattern) = GoalWithPlan();

        Should.Throw<ArgumentException>(() => ManualEarmark.Create(new ManualEarmarkOptions
        {
            FinanceId = 1, Date = new DateOnly(2025, 6, 15), Amount = 0m,
        }, pattern));

        Should.Throw<ArgumentException>(() => ManualEarmark.Create(new ManualEarmarkOptions
        {
            FinanceId = 2, Date = new DateOnly(2025, 6, 15), Amount = 300m,
        }, pattern));
    }
}
