# 07 — Deallocation / safety-cushion / priority-reallocation: staged implementation plan

The Q2 phase ("can I afford X?"), written 2026-07-10 to be resumable from a
clean context. **Read first:** [`06-deallocation-math.md`](06-deallocation-math.md)
(the math spec — fully resolved) and [`05-original-structure-restructure.md`](05-original-structure-restructure.md)
(the engine architecture + assumed-pairing philosophy + divergence tags). No
code for this phase exists yet.

## What this phase adds, in one sentence

Today the engine computes each fund jar independently and simply lets free
balance go negative when commitments exceed funds. This phase makes total jar
allocation **never exceed available funds**: when it would, drain the
lowest-priority jars first (cushion first), recording the pulls as implicit
negative earmarks — which is exactly the documented deallocation algorithm,
and is what finally lets the app *actively* answer "can I afford X?".

## Settled decisions (from this session — do NOT re-litigate)

1. **Engine stays** `TransactionLogBookFactory.CreateForecast` — in-memory per
   run, building the onion, stateless. Deallocation is added inside it.
2. **Assumed pairing** is the governing philosophy: with no `ActualTransaction`s,
   expected transactions stand in for actuals. Every substitution gets an
   `ASSUMED-PAIRING(...)` tag (registry in `05`).
3. **Two-step algorithm** per `06`: Step A = paired earmarks (pull each
   bought-goal's cost from its own jar); Step B = balancing earmarks, iterating
   **every** jar in priority order (lowest first), cancelling now-unaffordable
   scheduled earmarks past the point the need is met.
4. **Safety cushion** = the `finance_id = null` jar, **fixed priority 0**
   (drained first), not user-editable in priority; its *amount* is user-editable
   (incl. 0 / hidden). Floor `M = 0` (jars never negative), like every jar.
5. **Debt case**: if spending exceeds current funds, all jars drain to 0 and
   free balance goes negative by exactly `Nbn+1 = c + Σapx + au`.
6. **End-goal invariants** (Goal 1, 2.1, 2.2, 3.x in `06`) are the test oracles.
7. **Priority**: `FinancialPattern.Priority` (currently inert; 1 = lowest
   user-settable, higher = more important) drives Step B order. Cushion = 0.

## Assumed-pairing mapping (proof term → our engine)

The proof is written for actual transactions; translate as follows (tag each):

| Proof term | Our engine |
|---|---|
| `c` current funds (prev day) | the running projected balance carried into the day |
| `fx` jar balances (prev day) | previous snapshot's per-jar `ExpectedAmount` |
| `apx` paired actual txns | a goal's own occurrence (its `ExpectedTransaction`) — already modeled as the goal-release implicit earmark (`3.13c.a10` assumed-pairing form) |
| `au` unpaired actual txns | the day's expected expense transactions not tied to a goal jar (bills, discretionary) |
| `er`, `ei` existing earmarks | the day's scheduled repeated/isolated earmark events (goal contributions, bill-reserve steps) |
| deallocation-day test `c – Σfx + Σapx + au < 0` | the day's expected activity would push free balance below 0 |

## Steps (each independently reviewable)

### Step 1 — Pure deallocation function (SHOVEL-READY; do this first, in isolation)

A standalone, side-effect-free function in `MyMoneyForecast.Domain` — no
cascade integration yet. Signature roughly:

```
Deallocate(currentFunds c, jars[(financeId, priority, balance f, existingEarmark er+ei)],
           pairedTxns[(financeId, amount ap)], unpairedTxn au)
  -> earmarks[(financeId, amount)]   // paired p + balancing b, all negative-or-cancelling
```

- Implement Step A then Step B exactly per `06` (Step B over ALL jars, priority
  ascending, cushion at priority 0 first).
- Return the debt remainder too (`Nbn+1`), so the caller knows how negative the
  balance goes.
- **Reuse the general helper** `W = Max((M−A), N)` with `M = 0`.
- **Tests = the whole point of doing this first:** encode `06`'s two worked
  examples, then the three end-goal invariants as assertions, then mine the
  `DeallTest` / `Proof` / `Random` sheets of `DeallocationProof.ods` (via
  `redesign/parse_ods_properties.py`, extended to dump formulas/values) for
  numeric vectors. This function must match the proof before any integration.
- Deliverable: a green, proof-faithful deallocation function with zero
  dependence on the rest of the engine.

### Step 2 — Integrate into the cascade

**Integration model (author, 2026-07-10 — this is the organizing principle):**
deallocation is *not* a rewrite of jar computation. On a deallocation day it
simply **adds implicit (negative) earmark events to that day to balance
things out; then the existing cascade calculation runs as normal** and brings
the jars down, because the cascade already computes each jar as
`max(0, previous + that day's earmark events)`. So Step 2 = "on a deallocation
day, produce the deallocation earmarks (via the Step 1 function) and append
them to the day's `EarMarkEvent`s, before the cascade's value pass." Everything
downstream (Reserved column, detail pane, export) then works for free, since
these are ordinary earmark events.

This also means deallocation runs **only on deallocation days** (days whose
expected activity would push free below 0), not every day — and jar "recovery"
on later days needs no special handling: scheduled earmarks simply keep filling
per the normal cascade, re-deallocating if a later day is again short.

**Ordering within a day** (the deallocation function runs LAST, taking the
others as fixed `er`/`ei`/`p` inputs): scheduled repeated/isolated
contributions → goal-release (`3.13c.a10`) → bill auto-reservation (ramp/snap)
→ **then** deallocation balancing (Step B).

**Design decisions for this step (resolved with author 2026-07-10 unless noted):**

1. **[RESOLVED] Step A and the "goal-release" earmark are the SAME earmark, not
   two.** A *paired earmark* (money leaves a jar because the goal it saved for
   was bought) has a normal-day path — assumption `3.13c.a10`, which the current
   code already emits — and a deallocation-day path — Step A (`p`). No earmark
   type was missed: the proof's four categories (`er` repeated, `ei` isolated,
   `p` paired, `b` balancing — where `p`/`b` are system-made *isolated*
   earmarks, `repeated_earmark = false`) cover everything. **Integration rule:
   the two paths are mutually exclusive per day** — on a deallocation day Step A
   makes the paired earmark and the normal-day `3.13c.a10` path must NOT also
   fire, or the jar releases twice. (The current unconditional goal-release
   becomes the "not a deallocation day" branch.)
