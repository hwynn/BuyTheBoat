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

Regenerate with: `grep -rn "ASSUMED-PAIRING\|DIVERGENCE" src/` — the table below reflects the state at implementation time (16 tagged sites; the factory's header mention of the convention is not itself a site).

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
| `(positive-implicit)` | `EarMarkEvent.cs`, factory auto-bill event generation | The original design only ever created implicit isolated earmarks with negative amounts. The auto-bill reservation mechanism creates POSITIVE implicit events stepping each mandatory bill's jar along its accrual curve (linear ramp between due dates; snaps to the full remaining amount once no more income arrives before the due date). Author-endorsed 2026-07-09/10. |
| `(page-length)` | `TransactionLogBook.cs`, factory assembly | One window-sized page per forecast run; `PageLength = null`. Fixed month-length pages matter once persisted history and expired pages exist. |
| `(runoff)` | `AccountTransactionPage.PageRunoffData()` | Implemented as a documented stub returning the closing snapshot's data; nothing consumes it until multi-page books exist. |

### Divergences without a code tag (structural, documented here)

- **No pairing mechanism at all** — `pair_actual_event` / `unpair_actual_event` / `ScanForMatchs` from the documented model have no C# analogue yet. When import lands, they belong on `AccountTransactionPage` (per `psuedo_functions.txt`'s placement).
- **No `cancelled`-setting UI** — `ExpectedTransaction.Cancelled` exists and is respected by the cascade sum, but nothing sets it yet (per-occurrence editing is future work).
- **`Expired` is always false** — nothing persists across runs, so nothing can expire.
- **Safety-cushion placeholders** — every snapshot carries the `finance_id = null` cushion jar (9.5.a1) with `ExpectedAmount = 0`, and `IdealSafetyCushion`/`SafetyPriority` sit at 0 on the page. Real cushion math is the next phase (below).
- **Per-day jar flooring** — jar values floor at 0 each day (`max(0, prev + events)`), matching the archived Python's `max(..., 0)`, rather than the flat engine's cumulative-then-floor. Differs only in the pathological "withdraw more than saved mid-history, then keep contributing" case, where per-day is the more sensible answer.

## Deferred, in dependency order

1. **Deallocation days / safety cushion / priority reallocation** (the Q2 engine — next pass). Under assumed pairing, an expected transaction that would overdraw free funds triggers the deallocation day. **Prerequisite reading:** the assumptions' distribution math was never finished — `3.13.5.2.a3`/`3.13c.5.2.a4`/`3.13c.8.4.a2` carry unresolved `?normal_fund_daily_distribution?` / `?deallocation_fund_distribution?` / `?deallocation_implicit_amount?` placeholders pointing at a "deallocation proof spreadsheet," likely one of the 15 unmined sheets in `class documentation.ods` (only `Properties` has been fully re-audited; `redesign/parse_ods_properties.py` is the reusable extractor). The archived Python's `BalanceSnapshot.deallocation_fund_distribution()` (two-step priority-ordered redistribution) is the best concrete reference; its `is_deallocation_day()` formula is demonstrably unfinished (its actual-transaction terms self-cancel) — the docs' wording ("total actual transactions exceed the free funds available") is the spec.
2. **Actual-transaction import + pairing** — retire the ASSUMED-PAIRING sites.
3. **Multiple accounts** — populate `TransactionLogPage.AccountPages` beyond `"Primary"`; per-account balances and transfers.
4. **Fixed `PageLength` + cross-page runoff** — real `page_runoff_data` consumption, expired pages, cascade continuity between pages.
