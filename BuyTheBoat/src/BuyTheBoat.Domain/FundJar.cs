namespace BuyTheBoat.Domain;

/// <summary>
/// A named bucket of money set aside for one goal or bill, or the safety cushion when FinanceId is null.
/// It records that money is set aside without moving it to another account, and tracks whether enough has been set aside so far.
/// The user never creates a jar directly: it exists only because of earmark activity, and it's how bills, one-time goals, and the cushion alike set money aside.
/// </summary>
public sealed record FundJar
{
    /// <summary>Null for the safety cushion jar (3.7.a2); every snapshot has exactly one (9.5.a1).</summary>
    public required int? FinanceId { get; init; }

    /// <summary>
    /// How much is actually in the jar, which only real transactions can tell, so it's null for every future date ("None until its date is the current date").
    /// ASSUMED-PAIRING(as-of-day): on the as-of day's starting snapshot it equals ExpectedAmount, treating everything up to that day as settled.
    /// </summary>
    public required decimal? CurrentAmount { get; init; }

    /// <summary>3.13.5.3.a1: the previous day's amount plus today's earmark events for this finance id, never below 0.</summary>
    public required decimal ExpectedAmount { get; init; }

    /// <summary>
    /// 3.13.5.4.a1: how much should be in the jar by this date, the running total of scheduled contributions; compare with ExpectedAmount to see whether a goal is falling behind.
    /// Null when no savings plan drives this jar: the safety cushion, and bills reserved automatically.
    /// </summary>
    public required decimal? MilestoneAmount { get; init; }

    /// <summary>
    /// Does the jar already hold at least what its schedule calls for, so its next contribution could be skipped and it would still be on pace ("the glut case")?
    /// Skipping a contribution lowers ExpectedAmount and MilestoneAmount by the same amount, so today's comparison is the answer.
    /// False when no savings plan drives this jar.
    /// EarmarkConsolidation checks this so it doesn't erase a surplus.
    /// </summary>
    public bool HasGlut => MilestoneAmount is decimal milestone && ExpectedAmount >= milestone;

    /// <summary>
    /// How much ExpectedAmount sits above MilestoneAmount, the dollar amount behind HasGlut.
    /// Exactly on pace gives 0 while HasGlut is still true; 0 whenever there's no surplus or no milestone.
    /// </summary>
    public decimal GlutSurplus => MilestoneAmount is decimal milestone ? Math.Max(0m, ExpectedAmount - milestone) : 0m;
}
