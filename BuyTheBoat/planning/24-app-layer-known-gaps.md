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
