# 27 — Editing within a pattern's own chain (name pending)

**Status: Phase 1 is now BUILT too, 2026-08-17 — every phase this document set out to cover (1, 2, the
fourth relationship) is built and tested end to end, real WPF UI included. Only Phase 3 (declined by the
author, not built) remains.** Split off after a direct question surfaced a real, previously-unflagged gap
while working 25's own "avoid forcing consolidation" thread — see the entry logged in
[24](24-app-layer-known-gaps.md#editing-an-early-already-superseded-segment-of-a-break-offrenewal-chain-has-no-guard-at-all)
for the concrete failure that prompted this.

**A real correction happened along the way here, worth keeping on record even though it's now fully
resolved:** an earlier pass through this document, done from context/recollection rather than checked
against the actual code, marked Phase 1 "BUILT" alongside Phase 2 and the fourth relationship. It wasn't,
at the time. Caught the same day while responding to the author's own doubt ("I think there was a lot of
unresolved stuff from the big design we did today"), and verified two ways rather than re-checked by
feel: `grep` across `FinancePatternSaveConfirmation.cs` for `hasPredecessor`/`hasSuccessor`/
`PlanTouchesChainBoundary` found every match inside `RunForPlan` (the `EarMarkPattern`-editing path)
only — the `FinancialPattern`-editing path hardcoded `PlanTouchesChainBoundary = false` and never computed
a predecessor/successor at all; and `git show --stat` on the commit that landed this document plus Item
G's own fix showed `RestructureFactory.cs` gained exactly one method that day (`FindCurrentPlan`, ~28
lines) — not the `ExtendStart`/`ExtendUntil`/`CascadeForward` trio, which existed only for `EarMarkPattern`
chains at that point. Corrected here and in memory, and THEN actually built for real the same session —
see "What's built" below for the real account. Worth naming the pattern this slip fits, not just the
one-off: a design being fully decided (as Phase 1's own rules genuinely were, all three rounds below) is
not evidence it was implemented — "settled" and "built" are different claims, and conflating them is an
easy mistake once a structurally similar phase (Phase 2) really has been built.

**Sequencing decision (author, 2026-08-16):** this needs answering before returning to 25's own
still-open "avoid forcing consolidation" thread — three phases, in order:
1. **This document, `FinancialPattern` first** — rules for a change to one segment of a break-off/renewal
   chain cascading to (or needing reconciliation against) the segments that continue it. **BUILT
   2026-08-17**, UI included — see the correction above for why this took two passes, and "What's built"
   below for what actually landed.
2. **The same question for `EarMarkPattern` chains** (`RestructureFactory`'s own same-`finance_id`
   sequential segments), combined with the fourth relationship (the cross-boundary cascade) — its own
   section below. **BUILT 2026-08-17**, UI included.
3. **Then, and only then, back to [25](25-editing-patterns-with-history.md)'s own Item F** — cascading a
   change to a `FinancialPattern` onto the `EarMarkPattern`s that fund it (the "avoid forcing
   consolidation for a recurrence-shape change" thread, and the paycheck-association thread, both
   parked mid-conversation). **Asked 2026-08-17, once 1/2 were done — declined for now ("keep forcing
   consolidation"), not approved. Nothing built; the proposal stays on record for later.**

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
| `Priority`, `Mandatory`, `Description` | **STALE ROW, corrected 2026-08-17 — found while grounding for Phase 1's own implementation, not while reviewing the doc idly:** this said "no chain question, ever" (round 2's own retraction of round 1), but the "fourth relationship" section's own round 3 (below, same day) explicitly reopened it: **yes, a chain question too — cascade forward or just this segment, mirroring Amount/shape's own mechanism, but defaulting to "just this segment" instead** (the one place these fields DON'T mirror Amount/shape — nothing about today's actual behavior changes for anyone who accepts the default). No warning needed on this row either direction — neither choice has a real downside for these three fields. This table was written as part of round 3 itself, but round 3 kept going past this point (see the "fourth relationship" section) and the table here was never updated to match its own document's later conclusion. |

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

**The edge case flagged above — SETTLED, and now BUILT for the `EarMarkPattern`-chain side, 2026-08-16
(`RestructureFactory.ExtendUntil`/`ExtendStart`, `RecurrenceRule.WithStart` added alongside the existing
`WithUntil`).** "Keep it linked" pushed far enough that the neighbor would need to shrink past its own
boundary (zero or negative length) — possible in either direction, `Start` reaching back past the
predecessor's own `Start`, or `Until` reaching forward past the successor's own `Until` — resolves to
**absorption**: the neighbor is deleted outright, and the pattern being saved simply expands to cover the
range it used to occupy, overwriting whatever values were there. Deliberately not merged or reconciled —
the neighbor's own fields (`Amount`, shape, `Description`, everything) are gone, not blended.
**Deliberately reframed as a feature, not just a handled edge case (author):** this gives a user a real
way to consolidate a chain that's become too fragmented over time, by deliberately extending one segment
far enough to absorb its neighbors. `StartingAllocation` carries forward from every absorbed segment —
my own addition while building this, not something explicitly discussed, grounded directly in the
already-standing "already-realized jar money is never just dropped" principle (planning/26).

**A real correction to what this section said before, found while actually building it — worth being
honest about rather than quietly fixing:** "its own `EarMarkPattern`(s) go with it — `ManualEarmark`s...
tied to the absorbed segment are simply deleted" is **not accurate for this, `EarMarkPattern`-chain
level of absorption.** That description is correct at the `FinancialPattern` level (absorbing a whole
bill/goal segment genuinely orphans everything under its own now-gone `finance_id`, `ManualEarmark`s
included) — but at the `EarMarkPattern` level, the `finance_id` never changes, and the absorbing
segment's own new span covers the *union* of what both segments covered before, so a `ManualEarmark`
dated anywhere in the absorbed segment's old range is still covered by the survivor afterward. Nothing
is orphaned by absorption itself. What *is* correctly lost is `RecurrenceRule.ExcludedDates` — those live
directly on the absorbed row, not a separate record, so they're gone once that row is (as they should
be — an excluded date on a segment no longer being funded that way isn't meaningful to keep). Manual
earmarks only become genuinely orphaned by **"let the chain break"** opening a real gap — a different
scenario from absorb, already correctly described further up this document.

**Composes with the row mechanism above without any new machinery — checked, not assumed:** "stay
linked" already carries the row's default selection and already gets its warning drawn from whichever
consequence is real for the *current* edit, not a fixed script — so when "stay linked" escalates from
"nudge a boundary" to "absorb and delete a segment," the same warning slot just carries a more severe
message, still attached to the same (still-default) option. Nothing about the confirmation page itself
needs to change shape for this case.

**Absorption is not bounded to a single neighbor — confirmed by the author (round 3 of the small
questions) and now built that way**, walking as far through the chain as the new boundary reaches, one
absorbed segment at a time, stopping at the first one it doesn't fully reach.

**Warning content still not fully built** (real rows exist now, see below, but the specific wording
doesn't yet meet this bar): must name the concrete consequence, not a generic notice — which pattern is
being absorbed, its date range, how many manual earmarks (for the "let it break" case specifically) and
skipped dates go with it. Same "show the consequence, not just a yes/no" precedent [25](25-editing-patterns-with-history.md)'s
own Item E already established for orphaned data.

**What's built vs. what's still needed — updated 2026-08-16, the UI wiring now exists too:**
- **Domain, done and tested:** `RestructureFactory.ExtendUntil`/`ExtendStart` (boundary resolution,
  absorb included, 10 tests) and `RestructureFactory.CascadeForward` (the Amount/shape default, 4 tests).
- **The orchestrator extension, done and tested:** `FinancePatternSaveConfirmation` gained a second
  constructor — `(EarMarkPattern proposedPlan, DateOnly savedStart, FinancialPattern goal,
  requestForecast, repositories)` — a genuinely separate entry point (`RunForPlan`/`PerformEarmarkSave`),
  never touching any of the `FinancialPattern`-editing logic. `savedStart` is needed as its own
  parameter, distinct from the proposed plan's own `Start`, because `EarMarkPattern`'s persistence key
  *is* `(FinanceId, Start)` — exactly the field this whole mechanism can move. Detects whether `Start`/
  `Until` touch a real neighbor (`PlanTouchesChainBoundary`) and whether `Amount`/shape can cascade
  (`PlanChangeCanCascade`), fires the same `ConfirmImplicitChanges` popup mechanism the `FinancialPattern`
  side already uses (request/answer types extended with the two new questions, not duplicated), and
  saves. **Both settled defaults are wired in and proven by tests that run with no confirmation delegate
  at all** — stay linked, cascade forward. 9 new end-to-end tests, including the composed case (a
  boundary change and a cascade both landing on the very same successor in one save — its own dates move
  from the boundary resolution, its own amount changes from the cascade, one final row, not two
  conflicting writes) and the specific bug this design has to avoid (an amount-only edit, no `Start`/
  `Until` change, must update the existing row in place — the "does the key actually change" check runs
  *after* boundary resolution is known, never assumed up front).
- **A real bug, found while grounding the UI-wiring work below and FIXED the same pass, not just
  flagged:** `RunForPlan`'s own `hasPredecessor`/`hasSuccessor` originally compared only `Start` against
  `saved` — with no check for whether the "neighbor" actually forms a sequential chain versus being a
  genuinely concurrent, overlapping plan (F27 — e.g. the "Storage Unit Rental" seed scenario's own two
  household-partner funders). A concurrent plan with a differing `Start` satisfied the old check just as
  well as a real chain neighbor would, meaning extending `Until`/`Start` into its overlapping span could
  silently truncate or fully absorb (delete) it via `ExtendUntil`/`ExtendStart`, or have its own `Amount`
  silently overwritten via `CascadeForward` under the "cascade forward" default — a direct violation of
  this document's own settled rule that concurrent "must NOT get the same treatment as a sequential
  chain." Inert until this session's own UI wiring made `RunForPlan` reachable from a real save for the
  first time — not a regression, but newly *dangerous* the moment it became reachable, which is exactly
  why it surfaced now rather than earlier. **Fixed** by extracting `RestructureFactory.SpansOverlap`
  (the exact overlap check `FindCurrentPlan` already used, now a shared, reusable definition instead of a
  second copy) and filtering `otherPlans` by it before `hasPredecessor`/`hasSuccessor` are computed — the
  same filtered list also feeds `PerformEarmarkSave`'s own `predecessors`/`successors`, so both the *ask*
  and the *actual mutation* are protected by the one fix. 6 new regression tests (4 direct `SpansOverlap`
  cases in `RestructureFactoryTests`, 2 end-to-end in `FinancePatternSaveConfirmationEarmarkTests` proving
  a concurrent plan is untouched — and that `ConfirmImplicitChanges` is never even invoked — under both
  the boundary and cascade paths).
- **The confirmation window, done — real rows now, matching the existing minimal style:**
  `EditingHistoryConfirmationWindow` gained `ChainBoundarySection` (stay linked / let it break, plus a
  `ChainBreakWarningText` that only turns visible while "let it break" is the one currently selected —
  wired live via a shared `Checked` handler on both radios, not just computed at Save, matching the
  row-based design's own "contextual warning under the dangerous option" rule) and `CascadeSection`
  (cascade forward / just this plan). Both default to the settled system defaults. `ChoseStayLinked`/
  `ChoseCascadeForward` read back the same way the existing three properties already do. This is still
  the same deliberately-minimal, plain-WPF shape as the rest of the window (see its own header comment)
  — not the fuller styled mockup design, and not yet the generalized "every question is its own row with
  a live contextual warning" treatment applied to the *older* Item E/F sections too (they still work the
  way they always did — a static forced-notice text, no live warning toggling).
- **`EarmarkFormPanel`/`MainWindow` wiring, done:** `EarmarkFormPanel` now tracks `_loadedPlanStart` (the
  plan's own literal `Start` as loaded — distinct from `_loadedActiveStart`, which tracks `ActiveStart`
  for the isolated-starting-earmark's own unrelated bookkeeping) and passes it through the now two-
  parameter `PatternSaved` (`Action<EarMarkPattern, DateOnly>`, was `Action<EarMarkPattern>`) so the
  caller can supply `FinancePatternSaveConfirmation`'s `savedStart`. `MainWindow`'s own
  `OnEarmarkPatternSaved` constructs the confirmation, runs it, and only then refreshes/switches tabs —
  mirroring `OnExpensePatternSaved`'s own shape. `ConfirmImplicitChanges` for both save paths now shares
  one method (`ShowEditingHistoryConfirmation`) rather than two near-identical inline lambdas, since the
  window and the full set of answer fields worth reading back are the same either way.
- **One default that was never explicitly settled, flagged rather than guessed silently:** "stay linked"
  needed *some* default the same way cascading forward has one, and none was ever stated outright for it
  specifically. Defaulted to **stay linked** — the only one of the two choices that's never destructive
  on its own — as this implementation's own reasoned choice, not something ruled on. Worth a real answer,
  not just an assumption that held.
- **The `ManualEarmark` orphan cleanup, done and tested — 2026-08-17, broader than just "let it
  break":** `PerformEarmarkSave` now computes, unconditionally and before any write, the *final*
  post-save set of plans (`current` plus every other plan still standing after boundary resolution, using
  each one's adjusted shape where one applies) and deletes any `ManualEarmark` under the goal's own
  `finance_id` that no plan in that final set covers anymore (`RecurrenceRule.ActiveSpanContains`, the
  same check `ManualEarmark.Create`/`ManualEarmarkRepository.GetAll` already use to validate one). Not
  gated on `PlanTouchesChainBoundary` specifically — a **standalone plan with no chain neighbor at all**
  can orphan a manual earmark by shrinking its own span just as easily, and `PlanTouchesChainBoundary`
  never even fires for that case (nothing to ask about), so gating the cleanup on it would have missed
  the more common scenario. This was worth building now rather than deferring further: left unguarded,
  `ManualEarmarkRepository.GetAll()` re-validates every row on read and throws the moment one is no
  longer covered — a crash waiting to happen the first time a real gap (or a plain shrink) left one
  behind, not just quiet data untidiness. 4 new end-to-end tests: a chain gap actually deletes the
  stranded earmark; a standalone plan's own shrink does too, with no question ever asked; staying linked
  never deletes one that's still covered by the (possibly-adjusted) final set; absorbing a neighbor never
  deletes one inside the absorbed range either, matching this document's own already-settled "absorption
  orphans nothing" claim, now backed by a real `ManualEarmark`, not just `StartingAllocation`. **Suite:
  433 green** (330 domain + 58 scenario + 45 app, up from 429), 0 warnings.
- **The concrete-consequence wording itself, done and tested — 2026-08-17, closing the "still not built"
  gap this section used to end on:** `ImplicitChangeConfirmationRequest` gained three plain-text fields —
  `StayLinkedWarning`, `LetItBreakWarning`, `CascadeDescription` — computed by
  `BuildEarmarkConfirmationRequest` *before* the popup ever opens (so both possible answers' own
  consequences are known ahead of the user picking either one), via three new preview methods:
  `DescribeStayLinkedConsequence` dry-runs the exact same `ExtendStart`/`ExtendUntil` calls
  `PerformEarmarkSave` itself would make, returning `""` for a plain nudge (matching this document's own
  "the default option is never the dangerous one" reasoning — nothing to warn about) or a real sentence
  naming which segment(s) would be absorbed, their own date range, once the edit reaches that far.
  `DescribeLetItBreakConsequence` names whether a gap or overlap forms with the relevant neighbor
  (`RestructureFactory.SpansOverlap` decides which — reusing the same-day fix above rather than a second
  overlap check) and how many manual earmarks a gap would strand, via a dry run of the same
  `FindOrphanedManualEarmarkDates` helper the real deletion above uses, just against a *hypothetical*
  final set instead of the real one. `DescribeCascadeConsequence` names the direct edit's own date range
  and how far cascading would reach (which/how many later segments, through what date) — round 2 of the
  small questions' own explicit requirement, shown as a plain always-visible description under the
  Cascade row rather than a warning tied to one option, since neither cascade choice is destructive.
  `EditingHistoryConfirmationWindow` wires all three in: a warning slot under *each* ChainBoundary radio
  now (not just "let it break" — `StayLinkedWarningText` stays empty/hidden for a nudge, exactly the
  "escalates with the consequence" case this document already called for), toggled live by the existing
  shared `Checked` handler; `CascadeDescriptionText` always shown alongside the Cascade row. 6 new
  end-to-end tests capturing the real `ImplicitChangeConfirmationRequest` and asserting on its own warning
  text (not just the eventual saved state) — a nudge gets no warning; an absorb names the segment and its
  range; a break names a gap with its own start/end dates; a break names an overlap instead of a gap when
  the new span reaches into the neighbor without fully absorbing it; a break names the manual-earmark
  count and date it would strand; a cascade names its own range and reach. **Suite: 439 green** (330
  domain + 58 scenario + 51 app, up from 433), 0 warnings.

**Confirmed: everything in this section applies equally to `EarMarkPattern` chains (Phase 2) —
author, 2026-08-16.**

## A fourth relationship, surfaced 2026-08-16, BUILT 2026-08-17 — now slotted immediately after Phase 1, combined with Phase 2

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
already-existing successor — that part was open at the time; **now BUILT, see below.**

**The harder part, at the time genuinely unanswered — now BUILT, 2026-08-17 (see "What's built" further
down):** even with that fixed, nothing let an edit made *after* a break had already happened —
restructuring Rent's old segment's own plan, skipping a date on it — reach the new segment's own,
already-running plan. Doing so can't reuse this document's own "absorb" mechanism directly:
`EarMarkPattern.Create` requires `FinanceId` to match its own goal exactly, so one `EarMarkPattern` row
can never span two different `FinancialPattern`s the way "absorb" lets it span two segments of one.
Turned out not to need a different shape after all — see below for how `CascadeForward` itself already
generalized.

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

**Third round, SETTLED 2026-08-16, all four** (the fourth's own intro line here originally said "needs a
precise answer, not guessed" — stale the moment the RESOLVED bullet right below it was added the same
day; fixed 2026-08-17, found while re-grounding for this section's own implementation pass):
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

**What's built — 2026-08-17, closing out this section:** unified into the *same* `PlanChangeCanCascade`/
`CascadeSection` mechanism Phase 2 already built, rather than a separate new question, since the two
triggers turn out mutually exclusive by construction — `RunForPlan` only ever computes the cross-boundary
target when this plan's own same-`finance_id` chain has *no* successor (`!hasSuccessor`), matching
"forward-only" and the general "cascade through your own chain first, only then reach further" reading of
that rule. Composes the two lookups this section named from the start:
`BreakOffFactory.FindSuccessor(goal, allFinancialPatterns)` finds the far side's own `FinancialPattern`
(if the underlying bill has broken off at all), then `RestructureFactory.FindCurrentPlan` on that side's
own `EarMarkPatternsFor` finds its "current" plan — correctly `null`, and so correctly offering no
cascade, whenever the far side has no plan yet or its own plans are a genuinely concurrent set (no special
leniency, per round 2's own settled answer). No new domain mechanism needed for the cascade itself —
`RestructureFactory.CascadeForward` already generalizes: it takes exactly one `goal` for whatever plans
it's given, so pointing it at `[farSideCurrentPlan]` with the far side's OWN goal (not the one being
edited) works correctly unchanged, since `EarMarkPattern.Create`'s own validation just needs the `FinanceId`
and `goal` it's handed to actually match — which they now do, on purpose. The one real difference from the
same-`finance_id` case: the result can't go through the existing `toSave` dictionary (keyed for the edited
plan's own `finance_id` — a coincidental `Start` collision against an unrelated goal is a real, if
unlikely, risk) — saved directly instead, in its own clearly-commented branch.
`ImplicitChangeConfirmationRequest.CascadeDescription` now names which kind of cascade is on offer either
way — "later segments" for the same-`finance_id` case, or the far side's own label ("this bill has since
moved to a newer segment...") for the cross-boundary one — since `DescribeCascadeConsequence` takes both
possible targets and only one is ever non-empty. 6 new end-to-end tests in a new file,
`FinancePatternSaveConfirmationCrossBoundaryTests` (a genuinely different scenario — two `FinancialPattern`s,
not one — matching this project's own existing split-by-scenario test-file convention): the cascade itself
fires and applies; declining it leaves the far side untouched; no cascade is offered when the far side has
no plan yet, or when its own plans are concurrent; a same-`finance_id` successor takes priority when both
exist simultaneously (proving the mutual-exclusion reasoning above, not just asserting it); the description
names the successor segment correctly. **Suite: 445 green** (330 domain + 58 scenario + 57 app, up from
439), 0 warnings. Verified the app still launches clean (same baseline check as every other pass this
session).

**Still not built:** everything this document's own Phase 3 (below) already covers — this fourth
relationship's own scope stops at the cascade mechanism itself, not planning/25's separately-parked
consolidation-avoidance/paycheck-association threads.

## What Phase 1 actually built — 2026-08-17

Mirrors Phase 2's own shape closely, as expected, with two genuine differences called out below.
`BreakOffFactory` (the existing home for `FinancialPattern`-chain logic — `FindPredecessor`/
`FindSuccessor`/`FindCurrentSegment` already lived there) gained `ExtendStart`/`ExtendUntil` (boundary
resolution, absorb included — pure functions, no repository access, mirroring `RestructureFactory`'s own
`ChainBoundaryResult` shape as `FinancialChainBoundaryResult`), `CascadeForward` (Amount/shape), a new
`CascadeTrivialFieldsForward` (Priority/Mandatory/Description/AutoRenew — no `EarMarkPattern` equivalent,
since that class has none of these fields), and `SpansOverlap` (the same F27-style concurrent-pattern
guard Phase 2 needed, since nothing stops two `FinancialPattern`s from sharing a Source and overlapping
by mistake either). `FinancialPattern` itself gained `WithStart` alongside its existing `WithUntil`.
`FinancePatternSaveConfirmation`'s own `FinancialPattern`-editing path (`Run`/`DetermineConditions`) —
NOT a fresh parallel entry point the way Phase 2 got, since Phase 1 has to weave into the already-complex,
already-tested Items A-G machinery — gained `DetermineChainConditionsIfApplicable`/
`PerformChainChangesIfApplicable`, the new `TouchesChainBoundary`/`ChangeCanCascade`/
`TrivialFieldsCanCascade`/`SourceChangeWarning` conditions, and reuses the SAME `UserChoseStayLinked`/
`UserChoseCascadeForward` answer fields Phase 2 already has (the two modes never both run on one
instance, so nothing forced these apart) plus a new, Phase-1-only `UserChoseCascadeTrivialFields`
(defaulting to **false** — "just this segment," the one place this mechanism doesn't mirror Amount/
shape's own "cascade forward" default, per round 3's own settled answer).
`EditingHistoryConfirmationWindow` reuses its existing `ChainBoundarySection`/`CascadeSection` for
either chain type (shown whenever EITHER `PlanTouchesChainBoundary`/`TouchesChainBoundary` or
`PlanChangeCanCascade`/`ChangeCanCascade` is true — never both on one request) and gained a new
`TrivialFieldsCascadeSection` (its own default flipped to "just this segment") and a plain
`SourceChangeWarningText` block (announced, not asked — no radio, the edit proceeds either way).

**Two genuine differences from Phase 2, not just a mechanical port:**
- **Absorbing a whole `FinancialPattern` segment genuinely orphans everything under its own now-gone
  `FinanceId`** — its own `EarMarkPattern` chain (however many segments Phase 2's own restructuring left
  it with) and every `ManualEarmark` tied to it, unlike absorbing an `EarMarkPattern` segment (which
  orphans nothing — see the "confirmation page" section above). `PerformChainChangesIfApplicable` handles
  this by composing two already-existing repository calls — `EarMarkPatternRepository.Delete(financeId)`
  (already cascades to `ManualEarmarks`, its own established two-table `DELETE` pair) then
  `FinancialPatternRepository.Delete(financeId)` — the same order `DeleteByTransferId` already uses for
  its own three-table cascade. `StayLinkedWarningText`'s own content names this plainly when it applies:
  which segment, its date range, and how many of its own `EarMarkPattern`s go with it.
- **A deliberate scope limit, found while wiring this in, not decided in advance:** `TouchesChainBoundary`/
  `ChangeCanCascade`/`TrivialFieldsCanCascade` are gated on `!IsChangeCritical`. When a Critical edit's
  own default answer is "break off" (Item C), `_proposedPattern` is never actually saved under
  `_financeId` verbatim — a truncated original plus a brand-new successor get saved instead — so whatever
  Start/Until the user typed (the one that might reach into a predecessor/successor) never lands on the
  existing chain the way this mechanism assumes. The "correct it everywhere" answer WOULD make Phase 1's
  own question valid too, but that answer isn't known until Item C's own confirmation fires, and this
  method runs before it — composing "a chain question that only sometimes matters, depending on a
  DIFFERENT question's own answer on the same page" was judged too easy to get subtly wrong to build
  under this session's own time pressure. Narrowed to the always-safe case instead. **Worth a real answer
  from the author before widening it, not a guess** — this is the one place Phase 1 is less complete than
  its own settled rules technically call for.

**A real bug found and fixed along the way, affecting Phase 2 too, not just this new code:**
`ExtendStart`'s own outer filter (`otherPlans.Where(plan => plan.DatePattern.Start < current.DatePattern.Start)`)
used a strict `<` — but `current.DatePattern.Start` here is already the NEW, proposed Start (every real
caller passes it as both `current` and `newStart`), so a predecessor landing EXACTLY on the new Start
failed this filter and was silently skipped entirely, never even reaching the loop's own (correct)
`newStart <= plan.DatePattern.Start` absorb check. Found while writing Phase 1's own app-layer test for
exactly this boundary case — a scenario the EXISTING domain-level tests (for both `RestructureFactory`
and, initially, this new `BreakOffFactory` code) never actually hit, since they passed `current` with a
Start DIFFERENT from `newStart`, not matching how the real orchestrator actually calls this. Fixed in
BOTH `RestructureFactory.ExtendStart` (Phase 2) and `BreakOffFactory.ExtendStart` (Phase 1) — the outer
filter now uses `<=`, matching the loop's own absorb condition. New regression tests added at both the
domain level (one per file, deliberately modeling the real calling convention: `current`'s own Start
already equal to `newStart`) and the app layer (the test that caught it). **Worth noting for future
domain tests in this codebase generally:** a test that doesn't mirror how the real caller actually
constructs its arguments can pass while missing a real bug — matching the calling convention, not just
picking "some valid combination of parameters," is what caught this.

**Tests:** 13 new domain tests (`BreakOffFactoryTests`: `ExtendStart`/`ExtendUntil`/`CascadeForward`/
`CascadeTrivialFieldsForward`/`SpansOverlap`, absorb-multi-hop, the boundary-bug regression) plus 2 more
in `RestructureFactoryTests` (`SpansOverlap`-equivalent already existed there from Phase 2; only the
boundary-bug regression was new), and 12 new end-to-end tests in a new file,
`FinancePatternSaveConfirmationChainTests` (nudge, absorb-with-cascading-delete, let-it-break, no-chain-
neighbor no-op, the `!IsChangeCritical` gate proven directly, the concurrent-pattern guard, Amount
cascade forward/declined, trivial-fields cascade default/chosen, the Source warning, cancel). **Suite:
470 green** (343 domain + 58 scenario + 69 app, up from 445), 0 warnings on a clean rebuild. Verified the
app still launches clean (same baseline check as every other pass this session).

## Not started

- **Phase 3 — resuming [25](25-editing-patterns-with-history.md)'s own Item F** ("avoid forcing
  consolidation" for a recurrence-shape change, and the paycheck-association/"loose association"
  cascade) — asked about directly 2026-08-17 once Phase 2 and the fourth relationship were done; the
  author's answer was **"keep forcing consolidation for now"** — declined, not approved, so this stays
  not-started. See planning/25's own Item F closing note for the full proposal, left written up as-is for
  a future session. The paycheck-association thread is even earlier-stage than that — still just
  "detection is a live date-range comparison, genuinely new machinery," never actually proposed as a
  concrete mechanism — so it would need its own round of grounding before there's anything to ask
  approval for, not just an implementation green light.
