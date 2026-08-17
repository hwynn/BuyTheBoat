# 27 — Editing within a pattern's own chain (name pending)

**Status: OPEN — started 2026-08-16.** Split off after a direct question surfaced a real,
previously-unflagged gap while working [25](25-editing-patterns-with-history.md)'s own "avoid forcing
consolidation" thread — see the entry logged in
[24](24-app-layer-known-gaps.md#editing-an-early-already-superseded-segment-of-a-break-offrenewal-chain-has-no-guard-at-all)
for the concrete failure that prompted this.

**Sequencing decision (author, 2026-08-16):** this needs answering before returning to 25's own
still-open "avoid forcing consolidation" thread — three phases, in order:
1. **This document, `FinancialPattern` first** — rules for a change to one segment of a break-off/renewal
   chain cascading to (or needing reconciliation against) the segments that continue it.
2. **The same question for `EarMarkPattern` chains** (`RestructureFactory`'s own same-`finance_id`
   sequential segments) — its own section below, not started.
3. **Then, and only then, back to [25](25-editing-patterns-with-history.md)'s own Item F** — cascading a
   change to a `FinancialPattern` onto the `EarMarkPattern`s that fund it (the "avoid forcing
   consolidation for a recurrence-shape change" thread, and the paycheck-association thread, both
   parked mid-conversation).

Author's own framing for what each phase needs to answer: **what kinds of changes do what, and
how/when a warning or a question to the user should be part of the process** — the same two-axis shape
[25](25-editing-patterns-with-history.md) already used (does this touch a structural rule at all; is a
plain warning enough, or does it need to stop and ask) — applied fresh here, since chain-continuation is
a genuinely different kind of cascade than anything Items A–G already cover: those items are about one
segment's own relationship to its own past; this is about one segment's relationship to *other rows
entirely*.

## Naming — OPEN, proposed only

**Options (pick one, or veto all and propose your own):**
1. **"Editing within a pattern's own chain"** *(used as this document's own title below, not yet
   confirmed)*
2. **"Cascading changes across a break-off chain"**
3. **"Chain-continuation rules"**

---

## Reading list
1. [16 — Stage 3: break off](16-stage3-break-off.md) and [15 — Stage 2: pattern lifetime](15-stage2-pattern-lifetime.md)
   — `BreakOffFactory`, `Renew`, the "silent redirect" ruling this document found was never actually
   wired in.
2. [24 — App-layer known gaps](24-app-layer-known-gaps.md), the "editing an early... segment" entry —
   the concrete failure mode that prompted this document.
3. [25 — Editing patterns with history](25-editing-patterns-with-history.md) — Items A–G's own two-axis
   shape (a structural rule vs. a plain warning), reused here for a different cascade direction.
4. `BreakOffFactory.cs` (`FindPredecessor`/`FindSuccessor`/`FindCurrentSegment`) — the only linkage a
   chain has today: shared `Source`, contiguous dates.

---

## What's already confirmed, not re-derived here

- **Nothing links chain segments except `Source` (shared) and contiguous dates**
  (`Until + 1 day == Start` of the next segment). No stored "this continues that" reference exists —
  `FindCurrentSegment` resolves "which segment is current" by picking whichever same-`Source` pattern
  has the latest `Start`, nothing else.
- **The "silent redirect" ruling (planning/15/16) — always land on the current segment when editing —
  was designed but never wired in.** What exists instead ([24](24-app-layer-known-gaps.md)) is a passive
  continuity caption. A user can open and edit any segment directly today, including an old,
  already-superseded one.
- **`FinancialPattern.Create` validates nothing about a chain at all** — no overlap check against a
  sibling sharing the same `Source`, no contiguity check. Whatever rules this document settles on will
  need real validation added, not just UI guidance — matches this project's standing "make it
  structurally impossible, not just discouraged" bar ([23](23-form-behavior.md)'s own "Downward-only
  editing").
- **`FinancialPattern`'s full field set, confirmed against the actual record** (`FinancialPattern.cs`):
  `FinanceId`, `Source`, `DatePattern` (`Start`/`Until`/shape), `Amount`, `Priority`, `Mandatory`,
  `Description`, `AutoRenew`. The original class documentation's `amount_tolerance`/`date_tolerance`/
  `notifications` were never carried into the redesign — not a gap in this document's own table below,
  just confirming there's nothing to say a rule about for fields that don't exist here.

## Field-by-field — narrowed twice by the author since the first pass, 2026-08-16

**Round 1 (author, first pass at the rules):** no implicit cascade without either an ask or, for
`Until`, at least a warning — cascading without permission is exactly what would make it impossible for
a user to keep control of an individual segment (Philosophy 1, applied directly). The check was
originally meant to fire on *both* save buttons ("Save and Skip planning" and "Save and Plan" alike),
wrapped into the existing confirmation popup rather than a new one.

**Round 2 (author, same day, simplifying after reviewing round 1):** narrowed hard.
- **Only `Amount` and recurrence shape ("rrule") cascade forward at all.** Trivial fields
  (`Priority`/`Mandatory`/`Description`) never raise a chain question, no matter how many segments
  follow — plain save, exactly like today. Round 1's "even trivial fields ask" is retracted.
- **No backward cascade, and no "all segments in the chain" option** — retracted, on the author's own
  stated doubts about it. The risk it would have created: an early segment's own correction silently
  overwriting a *later* segment's deliberately different rate (a bill that was $80/month, deliberately
  raised to $100/month via an earlier break-off — fixing an old typo on the $80 segment should never
  quietly turn the $100 segment into the "corrected" number too). Cascading only forward removes that
  risk by construction, since a forward cascade can only ever reach segments that haven't been directly,
  deliberately set to something else *by this same mechanism* — see the open question below for the one
  case this doesn't cleanly resolve.
