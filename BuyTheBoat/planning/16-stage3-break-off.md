# 16 — Stage 3: Changing a pattern at a point in time

**Status: DOMAIN BUILT — 2026-07-29** (mechanism settled 2026-07-29, same day). The taxonomy (item 6),
break-off's mechanism (item 4 — `BreakOffFactory`), truncating (item 16 — `PatternTruncation`),
**breaking off a transfer** (`TransferBreakOffFactory`, resolved same day after confirming no other
stage owned it), and **the "ongoing" pattern's periodic-renewal reuse of break-off** (`BreakOffFactory.Renew`,
resolved with the author as a sequence of questions — full rationale in
[15's Ongoing/renewal section](15-stage2-pattern-lifetime.md#ongoing--renewal-resolved-and-built-author-2026-07-29),
since that's where "ongoing" was originally deferred from) are **designed and built**, 34 new domain
tests, 173 domain / 45 scenario green, 0 warnings. Deleting (item 18) got its one small improvement —
the confirmation dialog now names the amount being freed. **Genuinely still open:** item 4-D's confirm
screen — the *data* it needs is fully determined, but rendering it (like the item-16/18 entry points,
and whatever eventually triggers a renewal on a schedule) is UI/wiring work, deferred and **not wired
into the app yet**; F23 (`Source` uniqueness) is correctly parked pending actuals. Stage 3 of the
["Adjusting the Plan" phase](13-adjusting-the-plan-charter.md).

**The stage in one paragraph:** a repeating pattern's amount or schedule changes mid-life — rent goes
up, a job changes pay. The user wants the change to apply *going forward* while what already happened
stays computed the way it was reasoned about at the time. Constraint 1 (one `Amount`, one
`RecurrenceRule` per pattern) means that can only be expressed by ending the old pattern at the change
date and starting a new one — a genuine new `finance_id`, not an edit. This stage designs that
mechanism end to end (the cut, the jar hand-off, the earmark pattern's bound, the paycheck case, the
preview-and-confirm flow), plus its two simpler siblings — ending a pattern early with no continuation,
and deleting one outright — and the taxonomy that routes a change to the right one of the three.

### Reading list
1. [13 — charter](13-adjusting-the-plan-charter.md), the Stage 3 section, items 4/6/16/18, and the
   pressure-map rows `3.11.2.a2`/`3.13.7.a2`/`1.2.3.10.a3` (now updated with what Stage 2 already
   resolved for them).
2. [13a — workaround registry](13a-linearity-workaround-registry.md), **W8** (split-and-continue,
   proposed here) and **W7** (manual earmarks — jar lifetime = earmark pattern span, the mechanism
   this stage's hand-off leans on).
3. [09 — manual earmarks](09-manual-earmarks.md), ruling 1, and the "days ≤ as-of fold into the jar's
   seed value (like `StartingAllocation`, but dated)" line — the seed mechanism reused below.
4. [14 — stage 1](14-stage1-allocation-model.md) — `EarMarkPattern.StartingAllocation`,
   `AllocationPlanProposer`'s shape A (bills paced against a specific income's rhythm).
5. Code: `TransferFactory.cs`, `OneTimeGoalFactory.cs` (the two "one action, linked patterns"
   precedents), `EarMarkPattern.Create` (the active-span check this stage's split must satisfy), and
   `MainWindow.xaml.cs`'s `OnDeleteFinancialPatternClick` (item 18's starting point).

---

## What already exists that this stage builds on

- **W1 / the two precedents.** `OneTimeGoalFactory` (goal + savings plan) and `TransferFactory`
  (withdrawal + deposit + a `Transfer` link record) are both "one user action, several linked domain
  objects, returned as one result record." Item 4's break-off is explicitly the third of this family
  (charter, § What already exists) — but see item 4-A below for why it may **not** need a link record
  the way Transfer does.
- **Constraint 1 is *why* this stage exists**, not incidental to it. `FinancialPattern.Amount` /
  `RecurrenceRule` are each a single scalar / single rule for the pattern's *entire* lifetime — there
  is no way to say "was $1,600/mo until March, $1,800/mo after" inside one pattern. The class doc's own
  words on `FinancialPattern` are relevant here: *"changes cascaded into past stop at expired pages."*
  Today `Expired` is always `false` (the `(page-length)` divergence — nothing persists across runs), so
  a plain edit to `Amount` currently rewrites **all** history uniformly, past and future alike — which
  the documented model apparently expected, but which a user changing their rent going forward almost
  certainly does not want. That gap is precisely what break-off exists to close.
- **`EarMarkPattern.StartingAllocation` already exists** (`14-stage1-allocation-model.md`) — "how much
  is already sitting in this jar before any of `DatePattern`'s own occurrences run." Built for a
  goal that's already partly funded before this app knew about it. It is the exact mechanism item
  4-B needs for the jar hand-off: no new field, no new type.
- **09's ruling 1** ties a jar's lifetime to its earmark pattern's span, and separately notes that
  manual-earmark days *at or before the as-of date* "fold into the jar's seed value (like
  `StartingAllocation`, but dated)" — the same idea already in production use for a different case.
