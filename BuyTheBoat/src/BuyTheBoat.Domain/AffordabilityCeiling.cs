namespace BuyTheBoat.Domain;

/// <summary>
/// Whether a plan change is a suggestion shown to the user or an automatic change, which decides how bold the affordability calculation may be.
/// A suggestion lets the user decide whether to free up cushion or lower-priority money, so it can count on it; an automatic change has no one to ask, so it stays more cautious.
/// </summary>
public enum ChangeKind
{
    Implicit,
    Suggestion,
}

/// <summary>
/// The most a plan or earmark for a bill or goal may set aside without over-committing, built from AccountTransactionPage's frugality-tiered free-funds methods.
/// The tiers, windows, and near-versus-far split depend on whether the expense is mandatory, whether its savings-plan chain reaches past the split, and whether this is a suggestion or an automatic change.
/// </summary>
public static class AffordabilityCeiling
{
    // How far out the split sits, after which the ceiling can be bolder; kept as two settings so mandatory and non-mandatory can differ later.
    private const int MandatorySplitMonths = 3;
    private const int NonMandatorySplitMonths = 3;

    /// <summary>
    /// [CALC] Returns the most a plan or earmark for `target` may set aside on its tightest day without over-committing.
    /// It's the "never suggest drawing more than the user has" cap on earmark suggestions and automatic plan changes; the caller compares its proposed contribution against it.
    /// Null when a day the ceiling depends on has no worked-out free amount.
    /// </summary>
    /// <param name="page">The account page the target lives on — supplies both the free-funds tiers and the same-Source chain.</param>
    /// <param name="target">The bill or goal being funded; its Mandatory/Priority/Source/dates drive the whole choice.</param>
    /// <param name="asOfDate">Today, or the forecast's as-of date — every window starts here.</param>
    /// <param name="changeKind">Whether this is a bold Suggestion or a cautious Implicit change.</param>
    public static decimal? For(
        AccountTransactionPage page, FinancialPattern target, DateOnly asOfDate, ChangeKind changeKind)
    {
        var priority = target.Priority;

        if (!target.Mandatory)
        {
            var isOneTime = target.DatePattern
                .GetOccurrences(target.DatePattern.ActiveStart, target.DatePattern.Until).Count == 1;
            var dueDate = target.DatePattern.Until;
            var nonMandSplit = asOfDate.AddMonths(NonMandatorySplitMonths);

            // Non-mandatory one-time expense due after the split, when more money will have arrived: Relaxed until the split, Considerate after.
            if (isOneTime && dueDate > nonMandSplit)
            {
                return MinCeiling(
                    page.MinimumAvailableFunds(asOfDate, nonMandSplit, Frugality.Relaxed),
                    page.MinimumAvailableFunds(nonMandSplit, dueDate, Frugality.Considerate));
            }

            // Any other non-mandatory expense: only free money, over the target's own span.
            return page.MinimumAvailableFunds(asOfDate, WindowEnd(asOfDate, dueDate), Frugality.Relaxed);
        }

        // Mandatory: how far the whole chain (every same-Source segment) reaches decides whether there's a stretch beyond the split to treat more gently.
        var chainEnd = ChainEnd(page, target);
        var mandSplit = asOfDate.AddMonths(MandatorySplitMonths);

        // Mandatory, chain runs past the split: a bold tier for the near term, a gentler one for the far term, holding to whichever ceiling is tighter.
        if (chainEnd > mandSplit)
        {
            var shortTier = changeKind == ChangeKind.Suggestion ? Frugality.Miserly : Frugality.Thrifty;
            var longTier = changeKind == ChangeKind.Suggestion ? Frugality.Thrifty : Frugality.Considerate;
            return MinCeiling(
                page.MinimumAvailableFunds(asOfDate, mandSplit, shortTier, priority),
                page.MinimumAvailableFunds(mandSplit, chainEnd, longTier, priority));
        }

        // Mandatory, whole chain fits inside the split: one bold tier over its short life.
        var closeTier = changeKind == ChangeKind.Suggestion ? Frugality.Miserly : Frugality.Considerate;
        return page.MinimumAvailableFunds(asOfDate, WindowEnd(asOfDate, chainEnd), closeTier, priority);
    }

    /// <summary>
    /// [CALC] Returns the ceiling for a starting earmark placed on asOfDate, at the same tier For uses for that day; it caps a proposed starting earmark.
    /// Null when asOfDate's free amount isn't worked out.
    /// </summary>
    /// <param name="page">The account page the target lives on.</param>
    /// <param name="target">The bill or goal the earmark front-loads.</param>
    /// <param name="asOfDate">The day the starting earmark lands on.</param>
    /// <param name="changeKind">Whether this is a bold Suggestion or a cautious Implicit change.</param>
    public static decimal? ForStartingEarmark(
        AccountTransactionPage page, FinancialPattern target, DateOnly asOfDate, ChangeKind changeKind) =>
        page.AvailableAt(asOfDate, NearTier(page, target, asOfDate, changeKind), target.Priority);

    /// <summary>[CALC] Returns the frugality tier for asOfDate itself (For's near window), so For and ForStartingEarmark treat that day the same way.</summary>
    /// <param name="page">The account page whose finance patterns hold the chain.</param>
    /// <param name="target">The bill or goal being funded.</param>
    /// <param name="asOfDate">The day whose tier to resolve.</param>
    /// <param name="changeKind">Whether this is a bold Suggestion or a cautious Implicit change.</param>
    private static Frugality NearTier(
        AccountTransactionPage page, FinancialPattern target, DateOnly asOfDate, ChangeKind changeKind)
    {
        // Non-mandatory: Relaxed, since both non-mandatory rules start Relaxed.
        if (!target.Mandatory)
        {
            return Frugality.Relaxed;
        }

        // Mandatory: Miserly for a suggestion; for an automatic change, the near tier For would use.
        if (changeKind == ChangeKind.Suggestion)
        {
            return Frugality.Miserly;
        }
        return ChainEnd(page, target) > asOfDate.AddMonths(MandatorySplitMonths)
            ? Frugality.Thrifty
            : Frugality.Considerate;
    }

    /// <summary>[CALC] Returns the furthest date this pattern's chain reaches: the latest Until among patterns sharing its Source, including the target itself in case it's an unsaved edit not on the page.</summary>
    /// <param name="page">The account page whose finance patterns hold the chain.</param>
    /// <param name="target">The pattern whose chain to measure.</param>
    private static DateOnly ChainEnd(AccountTransactionPage page, FinancialPattern target) =>
        page.FinancePatterns
            .Where(pattern => pattern.Source == target.Source)
            .Select(pattern => pattern.DatePattern.Until)
            .Append(target.DatePattern.Until)
            .Max();

    /// <summary>[CALC] Clamps a window's end to no earlier than its start, so an already-ended target collapses to a single-day check instead of throwing on an inverted range.</summary>
    /// <param name="asOfDate">The window start.</param>
    /// <param name="end">The intended end.</param>
    private static DateOnly WindowEnd(DateOnly asOfDate, DateOnly end) => end < asOfDate ? asOfDate : end;

    /// <summary>[CALC] Returns the smaller of two window ceilings, or null if either is unknown.</summary>
    /// <param name="a">One window's ceiling.</param>
    /// <param name="b">The other window's ceiling.</param>
    private static decimal? MinCeiling(decimal? a, decimal? b) =>
        a is decimal x && b is decimal y ? Math.Min(x, y) : null;
}
