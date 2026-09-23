using BuyTheBoat.Domain;
using Shouldly;

namespace BuyTheBoat.Domain.Tests;

// Proof-faithful tests for the pure deallocation math (Step 1 of
// the deallocation algorithm).
//
// Three layers of evidence, strongest last:
//   1. The two worked examples from the proof, asserted to the
//      cent (exact per-jar p/b and resulting balances).
//   2. The three end-goal invariants (Goal 1, Goal 2.1/2.2, Goal 3.x) applied
//      to every scenario as a reusable oracle.
//   3. 14 numeric vectors mined straight from DeallocationProof.ods (sheet
//      DeallTest_2, the author's own randomised test grid) — the definitive
//      "does this match the proof" check, covering debt, fractional cents,
//      positive paired transactions, and mixed-sign scheduled earmarks.
public class DeallocationCalculatorTests
{
    // ===== Worked example 1 — simple, no paired transactions =====
    // c = 200; jars f = [50, 0, 120, 10]; free = 20; one unpaired au = -100.
    // 80 must come out of jars: jar 1 gives 50, jar 3 gives the last 30.
    [Fact]
    public void Worked_example_1_drains_lowest_priority_jars_first()
    {
        var jars = new List<DeallocationJar>
        {
            new(FinanceId: 1, Priority: 1, Balance: 50m, ExistingEarmark: 0m),
            new(FinanceId: 2, Priority: 2, Balance: 0m, ExistingEarmark: 0m),
            new(FinanceId: 3, Priority: 3, Balance: 120m, ExistingEarmark: 0m),
            new(FinanceId: 4, Priority: 4, Balance: 10m, ExistingEarmark: 0m),
        };

        var result = DeallocationCalculator.Deallocate(
            currentFunds: 200m, jars, pairedTransactions: [], unpairedTransaction: -100m);

        // No paired transactions: Step A is a no-op.
        result.Jars.ShouldAllBe(jar => jar.PairedEarmark == 0m);

        JarOut(result, 1).BalancingEarmark.ShouldBe(-50m);
        JarOut(result, 2).BalancingEarmark.ShouldBe(0m);
        JarOut(result, 3).BalancingEarmark.ShouldBe(-30m);
        JarOut(result, 4).BalancingEarmark.ShouldBe(0m);

        JarOut(result, 1).RemainingBalance.ShouldBe(0m);
        JarOut(result, 2).RemainingBalance.ShouldBe(0m);
        JarOut(result, 3).RemainingBalance.ShouldBe(90m);
        JarOut(result, 4).RemainingBalance.ShouldBe(10m);

        result.InDebt.ShouldBeFalse();
        result.DebtRemainder.ShouldBe(0m);
        // Final jars total the money that's actually left: c + au = 100.
        result.Jars.Sum(jar => jar.RemainingBalance).ShouldBe(100m);

        AssertProofInvariants(200m, jars, [], -100m, result);
    }

    // ===== Worked example 2 — a paired transaction =====
    // c = 700; jars f = [50, 0, 120, 500]; free = 30; unpaired au = -100;
    // paired on jar 4 (the boat) ap4 = -550 — pricier than the 500 saved.
    [Fact]
    public void Worked_example_2_pulls_the_bought_goal_from_its_own_jar_then_balances_the_rest()
    {
        var jars = new List<DeallocationJar>
        {
            new(FinanceId: 1, Priority: 1, Balance: 50m, ExistingEarmark: 0m),
            new(FinanceId: 2, Priority: 2, Balance: 0m, ExistingEarmark: 0m),
            new(FinanceId: 3, Priority: 3, Balance: 120m, ExistingEarmark: 0m),
            new(FinanceId: 4, Priority: 4, Balance: 500m, ExistingEarmark: 0m),
        };
        var paired = new List<PairedTransaction> { new(FinanceId: 4, Amount: -550m) };

        var result = DeallocationCalculator.Deallocate(
            currentFunds: 700m, jars, paired, unpairedTransaction: -100m);

        // Step A empties jar 4 (can't give more than its 500); 50 rolls over.
        JarOut(result, 4).PairedEarmark.ShouldBe(-500m);
        result.Jars.Where(jar => jar.FinanceId != 4).ShouldAllBe(jar => jar.PairedEarmark == 0m);

        // Step B: jar 1 gives 50, jar 3 gives the leftover 70.
        JarOut(result, 1).BalancingEarmark.ShouldBe(-50m);
        JarOut(result, 2).BalancingEarmark.ShouldBe(0m);
        JarOut(result, 3).BalancingEarmark.ShouldBe(-70m);
        JarOut(result, 4).BalancingEarmark.ShouldBe(0m);

        JarOut(result, 3).RemainingBalance.ShouldBe(50m);
        result.DebtRemainder.ShouldBe(0m);
        // c + au + ap4 = 700 - 100 - 550 = 50.
        result.Jars.Sum(jar => jar.RemainingBalance).ShouldBe(50m);

        AssertProofInvariants(700m, jars, paired, -100m, result);
    }