- **Delete-with-confirm is already built** (13b, rows B11/C6): deleting a `FinancialPattern` with a
  linked `EarMarkPattern` no longer blocks — it confirms, then cascades both. Item 18 mostly inherits
  this rather than starting from zero.
- **`3.11.2.a2` now holds literally, both directions, against the *active span*** (the `ActiveFrom`
  resolution, Stage 2): an earmark pattern can't begin before or extend past its goal's active span.
  Any split mechanism has to keep both new patterns' spans correctly nested under this check — see
  4-C.

---

## Item 6 — The taxonomy · **SETTLED 2026-07-29**

*"Which kinds of change need a split, which are a plain edit, and which need something else again."*

**The test:** does the user want this to be true **only from a date forward**, while the past keeps
being reasoned about the way it already was — or is it fine (or actively wanted) for the change to
apply uniformly across the whole pattern?

| Bucket | Test | Examples | Mechanism |
|---|---|---|---|
| **Plain edit** | Not baked into allocation math, or uniform-everywhere is correct/wanted | Priority (B7), skippable/unskippable (B8), description, a **correction** to amount/schedule (a typo, a data-entry mistake), **extending** `Until` further out | unchanged, today's edit form |
| **Break off** (item 4) | Amount or schedule genuinely changes, "as of a date," the pattern *continues* | Rent goes up; a raise changes paycheck amount | new mechanism, this stage |
| **Truncate** (item 16) | The pattern should *stop*, no continuation | "Cancelling Netflix next month" | new mechanism, this stage |
| **Delete outright** (item 18) | Remove entirely, immediately | A pattern created by mistake | mostly already built |
| **Out of Stage 3** | Allocation-only change, bill unchanged | Item 8 (Stage 4); item 5/17 (actuals, → 12) | elsewhere |

**F24 · Editing amount/schedule does not automatically route through break-off — the two stay
separate, deliberately chosen actions.** The taxonomy's real branch point is not "did amount or
schedule change," it's "does the user want the past preserved or corrected." A data-entry correction
(the amount was wrong, not changing) belongs in the **existing** edit form, which still applies
uniformly across the whole pattern (Constraint 1/W4, unchanged) — exactly right for a correction, and
this is what the form already does today, so no new gate belongs in front of it. **"Change starting on
a date"** (item 4) is a **separate, explicitly-chosen** action for when the user wants forward-only
effect. Conflating the two — e.g. auto-prompting "was this a correction or a real change?" on every
amount edit — would turn a rare, deliberate action into friction on the common case. This also settles
`Source`: a typo fix is a plain edit; a real-world identity change (the biller's statement text
changed) is item 5, already deferred to 12.

**Stress test confirming the boundary holds:** extending a bill's `Until` further into the future (a
lease renewed for another year) is a **plain edit**, not a break-off, even though it changes what the
schedule produces going forward — because it adds only *new* future occurrences at the *same*
amount/cadence; nothing about how any existing occurrence, past or future, is computed changes. This is
the mirror image of item 16 (shortening `Until`) and worth naming so it isn't mistaken for break-off
territory. (It's also W9's eventual mechanism for auto-renewing an "ongoing" pattern, once that lands —
the same plain-edit operation, done automatically and periodically rather than once by hand.)

