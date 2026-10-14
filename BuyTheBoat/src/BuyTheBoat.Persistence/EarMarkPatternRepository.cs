using System.Globalization;
using BuyTheBoat.Domain;

namespace BuyTheBoat.Persistence;

public sealed class EarMarkPatternRepository(PatternDatabase database, FinancialPatternRepository financialPatterns)
{
    /// <summary>[WRITES FILE] Creates a new savings plan segment, or updates the existing one starting on the same date.</summary>
    /// <param name="pattern">The savings plan to save.</param>
    public void Save(EarMarkPattern pattern)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        // Keyed on (FinanceId, StartDate), since one goal can have several plans in sequence (a Restructure's earlier and later plan): this updates the row starting on the same date and inserts a new row for a plan starting on a different date.
        command.CommandText = """
            INSERT INTO EarMarkPatterns
                (FinanceId, Amount, Frequency, IntervalValue, ByDay, ByMonthDay, StartDate, UntilDate, ActiveFrom, StartingAllocation, ExcludedDates, ExplicitlyCreated)
            VALUES
                ($FinanceId, $Amount, $Frequency, $IntervalValue, $ByDay, $ByMonthDay, $StartDate, $UntilDate, $ActiveFrom, $StartingAllocation, $ExcludedDates, $ExplicitlyCreated)
            ON CONFLICT(FinanceId, StartDate) DO UPDATE SET
                Amount = excluded.Amount,
                Frequency = excluded.Frequency,
                IntervalValue = excluded.IntervalValue,
                ByDay = excluded.ByDay,
                ByMonthDay = excluded.ByMonthDay,
                UntilDate = excluded.UntilDate,
                ActiveFrom = excluded.ActiveFrom,
                StartingAllocation = excluded.StartingAllocation,
                ExcludedDates = excluded.ExcludedDates,
                ExplicitlyCreated = excluded.ExplicitlyCreated;
            """;

        command.Parameters.AddWithValue("$FinanceId", pattern.FinanceId);
        command.Parameters.AddWithValue("$Amount", pattern.Amount.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$StartingAllocation", pattern.StartingAllocation.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ExplicitlyCreated", pattern.ExplicitlyCreated ? 1 : 0);
        RecurrenceRuleColumns.AddParameters(command, pattern.DatePattern);

        command.ExecuteNonQuery();
    }

    /// <summary>[READS FILE] Returns every savings plan (EarMarkPattern), one row per fund-jar segment.</summary>
    public IReadOnlyList<EarMarkPattern> GetAll()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM EarMarkPatterns ORDER BY FinanceId, StartDate;";

        using var reader = command.ExecuteReader();
        var patterns = new List<EarMarkPattern>();
        while (reader.Read())
        {
            var financeId = reader.GetInt32(reader.GetOrdinal("FinanceId"));
            var amount = decimal.Parse(reader.GetString(reader.GetOrdinal("Amount")), CultureInfo.InvariantCulture);
            var startingAllocation = decimal.Parse(reader.GetString(reader.GetOrdinal("StartingAllocation")), CultureInfo.InvariantCulture);
            var explicitlyCreated = reader.GetInt32(reader.GetOrdinal("ExplicitlyCreated")) != 0;
            var datePattern = RecurrenceRuleColumns.Read(reader);

            // Re-validates against the linked goal through EarMarkPattern.Create, so reads and writes both enforce 3.11.2.a2 in one place.
            var goal = financialPatterns.GetByFinanceId(financeId)
                ?? throw new InvalidOperationException(
                    $"EarMarkPattern {financeId} has no matching FinancialPattern — data is corrupt.");

            patterns.Add(EarMarkPattern.Create(
                new EarMarkPatternOptions
                {
                    FinanceId = financeId,
                    DatePattern = datePattern,
                    Amount = amount,
                    StartingAllocation = startingAllocation,
                    ExplicitlyCreated = explicitlyCreated,
                },
                goal));
        }

        return patterns;
    }

    /// <summary>[DELETES] Removes a savings plan and its manual earmarks.</summary>
    /// <param name="financeId">The plan to delete.</param>
    public void Delete(int financeId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        // Deletes the plan's manual earmarks too, since the plan's span is the jar's lifetime and they'd otherwise point at a jar that no longer exists.
        command.CommandText = """
            DELETE FROM ManualEarmarks WHERE FinanceId = $FinanceId;
            DELETE FROM EarMarkPatterns WHERE FinanceId = $FinanceId;
            """;
        command.Parameters.AddWithValue("$FinanceId", financeId);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// [DELETES] Removes one savings plan segment, the one starting on startDate, leaving other segments with the same finance id and all manual earmarks alone.
    /// Needed when a segment's Start moves (PatternTruncation.StartOn): Save's (FinanceId, StartDate) key makes saving the moved segment insert a new row, so this removes the stale row left at the old Start.
    /// Unlike Delete, it doesn't delete manual earmarks, since the caller has already decided which ones survive.
    /// </summary>
    /// <param name="financeId">Which goal or bill's savings plan this segment belongs to.</param>
    /// <param name="startDate">Which segment to remove, by its old Start.</param>
    public void Delete(int financeId, DateOnly startDate)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM EarMarkPatterns WHERE FinanceId = $FinanceId AND StartDate = $StartDate;";
        command.Parameters.AddWithValue("$FinanceId", financeId);
        command.Parameters.AddWithValue("$StartDate", startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }
}
