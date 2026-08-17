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

### Account/Expense saves don't refresh the shown forecast — Earmark's does

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

### Editing an early (already-superseded) segment of a break-off/renewal chain has no guard at all

**Where:** `src/MyMoneyForecast.App/FinancePatternSaveConfirmation.cs` (nothing checks this before
letting a Critical edit proceed) and `BreakOffFactory.FindCurrentSegment`
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
