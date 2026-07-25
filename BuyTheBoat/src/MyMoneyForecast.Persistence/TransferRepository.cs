using System.Globalization;
using Microsoft.Data.Sqlite;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.Persistence;

// A transfer is stored as its canonical Transfers row PLUS two ordinary
// FinancialPatterns — a withdrawal and a deposit (saved through
// FinancialPatternRepository, tagged with this transfer's id). The row is the
// definition; the patterns are what the cascade
// actually consumes. See planning/10 items 3 and 6.
public sealed class TransferRepository(PatternDatabase database, FinancialPatternRepository financialPatterns)
{
    // Saves the Transfers row and both patterns. Not wrapped in one transaction —
    // matching the rest of this single-user layer; a torn write leaves the pair
    // inconsistent, which the validation sweep is there to catch.
    public void Save(TransferResult result)
    {
        var transfer = result.Transfer;

        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO Transfers
                    (Id, FromAccountId, ToAccountId, Amount, Frequency, IntervalValue, ByDay, ByMonthDay, StartDate, UntilDate)
                VALUES
                    ($Id, $FromAccountId, $ToAccountId, $Amount, $Frequency, $IntervalValue, $ByDay, $ByMonthDay, $StartDate, $UntilDate)
                ON CONFLICT(Id) DO UPDATE SET
                    FromAccountId = excluded.FromAccountId,
                    ToAccountId = excluded.ToAccountId,
                    Amount = excluded.Amount,
                    Frequency = excluded.Frequency,
                    IntervalValue = excluded.IntervalValue,
                    ByDay = excluded.ByDay,
                    ByMonthDay = excluded.ByMonthDay,
                    StartDate = excluded.StartDate,
                    UntilDate = excluded.UntilDate;
                """;
            command.Parameters.AddWithValue("$Id", transfer.Id);
            command.Parameters.AddWithValue("$FromAccountId", transfer.FromAccountId);
            command.Parameters.AddWithValue("$ToAccountId", transfer.ToAccountId);
            command.Parameters.AddWithValue("$Amount", transfer.Amount.ToString(CultureInfo.InvariantCulture));
            RecurrenceRuleColumns.AddParameters(command, transfer.DatePattern);
            command.ExecuteNonQuery();
        }

        // The withdrawal files under the source account, the deposit under the destination;
        // both carry this transfer's id so they read back as its patterns.
        financialPatterns.Save(result.Withdrawal, transfer.FromAccountId, transfer.Id);
        financialPatterns.Save(result.Deposit, transfer.ToAccountId, transfer.Id);
    }

    public IReadOnlyList<Transfer> GetAll()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Transfers ORDER BY Id;";

        using var reader = command.ExecuteReader();
        var transfers = new List<Transfer>();
        while (reader.Read())
        {
            transfers.Add(Read(reader));
        }

        return transfers;
    }

    public int NextId()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Id), 0) + 1 FROM Transfers;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    // Deleting a transfer removes both of its patterns too — the pair is managed
    // through the transfer, never a pattern on its own.
    public void Delete(int id)
    {
        financialPatterns.DeleteByTransferId(id);

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Transfers WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Id", id);
        command.ExecuteNonQuery();
    }

    // An account can't be deleted while a transfer moves money in or out of it.
    // (Its patterns are patterns filed under it too, so the pattern guard catches
    // this as well — this gives the clearer, transfer-specific message.)
    public bool IsAccountReferenced(int accountId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Transfers WHERE FromAccountId = $A OR ToAccountId = $A;";
        command.Parameters.AddWithValue("$A", accountId);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private static Transfer Read(SqliteDataReader reader) =>
        Transfer.Create(new TransferOptions
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            FromAccountId = reader.GetInt32(reader.GetOrdinal("FromAccountId")),
            ToAccountId = reader.GetInt32(reader.GetOrdinal("ToAccountId")),
            Amount = decimal.Parse(reader.GetString(reader.GetOrdinal("Amount")), CultureInfo.InvariantCulture),
            DatePattern = RecurrenceRuleColumns.Read(reader),
        });
}
