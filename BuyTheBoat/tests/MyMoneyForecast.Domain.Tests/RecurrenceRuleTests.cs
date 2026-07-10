using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

// Scenarios lifted directly from mini_fund_project/goals.xlsx so the expected
// answers are real numbers someone actually budgeted with, not invented fixtures.
public class RecurrenceRuleTests
{
    [Fact]
    public void Daily_interval_produces_evenly_spaced_occurrences()
    {
        // "paycheck": DAILY, interval=14, dstart=2025-01-24, until=2027-12-25
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Daily,
            Start = new DateOnly(2025, 1, 24),
            Interval = 14,
            Until = new DateOnly(2025, 4, 1),
        });

        var occurrences = rule.GetOccurrences();

        occurrences.ShouldBe([
            new DateOnly(2025, 1, 24),
            new DateOnly(2025, 2, 7),
            new DateOnly(2025, 2, 21),
            new DateOnly(2025, 3, 7),
            new DateOnly(2025, 3, 21),
        ]);
    }

    [Fact]
    public void Weekly_with_no_explicit_byday_defaults_to_the_start_dates_weekday()
    {
        // "groceries": WEEKLY, dstart=2025-01-19 (a Sunday), no byday column populated.
        // wkst=MO in the source only affects week-boundary counting for interval>1 —
        // it does not mean "occurs on Monday." This pins down that RFC 5545 default.
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Weekly,
            Start = new DateOnly(2025, 1, 19),
            Until = new DateOnly(2025, 2, 16),
        });

        var occurrences = rule.GetOccurrences();

        occurrences.ShouldAllBe(date => date.DayOfWeek == DayOfWeek.Sunday);
        occurrences.ShouldBe([
            new DateOnly(2025, 1, 19),
            new DateOnly(2025, 1, 26),
            new DateOnly(2025, 2, 2),
            new DateOnly(2025, 2, 9),
            new DateOnly(2025, 2, 16),
        ]);
    }

    [Fact]
    public void Monthly_with_single_bymonthday_lands_on_that_day_each_month()
    {
        // "bill pool": MONTHLY, dstart=2025-01-19, bymonthday=10, until=2025-12-25
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            Start = new DateOnly(2025, 1, 19),
            ByMonthDay = [10],
            Until = new DateOnly(2025, 5, 1),
        });

        var occurrences = rule.GetOccurrences();

        occurrences.ShouldBe([
            new DateOnly(2025, 2, 10),
            new DateOnly(2025, 3, 10),
            new DateOnly(2025, 4, 10),
        ]);
    }

    [Fact]
    public void Monthly_with_bymonthday_list_lands_on_every_listed_day()
    {
        // paycheck shape from class documentation.ods's worked example: 9th and 25th of each month.
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            Start = new DateOnly(2019, 1, 2),
            ByMonthDay = [9, 25],
            Until = new DateOnly(2019, 3, 1),
        });

        var occurrences = rule.GetOccurrences();

        occurrences.ShouldBe([
            new DateOnly(2019, 1, 9),
            new DateOnly(2019, 1, 25),
            new DateOnly(2019, 2, 9),
            new DateOnly(2019, 2, 25),
        ]);
    }

    [Fact]
    public void Weekly_with_explicit_byday_list_lands_on_each_listed_weekday()
    {
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Weekly,
            Start = new DateOnly(2025, 1, 6), // a Monday
            ByDay = [DayOfWeek.Monday, DayOfWeek.Wednesday],
            Until = new DateOnly(2025, 1, 20),
        });

        var occurrences = rule.GetOccurrences();

        occurrences.ShouldBe([
            new DateOnly(2025, 1, 6),
            new DateOnly(2025, 1, 8),
            new DateOnly(2025, 1, 13),
            new DateOnly(2025, 1, 15),
            new DateOnly(2025, 1, 20),
        ]);
    }

    [Fact]
    public void Count_is_resolved_to_an_equivalent_until_date_and_never_survives_construction()
    {
        // Mirrors mini_fund_project/MmfUtility.py's count_to_until_rrule, and the
        // chart-only rule in redesign/05-assumption-dependency-graph.md requiring
        // date_pattern to use `until`, never `count`.
        var rule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Weekly,
            Start = new DateOnly(2025, 1, 6),
            Count = 3,
        });

        rule.Until.ShouldBe(new DateOnly(2025, 1, 20));
        rule.GetOccurrences().Count.ShouldBe(3);
    }

    [Fact]
    public void Create_throws_when_neither_until_nor_count_is_set()
    {
        Should.Throw<ArgumentException>(() => RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Weekly,
            Start = new DateOnly(2025, 1, 6),
        }));
    }

    [Fact]
    public void Create_throws_when_both_until_and_count_are_set()
    {
        Should.Throw<ArgumentException>(() => RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Weekly,
            Start = new DateOnly(2025, 1, 6),
            Until = new DateOnly(2025, 2, 1),
            Count = 3,
        }));
    }
}
