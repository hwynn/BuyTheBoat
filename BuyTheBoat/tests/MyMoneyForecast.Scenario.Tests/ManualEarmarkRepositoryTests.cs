using Microsoft.Data.Sqlite;
using MyMoneyForecast.Domain;
using MyMoneyForecast.Persistence;
using Shouldly;

namespace MyMoneyForecast.Scenario.Tests;

public class ManualEarmarkRepositoryTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"mymoneyforecast-test-{Guid.NewGuid()}.db");
    private readonly FinancialPatternRepository _financialPatterns;
    private readonly EarMarkPatternRepository _earMarkPatterns;
    private readonly ManualEarmarkRepository _manualEarmarks;

    private readonly EarMarkPattern _pattern;

    public ManualEarmarkRepositoryTests()
    {
        var database = new PatternDatabase(_databasePath);
        _financialPatterns = new FinancialPatternRepository(database);
        _earMarkPatterns = new EarMarkPatternRepository(database, _financialPatterns);
        _manualEarmarks = new ManualEarmarkRepository(database, _earMarkPatterns);

        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Japan trip",
            Amount = -3000m,
            Mandatory = false,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = new DateOnly(2026, 6, 1),
                Count = 1,
                ActiveFrom = new DateOnly(2025, 1, 1), // saving starts before the due date (planning/15)
            }),
        });
        _pattern = EarMarkPattern.Create(
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
            goal);

        _financialPatterns.Save(goal, accountId: 1);
        _earMarkPatterns.Save(_pattern);
    }

    private ManualEarmark Manual(DateOnly date, decimal amount) =>
        ManualEarmark.Create(new ManualEarmarkOptions { FinanceId = 1, Date = date, Amount = amount }, _pattern);

    [Fact]
    public void Saved_manual_earmarks_round_trip()
    {
        _manualEarmarks.Save(Manual(new DateOnly(2025, 6, 15), 300m));
        _manualEarmarks.Save(Manual(new DateOnly(2025, 8, 1), -50m));

        var all = _manualEarmarks.GetAll();
        all.Count.ShouldBe(2);
        all[0].Date.ShouldBe(new DateOnly(2025, 6, 15));
        all[0].Amount.ShouldBe(300m);
        all[1].Amount.ShouldBe(-50m);
    }

    [Fact]
    public void Saving_onto_an_occupied_day_replaces_that_days_amount()
    {
        // The documented one-isolated-earmark-per-jar-per-day rule, enforced
        // by the (FinanceId, EarmarkDate) primary key.
        _manualEarmarks.Save(Manual(new DateOnly(2025, 6, 15), 300m));
        _manualEarmarks.Save(Manual(new DateOnly(2025, 6, 15), 450m));

        var all = _manualEarmarks.GetAll();
        all.ShouldHaveSingleItem();
        all[0].Amount.ShouldBe(450m);
    }

    [Fact]
    public void Deleting_a_manual_earmark_removes_only_that_day()
    {
        _manualEarmarks.Save(Manual(new DateOnly(2025, 6, 15), 300m));
        _manualEarmarks.Save(Manual(new DateOnly(2025, 8, 1), 100m));

        _manualEarmarks.Delete(1, new DateOnly(2025, 6, 15));

        var all = _manualEarmarks.GetAll();
        all.ShouldHaveSingleItem();
        all[0].Date.ShouldBe(new DateOnly(2025, 8, 1));
    }

    [Fact]
    public void Deleting_the_earmark_pattern_deletes_its_manual_earmarks_too()
    {
        // The pattern's span is the jar's lifetime — its manual adjustments
        // die with it rather than becoming orphans.
        _manualEarmarks.Save(Manual(new DateOnly(2025, 6, 15), 300m));

        _earMarkPatterns.Delete(1);

        _manualEarmarks.GetAll().ShouldBeEmpty();
    }

    [Fact]
    public void Concurrent_plans_on_the_same_goal_dont_crash_GetAll_and_validate_against_the_right_one()
    {
        // More than one EarMarkPattern may share a finance id (a second
        // concurrent earmark pattern, or a break-off predecessor+successor).
        // GetAll() used to key its lookup dictionary by FinanceId alone,
        // throwing "same key already added" the moment two plans shared
        // one. Two non-overlapping segments here (like a real break-off)
        // prove the fix does more than dodge the crash — it validates each
        // earmark against whichever segment's own span actually covers its
        // date, not just whichever loaded first.
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 2,
            Source = "Kitchen renovation",
            Amount = -5000m,
            Mandatory = false,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = new DateOnly(2026, 1, 1),
                Count = 1,
                ActiveFrom = new DateOnly(2025, 1, 1),
            }),
        });
        _financialPatterns.Save(goal, accountId: 1);

        var earlySegment = EarMarkPattern.Create(new EarMarkPatternOptions
        {
            FinanceId = 2,
            Amount = -100m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2025, 6, 1),
            }),
        }, goal);
        var lateSegment = EarMarkPattern.Create(new EarMarkPatternOptions
        {
            FinanceId = 2,
            Amount = -150m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                DtStart = new DateOnly(2025, 7, 1),
                Until = new DateOnly(2025, 12, 1),
            }),
        }, goal);
        _earMarkPatterns.Save(earlySegment);
        _earMarkPatterns.Save(lateSegment);

        // Falls only inside the LATE segment's span — would fail the
        // pattern-span validation if GetAll() picked the early segment
        // instead of actually checking which one covers this date.
        _manualEarmarks.Save(ManualEarmark.Create(
            new ManualEarmarkOptions { FinanceId = 2, Date = new DateOnly(2025, 9, 1), Amount = 75m },
            lateSegment));

        var all = _manualEarmarks.GetAll();

        all.ShouldHaveSingleItem();
        all[0].Date.ShouldBe(new DateOnly(2025, 9, 1));
        all[0].Amount.ShouldBe(75m);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
