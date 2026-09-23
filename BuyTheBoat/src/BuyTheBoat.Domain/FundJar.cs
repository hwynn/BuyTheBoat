namespace BuyTheBoat.Domain;

// A named bucket of money set aside for one goal or bill — or the safety
// cushion, when FinanceId is null. Records that money is set aside without
// actually moving it to another account, and tracks whether enough has been
// set aside so far.
//
// A jar is never created directly by the user — it exists only as a side
// effect of earmark activity, and is the generic allocation mechanism for
// bills, one-time goals, and the cushion alike, not a goals-only feature.
public sealed record FundJar
{
    // null = the safety cushion jar (3.7.a2: current_safety_cushion is the
    // money in today's finance_id == None jar). Every snapshot carries
    // exactly one (9.5.a1). Real cushion math is the deallocation/priority
    // phase; the jar exists now so that phase changes values, not shapes.
    public required int? FinanceId { get; init; }

    // How much is ACTUALLY in the jar — knowable only from real
    // transactions, so per the documented rule ("None until its date is the
    // current date") this is null for every future date.
    // ASSUMED-PAIRING(as-of-day): on the as-of day's seed snapshot it is set
    // equal to ExpectedAmount, treating everything up to that day as settled.
    public required decimal? CurrentAmount { get; init; }

    // 3.13.5.3.a1: the previous day's amount plus today's earmark events for
    // this finance id. Floored at 0 — a jar can be emptied, never negative.
    public required decimal ExpectedAmount { get; init; }

    // 3.13.5.4.a1: how much SHOULD be in the jar by this date — the running
    // sum of repeated (pattern-generated) earmark contributions. Compare
    // against ExpectedAmount to see if a goal is falling behind. null when
    // no EarMarkPattern drives this jar: the safety cushion (per the docs)
    // and automatically-reserved bills (no savings plan exists to be behind
    // on).
    public required decimal? MilestoneAmount { get; init; }

    // A jar already holding more than its own schedule currently calls for —
    // its very next scheduled contribution could be skipped and the jar
    // would still read on-pace, not behind ("the glut case").
    // Reduces to comparing this same date's own two amounts: skipping one
    // future contribution moves ExpectedAmount and MilestoneAmount by the
    // same amount (both accumulate from the identical earmark-event stream,
    // 3.13.5.3.a1 / 3.13.5.4.a1), so whatever this reads today is exactly
    // what it would still read right after that contribution was skipped.
    // False when no EarMarkPattern drives this jar (MilestoneAmount is
    // null) — nothing to skip, nothing to be ahead of.
    //
    // Consulted by EarmarkConsolidation before it treats a
    // surviving plan's balance as fungible — see that class's own comment for
    // why. AllocationPlanProposer.ProposeSameSchedule/ProposeSameAmount and
    // EarmarkScaling were checked the same day and found not to need this:
    // the first two already carry the caller's own live jar-balance parameter
    // straight into both their sizing math and the resulting plan's own
    // StartingAllocation (nothing narrower in between to lose a surplus to);
    // Scale never touches balance at all, only the ongoing rate, by a fixed
    // ratio that preserves whatever glut proportion already existed. Proven
    // with real tests in each of those three, not just asserted here.
    public bool HasGlut => MilestoneAmount is decimal milestone && ExpectedAmount >= milestone;

    // The actual dollar amount HasGlut is checking the sign of — how much of
    // ExpectedAmount sits beyond what MilestoneAmount currently calls for.
    // Same boundary as HasGlut, not a stricter one: exactly on pace reads
    // GlutSurplus = 0 while HasGlut is still true (skipping the next
    // contribution is still safe at zero surplus, per HasGlut's own
    // reasoning) — this only adds the magnitude a caller needs to actually
    // carry a protected surplus forward, not a second opinion on whether one
    // exists. 0 whenever HasGlut is false, or there's no milestone to compare
    // against.
    public decimal GlutSurplus => MilestoneAmount is decimal milestone ? Math.Max(0m, ExpectedAmount - milestone) : 0m;
}
