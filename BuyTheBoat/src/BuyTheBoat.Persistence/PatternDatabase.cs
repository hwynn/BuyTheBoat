using Microsoft.Data.Sqlite;

namespace BuyTheBoat.Persistence;

// One connection-per-operation (not a long-lived shared connection) — this is
// a single-user desktop app talking to a local file, so simplicity wins over
// pooling cleverness. Lives under LocalAppData by default so data survives
// rebuilds and repeated `dotnet run`/F5 launches, not next to the binaries.
public sealed class PatternDatabase
{
    private readonly string _connectionString;

    /// <summary>[WRITES FILE] Opens (creating if needed) the SQLite database at the given path, or the default location, and runs any pending schema migrations.</summary>
    /// <param name="databasePath">Path to the database file; defaults to the app's standard LocalAppData location.</param>
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

    /// <summary>[CALC] Returns the live database file's path — under LocalAppData normally, or inside the portable demo's own folder when this is that demo. Delegates to AppPaths, which owns the whole where-do-files-go decision.</summary>
    public static string DefaultDatabasePath() => AppPaths.DatabasePath;

    /// <summary>[READS FILE] Opens a new connection to this database. One connection per operation — callers dispose it when done.</summary>
    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>[READS FILE] Checks whether a file is actually a BuyTheBoat database. Used before Import overwrites the live database with it, so the user doesn't accidentally wipe their data with an unrelated file.</summary>
    /// <param name="path">The file to check.</param>
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

    /// <summary>[CALC] Releases every pooled native SQLite connection handle. Called before Import overwrites the live database file — Microsoft.Data.Sqlite keeps pooled handles open even after every SqliteConnection using them has been disposed, which would otherwise block the overwrite.</summary>
    public static void ReleasePooledConnections() => SqliteConnection.ClearAllPools();

    /// <summary>[WRITES FILE] Overwrites one database file with another, first releasing pooled handles and deleting the destination's leftover -wal/-shm/-journal scratch files. Without that cleanup, a stale journal from the old data (left by an earlier crash) could be rolled back into the freshly copied file the next time it opens, corrupting it — the reason the SeedData tool clears the same scratch files when it resets the database. Used by Import to swap in the selected file.</summary>
    /// <param name="sourcePath">The database file to copy in.</param>
    /// <param name="destinationPath">The live database file to overwrite.</param>
    public static void ReplaceDatabaseFile(string sourcePath, string destinationPath)
    {
        ReleasePooledConnections();

        foreach (var sidecar in new[] { destinationPath + "-wal", destinationPath + "-shm", destinationPath + "-journal" })
        {
            if (File.Exists(sidecar))
            {
                File.Delete(sidecar);
            }
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    /// <summary>[READS FILE] Whether the app could actually LOAD this database file — a deeper check than LooksLikeValidDatabaseFile's table-presence one. Opens a throwaway copy, runs the same schema migrations startup would, then reads every repository (each re-validates its rows on the way out, the way the live app does). Catches an old-version or subtly-inconsistent export that passes the lighter check but would then crash the app on its first forecast after import — an orphaned earmark, a row outside its plan's span, and the like. Returns a short reason when it can't load, or null when it loads cleanly. Never throws: a failure to check is itself reported as "can't load."</summary>
    /// <param name="path">The database file to check.</param>
    public static string? DescribeLoadFailure(string path)
    {
        var checkCopyPath = Path.Combine(Path.GetTempPath(), $"buytheboat-import-check-{Guid.NewGuid():N}.db");
        try
        {
            File.Copy(path, checkCopyPath, overwrite: true);

            var database = new PatternDatabase(checkCopyPath); // runs migrations, exactly as startup does
            var financialPatterns = new FinancialPatternRepository(database);
            var earMarkPatterns = new EarMarkPatternRepository(database, financialPatterns);

            // Reading each is what exercises the corruption guards — a plan with no pattern, an earmark
            // with no plan, a row outside its plan's span — the exact throws that would otherwise crash
            // the app on load. Discarded; this is only about whether they read without throwing.
            _ = financialPatterns.GetAll();
            _ = earMarkPatterns.GetAll();
            _ = new ManualEarmarkRepository(database, earMarkPatterns).GetAll();
            _ = new AccountRepository(database).GetAll();
            _ = new TransferRepository(database, financialPatterns).GetAll();
            return null;
        }
        catch (Exception error)
        {
            return error.Message;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(checkCopyPath))
                {
                    File.Delete(checkCopyPath);
                }
            }
            catch
            {
                // Best-effort cleanup of a temp file — never the reason a check "fails".
            }
        }
    }

