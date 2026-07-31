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

    // planning/17, item 24 (F32): the mirror image of ShortfallAmount — a
    // goal met early (charter's own example: a big manual earmark fills a
    // jar ahead of schedule). Costs no new computation; both fields it reads
    // already exist. Same gross-vs-gross scope as ShortfallAmount: correct
    // for a one-time goal, and for a repeating one (a loan) it reads the
    // whole remaining span rather than pace against the next occurrence —
    // the same accepted limitation, not a new one.
    public decimal OverfundedAmount => Math.Max(0m, AmountAllocatedByDueDate - AmountNeeded);
}
