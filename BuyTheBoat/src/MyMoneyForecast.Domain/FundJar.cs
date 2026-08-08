namespace MyMoneyForecast.Domain;

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
}
