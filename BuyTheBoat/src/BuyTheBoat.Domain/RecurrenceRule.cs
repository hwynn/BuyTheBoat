using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace BuyTheBoat.Domain;

public enum RecurrenceFrequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly,
}

// Everything an EarMarkPattern/FinancialPattern's date_pattern needs, per
// planning/01-glossary-of-terms.md. Deliberately mirrors the RRULE fields used
// throughout mini_fund_project/goals.xlsx (freq, dstart, interval, byday,
// bymonthday, count, until) rather than exposing the full RFC 5545 surface.
public sealed record RecurrenceRuleOptions
{
    public required RecurrenceFrequency Frequency { get; init; }

    // The rrule DTSTART anchor, only for building the recurrence — NOT the
    // pattern's start (an ActiveFrom lead-in, or a one-time far-future event,
    // can push it past the real start; ActiveStart is the pattern's start).
    public required DateOnly DtStart { get; init; }

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
    // schedule would otherwise land on them ("the glut case," mechanism C). Deliberately NOT
    // validated against DtStart/Until/the pattern's own real occurrences here —
    // real EXDATE semantics treat a non-matching date as a harmless no-op,
    // not an error, and every existing range-narrowing operation (WithUntil,
    // PatternTruncation.EndOn, break-off's own predecessor truncation) would
    // otherwise risk throwing the moment it shortened a pattern past an
    // excluded date that used to be in range. Empty = nothing excluded, the
    // default for every existing caller.
    public IReadOnlyList<DateOnly> ExcludedDates { get; init; } = [];
}

// The chart-only rule found in planning/05-assumption-chart-full-text.md
// ("self.date_pattern rrule must have until property, not count property")
// is enforced here structurally: Count is accepted as input but Until is the
// only thing that exists on a constructed RecurrenceRule. Mirrors
// mini_fund_project/MmfUtility.py's count_to_until_rrule.
public sealed class RecurrenceRule
{
    private readonly RecurrencePattern _pattern;

    // The rrule DTSTART anchor: the pattern's persistence key and the date its
    // occurrences are counted from. NOT the pattern's start — a lead-in (or a
    // one-time far-future event) can push it past the real start. Use
    // ActiveStart for "when does this begin"; read DtStart only for persistence
    // keys and occurrence-anchor logic.
    public DateOnly DtStart { get; }
    public RecurrenceFrequency Frequency { get; }
    public int Interval { get; }
    public IReadOnlyList<DayOfWeek> ByDay { get; }
    public IReadOnlyList<int> ByMonthDay { get; }
    public DateOnly Until { get; }

    // The optional lead-in — a date the pattern counts as active from, earlier
    // than its first occurrence. Private; ActiveStart folds it in.
    private DateOnly? ActiveFrom { get; }
    public IReadOnlyList<DateOnly> ExcludedDates { get; }

    /// <summary>[CALC] The pattern's real start — its ActiveFrom lead-in if one is set, otherwise its rrule DtStart anchor. This is what "start" means to callers; DtStart is an internal rrule detail.</summary>
    public DateOnly ActiveStart => ActiveFrom ?? DtStart;

    /// <summary>[CALC] Reports whether a date falls inside the pattern's active span (ActiveStart..Until) — the range a fund jar for it may exist in, wider than its occurrences when there is a lead-in.</summary>
    /// <param name="date">The date to check.</param>
    public bool ActiveSpanContains(DateOnly date) => date >= ActiveStart && date <= Until;

    /// <summary>[CALC] Whether this pattern's active span ends exactly the day before another's begins — the back-to-back, no-gap-no-overlap shape that makes two patterns chain neighbors. Keyed on ActiveStart, not the rrule anchor.</summary>
    /// <param name="next">The pattern that would come immediately after this one.</param>
    public bool ImmediatelyPrecedes(RecurrenceRule next) => Until.AddDays(1) == next.ActiveStart;

    /// <summary>[CALC] Whether this pattern's whole active span sits inside another's — the containment an earmark pattern must keep against its goal (assumption 3.11.2.a2).</summary>
    /// <param name="outer">The pattern whose span must contain this one's.</param>
    public bool ActiveSpanWithin(RecurrenceRule outer) => ActiveStart >= outer.ActiveStart && Until <= outer.Until;

    /// <summary>[CALC] Whether this pattern's active span shares any day with another's — what tells a genuinely concurrent pair apart from strictly-sequential chain neighbors.</summary>
    /// <param name="other">The pattern to check for an overlapping span.</param>
    public bool ActiveSpansOverlap(RecurrenceRule other) => ActiveStart <= other.Until && other.ActiveStart <= Until;

