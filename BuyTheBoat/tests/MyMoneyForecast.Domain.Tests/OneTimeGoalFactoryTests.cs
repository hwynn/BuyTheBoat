using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

public class OneTimeGoalFactoryTests
{
    [Fact]
    public void Creates_a_goal_with_a_single_occurrence_on_the_due_date()
    {
        var result = OneTimeGoalFactory.Create(new OneTimeGoalRequest
        {
            FinanceId = 1,
            Description = "Trip to Japan",
            AmountNeeded = 10_000m,
            DueDate = new DateOnly(2025, 6, 1),
            StartSavingDate = new DateOnly(2025, 1, 19),
        });

        result.Goal.Amount.ShouldBe(-10_000m);
        result.Goal.Mandatory.ShouldBeFalse();
        result.Goal.Priority.ShouldBe(3); // default, when the caller doesn't specify one
        result.Goal.DatePattern.GetOccurrences().ShouldBe([new DateOnly(2025, 6, 1)]);
    }

    [Fact]
    public void Priority_is_set_on_the_goal_not_the_savings_plan()
    {
        // EarMarkPattern has no priority of its own — per the documented
        // model, an earmark's priority comes from its goal's FinancialPattern.
        var result = OneTimeGoalFactory.Create(new OneTimeGoalRequest
        {
            FinanceId = 1,
            Description = "Trip to Japan",
            AmountNeeded = 10_000m,
            DueDate = new DateOnly(2025, 6, 1),
            StartSavingDate = new DateOnly(2025, 1, 19),
            Priority = 5,
        });

        result.Goal.Priority.ShouldBe(5);
    }

    [Fact]
    public void Splits_the_amount_evenly_across_monthly_installments()
    {
        // Six monthly installments, 19th of the month, Jan 19 through Jun 19 —
        // but the due date is Jun 1, so the last installment (Jun 19) is
        // after the goal is due and shouldn't be generated; only 5 fall
        // strictly within [start, due].
        var result = OneTimeGoalFactory.Create(new OneTimeGoalRequest
        {
            FinanceId = 1,
            Description = "Trip to Japan",
            AmountNeeded = 1000m,
            DueDate = new DateOnly(2025, 6, 1),
            StartSavingDate = new DateOnly(2025, 1, 19),
            Frequency = SavingsFrequency.Monthly,
        });

        var occurrences = result.SavingsPlan.DatePattern.GetOccurrences();
        occurrences.Count.ShouldBe(5); // Jan 19, Feb 19, Mar 19, Apr 19, May 19
        result.SavingsPlan.Amount.ShouldBe(-200m); // 1000 / 5, evenly split
    }

    [Fact]
    public void Savings_plan_is_linked_to_the_goal_by_finance_id()
    {
        var result = OneTimeGoalFactory.Create(new OneTimeGoalRequest
        {
            FinanceId = 42,
            Description = "New couch",
            AmountNeeded = 800m,
            DueDate = new DateOnly(2025, 12, 1),
            StartSavingDate = new DateOnly(2025, 1, 1),
        });

        result.SavingsPlan.FinanceId.ShouldBe(result.Goal.FinanceId);
        result.SavingsPlan.FinanceId.ShouldBe(42);
    }

    [Fact]
    public void Every_other_week_frequency_is_honored()
    {
        var result = OneTimeGoalFactory.Create(new OneTimeGoalRequest
        {
            FinanceId = 1,
            Description = "Concert tickets",
            AmountNeeded = 300m,
            DueDate = new DateOnly(2025, 3, 1),
            StartSavingDate = new DateOnly(2025, 1, 1),
            Frequency = SavingsFrequency.EveryOtherWeek,
        });

        var occurrences = result.SavingsPlan.DatePattern.GetOccurrences();
        occurrences.Count.ShouldBeGreaterThan(1);
        for (var i = 1; i < occurrences.Count; i++)
        {
            occurrences[i].DayNumber.ShouldBe(occurrences[i - 1].DayNumber + 14);
        }
    }

    [Fact]
    public void Start_date_on_or_after_due_date_is_rejected()
    {
        Should.Throw<ArgumentException>(() => OneTimeGoalFactory.Create(new OneTimeGoalRequest
        {
            FinanceId = 1,
            Description = "Too late",
            AmountNeeded = 100m,
            DueDate = new DateOnly(2025, 1, 1),
            StartSavingDate = new DateOnly(2025, 1, 1),
        }));
    }

    [Fact]
    public void Even_a_very_short_window_still_produces_one_installment_on_the_start_date()
    {
        // BuildSavingsDatePattern always derives its ByDay/ByMonthDay from
        // StartSavingDate, so StartSavingDate always qualifies as the first
        // occurrence regardless of how little time is left before the due
        // date — there's no "too soon" case that produces zero installments.
        var result = OneTimeGoalFactory.Create(new OneTimeGoalRequest
        {
            FinanceId = 1,
            Description = "Very soon",
            AmountNeeded = 100m,
            DueDate = new DateOnly(2025, 1, 3),
            StartSavingDate = new DateOnly(2025, 1, 1),
            Frequency = SavingsFrequency.Monthly,
        });

        result.SavingsPlan.DatePattern.GetOccurrences().ShouldBe([new DateOnly(2025, 1, 1)]);
        result.SavingsPlan.Amount.ShouldBe(-100m);
    }

    [Fact]
    public void The_goal_is_active_from_the_save_start_date_so_its_plan_can_begin_before_the_due_date()
    {
        // The goal's only occurrence is its due date, but the savings plan starts
        // earlier — ActiveFrom stretches the goal's active span back to the
        // save-start day so the plan (and jar) legitimately live before the due
        // date (planning/15). The due-date occurrence itself is unchanged.
        var result = OneTimeGoalFactory.Create(new OneTimeGoalRequest
        {
            FinanceId = 1,
            Description = "Trip to Japan",
            AmountNeeded = 1000m,
            DueDate = new DateOnly(2025, 6, 1),
            StartSavingDate = new DateOnly(2025, 1, 19),
        });

        result.Goal.DatePattern.ToOptions().ActiveFrom.ShouldBe(new DateOnly(2025, 1, 19));
        result.Goal.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 1, 19));
        result.Goal.DatePattern.GetOccurrences().ShouldBe([new DateOnly(2025, 6, 1)]);
    }
}
