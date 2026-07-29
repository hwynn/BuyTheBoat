using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

public class AllocationPlanProposerTests
{
    private static readonly DateOnly AsOf = new(2025, 1, 1);

    private static FinancialPattern MonthlyBill(decimal amount, int dayOfMonth, DateOnly start, DateOnly until, int id = 1) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = id,
            Source = $"bill{id}",
            Amount = amount,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [dayOfMonth],
                Start = start,
                Until = until,
            }),
        });

    private static FinancialPattern MonthlyIncome(decimal amount, int dayOfMonth, DateOnly start, DateOnly until, int id = 100) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = id,
            Source = $"income{id}",
            Amount = amount,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [dayOfMonth],
                Start = start,
                Until = until,
            }),
        });

    private static FinancialPattern BiweeklyIncome(decimal amount, DateOnly start, DateOnly until, int id = 100) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = id,
            Source = $"income{id}",
            Amount = amount,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Weekly,
                Interval = 2,
                Start = start,
                Until = until,
            }),
        });

    [Fact]
    public void Rejects_a_non_outflow()
    {
        var income = MonthlyIncome(3000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2027, 1, 1), id: 1);
        Should.Throw<ArgumentException>(() => AllocationPlanProposer.Propose(income, [income], AsOf));
    }

    [Fact]
    public void Single_income_paces_on_the_income_rhythm_and_links_to_the_outflow()
    {
        // Monthly income (25th) + monthly bill (1st): 7 paydays and 7 bill
        // occurrences in the window, so bill x billOccs / incomeOccs is the
        // full bill amount each month.
        var income = MonthlyIncome(3000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2027, 1, 1));
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 8, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill, income], AsOf);

        result.Plan.FinanceId.ShouldBe(bill.FinanceId);
        result.Plan.DatePattern.Frequency.ShouldBe(RecurrenceFrequency.Monthly);
        result.Plan.Amount.ShouldBe(-300m); // negative = into the jar
        result.StartingEarmark.ShouldBeNull(); // the 25th contribution precedes the 1st-of-next-month bill
    }

    [Fact]
    public void Biweekly_income_against_a_monthly_bill_paces_below_the_full_amount()
    {
        // More paydays than bill occurrences, so each contribution is a
        // fraction of the bill — the whole point of pacing. Exact cents depend
        // on the payday count; the invariant is "less than the full amount,
        // still negative (into the jar)".
        var income = BiweeklyIncome(1000m, new DateOnly(2025, 1, 3), new DateOnly(2027, 1, 1));
        var bill = MonthlyBill(-600m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 8, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill, income], AsOf);

        result.Plan.DatePattern.Frequency.ShouldBe(RecurrenceFrequency.Weekly);
        result.Plan.DatePattern.Interval.ShouldBe(2);
        result.Plan.Amount.ShouldBeLessThan(0m);
        result.Plan.Amount.ShouldBeGreaterThan(-600m);
    }

    [Fact]
    public void A_bill_due_before_the_first_payday_gets_a_full_starting_earmark()
    {
        // Bill on the 10th (first occurrence Jan 10) with income on the 25th:
        // the first bill lands before the first contribution, so the gap is
        // front-loaded for the whole occurrence amount, dated at the as-of day.
        var income = MonthlyIncome(3000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2027, 1, 1));
        var bill = MonthlyBill(-300m, 10, new DateOnly(2025, 1, 10), new DateOnly(2025, 6, 10));

        var result = AllocationPlanProposer.Propose(bill, [bill, income], AsOf);

        result.StartingEarmark.ShouldNotBeNull();
        result.StartingEarmark!.Amount.ShouldBe(300m);
        result.StartingEarmark.Date.ShouldBe(AsOf);
        result.StartingEarmark.FinanceId.ShouldBe(bill.FinanceId);
    }

    [Fact]
    public void No_income_front_loads_the_full_amount_on_the_bills_cadence()
    {
        // No income at all: the plan reserves the full amount every cycle,
        // starting at the as-of date so the first contribution front-loads.
        // No divide-by-zero, no starting earmark.
        var bill = MonthlyBill(-500m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 6, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill], AsOf);

        result.Plan.Amount.ShouldBe(-500m);
        result.Plan.DatePattern.Frequency.ShouldBe(RecurrenceFrequency.Monthly);
        result.StartingEarmark.ShouldBeNull();
        result.Plan.DatePattern.GetOccurrences(AsOf, result.Plan.DatePattern.Until)[0].ShouldBe(AsOf);
    }

    [Fact]
    public void A_one_off_outflow_with_no_income_reserves_the_full_amount_once_up_front()
    {
        // A single-occurrence outflow must not generate one full contribution per
        // frequency cycle between the as-of date and its due date — it reserves
        // once, up front.
        var oneOff = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 5,
            Source = "Car repair",
            Amount = -800m,
            Mandatory = false,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly, // 8 months of cycles from the as-of date
                Start = new DateOnly(2025, 9, 1),
                Count = 1,
            }),
        });

        var result = AllocationPlanProposer.Propose(oneOff, [oneOff], AsOf);

        result.Plan.Amount.ShouldBe(-800m);
        result.Plan.DatePattern.GetOccurrences(AsOf, result.Plan.DatePattern.Until).Count.ShouldBe(1);
        result.StartingEarmark.ShouldBeNull();
    }

    [Fact]
    public void More_than_one_income_stream_falls_back_to_the_front_loaded_shape()
    {
        var income1 = MonthlyIncome(2000m, 15, new DateOnly(2024, 1, 15), new DateOnly(2027, 1, 1), id: 100);
        var income2 = MonthlyIncome(1000m, 28, new DateOnly(2024, 1, 28), new DateOnly(2027, 1, 1), id: 101);
        var bill = MonthlyBill(-500m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 6, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill, income1, income2], AsOf);

        result.Plan.Amount.ShouldBe(-500m); // full amount, the front-loaded shape
        result.StartingEarmark.ShouldBeNull();
    }

    [Fact]
    public void Income_that_has_already_ended_is_treated_as_no_usable_income()
    {
        // The one income stream ended before the as-of date, so there are no
        // paydays in the window — this must not divide by zero; it falls to the
        // front-loaded shape.
        var income = MonthlyIncome(3000m, 25, new DateOnly(2023, 1, 25), new DateOnly(2024, 12, 1));
        var bill = MonthlyBill(-500m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 6, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill, income], AsOf);

        result.Plan.Amount.ShouldBe(-500m);
    }

    [Fact]
    public void The_plan_never_outruns_the_outflows_end_date()
    {
        // EarMarkPattern.Create enforces Until <= the goal's; a successful
        // proposal proves it, and we assert it directly too.
        var income = MonthlyIncome(3000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2030, 1, 1));
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 8, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill, income], AsOf);

        result.Plan.DatePattern.Until.ShouldBeLessThanOrEqualTo(bill.DatePattern.Until);
    }

    [Fact]
    public void A_one_time_goal_is_just_the_single_occurrence_case()
    {
        // A one-time goal is a FinancialPattern with one occurrence, so the same
        // proposer handles it — billOccurrences = 1.
        var income = MonthlyIncome(4000m, 15, new DateOnly(2024, 1, 15), new DateOnly(2027, 1, 1));
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 7,
            Source = "Trip to Japan",
            Amount = -1200m,
            Mandatory = false,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                Start = new DateOnly(2025, 12, 1),
                Count = 1,
            }),
        });

        var result = AllocationPlanProposer.Propose(goal, [goal, income], AsOf);

        result.Plan.FinanceId.ShouldBe(7);
        result.Plan.Amount.ShouldBeLessThan(0m);
    }

    [Fact]
    public void A_future_dated_outflow_is_returned_stretched_back_to_the_as_of_date()
    {
        // The bill's first occurrence (Feb 1) is after the as-of date, so its plan
        // begins accumulating before it — the returned outflow carries an ActiveFrom
        // at the as-of date so the plan fits (planning/15). Occurrences are untouched.
        var bill = MonthlyBill(-300m, 1, new DateOnly(2025, 2, 1), new DateOnly(2025, 8, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill], AsOf);

        result.Outflow.DatePattern.ActiveFrom.ShouldBe(AsOf);
        result.Outflow.DatePattern.GetOccurrences().ShouldBe(bill.DatePattern.GetOccurrences());
    }

    [Fact]
    public void An_outflow_already_covering_the_as_of_date_keeps_a_null_active_from()
    {
        // Its first occurrence is on the as-of date, so nothing accumulates before
        // it — the sparing rule leaves ActiveFrom null.
        var bill = MonthlyBill(-300m, 1, AsOf, new DateOnly(2025, 8, 1));

        var result = AllocationPlanProposer.Propose(bill, [bill], AsOf);

        result.Outflow.DatePattern.ActiveFrom.ShouldBeNull();
    }
}
