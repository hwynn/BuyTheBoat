using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace MyMoneyForecast.Domain;

public enum RecurrenceFrequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly,
}

// Everything an EarMarkPattern/FinancialPattern's date_pattern needs, per
// redesign/01-glossary-of-terms.md. Deliberately mirrors the RRULE fields used
// throughout mini_fund_project/goals.xlsx (freq, dstart, interval, byday,
// bymonthday, count, until) rather than exposing the full RFC 5545 surface.
public sealed record RecurrenceRuleOptions
{
    public required RecurrenceFrequency Frequency { get; init; }
    public required DateOnly Start { get; init; }
    public int Interval { get; init; } = 1;
    public IReadOnlyList<DayOfWeek> ByDay { get; init; } = [];
    public IReadOnlyList<int> ByMonthDay { get; init; } = [];

    // Exactly one of these must be set. Count is a convenience for entry only —
    // see RecurrenceRule.Until for why it never survives construction.
    public DateOnly? Until { get; init; }
    public int? Count { get; init; }

    // DIVERGENCE(active-from): a "lead-in" date the pattern counts as active FROM
    // — earlier than its first occurrence — so a fund jar for it can exist before
    // the pattern starts producing occurrences (planning/15). A new property not
    // in the documented model. Null = no lead-in. Never affects occurrence
    // generation; used only for span/containment checks.
    public DateOnly? ActiveFrom { get; init; }
}

// The chart-only rule found in redesign/05-assumption-dependency-graph.md
// ("self.date_pattern rrule must have until property, not count property")
// is enforced here structurally: Count is accepted as input but Until is the
// only thing that exists on a constructed RecurrenceRule. Mirrors
// mini_fund_project/MmfUtility.py's count_to_until_rrule.
public sealed class RecurrenceRule
{
    private readonly RecurrencePattern _pattern;

    public DateOnly Start { get; }
    public RecurrenceFrequency Frequency { get; }
    public int Interval { get; }
    public IReadOnlyList<DayOfWeek> ByDay { get; }
    public IReadOnlyList<int> ByMonthDay { get; }
    public DateOnly Until { get; }
    public DateOnly? ActiveFrom { get; }

    /// <summary>[CALC] The date this pattern counts as active from — its ActiveFrom lead-in if one is set, otherwise its own Start.</summary>
    public DateOnly ActiveStart => ActiveFrom ?? Start;

    /// <summary>[CALC] Whether a date falls inside the pattern's active span (ActiveStart..Until) — the range a fund jar for it may exist in, wider than its occurrences when there is a lead-in.</summary>
    public bool ActiveSpanContains(DateOnly date) => date >= ActiveStart && date <= Until;

    /// <summary>[CALC] A copy of this rule with its ActiveFrom lead-in set to the given date — everything else, including which dates it occurs on, stays the same.</summary>
    public RecurrenceRule WithActiveFrom(DateOnly activeFrom) => Create(new RecurrenceRuleOptions
    {
        Frequency = Frequency,
        Start = Start,
        Interval = Interval,
        ByDay = ByDay,
        ByMonthDay = ByMonthDay,
        Until = Until,
        ActiveFrom = activeFrom,
    });

    private RecurrenceRule(RecurrenceRuleOptions options, DateOnly resolvedUntil)
    {
        Start = options.Start;
        Frequency = options.Frequency;
        Interval = options.Interval;
        ByDay = options.ByDay;
        ByMonthDay = options.ByMonthDay;
        Until = resolvedUntil;
        ActiveFrom = options.ActiveFrom;
        _pattern = BuildPattern(options with { Until = resolvedUntil, Count = null });
    }

    public static RecurrenceRule Create(RecurrenceRuleOptions options)
    {
        if (options.Interval < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Interval must be at least 1.");
        }

        if (options.ActiveFrom is { } activeFrom && activeFrom > options.Start)
        {
            throw new ArgumentException(
                "ActiveFrom cannot be after Start — it is a lead-in before the first occurrence.",
                nameof(options));
        }

        switch (options.Until, options.Count)
        {
            case (null, null):
                throw new ArgumentException("Either Until or Count must be set.", nameof(options));
            case (not null, not null):
                throw new ArgumentException("Only one of Until or Count may be set.", nameof(options));
            case (not null, null):
                return new RecurrenceRule(options, options.Until.Value);
            case (null, { } count):
                var resolvedUntil = ResolveCountToUntil(options, count);
                return new RecurrenceRule(options, resolvedUntil);
        }
    }

    // Builds a temporary count-bounded pattern purely to find its last
    // occurrence, then discards it — the returned RecurrenceRule never has a
    // Count-based pattern, only the resolved Until date.
    private static DateOnly ResolveCountToUntil(RecurrenceRuleOptions options, int count)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Count must be at least 1.");
        }

        var countPattern = BuildPattern(options with { Until = null, Count = count });
        var calendarEvent = new CalendarEvent
        {
            Start = new CalDateTime(options.Start.Year, options.Start.Month, options.Start.Day),
            RecurrenceRule = countPattern,
        };

        var lastOccurrence = calendarEvent
            .GetOccurrences(calendarEvent.Start)
            .Select(ToDateOnly)
            .Last();

        return lastOccurrence;
    }

    private static RecurrencePattern BuildPattern(RecurrenceRuleOptions options)
    {
        var pattern = new RecurrencePattern(ToFrequencyType(options.Frequency), options.Interval);

        if (options.ByDay.Count > 0)
        {
            pattern.ByDay = options.ByDay.Select(day => new WeekDay(day)).ToList();
        }

        if (options.ByMonthDay.Count > 0)
        {
            pattern.ByMonthDay = options.ByMonthDay.ToList();
        }

        if (options.Until is { } until)
        {
            pattern.Until = new CalDateTime(until.Year, until.Month, until.Day);
        }

        if (options.Count is { } count)
        {
            pattern.Count = count;
        }

        return pattern;
    }

    private static FrequencyType ToFrequencyType(RecurrenceFrequency frequency) => frequency switch
    {
        RecurrenceFrequency.Daily => FrequencyType.Daily,
        RecurrenceFrequency.Weekly => FrequencyType.Weekly,
        RecurrenceFrequency.Monthly => FrequencyType.Monthly,
        RecurrenceFrequency.Yearly => FrequencyType.Yearly,
        _ => throw new ArgumentOutOfRangeException(nameof(frequency)),
    };

    private static DateOnly ToDateOnly(Occurrence occurrence) =>
        DateOnly.FromDateTime(occurrence.Period.StartTime.Value);

    // Bounded by construction: Until is always set, so this never runs away.
    public IReadOnlyList<DateOnly> GetOccurrences(DateOnly? from = null, DateOnly? to = null)
    {
        var searchStart = from ?? Start;
        var searchEnd = to ?? Until;

        var calendarEvent = new CalendarEvent
        {
            Start = new CalDateTime(Start.Year, Start.Month, Start.Day),
            RecurrenceRule = _pattern,
        };

        return calendarEvent
            .GetOccurrences(new CalDateTime(searchStart.Year, searchStart.Month, searchStart.Day))
            .Select(ToDateOnly)
            .TakeWhile(date => date <= searchEnd)
            .ToList();
    }

    public string ToRruleString() => _pattern.ToString() ?? string.Empty;
}
