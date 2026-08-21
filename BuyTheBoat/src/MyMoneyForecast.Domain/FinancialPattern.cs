namespace MyMoneyForecast.Domain;

// A bill or a paycheck are the same thing here, distinguished only by the sign
// of Amount — see redesign/01-glossary-of-terms.md#financialpattern. One-time
// goals (e.g. "trip to Japan") are also a FinancialPattern, just with a
// DatePattern that has a single occurrence.
public sealed record FinancialPatternOptions
{
    public required int FinanceId { get; init; }
    public required string Source { get; init; }
    public required RecurrenceRule DatePattern { get; init; }
    public required decimal Amount { get; init; }
    public int Priority { get; init; }

    // null defers to "true if Amount is negative" — the documented default.
    public bool? Mandatory { get; init; }
    public string? Description { get; init; }

    // DIVERGENCE(auto-renew): a marker set invisibly when the user answers
    // "it just keeps going" at creation — gates a still-unbuilt scheduled
    // check that silently renews the pattern (BreakOffFactory.Renew) instead
    // of letting it end. Never affects the rrule, the math, or occurrence
    // generation; purely a flag for that future background check.
    public bool AutoRenew { get; init; }
}

public sealed class FinancialPattern
{
    public int FinanceId { get; }
    public string Source { get; }
    public RecurrenceRule DatePattern { get; }
    public decimal Amount { get; }
    public int Priority { get; }
    public bool Mandatory { get; }
    public string? Description { get; }
    public bool AutoRenew { get; }

    /// <summary>[CALC] Builds a FinancialPattern from already-validated options.</summary>
    /// <param name="options">The pattern's source, schedule, amount, and other fields.</param>
    /// <param name="mandatory">The resolved Mandatory value (options.Mandatory, or defaulted from the amount's sign).</param>
    private FinancialPattern(FinancialPatternOptions options, bool mandatory)
    {
        FinanceId = options.FinanceId;
        Source = options.Source;
        DatePattern = options.DatePattern;
        Amount = options.Amount;
        Priority = options.Priority;
        Mandatory = mandatory;
        Description = options.Description;
        AutoRenew = options.AutoRenew;
    }

    /// <summary>[CALC] Creates a bill, paycheck, or goal pattern, validating that the source name isn't blank.</summary>
    /// <param name="options">The pattern's source, schedule, amount, and other fields.</param>
    public static FinancialPattern Create(FinancialPatternOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Source))
        {
            // 4.2.a1: source cannot be None.
            throw new ArgumentException("Source cannot be empty.", nameof(options));
        }

        var mandatory = options.Mandatory ?? options.Amount < 0;
        return new FinancialPattern(options, mandatory);
    }

    /// <summary>[CALC] Returns a copy of this pattern whose date pattern is active from the given (earlier) date — so a plan and jar for it can begin before its first occurrence — with its occurrences and every other field unchanged.</summary>
    /// <param name="activeFrom">The new, earlier active-from date.</param>
    public FinancialPattern WithActiveFrom(DateOnly activeFrom) => Create(new FinancialPatternOptions
    {
        FinanceId = FinanceId,
        Source = Source,
        DatePattern = DatePattern.WithActiveFrom(activeFrom),
        Amount = Amount,
        Priority = Priority,
        Mandatory = Mandatory,
        Description = Description,
        AutoRenew = AutoRenew,
    });

    /// <summary>[CALC] Returns a copy of this pattern ending on the given (earlier) date instead — every other field, including occurrences up to that date, unchanged. Used to end a pattern early.</summary>
    /// <param name="until">The new, earlier end date.</param>
    public FinancialPattern WithUntil(DateOnly until) => Create(new FinancialPatternOptions
    {
        FinanceId = FinanceId,
        Source = Source,
        DatePattern = DatePattern.WithUntil(until),
        Amount = Amount,
        Priority = Priority,
        Mandatory = Mandatory,
        Description = Description,
        AutoRenew = AutoRenew,
    });

    /// <summary>[CALC] Returns a copy with its rrule DtStart anchor moved to the given date — every other field unchanged. WARNING: this re-phases an interval>1 (or implicit-by-rule) cadence, since DtStart is the RFC 5545 anchor; use it only when re-anchoring the rhythm is intended (a direct Start edit). To relink a chain neighbor while keeping its rhythm, use ReanchoredToStartOn.</summary>
    /// <param name="start">The new rrule anchor date.</param>
    public FinancialPattern WithStart(DateOnly start) => Create(new FinancialPatternOptions
    {
        FinanceId = FinanceId,
        Source = Source,
        DatePattern = DatePattern.WithStart(start),
        Amount = Amount,
        Priority = Priority,
        Mandatory = Mandatory,
        Description = Description,
        AutoRenew = AutoRenew,
    });

    /// <summary>[CALC] Returns a copy re-anchored so its active span begins on the given date, keeping its cadence phase — every other field unchanged. Used by BreakOffFactory's chain-boundary resolution to nudge a successor's own start to stay contiguous without silently re-phasing an interval>1 neighbor.</summary>
    /// <param name="newActiveStart">Where the re-anchored pattern's active span should begin.</param>
    public FinancialPattern ReanchoredToStartOn(DateOnly newActiveStart) => Create(new FinancialPatternOptions
    {
        FinanceId = FinanceId,
        Source = Source,
        DatePattern = DatePattern.ReanchoredToStartOn(newActiveStart),
        Amount = Amount,
        Priority = Priority,
        Mandatory = Mandatory,
        Description = Description,
        AutoRenew = AutoRenew,
    });
}
