# 05 — Restructure onto the original class organization, and the assumed-pairing philosophy

Implemented 2026-07-10, replacing the flat `ForecastCalculator` engine. Context: the lean rewrite answered Q1/Q3 but abandoned the original design's organizational spine, leaving Q2 with no home and the output split across two disconnected grids. The design's author ruled the original classes come back as human-readable data structures, with values computed by a day-by-day cascade. Full decision trail in the plan file (`immutable-hugging-squid` → `wise-churning-pumpkin`) and the session memory; the durable parts are here.

## The organization scheme (the "onion"), per the author

- **`TransactionLogBook`** — a book of ALL of a user's finance information.
- **`TransactionLogPage`** — one page: a large chunk of the calendar, showing everything about all accounts inside that range.
- **`AccountTransactionPage`** — a single account inside that page. Multiple accounts (checking/savings, transfers) come later; the layer exists now so they slot in without reshaping anything.
- **`AccountTransactionPage.BalanceRecord`** — `{date: BalanceSnapshot}`, one snapshot per event date (the adjust_snapshots rule), plus one dateless `InitialSnapshot` outside the record.
- **`BalanceSnapshot`** — the per-day answer container: its properties hold how much money exists, every fund jar (allocation + milestone = on-track status), and every event. The UI is layered views of this structure — the Forecast tab's timeline is the page, the detail pane is one snapshot.

Entry point: `TransactionLogBookFactory.CreateForecast(ForecastOptions) → ForecastResult` — a stateless, in-memory build per forecast run. Patterns + the entered balance remain the only persisted truth; snapshots/jars/events are never stored (deliberate scope decision — recompute-fresh cannot go stale, and Import/Export stays a byte-for-byte file copy).

The internal steps carry the documented cascade's names: an `AdjustSnapshots` materialization role (which dates get snapshots, what events they hold) and a `CascadePageBalanceRecord` value role (each snapshot's amounts derived from the previous snapshot's — never from anything later). Key formulas cite their assumption IDs inline (`10.3`, `3.13.5.3.a1`, `3.13.4.a1`, `3.13.5.4.a1`).

## The assumed-pairing philosophy (standing, adopted 2026-07-10)

Stated by the design's author:

> We assume that expected events will be paired with matching actual events. Decisions that would normally wait for actual events pairing up with an expected event can and should be made assuming that pairing will happen.

Rationale for the original "only an actual event can implicitly pull money out of a fund jar" rule (author-supplied, 2026-07-10 — the historical docs state the rule but never the reason): actual transactions were meant to be the driving force that triggers a deallocation day. Until import exists, expected transactions stand in as that trigger.

**Every code site that leans on this is tagged with a greppable comment token:**

- `ASSUMED-PAIRING(<topic>)` — logic that pretends expected→actual pairing already happened.
- `DIVERGENCE(<topic>)` — other deliberate departures from the original design.

When actual-transaction import becomes real, `grep -rn "ASSUMED-PAIRING" src/` is the worklist.

## Divergence registry

