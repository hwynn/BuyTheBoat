namespace MyMoneyForecast.Domain;

// The root of the onion: a book of ALL of a user's finance information,
// made of calendar pages (per the design's author — "the
// TransactionLogBook was actually meant to be like a book of all of a
// user's finance information. And it had pages... that represented large
// chunks of a calendar so we could isolate a range of dates and show all
// the information about all the accounts inside that range").
//
// DIVERGENCE(page-length): the original design's fixed PageLength (number
// of months per TransactionLogPage) and cross-page runoff are deferred —
// each forecast run currently produces exactly one page spanning the
// requested window, so PageLength is null and LogPages has one entry. What's
// missing is the page *mechanism* itself, not merely old pages: it's needed
// even without actuals or persisted history — moving the horizon forward is
// meant to move *through* pages, not stretch one page indefinitely.
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

    /// <summary>[CALC] Returns the FinanceId to give a brand-new FinancialPattern — one higher than the largest FinanceId currently in use across the whole book, or 1 if there are none yet.</summary>
    public int NextFinanceId() =>
        AllFinancialPatterns().Select(pattern => pattern.FinanceId).DefaultIfEmpty(0).Max() + 1;

    /// <summary>[CALC] Returns every EarMarkPattern the user has, across every account and page — every segment of every Savings Plan, not deduped. More than one can legitimately share a FinanceId (relaxing 3.11.1.a1 — a Restructure/break-off chain's segments, or concurrent earmark patterns), so unlike AllFinancialPatterns this never collapses them down to one.</summary>
    public IReadOnlyList<EarMarkPattern> AllEarMarkPatterns()
    {
        var patterns = new List<EarMarkPattern>();
        foreach (var page in LogPages)
        {
            foreach (var accountPage in page.AccountPages.Values)
            {
                patterns.AddRange(accountPage.EarmarkPatterns);
            }
        }

        return patterns;
    }

    /// <summary>[CALC] Returns every EarMarkPattern funding one specific goal or bill — the whole Savings Plan for that FinanceId, whether it's a single plan or several segments (sequential or concurrent).</summary>
    /// <param name="financeId">Which goal or bill's Savings Plan to return.</param>
    public IReadOnlyList<EarMarkPattern> EarMarkPatternsFor(int financeId) =>
        AllEarMarkPatterns().Where(pattern => pattern.FinanceId == financeId).ToList();

    /// <summary>[CALC] Returns the FinanceIds of every FinancialPattern in the same break-off chain as the given one — every pattern sharing its Source, the app's own definition of a chain (a same-Source pattern with a different Start is a later segment). Returns just the id itself when it has no pattern in the book, or no same-Source siblings. Lets a caller omit a whole savings-plan chain from an affordability re-forecast, or treat a chain as one unit anywhere else.</summary>
    /// <param name="financeId">A FinanceId anywhere in the chain.</param>
    public IReadOnlySet<int> ChainFinanceIds(int financeId)
    {
        var patterns = AllFinancialPatterns();
        if (patterns.FirstOrDefault(pattern => pattern.FinanceId == financeId)?.Source is not { } source)
        {
            return new HashSet<int> { financeId };
        }

        // Every pattern sharing the Source is a segment of the same chain.
        return patterns.Where(pattern => pattern.Source == source).Select(pattern => pattern.FinanceId).ToHashSet();
    }
}
