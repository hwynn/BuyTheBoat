using System.Globalization;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.Persistence;

// User-created one-off jar adjustments. The (FinanceId, EarmarkDate) primary
// key IS the documented one-isolated-earmark-per-jar-per-day merge rule:
// saving onto an occupied day replaces that day's manual amount.
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
        // Grouped by FinanceId, not keyed by it alone: a finance id can have
        // more than one EarMarkPattern (a second concurrent funder, or a
        // break-off predecessor+successor pair), so a stored earmark is
        // validated against whichever sibling plan actually covers its
        // date, not just whichever one happened to load first (see
        // Concurrent_plans_on_the_same_goal_dont_crash_GetAll_and_validate_against_the_right_one).
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

            // Re-validates against the linked earmark pattern on the way
            // out, same as ManualEarmark.Create going in — both paths go
            // through the one place that enforces the
            // pattern-span-is-jar-lifetime rule. A row whose pattern has
            // since shrunk its span would throw here;
            // EarMarkPatternRepository.Delete's own cascade prevents the
            // orphan case, and span edits are the restructuring feature's
            // own concern.
            if (!plansByFinanceId.TryGetValue(financeId, out var candidatePlans))
            {
                throw new InvalidOperationException(
                    $"ManualEarmark for finance id {financeId} has no matching EarMarkPattern — data is corrupt.");
            }

            // Prefer whichever sibling plan's own span actually covers this
            // date; fall back to the first so a genuinely out-of-span earmark
            // still fails through ManualEarmark.Create's own, more specific
            // error below rather than a generic one here.
            var pattern = candidatePlans.Find(candidate => candidate.DatePattern.Start <= date && date <= candidate.DatePattern.Until)
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
