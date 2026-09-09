using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

public class BreakOffFactoryTests
{
    private static FinancialPattern MonthlyBill(decimal amount, int dayOfMonth, DateOnly start, DateOnly until, int id = 1, bool autoRenew = false, string? source = null, int priority = 0, string? description = "Rent") =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = id,
            Source = source ?? $"bill{id}",
            Description = description,
            Amount = amount,
            Priority = priority,
            AutoRenew = autoRenew,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [dayOfMonth],
                DtStart = start,
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
                DtStart = start,
                Until = until,
            }),
        });

    private static RecurrenceRuleOptions MonthlyFrom(DateOnly start, int dayOfMonth, DateOnly until) => new()
    {
        Frequency = RecurrenceFrequency.Monthly,
        ByMonthDay = [dayOfMonth],
        DtStart = start,
        Until = until,
    };

    [Fact]
    public void Ends_the_predecessor_the_day_before_the_cut_and_starts_the_successor_on_it()
    {
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var cutDate = new DateOnly(2025, 7, 1);

        var result = BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -1_800m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2026, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });

        result.Predecessor.DatePattern.Until.ShouldBe(new DateOnly(2025, 6, 30));
        result.Successor.DatePattern.ActiveStart.ShouldBe(cutDate);
        result.Successor.Amount.ShouldBe(-1_800m);
        result.Successor.FinanceId.ShouldBe(2);
    }

    [Fact]
    public void The_successor_carries_the_predecessors_source_and_description()
    {
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var cutDate = new DateOnly(2025, 7, 1);

        var result = BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -1_800m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2026, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });

        result.Successor.Source.ShouldBe(rent.Source);
        result.Successor.Description.ShouldBe(rent.Description);
    }

    [Fact]
    public void The_successor_carries_the_predecessors_auto_renew_marker()
    {
        // planning/13 (B12): a break-off changes amount/schedule, not whether
        // the pattern "keeps going" — that marker travels with the successor
        // like every other identity field.
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1), autoRenew: true);
        var cutDate = new DateOnly(2025, 7, 1);

        var result = BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -1_800m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2026, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });

        result.Successor.AutoRenew.ShouldBeTrue();
    }

    [Fact]
    public void With_no_income_the_successors_plan_front_loads_like_any_new_outflow()
    {
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var cutDate = new DateOnly(2025, 7, 1);

        var result = BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -1_800m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2026, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });

        result.SuccessorPlan.ShouldNotBeNull();
        result.SuccessorPlan!.Amount.ShouldBe(-1_800m); // full amount, front-loaded shape
        result.SuccessorStartingEarmark.ShouldBeNull(); // front-loaded shape never needs one
    }

    [Fact]
    public void With_a_single_income_stream_the_successors_plan_paces_against_it()
    {
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var income = MonthlyIncome(3_000m, 25, new DateOnly(2024, 1, 25), new DateOnly(2027, 1, 1));
        var cutDate = new DateOnly(2025, 7, 1);

        var result = BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -1_800m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2026, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent, income],
        });

        result.SuccessorPlan.ShouldNotBeNull();
        result.SuccessorPlan!.DatePattern.Frequency.ShouldBe(RecurrenceFrequency.Monthly);
        result.SuccessorPlan.DatePattern.ByMonthDay.ShouldBe(income.DatePattern.ByMonthDay); // paced on the income's own day-of-month
        result.SuccessorPlan.Amount.ShouldBeLessThan(0m); // negative = into the jar
    }

    [Fact]
    public void The_carried_over_jar_balance_becomes_the_successors_starting_allocation()
    {
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var cutDate = new DateOnly(2025, 7, 1);

        var result = BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -1_800m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2026, 1, 1)),
            CarriedOverJarBalance = 340m,
            AllPatterns = [rent],
        });

        result.SuccessorPlan!.StartingAllocation.ShouldBe(340m);
    }

    [Fact]
    public void A_predecessor_with_no_plan_still_gives_the_successor_a_fresh_one()
    {
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var cutDate = new DateOnly(2025, 7, 1);

        var result = BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -1_800m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2026, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });

        result.PredecessorPlan.ShouldBeNull();
        result.SuccessorPlan.ShouldNotBeNull();
    }

    [Fact]
    public void The_predecessors_plan_is_truncated_to_match_the_cut()
    {
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = rent.FinanceId,
                Amount = -1_600m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2026, 1, 1),
                }),
            },
            rent);
        var cutDate = new DateOnly(2025, 7, 1);

        var result = BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = rent,
            PredecessorPlan = plan,
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -1_800m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2026, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });

        result.PredecessorPlan.ShouldNotBeNull();
        result.PredecessorPlan!.DatePattern.Until.ShouldBe(new DateOnly(2025, 6, 30));
    }

    [Fact]
    public void Breaking_off_income_skips_the_jar_machinery_entirely()
    {
        var oldJob = MonthlyIncome(4_000m, 15, new DateOnly(2024, 1, 15), new DateOnly(2027, 1, 1));
        var cutDate = new DateOnly(2025, 7, 15);

        var result = BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = oldJob,
            PredecessorPlan = null,
            CutDate = cutDate,
            SuccessorFinanceId = 101,
            SuccessorAmount = 4_500m,
            SuccessorSchedule = MonthlyFrom(cutDate, 15, new DateOnly(2027, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [oldJob],
        });

        result.Predecessor.DatePattern.Until.ShouldBe(cutDate.AddDays(-1));
        result.Successor.Amount.ShouldBe(4_500m);
        result.PredecessorPlan.ShouldBeNull();
        result.SuccessorPlan.ShouldBeNull();
        result.SuccessorStartingEarmark.ShouldBeNull();
    }

    [Fact]
    public void The_successors_active_from_stays_null_because_it_starts_exactly_on_the_cut_date()
    {
        // The proposer only stretches ActiveFrom when Start > asOfDate; here
        // Start == CutDate == the asOfDate passed to the proposer, so no
        // stretch happens — confirms item 4-B/F25's reasoning holds in code,
        // not just in the design doc.
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var cutDate = new DateOnly(2025, 7, 1);

        var result = BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -1_800m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2026, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });

        result.Successor.DatePattern.ToOptions().ActiveFrom.ShouldBeNull();
    }

    [Fact]
    public void A_cut_date_in_the_past_works_the_same_way_as_one_in_the_future()
    {
        // F25: nothing before the as-of date is a locked ledger yet, so a past
        // cut date ("starting three paychecks ago, my rent went up") is just
        // as valid as a future one — no special-casing needed.
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2024, 1, 1), new DateOnly(2026, 1, 1));
        var pastCutDate = new DateOnly(2024, 7, 1);

        var result = BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            CutDate = pastCutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -1_800m,
            SuccessorSchedule = MonthlyFrom(pastCutDate, 1, new DateOnly(2026, 1, 1)),
            CarriedOverJarBalance = 120m,
            AllPatterns = [rent],
        });

        result.Predecessor.DatePattern.Until.ShouldBe(new DateOnly(2024, 6, 30));
        result.Successor.DatePattern.ActiveStart.ShouldBe(pastCutDate);
        result.SuccessorPlan!.StartingAllocation.ShouldBe(120m);
    }

    [Fact]
    public void A_cut_date_on_or_before_the_predecessors_start_is_rejected()
    {
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2025, 6, 1), new DateOnly(2026, 1, 1));

        Should.Throw<ArgumentException>(() => BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            CutDate = new DateOnly(2025, 6, 1),
            SuccessorFinanceId = 2,
            SuccessorAmount = -1_800m,
            SuccessorSchedule = MonthlyFrom(new DateOnly(2025, 6, 1), 1, new DateOnly(2026, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        }));
    }

    [Fact]
    public void Reusing_the_predecessors_finance_id_for_the_successor_is_rejected()
    {
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1), id: 1);
        var cutDate = new DateOnly(2025, 7, 1);

        Should.Throw<ArgumentException>(() => BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            CutDate = cutDate,
            SuccessorFinanceId = 1, // same as the predecessor — item 4-A forbids this
            SuccessorAmount = -1_800m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2026, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        }));
    }

    [Fact]
    public void A_successor_schedule_not_starting_on_the_cut_date_is_rejected()
    {
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var cutDate = new DateOnly(2025, 7, 1);

        Should.Throw<ArgumentException>(() => BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -1_800m,
            SuccessorSchedule = MonthlyFrom(new DateOnly(2025, 7, 15), 1, new DateOnly(2026, 1, 1)), // mismatched start
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        }));
    }

    // Multi-plan BreakOff — planning/25's Item F, the consolidating case
    // (more than one EarMarkPattern already shares the predecessor's own
    // finance_id — F27's relaxation of 3.11.1.a1).

    private static EarMarkPattern MonthlyPlan(FinancialPattern goal, decimal amount, DateOnly start, DateOnly until) =>
        EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = amount,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [start.Day],
                    DtStart = start,
                    Until = until,
                }),
            },
            goal);

    [Fact]
    public void Multi_plan_break_off_truncates_every_surviving_plan_to_match_the_cut()
    {
        var carLease = MonthlyBill(-420m, 1, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var planA = MonthlyPlan(carLease, -300m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var planB = MonthlyPlan(carLease, -120m, new DateOnly(2025, 1, 2), new DateOnly(2027, 1, 1));
        var cutDate = new DateOnly(2025, 7, 1);

        var result = BreakOffFactory.BreakOff(new MultiPlanBreakOffRequest
        {
            Predecessor = carLease,
            PredecessorPlans = [planA, planB],
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -500m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2027, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [carLease],
        });

        result.PredecessorPlans.Count.ShouldBe(2);
        result.PredecessorPlans.ShouldAllBe(plan => plan.DatePattern.Until == new DateOnly(2025, 6, 30));
    }

    [Fact]
    public void Multi_plan_break_off_still_produces_exactly_one_freshly_proposed_successor_plan()
    {
        // Item F's own ruling: consolidating N plans always means ONE
        // successor, the same shape as an ordinary single-plan break-off —
        // never N successors.
        var carLease = MonthlyBill(-420m, 1, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var planA = MonthlyPlan(carLease, -300m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var planB = MonthlyPlan(carLease, -120m, new DateOnly(2025, 1, 2), new DateOnly(2027, 1, 1));
        var cutDate = new DateOnly(2025, 7, 1);

        var result = BreakOffFactory.BreakOff(new MultiPlanBreakOffRequest
        {
            Predecessor = carLease,
            PredecessorPlans = [planA, planB],
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -500m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2027, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [carLease],
        });

        result.SuccessorPlan.ShouldNotBeNull();
        result.SuccessorPlan!.FinanceId.ShouldBe(2);
    }

    [Fact]
    public void Multi_plan_break_off_seeds_the_successor_from_the_one_combined_carried_over_balance()
    {
        // Not per-plan — a finance_id has exactly one jar regardless of how
        // many EarMarkPatterns feed it (F27/F34), so there is only ever one
        // CarriedOverJarBalance to read, already summed before this request
        // is built.
        var carLease = MonthlyBill(-420m, 1, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var planA = MonthlyPlan(carLease, -300m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var planB = MonthlyPlan(carLease, -120m, new DateOnly(2025, 1, 2), new DateOnly(2027, 1, 1));
        var cutDate = new DateOnly(2025, 7, 1);

        var result = BreakOffFactory.BreakOff(new MultiPlanBreakOffRequest
        {
            Predecessor = carLease,
            PredecessorPlans = [planA, planB],
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -500m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2027, 1, 1)),
            CarriedOverJarBalance = 610m,
            AllPatterns = [carLease],
        });

        result.SuccessorPlan!.StartingAllocation.ShouldBe(610m);
    }

    [Fact]
    public void Multi_plan_break_off_shares_the_same_cut_boundary_validation_as_the_single_plan_overload()
    {
        var carLease = MonthlyBill(-420m, 1, new DateOnly(2025, 6, 1), new DateOnly(2027, 1, 1));
        var plan = MonthlyPlan(carLease, -420m, new DateOnly(2025, 6, 1), new DateOnly(2027, 1, 1));

        Should.Throw<ArgumentException>(() => BreakOffFactory.BreakOff(new MultiPlanBreakOffRequest
        {
            Predecessor = carLease,
            PredecessorPlans = [plan],
            CutDate = new DateOnly(2025, 6, 1), // not after the predecessor's own start
            SuccessorFinanceId = 2,
            SuccessorAmount = -500m,
            SuccessorSchedule = MonthlyFrom(new DateOnly(2025, 6, 1), 1, new DateOnly(2027, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [carLease],
        }));
    }

    // BreakOffKeepingPlansSeparate — Item F's "keep separate through a
    // break-off." Same truncated predecessor as the consolidating overload, but
    // one successor plan per surviving plan instead of one combined fresh one.

    [Fact]
    public void Keeping_plans_separate_gives_the_successor_one_plan_per_surviving_plan()
    {
        var carLease = MonthlyBill(-420m, 1, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var planA = MonthlyPlan(carLease, -300m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var planB = MonthlyPlan(carLease, -120m, new DateOnly(2025, 1, 2), new DateOnly(2027, 1, 1));
        var cutDate = new DateOnly(2025, 7, 1);

        var result = BreakOffFactory.BreakOffKeepingPlansSeparate(new MultiPlanBreakOffRequest
        {
            Predecessor = carLease,
            PredecessorPlans = [planA, planB],
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -500m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2027, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [carLease],
        });

        result.SuccessorPlans.Count.ShouldBe(2); // one per surviving plan — not folded into one
        result.SuccessorPlans.ShouldAllBe(plan => plan.FinanceId == 2);
        result.SuccessorPlans.Select(plan => plan.Amount).ShouldBe(new[] { -300m, -120m }, ignoreOrder: true); // each keeps its own rate
        // Each continues from the successor's own start, inside its span (3.11.2.a2).
        result.SuccessorPlans.ShouldAllBe(plan => plan.DatePattern.ActiveStart >= result.Successor.DatePattern.ActiveStart);
        result.SuccessorPlans.ShouldAllBe(plan => plan.DatePattern.Until == new DateOnly(2027, 1, 1));
    }

    [Fact]
    public void Keeping_plans_separate_truncates_every_surviving_predecessor_plan_to_the_cut()
    {
        var carLease = MonthlyBill(-420m, 1, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var planA = MonthlyPlan(carLease, -300m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var planB = MonthlyPlan(carLease, -120m, new DateOnly(2025, 1, 2), new DateOnly(2027, 1, 1));
        var cutDate = new DateOnly(2025, 7, 1);

        var result = BreakOffFactory.BreakOffKeepingPlansSeparate(new MultiPlanBreakOffRequest
        {
            Predecessor = carLease,
            PredecessorPlans = [planA, planB],
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -500m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2027, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [carLease],
        });

        result.Predecessor.DatePattern.Until.ShouldBe(new DateOnly(2025, 6, 30));
        result.PredecessorPlans.Count.ShouldBe(2);
        result.PredecessorPlans.ShouldAllBe(plan => plan.DatePattern.Until == new DateOnly(2025, 6, 30));
    }

    [Fact]
    public void Keeping_plans_separate_carries_the_one_combined_balance_on_a_single_successor_plan()
    {
        // Same as the consolidating overload: a finance_id has one jar however
        // many plans feed it, so there is one balance to carry — it rides on a
        // single successor plan, not split across them.
        var carLease = MonthlyBill(-420m, 1, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var planA = MonthlyPlan(carLease, -300m, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1));
        var planB = MonthlyPlan(carLease, -120m, new DateOnly(2025, 1, 2), new DateOnly(2027, 1, 1));
        var cutDate = new DateOnly(2025, 7, 1);

        var result = BreakOffFactory.BreakOffKeepingPlansSeparate(new MultiPlanBreakOffRequest
        {
            Predecessor = carLease,
            PredecessorPlans = [planA, planB],
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -500m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2027, 1, 1)),
            CarriedOverJarBalance = 610m,
            AllPatterns = [carLease],
        });

        result.SuccessorPlans.Sum(plan => plan.StartingAllocation).ShouldBe(610m); // the whole balance, carried once
        result.SuccessorPlans.Count(plan => plan.StartingAllocation > 0m).ShouldBe(1); // on exactly one plan
    }

    // Renew — periodic renewal for "ongoing" patterns (planning/15, worked
    // through with the author 2026-07-29). Distinct from BreakOff: nothing
    // about the bill changes, only how far out it reaches.

    [Fact]
    public void Renew_keeps_the_amount_and_schedule_shape_exactly_the_same_as_the_predecessor()
    {
        var rent = MonthlyBill(-150m, 5, new DateOnly(2023, 1, 5), new DateOnly(2026, 1, 5));
        var renewalDate = new DateOnly(2026, 1, 5);

        var result = BreakOffFactory.Renew(new RenewalRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            RenewalDate = renewalDate,
            SegmentYears = 1,
            SuccessorFinanceId = 2,
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });

        result.Successor.Amount.ShouldBe(-150m);
        result.Successor.DatePattern.Frequency.ShouldBe(RecurrenceFrequency.Monthly);
        result.Successor.DatePattern.ByMonthDay.ShouldBe(rent.DatePattern.ByMonthDay);
    }

    [Fact]
    public void Renew_extends_exactly_one_year_from_the_renewal_date()
    {
        var rent = MonthlyBill(-150m, 5, new DateOnly(2023, 1, 5), new DateOnly(2026, 1, 5));
        var renewalDate = new DateOnly(2026, 1, 5);

        var result = BreakOffFactory.Renew(new RenewalRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            RenewalDate = renewalDate,
            SegmentYears = 1,
            SuccessorFinanceId = 2,
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });

        result.Successor.DatePattern.ActiveStart.ShouldBe(renewalDate);
        result.Successor.DatePattern.Until.ShouldBe(new DateOnly(2027, 1, 5));
    }

    [Fact]
    public void Renew_reuses_the_source_verbatim()
    {
        var rent = MonthlyBill(-150m, 5, new DateOnly(2023, 1, 5), new DateOnly(2026, 1, 5));
        var renewalDate = new DateOnly(2026, 1, 5);

        var result = BreakOffFactory.Renew(new RenewalRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            RenewalDate = renewalDate,
            SegmentYears = 1,
            SuccessorFinanceId = 2,
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });

        result.Successor.Source.ShouldBe(rent.Source);
    }

    [Fact]
    public void Renew_carries_the_predecessors_auto_renew_marker_so_it_keeps_qualifying()
    {
        // The whole point of AutoRenew is surviving repeated renewals — if
        // Renew's relabeling step ever dropped it, a "keeps going" pattern
        // would stop qualifying after its very first renewal.
        var rent = MonthlyBill(-150m, 5, new DateOnly(2023, 1, 5), new DateOnly(2026, 1, 5), autoRenew: true);
        var renewalDate = new DateOnly(2026, 1, 5);

        var result = BreakOffFactory.Renew(new RenewalRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            RenewalDate = renewalDate,
            SegmentYears = 1,
            SuccessorFinanceId = 2,
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });

        result.Successor.AutoRenew.ShouldBeTrue();
    }

    [Fact]
    public void Renew_appends_a_renewed_marker_to_the_description()
    {
        var rent = MonthlyBill(-150m, 5, new DateOnly(2023, 1, 5), new DateOnly(2026, 1, 5));
        var renewalDate = new DateOnly(2026, 1, 5);

        var result = BreakOffFactory.Renew(new RenewalRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            RenewalDate = renewalDate,
            SegmentYears = 1,
            SuccessorFinanceId = 2,
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });

        result.Successor.Description.ShouldBe("Rent (renewed 2026-01-05)");
    }

    [Fact]
    public void A_second_renewal_replaces_the_marker_instead_of_stacking_it()
    {
        // Renewing an already-once-renewed pattern must not produce
        // "Rent (renewed 2026-01-05) (renewed 2027-01-05)" — only the latest
        // renewal date should ever show.
        var rent = MonthlyBill(-150m, 5, new DateOnly(2023, 1, 5), new DateOnly(2026, 1, 5));
        var firstRenewal = BreakOffFactory.Renew(new RenewalRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            RenewalDate = new DateOnly(2026, 1, 5),
            SegmentYears = 1,
            SuccessorFinanceId = 2,
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });

        var secondRenewal = BreakOffFactory.Renew(new RenewalRequest
        {
            Predecessor = firstRenewal.Successor,
            PredecessorPlan = null,
            RenewalDate = new DateOnly(2027, 1, 5),
            SegmentYears = 1,
            SuccessorFinanceId = 3,
            CarriedOverJarBalance = 0m,
            AllPatterns = [firstRenewal.Successor],
        });

        secondRenewal.Successor.Description.ShouldBe("Rent (renewed 2027-01-05)");
    }

    [Fact]
    public void Renews_carried_over_balance_lands_on_the_successors_starting_allocation()
    {
        var rent = MonthlyBill(-150m, 5, new DateOnly(2023, 1, 5), new DateOnly(2026, 1, 5));
        var renewalDate = new DateOnly(2026, 1, 5);

        var result = BreakOffFactory.Renew(new RenewalRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            RenewalDate = renewalDate,
            SegmentYears = 1,
            SuccessorFinanceId = 2,
            CarriedOverJarBalance = 42m,
            AllPatterns = [rent],
        });

        result.SuccessorPlan!.StartingAllocation.ShouldBe(42m);
    }

    [Fact]
    public void Renewing_a_paycheck_skips_the_jar_machinery_entirely()
    {
        var job = MonthlyIncome(4_000m, 15, new DateOnly(2023, 1, 15), new DateOnly(2026, 1, 15));
        var renewalDate = new DateOnly(2026, 1, 15);

        var result = BreakOffFactory.Renew(new RenewalRequest
        {
            Predecessor = job,
            PredecessorPlan = null,
            RenewalDate = renewalDate,
            SegmentYears = 1,
            SuccessorFinanceId = 101,
            CarriedOverJarBalance = 0m,
            AllPatterns = [job],
        });

        result.Successor.Amount.ShouldBe(4_000m);
        result.SuccessorPlan.ShouldBeNull();
    }

    [Fact]
    public void Renew_reaches_however_many_years_the_caller_asks_for()
    {
        // A rare, multi-year renewal cadence (author, 2026-07-29) — the
        // segment length is the caller's choice, not a hardcoded year.
        var rent = MonthlyBill(-150m, 5, new DateOnly(2023, 1, 5), new DateOnly(2026, 1, 5));
        var renewalDate = new DateOnly(2026, 1, 5);

        var result = BreakOffFactory.Renew(new RenewalRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            RenewalDate = renewalDate,
            SegmentYears = 4,
            SuccessorFinanceId = 2,
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });

        result.Successor.DatePattern.Until.ShouldBe(new DateOnly(2030, 1, 5));
    }

    [Fact]
    public void Renew_rejects_a_segment_length_under_one_year()
    {
        var rent = MonthlyBill(-150m, 5, new DateOnly(2023, 1, 5), new DateOnly(2026, 1, 5));

        Should.Throw<ArgumentOutOfRangeException>(() => BreakOffFactory.Renew(new RenewalRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            RenewalDate = new DateOnly(2026, 1, 5),
            SegmentYears = 0,
            SuccessorFinanceId = 2,
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        }));
    }

    // FindCurrentSegment — the free lookup Source-reuse already gives us
    // (2026-07-29): finding which segment of a bill is live, so an edit
    // started from a stale row can find the one it should actually target.

    [Fact]
    public void FindCurrentSegment_on_a_broken_off_predecessor_returns_its_successor()
    {
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var cutDate = new DateOnly(2025, 7, 1);
        var brokenOff = BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -1_800m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2026, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });
        var allPatterns = new[] { brokenOff.Predecessor, brokenOff.Successor };

        var current = BreakOffFactory.FindCurrentSegment(brokenOff.Predecessor, allPatterns);

        current.FinanceId.ShouldBe(brokenOff.Successor.FinanceId);
    }

    [Fact]
    public void FindCurrentSegment_on_the_successor_itself_returns_itself()
    {
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var cutDate = new DateOnly(2025, 7, 1);
        var brokenOff = BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -1_800m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2026, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });
        var allPatterns = new[] { brokenOff.Predecessor, brokenOff.Successor };

        var current = BreakOffFactory.FindCurrentSegment(brokenOff.Successor, allPatterns);

        current.FinanceId.ShouldBe(brokenOff.Successor.FinanceId);
    }

    [Fact]
    public void FindCurrentSegment_follows_a_two_hop_renewal_chain_to_the_latest_segment()
    {
        var rent = MonthlyBill(-150m, 5, new DateOnly(2023, 1, 5), new DateOnly(2026, 1, 5));
        var firstRenewal = BreakOffFactory.Renew(new RenewalRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            RenewalDate = new DateOnly(2026, 1, 5),
            SegmentYears = 3,
            SuccessorFinanceId = 2,
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });
        var secondRenewal = BreakOffFactory.Renew(new RenewalRequest
        {
            Predecessor = firstRenewal.Successor,
            PredecessorPlan = null,
            RenewalDate = new DateOnly(2029, 1, 5),
            SegmentYears = 3,
            SuccessorFinanceId = 3,
            CarriedOverJarBalance = 0m,
            AllPatterns = [firstRenewal.Successor],
        });
        var allPatterns = new[] { firstRenewal.Predecessor, firstRenewal.Successor, secondRenewal.Successor };

        // Starting from the OLDEST segment (the original, twice-superseded
        // predecessor) must skip past the middle one straight to the latest.
        var current = BreakOffFactory.FindCurrentSegment(firstRenewal.Predecessor, allPatterns);

        current.FinanceId.ShouldBe(secondRenewal.Successor.FinanceId);
    }

    [Fact]
    public void FindCurrentSegment_on_a_pattern_with_no_history_returns_itself()
    {
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));

        var current = BreakOffFactory.FindCurrentSegment(rent, [rent]);

        current.FinanceId.ShouldBe(rent.FinanceId);
    }

    // FindPredecessor/FindSuccessor — same Source-reuse lookup as
    // FindCurrentSegment, split into the two directions an editing UI
    // actually needs to ask about a specific segment: does this one
    // continue from an earlier one, and has it since been continued by a
    // later one.

    [Fact]
    public void FindPredecessor_on_a_successor_returns_its_predecessor()
    {
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var cutDate = new DateOnly(2025, 7, 1);
        var brokenOff = BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -1_800m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2026, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });
        var allPatterns = new[] { brokenOff.Predecessor, brokenOff.Successor };

        var predecessor = BreakOffFactory.FindPredecessor(brokenOff.Successor, allPatterns);

        predecessor.ShouldNotBeNull();
        predecessor.FinanceId.ShouldBe(brokenOff.Predecessor.FinanceId);
    }

    [Fact]
    public void FindPredecessor_on_a_pattern_with_no_history_returns_null()
    {
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));

        BreakOffFactory.FindPredecessor(rent, [rent]).ShouldBeNull();
    }

    [Fact]
    public void FindSuccessor_on_a_predecessor_returns_its_successor()
    {
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var cutDate = new DateOnly(2025, 7, 1);
        var brokenOff = BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -1_800m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2026, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });
        var allPatterns = new[] { brokenOff.Predecessor, brokenOff.Successor };

        var successor = BreakOffFactory.FindSuccessor(brokenOff.Predecessor, allPatterns);

        successor.ShouldNotBeNull();
        successor.FinanceId.ShouldBe(brokenOff.Successor.FinanceId);
    }

    [Fact]
    public void FindSuccessor_on_the_current_segment_returns_null()
    {
        var rent = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var cutDate = new DateOnly(2025, 7, 1);
        var brokenOff = BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = rent,
            PredecessorPlan = null,
            CutDate = cutDate,
            SuccessorFinanceId = 2,
            SuccessorAmount = -1_800m,
            SuccessorSchedule = MonthlyFrom(cutDate, 1, new DateOnly(2026, 1, 1)),
            CarriedOverJarBalance = 0m,
            AllPatterns = [rent],
        });
        var allPatterns = new[] { brokenOff.Predecessor, brokenOff.Successor };

        BreakOffFactory.FindSuccessor(brokenOff.Successor, allPatterns).ShouldBeNull();
    }

    // planning/27's Phase 1 — the FinancialPattern-level mirror of
    // RestructureFactory's own ExtendUntil/ExtendStart/CascadeForward, built
    // for EarMarkPattern chains first. Same shared-Source chain shape
    // BreakOff/FindPredecessor/FindSuccessor already use above, just testing
    // the boundary-resolution and cascade mechanisms directly instead.

    [Fact]
    public void SpansOverlap_is_true_for_two_same_source_patterns_whose_spans_overlap()
    {
        var a = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30), id: 1, source: "Rent");
        var b = MonthlyBill(-1_800m, 1, new DateOnly(2025, 5, 1), new DateOnly(2025, 12, 31), id: 2, source: "Rent");

        BreakOffFactory.SpansOverlap(a, b).ShouldBeTrue();
    }

    [Fact]
    public void SpansOverlap_is_false_for_two_strictly_contiguous_same_source_patterns()
    {
        var a = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30), id: 1, source: "Rent");
        var b = MonthlyBill(-1_800m, 1, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31), id: 2, source: "Rent");

        BreakOffFactory.SpansOverlap(a, b).ShouldBeFalse();
    }

    [Fact]
    public void ExtendUntil_nudges_a_successors_own_start_when_it_doesnt_fully_reach()
    {
        var current = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30), id: 1, source: "Rent");
        var successor = MonthlyBill(-1_800m, 1, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31), id: 2, source: "Rent");

        var result = BreakOffFactory.ExtendUntil(current, [successor], new DateOnly(2025, 8, 15));

        result.Current.DatePattern.Until.ShouldBe(new DateOnly(2025, 8, 15));
        result.Absorbed.ShouldBeEmpty();
        result.AdjustedNeighbor.ShouldNotBeNull();
        result.AdjustedNeighbor!.FinanceId.ShouldBe(2); // in-place update — FinanceId never changes
        result.AdjustedNeighbor!.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 8, 16));
        result.AdjustedNeighbor!.DatePattern.Until.ShouldBe(new DateOnly(2025, 12, 31)); // unchanged
        result.AdjustedNeighbor!.Amount.ShouldBe(-1_800m); // unchanged — this is a boundary nudge, not a cascade
    }

    [Fact]
    public void ExtendUntil_keeps_an_interval_gt_1_successors_cadence_phase_when_it_nudges_the_start()
    {
        // A biweekly-Friday successor (grid: Jun 6, 20, Jul 4, 18, ...). Growing
        // current's Until to Jul 8 nudges the successor's start to Jul 9 — which
        // lands in an "off" week of that biweekly grid. It must stay on the
        // successor's OWN Fridays (M3), not silently re-phase onto a Jul-9-
        // anchored cadence the way raw WithStart(newUntil+1) would.
        var current = BiweeklyFridayRent(-1_600m, new DateOnly(2025, 1, 3), new DateOnly(2025, 5, 30), id: 1);
        var successor = BiweeklyFridayRent(-1_800m, new DateOnly(2025, 6, 6), new DateOnly(2025, 12, 26), id: 2);

        var result = BreakOffFactory.ExtendUntil(current, [successor], new DateOnly(2025, 7, 8));

        var neighbor = result.AdjustedNeighbor!;
        neighbor.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 7, 9)); // contiguous with current's new Until
        neighbor.DatePattern.GetOccurrences().ShouldNotBeEmpty();
        // Every remaining occurrence is one the successor's own grid already had
        // — phase intact. Raw WithStart(Jul 9) would drift onto Jul 11, 25, ...
        neighbor.DatePattern.GetOccurrences().ShouldAllBe(date => successor.DatePattern.GetOccurrences().Contains(date));
    }

    private static FinancialPattern BiweeklyFridayRent(decimal amount, DateOnly start, DateOnly until, int id) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = id,
            Source = "Rent",
            Amount = amount,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Weekly,
                Interval = 2,
                ByDay = [DayOfWeek.Friday],
                DtStart = start,
                Until = until,
            }),
        });

    [Fact]
    public void ExtendUntil_absorbs_the_successor_entirely_when_it_fully_reaches()
    {
        var current = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30), id: 1, source: "Rent");
        var successor = MonthlyBill(-1_800m, 1, new DateOnly(2025, 7, 1), new DateOnly(2025, 9, 30), id: 2, source: "Rent");

        var result = BreakOffFactory.ExtendUntil(current, [successor], new DateOnly(2025, 12, 31));

        result.Current.DatePattern.Until.ShouldBe(new DateOnly(2025, 12, 31));
        result.Absorbed.ShouldHaveSingleItem();
        result.Absorbed[0].FinanceId.ShouldBe(2);
        result.AdjustedNeighbor.ShouldBeNull();
    }

    [Fact]
    public void ExtendUntil_absorption_walks_through_multiple_segments()
    {
        var current = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), id: 1, source: "Rent");
        var second = MonthlyBill(-1_700m, 1, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30), id: 2, source: "Rent");
        var third = MonthlyBill(-1_800m, 1, new DateOnly(2025, 7, 1), new DateOnly(2025, 9, 30), id: 3, source: "Rent");
        var fourth = MonthlyBill(-1_900m, 1, new DateOnly(2025, 10, 1), new DateOnly(2025, 12, 31), id: 4, source: "Rent");

        var result = BreakOffFactory.ExtendUntil(current, [second, third, fourth], new DateOnly(2025, 11, 15));

        result.Absorbed.Select(p => p.FinanceId).ShouldBe([2, 3]);
        result.AdjustedNeighbor.ShouldNotBeNull();
        result.AdjustedNeighbor!.FinanceId.ShouldBe(4);
        result.AdjustedNeighbor!.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 11, 16));
    }

    [Fact]
    public void ExtendStart_nudges_a_predecessors_own_until_when_it_doesnt_fully_reach()
    {
        var current = MonthlyBill(-1_800m, 1, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31), id: 2, source: "Rent");
        var predecessor = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30), id: 1, source: "Rent");

        var result = BreakOffFactory.ExtendStart(current, [predecessor], new DateOnly(2025, 5, 15));

        result.Current.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 5, 15));
        result.Absorbed.ShouldBeEmpty();
        result.AdjustedNeighbor.ShouldNotBeNull();
        result.AdjustedNeighbor!.FinanceId.ShouldBe(1);
        result.AdjustedNeighbor!.DatePattern.Until.ShouldBe(new DateOnly(2025, 5, 14));
        result.AdjustedNeighbor!.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 1, 1)); // unchanged
    }

    [Fact]
    public void ExtendStart_absorbs_the_predecessor_entirely_when_it_fully_reaches()
    {
        var current = MonthlyBill(-1_800m, 1, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31), id: 2, source: "Rent");
        var predecessor = MonthlyBill(-1_600m, 1, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30), id: 1, source: "Rent");

        var result = BreakOffFactory.ExtendStart(current, [predecessor], new DateOnly(2025, 1, 1));

        result.Current.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 1, 1));
        result.Absorbed.ShouldHaveSingleItem();
        result.Absorbed[0].FinanceId.ShouldBe(1);
        result.AdjustedNeighbor.ShouldBeNull();
    }

    // Deliberately matches how the real caller (FinancePatternSaveConfirmation)
    // actually invokes this — current's own Start is ALREADY newStart (every
    // real call passes current.DatePattern.ActiveStart as newStart directly), not
    // some other value like the test above uses. Found 2026-08-17: with
    // current.Start already equal to newStart, a predecessor landing on that
    // EXACT same date used to fail the (buggy) `pattern.Start < current.Start`
    // filter and get silently skipped instead of absorbed, even though the
    // loop's own `newStart <= pattern.Start` check would have said to absorb
    // it — the same bug RestructureFactory.ExtendStart had, fixed there too.
    [Fact]
    public void ExtendStart_absorbs_a_predecessor_landing_exactly_on_the_new_start_when_current_already_reflects_it()
    {
        var predecessor = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), id: 1, source: "Rent");
        var editedCurrent = MonthlyBill(-1_800m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), id: 2, source: "Rent"); // own Start already moved to Jan 1

        var result = BreakOffFactory.ExtendStart(editedCurrent, [predecessor], new DateOnly(2025, 1, 1));

        result.Absorbed.ShouldHaveSingleItem();
        result.Absorbed[0].FinanceId.ShouldBe(1);
        result.Current.DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 1, 1));
    }

    [Fact]
    public void ExtendUntil_throws_when_the_new_until_is_before_the_patterns_own_start()
    {
        var current = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30), id: 1, source: "Rent");

        Should.Throw<ArgumentException>(() => BreakOffFactory.ExtendUntil(current, [], new DateOnly(2024, 12, 31)));
    }

    [Fact]
    public void ExtendStart_throws_when_the_new_start_is_after_the_patterns_own_until()
    {
        var current = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30), id: 1, source: "Rent");

        Should.Throw<ArgumentException>(() => BreakOffFactory.ExtendStart(current, [], new DateOnly(2025, 7, 1)));
    }

    [Fact]
    public void CascadeForward_applies_the_new_amount_and_shape_to_later_segments_keeping_their_own_dates()
    {
        var successor = MonthlyBill(-1_800m, 1, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31), id: 2, source: "Rent", priority: 3, description: "Rent (raised)");
        var editedShape = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            ByMonthDay = [15], // moved from the 1st to the 15th
            DtStart = new DateOnly(2025, 1, 1),
            Until = new DateOnly(2025, 6, 30),
        });

        var result = BreakOffFactory.CascadeForward(editedShape, -1_650m, [successor]);

        result.ShouldHaveSingleItem();
        result[0].FinanceId.ShouldBe(2);
        result[0].Amount.ShouldBe(-1_650m);
        result[0].DatePattern.ByMonthDay.ShouldBe([15]);
        result[0].DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 7, 1)); // its own, untouched
        result[0].DatePattern.Until.ShouldBe(new DateOnly(2025, 12, 31)); // its own, untouched
        result[0].Priority.ShouldBe(3); // its own, untouched — cascade never touches trivial fields
        result[0].Description.ShouldBe("Rent (raised)"); // its own, untouched
    }

    [Fact]
    public void CascadeTrivialFieldsForward_applies_priority_mandatory_description_autorenew_keeping_everything_else()
    {
        var edited = MonthlyBill(-1_600m, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30), id: 1, source: "Rent", priority: 9, autoRenew: true, description: "Rent — landlord raised it");
        var successor = MonthlyBill(-1_800m, 1, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31), id: 2, source: "Rent", priority: 2, autoRenew: false, description: "Rent");

        var result = BreakOffFactory.CascadeTrivialFieldsForward(edited, [successor]);

        result.ShouldHaveSingleItem();
        result[0].FinanceId.ShouldBe(2);
        result[0].Priority.ShouldBe(9);
        result[0].AutoRenew.ShouldBeTrue();
        result[0].Description.ShouldBe("Rent — landlord raised it");
        result[0].Amount.ShouldBe(-1_800m); // its own, untouched
        result[0].DatePattern.ActiveStart.ShouldBe(new DateOnly(2025, 7, 1)); // its own, untouched
        result[0].DatePattern.Until.ShouldBe(new DateOnly(2025, 12, 31)); // its own, untouched
    }
}