2. **[RESOLVED] All jars are drainable in Step B** — cushion (priority 0) first,
   then ascending priority. Bill-reservation jars included: mandatory, but "we
   can't allocate money we don't have."
3. **[RESOLVED] Future (assumed-paired) expected transactions trigger
   deallocation, including unpaired ones.** In the original design actual
   transactions did this (even a surprise Xbox could drain funds saved for other
   things); without actuals, expected future transactions play that role. Test
   we must satisfy: next month's rent visibly drawing funds out of the vacation
   jar. **UI requirement (see Step 3): on a deallocation day, flag jars whose
   amounts were drained — red for now, distinct per-jar colors eventually.**
4. **[DEFERRED]** meaning of `HasNegativeFreeBalance` after deallocation —
   revisit later; author wants more explanation when the time comes.
5. **[RESOLVED] `current_free_amount` = free amount on "today"; `expected_free_
   amount` = the same thing on a future day.** Our per-day `ExpectedFreeAmount`
   is exactly this (today or future), so it's the right stand-in — no special
   mapping needed.
6. **[RESOLVED, belongs to Step 3] fill the cushion by reusing the bill
   mechanism**, so it needs minimal custom rules; deallocation only drains it.
   Design note: a bill reserves toward a *dated* amount and resets after the due
   date, whereas the cushion is a *standing* target (`ideal_safety_cushion`)
   with no due date — so "reserve up to the target and hold it" is a small
   adaptation of the bill machinery, not a parallel system.

### Step 3 — Cushion input + Q2 surfacing (UI)

- Persist + edit the cushion **amount** (`ideal_safety_cushion`): a field on
  the Forecast tab or a setting; default 0; allow hiding. `SafetyPriority`
  stays fixed at 0 (not shown/edited). Today these are $0 placeholders on
  `AccountTransactionPage` and the `finance_id = null` jar (see `05`). Fill it
  by reusing the bill-reservation mechanism (Step 2 decision #6).
- **Drained-jar highlight (author requirement):** on a deallocation day, any
  fund jar whose amount was pulled down is shown in a distinct color — **red
  for now**, with per-jar distinct colors planned later. Applies in the detail
  pane (and ideally a marker on the timeline row for the deallocation day).
- The cushion jar and any deallocation earmarks already render in the detail
  pane (they're jars/events) — verify labels read well ("Safety cushion",
  deallocation as an "Automatic release"/"Deallocation" kind).
- Optional but the payoff: a "what if I buy X on date D?" hypothetical input
  that injects a one-off expected expense and reports whether it fits in free
  funds and, if not, what gets deallocated / which goals fall short.

## Test strategy

- Step 1: proof-faithful unit tests (examples + invariants + mined vectors) —
  the strongest evidence, build here.
- Step 2: cascade tests asserting `Σ jars ≤ balance` every day; priority order
  (cushion first, then ascending); the debt case; and that goal-release +
  bill-reservation still behave. Keep every existing oracle green.
- Step 3: the established launch + UI-automation drive (Win32 `EnumWindows`;
  message-box buttons are `ControlType.Pane`; assert post-conditions) — see `05`.

## Suggested resumption prompt

"Implement Step 1 of planning/07 — the pure deallocation function — with tests
against 06's examples, the end-goal invariants, and vectors mined from
DeallocationProof.ods. No cascade integration yet." Then re-enter plan mode for
Step 2.
