namespace BuyTheBoat.Domain;

// Per-goal savings status, one per EarMarkPattern — evaluated against its own
// goal's due date, independent of whatever horizon a forecast's day-by-day
// timeline happens to cover. Always present for every goal that has a
// savings plan, not just the ones falling short — ShortfallAmount is 0 for
// a goal that's fully on track.
public sealed record GoalShortfall
{
    public required int FinanceId { get; init; }
    public required string Label { get; init; }
    public required DateOnly DueDate { get; init; }
    public required decimal AmountNeeded { get; init; }
    public required decimal AmountAllocatedByDueDate { get; init; }

    public decimal ShortfallAmount => Math.Max(0m, AmountNeeded - AmountAllocatedByDueDate);

    // The mirror of ShortfallAmount — how far ahead of schedule a goal is
    // (e.g. a big manual earmark filled the jar early). For a repeating
    // goal this reads the whole remaining span rather than pace against
    // the next occurrence, the same scope ShortfallAmount uses.
    public decimal OverfundedAmount => Math.Max(0m, AmountAllocatedByDueDate - AmountNeeded);

    // TODO: overfunded proactive nudge. Detection is done (OverfundedAmount
    // above); what's unbuilt is surfacing it unprompted — telling the user a
    // goal is running ahead without waiting for them to open and save its form.
    // Deferred as a future stage.
}
