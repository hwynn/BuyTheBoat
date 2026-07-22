using Microsoft.Data.Sqlite;

namespace MyMoneyForecast.Persistence;

// One connection-per-operation (not a long-lived shared connection) — this is
// a single-user desktop app talking to a local file, so simplicity wins over
// pooling cleverness. Lives under LocalAppData by default so data survives
// rebuilds and repeated `dotnet run`/F5 launches, not next to the binaries.
public sealed class PatternDatabase
{
    private readonly string _connectionString;

    public PatternDatabase(string? databasePath = null)
    {
        var path = databasePath ?? DefaultDatabasePath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        Initialize();
    }

    public static string DefaultDatabasePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MyMoneyForecast",
        "mymoneyforecast.db");

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    // Sanity check for Import: confirms the chosen file is actually a
    // MyMoneyForecast database (not some unrelated file the user picked)
    // before it gets copied over the live one.
    public static bool LooksLikeValidDatabaseFile(string path)
    {
        try
        {
            var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type = 'table' AND name IN ('FinancialPatterns', 'EarMarkPatterns');
                """;
            return Convert.ToInt64(command.ExecuteScalar()) == 2;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    // Microsoft.Data.Sqlite keeps pooled native handles to a file open even
    // after every SqliteConnection using it has been disposed. Import needs
    // to overwrite the live file on disk, so the pool must be released first.
    public static void ReleasePooledConnections() => SqliteConnection.ClearAllPools();

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS FinancialPatterns (
                FinanceId INTEGER PRIMARY KEY,
                Source TEXT NOT NULL,
                Amount TEXT NOT NULL,
                Priority INTEGER NOT NULL,
                Mandatory INTEGER NOT NULL,
                Description TEXT NULL,
                Frequency TEXT NOT NULL,
                IntervalValue INTEGER NOT NULL,
                ByDay TEXT NULL,
                ByMonthDay TEXT NULL,
                StartDate TEXT NOT NULL,
                UntilDate TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS EarMarkPatterns (
                FinanceId INTEGER PRIMARY KEY REFERENCES FinancialPatterns(FinanceId),
                Amount TEXT NOT NULL,
                Frequency TEXT NOT NULL,
                IntervalValue INTEGER NOT NULL,
                ByDay TEXT NULL,
                ByMonthDay TEXT NULL,
                StartDate TEXT NOT NULL,
                UntilDate TEXT NOT NULL,
                StartingAllocation TEXT NOT NULL DEFAULT '0'
            );

            CREATE TABLE IF NOT EXISTS CurrentBalance (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                Balance TEXT NOT NULL,
                AsOfDate TEXT NOT NULL,
                HorizonEndDate TEXT NULL,
                IdealSafetyCushion TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS ManualEarmarks (
                FinanceId INTEGER NOT NULL REFERENCES EarMarkPatterns(FinanceId),
                EarmarkDate TEXT NOT NULL,
                Amount TEXT NOT NULL,
                PRIMARY KEY (FinanceId, EarmarkDate)
            );

            CREATE TABLE IF NOT EXISTS Accounts (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL UNIQUE,
                Balance TEXT NOT NULL,
                IdealSafetyCushion TEXT NOT NULL DEFAULT '0'
            );
            """;
        command.ExecuteNonQuery();

        // First schema migration this project has needed. CREATE TABLE IF
        // NOT EXISTS only guards table creation — a database that already
        // had CurrentBalance from before HorizonEndDate existed won't pick up
        // the new column from the statement above.
        EnsureColumn(connection, "CurrentBalance", "HorizonEndDate", "TEXT NULL");
        EnsureColumn(connection, "CurrentBalance", "IdealSafetyCushion", "TEXT NULL");
        EnsureColumn(connection, "EarMarkPatterns", "StartingAllocation", "TEXT NOT NULL DEFAULT '0'");
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string columnDefinition)
    {
        var exists = false;
        using (var checkCommand = connection.CreateCommand())
        {
            checkCommand.CommandText = $"PRAGMA table_info({table});";
            using var reader = checkCommand.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(reader.GetOrdinal("name")), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (!exists)
        {
            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnDefinition};";
            alterCommand.ExecuteNonQuery();
        }
    }
}
