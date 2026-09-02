using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

// WithoutPlansFor — the "room for these plans" filter behind the affordability ceiling. Dropping a goal's
// EarMarkPatterns and re-forecasting is how the App works out how much free money those plans were
// reserving, so a suggestion can be sized against it (see AffordabilityCeilingTests for the App-wiring note).
public class ForecastOptionsTests
{
    // A single year-end goal you save toward — one occurrence on Dec 1, but active from January so the jar
    // (and a plan feeding it) can exist through the year. No mid-year release to muddy the accumulation.
    private static FinancialPattern Goal(int id) => FinancialPattern.Create(new FinancialPatternOptions
    {
        FinanceId = id,
        Source = $"goal{id}",
        DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            DtStart = new DateOnly(2026, 12, 1),
            Until = new DateOnly(2026, 12, 1),
            ActiveFrom = new DateOnly(2026, 1, 1),
        }),
        Amount = -600m,
    });

    private static EarMarkPattern Plan(int financeId) => EarMarkPattern.Create(
        new EarMarkPatternOptions
        {
            FinanceId = financeId,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                DtStart = new DateOnly(2026, 1, 1),
                Until = new DateOnly(2026, 11, 1), // contribute Jan..Nov toward the Dec goal
            }),
            Amount = -50m,
        },
        Goal(financeId));

    private static ForecastOptions Options(IReadOnlyList<FinancialPattern> goals, IReadOnlyList<EarMarkPattern> plans) => new()
    {
        FinancialPatterns = goals,
        EarMarkPatterns = plans,
        StartingBalance = 1000m,
        AsOfDate = new DateOnly(2026, 1, 1),
        HorizonEndDate = new DateOnly(2026, 12, 31),
    };

    [Fact]
    public void WithoutPlansFor_drops_only_the_named_goals_plans()
    {
        var options = Options([Goal(1), Goal(2)], [Plan(1), Plan(2)]);

        var trimmed = options.WithoutPlansFor(new HashSet<int> { 1 });

        trimmed.EarMarkPatterns.ShouldHaveSingleItem().FinanceId.ShouldBe(2); // goal 1's plan gone, goal 2's kept
        trimmed.FinancialPatterns.ShouldBe(options.FinancialPatterns);        // the goals themselves untouched
    }

    [Fact]
    public void WithoutPlansFor_leaves_everything_when_nothing_is_named()
    {
        var options = Options([Goal(1)], [Plan(1)]);

        options.WithoutPlansFor(new HashSet<int>()).EarMarkPatterns.ShouldHaveSingleItem().FinanceId.ShouldBe(1);
    }

    [Fact]
    public void Omitting_a_plan_raises_the_free_funds_it_was_reserving()
    {
        var options = Options([Goal(1)], [Plan(1)]);
        var midYear = new DateOnly(2026, 6, 1); // months of savings banked, the Dec goal not yet released

        var withPlan = TransactionLogBookFactory.CreateForecast(options);
        var withoutPlan = TransactionLogBookFactory.CreateForecast(options.WithoutPlansFor(new HashSet<int> { 1 }));

        // Removing the plan means its scheduled contributions no longer sit in a jar — that money reads as
        // free again, which is exactly the "room for this plan" an affordability check is after.
        withoutPlan.PrimaryAccountPage.AvailableFunds(midYear)!.Value
            .ShouldBeGreaterThan(withPlan.PrimaryAccountPage.AvailableFunds(midYear)!.Value);
    }
}
