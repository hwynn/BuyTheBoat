namespace BuyTheBoat.Domain;

/// <summary>
/// One movement of money into a fund jar on a specific day, or back out of it when negative: "'earmark' and 'earmark event' mean the same thing in this project. Each one occurs once on a specific day and moves a certain amount of money around." (class documentation.ods)
/// A repeated earmark comes from an EarMarkPattern's schedule; an isolated one is a one-off, made by the user or the system.
/// There's at most one of each per finance id per day; a second isolated earmark for the same jar and day merges into the first.
/// </summary>
public sealed record EarMarkEvent
{
    /// <summary>The bill or goal this earmark is for; null for the safety cushion.</summary>
    public required int? FinanceId { get; init; }

    /// <summary>"All earmarks need a date. Having a date lets us create earmarks manually whenever we want."</summary>
    public required DateOnly EarmarkDate { get; init; }

    public required bool RepeatedEarmark { get; init; }

    /// <summary>Positive moves money into the jar; negative moves it back to free funds.</summary>
    public required decimal ExpectedAmount { get; init; }

    /// <summary>
    /// Isolated earmarks only (null on repeated ones): the part the user entered, kept so the system can undo its own automatic adjustments without losing the user's intent (reverse_implicit_earmarks); 0 on events the system made.
    /// DIVERGENCE(positive-implicit): the original design only made negative automatic isolated earmarks, the deallocation-day give-backs; this also makes positive ones to refill the safety cushion toward its target.
    /// </summary>
    public required decimal? ExplicitAmount { get; init; }

    /// <summary>
    /// How much actually moved once the day happened, which only actual transactions can tell ("one of the last things a cascade update computes").
    /// ASSUMED-PAIRING(actual-amount): always null until transaction import exists.
    /// </summary>
    public decimal? ActualAmount { get; init; }
}
