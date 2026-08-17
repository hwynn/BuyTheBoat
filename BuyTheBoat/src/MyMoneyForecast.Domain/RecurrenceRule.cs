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
    // the pattern starts producing occurrences. Null = no lead-in. Never affects
    // occurrence generation; used only for span/containment checks.
    public DateOnly? ActiveFrom { get; init; }

    // RFC 5545's own EXDATE — dates GetOccurrences skips even though the
    // schedule would otherwise land on them (redesign/planning/26-editing-
    // an-earmark-pattern.md, "the glut case," mechanism C). Deliberately NOT
    // validated against Start/Until/the pattern's own real occurrences here —
    // real EXDATE semantics treat a non-matching date as a harmless no-op,
    // not an error, and every existing range-narrowing operation (WithUntil,
    // PatternTruncation.EndOn, break-off's own predecessor truncation) would
    // otherwise risk throwing the moment it shortened a pattern past an
    // excluded date that used to be in range. Empty = nothing excluded, the
    // default for every existing caller.
    public IReadOnlyList<DateOnly> ExcludedDates { get; init; } = [];
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
    public IReadOnlyList<DateOnly> ExcludedDates { get; }

    /// <summary>[CALC] The date this pattern counts as active from — its ActiveFrom lead-in if one is set, otherwise its own Start.</summary>
    public DateOnly ActiveStart => ActiveFrom ?? Start;

    /// <summary>[CALC] Reports whether a date falls inside the pattern's active span (ActiveStart..Until) — the range a fund jar for it may exist in, wider than its occurrences when there is a lead-in.</summary>
    /// <param name="date">The date to check.</param>
    public bool ActiveSpanContains(DateOnly date) => date >= ActiveStart && date <= Until;

    /// <summary>[CALC] Returns a copy of this rule with its ActiveFrom lead-in set to the given date — everything else, including which dates it occurs on, stays the same.</summary>
    /// <param name="activeFrom">The new lead-in date.</param>
    public RecurrenceRule WithActiveFrom(DateOnly activeFrom) => Create(new RecurrenceRuleOptions
    {
        Frequency = Frequency,
        Start = Start,
        Interval = Interval,
        ByDay = ByDay,
        ByMonthDay = ByMonthDay,
        Until = Until,
        ActiveFrom = activeFrom,
        ExcludedDates = ExcludedDates,
    });

    /// <summary>[CALC] Returns a copy of this rule ending on the given date instead — Start, ActiveFrom, and everything else stay the same. Used to end a pattern early.</summary>
    /// <param name="until">The new end date.</param>
    public RecurrenceRule WithUntil(DateOnly until) => Create(new RecurrenceRuleOptions
    {
        Frequency = Frequency,
        Start = Start,
        Interval = Interval,
        ByDay = ByDay,
        ByMonthDay = ByMonthDay,
        Until = until,
        ActiveFrom = ActiveFrom,
        ExcludedDates = ExcludedDates,
    });

    /// <summary>[CALC] Returns a copy of this rule starting on the given date instead — Until, ActiveFrom, and everything else (including ExcludedDates) stay the same. Start plays no part in building the underlying recurrence pattern itself, so this is as safe a substitution as WithUntil's.</summary>
    /// <param name="start">The new start date.</param>
    public RecurrenceRule WithStart(DateOnly start) => Create(new RecurrenceRuleOptions
    {
        Frequency = Frequency,
        Start = start,
        Interval = Interval,
        ByDay = ByDay,
        ByMonthDay = ByMonthDay,
        Until = Until,
        ActiveFrom = ActiveFrom,
        ExcludedDates = ExcludedDates,
    });

    /// <summary>[CALC] Returns a copy of this rule with a new set of excluded dates (RFC 5545 EXDATE) — everything else, including the schedule itself, stays the same. Replaces the whole list rather than adding one at a time, so a caller removing a date doesn't need a separate method.</summary>
    /// <param name="excludedDates">The complete new list of dates to skip.</param>
    public RecurrenceRule WithExcludedDates(IReadOnlyList<DateOnly> excludedDates) => Create(new RecurrenceRuleOptions
    {
        Frequency = Frequency,
        Start = Start,
        Interval = Interval,
        ByDay = ByDay,
        ByMonthDay = ByMonthDay,
        Until = Until,
        ActiveFrom = ActiveFrom,
        ExcludedDates = excludedDates,
    });

    /// <summary>[CALC] Builds a RecurrenceRule from already-validated options and a resolved (non-null) Until date.</summary>
    /// <param name="options">The rule's frequency, start, interval, and day constraints.</param>
    /// <param name="resolvedUntil">The rule's actual end date — either the caller's own Until, or Count resolved to a date.</param>
    private RecurrenceRule(RecurrenceRuleOptions options, DateOnly resolvedUntil)
    {
        Start = options.Start;
        Frequency = options.Frequency;
        Interval = options.Interval;
        ByDay = options.ByDay;
        ByMonthDay = options.ByMonthDay;
        Until = resolvedUntil;
        ActiveFrom = options.ActiveFrom;
        ExcludedDates = options.ExcludedDates;
        _pattern = BuildPattern(options with { Until = resolvedUntil, Count = null });
    }

    /// <summary>[CALC] Creates a recurrence rule, resolving a Count into a concrete Until date — every constructed rule has an Until, never a Count, so downstream code has exactly one end-of-schedule shape to handle.</summary>
    /// <param name="options">The rule's frequency, start, interval, day constraints, and either an Until or a Count (exactly one).</param>
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

    /// <summary>[CALC] Works out the Until date N occurrences resolves to, by building a temporary count-bounded pattern purely to find its last occurrence, then discarding it.</summary>
    /// <param name="options">The rule's frequency, start, interval, and day constraints.</param>
    /// <param name="count">How many occurrences to resolve the end date from.</param>
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

    /// <summary>[CALC] Builds the underlying Ical.Net recurrence pattern from a rule's options.</summary>
    /// <param name="options">The rule's frequency, interval, day constraints, and end (Until or Count).</param>
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

    /// <summary>[CALC] Maps this project's RecurrenceFrequency to Ical.Net's own FrequencyType.</summary>
    /// <param name="frequency">The frequency to map.</param>
    private static FrequencyType ToFrequencyType(RecurrenceFrequency frequency) => frequency switch
    {
        RecurrenceFrequency.Daily => FrequencyType.Daily,
        RecurrenceFrequency.Weekly => FrequencyType.Weekly,
        RecurrenceFrequency.Monthly => FrequencyType.Monthly,
        RecurrenceFrequency.Yearly => FrequencyType.Yearly,
        _ => throw new ArgumentOutOfRangeException(nameof(frequency)),
    };

    /// <summary>[CALC] Converts an Ical.Net occurrence to a plain DateOnly.</summary>
    /// <param name="occurrence">The occurrence to convert.</param>
    private static DateOnly ToDateOnly(Occurrence occurrence) =>
        DateOnly.FromDateTime(occurrence.Period.StartTime.Value);

    /// <summary>[CALC] Returns every date this rule occurs on within a range, with ExcludedDates already left out. Always bounded — Until is always set by construction, so this never runs away.</summary>
    /// <param name="from">Start of the range to search; defaults to the rule's own Start.</param>
    /// <param name="to">End of the range to search; defaults to the rule's own Until.</param>
    public IReadOnlyList<DateOnly> GetOccurrences(DateOnly? from = null, DateOnly? to = null)
    {
        var searchStart = from ?? Start;
        var searchEnd = to ?? Until;

        var calendarEvent = new CalendarEvent
        {
            Start = new CalDateTime(Start.Year, Start.Month, Start.Day),
            RecurrenceRule = _pattern,
        };

        // EXDATE, RFC 5545's own way to skip specific dates without
        // reshaping the rule itself — every caller of GetOccurrences (the
        // forecast cascade, milestone trajectories, form previews) honors an
        // exclusion automatically just by going through here, with nothing
        // extra to wire up at any of those call sites.
        foreach (var excluded in ExcludedDates)
        {
            calendarEvent.ExceptionDates.Add(new CalDateTime(excluded.Year, excluded.Month, excluded.Day));
        }

        return calendarEvent
            .GetOccurrences(new CalDateTime(searchStart.Year, searchStart.Month, searchStart.Day))
            .Select(ToDateOnly)
            .TakeWhile(date => date <= searchEnd)
            .ToList();
    }

    /// <summary>[CALC] Returns this rule's RRULE string representation (e.g. "FREQ=WEEKLY;INTERVAL=2;UNTIL=..."), for display or storage.</summary>
    public string ToRruleString() => _pattern.ToString() ?? string.Empty;
}
