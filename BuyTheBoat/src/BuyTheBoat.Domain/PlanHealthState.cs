namespace BuyTheBoat.Domain;

/// <summary>
/// One savings plan's full health picture, covering every EarMarkPattern sharing one FinanceId.
/// CreateForecast works it out alongside GoalShortfall, so the Expense and Earmark forms read one shared value instead of each recalculating.
/// The forms build their own messages from it; not every property is shown to the user directly.
/// </summary>
public sealed record PlanHealthState
{
    public required int FinanceId { get; init; }

    /// <summary>Whether the jar will be underfunded by the time the expense is due, under the current plan.</summary>
    public required GoalShortfall Shortfall { get; init; }

    /// <summary>How far the jar's balance sits below its milestone today.</summary>
    public required decimal CurrentShortfallAmount { get; init; }

    /// <summary>How far the jar's balance sits above its milestone today; at most one of this and CurrentShortfallAmount is non-zero.</summary>
    public required decimal CurrentOverfundedAmount { get; init; }

    /// <summary>
    /// Will the plan's scheduled contributions, left alone, never catch up: a problem with the rate itself, not a one-time gap?
    /// Only meaningful for a repeating pattern.
    /// </summary>
    public required bool IsChronicShortfall { get; init; }

    /// <summary>
    /// Will the plan's scheduled contributions keep putting in more than the goal needs every cycle until the plan changes, rather than sitting ahead for a one-off reason?
    /// For example, two concurrent plans that together contribute more per cycle than the bill.
    /// Only meaningful for a repeating pattern.
    /// </summary>
    public required bool IsChronicOverfund { get; init; }

    /// <summary>Is a predicted shortfall or surplus big enough to warn the user about, rather than a rounding-level blip?</summary>
    public required bool IsWorthWarningAbout { get; init; }

    /// <summary>
    /// The single health state to show when more than one applies.
    /// A shortage today always wins, then any future shortage regardless of timing; an excess only shows when there's no shortage anywhere.
    /// </summary>
    public required PlanHealthCategory MostImportantHealthState { get; init; }

    /// <summary>Dates on which this plan's own release came up short of the full amount.</summary>
    public required IReadOnlyList<DateOnly> UnderfundedReleaseDates { get; init; }

    /// <summary>The earliest date this plan is projected to fall short, for captioning the RRule preview; null when no shortfall is projected.</summary>
    public required DateOnly? ProjectedShortfallStartDate { get; init; }

    /// <summary>Is the goal's first occurrence still ahead, counting today as ahead?</summary>
    public required bool IsFirstOccurrencePending { get; init; }

    /// <summary>
    /// How far the plan's contributions so far would fall short of the first occurrence specifically, separate from whether the whole span is on pace.
    /// CurrentShortfallAmount can't show this: a plan reads on pace at $0 against $0 right up until its first payment fails.
    /// </summary>
    public required decimal FirstOccurrenceShortfall { get; init; }

    /// <summary>
    /// Free funds entering the first payment's day: the cash on hand to cover whatever part of that payment isn't set aside yet.
    /// This tells "the money's there, it just isn't earmarked" apart from "you won't have enough".
    /// Null when the first occurrence isn't pending or that day's free amount isn't worked out, including a preview of an unsaved plan, which falls back to the set-aside gap alone.
    /// </summary>
    public decimal? FirstOccurrenceFreeFunds { get; init; }

    /// <summary>The date the first pending payment lands, for wording like "the Mar 8 payment falls short"; null when the first occurrence isn't pending.</summary>
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
