namespace MyMoneyForecast.Domain;

// One page of the log book: "A document set up for a designated period of
// time. Used for a period of time between two fixed dates. Holds a schedule
// of expected transactions." (class documentation.ods). A chunk of the
// calendar showing ALL accounts' activity inside it — the single
// human-readable output the forecast display renders is one of these.
public sealed record TransactionLogPage
{
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }

    // Account name -> that account's slice of this date range. Single entry
    // for now; the dictionary shape is the multi-account seam.
    public required IReadOnlyDictionary<string, AccountTransactionPage> AccountPages { get; init; }
}
