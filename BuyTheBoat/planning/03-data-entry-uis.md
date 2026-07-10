# Data Entry UIs

You can't do TDD without data to run tests against, and per your own account, data entry was the most time-consuming part of this project historically. This tracks the plan and status for the "quick and dirty" UIs needed before testing can go much further.

## The four data types

1. **Actual transactions — shelved.** Too much data to hand-enter, and no clean source of real transaction data yet (spreadsheet paste / file upload / bank API are all future work, deliberately not started). This means `ExpectedTransaction`-driven behavior is untestable for now too, since expected-vs-actual pairing needs both sides.
2. **Finance/Earmark patterns** — user-created directly, not many of them. Needs a list view of all existing patterns and a separate create-new view/popup. Not yet built (next up after the RRule designer, since pattern creation needs to embed it).
3. **RRules — built, see below.** Called out as needing to happen first, and scoped as its own mini-project.
4. **Current balance + current date** — not yet built. Simple value entry; date defaults to system date but needs an override for testing across different dates.

## Prior art: `mini_fund_project`

Before building anything, worth knowing this was already prototyped once, in Python, using Excel as the UI (`mini_fund_project/` at the repo root, referenced in the request as "MiniFundJars"). It reads a paycheck rrule + amount, a starting balance, one-time goals (each a description + amount + due date), and recurring bills (each its own rrule + amount) from `goals.xlsx`, and computes a per-goal/bill funding schedule plus a running free-to-spend balance — into another spreadsheet, `output.xlsx`. `fund jar mini plan.txt` (in the same folder) is the design scratchpad that led to it; it deliberately used different terminology (`fundingPiles`/`fundingPlans`/`expectedSpendingEvent`) specifically so it wouldn't be confused with the real domain model, with the stated intent to "refine it and get it working smoothly... then maybe recreate it in the money manager project someday."

This matters for two reasons:
- It's a **validated proof that the four core questions can be answered using only patterns + rrules + a balance + a date** — no actual transactions required. That's exactly the subset this C# rework is now limited to, so the ceiling here is "at least match what MiniFundJars did," per your framing.
- `MmfUtility.py`'s `count_to_until_rrule` function is the same "count must become until" conversion already found independently in the assumption chart ([05-assumption-dependency-graph.md, chart-only content](../../05-assumption-dependency-graph.md#chart-only-content-not-in-any-txt-file)) — two independent parts of this project's history arrived at the same rule. `RecurrenceRule.Create` (below) enforces it directly.

## RRule designer — built

### Why this needed its own architecture decision

Recurrence rules aren't a UI concern, they're a domain concept (`FinancialPattern.DatePattern`/`EarMarkPattern.DatePattern` are documented as `rrule`-typed in [01-glossary-of-terms.md](../../01-glossary-of-terms.md)). So the real logic lives in `MyMoneyForecast.Domain`, not in the UI project:

