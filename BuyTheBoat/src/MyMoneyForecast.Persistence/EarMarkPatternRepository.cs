using System.Globalization;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.Persistence;

public sealed class EarMarkPatternRepository(PatternDatabase database, FinancialPatternRepository financialPatterns)
{
    /// <summary>[WRITES FILE] Creates a new savings plan segment, or updates the existing one starting on the same date.</summary>
    /// <param name="pattern">The savings plan to save.</param>
    public void Save(EarMarkPattern pattern)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        // Keyed on (FinanceId, StartDate), not FinanceId alone: more than
        // one plan may fund the same goal in sequence (a "Restructure"
        // predecessor + successor), so this updates the one row that
        // already starts on that date — typically the same plan being
        // re-edited — and inserts a new row for a genuinely new segment
        // starting on a different date.
        command.CommandText = """
            INSERT INTO EarMarkPatterns
                (FinanceId, Amount, Frequency, IntervalValue, ByDay, ByMonthDay, StartDate, UntilDate, ActiveFrom, StartingAllocation, ExcludedDates)
            VALUES
                ($FinanceId, $Amount, $Frequency, $IntervalValue, $ByDay, $ByMonthDay, $StartDate, $UntilDate, $ActiveFrom, $StartingAllocation, $ExcludedDates)
            ON CONFLICT(FinanceId, StartDate) DO UPDATE SET
                Amount = excluded.Amount,
                Frequency = excluded.Frequency,
                IntervalValue = excluded.IntervalValue,
                ByDay = excluded.ByDay,
                ByMonthDay = excluded.ByMonthDay,
                UntilDate = excluded.UntilDate,
                ActiveFrom = excluded.ActiveFrom,
                StartingAllocation = excluded.StartingAllocation,
                ExcludedDates = excluded.ExcludedDates;
            """;

        command.Parameters.AddWithValue("$FinanceId", pattern.FinanceId);
        command.Parameters.AddWithValue("$Amount", pattern.Amount.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$StartingAllocation", pattern.StartingAllocation.ToString(CultureInfo.InvariantCulture));
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
            var datePattern = RecurrenceRuleColumns.Read(reader);

            // Re-validates against the linked goal on the way back out, same
            // as EarMarkPattern.Create does going in — a goal's own dates
            // can't have changed underneath it since this was saved, but
            // this keeps both read and write paths going through the one
            // place that enforces 3.11.2.a2.
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
        // The pattern's span is the fund jar's lifetime, so the jar's manual
        // adjustments die with the pattern — otherwise they'd be orphans
        // pointing at a jar that no longer exists.
        command.CommandText = """
            DELETE FROM ManualEarmarks WHERE FinanceId = $FinanceId;
            DELETE FROM EarMarkPatterns WHERE FinanceId = $FinanceId;
            """;
        command.Parameters.AddWithValue("$FinanceId", financeId);
        command.ExecuteNonQuery();
    }

    /// <summary>[DELETES] Removes one savings plan segment — the one starting on a specific date — without touching any sibling segment sharing the same finance id, or that segment's own ManualEarmarks. Needed whenever a segment's own Start moves (PatternTruncation.StartOn): Save's own (FinanceId, StartDate) upsert key means saving the moved segment inserts a second row rather than replacing the original, since its StartDate no longer matches — this is the other half of that move, removing the stale row left at the old StartDate. Unlike the FinanceId-only Delete above, this deliberately does NOT cascade to ManualEarmarks — the caller has already decided which of those survive (the ones now on or after the new Start) and which don't.</summary>
    /// <param name="financeId">Which goal or bill's savings plan this segment belongs to.</param>
    /// <param name="startDate">Which segment to remove, by its own (now-stale) Start.</param>
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
