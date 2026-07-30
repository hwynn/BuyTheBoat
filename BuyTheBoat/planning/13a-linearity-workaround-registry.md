# 13a — Linearity workaround registry

**A living registry. Append to it whenever a stage invents a new trick.** Companion to
[13-adjusting-the-plan-charter.md](13-adjusting-the-plan-charter.md); this is Constraint 1's
worklist.

**The constraint being worked around:** a `FinancialPattern` / `EarMarkPattern` carries **one**
amount and **one** recurrence rule. No curve, no step, no gap, no per-occurrence override. Any
change to what a pattern does regenerates its occurrences wholesale.

Each entry answers three things: **how** it evades the constraint, **what it costs**, and **what it
still can't do**. The cost column is the point — these are all trades, and a stage that reaches for
one should know what it is paying.

**Status values:** `IN USE` (built) · `PROPOSED (stage N)` (designed or sketched, not built).

---

## W1 · Compose one user concept from several patterns  ·  IN USE

**How:** a single user action creates and links two or more patterns; the UI presents them as one
thing. `OneTimeGoalFactory` (goal + savings plan), `TransferFactory` (a withdrawal + a deposit + a
`Transfer` record that owns them).

**Costs:** the pieces must be kept consistent — transfers needed a validation sweep before each
forecast ([10 item 3-C](10-multiple-accounts.md#item-3--transfers--settled-2026-07-21)) precisely
because nothing else guarantees the two still agree. Editing and deleting must be routed through
the composite, and the pieces have to be hidden from lists that would otherwise show them raw
(`WHERE TransferId IS NULL`). Every composite adds a place where data can drift.

**Can't do:** make the pieces behave as one for anything the engine does per-pattern — priority,
mandatory, and per-account filing all still apply to each pattern separately.

## W2 · A single-occurrence rrule as "a one-time thing"  ·  IN USE

**How:** `Count = 1`, so a one-off is an ordinary pattern with one occurrence. A one-time goal, a
one-off transfer, and (per the class docs) any "isolated" expected transaction all use this.

**Costs:** essentially none — this is the documented model's own approach ("if it exists in an
ACTIVE page it has an associated financial pattern — true even for one-time payments").

**Can't do:** avoid synthesizing a `finance_id` for genuinely ad-hoc money movement; everything
needs a pattern to hang off.

## W3 · `Count` resolved to `Until` at construction  ·  IN USE

**How:** `RecurrenceRule.Create` accepts `Count` as entry sugar, resolves it to the equivalent last
date, and keeps only `Until`. Mirrors `mini_fund_project`'s `count_to_until_rrule` and structurally
enforces the chart-only "must use until, not count" rule.

**Costs:** **the user's intent is not retained.** "12 payments" becomes a date; if the start date
later moves, the count does not re-derive — the end date stays where it was. Nothing currently
warns about this.

**Can't do:** express "however long it takes" — which is item 10's whole problem.

> **Stage 2 owns this one.** A loan payoff-date helper computes an `Until` from an amount and a
> payment; W3 is why that computation is a one-time entry convenience rather than a live
> relationship, unless stage 2 decides to store the inputs too.

## W4 · Compute per day instead of storing  ·  IN USE (structural)

**How:** snapshots, jars, and events are never persisted — `TransactionLogBookFactory` rebuilds the
whole onion in memory on every forecast run. Patterns, the balances, and manual earmarks are the
only persisted truth.

**Costs:** nothing can be frozen. There is no history, no "this is what we thought last month," and
`Expired` is permanently false because nothing survives a run. Every edit is retroactive by default.

**Buys:** a great deal — it is why moving a pattern between accounts is structurally clean ([10 item
2-D](10-multiple-accounts.md#item-2--filing-patterns-under-accounts--settled-2026-07-21-a-corrected-2026-07-23)),
why recompute-fresh cannot go stale, and why Import/Export is a byte-for-byte file copy.

**Can't do:** anything needing a *record* of the past — which is why items 5 and 17 (actuals) can't
be designed in this phase.

## W5 · The implicit isolated earmark — the general escape hatch  ·  IN USE

**How:** a one-off `EarMarkEvent` on a specific day that **no pattern generates**. `repeated_earmark
= false`, so it is exempt from the linearity constraint entirely. Two existing users: deallocation
(negative — the documented case) and the automatic funding (positive — the
`DIVERGENCE(positive-implicit)` case, author-endorsed 2026-07-09/10).

**Costs:** it is **invisible as a plan.** The user sees money move with no editable schedule behind
it. The original design forbade positive implicit earmarks outright ("we will never implicitly add
to a fund jar's expected amount"); we already broke that, and each further use widens the gap
between "what the jar does" and "what the user can see and change." The merge rule (a second
isolated earmark on the same jar/day adds onto the first) means two mechanisms writing implicit
earmarks to the same jar/day silently combine.

**Can't do:** be edited by the user as a schedule — only as an individual event, and only if the UI
exposes it.

> **This is the main lever available to stages 3 and 4** — the jar hand-off across a break-off point
> and the deferral of over-funded contributions are both most naturally one-off events. Both stages
> should state explicitly what the user is shown, because "invisible as a plan" is the standing cost.

## W6 · A computed non-linear curve behind a linear pattern  ·  RETIRED (stage 1 revision, 2026-07-24)

> **RETIRED 2026-07-24 — see [planning/14 Revision](14-stage1-allocation-model.md#revision-2026-07-24--allocation-plans-replace-the-ramp).**
> The computed A/B ramp is replaced by a real, editable **Allocation Plan** (`EarMarkPattern`)
> proposed and pre-filled at creation. The hidden curve — the whole point of this workaround — no
> longer exists; a jar's fill is now a visible, previewable schedule (which is *not* a linearity
> workaround, so it earns no new entry). The A/B *heuristic* itself survives, but only as the
> one-time function that picks a plan's default at creation (pace when income comes first; front-load
> when it doesn't), not as a per-day computation. The description below is kept as the record of the
> retired mechanism.

**How:** `BillAccrualAt` fills a bill's jar along a curve that no pattern expresses — per day, it
picks between **A** (pace linearly through the current cycle, because a paycheck lands before the
due date) and **B** (reserve the full amount now, because nothing arrives first). Walking forward, a
bill ramps then snaps to full.

**Costs:** the shape lives in **engine code, not data** — changing it is a code change, and it is
neither user-editable nor previewable as a schedule. A jar built this way has a null milestone
(nothing to be "behind" on), so it is outside the Q3 on-track machinery.

**Can't do:** vary per bill, or be overridden for one bill without an `EarMarkPattern` (creating one
opts the bill out of this mechanism entirely — see `GetAutomaticallyEarmarkedBills`).

> **Item 14 is this entry.** The author's "set money aside if no paycheck comes first" trick is
> branch **B**, already built. Stage 1 decides whether this mechanism generalizes to every expected
> transaction or stays bills-only.

## W7 · User-authored one-offs layered on a pattern  ·  IN USE

**How:** manual earmarks — Add / Withdraw / Move — let the user deviate from the pattern's schedule
on any single day without touching the pattern. Fully designed in
[09-manual-earmarks.md](09-manual-earmarks.md).

**Costs:** bounded to the pattern's span (ruling 1: the earmark pattern's span *is* the jar's
lifetime), jars can't go below zero (ruling 2), and over-adds are allowed but can be clawed back by
deallocation from a *different* jar (ruling 3). A drifting plan can leave a stored withdrawal
oversized for its day; the cascade floors it and the UI flags it in place.

**Can't do:** exist for a jar with no earmark pattern (automatically funded expense jar and the cushion are
system-managed), or move a milestone — a manual catch-up closes the gap, it doesn't redefine it.

> **Items 8, 22 and 23 all press on ruling 1.** Allocating toward something that hasn't started yet
> (item 23) is precisely a request for a jar outside any pattern's span.

## W8 · Split-and-continue ("break off", user-facing: "Change starting on a date")  ·  PROPOSED (stage 3)

**How:** cut a pattern's rrule at the change date and create a second pattern that continues from
there, handing the jar balance across so allocated money isn't dropped into free funds.

**Costs (anticipated — stage 3 confirms):** **identity splits** — one real-world bill becomes two
`finance_id`s, so anything that reasons about "the electric bill" over time must re-join them. The
earmark pattern has to be split in parallel. `1.2.3.10.a3` (two pages sharing a finance_id must have
the exact same rrule) becomes relevant once multi-page books are real. Repeated splitting
accumulates patterns.

## W9 · Internal auto-renewal of an open-ended rule  ·  DESIGNED (stage 2, 2026-07-24)

> **DEFERRED & REOPENED 2026-07-28.** The author deferred the open-ended-pattern mechanism and reconsidered this approach: rolling `Until` to the horizon accumulates *hundreds of past occurrences* over years, so **periodic break-off / renewal ([W8](#w8--split-and-continue-break-off)) is now the leading alternative.** `ActiveFrom` (early-allocation span, no new occurrences) is a *different* mechanism and is **not** this one. See [planning/15 — Ongoing: deferred and reframed](15-stage2-pattern-lifetime.md). The description below is kept as the original proposal.

**How:** a pattern flagged *ongoing* keeps a real `Until` — Constraint 2 gives no choice — but that
date is an implementation detail, extended to **the forecast horizon plus at least one full cycle**
and recomputed per run. The user is never asked "how long do you want electricity for?" and never
sees the stored date. Applies to income as well as bills. Settled in
[15](15-stage2-pattern-lifetime.md).

**Costs (confirmed):** the stored `Until` is **a number the UI must never show raw**, and "when does
this end" no longer has one honest answer — an ongoing pattern shows its upcoming occurrences
instead. It needs a **new property on `FinancialPattern`**, which is a genuine divergence from the
documented ten (F19). And it propagates: a savings plan attached to an ongoing pattern must be
ongoing too, or it silently expires (F18).

**Can't do:** use a far-future sentinel date. Occurrence lists are materialized over each pattern's
**whole lifetime**, so a year-2999 `Until` would generate hundreds of thousands of dates per pattern
per run — and stage 1 puts every outflow on that path (F17). The bounded extension is not an
optimization, it is the only workable form.

---

## Guidance for using this registry

1. **Reach for the cheapest entry that works.** W2 and W4 cost almost nothing; W5 and W6 cost
   legibility, which is the currency philosophies 1 and 2 are denominated in.
2. **Two entries that both write to the same jar on the same day will merge silently.** W5's merge
   rule is a real hazard once several mechanisms are in play — stage 1's implicit-earmark registry
   is where that gets checked.
3. **Every new entry needs a `DIVERGENCE(<topic>)` tag** at its code site and a row in
   [05's registry](05-original-structure-restructure.md#divergence-registry), if it departs from the
   original design.
