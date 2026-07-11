namespace MyMoneyForecast.Domain;

// The pure deallocation math — the Q2 ("can I afford X?") engine's core,
// implemented standalone and side-effect-free per Step 1 of
// redesign/MyMoneyForecast/planning/07-deallocation-implementation-plan.md.
//
// On a "deallocation day" the money committed to fund jars would exceed what's
// actually in the account (the jars are bookkeeping over ONE real balance, not
// separate accounts). This computes the negative "give-back" earmarks that
// pull money back out of jars — draining the least-important jars first — so
// total allocation never exceeds available funds.
//
// The algorithm, symbols, formulas, worked examples and invariants are all
// from redesign/MyMoneyForecast/planning/06-deallocation-math.md, reconstructed
// from DeallocationProof.ods. Two steps:
//   Step A (paired earmarks, p) — for each jar whose saved-for goal was bought
//     (a paired transaction), pull its cost from that jar; leftover rolls on.
//   Step B (balancing earmarks, b) — cover everything still owed by draining
//     EVERY jar in priority order (lowest first), and past the point the need
//     is met, cancel any now-unaffordable scheduled earmark on each jar.
//
// No cascade integration lives here (that's Step 2): this is proof-faithful
// math with zero dependence on the rest of the engine, validated against 06's
// examples + invariants and vectors mined from the proof workbook.
public static class DeallocationCalculator
{
    // The deallocation-day test (06): free funds yesterday (c - Σfx) plus all
    // of today's money movement (Σapx + au) lands below zero — spending
    // outran free funds, so jars must give money back. Step 2 uses this to
    // decide whether to call Deallocate at all; Deallocate itself assumes the
    // caller already confirmed it (Nb1 is guaranteed < 0 on a deallocation
    // day, so Step B only ever withdraws).
    public static bool IsDeallocationDay(
        decimal currentFunds,
        IReadOnlyList<DeallocationJar> jars,
        IReadOnlyList<PairedTransaction> pairedTransactions,
        decimal unpairedTransaction)
    {
        var jarTotal = 0m;
        foreach (var jar in jars)
        {
            jarTotal += jar.Balance;
        }

        var pairedTotal = 0m;
        foreach (var paired in pairedTransactions)
        {
            pairedTotal += paired.Amount;
        }

        return currentFunds - jarTotal + pairedTotal + unpairedTransaction < 0m;
    }

    // Compute the deallocation earmarks (paired p + balancing b) and how far,
    // if at all, the balance goes into debt.
    //
    //   currentFunds        c   — the balance carried into the day, >= 0.
    //   jars                per-jar (finance id, priority, balance f, existing
    //                       earmark er+ei). Priority 0 = cushion (drained
    //                       first); higher priority is drained later.
    //   pairedTransactions  ap  — a goal's own purchase, pulling from its jar.
    //   unpairedTransaction au  — the day's spend not tied to any goal jar.
    public static DeallocationResult Deallocate(
        decimal currentFunds,
        IReadOnlyList<DeallocationJar> jars,
        IReadOnlyList<PairedTransaction> pairedTransactions,
        decimal unpairedTransaction)
    {
        // Paired amounts fold by finance id (Σapx uses the total; Step A uses
        // each jar's own share). A paired transaction with no matching jar is
        // a caller bug — the money would silently vanish from the math.
        var pairedByJar = new Dictionary<int, decimal>();
        var pairedTotal = 0m;
        foreach (var paired in pairedTransactions)
        {
            pairedByJar[paired.FinanceId] =
                pairedByJar.GetValueOrDefault(paired.FinanceId) + paired.Amount;
            pairedTotal += paired.Amount;
        }

        foreach (var financeId in pairedByJar.Keys)
        {
            if (!jars.Any(jar => jar.FinanceId == financeId))
            {
                throw new ArgumentException(
                    $"Paired transaction references finance id {financeId}, which has no jar.",
                    nameof(pairedTransactions));
            }
        }

        // Step B drains lowest priority first (cushion at 0). Stable on the
        // original position so equal priorities — which the domain forbids but
        // the pure function shouldn't assume away — stay deterministic.
        var ordered = jars
            .Select((jar, index) => (jar, index))
            .OrderBy(item => item.jar.Priority)
            .ThenBy(item => item.index)
            .Select(item => item.jar)
            .ToList();

        // === Step A — paired earmarks (p) ===
        // px = Max(-fx, apx): pull the goal's cost, but never more than the jar
        // holds (-fx empties it). Jars with no paired transaction take apx = 0,
        // so px = Max(-fx, 0) = 0 and are untouched by Step A. Positive apx
        // (a goal that was adding, not spending) flows straight through.
        var paidEarmark = new Dictionary<int, decimal>(ordered.Count);
        var remainingAfterA = new Dictionary<int, decimal>(ordered.Count);
        var afterATotal = 0m;
        foreach (var jar in ordered)
        {
            var ap = jar.FinanceId is { } id ? pairedByJar.GetValueOrDefault(id) : 0m;
            var p = Math.Max(-jar.Balance, ap);
            var fa = jar.Balance + p;            // Fax = fx + px, still >= 0

            var key = JarKey(jar);
            paidEarmark[key] = p;
            remainingAfterA[key] = fa;
            afterATotal += fa;
        }

        // === Step B — balancing earmarks (b) ===
        // Nb1 = c + au + Σapx - ΣFax: everything still to pull from jars once
        // Step A's releases are accounted for.
        var need = currentFunds + unpairedTransaction + pairedTotal - afterATotal;

        var outcomes = new List<JarDeallocation>(ordered.Count);
        foreach (var jar in ordered)
        {
            var key = JarKey(jar);
            var fa = remainingAfterA[key];
            var p = paidEarmark[key];

            // W = Max((0 - Fax), Nbx) with the floor M = 0 (jars never go
            // negative): withdraw the lesser of the whole jar and what's still
            // needed. Once the need is met (Nb >= 0), W = 0.
            var withdrawal = Math.Max(-fa, need);

            // The new earmark only makes up the difference the scheduled
            // earmarks (er + ei) don't already move. Past the point the need
            // is met this is b = -(er + ei), cancelling a now-unaffordable
            // scheduled contribution; it is 0 for a jar with nothing scheduled.
            var b = withdrawal - jar.ExistingEarmark;

            // Nb(x+1) = Nbx - bx - (er+ei) = Nbx - W.
            need -= withdrawal;

            // Goal 3.x: fx + erx + eix + px + bx = Fbx — the jar's real
            // end-of-day balance, which the cascade will reproduce as
            // max(0, previous + today's earmark events).
            var remaining = jar.Balance + jar.ExistingEarmark + p + b;

            outcomes.Add(new JarDeallocation
            {
                FinanceId = jar.FinanceId,
                PairedEarmark = p,
                BalancingEarmark = b,
                RemainingBalance = remaining,
            });
        }

        // Nbn+1: 0 when solvent; in debt it is negative and equals c + Σapx +
        // au (Goal 2.2) — exactly how far the real balance goes below zero.
        return new DeallocationResult
        {
            Jars = outcomes,
            DebtRemainder = need,
        };
    }