    // ===== The Q1 resolution example — cancelling an unaffordable scheduled
    // earmark on a jar past the point the need is met (Intention 2) =====
    // c = 100, jars f = [30, 40]; a scheduled +20 on jar 2; unpaired -60.
    [Fact]
    public void Step_B_iterates_all_jars_cancelling_scheduled_earmarks_it_can_no_longer_afford()
    {
        var jars = new List<DeallocationJar>
        {
            new(FinanceId: 1, Priority: 1, Balance: 30m, ExistingEarmark: 0m),
            new(FinanceId: 2, Priority: 2, Balance: 40m, ExistingEarmark: 20m),
        };

        var result = DeallocationCalculator.Deallocate(
            currentFunds: 100m, jars, pairedTransactions: [], unpairedTransaction: -60m);

        // The spending gap is covered by jar 1 alone...
        JarOut(result, 1).BalancingEarmark.ShouldBe(-30m);
        // ...but jar 2's now-unaffordable planned +20 contribution is cancelled
        // rather than left to over-allocate.
        JarOut(result, 2).BalancingEarmark.ShouldBe(-20m);

        JarOut(result, 1).RemainingBalance.ShouldBe(0m);
        JarOut(result, 2).RemainingBalance.ShouldBe(40m);
        result.Jars.Sum(jar => jar.RemainingBalance).ShouldBe(40m); // c + au

        AssertProofInvariants(100m, jars, [], -60m, result);
    }

    // ===== Debt case (Q2 / Goal 2.2) =====
    // Spending exceeds current funds: every jar drains to 0 and free balance
    // goes negative by exactly Nbn+1 = c + Σap + au.
    [Fact]
    public void Debt_case_drains_every_jar_to_zero_and_reports_how_far_negative_the_balance_goes()
    {
        var jars = new List<DeallocationJar>
        {
            new(FinanceId: 1, Priority: 1, Balance: 30m, ExistingEarmark: 0m),
        };

        var result = DeallocationCalculator.Deallocate(
            currentFunds: 100m, jars, pairedTransactions: [], unpairedTransaction: -200m);

        result.InDebt.ShouldBeTrue();
        JarOut(result, 1).RemainingBalance.ShouldBe(0m);
        // c + Σap + au = 100 + 0 - 200 = -100.
        result.DebtRemainder.ShouldBe(-100m);

        AssertProofInvariants(100m, jars, [], -200m, result);
    }

    // ===== IsDeallocationDay: c - Σfx + Σapx + au < 0 =====
    [Fact]
    public void IsDeallocationDay_is_true_only_when_spending_outruns_free_funds()
    {
        var jars = new List<DeallocationJar>
        {
            new(FinanceId: 1, Priority: 1, Balance: 120m, ExistingEarmark: 0m),
        };

        // free funds = c - Σf = 150 - 120 = 30; a 20 spend fits.
        DeallocationCalculator.IsDeallocationDay(150m, jars, [], -20m).ShouldBeFalse();
        // a 40 spend does not.
        DeallocationCalculator.IsDeallocationDay(150m, jars, [], -40m).ShouldBeTrue();
        // exactly at zero is not a deallocation day (strict <).
        DeallocationCalculator.IsDeallocationDay(150m, jars, [], -30m).ShouldBeFalse();
    }

    [Fact]
    public void A_paired_transaction_referencing_a_missing_jar_is_rejected()
    {
        var jars = new List<DeallocationJar>
        {
            new(FinanceId: 1, Priority: 1, Balance: 50m, ExistingEarmark: 0m),
        };

        Should.Throw<ArgumentException>(() => DeallocationCalculator.Deallocate(
            currentFunds: 100m, jars,
            pairedTransactions: [new(FinanceId: 99, Amount: -10m)],
            unpairedTransaction: 0m));
    }

