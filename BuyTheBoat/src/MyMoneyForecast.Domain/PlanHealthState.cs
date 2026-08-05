namespace MyMoneyForecast.Domain;

// One Savings Plan's full diagnostic picture — every EarMarkPattern sharing
// one finance_id (planning/21, "Terminology, SETTLED 2026-08-03") — computed
// fresh inside CreateForecast, same as GoalShortfall, so the Expense and
// Earmark forms both read from one already-computed value instead of each
// recalculating their own answer (planning/21, four plan-health states).
//
// This is the domain layer's diagnostic data, not the user-facing content
// itself (author, 2026-08-04) — a form's helper region composes its actual
// message from a subset of these fields plus its own wording; not every
// property here is necessarily shown to the user directly.
public sealed record PlanHealthState
{
    public required int FinanceId { get; init; }

    // Author's question: "Will the fund jars be underfunded in the future
    // based on the current proposed plan in such a way that they will be
    // underfunded on the day the expense actually happens?" — the existing
    // whole-span, due-date-anchored metric (Stage 4), nested rather than
    // copied so there is exactly one place AmountNeeded /
    // AmountAllocatedByDueDate / ShortfallAmount / OverfundedAmount live.
    public required GoalShortfall Shortfall { get; init; }

    // Author's question: "Are the fund jars currently underfunded compared
    // to the milestone amount?" — today's live pace: the jar's ExpectedAmount
    // against its (reset-at-release, 2026-08-03) MilestoneAmount.
    public required decimal CurrentShortfallAmount { get; init; }

    // Author's question: "Do we currently have more funds allocated to this
    // fund jar than our milestone amount?" — the mirror of
    // CurrentShortfallAmount; at most one of the two is ever non-zero on a
    // given day.
    public required decimal CurrentOverfundedAmount { get; init; }

    // Author's question: "In the case of a repeated expected transaction,
    // will a one time earmark fix the shortfall indefinitely? Or is this a
    // chronic problem that will cause a consistent shortage of funds?" — true
    // means the Savings Plan's own scheduled contributions, run to completion
    // with no further intervention, would not reach AmountNeeded on their
    // own: a structural fact about the two patterns' own rates, independent
    // of anything that has already happened. Only meaningful for a
    // repeating FinancialPattern (confirmed 2026-08-05) — a one-time
    // expense has no ongoing rate to be chronically wrong about, so this
    // is pointless there even though the computation itself doesn't fail.
    public required bool IsChronicShortfall { get; init; }

    // Author's question: "If we predict such a shortage, is the shortage
    // worth warning the user about?" — built 2026-08-05 (planning/22 §5),
    // separate non-repeated/repeated rule sets: a today-shortage always
    // warns; a non-repeated goal ignores a far-off small shortfall (under
    // half of projected account free funds that day); a repeating pattern
    // never gets to ignore a shortfall inside its own six-month lookahead,
    // even a far one, and otherwise falls back to the same half-of-free-
    // funds boundary. The excess mirror (both kinds): worth flagging once
    // the projected excess passes double the Savings Plan's own smallest
    // repeated contribution.
    public required bool IsWorthWarningAbout { get; init; }

    // Author's question: "If multiple of these abnormal health states exist,
    // which one is the most important?" — the one state to actually show, per
    // the author's ranking rule (2026-08-03, confirmed 2026-08-04): a
    // shortage today always wins; any future shortage beats any excess
    // regardless of timing; an excess only surfaces when no shortage exists
    // anywhere. "Today beats a later excess" when only excess states are in
    // play is this file's own inferred extension of that rule.
    public required PlanHealthCategory MostImportantHealthState { get; init; }

    // Author's question: "Is there a history of past short closes? Has this
    // already happened?" — OPEN (2026-08-04): dates on which this plan's own
    // release came up short of the full amount, extending the existing
    // floor-detection to a goal's own release, not just manual withdrawals.
    // The author has separately raised whether this should instead (or also)
    // count one-time earmarks that were needed to reach the milestone amount
    // — a different underlying event, not yet resolved which this field
    // should track.
    public required IReadOnlyList<DateOnly> UnderfundedReleaseDates { get; init; }

    // Author's question (2026-08-05): "when do the projected milestone
    // misses start, so the RRule preview can caption itself?" Built
    // 2026-08-05 — the earliest date this financeId's own release actually
    // came up short, from the same per-occurrence forward walk
    // IsWorthWarningAbout uses (only ever covers whatever horizon the
    // caller requested). Falls back to Shortfall.DueDate when the walk found
    // no specific short occurrence but the whole-span shortfall is still
    // positive — that date is the one still vouched for either way. Null
    // means no projected shortfall at all.
    public required DateOnly? ProjectedShortfallStartDate { get; init; }
}

public enum PlanHealthCategory
{
    Healthy,
    AlreadyMissing,
    WillMiss,
    CurrentlyOverfunded,
    WillBeOverfunded,
}
