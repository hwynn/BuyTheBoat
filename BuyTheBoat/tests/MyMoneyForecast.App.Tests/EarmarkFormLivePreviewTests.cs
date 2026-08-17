using Microsoft.Data.Sqlite;
using MyMoneyForecast.Domain;
using MyMoneyForecast.Persistence;
using Shouldly;

namespace MyMoneyForecast.App.Tests;

// Checks a different consistency than FinancePatternSaveConfirmationTests
// does: not "did a save produce the right saved state" but "does the live,
// no-forecast-needed preview EarmarkFormPanel shows WHILE the user is still
// typing (before Save is even clicked) match what a real save, followed by a
// freshly rebuilt forecast, actually produces." EarmarkFormPanel's own
// private GetLiveJarAmounts/GetPatternsForLiveCheck can't be called directly
// from a test (WPF code-behind, no UI test harness in this project) — each
// test below reproduces their formula inline, calling the exact same public
// TransactionLogBookFactory.ComputeMilestoneTrajectory they call, so the
// only thing being hand-copied is the trivial composition around it, not the
// nontrivial math itself. Real (temporary) SQLite file per test, same shape
// as every other test file in this project — see PatternRepositoryTests' own
// header comment for why.
public class EarmarkFormLivePreviewTests : IDisposable
{
    private static readonly DateOnly AsOf = new(2025, 6, 15);

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"mymoneyforecast-test-{Guid.NewGuid()}.db");
    private readonly FinancialPatternRepository _financialPatterns;
    private readonly EarMarkPatternRepository _earMarkPatterns;
    private readonly ManualEarmarkRepository _manualEarmarks;

    public EarmarkFormLivePreviewTests()
    {
        var database = new PatternDatabase(_databasePath);
        _financialPatterns = new FinancialPatternRepository(database);
        _earMarkPatterns = new EarMarkPatternRepository(database, _financialPatterns);
        _manualEarmarks = new ManualEarmarkRepository(database, _earMarkPatterns);
    }

    // GetLiveJarAmounts' own header comment proves this algebraically: once
    // a goal has released at least once since ActiveStart, a reset-at-release
    // walk seeded from any starting balance is identical to the unseeded
    // walk from that release onward, so live ExpectedAmount == live
    // MilestoneAmount exactly. That proof is specifically about the SEED
    // (StartingAllocation) — it says nothing about a plan whose own rate has
    // ever diverged from what the goal needed (see the next test for that
    // case) — so this one deliberately keeps the plan's rate unchanged
    // throughout its whole history: the "form was just opened, nothing
    // edited yet" moment, the live preview's own true native domain. Turns
    // the written proof into a continuously-checked one — the live number a
    // user would see compared against an actual save plus a freshly rebuilt
    // real forecast, for the exact same day.
    [Fact]
    public void The_live_preview_matches_a_real_save_and_rebuilt_forecast_when_the_rate_never_changed()
    {
        var goal = Bill(1, "Kitchen Remodel", -100m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        _financialPatterns.Save(goal, accountId: 1);
        var saved = Plan(goal, -100m, goal.DatePattern.Start, goal.DatePattern.Until);
        _earMarkPatterns.Save(saved);

        // The user opens the Earmark form for this plan — RuleEditor/
        // AmountTextBox load pre-filled from the saved pattern, so the
        // "draft" EarmarkFormPanel would build right now is identical to
        // what's already saved, until they actually change something.
        var draft = Plan(goal, -100m, goal.DatePattern.Start, goal.DatePattern.Until);

        // EarmarkFormPanel.GetLiveJarAmounts, reproduced: single plan, no
        // concurrent funder, so GetPatternsForLiveCheck's own list is just
        // [draft]; startingTotal is 0 (no StartingAllocation, no manual
        // earmark dated exactly on ActiveStart).
        var liveTrajectory = TransactionLogBookFactory.ComputeMilestoneTrajectory(
            [draft], goal, draft.DatePattern.ActiveStart, AsOf);
        var liveMilestone = liveTrajectory.Count > 0 ? liveTrajectory[^1].MilestoneAmount : 0m;
        var hasReleased = goal.DatePattern.GetOccurrences(draft.DatePattern.ActiveStart, AsOf).Count > 0;
        hasReleased.ShouldBeTrue(); // confirms this scenario exercises the "true pace" branch the proof above is about
        var liveExpected = liveMilestone; // startingTotal (0) plays no role once hasReleased is true

        // Now actually save the draft — EarmarkFormPanel.SaveSavingsPlan's
        // own path for an existing plan: same (FinanceId, Start), an
        // in-place update, no FinancePatternSaveConfirmation involved (that
        // orchestrator is for editing the GOAL, not the plan funding it).
        _earMarkPatterns.Save(draft);

        var forecast = Forecast();
        var realJar = forecast.GetTimeline(1).Last(entry => entry.Date <= AsOf).Snapshot.FundJars.Single(jar => jar.FinanceId == 1);

        realJar.ExpectedAmount.ShouldBe(liveExpected);
        (realJar.MilestoneAmount ?? 0m).ShouldBe(liveMilestone);
    }

    // The flip side, and a deliberate, already-documented gap rather than a
    // new one: GetLiveJarAmounts' own header comment says outright it
    // "doesn't model manual earmarks beyond the starting point." Adding one
    // mid-plan (after ActiveStart, and deliberately off any release date so
    // same-day ordering can't muddy the read) proves that gap out with real
    // numbers instead of just asserting it in prose — the real,
    // saved-and-rebuilt jar reflects the top-up; the live preview a user
    // would have seen a moment before saving does not.
    [Fact]
    public void The_live_preview_does_not_reflect_a_mid_plan_manual_earmark_the_real_forecast_does()
    {
        var goal = Bill(1, "Kitchen Remodel", -100m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        _financialPatterns.Save(goal, accountId: 1);
        var plan = Plan(goal, -100m, goal.DatePattern.Start, goal.DatePattern.Until);
        _earMarkPatterns.Save(plan);
        _manualEarmarks.Save(ManualEarmark.Create(
            new ManualEarmarkOptions { FinanceId = 1, Date = new DateOnly(2025, 3, 15), Amount = 50m }, // a mid-plan top-up
            plan));

        var liveTrajectory = TransactionLogBookFactory.ComputeMilestoneTrajectory(
            [plan], goal, plan.DatePattern.ActiveStart, AsOf);
        var liveMilestone = liveTrajectory.Count > 0 ? liveTrajectory[^1].MilestoneAmount : 0m;
        goal.DatePattern.GetOccurrences(plan.DatePattern.ActiveStart, AsOf).Count.ShouldBeGreaterThan(0);
        var liveExpected = liveMilestone; // never references the manual earmark at all

        var forecast = Forecast();
        var realJar = forecast.GetTimeline(1).Last(entry => entry.Date <= AsOf).Snapshot.FundJars.Single(jar => jar.FinanceId == 1);

        // The $50 top-up is real money the user would see reflected the
        // instant they saved and reloaded — nothing in the live preview they
        // were looking at a moment before saving would have shown it.
        (realJar.ExpectedAmount - liveExpected).ShouldBe(50m);
    }

    // NOT asked for — found while building the two tests above, surfaced
    // rather than silently worked around. Bigger than the manual-earmark gap
    // above, in two ways:
    //
    // 1. GetLiveJarAmounts' own proof (see the first test's header comment)
    //    only covers the SEED — it says nothing about a plan whose own rate
    //    has ever diverged from what the goal needed. MilestoneAmount
    //    resets to 0 at every release BY DEFINITION (that's what "on pace
    //    this cycle" means), so it structurally cannot show a surplus/
    //    deficit accumulated across past cycles the way ExpectedAmount does
    //    — the live preview isn't just missing one input (like manual
    //    earmarks); it CAN'T represent this at all, even in principle.
    //
    // 2. Far more significant: EarmarkFormPanel.SaveSavingsPlan builds a
    //    plain EarMarkPattern (same FinanceId+Start as whatever's already
    //    saved) and this test saves it directly via _earMarkPatterns.Save,
    //    the same way MainWindow's own EarmarkForm.PatternSaved handler did
    //    until 2026-08-16. As of planning/27, that handler now goes through
    //    FinancePatternSaveConfirmation's own EarMarkPattern-editing
    //    constructor — but its two questions ("stay linked or break,"
    //    "cascade forward or not") only fire for a plan that's part of a
    //    chain (another EarMarkPattern sharing the same finance_id), and
    //    even then they're about the CHAIN's own neighbors, not a Item-B-G-
    //    style "this rewrites your own past" guard. For exactly the single,
    //    unchained plan this test builds, the save path is still, today,
    //    functionally identical to this test's own direct repository call —
    //    no FinancePatternSaveConfirmation question, no "this has history
    //    behind it" check of any kind, applies. Every mechanism this session
    //    (and planning/25 generally) built protects editing the GOAL
    //    (FinancialPattern) from silently rewriting the past. None of it
    //    applies to editing the SAVINGS PLAN (EarMarkPattern) funding that
    //    goal — changing its own Amount retroactively re-rates every past
    //    release the instant the forecast rebuilds, with no warning.
    //
    // Whether that's an intentional scope boundary (a plan's own
    // contribution rate is the user's lever to adjust freely) or an
    // unaddressed gap is a real, open design question — not this test's
    // call. Documents CURRENT behavior with real numbers so the question has
    // concrete evidence behind it: a plan funding a $100/month bill, edited
    // to $120/month with 6 months of real history behind it, silently
    // accumulates a $120 surplus ($20 x 6 releases) that neither the live
    // preview nor any confirmation step ever surfaced.
    [Fact]
    public void Editing_a_savings_plans_own_amount_retroactively_rerates_its_whole_history_with_no_protection()
    {
        var goal = Bill(1, "Kitchen Remodel", -100m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        _financialPatterns.Save(goal, accountId: 1);
        _earMarkPatterns.Save(Plan(goal, -100m, goal.DatePattern.Start, goal.DatePattern.Until));

        // The user opens the form and raises the amount from $100 to $120 —
        // AsOf (June 15) is well past 6 monthly releases (Jan-Jun 1st), so
        // this plan has real history behind it.
        var draft = Plan(goal, -120m, goal.DatePattern.Start, goal.DatePattern.Until);

        var liveTrajectory = TransactionLogBookFactory.ComputeMilestoneTrajectory(
            [draft], goal, draft.DatePattern.ActiveStart, AsOf);
        var liveMilestone = liveTrajectory.Count > 0 ? liveTrajectory[^1].MilestoneAmount : 0m;
        var liveExpected = liveMilestone; // GetLiveJarAmounts' own formula — see this test's own header comment for why this can't see the surplus below

        // EarmarkFormPanel.SaveSavingsPlan's real path, reproduced exactly:
        // a plain, unguarded save under the same (FinanceId, Start).
        _earMarkPatterns.Save(draft);

        var forecast = Forecast();
        var realJar = forecast.GetTimeline(1).Last(entry => entry.Date <= AsOf).Snapshot.FundJars.Single(jar => jar.FinanceId == 1);

        liveExpected.ShouldBe(0m); // the live preview shows nothing unusual...
        realJar.ExpectedAmount.ShouldBe(120m); // ...while $120 has actually, silently, accumulated
    }

    // ---- shared scenario-building helpers ----------------------------------

    private static FinancialPattern Bill(int financeId, string source, decimal amount, DateOnly start, DateOnly until) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = financeId,
            Source = source,
            Amount = amount,
            DatePattern = Monthly(start, until),
        });

    private static EarMarkPattern Plan(FinancialPattern goal, decimal amount, DateOnly start, DateOnly until) =>
        EarMarkPattern.Create(
            new EarMarkPatternOptions { FinanceId = goal.FinanceId, Amount = amount, DatePattern = Monthly(start, until) },
            goal);

    private static RecurrenceRule Monthly(DateOnly start, DateOnly until) => RecurrenceRule.Create(new RecurrenceRuleOptions
    {
        Frequency = RecurrenceFrequency.Monthly,
        ByMonthDay = [start.Day],
        Start = start,
        Until = until,
    });

    private ForecastResult Forecast() => TransactionLogBookFactory.CreateForecast(new ForecastOptions
    {
        FinancialPatterns = _financialPatterns.GetAll(),
        EarMarkPatterns = _earMarkPatterns.GetAll(),
        ManualEarmarks = _manualEarmarks.GetAll(),
        StartingBalance = 10_000m,
        AsOfDate = AsOf,
        HorizonEndDate = new DateOnly(2025, 12, 31),
    });

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