    // ===== Mined proof vectors — the definitive fidelity check =====
    [Theory]
    [MemberData(nameof(ProofVectorCases))]
    public void Matches_the_proof_workbook_vectors(ProofVector v)
    {
        // Four jars, priority ascending (jar index n = index in the sheet's
        // fund-jar list, ordered lowest priority first). Finance ids 1..4.
        var jars = new List<DeallocationJar>();
        for (var j = 0; j < 4; j++)
        {
            jars.Add(new DeallocationJar(
                FinanceId: j + 1, Priority: j + 1, Balance: v.F[j], ExistingEarmark: v.Existing[j]));
        }

        var paired = new List<PairedTransaction>();
        for (var j = 0; j < 4; j++)
        {
            if (v.Ap[j] != 0m)
            {
                paired.Add(new PairedTransaction(FinanceId: j + 1, Amount: v.Ap[j]));
            }
        }

        var result = DeallocationCalculator.Deallocate(v.C, jars, paired, v.Au);

        const decimal tolerance = 0.005m; // sheet values carry float rounding
        for (var j = 0; j < 4; j++)
        {
            var outcome = JarOut(result, j + 1);
            outcome.PairedEarmark.ShouldBe(v.P[j], tolerance);
            outcome.BalancingEarmark.ShouldBe(v.B[j], tolerance);
            outcome.RemainingBalance.ShouldBe(v.Fb[j], tolerance);
        }

        result.DebtRemainder.ShouldBe(v.DebtRemainder, tolerance);
        result.InDebt.ShouldBe(v.InDebt);

        // The invariants must hold on every mined case too.
        AssertProofInvariants(v.C, jars, paired, v.Au, result);
    }

    // ---- the three end-goal invariants from the proof, as a reusable oracle ----
    private static void AssertProofInvariants(
        decimal c,
        IReadOnlyList<DeallocationJar> jars,
        IReadOnlyList<PairedTransaction> paired,
        decimal au,
        DeallocationResult result)
    {
        var sumExisting = jars.Sum(jar => jar.ExistingEarmark);   // Σer + Σei
        var sumF = jars.Sum(jar => jar.Balance);                  // Σfx
        var sumAp = paired.Sum(p => p.Amount);                    // Σapx
        var sumDealloc = result.Jars.Sum(jar => jar.TotalEarmark); // Σpx + Σbx

        // Goal 1 — every earmark on the day (scheduled + deallocation) plus any
        // un-deallocatable debt totals the amount to withdraw from jars.
        (sumExisting + sumDealloc + result.DebtRemainder)
            .ShouldBe(c - sumF + sumAp + au);

        // Goal 2.1 / 2.2 — the debt terminus.
        if (-sumAp - au <= c)
        {
            result.DebtRemainder.ShouldBe(0m);
        }
        else
        {
            result.DebtRemainder.ShouldBe(c + sumAp + au);
        }

        // Goal 3.x — each jar ends at its initial amount plus all its earmarks,
        // and never goes negative.
        foreach (var jar in jars)
        {
            var outcome = JarOut(result, jar.FinanceId!.Value);
            outcome.RemainingBalance.ShouldBe(
                jar.Balance + jar.ExistingEarmark + outcome.PairedEarmark + outcome.BalancingEarmark);
            outcome.RemainingBalance.ShouldBeGreaterThanOrEqualTo(0m);
        }
    }

    private static JarDeallocation JarOut(DeallocationResult result, int financeId) =>
        result.Jars.Single(jar => jar.FinanceId == financeId);

    public static IEnumerable<object[]> ProofVectorCases() =>
        MinedVectors.Select(v => new object[] { v });

    // A mined test case: inputs (F, Existing = er+ei, Ap, Au, C) and the
    // proof's expected outputs (P, B, Fb, DebtRemainder). One per DeallTest_2
    // column where the scenario is a valid deallocation day.
    public sealed record ProofVector(
        int Col, decimal C, decimal[] F, decimal[] Existing, decimal[] Ap, decimal Au,
        decimal[] P, decimal[] B, decimal[] Fb, decimal DebtRemainder, bool InDebt)
    {
        public override string ToString() => $"DeallTest_2 col {Col}";
    }

