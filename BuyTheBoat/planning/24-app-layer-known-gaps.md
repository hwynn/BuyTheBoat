# 24 — App-layer known gaps (no test project exists yet)

**A living registry.** `MyMoneyForecast.App` has no test project — only `Domain.Tests` and
`Scenario.Tests` exist. The 2026-08-08 doc-comment cleanup pass (trimming overly-narrative
comments across the codebase) keeps surfacing real, specific concerns in xaml.cs files that would
otherwise deserve a test if one were possible — this is where those go instead of staying as a
wall of prose on the function they describe. Append here whenever that cleanup pass finds another
one; check here once an App-layer test project exists and it's time to mine this list, or whenever
you need the full story behind one of these gaps.

## Entries

### EarmarkFormPanel.GetOneOffLiveDelta — a backdated One-off entry shows a stale live preview

**Where:** `src/MyMoneyForecast.App/EarmarkFormPanel.xaml.cs`, `GetOneOffLiveDelta`.

**The gap:** a release does not reset a jar's `ExpectedAmount` to exactly 0 — it only subtracts
the goal's own fixed `Amount` (`TransactionLogBookFactory.AppendDeallocationOrGoalReleases`), so
whatever the jar held above that amount is left behind. `GetOneOffLiveDelta` only reflects a
typed One-off adjustment in the Summary aside when its date falls strictly after the goal's own
most recent release (or on/after the plan's `ActiveStart`, if it hasn't released yet) and on or
before today. A date backdated to before that release doesn't replay the real day-by-day cascade,
so the live preview shows no change even though saving the entry for real would shift today's
balance. A real forecast run always computes the correct number regardless — only the live
*preview*, before Save, is affected.

**How to reproduce:** open Car Insurance Co's savings plan (seed data,
`tools/SeedData/Program.cs`) in One-off mode, pick Feb 1, 2026 (before its own Apr 1, 2026
release), type any amount — the "Fund jar, today" aside should not move. If it does, this gap has
already been closed; re-read `GetOneOffLiveDelta`'s current body before trusting the rest of this
note.

**What a real fix needs:** replaying the actual day-by-day cascade
(`TransactionLogBookFactory.CreateForecast`'s per-day loop — floor-at-0, the leftover-after-release
carry described above) from the backdated date forward, not a shortcut computed in the UI layer.
That's either a new domain function built for this specific purpose, or accepting the cost of a
full `RequestForecast()` re-run with the proposed entry inserted.

**What a test would look like, once there's somewhere to put it:** a scenario-level test can
already assert the *save* path is correct — that a One-off entry dated before a goal's most recent
release, once saved, changes that day's `ExpectedAmount` in the next real forecast. The *live
preview* gap itself is UI-layer and would need either UI test automation or the logic extracted
out of code-behind into something Domain-testable.

### Account/Expense saves don't refresh the shown forecast — Earmark's does — FIXED 2026-08-27

**Fix:** both save paths now recompute the shown forecast before landing on the Forecast tab, the same
way Earmark's save already did. `AccountForm.AccountSaved` and the `ExpenseForm.PatternSaved` "Save and
skip planning" branch each call `RefreshForecast(shown.AsOfDate, shown.HorizonEndDate)` (guarded on
`_lastForecast is { } shown`) after persisting. The "Save and Plan" branch was already covered separately
the same day by `NavigateToEarmarkForm` recomputing the forecast before opening the plan form (which also
fixed the stale-Summary gap in `EarmarkFormPanel.UpdateSummary`). WPF-lifecycle wiring, no test harness —
verified by a clean build and the full suite staying green.

**Where:** `src/MyMoneyForecast.App/MainWindow.xaml.cs`, the `AccountForm.AccountSaved` and
`ExpenseForm.PatternSaved` callbacks (constructor).

**The gap:** saving a bill, paycheck, goal, or account edit persists correctly and refreshes the
grids, but never calls `RefreshForecast`/`RefreshShownForecast` — so a forecast already on screen
keeps showing stale numbers until the user presses Forecast again. `EarmarkForm`'s save callbacks
do call these. Not a regression (the old popup handlers never refreshed the shown forecast either),
but it now reads as an inconsistency sitting next to Earmark's live refresh, which is new behavior
from the same permanent-tab pass that never got carried over to Account/Expense.

