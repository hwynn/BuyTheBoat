using System.Globalization;
using BuyTheBoat.Domain;

namespace BuyTheBoat.Persistence;

public sealed class FinancialPatternRepository(PatternDatabase database)
{
    /// <summary>[WRITES FILE] Creates a new financial pattern, or updates the existing one with the same FinanceId.</summary>
    /// <param name="pattern">The pattern to save.</param>
    /// <param name="accountId">Which account this pattern is filed under — stored separately since the domain type itself carries no account.</param>
    /// <param name="transferId">When set, marks this pattern as one leg of a transfer.</param>
    public void Save(FinancialPattern pattern, int accountId, int? transferId = null)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO FinancialPatterns
                (FinanceId, Source, Amount, Priority, Mandatory, Description, AccountId, TransferId, Frequency, IntervalValue, ByDay, ByMonthDay, StartDate, UntilDate, ActiveFrom, AutoRenew, ExcludedDates)
            VALUES
                ($FinanceId, $Source, $Amount, $Priority, $Mandatory, $Description, $AccountId, $TransferId, $Frequency, $IntervalValue, $ByDay, $ByMonthDay, $StartDate, $UntilDate, $ActiveFrom, $AutoRenew, $ExcludedDates)
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
                UntilDate = excluded.UntilDate,
                ActiveFrom = excluded.ActiveFrom,
                AutoRenew = excluded.AutoRenew,
                ExcludedDates = excluded.ExcludedDates;
            """;

        command.Parameters.AddWithValue("$AccountId", accountId);
        command.Parameters.AddWithValue("$TransferId", (object?)transferId ?? DBNull.Value);
        command.Parameters.AddWithValue("$FinanceId", pattern.FinanceId);
        command.Parameters.AddWithValue("$Source", pattern.Source);
        command.Parameters.AddWithValue("$Amount", pattern.Amount.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$Priority", pattern.Priority);
        command.Parameters.AddWithValue("$Mandatory", pattern.Mandatory ? 1 : 0);
        command.Parameters.AddWithValue("$Description", (object?)pattern.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$AutoRenew", pattern.AutoRenew ? 1 : 0);
        RecurrenceRuleColumns.AddParameters(command, pattern.DatePattern);

        command.ExecuteNonQuery();
    }

    /// <summary>[READS FILE] Returns every financial pattern in storage, in FinanceId order.</summary>
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

    /// <summary>[READS FILE] Returns one financial pattern by its FinanceId, or null if none exists.</summary>
    /// <param name="financeId">The pattern to look up.</param>
    public FinancialPattern? GetByFinanceId(int financeId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM FinancialPatterns WHERE FinanceId = $FinanceId;";
        command.Parameters.AddWithValue("$FinanceId", financeId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <summary>[READS FILE] Returns a transfer's two legs (its withdrawal and deposit patterns), by the transfer id they're tagged with — the withdrawal is the one with a negative Amount, the deposit positive. Empty if the transfer has no patterns. Used to extend an ongoing transfer forward, which needs the legs the FinancialPattern objects themselves don't name.</summary>
    /// <param name="transferId">The transfer whose legs to fetch.</param>
    public IReadOnlyList<FinancialPattern> GetByTransferId(int transferId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM FinancialPatterns WHERE TransferId = $TransferId ORDER BY FinanceId;";
        command.Parameters.AddWithValue("$TransferId", transferId);

        using var reader = command.ExecuteReader();
        var legs = new List<FinancialPattern>();
        while (reader.Read())
        {
            legs.Add(Read(reader));
        }

        return legs;
    }

    /// <summary>[READS FILE] Returns every financial pattern, grouped by the account it's filed under. Feeds the forecast engine's per-account partitioning.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<FinancialPattern>> GetAllByAccount()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM FinancialPatterns ORDER BY AccountId, FinanceId;";

        using var reader = command.ExecuteReader();
        var byAccount = new Dictionary<int, List<FinancialPattern>>();
        while (reader.Read())
        {
            // The stored account id is consumed here and never reaches the
            // domain type — past this point the model is pure containment,
            // exactly as the class documentation describes: an account's
            // patterns live in its own list, not on the pattern itself.
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

    /// <summary>[READS FILE] Returns every financial pattern except the two legs of a transfer. Feeds the Bills/Paychecks list, where a transfer shows as one thing on its own tab instead of its two underlying patterns. Don't use this for the forecast engine — it needs GetAll/GetAllByAccount instead, since a transfer's patterns are what actually move money in the cascade.</summary>
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

    /// <summary>[READS FILE] Returns one account's financial patterns, except the two legs of a transfer. Used to scope a new outflow's Allocation Plan proposal to income from the same account — a plan paced against another account's paycheck would never actually be funded by it.</summary>
    /// <param name="accountId">The account to scope the patterns to.</param>
    public IReadOnlyList<FinancialPattern> GetByAccountExcludingTransferPatterns(int accountId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM FinancialPatterns WHERE AccountId = $AccountId AND TransferId IS NULL ORDER BY FinanceId;";
        command.Parameters.AddWithValue("$AccountId", accountId);

        using var reader = command.ExecuteReader();
        var patterns = new List<FinancialPattern>();
        while (reader.Read())
        {
            patterns.Add(Read(reader));
        }

        return patterns;
    }

    /// <summary>[READS FILE] Returns the FinanceId of every transfer's withdrawal leg (the negative pattern, in the account the money leaves). Used to exclude a transfer withdrawal's Allocation Plan from the earmark-patterns grid — a transfer shows as one thing on its own tab, never as its underlying reservation plan.</summary>
    public IReadOnlySet<int> GetTransferWithdrawalFinanceIds()
    {
        // The domain FinancialPattern carries no TransferId of its own (a
        // storage-only concern), so callers are handed this set rather than
        // reading it off the pattern directly.
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT FinanceId, Amount FROM FinancialPatterns WHERE TransferId IS NOT NULL;";

        using var reader = command.ExecuteReader();
        var ids = new HashSet<int>();
        while (reader.Read())
        {
            // Amounts are stored as invariant-culture TEXT, so the sign test
            // happens here in C# rather than in SQL, where comparing a text
            // column numerically isn't dependable.
            var amount = decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
            if (amount < 0m)
            {
                ids.Add(reader.GetInt32(0));
            }
        }

        return ids;
    }

    /// <summary>[READS FILE] Returns the FinanceId of both legs of every transfer. Used wherever a transfer's own two patterns need to be excluded from a general pattern list — the picker, the Bills/Paychecks tab — since a transfer shows as one thing, never as its two underlying patterns.</summary>
    public IReadOnlySet<int> GetTransferFinanceIds()
    {
        // The domain FinancialPattern carries no TransferId of its own (a
        // storage-only concern), so callers are handed this set rather than
        // reading it off the pattern directly.
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT FinanceId FROM FinancialPatterns WHERE TransferId IS NOT NULL;";

        using var reader = command.ExecuteReader();
        var ids = new HashSet<int>();
        while (reader.Read())
        {
            ids.Add(reader.GetInt32(0));
        }

        return ids;
    }

    /// <summary>[DELETES] Removes both patterns of a transfer, along with their Allocation Plan and any manual earmarks — used when the transfer itself is deleted.</summary>
    /// <param name="transferId">The transfer whose patterns to delete.</param>
    public void DeleteByTransferId(int transferId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        // Children deleted before the patterns they reference. A savings
        // plan whose goal pattern is gone is invalid by 3.10.a3 — leaving it
        // behind would orphan a jar and make the next forecast's plan
        // read-back throw.
        command.CommandText = """
            DELETE FROM ManualEarmarks WHERE FinanceId IN
                (SELECT FinanceId FROM FinancialPatterns WHERE TransferId = $TransferId);
            DELETE FROM EarMarkPatterns WHERE FinanceId IN
                (SELECT FinanceId FROM FinancialPatterns WHERE TransferId = $TransferId);
            DELETE FROM FinancialPatterns WHERE TransferId = $TransferId;
            """;
        command.Parameters.AddWithValue("$TransferId", transferId);
        command.ExecuteNonQuery();
    }

    /// <summary>[READS FILE] Looks up which account a pattern is filed under, so the Expense form can pre-select it when editing. Null if the pattern doesn't exist.</summary>
    /// <param name="financeId">The pattern to look up.</param>
    public int? GetAccountId(int financeId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT AccountId FROM FinancialPatterns WHERE FinanceId = $FinanceId;";
        command.Parameters.AddWithValue("$FinanceId", financeId);

        var result = command.ExecuteScalar();
        return result is null or DBNull ? null : Convert.ToInt32(result);
    }

    /// <summary>[READS FILE] Reports whether any pattern is still filed under an account — backs the "can't delete an account that's still holding things" guard.</summary>
    /// <param name="accountId">The account to check.</param>
    public bool HasPatternsInAccount(int accountId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM FinancialPatterns WHERE AccountId = $AccountId;";
        command.Parameters.AddWithValue("$AccountId", accountId);

        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    /// <summary>[READS FILE] Reports whether a pattern still has a linked savings plan. Checked before Delete, standing in for a database-level foreign key (SQLite doesn't enforce those by default).</summary>
    /// <param name="financeId">The pattern to check.</param>
    public bool HasLinkedEarMarkPattern(int financeId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM EarMarkPatterns WHERE FinanceId = $FinanceId;";
        command.Parameters.AddWithValue("$FinanceId", financeId);

        return (long)command.ExecuteScalar()! > 0;
    }

    /// <summary>[DELETES] Removes a financial pattern.</summary>
    /// <param name="financeId">The pattern to delete.</param>
    public void Delete(int financeId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FinancialPatterns WHERE FinanceId = $FinanceId;";
        command.Parameters.AddWithValue("$FinanceId", financeId);
        command.ExecuteNonQuery();
    }

    /// <summary>[CALC] Builds a FinancialPattern from one row of a FinancialPatterns query result.</summary>
    /// <param name="reader">The reader, positioned on the row to read.</param>
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
            AutoRenew = reader.GetInt32(reader.GetOrdinal("AutoRenew")) != 0,
            DatePattern = RecurrenceRuleColumns.Read(reader),
        });
}