- **`Domain/RecurrenceRule.cs`** — wraps `Ical.Net` (the rrule library flagged as needing a spike in [02-csharp-sqlite-build-plan.md](02-csharp-sqlite-build-plan.md); spike's done, it works). `RecurrenceRuleOptions` accepts either `Until` or `Count` at construction, but **`RecurrenceRule.Until` is the only thing that exists on the constructed object** — `Count` gets resolved to an equivalent date and discarded, mirroring `count_to_until_rrule` and structurally enforcing the "must use until, not count" rule the same way nullable reference types enforce other assumptions elsewhere in this codebase.
- **`Domain.Tests/RecurrenceRuleTests.cs`** — real tests, not throwaway, using actual numbers from `mini_fund_project/goals.xlsx` (the DAILY-interval-14 paycheck, the WEEKLY groceries pattern, the MONTHLY bill-pool pattern) plus the 9th/25th-of-month paycheck from `class documentation.ods`'s worked example. All 9 tests pass. One genuinely useful thing this caught: the "groceries" pattern in `goals.xlsx` has `wkst=MO` and no explicit weekday column — that's the week-start setting, **not** "occurs on Monday." Its actual start date is a Sunday, so it recurs on Sundays. Easy to misread the spreadsheet and get this backwards; there's now a test pinning down the correct (RFC 5545 default-to-start-weekday) behavior.
- **`App/MainWindow.xaml` + `.xaml.cs`** — the actual designer window. `MyMoneyForecast.App` was converted from a console app to WPF for this (it was an empty "Hello World" console harness with nothing worth preserving — see [02-csharp-sqlite-build-plan.md](02-csharp-sqlite-build-plan.md), now `net10.0-windows` + `UseWPF`). Layout: form on the left (frequency, interval, weekday checkboxes, day-of-month list, start date, end condition as either a date or an occurrence count with a live "effective until" readout), a WPF `Calendar` control on the right with occurrence dates highlighted via `SelectedDates`, plus a plain-text scrollable list of every occurrence (the calendar alone can only show one month at a time, so far-future occurrences would otherwise need a lot of clicking through months to spot-check), and a read-only RRULE string readout for the technical view. Recalculates live on every input change.

### What's verified vs. not

Compiles clean (0 warnings across the whole solution) and the domain logic behind it is fully unit-tested. **What I could not verify myself: that the window actually renders and behaves correctly on screen.** There's no GUI-automation tooling available in this environment for a Windows desktop app the way there is for a browser — launching it just confirms the process starts without an immediate crash, not that the layout looks right or that clicking things behaves as expected. Please run it (`dotnet run --project src/MyMoneyForecast.App`, or F5 in Visual Studio) and sanity check it before trusting it further, especially:
- Toggling frequency actually shows/hides the weekday checkboxes vs. the day-of-month box correctly.
- The calendar view lands on the right month and the highlighted dates visually match the text list.
- Switching between "ends on date" and "ends after N occurrences" behaves as expected, including the live-computed "effective until" text.

## Not yet built

- **Pattern list + create view.** Once you've kicked the tires on the RRule designer, the natural next step is turning it into a reusable piece (it's already structured as its own window, not yet extracted into an embeddable `UserControl`, but that's a small refactor) and embedding it in a "create Finance/Earmark Pattern" form, alongside the other pattern fields (amount, description, priority, mandatory, etc. — see [01-glossary-of-terms.md](../../01-glossary-of-terms.md#financialpattern) for the full field list).
- **Balance + date entry.** Simplest of the remaining three — a number field and a date field (defaulting to `DateTime.Today`, overridable). Worth deciding whether "today" being overridable lives as a per-session UI setting or an actual persisted value, given it's explicitly meant to support testing across multiple simulated dates.

## Pattern list + create UI — built

You confirmed you want all three pattern types usable even in this reduced (no actual transactions) version: bills, paychecks, and savings goals. Two domain clarifications came out of that discussion, both now reflected directly in the code:

- **A paycheck is a `FinancialPattern`, not something separate** — bills and paychecks are the exact same type, distinguished only by the sign of `Amount`. `FinancialPattern.Mandatory` defaults to `true` exactly when `Amount` is negative, matching the documented rule precisely.
- **`EarMarkPattern` is deliberately the odd one out** — it doesn't represent real money movement, it represents a self-imposed, flexible savings plan *toward* a goal. Structurally this shows up as `EarMarkPattern.FinanceId` not being its own identity — it's the linked goal `FinancialPattern`'s `FinanceId`, and `EarMarkPattern.Create` takes that goal as a required second argument and validates against it (assumption `3.11.2.a2`: the earmark's date range can't extend beyond the goal's own).

Built, in `src/MyMoneyForecast.Domain/`:
- **`FinancialPattern.cs`**, **`EarMarkPattern.cs`** — both use the same `Options` + `Create()` factory shape as `RecurrenceRule`, for the same reason: derived defaults (mandatory-from-amount-sign) and cross-object validation (earmark-fits-inside-goal) need a place to live that a plain constructor or `record` can't cleanly provide.
- Real tests for both in `Domain.Tests/` — no throwaway placeholders.

