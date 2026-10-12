namespace BuyTheBoat.Domain;

/// <summary>The fields for building a FinancialPattern with FinancialPattern.Create.</summary>
public sealed record FinancialPatternOptions
{
    public required int FinanceId { get; init; }
    public required string Source { get; init; }
    public required RecurrenceRule DatePattern { get; init; }
    public required decimal Amount { get; init; }
    public int Priority { get; init; }

    /// <summary>Null means the documented default: true if Amount is negative.</summary>
    public bool? Mandatory { get; init; }
    public string? Description { get; init; }

    /// <summary>Free-form text the user types on the Expense form; the forecast never reads it.</summary>
    public string? Notes { get; init; }

    /// <summary>
    /// DIVERGENCE(auto-renew): set when the user answers "it just keeps going" at creation; it never changes the schedule or the math, and the pattern still ends on its stored Until.
    /// The app extends these patterns at forecast time so they always reach the horizon (MainWindow.RenewOngoingPatternsToHorizon adds new chain segments; transfers are extended in place).
    /// </summary>
    public bool AutoRenew { get; init; }
}

/// <summary>
/// A bill or a paycheck, which differ only in the sign of Amount (planning/01-glossary-of-terms.md#financialpattern).
/// A one-time goal (e.g. "trip to Japan") is also a FinancialPattern, with a single occurrence.
/// </summary>
public sealed class FinancialPattern
{
    public int FinanceId { get; }
    public string Source { get; }
    public RecurrenceRule DatePattern { get; }
    public decimal Amount { get; }
    public int Priority { get; }
    public bool Mandatory { get; }
    public string? Description { get; }
    public string? Notes { get; }
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
        Notes = options.Notes;
        AutoRenew = options.AutoRenew;
    }

    /// <summary>[CALC] Creates a bill, paycheck, or goal pattern, validating that the source name isn't blank.</summary>
    /// <param name="options">The pattern's source, schedule, amount, and other fields.</param>
    public static FinancialPattern Create(FinancialPatternOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Source))
        {
            // 4.2.a1: source cannot be None.
            throw new FinancialPatternSourceMissingException("Source cannot be empty.", nameof(options));
        }

        var mandatory = options.Mandatory ?? options.Amount < 0;
        return new FinancialPattern(options, mandatory);
    }

    /// <summary>[CALC] Returns a copy of this pattern active from the given earlier date, so a plan and jar for it can begin before its first occurrence; everything else is unchanged.</summary>
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
        Notes = Notes,
        AutoRenew = AutoRenew,
    });

    /// <summary>[CALC] Returns a copy of this pattern ending on the given earlier date instead; everything else, including occurrences up to that date, is unchanged.</summary>
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
        Notes = Notes,
        AutoRenew = AutoRenew,
    });

    /// <summary>
    /// [CALC] Returns a copy with its rrule DtStart anchor moved to the given date; everything else is unchanged.
    /// Warning: for interval > 1 or an implicit weekday this moves the whole schedule onto different days, so use it only for a direct Start edit; use ReanchoredToStartOn to keep the days.
    /// </summary>
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
        Notes = Notes,
        AutoRenew = AutoRenew,
    });

    /// <summary>
    /// [CALC] Returns a copy whose active span begins on the given date while its occurrences stay on the same days; everything else is unchanged.
    /// BreakOffFactory uses it to keep chain neighbors back to back.
    /// </summary>
    /// <param name="newActiveStart">Where the pattern's active span should begin.</param>
    public FinancialPattern ReanchoredToStartOn(DateOnly newActiveStart) => Create(new FinancialPatternOptions
    {
        FinanceId = FinanceId,
        Source = Source,
        DatePattern = DatePattern.ReanchoredToStartOn(newActiveStart),
        Amount = Amount,
        Priority = Priority,
        Mandatory = Mandatory,
        Description = Description,
        Notes = Notes,
        AutoRenew = AutoRenew,
    });
}
