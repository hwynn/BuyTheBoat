namespace MyMoneyForecast.Domain;

// Per-goal savings status, one per EarMarkPattern — evaluated against its own
// goal's due date (~3.13.5.4.a1's milestone_amount idea), independent of
// whatever horizon a Forecast's day-by-day timeline happens to cover. Always
// present for every goal that has a savings plan, not just the ones falling
// short — ShortfallAmount is 0 for a goal that's fully on track.
public sealed record GoalShortfall
{
    public required int FinanceId { get; init; }
    public required string Label { get; init; }
    public required DateOnly DueDate { get; init; }
    public required decimal AmountNeeded { get; init; }
    public required decimal AmountAllocatedByDueDate { get; init; }

    public decimal ShortfallAmount => Math.Max(0m, AmountNeeded - AmountAllocatedByDueDate);
}
