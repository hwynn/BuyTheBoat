# 26 — Editing an earmark pattern

**Status: IN PROGRESS — grounding phase, started 2026-08-14, continued 2026-08-15.** Most of this
document's own scope is still deliberately deferred to a future iterative development cycle, per the
author's own original call. **One piece was pulled forward the same day: "deferring funding" (below) —
the author's own reasoning is that the implicit changes planning/25's Items C–G already make to a
Savings Plan need to be able to preserve a user's own deferred-funding choice, and those mechanisms are
live today, not deferred.** Everything else here is still direction, not a committed design.

**As of 2026-08-15 (end of day): the whole "deferring funding" thread is now functionally complete.** The
pause case's one scoped piece (the Summary statement) is built; the glut-detection helper (`FundJar.HasGlut`,
plus a `GlutSurplus` magnitude added alongside it) is built; mechanism (C) — a user-facing "skip specific
dates" control, full domain + persistence + cascade + UI — is built end-to-end; and `HasGlut`/`GlutSurplus`
are now wired into all four re-proposal mechanisms — one (`EarmarkConsolidation`) had a real erasure bug
and was fixed (plus a second, related timing bug in its own caller, found and fixed the same pass); the
other three (`EarmarkScaling`, `ProposeSameSchedule`, `ProposeSameAmount`) were checked and found to
already protect a glut correctly by construction, verified with real tests rather than left as an
assumption. **Still undecided, and deliberately not designed here:** exactly how excluded dates should
interact with every OTHER pattern-rebuilding mechanism (break-off, renewal, `PatternTruncation.StartOn`)
— the author was explicit this wasn't designed yet, so what exists today for those is only what already
falls out of existing, unmodified code, not a real decision. See "Deferring funding" below for the full
detail.

## Why this exists

[planning/13](13-adjusting-the-plan-charter.md)'s editing-history record (formerly planning/25)'s own scope note (added 2026-08-14, prompted by
`EarmarkFormLivePreviewTests.Editing_a_savings_plans_own_amount_retroactively_rerates_its_whole_history_with_no_protection`)
found that editing an `EarMarkPattern` through the Earmark form has none of Items B–G's protection —
at the time, `EarmarkFormPanel.SaveSavingsPlan` → `MainWindow`'s `EarmarkForm.PatternSaved` → a plain
`_earMarkPatterns.Save(pattern)`, no confirmation, nothing. That raised an open question: intentional
scope boundary, or a real gap? The author's answer, same day: a real gap worth addressing eventually,
but not now.

The chain-cascade work (formerly planning/27, now in the code + [13](13-adjusting-the-plan-charter.md))
gave that save path a real `FinancePatternSaveConfirmation`, but its questions were scoped to a plan's
relationship with its chain siblings, not to rewriting a lone plan's *own* past.

**BUILT 2026-09-20 — the lone-plan gap this section identified is now closed.** Editing a single,
unchained savings plan's amount/rate when it's already been accumulating (its active span began before
today) now offers a **"break off vs. recalculate the whole plan"** choice in the save confirmation
(`EarmarkPatternSaveConfirmation`, using `RestructureFactory.Restructure`). Break off — the pre-selected,
non-destructive default — splits the plan at today, keeping what's already set aside (the jar's current
balance) and applying the new rate only going forward; recalculating re-rates the whole history (warned
that the current set-aside amount changes). A headless save (no popup) still re-rates in place, so
nothing silently restructures. **Still deferred:** the broader direction below — freedom-by-default
editing, protecting the jar's real `ExpectedAmount`, and a "defer allocation on purpose" want.

## Vocabulary, reaffirmed (not new)