    /// <summary>[WRITES FILE] Creates every table this app needs if they don't exist yet, then runs each schema migration in order. Idempotent — safe to run on every startup, on a fresh database or one already migrated by an earlier run.</summary>
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
                AutoRenew INTEGER NOT NULL DEFAULT 0,
                ExcludedDates TEXT NULL
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
                ExcludedDates TEXT NULL,
                ExplicitlyCreated INTEGER NOT NULL DEFAULT 1,
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
                ActiveFrom TEXT NULL,
                ExcludedDates TEXT NULL
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
        // domain FinancialPattern has no account property; a pattern belongs
        // to an account by living in that account's page. Pages are never
        // persisted, so this column is the only place that containment can
        // be recorded and rebuilt from on load. The DEFAULT 1 *is* the
        // migration: every pre-existing pattern files under the seeded
        // "primary" account.
        EnsureColumn(connection, "FinancialPatterns", "AccountId", "INTEGER NOT NULL DEFAULT 1");

        // Which transfer a pattern is a pattern of, if any. NULL for ordinary
        // user-created patterns; set for a transfer's two patterns, which are
        // hidden from the pattern list and shown as one transfer instead. The
        // engine still reads every pattern, transfer patterns included.
        EnsureColumn(connection, "FinancialPatterns", "TransferId", "INTEGER NULL");

        // The ActiveFrom lead-in: a nullable date on a pattern's rrule,
        // earlier than its first occurrence, so a jar can exist before the
        // pattern's occurrences begin. NULL for every pre-existing pattern —
        // the migration is simply the absence of a value (no lead-in).
        EnsureColumn(connection, "FinancialPatterns", "ActiveFrom", "TEXT NULL");
        EnsureColumn(connection, "EarMarkPatterns", "ActiveFrom", "TEXT NULL");
        EnsureColumn(connection, "Transfers", "ActiveFrom", "TEXT NULL");

        // The AutoRenew marker: set invisibly when the user answers "it just
        // keeps going" at creation. DEFAULT 0 *is* the migration — every
        // pre-existing pattern was created before this question existed, so
        // none of them opted in.
        EnsureColumn(connection, "FinancialPatterns", "AutoRenew", "INTEGER NOT NULL DEFAULT 0");

        // RFC 5545's own EXDATE: specific dates a schedule otherwise would
        // land on, skipped anyway ("the glut case,"
        // mechanism C). NULL for every pre-existing pattern — the migration
        // is simply the absence of any exclusion, same shape as ActiveFrom's
        // own migration above. Only the Earmark form's own recurrence editor
        // exposes a way to set this today; FinancialPatterns/Transfers carry
        // the column for schema symmetry with the shared RecurrenceRule type
        // (same reasoning as their own unused-so-far ActiveFrom column), not
        // because either form offers a way to populate it yet.
        EnsureColumn(connection, "FinancialPatterns", "ExcludedDates", "TEXT NULL");
        EnsureColumn(connection, "EarMarkPatterns", "ExcludedDates", "TEXT NULL");
        EnsureColumn(connection, "Transfers", "ExcludedDates", "TEXT NULL");

        // EarMarkPattern.ExplicitlyCreated — whether the user made this savings
        // plan their own. Pre-existing rows predate the distinction, so they
        // migrate as 1 (explicit);
        EnsureColumn(connection, "EarMarkPatterns", "ExplicitlyCreated", "INTEGER NOT NULL DEFAULT 1");

        // More than one EarMarkPattern may now share a finance_id, so
        // FinanceId alone can no longer be the table's key. Run after the
        // EnsureColumn calls above so a pre-existing table already has every
        // column before it's copied across.
        EnsureEarMarkPatternsAllowMultiplePerFinanceId(connection);

