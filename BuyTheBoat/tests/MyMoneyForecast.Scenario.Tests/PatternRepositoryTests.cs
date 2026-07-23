using Microsoft.Data.Sqlite;
using MyMoneyForecast.Domain;
using MyMoneyForecast.Persistence;
using Shouldly;

namespace MyMoneyForecast.Scenario.Tests;

// Exercises Domain + Persistence together against a real (temporary) SQLite
// file — the one layer of the testing pyramid allowed to touch a database.
public class PatternRepositoryTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"mymoneyforecast-test-{Guid.NewGuid()}.db");
    private readonly FinancialPatternRepository _financialPatterns;
    private readonly EarMarkPatternRepository _earMarkPatterns;

    public PatternRepositoryTests()
    {
        var database = new PatternDatabase(_databasePath);
        _financialPatterns = new FinancialPatternRepository(database);
        _earMarkPatterns = new EarMarkPatternRepository(database, _financialPatterns);
    }

    [Fact]
    public void Financial_pattern_round_trips_through_sqlite()
    {
        var paycheck = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 291327,
            Source = "Mike's Office Inc",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                Start = new DateOnly(2019, 1, 2),
                ByMonthDay = [9, 25],
                Until = new DateOnly(2025, 1, 1),
            }),
            Amount = 300m,
            Description = "Biweekly paycheck",
        });

        _financialPatterns.Save(paycheck, accountId: 1);
        var all = _financialPatterns.GetAll();

        all.Count.ShouldBe(1);
        var loaded = all[0];
        loaded.FinanceId.ShouldBe(paycheck.FinanceId);
        loaded.Source.ShouldBe(paycheck.Source);
        loaded.Amount.ShouldBe(paycheck.Amount);
        loaded.Mandatory.ShouldBe(paycheck.Mandatory);
        loaded.Description.ShouldBe(paycheck.Description);
        loaded.DatePattern.GetOccurrences().ShouldBe(paycheck.DatePattern.GetOccurrences());
    }

    [Fact]
    public void Saving_a_financial_pattern_again_updates_it_instead_of_duplicating()
    {
        var original = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "City Power",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                Start = new DateOnly(2019, 1, 2),
                ByMonthDay = [11],
                Until = new DateOnly(2025, 1, 1),
            }),
            Amount = -70m,
        });
        _financialPatterns.Save(original, accountId: 1);

        var rateWentUp = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "City Power",
            DatePattern = original.DatePattern,
            Amount = -80m,
        });
        _financialPatterns.Save(rateWentUp, accountId: 1);

        var all = _financialPatterns.GetAll();
        all.Count.ShouldBe(1);
        all[0].Amount.ShouldBe(-80m);
    }

    [Fact]
    public void Earmark_pattern_round_trips_and_stays_linked_to_its_goal()
    {
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 33777,
            Source = "Boat Dealer",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                Start = new DateOnly(2019, 1, 2),
                ByMonthDay = [1],
                Until = new DateOnly(2022, 7, 1),
            }),
            Amount = -5000m,
        });
        _financialPatterns.Save(goal, accountId: 1);

        var earmark = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    Start = new DateOnly(2019, 6, 9),
                    ByMonthDay = [9, 25],
                    Until = new DateOnly(2019, 7, 20),
                }),
                Amount = -200m,
            },
            goal);
        _earMarkPatterns.Save(earmark);

        var all = _earMarkPatterns.GetAll();
        all.Count.ShouldBe(1);
        all[0].FinanceId.ShouldBe(goal.FinanceId);
        all[0].Amount.ShouldBe(-200m);
    }

    [Fact]
    public void Earmark_pattern_starting_allocation_round_trips_through_sqlite()
    {
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 33778,
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
        _financialPatterns.Save(goal, accountId: 1);

        var earmark = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    Start = new DateOnly(2025, 1, 1),
                    ByMonthDay = [1],
                    Until = new DateOnly(2029, 12, 1),
                }),
                Amount = -100m,
                StartingAllocation = 5000m,
            },
            goal);
        _earMarkPatterns.Save(earmark);

        var all = _earMarkPatterns.GetAll();
        all.Count.ShouldBe(1);
        all[0].StartingAllocation.ShouldBe(5000m);
    }

    [Fact]
    public void Deleting_a_financial_pattern_with_no_linked_earmark_pattern_succeeds()
    {
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 5,
            Source = "Streaming Service",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                Start = new DateOnly(2025, 1, 1),
                ByMonthDay = [1],
                Until = new DateOnly(2025, 12, 1),
            }),
            Amount = -15m,
        });
        _financialPatterns.Save(bill, accountId: 1);

        _financialPatterns.HasLinkedEarMarkPattern(bill.FinanceId).ShouldBeFalse();

        _financialPatterns.Delete(bill.FinanceId);

        _financialPatterns.GetAll().ShouldBeEmpty();
    }

    [Fact]
    public void Financial_pattern_with_a_linked_earmark_pattern_is_flagged_before_deleting()
    {
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 6,
            Source = "Boat Dealer",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                Start = new DateOnly(2025, 1, 1),
                Count = 1,
            }),
            Amount = -5000m,
        });
        _financialPatterns.Save(goal, accountId: 1);

        var earmark = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    Start = new DateOnly(2022, 1, 1),
                    ByMonthDay = [1],
                    Until = new DateOnly(2025, 1, 1),
                }),
                Amount = -138.89m,
            },
            goal);
        _earMarkPatterns.Save(earmark);

        _financialPatterns.HasLinkedEarMarkPattern(goal.FinanceId).ShouldBeTrue();

        _earMarkPatterns.Delete(earmark.FinanceId);
        _financialPatterns.HasLinkedEarMarkPattern(goal.FinanceId).ShouldBeFalse();
    }

    private static FinancialPattern Bill(int financeId, string source) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = financeId,
            Source = source,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                Start = new DateOnly(2025, 1, 1),
                ByMonthDay = [1],
                Until = new DateOnly(2027, 1, 1),
            }),
            Amount = -100m,
        });

    // The account a pattern is filed under is storage-only — these cover that
    // the filing round-trips and regroups into per-account lists, which is what
    // rebuilds each page's finance_patterns (planning/10 item 2-A).
    [Fact]
    public void Patterns_regroup_into_the_account_they_were_filed_under()
    {
        _financialPatterns.Save(Bill(1, "Rent"), accountId: 7);
        _financialPatterns.Save(Bill(2, "Electric"), accountId: 7);
        _financialPatterns.Save(Bill(3, "Boat fund"), accountId: 9);

        var byAccount = _financialPatterns.GetAllByAccount();

        byAccount.Keys.OrderBy(key => key).ToArray().ShouldBe(new[] { 7, 9 });
        byAccount[7].Select(pattern => pattern.Source).ToArray().ShouldBe(new[] { "Rent", "Electric" });
        byAccount[9].Single().Source.ShouldBe("Boat fund");
    }

    [Fact]
    public void The_filed_account_can_be_looked_up_and_re_filed_without_duplicating()
    {
        var bill = Bill(1, "Rent");
        _financialPatterns.Save(bill, accountId: 4);
        _financialPatterns.GetAccountId(1).ShouldBe(4);

        _financialPatterns.Save(bill, accountId: 5);

        _financialPatterns.GetAccountId(1).ShouldBe(5);
        _financialPatterns.GetAll().Count.ShouldBe(1);
    }

    [Fact]
    public void An_unknown_pattern_has_no_filed_account()
    {
        _financialPatterns.GetAccountId(404).ShouldBeNull();
    }

    [Fact]
    public void An_account_knows_whether_it_still_holds_patterns()
    {
        _financialPatterns.Save(Bill(1, "Rent"), accountId: 3);

        _financialPatterns.HasPatternsInAccount(3).ShouldBeTrue();
        _financialPatterns.HasPatternsInAccount(4).ShouldBeFalse();

        _financialPatterns.Delete(1);

        _financialPatterns.HasPatternsInAccount(3).ShouldBeFalse();
    }

    public void Dispose()
    {
        // Microsoft.Data.Sqlite pools connections by default, which keeps a
        // native file handle open past Dispose — fine for the real app, but
        // it means the temp file can't be deleted here without clearing the
        // pool first.
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
