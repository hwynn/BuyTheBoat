using System.Globalization;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.Persistence;

public sealed class EarMarkPatternRepository(PatternDatabase database, FinancialPatternRepository financialPatterns)
{
    // Keyed on (FinanceId, StartDate), not FinanceId alone (planning/17, item
    // 8 — F27/F29): more than one plan may now fund the same goal in
    // sequence (a "Restructure" predecessor + successor), so saving a plan
    // updates the ONE row that already starts on that date — typically the
    // same plan being re-edited — and inserts a new row for a genuinely new
    // segment starting on a different date.
    public void Save(EarMarkPattern pattern)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO EarMarkPatterns
                (FinanceId, Amount, Frequency, IntervalValue, ByDay, ByMonthDay, StartDate, UntilDate, ActiveFrom, StartingAllocation)
            VALUES
                ($FinanceId, $Amount, $Frequency, $IntervalValue, $ByDay, $ByMonthDay, $StartDate, $UntilDate, $ActiveFrom, $StartingAllocation)
            ON CONFLICT(FinanceId, StartDate) DO UPDATE SET
                Amount = excluded.Amount,
                Frequency = excluded.Frequency,
                IntervalValue = excluded.IntervalValue,
                ByDay = excluded.ByDay,
                ByMonthDay = excluded.ByMonthDay,
                UntilDate = excluded.UntilDate,
                ActiveFrom = excluded.ActiveFrom,
                StartingAllocation = excluded.StartingAllocation;
            """;

        command.Parameters.AddWithValue("$FinanceId", pattern.FinanceId);
        command.Parameters.AddWithValue("$Amount", pattern.Amount.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$StartingAllocation", pattern.StartingAllocation.ToString(CultureInfo.InvariantCulture));
        RecurrenceRuleColumns.AddParameters(command, pattern.DatePattern);

        command.ExecuteNonQuery();
    }

    // Re-validates against the linked goal on the way back out, same as
    // EarMarkPattern.Create does going in — a goal's own dates can't have
    // changed underneath it since this was saved, but this keeps both paths
    // going through the one place that enforces 3.11.2.a2.
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

    public void Delete(int financeId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        // The pattern's span is the fund jar's lifetime (planning/09), so the
        // jar's manual adjustments die with the pattern — otherwise they'd be
        // orphans pointing at a jar that no longer exists.
        command.CommandText = """
            DELETE FROM ManualEarmarks WHERE FinanceId = $FinanceId;
            DELETE FROM EarMarkPatterns WHERE FinanceId = $FinanceId;
            """;
        command.Parameters.AddWithValue("$FinanceId", financeId);
        command.ExecuteNonQuery();
    }
}
