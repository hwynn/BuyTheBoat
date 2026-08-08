using System.Globalization;
using Microsoft.Data.Sqlite;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.Persistence;

// Shared between FinancialPatternRepository and EarMarkPatternRepository —
// both tables store a RecurrenceRule using the same six columns.
internal static class RecurrenceRuleColumns
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>[WRITES FILE] Adds a recurrence rule's six columns as SQL parameters, ready for an INSERT/UPDATE. Shared by FinancialPatternRepository and EarMarkPatternRepository, since both tables store a rule the same way.</summary>
    /// <param name="command">The command to add parameters to.</param>
    /// <param name="rule">The rule to serialize.</param>
    public static void AddParameters(SqliteCommand command, RecurrenceRule rule)
    {
        command.Parameters.AddWithValue("$Frequency", rule.Frequency.ToString());
        command.Parameters.AddWithValue("$IntervalValue", rule.Interval);
        command.Parameters.AddWithValue(
            "$ByDay", rule.ByDay.Count > 0 ? string.Join(',', rule.ByDay) : DBNull.Value);
        command.Parameters.AddWithValue(
            "$ByMonthDay", rule.ByMonthDay.Count > 0 ? string.Join(',', rule.ByMonthDay) : DBNull.Value);
        command.Parameters.AddWithValue("$StartDate", rule.Start.ToString(DateFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$UntilDate", rule.Until.ToString(DateFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$ActiveFrom",
            rule.ActiveFrom is { } activeFrom
                ? activeFrom.ToString(DateFormat, CultureInfo.InvariantCulture)
                : DBNull.Value);
    }

    /// <summary>[CALC] Builds a RecurrenceRule from a row's six recurrence columns.</summary>
    /// <param name="reader">The reader, positioned on the row to read.</param>
    public static RecurrenceRule Read(SqliteDataReader reader)
    {
        var frequency = Enum.Parse<RecurrenceFrequency>(ReadString(reader, "Frequency"));
        var interval = reader.GetInt32(reader.GetOrdinal("IntervalValue"));

        var byDayText = ReadNullableString(reader, "ByDay");
        var byDay = byDayText is null
            ? []
            : byDayText.Split(',').Select(Enum.Parse<DayOfWeek>).ToList();

        var byMonthDayText = ReadNullableString(reader, "ByMonthDay");
        var byMonthDay = byMonthDayText is null
            ? []
            : byMonthDayText.Split(',').Select(int.Parse).ToList();

        var start = DateOnly.ParseExact(ReadString(reader, "StartDate"), DateFormat, CultureInfo.InvariantCulture);
        var until = DateOnly.ParseExact(ReadString(reader, "UntilDate"), DateFormat, CultureInfo.InvariantCulture);

        var activeFromText = ReadNullableString(reader, "ActiveFrom");
        var activeFrom = activeFromText is null
            ? (DateOnly?)null
            : DateOnly.ParseExact(activeFromText, DateFormat, CultureInfo.InvariantCulture);

        return RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = frequency,
            Start = start,
            Interval = interval,
            ByDay = byDay,
            ByMonthDay = byMonthDay,
            Until = until,
            ActiveFrom = activeFrom,
        });
    }

    /// <summary>[CALC] Reads a non-null string column by name.</summary>
    /// <param name="reader">The reader, positioned on the row to read.</param>
    /// <param name="columnName">The column to read.</param>
    private static string ReadString(SqliteDataReader reader, string columnName) =>
        reader.GetString(reader.GetOrdinal(columnName));

    /// <summary>[CALC] Reads a nullable string column by name, or null if it's DB null.</summary>
    /// <param name="reader">The reader, positioned on the row to read.</param>
    /// <param name="columnName">The column to read.</param>
    private static string? ReadNullableString(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
