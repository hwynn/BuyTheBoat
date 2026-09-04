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

    // WithManualEarmark — the "what if this one-off were saved" fold behind the live one-off preview.
    [Fact]
    public void WithManualEarmark_folds_a_one_off_into_the_forecast_jar()
    {
        var options = Options([Goal(1)], [Plan(1)]);
        var midYear = new DateOnly(2026, 6, 1); // before the Dec release, so the one-off is still in the jar
        var oneOff = ManualEarmark.Create(
            new ManualEarmarkOptions { FinanceId = 1, Date = new DateOnly(2026, 3, 15), Amount = 200m }, Plan(1));

        var without = TransactionLogBookFactory.CreateForecast(options);
        var with = TransactionLogBookFactory.CreateForecast(options.WithManualEarmark(oneOff));

        // The proposed $200 shows up in the goal's real jar, exactly as the saved forecast would compute it.
        JarExpectedOn(with, financeId: 1, midYear).ShouldBe(JarExpectedOn(without, financeId: 1, midYear) + 200m);
    }

    [Fact]
    public void WithManualEarmark_replaces_rather_than_stacks_at_the_same_finance_id_and_date()
    {
        var date = new DateOnly(2026, 3, 15);
        var first = ManualEarmark.Create(new ManualEarmarkOptions { FinanceId = 1, Date = date, Amount = 100m }, Plan(1));
        var second = ManualEarmark.Create(new ManualEarmarkOptions { FinanceId = 1, Date = date, Amount = 250m }, Plan(1));

        // Editing a one-off (same finance id + date) replaces it, never doubles it up.
        var options = Options([Goal(1)], [Plan(1)]).WithManualEarmark(first).WithManualEarmark(second);

        options.ManualEarmarks.Where(m => m.FinanceId == 1 && m.Date == date).ShouldHaveSingleItem().Amount.ShouldBe(250m);
    }

    // WithProposedPlan — the "what if I saved this plan" substitution behind the Earmark form's live
    // first-payment warning.
    [Fact]
    public void WithProposedPlan_swaps_the_named_segment_for_the_proposed_one()
    {
        var options = Options([Goal(1)], [Plan(1)]); // saved plan contributes $50/month
        var proposed = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    DtStart = new DateOnly(2026, 1, 1),
                    Until = new DateOnly(2026, 11, 1),
                }),
                Amount = -120m, // a bigger contribution typed in
            },
            Goal(1));
        var midYear = new DateOnly(2026, 6, 1);

        var swapped = options.WithProposedPlan(proposed, replacedActiveStart: new DateOnly(2026, 1, 1));

        // The saved $50/month is replaced, not stacked alongside — one plan, the proposed one.
        swapped.EarMarkPatterns.ShouldHaveSingleItem().Amount.ShouldBe(-120m);
        // And forecasting it reflects the bigger contributions in the goal's real jar.
        JarExpectedOn(TransactionLogBookFactory.CreateForecast(swapped), financeId: 1, midYear)
            .ShouldBeGreaterThan(JarExpectedOn(TransactionLogBookFactory.CreateForecast(options), financeId: 1, midYear));
    }

    [Fact]
    public void WithProposedPlan_adds_a_brand_new_plan_without_dropping_others()
    {
        // A null replacedActiveStart is a brand-new plan (goal 1 had none) — it's added, and an unrelated
        // goal's saved plan stays put.
        var options = Options([Goal(1), Goal(2)], [Plan(2)]);

        var result = options.WithProposedPlan(Plan(1), replacedActiveStart: null);

        result.EarMarkPatterns.Select(plan => plan.FinanceId).OrderBy(id => id).ShouldBe([1, 2]);
    }

    private static decimal JarExpectedOn(ForecastResult forecast, int financeId, DateOnly date) =>
        forecast.GetTimeline(financeId)
            .Where(entry => entry.Date <= date)
            .Select(entry => entry.Snapshot.FundJars.FirstOrDefault(jar => jar.FinanceId == financeId))
            .Last(jar => jar is not null)!.ExpectedAmount;
}
