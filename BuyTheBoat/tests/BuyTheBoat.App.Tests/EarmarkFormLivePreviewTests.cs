using Microsoft.Data.Sqlite;
using BuyTheBoat.Domain;
using BuyTheBoat.Persistence;
using Shouldly;

namespace BuyTheBoat.App.Tests;

// Checks that the no-forecast preview EarmarkFormPanel shows while the user is still typing matches what a real save and a rebuilt forecast produce.
// EarmarkFormPanel.GetLiveJarAmounts and GetPatternsForLiveCheck are private WPF code-behind with no UI test harness, so each test reproduces their short composition inline around the same public TransactionLogBookFactory walks they call.
// Real (temporary) SQLite file per test, same shape as every other test file in this project — see PatternRepositoryTests' own header comment for why.
public class EarmarkFormLivePreviewTests : IDisposable
{
    private static readonly DateOnly AsOf = new(2025, 6, 15);

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"buytheboat-test-{Guid.NewGuid()}.db");
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

    // The "form was just opened, nothing edited yet" moment: the live reading a user would see, against an actual save plus a freshly rebuilt forecast, for the same day.
    [Fact]
    public void The_live_preview_matches_a_real_save_and_rebuilt_forecast_when_the_rate_never_changed()
    {
        var goal = Bill(1, "Kitchen Remodel", -100m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        _financialPatterns.Save(goal, accountId: 1);
        var saved = Plan(goal, -100m, goal.DatePattern.ActiveStart, goal.DatePattern.Until);
        _earMarkPatterns.Save(saved);

        // The form loads pre-filled from the saved plan, so the draft it builds is identical until something changes.
        var draft = Plan(goal, -100m, goal.DatePattern.ActiveStart, goal.DatePattern.Until);
        var (liveExpected, liveMilestone) = LiveJarAmounts(draft, goal);

        // EarmarkFormPanel.SaveSavingsPlan's path for an existing plan: an in-place update under the same (FinanceId, Start).
        _earMarkPatterns.Save(draft);

        var realJar = RealJarAsOf(Forecast());
        realJar.ExpectedAmount.ShouldBe(liveExpected);
        (realJar.MilestoneAmount ?? 0m).ShouldBe(liveMilestone);
    }

    // A $50 top-up partway through the plan, off any release date so same-day ordering can't muddy the read.
    [Fact]
    public void The_live_preview_reflects_a_mid_plan_manual_earmark_the_real_forecast_does()
    {
        var goal = Bill(1, "Kitchen Remodel", -100m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        _financialPatterns.Save(goal, accountId: 1);
        var plan = Plan(goal, -100m, goal.DatePattern.ActiveStart, goal.DatePattern.Until);
        _earMarkPatterns.Save(plan);
        _manualEarmarks.Save(ManualEarmark.Create(
            new ManualEarmarkOptions { FinanceId = 1, Date = new DateOnly(2025, 3, 15), Amount = 50m },
            plan));

        var (liveExpected, _) = LiveJarAmounts(plan, goal);

        RealJarAsOf(Forecast()).ExpectedAmount.ShouldBe(liveExpected);
        liveExpected.ShouldBe(50m);
    }

    /// <summary>
    /// [READS FILE] EarmarkFormPanel.GetLiveJarAmounts for a single plan with no starting allocation, reproduced: the expected-balance walk with the goal's saved manual earmarks, and the milestone walk.
    /// The starting earmark on the plan's start is left out of the manual earmarks, as the form does, since it counts in the starting total (0 here).
    /// </summary>
    private (decimal ExpectedAmount, decimal MilestoneAmount) LiveJarAmounts(EarMarkPattern plan, FinancialPattern goal)
    {
        var activeStart = plan.DatePattern.ActiveStart;
        var laterManualEarmarks = _manualEarmarks.GetAll().Where(manual => manual.Date != activeStart).ToList();
        var expected = TransactionLogBookFactory.ComputeExpectedTrajectory(
            [plan], goal, activeStart, AsOf, startingAllocation: 0m, laterManualEarmarks)[^1].ExpectedAmount;
        var milestones = TransactionLogBookFactory.ComputeMilestoneTrajectory([plan], goal, activeStart, AsOf);
        return (expected, milestones.Count > 0 ? milestones[^1].MilestoneAmount : 0m);
    }

    /// <summary>[CALC] The goal's real jar on the as-of date, read from a rebuilt forecast.</summary>
    private static FundJar RealJarAsOf(ForecastResult forecast) =>
        forecast.GetTimeline(1).Last(entry => entry.Date <= AsOf).Snapshot.FundJars.Single(jar => jar.FinanceId == 1);

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
        DtStart = start,
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
