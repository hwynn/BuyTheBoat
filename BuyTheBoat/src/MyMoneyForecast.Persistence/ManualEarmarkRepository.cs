using System.Globalization;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.Persistence;

// The rewrite's first persisted EVENT (everything else stored is patterns +
// the balance row): user-created one-off jar adjustments — see
// planning/09-manual-earmarks.md. The (FinanceId, EarmarkDate) primary key IS
// the documented one-isolated-earmark-per-jar-per-day merge rule: saving onto
// an occupied day replaces that day's manual amount.
public sealed class ManualEarmarkRepository(PatternDatabase database, EarMarkPatternRepository earMarkPatterns)
{
    private const string DateFormat = "yyyy-MM-dd";

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

    // Re-validates against the linked earmark pattern on the way out, same as
    // ManualEarmark.Create going in — both paths go through the one place
    // that enforces the pattern-span-is-jar-lifetime rule. A row whose
    // pattern has since shrunk its span would throw here; Delete-with-pattern
    // (below) prevents the orphan case, and span edits are the restructuring
    // feature's concern.
    public IReadOnlyList<ManualEarmark> GetAll()
    {
        var patternsById = earMarkPatterns.GetAll().ToDictionary(pattern => pattern.FinanceId);

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT FinanceId, EarmarkDate, Amount FROM ManualEarmarks ORDER BY EarmarkDate, FinanceId;";

        using var reader = command.ExecuteReader();
        var earmarks = new List<ManualEarmark>();
        while (reader.Read())
        {
            var financeId = reader.GetInt32(0);
            if (!patternsById.TryGetValue(financeId, out var pattern))
            {
                throw new InvalidOperationException(
                    $"ManualEarmark for finance id {financeId} has no matching EarMarkPattern — data is corrupt.");
            }

            earmarks.Add(ManualEarmark.Create(
                new ManualEarmarkOptions
                {
                    FinanceId = financeId,
                    Date = DateOnly.ParseExact(reader.GetString(1), DateFormat, CultureInfo.InvariantCulture),
                    Amount = decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                },
                pattern));
        }

        return earmarks;
    }

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
