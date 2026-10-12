namespace BuyTheBoat.Domain;

/// <summary>
/// The deallocation math at the core of the "can I afford X?" engine, with no dependence on the rest of the forecast.
/// On a deallocation day, the money committed to jars would exceed what's in the account (the jars share one real balance), so this works out the give-back earmarks that pull money back out of jars, least important first.
/// </summary>
/// <remarks>
/// The algorithm, symbols, formulas, and invariants come from DeallocationProof.ods, and the tests check it against that workbook's examples.
/// Step A (paired earmarks, p): for each jar whose bill or goal was paid today (a paired transaction), takes its cost from that jar.
/// Step B (balancing earmarks, b): covers everything still owed by draining every jar in drain order, and once the need is met, cancels any scheduled earmark that's no longer affordable.
/// TransactionLogBookFactory.AppendDeallocationOrGoalReleases calls this from the forecast.
/// </remarks>
public static class DeallocationCalculator
{
    /// <summary>
    /// [CALC] Reports whether today is a deallocation day: yesterday's free funds plus all of today's money movement lands below zero, so jars must give money back.
    /// The forecast calls Deallocate only when this is true.
    /// </summary>
    /// <param name="currentFunds">c — the balance carried into the day, always &gt;= 0.</param>
    /// <param name="jars">Every fund jar's current state.</param>
    /// <param name="pairedTransactions">ap — today's purchases tied to a specific goal jar.</param>
    /// <param name="unpairedTransaction">au — today's spend not tied to any goal jar.</param>
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

    /// <summary>[CALC] Computes the deallocation earmarks (paired + balancing) for every jar, and how far, if at all, the balance goes into debt.</summary>
    /// <param name="currentFunds">c — the balance carried into the day, always &gt;= 0.</param>
    /// <param name="jars">Per-jar state (finance id, priority, balance, existing earmarks). Priority 0 is the cushion, drained first; higher priority is drained later.</param>
    /// <param name="pairedTransactions">ap — a goal's own purchase, pulling from its jar.</param>
    /// <param name="unpairedTransaction">au — the day's spend not tied to any goal jar.</param>
    public static DeallocationResult Deallocate(
        decimal currentFunds,
        IReadOnlyList<DeallocationJar> jars,
        IReadOnlyList<PairedTransaction> pairedTransactions,
        decimal unpairedTransaction)
    {
        // Adds each paired transaction to its jar's share and to the total (Step A uses the shares, Σapx the total).
        // A paired transaction with no matching jar is a caller bug, so the loop below throws rather than lose the money.
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

        // The drain order: the cushion, then skippable jars, then must-pay jars, lowest priority first within each group, so a must-pay bill is never drained while a skippable goal still holds money.
        // Ties keep their original order, so the result is deterministic.
        var ordered = jars
            .Select((jar, index) => (jar, index))
            .OrderBy(item => DrainGroup(item.jar))
            .ThenBy(item => item.jar.Priority)
            .ThenBy(item => item.index)
            .Select(item => item.jar)
            .ToList();

        // === Step A: works out each jar's paired earmark p and its balance after it ===
        // px = Max(-fx, apx): takes the cost, but never more than the jar holds; a jar with no paired transaction gets 0.
        // A positive apx (a goal that was adding, not spending) passes straight through.
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

        // === Step B: works out each jar's balancing earmark b, in drain order ===
        // Nb1 = c + au + Σapx - ΣFax: everything still to pull from jars after Step A.
        var need = currentFunds + unpairedTransaction + pairedTotal - afterATotal;

        var outcomes = new List<JarDeallocation>(ordered.Count);
        foreach (var jar in ordered)
        {
            var key = JarKey(jar);
            var fa = remainingAfterA[key];
            var p = paidEarmark[key];

            // W = Max(-Fax, Nbx): withdraws the lesser of the whole jar and what's still needed; 0 once the need is met.
            var withdrawal = Math.Max(-fa, need);

            // b only makes up what the scheduled earmarks (er + ei) don't already move.
            // Once the need is met, b = -(er + ei), cancelling a scheduled contribution that's no longer affordable.
            var b = withdrawal - jar.ExistingEarmark;

            // Nb(x+1) = Nbx - bx - (er+ei) = Nbx - W.
            need -= withdrawal;

            // Goal 3.x: fx + erx + eix + px + bx = Fbx, the jar's end-of-day balance; the forecast reaches the same number by applying the day's earmark events.
            var remaining = jar.Balance + jar.ExistingEarmark + p + b;

            outcomes.Add(new JarDeallocation
            {
                FinanceId = jar.FinanceId,
                PairedEarmark = p,
                BalancingEarmark = b,
                RemainingBalance = remaining,
            });
        }

        // Nbn+1: 0 when solvent; in debt it's negative and equals c + Σapx + au (Goal 2.2), how far the real balance goes below zero.
        return new DeallocationResult
        {
            Jars = outcomes,
            DebtRemainder = need,
        };
    }