Regenerate with: `grep -rn "ASSUMED-PAIRING\|DIVERGENCE" src/` — the table below reflects the state at implementation time (18 tagged sites after `active-from` was added 2026-07-28; the factory's header mention of the convention is not itself a site).

### ASSUMED-PAIRING sites

| Tag | Where | What it covers |
|---|---|---|
| `(import)` | `ActualTransaction.cs` (whole type), `BalanceSnapshot.cs` (`ActualTransactions` always empty) | The stub class itself and every always-empty transaction list. |
| `(fulfillment)` | `ExpectedTransaction.cs` (`PairedAmount`/`PairedActualDate`) | Pairing fields present but dormant-null; all math proceeds as if fulfillment is certain. |
| `(actual-amount)` | `EarMarkEvent.cs` (`ActualAmount`) | "How much actually moved" is unknowable without actuals — always null. |
| `(unpaid-expected)` | `AccountTransactionPage.cs` + factory assembly | `current_unpaid_expected` pinned to 0: everything on/before the as-of date is assumed already fulfilled and reflected in the entered balance. |
| `(as-of-day-settled)` | `TransactionLogBookFactory.cs` (cascade value pass) | A snapshot dated exactly on the as-of date attaches its events for display but contributes no deltas — the entered balance already includes that day. |
| `(as-of-day)` | `FundJar.cs` (`CurrentAmount`), factory `BuildJars` | Jar `CurrentAmount` set equal to `ExpectedAmount` on the seed snapshot (treating everything to date as settled); null for all future dates, per the documented "None until the day occurs" rule. |
| `(3.13c.a10)` | `TransactionLogBookFactory.cs` (goal-release events) | The normal-day implicit jar withdrawal that the original design triggers off a PAIRED ACTUAL transaction is triggered off the expected occurrence directly. |

### DIVERGENCE sites

| Tag | Where | What it covers |
|---|---|---|
| `(positive-implicit)` | `EarMarkEvent.cs`, factory automatic funding event generation | The original design only ever created implicit isolated earmarks with negative amounts. The automatic funding mechanism creates POSITIVE implicit events stepping a jar along its accrual curve. Author-endorsed 2026-07-09/10. **Widened 2026-07-24 (planning/14 item A): this now applies to EVERY outflow without a savings plan, not just mandatory bills — `Mandatory` no longer gates reserving at all, and instead protects a jar from deallocation (item B).** **The curve is a per-day A/B choice the user wanted both halves of:** if a paycheck arrives before the bill's next due date → **A**, reserve a linear fraction of the current cycle (don't over-reserve for distant bills); if the bill's next occurrence beats the next paycheck → **B**, reserve the full amount immediately (short-term free balance stays honest). Trigger = "is there income between tomorrow and the next due date." A's ramp anchors to the previous occurrence; for a bill's **first-ever occurrence** (no prior cycle) it anchors to the forecast's as-of date, and B still reserves in full — closing an earlier gap where a brand-new bill reserved nothing until it hit. `BillAccrualAt` in `TransactionLogBookFactory.cs`. **REVISED 2026-07-24 (design settled, re-implementation pending — see [planning/14 Revision](14-stage1-allocation-model.md#revision-2026-07-24--allocation-plans-replace-the-ramp)):** the per-day ramp is **retired**. Outflows now fund via explicit **Allocation Plans** (`EarMarkPattern`-generated *repeated* earmarks — not implicit) plus user-confirmed **starting earmarks** (*explicit* isolated, `ExplicitAmount` set — not implicit). So `positive-implicit` narrows to **the safety-cushion refill alone** (the only remaining system-authored positive event with no user confirmation). The A/B logic survives as the one-time default-plan *proposer*, not a per-day curve. The code below still reflects the pre-revision ramp until re-implemented. |
| `(page-length)` | `TransactionLogBook.cs`, factory assembly | One window-sized page per forecast run; `PageLength = null`. Fixed month-length pages matter once persisted history and expired pages exist. |
| `(runoff)` | `AccountTransactionPage.PageRunoffData()` | Implemented as a documented stub returning the closing snapshot's data; nothing consumes it until multi-page books exist. |
| `(active-from)` | `RecurrenceRule.cs` (`ActiveFrom` field), `EarMarkPattern.Create` | A new property not in the documented ten: a "lead-in" date letting a pattern's active span (and its jar) begin before its first occurrence, so saving in advance holds `3.11.2.a2` / `3.13.5.a2` literally instead of relaxing them. `GetOccurrences` ignores it (occurrences unchanged); `EarMarkPattern.Create` checks the earmark's active span against the goal's in both directions. The creation paths (`OneTimeGoalFactory`, the proposer) set it only when the outflow starts after the as-of date, and a load-time migration backfills pre-existing goals. **Step 3 (2026-07-29):** the empty/declined-plan factory `AllocationPlanProposer.ProposeEmpty` also sets `ActiveFrom` — on the prepared outflow and on the `Amount=0` plan (single occurrence at the outflow's `Until`) — so a declined jar still reaches today. planning/15; ActiveFrom Steps 2–3, 2026-07-28/29. |

### Divergences without a code tag (structural, documented here)

- **No pairing mechanism at all** — `pair_actual_event` / `unpair_actual_event` / `ScanForMatchs` from the documented model have no C# analogue yet. When import lands, they belong on `AccountTransactionPage` (per `psuedo_functions.txt`'s placement).
- **No `cancelled`-setting UI** — `ExpectedTransaction.Cancelled` exists and is respected by the cascade sum, but nothing sets it yet (per-occurrence editing is future work).
- **`Expired` is always false** — nothing persists across runs, so nothing can expire.
- **Safety-cushion placeholders** — every snapshot carries the `finance_id = null` cushion jar (9.5.a1) with `ExpectedAmount = 0`, and `IdealSafetyCushion`/`SafetyPriority` sit at 0 on the page. Real cushion math is the next phase (below).
- **Per-day jar flooring** — jar values floor at 0 each day (`max(0, prev + events)`), matching the archived Python's `max(..., 0)`, rather than the flat engine's cumulative-then-floor. Differs only in the pathological "withdraw more than saved mid-history, then keep contributing" case, where per-day is the more sensible answer.

## Deferred, in dependency order

1. **Deallocation days / safety cushion / priority reallocation** (the Q2 engine — next pass). Under assumed pairing, an expected transaction that would overdraw free funds triggers the deallocation day. **Prerequisite reading is now DONE:** the distribution math lives in a separate `DeallocationProof.ods` (not an unmined `class documentation.ods` sheet as first guessed), and is fully written up in **[`06-deallocation-math.md`](06-deallocation-math.md)** — the two-step process (Step A paired earmarks, Step B balancing earmarks over all jars in priority order), the verbatim formulas, both worked examples, the three end-goal invariants (test oracles), the debt case, and the safety cushion (always priority 0). All five open questions on it were resolved with the author 2026-07-10. `DeallTest`/`Proof`/`Random` sheets there still hold ready-made numeric test vectors to mine when building the engine. The archived Python's `BalanceSnapshot.deallocation_fund_distribution()` is a secondary reference but its `is_deallocation_day()` is demonstrably unfinished — trust `06`, not that code.
2. **Actual-transaction import + pairing** — retire the ASSUMED-PAIRING sites.
3. **Multiple accounts** — populate `TransactionLogPage.AccountPages` beyond `"Primary"`; per-account balances and transfers.
4. **Fixed `PageLength` + cross-page runoff** — real `page_runoff_data` consumption, expired pages, cascade continuity between pages.
   - **Author correction, 2026-07-24:** this is **needed even in the no-actual-transactions design**, and should not be read as waiting on persisted history. The `(page-length)` divergence note above ties it to "once persisted history and expired pages exist," which undersells it — a user moving the horizon forward is meant to move *through pages*, not to stretch one page indefinitely. Today there is exactly **one** page per run, spanning as-of → horizon, rebuilt from scratch each time: no past pages, no future pages, `PageLength = null`, `Expired` permanently false. What is missing is the page *mechanism*, not merely old pages.
