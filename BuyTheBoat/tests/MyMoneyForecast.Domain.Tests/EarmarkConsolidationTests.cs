using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

public class EarmarkConsolidationTests
{
    private static readonly DateOnly GoalStart = new(2025, 1, 1);
    private static readonly DateOnly GoalEnd = new(2025, 4, 1);

    // $300/month, four occurrences (Jan/Feb/Mar/Apr 1st) inside the window
    // above — $1,200 total consumption, the number every test's own
    // arithmetic is checked against.
    private static FinancialPattern MonthlyGoal(int id = 1) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = id,
            Source = $"goal{id}",
            Amount = -300m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = GoalStart,
                Until = GoalEnd,
            }),
        });

    private static FinancialPattern MonthlyIncome(int dayOfMonth, DateOnly start, DateOnly until, int id = 100) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = id,
            Source = $"income{id}",
            Amount = 3000m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [dayOfMonth],
                DtStart = start,
                Until = until,
            }),
        });

    private static EarMarkPattern SurvivingPlan(
        FinancialPattern goal, DateOnly start, DateOnly until, decimal startingAllocation = 0m, DateOnly? activeFrom = null) =>
        EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [start.Day],
                    DtStart = start,
                    Until = until,
                    ActiveFrom = activeFrom,
                }),
                Amount = -100m,
                StartingAllocation = startingAllocation,
            },
            goal);

    [Fact]
    public void Single_income_paces_the_consolidated_plan_and_covers_the_full_window()
    {
        var goal = MonthlyGoal();
        var income = MonthlyIncome(25, new DateOnly(2024, 6, 25), new DateOnly(2026, 1, 1));
        var plan = SurvivingPlan(goal, GoalStart, GoalEnd);

        var result = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = [plan],
            ManualEarmarksForThisGoal = [],
            AllPatterns = [goal, income],
        });

        // 3 paydays (Jan/Feb/Mar 25th — Apr 25th falls after GoalEnd) share
        // the full $1,200: $400 each.
        result.Start.ShouldBe(GoalStart);
        result.End.ShouldBe(GoalEnd);
        result.ConsolidatedPlan.Amount.ShouldBe(-400m);
        result.ConsolidatedPlan.DatePattern.ByMonthDay.ShouldBe([25]);
        result.ConsolidatedPlan.DatePattern.ActiveStart.ShouldBe(GoalStart);
        result.ConsolidatedPlan.DatePattern.Until.ShouldBe(GoalEnd);
    }

    [Fact]
    public void Start_is_the_earliest_surviving_plans_own_ActiveStart_not_its_literal_Start()
    {
        var goal = MonthlyGoal();
        // A lead-in: the plan's own Start is Feb 1, but it was already active
        // from Jan 1 (ActiveFrom) — the earlier date is what should count.
        var plan = SurvivingPlan(goal, new DateOnly(2025, 2, 1), GoalEnd, activeFrom: GoalStart);

        var result = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = [plan],
            ManualEarmarksForThisGoal = [],
            AllPatterns = [goal],
        });

        result.Start.ShouldBe(GoalStart);
    }

    [Fact]
    public void Every_surviving_plans_own_StartingAllocation_reduces_the_total_needed()
    {
        var goal = MonthlyGoal();
        var planA = SurvivingPlan(goal, GoalStart, GoalEnd, startingAllocation: 200m);
        var planB = SurvivingPlan(goal, new DateOnly(2025, 2, 1), GoalEnd, startingAllocation: 100m);

        var result = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = [planA, planB],
            ManualEarmarksForThisGoal = [],
            AllPatterns = [goal], // no income -> spreads on the goal's own monthly cadence
        });

        // $1,200 needed, minus $300 already banked ($200 + $100), spread
        // across the goal's own 4 monthly occurrences: $225 each.
        result.ConsolidatedPlan.Amount.ShouldBe(-225m);
        // And that $300 doesn't just shrink the new plan's own Amount — it
        // has to carry forward as the new plan's own StartingAllocation too,
        // or GoalShortfall stops counting it anywhere the moment planA/planB's
        // rows are gone (found 2026-08-14 via a save-then-rebuild-the-forecast
        // test in FinancePatternSaveConfirmationTests, fixed here).
        result.ConsolidatedPlan.StartingAllocation.ShouldBe(300m);
    }

    [Fact]
    public void A_manual_earmark_dated_inside_the_window_reduces_the_total_the_same_way_StartingAllocation_does()
    {
        var goal = MonthlyGoal();
        var plan = SurvivingPlan(goal, GoalStart, GoalEnd);
        var earmark = ManualEarmark.Create(
            new ManualEarmarkOptions { FinanceId = goal.FinanceId, Date = new DateOnly(2025, 2, 10), Amount = 150m },
            plan);

        var result = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = [plan],
            ManualEarmarksForThisGoal = [earmark],
            AllPatterns = [goal],
        });

        // $1,200 - $150 already contributed by hand, spread across 4 monthly
        // occurrences: $262.50 each.
        result.ConsolidatedPlan.Amount.ShouldBe(-262.5m);
    }

    // Mechanism-C follow-on ("the glut case",
    // 2026-08-15): a surviving plan's real, already-accumulated glut counts
    // toward "already banked" the same way StartingAllocation and a manual
    // earmark already do — both in sizing the new rate AND carried forward
    // onto the new plan's own StartingAllocation, or it would be discounted
    // once here and then never counted again anywhere the instant the old
    // plan's row is gone. See TransactionLogBookFactoryTests'
    // An_existing_glut_survives_consolidation_spent_down_evenly_instead_of_erased
    // for the real save-then-rebuild proof this unit-level number is backed by.
    [Fact]
    public void A_glutted_current_jar_reduces_the_total_the_same_way_StartingAllocation_does()
    {
        var goal = MonthlyGoal();
        var plan = SurvivingPlan(goal, GoalStart, GoalEnd);
        var currentJar = new FundJar { FinanceId = goal.FinanceId, CurrentAmount = null, ExpectedAmount = 200m, MilestoneAmount = 50m }; // GlutSurplus = 150

        var result = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = [plan],
            ManualEarmarksForThisGoal = [],
            AllPatterns = [goal],
            CurrentJar = currentJar,
        });

        // $1,200 - $150 glut, spread across 4 monthly occurrences: $262.50
        // each — identical arithmetic to the manual-earmark test above,
        // proving the glut term is wired into the same place, not a
        // parallel, easy-to-miss path.
        result.ConsolidatedPlan.Amount.ShouldBe(-262.5m);
        result.ConsolidatedPlan.StartingAllocation.ShouldBe(150m);
    }

    [Fact]
    public void A_jar_with_no_glut_contributes_nothing_extra_beyond_StartingAllocation_and_manual_earmarks()
    {
        var goal = MonthlyGoal();
        var plan = SurvivingPlan(goal, GoalStart, GoalEnd, startingAllocation: 100m);
        var currentJar = new FundJar { FinanceId = goal.FinanceId, CurrentAmount = null, ExpectedAmount = 30m, MilestoneAmount = 50m }; // behind, not glutted

        var result = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = [plan],
            ManualEarmarksForThisGoal = [],
            AllPatterns = [goal],
            CurrentJar = currentJar,
        });

        // $1,200 - $100 StartingAllocation only — the behind-pace jar
        // contributes $0 extra, not a negative discount.
        result.ConsolidatedPlan.Amount.ShouldBe(-275m);
        result.ConsolidatedPlan.StartingAllocation.ShouldBe(100m);
    }

    [Fact]
    public void A_null_current_jar_behaves_exactly_like_no_glut_to_protect()
    {
        var goal = MonthlyGoal();
        var plan = SurvivingPlan(goal, GoalStart, GoalEnd, startingAllocation: 100m);

        var result = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = [plan],
            ManualEarmarksForThisGoal = [],
            AllPatterns = [goal],
            CurrentJar = null, // every pre-2026-08-15 caller — backward compatible
        });

        result.ConsolidatedPlan.Amount.ShouldBe(-275m);
        result.ConsolidatedPlan.StartingAllocation.ShouldBe(100m);
    }

    [Fact]
    public void No_single_clear_income_spreads_across_the_goals_own_occurrences_instead()
    {
        var goal = MonthlyGoal();
        var plan = SurvivingPlan(goal, GoalStart, GoalEnd);

        var result = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = [plan],
            ManualEarmarksForThisGoal = [],
            AllPatterns = [goal], // no income pattern at all
        });

        result.ConsolidatedPlan.DatePattern.Frequency.ShouldBe(RecurrenceFrequency.Monthly);
        result.ConsolidatedPlan.Amount.ShouldBe(-300m); // $1,200 / 4 occurrences
    }

    [Fact]
    public void More_than_one_income_stream_also_falls_back_to_spreading_on_the_goals_own_cadence()
    {
        var goal = MonthlyGoal();
        var plan = SurvivingPlan(goal, GoalStart, GoalEnd);
        var incomeA = MonthlyIncome(10, new DateOnly(2024, 1, 10), new DateOnly(2026, 1, 1), id: 100);
        var incomeB = MonthlyIncome(25, new DateOnly(2024, 1, 25), new DateOnly(2026, 1, 1), id: 101);

        var result = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = [plan],
            ManualEarmarksForThisGoal = [],
            AllPatterns = [goal, incomeA, incomeB],
        });

        result.ConsolidatedPlan.Amount.ShouldBe(-300m); // same as the no-income case, not paced to either income
    }

    [Fact]
    public void Already_fully_banked_clamps_to_a_zero_contribution_rather_than_going_negative()
    {
        var goal = MonthlyGoal();
        var plan = SurvivingPlan(goal, GoalStart, GoalEnd, startingAllocation: 5_000m); // far more than the $1,200 needed

        var result = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = [plan],
            ManualEarmarksForThisGoal = [],
            AllPatterns = [goal],
        });

        result.ConsolidatedPlan.Amount.ShouldBe(0m);
    }

    [Fact]
    public void Throws_when_there_are_no_surviving_plans_to_consolidate()
    {
        var goal = MonthlyGoal();

        Should.Throw<ArgumentException>(() => EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = [],
            ManualEarmarksForThisGoal = [],
            AllPatterns = [goal],
        }));
    }

    // ---- The two consolidate-strategy choices ----

    [Fact]
    public void KeepCurrentPace_sizes_to_the_plans_current_scheduled_rate_not_the_goal()
    {
        // Two $100/month plans — $200/month combined — under a $300/month goal.
        // KeepCurrentPace holds the $200 rate (under-funding the goal), rather than
        // sizing up to the $300 the goal itself needs.
        var goal = MonthlyGoal();
        var plan1 = SurvivingPlan(goal, GoalStart, GoalEnd);
        var plan2 = SurvivingPlan(goal, GoalStart, GoalEnd);

        var result = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = [plan1, plan2],
            ManualEarmarksForThisGoal = [],
            AllPatterns = [goal], // no income → spread evenly
            Sizing = ConsolidationSizing.KeepCurrentPace,
        });

        result.ConsolidatedPlan.Amount.ShouldBe(-200m); // the combined $200/month, not the goal's $300
    }

    [Fact]
    public void MeetGoal_sizes_up_to_fully_fund_the_goal()
    {
        var goal = MonthlyGoal();
        var plan1 = SurvivingPlan(goal, GoalStart, GoalEnd);
        var plan2 = SurvivingPlan(goal, GoalStart, GoalEnd);

        var result = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = [plan1, plan2],
            ManualEarmarksForThisGoal = [],
            AllPatterns = [goal],
            Sizing = ConsolidationSizing.MeetGoal, // the default
        });

        result.ConsolidatedPlan.Amount.ShouldBe(-300m); // sized up to fully fund the $300/month goal
    }

    [Fact]
    public void Evenly_spreads_on_the_goals_occurrences_even_when_an_income_exists()
    {
        // An income exists, so AcrossPaydays would pace to its 25th-of-month
        // schedule. Evenly ignores it and uses the goal's own 1st-of-month dates.
        var goal = MonthlyGoal();
        var income = MonthlyIncome(25, new DateOnly(2024, 6, 25), new DateOnly(2026, 1, 1));
        var plan = SurvivingPlan(goal, GoalStart, GoalEnd);

        var result = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = [plan],
            ManualEarmarksForThisGoal = [],
            AllPatterns = [goal, income],
            Spread = ConsolidationSpread.Evenly,
        });

        result.ConsolidatedPlan.DatePattern.GetOccurrences().ShouldAllBe(date => date.Day == 1); // goal's 1st, not income's 25th
    }

    [Fact]
    public void KeepCurrentPace_counts_skipped_dates_so_a_skip_doesnt_lower_the_rate()
    {
        // A plan skipping its Feb occurrence still counts at full monthly cadence,
        // so keeping the current pace uses the un-skipped $100/month, not $75.
        var goal = MonthlyGoal();
        var planWithSkip = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = GoalStart,
                    Until = GoalEnd,
                    ExcludedDates = [new DateOnly(2025, 2, 1)],
                }),
                Amount = -100m,
            },
            goal);

        var result = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = [planWithSkip],
            ManualEarmarksForThisGoal = [],
            AllPatterns = [goal],
            Sizing = ConsolidationSizing.KeepCurrentPace,
        });

        result.ConsolidatedPlan.Amount.ShouldBe(-100m); // 4 full occurrences × $100 / 4, not 3 skip-reduced
    }

    // The affordability cap on the one consolidated plan's per-cycle contribution — silent (no result flag),
    // like Scale, since a fold is an implicit in-place correction the user doesn't weigh.

    [Fact]
    public void The_consolidated_plans_per_cycle_contribution_is_held_under_the_affordability_ceiling()
    {
        var goal = MonthlyGoal();
        var plan = SurvivingPlan(goal, GoalStart, GoalEnd);

        // Fully funding wants $300/occurrence ($1,200 / 4), but only $200 can be spared → held to $200.
        var result = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = [plan],
            ManualEarmarksForThisGoal = [],
            AllPatterns = [goal],
        }, affordabilityCeiling: 200m);

        result.ConsolidatedPlan.Amount.ShouldBe(-200m); // knowingly underfunds rather than reserving what isn't there
    }

    [Fact]
    public void The_consolidated_plan_is_uncapped_when_the_ceiling_covers_the_full_contribution()
    {
        var goal = MonthlyGoal();
        var plan = SurvivingPlan(goal, GoalStart, GoalEnd);

        // Ceiling above the $300/occurrence the goal needs → the fold meets it in full.
        var result = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = [plan],
            ManualEarmarksForThisGoal = [],
            AllPatterns = [goal],
        }, affordabilityCeiling: 400m);

        result.ConsolidatedPlan.Amount.ShouldBe(-300m);
    }

    [Fact]
    public void The_already_banked_money_still_carries_forward_when_the_ongoing_rate_is_capped()
    {
        var goal = MonthlyGoal();
        var plan = SurvivingPlan(goal, GoalStart, GoalEnd, startingAllocation: 150m);

        // The cap only touches the ongoing ask — the $150 already banked still rides forward untouched.
        var result = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = [plan],
            ManualEarmarksForThisGoal = [],
            AllPatterns = [goal],
        }, affordabilityCeiling: 100m);

        result.ConsolidatedPlan.Amount.ShouldBe(-100m);              // ongoing rate held to the ceiling
        result.ConsolidatedPlan.StartingAllocation.ShouldBe(150m);   // banked money preserved regardless
    }
}
