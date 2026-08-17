using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

public class RestructureFactoryTests
{
    private static FinancialPattern Goal(decimal amount, DateOnly start, DateOnly until, int id = 1) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = id,
            Source = $"goal{id}",
            Description = "Boat fund",
            Amount = amount,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                Start = start,
                Until = until,
            }),
        });

    private static EarMarkPattern Plan(decimal amount, DateOnly start, DateOnly until, FinancialPattern goal) =>
        EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = amount,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    Start = start,
                    Until = until,
                }),
            },
            goal);

    private static RecurrenceRuleOptions MonthlyFrom(DateOnly start, DateOnly until) => new()
    {
        Frequency = RecurrenceFrequency.Monthly,
        ByMonthDay = [1],
        Start = start,
        Until = until,
    };

    [Fact]
    public void Truncates_the_predecessor_the_day_before_the_cut_and_starts_the_successor_on_it()
    {
        var goal = Goal(-5_000m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var predecessor = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1), goal);
        var cutDate = new DateOnly(2025, 7, 1);

        var result = RestructureFactory.Restructure(new RestructureRequest
        {
            Predecessor = predecessor,
            Goal = goal,
            CutDate = cutDate,
            SuccessorAmount = -150m,
            SuccessorSchedule = MonthlyFrom(cutDate, new DateOnly(2027, 1, 1)),
        });

        result.Predecessor.DatePattern.Until.ShouldBe(new DateOnly(2025, 6, 30));
        result.Successor.DatePattern.Start.ShouldBe(cutDate);
        result.Successor.Amount.ShouldBe(-150m);
    }

    [Fact]
    public void The_successor_shares_the_predecessors_finance_id_not_a_new_one()
    {
        // Unlike BreakOffFactory (a new finance_id for a changed bill/goal),
        // item 8 never touches the goal — F27's whole point is that one
        // finance_id can now have more than one EarMarkPattern.
        var goal = Goal(-5_000m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var predecessor = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1), goal);
        var cutDate = new DateOnly(2025, 7, 1);

        var result = RestructureFactory.Restructure(new RestructureRequest
        {
            Predecessor = predecessor,
            Goal = goal,
            CutDate = cutDate,
            SuccessorAmount = -150m,
            SuccessorSchedule = MonthlyFrom(cutDate, new DateOnly(2027, 1, 1)),
        });

        result.Successor.FinanceId.ShouldBe(predecessor.FinanceId);
        result.Predecessor.FinanceId.ShouldBe(predecessor.FinanceId);
    }

    [Fact]
    public void The_successor_has_no_starting_allocation()
    {
        // F28: no jar hand-off is needed at all — the same finance_id means
        // the same jar throughout, so its balance carries across the cut via
        // the ordinary cascade, with nothing to seed.
        var goal = Goal(-5_000m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var predecessor = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1), goal);
        var cutDate = new DateOnly(2025, 7, 1);

        var result = RestructureFactory.Restructure(new RestructureRequest
        {
            Predecessor = predecessor,
            Goal = goal,
            CutDate = cutDate,
            SuccessorAmount = -150m,
            SuccessorSchedule = MonthlyFrom(cutDate, new DateOnly(2027, 1, 1)),
        });

        result.Successor.StartingAllocation.ShouldBe(0m);
    }

    [Fact]
    public void A_predecessor_plan_that_already_ended_earlier_keeps_its_own_earlier_end()
    {
        var goal = Goal(-5_000m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        // The plan itself was already set to stop in May, before the July cut.
        var predecessor = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 5, 1), goal);
        var cutDate = new DateOnly(2025, 7, 1);

        var result = RestructureFactory.Restructure(new RestructureRequest
        {
            Predecessor = predecessor,
            Goal = goal,
            CutDate = cutDate,
            SuccessorAmount = -150m,
            SuccessorSchedule = MonthlyFrom(cutDate, new DateOnly(2027, 1, 1)),
        });

        result.Predecessor.DatePattern.Until.ShouldBe(new DateOnly(2025, 5, 1));
    }

    [Fact]
    public void Rejects_a_cut_date_on_or_before_the_predecessors_start()
    {
        var goal = Goal(-5_000m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var predecessor = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1), goal);

        Should.Throw<ArgumentException>(() => RestructureFactory.Restructure(new RestructureRequest
        {
            Predecessor = predecessor,
            Goal = goal,
            CutDate = new DateOnly(2025, 1, 1), // == predecessor's own Start
            SuccessorAmount = -150m,
            SuccessorSchedule = MonthlyFrom(new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1)),
        }));
    }

    [Fact]
    public void Rejects_a_successor_schedule_that_doesnt_start_exactly_on_the_cut_date()
    {
        var goal = Goal(-5_000m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var predecessor = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1), goal);

        Should.Throw<ArgumentException>(() => RestructureFactory.Restructure(new RestructureRequest
        {
            Predecessor = predecessor,
            Goal = goal,
            CutDate = new DateOnly(2025, 7, 1),
            SuccessorAmount = -150m,
            SuccessorSchedule = MonthlyFrom(new DateOnly(2025, 8, 1), new DateOnly(2027, 1, 1)), // off by a month
        }));
    }

    // planning/17, item 22 (F31): "stop contributing, keep the jar alive" is
    // Restructure targeting a zero-rate successor, not a separate mechanism.
    [Fact]
    public void StopContributing_zeroes_the_successors_amount()
    {
        var goal = Goal(-5_000m, new DateOnly(2025, 1, 1), new DateOnly(2026, 6, 1));
        var predecessor = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2026, 6, 1), goal);
        var cutDate = new DateOnly(2025, 7, 1);

        var result = RestructureFactory.StopContributing(predecessor, goal, cutDate);

        result.Successor.Amount.ShouldBe(0m);
    }

    [Fact]
    public void StopContributing_puts_the_successors_one_occurrence_at_the_goals_own_due_date()
    {
        var dueDate = new DateOnly(2026, 6, 1);
        var goal = Goal(-5_000m, new DateOnly(2025, 1, 1), dueDate);
        var predecessor = Plan(-100m, new DateOnly(2025, 1, 1), dueDate, goal);
        var cutDate = new DateOnly(2025, 7, 1);

        var result = RestructureFactory.StopContributing(predecessor, goal, cutDate);

        result.Successor.DatePattern.GetOccurrences().ShouldBe(new[] { dueDate });
    }

    [Fact]
    public void StopContributings_active_span_reaches_back_to_the_cut_date()
    {
        // The jar must stay alive/computable from the cut date onward, not
        // just on the due date itself — checked via ActiveStart, the same
        // helper containment checks use throughout.
        var dueDate = new DateOnly(2026, 6, 1);
        var goal = Goal(-5_000m, new DateOnly(2025, 1, 1), dueDate);
        var predecessor = Plan(-100m, new DateOnly(2025, 1, 1), dueDate, goal);
        var cutDate = new DateOnly(2025, 7, 1);

        var result = RestructureFactory.StopContributing(predecessor, goal, cutDate);

        result.Successor.DatePattern.ActiveStart.ShouldBe(cutDate);
    }

    [Fact]
    public void StopContributing_works_the_same_for_a_repeating_goal_like_a_loan()
    {
        // The goal's own Until is far beyond any single occurrence — the
        // successor's active span must still reach all the way there, so the
        // jar keeps releasing on every remaining occurrence along the way.
        var loanPaidOff = new DateOnly(2030, 1, 1);
        var loan = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 5,
            Source = "Car loan",
            Amount = -350m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                Start = new DateOnly(2025, 1, 1),
                Until = loanPaidOff,
            }),
        });
        var predecessor = Plan(-350m, new DateOnly(2025, 1, 1), loanPaidOff, loan);
        var cutDate = new DateOnly(2026, 1, 1); // a big manual earmark got ahead of pace

        var result = RestructureFactory.StopContributing(predecessor, loan, cutDate);

        result.Successor.Amount.ShouldBe(0m);
        result.Successor.DatePattern.ActiveStart.ShouldBe(cutDate);
        result.Successor.DatePattern.GetOccurrences().ShouldBe(new[] { loanPaidOff });
    }

    [Fact]
    public void Rejects_a_successor_schedule_extending_past_the_goals_own_end()
    {
        // Composes with EarMarkPattern.Create's own 3.11.2.a2 check — no
        // duplicate validation needed in the factory itself.
        var goal = Goal(-5_000m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var predecessor = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1), goal);
        var cutDate = new DateOnly(2025, 7, 1);

        Should.Throw<ArgumentException>(() => RestructureFactory.Restructure(new RestructureRequest
        {
            Predecessor = predecessor,
            Goal = goal,
            CutDate = cutDate,
            SuccessorAmount = -150m,
            SuccessorSchedule = MonthlyFrom(cutDate, new DateOnly(2028, 1, 1)), // past the goal's own Until
        }));
    }

    // planning/24's own Item-G gap, fixed 2026-08-16: FindCurrentPlan is what
    // lets DeterminePlanShapeCandidatesIfApplicable tell a genuine sequential
    // chain apart from F27's concurrent-funder shape.
    [Fact]
    public void FindCurrentPlan_picks_the_plan_with_the_latest_start_among_a_sequential_chain()
    {
        var goal = Goal(-5_000m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var earlier = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30), goal);
        var current = Plan(-150m, new DateOnly(2025, 7, 1), new DateOnly(2027, 1, 1), goal);

        RestructureFactory.FindCurrentPlan([earlier, current]).ShouldBe(current);
    }

    [Fact]
    public void FindCurrentPlan_returns_null_when_two_plans_own_active_spans_overlap()
    {
        // F27's own concurrent-funder shape (e.g. two household partners) —
        // both plans genuinely active at once, so there's no single
        // "current" one to pick.
        var goal = Goal(-5_000m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var partnerOne = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1), goal);
        var partnerTwo = Plan(-50m, new DateOnly(2025, 1, 2), new DateOnly(2027, 1, 1), goal);

        RestructureFactory.FindCurrentPlan([partnerOne, partnerTwo]).ShouldBeNull();
    }

    [Fact]
    public void FindCurrentPlan_returns_null_for_an_empty_list()
    {
        RestructureFactory.FindCurrentPlan([]).ShouldBeNull();
    }

    [Fact]
    public void FindCurrentPlan_returns_the_only_plan_when_theres_just_one()
    {
        var goal = Goal(-5_000m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var onlyPlan = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1), goal);

        RestructureFactory.FindCurrentPlan([onlyPlan]).ShouldBe(onlyPlan);
    }
}
