using System.Globalization;
using Microsoft.Data.Sqlite;
using BuyTheBoat.Domain;

namespace BuyTheBoat.Persistence;

// A transfer is stored as its canonical Transfers row PLUS two ordinary
// FinancialPatterns — a withdrawal and a deposit (saved through
// FinancialPatternRepository, tagged with this transfer's id). The row is the
// definition; the patterns are what the cascade actually consumes.
public sealed class TransferRepository(PatternDatabase database, FinancialPatternRepository financialPatterns)
{
    /// <summary>[WRITES FILE] Saves a transfer's canonical row plus its withdrawal and deposit patterns.</summary>
    /// <param name="result">The transfer and its two patterns to save.</param>
    public void Save(TransferResult result)
    {
        var transfer = result.Transfer;

        // Not wrapped in one transaction — matching the rest of this
        // single-user layer; a torn write leaves the pair inconsistent,
        // which the validation sweep is there to catch.
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO Transfers
                    (Id, FromAccountId, ToAccountId, Amount, Frequency, IntervalValue, ByDay, ByMonthDay, StartDate, UntilDate, ActiveFrom, ExcludedDates)
                VALUES
                    ($Id, $FromAccountId, $ToAccountId, $Amount, $Frequency, $IntervalValue, $ByDay, $ByMonthDay, $StartDate, $UntilDate, $ActiveFrom, $ExcludedDates)
                ON CONFLICT(Id) DO UPDATE SET
                    FromAccountId = excluded.FromAccountId,
                    ToAccountId = excluded.ToAccountId,
                    Amount = excluded.Amount,
                    Frequency = excluded.Frequency,
                    IntervalValue = excluded.IntervalValue,
                    ByDay = excluded.ByDay,
                    ByMonthDay = excluded.ByMonthDay,
                    StartDate = excluded.StartDate,
                    UntilDate = excluded.UntilDate,
                    ActiveFrom = excluded.ActiveFrom,
                    ExcludedDates = excluded.ExcludedDates;
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

    /// <summary>[READS FILE] Returns every transfer, in Id order.</summary>
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

    /// <summary>[READS FILE] Returns the next Id to assign a new transfer: max existing + 1, or 1 if there are none yet.</summary>
    public int NextId()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Id), 0) + 1 FROM Transfers;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>[DELETES] Removes a transfer, along with its withdrawal and deposit patterns — the pair is always managed through the transfer, never edited on its own.</summary>
    /// <param name="id">The transfer to delete.</param>
    public void Delete(int id)
    {
        financialPatterns.DeleteByTransferId(id);

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Transfers WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>[READS FILE] Reports whether any transfer moves money in or out of an account — checked before an account can be deleted, for a clearer message than the generic pattern guard alone would give.</summary>
    /// <param name="accountId">The account to check.</param>
    public bool IsAccountReferenced(int accountId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Transfers WHERE FromAccountId = $A OR ToAccountId = $A;";
        command.Parameters.AddWithValue("$A", accountId);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    /// <summary>[CALC] Builds a Transfer from one row of a Transfers query result.</summary>
    /// <param name="reader">The reader, positioned on the row to read.</param>
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
