namespace BuyTheBoat.Domain;

// How hard a "what could we afford" estimate hunts for money — the single caution dial the available-funds
// methods on AccountTransactionPage share, ordered least to most money counted as reachable. Named by the
// spending stance each takes so intent reads at the call site; the comment on each says exactly what it
// counts, since that is the thing to look up later.
public enum Frugality
{
    // Free funds only (the day's ExpectedFreeAmount) — nothing already set aside is touched.
    Relaxed,

    // Free funds + the safety cushion.
    Considerate,

    // Free funds + jars for non-mandatory expenses. Needs a target priority (see AvailableFundsFor).
    Thrifty,

    // Free funds + non-mandatory jars + jars for expenses with priority below the target's. Needs a
    // target priority too.
    Miserly,
}