**Verified in code, 2026-07-29 — the "zero retroactive effect" claim for Priority/Mandatory is not just
documentation.** `TransactionLogBookFactory.cs` reads `Priority`/`Mandatory` directly off the *current*
pattern object when building each day's deallocation input (`patternsById[financeId].Priority`,
`!patternsById[financeId].Mandatory`) — nothing about either value is cascaded or stored from a
previous run (W4: the whole onion rebuilds from scratch every time). Confirms the boundary note above
is a fact about this engine, not just an inference from the docs.

**F25 · The cut date may be in the past, not only the future** — and this build's model makes that
coherent rather than fraught. There is no locked actual-transaction ledger yet (`ActualTransaction` is
a pure stub); everything before the as-of date is *reconstructed* from the current patterns, not a
frozen historical record. So "starting three paychecks ago, my raise took effect" (entered late, a
common real case) is not a contradiction — it corrects the reconstruction to match what actually
happened, exactly the outcome break-off exists to produce. See 4-B for how the jar hand-off differs
between a past and a future cut date.

**Boundary note — out of scope, not this taxonomy's job:**
- **Allocation-only changes** (an earmark pattern's amount/schedule with the bill itself unchanged) —
  that is Stage 4's territory (items 8/9/22/23/24), flagged explicitly in
  [09](09-manual-earmarks.md)'s own opening as "its own future design." Worth stating plainly so Stage
  3 doesn't scope-creep into it.

---

## Item 4 — Break off · **BUILT (domain) 2026-07-29** — `BreakOffFactory`, 13 tests (4-D UI not wired in)

**`BreakOffFactory.BreakOff`** (`src/MyMoneyForecast.Domain/BreakOffFactory.cs`) takes a
`BreakOffRequest` (the predecessor + its plan, the cut date, the successor's new FinanceId/amount/
schedule, the carried-over jar balance read by the caller, and `AllPatterns` for the proposer) and
returns a `BreakOffResult` (the truncated predecessor + plan, the successor, its freshly-proposed plan,
and any starting earmark). It calls `PatternTruncation.EndOn` (item 16, below) for the predecessor half
and `AllocationPlanProposer.Propose` for the successor's plan — no new allocation logic, exactly as
4-B/4-C settled. Validates: the cut date must be after the predecessor's start; the successor needs its
own FinanceId; the successor's schedule must start exactly on the cut date. 13 tests cover both outflow
shapes (front-loaded, paced), the no-predecessor-plan case, the income/paycheck path (skips the jar
machinery entirely), the carried-over balance landing on `StartingAllocation`, `ActiveFrom` staying null
per 4-B's reasoning, a past cut date (F25), and all three validation rejections.

### 4-A — Identity across the cut · **SETTLED 2026-07-29**

Constraint 1 forces a genuinely new `finance_id` for the segment starting at the cut — there is no way
for one `FinancialPattern` object to carry two amounts over time. The question was how much plumbing
ties the predecessor and successor together for the user, who should see "my electric bill," not
"electric bill #1" and "electric bill #2," and whether the predecessor stays visible in the pattern
list once bounded.

**Ruling: no `Transfer`-style link record.** Transfer needs one because both legs coexist
*simultaneously and permanently* — every occurrence, forever, the withdrawal and the deposit are both
true at once, and the UI has to hide two rows and reason about them as a pair on an ongoing basis.
Break-off is different in kind: it's a point-in-time hand-off. After the cut, the predecessor is
*done* — bounded, no more occurrences — and only the successor is "live." Nothing needs to look them
up as a pair during normal operation. Copying the predecessor's `Description` verbatim onto the
successor is enough for the user to read it as continuity, and a new `finance_id` for a genuinely
changed thing is arguably *more* consistent with the documented model, not less — recall
`FinancialPattern`'s own class-doc quote: two patterns match (get treated as the same thing) only "if
they have the same source, rrule (excluding start date), amount." A pattern whose amount changed does
not match by the model's own stated criterion.

**F23 · `Source` uniqueness collides with keeping the predecessor's history.** `4.2.a1` requires
`Source` non-null, and the pressure map separately lists "no two patterns share one" — but **this
uniqueness is documented, not enforced**: `FinancialPattern.Create` only checks non-empty (confirmed
in code, 2026-07-29), and there is no unique constraint at the persistence layer either. So today,
nothing stops the successor from reusing the predecessor's exact `Source` string even while the
predecessor is kept around (bounded, not deleted) — but the *moment* actuals/bank-pairing land and
that rule gets enforced for real, a kept predecessor and a same-`Source` successor will conflict. This
is genuinely a `12`-territory consideration (recorded there, not resolved here) but it directly shapes
4-A: if the predecessor is kept for history, the successor likely needs its **own** `Source` (breaking
continued bank-statement auto-pairing until item 5's identity-change flow exists), or the predecessor
needs to be treated as retired/exempt from uniqueness once superseded. Either way, this is a real
constraint on the identity design, not just a UI question of whether two rows are shown.

**Ruling: predecessor stays visible — this was never actually break-off's decision to make.**
Verified in code, 2026-07-29: `MainWindow`'s pattern list (`FinancialPatternsGrid.ItemsSource =
_financialPatterns.GetAllExcludingTransferPatterns()...`) applies **no filter on `Until`** — every
pattern shows today, including ones already bounded in the past (a paid-off loan, an ended one-time
goal). A bounded predecessor is, structurally, just another instance of that same already-existing
case — nothing about break-off needs to special-case it. **If** hiding already-ended patterns is ever
wanted, that is general list-hygiene work belonging to Stage 6 (or a UI-phase cleanup), not something
break-off's mechanism has to solve or block on.

### 4-B — The jar hand-off · **SETTLED 2026-07-29**

**Seed the successor's `EarMarkPattern.StartingAllocation` with the predecessor's jar balance at the
cut date.** No new field. The mechanism to *read* that balance already exists in practice —
`ManualEarmarkWindow` already shows "live day balances" for a chosen date by reading the current
forecast — so a break-off's confirm screen can do the same: run the forecast as-is, read the jar's
value on the cut date, and pre-fill it as the successor's starting point.

**Refined for F25 (the cut date can be past or future):**
- **Future cut date:** the jar balance is a **projection from today's data**, not a locked-in fact —
  the same "floor, does not re-derive" honesty already established for the payoff estimate (W3). The
  confirm screen should say "as currently projected," not present the number as certain.
- **Past cut date:** there's no separate "actual" value to defer to (no locked ledger exists yet — see
  F25) — the jar balance the app currently *reconstructs* for that date under the *old* pattern **is**
  the number, full stop, and it becomes the successor's starting point without the projection caveat.

### 4-C — The earmark pattern's "parallel split" · **SETTLED 2026-07-29**

The charter's own phrase ("split the earmark pattern to match") could be misread as needing a new
earmark-splitting primitive. It doesn't: **truncate the predecessor's earmark pattern's `Until` down to
the cut date** (already required — `3.11.2.a2` forbids an earmark outstanding past its now-shorter
finance pattern), **then create an ordinary new `EarMarkPattern`** for the successor, seeded with 4-B's
`StartingAllocation`. Two existing operations, no new one.

**Ruling: the successor's ongoing contribution schedule is always freshly proposed, never copied
from the predecessor.** This needs no new decision — it falls straight out of Stage 1's already-settled
default: *every* outflow gets its plan from `AllocationPlanProposer`, unconditionally, unless the user
takes it over by hand ("Set Up Savings Plan," item C12). A break-off successor is, mechanically, just a
new outflow — Stage 1's rule already covers it without a Stage-3-specific carve-out. It's also simply
more correct than copying: the whole premise of break-off is that the amount or schedule changed, so a
copied old schedule is sized against a fact that's no longer true. A user who wants to keep hand-tuned
saving habits already has the same override available to them as for any other pattern.

### 4-D — Preview-and-confirm · **data contract settled; not wired into the app — deferred to the UI phase**

Now that 4-A–4-C are settled, the confirm screen's **inputs are fully determined**, even though its
layout isn't (unbuildable/unverifiable here, same as Item D): the cut date; the predecessor's new
(earlier) `Until`; the successor's amount and schedule (freshly proposed per 4-C, editable before
confirming); the jar balance being carried over via `StartingAllocation` (labelled as projected or
reconstructed per 4-B); and, for a paycheck break-off, nothing from 4-B/4-C at all (4-E). Per
[11's standing UI principles](11-ui-design-and-decisions.md#standing-ui-principles-distilled-from-the-above)
("pre-fill a form as far as it can honestly go, then stop at the confirm... scoped to the problem") —
every one of those fields is pre-filled, none require the user to compute anything by hand.

### 4-E — The paycheck case · **SETTLED 2026-07-29** (the optional prompt deferred to Stage 6)

Income never gets a jar (Stage 1), so breaking off a paycheck skips 4-B/4-C entirely: cut the old
pattern's `Until`, create a new one continuing from there. **That alone is Stage 3's complete job for
paychecks.** What the charter flags as "may disturb coinciding earmark patterns": `AllocationPlanProposer`'s
shape A paces *other* bills' plans against a *specific* income's rhythm (frequency/interval/by-day
copied directly from that income's `DatePattern`) — if the payday's schedule or amount changes,
previously-proposed plans elsewhere don't
auto-update; a proposal is a one-time snapshot, not a live binding (the same known gap already named in
Stage 1's remaining refinements — "bill edit doesn't re-propose its plan"). **This is not a silent
gap even without a prompt**: F21's shortfall formula already flags a plan that's fallen behind, which
is exactly what a now-stale pacing produces. **Ruling: the "optional prompt" is deferred to Stage 6, not
part of Stage 3's deliverable.** It is a lever on top of an already-safe fallback (philosophy 1's own
framing), not a correctness requirement — F21's existing shortfall flag means nothing silently breaks
without it. Same treatment as item 16's legibility framing below: making the underlying mechanism
correct is Stage 3's job; the follow-up nudge belongs with the rest of Stage 6's warnings-and-levers
work.

**Transfers — RESOLVED and BUILT 2026-07-29.** Item 4's charter language was framed around ordinary
bills and paychecks; a transfer's two `FinancialPattern`s (produced by `TransferFactory`, linked by a
`Transfer` record) were never named, and nothing in 4-A–4-E was checked against that case. Confirmed
not owned by any other stage (the charter names `Transfer` exactly once, the precedent citation), so
resolved here rather than left parked. **`TransferBreakOffFactory.BreakOff`**
(`src/MyMoneyForecast.Domain/TransferBreakOffFactory.cs`) — a transfer is *three* linked things, not
one (the withdrawal leg, the deposit leg, and the `Transfer` record itself, which is the canonical
definition the project's own validation sweep checks the legs against). Calling `BreakOffFactory.BreakOff`
independently on each leg would let them drift (different cut dates, mismatched amounts) and would
leave the *old* `Transfer` record's own schedule pointing past where its legs now end — exactly the
inconsistency the sweep exists to catch, this time caused by us. The new factory makes that
structurally impossible: it calls `BreakOffFactory.BreakOff` once per leg with the same cut date and
mirrored amount, truncates the old `Transfer` record to match via a new `Transfer.WithUntil` helper,
and builds a new `Transfer` record linking the two successors — reusing every existing ruling (4-A
through 4-F) unchanged; no new principle, pure composition. Validates the predecessor legs actually
agree with the predecessor `Transfer`'s amount before doing anything (catches pre-existing drift rather
than compounding it). **Checked against assumptions before building** (`3.11.2.a2`, `3.13.5.a2`,
`3.10.a3`, `1.2.3.10.a3`) — none violated; a transfer leg is invisible to all of them as anything other
than an ordinary `FinancialPattern`. 8 tests: both legs and the `Transfer` record land on the same cut
date and mirrored amount; the old `Transfer` truncates to match its legs; the new `Transfer` references
the two new `FinanceId`s; only the withdrawal leg gets a plan; the carried-over balance lands correctly;
already-drifted predecessor legs are rejected (both directions); a past cut date works. 166 domain / 45
scenario green, 0 warnings. **Not wired into the app** — same UI-phase deferral as ordinary break-off.

### 4-F — Naming · **SETTLED 2026-07-29**

**User-facing name: "Change starting on a date."** Chosen over "Change effective [date]" (real
terminology, but jargon the user has to already know — philosophy 2), "Update from here" (shortest,
but context-dependent), and "Schedule a change" (risks blending with the app's existing, unrelated use
of "schedule" for a pattern's rrule/date_pattern). Mirrors the phrasing already settled for item 16's
own action, which reads consistently next to it. **"Break off" stays the internal/developer term** —
this file's name, W8's registry entry, and all reasoning above keep using it; only what the user sees
changes.

---

## Item 16 — Ending a pattern early ("truncate") · **BUILT 2026-07-29** — `PatternTruncation`, 6 tests (legibility deferred to Stage 6)

**`PatternTruncation.EndOn(pattern, plan, lastDay)`** (`src/MyMoneyForecast.Domain/PatternTruncation.cs`)
is the entire mechanism, exactly as scoped: it ends the pattern via the new `FinancialPattern.WithUntil`
helper, and — only if a plan exists — truncates the plan to `min(plan.Until, lastDay)`, never
extending it. Shared with item 4 above (its predecessor half is exactly this call). 6 tests: both
fields end together; every other field (source, description, amount, priority, mandatory,
`ActiveFrom`) survives untouched; a plan that already ended earlier keeps its own earlier end (never
stretched out); a pattern with no plan truncates cleanly; an end date before the pattern's start is
rejected. **Not yet wired to a UI entry point** ("Stop this on a date," 4-D-adjacent) — that's the
deferred UI-phase piece; the mechanism itself is complete and tested.

*"I'm cancelling Netflix next month."* Mechanically the simplest of the three: set the finance
pattern's `Until` to the chosen date. What's "clunky" about doing this today (per the charter) is that
it requires the advanced RRule editor and offers no jar-aware guidance — there's no dedicated action,
and no consideration of what happens to the money already set aside.

**The jar question resolves for free.** `3.11.2.a2` forces the earmark pattern's `Until` down to match
the shortened finance pattern; once that happens, `3.13.5.a2` means the jar simply stops being computed
past that date — nothing is destroyed, nothing needs to be moved, because a jar was never a real
transfer of money to begin with (`FundJar`'s own class doc: "recording that money is set aside
*without moving it to another account*"). Whatever was in the jar becomes ordinary free balance
automatically, structurally, via the same containment assumption already enforced everywhere else.

**Ruling: the *legibility* question (philosophy 2) — should the user be told "you'll get $340 back once
Netflix ends," or is it enough that free funds quietly reflects it — is deferred to Stage 6**
(warnings/levers), not decided here. Stage 3's job is making the truncation possible and correct;
*announcing* its effect is exactly Stage 6's subject, and nothing about the mechanism above depends on
how that gets answered.

The concrete build, once a UI is chosen: a dedicated "Stop this on a date" action near wherever a bill
is edited, using the same schedule-driven end-date input Item D already built for the payoff question —
not a new control type, a new *entry point* into machinery that already exists.

---

## Item 18 — Deleting a pattern outright · **BUILT 2026-07-29** (one small dialog-wording improvement)

The charter's own question — "any implications not already covered?" — turns out to have a short
answer. `OnDeleteFinancialPatternClick` (verified in code, 2026-07-29) already checks
`HasLinkedEarMarkPattern`, confirms with the user ("Deleting X will also remove its savings plan —
continue?"), and cascades both deletions; 13b already records this for both bills (B11) and goals (C6).

**The one implication genuinely worth naming:** what happens to money already in the deleted jar?
Because nothing computed is ever persisted (W4 — every forecast rebuilds the whole onion from
patterns + manual earmarks + balance), deleting the `EarMarkPattern` means the jar simply isn't
computed on the *next* run — there was never a real balance sitting anywhere to lose, so the money
reads as free again automatically, the same structural argument as item 16's. **Built 2026-07-29:** `OnDeleteFinancialPatternClick` (`MainWindow.xaml.cs`) now reads the jar's current
amount (the same `CurrentJarAmount` helper the earmark-creation flow already uses) and, when positive,
appends "...and free up {amount:C} currently set aside" to the existing confirmation dialog. No new
mechanism — a one-line message change using data the window already had access to. Build clean, 0
warnings; **UI-unverified** (WPF, same caveat as every other App-layer change this phase) — worth a
glance next time the app is run.

---

## Items 5 and 17 — registered, not designed here

Per the charter, both are actuals-dependent and belong in
[12-actual-transactions-deferred-design.md](12-actual-transactions-deferred-design.md), not this
document. **Neither had actually been recorded there yet** (checked 2026-07-29) — added now, alongside
F23 above, under a new "Pattern identity & break-off" section in 12.

---

## Grounding check — no divergence

`BreakOffFactory`, `PatternTruncation`, and `TransferBreakOffFactory` add no new domain property (unlike
`ActiveFrom`/`ongoing`) — all three are pure orchestration over existing types via the existing
`StartingAllocation` field and the `WithUntil` convenience constructors (`RecurrenceRule`,
`FinancialPattern`, and now `Transfer` — all mirroring `WithActiveFrom`, none a model change).
Truncation *enforces* `3.11.2.a2` more precisely, not less. A new `FinanceId` for a genuinely changed
pattern was already argued in 4-A to be *more* consistent with the documented model's own matching
criterion, not a departure from it. `Transfer` itself carries no documented assumptions to begin with —
it's a philosophy-3(a) abstraction, not a class from the original model — so `TransferBreakOffFactory`
answers only to the project's *own* transfer-consistency rule, which it's built specifically to satisfy.
**No `DIVERGENCE(...)` tag, no [05 registry](05-original-structure-restructure.md) row needed for any of
the three.**

## Findings

*(continuing the phase's numbering from [14](14-stage1-allocation-model.md)/[15](15-stage2-pattern-lifetime.md))*

| # | Finding | Bears on |
|---|---|---|
| F23 | `Source` uniqueness (`4.2.a1` + "no two patterns share one") is documented but **unenforced** in code today — a kept predecessor and a same-`Source` successor won't collide *yet*, but will the moment actuals/pairing land | item 4-A; registered in 12 |
| F24 | Editing amount/schedule does not automatically imply break-off — the real branch is "correct the past" (plain edit, unchanged) vs. "change only going forward" (break-off, a separate deliberate action) | item 6 taxonomy |
| F25 | The cut date may be in the past as well as the future — coherent because nothing before the as-of date is a locked ledger yet, only a reconstruction from the current patterns | item 6; item 4-B |
| F26 | A transfer is three linked things, not one — the `Transfer` record itself, not just its two `FinancialPattern` legs, needs the same cut-date truncation or the project's own validation sweep flags it as drifted | transfers (resolved by `TransferBreakOffFactory`) |

---

## Naming — settled (see 4-F)

The charter named this explicitly: *"a better user-facing name than 'break off' gets chosen here."*
Resolved directly with the author, 2026-07-29 — see [4-F](#4-f--naming--settled-2026-07-29).
**"Break off" stays the internal/developer term** (this file's name, W8's registry entry), the same way
"Allocation Plan" and "the ramp" stayed internal while Stage 1 chose separate user-facing wording.

---

## Parked — explicitly out of scope for Stage 3

- **Allocation-only changes** (the bill itself unchanged) — items 8/9/22/23/24, Stage 4.
- **Cross-account interactions during a break-off** — Stage 4's territory per the charter.
- **The "ongoing" pattern's periodic-renewal use of break-off** — Stage 2 flagged this as a *future
  consumer* of whatever mechanism lands here ("break-off stays... possibly for renewing open-ended
  patterns"). Worth keeping in mind while designing 4-A–4-C so nothing here assumes the action is
  always user-initiated in the moment — a forward-compatibility note, not a Stage 3 obligation.
- **Item 5** (identity-only change, no properties differ, only the source string does) and **item 17**
  (a bill cancelled in the past and never told to the app) — both → 12, registered above.
- ~~**Breaking off a transfer's withdrawal or deposit leg**~~ — **resolved 2026-07-29**, see 4-E above
  and `TransferBreakOffFactory`. No longer parked.