    // Auto-extracted from DeallocationProof.ods sheet DeallTest_2 (columns where
    // Skippability outranks the priority number outright.
    // Everything the user said they could skip is emptied before anything they
    // said they have to pay is touched.
    [Fact]
    public void Skippable_jars_drain_before_unskippable_ones_whatever_their_priorities()
    {
        // The UNSKIPPABLE jar carries the lower priority number, which before
        // item B would have made it drain first. $100 has to come back.
        var jars = new List<DeallocationJar>
        {
            new(FinanceId: 1, Priority: 1, Balance: 300m, ExistingEarmark: 0m, Skippable: false),
            new(FinanceId: 2, Priority: 9, Balance: 300m, ExistingEarmark: 0m, Skippable: true),
        };

        var result = DeallocationCalculator.Deallocate(
            currentFunds: 500m, jars, pairedTransactions: [], unpairedTransaction: 0m);

        result.Jars.Single(jar => jar.FinanceId == 1).RemainingBalance.ShouldBe(300m); // protected
        result.Jars.Single(jar => jar.FinanceId == 2).RemainingBalance.ShouldBe(200m); // gave the 100 back
    }

    [Fact]
    public void The_cushion_still_drains_before_everything_including_skippable_jars()
    {
        var jars = new List<DeallocationJar>
        {
            new(FinanceId: 2, Priority: 9, Balance: 300m, ExistingEarmark: 0m, Skippable: true),
            new(FinanceId: null, Priority: 0, Balance: 100m, ExistingEarmark: 0m),
        };

        var result = DeallocationCalculator.Deallocate(
            currentFunds: 350m, jars, pairedTransactions: [], unpairedTransaction: 0m);

        // $50 needed: the cushion covers it alone and the skippable jar is left
        // whole. The cushion goes first by identity, not by its priority number.
        result.Jars.Single(jar => jar.FinanceId is null).RemainingBalance.ShouldBe(50m);
        result.Jars.Single(jar => jar.FinanceId == 2).RemainingBalance.ShouldBe(300m);
    }

    [Fact]
    public void Priority_still_orders_jars_within_the_same_skippability()
    {
        var jars = new List<DeallocationJar>
        {
            new(FinanceId: 1, Priority: 5, Balance: 300m, ExistingEarmark: 0m, Skippable: true),
            new(FinanceId: 2, Priority: 1, Balance: 300m, ExistingEarmark: 0m, Skippable: true),
        };

        var result = DeallocationCalculator.Deallocate(
            currentFunds: 500m, jars, pairedTransactions: [], unpairedTransaction: 0m);

        result.Jars.Single(jar => jar.FinanceId == 2).RemainingBalance.ShouldBe(200m); // lower priority first
        result.Jars.Single(jar => jar.FinanceId == 1).RemainingBalance.ShouldBe(300m);
    }