- **No extra history protection for a downstream cascade target.** A segment being cascaded *into*
  does **not** get its own separate run through [25](25-editing-patterns-with-history.md)'s Items A–C
  (`MightAlterPast`/correct-vs-break-off) the way a segment being *directly* edited does — SETTLED,
  author: "we don't need to worry about history protection... users will have to make sure they've
  selected the right finance pattern in a chain when they want to make changes that cascade forward."
  The lever for scope is which segment you open, not a second protection layer nested inside the
  cascade.
- **New content requirement:** whatever confirms an `Amount`/shape cascade must show, plainly, the date
  range the direct edit itself covers and how far the cascade reaches into the future — which segments,
  through what date — not a bare yes/no.

**Round 3 (author, same day) — corrects round 2: `Start` and `Until` are not "cascade a value" fields
at all, and both were wrong to treat separately.** They define the borders of a pattern and where its
range connects to its neighbors — so the real question isn't "cascade this forward," it's **"does this
pattern stay linked in the chain at all."** One unified question, asked for either field, sharing a
question on the confirmation page when both are in play:

- **Keep it linked** — the relevant neighbor (predecessor for `Start`, successor for `Until`) has its
  own boundary adjusted to match, automatically, so the chain stays contiguous.
- **Let the chain break** — the neighbor is left untouched, producing a gap or an overlap between what
  used to be connected segments. A warning names that consequence whenever this option is the one
  currently selected.

**This explicitly reopens `Until`** — round 1's "no ask, just a forced cascade + warning" is retracted;
`Until` now asks, exactly like `Start` does, for consistency between the two.

**Amount/shape's own open question, resolved:** yes, a real choice — cascade forward, or only this
segment — **cascading forward is the system default.**

**The table, restated whole now that round 3 has settled — SETTLED as of 2026-08-16:**