    /// <summary>
    /// [CALC] Returns which of the three drain groups a jar falls in (see the sort in Deallocate).
    /// The cushion goes first because it's the cushion, not because of its priority number.
    /// </summary>
    /// <param name="jar">The jar to classify.</param>
    private static int DrainGroup(DeallocationJar jar) => jar switch
    {
        { FinanceId: null } => 0, // the safety cushion — always drained first
        { Skippable: true } => 1, // the user said they could skip or delay this
        _ => 2,                   // the user said they have to pay this
    };

    /// <summary>The cushion's dictionary key, since its finance id is null; there's only one cushion per snapshot (9.5.a1).</summary>
    private const int CushionKey = int.MinValue;

    /// <summary>[CALC] Returns a dictionary key for a jar: its FinanceId, or CushionKey for the cushion.</summary>
    /// <param name="jar">The jar to key.</param>
    private static int JarKey(DeallocationJar jar) => jar.FinanceId ?? CushionKey;
}

/// <summary>One jar's state before deallocation, an input row for Steps A and B.</summary>
/// <param name="FinanceId">Null for the safety cushion.</param>
/// <param name="Priority">0 for the cushion; 1 is the lowest a user can set, and higher priorities are drained later.</param>
/// <param name="Balance">f: the jar's balance from the previous day, never negative.</param>
/// <param name="ExistingEarmark">er + ei: the repeated and isolated earmarks already scheduled for this jar today, summed; any sign.</param>
/// <param name="Skippable">Whether the user said they could skip or delay what this jar is for (the opposite of FinancialPattern.Mandatory); skippable jars are drained before any must-pay one. Defaults to false, "I have to pay this".</param>
public sealed record DeallocationJar(
    int? FinanceId,
    int Priority,
    decimal Balance,
    decimal ExistingEarmark,
    bool Skippable = false);

/// <summary>
/// A paired transaction (ap): a bill or goal's own payment, taken from its jar.
/// Negative means the money was spent; positive means the goal was adding.
/// </summary>
public sealed record PairedTransaction(int FinanceId, decimal Amount);

/// <summary>One jar's deallocation earmarks and resulting balance.</summary>
public sealed record JarDeallocation
{
    public required int? FinanceId { get; init; }

    /// <summary>p: the Step A paired earmark; 0 for a jar with no paired transaction.</summary>
    public required decimal PairedEarmark { get; init; }

    /// <summary>b: the Step B balancing earmark.</summary>
    public required decimal BalancingEarmark { get; init; }

    /// <summary>p + b: the single isolated earmark added for this jar on a deallocation day, since both merge.</summary>
    public decimal TotalEarmark => PairedEarmark + BalancingEarmark;

    /// <summary>Fbx: the jar's balance after the day's earmarks (Goal 3.x), never negative.</summary>
    public required decimal RemainingBalance { get; init; }
}

public sealed record DeallocationResult
{
    /// <summary>Each jar's outcome, in drain order.</summary>
    public required IReadOnlyList<JarDeallocation> Jars { get; init; }

    /// <summary>Nbn+1: 0 when the day is solvent; when in debt, negative and equal to c + Σapx + au, how far below zero the real balance lands.</summary>
    public required decimal DebtRemainder { get; init; }

    public bool InDebt => DebtRemainder < 0m;

    /// <summary>The earmark events the forecast adds to the day, skipping jars whose total earmark is zero.</summary>
    public IEnumerable<(int? FinanceId, decimal Amount)> EarmarkEvents =>
        Jars.Where(jar => jar.TotalEarmark != 0m)
            .Select(jar => (jar.FinanceId, jar.TotalEarmark));
}
