using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

// Behavioral oracles carried over from the flat-engine era (same scenarios,
// same expected numbers — the restructure onto the documented
// Book/Page/Snapshot model must not change any answer), plus new
// structure-shape tests for the onion itself.
public class TransactionLogBookFactoryTests
{
    private static int _nextFinanceId = 1000;

    private static FinancialPattern OneOffPattern(string label, decimal amount, DateOnly date) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = _nextFinanceId++,
            Source = label,
            Description = label,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                Start = date,
                Count = 1,
            }),
            Amount = amount,
        });

    private static ForecastOptions Options(
        decimal startingBalance,
        DateOnly asOfDate,
        DateOnly horizonEndDate,
        IReadOnlyList<FinancialPattern>? financialPatterns = null,
        IReadOnlyList<EarMarkPattern>? earMarkPatterns = null,
        decimal idealSafetyCushion = 0m) => new()
        {
            StartingBalance = startingBalance,
            AsOfDate = asOfDate,
            HorizonEndDate = horizonEndDate,
            FinancialPatterns = financialPatterns ?? [],
            EarMarkPatterns = earMarkPatterns ?? [],
            IdealSafetyCushion = idealSafetyCushion,
        };

    private static BalanceSnapshot SnapshotOn(ForecastResult result, DateOnly date) =>
        result.GetTimeline().Single(entry => entry.Date == date).Snapshot;

    private static decimal Jar(BalanceSnapshot snapshot, int financeId) =>
        snapshot.FundJars.Single(jar => jar.FinanceId == financeId).ExpectedAmount;

    private static decimal CushionJar(BalanceSnapshot snapshot) =>
        snapshot.FundJars.Single(jar => jar.FinanceId is null).ExpectedAmount;

    [Fact]
    public void Single_pattern_projection_matches_the_known_1240_oracle()
    {
        // Same numbers as the walking-skeleton oracle in
        // 02-csharp-sqlite-build-plan.md ($1000 - $60 + $300 = $1240).
        var soap = OneOffPattern("Soap", -60m, new DateOnly(2019, 5, 2));
        var paycheck = OneOffPattern("Paycheck", 300m, new DateOnly(2019, 5, 9));

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 1000m,
            asOfDate: new DateOnly(2019, 5, 1),
            horizonEndDate: new DateOnly(2019, 5, 31),
            financialPatterns: [soap, paycheck]));

        var payday = SnapshotOn(result, new DateOnly(2019, 5, 9));
        payday.ExpectedAmount.ShouldBe(1240m);
        // Soap is a one-occurrence mandatory expense with no accruable cycle,
        // so nothing sits in a jar: free == expected here.
        payday.ExpectedFreeAmount.ShouldBe(1240m);
    }

    [Fact]
    public void Two_occurrences_on_the_same_day_both_apply_instead_of_one_overwriting_the_other()
    {
        var rent = OneOffPattern("Rent", -50m, new DateOnly(2025, 1, 15));
        var utilities = OneOffPattern("Utilities", -30m, new DateOnly(2025, 1, 15));

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 500m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 2, 1),
            financialPatterns: [rent, utilities]));

        var day = SnapshotOn(result, new DateOnly(2025, 1, 15));
        day.ExpectedAmount.ShouldBe(420m); // 500 - 50 - 30
        day.ExpectedTransactions.Count.ShouldBe(2);
    }

    [Fact]
    public void Goal_milestone_accumulates_and_is_fully_funded_by_its_due_date()
    {
        // $1000 needed, 5 monthly installments of $200, Jan 19 - May 19, due Jun 1.
        var goal = OneTimeGoalFactory.Create(new OneTimeGoalRequest
        {
            FinanceId = 1,
            Description = "Trip to Japan",
            AmountNeeded = 1000m,
            DueDate = new DateOnly(2025, 6, 1),
            StartSavingDate = new DateOnly(2025, 1, 19),
        });

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 6, 1),
            financialPatterns: [goal.Goal],
            earMarkPatterns: [goal.SavingsPlan]));

        Jar(SnapshotOn(result, new DateOnly(2025, 1, 19)), 1).ShouldBe(200m);
        Jar(SnapshotOn(result, new DateOnly(2025, 3, 19)), 1).ShouldBe(600m);
        Jar(SnapshotOn(result, new DateOnly(2025, 5, 19)), 1).ShouldBe(1000m);

        // 3.13.5.4.a1: the milestone tracks what SHOULD be saved by each date.
        SnapshotOn(result, new DateOnly(2025, 3, 19)).FundJars.Single(j => j.FinanceId == 1)
            .MilestoneAmount.ShouldBe(600m);

        // The due date itself both pays out the goal (ExpectedAmount drops by
        // 1000 via its ExpectedTransaction) and releases the jar
        // (ASSUMED-PAIRING form of 3.13c.a10 — spent money is no longer
        // earmarked).
        var dueDay = SnapshotOn(result, new DateOnly(2025, 6, 1));
        Jar(dueDay, 1).ShouldBe(0m);
        dueDay.ExpectedAmount.ShouldBe(4000m); // 5000 - 1000

        result.GoalShortfalls.ShouldHaveSingleItem();
        result.GoalShortfalls.Single().ShortfallAmount.ShouldBe(0m);
    }

    [Fact]
    public void A_goal_already_in_progress_before_asofdate_shows_a_nonzero_jar_balance_today()
    {
        var goal = OneTimeGoalFactory.Create(new OneTimeGoalRequest
        {
            FinanceId = 1,
            Description = "Trip to Japan",
            AmountNeeded = 1000m,
            DueDate = new DateOnly(2025, 6, 1),
            StartSavingDate = new DateOnly(2025, 1, 19),
        });

        // AsOfDate is March 1 — Jan 19 and Feb 19 installments have already
        // happened, so $400 should already be sitting in the jar today, even
        // though March 1 itself has no event (the dateless initial snapshot
        // renders as the as-of row).
        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 3, 1),
            horizonEndDate: new DateOnly(2025, 6, 1),
            financialPatterns: [goal.Goal],
            earMarkPatterns: [goal.SavingsPlan]));

        var today = SnapshotOn(result, new DateOnly(2025, 3, 1));
        Jar(today, 1).ShouldBe(400m);
        today.ExpectedAmount.ShouldBe(5000m); // no expected transactions yet
        today.ExpectedFreeAmount.ShouldBe(4600m); // 5000 - 400 already earmarked
    }

    [Fact]
    public void A_savings_schedule_that_does_not_reach_the_goal_amount_is_flagged_as_a_shortfall()
    {
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 10,
            Source = "Boat Fund",
            Description = "Boat Fund",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                Start = new DateOnly(2025, 6, 1),
                Count = 1,
            }),
            Amount = -1000m,
            Mandatory = false,
        });

        // Hand-built (not via OneTimeGoalFactory, which always splits evenly)
        // so the schedule can fall genuinely short: only 3 x $100 = $300
        // saved against a $1000 goal.
        var earmark = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 10,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    Start = new DateOnly(2025, 3, 1),
                    Until = new DateOnly(2025, 5, 1),
                }),
                Amount = -100m,
            },
            goal);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 6, 1),
            financialPatterns: [goal],
            earMarkPatterns: [earmark]));

        var shortfall = result.GoalShortfalls.ShouldHaveSingleItem();
        shortfall.AmountAllocatedByDueDate.ShouldBe(300m);
        shortfall.ShortfallAmount.ShouldBe(700m);
    }

    [Fact]
    public void A_bill_exceeding_the_starting_balance_is_flagged_as_a_negative_free_balance()
    {
        var bigBill = OneOffPattern("Car repair", -500m, new DateOnly(2025, 2, 1));

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 100m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 3, 1),
            financialPatterns: [bigBill]));

        result.HasNegativeFreeBalance.ShouldBeTrue();

        // The car repair is a mandatory bill due Feb 1 with no income before
        // it, so its full amount is reserved immediately (behaviour B). The
        // shortfall therefore shows from the as-of day (Jan 1) — you can't
        // afford the upcoming repair *now*, not merely when it finally hits.
        result.FirstNegativeFreeBalanceDate.ShouldBe(new DateOnly(2025, 1, 1));
        SnapshotOn(result, new DateOnly(2025, 1, 1)).ExpectedFreeAmount.ShouldBe(-400m); // 100 - 500 reserved
        SnapshotOn(result, new DateOnly(2025, 2, 1)).ExpectedFreeAmount.ShouldBe(-400m); // still short once paid
    }

    [Fact]
    public void Occurrences_are_truncated_at_the_horizon_end_date_inclusive_of_that_day()
    {
        var asOfDate = new DateOnly(2025, 1, 1);
        var horizonEndDate = new DateOnly(2025, 4, 1);

        // Two consecutive daily occurrences straddling the horizon boundary:
        // one exactly on it, one the day after.
        var straddling = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 20,
            Source = "Straddling pattern",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Daily,
                Start = horizonEndDate,
                Until = horizonEndDate.AddDays(1),
            }),
            Amount = -10m,
        });

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 1000m,
            asOfDate: asOfDate,
            horizonEndDate: horizonEndDate,
            financialPatterns: [straddling]));

        var timeline = result.GetTimeline();
        timeline.ShouldContain(entry => entry.Date == horizonEndDate);
        timeline.ShouldNotContain(entry => entry.Date == horizonEndDate.AddDays(1));
    }

    [Fact]
    public void A_goal_due_after_the_horizon_still_gets_a_correct_shortfall_verdict()
    {
        // Due date (May 1) is one month past the horizon (Apr 1) — the goal
        // shouldn't appear fully in the day-by-day timeline, but its
        // shortfall verdict must still be evaluated against its own due date.
        var goal = OneTimeGoalFactory.Create(new OneTimeGoalRequest
        {
            FinanceId = 1,
            Description = "Trip to Japan",
            AmountNeeded = 1000m,
            DueDate = new DateOnly(2025, 5, 1),
            StartSavingDate = new DateOnly(2025, 1, 1),
        });

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 4, 1),
            financialPatterns: [goal.Goal],
            earMarkPatterns: [goal.SavingsPlan]));

        result.GetTimeline().ShouldNotContain(entry => entry.Date == new DateOnly(2025, 5, 1));
        result.GoalShortfalls.Single().ShortfallAmount.ShouldBe(0m);
    }

    [Fact]
    public void A_recurring_mandatory_bill_accrues_linearly_and_resets_after_each_payment()
    {
        var rent = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 30,
            Source = "Rent",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                Start = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 12, 1),
            }),
            Amount = -300m,
        });
        rent.Mandatory.ShouldBeTrue(); // sanity check this test relies on the amount-sign default

        // A paycheck landing before every rent due date — needed for the ramp
        // itself to apply (accrual snaps to full once nothing more arrives
        // before the due date; see the dedicated test below).
        var paycheck = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 31,
            Source = "Employer",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [25],
                Start = new DateOnly(2025, 1, 25),
                Until = new DateOnly(2025, 12, 25),
            }),
            Amount = 2000m,
        });

        // A zero-effect probe on Jan 16 purely to force that date into the
        // timeline — snapshots exist only on event dates, and bill accrual
        // has no discrete occurrence of its own to probe mid-cycle.
        var midCycleProbe = OneOffPattern("Probe", 0m, new DateOnly(2025, 1, 16));

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 1000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 3, 1),
            financialPatterns: [rent, paycheck, midCycleProbe]));

        Jar(SnapshotOn(result, new DateOnly(2025, 1, 1)), 30).ShouldBe(0m);

        var midCycle = SnapshotOn(result, new DateOnly(2025, 1, 16));
        Jar(midCycle, 30).ShouldBe(145.16m); // 15/31 of $300, already reserved out of free balance
        midCycle.ExpectedFreeAmount.ShouldBe(854.84m); // 1000 - 145.16

        var paymentDay = SnapshotOn(result, new DateOnly(2025, 2, 1));
        Jar(paymentDay, 30).ShouldBe(0m); // released the moment it's paid, not double-counted
        paymentDay.ExpectedAmount.ShouldBe(2700m); // 1000 - 300 (rent) + 2000 (Jan 25 paycheck)
        paymentDay.ExpectedFreeAmount.ShouldBe(2700m);
    }

    [Fact]
    public void A_recurring_mandatory_bill_is_fully_reserved_once_no_more_income_arrives_before_its_due_date()
    {
        var rent = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 30,
            Source = "Rent",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                Start = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 12, 1),
            }),
            Amount = -310m, // divides evenly by the 31 days in this cycle, to keep the math readable
        });

        var paycheck = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 31,
            Source = "Employer",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [15],
                Start = new DateOnly(2025, 1, 15),
                Until = new DateOnly(2025, 12, 15),
            }),
            Amount = 2000m,
        });

        var beforePaycheckProbe = OneOffPattern("Probe before payday", 0m, new DateOnly(2025, 1, 10));
        var afterPaycheckProbe = OneOffPattern("Probe after payday", 0m, new DateOnly(2025, 1, 20));

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 1000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 2, 1),
            financialPatterns: [rent, paycheck, beforePaycheckProbe, afterPaycheckProbe]));

        // Jan 10 — the Jan 15 payday still lands before Feb 1's rent, so the
        // ordinary ramp applies: 9/31 of $310.
        Jar(SnapshotOn(result, new DateOnly(2025, 1, 10)), 30).ShouldBe(90.00m);

        // Jan 20 — the Jan 15 payday has already happened, and the *next*
        // one (Feb 15) is after rent is due, so nothing more is expected to
        // arrive before Feb 1. The full $310 is reserved now, not the
        // linear-ramp fraction (19/31 of $310 = $190.00) — proving the
        // snap-to-full branch fired, not a coincidence.
        Jar(SnapshotOn(result, new DateOnly(2025, 1, 20)), 30).ShouldBe(310.00m);
    }

    [Fact]
    public void A_brand_new_bills_first_occurrence_before_any_income_is_fully_reserved_from_the_as_of_day()
    {
        // A mandatory bill whose first-ever occurrence is in the future (no
        // prior cycle) and lands before any paycheck. The money must already
        // be in hand, so it should be fully reserved on the current (as-of)
        // day — the gap that previously left a brand-new upcoming bill
        // looking unfunded until it hit.
        var registration = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 100,
            Source = "Registration",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [20],
                Start = new DateOnly(2025, 1, 20),
                Until = new DateOnly(2025, 12, 20),
            }),
            Amount = -300m,
        });
        registration.Mandatory.ShouldBeTrue();

        // No income at all -> nothing arrives before the Jan 20 due date -> B.
        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 1000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 2, 1),
            financialPatterns: [registration]));

        var asOfDay = SnapshotOn(result, new DateOnly(2025, 1, 1));
        Jar(asOfDay, 100).ShouldBe(300m); // fully reserved from day one
        asOfDay.ExpectedFreeAmount.ShouldBe(700m); // 1000 - 300, not a misleading 1000
    }

    [Fact]
    public void A_brand_new_bills_first_occurrence_after_a_paycheck_paces_from_the_forecast_start()
    {
        // Paycheck Jan 15 lands before the bill's first occurrence (Feb 15),
        // so a future paycheck helps fund it -> behaviour A. With no prior
        // cycle to anchor to, the ramp paces from the as-of date (Jan 1)
        // rather than reserving nothing.
        var paycheck = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 101,
            Source = "Employer",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [15],
                Start = new DateOnly(2025, 1, 15),
                Until = new DateOnly(2025, 12, 15),
            }),
            Amount = 2000m,
        });

        var tuition = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 102,
            Source = "Tuition",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [15],
                Start = new DateOnly(2025, 2, 15),
                Until = new DateOnly(2025, 12, 15),
            }),
            Amount = -450m,
        });
        tuition.Mandatory.ShouldBeTrue();

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 3, 1),
            financialPatterns: [paycheck, tuition]));

        // Jan 1 (as-of): pacing has just begun -> nothing reserved yet.
        Jar(SnapshotOn(result, new DateOnly(2025, 1, 1)), 102).ShouldBe(0m);

        // Jan 15 (paycheck snapshot): 14 of the 45 days from Jan 1 to the
        // Feb 15 due date have elapsed -> 14/45 * $450 = $140, proving the
        // first-cycle ramp is anchored to the forecast start, not stuck at 0.
        Jar(SnapshotOn(result, new DateOnly(2025, 1, 15)), 102).ShouldBe(140m);
    }

    [Fact]
    public void A_mandatory_bill_with_an_explicit_earmark_pattern_is_not_also_auto_accrued()
    {
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 40,
            Source = "Insurance",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                Start = new DateOnly(2025, 6, 1),
                Count = 1,
            }),
            Amount = -1200m,
        });
        bill.Mandatory.ShouldBeTrue();

        var earmark = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 40,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    Start = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 5, 1),
                }),
                Amount = -240m,
            },
            bill);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 6, 1),
            financialPatterns: [bill],
            earMarkPatterns: [earmark]));

        // The explicit earmark contributes in discrete $240 steps (4 of its 5
        // occurrences by Apr 1) — the smooth linear-accrual formula would
        // never land on exactly $960 here, so this value proves the explicit
        // path is the one being used, not a second, implicit one stacked on top.
        Jar(SnapshotOn(result, new DateOnly(2025, 4, 1)), 40).ShouldBe(960m);
    }

    [Fact]
    public void A_non_mandatory_pattern_without_an_earmark_gets_no_implicit_jar()
    {
        var subscription = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 50,
            Source = "Streaming service",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                Start = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 6, 1),
            }),
            Amount = -15m,
            Mandatory = false,
        });

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 500m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 3, 1),
            financialPatterns: [subscription]));

        SnapshotOn(result, new DateOnly(2025, 2, 1)).FundJars
            .ShouldNotContain(jar => jar.FinanceId == 50);
    }

    [Fact]
    public void A_goal_with_a_manually_set_starting_allocation_does_not_forecast_from_zero()
    {
        var retirement = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 70,
            Source = "Retirement",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                Start = new DateOnly(2030, 1, 1),
                Count = 1,
            }),
            Amount = -10000m,
            Mandatory = false,
        });

        var earmark = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 70,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    Start = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2029, 12, 1),
                }),
                Amount = -100m,
                StartingAllocation = 5000m,
            },
            retirement);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 20000m,
            asOfDate: new DateOnly(2024, 6, 1), // before the earmark's own Start (2025-01-01)
            horizonEndDate: new DateOnly(2024, 6, 1),
            financialPatterns: [retirement],
            earMarkPatterns: [earmark]));

        // Before the earmark's own contribution schedule has even started —
        // the jar already reads the manually-entered $5000, not $0.
        Jar(SnapshotOn(result, new DateOnly(2024, 6, 1)), 70).ShouldBe(5000m);

        var shortfall = result.GoalShortfalls.ShouldHaveSingleItem();
        shortfall.AmountAllocatedByDueDate.ShouldBe(5000m + 100m * 60); // starting + 60 monthly installments
    }

    [Fact]
    public void GetAutomaticallyEarmarkedBills_excludes_non_mandatory_patterns_and_bills_with_an_explicit_earmark()
    {
        var rent = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 80,
            Source = "Rent",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                Start = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 12, 1),
            }),
            Amount = -1200m,
        });

        var insurance = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 81,
            Source = "Insurance",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                Start = new DateOnly(2025, 6, 1),
                Count = 1,
            }),
            Amount = -1200m,
        });

        var subscription = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 82,
            Source = "Streaming service",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                Start = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 12, 1),
            }),
            Amount = -15m,
            Mandatory = false,
        });

        var insuranceEarmark = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 81,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    Start = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 5, 1),
                }),
                Amount = -240m,
            },
            insurance);

        var automatic = TransactionLogBookFactory.GetAutomaticallyEarmarkedBills(
            [rent, insurance, subscription],
            [insuranceEarmark]);

        var automaticIds = automatic.Select(pattern => pattern.FinanceId).ToList();
        automaticIds.ShouldBe([80]); // insurance is explicitly earmarked; the subscription isn't mandatory
    }

    [Fact]
    public void JarLabels_covers_both_automatically_earmarked_bills_and_explicit_goals()
    {
        var rent = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 90,
            Source = "Rent Co",
            Description = "Rent",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                Start = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 12, 1),
            }),
            Amount = -1000m,
        });

        var goal = OneTimeGoalFactory.Create(new OneTimeGoalRequest
        {
            FinanceId = 91,
            Description = "Trip to Japan",
            AmountNeeded = 1000m,
            DueDate = new DateOnly(2025, 6, 1),
            StartSavingDate = new DateOnly(2025, 1, 19),
        });

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 2, 1),
            financialPatterns: [rent, goal.Goal],
            earMarkPatterns: [goal.SavingsPlan]));

        result.JarLabels[90].ShouldBe("Rent"); // automatically-earmarked bill, no explicit EarMarkPattern
        result.JarLabels[91].ShouldBe("Trip to Japan"); // explicit goal
    }

    [Fact]
    public void A_15_year_horizon_with_multiple_recurring_patterns_computes_correctly_and_quickly()
    {
        // No app-imposed ceiling on the horizon means years-long horizons x
        // multiple recurring patterns is a real scenario. This checks
        // correctness at that scale and guards against an accidental
        // O(dates x occurrences) blowup in the cascade.
        var paycheck = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 60,
            Source = "Employer",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [15],
                Start = new DateOnly(2025, 1, 15),
                Until = new DateOnly(2040, 1, 15),
            }),
            Amount = 2000m,
        });

        var rent = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 61,
            Source = "Rent",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                Start = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2040, 1, 1),
            }),
            Amount = -1200m,
        });

        var asOfDate = new DateOnly(2025, 1, 1);
        var horizonEndDate = new DateOnly(2040, 1, 1); // exactly 15 years out

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: asOfDate,
            horizonEndDate: horizonEndDate,
            financialPatterns: [paycheck, rent]));
        stopwatch.Stop();

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));

        var timeline = result.GetTimeline();
        timeline.Select(entry => entry.Date).Min().ShouldBe(asOfDate);
        timeline.Select(entry => entry.Date).Max().ShouldBe(horizonEndDate);

        // Hand-checkable near the start, where the arithmetic stays simple.
        SnapshotOn(result, new DateOnly(2025, 1, 1)).ExpectedAmount.ShouldBe(5000m); // AsOfDate itself
        SnapshotOn(result, new DateOnly(2025, 1, 15)).ExpectedAmount.ShouldBe(7000m); // + paycheck
        SnapshotOn(result, new DateOnly(2025, 2, 1)).ExpectedAmount.ShouldBe(5800m); // - rent

        // Rent's automatic reservation jar resets every month, so it must
        // never grow unbounded across 180 cycles.
        timeline.ShouldAllBe(entry => entry.Snapshot.FundJars.Single(j => j.FinanceId == 61).ExpectedAmount <= 1200m);
    }

    [Fact]
    public void No_patterns_at_all_still_yields_a_seed_day_and_no_crash()
    {
        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 250m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 4, 1)));

        var timeline = result.GetTimeline();
        timeline.ShouldHaveSingleItem();
        timeline.Single().Date.ShouldBe(new DateOnly(2025, 1, 1));
        timeline.Single().Snapshot.ExpectedAmount.ShouldBe(250m);
        result.GoalShortfalls.ShouldBeEmpty();
        result.HasNegativeFreeBalance.ShouldBeFalse();
    }

    // ===== Structure-shape tests: the onion itself =====

    [Fact]
    public void The_book_holds_one_window_sized_page_with_one_primary_account()
    {
        var paycheck = OneOffPattern("Paycheck", 300m, new DateOnly(2025, 1, 15));

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 100m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 2, 1),
            financialPatterns: [paycheck]));

        result.Book.PageLength.ShouldBeNull(); // DIVERGENCE(page-length): one window-sized page for now
        var page = result.Book.LogPages.ShouldHaveSingleItem();
        page.StartDate.ShouldBe(new DateOnly(2025, 1, 1));
        page.EndDate.ShouldBe(new DateOnly(2025, 2, 1));
        var accountPage = page.AccountPages.ShouldHaveSingleItem();
        accountPage.Key.ShouldBe(TransactionLogBookFactory.PrimaryAccountName);
        accountPage.Value.Expired.ShouldBeFalse();
    }

    [Fact]
    public void The_initial_snapshot_is_dateless_carries_the_entered_balance_and_lives_outside_the_balance_record()
    {
        var paycheck = OneOffPattern("Paycheck", 300m, new DateOnly(2025, 1, 15));

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 100m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 2, 1),
            financialPatterns: [paycheck]));

        var page = result.PrimaryAccountPage;
        page.InitialSnapshot.SnapshotDate.ShouldBeNull();
        page.InitialSnapshot.FullAmount.ShouldBe(100m); // the one manually-entered number
        page.InitialSnapshot.ExpectedTransactions.ShouldBeEmpty();
        page.InitialSnapshot.EarMarkEvents.ShouldBeEmpty();
        page.BalanceRecord.Values.ShouldNotContain(snapshot => snapshot.SnapshotDate == null);

        // The dynamic "right now" values (ASSUMED-PAIRING: unpaid = 0).
        page.CurrentUnpaidExpected.ShouldBe(0m);
        page.CurrentFreeAmount.ShouldBe(page.InitialSnapshot.ExpectedFreeAmount);
    }

    [Fact]
    public void Snapshots_exist_exactly_on_event_dates_and_hold_that_days_events()
    {
        var paycheck = OneOffPattern("Paycheck", 300m, new DateOnly(2025, 1, 15));

        var goal = OneTimeGoalFactory.Create(new OneTimeGoalRequest
        {
            FinanceId = 1,
            Description = "Trip",
            AmountNeeded = 300m,
            DueDate = new DateOnly(2025, 3, 10),
            StartSavingDate = new DateOnly(2025, 1, 20),
        });

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 1000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 3, 15),
            financialPatterns: [paycheck, goal.Goal],
            earMarkPatterns: [goal.SavingsPlan]));

        var page = result.PrimaryAccountPage;

        // adjust_snapshots rule: a snapshot on every event date, nowhere else.
        page.BalanceRecord.Keys.ShouldBe([
            new DateOnly(2025, 1, 15), // paycheck ExpectedTransaction
            new DateOnly(2025, 1, 20), // savings installment EarMarkEvent
            new DateOnly(2025, 2, 20),
            new DateOnly(2025, 3, 10), // goal due: ExpectedTransaction + release event
        ]);

        var payday = page.BalanceRecord[new DateOnly(2025, 1, 15)];
        var expectedTransaction = payday.ExpectedTransactions.ShouldHaveSingleItem();
        expectedTransaction.ExpectedAmount.ShouldBe(300m);
        expectedTransaction.PairedAmount.ShouldBeNull(); // ASSUMED-PAIRING: dormant
        payday.ActualTransactions.ShouldBeEmpty();

        var installmentDay = page.BalanceRecord[new DateOnly(2025, 1, 20)];
        var contribution = installmentDay.EarMarkEvents.ShouldHaveSingleItem();
        contribution.RepeatedEarmark.ShouldBeTrue();
        contribution.ExpectedAmount.ShouldBe(150m); // positive = into the jar
        contribution.ActualAmount.ShouldBeNull();

        var dueDay = page.BalanceRecord[new DateOnly(2025, 3, 10)];
        var release = dueDay.EarMarkEvents.ShouldHaveSingleItem();
        release.RepeatedEarmark.ShouldBeFalse(); // implicit isolated event
        release.ExpectedAmount.ShouldBe(-300m); // money released as the goal pays out
        release.ExplicitAmount.ShouldBe(0m); // nothing user-entered about it
    }

    [Fact]
    public void Every_snapshot_carries_exactly_one_safety_cushion_jar_with_no_milestone()
    {
        var rent = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 30,
            Source = "Rent",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                Start = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 12, 1),
            }),
            Amount = -300m,
        });

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 1000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 3, 1),
            financialPatterns: [rent]));

        foreach (var entry in result.GetTimeline())
        {
            // 9.5.a1: exactly one finance_id = null jar per snapshot.
            var cushion = entry.Snapshot.FundJars.Single(jar => jar.FinanceId == null);
            cushion.ExpectedAmount.ShouldBe(0m); // real cushion math is the next phase
            cushion.MilestoneAmount.ShouldBeNull();
        }

        // An auto-reserved bill has no savings plan, so nothing to be
        // "behind" on — its jar carries no milestone either.
        var billJar = result.GetTimeline()[^1].Snapshot.FundJars.Single(jar => jar.FinanceId == 30);
        billJar.MilestoneAmount.ShouldBeNull();
    }

    [Fact]
    public void Expected_free_amount_always_equals_expected_minus_the_sum_of_all_jars()
    {
        var goal = OneTimeGoalFactory.Create(new OneTimeGoalRequest
        {
            FinanceId = 1,
            Description = "Trip",
            AmountNeeded = 600m,
            DueDate = new DateOnly(2025, 6, 1),
            StartSavingDate = new DateOnly(2025, 1, 10),
        });

        var rent = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 2,
            Source = "Rent",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                Start = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 12, 1),
            }),
            Amount = -800m,
        });

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 3000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 4, 1),
            financialPatterns: [goal.Goal, rent],
            earMarkPatterns: [goal.SavingsPlan]));

        foreach (var entry in result.GetTimeline())
        {
            // 3.13.4.a1, held on every single day of the timeline.
            entry.Snapshot.ExpectedFreeAmount.ShouldBe(
                entry.Snapshot.ExpectedAmount!.Value - entry.Snapshot.FundJars.Sum(jar => jar.ExpectedAmount));
        }
    }

    // ===== Step 2: deallocation integrated into the cascade =====

    // A goal jar pre-loaded with `alreadySaved` and no in-window activity: the
    // purchase and the savings schedule are both parked far in the future, so
    // the jar just sits at `alreadySaved` until something drains it. Lets a
    // deallocation scenario start from a known jar balance.
    private static (FinancialPattern Goal, EarMarkPattern Earmark) ParkedGoal(
        int financeId, int priority, decimal alreadySaved, string label)
    {
        var farFuture = new DateOnly(2030, 1, 1);
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = financeId,
            Source = label,
            Description = label,
            Priority = priority,
            Mandatory = false,
            Amount = -1000m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                Start = farFuture,
                Count = 1,
            }),
        });
        var earmark = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = financeId,
                StartingAllocation = alreadySaved,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Yearly,
                    Start = farFuture,
                    Count = 1,
                }),
            },
            goal);
        return (goal, earmark);
    }

    // A one-off NON-mandatory expense: has an ExpectedTransaction but no
    // auto-reservation jar, so it lands as pure unpaired spending (`au`).
    private static FinancialPattern Discretionary(int financeId, decimal amount, DateOnly date, string label) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = financeId,
            Source = label,
            Description = label,
            Mandatory = false,
            Amount = amount,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                Start = date,
                Count = 1,
            }),
        });

    [Fact]
    public void An_unpaired_expense_drains_a_goal_jar_on_a_deallocation_day()
    {
        // $500 parked in a Vacation jar; balance is exactly that, so free is $0.
        // A $400 purchase can't come from free funds → it deallocates, pulling
        // $400 out of the Vacation jar (06/07 decision #3).
        var (goal, earmark) = ParkedGoal(financeId: 1, priority: 5, alreadySaved: 500m, label: "Vacation");
        var purchase = Discretionary(financeId: 2, amount: -400m, date: new DateOnly(2025, 6, 1), label: "New couch");

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 500m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goal, purchase],
            earMarkPatterns: [earmark]));

        // Before the purchase the jar holds the full 500 (the as-of row).
        Jar(SnapshotOn(result, new DateOnly(2025, 1, 1)), 1).ShouldBe(500m);

        // On the purchase day it is drained to 100 and free stays at 0.
        var purchaseDay = SnapshotOn(result, new DateOnly(2025, 6, 1));
        Jar(purchaseDay, 1).ShouldBe(100m);
        purchaseDay.ExpectedFreeAmount.ShouldBe(0m);
        result.HasNegativeFreeBalance.ShouldBeFalse();
    }

    [Fact]
    public void Deallocation_drains_the_lower_priority_jar_first()
    {
        // Two $300 jars; a $200 purchase deallocates. Priority 1 (lower) drains
        // before priority 5 (higher / more protected) — doc 07 decision #7.
        var (lowGoal, lowEarmark) = ParkedGoal(1, priority: 1, alreadySaved: 300m, label: "Low priority");
        var (highGoal, highEarmark) = ParkedGoal(2, priority: 5, alreadySaved: 300m, label: "High priority");
        var purchase = Discretionary(3, -200m, new DateOnly(2025, 6, 1), "Purchase");

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 600m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [lowGoal, highGoal, purchase],
            earMarkPatterns: [lowEarmark, highEarmark]));

        var day = SnapshotOn(result, new DateOnly(2025, 6, 1));
        Jar(day, 1).ShouldBe(100m); // lower priority drained first
        Jar(day, 2).ShouldBe(300m); // higher priority untouched
        day.ExpectedFreeAmount.ShouldBe(0m);
    }

    [Fact]
    public void Total_jar_allocation_never_exceeds_available_funds_across_the_timeline()
    {
        // A $500 jar, then three $200 purchases: the first two deallocate and
        // stay solvent, the third overruns into debt. On every day allocation
        // stays capped at (non-negative) funds, and free is only negative once
        // the jars are fully drained.
        var (goal, earmark) = ParkedGoal(1, priority: 5, alreadySaved: 500m, label: "Goal");
        var p1 = Discretionary(2, -200m, new DateOnly(2025, 3, 1), "Buy 1");
        var p2 = Discretionary(3, -200m, new DateOnly(2025, 6, 1), "Buy 2");
        var p3 = Discretionary(4, -200m, new DateOnly(2025, 9, 1), "Buy 3");

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 500m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goal, p1, p2, p3],
            earMarkPatterns: [earmark]));

        foreach (var entry in result.GetTimeline())
        {
            var expected = entry.Snapshot.ExpectedAmount!.Value;
            var allocated = entry.Snapshot.FundJars.Sum(jar => jar.ExpectedAmount);

            allocated.ShouldBeLessThanOrEqualTo(Math.Max(0m, expected));
            // Free is non-negative unless we're in debt, in which case every jar
            // has been fully drained.
            (entry.Snapshot.ExpectedFreeAmount >= 0m || allocated == 0m).ShouldBeTrue();
        }

        // The third purchase is the debt day.
        var debtDay = SnapshotOn(result, new DateOnly(2025, 9, 1));
        Jar(debtDay, 1).ShouldBe(0m);
        debtDay.ExpectedFreeAmount.ShouldBe(-100m);
    }

    [Fact]
    public void A_debt_day_drains_every_jar_to_zero_and_reports_a_negative_free_balance()
    {
        // Balance 100, all parked in a jar; a $300 purchase can't be covered
        // even by draining the jar → debt of $200 (Goal 2.2).
        var (goal, earmark) = ParkedGoal(1, priority: 5, alreadySaved: 100m, label: "Goal");
        var purchase = Discretionary(2, -300m, new DateOnly(2025, 6, 1), "Emergency");

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 100m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goal, purchase],
            earMarkPatterns: [earmark]));

        var day = SnapshotOn(result, new DateOnly(2025, 6, 1));
        Jar(day, 1).ShouldBe(0m);
        day.ExpectedFreeAmount.ShouldBe(-200m); // c + au = 100 - 300
        result.HasNegativeFreeBalance.ShouldBeTrue();
        result.FirstNegativeFreeBalanceDate.ShouldBe(new DateOnly(2025, 6, 1));
    }

    [Fact]
    public void A_scheduled_goal_contribution_on_a_deallocation_day_is_cancelled()
    {
        // Jar at $200 with a scheduled +$100 contribution on the same day a $50
        // purchase deallocates. The contribution is cancelled (the jar does not
        // rise) and $50 more is pulled out — the Step-B Intention-2 path.
        var farFuture = new DateOnly(2030, 1, 1);
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Goal",
            Description = "Goal",
            Priority = 5,
            Mandatory = false,
            Amount = -1000m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                Start = farFuture,
                Count = 1,
            }),
        });
        var earmark = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                StartingAllocation = 200m,
                Amount = -100m, // +100 into the jar
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    Start = new DateOnly(2025, 2, 1),
                    Until = new DateOnly(2025, 2, 1),
                }),
            },
            goal);
        var purchase = Discretionary(2, -50m, new DateOnly(2025, 2, 1), "Purchase");

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 200m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goal, purchase],
            earMarkPatterns: [earmark]));

        var day = SnapshotOn(result, new DateOnly(2025, 2, 1));
        // Without deallocation the jar would be 200 + 100 = 300; instead the
        // contribution is undone and $50 drained, leaving 150.
        Jar(day, 1).ShouldBe(150m);
        day.ExpectedFreeAmount.ShouldBe(0m);
    }

    [Fact]
    public void A_drained_auto_bill_jar_keeps_a_single_isolated_earmark_for_the_day()
    {
        // A mandatory bill mid-ramp plus a parked goal; a purchase deallocates
        // on a day the bill is still accruing. The bill jar carries an isolated
        // reservation delta AND a deallocation give-back — they must MERGE into
        // one isolated earmark, never two (EarMarkEvent's one-isolated-per-jar
        // rule), or the detail pane double-counts.
        var (goal, earmark) = ParkedGoal(1, priority: 5, alreadySaved: 350m, label: "Goal");
        var bill = OneOffPattern("Rent", -300m, new DateOnly(2025, 6, 1)); // mandatory → auto-reserved
        var paycheck = OneOffPattern("Paycheck", 100m, new DateOnly(2025, 5, 1)); // income before due → bill ramps
        var purchase = Discretionary(2, -100m, new DateOnly(2025, 4, 1), "Purchase");

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 350m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goal, bill, paycheck, purchase],
            earMarkPatterns: [earmark]));

        // No jar ever carries more than one isolated earmark on any day.
        foreach (var entry in result.GetTimeline())
        {
            entry.Snapshot.EarMarkEvents
                .Where(e => !e.RepeatedEarmark)
                .GroupBy(e => e.FinanceId)
                .ShouldAllBe(group => group.Count() == 1);
        }

        // On the deallocation day the goal drains and the bill's reservation is
        // cancelled, but the bill still shows exactly one (merged) earmark.
        var day = SnapshotOn(result, new DateOnly(2025, 4, 1));
        Jar(day, 1).ShouldBe(250m);
        Jar(day, bill.FinanceId).ShouldBe(0m);
        day.EarMarkEvents.Count(e => e.FinanceId == bill.FinanceId).ShouldBe(1);
    }

    // ===== Step 3: safety cushion =====

    [Fact]
    public void The_safety_cushion_occupies_free_funds_up_to_its_target()
    {
        // A $100 cushion against a $500 balance with no other jars: free reads
        // $400, and the cushion jar holds the $100.
        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 500m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            idealSafetyCushion: 100m));

        var today = SnapshotOn(result, new DateOnly(2025, 1, 1));
        CushionJar(today).ShouldBe(100m);
        today.ExpectedFreeAmount.ShouldBe(400m);
    }

    [Fact]
    public void The_cushion_is_drained_before_any_goal_jar_on_a_deallocation_day()
    {
        // Balance 500 = $100 cushion + $300 goal + $100 free. A $250 purchase:
        // the $100 free absorbs part, then the cushion (priority 0) empties
        // fully before the goal gives up only what's still needed.
        var (goal, earmark) = ParkedGoal(1, priority: 5, alreadySaved: 300m, label: "Goal");
        var purchase = Discretionary(2, -250m, new DateOnly(2025, 6, 1), "Purchase");

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 500m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goal, purchase],
            earMarkPatterns: [earmark],
            idealSafetyCushion: 100m));

        var day = SnapshotOn(result, new DateOnly(2025, 6, 1));
        CushionJar(day).ShouldBe(0m);   // drained first, fully
        Jar(day, 1).ShouldBe(250m);     // goal only gives up the remaining 50
        day.ExpectedFreeAmount.ShouldBe(0m);
        day.IsDeallocationDay.ShouldBeTrue();
    }

    [Fact]
    public void The_cushion_refills_toward_its_target_after_a_drain_once_funds_allow()
    {
        // Balance 100, all in a $100 cushion (free $0). A $50 purchase drains
        // the cushion to $50; a later $200 paycheck lets it refill to $100.
        var purchase = Discretionary(1, -50m, new DateOnly(2025, 3, 1), "Purchase");
        var paycheck = Discretionary(2, 200m, new DateOnly(2025, 6, 1), "Paycheck");

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 100m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [purchase, paycheck],
            idealSafetyCushion: 100m));

        CushionJar(SnapshotOn(result, new DateOnly(2025, 3, 1))).ShouldBe(50m);  // drained
        CushionJar(SnapshotOn(result, new DateOnly(2025, 6, 1))).ShouldBe(100m); // refilled
    }

    [Fact]
    public void An_unaffordable_firm_cushion_reserves_in_full_and_drives_free_negative()
    {
        // Firm target: a $200 cushion against a $100 balance reserves the whole
        // $200, so reported free goes -$100 from the as-of day.
        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 100m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            idealSafetyCushion: 200m));

        var today = SnapshotOn(result, new DateOnly(2025, 1, 1));
        CushionJar(today).ShouldBe(200m);
        today.ExpectedFreeAmount.ShouldBe(-100m);
        result.HasNegativeFreeBalance.ShouldBeTrue();
        result.FirstNegativeFreeBalanceDate.ShouldBe(new DateOnly(2025, 1, 1));
    }

    [Fact]
    public void The_deallocation_day_flag_is_set_only_on_days_that_actually_deallocate()
    {
        // A tiny early expense (covered by free) is not a deallocation day; the
        // later big one (which raids the goal jar) is.
        var (goal, earmark) = ParkedGoal(1, priority: 5, alreadySaved: 300m, label: "Goal");
        var small = Discretionary(2, -10m, new DateOnly(2025, 3, 1), "Small");
        var big = Discretionary(3, -250m, new DateOnly(2025, 6, 1), "Big");

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 500m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goal, small, big],
            earMarkPatterns: [earmark]));

        SnapshotOn(result, new DateOnly(2025, 3, 1)).IsDeallocationDay.ShouldBeFalse();
        SnapshotOn(result, new DateOnly(2025, 6, 1)).IsDeallocationDay.ShouldBeTrue();
    }
}