        // A manual earmark is about the GOAL (the jar), not any one plan
        // segment, so it always should have referenced FinancialPatterns —
        // but now it MUST: SQLite rejects any statement touching a table
        // declaring REFERENCES EarMarkPatterns(FinanceId) once FinanceId
        // alone is no longer that table's key ("foreign key mismatch",
        // checked at prepare time regardless of PRAGMA foreign_keys).
        EnsureManualEarmarksReferenceFinancialPatterns(connection);

        // One-time backfill for the 3.11.2.a2 front-half check: a
        // pre-existing goal whose savings plan starts before the goal's own
        // Start had no ActiveFrom, which the check rejects on load. Give
        // each such goal an ActiveFrom equal to its plan's Start —
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

    /// <summary>[WRITES FILE] Migrates the EarMarkPatterns table to its composite (FinanceId, StartDate) primary key, rebuilding the table if it's still on the old single-column key. Idempotent — a no-op on a fresh install or an already-migrated database.</summary>
    /// <param name="connection">The open database connection to migrate.</param>
    private static void EnsureEarMarkPatternsAllowMultiplePerFinanceId(SqliteConnection connection)
    {
        // SQLite can't alter a primary key in place, so this renames the
        // old table aside, recreates it with the composite key the CREATE
        // TABLE statement in Initialize() now declares, copies the data
        // across, and drops the old copy. Checked via PRAGMA table_info
        // rather than a version flag, so it's a no-op both on a fresh
        // install (already created with the composite key) and on a
        // database already migrated by an earlier run.
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

        // Columns beyond the original single-key shape (ActiveFrom,
        // StartingAllocation, ExcludedDates) are already guaranteed present
        // on the old table by the EnsureColumn calls that ran before this —
        // carried across explicitly here so a database still old enough to
        // need this migration doesn't lose them in the rebuild.
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
                ExcludedDates TEXT NULL,
                ExplicitlyCreated INTEGER NOT NULL DEFAULT 1,
                PRIMARY KEY (FinanceId, StartDate)
            );

            INSERT INTO EarMarkPatterns
                (FinanceId, Amount, Frequency, IntervalValue, ByDay, ByMonthDay, StartDate, UntilDate, ActiveFrom, StartingAllocation, ExcludedDates, ExplicitlyCreated)
            SELECT FinanceId, Amount, Frequency, IntervalValue, ByDay, ByMonthDay, StartDate, UntilDate, ActiveFrom, StartingAllocation, ExcludedDates, ExplicitlyCreated
            FROM EarMarkPatterns_old_singlekey;

            DROP TABLE EarMarkPatterns_old_singlekey;
            """;
        migrate.ExecuteNonQuery();
    }

    /// <summary>[WRITES FILE] Repoints ManualEarmarks at FinancialPatterns if it isn't already, rebuilding the table if needed. Idempotent — a no-op on a fresh install or an already-migrated database.</summary>
    /// <param name="connection">The open database connection to migrate.</param>
    private static void EnsureManualEarmarksReferenceFinancialPatterns(SqliteConnection connection)
    {
        // Checked via PRAGMA foreign_key_list rather than a version flag,
        // so it's a no-op both on a fresh install (already created
        // referencing FinancialPatterns) and on a database already
        // migrated by an earlier run. Checks for the one thing that
        // actually matters — does it already correctly reference
        // FinancialPatterns — so it self-heals from any stale target left
        // behind by an interrupted migration, not just one specific name
        // (see PatternRepositoryTests:
        // A_manual_earmarks_table_referencing_a_stale_leftover_table_is_repointed_at_financial_patterns).
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

    /// <summary>[WRITES FILE] Adds a column to a table if it doesn't already exist — the general-purpose schema migration this project's column-level upgrades all go through.</summary>
    /// <param name="connection">The open database connection to migrate.</param>
    /// <param name="table">The table to check/alter.</param>
    /// <param name="column">The column to add if missing.</param>
    /// <param name="columnDefinition">The column's SQL type/constraints (everything after the column name in an ADD COLUMN clause).</param>
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
