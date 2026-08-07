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
                UntilDate TEXT NOT NULL,
                ActiveFrom TEXT NULL,
                AutoRenew INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS EarMarkPatterns (
                FinanceId INTEGER NOT NULL REFERENCES FinancialPatterns(FinanceId),
                Amount TEXT NOT NULL,
                Frequency TEXT NOT NULL,
                IntervalValue INTEGER NOT NULL,
                ByDay TEXT NULL,
                ByMonthDay TEXT NULL,
                StartDate TEXT NOT NULL,
                UntilDate TEXT NOT NULL,
                ActiveFrom TEXT NULL,
                StartingAllocation TEXT NOT NULL DEFAULT '0',
                PRIMARY KEY (FinanceId, StartDate)
            );

            CREATE TABLE IF NOT EXISTS CurrentBalance (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                Balance TEXT NOT NULL,
                AsOfDate TEXT NOT NULL,
                HorizonEndDate TEXT NULL,
                IdealSafetyCushion TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS ManualEarmarks (
                FinanceId INTEGER NOT NULL REFERENCES FinancialPatterns(FinanceId),
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

            CREATE TABLE IF NOT EXISTS Transfers (
                Id INTEGER PRIMARY KEY,
                FromAccountId INTEGER NOT NULL,
                ToAccountId INTEGER NOT NULL,
                Amount TEXT NOT NULL,
                Frequency TEXT NOT NULL,
                IntervalValue INTEGER NOT NULL,
                ByDay TEXT NULL,
                ByMonthDay TEXT NULL,
                StartDate TEXT NOT NULL,
                UntilDate TEXT NOT NULL,
                ActiveFrom TEXT NULL
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

        // Which account each pattern is FILED UNDER. This is storage only — the
        // domain FinancialPattern has no account property (see planning/10 item
        // 2-A); a pattern belongs to an account by living in that account's
        // page. Pages are never persisted, so this column is the only place
        // that containment can be recorded and rebuilt from on load. The
        // DEFAULT 1 *is* the migration: every pre-existing pattern files under
        // the seeded "primary" account.
        EnsureColumn(connection, "FinancialPatterns", "AccountId", "INTEGER NOT NULL DEFAULT 1");

        // Which transfer a pattern is a pattern of, if any (planning/10 item 3).
        // NULL for ordinary user-created patterns; set for a transfer's two
        // patterns, which are hidden from the pattern list and shown as one transfer
        // instead. The engine still reads every pattern, patterns included.
        EnsureColumn(connection, "FinancialPatterns", "TransferId", "INTEGER NULL");

        // The ActiveFrom lead-in (planning/15): a nullable date on a pattern's
        // rrule, earlier than its first occurrence, so a jar can exist before the
        // pattern's occurrences begin. NULL for every pre-existing pattern — the
        // migration is simply the absence of a value (no lead-in).
        EnsureColumn(connection, "FinancialPatterns", "ActiveFrom", "TEXT NULL");
        EnsureColumn(connection, "EarMarkPatterns", "ActiveFrom", "TEXT NULL");
        EnsureColumn(connection, "Transfers", "ActiveFrom", "TEXT NULL");

        // The AutoRenew marker (planning/18, B12): set invisibly when the user
        // answers "it just keeps going" at creation. DEFAULT 0 *is* the
        // migration — every pre-existing pattern was created before this
        // question existed, so none of them opted in.
        EnsureColumn(connection, "FinancialPatterns", "AutoRenew", "INTEGER NOT NULL DEFAULT 0");

        // planning/17, item 8 (F27/F29): more than one EarMarkPattern may now
        // share a finance_id, so FinanceId alone can no longer be the table's
        // key. Run after the EnsureColumn calls above so a pre-existing table
        // already has every column before it's copied across.
        EnsureEarMarkPatternsAllowMultiplePerFinanceId(connection);

        // A manual earmark is about the GOAL (the jar), not any one plan
        // segment, so it always should have referenced FinancialPatterns —
        // but now it MUST: SQLite rejects any statement touching a table
        // declaring REFERENCES EarMarkPatterns(FinanceId) once FinanceId
        // alone is no longer that table's key ("foreign key mismatch",
        // checked at prepare time regardless of PRAGMA foreign_keys).
        EnsureManualEarmarksReferenceFinancialPatterns(connection);

        // One-time backfill for the restored 3.11.2.a2 front-half check
        // (planning/15): a pre-existing goal whose savings plan starts before the
        // goal's own Start had no ActiveFrom, which the restored check rejects on
        // load. Give each such goal an ActiveFrom equal to its plan's Start —
        // matching what creation now sets. Dates are stored as yyyy-MM-dd TEXT, so
        // the string comparison sorts chronologically. Idempotent: only touches
        // rows still NULL, so re-running does nothing.
        using var backfill = connection.CreateCommand();
        backfill.CommandText = """
            UPDATE FinancialPatterns
            SET ActiveFrom = (
                SELECT e.StartDate FROM EarMarkPatterns e
                WHERE e.FinanceId = FinancialPatterns.FinanceId)
            WHERE ActiveFrom IS NULL
              AND EXISTS (
                SELECT 1 FROM EarMarkPatterns e
                WHERE e.FinanceId = FinancialPatterns.FinanceId
                  AND e.StartDate < FinancialPatterns.StartDate);
            """;
        backfill.ExecuteNonQuery();
    }

    // SQLite can't ALTER a primary key in place, so a database still on the
    // old single-column key (FinanceId alone) is rebuilt: renamed aside,
    // recreated with the composite key the CREATE TABLE statement above now
    // declares, data copied across, old copy dropped. Checked via
    // PRAGMA table_info rather than a version flag, so this is a no-op both
    // on a fresh install (already created with the composite key) and on a
    // database already migrated by an earlier run.
    private static void EnsureEarMarkPatternsAllowMultiplePerFinanceId(SqliteConnection connection)
    {
        var startDateIsPartOfPrimaryKey = false;
        using (var checkCommand = connection.CreateCommand())
        {
            checkCommand.CommandText = "PRAGMA table_info(EarMarkPatterns);";
            using var reader = checkCommand.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(reader.GetOrdinal("name")), "StartDate", StringComparison.OrdinalIgnoreCase)
                    && reader.GetInt32(reader.GetOrdinal("pk")) > 0)
                {
                    startDateIsPartOfPrimaryKey = true;
                    break;
                }
            }
        }

        if (startDateIsPartOfPrimaryKey)
        {
            return;
        }

        using var migrate = connection.CreateCommand();
        migrate.CommandText = """
            ALTER TABLE EarMarkPatterns RENAME TO EarMarkPatterns_old_singlekey;

            CREATE TABLE EarMarkPatterns (
                FinanceId INTEGER NOT NULL REFERENCES FinancialPatterns(FinanceId),
                Amount TEXT NOT NULL,
                Frequency TEXT NOT NULL,
                IntervalValue INTEGER NOT NULL,
                ByDay TEXT NULL,
                ByMonthDay TEXT NULL,
                StartDate TEXT NOT NULL,
                UntilDate TEXT NOT NULL,
                ActiveFrom TEXT NULL,
                StartingAllocation TEXT NOT NULL DEFAULT '0',
                PRIMARY KEY (FinanceId, StartDate)
            );

            INSERT INTO EarMarkPatterns
                (FinanceId, Amount, Frequency, IntervalValue, ByDay, ByMonthDay, StartDate, UntilDate, ActiveFrom, StartingAllocation)
            SELECT FinanceId, Amount, Frequency, IntervalValue, ByDay, ByMonthDay, StartDate, UntilDate, ActiveFrom, StartingAllocation
            FROM EarMarkPatterns_old_singlekey;

            DROP TABLE EarMarkPatterns_old_singlekey;
            """;
        migrate.ExecuteNonQuery();
    }

    // Checked via PRAGMA foreign_key_list rather than a version flag, so this
    // is a no-op both on a fresh install (already created referencing
    // FinancialPatterns) and on a database already migrated by an earlier run.
    //
    // BUG FOUND AND FIXED 2026-08-05: this used to check for a reference to
    // the literal name "EarMarkPatterns" specifically, on the assumption that
    // was the only stale value a pre-migration database could have. A real
    // database was found still referencing "EarMarkPatterns_old_singlekey" —
    // the transient rename-target EnsureEarMarkPatternsAllowMultiplePerFinanceId
    // uses below — left over from some earlier, incomplete migration
    // sequence, and dropped by the time that migration finished, so every
    // later Initialize() saw a dangling reference this check never caught
    // (SQLite rejects any statement touching ManualEarmarks once its
    // referenced table doesn't exist, "checked at prepare time regardless of
    // PRAGMA foreign_keys" — same class of error the comment below already
    // describes, just from a second stale name nobody had hit yet). Inverted
    // to check for the one thing that actually matters — does it already
    // correctly reference FinancialPatterns — so it self-heals from *any*
    // stale target, not just the specific one this was first written against.
    private static void EnsureManualEarmarksReferenceFinancialPatterns(SqliteConnection connection)
    {
        var alreadyReferencesFinancialPatterns = false;
        using (var checkCommand = connection.CreateCommand())
        {
            checkCommand.CommandText = "PRAGMA foreign_key_list(ManualEarmarks);";
            using var reader = checkCommand.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(reader.GetOrdinal("table")), "FinancialPatterns", StringComparison.OrdinalIgnoreCase))
                {
                    alreadyReferencesFinancialPatterns = true;
                    break;
                }
            }
        }

        if (alreadyReferencesFinancialPatterns)
        {
            return;
        }

        using var migrate = connection.CreateCommand();
        migrate.CommandText = """
            ALTER TABLE ManualEarmarks RENAME TO ManualEarmarks_old_reference;

            CREATE TABLE ManualEarmarks (
                FinanceId INTEGER NOT NULL REFERENCES FinancialPatterns(FinanceId),
                EarmarkDate TEXT NOT NULL,
                Amount TEXT NOT NULL,
                PRIMARY KEY (FinanceId, EarmarkDate)
            );

            INSERT INTO ManualEarmarks (FinanceId, EarmarkDate, Amount)
            SELECT FinanceId, EarmarkDate, Amount FROM ManualEarmarks_old_reference;

            DROP TABLE ManualEarmarks_old_reference;
            """;
        migrate.ExecuteNonQuery();
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
