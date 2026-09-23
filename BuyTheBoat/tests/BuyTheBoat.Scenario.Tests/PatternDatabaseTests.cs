using Microsoft.Data.Sqlite;
using BuyTheBoat.Domain;
using BuyTheBoat.Persistence;
using Shouldly;

namespace BuyTheBoat.Scenario.Tests;

// Covers the Import/Export support on PatternDatabase — file-level
// operations, not covered by PatternRepositoryTests' round-trip scenarios.
public class PatternDatabaseTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"buytheboat-test-{Guid.NewGuid()}.db");

    [Fact]
    public void A_freshly_created_database_file_looks_valid()
    {
        _ = new PatternDatabase(_databasePath);

        PatternDatabase.LooksLikeValidDatabaseFile(_databasePath).ShouldBeTrue();
    }

    [Fact]
    public void An_unrelated_file_does_not_look_like_a_valid_database()
    {
        File.WriteAllText(_databasePath, "this is not a sqlite file");

        PatternDatabase.LooksLikeValidDatabaseFile(_databasePath).ShouldBeFalse();
    }

    [Fact]
    public void A_missing_file_does_not_look_like_a_valid_database()
    {
        PatternDatabase.LooksLikeValidDatabaseFile(_databasePath).ShouldBeFalse();
    }

    [Fact]
    public void Releasing_pooled_connections_allows_the_live_file_to_be_overwritten()
    {
        _ = new PatternDatabase(_databasePath);

        var replacementPath = Path.Combine(Path.GetTempPath(), $"buytheboat-test-{Guid.NewGuid()}.db");
        _ = new PatternDatabase(replacementPath);

        // Simulates Import: without releasing the pool first, Microsoft.Data.Sqlite
        // can still be holding the file open even though every SqliteConnection
        // that used it has already been disposed.
        PatternDatabase.ReleasePooledConnections();

        Should.NotThrow(() => File.Copy(replacementPath, _databasePath, overwrite: true));

        File.Delete(replacementPath);
    }

    [Fact]
    public void Replacing_the_database_file_deletes_the_destinations_leftover_journal_sidecar()
    {
        // A hot -journal left beside the live database by an earlier crash must not
        // survive an Import: SQLite would otherwise try to roll it back into the
        // freshly copied data on the next open. ReplaceDatabaseFile clears it.
        _ = new PatternDatabase(_databasePath);

        var replacementPath = Path.Combine(Path.GetTempPath(), $"buytheboat-test-{Guid.NewGuid()}.db");
        _ = new PatternDatabase(replacementPath);

        var staleJournal = _databasePath + "-journal";
        File.WriteAllText(staleJournal, "leftover journal contents");

        PatternDatabase.ReplaceDatabaseFile(replacementPath, _databasePath);

        File.Exists(staleJournal).ShouldBeFalse();
        PatternDatabase.LooksLikeValidDatabaseFile(_databasePath).ShouldBeTrue();

        File.Delete(replacementPath);
    }

    [Fact]
    public void A_freshly_created_database_reports_no_load_failure()
    {
        _ = new PatternDatabase(_databasePath);

        PatternDatabase.DescribeLoadFailure(_databasePath).ShouldBeNull();
    }

    [Fact]
    public void An_unrelated_file_reports_a_load_failure()
    {
        File.WriteAllText(_databasePath, "this is not a sqlite file");

        PatternDatabase.DescribeLoadFailure(_databasePath).ShouldNotBeNull();
    }

    [Fact]
    public void A_database_whose_plan_no_longer_covers_a_manual_earmark_reports_a_load_failure()
    {
        // A valid goal + plan + one-off, then the plan's span is shrunk out from under the one-off — the
        // exact "a row whose pattern has since shrunk its span" case the repositories re-validate against on
        // read, the kind of inconsistency a hand-edited or cross-version import could carry in.
        var database = new PatternDatabase(_databasePath);
        var financialPatterns = new FinancialPatternRepository(database);
        var earMarkPatterns = new EarMarkPatternRepository(database, financialPatterns);
        var manualEarmarks = new ManualEarmarkRepository(database, earMarkPatterns);

        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Japan trip",
            Amount = -3000m,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = new DateOnly(2026, 6, 1),
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
                    Until = new DateOnly(2025, 12, 1),
                }),
            },
            goal);
        financialPatterns.Save(goal, accountId: 1);
        earMarkPatterns.Save(plan);
        manualEarmarks.Save(ManualEarmark.Create(
            new ManualEarmarkOptions { FinanceId = 1, Date = new DateOnly(2025, 6, 15), Amount = 300m }, plan));

        // Shrink the plan so it ends in March — the June one-off now falls outside its span.
        using (var connection = new PatternDatabase(_databasePath).OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE EarMarkPatterns SET UntilDate = '2025-03-01' WHERE FinanceId = 1;";
            command.ExecuteNonQuery();
        }
        PatternDatabase.ReleasePooledConnections();

        PatternDatabase.DescribeLoadFailure(_databasePath).ShouldNotBeNull();
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
