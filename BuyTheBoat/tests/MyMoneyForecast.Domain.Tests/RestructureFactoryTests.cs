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
                DtStart = start,
                Until = until,
            }),
        });

    private static EarMarkPattern Plan(decimal amount, DateOnly start, DateOnly until, FinancialPattern goal, decimal startingAllocation = 0m) =>
        EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = amount,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = start,
                    Until = until,
                }),
                StartingAllocation = startingAllocation,
            },
            goal);

    private static RecurrenceRuleOptions MonthlyFrom(DateOnly start, DateOnly until) => new()
    {
        Frequency = RecurrenceFrequency.Monthly,
        ByMonthDay = [1],
        DtStart = start,
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
        result.Successor.DatePattern.ActiveStart.ShouldBe(cutDate);
        result.Successor.Amount.ShouldBe(-150m);
    }

    [Fact]
    public void The_successor_shares_the_predecessors_finance_id_not_a_new_one()
    {
        // Unlike BreakOffFactory (a new finance_id for a changed bill/goal),
        // this never touches the goal — the whole point is that one
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
        // No jar hand-off is needed at all — the same finance_id means
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

    // "stop contributing, keep the jar alive" is
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
                DtStart = new DateOnly(2025, 1, 1),
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

    // FindCurrentPlan is what
    // lets DeterminePlanShapeCandidatesIfApplicable tell a genuine sequential
    // chain apart from the concurrent earmark pattern shape.
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
        // The concurrent earmark pattern shape (e.g. two household partners) —
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

    // SpansOverlap itself — extracted from FindCurrentPlan's own pairwise
    // check above, so FinancePatternSaveConfirmation.RunForPlan
    // can rule out a concurrent plan before ever treating it as a chain
    // predecessor/successor, found necessary while grounding the UI-wiring
    // work: without it, a concurrent plan with a later Start than the one
    // being edited satisfied the old hasSuccessor check too, so extending
    // Until into its overlapping span would have silently truncated it.
    [Fact]
    public void SpansOverlap_is_true_for_two_plans_whose_spans_partially_overlap()
    {
        var goal = Goal(-5_000m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var main = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), goal);
        var concurrent = Plan(-50m, new DateOnly(2025, 3, 1), new DateOnly(2025, 8, 31), goal);

        RestructureFactory.SpansOverlap(main, concurrent).ShouldBeTrue();
        RestructureFactory.SpansOverlap(concurrent, main).ShouldBeTrue(); // symmetric
    }

    [Fact]
    public void SpansOverlap_is_false_for_two_plans_that_are_merely_adjacent()
    {
        var goal = Goal(-5_000m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var earlier = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30), goal);
        var later = Plan(-100m, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31), goal);

        RestructureFactory.SpansOverlap(earlier, later).ShouldBeFalse();
    }

    [Fact]
    public void SpansOverlap_is_false_for_two_plans_with_a_real_gap_between_them()
    {
        var goal = Goal(-5_000m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var earlier = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), goal);
        var later = Plan(-100m, new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 31), goal);

        RestructureFactory.SpansOverlap(earlier, later).ShouldBeFalse();
    }

    [Fact]
    public void SpansOverlap_is_true_for_two_plans_covering_the_exact_same_span()
    {
        var goal = Goal(-5_000m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var partnerOne = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), goal);
        var partnerTwo = Plan(-50m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), goal);

        RestructureFactory.SpansOverlap(partnerOne, partnerTwo).ShouldBeTrue();
    }

    // The "stay linked in the chain, or let it break" question,
    // resolved for a same-finance_id EarMarkPattern chain. Goal spans the
    // whole of 2025 so there's room for several segments.
    private static readonly FinancialPattern ChainGoal =
        Goal(-12_000m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));

    [Fact]
    public void ExtendUntil_shrinks_the_neighbor_it_reaches_into_without_absorbing_it()
    {
        var current = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), ChainGoal);
        var neighbor = Plan(-80m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30), ChainGoal);

        var result = RestructureFactory.ExtendUntil(current, [neighbor], ChainGoal, new DateOnly(2025, 4, 15));

        result.Current.DatePattern.Until.ShouldBe(new DateOnly(2025, 4, 15));
        result.Absorbed.ShouldBeEmpty();
        result.AdjustedNeighbor.ShouldNotBeNull();
        result.AdjustedNeighbor!.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 4, 16));
        result.AdjustedNeighbor.DatePattern.Until.ShouldBe(new DateOnly(2025, 6, 30)); // unchanged
        result.AdjustedNeighbor.Amount.ShouldBe(-80m); // unchanged
    }

    [Fact]
    public void ExtendUntil_expands_the_neighbor_backward_when_shrinking_away_from_it()
    {
        var current = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), ChainGoal);
        var neighbor = Plan(-80m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30), ChainGoal);

        var result = RestructureFactory.ExtendUntil(current, [neighbor], ChainGoal, new DateOnly(2025, 3, 15));

        result.Current.DatePattern.Until.ShouldBe(new DateOnly(2025, 3, 15));
        result.Absorbed.ShouldBeEmpty();
        result.AdjustedNeighbor!.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 3, 16)); // closes what would be a gap
        result.AdjustedNeighbor.DatePattern.Until.ShouldBe(new DateOnly(2025, 6, 30));
    }

    [Fact]
    public void ExtendUntil_absorbs_one_neighbor_entirely_and_adjusts_the_next_one()
    {
        var current = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), ChainGoal);
        var absorbedNeighbor = Plan(-80m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30), ChainGoal, startingAllocation: 50m);
        var farNeighbor = Plan(-60m, new DateOnly(2025, 7, 1), new DateOnly(2025, 9, 30), ChainGoal);

        var result = RestructureFactory.ExtendUntil(current, [absorbedNeighbor, farNeighbor], ChainGoal, new DateOnly(2025, 7, 10));

        result.Current.DatePattern.Until.ShouldBe(new DateOnly(2025, 7, 10));
        result.Current.StartingAllocation.ShouldBe(50m); // carried forward from the absorbed neighbor
        result.Absorbed.ShouldHaveSingleItem();
        result.Absorbed[0].ShouldBe(absorbedNeighbor);
        result.AdjustedNeighbor!.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 7, 11));
        result.AdjustedNeighbor.DatePattern.Until.ShouldBe(new DateOnly(2025, 9, 30)); // farNeighbor, untouched otherwise
    }

    [Fact]
    public void ExtendUntil_absorbs_through_several_neighbors_in_one_go()
    {
        var current = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), ChainGoal);
        var neighborA = Plan(-80m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30), ChainGoal, startingAllocation: 50m);
        var neighborB = Plan(-70m, new DateOnly(2025, 7, 1), new DateOnly(2025, 9, 30), ChainGoal, startingAllocation: 30m);
        var neighborC = Plan(-60m, new DateOnly(2025, 10, 1), new DateOnly(2025, 12, 31), ChainGoal);

        var result = RestructureFactory.ExtendUntil(current, [neighborA, neighborB, neighborC], ChainGoal, new DateOnly(2025, 10, 15));

        result.Absorbed.Count.ShouldBe(2);
        result.Absorbed.ShouldBe([neighborA, neighborB]); // earliest first
        result.Current.StartingAllocation.ShouldBe(80m); // 50 + 30
        result.AdjustedNeighbor!.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 10, 16));
    }

    [Fact]
    public void ExtendUntil_absorbing_the_last_remaining_neighbor_leaves_nothing_to_adjust()
    {
        var current = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), ChainGoal);
        var onlyNeighbor = Plan(-80m, new DateOnly(2025, 4, 1), ChainGoal.DatePattern.Until, ChainGoal);

        var result = RestructureFactory.ExtendUntil(current, [onlyNeighbor], ChainGoal, ChainGoal.DatePattern.Until);

        result.Absorbed.ShouldHaveSingleItem();
        result.AdjustedNeighbor.ShouldBeNull();
        result.Current.DatePattern.Until.ShouldBe(ChainGoal.DatePattern.Until);
    }

    [Fact]
    public void ExtendUntil_preserves_the_adjusted_neighbors_own_excluded_dates()
    {
        var current = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), ChainGoal);
        var skippedDate = new DateOnly(2025, 5, 1);
        var neighbor = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = ChainGoal.FinanceId,
                Amount = -80m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 4, 1),
                    Until = new DateOnly(2025, 6, 30),
                    ExcludedDates = [skippedDate],
                }),
            },
            ChainGoal);

        var result = RestructureFactory.ExtendUntil(current, [neighbor], ChainGoal, new DateOnly(2025, 4, 15));

        result.AdjustedNeighbor!.DatePattern.ExcludedDates.ShouldBe([skippedDate]);
    }

    [Fact]
    public void ExtendUntil_rejects_a_new_until_before_the_plans_own_start()
    {
        var current = Plan(-100m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30), ChainGoal);

        Should.Throw<ArgumentException>(() =>
            RestructureFactory.ExtendUntil(current, [], ChainGoal, new DateOnly(2025, 3, 1)));
    }

    [Fact]
    public void ExtendStart_shrinks_the_predecessor_it_reaches_into_without_absorbing_it()
    {
        var predecessor = Plan(-80m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), ChainGoal);
        var current = Plan(-100m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30), ChainGoal);

        var result = RestructureFactory.ExtendStart(current, [predecessor], ChainGoal, new DateOnly(2025, 3, 15));

        result.Current.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 3, 15));
        result.Absorbed.ShouldBeEmpty();
        result.AdjustedNeighbor!.DatePattern.Until.ShouldBe(new DateOnly(2025, 3, 14));
        result.AdjustedNeighbor.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 1, 1)); // unchanged
    }

    [Fact]
    public void ExtendStart_absorbs_the_predecessor_entirely_and_carries_its_starting_allocation()
    {
        var predecessor = Plan(-80m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), ChainGoal, startingAllocation: 20m);
        var current = Plan(-100m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30), ChainGoal);

        var result = RestructureFactory.ExtendStart(current, [predecessor], ChainGoal, new DateOnly(2025, 1, 1));

        result.Absorbed.ShouldHaveSingleItem();
        result.Absorbed[0].ShouldBe(predecessor);
        result.AdjustedNeighbor.ShouldBeNull();
        result.Current.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 1, 1));
        result.Current.StartingAllocation.ShouldBe(20m);
    }

    // Deliberately matches how the real caller (FinancePatternSaveConfirmation)
    // actually invokes this — current's own Start is ALREADY newStart (every
    // real call passes current.DatePattern.ActiveStart as newStart directly), not
    // some other value like the test above uses. With
    // current.Start equal to newStart, a predecessor landing on that
    // EXACT same date must still be absorbed: a strict `plan.Start < current.Start`
    // filter would skip it even though the loop's own `newStart <= plan.Start`
    // check says to absorb it.
    [Fact]
    public void ExtendStart_absorbs_a_predecessor_landing_exactly_on_the_new_start_when_current_already_reflects_it()
    {
        var predecessor = Plan(-80m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), ChainGoal);
        var editedCurrent = Plan(-100m, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30), ChainGoal); // own Start already moved to Jan 1

        var result = RestructureFactory.ExtendStart(editedCurrent, [predecessor], ChainGoal, new DateOnly(2025, 1, 1));

        result.Absorbed.ShouldHaveSingleItem();
        result.Absorbed[0].ShouldBe(predecessor);
        result.Current.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 1, 1));
    }

    [Fact]
    public void ExtendStart_rejects_a_new_start_after_the_plans_own_until()
    {
        var current = Plan(-100m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30), ChainGoal);

        Should.Throw<ArgumentException>(() =>
            RestructureFactory.ExtendStart(current, [], ChainGoal, new DateOnly(2025, 7, 1)));
    }

    // The "cascade forward" default for an Amount/shape change
    // on an EarMarkPattern chain.
    [Fact]
    public void CascadeForward_applies_the_new_amount_and_shape_to_a_later_plan_keeping_its_own_dates()
    {
        var laterPlan = Plan(-80m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30), ChainGoal, startingAllocation: 25m);
        var newShape = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Weekly,
            Interval = 2,
            DtStart = new DateOnly(2025, 1, 3), // irrelevant to the result — only shape is read
            Until = new DateOnly(2025, 3, 31),
        });

        var result = RestructureFactory.CascadeForward(newShape, -120m, [laterPlan], ChainGoal);

        result.ShouldHaveSingleItem();
        var updated = result[0];
        updated.Amount.ShouldBe(-120m);
        updated.DatePattern.Frequency.ShouldBe(RecurrenceFrequency.Weekly);
        updated.DatePattern.Interval.ShouldBe(2);
        updated.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 4, 1)); // laterPlan's own, untouched
        updated.DatePattern.Until.ShouldBe(new DateOnly(2025, 6, 30)); // laterPlan's own, untouched
        updated.StartingAllocation.ShouldBe(25m); // laterPlan's own, untouched
    }

    [Fact]
    public void CascadeForward_applies_to_every_later_plan_in_order()
    {
        var planA = Plan(-80m, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30), ChainGoal);
        var planB = Plan(-80m, new DateOnly(2025, 7, 1), new DateOnly(2025, 9, 30), ChainGoal);
        var newShape = MonthlyFrom(new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        var newShapeRule = RecurrenceRule.Create(newShape);

        var result = RestructureFactory.CascadeForward(newShapeRule, -150m, [planA, planB], ChainGoal);

        result.Count.ShouldBe(2);
        result.ShouldAllBe(plan => plan.Amount == -150m);
        result[0].DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 4, 1));
        result[1].DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 7, 1));
    }

    [Fact]
    public void CascadeForward_preserves_a_later_plans_own_excluded_dates()
    {
        var skippedDate = new DateOnly(2025, 5, 1);
        var laterPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = ChainGoal.FinanceId,
                Amount = -80m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 4, 1),
                    Until = new DateOnly(2025, 6, 30),
                    ExcludedDates = [skippedDate],
                }),
            },
            ChainGoal);
        var newShape = RecurrenceRule.Create(MonthlyFrom(new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31)));

        var result = RestructureFactory.CascadeForward(newShape, -100m, [laterPlan], ChainGoal);

        result[0].DatePattern.ExcludedDates.ShouldBe([skippedDate]);
    }

    [Fact]
    public void CascadeForward_returns_empty_for_no_later_plans()
    {
        var newShape = RecurrenceRule.Create(MonthlyFrom(new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31)));

        RestructureFactory.CascadeForward(newShape, -100m, [], ChainGoal).ShouldBeEmpty();
    }
}
