using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// Turns PlanHealthState's numbers into the exact user-facing wording settled
// in planning/22 §6 — kept separate from any UserControl so the wording
// itself is plain, testable logic, and so Current jar state / Summary / the
// RRule preview all read from one place instead of composing strings
// independently. Every method here corresponds to one settled row in
// planning/22's own content tables; each doc comment cites which.
public static class PlanHealthMessages
{
    // planning/22 §6a, SETTLED: the append-only delta for the two "today"
    // states. Null for Healthy/WillMiss/WillBeOverfunded — those either show
    // no delta at all (Healthy) or use SummaryFutureLine's own sentence
    // instead (the future two).
    public static string? CurrentJarStateDelta(PlanHealthState state) => state.MostImportantHealthState switch
    {
        PlanHealthCategory.AlreadyMissing => $"{state.CurrentShortfallAmount:C0} short",
        PlanHealthCategory.CurrentlyOverfunded => $"{state.CurrentOverfundedAmount:C0} over",
        _ => null,
    };

    // planning/22 §6a, SETTLED, full sentence: "$X saved of $Y milestone[
    // — $Z short/over]" — the base fact always shows; the delta only appends
    // for the two "today" states. jar must be this Savings Plan's own
    // FundJar (today's reading) — PlanHealthState itself only carries the
    // derived deltas, not the raw saved/milestone amounts.
    public static string CurrentJarStateLine(FundJar jar, PlanHealthState state)
    {
        var basic = $"{jar.ExpectedAmount:C0} saved of {jar.MilestoneAmount ?? 0m:C0} milestone";
        var delta = CurrentJarStateDelta(state);
        return delta is null ? basic : $"{basic} — {delta}";
    }

    // planning/22 §6c, SETTLED (Summary A/B aside, one-time goal): the
    // future-facing pair — "on pace for $Y of $Z needed — $W short/over."
    // Healthy uses today's own jar/milestone (same numbers CurrentJarStateLine
    // would show, just phrased as "on track" instead of appending a delta) —
    // there's nothing to project when today's reading is already fine.
    // AlreadyMissing/CurrentlyOverfunded return null; those two live in
    // CurrentJarStateLine instead (planning/22 §6, the per-region intent
    // table, is the map of which region owns which state).
    public static string? SummaryFutureLine(FundJar jar, GoalShortfall shortfall, PlanHealthCategory category) =>
        category switch
        {
            PlanHealthCategory.Healthy =>
                $"{jar.ExpectedAmount:C0} saved, matching the {jar.MilestoneAmount ?? 0m:C0} milestone — on track.",
            PlanHealthCategory.WillMiss =>
                $"{jar.ExpectedAmount:C0} saved, on pace for {shortfall.AmountAllocatedByDueDate:C0} of {shortfall.AmountNeeded:C0} needed — {shortfall.ShortfallAmount:C0} short",
            PlanHealthCategory.WillBeOverfunded =>
                $"{jar.ExpectedAmount:C0} saved, on pace for {shortfall.AmountAllocatedByDueDate:C0} of {shortfall.AmountNeeded:C0} needed — {shortfall.OverfundedAmount:C0} over",
            _ => null,
        };

    // planning/22 §6c, SETTLED 2026-08-05 (repeating pattern, Summary C
    // aside's second fact slot — replaces "Lifetime contributed"): flags
    // *that* the shortfall recurs, gated on IsChronicShortfall plus an
    // already-showing shortfall state — a modifier on an existing warning,
    // not an independent announcement.
    //
    // TODO (2026-08-05): the overfunded mirror of this was never actually
    // settled — doc 22 only ever confirmed the shortfall phrase, and whether
    // a repeating overfunded plan needs its own Summary-aside phrase (vs.
    // just relying on the RRule preview's "Projected overfunded") is an open
    // question, not resolved here. Shipping the shortfall case now and
    // returning null for the overfunded one (no phrase — same as before this
    // slot existed) rather than block on it. Candidate direction for the next
    // sweep, NOT adopted, deliberately left as a comment rather than a live
    // branch so it doesn't silently start firing before anyone decided it should:
    //     PlanHealthCategory.CurrentlyOverfunded or PlanHealthCategory.WillBeOverfunded
    //         => "Consistently ahead.",
    public static string? SummaryRecurringPhrase(PlanHealthState state)
    {
        var isShortfallState = state.MostImportantHealthState is PlanHealthCategory.AlreadyMissing or PlanHealthCategory.WillMiss;
        return isShortfallState && state.IsChronicShortfall ? "Keeps falling short." : null;
    }

    // planning/22 §6b, SETTLED 2026-08-05: the RRule preview's own caption —
    // generic ("Projected short/overfunded"), except the shortfall case
    // splits on IsChronicShortfall and reuses SummaryRecurringPhrase's exact
    // words rather than a new synonym. Null for Healthy/AlreadyMissing/
    // CurrentlyOverfunded — this region only ever speaks for the future two.
    public static string? RRulePreviewCaption(PlanHealthState state) => state.MostImportantHealthState switch
    {
        PlanHealthCategory.WillMiss => state.IsChronicShortfall ? "Keeps falling short" : "Projected short",
        PlanHealthCategory.WillBeOverfunded => "Projected overfunded",
        _ => null,
    };

    // planning/22 §6b: wording SETTLED, mechanism still pending — needs the
    // form/UI layer's at-open-vs-live diff (not built; that layer doesn't
    // exist yet). Not called from anywhere yet; the strings exist so whoever
    // builds that diffing mechanism doesn't also have to invent the copy.
    public const string FixedNoLongerShort = "Fixed — no longer short";
    public const string FixedNoLongerOverfunded = "Fixed — no longer overfunded";
}
