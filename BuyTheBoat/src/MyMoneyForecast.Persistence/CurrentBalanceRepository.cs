using System.Globalization;

namespace MyMoneyForecast.Persistence;

public sealed record CurrentBalance(decimal Balance, DateOnly AsOfDate, DateOnly HorizonEndDate);

// Single mutable row (see the CHECK (Id = 1) constraint in PatternDatabase) —
// there's nothing to version yet, since a Forecast is recomputed fresh from
// this seed value every time rather than persisted itself.
public sealed class CurrentBalanceRepository(PatternDatabase database)
{
    private const string DateFormat = "yyyy-MM-dd";

    public void Save(decimal balance, DateOnly asOfDate, DateOnly horizonEndDate)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CurrentBalance (Id, Balance, AsOfDate, HorizonEndDate)
            VALUES (1, $Balance, $AsOfDate, $HorizonEndDate)
            ON CONFLICT(Id) DO UPDATE SET
                Balance = excluded.Balance,
                AsOfDate = excluded.AsOfDate,
                HorizonEndDate = excluded.HorizonEndDate;
            """;

        command.Parameters.AddWithValue("$Balance", balance.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$AsOfDate", asOfDate.ToString(DateFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$HorizonEndDate", horizonEndDate.ToString(DateFormat, CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    public CurrentBalance? GetCurrent()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Balance, AsOfDate, HorizonEndDate FROM CurrentBalance WHERE Id = 1;";

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var balance = decimal.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
        var asOfDate = DateOnly.ParseExact(reader.GetString(1), DateFormat, CultureInfo.InvariantCulture);

        // Rows saved before the horizon became user-adjustable won't have
        // this column populated — fall back to the original fixed default
        // rather than surfacing a blank or invalid date.
        var horizonEndDate = reader.IsDBNull(2)
            ? asOfDate.AddMonths(3)
            : DateOnly.ParseExact(reader.GetString(2), DateFormat, CultureInfo.InvariantCulture);

        return new CurrentBalance(balance, asOfDate, horizonEndDate);
    }
}
