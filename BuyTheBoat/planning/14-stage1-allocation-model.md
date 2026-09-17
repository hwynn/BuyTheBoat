# 14 — Stage 1: The allocation model ("a jar for everything")

Stage 1 of the ["Adjusting the Plan" phase](13-adjusting-the-plan-charter.md); design complete and built.

**What it settled.** Every scheduled outflow (a bill, a non-mandatory expected transaction, a
transfer's withdrawal) gets a fund jar that reserves against free funds, backed by a real, editable
`EarMarkPattern` — user-facing name **Allocation Plan** — proposed and pre-filled at creation by a
scanner that looks at income. A jar always has a plan behind it or doesn't exist; nothing creates a
savings plan silently. **Income never gets a jar.** A transfer reserves in the account it leaves,
which lowers that account's own free funds like any other outflow's reservation.

- **`Mandatory` became skippability** — it no longer governs *whether* something reserves, only drain
  order: an unskippable jar is emptied only after every skippable one is.
- **"Thin" = the safety cushion is not whole** (the middle warning state, between healthy and short).
- Deleting a goal removes its Allocation Plan after confirming.

The original computed **A/B ramp** was retired in favour of Allocation Plans; the A/B logic survives
only as the one-time default-plan *proposer*, not a per-day curve.

**Built — see the code:** `AllocationPlanProposer`, `FundJar`, `TransactionLogBookFactory` (jar
cascade, skippable drain order), `DeallocationCalculator` (unskippable-last). Covers charter items
1, 2, 11–15 and planning-10's parked jar sub-questions.

**Still open (minor):** A-1 — whether the jar is strictly outflows-only (recommended, weakly open);
B-4 — a wording sub-decision on skippability. Actuals notes → [12](12-actual-transactions-deferred-design.md).
