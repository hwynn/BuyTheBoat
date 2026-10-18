using BuyTheBoat.Domain;
using Shouldly;

namespace BuyTheBoat.App.Tests;

// Covers the Earmark form narrative's sentence naming a goal's other plans running at the same time.
public class ConcurrentPlansSentenceTests
{
    private static readonly FinancialPattern Goal = FinancialPattern.Create(new FinancialPatternOptions
    {
        FinanceId = 1,
        Source = "Vacation",
        DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Yearly,
            DtStart = new DateOnly(2026, 12, 1),
            Count = 1,
            ActiveFrom = new DateOnly(2026, 1, 1),
        }),
        Amount = -2000m,
    });

    /// <summary>[CALC] Builds a plan for the goal starting on the given day, contributing the given amount on the given cadence.</summary>
    private static EarMarkPattern Plan(DateOnly start, decimal amount, RecurrenceFrequency frequency = RecurrenceFrequency.Monthly, int interval = 1) =>
        EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -amount,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = frequency,
                    Interval = interval,
                    DtStart = start,
                    Until = new DateOnly(2026, 11, 30),
                }),
            },
            Goal);

    private static readonly EarMarkPattern Mine = Plan(new DateOnly(2026, 1, 1), 100m);
    private static readonly EarMarkPattern Partners = Plan(new DateOnly(2026, 2, 1), 50m);
    private static readonly EarMarkPattern Biweekly = Plan(new DateOnly(2026, 3, 1), 20m, RecurrenceFrequency.Weekly, interval: 2);

    [Fact]
    public void A_lone_plan_adds_nothing()
    {
        EarmarkFormText.ConcurrentPlansSentence([Mine], Mine, isOneOff: false).ShouldBeEmpty();
    }

    [Fact]
    public void One_other_plan_is_named_with_its_amount_and_cadence()
    {
        EarmarkFormText.ConcurrentPlansSentence([Mine, Partners], Mine, isOneOff: false)
            .ShouldBe(" Another plan also sets aside 50 a month toward it; the chart and the figures beside it count both.");
    }

    [Fact]
    public void Several_other_plans_are_listed()
    {
        EarmarkFormText.ConcurrentPlansSentence([Mine, Partners, Biweekly], Mine, isOneOff: false)
            .ShouldBe(" 2 other plans also set aside 50 a month and 20 every 2 weeks toward it; the chart and the figures beside it count them all.");
    }

    [Fact]
    public void One_off_mode_names_every_plan()
    {
        EarmarkFormText.ConcurrentPlansSentence([Mine, Partners, Biweekly], Mine, isOneOff: true)
            .ShouldBe(" 3 plans fund it — 100 a month, 50 a month and 20 every 2 weeks; the chart and the figures beside it count them all.");
    }
}