    /// <summary>[CALC] Whether two rules share the same recurrence SHAPE — Frequency, Interval, ByDay, and ByMonthDay — ignoring where each begins and ends. What tells an amount-only edit apart from one that also moves the occurrence dates.</summary>
    /// <param name="other">The rule to compare shapes with.</param>
    public bool HasSameShapeAs(RecurrenceRule other) =>
        Frequency == other.Frequency
        && Interval == other.Interval
        && ByDay.SequenceEqual(other.ByDay)
        && ByMonthDay.SequenceEqual(other.ByMonthDay);

    /// <summary>[CALC] Whether a single rule spanning both this and <paramref name="other"/> would land on exactly the union of their occurrences — nothing shifted, added, or dropped. Needs the same recurrence shape (Frequency/Interval/ByDay/ByMonthDay), the same cadence, and no gap between them a merged rule would fill in. The rrule half of EarMarkPattern.CanJoinWithoutConsequence (the silent join); a pair whose merge WOULD move a date is a genuine difference, left for a with-consequence consolidation instead.</summary>
    /// <param name="other">The rule to test merging with.</param>
    public bool CanMergeWith(RecurrenceRule other)
    {
        if (!HasSameShapeAs(other))
        {
            return false;
        }

        var mergedStart = ActiveStart < other.ActiveStart ? ActiveStart : other.ActiveStart;
        var mergedUntil = Until > other.Until ? Until : other.Until;
        var merged = ReanchoredToStartOn(mergedStart).WithUntil(mergedUntil);

        var union = GetOccurrences()
            .Concat(other.GetOccurrences())
            .Distinct()
            .OrderBy(date => date)
            .ToList();

        return merged.GetOccurrences().SequenceEqual(union);
    }

    /// <summary>[CALC] This rule's full state as options, raw DtStart anchor and ActiveFrom included — for persistence/serialization that must round-trip the exact rule. Ordinary callers want ActiveStart and the span methods, not this.</summary>
    public RecurrenceRuleOptions ToOptions() => new()
    {
        Frequency = Frequency,
        DtStart = DtStart,
        Interval = Interval,
        ByDay = ByDay,
        ByMonthDay = ByMonthDay,
        Until = Until,
        ActiveFrom = ActiveFrom,
        ExcludedDates = ExcludedDates,
    };

    /// <summary>[CALC] A copy whose active span begins on the given date while KEEPING its cadence — the safe replacement for WithStart when relinking a chain. Its occurrences stay on the same days (unlike WithStart, which shifts an interval>1 or implicit-by-rule pattern onto different days); only where the span begins, and how far back that cadence reaches when it grows earlier, changes. Until, amount, and shape are untouched.</summary>
    /// <param name="newActiveStart">Where the pattern's active span should begin.</param>
    public RecurrenceRule ReanchoredToStartOn(DateOnly newActiveStart)
    {
        // Walk this rule's own anchor back by whole cadence periods until it
        // sits on or before the new start, so its recurring days are defined
        // across the whole new span — needed when a segment grows backward.
        // Stepping by exact periods never changes which dates it lands on, only
        // how early it can be enumerated from.
        var anchor = DtStart;
        for (var guard = 0; anchor > newActiveStart && guard < 6000; guard++)
        {
            anchor = StepBackOnePeriod(anchor);
        }

        // The new DtStart is the first occurrence on or after the new start; a
        // lead-in (ActiveFrom) fills the gap when it lands later, so the active
        // span still begins exactly where asked.
        var grid = Create(new RecurrenceRuleOptions
        {
            Frequency = Frequency,
            Interval = Interval,
            ByDay = ByDay,
            ByMonthDay = ByMonthDay,
            DtStart = anchor,
            Until = Until >= anchor ? Until : anchor,
            ExcludedDates = ExcludedDates,
        });
        var onOrAfter = grid.GetOccurrences(newActiveStart, grid.Until);
        var newDtStart = onOrAfter.Count > 0 ? onOrAfter[0] : newActiveStart;

        return Create(new RecurrenceRuleOptions
        {
            Frequency = Frequency,
            Interval = Interval,
            ByDay = ByDay,
            ByMonthDay = ByMonthDay,
            DtStart = newDtStart,
            ActiveFrom = newDtStart > newActiveStart ? newActiveStart : null,
            Until = Until,
            ExcludedDates = ExcludedDates,
        });
    }

    /// <summary>[CALC] This date one cadence period earlier — a day/week/month/year step scaled by Interval — for walking an anchor back without shifting the cadence.</summary>
    /// <param name="date">The date to step back from.</param>
    private DateOnly StepBackOnePeriod(DateOnly date) => Frequency switch
    {
        RecurrenceFrequency.Daily => date.AddDays(-Interval),
        RecurrenceFrequency.Weekly => date.AddDays(-7 * Interval),
        RecurrenceFrequency.Monthly => date.AddMonths(-Interval),
        RecurrenceFrequency.Yearly => date.AddYears(-Interval),
        _ => throw new ArgumentOutOfRangeException(nameof(Frequency), Frequency, "Unknown frequency."),
    };

