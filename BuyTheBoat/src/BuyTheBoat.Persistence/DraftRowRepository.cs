using Microsoft.Data.Sqlite;

namespace BuyTheBoat.Persistence;

// One draft row from the Bills & Paychecks › Spreadsheet subtab: a bill or paycheck
// jotted down but not yet saved through the Form. Every cell is kept exactly as typed,
// invalid text included, so nothing the user wrote is lost between runs.
// Blank cells are "", never null.
public sealed record DraftRow
{
    public required int Id { get; init; }
    public string Source { get; init; } = "";
    public string Description { get; init; } = "";
    public string Account { get; init; } = "";
    public string Amount { get; init; } = "";
    public string Priority { get; init; } = "";
    public string Frequency { get; init; } = "";
    public string Every { get; init; } = "";
    public string DaysOfWeek { get; init; } = "";
    public string DaysOfMonth { get; init; } = "";
    public string Starts { get; init; } = "";
    public string Ends { get; init; } = "";
    public string Notes { get; init; } = "";
}

// Storage for draft rows (the DraftPatterns table). Ids are app-assigned (NextId = max + 1), like accounts.
// Drafts come back in Id order, so a new draft lands at the bottom of the drafts section.
public sealed class DraftRowRepository(PatternDatabase database)
{
    private const string Columns =
        "Id, Source, Description, Account, Amount, Priority, Frequency, Every, DaysOfWeek, DaysOfMonth, Starts, Ends, Notes";

    /// <summary>[WRITES FILE] Saves a draft row, creating it or replacing the one with the same Id.</summary>
    public void Save(DraftRow draft)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT OR REPLACE INTO DraftPatterns ({Columns})
            VALUES ($Id, $Source, $Description, $Account, $Amount, $Priority, $Frequency, $Every, $DaysOfWeek, $DaysOfMonth, $Starts, $Ends, $Notes);
            """;

        command.Parameters.AddWithValue("$Id", draft.Id);
        command.Parameters.AddWithValue("$Source", draft.Source);
        command.Parameters.AddWithValue("$Description", draft.Description);
        command.Parameters.AddWithValue("$Account", draft.Account);
        command.Parameters.AddWithValue("$Amount", draft.Amount);
        command.Parameters.AddWithValue("$Priority", draft.Priority);
        command.Parameters.AddWithValue("$Frequency", draft.Frequency);
        command.Parameters.AddWithValue("$Every", draft.Every);
        command.Parameters.AddWithValue("$DaysOfWeek", draft.DaysOfWeek);
        command.Parameters.AddWithValue("$DaysOfMonth", draft.DaysOfMonth);
        command.Parameters.AddWithValue("$Starts", draft.Starts);
        command.Parameters.AddWithValue("$Ends", draft.Ends);
        command.Parameters.AddWithValue("$Notes", draft.Notes);
        command.ExecuteNonQuery();
    }

    /// <summary>[READS FILE] Returns every draft row, oldest first.</summary>
    public IReadOnlyList<DraftRow> GetAll()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM DraftPatterns ORDER BY Id;";

        using var reader = command.ExecuteReader();
        var drafts = new List<DraftRow>();
        while (reader.Read())
        {
            drafts.Add(Read(reader));
        }

        return drafts;
    }

    /// <summary>[READS FILE] Returns the Id a new draft row should get: max existing + 1, or 1 when there are none.</summary>
    public int NextId()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Id), 0) + 1 FROM DraftPatterns;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>[DELETES] Removes a draft row. Deleting one that's already gone does nothing.</summary>
    public void Delete(int id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM DraftPatterns WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>[CALC] Builds a DraftRow from one row of a DraftPatterns query, in Columns order.</summary>
    private static DraftRow Read(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Source = reader.GetString(1),
        Description = reader.GetString(2),
        Account = reader.GetString(3),
        Amount = reader.GetString(4),
        Priority = reader.GetString(5),
        Frequency = reader.GetString(6),
        Every = reader.GetString(7),
        DaysOfWeek = reader.GetString(8),
        DaysOfMonth = reader.GetString(9),
        Starts = reader.GetString(10),
        Ends = reader.GetString(11),
        Notes = reader.GetString(12),
    };
}
