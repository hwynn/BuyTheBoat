# Forecast/Timeline Tab — Planned, Not Built

Requested 2026-07-07, deliberately **not built in the same pass** as the pattern list/edit/delete work in [03-data-entry-uis.md](03-data-entry-uis.md) — this needs real balance-calculation logic that doesn't exist yet in `MyMoneyForecast.Domain`, and deserves its own focused pass rather than being bolted onto UI CRUD work. This doc exists so the requirements aren't lost between now and then.

## What it needs to show

In your words: a timeline of paycheck occurrences and allocation (earmark) events — which don't have to land on the same dates — plus how much is sitting in each fund jar at any point along that timeline, and whether the user is falling short of their goals.

This is, almost exactly, the UI surface for the three core questions that are actually well-supported by the documented model ([04-project-goals-and-user-questions.md](../../04-project-goals-and-user-questions.md)):
- Q1 (how much free money do I have) → the running balance column.
- Q3 (am I on track for my goals) → `FundJar.milestone_amount` vs. `current_amount`, per goal, per date — this tab *is* the shortfall warning.
- Q2 (can I afford X) is a hypothetical layered on top of this same timeline, not a separate view.

## Prior art: `mini_fund_project/output.xlsx`

Worth reproducing the shape of, not the exact calculation. Structure observed: dates run across columns (not down rows), with a header row of dates, an "Income" row (paycheck amounts on paycheck dates only), a running "Total Moneys" balance row, and then a repeating 4-row block per bill/goal (`description`, `should have saved` — i.e. the running milestone amount for that bill/goal — spaced every 4 rows down), ending in "Allocated" and "Free to spend" summary rows across the bottom.

**You flagged that the underlying calculation was buggy** — specifically an off-by-one when counting paychecks (`make_bill_plan` in `MmfUtility.py`), bad enough that you had to enter an adjacent paycheck date to get correct output. **Take the shape of `output.xlsx` as a reference for what to display, not the algorithm that produced it.** The actual computation for the real tab should be built from the documented assumption model instead — which was written before this bug existed and doesn't share it:

- [03-assumptions-glossary.md, Chapters 11-16](../../03-assumptions-glossary.md#chapter-11-balance-record) — the `BalanceSnapshot`/`FundJar` maintenance rules this tab would be visualizing directly (`3.13.2.a4` full_amount, `3.13.5.4.a1` milestone_amount, etc.).
- [06-assumption-dependency-graph.md, Process regions](../../06-assumption-dependency-graph.md#process-regions--the-cascade-steps) — already maps out the cascade-update control flow (the nested loop structure) this tab's underlying engine would need to implement, including the deallocation-day handling that governs what happens when actual spending outpaces free funds on a given day.

## Why this wasn't built yet

Everything built so far (`FinancialPattern`, `EarMarkPattern`, `RecurrenceRule`, `OneTimeGoalFactory`) describes *patterns* — recurring rules for what should happen. Nothing yet computes what actually *has* happened on a timeline: there's no `BalanceSnapshot`, no `FundJar` (the runtime one that tracks `current_amount`, as opposed to the pattern that generates earmark events), and no cascade engine walking day-by-day applying patterns to a starting balance. That's a genuinely different, larger piece of work than pattern CRUD — it's the actual "answer the four questions" engine — and building it well deserves its own planning pass rather than being squeezed in alongside this one.

## Open questions for that future pass

- Since actual transactions are shelved, this timeline is necessarily a pure *forecast* (all `ExpectedTransaction`-driven, nothing reconciled against real bank activity) — worth deciding whether that's labeled explicitly in the UI so it doesn't read as more certain than it is.
- Whether to build the real `BalanceSnapshot`/cascade machinery per the full documented assumption set, or a deliberately simplified version scoped to just what this tab needs first (matching how `RecurrenceRule`/`FinancialPattern`/`EarMarkPattern` were each scoped to just what their immediate UI needed, not the full documented property set up front).
- Display shape: `output.xlsx`'s dates-as-columns layout works for a spreadsheet; a WPF tab might read better as a chart (running balance as a line, fund jars as stacked areas) plus a detail grid, rather than a literal reproduction of the spreadsheet's cell layout.
