namespace MyMoneyForecast.Domain;

// The root of the onion: a book of ALL of a user's finance information,
// made of calendar pages (per the design's author, 2026-07-10 — "the
// TransactionLogBook was actually meant to be like a book of all of a
// user's finance information. And it had pages... that represented large
// chunks of a calendar so we could isolate a range of dates and show all
// the information about all the accounts inside that range").
//
// DIVERGENCE(page-length): the original design's fixed PageLength (number
// of months per TransactionLogPage) and cross-page runoff are deferred —
// each forecast run currently produces exactly one page spanning the
// requested window, so PageLength is null and LogPages has one entry. Fixed
// page lengths matter once persisted history and expired pages exist.
public sealed record TransactionLogBook
{
    public int? PageLength { get; init; }
    public required IReadOnlyList<TransactionLogPage> LogPages { get; init; }

    /// <summary>[CALC] Returns every FinancialPattern the user has, across every account and page — including one whose occurrences fall entirely outside any single page's own date window. Deduped by FinanceId; where the same pattern appears on more than one page, the later page's version wins.</summary>
    public IReadOnlyList<FinancialPattern> AllFinancialPatterns()
    {
        var byFinanceId = new Dictionary<int, FinancialPattern>();
        foreach (var page in LogPages)
        {
            foreach (var accountPage in page.AccountPages.Values)
            {
                foreach (var pattern in accountPage.FinancePatterns)
                {
                    byFinanceId[pattern.FinanceId] = pattern;
                }
            }
        }

        return byFinanceId.Values.ToList();
    }
}
