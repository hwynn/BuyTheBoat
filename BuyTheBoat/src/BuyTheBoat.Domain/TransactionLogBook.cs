namespace BuyTheBoat.Domain;

/// <summary>
/// The top of the structure: a book of all of a user's finance information, made of calendar pages ("pages... that represented large chunks of a calendar so we could isolate a range of dates and show all the information about all the accounts inside that range").
/// </summary>
/// <remarks>
/// DIVERGENCE(page-length): the original design's fixed PageLength (months per page) and carrying values from page to page aren't built; each forecast makes one page spanning its window, so PageLength is null and LogPages has one entry.
/// Pages are needed even without saved history: moving the horizon forward is meant to move through pages, not stretch one page forever.
/// </remarks>
public sealed record TransactionLogBook
{
    public int? PageLength { get; init; }
    public required IReadOnlyList<TransactionLogPage> LogPages { get; init; }

    /// <summary>
    /// [CALC] Returns every FinancialPattern the user has, across every account and page, including ones with no occurrences inside any page's window.
    /// Each FinanceId appears once; when a pattern is on more than one page, the later page's version wins.
    /// </summary>
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

    /// <summary>[CALC] Returns the FinanceId for a new FinancialPattern: one higher than the largest in the book, or 1 if there are none yet.</summary>
    public int NextFinanceId() =>
        AllFinancialPatterns().Select(pattern => pattern.FinanceId).DefaultIfEmpty(0).Max() + 1;

    /// <summary>
    /// [CALC] Returns every EarMarkPattern the user has, across every account and page: every segment of every savings plan.
    /// Unlike AllFinancialPatterns, it keeps every plan sharing a FinanceId (relaxing 3.11.1.a1), whether chain segments or concurrent plans.
    /// </summary>
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

    /// <summary>
    /// [CALC] Returns the FinanceIds of every pattern in the same chain as the given one, meaning every pattern sharing its Source; just the id itself when it isn't in the book or has no siblings.
    /// Lets a caller leave a whole chain out of an affordability re-forecast, or treat a chain as one unit.
    /// </summary>
    /// <param name="financeId">A FinanceId anywhere in the chain.</param>
    public IReadOnlySet<int> ChainFinanceIds(int financeId)
    {
        var patterns = AllFinancialPatterns();
        if (patterns.FirstOrDefault(pattern => pattern.FinanceId == financeId)?.Source is not { } source)
        {
            return new HashSet<int> { financeId };
        }

        return patterns.Where(pattern => pattern.Source == source).Select(pattern => pattern.FinanceId).ToHashSet();
    }
}
