using System.Globalization;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.Persistence;

public sealed class FinancialPatternRepository(PatternDatabase database)
{
    public void Save(FinancialPattern pattern)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO FinancialPatterns
                (FinanceId, Source, Amount, Priority, Mandatory, Description, Frequency, IntervalValue, ByDay, ByMonthDay, StartDate, UntilDate)
            VALUES
                ($FinanceId, $Source, $Amount, $Priority, $Mandatory, $Description, $Frequency, $IntervalValue, $ByDay, $ByMonthDay, $StartDate, $UntilDate)
            ON CONFLICT(FinanceId) DO UPDATE SET
                Source = excluded.Source,
                Amount = excluded.Amount,
                Priority = excluded.Priority,
                Mandatory = excluded.Mandatory,
                Description = excluded.Description,
                Frequency = excluded.Frequency,
                IntervalValue = excluded.IntervalValue,
                ByDay = excluded.ByDay,
                ByMonthDay = excluded.ByMonthDay,
                StartDate = excluded.StartDate,
                UntilDate = excluded.UntilDate;
            """;

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
