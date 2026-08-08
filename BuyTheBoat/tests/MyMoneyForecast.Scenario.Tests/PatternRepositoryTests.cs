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
        loaded.AutoRenew.ShouldBe(paycheck.AutoRenew);
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
                ActiveFrom = new DateOnly(2025, 1, 1), // saving starts before the due date (planning/15)
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

    // planning/17, item 8 (F27): more than one EarMarkPattern may now share a
    // finance_id (a "Restructure the plan" predecessor + successor) — keyed
    // on (FinanceId, StartDate), not FinanceId alone.
    [Fact]
    public void Two_earmark_patterns_can_share_a_finance_id_with_different_start_dates()
    {
        var goal = Bill(80, "Boat fund");
        _financialPatterns.Save(goal, accountId: 1);

        var predecessor = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 80,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    Start = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2025, 6, 1),
                }),
            },
            goal);
        var successor = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 80,
                Amount = -150m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    Start = new DateOnly(2025, 7, 1),
                    Until = new DateOnly(2027, 1, 1),
                }),
            },
            goal);
        _earMarkPatterns.Save(predecessor);
        _earMarkPatterns.Save(successor);

        var all = _earMarkPatterns.GetAll();
        all.Count.ShouldBe(2);
        all.Select(pattern => pattern.Amount).ShouldBe(new[] { -100m, -150m }); // ordered by StartDate
    }

    [Fact]
    public void Saving_an_earmark_pattern_again_with_the_same_start_date_updates_it_instead_of_duplicating()
    {
        var goal = Bill(81, "Rent fund");
        _financialPatterns.Save(goal, accountId: 1);

        var original = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 81,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    Start = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2027, 1, 1),
                }),
            },
            goal);
        _earMarkPatterns.Save(original);

        var edited = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 81,
                Amount = -125m, // same Start, just a different amount
                DatePattern = original.DatePattern,
            },
            goal);
        _earMarkPatterns.Save(edited);

        var all = _earMarkPatterns.GetAll();
        all.Count.ShouldBe(1);
        all[0].Amount.ShouldBe(-125m);
    }

    // Simulates a database created before this stage: EarMarkPatterns keyed
    // on FinanceId alone. Reopening must rebuild it onto the composite key
    // without losing the existing row.
    [Fact]
    public void An_existing_database_on_the_old_single_key_schema_is_migrated_without_losing_data()
    {
        // The goal must exist BEFORE the raw earmark row is inserted — FK
        // enforcement is active (Microsoft.Data.Sqlite defaults PRAGMA
        // foreign_keys to ON), so an orphaned row would fail to insert here
        // exactly as it would in a real database.
        _financialPatterns.Save(Bill(82, "Migrated goal"), accountId: 1);
        RecreateEarMarkPatternsOnTheOldSingleKeySchema();
        InsertRawEarMarkPatternRow(
            financeId: 82, amount: "-100", start: "2025-01-01", until: "2027-01-01");

        // Reopening runs Initialize again, which must detect the old schema
        // and migrate it.
        var reopened = new FinancialPatternRepository(new PatternDatabase(_databasePath));
        var reopenedEarmarks = new EarMarkPatternRepository(new PatternDatabase(_databasePath), reopened);

        var all = reopenedEarmarks.GetAll();

        all.ShouldHaveSingleItem();
        all[0].Amount.ShouldBe(-100m);
    }

    // A real database was found with ManualEarmarks still referencing
    // "EarMarkPatterns_old_singlekey" — the transient table the single-key
    // migration above renames the old EarMarkPatterns to before dropping it
    // — left over from an earlier, incomplete migration run on that
    // database. Reopening must detect that ManualEarmarks isn't correctly
    // referencing FinancialPatterns (not just check for that one specific
    // stale name) and repoint it, so it self-heals from any stale target,
    // not only the one this was first written against.
    [Fact]
    public void A_manual_earmarks_table_referencing_a_stale_leftover_table_is_repointed_at_financial_patterns()
    {
        var goal = Bill(91, "Stale-reference goal");
        _financialPatterns.Save(goal, accountId: 1);
        _earMarkPatterns.Save(EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 91,
                Amount = -50m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    Start = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2027, 1, 1),
                }),
            },
            goal));
        RecreateManualEarmarksReferencingAStaleLeftoverTable();
        InsertRawManualEarmarkRowThenDropTheStaleTable(financeId: 91, date: "2025-06-01", amount: "50");

        // Reopening must not throw ("foreign key mismatch") and must read
        // the earmark back correctly.
        var reopenedFinancialPatterns = new FinancialPatternRepository(new PatternDatabase(_databasePath));
        var reopenedEarMarkPatterns = new EarMarkPatternRepository(new PatternDatabase(_databasePath), reopenedFinancialPatterns);
        var reopenedManualEarmarks = new ManualEarmarkRepository(new PatternDatabase(_databasePath), reopenedEarMarkPatterns);

        var all = reopenedManualEarmarks.GetAll();

        all.ShouldHaveSingleItem();
        all[0].Amount.ShouldBe(50m);

        // A plain SELECT tolerates a dangling FK reference — it's a WRITE
        // that actually trips "foreign key mismatch". Saving a second entry
        // proves the table was really migrated, not just readable by luck.
        reopenedManualEarmarks.Save(ManualEarmark.Create(
            new ManualEarmarkOptions { FinanceId = 91, Date = new DateOnly(2025, 7, 1), Amount = 25m },
            reopenedEarMarkPatterns.GetAll()[0]));
        reopenedManualEarmarks.GetAll().Count.ShouldBe(2);
    }

    private void RecreateManualEarmarksReferencingAStaleLeftoverTable()
    {
        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _databasePath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        // The leftover table exists just long enough for the row insert
        // below to pass FK validation, then gets dropped — matching the real
        // sequence (rename, drop) that leaves ManualEarmarks referencing a
        // table that's actually gone by the time this database reopens.
        command.CommandText = """
            CREATE TABLE EarMarkPatterns_old_singlekey (FinanceId INTEGER PRIMARY KEY);
            INSERT INTO EarMarkPatterns_old_singlekey (FinanceId) VALUES (91);

            DROP TABLE ManualEarmarks;
            CREATE TABLE ManualEarmarks (
                FinanceId INTEGER NOT NULL REFERENCES EarMarkPatterns_old_singlekey(FinanceId),
                EarmarkDate TEXT NOT NULL,
                Amount TEXT NOT NULL,
                PRIMARY KEY (FinanceId, EarmarkDate)
            );
            """;
        command.ExecuteNonQuery();
    }

    private void InsertRawManualEarmarkRowThenDropTheStaleTable(int financeId, string date, string amount)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _databasePath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ManualEarmarks (FinanceId, EarmarkDate, Amount)
            VALUES ($FinanceId, $EarmarkDate, $Amount);
            """;
        command.Parameters.AddWithValue("$FinanceId", financeId);
        command.Parameters.AddWithValue("$EarmarkDate", date);
        command.Parameters.AddWithValue("$Amount", amount);
        command.ExecuteNonQuery();

        // FK enforcement blocks dropping a table a live FK still points at —
        // switched off just for this drop to reach the end state a crashed
        // real migration could actually leave behind.
        using var pragmaOff = connection.CreateCommand();
        pragmaOff.CommandText = "PRAGMA foreign_keys = OFF;";
        pragmaOff.ExecuteNonQuery();

        using var drop = connection.CreateCommand();
        drop.CommandText = "DROP TABLE EarMarkPatterns_old_singlekey;";
        drop.ExecuteNonQuery();

        using var pragmaOn = connection.CreateCommand();
        pragmaOn.CommandText = "PRAGMA foreign_keys = ON;";
        pragmaOn.ExecuteNonQuery();
    }

    private void RecreateEarMarkPatternsOnTheOldSingleKeySchema()
    {
        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _databasePath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DROP TABLE EarMarkPatterns;
            CREATE TABLE EarMarkPatterns (
                FinanceId INTEGER PRIMARY KEY,
                Amount TEXT NOT NULL,
                Frequency TEXT NOT NULL,
                IntervalValue INTEGER NOT NULL,
                ByDay TEXT NULL,
                ByMonthDay TEXT NULL,
                StartDate TEXT NOT NULL,
                UntilDate TEXT NOT NULL,
                ActiveFrom TEXT NULL,
                StartingAllocation TEXT NOT NULL DEFAULT '0'
            );
            """;
        command.ExecuteNonQuery();
    }

    private void InsertRawEarMarkPatternRow(int financeId, string amount, string start, string until)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _databasePath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO EarMarkPatterns (FinanceId, Amount, Frequency, IntervalValue, StartDate, UntilDate)
            VALUES ($FinanceId, $Amount, 'Monthly', 1, $Start, $Until);
            """;
        command.Parameters.AddWithValue("$FinanceId", financeId);
        command.Parameters.AddWithValue("$Amount", amount);
        command.Parameters.AddWithValue("$Start", start);
        command.Parameters.AddWithValue("$Until", until);
        command.ExecuteNonQuery();
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
                ActiveFrom = new DateOnly(2022, 1, 1), // saving starts before the due date (planning/15)
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

    [Fact]
    public void Financial_pattern_active_from_round_trips_through_sqlite()
    {
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 42,
            Source = "Trip to Japan",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                Start = new DateOnly(2027, 1, 1),
                Count = 1,
                ActiveFrom = new DateOnly(2025, 6, 1),
            }),
            Amount = -3000m,
            Mandatory = false,
        });
        _financialPatterns.Save(goal, accountId: 1);

        _financialPatterns.GetAll().Single().DatePattern.ActiveFrom.ShouldBe(new DateOnly(2025, 6, 1));
    }

    [Fact]
    public void A_pattern_with_no_active_from_round_trips_as_null()
    {
        _financialPatterns.Save(Bill(1, "Rent"), accountId: 1);

        _financialPatterns.GetAll().Single().DatePattern.ActiveFrom.ShouldBeNull();
    }

    [Fact]
    public void A_pattern_with_auto_renew_set_round_trips_through_sqlite()
    {
        var rent = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Rent",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                Start = new DateOnly(2025, 1, 1),
                Until = new DateOnly(2026, 1, 1),
            }),
            Amount = -1_600m,
            Mandatory = true,
            AutoRenew = true,
        });
        _financialPatterns.Save(rent, accountId: 1);

        _financialPatterns.GetAll().Single().AutoRenew.ShouldBeTrue();
    }

    [Fact]
    public void A_pattern_with_no_auto_renew_round_trips_as_false()
    {
        _financialPatterns.Save(Bill(1, "Rent"), accountId: 1);

        _financialPatterns.GetAll().Single().AutoRenew.ShouldBeFalse();
    }

    [Fact]
    public void An_existing_goal_whose_plan_predates_it_gets_active_from_backfilled_on_load()
    {
        // Simulates pre-ActiveFrom data: a goal plus a save-in-advance plan that
        // starts before it. Save them (valid — the goal has ActiveFrom), null the
        // goal's ActiveFrom to mimic an old database, then re-open. The migration
        // backfills ActiveFrom = the plan's Start, so the restored check passes on
        // load (GetAll re-validates and would otherwise throw).
        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 55,
            Source = "Boat",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                Start = new DateOnly(2027, 1, 1),
                Count = 1,
                ActiveFrom = new DateOnly(2025, 1, 1),
            }),
            Amount = -3000m,
            Mandatory = false,
        });
        _financialPatterns.Save(goal, accountId: 1);
        _earMarkPatterns.Save(EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 55,
                Amount = -100m,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [1],
                    Start = new DateOnly(2025, 1, 1),
                    Until = new DateOnly(2026, 12, 1),
                }),
            },
            goal));

        StripActiveFrom(financeId: 55);

        // Re-open: Initialize runs the backfill. A clean GetAll proves the goal
        // regained an ActiveFrom that satisfies the earmark's containment check.
        var reopened = new FinancialPatternRepository(new PatternDatabase(_databasePath));
        reopened.GetAll().Single().DatePattern.ActiveFrom.ShouldBe(new DateOnly(2025, 1, 1));
    }

    private void StripActiveFrom(int financeId)
    {
        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _databasePath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE FinancialPatterns SET ActiveFrom = NULL WHERE FinanceId = $FinanceId;";
        command.Parameters.AddWithValue("$FinanceId", financeId);
        command.ExecuteNonQuery();
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

    // planning/17, F33: the Allocation Plan proposer's income scan must be
    // scoped to the outflow's own account, not household-wide.
    [Fact]
    public void Excluding_transfer_patterns_by_account_only_returns_that_accounts_non_transfer_patterns()
    {
        _financialPatterns.Save(Bill(1, "Rent"), accountId: 1);
        _financialPatterns.Save(Bill(2, "Paycheck"), accountId: 2); // a different account
        _financialPatterns.Save(Bill(3, "Transfer leg"), accountId: 1, transferId: 9);

        var forAccountOne = _financialPatterns.GetByAccountExcludingTransferPatterns(1);

        forAccountOne.Select(pattern => pattern.Source).ShouldBe(new[] { "Rent" });
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
