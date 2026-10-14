using Microsoft.Data.Sqlite;

namespace BuyTheBoat.Persistence;

/// <summary>
/// The SQLite database, opened with a new connection for each operation rather than one shared connection, since this is a single-user app on a local file.
/// AppPaths decides where the file lives: under LocalAppData normally, so data survives rebuilds and repeated runs, or in the portable demo's own folder.
/// </summary>
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

    /// <summary>
    /// [CALC] The live database file's path: under LocalAppData normally, or in the portable demo's folder.
    /// AppPaths makes that decision.
    /// </summary>
    public static string DefaultDatabasePath() => AppPaths.DatabasePath;

    /// <summary>[READS FILE] Opens a new connection to this database. One connection per operation — callers dispose it when done.</summary>
    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// [READS FILE] Checks whether a file is actually a BuyTheBoat database.
    /// Import checks this before overwriting the live database, so an unrelated file can't wipe the user's data.
    /// </summary>
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

    /// <summary>
    /// [CALC] Releases every pooled SQLite connection handle; Import calls it before overwriting the live database file.
    /// Microsoft.Data.Sqlite keeps pooled handles open after their connections are disposed, which would block the overwrite.
    /// </summary>
    public static void ReleasePooledConnections() => SqliteConnection.ClearAllPools();

    /// <summary>
    /// [WRITES FILE] Overwrites one database file with another; Import uses it to swap in the chosen file.
    /// First releases pooled handles and deletes the destination's leftover -wal, -shm, and -journal files, since a stale journal from an earlier crash could otherwise be rolled back into the new file and corrupt it.
    /// </summary>
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

    /// <summary>
    /// [READS FILE] Whether the app could actually load this database file, a deeper check than LooksLikeValidDatabaseFile's table check.
    /// Opens a throwaway copy, runs the startup migrations, then reads every repository, each of which re-validates its rows as the live app does.
    /// Catches an old or inconsistent export that would pass the lighter check but crash the app on its first forecast, such as an orphaned earmark or a row outside its plan's span.
    /// Returns a short reason when it can't load, or null when it loads cleanly; never throws, since a failed check is reported as "can't load."
    /// </summary>
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

            // Reads each repository so its corruption checks run (a plan with no pattern, an earmark with no plan, a row outside its plan's span); the results are discarded.
            _ = financialPatterns.GetAll();
            _ = earMarkPatterns.GetAll();
            _ = new ManualEarmarkRepository(database, earMarkPatterns).GetAll();
            _ = new AccountRepository(database).GetAll();
            _ = new TransferRepository(database, financialPatterns).GetAll();
            _ = new DraftRowRepository(database).GetAll();
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

    /// <summary>
    /// [WRITES FILE] Creates every table this app needs if it doesn't exist yet, then runs each schema migration in order.
    /// Safe to run on every startup, on a fresh or an already-migrated database.
    /// </summary>
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
                ExcludedDates TEXT NULL,
                Notes TEXT NULL
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

            CREATE TABLE IF NOT EXISTS DraftPatterns (
                Id INTEGER PRIMARY KEY,
                Source TEXT NOT NULL DEFAULT '',
                Description TEXT NOT NULL DEFAULT '',
                Account TEXT NOT NULL DEFAULT '',
                Amount TEXT NOT NULL DEFAULT '',
                Priority TEXT NOT NULL DEFAULT '',
                Frequency TEXT NOT NULL DEFAULT '',
                Every TEXT NOT NULL DEFAULT '',
                DaysOfWeek TEXT NOT NULL DEFAULT '',
                DaysOfMonth TEXT NOT NULL DEFAULT '',
                Starts TEXT NOT NULL DEFAULT '',
                Ends TEXT NOT NULL DEFAULT '',
                Notes TEXT NOT NULL DEFAULT ''
            );
            """;
        command.ExecuteNonQuery();

        // CREATE TABLE IF NOT EXISTS doesn't add new columns to an existing table, so each column added later goes through EnsureColumn.
        EnsureColumn(connection, "CurrentBalance", "HorizonEndDate", "TEXT NULL");
        EnsureColumn(connection, "CurrentBalance", "IdealSafetyCushion", "TEXT NULL");
        EnsureColumn(connection, "EarMarkPatterns", "StartingAllocation", "TEXT NOT NULL DEFAULT '0'");

        // Which account each pattern is filed under; storage only, since the domain FinancialPattern has no account and account pages aren't saved.
        // DEFAULT 1 files older patterns under the "primary" account.
        EnsureColumn(connection, "FinancialPatterns", "AccountId", "INTEGER NOT NULL DEFAULT 1");

        // Which transfer a pattern belongs to, or NULL for an ordinary pattern.
        // A transfer's two patterns are hidden from the pattern list and shown as one transfer, but the forecast still reads them.
        EnsureColumn(connection, "FinancialPatterns", "TransferId", "INTEGER NULL");

        // The ActiveFrom lead-in: a date before a rule's first occurrence, so a jar can exist before its occurrences begin.
        // NULL means no lead-in, which is what older rows get.
        EnsureColumn(connection, "FinancialPatterns", "ActiveFrom", "TEXT NULL");
        EnsureColumn(connection, "EarMarkPatterns", "ActiveFrom", "TEXT NULL");
        EnsureColumn(connection, "Transfers", "ActiveFrom", "TEXT NULL");

        // The AutoRenew flag, set when the user answers "it just keeps going".
        // DEFAULT 0, since older patterns never opted in.
        EnsureColumn(connection, "FinancialPatterns", "AutoRenew", "INTEGER NOT NULL DEFAULT 0");

        // RFC 5545's EXDATE: dates a schedule would land on but skips ("the glut case," mechanism C).
        // NULL means none, which is what older rows get.
        // Only the Earmark form's recurrence editor can set it; the other two tables have the column because they store the same RecurrenceRule.
        EnsureColumn(connection, "FinancialPatterns", "ExcludedDates", "TEXT NULL");
        EnsureColumn(connection, "EarMarkPatterns", "ExcludedDates", "TEXT NULL");
        EnsureColumn(connection, "Transfers", "ExcludedDates", "TEXT NULL");

        // EarMarkPattern.ExplicitlyCreated: whether the user made this savings plan their own.
        // Older rows become 1 (explicit), since they predate the distinction.
        EnsureColumn(connection, "EarMarkPatterns", "ExplicitlyCreated", "INTEGER NOT NULL DEFAULT 1");

        // FinancialPattern.Notes, the user's free-form notes; NULL means none.
        EnsureColumn(connection, "FinancialPatterns", "Notes", "TEXT NULL");

        // Several EarMarkPatterns can share a FinanceId, so the key is (FinanceId, StartDate).
        // Must run after the EnsureColumn calls above, so an older table has every column before it's copied across.
        EnsureEarMarkPatternsAllowMultiplePerFinanceId(connection);

        // A manual earmark belongs to the goal (the jar), not one plan, so it references FinancialPatterns.
        // It must: with EarMarkPatterns keyed on (FinanceId, StartDate), SQLite rejects any statement touching a table that declares REFERENCES EarMarkPatterns(FinanceId) ("foreign key mismatch", checked even without PRAGMA foreign_keys).
        EnsureManualEarmarksReferenceFinancialPatterns(connection);

        // Backfill for the 3.11.2.a2 front-half check: an older goal whose savings plan starts before the goal's Start has no ActiveFrom, which the check rejects on load.
        // Each such goal gets ActiveFrom equal to its plan's Start, as creation sets.
        // Dates are yyyy-MM-dd text, so the string comparison sorts by date; only rows still NULL are touched, so re-running does nothing.
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

    /// <summary>
    /// [WRITES FILE] Moves the EarMarkPatterns table to its (FinanceId, StartDate) primary key, rebuilding it if it's still keyed on FinanceId alone.
    /// Does nothing on a fresh install or an already-migrated database.
    /// </summary>
    /// <param name="connection">The open database connection to migrate.</param>
    private static void EnsureEarMarkPatternsAllowMultiplePerFinanceId(SqliteConnection connection)
    {
        // SQLite can't change a primary key in place, so this renames the old table aside, recreates it with the key Initialize() declares, copies the rows, and drops the old table.
        // Checks PRAGMA table_info rather than a version flag, so it does nothing on a fresh install or an already-migrated database.
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

        // Copies ActiveFrom, StartingAllocation, and ExcludedDates explicitly; the EnsureColumn calls before this have already added them to the old table.
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

    /// <summary>
    /// [WRITES FILE] Repoints ManualEarmarks at FinancialPatterns if it isn't already, rebuilding the table if needed.
    /// Does nothing on a fresh install or an already-migrated database.
    /// </summary>
    /// <param name="connection">The open database connection to migrate.</param>
    private static void EnsureManualEarmarksReferenceFinancialPatterns(SqliteConnection connection)
    {
        // Checks PRAGMA foreign_key_list for a reference to FinancialPatterns rather than a version flag, so it does nothing on an already-correct table and also repairs one left pointing at a stale table by an interrupted migration.
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
