using System.Globalization;
using Microsoft.Data.Sqlite;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.Persistence;

// Shared between FinancialPatternRepository and EarMarkPatternRepository —
// both tables store a RecurrenceRule using the same six columns.
internal static class RecurrenceRuleColumns
{
    private const string DateFormat = "yyyy-MM-dd";

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
    }

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

        return RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = frequency,
            Start = start,
            Interval = interval,
            ByDay = byDay,
            ByMonthDay = byMonthDay,
            Until = until,
        });
    }

    private static string ReadString(SqliteDataReader reader, string columnName) =>
        reader.GetString(reader.GetOrdinal(columnName));

    private static string? ReadNullableString(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
