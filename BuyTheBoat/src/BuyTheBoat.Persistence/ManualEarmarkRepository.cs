using System.Globalization;
using BuyTheBoat.Domain;

namespace BuyTheBoat.Persistence;

/// <summary>
/// Stores manual (one-off) jar adjustments.
/// The (FinanceId, EarmarkDate) primary key is the one-isolated-earmark-per-jar-per-day rule: saving onto a day that already has one replaces its amount.
/// </summary>
public sealed class ManualEarmarkRepository(PatternDatabase database, EarMarkPatternRepository earMarkPatterns)
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>[WRITES FILE] Creates a new manual earmark, or replaces the amount of the existing one on the same day.</summary>
    /// <param name="earmark">The manual earmark to save.</param>
    public void Save(ManualEarmark earmark)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ManualEarmarks (FinanceId, EarmarkDate, Amount)
            VALUES ($FinanceId, $EarmarkDate, $Amount)
            ON CONFLICT(FinanceId, EarmarkDate) DO UPDATE SET
                Amount = excluded.Amount;
            """;

        command.Parameters.AddWithValue("$FinanceId", earmark.FinanceId);
        command.Parameters.AddWithValue("$EarmarkDate", earmark.Date.ToString(DateFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$Amount", earmark.Amount.ToString(CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    /// <summary>[READS FILE] Returns every manual (one-off) earmark, validated against its linked savings plan.</summary>
    public IReadOnlyList<ManualEarmark> GetAll()
    {
        // Groups plans by FinanceId, since a goal can have several (plans running at once, or a break-off's earlier and later plan), so each earmark is checked against the plan covering its date.
        var plansByFinanceId = earMarkPatterns.GetAll()
            .GroupBy(pattern => pattern.FinanceId)
            .ToDictionary(group => group.Key, group => group.ToList());

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT FinanceId, EarmarkDate, Amount FROM ManualEarmarks ORDER BY EarmarkDate, FinanceId;";

        using var reader = command.ExecuteReader();
        var earmarks = new List<ManualEarmark>();
        while (reader.Read())
        {
            var financeId = reader.GetInt32(0);
            var date = DateOnly.ParseExact(reader.GetString(1), DateFormat, CultureInfo.InvariantCulture);

            // No plan for this finance id means the earmark's jar is gone, which is corrupt data.
            if (!plansByFinanceId.TryGetValue(financeId, out var candidatePlans))
            {
                throw new InvalidOperationException(
                    $"ManualEarmark for finance id {financeId} has no matching EarMarkPattern — data is corrupt.");
            }

            // Re-validates through ManualEarmark.Create against the plan whose span covers this date, so reads and writes enforce the plan-span-is-jar-lifetime rule in one place.
            // Falls back to the first plan so an earmark outside every span fails with ManualEarmark.Create's more specific error.
            var pattern = candidatePlans.Find(candidate => candidate.DatePattern.ActiveSpanContains(date))
                ?? candidatePlans[0];

            earmarks.Add(ManualEarmark.Create(
                new ManualEarmarkOptions
                {
                    FinanceId = financeId,
                    Date = date,
                    Amount = decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                },
                pattern));
        }

        return earmarks;
    }

    /// <summary>[DELETES] Removes one day's manual earmark.</summary>
    /// <param name="financeId">Which fund the earmark is on.</param>
    /// <param name="date">Which day's earmark to delete.</param>
    public void Delete(int financeId, DateOnly date)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ManualEarmarks WHERE FinanceId = $FinanceId AND EarmarkDate = $EarmarkDate;";
        command.Parameters.AddWithValue("$FinanceId", financeId);
        command.Parameters.AddWithValue("$EarmarkDate", date.ToString(DateFormat, CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }
}
