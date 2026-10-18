using BuyTheBoat.Domain;
using Shouldly;

namespace BuyTheBoat.App.Tests;

// Covers the Earmark form's "What can I allocate today?" text for one day.
public class AllocateTodayTextTests
{
    /// <summary>[CALC] Builds a one-time goal with the given finance id and priority.</summary>
    private static FinancialPattern Goal(int financeId, int priority) => FinancialPattern.Create(new FinancialPatternOptions
    {
        FinanceId = financeId,
        Source = $"goal{financeId}",
        DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Yearly,
            DtStart = new DateOnly(2026, 12, 1),
            Count = 1,
        }),
        Amount = -1000m,
        Priority = priority,
    });

    /// <summary>[CALC] Builds a jar holding the given amount; a null finance id is the safety cushion.</summary>
    private static FundJar Jar(int? financeId, decimal amount) =>
        new() { FinanceId = financeId, CurrentAmount = null, ExpectedAmount = amount, MilestoneAmount = null };

    /// <summary>[CALC] Builds one day's snapshot with the given free funds and jars.</summary>
    private static BalanceSnapshot Day(decimal free, params FundJar[] jars) => new()
    {
        SnapshotDate = new DateOnly(2026, 6, 1),
        FullAmount = null,
        ExpectedAmount = free + jars.Sum(jar => jar.ExpectedAmount),
        ExpectedFreeAmount = free,
        FundJars = jars,
        ActualTransactions = [],
        ExpectedTransactions = [],
        EarMarkEvents = [],
    };

    private static readonly IReadOnlyDictionary<int, int> Priorities = new Dictionary<int, int> { [1] = 3, [2] = 5, [3] = 3, [4] = 1 };

    [Fact]
    public void Set_aside_money_is_split_by_importance_against_this_goal()
    {
        var day = Day(40m, Jar(null, 200m), Jar(1, 100m), Jar(2, 300m), Jar(3, 50m), Jar(4, 25m));

        EarmarkFormText.AllocateTodayText(day, Goal(1, priority: 3), Priorities, sameDayOneOff: null).ShouldBe(string.Join(Environment.NewLine,
            "Free: 40.00",
            "Set aside: 675.00 — 100.00 for this goal, 300.00 for more important goals, 50.00 for equally important ones, 25.00 for less important ones, 200.00 in the safety cushion."));
    }

    [Fact]
    public void An_empty_cushion_is_left_out()
    {
        var day = Day(40m, Jar(null, 0m), Jar(1, 100m));

        EarmarkFormText.AllocateTodayText(day, Goal(1, priority: 3), Priorities, sameDayOneOff: null)
            .ShouldEndWith("0.00 for less important ones.");
    }

    [Fact]
    public void An_existing_one_off_on_the_same_day_is_noted()
    {
        var day = Day(40m, Jar(null, 0m), Jar(1, 100m));

        EarmarkFormText.AllocateTodayText(day, Goal(1, priority: 3), Priorities, sameDayOneOff: 75m)
            .ShouldEndWith("This goal already has a one-off of 75.00 on this day; saving adds to it.");
    }
}
