namespace BuyTheBoat.Domain;

/// <summary>
/// One page of the log book: "A document set up for a designated period of time. Used for a period of time between two fixed dates. Holds a schedule of expected transactions." (class documentation.ods)
/// It covers a stretch of the calendar for every account.
/// </summary>
public sealed record TransactionLogPage
{
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }

    /// <summary>Each account's part of this date range, keyed by account name.</summary>
    public required IReadOnlyDictionary<string, AccountTransactionPage> AccountPages { get; init; }
}
