namespace BuyTheBoat.Domain;

/// <summary>
/// One goal's savings status, judged against its own due date regardless of how far the forecast's day-by-day timeline reaches.
/// Every goal with a savings plan has one, not just those falling short; ShortfallAmount is 0 for a goal that's on track.
/// </summary>
/// <remarks>
/// TODO: tell the user unprompted when a goal is running ahead (OverfundedAmount), rather than waiting for them to open and save its form.
/// </remarks>
public sealed record GoalShortfall
{
    public required int FinanceId { get; init; }
    public required string Label { get; init; }
    public required DateOnly DueDate { get; init; }
    public required decimal AmountNeeded { get; init; }
    public required decimal AmountAllocatedByDueDate { get; init; }

    public decimal ShortfallAmount => Math.Max(0m, AmountNeeded - AmountAllocatedByDueDate);

    /// <summary>
    /// How far ahead of schedule a goal is, for example after a big manual earmark filled the jar early.
    /// For a repeating goal, this covers the whole remaining span rather than the next occurrence, the same as ShortfallAmount.
    /// </summary>
    public decimal OverfundedAmount => Math.Max(0m, AmountAllocatedByDueDate - AmountNeeded);
}