    /// <summary>[CALC] Returns a copy of this rule with its ActiveFrom lead-in set to the given date — everything else, including which dates it occurs on, stays the same.</summary>
    /// <param name="activeFrom">The new lead-in date.</param>
    public RecurrenceRule WithActiveFrom(DateOnly activeFrom) => Create(new RecurrenceRuleOptions
    {
        Frequency = Frequency,
        DtStart = DtStart,
        Interval = Interval,
        ByDay = ByDay,
        ByMonthDay = ByMonthDay,
        Until = Until,
        ActiveFrom = activeFrom,
        ExcludedDates = ExcludedDates,
    });

    /// <summary>[CALC] Returns a copy of this rule ending on the given date instead — DtStart, ActiveFrom, and everything else stay the same. Used to end a pattern early.</summary>
    /// <param name="until">The new end date.</param>
    public RecurrenceRule WithUntil(DateOnly until) => Create(new RecurrenceRuleOptions
    {
        Frequency = Frequency,
        DtStart = DtStart,
        Interval = Interval,
        ByDay = ByDay,
        ByMonthDay = ByMonthDay,
        Until = until,
        ActiveFrom = ActiveFrom,
        ExcludedDates = ExcludedDates,
    });

    /// <summary>[CALC] Returns a copy with its rrule DtStart anchor moved to the given date — Until, ActiveFrom, and the rest stay the same. WARNING: for interval>1 or an implicit by-rule this shifts the whole cadence onto different days (DtStart is the RFC 5545 anchor), so use it only when moving which days it lands on is actually intended; ReanchoredToStartOn keeps the cadence instead.</summary>
    /// <param name="start">The new rrule anchor date.</param>
    public RecurrenceRule WithStart(DateOnly start) => Create(new RecurrenceRuleOptions
    {
        Frequency = Frequency,
        DtStart = start,
        Interval = Interval,
        ByDay = ByDay,
        ByMonthDay = ByMonthDay,
        Until = Until,
        ActiveFrom = ActiveFrom,
        ExcludedDates = ExcludedDates,
    });

    /// <summary>[CALC] A copy of this rule wearing another's recurrence shape — its Frequency, Interval, ByDay, ByMonthDay — while keeping this rule's own dates (DtStart anchor, Until, ActiveFrom, ExcludedDates). For cascading an amount/shape change onto a later segment without moving where it sits.</summary>
    /// <param name="shape">The rule whose recurrence shape to take on.</param>
    public RecurrenceRule WithShapeOf(RecurrenceRule shape) => Create(new RecurrenceRuleOptions
    {
        Frequency = shape.Frequency,
        Interval = shape.Interval,
        ByDay = shape.ByDay,
        ByMonthDay = shape.ByMonthDay,
        DtStart = DtStart,
        Until = Until,
        ActiveFrom = ActiveFrom,
        ExcludedDates = ExcludedDates,
    });

    /// <summary>[CALC] Returns a copy of this rule with a new set of excluded dates (RFC 5545 EXDATE) — everything else, including the schedule itself, stays the same. Replaces the whole list rather than adding one at a time, so a caller removing a date doesn't need a separate method.</summary>
    /// <param name="excludedDates">The complete new list of dates to skip.</param>
    public RecurrenceRule WithExcludedDates(IReadOnlyList<DateOnly> excludedDates) => Create(new RecurrenceRuleOptions
    {
        Frequency = Frequency,
        DtStart = DtStart,
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
        DtStart = options.DtStart;
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

        if (options.ActiveFrom is { } activeFrom && activeFrom > options.DtStart)
        {
            throw new ArgumentException(
                "ActiveFrom cannot be after DtStart — it is a lead-in before the first occurrence.",
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
            Start = new CalDateTime(options.DtStart.Year, options.DtStart.Month, options.DtStart.Day),
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
    /// <param name="from">Start of the range to search; defaults to the rule's own DtStart.</param>
    /// <param name="to">End of the range to search; defaults to the rule's own Until.</param>
    public IReadOnlyList<DateOnly> GetOccurrences(DateOnly? from = null, DateOnly? to = null)
    {
        var searchStart = from ?? DtStart;
        var searchEnd = to ?? Until;

        var calendarEvent = new CalendarEvent
        {
            Start = new CalDateTime(DtStart.Year, DtStart.Month, DtStart.Day),
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