    // sensible allocation == 1 AND Deallocation == 1). Regenerate with
    // redesign/extract_deallocation_vectors.py if the workbook changes.
    private static readonly ProofVector[] MinedVectors =
    [
        new(Col: 2, C: 268m, F: [200m, 40m, 0m, 0m], Existing: [0m, 0m, 0m, 0m], Ap: [0m, 0m, 0m, 0m], Au: -80m, P: [0m, 0m, 0m, 0m], B: [-52m, 0m, 0m, 0m], Fb: [148m, 40m, 0m, 0m], DebtRemainder: 0m, InDebt: false),
        new(Col: 6, C: 234m, F: [36m, 0m, 0m, 4m], Existing: [83m, 0m, 42m, -4m], Ap: [-55m, -14m, -81m, -0m], Au: -972m, P: [-36m, 0m, 0m, -0m], B: [-83m, 0m, -42m, 0m], Fb: [0m, 0m, 0m, 0m], DebtRemainder: -888m, InDebt: true),
        new(Col: 7, C: 90m, F: [0m, 0m, 8.16m, 21.75m], Existing: [3m, -73m, -52m, 89m], Ap: [-0m, 1m, -0m, -20m], Au: -392m, P: [-0m, 1m, -0m, -20m], B: [-3m, 72m, 43.84m, -90.75m], Fb: [0m, 0m, 0m, 0m], DebtRemainder: -321m, InDebt: true),
        new(Col: 13, C: 532m, F: [55.61m, 17.5m, 1.25m, 0m], Existing: [0m, -23m, 134m, -69m], Ap: [-3m, -50m, -0m, -55m], Au: -1152m, P: [-3m, -17.5m, -0m, 0m], B: [-52.61m, 23m, -135.25m, 69m], Fb: [0m, 0m, 0m, 0m], DebtRemainder: -728m, InDebt: true),
        new(Col: 17, C: 960m, F: [0m, 410m, 12.58m, 0m], Existing: [0m, -105m, -53m, 0m], Ap: [-0m, 0m, -29m, 9m], Au: -672m, P: [-0m, 0m, -12.58m, 9m], B: [0m, -46m, 53m, 0m], Fb: [0m, 259m, 0m, 9m], DebtRemainder: 0m, InDebt: false),
        new(Col: 18, C: 268m, F: [85m, 0m, 0m, 8.25m], Existing: [40m, 124m, 5m, -100m], Ap: [36m, -21m, -74m, -44m], Au: -740m, P: [36m, 0m, 0m, -8.25m], B: [-161m, -124m, -5m, 100m], Fb: [0m, 0m, 0m, 0m], DebtRemainder: -575m, InDebt: true),
        new(Col: 19, C: 216m, F: [0m, 0m, 155m, 0m], Existing: [0m, -0m, 77m, 0m], Ap: [0m, -74m, -1m, 56m], Au: -648m, P: [0m, 0m, -1m, 56m], B: [0m, 0m, -231m, -56m], Fb: [0m, 0m, 0m, 0m], DebtRemainder: -451m, InDebt: true),
        new(Col: 23, C: 90m, F: [36m, 15.98m, 0m, 0m], Existing: [-140m, 88m, -110m, 0m], Ap: [0m, -74m, -0m, 0m], Au: -100m, P: [0m, -15.98m, -0m, 0m], B: [104m, -88m, 110m, 0m], Fb: [0m, 0m, 0m, 0m], DebtRemainder: -84m, InDebt: true),
        new(Col: 29, C: 194m, F: [92m, 0m, 0m, 0m], Existing: [0m, 0m, 89m, 0m], Ap: [-0m, -36m, -90m, -0m], Au: -600m, P: [-0m, 0m, 0m, -0m], B: [-92m, 0m, -89m, 0m], Fb: [0m, 0m, 0m, 0m], DebtRemainder: -532m, InDebt: true),
        new(Col: 36, C: 430m, F: [8m, 0m, 0m, 0m], Existing: [-54m, -0m, 0m, -41m], Ap: [0m, -0m, 0m, -28m], Au: -792m, P: [0m, -0m, 0m, 0m], B: [46m, 0m, 0m, 41m], Fb: [0m, 0m, 0m, 0m], DebtRemainder: -390m, InDebt: true),
        new(Col: 37, C: 275m, F: [0m, 74m, 4.25m, 0m], Existing: [0m, -47m, 15m, 0m], Ap: [0m, -0m, -71m, -0m], Au: -1020m, P: [0m, -0m, -4.25m, -0m], B: [0m, -27m, -15m, 0m], Fb: [0m, 0m, 0m, 0m], DebtRemainder: -816m, InDebt: true),
        new(Col: 44, C: 268m, F: [0m, 5.44m, 213m, 0m], Existing: [-56m, 0m, -102m, 0m], Ap: [-0m, 53m, -0m, -30m], Au: -74m, P: [-0m, 53m, -0m, 0m], B: [56m, -54.44m, 102m, 0m], Fb: [0m, 4m, 213m, 0m], DebtRemainder: 0m, InDebt: false),
        new(Col: 47, C: 112m, F: [0m, 7m, 0m, 20m], Existing: [74m, -35m, -121m, 90m], Ap: [-0m, -31m, -21m, 0m], Au: -84m, P: [-0m, -7m, 0m, 0m], B: [-74m, 35m, 121m, -110m], Fb: [0m, 0m, 0m, 0m], DebtRemainder: -24m, InDebt: true),
        new(Col: 48, C: 549m, F: [21m, 0m, 0m, 0m], Existing: [0m, -67m, 8m, -23m], Ap: [-100m, -0m, 73m, 0m], Au: -1640m, P: [-21m, -0m, 73m, 0m], B: [0m, 67m, -81m, 23m], Fb: [0m, 0m, 0m, 0m], DebtRemainder: -1118m, InDebt: true),
    ];
}
