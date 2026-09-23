namespace BuyTheBoat.Domain;

// Whether a plan change is a user-facing SUGGESTION or a silent IMPLICIT one — the axis that decides how
// bold the affordability calculation is allowed to be. A suggestion hands the user the reallocation lever
// (so it can count on cushion / lower-priority money actually being freed up), while an implicit change
// has no one to prompt, so it stays more cautious. See AffordabilityCeiling for how each maps onto the
// Frugality tiers.
public enum ChangeKind
{
    Implicit,
    Suggestion,
}

// The most money a plan or earmark for a given bill/goal may set aside without over-committing — one
// ceiling a suggestion or implicit change can size itself against, composed from the Frugality-tiered
// free-funds methods on AccountTransactionPage. It applies the settled tier rules: the tier(s),
// window(s), and the short-/long-term split are chosen from whether
// the expense is mandatory, whether its whole savings-plan chain reaches past the split point, and whether
// this is a bold suggestion or a cautious implicit change.
public static class AffordabilityCeiling
{
    // How far out the "we can afford to be bolder later" split sits. Held as two separate tunables — one
    // per branch — so the mandatory and non-mandatory splits can diverge later; equal (3 months) for now.
    private const int MandatorySplitMonths = 3;
    private const int NonMandatorySplitMonths = 3;

    /// <summary>[CALC] Returns the most a plan/earmark for `target` may set aside on its tightest day without over-committing, applying the settled tier rules. Feeds the "never suggest drawing more than the user has" cap on earmark suggestions and implicit plan changes — the caller compares its own proposed contribution against this. Null when a day the ceiling depends on has no computed free amount.</summary>
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

            // Non-mandatory, a one-time expense far enough out that we can bank on more money arriving:
            // relaxed until the split, considerate after (optimistically willing to lean on the cushion by then).
            if (isOneTime && dueDate > nonMandSplit)
            {
                return MinCeiling(
                    page.MinimumAvailableFunds(asOfDate, nonMandSplit, Frugality.Relaxed),
                    page.MinimumAvailableFunds(nonMandSplit, dueDate, Frugality.Considerate));
            }

            // Non-mandatory, everything else: only genuinely free money, over the target's own span.
            return page.MinimumAvailableFunds(asOfDate, WindowEnd(asOfDate, dueDate), Frugality.Relaxed);
        }

        // Mandatory: how far the WHOLE savings-plan chain (every same-Source segment) reaches decides whether
        // there is a meaningful "beyond the split" stretch to treat more gently at all.
        var chainEnd = ChainEnd(page, target);
        var mandSplit = asOfDate.AddMonths(MandatorySplitMonths);

        // Mandatory, chain runs past the split: pace the near term aggressively (the money is needed soon)
        // and the far term more gently, then hold to whichever of the two ceilings is tighter.
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

    /// <summary>[CALC] Returns the ceiling for a one-day starting/front-load earmark placed on asOfDate — the single-date counterpart to For, sizing that one set-aside via the single-date methods at whatever tier governs asOfDate. Feeds the cap on a proposed starting earmark. Null when asOfDate's free amount isn't computed.</summary>
    /// <param name="page">The account page the target lives on.</param>
    /// <param name="target">The bill or goal the earmark front-loads.</param>
    /// <param name="asOfDate">The day the starting earmark lands on.</param>
    /// <param name="changeKind">Whether this is a bold Suggestion or a cautious Implicit change.</param>
    public static decimal? ForStartingEarmark(
        AccountTransactionPage page, FinancialPattern target, DateOnly asOfDate, ChangeKind changeKind) =>
        page.AvailableAt(asOfDate, NearTier(page, target, asOfDate, changeKind), target.Priority);

    /// <summary>[CALC] Returns the Frugality tier that governs asOfDate itself — the near (first) window in For's own split — so For and ForStartingEarmark stay in step on how boldly that day is treated.</summary>
    /// <param name="page">The account page whose finance patterns hold the chain.</param>
    /// <param name="target">The bill or goal being funded.</param>
    /// <param name="asOfDate">The day whose tier to resolve.</param>
    /// <param name="changeKind">Whether this is a bold Suggestion or a cautious Implicit change.</param>
    private static Frugality NearTier(
        AccountTransactionPage page, FinancialPattern target, DateOnly asOfDate, ChangeKind changeKind)
    {
        // Non-mandatory always opens relaxed — asOfDate is in the first window, which is relaxed in both
        // non-mandatory rules.
        if (!target.Mandatory)
        {
            return Frugality.Relaxed;
        }

        // Mandatory: bold for a suggestion (Miserly either way); for an implicit change, the short-window
        // tier when the chain runs past the split, else the single close tier.
        if (changeKind == ChangeKind.Suggestion)
        {
            return Frugality.Miserly;
        }
        return ChainEnd(page, target) > asOfDate.AddMonths(MandatorySplitMonths)
            ? Frugality.Thrifty
            : Frugality.Considerate;
    }

    /// <summary>[CALC] Returns the furthest date this pattern's break-off chain reaches — the latest Until across every finance pattern sharing its Source (the app's own definition of a chain), the target itself included in case it's a not-yet-saved edit that isn't on the page.</summary>
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

    /// <summary>[CALC] Returns the tighter (smaller) of two window ceilings, or null if either is unknown — one unknown window means the combined ceiling can't be pinned down.</summary>
    /// <param name="a">One window's ceiling.</param>
    /// <param name="b">The other window's ceiling.</param>
    private static decimal? MinCeiling(decimal? a, decimal? b) =>
        a is decimal x && b is decimal y ? Math.Min(x, y) : null;
}
