using System.Globalization;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.Persistence;

public sealed class FinancialPatternRepository(PatternDatabase database)
{
    // accountId is which account this pattern is FILED UNDER — deliberately a
    // separate argument rather than a property of the pattern, because the
    // documented model gives FinancialPattern no account (planning/10 item 2-A).
    // transferId, when set, marks this pattern as one pattern of a transfer (item 3).
    public void Save(FinancialPattern pattern, int accountId, int? transferId = null)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO FinancialPatterns
                (FinanceId, Source, Amount, Priority, Mandatory, Description, AccountId, TransferId, Frequency, IntervalValue, ByDay, ByMonthDay, StartDate, UntilDate)
            VALUES
                ($FinanceId, $Source, $Amount, $Priority, $Mandatory, $Description, $AccountId, $TransferId, $Frequency, $IntervalValue, $ByDay, $ByMonthDay, $StartDate, $UntilDate)
            ON CONFLICT(FinanceId) DO UPDATE SET
                Source = excluded.Source,
                Amount = excluded.Amount,
                Priority = excluded.Priority,
                Mandatory = excluded.Mandatory,
                Description = excluded.Description,
                AccountId = excluded.AccountId,
                TransferId = excluded.TransferId,
                Frequency = excluded.Frequency,
                IntervalValue = excluded.IntervalValue,
                ByDay = excluded.ByDay,
                ByMonthDay = excluded.ByMonthDay,
                StartDate = excluded.StartDate,
                UntilDate = excluded.UntilDate;
            """;

        command.Parameters.AddWithValue("$AccountId", accountId);
        command.Parameters.AddWithValue("$TransferId", (object?)transferId ?? DBNull.Value);
        command.Parameters.AddWithValue("$FinanceId", pattern.FinanceId);
        command.Parameters.AddWithValue("$Source", pattern.Source);
        command.Parameters.AddWithValue("$Amount", pattern.Amount.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$Priority", pattern.Priority);
        command.Parameters.AddWithValue("$Mandatory", pattern.Mandatory ? 1 : 0);
        command.Parameters.AddWithValue("$Description", (object?)pattern.Description ?? DBNull.Value);
        RecurrenceRuleColumns.AddParameters(command, pattern.DatePattern);

        command.ExecuteNonQuery();
    }

    public IReadOnlyList<FinancialPattern> GetAll()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM FinancialPatterns ORDER BY FinanceId;";

        using var reader = command.ExecuteReader();
        var patterns = new List<FinancialPattern>();
        while (reader.Read())
        {
            patterns.Add(Read(reader));
        }

        return patterns;
    }

    public FinancialPattern? GetByFinanceId(int financeId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM FinancialPatterns WHERE FinanceId = $FinanceId;";
        command.Parameters.AddWithValue("$FinanceId", financeId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    // Rebuilds the containment the class model describes: each account's page
    // gets exactly its own finance_patterns list. The stored account id is
    // consumed here and never reaches the domain type — past this point the
    // model is pure containment, as documented.
    public IReadOnlyDictionary<int, IReadOnlyList<FinancialPattern>> GetAllByAccount()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM FinancialPatterns ORDER BY AccountId, FinanceId;";

        using var reader = command.ExecuteReader();
        var byAccount = new Dictionary<int, List<FinancialPattern>>();
        while (reader.Read())
        {
            var accountId = reader.GetInt32(reader.GetOrdinal("AccountId"));
            if (!byAccount.TryGetValue(accountId, out var patterns))
            {
                patterns = [];
                byAccount[accountId] = patterns;
            }

            patterns.Add(Read(reader));
        }

        return byAccount.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<FinancialPattern>)entry.Value);
    }

    // The pattern-list UI shows only patterns the user created directly — a
    // transfer's two patterns are hidden here and surfaced as the single transfer
    // instead (planning/10 item 3). GetAll (and GetAllByAccount) still return
    // the patterns, because they are what actually move money in the cascade.
    public IReadOnlyList<FinancialPattern> GetAllExcludingTransferPatterns()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM FinancialPatterns WHERE TransferId IS NULL ORDER BY FinanceId;";

        using var reader = command.ExecuteReader();
        var patterns = new List<FinancialPattern>();
        while (reader.Read())
        {
            patterns.Add(Read(reader));
        }

        return patterns;
    }

    // The finance ids of every transfer's WITHDRAWAL — the negative pattern, in
    // the account the money leaves. The engine needs these for planning/14 item
    // A-1: a transfer reserves in the account it leaves, but the household view
    // must not count that as set aside, since the household is not down a cent.
    //
    // The domain FinancialPattern deliberately carries no TransferId (that is a
    // storage concern, planning/10 item 2-A), so the engine is handed the set
    // rather than working it out. Amounts are stored as invariant-culture TEXT,
    // so the sign test is done in C# rather than in SQL, where comparing a text
    // column numerically is not dependable.
    public IReadOnlySet<int> GetTransferWithdrawalFinanceIds()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT FinanceId, Amount FROM FinancialPatterns WHERE TransferId IS NOT NULL;";

        using var reader = command.ExecuteReader();
        var ids = new HashSet<int>();
        while (reader.Read())
        {
            var amount = decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
            if (amount < 0m)
            {
                ids.Add(reader.GetInt32(0));
            }
        }

        return ids;
    }

    // Removes both patterns of a transfer — used when the transfer itself is deleted.
    public void DeleteByTransferId(int transferId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FinancialPatterns WHERE TransferId = $TransferId;";
        command.Parameters.AddWithValue("$TransferId", transferId);
        command.ExecuteNonQuery();
    }

    // Which account a pattern is filed under — so the UI can show it and
    // pre-select it when editing. Null if the pattern doesn't exist.
    public int? GetAccountId(int financeId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT AccountId FROM FinancialPatterns WHERE FinanceId = $FinanceId;";
        command.Parameters.AddWithValue("$FinanceId", financeId);

        var result = command.ExecuteScalar();
        return result is null or DBNull ? null : Convert.ToInt32(result);
    }

    // Backs the "an account still holding things can't be deleted" guard.
    public bool HasPatternsInAccount(int accountId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM FinancialPatterns WHERE AccountId = $AccountId;";
        command.Parameters.AddWithValue("$AccountId", accountId);

        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    // Checked before Delete rather than relying on a database-level foreign
    // key (SQLite doesn't enforce FK constraints by default, and this way
    // works regardless of when a given .db file was first created).
    public bool HasLinkedEarMarkPattern(int financeId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM EarMarkPatterns WHERE FinanceId = $FinanceId;";
        command.Parameters.AddWithValue("$FinanceId", financeId);

        return (long)command.ExecuteScalar()! > 0;
    }

    public void Delete(int financeId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FinancialPatterns WHERE FinanceId = $FinanceId;";
        command.Parameters.AddWithValue("$FinanceId", financeId);
        command.ExecuteNonQuery();
    }

    private static FinancialPattern Read(Microsoft.Data.Sqlite.SqliteDataReader reader) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = reader.GetInt32(reader.GetOrdinal("FinanceId")),
            Source = reader.GetString(reader.GetOrdinal("Source")),
            Amount = decimal.Parse(reader.GetString(reader.GetOrdinal("Amount")), CultureInfo.InvariantCulture),
            Priority = reader.GetInt32(reader.GetOrdinal("Priority")),
            Mandatory = reader.GetInt32(reader.GetOrdinal("Mandatory")) != 0,
            Description = reader.IsDBNull(reader.GetOrdinal("Description"))
                ? null
                : reader.GetString(reader.GetOrdinal("Description")),
            DatePattern = RecurrenceRuleColumns.Read(reader),
        });
}