    // Cushion jars share the null finance id, so they can't key a dictionary
    // by finance id. There is only ever one cushion per snapshot (9.5.a1), so
    // a single sentinel is enough to distinguish it from the numbered jars.
    private const int CushionKey = int.MinValue;

    private static int JarKey(DeallocationJar jar) => jar.FinanceId ?? CushionKey;
}

// One jar's pre-deallocation state (a Step-A/B input row).
public sealed record DeallocationJar(
    // null = the safety cushion jar (finance_id == None).
    int? FinanceId,
    // 0 = cushion (lowest, drained first); 1 = lowest user-settable goal;
    // higher = more important, drained later.
    int Priority,
    // f: the previous day's jar amount (current_amount), >= 0.
    decimal Balance,
    // er + ei: the repeated + isolated earmarks already scheduled for this jar
    // today, summed. Any sign.
    decimal ExistingEarmark);

// A paired transaction (ap): a goal's own purchase, pulling from its jar.
// Negative = we bought what we saved for; positive = the goal was adding.
public sealed record PairedTransaction(int FinanceId, decimal Amount);

// The deallocation earmarks + resulting balance for one jar.
public sealed record JarDeallocation
{
    public required int? FinanceId { get; init; }

    // p: the Step-A paired earmark (0 for jars with no paired transaction).
    public required decimal PairedEarmark { get; init; }

    // b: the Step-B balancing earmark.
    public required decimal BalancingEarmark { get; init; }

    // p + b: the single implicit isolated earmark to append for this jar on a
    // deallocation day (both are system-made isolated earmarks that merge).
    public decimal TotalEarmark => PairedEarmark + BalancingEarmark;

    // Fbx: the jar's balance after the day's earmarks (Goal 3.x). >= 0.
    public required decimal RemainingBalance { get; init; }
}

public sealed record DeallocationResult
{
    // Per-jar outcomes in the order they were processed (priority ascending,
    // cushion first).
    public required IReadOnlyList<JarDeallocation> Jars { get; init; }

    // Nbn+1: 0 when the day is solvent; negative in the debt case, equal to
    // c + Σapx + au (how far below zero the real balance lands).
    public required decimal DebtRemainder { get; init; }

    public bool InDebt => DebtRemainder < 0m;

    // The nonzero implicit isolated earmark events Step 2 appends to the day
    // (jars whose total give-back/cancellation is zero contribute no event).
    public IEnumerable<(int? FinanceId, decimal Amount)> EarmarkEvents =>
        Jars.Where(jar => jar.TotalEarmark != 0m)
            .Select(jar => (jar.FinanceId, jar.TotalEarmark));
}
