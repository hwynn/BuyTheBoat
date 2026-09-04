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
                DtStart = date,
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
        decimal idealSafetyCushion = 0m,
        IReadOnlyList<ManualEarmark>? manualEarmarks = null) => new()
        {
            StartingBalance = startingBalance,
            AsOfDate = asOfDate,
            HorizonEndDate = horizonEndDate,
            FinancialPatterns = financialPatterns ?? [],
            EarMarkPatterns = earMarkPatterns ?? [],
            IdealSafetyCushion = idealSafetyCushion,
            ManualEarmarks = manualEarmarks ?? [],
        };

    private static BalanceSnapshot SnapshotOn(ForecastResult result, DateOnly date) =>
        result.GetTimeline().Single(entry => entry.Date == date).Snapshot;

    private static decimal Jar(BalanceSnapshot snapshot, int financeId) =>
        snapshot.FundJars.Single(jar => jar.FinanceId == financeId).ExpectedAmount;

    private static decimal CushionJar(BalanceSnapshot snapshot) =>
        snapshot.FundJars.Single(jar => jar.FinanceId is null).ExpectedAmount;

    // Answers a real, asked question (2026-08-14): does a release reset
    // ExpectedAmount to 0, or does it only subtract the goal's own
    // per-occurrence amount, leaving any excess sitting in the jar? Only
    // MilestoneAmount resets at release (3.13.5.4.a1) — ExpectedAmount just
    // tracks net contributions minus releases, accurately, forever. A
    // structural glut (contributing more than the goal needs every single
    // cycle, not just a one-time head start) is never wiped by ordinary
    // cascade behavior — it accumulates, cycle over cycle. The only real
    // risk to a glut is the newer re-proposal mechanisms
    // (EarmarkConsolidation/EarmarkScaling/ProposeSameSchedule/ProposeSameAmount)
    // that explicitly treat "already banked" as one redistributable number —
    // not anything in the base cascade itself.
    [Fact]
    public void A_structural_glut_accumulates_across_releases_instead_of_being_reset()
    {
        // Contributes MORE than the goal needs every single cycle — a
        // permanent, structural glut, not a one-time head start.
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = _nextFinanceId++,
            Source = "Rent",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 12, 31),
            }),
            Amount = -100m,
        });
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = -150m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 12, 31),
                }),
            },
            goal);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 10_000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 3, 31),
            financialPatterns: [goal],
            earMarkPatterns: [plan]));

        // $50 excess per cycle ($150 contributed - $100 released), growing
        // by $50 every month rather than resetting — not reset to $0, and
        // not reset back to just one cycle's own $50 either.
        Jar(SnapshotOn(result, new DateOnly(2025, 1, 1)), goal.FinanceId).ShouldBe(50m);
        Jar(SnapshotOn(result, new DateOnly(2025, 2, 1)), goal.FinanceId).ShouldBe(100m);
        Jar(SnapshotOn(result, new DateOnly(2025, 3, 1)), goal.FinanceId).ShouldBe(150m);
    }

    // Mechanism C, end to end: proves an EarMarkPattern's own ExcludedDates
    // (redesign/planning/26, "the glut case") actually suppresses a real
    // contribution in a real forecast, not just in GetOccurrences' own unit
    // tests (RecurrenceRuleTests) — every call site the cascade uses
    // (TransactionLogBookFactory.cs line ~375, the repeated-earmark-event
    // generation loop) reads DatePattern.GetOccurrences() the same way, so
    // this is the one place the wiring could still have been missed.
    [Fact]
    public void An_excluded_date_produces_no_contribution_and_no_milestone_increment_in_a_real_forecast()
    {
        // A one-time goal due much later — no release lands inside the test
        // window, so ExpectedAmount is a clean running total of whichever
        // contributions actually fired, with nothing draining it back down
        // to mask the difference. ActiveFrom stretches the goal's own active
        // span back far enough for the plan below to start in January,
        // same shape as the pause-case test above.
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = _nextFinanceId++,
            Source = "Boat",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = new DateOnly(2025, 12, 25),
                Count = 1,
                ActiveFrom = new DateOnly(2025, 1, 1),
            }),
            Amount = -1000m,
        });
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 4, 1),
                    ExcludedDates = [new DateOnly(2025, 3, 1)], // deliberately skipped
                }),
            },
            goal);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 10_000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 4, 30),
            financialPatterns: [goal],
            earMarkPatterns: [plan]));

        // Jan + Feb + Apr = 3 real contributions, not 4 — March's own $100
        // never happened, exactly as if that occurrence had never been
        // scheduled at all. March 1 itself isn't queried here: with nothing
        // scheduled that day (no contribution, no release), it's not a
        // BalanceRecord key at all — same as any other ordinary non-event
        // day — so April 1 landing on 300, not 400, is where the skipped
        // contribution's absence actually shows up.
        Jar(SnapshotOn(result, new DateOnly(2025, 1, 1)), goal.FinanceId).ShouldBe(100m);
        Jar(SnapshotOn(result, new DateOnly(2025, 2, 1)), goal.FinanceId).ShouldBe(200m);
        Jar(SnapshotOn(result, new DateOnly(2025, 4, 1)), goal.FinanceId).ShouldBe(300m);

        // The milestone line is pure schedule math (3.13.5.4.a1) — proves it
        // skips the excluded occurrence too, not just the real contribution.
        var milestoneAtEnd = SnapshotOn(result, new DateOnly(2025, 4, 1)).FundJars
            .Single(jar => jar.FinanceId == goal.FinanceId).MilestoneAmount;
        milestoneAtEnd.ShouldBe(300m);
    }

    // Answers a real, asked question (2026-08-14): can a zero-amount
    // EarMarkPattern hold an already-accumulated balance through a funding
    // pause, then a THIRD plan resume real contributions afterward — three
    // separate rows sharing one finance_id, sequential and non-overlapping,
    // nothing deleted. Confirms both halves of "the money doesn't move
    // during the pause" and "contributions pick back up cleanly after it,"
    // using the exact mechanism AllocationPlanProposer.ProposeEmpty already
    // relies on for its own "declined plan" case (Amount = 0, no other
    // special casing) — not a new mechanism, a new use of an existing one.
    [Fact]
    public void A_zero_amount_plan_holds_a_balance_through_a_pause_then_a_third_plan_resumes_contributing()
    {
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = _nextFinanceId++,
            Source = "Boat",
            Description = "Boat",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = new DateOnly(2026, 6, 15),
                Count = 1,
                ActiveFrom = new DateOnly(2025, 1, 1),
            }),
            Amount = -3000m,
        });

        var contributingPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 6, 30),
                }),
            },
            goal);

        var pausePlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = 0m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 7, 1),
                    Until = new DateOnly(2025, 12, 31),
                }),
            },
            goal);

        var resumedPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2026, 1, 1),
                    Until = new DateOnly(2026, 6, 15),
                }),
            },
            goal);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 10_000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2026, 6, 30),
            financialPatterns: [goal],
            earMarkPatterns: [contributingPlan, pausePlan, resumedPlan]));

        // Mid-pause: frozen at whatever 6 real months already put in.
        Jar(SnapshotOn(result, new DateOnly(2025, 9, 1)), goal.FinanceId).ShouldBe(600m);
        // Still frozen right up to the pause's own last day.
        Jar(SnapshotOn(result, new DateOnly(2025, 12, 1)), goal.FinanceId).ShouldBe(600m);
        // Resumed contributions add on top of the held balance, not from 0.
        Jar(SnapshotOn(result, new DateOnly(2026, 3, 1)), goal.FinanceId).ShouldBe(900m);
    }

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

        var onTrack = result.GoalShortfalls.ShouldHaveSingleItem();
        onTrack.ShortfallAmount.ShouldBe(0m);
        onTrack.OverfundedAmount.ShouldBe(0m); // exactly on track — neither signal fires
    }

    [Fact]
    public void Milestone_resets_after_each_release_instead_of_climbing_forever()
    {
        // planning/14 (2026-08-03): a repeating goal's milestone must track
        // pacing toward the CURRENT cycle, not a lifetime total — otherwise a
        // plan that is exactly on schedule reads as "short" by an ever-growing
        // amount the moment it has paid more than once. Bill (1st) and
        // contribution (15th) are offset so accumulation and release never
        // land on the same day, isolating the reset itself.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Loan",
            Amount = -500m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 12, 1),
            }),
        });
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -500m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [15],
                    DtStart = new DateOnly(2025, 1, 15),
                    Until = new DateOnly(2025, 11, 15), // must not outlast the bill's own Dec 1 end (3.11.2.a2)
                }),
            },
            bill);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2024, 12, 1),
            horizonEndDate: new DateOnly(2025, 3, 15),
            financialPatterns: [bill],
            earMarkPatterns: [plan]));

        // First cycle ramps normally.
        SnapshotOn(result, new DateOnly(2025, 1, 15)).FundJars.Single(j => j.FinanceId == 1)
            .MilestoneAmount.ShouldBe(500m);

        // The 1st release resets it — under the old cumulative-forever formula
        // this would still read 500 (nothing ever subtracted); the fix is that
        // it reads 0 here.
        SnapshotOn(result, new DateOnly(2025, 2, 1)).FundJars.Single(j => j.FinanceId == 1)
            .MilestoneAmount.ShouldBe(0m);

        // The next cycle ramps from the fresh baseline, not the old total —
        // under the old formula this would read 1000 (500 carried + 500 new).
        SnapshotOn(result, new DateOnly(2025, 2, 15)).FundJars.Single(j => j.FinanceId == 1)
            .MilestoneAmount.ShouldBe(500m);

        // And resets again on the 2nd release.
        SnapshotOn(result, new DateOnly(2025, 3, 1)).FundJars.Single(j => j.FinanceId == 1)
            .MilestoneAmount.ShouldBe(0m);
    }

    [Fact]
    public void ComputeMilestoneTrajectory_matches_the_real_cascades_reset_points_exactly()
    {
        // Same bill/plan as the cascade test just above (Milestone_resets_
        // after_each_release...) — same numbers, on purpose: this is the
        // no-forecast-needed replacement asked to prove it agrees with the
        // already-trusted day-by-day walk before relying on it anywhere.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Loan",
            Amount = -500m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 12, 1),
            }),
        });
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -500m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [15],
                    DtStart = new DateOnly(2025, 1, 15),
                    Until = new DateOnly(2025, 11, 15),
                }),
            },
            bill);

        var trajectory = TransactionLogBookFactory.ComputeMilestoneTrajectory(
            [plan], bill, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 15));

        trajectory.Single(p => p.Date == new DateOnly(2025, 1, 15)).MilestoneAmount.ShouldBe(500m);
        trajectory.Single(p => p.Date == new DateOnly(2025, 2, 1)).MilestoneAmount.ShouldBe(0m);
        trajectory.Single(p => p.Date == new DateOnly(2025, 2, 15)).MilestoneAmount.ShouldBe(500m);
        trajectory.Single(p => p.Date == new DateOnly(2025, 3, 1)).MilestoneAmount.ShouldBe(0m);
    }

    [Fact]
    public void ComputeMilestoneTrajectory_covers_dates_no_forecasts_own_AsOfDate_could_ever_reach()
    {
        // The whole point of this function, over just reading FundJar off a
        // real forecast: TransactionLogBookFactory.CreateForecast only ever
        // cascades forward from its own AsOfDate — asking it for anything
        // earlier is structurally impossible. This asks for a date range
        // that ends BEFORE "today" in every realistic sense (a full plan
        // lifetime that finished nearly a year before this test even
        // pretends to run) and still gets the right reset-and-climb shape,
        // because the computation never once needed a starting balance or
        // "as of" reference point to begin with.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 2,
            Source = "Quarterly Insurance",
            Amount = -300m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                Interval = 3,
                ByMonthDay = [1],
                DtStart = new DateOnly(2020, 1, 1),
                Until = new DateOnly(2020, 12, 1),
            }),
        });
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 2,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [15],
                    DtStart = new DateOnly(2020, 1, 15),
                    Until = new DateOnly(2020, 11, 15),
                }),
            },
            bill);

        var trajectory = TransactionLogBookFactory.ComputeMilestoneTrajectory(
            [plan], bill, new DateOnly(2020, 1, 1), new DateOnly(2020, 4, 1));

        // Climbs through the first quarter's three contributions...
        trajectory.Single(p => p.Date == new DateOnly(2020, 1, 15)).MilestoneAmount.ShouldBe(100m);
        trajectory.Single(p => p.Date == new DateOnly(2020, 2, 15)).MilestoneAmount.ShouldBe(200m);
        trajectory.Single(p => p.Date == new DateOnly(2020, 3, 15)).MilestoneAmount.ShouldBe(300m);
        // ...and resets the moment the quarterly bill actually releases.
        trajectory.Single(p => p.Date == new DateOnly(2020, 4, 1)).MilestoneAmount.ShouldBe(0m);
    }

    [Fact]
    public void ComputeMilestoneTrajectory_washes_out_a_same_day_contribution_with_its_own_release()
    {
        // A plan deliberately paced to land its contribution on the exact
        // day its own bill releases (planning/23 seed data's own Mobile
        // Carrier scenario) never shows a nonzero milestone at all — the
        // contribution accumulates first, then the same-day reset wipes it,
        // every single cycle. Not a bug in the caller reading this; the
        // underlying stream genuinely never gets ahead even by a day.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 3,
            Source = "Same-Day Bill",
            Amount = -65m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [12],
                DtStart = new DateOnly(2026, 1, 12),
                Until = new DateOnly(2026, 6, 12),
            }),
        });
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 3,
                Amount = -45m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [12],
                    DtStart = new DateOnly(2026, 1, 12),
                    Until = new DateOnly(2026, 6, 12),
                }),
            },
            bill);

        var trajectory = TransactionLogBookFactory.ComputeMilestoneTrajectory(
            [plan], bill, new DateOnly(2026, 1, 12), new DateOnly(2026, 6, 12));

        trajectory.ShouldAllBe(p => p.MilestoneAmount == 0m);
    }

    [Fact]
    public void ComputeMilestoneTrajectory_seed_only_survives_until_the_first_release_then_matches_the_unseeded_walk_exactly()
    {
        // The Summary chart's new "proposed — live estimate" line needs to
        // start from whatever's currently typed in the Starting-point
        // region, not $0 — this proves the seed is safe to add: it should
        // show up on every point BEFORE the first release (seeded ==
        // unseeded + the seed), then vanish completely from the first
        // release onward (seeded == unseeded exactly, no seed left in it) —
        // the same reset-erases-the-seed proof GetLiveJarAmounts' own
        // header comment already argues for its one "today" point, checked
        // here across a whole trajectory instead of just one.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 16,
            Source = "Seed Test Bill",
            Amount = -100m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [15],
                DtStart = new DateOnly(2026, 1, 15),
                Until = new DateOnly(2026, 2, 15),
            }).WithActiveFrom(new DateOnly(2026, 1, 1)),
        });
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 16,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [5],
                    DtStart = new DateOnly(2026, 1, 5),
                    Until = new DateOnly(2026, 2, 5),
                }),
            },
            bill);

        var from = new DateOnly(2026, 1, 5);
        var to = new DateOnly(2026, 2, 15);
        var unseeded = TransactionLogBookFactory.ComputeMilestoneTrajectory([plan], bill, from, to);
        var seeded = TransactionLogBookFactory.ComputeMilestoneTrajectory([plan], bill, from, to, startingAllocation: 200m);

        seeded.Count.ShouldBe(unseeded.Count);
        var firstReleaseIndex = seeded.ToList().FindIndex(p => p.Date == new DateOnly(2026, 1, 15));
        firstReleaseIndex.ShouldBeGreaterThan(0); // the Jan 5 contribution must land before it for this test to prove anything

        for (var i = 0; i < seeded.Count; i++)
        {
            var expected = i < firstReleaseIndex ? unseeded[i].MilestoneAmount + 200m : unseeded[i].MilestoneAmount;
            seeded[i].MilestoneAmount.ShouldBe(expected);
        }

        // Concrete numbers, not just the relative check above — the exact
        // scenario described in the comment: $300 on Jan 5 (100 unseeded +
        // the 200 seed), 0 at the Jan 15 release (seed already gone), 100
        // again on Feb 5 (identical to the unseeded walk from here on).
        seeded[0].ShouldBe((new DateOnly(2026, 1, 5), 300m));
        seeded[firstReleaseIndex].ShouldBe((new DateOnly(2026, 1, 15), 0m));
        seeded[^1].ShouldBe(unseeded[^1]);
    }

    [Fact]
    public void IsFirstOccurrencePending_is_true_before_the_first_occurrence_and_on_it_but_false_after()
    {
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 10,
            Source = "Rent",
            Amount = -1000m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2026, 3, 1),
                Until = new DateOnly(2026, 12, 1),
            }),
        });

        TransactionLogBookFactory.IsFirstOccurrencePending(bill, new DateOnly(2026, 2, 15)).ShouldBeTrue();
        TransactionLogBookFactory.IsFirstOccurrencePending(bill, new DateOnly(2026, 3, 1)).ShouldBeTrue(); // same day still counts
        TransactionLogBookFactory.IsFirstOccurrencePending(bill, new DateOnly(2026, 3, 2)).ShouldBeFalse();
    }

    [Fact]
    public void FirstOccurrenceShortfall_is_the_full_amount_when_no_contribution_lands_before_it()
    {
        // The exact scenario the author asked about directly: a plan whose
        // own first contribution comes AFTER the bill's first due date.
        // MilestoneAmount stays at $0 right up to that point (nothing has
        // happened yet to move it) — this is the check CurrentShortfallAmount
        // can't make on its own, since $0-vs-$0 reads as perfectly on pace.
        // Repeating, not one-time: an EarMarkPattern can't outlive its own
        // goal's date range (3.11.2.a2), so a plan whose contributions run
        // for months needs a goal with room for that — this is also the
        // realistic shape for this feature (DMV Registration's own real
        // seed-data scenario), not just a test-construction convenience.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 11,
            Source = "Registration",
            Amount = -180m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2026, 3, 1),
                Until = new DateOnly(2027, 12, 1),
            }),
        });
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 11,
                Amount = -60m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [15],
                    DtStart = new DateOnly(2026, 3, 15), // AFTER the Mar 1 bill
                    Until = new DateOnly(2026, 12, 15),
                }),
            },
            bill);

        TransactionLogBookFactory.FirstOccurrenceShortfall([plan], bill, [], new DateOnly(2026, 2, 1))
            .ShouldBe(180m); // nothing accumulated yet — the whole bill is exposed
    }

    [Fact]
    public void FirstOccurrenceShortfall_is_zero_when_StartingAllocation_alone_covers_it()
    {
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 12,
            Source = "Registration",
            Amount = -180m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2026, 3, 1),
                Until = new DateOnly(2027, 12, 1),
            }),
        });
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 12,
                Amount = -60m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [15],
                    DtStart = new DateOnly(2026, 3, 15),
                    Until = new DateOnly(2026, 12, 15),
                }),
                StartingAllocation = 200m,
            },
            bill);

        TransactionLogBookFactory.FirstOccurrenceShortfall([plan], bill, [], new DateOnly(2026, 2, 1)).ShouldBe(0m);
    }

    [Fact]
    public void FirstOccurrenceShortfall_counts_a_manual_earmark_dated_before_the_first_occurrence_but_not_after()
    {
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 13,
            Source = "Registration",
            Amount = -180m,
            // WithActiveFrom here too — a plan's own ActiveFrom can't precede
            // its goal's (3.13.8.a2's own family), so the goal needs an
            // earlier lead-in of its own. Its actual first OCCURRENCE stays
            // Mar 1 either way — WithActiveFrom only moves the active-span
            // boundary, never what GetOccurrences returns.
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2026, 3, 1),
                Until = new DateOnly(2027, 12, 1),
            }).WithActiveFrom(new DateOnly(2026, 1, 1)),
        });
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 13,
                Amount = -60m,
                // ManualEarmark.Create checks against DatePattern.Start
                // literally, not ActiveStart — WithActiveFrom widens the
                // active *span* but not that specific check — so Start
                // itself has to be early enough for Feb 20 to be valid.
                // ByMonthDay's own true first match (the 15th) still lands
                // on Mar 15, safely after the bill's own Mar 1 occurrence,
                // so this plan's own SCHEDULE contributes nothing in the
                // window this test cares about — verified below, not
                // assumed (this project's own RRULE/DTSTART quirks have
                // bitten hand-reasoning about this exact kind of thing
                // before).
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [15],
                    DtStart = new DateOnly(2026, 2, 20),
                    Until = new DateOnly(2026, 12, 15),
                }),
            },
            bill);

        // Confirms the schedule itself contributes nothing between Feb 20
        // and the bill's own Mar 1 occurrence, so the two assertions below
        // are actually isolating the manual earmark's own effect, not
        // silently riding on an unverified assumption about this schedule.
        plan.DatePattern.GetOccurrences(new DateOnly(2026, 2, 20), new DateOnly(2026, 3, 1)).ShouldBeEmpty();

        var earlyManual = ManualEarmark.Create(new ManualEarmarkOptions { FinanceId = 13, Date = new DateOnly(2026, 2, 20), Amount = 180m }, plan);
        var lateManual = ManualEarmark.Create(new ManualEarmarkOptions { FinanceId = 13, Date = new DateOnly(2026, 4, 1), Amount = 180m }, plan);

        TransactionLogBookFactory.FirstOccurrenceShortfall([plan], bill, [earlyManual], new DateOnly(2026, 2, 1)).ShouldBe(0m);
        TransactionLogBookFactory.FirstOccurrenceShortfall([plan], bill, [lateManual], new DateOnly(2026, 2, 1)).ShouldBe(180m);
    }

    [Fact]
    public void FirstOccurrenceShortfall_is_zero_once_the_first_occurrence_has_already_happened()
    {
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 14,
            Source = "Registration",
            Amount = -180m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2026, 3, 1),
                Until = new DateOnly(2027, 12, 1),
            }),
        });
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 14,
                Amount = -60m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [15],
                    DtStart = new DateOnly(2026, 3, 15),
                    Until = new DateOnly(2026, 12, 15),
                }),
            },
            bill);

        // asOfDate is AFTER the Mar 1 first occurrence — no longer pending,
        // even though nothing was ever actually contributed toward it.
        TransactionLogBookFactory.FirstOccurrenceShortfall([plan], bill, [], new DateOnly(2026, 3, 2)).ShouldBe(0m);
    }

    [Fact]
    public void FirstOccurrenceShortfall_is_a_partial_amount_when_only_one_of_two_needed_contributions_has_landed()
    {
        // The exact scenario the author asked about directly: a plan that
        // NEEDS two contributions to cover the bill, where only the first
        // of those two has actually landed by the bill's own due date — the
        // second is scheduled, but too late to help this occurrence. This
        // must read as a GENUINE partial shortfall (not $0, and not the
        // full $200) — proving the "current funds AND the upcoming earmark
        // events" framing the author gave: GetOccurrences counts only
        // contributions that fall on-or-before firstOccurrence, so a
        // contribution scheduled for AFTER it (Feb 1, here) simply isn't in
        // the sum yet, however "upcoming" it is.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 15,
            Source = "Soon Bill",
            Amount = -200m,
            // WithActiveFrom so the plan (below) is allowed to start Jan 1 —
            // a month ahead of the bill's own literal Start — without
            // tripping the "can't begin before the goal's active span
            // starts" check. The bill's actual first OCCURRENCE stays
            // Jan 15 either way; ActiveFrom only widens the active span.
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [15],
                DtStart = new DateOnly(2026, 1, 15),
                Until = new DateOnly(2027, 12, 15),
            }).WithActiveFrom(new DateOnly(2025, 12, 1)),
        });
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 15,
                Amount = -100m, // needs TWO of these to cover the $200 bill
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2026, 1, 1), // 1st contribution: Jan 1 — before the Jan 15 due date
                    Until = new DateOnly(2027, 11, 1), // 2nd contribution: Feb 1 — AFTER it
                }),
            },
            bill);

        // Confirmed, not assumed: exactly one contribution falls on-or-before
        // the due date, not two and not zero.
        plan.DatePattern.GetOccurrences(plan.DatePattern.ActiveStart, new DateOnly(2026, 1, 15)).Count.ShouldBe(1);

        TransactionLogBookFactory.FirstOccurrenceShortfall([plan], bill, [], new DateOnly(2026, 1, 1))
            .ShouldBe(100m); // $100 landed, $100 still short — not $0, not $200
    }

    [Fact]
    public void FirstOccurrenceFreeFunds_reports_the_free_cash_entering_the_first_payment_day()
    {
        // A $400 bill due Jan 15 with only one $100 contribution set aside by
        // then ($300 not yet earmarked), against a $1,000 starting balance — so
        // the money to cover that gap plainly EXISTS as free cash. This is the
        // number that tells "it's there, just not earmarked" from a real shortfall.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = _nextFinanceId++,
            Source = "Water bill",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [15],
                DtStart = new DateOnly(2026, 1, 1),
                Until = new DateOnly(2026, 12, 31),
            }),
            Amount = -400m,
        });
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = bill.FinanceId,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [10],
                    DtStart = new DateOnly(2026, 1, 1),
                    Until = new DateOnly(2026, 12, 31),
                }),
            },
            bill);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 1000m,
            asOfDate: new DateOnly(2026, 1, 1),
            horizonEndDate: new DateOnly(2026, 3, 1),
            financialPatterns: [bill],
            earMarkPatterns: [plan]));

        var health = result.PlanHealthStates.Single(state => state.FinanceId == bill.FinanceId);

        health.FirstOccurrenceShortfall.ShouldBe(300m); // $400 needed − $100 set aside by Jan 15
        // $1,000 balance, less the $100 moved into the jar on Jan 10 = $900 free
        // entering Jan 15 — well over the $300 gap, so the money's there.
        health.FirstOccurrenceFreeFunds.ShouldBe(900m);
        // The payment's own date, for the urgent warning's date stamp.
        health.FirstOccurrenceDate.ShouldBe(new DateOnly(2026, 1, 15));
    }

    [Fact]
    public void An_underfunded_streams_milestone_floors_at_zero_instead_of_going_negative()
    {
        // planning/14 (2026-08-03): a stream that has fallen behind must never
        // show a NEGATIVE milestone — that would flip the ExpectedAmount-vs-
        // MilestoneAmount comparison backwards and make a badly underfunded
        // plan read as overfunded instead of merely "on track" (the honest
        // limit of a per-cycle pacing signal — GoalShortfall is what actually
        // flags this stream as short over its whole span; see the sibling
        // underfunding test above). asOfDate lands after 4 cycles have already
        // run (Jan-Apr), each contributing 50 against a 100 release, so the
        // seed's own closed-form formula (200 contributed - 400 released)
        // goes negative (-200) before the floor.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Rent",
            Amount = -100m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 12, 1),
            }),
        });
        var underfundingPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -50m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 12, 1),
                }),
            },
            bill);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 4, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [bill],
            earMarkPatterns: [underfundingPlan]));

        var seeded = SnapshotOn(result, new DateOnly(2025, 4, 1)).FundJars.Single(j => j.FinanceId == 1);
        seeded.ExpectedAmount.ShouldBe(0m);
        seeded.MilestoneAmount.ShouldBe(0m); // not -200
    }

    [Fact]
    public void Two_concurrent_plans_summing_past_the_bills_own_rate_dont_let_the_milestone_accumulate_a_lifetime_surplus()
    {
        // Found in the field (2026-08-13, Storage Unit Rental): two
        // concurrent EarMarkPatterns, $35 + $25 = $60/mo, against a $50/mo
        // bill — genuinely $10/mo ahead. The initial-snapshot milestone used
        // to take a "lifetime contributed minus lifetime withdrawn"
        // shortcut that only resets correctly when a goal's plans exactly
        // match its own rate — every OTHER cycle nets to zero and cancels
        // out of the lifetime sum in that case, leaving just the current
        // cycle's own residual. The moment the plans don't sum to the
        // bill's own rate, every prior cycle leaves a real residual too,
        // and the old shortcut let it accumulate across all 8 elapsed
        // cycles into "$80 saved this cycle" instead of resetting the way
        // Milestone_resets_after_each_release_instead_of_climbing_forever
        // already established a repeating goal's milestone must. The jar's
        // own real balance (ExpectedAmount) SHOULD keep growing — the money
        // really is piling up, that isn't a bug — only the per-cycle pacing
        // milestone needed fixing.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Storage Unit Rental",
            Amount = -50m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2026, 1, 1),
                Until = new DateOnly(2027, 12, 31),
            }),
        });
        var largerPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -35m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2026, 1, 1),
                    Until = new DateOnly(2027, 12, 31),
                }),
            },
            bill);
        var smallerPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -25m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [2],
                    DtStart = new DateOnly(2026, 1, 2),
                    Until = new DateOnly(2027, 12, 31),
                }),
            },
            bill);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 10_000m,
            asOfDate: new DateOnly(2026, 8, 13), // 8 cycles elapsed: Jan through Aug releases/contributions
            horizonEndDate: new DateOnly(2026, 12, 31),
            financialPatterns: [bill],
            earMarkPatterns: [largerPlan, smallerPlan]));

        var jar = SnapshotOn(result, new DateOnly(2026, 8, 13)).FundJars.Single(j => j.FinanceId == 1);
        jar.ExpectedAmount.ShouldBe(80m); // the real jar genuinely holds 8 months' worth of the $10/mo surplus
        jar.MilestoneAmount.ShouldBe(25m); // but only the $25 contributed since the Aug 1 release counts as "this cycle" — not the lifetime total
    }

    [Fact]
    public void IsChronicShortfall_is_true_when_the_plans_own_rate_cannot_cover_the_need()
    {
        // Same shape as the sibling GoalShortfall underfunding test: 100/month
        // needed, only 50/month planned — the rate itself can never catch up,
        // so a one-time top-up would only paper over this cycle.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Rent",
            Amount = -100m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 12, 1),
            }),
        });
        var underfundingPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -50m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 12, 1),
                }),
            },
            bill);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [bill],
            earMarkPatterns: [underfundingPlan]));

        var state = result.PlanHealthStates.ShouldHaveSingleItem();
        state.IsChronicShortfall.ShouldBeTrue();

        // Every month's release wants the full 100 but the jar only ever
        // holds 50 — every occurrence the per-day cascade actually walks
        // (Feb through Dec; Jan coincides with asOfDate and is folded into
        // the seed instead) comes up short.
        state.UnderfundedReleaseDates.Count.ShouldBe(11);
        state.UnderfundedReleaseDates.ShouldContain(new DateOnly(2025, 2, 1));
        state.UnderfundedReleaseDates.ShouldContain(new DateOnly(2025, 12, 1));
    }

    [Fact]
    public void IsChronicOverfund_is_true_when_the_plans_own_rate_permanently_exceeds_the_need()
    {
        // The excess-side mirror of the sibling IsChronicShortfall test
        // above: 100/month needed, 150/month planned — the rate itself
        // permanently outpaces the goal, not a one-off surplus. Same shape
        // as Storage Unit Rental's own real bug (two concurrent plans
        // together outpacing the bill), collapsed to a single plan here for
        // a minimal repro.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Rent",
            Amount = -100m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 12, 1),
            }),
        });
        var overfundingPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -150m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 12, 1),
                }),
            },
            bill);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [bill],
            earMarkPatterns: [overfundingPlan]));

        var state = result.PlanHealthStates.ShouldHaveSingleItem();
        state.IsChronicOverfund.ShouldBeTrue();
        state.IsChronicShortfall.ShouldBeFalse(); // never both at once — opposite sides of the same gap
    }

    [Fact]
    public void IsChronicShortfall_is_false_when_a_one_time_withdrawal_caused_the_shortfall()
    {
        // The plan's own rate (100/month) exactly matches the bill (100/month)
        // — a withdrawal, not the rate, creates the shortfall, so a one-time
        // catch-up genuinely fixes it for good.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Rent",
            Amount = -100m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 6, 1),
            }),
        });
        var matchedPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 6, 1),
                }),
            },
            bill);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [bill],
            earMarkPatterns: [matchedPlan],
            manualEarmarks: [Manual(matchedPlan, new DateOnly(2025, 3, 15), -150m)]));

        var state = result.PlanHealthStates.ShouldHaveSingleItem();
        state.Shortfall.ShortfallAmount.ShouldBe(150m); // there IS a shortfall today...
        state.IsChronicShortfall.ShouldBeFalse();        // ...but not a structural one
    }

    [Fact]
    public void MostImportantHealthState_is_AlreadyMissing_when_todays_jar_is_short()
    {
        var (goal, earmark) = LiveGoal(1);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 4, 15),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goal],
            earMarkPatterns: [earmark],
            manualEarmarks: [Manual(earmark, new DateOnly(2025, 4, 10), -300m)]));

        var state = result.PlanHealthStates.ShouldHaveSingleItem();
        state.CurrentShortfallAmount.ShouldBe(200m); // milestone 400, jar 200 after the withdrawal
        state.MostImportantHealthState.ShouldBe(PlanHealthCategory.AlreadyMissing);
    }

    [Fact]
    public void MostImportantHealthState_is_WillMiss_when_today_is_fine_but_the_due_date_projects_short()
    {
        // LiveGoal's own contributions stop 6 months before its 2026 due
        // date, well short of the 10000 needed — today's own reading is
        // never short (at most a small overfund from the starting
        // allocation), but the due-date projection always is.
        var (goal, earmark) = LiveGoal(1);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goal],
            earMarkPatterns: [earmark]));

        var state = result.PlanHealthStates.ShouldHaveSingleItem();
        state.CurrentShortfallAmount.ShouldBe(0m);
        state.Shortfall.ShortfallAmount.ShouldBeGreaterThan(0m);
        state.MostImportantHealthState.ShouldBe(PlanHealthCategory.WillMiss);
        // Stub: defaults to the due date itself until the real per-occurrence walk exists.
        state.ProjectedShortfallStartDate.ShouldBe(new DateOnly(2026, 6, 1));
    }

    [Fact]
    public void MostImportantHealthState_prefers_a_current_overfund_over_a_later_one_when_nothing_is_short()
    {
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Camera",
            Amount = -500m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = new DateOnly(2025, 6, 1),
                Count = 1,
                ActiveFrom = new DateOnly(2025, 1, 1),
            }),
        });
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 5, 1),
                }),
            },
            goal);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 3, 15),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goal],
            earMarkPatterns: [plan],
            manualEarmarks: [Manual(plan, new DateOnly(2025, 2, 15), 200m)]));

        var state = result.PlanHealthStates.ShouldHaveSingleItem();
        state.CurrentOverfundedAmount.ShouldBeGreaterThan(0m);
        state.Shortfall.ShortfallAmount.ShouldBe(0m);
        state.Shortfall.OverfundedAmount.ShouldBeGreaterThan(0m); // also over by the due date
        state.MostImportantHealthState.ShouldBe(PlanHealthCategory.CurrentlyOverfunded);
    }

    [Fact]
    public void MostImportantHealthState_is_Healthy_when_nothing_is_wrong()
    {
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

        var state = result.PlanHealthStates.ShouldHaveSingleItem();
        state.MostImportantHealthState.ShouldBe(PlanHealthCategory.Healthy);
        state.ProjectedShortfallStartDate.ShouldBeNull();
    }

    [Fact]
    public void CurrentShortfallAmount_can_read_zero_while_the_due_date_projection_is_still_short()
    {
        // A withdrawal SCHEDULED for later in the year already counts toward
        // Shortfall.ShortfallAmount (it sums every manual earmark dated on or
        // before the due date, past or future) — but CurrentShortfallAmount
        // only reflects what has actually happened as of AsOfDate, so it has
        // no way to know about it yet. A perfectly matched rate keeps today's
        // own jar and milestone in lockstep at 0 until then.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Rent",
            Amount = -100m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 12, 1),
            }),
        });
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 12, 1),
                }),
            },
            bill);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 4, 1), // 4 matched cycles already behind us; nothing wrong yet
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [bill],
            earMarkPatterns: [plan],
            manualEarmarks: [Manual(plan, new DateOnly(2025, 10, 1), -150m)])); // still 6 months away

        var state = result.PlanHealthStates.ShouldHaveSingleItem();
        state.CurrentShortfallAmount.ShouldBe(0m);
        state.Shortfall.ShortfallAmount.ShouldBe(150m); // October's withdrawal already counted here
        state.IsChronicShortfall.ShouldBeFalse();        // the rate is fine; one 150 catch-up still fixes it
        state.MostImportantHealthState.ShouldBe(PlanHealthCategory.WillMiss);
    }

    [Fact]
    public void CurrentShortfallAmount_can_be_positive_while_the_due_date_projection_is_not_short()
    {
        // The reverse: a withdrawal that has ALREADY happened shows up in
        // today's own reading right away, but a larger addition already
        // scheduled for later in the year (before the due date) more than
        // covers it in Shortfall.ShortfallAmount, which counts every
        // scheduled event regardless of whether it's before or after
        // AsOfDate. Bill (15th) and plan (1st) are offset so AsOfDate can
        // land mid-cycle, after a contribution but before that cycle's own
        // release — otherwise both readings pass through 0 together and the
        // withdrawal's effect on today's own reading has nothing to show
        // against.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Rent",
            Amount = -100m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [15],
                DtStart = new DateOnly(2025, 1, 15),
                Until = new DateOnly(2025, 12, 15),
                ActiveFrom = new DateOnly(2025, 1, 1), // the plan's own Start predates this
            }),
        });
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 12, 1),
                }),
            },
            bill);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 3, 10), // after Mar 1's contribution, before Mar 15's release
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [bill],
            earMarkPatterns: [plan],
            manualEarmarks:
            [
                Manual(plan, new DateOnly(2025, 3, 5), -40m),   // already happened
                Manual(plan, new DateOnly(2025, 8, 15), 200m),  // scheduled, hasn't happened yet
            ]));

        var state = result.PlanHealthStates.ShouldHaveSingleItem();
        state.CurrentShortfallAmount.ShouldBe(40m);        // the August addition hasn't happened yet
        state.Shortfall.ShortfallAmount.ShouldBe(0m);       // but it's already scheduled, so the projection is fine
        state.Shortfall.OverfundedAmount.ShouldBe(160m);
        state.MostImportantHealthState.ShouldBe(PlanHealthCategory.AlreadyMissing); // today still wins
    }

    // planning/22 §5, full IsWorthWarningAbout spec, built 2026-08-05 (was a
    // placeholder always returning true). Non-repeated and repeated get
    // separate coverage below because the rule sets genuinely differ.

    [Fact]
    public void IsWorthWarningAbout_true_for_currently_short_regardless_of_anything_else()
    {
        // Same fixture as MostImportantHealthState_is_AlreadyMissing... above
        // — a shortage today always wins, before any of §5's other rules.
        var (goal, earmark) = LiveGoal(1);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 4, 15),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goal],
            earMarkPatterns: [earmark],
            manualEarmarks: [Manual(earmark, new DateOnly(2025, 4, 10), -300m)]));

        var state = result.PlanHealthStates.ShouldHaveSingleItem();
        state.CurrentShortfallAmount.ShouldBeGreaterThan(0m);
        state.IsWorthWarningAbout.ShouldBeTrue();
    }

    [Fact]
    public void IsWorthWarningAbout_true_for_a_non_repeated_goal_short_and_due_soon()
    {
        // planning/22 §5, non-repeated rule 2: due within the warn-if-within
        // window → warn regardless of size, even though the free balance
        // here is large enough that the far-off rule would have ignored it.
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Trip",
            Amount = -1000m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = new DateOnly(2025, 2, 15), // 45 days out — well inside 2 months
                Count = 1,
                ActiveFrom = new DateOnly(2025, 1, 1),
            }),
        });
        var earmark = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -50m, // one $50 contribution against a $1000 goal — barely started
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 1, 1),
                }),
            },
            goal);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 1_000_000m, // huge free balance — proves this isn't the half-of-free-funds rule firing
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 3, 1),
            financialPatterns: [goal],
            earMarkPatterns: [earmark]));

        var state = result.PlanHealthStates.ShouldHaveSingleItem();
        state.Shortfall.ShortfallAmount.ShouldBeGreaterThan(0m);
        state.IsWorthWarningAbout.ShouldBeTrue();
    }

    [Fact]
    public void IsWorthWarningAbout_false_for_a_non_repeated_goal_short_but_far_off_and_small_next_to_free_funds()
    {
        // planning/22 §5, non-repeated rule 3: far off, and the shortfall is
        // under half of projected account free funds that day → ignore.
        // Reuses LiveGoal's well-established 8700 shortfall (see the
        // ProjectedShortfallStartDate/WillMiss tests above) against a huge
        // starting balance, so half of free funds vastly exceeds it.
        var (goal, earmark) = LiveGoal(1);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 1_000_000m,
            asOfDate: new DateOnly(2025, 1, 1),
            // Has to reach past the 2026-06-01 due date, or the cascade never
            // simulates the actual release and HalfOfFreeFunds reads
            // pre-release (still-reserved) free funds instead of post-release
            // — exactly the horizon caveat planning/22 §3 flagged.
            horizonEndDate: new DateOnly(2026, 7, 1),
            financialPatterns: [goal],
            earMarkPatterns: [earmark]));

        var state = result.PlanHealthStates.ShouldHaveSingleItem();
        state.Shortfall.ShortfallAmount.ShouldBe(8700m);
        state.Shortfall.DueDate.ShouldBe(new DateOnly(2026, 6, 1)); // ~17 months out — well past the 2-month window
        state.IsWorthWarningAbout.ShouldBeFalse();
    }

    [Fact]
    public void IsWorthWarningAbout_true_for_a_non_repeated_goal_short_and_far_off_but_large_next_to_free_funds()
    {
        // planning/22 §5, non-repeated rule 4: same far-off 8700 shortfall as
        // above, but this time the starting balance is small enough that
        // half of projected free funds no longer covers it.
        var (goal, earmark) = LiveGoal(1);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 20_000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2026, 7, 1), // must reach past the due date — see the note above
            financialPatterns: [goal],
            earMarkPatterns: [earmark]));

        var state = result.PlanHealthStates.ShouldHaveSingleItem();
        state.Shortfall.ShortfallAmount.ShouldBe(8700m);
        state.IsWorthWarningAbout.ShouldBeTrue();
    }

    [Fact]
    public void IsWorthWarningAbout_true_for_a_repeated_pattern_short_within_the_six_month_lookahead()
    {
        // planning/22 §5, repeated rule 3: any occurrence within six months
        // being short warns regardless of the half-of-free-funds check.
        // Same underfunding fixture as IsChronicShortfall_is_true_... above
        // (100/month bill, 50/month plan) — Feb through Jun 2025 are all
        // inside the six-month window and all underfunded.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Rent",
            Amount = -100m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 12, 1),
            }),
        });
        var underfundingPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -50m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 12, 1),
                }),
            },
            bill);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [bill],
            earMarkPatterns: [underfundingPlan]));

        var state = result.PlanHealthStates.ShouldHaveSingleItem();
        state.UnderfundedReleaseDates.ShouldContain(new DateOnly(2025, 2, 1)); // inside the 6-month window
        state.IsWorthWarningAbout.ShouldBeTrue();
        // Real per-occurrence data now, not the due-date fallback — the
        // earliest actual short release (Jan 1 is the seed/as-of day, folded
        // into the initial snapshot with no delta, so Feb 1 is the first date
        // the walk itself finds short), well before the goal's own Dec 1 due date.
        state.ProjectedShortfallStartDate.ShouldBe(new DateOnly(2025, 2, 1));
    }

    [Fact]
    public void IsWorthWarningAbout_true_for_a_repeated_pattern_clear_near_term_but_large_far_off_shortfall()
    {
        // planning/22 §5, repeated rule 2: the plan matches the bill exactly
        // through August (every near-term release is fully funded — nothing
        // in the six-month lookahead is short), then stops contributing
        // while the bill keeps going, so releases from September onward come
        // up short. That first short date is well past the six-month window,
        // so only the half-of-free-funds check decides this one.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Rent",
            Amount = -100m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2026, 12, 1),
            }),
        });
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -100m, // matches the bill exactly while it runs
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 8, 1), // stops after August — bill keeps going 16 more months
                }),
            },
            bill);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 2000m, // small enough that half of it (1000) is under the 1600 shortfall
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31), // covers the near-term window; proves it's genuinely clear
            financialPatterns: [bill],
            earMarkPatterns: [plan]));

        var state = result.PlanHealthStates.ShouldHaveSingleItem();
        state.UnderfundedReleaseDates.Any(date => date <= new DateOnly(2025, 7, 1)).ShouldBeFalse(); // clear near-term
        state.Shortfall.ShortfallAmount.ShouldBe(1600m); // 16 unfunded months (Sep 2025-Dec 2026) * 100
        state.IsWorthWarningAbout.ShouldBeTrue();
    }

    [Fact]
    public void IsWorthWarningAbout_false_for_a_repeated_pattern_clear_near_term_and_small_far_off_shortfall()
    {
        // Same shape as the test above, but with enough free balance that
        // half of it comfortably covers the far-off shortfall.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Rent",
            Amount = -100m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2026, 12, 1),
            }),
        });
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 8, 1),
                }),
            },
            bill);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 1_000_000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [bill],
            earMarkPatterns: [plan]));

        var state = result.PlanHealthStates.ShouldHaveSingleItem();
        state.Shortfall.ShortfallAmount.ShouldBe(1600m);
        state.IsWorthWarningAbout.ShouldBeFalse();
    }

    [Fact]
    public void IsWorthWarningAbout_true_for_excess_beyond_double_the_smallest_repeated_contribution()
    {
        // planning/22 §5's excess rule: projected excess on the date of the
        // next expected transaction exceeds double the smallest repeated
        // EarMarkPattern amount. $500/month for 5 months way overshoots a
        // $1000 goal; double the $500 rate is $1000, and the projected
        // excess (1500) clears that.
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Trip",
            Amount = -1000m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = new DateOnly(2025, 6, 1),
                Count = 1,
                ActiveFrom = new DateOnly(2025, 1, 1),
            }),
        });
        var earmark = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -500m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 5, 1),
                }),
            },
            goal);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goal],
            earMarkPatterns: [earmark]));

        var state = result.PlanHealthStates.ShouldHaveSingleItem();
        state.Shortfall.OverfundedAmount.ShouldBe(1500m); // 2500 contributed - 1000 needed
        state.IsWorthWarningAbout.ShouldBeTrue();
    }

    [Fact]
    public void IsWorthWarningAbout_false_for_excess_within_double_the_smallest_repeated_contribution()
    {
        // Same shape, smaller rate: $300/month for 5 months overshoots the
        // same $1000 goal by only 500, which doesn't clear double the $300
        // rate (600).
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Trip",
            Amount = -1000m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = new DateOnly(2025, 6, 1),
                Count = 1,
                ActiveFrom = new DateOnly(2025, 1, 1),
            }),
        });
        var earmark = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -300m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 5, 1),
                }),
            },
            goal);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goal],
            earMarkPatterns: [earmark]));

        var state = result.PlanHealthStates.ShouldHaveSingleItem();
        state.Shortfall.OverfundedAmount.ShouldBe(500m);
        state.IsWorthWarningAbout.ShouldBeFalse();
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
                DtStart = new DateOnly(2025, 6, 1),
                Count = 1,
                ActiveFrom = new DateOnly(2025, 1, 1), // saving starts before the due date (planning/15)
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
                    DtStart = new DateOnly(2025, 3, 1),
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
        shortfall.OverfundedAmount.ShouldBe(0m); // short, not over-funded — mutually exclusive
    }

    // planning/17, item 24 (F32): the mirror case — a goal met early (the
    // charter's own example: a big manual earmark got a jar ahead of
    // schedule). No new engine computation; OverfundedAmount just surfaces
    // what AmountAllocatedByDueDate/AmountNeeded already carry.
    [Fact]
    public void An_overfunded_one_time_goal_reports_the_surplus_with_no_shortfall()
    {
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 94,
            Source = "Vacation fund",
            Amount = -1_000m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = new DateOnly(2025, 6, 1),
                Count = 1,
                ActiveFrom = new DateOnly(2025, 1, 1),
            }),
        });
        // Already saved more than the goal needs, via a lump sum rather than
        // ongoing contributions.
        var earmark = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 94,
                Amount = 0m,
                StartingAllocation = 1_200m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Yearly,
                    DtStart = new DateOnly(2025, 6, 1),
                    Count = 1,
                }),
            },
            goal);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5_000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 6, 1),
            financialPatterns: [goal],
            earMarkPatterns: [earmark]));

        var shortfall = result.GoalShortfalls.ShouldHaveSingleItem();
        shortfall.ShortfallAmount.ShouldBe(0m);
        shortfall.OverfundedAmount.ShouldBe(200m);
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

        // Stage-1 revision: the car repair has no Allocation Plan, so it is not
        // reserved ahead of time — it only reduces free funds when it lands on
        // Feb 1. So the shortfall first shows on Feb 1, not on the as-of day.
        result.FirstNegativeFreeBalanceDate.ShouldBe(new DateOnly(2025, 2, 1));
        SnapshotOn(result, new DateOnly(2025, 1, 1)).ExpectedFreeAmount.ShouldBe(100m); // not reserved ahead
        SnapshotOn(result, new DateOnly(2025, 2, 1)).ExpectedFreeAmount.ShouldBe(-400m); // 100 - 500 when it hits
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
                DtStart = horizonEndDate,
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
    public void A_bill_with_an_earmark_pattern_reserves_through_that_plan()
    {
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 40,
            Source = "Insurance",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = new DateOnly(2025, 6, 1),
                Count = 1,
                ActiveFrom = new DateOnly(2025, 1, 1), // saving starts before the due date (planning/15)
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
                    DtStart = new DateOnly(2025, 1, 1),
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
    public void An_outflow_without_an_allocation_plan_does_not_reserve()
    {
        // Stage-1 revision (planning/14 "Revision 2026-07-24"): the computed
        // ramp is retired. An outflow with no Allocation Plan of its own gets
        // NO jar — it simply reduces free funds on its due date. (In the app
        // every outflow is given a plan at creation; the engine reserves only
        // what has one.)
        var subscription = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 50,
            Source = "Streaming service",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 1, 1),
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

        // No jar for the subscription — nothing reserves it ahead of time.
        SnapshotOn(result, new DateOnly(2025, 2, 1)).FundJars
            .ShouldNotContain(jar => jar.FinanceId == 50);

        // It still lands as an expected transaction on its due date, reducing
        // free funds then (500 - 15 for the Feb 1 occurrence).
        SnapshotOn(result, new DateOnly(2025, 2, 1)).ExpectedFreeAmount.ShouldBe(485m);
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
                DtStart = new DateOnly(2030, 1, 1),
                Count = 1,
                ActiveFrom = new DateOnly(2025, 1, 1), // saving starts before the due date (planning/15)
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
                    DtStart = new DateOnly(2025, 1, 1),
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
    public void A_repeating_bills_plan_that_underfunds_the_stream_is_flagged_short()
    {
        // F21: the need is the whole stream (100 x 12 = 1200), not one
        // occurrence. A plan contributing only 50/month reaches 600 across the
        // 12 months, so it is short by 600.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Rent",
            Amount = -100m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 12, 1),
            }),
        });
        var underfundingPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -50m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 12, 1),
                }),
            },
            bill);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [bill],
            earMarkPatterns: [underfundingPlan]));

        var shortfall = result.GoalShortfalls.ShouldHaveSingleItem();
        shortfall.AmountNeeded.ShouldBe(1200m);            // 100 x 12 occurrences
        shortfall.AmountAllocatedByDueDate.ShouldBe(600m); // 50 x 12
        shortfall.ShortfallAmount.ShouldBe(600m);
    }

    [Fact]
    public void An_isolated_earmark_counts_toward_the_amount_allocated_by_the_due_date()
    {
        // F21: manual earmarks (and the starting earmark) count toward what the
        // plan will have put in by the due date, not just the pattern's own
        // contributions.
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Rent",
            Amount = -100m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 3, 1),
            }),
        });
        var plan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 1,
                Amount = -50m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 3, 1),
                }),
            },
            bill);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [bill],
            earMarkPatterns: [plan],
            manualEarmarks: [Manual(plan, new DateOnly(2025, 2, 1), 100m)]));

        // 3 plan contributions of 50 (= 150) + the 100 manual earmark = 250.
        var shortfall = result.GoalShortfalls.ShouldHaveSingleItem();
        shortfall.AmountNeeded.ShouldBe(300m); // 100 x 3
        shortfall.AmountAllocatedByDueDate.ShouldBe(250m);
    }

    [Fact]
    public void JarLabels_covers_both_a_planned_bill_and_an_explicit_goal()
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
                DtStart = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 12, 1),
            }),
            Amount = -1000m,
        });

        // Stage-1 revision: a bill reserves through its own Allocation Plan (an
        // EarMarkPattern), exactly like a goal — so it gets a jar and a label
        // the same way.
        var rentPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 90,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [15],
                    DtStart = new DateOnly(2025, 1, 15),
                    Until = new DateOnly(2025, 12, 1),
                }),
                Amount = -1000m,
            },
            rent);

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
            earMarkPatterns: [rentPlan, goal.SavingsPlan]));

        result.JarLabels[90].ShouldBe("Rent"); // a bill with an Allocation Plan
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
                DtStart = new DateOnly(2025, 1, 15),
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
                DtStart = new DateOnly(2025, 1, 1),
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

        // Stage-1 revision: rent has no Allocation Plan, so it no longer gets a
        // jar (the ramp's monthly-resetting auto-funding jar is gone). The
        // performance guard and the expected-amount arithmetic above are what
        // this test protects.
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
                DtStart = new DateOnly(2025, 1, 1),
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

        // Stage-1 revision: the rent bill has no Allocation Plan of its own, so
        // it gets no jar at all — nothing to carry a milestone.
        result.GetTimeline()[^1].Snapshot.FundJars.ShouldNotContain(jar => jar.FinanceId == 30);
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
                DtStart = new DateOnly(2025, 1, 1),
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
                DtStart = farFuture,
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
                    DtStart = farFuture,
                    Count = 1,
                }),
            },
            goal);
        return (goal, earmark);
    }

    // A one-off NON-mandatory expense: has an ExpectedTransaction but no
    // automatic funding jar, so it lands as pure unpaired spending (`au`).
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
                DtStart = date,
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

        // Stage-1 revision: the couch has no Allocation Plan, so it is not
        // reserved ahead — the $500 balance is exactly committed to Vacation,
        // free reads $0 throughout, and the deallocation on the purchase day
        // rebalances without ever driving free negative.
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

        // Dated snapshots only. The as-of row is deliberately allowed to be
        // over-allocated: deallocation is skipped on the page's starting date,
        // because a
        // plan that commits more than the balance covers is the honest "you are
        // short right now" signal rather than something to quietly drain away.
        // Item A makes that common — every planned outflow reserves — so what
        // used to be a rare case is now the normal opening position.
        foreach (var entry in result.GetTimeline().Where(row => row.Date != result.AsOfDate))
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
        // The purchase day, not the as-of day: the emergency has no Allocation
        // Plan, so it isn't reserved ahead — free only goes negative when it
        // lands on Jun 1.
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
                DtStart = farFuture,
                Count = 1,
                ActiveFrom = new DateOnly(2025, 2, 1), // saving starts before the due date (planning/15)
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
                    DtStart = new DateOnly(2025, 2, 1),
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

        // F20 DISSOLVED by the stage-1 revision (planning/14): the purchase has
        // no Allocation Plan, so it has no jar to strand money in — the $50 comes
        // straight out of the cushion on the purchase day, leaving $50 (the
        // correct answer this asserted before stage 1). The paycheck refills it.
        CushionJar(SnapshotOn(result, new DateOnly(2025, 3, 1))).ShouldBe(50m);
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

        // Stage-1 revision: outflows no longer reserve ahead, so this is back to
        // the pre-item-A behaviour the test name describes. The small $10 expense
        // is covered by free funds ($200 free after the $300 goal jar), so its
        // day is NOT a deallocation day. The later $250 expense exceeds free and
        // raids the goal jar, so it is.
        SnapshotOn(result, new DateOnly(2025, 3, 1)).IsDeallocationDay.ShouldBeFalse();
        SnapshotOn(result, new DateOnly(2025, 6, 1)).IsDeallocationDay.ShouldBeTrue();
    }

    // ===== Manual (explicit) earmarks — planning/09 =====

    // A goal whose savings plan is LIVE in the 2025 test window: $100 on the
    // 1st of each month (Jan–Dec 2025), $100 already saved, purchase far out.
    private static (FinancialPattern Goal, EarMarkPattern Earmark) LiveGoal(int financeId, string label = "Goal")
    {
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = financeId,
            Source = label,
            Description = label,
            Priority = 5,
            Mandatory = false,
            Amount = -10000m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = new DateOnly(2026, 6, 1),
                Count = 1,
                ActiveFrom = new DateOnly(2025, 1, 1), // saving starts before the due date (planning/15)
            }),
        });
        var earmark = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = financeId,
                StartingAllocation = 100m,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 12, 1),
                }),
            },
            goal);
        return (goal, earmark);
    }

    private static ManualEarmark Manual(EarMarkPattern pattern, DateOnly date, decimal amount) =>
        ManualEarmark.Create(
            new ManualEarmarkOptions { FinanceId = pattern.FinanceId, Date = date, Amount = amount },
            pattern);

    // planning/17, item 8 (F27/F29): more than one EarMarkPattern can now
    // share a finance_id — a "Restructure the plan" predecessor + successor.
    // These two tests are the regression proof for the aggregation bug found
    // while building it: both the initial-jar seed and the shortfall used to
    // ASSIGN per pattern (last one processed wins) instead of summing across
    // every plan funding the same goal.
    [Fact]
    public void Two_earmark_patterns_sharing_a_finance_id_sum_into_one_jar()
    {
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 77,
            Source = "Boat fund",
            Amount = -10_000m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = new DateOnly(2026, 6, 1),
                Count = 1,
                ActiveFrom = new DateOnly(2024, 1, 1),
            }),
        });
        // Predecessor: $100/month, Jan-Jun 2025 (6 occurrences).
        var predecessor = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 77,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 6, 1),
                }),
            },
            goal);
        // Successor ("Restructure the plan"): $150/month, Jul-Dec 2025 (6 occurrences).
        var successor = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 77,
                Amount = -150m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 7, 1),
                    Until = new DateOnly(2025, 12, 1),
                }),
            },
            goal);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 20_000m,
            asOfDate: new DateOnly(2026, 1, 1), // after both segments have fully run
            horizonEndDate: new DateOnly(2026, 6, 1),
            financialPatterns: [goal],
            earMarkPatterns: [predecessor, successor]));

        // 6 × 100 + 6 × 150 = 1500 — both segments counted, not just one.
        Jar(SnapshotOn(result, new DateOnly(2026, 1, 1)), 77).ShouldBe(1_500m);
    }

    [Fact]
    public void Two_earmark_patterns_sharing_a_finance_id_produce_one_shortfall_row()
    {
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 78,
            Source = "Boat fund 2",
            Amount = -10_000m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = new DateOnly(2026, 6, 1),
                Count = 1,
                ActiveFrom = new DateOnly(2024, 1, 1),
            }),
        });
        var predecessor = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 78,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 6, 1),
                }),
            },
            goal);
        var successor = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 78,
                Amount = -150m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 7, 1),
                    Until = new DateOnly(2025, 12, 1),
                }),
            },
            goal);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 20_000m,
            asOfDate: new DateOnly(2026, 1, 1),
            horizonEndDate: new DateOnly(2026, 6, 1),
            financialPatterns: [goal],
            earMarkPatterns: [predecessor, successor]));

        // One row for the goal, not one per plan — and its allocated total
        // sums both segments (1500), not just whichever pattern was last.
        var shortfall = result.GoalShortfalls.ShouldHaveSingleItem();
        shortfall.AmountAllocatedByDueDate.ShouldBe(1_500m);
    }

    // planning/17, item 9 (F30): two concurrent earmark patterns (e.g. a household
    // partner's own paycheck starts funding the same goal) can generate an
    // occurrence on the same day — merged into ONE event, summing the
    // amounts, not two separate events (3.13.8.1.a2).
    [Fact]
    public void Two_concurrent_earmark_patterns_landing_on_the_same_day_merge_into_one_event()
    {
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 91,
            Source = "Shared savings goal",
            Amount = -10_000m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = new DateOnly(2027, 1, 1),
                Count = 1,
                ActiveFrom = new DateOnly(2025, 1, 1),
            }),
        });
        // Partner A, already contributing from January.
        var partnerA = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 91,
                Amount = -50m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 12, 1),
                }),
            },
            goal);
        // Partner B, starting a concurrent plan in February — same day of
        // month, so their occurrences collide with Partner A's from then on.
        var partnerB = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 91,
                Amount = -30m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 2, 1),
                    Until = new DateOnly(2025, 12, 1),
                }),
            },
            goal);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 20_000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goal],
            earMarkPatterns: [partnerA, partnerB]));

        // Before Partner B starts: one event, Partner A's own amount.
        var january = SnapshotOn(result, new DateOnly(2025, 1, 1));
        january.EarMarkEvents.Count(e => e.FinanceId == 91).ShouldBe(1);
        january.EarMarkEvents.Single(e => e.FinanceId == 91).ExpectedAmount.ShouldBe(50m);

        // Once both land on the same day: ONE merged event, not two, summing
        // both amounts.
        var february = SnapshotOn(result, new DateOnly(2025, 2, 1));
        february.EarMarkEvents.Count(e => e.FinanceId == 91).ShouldBe(1);
        february.EarMarkEvents.Single(e => e.FinanceId == 91).ExpectedAmount.ShouldBe(80m);
    }

    // planning/17, item 22 (F31): "stop contributing, keep the jar alive" —
    // proves the actual forecast behavior, not just the successor's shape
    // (already covered by RestructureFactoryTests): no new inflow, but the
    // jar keeps draining on the goal's own schedule right through to the due
    // date, where the empty successor's own $0 occurrence lands too.
    [Fact]
    public void Stop_contributing_keeps_the_jar_alive_and_releasing_with_no_new_inflow()
    {
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 90,
            Source = "Boat fund",
            Amount = -100m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 6, 1),
                ActiveFrom = new DateOnly(2024, 6, 1), // saving started well before the due date
            }),
        });
        // Already fully funded via a lump sum before the window starts;
        // contributes nothing further itself even before being stopped.
        var predecessor = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 90,
                Amount = 0m,
                StartingAllocation = 600m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2024, 6, 1),
                    Until = new DateOnly(2024, 12, 1),
                }),
            },
            goal);

        var result = RestructureFactory.StopContributing(predecessor, goal, new DateOnly(2025, 1, 1));

        var forecast = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 10_000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 6, 1),
            financialPatterns: [goal],
            earMarkPatterns: [result.Predecessor, result.Successor]));

        // $600 to start (the as-of day is a no-delta day), then $100 off on
        // each of the goal's own occurrences — no new inflow ever raises it.
        Jar(SnapshotOn(forecast, new DateOnly(2025, 1, 1)), 90).ShouldBe(500m);
        Jar(SnapshotOn(forecast, new DateOnly(2025, 3, 1)), 90).ShouldBe(300m);
        // The due date, where the empty successor's own $0 occurrence lands too.
        Jar(SnapshotOn(forecast, new DateOnly(2025, 6, 1)), 90).ShouldBe(0m);
    }

    [Fact]
    public void A_manual_addition_raises_the_jar_on_its_day_and_persists_forward()
    {
        var (goal, earmark) = LiveGoal(1);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goal],
            earMarkPatterns: [earmark],
            manualEarmarks: [Manual(earmark, new DateOnly(2025, 6, 15), 300m)]));

        // Seed (Jan 1): 100 saved + Jan contribution = 200; by Jun 1: +500.
        Jar(SnapshotOn(result, new DateOnly(2025, 6, 1)), 1).ShouldBe(700m);

        var manualDay = SnapshotOn(result, new DateOnly(2025, 6, 15));
        Jar(manualDay, 1).ShouldBe(1000m);
        manualDay.ExpectedFreeAmount.ShouldBe(4000m); // 5000 − 1000

        // Persists: still there before the next scheduled contribution.
        Jar(SnapshotOn(result, new DateOnly(2025, 7, 1)), 1).ShouldBe(1100m);
    }

    [Fact]
    public void A_manual_earmark_on_or_before_the_asof_date_folds_into_the_seed()
    {
        var (goal, earmark) = LiveGoal(1);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 3, 15),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goal],
            earMarkPatterns: [earmark],
            manualEarmarks: [Manual(earmark, new DateOnly(2025, 2, 10), 300m)]));

        // Seed: 100 start + Jan/Feb/Mar contributions (300) + manual 300 = 700.
        Jar(SnapshotOn(result, new DateOnly(2025, 3, 15)), 1).ShouldBe(700m);
    }

    [Fact]
    public void A_manual_addition_never_moves_the_milestone()
    {
        var (goal, earmark) = LiveGoal(1);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goal],
            earMarkPatterns: [earmark],
            manualEarmarks: [Manual(earmark, new DateOnly(2025, 6, 15), 300m)]));

        // 3.13.5.4.a1: the milestone counts scheduled (repeated) contributions
        // only — Jan..Jun = 600. The manual catch-up closes the gap to it, it
        // does not redefine it.
        SnapshotOn(result, new DateOnly(2025, 6, 15)).FundJars.Single(j => j.FinanceId == 1)
            .MilestoneAmount.ShouldBe(600m);
    }

    [Fact]
    public void An_oversized_manual_withdrawal_floors_at_the_jar_and_is_reported()
    {
        var (goal, earmark) = LiveGoal(1);

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goal],
            earMarkPatterns: [earmark],
            manualEarmarks: [Manual(earmark, new DateOnly(2025, 6, 15), -5000m)]));

        // Jar held 700 on Jun 15 — the withdrawal delivers only that (no money
        // from nothing) and the shortfall is reported for the UI flag.
        var day = SnapshotOn(result, new DateOnly(2025, 6, 15));
        Jar(day, 1).ShouldBe(0m);
        day.ExpectedFreeAmount.ShouldBe(5000m); // free gained the 700 that existed, no more
        result.FlooredManualEarmarks.ShouldContain((new DateOnly(2025, 6, 15), 1));
    }

    [Fact]
    public void A_move_between_jars_is_two_manual_earmarks_and_leaves_free_unchanged()
    {
        var (goalA, earmarkA) = LiveGoal(1, "Boat");
        var (goalB, earmarkB) = LiveGoal(2, "Japan trip");

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goalA, goalB],
            earMarkPatterns: [earmarkA, earmarkB],
            manualEarmarks:
            [
                Manual(earmarkA, new DateOnly(2025, 6, 15), -200m),
                Manual(earmarkB, new DateOnly(2025, 6, 15), 200m),
            ]));

        var before = SnapshotOn(result, new DateOnly(2025, 6, 1));
        var moveDay = SnapshotOn(result, new DateOnly(2025, 6, 15));

        Jar(moveDay, 1).ShouldBe(Jar(before, 1) - 200m);
        Jar(moveDay, 2).ShouldBe(Jar(before, 2) + 200m);
        moveDay.ExpectedFreeAmount.ShouldBe(before.ExpectedFreeAmount); // net-zero on free
        result.FlooredManualEarmarks.ShouldBeEmpty();
    }

    [Fact]
    public void A_deallocation_day_give_back_merges_into_the_manual_earmark_keeping_the_users_amount()
    {
        var (goal, earmark) = LiveGoal(1);
        var expense = Discretionary(9, -4800m, new DateOnly(2025, 6, 15), "Emergency");

        var result = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 5000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 12, 31),
            financialPatterns: [goal, expense],
            earMarkPatterns: [earmark],
            manualEarmarks: [Manual(earmark, new DateOnly(2025, 6, 15), 50m)]));

        // The property under test is the MERGE (3.13c.8.4.a2): a give-back folds
        // into the user's own isolated event, and ExplicitAmount keeps their
        // number no matter how large the give-back is.
        //
        // Stage-1 revision: the $4,800 emergency has no Allocation Plan, so it
        // isn't reserved ahead — there are no deallocation days before Jun 15, so
        // the goal's $100 monthly contributions all land and its jar reaches $700
        // by June ($100 start + 6 months). On Jun 15 the emergency forces a
        // deallocation that must pull $500 from jars: the give-back is −550
        // against the user's +50, netting −500.
        var day = SnapshotOn(result, new DateOnly(2025, 6, 15));
        day.IsDeallocationDay.ShouldBeTrue();

        var manualEvent = day.EarMarkEvents.Single(e => e.FinanceId == 1 && !e.RepeatedEarmark);
        manualEvent.ExplicitAmount.ShouldBe(50m);   // the user's intent, preserved
        manualEvent.ExpectedAmount.ShouldBe(-500m); // explicit (+50) + implicit (−550)

        Jar(day, 1).ShouldBe(200m); // 700 + (−500)
        day.ExpectedFreeAmount.ShouldBe(0m);
    }

    // End to end: proves EarmarkConsolidation's own glut protection
    // (CurrentJar/GlutSurplus, 2026-08-15) survives a real save-then-rebuild,
    // not just Consolidate's own immediate return value. Same $100 goal /
    // $150 plan shape as the structural-glut test above (reuses its
    // already-verified $150-at-Mar-1 number), fed through consolidation and
    // replayed from scratch as if the old plan's row had really been deleted
    // and the new one saved in its place — exactly what
    // FinancePatternSaveConfirmation.ConsolidateSurvivingPlansIfNeeded does.
    [Fact]
    public void An_existing_glut_survives_consolidation_spent_down_evenly_instead_of_erased()
    {
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = _nextFinanceId++,
            Source = "Rent",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 4, 1),
            }),
            Amount = -100m,
        });
        var oldPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = -150m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 4, 1),
                }),
            },
            goal);

        // "Today" = Mar 1: the real jar already holds $150 more than its own
        // milestone calls for (a genuine, verified glut — HasGlut is true).
        var today = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 10_000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 4, 30),
            financialPatterns: [goal],
            earMarkPatterns: [oldPlan]));
        var currentJar = SnapshotOn(today, new DateOnly(2025, 3, 1)).FundJars.Single(j => j.FinanceId == goal.FinanceId);
        currentJar.HasGlut.ShouldBeTrue();
        currentJar.GlutSurplus.ShouldBe(150m);

        var consolidation = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = [oldPlan],
            ManualEarmarksForThisGoal = [],
            AllPatterns = [goal],
            CurrentJar = currentJar,
        });

        // $400 total need, minus the $150 glut = $250, spread across the
        // goal's own 4 monthly occurrences: $62.50 each — noticeably less
        // than the old plan's own $150/month, since part of what's still
        // owed is already covered. Without the fix this would be $100/month
        // (the old, unmodified B - A) and StartingAllocation would be $0.
        consolidation.ConsolidatedPlan.Amount.ShouldBe(-62.5m);
        consolidation.ConsolidatedPlan.StartingAllocation.ShouldBe(150m);

        // Old plan "deleted", new consolidated plan saved in its place —
        // replay from scratch, exactly as a real save would.
        var rebuilt = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 10_000m,
            asOfDate: new DateOnly(2025, 1, 1),
            horizonEndDate: new DateOnly(2025, 4, 30),
            financialPatterns: [goal],
            earMarkPatterns: [consolidation.ConsolidatedPlan]));

        // The $150 head start gets drawn down evenly to cover part of each
        // remaining release, landing at exactly $0 right on the goal's own
        // due date — spent down on schedule, not erased the instant the old
        // plan's row disappeared (which, unfixed, would have replayed flat
        // at $0 every single month instead: $100 in, $100 out, no memory of
        // the $150 that was ever there).
        Jar(SnapshotOn(rebuilt, new DateOnly(2025, 1, 1)), goal.FinanceId).ShouldBe(112.5m);
        Jar(SnapshotOn(rebuilt, new DateOnly(2025, 2, 1)), goal.FinanceId).ShouldBe(75m);
        Jar(SnapshotOn(rebuilt, new DateOnly(2025, 3, 1)), goal.FinanceId).ShouldBe(37.5m);
        Jar(SnapshotOn(rebuilt, new DateOnly(2025, 4, 1)), goal.FinanceId).ShouldBe(0m);
    }

    // EarmarkScaling's own glut check, 2026-08-15 — unlike EarmarkConsolidation
    // (above), Scale needed no code change: it never reads or discounts
    // "already banked" money at all, only multiplies the ongoing rate by the
    // same ratio the goal's own Amount changed by, leaving StartingAllocation
    // and the schedule untouched (EarmarkScalingTests already covers that
    // directly). This proves the CONSEQUENCE that reasoning predicts, with
    // real numbers rather than trusting the argument alone: the same $100/
    // $150 glutted shape from the structural-glut test above, scaled by the
    // same ratio (2x) the goal itself doubles by, produces a jar whose own
    // GlutSurplus scales by exactly that same ratio — proportionally
    // identical, not smoothed away or distorted by the scale.
    [Fact]
    public void Scaling_a_glutted_plan_preserves_its_glut_proportionally()
    {
        var unscaledGoal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = _nextFinanceId++,
            Source = "Rent",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 12, 31),
            }),
            Amount = -100m,
        });
        var unscaledPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = unscaledGoal.FinanceId,
                Amount = -150m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    DtStart = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 12, 31),
                }),
            },
            unscaledGoal);

        var scaledGoal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = _nextFinanceId++,
            Source = "Rent",
            DatePattern = unscaledGoal.DatePattern,
            Amount = -200m, // 2x
        });
        var scaledPlans = EarmarkScaling.Scale(new ScaleRequest
        {
            Goal = scaledGoal,
            PreviousGoalAmount = unscaledGoal.Amount,
            SurvivingPlans = [EarMarkPattern.Create(
                new EarMarkPatternOptions
                {
                    FinanceId = scaledGoal.FinanceId,
                    Amount = unscaledPlan.Amount,
                    DatePattern = unscaledPlan.DatePattern,
                },
                scaledGoal)],
        });
        scaledPlans.Single().Amount.ShouldBe(-300m); // -150 x 2, EarmarkScalingTests' own math, reused as this test's setup

        var unscaledResult = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 10_000m, asOfDate: new DateOnly(2025, 1, 1), horizonEndDate: new DateOnly(2025, 3, 31),
            financialPatterns: [unscaledGoal], earMarkPatterns: [unscaledPlan]));
        var scaledResult = TransactionLogBookFactory.CreateForecast(Options(
            startingBalance: 10_000m, asOfDate: new DateOnly(2025, 1, 1), horizonEndDate: new DateOnly(2025, 3, 31),
            financialPatterns: [scaledGoal], earMarkPatterns: scaledPlans));

        var unscaledJar = SnapshotOn(unscaledResult, new DateOnly(2025, 3, 1)).FundJars.Single(j => j.FinanceId == unscaledGoal.FinanceId);
        var scaledJar = SnapshotOn(scaledResult, new DateOnly(2025, 3, 1)).FundJars.Single(j => j.FinanceId == scaledGoal.FinanceId);

        unscaledJar.HasGlut.ShouldBeTrue();
        unscaledJar.GlutSurplus.ShouldBe(150m); // matches the structural-glut test above exactly
        scaledJar.HasGlut.ShouldBeTrue();
        scaledJar.GlutSurplus.ShouldBe(300m); // exactly 2x — proportionally identical, nothing lost or distorted
    }
}
