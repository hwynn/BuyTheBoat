# Data Entry UIs

You can't do TDD without data to run tests against. This tracked the plan and status for the
"quick and dirty" UIs needed before testing could go much further. **Status, 2026-08-08: all four
data types below are built** (actual transactions remain the one deliberately-shelved exception) —
this is now a reference for how they're organized and why a few things aren't the obvious first
guess, not a to-do list.

## The four data types

1. **Actual transactions — still shelved.** Too much data to hand-enter, and no clean source of
   real transaction data yet (spreadsheet paste / file upload / bank API are all future work,
   deliberately not started). `ExpectedTransaction`-driven behavior stays untestable in this
   dimension until it lands.
2. **Finance/Earmark patterns — built.** List view + create/edit popups for both types, plus a
   simplified one-time-goal flow. See "What's built" below.
3. **RRules — built**, as a reusable `RecurrenceRuleEditor` `UserControl`, embedded wherever a date
   pattern needs entering.
4. **Current balance + current date — built**, on the Forecast tab.

## Prior art: `mini_fund_project`

Before building anything, this was already prototyped once, in Python, using Excel as the UI
(`mini_fund_project/` at the repo root, "MiniFundJars"). It reads a paycheck rrule + amount, a
starting balance, one-time goals, and recurring bills, and computes a per-goal/bill funding
schedule plus a running free-to-spend balance. It's a **validated proof that the four core
questions can be answered using only patterns + rrules + a balance + a date** — no actual
transactions required — which is exactly the subset this C# rework targets. `MmfUtility.py`'s
`count_to_until_rrule` function independently arrived at the same "count must become until" rule
found in the assumption chart
([06-assumption-dependency-graph.md](../../06-assumption-dependency-graph.md#notes-on-the-charts-notation-and-coverage));
`RecurrenceRule.Create` enforces it directly.

## What's built

- **`Domain/RecurrenceRule.cs`** — wraps `Ical.Net`. Recurrence rules are a domain concept, not a
  UI concern (`FinancialPattern.DatePattern` is documented `rrule`-typed), so this logic lives in
  `MyMoneyForecast.Domain`, not the App project. `RecurrenceRuleOptions` accepts either `Until` or
  `Count`, but only `Until` survives construction — `Count` resolves to an equivalent date and is
  discarded, structurally enforcing "must use until, not count."
- **`App/RecurrenceRuleEditor`** — the RRule form + live calendar preview, as a reusable
  `UserControl` (exposes `Result` + `ResultChanged`).
- **`App/MainWindow`** — the pattern list, tabbed ("Bills & Paychecks" / "Savings Goals").
- **`CreateFinancialPatternWindow`**, **`CreateEarMarkPatternWindow`**, **`CreateOneTimeGoalWindow`**
  — create/edit popups; the earmark one only offers goals that already exist.
- **`src/MyMoneyForecast.Persistence/`** — `PatternDatabase.cs` (SQLite under
  `%LocalAppData%\MyMoneyForecast\`, not next to the binaries, so data survives rebuilds), plus a
  repository per pattern type. A `RecurrenceRule` is stored as six plain columns rather than
  round-tripped through an RRULE string, since that maps directly onto `RecurrenceRuleOptions`
  without trusting an unverified parsing path.

## Design decisions worth remembering

- **A paycheck is a `FinancialPattern`, not something separate** — bills and paychecks are the same
  type, distinguished only by the sign of `Amount`. `Mandatory` defaults to `true` exactly when
  `Amount` is negative.
- **`EarMarkPattern` is deliberately the odd one out** — it doesn't represent real money movement,
  it represents a self-imposed savings plan *toward* a goal; `FinanceId` is the linked goal's, not
  its own. Its date-range-vs-goal containment rule (assumption `3.11.2.a2`) went through a real
  revision: relaxed to upper-bound-only so saving-in-advance could start before the goal's own
  range, then **reverted 2026-07-28** (see [15-stage2-pattern-lifetime.md](15-stage2-pattern-lifetime.md))
  once `ActiveFrom` gave the goal itself a reaching-back start date, so the containment rule could
  hold literally instead of being relaxed.
- **Priority convention: 1 = lowest, higher = higher priority** — spelled out inline everywhere
  it's asked, since it isn't self-evident from a bare number. Priority is the always-visible
  question on the one-time-goal flow, not a savings-cadence picker — allocation timing is meant to
  be dynamically reorganized by priority once deallocation logic exists, so hand-picking a cadence
  up front was asking the wrong question.
- **Expense/Income radio buttons, not a sign to type.** Typing a leading "−" to mean "this is a
  bill" was flagged as bad UX. Considered a checkbox, plain-language radio buttons, separate
  debit/credit fields, and a compact toggle — went with radio buttons, since a checkbox's meaning
  still depends on a well-written label while two named options read correctly on their own.
  `Math.Abs()` is applied before the direction sign, so a stray "−" typed out of habit can't
  double-negate.

See [04-forecast-timeline-tab.md](04-forecast-timeline-tab.md) for the forecast/timeline tab that
followed this work.