**How to reproduce:** compute a forecast, edit a bill's amount on the Expense tab and save, switch
back to Forecast — the calendar and totals still reflect the old amount. Re-running the forecast
corrects it.

**What a fix needs:** call `RefreshForecast(shown.AsOfDate, shown.HorizonEndDate)` (guarded on
`_lastForecast is { } shown`, matching `EarmarkForm.PatternSaved`'s own callback) from both
`AccountForm.AccountSaved` and `ExpenseForm.PatternSaved`.

### Editing an early (already-superseded) segment of a break-off/renewal chain has no guard at all — FIXED 2026-08-17

**Fix:** wired up planning/15/16's own original "silent redirect" ruling at last — opening ANY segment
of a break-off/renewal chain for editing now always lands on the CURRENT one, no warning, no escape
hatch, exactly as designed. `ExpenseFormPanel.LoadPattern` — the one place both entry points (the grid's
own "Edit Selected" and this form's own instance picker, confirmed by tracing every caller) funnel
through — now calls `BreakOffFactory.FindCurrentSegment(existing, _allPatterns)` first and loads
whatever it returns instead of the picked pattern verbatim; a no-op for the vast majority of patterns
(anything not part of a chain returns itself, unchanged). `ExpenseFormPanel.SetContext` gained a new
`allPatterns` parameter to make the search possible — `EarmarkFormPanel.SetContext` already received its
own full goal list for its picker; `ExpenseFormPanel` simply hadn't needed it for anything else before
this. The account shown is re-resolved against the redirected pattern's own FinanceId when a redirect
actually happens (rare — a chain crossing accounts), not just inherited from the picked row. No new
domain code needed: `BreakOffFactory.FindCurrentSegment` already existed and is already covered by
`BreakOffFactoryTests`; this was purely a wiring gap. 445 tests still green (unchanged — this is WPF UI
code with no direct test harness, same as the rest of `MainWindow`/`ExpenseFormPanel`; verified by a
clean build, the full suite staying green, and the app still launching), 0 warnings.

**Where (as originally found):** `src/MyMoneyForecast.App/FinancePatternSaveConfirmation.cs` (nothing
checked this before letting a Critical edit proceed) and `BreakOffFactory.FindCurrentSegment`
(`src/MyMoneyForecast.Domain/BreakOffFactory.cs`) — the "latest `Start` wins" heuristic that would
silently mis-resolve this if it happened. Found 2026-08-16 while answering the author's own direct
question ("do we have solid rules for a change to an early finance pattern cascading to the
continuing one"), not via the usual doc-comment cleanup pass — logged here anyway since it's a real
App-layer behavioral gap and this is where those go.

**The gap:** planning/15/16's own "silent redirect" ruling — opening any segment of a chain for
editing should always land on the CURRENT one, no exceptions — was designed but never wired in. What
got built instead (2026-08-13) is `ExpenseFormPanel.UpdateContinuityNote`, a passive caption ("This
pattern continues an earlier one...") sourced from `FindPredecessor`/`FindSuccessor` — informational
only; nothing redirects or blocks. So today, nothing stops a user from directly opening and editing an
already-superseded predecessor segment through the instance picker.

If they do, and the edit touches a restricted field (`start_date`/`amount`/recurrence shape) — which
it will, since an old segment's own occurrences are by now entirely in the past, so
`MightAlterPast`/`IsChangeCritical` fires — the default path (Item C's break-off) creates a brand-new
successor sharing the SAME `Source` as the predecessor, with nothing checking whether that predecessor
is still the head of its own chain first. If a real successor already exists further down the line,
this produces a second, competing branch off the same `Source`, and `FindCurrentSegment`'s own rule
("whichever pattern sharing this Source has the latest `Start` wins") can't tell an intentional
continuation from an accidental one apart: the new branch's own `Start` is always today (the cut date
is pinned to "today, literally"), so it silently wins over the real successor the instant that real
successor's own `Start` is already in the past — i.e., the moment it's the one actually active and
running.

**How to reproduce:** break off a pattern once (predecessor + successor exist, successor already
started), then open the *predecessor* directly through the instance picker (nothing prevents this) and
edit its `Amount`, accepting the default "break off" choice. `FindCurrentSegment(predecessor,
allPatterns)` now returns the new branch, not the real successor.

**What a fix needs:** either wire up the original silent-redirect ruling (block/redirect before a
stale segment can even be opened for editing at all — the more thorough fix, matching what was
actually designed), or, narrower, have `FinancePatternSaveConfirmation`/`BreakOffFactory.BreakOff`
refuse (or ask) when the pattern being broken off isn't `FindCurrentSegment`'s own result for itself.
**Took the first, more thorough option** — see the fix note at the top of this entry. The narrower,
save-layer option is no longer needed on top of it: once the form can never load a stale segment in the
first place, `BreakOffFactory.BreakOff` can never be called with one either, for this entry point at
least — a defense-in-depth check there would only guard against some other, not-yet-known way to reach
it, not a gap that's actually known to exist today.

### Item G's "keep the same schedule/amount" choice is permanently disabled the first time a Savings Plan is ever restructured — FIXED 2026-08-16

**Fix:** `RestructureFactory.FindCurrentPlan` (new) tells a genuine sequential chain apart from F27's
concurrent-funder shape — non-overlapping active spans among the plans sharing a finance_id means a
chain, and the one with the latest `Start` is picked as the current segment; any pairwise overlap
returns null (concurrent, correctly still excluded). `DeterminePlanShapeCandidatesIfApplicable` now
calls this instead of bailing out on `HasMultipleEarmarkPatterns`/`.SingleOrDefault()`.
`MultiPlanBreakOffRequest` gained its own `ChosenSuccessorPlan` field (mirroring `BreakOffRequest`'s),
and `PerformMultiPlanBreakOff` now matches `ChosenPlanShape` back to a real candidate the same way
`PerformSingleSuccessorBreakOff` already did. 4 new domain tests (`RestructureFactoryTests`), 2 new
app-layer tests (`FinancePatternSaveConfirmationTests` — one proving candidates now build from the
current segment specifically, one regression-locking that a genuinely concurrent predecessor still
gets none). 400 tests green (up from 394), 0 warnings.

**Where (as originally found):** `FinancePatternSaveConfirmation.DeterminePlanShapeCandidatesIfApplicable` and
`PerformMultiPlanBreakOff` (`src/MyMoneyForecast.App/FinancePatternSaveConfirmation.cs`); found
2026-08-16 while answering the author's own question about whether `EarMarkPattern` changes should ever
cascade across a `FinancialPattern` break-off boundary — see
[27](27-editing-within-a-patterns-chain.md) for the bigger, still-open question this sits next to.

**The gap:** `DeterminePlanShapeCandidatesIfApplicable` (planning/25's Item G — offering "keep the same
schedule" / "keep the same amount" alongside a break-off's freshly-proposed default) returns immediately
whenever `HasMultipleEarmarkPatterns` is true, before building any candidates at all. `HasMultipleEarmarkPatterns`
comes from `EarMarkPatternsFor(_financeId)`, which filters only by `FinanceId` — no date filtering, so it
counts *every* `EarMarkPattern` row ever saved under that id, truncated/expired or not (truncated
segments are kept, not deleted, by design — same reasoning as a break-off's own predecessor). So this
isn't limited to genuinely concurrent plans: **the first time a Savings Plan is ever restructured
(`RestructureFactory`, even once, even years ago, even if only one segment is live today) permanently
disables Item G for every future break-off of that same bill.** `PerformMultiPlanBreakOff`'s own request
type (`MultiPlanBreakOffRequest`) doesn't even have a field to receive a chosen shape — the multi-plan
path always calls `AllocationPlanProposer.Propose` fresh, unconditionally.

**How to reproduce:** restructure a Savings Plan's rate once (`RestructureFactory.Restructure`), then
later make a Critical edit to the bill itself that would trigger a break-off. Confirm
`_planShapeCandidates` stays empty regardless of how carefully the restructured rate was tuned — no
"keep the same schedule"/"keep the same amount" option ever appears, even though exactly one segment is
actually active.

**What a fix needs:** build Item G's candidates from whichever segment is *currently active* — the same
"latest `Start` wins" resolution `BreakOffFactory.FindCurrentSegment` already uses for `FinancialPattern`
chains, applied here to `EarMarkPatternsFor(_financeId)` instead of bailing out the moment more than one
row exists. Narrower than the bigger cross-`FinancialPattern`-boundary question in
[27](27-editing-within-a-patterns-chain.md) — this only fixes the moment a break-off itself happens, not
whether a later edit to an already-superseded segment should reach forward into an already-existing
successor's own plan.

### A concurrent EarMarkPattern could be silently truncated, absorbed, or amount-overwritten by an unrelated plan's edit — FIXED 2026-08-16

**Fix:** extracted `RestructureFactory.SpansOverlap(a, b)` from `FindCurrentPlan`'s own existing pairwise
overlap check (one shared definition instead of a second copy) and used it to filter
`FinancePatternSaveConfirmation.RunForPlan`'s own `otherPlans` *before* `hasPredecessor`/`hasSuccessor`
are computed — the same filtered list already flows into `PerformEarmarkSave`'s own
`predecessors`/`successors`, so one fix protects both the confirmation ask and the actual mutation. 6 new
regression tests: 4 direct `SpansOverlap` cases (`RestructureFactoryTests`) plus 2 end-to-end
(`FinancePatternSaveConfirmationEarmarkTests`) proving a concurrent plan survives an unrelated edit
completely untouched — and that `ConfirmImplicitChanges` is never even invoked — under both the boundary
and cascade paths. 429 tests green (up from 423), 0 warnings.

**Where (as originally found):** `FinancePatternSaveConfirmation.RunForPlan`
(`src/MyMoneyForecast.App/FinancePatternSaveConfirmation.cs`); found 2026-08-16 while grounding the
[27](27-editing-within-a-patterns-chain.md) UI-wiring work in source before writing it, the same
discipline [[feedback-reground-in-source-before-building]] calls for — not via a user report.

**The gap:** `hasPredecessor`/`hasSuccessor` compared only `Start` against the plan being saved, with no
check for whether the "neighbor" actually forms a sequential chain versus being a genuinely concurrent,
overlapping plan (F27 — e.g. the "Storage Unit Rental" seed scenario's own two household-partner
funders). A concurrent plan with a differing `Start` satisfied the same check a real chain neighbor
would. Left unguarded: extending `Until`/`Start` on one concurrent plan into its partner's own
overlapping span would silently truncate or fully absorb (delete) the partner via
`RestructureFactory.ExtendUntil`/`ExtendStart` — neither of which has any concept of "this isn't really a
successor," they just do what they're told — and an `Amount` edit could silently overwrite the partner's
own independent rate via `CascadeForward`, under the "cascade forward" default. Directly violates
planning/27's own settled rule that concurrent plans "must NOT get the same treatment as a sequential
chain." The underlying mechanism (`ExtendUntil`/`ExtendStart`/`CascadeForward`) was built and tested
correctly against sequential scenarios only, earlier in the same session — this failure mode was inert
(`RunForPlan` had no real caller yet) until the UI-wiring work below made it reachable from an actual save
for the first time, which is why it surfaced now rather than when the mechanism was first built.

**How to reproduce (pre-fix):** seed a goal with two `EarMarkPattern`s under one `finance_id` whose active
spans overlap (e.g. Jan 1 – Dec 31 and Mar 1 – Aug 31). Load the wider one and shrink its own `Until` to,
say, Jun 30 — still inside the narrower plan's own span. Pre-fix, `hasSuccessor` was true (the narrower
plan's `Start` is later), so `PlanTouchesChainBoundary` fired and the default "stay linked" answer called
`ExtendUntil`, which rewrote the narrower, unrelated plan's own `Start` to Jul 1 — silently dropping its
Mar–Jun coverage.

### ExtendStart silently skipped a predecessor landing exactly on the new Start — FIXED 2026-08-17

**Fix:** `ExtendStart`'s own outer filter (`otherPlans.Where(plan => plan.DatePattern.Start < current.DatePattern.Start)`)
used a strict `<`, but `current.DatePattern.Start` is already the NEW, proposed Start by the time any
real caller invokes this (every caller passes it as both `current` and `newStart`) — so a predecessor
whose own `Start` landed EXACTLY on the new Start failed this filter and was silently skipped, never even
reaching the loop's own (already-correct) `newStart <= plan.DatePattern.Start` absorb check. Changed `<`
to `<=` in both `RestructureFactory.ExtendStart` (`EarMarkPattern` chains) and
`BreakOffFactory.ExtendStart` (`FinancialPattern` chains, same bug, same fix). Full account, including
why the existing domain tests never caught it, in
[27](27-editing-within-a-patterns-chain.md#what-phase-1-actually-built--2026-08-17).

**Where:** `src/MyMoneyForecast.Domain/RestructureFactory.cs` and `.../BreakOffFactory.cs`, both
`ExtendStart` methods. Found 2026-08-17 while writing `BreakOffFactory.ExtendStart`'s own app-layer test
for planning/27's Phase 1 — not via a user report.

**The gap:** since `EarmarkFormPanel`'s own savings-plan editing was already fully UI-wired
(`RestructureFactory.ExtendStart`, built and reachable from a real save), this was live and reachable in
the app, not just latent domain code: choosing "stay linked" (the default) after moving a plan's own
`Start` back to land exactly on an existing predecessor's own `Start` — a real way to fully absorb it —
silently did nothing to that predecessor at all, leaving it in place unabsorbed, contrary to what the
default answer promised. 4 new regression tests (2 domain, one per file, deliberately modeling the real
calling convention; the app-layer test that caught it, in `FinancePatternSaveConfirmationChainTests`).

### BuildSuccessorSchedule silently drifted a Weekly pattern's own cadence on break-off — FIXED 2026-08-17

**Fix:** `BuildSuccessorSchedule` always set `Start = cutDate` directly on the successor's own schedule,
copying `Frequency`/`Interval`/`ByDay`/`ByMonthDay` through unchanged. For Monthly/Yearly (which always
carry an explicit `ByMonthDay`) this is harmless — ical.net finds the right day regardless of where
`Start` falls. For **Weekly**, it broke in two distinct, independently-confirmed ways: an omitted `ByDay`
is implicitly tied to `DTSTART`'s own weekday per RFC 5545, so the successor's occurrences silently
retargeted to `cutDate`'s own weekday instead of the pattern's actual one (the same bug class already
fixed in `AllocationPlanProposer.ProposePaced` on 2026-08-14) — and, confirmed empirically rather than
just reasoned about (`RecurrenceRuleTests.Explicit_byday_alone_does_not_protect_an_intervals_own_week_phase_when_start_is_pinned_elsewhere`),
an `Interval > 1` rule's own "every Nth week" is *also* counted from `DTSTART`'s own calendar week even
with an **explicit** `ByDay` — so pinning `Start` at `cutDate` could land the whole cadence a full
interval-step off regardless of whether the weekday was spelled out. `BuildSuccessorSchedule` now branches
on `Frequency`: non-Weekly is untouched (`Start = cutDate`, exactly as before — every existing Monthly
break-off test depends on this); Weekly reuses `AllocationPlanProposer.AlignedSchedule` (made `public` for
this) to re-anchor `Start` at the reference pattern's own next real occurrence on or after `cutDate`, with
`ActiveFrom = cutDate` carrying "active from the cut" separately whenever that lands later.
`BreakOffFactory.ValidateCutBoundaries` was updated to match — it now checks the successor's own
`ActiveStart` (`ActiveFrom ?? Start`) rather than raw `Start`, since `ActiveFrom` is what actually
represents "no gap, no overlap" against the truncated predecessor once `Start` itself can legitimately
land later. 4 new tests (2 domain — the raw ical.net finding above, and the original implicit-`ByDay`
symptom's own regression test converted from scratch code into a permanent one; 2 app-layer — implicit and
explicit `ByDay` both proven to keep the correct phase end to end, plus a Monthly-pattern test locking in
that the common case is unaffected).

**Where:** `src/MyMoneyForecast.App/FinancePatternSaveConfirmation.cs` (`BuildSuccessorSchedule`),
`src/MyMoneyForecast.Domain/BreakOffFactory.cs` (`ValidateCutBoundaries`), `.../AllocationPlanProposer.cs`
(`AlignedSchedule`, visibility only — its own logic was already correct and needed no change). Found
2026-08-17 by inspection while grounding the paycheck-association cascade (not via a user report),
flagged as its own background task, then fixed directly in this session once the user handed the same
task prompt back for immediate work. A genuinely separate, spun-off session for the same task had already
been started via the flagged chip by the time this began — the user was asked directly rather than risking
duplicate or conflicting work, and chose to have it done here.

**The gap:** reachable via the real UI, not just a test artifact — `RecurrenceRuleEditor`'s own `ByDay`
checkboxes let a real user leave every weekday unchecked for a Weekly pattern, and any Weekly pattern with
`Interval > 1` (biweekly, etc.) was affected regardless of `ByDay`. A bill or paycheck on a biweekly
schedule, edited after it already had history (triggering the default "break off, preserve the past"
path), would have its successor's own cadence silently drift — either to the wrong weekday, or to the
right weekday one interval-step late — with nothing in the UI surfacing that anything had changed.

### Two silent no-ops in the multi-plan "keep separate" paths, plus a related EarmarkConsolidation bug — FIXED 2026-08-17

**Fix:** found while auditing every path through the confirmation popup for anything that should ask or
warn but silently didn't, on direct request ("make sure everything I want to be a warning or a question is
in there"). Two combinations of `IsChangeCritical && HasMultipleEarmarkPatterns` used to save **nothing at
all** for the plan side — one of them saved nothing for the `FinancialPattern` itself either — whenever the
user picked (or, via `DefaultConfirmationAnswer`, silently defaulted to) "keep them separate," because that
option was never actually built for either case, just offered as if it were:
- **Break-off side, any field combination:** `PerformImplicitEarmarkChanges` used to `return` with nothing
  saved at all once `HasMultipleEarmarkPatterns` was true and "keep separate" was chosen — not even the
  bill's own edit landed anywhere (`PerformSave`'s own guard already skips it on this path). Now always
  falls back to `PerformMultiPlanBreakOff` (the same consolidation the shape-changed case already used)
  instead of doing nothing.
- **Retroactive-correction side, a `start_date` change specifically (not amount-only):** `ConsolidationNeeded`
  used to check only `_recurrenceShapeChanged`, so a pure `start_date` change sailed past it into the same
  unbuilt "keep separate" branch — a genuine crash risk, not just a missing feature: every surviving plan
  was left with a now-stale `ActiveStart` against the goal's own newly-moved one, which
  `EarMarkPattern.Create`'s validation rejects the next time anything reads them back.
  `ConsolidationNeeded` is now `HasMultipleEarmarkPatterns && (_recurrenceShapeChanged || _startChanged)`,
  closing this the same way.

Writing the regression test for the widened trigger's own retroactive-correction path surfaced a **third,
related bug**: `EarmarkConsolidation.Consolidate` (built 2026-08-13) always sized the consolidated plan's
own `Start` as the earliest surviving plan's own `ActiveStart`, with no clamp against the goal's own —
correct for the only scenario it had ever been exercised against (a shape change, where the goal's own
`Start` never moves), but the same validation throw the moment `Start` genuinely moves too. Clamped forward
to the goal's own `ActiveStart` when that lands later; confirmed via tracing that this has no effect on the
money math (`GetOccurrences` already self-clamps to the goal's own real `Start` regardless of how early a
lower bound it's asked from).

Since "keep separate" is no longer honestly offered in every case it used to silently pretend to support, a
new `ConsolidationCaveat` warns ahead of the choice (only when relevant — an amount-only, multi-plan,
Critical edit) that picking "break off" above will always combine plans regardless of what's picked here.
`ConsolidationForcedText`'s own wording became request-driven (`ConsolidationForcedReason`) rather than a
hardcoded XAML string, since it now needs to correctly describe a `start_date` reason too, not just a
schedule one. 3 new/rewritten tests (`FinancePatternSaveConfirmationTests`), plus the pre-existing
`A_break_off_with_multiple_surviving_plans_kept_separate_is_a_safe_no_op_for_now` test — which had pinned
the old silent-no-op as correct — renamed and rewritten to assert the fixed, consolidating behavior instead.

**Where:** `src/MyMoneyForecast.App/FinancePatternSaveConfirmation.cs`
(`DetermineConditions`/`PerformImplicitEarmarkChanges`), `src/MyMoneyForecast.Domain/EarmarkConsolidation.cs`
(`Consolidate`). Found 2026-08-17 while auditing the confirmation system end to end on direct request, not
via a user report of the crash itself.

**A related, NOT-yet-fixed gap surfaced the same day, flagged as its own background task rather than
patched here:** the retroactive-correction side's OTHER known limitation —
`DetermineNarrowingPlanIfApplicable` returning early, unable to narrow a single surviving plan when the
goal's own new `ActiveStart` lands before the forecast's own `AsOfDate` (no safe way to read a balance from
before it) — got a real warning surfaced to the user this same day (`NarrowingLimitationWarning`, see
below) instead of total silence. That surfacing is what exposed a genuine crash risk this method's own
narrower, single-plan gap always had: the goal still saves with its new, earlier `ActiveStart`, but the
plan is left untouched at its own old one — the identical `ActiveStart < goal.ActiveStart` invariant
violation as above, just for the single-plan case, and with no safe fallback available (absorbing a wrong
balance was already, deliberately, ruled out). Confirmed via a test that deliberately stops short of
asserting the post-save `EarMarkPatterns.GetAll()` read, with a comment explaining why. Needs real design
work — not solved here.

### The Concerning popup, the plan-disambiguation picker, and Item E's own concrete-consequence wording — BUILT 2026-08-17

**Built:** three more genuine gaps from the same audit, each a real "should ask or warn but doesn't" rather
than a silent-failure risk:
- **`AskForSuggestions` (the Concerning popup)** — was a total no-op. Now reads a fresh `PlanHealthState`/
  `FundJar` off `_navigationFinanceId` and shows `PlanHealthMessages.CurrentJarStateLine`'s own existing
  sentence (the same wording the Earmark form's own passive Summary aside already uses) via a new
  `ShowSuggestion` delegate, matching `ConfirmImplicitChanges`/`NavigateToEarmarkForm`'s own callback idiom
  so this class stays WPF-free. Deliberately minimal — **not** the elaborate strategy-picker (pre-fill
  Suggested/Working-state values) planning/25's own "Still open" section describes and explicitly defers;
  its own specific strategies were never designed. `MainWindow` shows it as a plain `MessageBox`, matching
  this project's own existing acknowledge-only convention.
- **`AskWhichEarmarkPatternToOpen`'s own disambiguation** — used to always pick the first surviving plan as
  a placeholder. New `EarmarkPatternPickerWindow` (mirrors `FinancialPatternPickerWindow`'s own shape — a
  plain grid + Select/Cancel, Amount/date range/starting allocation as the columns that actually
  distinguish one segment from another) wired through a new `PickEarmarkPattern` delegate, same idiom.
- **Item E's own "show the consequence, not just a yes/no"** — planning/25's long-standing requirement,
  never built. New `AlterPastConsequence`/`DescribeAlterPastConsequence` names the SPECIFIC absorbed
  balance and/or deleted manual earmarks `_narrowingPlan` already works out, shown only while "apply it
  everywhere" is the currently-selected `AlterPastSection` radio — the same "warning escalates with the
  currently-selected option" pattern `ChainBoundarySection` already established, not a change to the
  generic top-level description.

4 new tests covering all three, each driving `Run()` end to end. **Suite: 495 green** (352 domain + 58
scenario + 85 app, up from 489), 0 warnings on a clean rebuild. Verified the app still launches clean.

**Where:** `src/MyMoneyForecast.App/FinancePatternSaveConfirmation.cs`,
`.../EditingHistoryConfirmationWindow.xaml(.cs)`, `.../MainWindow.xaml.cs`, and two new files,
`.../EarmarkPatternPickerWindow.xaml(.cs)`. Found 2026-08-17 via the same direct audit as the entry above.
