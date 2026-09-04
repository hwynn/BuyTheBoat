namespace MyMoneyForecast.Domain;

// One savings plan's full diagnostic picture — every EarMarkPattern sharing
// one FinanceId. Computed fresh inside CreateForecast, alongside
// GoalShortfall, so the Expense and Earmark forms both read one
// already-computed value instead of each recalculating their own.
//
// This is diagnostic data for those forms to compose their own messages
// from — not every property here is necessarily shown to the user directly.
public sealed record PlanHealthState
{
    public required int FinanceId { get; init; }

    // Whether the fund jars will be underfunded by the time the expense is
    // actually due, under the current plan.
    public required GoalShortfall Shortfall { get; init; }

    // How far the jar's current balance sits below its milestone target
    // today.
    public required decimal CurrentShortfallAmount { get; init; }

    // How far the jar's current balance sits above its milestone target
    // today. At most one of this and CurrentShortfallAmount is ever
    // non-zero on a given day.
    public required decimal CurrentOverfundedAmount { get; init; }

    // Whether this plan's own scheduled contributions, left alone, would
    // never fully catch up — a structural rate problem, not a one-time
    // gap. Only meaningful for a repeating pattern.
    public required bool IsChronicShortfall { get; init; }

    // The excess-side mirror of IsChronicShortfall: whether this plan's own
    // scheduled contributions structurally outpace what the goal actually
    // needs — e.g. more than one EarMarkPattern funding the same goal,
    // together contributing more per cycle than the goal's own amount — as
    // opposed to happening to sit a little ahead today for a one-off
    // reason. Distinguishes "this will keep reading as overfunded every
    // cycle, permanently, until the plan itself changes" from "a genuinely
    // transient surplus." Added 2026-08-13 (found via Storage Unit Rental
    // in the field: two concurrent plans summing to more than the bill
    // needed, permanently, which read identically to a one-off surplus
    // until this existed). Only meaningful for a repeating pattern, same as
    // IsChronicShortfall.
    public required bool IsChronicOverfund { get; init; }

    // Whether a predicted shortfall or surplus is significant enough to
    // actually warn the user about, rather than a rounding-level blip.
    public required bool IsWorthWarningAbout { get; init; }

    // Which single health state to actually show when more than one
    // applies: a shortage today always wins; any future shortage beats any
    // excess regardless of timing; an excess only shows when no shortage
    // exists anywhere.
    public required PlanHealthCategory MostImportantHealthState { get; init; }

    // Dates on which this plan's own release came up short of the full
    // amount.
    public required IReadOnlyList<DateOnly> UnderfundedReleaseDates { get; init; }

    // The earliest date this plan is projected to fall short, for
    // captioning the RRule preview. Null when no shortfall is projected.
    public required DateOnly? ProjectedShortfallStartDate { get; init; }

    // Whether this goal's very first occurrence hasn't happened yet —
    // today still counts as pending.
    public required bool IsFirstOccurrencePending { get; init; }

    // How much of the plan's contributions so far would fail to cover the
    // very first occurrence specifically, separate from whether the whole
    // span is on pace. CurrentShortfallAmount can't answer this on its own
    // — a plan reads perfectly on-pace at $0-vs-$0 right up until its
    // first payment actually fails.
    public required decimal FirstOccurrenceShortfall { get; init; }

    // Free-to-spend money in the account entering the first payment's day —
    // the cash on hand to cover whatever part of that payment isn't set aside
    // yet. This is what tells "the money's there, it just isn't earmarked"
    // (free covers the gap) apart from "you genuinely won't have enough"
    // (it doesn't). Null when the first occurrence isn't pending, or that
    // day's free amount isn't computed — including the lightweight preview
    // path (a proposed, unsaved plan with no forecast to read a page from),
    // which leaves it null and falls back to the plain set-aside gap. Not
    // `required` for that same reason.
    public decimal? FirstOccurrenceFreeFunds { get; init; }

    // The date the first pending payment lands, for the urgent warning's
    // "the Mar 8 payment falls short" wording. Null when the first occurrence
    // isn't pending — nothing urgent to date-stamp.
    public DateOnly? FirstOccurrenceDate { get; init; }
}

public enum PlanHealthCategory
{
    Healthy,
    AlreadyMissing,
    WillMiss,
    CurrentlyOverfunded,
    WillBeOverfunded,
}
