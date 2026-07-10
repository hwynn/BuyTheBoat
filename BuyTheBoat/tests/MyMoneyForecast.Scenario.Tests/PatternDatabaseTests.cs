using Microsoft.Data.Sqlite;
using MyMoneyForecast.Persistence;
using Shouldly;

namespace MyMoneyForecast.Scenario.Tests;

// Covers the Import/Export support on PatternDatabase — file-level
// operations, not covered by PatternRepositoryTests' round-trip scenarios.
public class PatternDatabaseTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"mymoneyforecast-test-{Guid.NewGuid()}.db");

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

        var replacementPath = Path.Combine(Path.GetTempPath(), $"mymoneyforecast-test-{Guid.NewGuid()}.db");
        _ = new PatternDatabase(replacementPath);

        // Simulates Import: without releasing the pool first, Microsoft.Data.Sqlite
        // can still be holding the file open even though every SqliteConnection
        // that used it has already been disposed.
        PatternDatabase.ReleasePooledConnections();

        Should.NotThrow(() => File.Copy(replacementPath, _databasePath, overwrite: true));

        File.Delete(replacementPath);
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
