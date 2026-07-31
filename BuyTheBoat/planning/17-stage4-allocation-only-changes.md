# 17 — Stage 4: Changing an allocation without changing the bill

**Status: OPEN — started 2026-07-29.** Items **8, 9, 22, 23, 24** + planning/10's parked
**cross-account funding interactions**. Stage 4 of the
["Adjusting the Plan" phase](13-adjusting-the-plan-charter.md). **F27 SETTLED 2026-07-30** —
author's ruling: item 8 is a lasting rate change (not a one-time catch-up), and `3.11.1.a1` is
relaxed to allow multiple `EarMarkPattern`s per `finance_id`. **Item 8 ("Restructure the plan") is
BUILT** — `RestructureFactory` + the schema migration + two engine aggregation fixes it exposed
(F34/F35). **Item 22 is BUILT** — `RestructureFactory.StopContributing`, a thin wrapper over item 8's
mechanism (F31), needed one small generalization to `Restructure`'s own validation (F36). **Item 9 is
BUILT** — the creation path already worked (no UI gate, confirmed by reading the code) once item 8's
schema fix landed; the one missing piece, the same-day event merge (F30), is now implemented and
tested, with one narrow, accepted, unfixed edge case noted (F37 — an identical-start-date collision).
**Item 24's detection is BUILT** — `GoalShortfall.OverfundedAmount`, the mirror of `ShortfallAmount`,
no new engine computation; correct for a one-time goal, gross-only/incomplete for a repeating one, by
design (F32). Its own system-triggered entry point and the nudge UI (Stage 6) are still open. **Item
23 SETTLED** — create the real goal now with a best-guess date, no new mechanism; correcting the date
later is a plain edit (F24), not a "Restructure." **Cross-account funding: F33 found and FIXED** —
`GetByAccountExcludingTransferPatterns` scopes the income scan to the outflow's own account. **Suite:
245 green (195 domain + 50 scenario), 0 warnings.** What's left: item 24's own entry-point/nudge, and
UI wiring for everything built (8, 9, 22, 24's detection).

**The stage in one paragraph:** the earmark-side counterpart to Stage 3 — the bill or goal itself
(`FinancialPattern`) stays exactly as it is; what changes is *how it gets funded*. Charter framing:
"the closest thing to a direct answer to Q4." Items 22/23/24 are one family (a jar's fill doesn't
match the naive linear plan); item 9 is flagged by the charter as "the one assumption break big
enough to deserve its own decision record." Reading the sources first (per the standing rule)
surfaced that **8, 9, and 22 are that same decision record**, not three separate ones — see F27.

### Reading list
1. [13 — charter](13-adjusting-the-plan-charter.md), the Stage 4 section and the pressure-map row on
   `3.11.1.a1` ("**9** breaks it head-on").
2. [13a — workaround registry](13a-linearity-workaround-registry.md), **W5** (the implicit isolated
   earmark) and **W7** (manual earmarks) — the two existing levers this stage tests itself against
   before reaching for a new one.
3. [09 — manual earmarks](09-manual-earmarks.md) — ruling 1 (a jar's lifetime = its earmark
   pattern's span), and its own opening line naming this stage's territory as unfinished business:
   *"the second [prerequisite for Q4], restructuring an earmark pattern's rrule/amount, is its own
   future design."*
4. [14 — stage 1](14-stage1-allocation-model.md) — `EarMarkPattern.StartingAllocation` and
   `AllocationPlanProposer.ProposeEmpty` (the "empty, jar-alive, no contributions" shape, built for
   the declined-plan case in Stage 2 — see F27's item-22 preview below).
5. [16 — stage 3](16-stage3-break-off.md), items 4-A (identity across a cut) and 4-B (the jar
   hand-off via `StartingAllocation`) — the direct precedent this stage's mechanism either reuses or
   explains why it can't.
6. [10 — multiple accounts § Parked](10-multiple-accounts.md#parked-for-the-cascade-tweaking-phase)
   — the cross-account funding interactions item.

---

## What already exists that this stage builds on

- **`AllocationPlanProposer.ProposeEmpty`** (Stage 2/[15](15-stage2-pattern-lifetime.md)) — an
  `EarMarkPattern` with `Amount = 0`, a `Count = 1` rrule at the outflow's `Until`, and `ActiveFrom`
  reaching back so the jar is alive with no contributions. Built for a declined plan, but
  structurally identical to what item 22 ("stop contributing, keep the jar alive") needs.
- **`StartingAllocation` + the jar hand-off** ([16](16-stage3-break-off.md), item 4-B) — carrying a
  jar's balance at a cut date into a successor pattern with no new field. The mechanism item 8/22
  would reuse if the multiplicity question (F27) resolves toward sequential succession.
- **`PatternTruncation.EndOn`** ([16](16-stage3-break-off.md), item 16) — cleanly ending a pattern's
  `Until`; reusable for the "predecessor" half of any earmark-only succession.
- **`3.13.5.4.a1`** (the milestone formula) is already written as *"sum of **all** expected values of
  repeated earmarks up to and including this date"* — plural, over events, not over one pattern. It
  needs no change if a finance_id ever has more than one earmark pattern generating events into the
  same jar.

---

## Finding F27 — items 8, 9, and 22 are one relaxation, not three

*(continuing the phase's numbering from [16](16-stage3-break-off.md), which reached F26)*

**The question underneath all three, stated in the model's own terms:** can more than one
`EarMarkPattern` exist for the same `finance_id`?

**Why Stage 3's precedent doesn't transfer.** Break-off ([16](16-stage3-break-off.md), item 4-A)
gives a changed `FinancialPattern`'s successor a **brand-new** `finance_id` — legitimate, because the
class doc's own matching rule says two patterns are "the same thing" only if source/rrule/amount all
agree, and a changed amount means they don't. But `EarMarkPattern.finance_id` is not its own identity
the same way — per [01-glossary](../../01-glossary-of-terms.md#earmarkpattern), it **is** "the
`finance_id` of the `ExpectedTransaction` being saved for. One earmark pattern per goal, one goal per
earmark pattern — this is the only link between them." Items 8 and 22 explicitly keep the goal/bill
**untouched** ("without touching its finance pattern"), so a second earmark pattern for it cannot take
a new `finance_id` without also inventing a new goal — which is precisely what these items rule out.
So even a purely **sequential** change (8: a savings rate steps up partway through; 22: contributions
stop early but the jar survives to the due date) needs two `EarMarkPattern` rows sharing one
`finance_id`, non-overlapping in time. Item 9's **concurrent** case (two funders, same goal, same
time) needs exactly the same relaxation, just with overlapping spans instead of sequential ones.

**The assumption at the center:** `3.11.1.a1` — *"all earmark patterns [in] self.earmark_patterns
should have unique finance_ids"* — is what forbids all three today, uniformly. Nothing else in the
containment chain (`3.13.5.a1`/`3.13.5.a2`, the jar's own `9.5.1.a1` uniqueness) needs to change: the
**jar** stays one-per-finance_id either way (a goal still has exactly one bucket of money); only the
**pattern-to-jar** cardinality moves from 1:1 to many:1.

**A real consequence, not just a relaxation.** `3.13.8.1.a2` — *"only one repeated earmark with
finance_id x can exist on a single day"* — would be reachable for the first time if two concurrent
patterns' occurrences ever land on the same day (item 9's household-partner case, if both paydays
coincide). Isolated earmarks already have a merge rule for same-jar/same-day collisions
([W5](13a-linearity-workaround-registry.md#w5--the-implicit-isolated-earmark--the-general-escape-hatch));
repeated earmarks currently have none, because there was never more than one source. Whichever
direction F27 resolves, this needs an explicit answer, not a silent gap.

**What this does *not* decide.** Items 23/24 (allocating toward something not yet scheduled at all,
and deferring an over-funded jar) are a different question — there is no existing goal/`finance_id`
to attach a second pattern to at all. Parked below, after F27 lands.

### The ruling · SETTLED 2026-07-30

| Sub-question | Ruling |
|---|---|
| **1 — what item 8 is** | **A lasting rate change.** The ongoing per-payday amount changes from a point forward and stays changed. (A one-time catch-up remains separately available via manual earmarks, unaffected by anything in this stage.) |
| **2 — the mechanism** | **Relax `3.11.1.a1`.** Multiple `EarMarkPattern`s may share a `finance_id`; the one shared jar aggregates every pattern's generated events, exactly as `3.13.5.4.a1`'s "sum of all" wording already anticipates. |

---

## Item 8 — "Restructure the plan" · **BUILT 2026-07-30** (domain + persistence + engine; UI not wired in)

### 8-A · Identity across the cut — reasoned directly from Stage 3's precedent

Unlike break-off ([16](16-stage3-break-off.md), 4-A), where the successor gets a **new**
`finance_id`, item 8's two `EarMarkPattern` rows share the **same** `finance_id` — that is the whole
point of F27's ruling. **The old pattern is truncated (`PatternTruncation.EndOn`) and kept, not
deleted** — identical reasoning to 4-A: a bounded, truncated pattern is just another instance of the
case the pattern list already handles (nothing filters on `Until`), and keeping it preserves an
honest record of what the rate used to be. No new decision needed; this is 4-A's ruling applied
without modification.

### 8-B · The jar hand-off — **F28: no hand-off is needed at all**

Break-off needs `StartingAllocation` to carry a jar's balance into a successor because a *new*
`finance_id` means a *new* jar starting from zero. Item 8 keeps the **same** `finance_id`, so there
is only ever **one** jar — it never restarts. `3.13.5.3.a1` computes a jar's `expected_amount` from
*yesterday's* value plus *today's* earmark events **for that finance_id**, with no reference to which
pattern generated either day's events. The balance carries across the cut date automatically, through
the ordinary cascade. **No new mechanism at all** — item 8 is genuinely simpler than break-off in
this one respect, and it's a direct payoff of relaxing `3.11.1.a1` rather than inventing a
new-finance_id workaround.

### 8-C · What the new pattern's amount/schedule are — reasoned, flagging for correction if wrong

Break-off's successor is freshly *proposed* (`AllocationPlanProposer`) because the underlying bill or
income changed — a new fact for the proposer to react to. Item 8 has no such new fact: the goal is
unchanged, and the user is changing **their own preference** for funding it. So the new pattern's
amount/schedule read as **user-specified directly**, the same as editing any other pattern — there's
nothing for a proposer to compute a default from. This shapes the UI (a plain schedule editor, not a
propose-and-confirm step), so flagging it as a reasoned reading rather than a settled ruling — correct
me if the intent was for the system to suggest something here instead.

### 8-D · Naming — SETTLED 2026-07-30

**Internal/developer term: "Restructure" (`RestructureFactory`, mirroring `BreakOffFactory`'s
naming).** Matches [09](09-manual-earmarks.md)'s own existing informal phrase, "restructuring an
earmark pattern's rrule/amount."

**User-facing name: "Change my savings plan starting on a date."** Deliberately parallel to
break-off's settled phrasing, *"Change starting on a date"* ([16](16-stage3-break-off.md), 4-F) — the
two actions read as one family to the user (same words, one level down: the plan, not the whole
bill), which is exactly right since item 8 *is* break-off's earmark-side counterpart.

### What got built

**`RestructureFactory.Restructure`** (`src/MyMoneyForecast.Domain/RestructureFactory.cs`) — matches
8-A through 8-D exactly: truncates the predecessor plan to the day before the cut (kept, not
deleted), builds the successor at the user-specified amount/schedule sharing the **same**
`FinanceId`, seeds no `StartingAllocation` (F28). Validates the cut date is after the predecessor's
own start and the successor's schedule starts exactly on the cut date; extending past the goal's own
`Until` is rejected for free by `EarMarkPattern.Create`'s existing check. 7 tests.

**F34 — the two engine assumptions F29 predicted were confirmed and fixed.** Building the factory is
only half the job — F27's relaxation only works if the engine actually aggregates multiple plans per
`finance_id` instead of picking one. Checked directly against `TransactionLogBookFactory.cs` before
writing `RestructureFactory`, and found exactly what F29 predicted, in two places:
- The initial-snapshot seed (`jarValues[financeId] = ...` / `milestones[financeId] = ...`) **assigned**
  per earmark pattern rather than summing — a second pattern sharing a `finance_id` would have
  silently overwritten the first's contribution history rather than adding to it.
- `CalculateGoalShortfalls` produced **one `GoalShortfall` row per plan**, not per goal — a
  restructured goal would have shown up twice in the shortfall list, each row reflecting only one
  segment's contribution.

Both are now grouped by `finance_id` (`ToLookup`) and summed across every plan sharing it. The
*forward* cascade (day-by-day, from the as-of date onward) needed no change — it was already
built around flat per-day event lists that naturally aggregate regardless of which pattern generated
an event, which is what made this a narrower fix than it could have been. Two regression tests added
directly to `TransactionLogBookFactoryTests.cs` (one jar, one shortfall), each proving the exact
failure the bug would have produced (an overwritten jar value; a duplicate shortfall row) before the
fix.

**Schema migration:** `EarMarkPatterns.FinanceId` was a genuine `PRIMARY KEY` — saving a second plan
for the same goal would have silently overwritten the first at the database level, upsert and all.
Rebuilt onto a composite `PRIMARY KEY (FinanceId, StartDate)` (SQLite can't alter a primary key in
place, so this is a real rename/recreate/copy/drop migration, run once and guarded by
`PRAGMA table_info`). `EarMarkPatternRepository.Save`'s `ON CONFLICT` clause moved to match — editing
a plan at its existing start date still updates in place; a genuinely new segment at a different
start date now inserts instead of overwriting.

**F35 — a second, unplanned schema fix, and a correction to a standing assumption.** Migrating
`EarMarkPatterns`' key broke `ManualEarmarks.FinanceId REFERENCES EarMarkPatterns(FinanceId)` — SQLite
rejects a declared FK reference whose target column is no longer unique ("foreign key mismatch"),
independent of enforcement. Fixed by repointing the reference at `FinancialPatterns(FinanceId)`
instead, migrated the same way for existing databases. This is also the *more correct* reference: a
manual earmark is about the goal (the jar), not any one plan segment, so it never should have pointed
at `EarMarkPatterns` to begin with. **Surfaced along the way:** this project's own comments elsewhere
state "SQLite's FK enforcement is off by default" — building the migration test proved that's not
true of this setup (Microsoft.Data.Sqlite defaults `PRAGMA foreign_keys` to `ON`); it never mattered
before because nothing had violated insertion ordering until this test tried to. Worth knowing before
relying on that comment elsewhere.

**Tests: 12 new (238 total, was 226 — 188 domain + 50 scenario), 0 warnings.** 7
`RestructureFactoryTests`; 2 `TransactionLogBookFactoryTests` (the F34 regressions); 3
`PatternRepositoryTests` (two plans sharing a `finance_id` round-trip; saving at the same start date
still updates in place, not duplicates; an old single-key database migrates without losing data).

**Not done — deferred to the UI phase, same as every other stage's mechanism so far:** no entry point
in `MainWindow` calls `RestructureFactory` yet. The domain/persistence/engine layers are built and
tested; wiring a "Change my savings plan starting on a date" action into the app is unbuilt.

---

## Item 9 — **BUILT 2026-07-30** (engine; UI entry point already exists, unfiltered)

Per F27's ruling, item 9 (two funders on one goal, concurrently) needs **no new mechanism** beyond
the relaxation itself: each `EarMarkPattern`'s own `Amount`/`DatePattern` already vary independently
(nothing new needed there), and `EarMarkPattern` carries no `Priority` of its own to reconcile —
priority lives on the goal's `FinancialPattern`
([01-glossary](../../01-glossary-of-terms.md#financialpattern)), shared automatically by every
pattern funding it. Two things remain, both consequences rather than new design:

- **F29 — every "the earmark pattern for this goal" lookup becomes "all earmark patterns for this
  goal."** Concretely: `AllocationPlanProposer`, `EarMarkPatternRepository`, and the
  Savings-Goals/Allocations tab's one-row-per-goal display all currently assume a 0-or-1 relationship
  and need to become 0-or-many. Mechanical, but real — worth listing so it isn't missed at build
  time. (Item 8 hits the same lookups, so this is shared infrastructure, not item-9-specific.)
- **F30 — the same-day collision, resolved by existing precedent.** `3.13.8.1.a2` ("only one
  repeated earmark with finance_id x can exist on a single day") becomes reachable for the first time
  once two concurrent patterns can both land on a day. **Resolution: merge them into one event,
  summing the amounts** — identical to the treatment
  [01-glossary](../../01-glossary-of-terms.md#earmarkevent) already documents for two *isolated*
  earmarks on the same finance_id/day ("a second isolated earmark... just adds onto the first's
  amount rather than creating a new one"). Extending that same merge rule to repeated earmarks from
  different patterns is a direct continuation of existing precedent, not a new principle.
- **Checked against code, resolved: no gate exists.** `OnAddEarMarkPatternClick`
  (`MainWindow.xaml.cs`) passes `_financialPatterns.GetAll()` — every goal, unfiltered — to
  `CreateEarMarkPatternWindow`'s create-mode constructor. The only place that blocks re-targeting an
  already-earmarked goal is the *separate* "Set Up Savings Plan" (materialize) button, a different
  entry point for a different purpose (turning an auto-filled jar into a real plan, planning/14 item
  D-1). So item 9's creation path needed no UI change at all — combined with item 8's schema fix
  (composite key), a user could already save a second concurrent plan for an earmarked goal through
  the existing window before this round of work; only the engine's *handling* of that case (F30,
  below) was missing.

### What got built

**F30, implemented: `MergeOrAppendRepeatedEarmark`** (`TransactionLogBookFactory.cs`), the repeated
counterpart to the existing `MergeOrAppendIsolatedEarmark`. Two `EarMarkPattern`s sharing a
`finance_id` whose occurrences land on the same day now merge into **one** event (summing the
amounts) instead of coexisting as two — `3.13.8.1.a2` ("only one repeated earmark with finance_id x
can exist on a single day") now holds literally. One engine test proves it directly: two concurrent
"partners" funding the same goal at different amounts, whose schedules land on the same calendar day
from a point forward — before that point, one event at one partner's amount; from that point, one
merged event at the sum, never two.

**F37 — a narrow, accepted persistence limitation, not fixed.** `EarMarkPatterns`' composite key
(`FinanceId`, `StartDate`, from item 8's schema migration) assumes two plans for the same goal never
share an *identical* start date. Two concurrent funders created at genuinely different moments will
almost always have different start dates in practice (each set up whenever that person actually
began contributing) — but if two plans were ever saved with the exact same start date, the second
would silently overwrite the first (the same upsert behavior that's correct for editing one plan in
place). A true surrogate key (mirroring `Accounts.Id`/`Transfers.Id`) would close this for good, but
is a second invasive schema change with no evidence it's needed yet — noted here rather than built,
consistent with not adding machinery beyond what's demonstrated. Worth revisiting if a real
same-day-start collision ever surfaces.

**Tests: 1 new (244 total, was 243 — 194 domain + 50 scenario), 0 warnings.**

## Findings register

*(continuing the phase's numbering from [16](16-stage3-break-off.md), which reached F26)*

| # | Finding | Bears on |
|---|---|---|
| F27 | Items 8, 9, and 22 all reduce to one question — can more than one `EarMarkPattern` share a `finance_id` — because `EarMarkPattern.finance_id` is the goal's own id, not an independent identity | items 8, 9, 22 — **SETTLED**: relax `3.11.1.a1` |
| F28 | Item 8 needs no jar hand-off/`StartingAllocation` — unlike break-off, the `finance_id` doesn't change, so the jar never restarts; the ordinary day-to-day cascade carries its balance across the cut for free | item 8-B |
| F29 | Every current "the earmark pattern for this goal" lookup (proposer, repository, Savings-Goals tab) assumes 0-or-1 and must become 0-or-many | items 8, 9 |
| F30 | Two concurrent patterns' occurrences can now collide on the same finance_id/day (`3.13.8.1.a2`) — resolved by extending the existing isolated-earmark merge rule to repeated earmarks | item 9 |
| F31 | Item 22 ("stop contributing, keep the jar alive") is item 8's mechanism with a zero-rate successor (`ProposeEmpty`), not a new mechanism | item 22 — **SETTLED** |
| F32 | Item 24's "over-funded" signal falls out of F21's shortfall formula for free (the un-floored negative value) for a one-time goal; a repeating goal's pace-relative version is an accepted, unresolved gap, mirroring F21's own gross-vs-gross limitation | item 24 — mechanism **SETTLED**, detection partial |
| F33 | `AutoCreateAllocationPlan`'s income scan was household-wide, not account-scoped — a bill could silently pace against a paycheck filed under a different account. A real, narrow (single-income-pattern-only) correctness gap, not just an assumption. Transfer deposits are correctly excluded already (by design) | cross-account — **FIXED 2026-07-30**: `GetByAccountExcludingTransferPatterns`, 2 new tests, 226 green (was 224), 0 warnings |
| F34 | F29's predicted consequence, confirmed and fixed: the engine's initial-jar seed and `CalculateGoalShortfalls` both assumed 0-or-1 `EarMarkPattern` per finance_id (assignment instead of summing across every plan sharing an id) | item 8 — **FIXED 2026-07-30**, 2 regression tests |
| F35 | Migrating `EarMarkPatterns`' key broke `ManualEarmarks`' declared FK reference to it ("foreign key mismatch") — repointed at `FinancialPatterns`, which is also the more correct reference. Surfaced that FK enforcement is actually ON by default here, contradicting this project's own "off by default" assumption elsewhere | item 8 — **FIXED 2026-07-30**, migration + test |
| F36 | `Restructure`'s validation had to check the successor's *active-span* start (`ActiveFrom ?? Start`), not its literal `Start` — needed so `StopContributing`'s successor (one occurrence at the goal's due date, active span from the cut date) fits through the same validation as item 8's ordinary case (where the two are identical) | item 22 — **FIXED 2026-07-30** |
| F37 | The `(FinanceId, StartDate)` composite key (item 8's schema fix) assumes two plans for one goal never share an identical start date — a genuinely concurrent same-day collision would silently overwrite. Narrow, accepted, not fixed — a true surrogate key would close it if ever needed | item 9 — **noted, not built** |

---

## Item 22 — "A goal met early" · **BUILT 2026-07-30** (domain; UI not wired in)

**F31 — item 22 is item 8's mechanism with a zero-rate target, not a separate mechanism.** "Stop
contributing, keep the money allocated" is **"Restructure the plan"** (item 8's `RestructureFactory`)
called with the successor being `AllocationPlanProposer.ProposeEmpty` (the Amount=0/`Count=1`-at-`Until`
shape already built for Stage 2's declined-plan case) instead of a user-specified rate. Everything
settled for item 8 carries over unmodified: **8-A** (old pattern truncated, kept, not deleted),
**8-B**/**F28** (no jar hand-off — same `finance_id`, the cascade carries the balance across the cut
for free), and **8-D**'s naming ("Restructure the plan," this time targeting zero).

**The loan case, checked directly against the model.** For a repeating goal (the charter's own
example), stopping contributions only pauses the *inflow* — the jar keeps releasing/rolling forward
to cover each occurrence exactly as today, since that behavior is driven entirely by the goal's own
`FinancialPattern` occurrences (`3.13.5.4.a1`'s milestone, the repeating-bill jar's roll-forward),
none of which item 22 touches. Resuming later, once the balance depletes, is simply "Restructure the
plan" run again with a non-zero rate — the same action as item 8, not a new one.

**"Notify?" (the charter's own question) belongs to Stage 6, not this stage** — consistent with
established precedent, not a fresh call: Stage 3 deferred the identical shape of question twice
(item 4-E's "optional prompt," item 16's "should the user be told $340 comes back" legibility
question), both explicitly to Stage 6 on the grounds that making the mechanism *correct* is the
current stage's job, and *announcing* its effect is a warnings/levers question. Item 22's "should the
system tell the user their goal is ahead of schedule" is the same shape of question.

### What got built

**`RestructureFactory.StopContributing(predecessor, goal, cutDate)`** — a thin wrapper over
`Restructure`, the same relationship `BreakOffFactory.Renew` has to `BreakOff`: it constructs the
zero-rate successor automatically (`Amount = 0`, one occurrence at the goal's own due date, `Until`)
and calls `Restructure` with it. Confirmed it produces the identical shape to
`AllocationPlanProposer.ProposeEmpty` (Amount=0, Count=1 at `Until`, `ActiveFrom` reaching back), and
confirmed directly (not just reasoned) that it works the same for a repeating goal — a loan — as for
a one-time one: the successor's *active span* reaches from the cut date to the goal's own `Until`, so
the jar stays alive across every remaining occurrence in between, and each of those occurrences still
releases money exactly as today; only the inflow stops. 4 domain tests plus one full forecast-level
test proving the actual behavior (a jar that only ever drains, on schedule, with no new inflow ever
raising it).

**F36 — `Restructure`'s own validation needed a small generalization to host this.** The base method
required the successor's schedule to literally `Start` on the cut date. `StopContributing`'s successor
needs its one *occurrence* at the goal's due date instead (mirroring `ProposeEmpty`), while its
*active span* — where the jar stays computable — still has to begin exactly on the cut date. Fixed by
checking `ActiveFrom ?? Start` (the active-span start) against the cut date, rather than the literal
`Start`. For item 8's ordinary case (no `ActiveFrom` on the successor) the two are identical, so this
changes nothing there — confirmed by the existing 7 `RestructureFactoryTests` staying green unmodified.

**Tests: 5 new (243 total, was 238 — 193 domain + 50 scenario), 0 warnings.**

**Not done:** no UI entry point for "stop contributing" yet, same deferral as item 8.

## Item 24 — "Deferring allocations" · **Detection BUILT 2026-07-30 (one-time goals); mechanism is item 22's; nudge is Stage 6's**

**F32 — item 24 is item 22's mechanism (F31), triggered by the system instead of the user**, plus a
concrete definition of "over-funded." Defining that meaning is this stage's job, not Stage 6's —
the exact precedent [Item C](14-stage1-allocation-model.md#item-c--what-thin-means--settled-2026-07-23)
set for "thin" ("deciding what this means is a cascade/allocation question, not a rendering one").

**For a one-time goal, "over-funded" already falls out of F21 for free.**
[F21's shortfall formula](14-stage1-allocation-model.md#f21--one-shortfall-formula-for-goals-and-bills)
— `ShortfallAmount = Max(0, AmountNeeded − AmountAllocatedByDueDate)` — floors a negative result to
zero. The **unfloored** value *is* the over-funded signal: a goal is over-funded exactly when
`AmountAllocatedByDueDate > AmountNeeded`, by that surplus. No new computation — only reporting a
value the engine already computes internally and currently discards at the floor.

**For a repeating goal (a loan), this is incomplete — flagged, not resolved.** F21's formula compares
gross totals over the *whole* remaining span, the same accepted limitation F21 already documents
("gross-vs-gross... cannot see timing"). "Over-funded ahead of *pace*" (comfortably ahead of the
*next* occurrence, without necessarily covering the entire remaining balance) is a finer-grained
question the existing formula can't answer. Mirroring F21's own resolution rather than opening a new
one: **ship the gross-level signal, note the limitation, don't block on it.**

**The offer/nudge itself is Stage 6's**, same reasoning as item 22.

### What got built

**`GoalShortfall.OverfundedAmount`** (`src/MyMoneyForecast.Domain/GoalShortfall.cs`) — the mirror
image of the existing `ShortfallAmount`, reading the exact same two fields
(`AmountAllocatedByDueDate`, `AmountNeeded`) that were already there. No new engine computation —
`CalculateGoalShortfalls` is untouched. Carries the identical gross-vs-gross scope as
`ShortfallAmount`: correct for a one-time goal, and for a repeating one it reads the whole remaining
span rather than pacing against the next occurrence — the same accepted limitation, not a new one.

**Tests: 1 new, 2 existing extended (245 total, was 244 — 195 domain + 50 scenario), 0 warnings.** A
new test for the genuinely new case (a one-time goal over-funded via a lump sum, `OverfundedAmount`
reads the surplus with `ShortfallAmount` at zero); the existing "on track" and "short" tests each
gained one assertion confirming `OverfundedAmount` is zero in both of those states — the two signals
are mutually exclusive.

**Not done:** the DETECTION exists; the offer/nudge UI (Stage 6) and item 24's own system-triggered
entry point (as opposed to item 22's user-triggered one) are both still open — this only exposes the
number, it doesn't act on it.

## Item 23 — "Allocating toward something that doesn't exist yet" · SETTLED 2026-07-30

**Author's ruling: create the real goal now, with a best-guess date — no new mechanism.** The
healthcare-plan example has a known *amount* (the bonus) and an unknown *date*; the resolution is to
create an ordinary goal (`FinancialPattern`) today with a tentative `Start`, set `ActiveFrom` to the
creation as-of date so contributions can begin immediately even though the occurrence itself is
uncertain, and correct the date later once the real start is known.

**Correcting the guessed date later is a plain edit, not a "Restructure."** This falls directly out
of [16](16-stage3-break-off.md)'s own **F24** distinction: the real branch is "does the user want the
past preserved or corrected," and fixing a wrong estimate is a **correction**, not "this changes from
a date forward." So no new taxonomy entry, and no interaction with item 8's mechanism — editing
`Start`/`Until` is today's ordinary edit form, uniform-everywhere, exactly as F24 already settled for
a different case.

**Nothing else recomputes specially.** `AmountNeeded` (F21) scales off the pattern's *current*
occurrence count, so correcting the date automatically corrects any shortfall/over-funded read on the
next forecast — no caching, no special-case drift handling, consistent with the whole engine rebuilding
from scratch every run (**W4**). Any reminder to "firm up this date" once it's closer is Stage 6's
territory, same as every other notify/nudge question this stage has run into.

**Rejected directions, for the record:** "move it to another account" (the charter's own floated
honest-limitation answer) and a new placeholder-goal type (a fresh divergence) were both presented
and declined in favor of reusing this phase's own already-built tools.

## Cross-account funding interactions · F33 CORRECTED 2026-07-30 after checking code — real gap found

*(Read [10 §Foundation/Items 1–3/Parked](10-multiple-accounts.md) in full for the reasoning below;
the 2026-07-30 draft of F33 below was wrong on both open points and has been replaced.)*

**F33, corrected — the income scan is household-wide, not account-scoped, and that's a live
correctness gap; a Transfer deposit is (correctly) never treated as income.** Checked directly against
`AllocationPlanProposer.cs` and `MainWindow.xaml.cs`, 2026-07-30:

- **`AutoCreateAllocationPlan`** (`MainWindow.xaml.cs:1092-1093`) calls
  `AllocationPlanProposer.Propose(pattern, _financialPatterns.GetAllExcludingTransferPatterns(), asOfDate)`.
- **`GetAllExcludingTransferPatterns()`** (`FinancialPatternRepository.cs:109-113`) is
  `SELECT * FROM FinancialPatterns WHERE TransferId IS NULL` — **every** non-transfer pattern in the
  household, with **no account filter**.
- **`AllocationPlanProposer.Propose`** itself (line 75) picks its "income" candidates with
  `allPatterns.Where(pattern => pattern.Amount > 0m)` — a bare sign check, nothing about which account
  a pattern is filed under.

**The consequence, concretely:** whenever a household has *exactly one* income pattern total
(`incomePatterns.Count == 1`, the gate into Shape A/paced), every new outflow gets its default plan
paced against **that** income — even when the outflow is filed under a *different* account than the
income. A bill in Checking can silently get its Allocation Plan paced against a paycheck filed under
Savings. That's not a theoretical gap; it directly contradicts
[item 1](10-multiple-accounts.md#item-1--the-account-entity--settled-2026-07-21)'s founding premise —
"having enough money means having enough in the *right* account" — for exactly the reason the parked
note worried about, just arriving from the ordinary (non-transfer, non-reassignment) case rather than
the one the note named.

**The fix looks small and already has the pieces it needs.** `FinancialPatternRepository` already
exposes `GetAllByAccount()` (a `Dictionary<accountId, patterns>`, used elsewhere for the pattern-list
UI's per-account labels) — so the fix is scoping `AutoCreateAllocationPlan`'s income candidates to
`GetAllByAccount()[accountId]` (minus transfer patterns) instead of the household-wide query. No new
repository method, no domain change — a call-site correction, the same shape of fix as Stage 1's F1
("the auto-reserve gate never checks the amount's sign").

**Transfer exclusion is correct, not a gap — my original reading of this half was right.**
`AutoCreateAllocationPlan`'s own comment confirms it's deliberate: *"Transfer patterns are excluded
from the income scan so a deposit isn't mistaken for a paycheck."* This means my earlier idea — "set
up a Transfer and the proposer will pace against it" — **does not hold**: a Transfer's deposit leg is
never eligible income, by design. That exclusion is a defensible v1 simplification (the proposer's own
file header already lists several, self-labeled as such) — the honest way to fund a bill from another
account's money today is a manual earmark ([09](09-manual-earmarks.md)) or hand-editing the
auto-proposed plan, not something the default-proposer should try to infer automatically.

**Net:** the parked note's two worries resolve differently than I first thought — (a) reassignment
(item 2-D) is still mechanically clean, no bug there; (b) the "standing assumption" isn't just
*implicit*, it's a **real, unenforced gap in the income-pacing call site**, narrow (only bites at
exactly one household income pattern) but genuine, and worth fixing as part of this stage rather than
left as an assumption to live with.

### FIXED 2026-07-30 — `FinancialPatternRepository.GetByAccountExcludingTransferPatterns(accountId)`

**The change:** a new repository method — `SELECT * FROM FinancialPatterns WHERE AccountId = $AccountId
AND TransferId IS NULL` — and `AutoCreateAllocationPlan` (`MainWindow.xaml.cs`) now calls it instead of
the household-wide `GetAllExcludingTransferPatterns()`. No domain change; `AllocationPlanProposer`
itself already took whatever pattern list it was handed (its own tests are unaffected). The transfer
withdrawal's own proposer call (`Propose(result.Withdrawal, [], ...)`) was already correct — it never
scanned for income at all — and is untouched.

**Tests added, both proving the fix rather than just the mechanism:**
- `PatternRepositoryTests.Excluding_transfer_patterns_by_account_only_returns_that_accounts_non_transfer_patterns`
  — the new query filters by account and still excludes transfer legs.
- `AllocationPlanWiringTests.An_outflows_plan_does_not_pace_against_another_accounts_income` — a
  Weekly paycheck filed under a different account than a Monthly bill; asserts the proposed plan comes
  back `Monthly` (front-loaded, against the bill's own cadence). Before this fix, using the
  household-wide method here would have produced `Weekly` (paced against the wrong account's paycheck)
  — checked by tracing the proposer's own branch logic, not re-run against the old code.
- The pre-existing `A_bill_and_its_proposed_plan_persist_read_back_and_reserve` was updated to call the
  new method too, so it now exercises exactly what `AutoCreateAllocationPlan` does (same-account case).

**Verified:** `dotnet build` on `MyMoneyForecast.App` — 0 warnings. Domain suite: 179 green (unchanged
— nothing here touches the Domain project). Scenario suite: **47 green (was 45)**, 0 warnings. Total
**226** (was 224).

Deliberately not started this round — they are a different question in kind (no existing
`finance_id` to attach anything to, for 23) and depend on how much of 22's shape survives F27.
Flagged here so nothing is lost:

- **Item 23** — allocating toward something with no scheduled `FinancialPattern` yet. The charter's
  own framing of the honest alternative ("move it to another account") needs weighing against
  whatever a placeholder/stub goal would cost.
- **Item 24** — generalizing 22 into something the system *notices* (an over-funded jar) and offers
  to defer, rather than something the user triggers by hand.
- **Cross-account funding interactions** ([10 § Parked](10-multiple-accounts.md#parked-for-the-cascade-tweaking-phase))
  — what happens to a manual earmark that pre-allocated a specific account's anticipated income when
  the bill it was funding moves accounts, and the standing implicit assumption that a goal's funding
  paychecks land in the same account it lives in.