**"Savings Plan"** — already settled (see `feedback_design_in_class_documentation_terms.md`'s own
worked example, and [planning/21](21-forms-and-ui.md#the-forms)) — means every
`EarMarkPattern` sharing one `FinancialPattern`'s `finance_id`, collectively. The author restated this
explicitly (2026-08-14) while giving the direction below: everything here is about editing any one (or
more) of the `EarMarkPattern`s making up a goal's Savings Plan.

## The core philosophy: EarmarkPatterns aren't "real" the way FinancialPatterns are

Author's own framing (2026-08-14): a `FinancialPattern` creates real bill/paycheck events that actually
happen — eventually enforced by a pairing system (the "assumed-pairing" philosophy, planning/12; see
also `project_actual_transactions_register.md`). An `EarMarkPattern` creates `EarMarkEvent`s, which are
**purely an organizational construct for the user** — earmarks and fund jars aren't "real" the same way
expected transactions are. Because of this asymmetry, the author wants the user to have **much more
freedom** editing `EarMarkPattern`s than planning/25's Items B–G give for editing a `FinancialPattern` —
explicitly including a plan that doesn't meet its goal, holds excess funds, or has a recurrence that
doesn't line up conveniently with paychecks or the expense's own occurrences. **This freedom is scoped to
user-initiated edits only** (author's own clarification, same day): anything this app *implicitly
creates or suggests* on the user's behalf — the default proposer, or the not-yet-built suggestion
methods planning/25's Item G anticipates — is held to a stricter standard and should still aim to be
genuinely ideal, not just "whatever passes validation." A user can knowingly choose an imperfect plan;
the app shouldn't hand them one as if it were the good option.

Default posture, in the author's own words: "I can't think of many situations where we would lock the
user out of a change they want to make in an earmark pattern, as long as the result didn't violate an
assumption." Some edits are still expected to trigger a **confirmation message as a soft protection
measure** — not a hard block, and not every edit, just some.

## The one thing that does need protecting: a fund jar's ExpectedAmount

Freedom doesn't mean no protection at all. The author's own example: if a user has allocated $1,000
toward a boat, an edit shouldn't make it easy to accidentally "forget" that $1,000 and let
`ExpectedAmount` fall back to 0. This is already a live design principle, not a new one — it's the
reason break-off passes the predecessor's `ExpectedAmount` forward as the successor's own
`StartingAllocation` (planning/16, item 4) instead of starting the new plan from $0. Whatever gets
designed for editing an `EarMarkPattern` directly should honor the same principle: **real,
already-realized money in a jar is protected from being casually cleared out**, however the edit that
could clear it gets triggered.

## Deferring funding — the one piece pulled forward (in progress, 2026-08-14)

**Term: "deferring funding"** (author's own choice, 2026-08-14, over an earlier "deferring
allocation"/"deferring payments" — no actual payment is involved, only allocation of funds already set
aside). Two genuinely different scenarios, confirmed by the author to need two different mechanisms —
don't conflate them:

### The pause case — confirmed workable, and now recognized in the Summary (built 2026-08-15)

A user stops contributing to a goal for a while (something came up) but wants the already-accumulated
balance to just sit, untouched, until funding resumes — possibly with zero `EarMarkPattern` occurrences
for a long stretch in between.

**Checked against the assumptions directly, not assumed:** `3.13.5.a1`/`3.13.5.a2` — a `FundJar` cannot
exist on any day with no covering `EarMarkPattern` (the safety-cushion jar is the one named exception).
So a jar can't just "float" through a gap with nothing behind it — holding money through a pause has to
be a real `EarMarkPattern` spanning the whole gap, shaped so it produces no real contributions. That's
not a new mechanism: `AllocationPlanProposer.ProposeEmpty` already does almost exactly this (`Count = 1`,
`Amount = 0`, `ActiveFrom` stretched back) for the "declined a plan" case.

**Verified end-to-end, not just reasoned about:**
`TransactionLogBookFactoryTests.A_zero_amount_plan_holds_a_balance_through_a_pause_then_a_third_plan_resumes_contributing`
— three real `EarMarkPattern` rows sharing one `finance_id`, sequential and non-overlapping, nothing
deleted (contribute → `Amount = 0m` pause → contribute again): the balance sits frozen through the pause
and resumed contributions add on top of it, not from 0. **The mechanism itself is functional today.**

**Scoped by the author, 2026-08-15, to exactly one piece — and that piece is now built:** "the only thing
I want implemented for the pause case is an update to the statement in the summary." When a plan's own
rule produces no occurrences, or every occurrence shares the pattern's one `Amount` at $0,
`EarmarkFormPanel`'s Summary narrative now reads "Funding is currently paused, with nothing scheduled
toward it." instead of naming "$0" or silently dropping the contribution sentence altogether. Built as
`PlanHealthMessages.IsPaused`/`PausedFundingSentence` (`src/MyMoneyForecast.App/PlanHealthMessages.cs`,
matching that file's own established convention — wording kept out of the UserControl code-behind so
it's plain, testable logic), wired into `EarmarkFormPanel.UpdateSummary`. Tested:
`PlanHealthMessagesTests.cs` (`tests/MyMoneyForecast.App.Tests/`).

**Still not built, and deliberately out of scope for now** (candidate ideas only, not designed further):
a dedicated shortcut so the user can deliberately pause/resume without hand-building a `Count`/
`Amount = 0` rule themselves.

### The glut case — the skip mechanism is built (2026-08-15); using it to actually protect a glut is not

A user front-loads several cycles' worth at once (a deliberate "glut," e.g. paying several months of a
repeating bill ahead of schedule) and doesn't want the reset-at-release rule (`3.13.5.4.a1`) or, more
concretely, this session's own newer mechanisms (`EarmarkConsolidation`, `EarmarkScaling`,
`ProposeSameSchedule`, `ProposeSameAmount`) to treat that glut as ordinary "already banked" money to be
smoothed/redistributed. Those four mechanisms are the actual risk — they already exist and already run
in the live save path, which is why this couldn't wait for the deferred cycle.

**Glut detection — author's own precise definition, 2026-08-14, confirmed correct 2026-08-15 and now
BUILT:** a `FundJar` (as of some date D) has a glut if its finance_id's next *repeated earmark* (the
next scheduled contribution strictly after D, not on D) could be skipped and the jar would still read
`ExpectedAmount >= MilestoneAmount` immediately after that point.

Built as `FundJar.HasGlut` (`src/MyMoneyForecast.Domain/FundJar.cs`) — placed directly on the class
documentation's own `FundJar`, per the author's ask, since it turns out to need nothing beyond that
class's own two existing fields. **Why the reduction to a same-day comparison is sound, not a
shortcut:** skipping that one future contribution moves `ExpectedAmount` and `MilestoneAmount` by the
identical amount, because both accumulate from the same earmark-event stream for that occurrence
(`3.13.5.3.a1` / `3.13.5.4.a1`) — so whatever the comparison reads *today* is exactly what it would
still read immediately after that specific contribution was skipped. (Checked for a hidden edge case:
if a release lands strictly between D and the next contribution, `ExpectedAmount` floors at 0 and
`MilestoneAmount` resets to exactly 0 at that release regardless of the skip, which trivially satisfies
`>=` no matter how underfunded the jar actually was — but that only matters for an arbitrary hypothetical
date D; every real call site evaluates this live, at today's date, from numbers already on hand, so it
doesn't come up in practice.) False whenever `MilestoneAmount` is null (the safety cushion, auto-reserved
bills) — no schedule means no notion of "ahead of." Tested directly: `FundJarTests.cs`
(`tests/MyMoneyForecast.Domain.Tests/`) — ahead, exactly-at-the-boundary, behind, and no-milestone cases.

**Wired into all four, 2026-08-15 — but the honest finding is that only one of them actually needed a
code change.** See "Not yet wired in" further down (now retitled) for the full account: `EarmarkConsolidation`
had a real, confirmed erasure bug and was fixed; `EarmarkScaling` and
`AllocationPlanProposer.ProposeSameSchedule`/`ProposeSameAmount` were checked and found to already protect
a glut correctly, by construction — verified with real tests, not just re-asserted.

**How to actually skip a contribution — three mechanisms the author identified, tradeoffs included:**
- **(A) Break off the pattern for a temporary gap, resume after.** Author's own objection: pollutes the
  Savings Plan with extra rows for what might be a short, one-off skip — "it feels much more natural...
  to keep the earmark pattern intact."
- **(B) Flag an individual `EarMarkEvent` as skippable.** Author's own objection: `3.13.8.a3`/`3.13.8.4.a1`
  currently *require* a scheduled repeated earmark to exist with an amount matching the pattern exactly
  — no flag exists today, and every assumption that currently reads "if scheduled, it happens" would need
  an "unless skipped" carve-out threaded through it.
- **(C) A list of skip-dates on the `EarMarkPattern` itself**, so the earmark event for those dates
  simply never gets generated. Author's own read: still real design/assumption work, but less invasive
  than (B).

**Checked, not assumed: (C) is cheaper than it looks — and settled as the preferred direction,
2026-08-15.** This is RFC 5545's own EXDATE concept, and the `Ical.Net` library this project already
depends on supports it natively — confirmed directly with a throwaway spike
(`CalendarEvent.ExceptionDates`, one date added to an otherwise-ordinary monthly rule, that exact date
cleanly excluded from `GetOccurrences()`, nothing else disturbed).

**Correcting an assumption the author asked about directly:** EXDATE is *not* encoded into the RRULE
string itself — it's its own separate iCalendar property, sitting alongside RRULE on the same event
(`CalendarEvent.ExceptionDates`, a distinct list), not a field inside RRULE's own `FREQ=...;BYDAY=...`
syntax. `RecurrenceRule.ToRruleString()` would stay exactly as it is today; a skip-dates list would need
its own separate storage, not a change to that string.

**Also checked directly, per the author's follow-up ("Can it not exclude multiple dates?"):** yes —
a second spike added two exception dates to one monthly rule and confirmed both, and only both, were
excluded from `GetOccurrences()`, everything else undisturbed. Not a one-date-only limitation; a real
list, matching what an EarMarkPattern skip-dates field would need to be (a user deferring a lot of
occurrences, in the author's own words, not just one).

`RecurrenceRuleOptions` deliberately doesn't expose this today ("mirrors the RRULE fields used
throughout `mini_fund_project`... rather than exposing the full RFC 5545 surface" — its own header
comment) — but the library-level capability isn't the blocker; exposing and persisting it through this
project's own model is the real remaining work, not inventing exclusion from scratch. **Not yet built**
— no skip-dates field on `EarMarkPatternOptions`, no persistence, no cascade change to actually honor
one, no UI. Ready to start; hasn't been started.

**Two scope notes from the author, recorded here rather than acted on yet:**
- **Renewal/continuation cleanup (future work, not now):** if a pattern carrying skip-dates is later
  continued, renewed, or broken off, any excluded date that no longer falls within the new pattern's own
  range should be discarded rather than carried forward as dead weight. Not designed further than that.
- **Isolated (manual) earmarks need no skip mechanism at all.** None of A/B/C apply to a `ManualEarmark`
  — the author's own point: a one-off, isolated earmark that needs skipping can simply be deleted. A/B/C
  are only ever about a *repeated* earmark event a pattern would otherwise keep generating.

**A fourth option considered, not recommended:** offset the scheduled contribution with an equal,
opposite `ManualEarmark` on the same day — no `EarMarkPattern`/`EarMarkEvent` changes at all, fully
assumption-compliant today. Rejected on inspection: `MilestoneAmount` is driven purely by the *schedule*
(`ComputeMilestoneTrajectory`), which a manual earmark never touches — the milestone would keep climbing
on the old schedule while `ExpectedAmount` stayed flat, reading as a growing shortfall (a false "you're
falling behind" warning) rather than the "intentionally paused, nothing wrong" the author wants. (C)
doesn't have this problem, since an excluded date never generates a milestone contribution either.

**The author's own remaining open question, CONFIRMED 2026-08-15 with a real permanent test, not just
traced through code:** does a release reset a jar's `ExpectedAmount` to 0, so a structural glut (a plan
that habitually over-contributes every cycle) would just get thrown away by the program's own default
behavior rather than surviving? **No — confirmed the opposite, exactly as the author's own hypothesis
predicted.** `TransactionLogBookFactoryTests.A_structural_glut_accumulates_across_releases_instead_of_being_reset`
— a goal at $100/month paired with a plan contributing $150/month every cycle, every month a $50 glut —
walks three real monthly cycles and finds the jar at $50, then $100, then $150: strictly climbing, never
reset. A release only ever subtracts the goal's own fixed per-occurrence amount
(`TransactionLogBookFactory.AppendDeallocationOrGoalReleases`) — it does not zero the jar out. Only
`MilestoneAmount` resets at release (`3.13.5.4.a1`); `ExpectedAmount` does not.

**What this settles:** the base cascade already protects a structural glut on its own, with no new
mechanism needed — a deliberately over-funded jar is safe from ordinary release/deallocation behavior
today. The four re-proposal mechanisms this session built (`EarmarkConsolidation`, `EarmarkScaling`,
`ProposeSameSchedule`, `ProposeSameAmount`) are the *only* real risk to a glut, since they're the only
things that treat "already banked" as one fungible, redistributable number — which is exactly why
`FundJar.HasGlut` (above) needs to end up wired into those four specifically, not into the cascade
itself.

**`Mandatory`/auto-reserve is a separate, real concern — confirmed by the author, deliberately NOT part
of this design.** The trace above (the *only* live use of `FinancialPattern.Mandatory` in the current
cascade is `Skippable` in deallocation priority; `EarMarkEvent.cs`'s own comment about "POSITIVE implicit
events to auto-reserve mandatory bills ahead of their due dates... endorsed by the design's author,
2026-07-09/10" matches no live call site anywhere in `MyMoneyForecast.Domain` today) was flagged for the
author rather than corrected. Their answer, 2026-08-15: this describes a real, intended mechanism — a
mandatory bill's occurrence coming due within a week, with the repeated earmarks in place insufficient
to cover it, should get an implicit isolated earmark so the milestone is still reached in time — and if
it's genuinely missing, "that's a problem." **Explicitly not related to deferring funding** (the
author's own words) — tracked instead as state #8 in
the concerning-states inventory (former planning/19's Stage 6, dropped — see [13](13-adjusting-the-plan-charter.md)),
alongside the author's added ask for forecast-tab detection plus a shortcut button.

**Mechanism (C) is now BUILT, 2026-08-15** — "go ahead and build mechanism C," the author's own direction,
same day as the settling above. What exists now:

- **Domain:** `RecurrenceRuleOptions`/`RecurrenceRule` gained `ExcludedDates` (`IReadOnlyList<DateOnly>`,
  default `[]`). `GetOccurrences` feeds them straight into `Ical.Net`'s own `CalendarEvent.ExceptionDates`
  before generating — every call site in the cascade, milestone trajectories, and form previews honors an
  exclusion automatically just by going through `GetOccurrences`, with nothing extra wired up at any of
  them (confirmed, not assumed —
  `TransactionLogBookFactoryTests.An_excluded_date_produces_no_contribution_and_no_milestone_increment_in_a_real_forecast`
  proves a real forecast run skips both the contribution and the milestone increment). New
  `WithExcludedDates(...)`, matching `WithActiveFrom`/`WithUntil`'s own shape; both of those now carry
  `ExcludedDates` through unchanged too, since their own doc comments already promise "everything else
  stays the same."
- **Deliberately NOT validated against Start/Until or the rule's own real occurrences** — a design choice,
  not an oversight, made to answer the "how cascading changes will affect it" question the author flagged
  as undecided. Real EXDATE semantics treat a non-matching date as a harmless no-op, not an error; the
  alternative (validating) would have made `WithUntil` and every range-narrowing operation built on it
  (`PatternTruncation.EndOn`, break-off's own predecessor truncation) newly crash-prone the instant a
  shortened range left a stale excluded date behind. Confirmed via test
  (`An_excluded_date_that_is_not_a_real_occurrence_is_a_harmless_no_op`).
- **Persistence:** a 7th shared column, `ExcludedDates` (comma-joined `yyyy-MM-dd`, same convention as
  `ByDay`/`ByMonthDay`), added to all three tables the shared `RecurrenceRuleColumns` serializer already
  covers — `EarMarkPatterns` (the only one a form can actually populate), plus `FinancialPatterns` and
  `Transfers` for schema symmetry with the rest of `RecurrenceRule`, same reasoning as their own
  already-unused `ActiveFrom` column. Migrated on existing databases (`PatternDatabase.EnsureColumn` ×3);
  the legacy single-key `EarMarkPatterns` rebuild (`EnsureEarMarkPatternsAllowMultiplePerFinanceId`) was
  also updated to carry the column across — it has its own separate inline `CREATE TABLE`, which would
  otherwise have silently dropped it for anyone still on that old schema.
- **UI:** a "Skip specific dates" section in `RecurrenceRuleEditor`, collapsed by default
  (`ShowExcludedDatesEditor()`, called only from `EarmarkFormPanel` — Expense/Transfer are unaffected).
  Deliberately a picker over the schedule's own real occurrences (a ComboBox populated from
  `GetOccurrences` with whatever's already skipped removed), not a freeform date field — "all it will do
  is give users control of that iCalendar property" still holds; this just guards against silently
  "skipping" a date that was never going to happen anyway. Each skipped date lists with its own Restore
  button.
- **Isolated (manual) earmarks were deliberately left alone** — no skip UI there, per the author's own
  point above (just delete them).

**What was NOT decided, and had to be defaulted rather than designed — flagged here, not buried:** the
author was explicit they don't have a design yet for how cascading changes interact with this, so nothing
below was invented beyond what already falls out of existing code, unmodified:
- A **brand-new** pattern (a fresh successor from `AllocationPlanProposer`/break-off, a consolidated plan
  from `EarmarkConsolidation`, `ProposeEmpty`) starts with **no** excluded dates — `RecurrenceRuleOptions`
  simply defaults to `[]`, and none of those files were touched, so this is the existing behavior, not a
  new rule.
- `EarmarkScaling.Scale` preserves them, since it reuses the exact same `DatePattern` object and only
  changes `Amount` — confirmed by reading the code, not assumed.
- `PatternTruncation.StartOn` (moving a plan's own Start forward, planning/25 Item E) does **not** carry
  excluded dates forward — it already drops `ActiveFrom` the same way, by its own explicit design ("Any
  lead-in (ActiveFrom) is cleared" — its own doc comment), so this isn't a new inconsistency, but it does
  mean a date excluded before the narrowing is silently lost rather than preserved. Not fixed here.
- **Nobody prunes a stale excluded date** that falls outside a pattern's range after it's been narrowed —
  matches the author's own "future work, not now" call on this exact point (below). It just sits there,
  inert (per the no-op semantics above), until something removes it.

None of this is a final design — it's what the two rules above ("brand-new starts clean," "same-pattern
edit carries forward") produce automatically, stated plainly so a real decision can override any piece of
it later rather than this silently becoming the de facto answer by nobody having looked at it again.

**`HasGlut` wired into all four re-proposal mechanisms, 2026-08-15 — author's own direct instruction.**
Each of the four is structurally different, so this meant actually reading what each one's own formula
does with "already banked" money before deciding what "wire it in" should even mean for that one, rather
than forcing the same change into all four for appearance's sake:

- **`EarmarkConsolidation` — a real, confirmed erasure bug, now fixed.** Its own `alreadyBanked` term only
  ever counted `StartingAllocation` + manual earmarks — blind to a surviving plan's REPEATED contributions
  having built up a real glut beyond that. `ConsolidationRequest` gained `CurrentJar` (a `FundJar?`, read
  by the caller off the live forecast — a pure domain function has none of its own, same reasoning as
  `BreakOffRequest.CarriedOverJarBalance`); `Consolidate` now adds `CurrentJar.GlutSurplus` to both the
  sizing formula AND the result's own `StartingAllocation` — the same two-part treatment
  `StartingAllocation` itself already got on 2026-08-14 (discount it once when sizing, or it's forgotten
  the instant the old plans' rows are gone). **Caught and fixed a second bug while wiring the real caller
  in:** `FinancePatternSaveConfirmation` originally read the jar too late (inside
  `ConsolidateSurvivingPlansIfNeeded`, which runs AFTER `PerformSave`) — reading it against the
  ALREADY-edited goal's new schedule, not the one that actually produced the glut. Moved into
  `DetermineConsolidationPlanIfApplicable` (runs before any save), stored on `ConsolidationPlan` alongside
  `SurvivingPlans` and the rest — found via two existing tests whose own numbers shifted, not by
  inspection. Every number involved (the fix itself, and the timing bug) was checked against a real
  forecast replay, not hand-derived and trusted — see
  `TransactionLogBookFactoryTests.An_existing_glut_survives_consolidation_spent_down_evenly_instead_of_erased`.
  Two pre-existing tests' own expected values were outdated by this fix (their own scenarios happened to
  contain an incidental glut) and were updated with comments explaining why, not reverted.
- **`EarmarkScaling` and `ProposeSameSchedule`/`ProposeSameAmount` — checked, found to already be safe,
  no code change.** `EarmarkScaling.Scale` never reads or discounts "already banked" money at all; it only
  multiplies the ongoing rate by the goal's own ratio, leaving `StartingAllocation` and the schedule
  untouched — proportionally preserves whatever glut already existed, proven with a real test
  (`Scaling_a_glutted_plan_preserves_its_glut_proportionally`: a glut scaled 2x reads exactly 2x, not
  smoothed away). `ProposeSameSchedule`/`ProposeSameAmount` already take the CALLER's own live
  `carriedOverJarBalance` (the real `ExpectedAmount`, not a narrower proxy) straight into both their
  sizing math and the resulting plan's own `StartingAllocation` — nothing narrower in between for a
  surplus to get lost in. Proven with a real, forecast-driven glut, not just a hand-picked number
  (`ProposeSameSchedule_carries_a_real_forecast_glut_forward_into_the_new_plans_own_StartingAllocation`).

This build gives the user their own manual control over skipping a date (mechanism C, above) AND makes
sure the four mechanisms that already existed treat a real glut correctly — whichever of the two paths
(the user's own deliberate skip, or one of these four silently discounting a genuine surplus) a glut was
actually at risk from, both are now covered.

**A related loose end from earlier in this same thread got tracked down and closed the same day too:**
back when this whole area was first being grounded, the author proposed *"keeping the glut as an up
front earmark event should be a valid option... we might need an optional parameter on the propose
functions to handle that."* That got set aside while mechanism C took priority and was nearly lost —
found again by searching the session's own transcript at the author's request. Turned out to already be
half-true: `BreakOffFactory.BreakOff` already unconditionally carries a real balance forward as
`StartingAllocation` no matter which Item G candidate gets chosen, so nothing was ever actually at risk
of being lost. What WAS still wrong: `AllocationPlanProposer.Propose`, the source of Item G's own
"Recommended" candidate, never reflected that balance in its own preview — reading `StartingAllocation =
0` in the picker even when a real one existed, alongside its two siblings
(`ProposeSameSchedule`/`ProposeSameAmount`) which already got this right. Fixed the same way those two
already work: `Propose` gained the identical optional `carriedOverJarBalance` parameter. Full account in
Item G's strategy-choice for an implicit plan proposal (built; see [28](28-refactoring-the-save-confirmation.md)),
Item G's own section, since that's where the candidate picker itself lives.

**One more downstream consideration, raised by the author the same day, recorded not built — a genuinely
separate concern from anything above, tracked in full as its own new state (#9) in
the concerning-states inventory (former planning/19's Stage 6, dropped — see [13](13-adjusting-the-plan-charter.md))
rather than duplicated here:** now that skipping a specific date is possible, a future goal-shortfall
lever (which doesn't exist in any form today — checked, not assumed) shouldn't default to suggesting
the user raise their allocation rate or add a manual earmark. It should first check whether the shortfall
is explained by a plan's own skipped dates that a glut isn't already covering for, and if restoring them
would fix it, suggest that instead — resuming a deliberate pause the user already chose, rather than
asking for money they never agreed to. Same "not yet designed" status as state #8 above — nothing here is
built, on the Earmark form, the Forecast tab, or in the domain layer.

## What prompted this, concretely

`EarmarkFormLivePreviewTests.Editing_a_savings_plans_own_amount_retroactively_rerates_its_whole_history_with_no_protection`
(`redesign/MyMoneyForecast/tests/MyMoneyForecast.App.Tests/`) — a plan funding a $100/month bill, edited
to $120/month with 6 months of real history behind it, silently accumulates a $120 surplus with no
warning and no live-preview indication. Whatever design eventually comes out of this document should
resolve that concrete case, one way or another — either by deciding it's fine (freedom includes this) or
by building the soft-protection confirmation the direction above anticipates.

## Also open, separately: the live preview's own honesty

Not the same question as the above, but raised the same day and worth keeping together with it: the
author isn't fully accepting the "known, accepted gap" framing this session gave
`EarmarkFormPanel.GetLiveJarAmounts` not reflecting a mid-plan `ManualEarmark`
(`EarmarkFormLivePreviewTests.The_live_preview_does_not_reflect_a_mid_plan_manual_earmark_the_real_forecast_does`).
Their own words: "it sounds like the summary preview could be providing misleading information in some
situations." Low priority, explicitly not to be forgotten — a future pass should revisit the Summary
region's live-preview design and capabilities and consider improving it, **even beyond what its own
currently-stated goals require** (planning/22's own content goals for that region might turn out to be
part of what gets reconsidered, not just the implementation). Not scoped further than that here.
