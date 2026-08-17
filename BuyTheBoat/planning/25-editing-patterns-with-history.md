# 25 — Editing a finance pattern with existing history

**Status: OPEN — started 2026-08-11.** A new problem surfaced outside the "Adjusting the Plan" phase's
original 0–6 stage sequence (all design-complete, see [13](13-adjusting-the-plan-charter.md)) — during
the same UI-implementation stretch that produced [21](21-form-architecture.md)/[22](22-plan-health-state.md)/[23](23-form-behavior.md),
the same way those three did. Items A–F below are all SETTLED, including F's own feasibility test, and
the Trivial/Critical/Concerning field categorization is SETTLED — see "Final field categorization"
below. Only the UI-depth work (popup content/layout, the specific strategies) remains — see "Still
open" at the bottom. **Item F's own in-place consolidation (2026-08-13) and amount-only scaling
(2026-08-14) mechanisms — two of the four keep-separate/consolidate combinations — are now BUILT too**
— see Item F's own updated closing note for exactly which two remain open. **Item G, added 2026-08-13,
is now BUILT too (2026-08-14), for the single-plan break-off case** — see its own closing note for the
multi-plan scoping decision and what's still just placeholder popup content.

**The problem in one paragraph:** editing a `FinancialPattern` through the ordinary Expense form can,
today, retroactively rewrite already-occurred history — nothing before the as-of date is a locked
ledger ([13a, W4](13a-linearity-workaround-registry.md#w4--compute-per-day-instead-of-storing--in-use-structural)).
A new assumption locks that down by default; a new standing UI rule requires confirmation before it
happens anyway; and the mechanism that satisfies both, once confirmed, turns out to mostly already
exist.

**Scope note, found 2026-08-14 — read before assuming this document's protection is broader than it
is:** everything below is about editing a `FinancialPattern` (the goal/bill itself) through the Expense
form. Editing the `EarMarkPattern` (the savings plan *funding* that goal) through the Earmark form is a
completely separate save path, with none of Items B–G's machinery anywhere in it — Items B-G are about
protecting a `FinancialPattern`'s own already-occurred history, and nothing built for a `EarMarkPattern`
edit asks that same question, under either save path described below.

**Updated 2026-08-16 — the save path itself changed, but not in a way that touches this gap.**
`EarmarkFormPanel.SaveSavingsPlan` → `MainWindow`'s `EarmarkForm.PatternSaved` handler is no longer a
bare `_earMarkPatterns.Save(pattern)` — it now goes through `FinancePatternSaveConfirmation`'s own
second, EarMarkPattern-editing constructor (see [27](27-editing-within-a-patterns-chain.md), BUILT). But
that mechanism's two questions ("stay linked or break," "cascade forward or not") are scoped to a plan's
relationship with its own chain *siblings* (other `EarMarkPattern`s sharing the same `finance_id`) — they
only fire when `hasPredecessor`/`hasSuccessor` is true. They are not a B–G-style "you're about to
retroactively rewrite already-occurred history" guard, and were never designed as one. So for exactly the
scenario this note originally called out — a single, unchained plan (no predecessor, no successor) —
**the gap described below is completely unchanged**: an `Amount` edit still re-rates the plan's *entire*
history the next time the forecast rebuilds, silently, the instant Save is clicked, with no question asked
at all (`PlanChangeCanCascade` stays false with no successor to cascade to). Only a plan that's *part of a
chain* now gets asked anything, and even then the question is about its neighbors going forward, not about
its own past.

Changing a plan's own `Amount` retroactively re-rates its *entire* history the
next time the forecast rebuilds, silently, the instant Save is clicked, no matter how much real history
sits behind it. Confirmed with real numbers in
`EarmarkFormLivePreviewTests.Editing_a_savings_plans_own_amount_retroactively_rerates_its_whole_history_with_no_protection`:
a plan funding a $100/month bill, edited to $120/month with 6 months of history behind it, silently
accumulates a $120 surplus with no warning and no live-preview indication either (that same test file's
own header comment covers why the live preview structurally can't show it). **Answered by the author,
same day: a real gap, not an intentional boundary — but deliberately parked for a future iterative
development cycle, after this document's own work is fully handled. See
[26](26-editing-an-earmark-pattern.md) for the author's own stated direction, recorded so it isn't lost
before that cycle starts — nothing there is designed yet.**

### Reading list
1. [03-assumptions-glossary.md, Chapter 20](../../03-assumptions-glossary.md#chapter-20-editing-a-finance-pattern-with-existing-history) — the new assumption itself, `1.2.3.10.a5`.
2. [16 — Stage 3: break off](16-stage3-break-off.md), **item 4** specifically (see the terminology
   note below) — `BreakOffFactory.BreakOff`, the mechanism this doc reuses almost unmodified.
3. [17 — Stage 4](17-stage4-allocation-only-changes.md), F27/F28 — why `RestructureFactory` (same
   `finance_id`, no jar hand-off) is the *wrong* shape for this problem, unlike item 4's shape.
4. [23 — form behavior](23-form-behavior.md) — the standing rules ("Downward-only editing,"
   Forced/Suggested) and item E, whose own "Deferred" section anticipated this work bundling in here.

---

## Terminology note

The author's own general sense of **"breaking off"** — stopping a pattern early with the intention of
continuing it as a new pattern — is the *same thing* [16](16-stage3-break-off.md) already built as
**item 4** (`BreakOffFactory`, user-facing name "Change starting on a date," a.k.a. **B14** in the
[action catalog](13b-user-action-catalog.md)). It's easy to conflate with **item 16** in that same
document — a *different* mechanism (`PatternTruncation`, "I'm cancelling Netflix next month," no
continuation at all) that happens to share its number with the file's own name. This doc always cites
"item 4" or "`BreakOffFactory`" explicitly to keep the two apart.

---

## Item A — The new assumption · SETTLED 2026-08-11

`1.2.3.10.a5`, recorded in [03, Chapter 20](../../03-assumptions-glossary.md#chapter-20-editing-a-finance-pattern-with-existing-history):
once a `FinancialPattern` has an expected transaction dated on or before the as-of date, only its
`end_date` may change without triggering item C below.

New assumptions get a clearly-separated section in `03` (not mixed into the verbatim a01/a02/a03
reconstruction) and, for now, **no row in [05](05-original-structure-restructure.md)'s divergence
registry** — that registry is regenerated from real `DIVERGENCE(...)` code tags, so it earns a row
once code actually lands, the same way `ActiveFrom`/`AutoRenew` got theirs. Also **not yet a node in
[06](../../06-assumption-dependency-graph.md)** — that graph is mechanically regenerated from the
original `.uxf` chart, and this assumption has no chart box to regenerate from; a hand-added node
would be silently dropped the next regeneration.

**Note (2026-08-11): `03`'s own copy of Chapter 20 initially wasn't persisting on disk** — the first
four attempts were silently reverted, because the file was open elsewhere at the time. Confirmed
saved on the fifth attempt, after it was closed. Both `03` and this doc now agree.

**Scope, refined 2026-08-11 (author).** Originally worded as "only `end_date`" — narrowed once it
became clear most fields have no bearing on what this assumption actually protects. Restricted:
`start_date`, `amount`, and the recurrence shape (frequency/interval/by-day) — these are the fields
that touch `ExpectedTransaction` identity/amount (`3.13.7.a1`/`3.13.7.a2`, `expected_amount` drawn
from `FinancialPattern.Amount`) or the linked `EarMarkPattern`'s span (`3.11.2.a2`). **Never
restricted, regardless of history:** `end_date`, `description`, `source`, `priority`, and
`mandatory`/skippable — [B7/B8/B9 in the action catalog](13b-user-action-catalog.md) already settled
each of these as a plain, uniform, no-retroactive-effect edit, and nothing ties any of them to
anything this assumption exists to protect. (`Mandatory` specifically: the premise that it gates
whether an outflow gets earmarked at all was true of the old, retired `BillAccrualAt` ramp — post
Stage-1-revision, every outflow gets a plan regardless of mandatory status;
[`(positive-implicit)` in 05](05-original-structure-restructure.md#divergence-sites) confirms this
directly. Toggling it only changes deallocation drain order going forward.)

## Item B — The new UI design rule · SETTLED 2026-08-11

If a form change would alter, delete, or remake an expected transaction dated on or before the as-of
date, the user must be given a confirmation before it happens — never silent. This resolves
[23](23-form-behavior.md)'s own parked item, *"the warnings/confirmation mechanism for implicit
downward changes"* — landed bundled with item E, exactly as that doc's own deferred note guessed it
would.

## Item C — The mechanism · SETTLED 2026-08-11

**This is item 4 (`BreakOffFactory.BreakOff`), reused almost as-is — not a new factory.** Confirmed: a
`FinancialPattern` edit that would violate `1.2.3.10.a5` always needs a genuinely new `finance_id`
(never `RestructureFactory`'s same-`finance_id` shape — F27/F28 don't apply here, since it's the
*bill itself* changing, not just how it's funded). Item 4's own earmark-pattern split (4-C) and jar
hand-off (4-B, `StartingAllocation`) come along for free the moment `BreakOffFactory.BreakOff` is the
thing actually being called.

What's new relative to item 4's existing, already-built *deliberate* flow:

- **Trigger:** detected from a plain-edit diff against `1.2.3.10.a5`, not only reachable by
  deliberately pressing "Change starting on a date."
- **Cut date: pinned to today, literally** — SETTLED 2026-08-11 (author), resolving the two-reading
  ambiguity this bullet used to carry ("or the day after the latest already-occurred expected
  transaction" is not the rule). Never user-chosen the way item 4's own deliberate confirm screen
  already allows. No extra decision for the user to make in this flow.
  **Known, deliberately deferred sharp edge:** if the pattern's own `Start` IS today (its only past
  occurrence), the cut date equals `Start`, and `BreakOffFactory.BreakOff` throws — it requires the
  cut strictly after `Start`. Left unhandled until it actually comes up (author).
- **The confirmation offers a real choice**, once a past expected transaction is actually touched —
  this is [16, F24](16-stage3-break-off.md#item-6--the-taxonomy--settled-2026-07-29)'s own
  correction-vs-change distinction, just exposed at edit time instead of requiring the user to already
  know to reach for item 4's own button ahead of time:
  - **Correct it everywhere** — an ordinary plain edit, same `finance_id`, applies retroactively.
    Nothing new; today's existing edit form, unchanged.
  - **Change only from here on** — item 4's `BreakOffFactory.BreakOff`, cut pinned to today, exactly
    as described above.

**TODO — keep this compatible with renewal.** `BreakOffFactory.Renew`
([15](15-stage2-pattern-lifetime.md#ongoing--renewal-resolved-and-built-author-2026-07-29)) already
wraps plain `BreakOff` for the periodic-renewal case. Whatever thin wrapper implements this trigger's
pinned-cut-date call into `BreakOff` should sit alongside `Renew`, not fork away from it — both are
callers of the same underlying factory, and `Renew` should keep working unmodified once this lands.

## Item D — Data inside the gap · SETTLED 2026-08-11

If the new pattern's own `Start` lands later than the (pinned-to-today) cut date, a gap opens between
them. Any `ManualEarmark`, or the current earmark pattern's own span, already committed to a date
inside that gap **folds into `StartingAllocation`** — the same mechanism item 4-B already uses for the
jar's balance at the cut, no new machinery. The money keeps its value; it loses its own specific date.

## Item E — Retroactive correction is always offered, even when it orphans data · SETTLED 2026-08-11

**Ruling (author): the "correct it everywhere" choice from Item C is never removed from the menu, not
even when it would orphan already-committed data.** The system's job is to show the user what would be
destroyed and let them decide — never to decide for them by making the option unavailable. This is
Philosophy 1 applied literally to the choice itself, not just to the outcome.

**What "orphaned" resolves to under this path.** Choosing retroactive correction keeps the *same*
`finance_id` on both sides — no successor pattern is created, so there's no separate pattern to hand a
balance to the way Item D's break-off path does. Instead, the *existing* `EarMarkPattern`'s own active
span narrows in place (to stay inside the finance pattern's new, narrower one, per `3.11.2.a2`), and
anything that was inside the now-excluded span — a `ManualEarmark`, or the pattern's own earlier
accrued history — **folds into that same `EarMarkPattern`'s own `StartingAllocation`**, bumped up to
absorb it. Same absorption principle as Item D, applied in place rather than across a break-off's cut.

**The confirmation shows the consequence, not just a yes/no.** Concretely: when retroactive correction
would orphan something, the popup names what's being folded in (amount and, ideally, what it was) so
the user is deciding with the real stakes visible — not a bare "are you sure?"

## Item F — Multiple existing EarMarkPatterns ("consolidation") · SETTLED 2026-08-11

A `FinancialPattern` can already have more than one `EarMarkPattern` sharing its `finance_id`
(F27's relaxation of `3.11.1.a1`) — sequentially, from an earlier `Restructure`/break-off, or
concurrently (item 9's multiple-funders case). Neither Item C nor Item D assumed there'd only ever be
one, but they were never stress-tested against several at once either. The author's own example: a
finance pattern breaks off to dramatically change its dates or amount, and multiple `EarMarkPattern`s
already exist for it past the point where the break happens.

**Ruling (author): the confirmation checks whether keeping the existing plans separate is actually
workable.** If it is, it **asks** which the user wants (keep them distinct, going forward under the
new `finance_id`, or consolidate into one); if it isn't, it **warns** that consolidation is happening
rather than offering a choice that isn't real. One rule, regardless of whether the multiple plans got
there sequentially or concurrently — not a different rule per origin.

**What's already covered, no new mechanism needed:** if consolidation *does* happen (by choice or by
necessity), Item C already says the successor is always freshly proposed regardless of how many old
plans preceded it (`AllocationPlanProposer.Propose` takes no predecessor as input at all), and the
jar-balance handoff already sums across every plan sharing a `finance_id`
([F27/F34](17-stage4-allocation-only-changes.md#finding-f27--items-8-9-and-22-are-one-relaxation-not-three)).
So "consolidate" costs nothing new mechanically — it's Item C run once, same as always.

**The feasibility test · SETTLED 2026-08-11 (author).** ~~Earlier hypothesis (whether exactly one
`EarMarkPattern` is active as of today, sequential vs. concurrent) — superseded, wrong axis.~~ The
real test: **does the `FinancialPattern`'s change actually shift which dates its `ExpectedTransaction`s
occur on** — not how many `EarMarkPattern`s exist in the Savings Plan or how they arose. Concrete
rules:

| Change | Feasible to keep separate? | Why |
|---|---|---|
| Only one `EarMarkPattern` in the Savings Plan | Feasible (nothing to decide) | Nothing to consolidate |
| Recurrence shape changed (occurrence dates move) | **Not feasible — always consolidate** | Every surviving `EarMarkPattern` would need its own timing re-derived against the new dates; one fresh proposal (Item C) does this correctly, individually patching several doesn't |
| Only a Trivial field changed (description/source/priority/mandatory) | Feasible | Consistency check, not a new case — Trivial fields never reach Item F at all, since they never trigger the Critical path that puts it in play |
| `amount` changed, no date shift | Feasible — **BUILT 2026-08-14** | Scale every surviving `EarMarkPattern`'s own amount by the same ratio the bill's amount changed by, with the user's permission in the confirmation popup — see this item's own closing note below for the mechanism |
| `end_date` changed | Feasible | `Until` only ever truncates/extends the far end — it never moves a surviving occurrence's own date |
| `start_date` changed | Feasible | Same logic as `end_date` — `start_date` only truncates/adds at its own boundary, never shifts a surviving occurrence's date. Independent of Item E's own orphaned-data question, which is about something else (whether *history* is preserved, not whether keeping *multiple plans* is workable) |

**Combining rule (not yet independently confirmed, follows directly from the table):** if the set of
changed fields includes a recurrence-shape change, infeasible regardless of what else also changed;
otherwise, feasible.

**Scope, settled 2026-08-11 (author): Item F applies to both Item E paths, not break-off only.** The
table and wording above were written with break-off in mind (the successor's own new `finance_id`) —
but the question "is it still workable to keep multiple `EarMarkPattern`s separate" applies just as
much when the user instead picks Item E's "correct it everywhere." What "consolidate" resolves to
differs by path, though: for break-off, it's the shape already described above (one freshly-proposed
successor, Item C's existing rule). For a retroactive correction — same `finance_id` throughout, no
successor pattern at all — consolidating instead means folding the existing `EarMarkPattern`s together
**in place**, under that same `finance_id`.

**The in-place shape · BUILT 2026-08-13** (`EarmarkConsolidation.Consolidate`,
`redesign/MyMoneyForecast/src/MyMoneyForecast.Domain/EarmarkConsolidation.cs`, wired into
`FinancePatternSaveConfirmation.ConsolidateSurvivingPlansIfNeeded`). Author-derived formula: every
surviving `EarMarkPattern` sharing the `finance_id` is folded into ONE, spanning from the earliest
surviving plan's own start through the goal's own end. Call the amount already banked before that
start A, the total the goal will consume in the window B, and the target balance right after the
window's last release washes out C — conservation gives contributions = B + C − A. C is not a free
choice: `3.13.5.4.a1`'s reset-at-release rule means the milestone resets to zero the instant a release
lands, so "fully funded" means exactly C = 0, and the formula collapses to B − A. A, in turn, needs no
day-by-day forecast read — summing every surviving plan's own `StartingAllocation` plus any
`ManualEarmark` already dated on or before the window's end (the same fields `GoalShortfall.
AmountAllocatedByDueDate` already sums) is the complete, correct accounting, with no double-count
against the scheduled contributions being torn down and replaced. Paces to a single clear income
stream the same way `AllocationPlanProposer`'s own paced shape does (`Total ÷ paydayCount`), or spreads
evenly across the goal's own occurrences otherwise. 8 domain tests (now 8 — one strengthened, see
below), 3 App-layer tests.

**Bug found and fixed same-day, 2026-08-14:** A above correctly discounts a surviving plan's own
`StartingAllocation` when sizing the new consolidated plan smaller, but the new plan it built used to
leave its own `StartingAllocation` at 0 rather than carrying that money forward. Manual earmarks were
always safe (their own row, independent of which `EarMarkPattern` survives); a surviving plan's
already-banked `StartingAllocation` was not — it simply stopped being counted anywhere once that plan's
row was deleted and folded in, leaving `GoalShortfall.ShortfallAmount` short by exactly the lost amount
even though the money was still real. Found via a save-then-rebuild-the-forecast test
(`FinancePatternSaveConfirmationTests`, prompted by the author's own question about testing whether a
save actually resolves what it was meant to fix) and fixed by setting the consolidated plan's own
`StartingAllocation` to `request.SurvivingPlans.Sum(p => p.StartingAllocation)` — pinned by a new
assertion on the existing `Every_surviving_plans_own_StartingAllocation_reduces_the_total_needed` domain
test, plus the App-layer test that first found it, now asserting the fix instead of documenting the bug.

**The `amount`-changed, keep-them-separate shape · BUILT 2026-08-14** (`EarmarkScaling.Scale`,
`redesign/MyMoneyForecast/src/MyMoneyForecast.Domain/EarmarkScaling.cs`, wired into
`FinancePatternSaveConfirmation.ScaleSurvivingPlansIfNeeded`). The table's own `amount`-changed row,
finished: every surviving `EarMarkPattern`'s own `Amount` scales by the same ratio the goal's own
`Amount` just changed by — `DatePattern`, `StartingAllocation`, and `finance_id` all carried over
untouched. Deliberately no boundary work at all: this row is exactly the case where nothing about any
plan's own dates moves, so each scaled plan saves back under its own original `(finance_id, Start)` —
an in-place update, not the delete-and-recreate the in-place *consolidation* shape above needs.
`StartingAllocation` is never scaled — already-realized money, not an ongoing rate, so a bigger or
smaller bill shouldn't retroactively change what already accumulated. Not choosing to scale is a
genuine, correct no-op for this same row, not a gap — the plans are already valid exactly as they are.
Gated on `IsAmountOnlyChange`, not just the user's own scale choice, so the mechanism can't be reached
outside the one row it was designed for. 6 domain tests, 1 new App-layer test (plus the existing
kept-separate-without-scaling test, strengthened to assert both plans' amounts stay untouched).

**Still open, deliberately not attempted:** keeping the plans separate at all on the *break-off* side
(with or without scaling — a materially different, harder mechanism there, since each surviving plan
would need its own successor reborn under the new `finance_id`, splitting the jar balance across them);
and keeping them separate on the *retroactive-correction* side for a `start_date` change specifically
(this table's own `start_date` row — "same logic as `end_date`," meaning each surviving plan would need
its own individual narrowing, `NarrowSurvivingPlanIfNeeded`'s own shape applied per-plan rather than to
just one — not yet built for more than one plan at a time).

**Author actively designing against this, 2026-08-15 — "how to avoid forcefully consolidating earmark
patterns," worried about overcomplicating it.** Re-grounded directly in this table plus
`FinancePatternSaveConfirmation.cs` itself (`ConsolidationNeeded = HasMultipleEarmarkPatterns &&
_recurrenceShapeChanged`, the ONLY forcing condition — identical on both the break-off and
retroactive-correction sides) rather than re-derived from memory. **The forcing surface is narrow, not
sprawling:** amount-only, start_date-only, and end_date-only changes already have (or, for start_date,
already anticipate) a keep-separate path; a recurrence-shape change is the one dimension with no
keep-separate mechanism at all today — not because it's judged impossible, but because nobody has built
the harder "re-derive each surviving plan's own individual timing against the new dates" mechanism this
same table's own "Why" column already names. Whatever design comes out of this belongs here, next to the
table it's answering.

**Continued the same day — re-grounded directly in `EarMarkPattern.Create`'s own validation
(`EarMarkPattern.cs`) and the original F27 finding (item 8/9/22's shared relaxation of `3.11.1.a1`,
letting more than one `EarMarkPattern` share a `finance_id` at all — [planning/17](17-stage4-allocation-only-changes.md))
rather than reasoned from the table alone — the forcing rule turns out to be stricter than the model
actually requires.** `EarMarkPattern.Create` validates exactly three things: `FinanceId` matches the
goal's own; the plan's active span (`ActiveStart`/`Until`) fits inside the goal's own (`3.11.2.a2`);
and `StartingAllocation >= 0`. Nothing checks a plan's own `Frequency`/`Interval`/`ByDay`/`ByMonthDay`
against the goal's shape, or against a sibling plan's — confirmed further by F27's own finding, which
states plainly that once multiple `EarMarkPattern`s can share a `finance_id`, each one's own
`Amount`/`DatePattern` "already vary independently," with the shared `FundJar` simply summing whatever
events every pattern sharing the id happens to generate that day. The one other original assumption
that mentions "the exact same rrule" for a shared finance_id (`1.2.3c.11.a3`, Chapter 17) turned out, on
reading that chapter in full, to be about a single pattern's own identity staying stable from one *page*
to the next *over time* ("page X immediately before page Y") — a page-regeneration-continuity rule this
project's own fresh-each-day cascade already satisfies structurally ([13a, W4](13a-linearity-workaround-registry.md#w4--compute-per-day-instead-of-storing--in-use-structural))
— not a same-page, same-time constraint between two concurrent plans. Neither check has anything to say
about "must a surviving plan's own shape track the goal's."

**What this means: leaving a surviving plan's own `Amount`/`DatePattern` completely untouched through a
recurrence-shape edit is fully valid, not just structurally tolerated.** The only real risk is pacing —
does the plan still land its money sensibly against the new dates — which is exactly the *Concerning*
axis this document's own field table already tracks for `amount`/`end_date`, with an already-built
passive answer (`PlanHealthState`/`GoalShortfall`'s existing warnings), not a new problem the
recurrence-shape case needs a hard block to avoid.
`Retroactive_correction_with_multiple_surviving_plans_kept_separate_is_also_a_safe_no_op_for_now`
(`FinancePatternSaveConfirmationTests.cs`) already proves and states this exact reasoning for the
amount-only case — *"since dates never move on this side, each plan's own schedule and rate are still
exactly what they were, still valid against the now-edited goal"* — and nothing about that reasoning
actually depends on amount being the only field that can change; it depends only on the plan's own row
being left alone, which is equally available for a shape-only edit.

**PROPOSED 2026-08-16; asked directly and DECLINED FOR NOW, 2026-08-17 (author, via
[27](27-editing-within-a-patterns-chain.md)'s own "keep going on open items" check-in, once Phase 1/2/the
fourth relationship were all closed out and this was the only remaining ready-to-ask item) — "keep
forcing consolidation for now."** Not a rejection of the reasoning above, which stands — just not
something to build today. Left written up exactly as it was, in case a future session picks it back up;
don't re-propose without checking here first. The proposal itself, unbuilt: extend the same no-op
treatment — no new domain mechanism, just widening the existing gate — to a recurrence-shape change with
no accompanying `start_date` change, on the retroactive-correction side specifically (same `finance_id`,
Item E's "correct it everywhere"). Concretely: `ConsolidationNeeded` stops being unconditionally true
whenever `_recurrenceShapeChanged` alone is the trigger; the user gets *asked*, same as every other
Critical field, instead of being told; choosing to keep plans separate runs the existing
(currently-TODO, amount-only-shaped) no-op branch in `PerformImplicitEarmarkChanges`, generalized to
also cover this case rather than left gated to `_isAmountOnlyChange` alone.

Two things this does **not** solve, both already tracked separately above, neither reopened by this:
- **The break-off side's own "keep separate"** stays exactly as open as it already was for every
  Critical field, shape included — splitting one jar balance across several brand-new successor plans
  under a new `finance_id` is a materially different, harder problem than "leave the row alone," and is
  already this document's other listed gap, not something this proposal changes.
- **A shape change bundled with a `start_date` change in the same edit** still needs the already-flagged
  per-plan narrowing this document's own table calls out under `start_date` — a shape-only edit (no
  `start_date` change) sidesteps that problem entirely, but a combined edit doesn't.

**One nuance flagged for a real decision, not a blocker:** not every surviving plan drifts the same
amount after a shape change. A plan built by `AllocationPlanProposer.Propose`'s no-income shape
(`ProposeFrontLoaded`'s multi-occurrence branch, which copies the goal's own `Frequency`/`Interval`
onto the plan directly) is left representing a stale copy of the *old* cadence once the goal's own
shape moves — a more pointed kind of drift than a plan paced against income, or a hand-edited one,
would see from the same edit. Worth deciding whether the confirmation's own wording should call that
out more specifically for a shape change than the generic copy the amount-only case already uses, or
whether the existing passive warning is enough either way — not decided here.

**Existing coverage that would need to change, not break, if this is adopted:**
`Retroactive_correction_with_multiple_surviving_plans_forced_by_a_recurrence_shape_change_consolidates_them_in_place`
currently pins today's forced behavior for exactly this scenario — it would need `ChooseConsolidation =
true` added to keep testing the consolidate path (still available, just no longer the only option),
alongside a new test for the newly-enabled keep-separate path, mirroring the amount-only pair already in
that file.

## Item G — Strategy choice for an implicit plan proposal · BUILT 2026-08-14 for the single-plan break-off case

When Item C's break-off needs `AllocationPlanProposer` to propose a fresh `EarMarkPattern`, and more
than one shape is genuinely available for it, offer the user a choice between them as part of the same
confirmation, rather than the proposer silently picking one on its own. Philosophy 1 applied to the
implicit-change step itself, not just to whether the step happens at all.

**Explicitly distinct from the "strategy picker" already named under "Still open," below — the two are
easy to conflate but answer different questions.** That one lives on the *Concerning* axis: a
`PlanHealthState`-sourced picker that pre-fills the Earmark form's fields as Suggested/Working-state
values for the user to look at and decide whether to act on, later, on the Earmark form itself — a
deferred suggestion. Item G lives on the *Critical* axis, inside the implicit change
`PerformImplicitEarmarkChanges` is already making as part of *this* save — whichever shape the user
picks is applied immediately, not surfaced as something to maybe act on afterward. Author's own words,
2026-08-13: *"these wouldn't get turned into suggestions, they would be implied implicitly."*

**Built, 2026-08-14 — `FinancePatternSaveConfirmation.DeterminePlanShapeCandidatesIfApplicable`**, one
more question folded into the same confirmation popup Items E/F already use:
- `ImplicitChangeConfirmationRequest.PlanShapeCandidates` — up to three `PlanShapeCandidate`s
  (`Propose`'s own default, always first; `ProposeSameSchedule`; `ProposeSameAmount`, whichever of the
  latter two return non-null) — empty whenever there's nothing to choose between.
- `ImplicitChangeConfirmationAnswer.ChosenPlanShape` — one of those candidates' own `EarMarkPattern`, by
  reference, or null for the default. `PerformSingleSuccessorBreakOff` matches it back to its own full
  `ProposedAllocationPlan` (`StartingEarmark` included) and passes it to a new
  `BreakOffFactory.BreakOffRequest.ChosenSuccessorPlan` field, which the factory uses in place of its own
  internal `Propose` call when supplied.
- Proven end-to-end, not just wired: `FinancePatternSaveConfirmationTests.Item_G_a_chosen_plan_shape_candidate_is_the_one_that_actually_gets_saved`
  drives a fake `ConfirmImplicitChanges` delegate that reads the real candidates `Run()` built, picks
  `ProposeSameSchedule`'s own, and confirms the actual saved successor reflects it.

**Fixed 2026-08-15 — the "Recommended" candidate's own preview understated a carried-over balance.**
Traced back to a detail the author raised while first designing the glut-protection work
([26](26-editing-an-earmark-pattern.md)'s "the glut case") and then flagged again later, after it had
been set aside while other work took priority: *"keeping the glut as an up front earmark event should be
a valid option... we might need an optional parameter on the propose functions to handle that."*
`ProposeSameSchedule`/`ProposeSameAmount` already took `carriedOverJarBalance` and set it correctly on
their own candidates — but `Propose` (the "Recommended" candidate's own source) never did, always
reading `StartingAllocation = 0` in the picker even when a real balance existed. `BreakOffFactory.BreakOff`
already overrode it correctly at save time regardless of which candidate got chosen, so nothing was ever
actually lost — but the picker's own preview was quietly wrong about what "Recommended" would produce,
the same class of bug `EarmarkFormLivePreviewTests` already found in a different region. `Propose` gained
the same optional `carriedOverJarBalance` parameter its two siblings already have (default `0m`, every
other existing caller unaffected); `DeterminePlanShapeCandidatesIfApplicable` now passes it through.
Proven both ways — the new test fails with the old code (confirmed by reverting it and watching the
assertion fail: preview read `$0` against a real `$300` balance) and passes with the fix:
`Item_G_the_Recommended_candidates_own_preview_matches_what_actually_gets_saved_for_StartingAllocation`.

**Originally scoped to exactly one existing plan (`!HasMultipleEarmarkPatterns`) — no candidates at all
otherwise — resolved 2026-08-14 after the author's own clarifying question surfaced a real
distinction, then FIXED 2026-08-16 (see [24](24-app-layer-known-gaps.md)):**
`HasMultipleEarmarkPatterns` can mean two different things — **sequential** segments joined end-to-end
(a `RestructureFactory` chain, same `finance_id` throughout — pick whichever surviving plan's own `Until`
connects to the new plan's own `Start`) or **concurrent** plans genuinely overlapping (F27's shape, e.g.
two household partners). Originally, `RestructureFactory` wasn't called from anywhere in
`MyMoneyForecast.App`, so the sequential case wasn't reachable through any wired save path and
`HasMultipleEarmarkPatterns` only ever meant concurrent in practice — but the *domain-level* gap this
left (a Savings Plan that had ever been restructured, even via seed data or a future UI, permanently lost
Item G) was real regardless of what the wired app could reach yet, so it's fixed now:
`RestructureFactory.FindCurrentPlan` tells the two cases apart (non-overlapping active spans among the
existing plans means a sequential chain — pick the one with the latest `Start`; any overlap means
concurrent — no candidates, exactly as before). For concurrent plans, the author's own ruling stands
unchanged: continuing both of them (when the user declines Item F's consolidation) needs its own new,
not-yet-designed function that sizes both plans in unison — one of this document's two remaining Item F
gaps, above — and should **not** additionally offer an Item G shape choice on top of that complexity
("It'll already be complicated enough" — author).

Still open: the popup content itself (labels are currently placeholder strings — "Recommended," "Keep
the same schedule," "Keep the same amount" — not designed UI copy), and how this composes visually with
Items E/F's own questions in the same popup.

---

## Field-by-field findings, from the author's draft Trivial/Critical/Concerning categorization

Worked through in conversation, 2026-08-11 — not the author's own final table (that's still theirs to
set), but the corrections/confirmations that came out of checking each field against what's already
settled elsewhere. **Two axes, not one**, and a field's category is really "the more demanding of the
two": does it touch `1.2.3.10.a5` at all (Item A/C/E territory — a real choice between correcting and
breaking off), and separately, does it leave the plan under-paced for the goal/bill regardless (the
Concerning axis, `PlanHealthState`/`GoalShortfall`'s territory)?

| Field | Touches `1.2.3.10.a5`? | Concerning (pacing) axis? | Note |
|---|---|---|---|
| `start_date` | Yes | — | The orphaning-risk field; Item E's "always offer both, warn on consequence" applies directly |
| `end_date` | No — exempted by the assumption itself | Yes | Both directions: closer means the rate may not reach the milestone in time; farther, on a *recurring* bill, means the earmark pattern's own `Until` doesn't auto-follow, so later occurrences go unfunded — the same gap [F18](15-stage2-pattern-lifetime.md#f18--an-ongoing-patterns-savings-plan-must-be-ongoing-too) found for the "ongoing" flag, here triggered by a manual edit instead |
| recurrence shape (frequency/interval/by-day) | Yes — `3.13.7.a1`/`3.13.7.a2` tie valid dates to the rrule shape directly | Yes — sawtooth misalignment | Re-proposing via `AllocationPlanProposer.Propose()` (the same function already used for the Concerning-category's default suggestion) naturally realigns to the new dates — no separate mechanism needed |
| `amount` | Yes — `ExpectedTransaction.expected_amount` is drawn straight from it | Yes | |
| `mandatory`/skippable | **No** (corrected from "concerning in some situations") | **No** | Matches [B8](13b-user-action-catalog.md) exactly — every outflow already gets a plan regardless of mandatory status since the Stage-1 revision; toggling only changes deallocation drain order |
| `description`/`source` | **No** (corrected from "trivial, with an invisible break") | No | Matches [B9](13b-user-action-catalog.md) exactly — a pure no-op on the earmark side, no break of any kind needed |
| `priority` | No | No | Matches [B7](13b-user-action-catalog.md) exactly, already verified in code |
| Multiple existing `EarMarkPattern`s | Not its own field — a consequence that can accompany any of the above | — | Item F |

## Final field categorization · SETTLED 2026-08-11

**One correction to the shape of the categorization itself, not just individual fields: Critical is
not a fixed label per field — it's conditional on whether history exists for that `finance_id`.**
`1.2.3.10.a5` only fires "if an expected transaction... exists on an expired page in the past." A
brand-new or entirely-future pattern never trips it, no matter which field changes — Item A's own
carve-out ("finance patterns that extend into the past yet don't have any expected transactions...
can be altered without restriction. The same is true of finance patterns that exist only after the
current day"). So the table below labels each field by what it *can* become, with the trigger stated
explicitly rather than baked into one word.

| Field | Category | When |
|---|---|---|
| `start_date` | **Critical**, else a plain edit | Critical only if a past-occurred `ExpectedTransaction` already exists for this `finance_id`; otherwise unrestricted |
| `amount` | **Critical**, else a plain edit; **also Concerning** once resolved | Same Critical condition as `start_date`; Concerning because the new amount may leave the plan mis-paced regardless of which resolution (correction or break-off) was chosen |
| recurrence shape (frequency/interval/by-day) | **Critical**, else a plain edit; **also Concerning** once resolved | Same Critical condition; Concerning for the sawtooth-realignment reason — resolved by re-proposing via `AllocationPlanProposer`, not a new mechanism |
| `end_date` | **Concerning only** — never Critical | Exempted by the assumption itself; pacing risk both directions (rate too slow if closer; a recurring bill's earmark pattern not auto-following if farther) |
| `mandatory`/skippable | **Trivial** | Always — no funding-existence effect since the Stage-1 revision |
| `description`/`source` | **Trivial** | Always — no assumption ties either to the earmark side |
| `priority` | **Trivial** | Always — no retroactive effect, verified in code |
| *(modifier)* multiple existing `EarMarkPattern`s survive past the cut | Adds Item F's own confirmation on top of whichever row above applies | Whenever a Critical-triggering change (start_date/amount/recurrence) lands on a `finance_id` with more than one surviving plan |

## Still open

- **The confirmation popups' own content and layout.** Sketched in conversation, not designed as UI
  yet: Critical fires regardless of which Save button is pressed; Concerning fires only on "Save and
  Plan," offering a `PlanHealthState`-sourced problem summary plus a strategy picker (one default
  suggested set, or none — no per-field toggles) that pre-fills the Earmark form's fields as
  Suggested/Working-state values ([23, item A1-a](23-form-behavior.md#a1-does-what-a-form-shows-match-whats-saved)
  and [item A6](23-form-behavior.md#a6-the-reset-to-saved-control--settled-2026-08-06)). "Save and
  Skip Planning" on a Concerning change saves straight through with no popup — `GoalShortfall`/
  `PlanHealthState`'s existing passive warning is the whole fallback, matching
  [19](19-stage6-warnings-levers-shortcuts.md)'s own "trust the user to notice" ruling for states 3/4.
- **The specific strategies** offered in that picker ("start from scratch but save faster," "maintain
  current funds and pace, but change amount," etc.) — explicitly deferred by the author to a later
  pass. **The candidate-generating machinery for this is now built, 2026-08-14** (`AllocationPlanProposer.ProposeSameSchedule`/
  `ProposeSameAmount`, alongside the existing `Propose`) — three methods, deliberately kept next to each
  other: `Propose` builds a fresh plan from scratch; `ProposeSameSchedule` keeps an existing plan's own
  recurrence shape and solves for an ideal `Amount`; `ProposeSameAmount` keeps the existing `Amount` and
  solves for an ideal cadence. Both new methods return `null` — "don't offer this" — whenever their own
  result wouldn't be genuinely ideal or would be indistinguishable from `Propose`'s own default; skipping
  every suggestion is always still available on top, so a caller shows at least the default plus "skip,"
  never fewer than two options. Deliberately narrower freedom than a user's own edits get, per planning/26's
  own philosophy note: implicit/suggested plans should always aim to be genuinely ideal. **`ProposeSameSchedule`/
  `ProposeSameAmount` take a `carriedOverJarBalance` parameter, not `existingPlan.StartingAllocation`**
  (author's own explicit direction, 2026-08-14: whatever plan gets created must carry the previous
  plan's real funds forward) — `StartingAllocation` is a static field frozen at whenever the existing
  plan was first created, not what the jar holds today; the caller is expected to read the real balance
  off the live forecast, same as `BreakOffFactory`'s own `CarriedOverJarBalance` field already does for
  `Propose`'s own default successor. **The picker UI itself — which of these to call, and how they're
  presented — is still not designed.**

## Parked

- Whether item 4's existing deliberate confirm screen (its own 4-D data contract) and this new
  trigger's confirmation are the same screen, or two that can stack for a Critical change on "Save and
  Plan" — not yet decided, noted when it came up.