Built, in `src/MyMoneyForecast.Persistence/`:
- **`PatternDatabase.cs`** — SQLite file under `%LocalAppData%\MyMoneyForecast\`, *not* next to the binaries, so data survives rebuilds and repeated F5 launches from Visual Studio.
- **`FinancialPatternRepository.cs`**, **`EarMarkPatternRepository.cs`** — a `RecurrenceRule` is stored as six plain columns (frequency, interval, by-day list, by-month-day list, start, until) rather than round-tripped through an RRULE string, since that maps directly onto `RecurrenceRuleOptions` without needing to trust an unverified string-parsing path.
- Round-trip tests against a real (temporary) SQLite file in `Scenario.Tests/`.

Built, in `src/MyMoneyForecast.App/` — this is the part you actually asked about, "a tab or a popup or a frame":
- **`RecurrenceRuleEditor`** — the RRule form + calendar preview from the first pass, pulled out of the old `MainWindow` into a reusable `UserControl` (exposes the computed `RecurrenceRule` via a `Result` property + `ResultChanged` event) so it can be embedded anywhere a date pattern needs entering.
- **`MainWindow`** is now the pattern list — the real startup window, tabbed ("Bills & Paychecks" / "Savings Goals"), reading from SQLite on load. This replaces the old MainWindow-as-RRule-designer from the first pass.
- **`CreateFinancialPatternWindow`**, **`CreateEarMarkPatternWindow`** — popups, each embedding `RecurrenceRuleEditor` alongside the pattern-specific fields. The earmark one only offers goals that already exist (creating one with zero `FinancialPattern`s defined shows a message instead of an empty picker).

Same verification caveat as the RRule designer: compiles clean, all 18 tests pass (15 domain + 3 scenario), the process starts without crashing — but I can't visually confirm the window layout/behavior myself in this environment. You'll want to actually click through it, especially: the tab switch, both "Add New" flows end-to-end, and that an out-of-range earmark attempt shows a sensible error instead of a crash.

One thing to double check on your end since I can't verify it from here: with only `MyMoneyForecast.App` being an executable project, Visual Studio should auto-select it as the startup project, but if F5 doesn't launch it, right-click `MyMoneyForecast.App` in Solution Explorer → "Set as Startup Project."

## Edit, delete, and the simplified one-time-goal flow — built (2026-07-07)

Confirmed the finance pattern round-tripped correctly across app restarts — SQLite persistence works as intended.

**Edit/Delete for both pattern types.** `FinanceId` is locked (disabled) in both edit popups — it's an internal identifier, not user data, and other rows may already reference it. Deleting a `FinancialPattern` that has a linked `EarMarkPattern` is blocked with a message telling you to delete the earmark pattern first (`FinancialPatternRepository.HasLinkedEarMarkPattern`) rather than silently cascading or leaving orphaned data — SQLite doesn't enforce the `FOREIGN KEY` declared in the schema by default, so this is an application-level check, checked either way.

**Design correction, found while building the one-time-goal flow:** `EarMarkPattern.Create` used to require the earmark's date range to fit *entirely inside* its goal's — both start and end. That's wrong for the single most common case: saving in advance for a one-time goal, where the goal itself is a single-occurrence pattern (`Start == Until == due date`) and the earmark necessarily starts well *before* that. Relaxed to only check the upper bound (earmark can't still be allocating funds *after* the goal's due date) — matches what you'd already concluded about earmark patterns not being tied to any specific triggering event's timing. Tests updated to cover both directions explicitly (`EarMarkPatternTests.cs`).

**`CreateOneTimeGoalWindow`** — the simplified flow, on the Savings Goals tab as "Create One-Time Goal...". Asks only for description, amount needed, due date, and a start-saving date (with a "Start today" checkbox), plus one small addition beyond your original four fields: a "how often to save" dropdown (Weekly / Every other week / Monthly, default Monthly) — a lighter substitute for picking full RRule details, not a reintroduction of "pick an allocation trigger." No `Goal` type exists in the documented model or in code — this implicitly creates a `FinancialPattern` (single occurrence on the due date, `Mandatory = false` even though the amount is negative, since a discretionary goal isn't a bill) and a linked `EarMarkPattern` (installment amount = amount needed ÷ number of occurrences between start and due date, evenly split and rounded to the cent). The actual construction logic lives in `Domain/OneTimeGoalFactory.cs`, unit-tested (`OneTimeGoalFactoryTests.cs`) rather than buried in the popup's code-behind — this is real business logic (how to split a goal into installments), not UI plumbing.

Now 26 tests total (21 domain + 5 scenario), all passing, 0 build warnings.

See [04-forecast-timeline-tab.md](04-forecast-timeline-tab.md) for the next planned piece — a timeline/forecast tab, requested in the same conversation but deliberately not built yet since it needs real balance-calculation logic that doesn't exist in the domain model so far.

## Priority over timing, and explaining Mandatory — built (2026-07-07)

**One-time goals no longer ask about savings timing by default.** The "how often to save" dropdown from the previous pass turned out to still be asking the wrong question — per the user, exactly when allocation happens is expected to be dynamically reorganized by priority once deallocation logic exists (`3.13c.a6`-`a10` in [03-assumptions-glossary.md](../../03-assumptions-glossary.md#chapter-11-balance-record); this is still on the not-yet-built list, same as the forecast tab), so hand-picking a cadence up front is asking the user to plan around a mechanism that's supposed to be automatic. Replaced with **Priority** (added to `OneTimeGoalRequest`, threaded to the goal `FinancialPattern.Priority`, default `3`) as the always-visible question, with the frequency picker demoted to a collapsed "Advanced: choose a specific savings schedule" `Expander` for anyone who wants to override the default (still Monthly).

**Priority's meaning is now spelled out inline everywhere it's asked**, since the convention (1 = lowest, higher = higher priority) isn't self-evident from a bare number and the user flagged exactly that ambiguity. `CreateOneTimeGoalWindow` uses a labeled `ComboBox` ("1 — Low: nice to have, fine to delay" ... "5 — High: protect this before lower-priority goals"); `CreateFinancialPatternWindow` keeps a free-entry field (since edit mode needs to support arbitrary existing values, including ones outside a 1-5 range, like the `4`/`7`/`9` seen in the ODS's own worked examples) but with the same "1 = lowest, higher = higher priority" caption directly underneath.

**Mandatory is now explained inline** in `CreateFinancialPatternWindow`, directly answering "what does false imply": checked means an obligated bill (rent, utilities, debt) that outranks non-mandatory patterns when money is short; unchecked means discretionary — a paycheck, a subscription, a planned purchase, or a savings goal, all nice-to-have-but-skippable in the model's terms. This also surfaced that the checkbox wasn't actually using the "derive from amount sign" convenience already built into `FinancialPattern.Create` (`Mandatory: bool?` — null defers to the sign-based default) — the UI always sent an explicit value, so that domain feature was dead from the UI's perspective. Fixed: the checkbox now live-updates to match the amount's sign as you type, until you (or, in edit mode, the previously-saved value) touch it explicitly, at which point it stops auto-following.

**"Create Bill..." button** added next to the general "Add New (advanced)..." on the Bills & Paychecks tab — same `CreateFinancialPatternWindow`, opened with `forcedMandatory: true`, which hides the Mandatory section entirely rather than just pre-checking it (one less decision for the one case where the answer is always the same).

Now 27 tests total (22 domain + 5 scenario), all passing, 0 build warnings.

## Replacing the +/− amount sign with Expense/Income — built (2026-07-07)

You flagged that typing a leading "−" to mean "this is a bill" is bad UX even though it's logically fine — and pointed out the "Create Bill" shortcut didn't actually fix that, since it only automated Mandatory, not the sign. You proposed a checkbox + relabeling; after weighing a checkbox against plain-language radio buttons, two separate debit/credit fields, and a compact inline toggle, went with **radio buttons** (a checkbox's meaning still depends on a well-written label; two named options read correctly with no label-reading required) — you confirmed this directly.

`CreateFinancialPatternWindow`'s amount section is now: an "Expense" / "Income" radio pair, then an amount field that's always a plain positive number — its label switches between "Amount owed" and "Amount received" depending on the selection. `Math.Abs()` is applied to whatever's typed before the direction sign is applied, so a stray "−" typed out of habit can't silently double-negate. In "Create Bill" mode, the whole Expense/Income question is hidden (same treatment as Mandatory) — `ExpenseRadioButton` defaults to checked in XAML regardless of visibility, so hiding it doesn't need a separate forced-direction code path. The Mandatory auto-suggestion (added in the previous pass) now keys off the radio selection directly rather than re-parsing the amount's sign, which is simpler and doesn't depend on the amount field containing a valid number yet.

`CreateEarMarkPatternWindow`'s amount field had the same issue, stated even more explicitly in its old label ("usually negative — money leaving free balance into the fund jar"). Since a user-authored earmark pattern is *always* an allocation in the current scope — deallocation is the system's dynamic response to a low balance, not something a user hand-schedules as its own recurring pattern — there was no real direction to ask about. Fixed the same way "Create Bill" fixes Mandatory: the field takes a plain positive magnitude, and the sign is hardcoded (`-Math.Abs(...)`) rather than asked.

`CreateOneTimeGoalWindow`'s "how much do you need?" field was already sign-free (always positive, negated internally by `OneTimeGoalFactory`) — no change needed there; it's now the pattern the other two windows were brought in line with, not an exception to it.

No domain-layer changes — this was UI-layer only (parsing/labeling in code-behind), so the test count is unchanged at 27.