| Field | Chain behavior |
|---|---|
| `Source` | **SETTLED, 2026-08-16: warn, don't block.** Changing it on a mid-chain segment orphans that segment from the chain entirely (`FindPredecessor`/`FindSuccessor` stop finding it) — this isn't editing a pattern, it's re-linking (or un-linking) the chain itself, but the edit is still allowed, with a serious warning naming what disconnects. Matches [25](25-editing-patterns-with-history.md)'s own Item E precedent exactly ("the system's job is to show the user what would be destroyed and let them decide — never to decide for them by making the option unavailable") — a hard block here would have been the one place breaking that pattern for no real reason. Still open: automatic bank/Mint pairing can update `Source` on its own (`01-glossary-of-terms.md`'s own `source` row) — a chain could be silently orphaned by pairing, not just by a deliberate form edit; the warning above only fires from a form save, and pairing doesn't go through one. |
| `Start` | **"Stay linked in the chain, or let it break" — same question `Until` gets, pointed at the predecessor instead.** Keep linked → the predecessor's own `Until` adjusts to match. Let it break → a gap or overlap forms with the predecessor, warned about when that option is selected. |
| `Until` | **"Stay linked in the chain, or let it break," pointed at the successor.** Keep linked → the successor's own `Start` adjusts to match. Let it break → a gap or overlap forms with the successor, warned about when selected. Round 1's "forced, no ask" is retracted — this now asks, for consistency with `Start`. |
| `Amount`, recurrence shape | **The only two fields that cascade a *value* forward.** Real choice — cascade forward, or only this segment — cascading forward is the system default. |
| `Priority`, `Mandatory`, `Description` | **No chain question, ever** — round 2 retracts round 1's "even trivial fields ask." |

## The confirmation page itself — a new standing shape, not just for this document

**Author's own proposal, 2026-08-16, worth logging as a candidate for
[11 § C](../11-ui-design-and-decisions.md#standing-ui-principles-distilled-from-the-above)'s standing
UI principles, not just this document's own local rule:** every independent question on a confirmation
page — this document's own "stay linked or break," "cascade forward or just this segment," plus
[25](25-editing-patterns-with-history.md)'s existing "correct it everywhere or break off," the
multi-plan consolidate-or-separate choice, the plan-shape picker — becomes **one row**: a set of
options, a system default already selected, a brief description of what each option does. Picking an
option that's "dangerous" or inconvenient surfaces a warning *below that row specifically*, naming the
real consequence — the author's own examples: *"the chain of finance patterns will be broken,"* *"this
savings plan will be underfunded,"* *"manual earmarks on this range will be erased."* This is how
several independent questions fit on **one** confirmation page instead of a stack of sequential popups.
**Cancel stays present on every such page** — already built (`ImplicitChangeConfirmationAnswer.Proceed
= false`), returns to the exact unsaved, uncleared form.

**Two things worth noting, not asking about — already covered by existing machinery, not new gaps:**
- *"Manual earmarks... erased"* is exactly `ManualEarmarkRepository.Delete`'s existing role for an
  orphaned date — [25](25-editing-patterns-with-history.md)'s own `ApplyBackTruncationsIfNeeded`/
  `NarrowSurvivingPlanIfNeeded` already delete a `ManualEarmark` that falls outside a newly-narrowed
  span. "Let the chain break" opening a gap is a new *source* of orphaned dates, not a new deletion
  mechanism.
- *"This savings plan will be underfunded"* already has a real source —
  `PlanHealthState.IsWorthWarningAbout`/`GoalShortfall` are built and already answer exactly this
  question elsewhere; this is a new place to surface them, not new logic to compute them.

**The edge case flagged above — SETTLED, author, 2026-08-16.** "Keep it linked" pushed far enough that
the neighbor would need to shrink past its own boundary (zero or negative length) — possible in either
direction, `Start` reaching back past the predecessor's own `Start`, or `Until` reaching forward past
the successor's own `Until` — resolves to **absorption**: the neighbor is deleted outright, and the
pattern being saved simply expands to cover the range it used to occupy, overwriting whatever values
were there. Deliberately not merged or reconciled — the neighbor's own fields (`Amount`, shape,
`Description`, everything) are gone, not blended. **Its own `EarMarkPattern`(s) go with it** —
`ManualEarmark`s and `RecurrenceRule.ExcludedDates` (planning/26's skip-dates mechanism) tied to the
absorbed segment are simply deleted, same "no extra history protection, the user's own segment choice
is the control" principle already governing the forward-cascade case above — no preserve option, cancel
is the only way out. **Deliberately reframed as a feature, not just a handled edge case (author):** this
gives a user a real way to consolidate a chain that's become too fragmented over time, by deliberately
extending one segment far enough to absorb its neighbors.

**Composes with the row mechanism above without any new machinery — checked, not assumed:** "stay
linked" already carries the row's default selection and already gets its warning drawn from whichever
consequence is real for the *current* edit, not a fixed script — so when "stay linked" escalates from
"nudge a boundary" to "absorb and delete a segment," the same warning slot just carries a more severe
message, still attached to the same (still-default) option. Nothing about the confirmation page itself
needs to change shape for this case.

**One inference, not stated directly, flagged rather than assumed: absorption is not bounded to a
single neighbor.** If the new boundary reaches far enough to swallow more than one segment in a row,
each one is absorbed in turn — reads as the more useful version of "a mechanism to consolidate a
fragmented chain" (a chain fragmented into many short segments is exactly the case where sweeping up
several at once matters most), but it's this document's own extrapolation, not something said explicitly
— worth a veto if a single-hop limit was actually intended.

**Warning content, SETTLED:** must name the concrete consequence, not a generic notice — which pattern
is being absorbed, its date range, how many manual earmarks and skipped dates go with it. Same "show the
consequence, not just a yes/no" precedent [25](25-editing-patterns-with-history.md)'s own Item E already
established for orphaned data.

**Confirmed: everything in this section applies equally to `EarMarkPattern` chains (Phase 2) —
author, 2026-08-16.**

## A fourth relationship, surfaced 2026-08-16 — now slotted immediately after Phase 1, combined with Phase 2

**The author's own question: does a change to an `EarMarkPattern` ever cascade to the `EarMarkPattern`s
funding the *same conceptual expense* on the other side of a `FinancialPattern` break — a different
`finance_id`, in that segment's own separate chain?** Distinct from every relationship named above —
Phase 1 is `FinancialPattern` ↔ its own future segments; Phase 2 (below) is `EarMarkPattern` ↔ its own
future segments *sharing one `finance_id`*; this is `EarMarkPattern` ↔ `EarMarkPattern`, like Phase 2,
but crossing a Phase-1-style `finance_id` boundary — something neither phase's own mechanism reaches,
since an `EarMarkPattern`'s only identity is `FinanceId`/`DatePattern`/`Amount`/`StartingAllocation`, and
`FinanceId` is exactly what changes at a break. Finding the "other side" at all requires composing two
already-built lookups that have never been composed across this boundary before:
`BreakOffFactory.FindPredecessor`/`FindSuccessor` (the `FinancialPattern`-level chain, `Source`-based)
to find the neighboring segment, then `EarMarkPatternsFor` on *that* segment's own `finance_id`.

**A real, concrete piece of this — FIXED 2026-08-16, see
[24](24-app-layer-known-gaps.md#item-gs-keep-the-same-scheduleamount-choice-is-permanently-disabled-the-first-time-a-savings-plan-is-ever-restructured):**
Item G's own "keep the same schedule/amount" candidates ([25](25-editing-patterns-with-history.md)) used
to silently stop being offered the first time a Savings Plan was ever restructured, even once, even
years ago. Narrower than the question below, since it only ever affected the moment a break-off itself
happens, not whether a *later* edit to an already-superseded segment reaches forward into an
already-existing successor — that part is still open, right below.

**The harder part, genuinely unanswered:** even with that fixed, nothing today lets an edit made *after*
a break has already happened — restructuring Rent's old segment's own plan, skipping a date on it —
reach the new segment's own, already-running plan. Doing so can't reuse this document's own "absorb"
mechanism directly: `EarMarkPattern.Create` requires `FinanceId` to match its own goal exactly, so one
`EarMarkPattern` row can never span two different `FinancialPattern`s the way "absorb" lets it span two
segments of one. Whatever this needs would be a different shape, not a reuse.

**Priority, SETTLED 2026-08-16 (author, via a direct question): this matters a lot — "the actual pain
point," worth building soon, not deferred as a someday-maybe.** The one-time carry-forward Item G's own
fix provides ([24](24-app-layer-known-gaps.md)) is not considered enough on its own.

**Second round of small questions, SETTLED 2026-08-16, all four:**
- **The cascade itself asks, every time — not automatic.** Consistent with the general tiebreaker
  below; reaching into an already-existing successor's own plan gets the same "ask, don't silently act"
  treatment as everything else in this document.
- **Design this together with Phase 2, as one combined piece, rather than finishing Phase 2 in
  isolation first.** Doesn't dictate build order, only that the two shouldn't be designed as if
  unrelated — they're close enough in spirit that a separate design pass risks redoing work. Slotted
  immediately after Phase 1 in the order below.
- **`EarMarkPattern` chain-boundary mechanics (stay linked / let it break / absorb) work exactly the
  same as `FinancialPattern`'s — no extra leniency.** A genuinely useful scoping finding, not just an
  answer: [planning/26](26-editing-an-earmark-pattern.md)'s own "Savings Plans aren't real the way bills
  are, more freedom" principle does **not** blanket-apply to everything — it was scoped there to a
  plan's own *rate/recurrence choices* (does it meet its goal, is it over/under-funded), not to
  *structural chain mechanics*. Worth keeping in mind the next time "more freedom for EarMarkPatterns"
  comes up — it's not a universal exemption.
- **Absorption reaches as far as the new boundary goes, confirmed** — not limited to one hop. Settles
  the inference flagged earlier in this document.

**Third round, SETTLED 2026-08-16 (three of four; the fourth needs a precise answer, not guessed —
see the open item right below):**
- **The cross-boundary cascade is forward-only too**, same reasoning as Phase 1 — an edit to a later
  segment's own plan shouldn't reach back and rewrite an earlier, already-settled one.
- **No picker for what gets carried forward — always whichever rate/schedule is current today.**
  Simpler than Item G's own candidate choice; the real decision point is "cascade at all," not "which
  of several historical shapes."
- **`RecurrenceRule.ExcludedDates` (planning/26's skip-dates mechanism) need no cascade mechanism of
  their own.** An excluded date is tied to one specific occurrence, which only ever falls inside
  whichever segment's own date range actually contains it — once that segment ends, the old excluded
  date isn't a candidate occurrence in the new segment's own range at all, so there's nothing left to
  carry forward.
- **RESOLVED: yes, reopen it — `FinancialPattern`'s own trivial fields
  (`Description`/`Priority`/`Mandatory`) get a chain question too, not "no chain question, ever" as
  round 2 settled.** Default stays "just this segment" — nothing about today's actual behavior changes
  for anyone who accepts the row's own default — this is about exposing the option, not changing what
  happens by default. No warning needed on this row either direction; neither choice has a real
  downside for these three fields. **Doesn't extend to `EarMarkPattern`'s own side of the cross-boundary
  cascade** — it has no `Description`/`Priority`/`Mandatory` equivalent at all (confirmed field list
  above), so there's nothing analogous to reopen there.

**A new standing principle behind that answer, worth keeping distinct from the specific field it fixed
— author's own two-part reasoning, 2026-08-16:** *"if we present the questions as options with a
default answer already picked, it shouldn't be many more clicks at all... and it prevents the user from
getting locked out of making a certain kind of change we didn't anticipate."* Once the row-based
confirmation page (above) makes a question nearly free to answer — a pre-picked default, no extra click
required to just proceed — **prefer exposing it as an always-available option over hard-coding a
"never" rule**, even for a field that seems safe to skip entirely. The cost of asking is close to zero;
the cost of a hard "never" is permanently foreclosing a use case nobody anticipated yet. Candidate for
[11 § C](../11-ui-design-and-decisions.md#standing-ui-principles-distilled-from-the-above) alongside the
row-based confirmation shape itself.

**Two general standing rules, SETTLED 2026-08-16, both apply beyond just this document:**
- **Ordering, when one save could raise both a chain question and a funding question at once: the chain
  question resolves first.** Whether a pattern stays connected to its own other segments is settled
  before computing what that pattern's own funding implications even are — the funding math depends on
  the chain's final, settled shape, not the other way around.
- **The general tiebreaker for anything still genuinely close: ask or warn, don't silently decide.**
  Between "the app does something unwanted without saying so" and "saving becomes a chore with too many
  questions," the author's own preference is firmly the former risk over the latter — lean toward
  surfacing a question or a warning whenever a choice is real, even at the cost of more clicks.

## Not started

- **Phase 2 — the same question for `EarMarkPattern` chains** (`RestructureFactory`, same `finance_id`,
  no new identity) **combined with the fourth relationship above** (SETTLED 2026-08-16: designed
  together, not as two separate passes) — one design pass covering both same-`finance_id`
  `EarMarkPattern` chains and the cross-`FinancialPattern`-boundary cascade, next up once Phase 1's own
  remaining loose ends (the multi-hop absorption inference, now confirmed; anything else that surfaces
  while starting this) are closed out. A related gap is already on record (planning/21's own "editing an
  early (superseded) segment has a real knock-on effect on its successor... the jar never restarts
  across a cut" note) — worth reading again once this starts, not re-derived here.
- **Phase 3 — resuming [25](25-editing-patterns-with-history.md)'s own Item F** ("avoid forcing
  consolidation" for a recurrence-shape change, and the paycheck-association/"loose association"
  cascade) — deliberately parked until Phase 1 and the combined Phase 2 are settled.
