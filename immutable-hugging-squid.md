# Forecast Tab — Balance/Date Entry + Projection Engine

## Context

`MyMoneyForecast` (the C#/.NET 10 + SQLite rewrite at `redesign/MyMoneyForecast/`) currently has two tabs for entering *patterns* (bills/paychecks, savings goals) but nothing that answers the actual questions the app exists for: how much free money is there, and is the user on track for their goals. The user wants a third tab — the app's main/output screen — where you enter today's balance and today's date, hit a button, and see a forecast shaped like `mini_fund_project/output.xlsx` (the validated Python+Excel prototype that already answers these questions with zero real transaction data).

This requires building the calculation engine from scratch — nothing in `MyMoneyForecast.Domain` today computes a running balance or tracks goal progress; only `RecurrenceRule`, `FinancialPattern`, `EarMarkPattern`, and `OneTimeGoalFactory` exist. Real bank-transaction import is permanently shelved for now (too much data to hand-enter, no source hooked up), which rules out large parts of the originally-documented model (transaction pairing, deviation warnings, deallocation days) — none of that can trigger without real transaction data, so whatever gets built is necessarily a pure "everything happens exactly as scheduled" projection.

Two architectures were fully designed and compared (a lean purpose-built engine vs. building the documented `BalanceSnapshot`/`FundJar`/`AccountTransactionPage` classes). Conclusion: migration between them later is cheap, because neither approach implements the genuinely hard future work (persisted incremental recalculation, actual-transaction pairing, deallocation, cross-page continuity) — both are, today, a stateless projection computed fresh from patterns + a seed balance. The "faithful" naming would cost roughly 2x the files/tests now to buy a mechanical rename later. **Decision: build the lean engine only, as part of the existing `MyMoneyForecast` solution — no parallel project.**

## Decisions locked in with the user

- **Engine**: new, purpose-built domain types (not the documented `BalanceSnapshot`/`FundJar`/`AccountTransactionPage` names). Formulas will be comment-annotated with their corresponding assumption IDs (`3.13.3.a1`, `3.13.5.4.a1`, etc.) so a future rename/migration has a direct map back to the documented model, the same way `FinancialPattern.cs` already cites `4.2.a1` inline.
- **Display**: two `DataGrid`s (day-by-day timeline + per-goal status), no chart, no dynamic per-date columns — reuses the existing static-column, flat-row-class pattern from `FinancialPatternsGrid`/`EarMarkPatternsGrid`.
- **Horizon**: fixed 3 months ahead of the entered date, no UI control for it. Matches the user's own minimal description of the tab (date field, amount field, one button, otherwise empty) and the "roughly 3 months" confident look-ahead already referenced in `01-glossary-of-terms.md`.
- **Explicitly not modeled this pass** (matches the pre-existing agreed build order in `02-csharp-sqlite-build-plan.md`, which puts safety cushion/priority allocation in the *next* phase, not this one): safety cushion, priority-based reallocation when money is short, actual transactions, transaction pairing, deviation warnings, deallocation days. No per-goal manual starting-balance entry either — see the jar-seeding design below for why that's not needed.

## Domain layer — new files in `src/MyMoneyForecast.Domain/`

All pure, no persistence references, one class per file (matches existing convention). `ForecastCalculator` is a stateless static computation over already-validated domain objects, so it doesn't need the `Options` + `Create()` validation ceremony `FinancialPattern`/`RecurrenceRule` use — there's nothing to validate that isn't already validated.

- **`ForecastOptions.cs`** (record) — `FinancialPatterns`, `EarMarkPatterns` (both `IReadOnlyList<>`, read via the existing `FinancialPatternRepository`/`EarMarkPatternRepository`), `StartingBalance` (decimal), `AsOfDate` (DateOnly), `HorizonEndDate` (DateOnly — computed by the caller as `AsOfDate.AddMonths(3)`, kept as an explicit input so the engine itself doesn't hardcode "3").
- **`Forecast.cs`** (record, result) — `AsOfDate`, `HorizonEndDate`, `Days` (`IReadOnlyList<ForecastDay>`), `GoalShortfalls` (`IReadOnlyList<GoalShortfall>`), `HasNegativeFreeBalance` (bool), `FirstNegativeFreeBalanceDate` (DateOnly?).
- **`ForecastDay.cs`** (record) — `Date`, `Balance` (running total from `FinancialPattern` events), `FreeBalance` (`Balance` minus the sum of all jar balances that day), `JarBalances` (`IReadOnlyDictionary<int, decimal>` keyed by `FinanceId`), `Events` (`IReadOnlyList<ForecastEvent>` for that day).
- **`ForecastEvent.cs`** (record + enum `ForecastEventKind { Income, Expense, EarmarkContribution }`) — `FinanceId`, `Label`, `Amount`, `Kind`.
- **`GoalShortfall.cs`** (record) — `FinanceId`, `Label`, `DueDate`, `AmountNeeded`, `AmountAllocatedByDueDate`, `ShortfallAmount` (computed property, `Max(0, AmountNeeded - AmountAllocatedByDueDate)`).
- **`ForecastCalculator.cs`** (static class, single entry point `Create(ForecastOptions options) -> Forecast`). Algorithm, in order:
  1. **Balance line** (~`3.13.3.a1`): gather every `FinancialPattern.DatePattern.GetOccurrences(options.AsOfDate, options.HorizonEndDate)` occurrence as a `ForecastEvent`, group by date, walk forward from `StartingBalance` summing each day's events.
  2. **Jar lines — seeded from each pattern's own start, not from `AsOfDate`** (~`3.13.5.3.a1`): for each `EarMarkPattern`, compute its cumulative contribution as of `AsOfDate` by summing **all** occurrences from `EarMarkPattern.DatePattern.Start` through `AsOfDate` (inclusive) — this is the fix for a goal that's already partway through its savings schedule (e.g., started 2 months ago) correctly showing a non-zero jar balance today, which reduces `FreeBalance` from day one instead of pretending every jar starts at zero. Then extend day-by-day through `HorizonEndDate` the same way as the balance line.
  3. **`FreeBalance` per day** (~`3.13.4.a1`): `Balance[day] - Sum(JarBalances[*, day])`.
  4. **Emit one `ForecastDay` per date with at least one event**, plus force-include `AsOfDate` itself as the seed row even if nothing happens that day (mirrors the documented "one snapshot per event-date" rule from `3.13.a3`, and guarantees the grid always has a starting row).
  5. **`HasNegativeFreeBalance`/`FirstNegativeFreeBalanceDate`**: scan the emitted days.
  6. **`GoalShortfall` — evaluated independently per goal, NOT bounded by `HorizonEndDate`** (~`3.13.5.4.a1`): for each `FinancialPattern` that has a linked `EarMarkPattern` (i.e., each goal), walk the earmark's occurrences from its own `Start` to the goal's own due date (`FinancialPattern.DatePattern.Until`) and compare the total against `Math.Abs(FinancialPattern.Amount)`. This deliberately ignores the display horizon — a goal due in 8 months still gets a correct on-track/short verdict even though it won't appear in the (3-month) day-by-day grid, which avoids a false "on track" reading for anything due later than the timeline shows.

## Persistence — `src/MyMoneyForecast.Persistence/`

- **`PatternDatabase.cs`**: add one table to `Initialize()`, following the existing `CHECK (Id = 1)` single-row convention already used for the rest of the schema:
  ```sql
  CREATE TABLE IF NOT EXISTS CurrentBalance (
      Id INTEGER PRIMARY KEY CHECK (Id = 1),
      Balance TEXT NOT NULL,
      AsOfDate TEXT NOT NULL
  );
  ```
  Lives in the same file Export/Import already copies wholesale, so both features pick this up with zero changes to `OnExportClick`/`OnImportClick`/`LooksLikeValidDatabaseFile`.
- **`CurrentBalanceRepository.cs`** (new) — `Save(decimal balance, DateOnly asOfDate)` (upsert via `ON CONFLICT`, same style as `FinancialPatternRepository`), `GetCurrent()` returning a nullable result for first-run (no row yet).

## UI — `src/MyMoneyForecast.App/`

- **`MainWindow.xaml`**: third `TabItem` header `"Forecast"`, inline content (no new `UserControl` — matches how the existing two tabs are inline in `MainWindow`, not extracted; `RecurrenceRuleEditor` was only pulled out because it's reused across three separate popups, which doesn't apply here). Export/Import buttons need no changes — they already sit above the `TabControl`.
  - `DatePicker` "As of", `TextBox` "Current balance", `Button` "Forecast".
  - Caption text under the inputs: *"Forecast only — assumes every bill and paycheck happens exactly as scheduled. No real transactions are tracked yet."* — resolves the labeling question raised in `04-forecast-timeline-tab.md`.
  - Two `DataGrid`s, `AutoGenerateColumns="False"`, matching `FinancialPatternsGrid`'s style:
    - **Timeline grid**: Date, Balance, Free Balance, Events (a joined summary string, e.g. "Paycheck +$300, Rent -$1200").
    - **Goal status grid**: Goal, Due Date, Amount Needed, Allocated by Due Date, Shortfall (blank/$0 when on track).
- **`MainWindow.xaml.cs`**: on load, call `CurrentBalanceRepository.GetCurrent()` and pre-fill the two inputs if a row exists (date defaults to `DateTime.Today` otherwise, matching the existing "overridable for testing across simulated dates" requirement). `OnForecastClick`: parse inputs, `CurrentBalanceRepository.Save(...)`, build `ForecastOptions` from `_financialPatterns.GetAll()` / `_earMarkPatterns.GetAll()` (already-constructed repositories, reused as-is) with `HorizonEndDate = asOfDate.AddMonths(3)`, call `ForecastCalculator.Create(...)`, bind both grids.
- **`ForecastRows.cs`** (new, following the `PatternRows.cs` convention) — `ForecastDayRow` and `GoalStatusRow` flat display-shaping classes.

## Testing — chokepoints

- **`ForecastCalculatorTests.cs`** (`Domain.Tests`, no DB):
  1. **Single-pattern projection** — port the existing `$1000 − $60 + $300 = $1240` oracle from `02-csharp-sqlite-build-plan.md`, adapted to run through `ExpectedTransaction`-equivalent `FinancialPattern` occurrences rather than `ActualTransaction`s.
  2. **Same-day aggregation** — two occurrences landing on the same date sum rather than overwrite.
  3. **Goal milestone accumulation** — a goal starting at/after `AsOfDate` (e.g., $1000 needed, 5 monthly $200 installments); assert cumulative jar totals at each installment and an empty `GoalShortfalls`.
  4. **Already-in-progress goal** (new oracle, not covered by either prior design pass) — a goal whose `EarMarkPattern.Start` is *before* `AsOfDate`; assert the jar balance on `AsOfDate` already reflects the pre-`AsOfDate` installments, and `FreeBalance` is reduced accordingly from day one.
  5. **Deliberate shortfall** — an earmark schedule that doesn't reach the goal amount by the due date; assert one `GoalShortfall` with the correct `ShortfallAmount`.
  6. **Negative free balance** — a bill exceeding the starting balance; assert `HasNegativeFreeBalance` and `FirstNegativeFreeBalanceDate`.
  7. **Horizon boundary** — an occurrence exactly on `HorizonEndDate` is included; one day past is excluded from the timeline (but see #8).
  8. **Goal beyond the horizon** — a goal due after `HorizonEndDate` still gets a correct `GoalShortfall`/on-track verdict, proving the goal-evaluation pass is genuinely horizon-independent.
- **`CurrentBalanceRepositoryTests.cs`** (`Scenario.Tests`, real temp SQLite) — save/`GetCurrent` round-trip, upsert-not-duplicate, `null` on a fresh database.

## Phased build order

1. `ForecastOptions`/`Forecast`/`ForecastDay`/`ForecastCalculator` for `FinancialPattern`s only (no jars) — port oracle #1 first.
2. Add `EarMarkPattern` jar accumulation (including the already-in-progress seeding fix) — oracles #3 and #4.
3. Add `GoalShortfall` computation — oracle #5.
4. Harden edges — oracles #2, #6, #7, #8.
5. `CurrentBalanceRepository` + schema change + repository tests.
6. UI: third tab, `ForecastRows.cs`, click-handler wiring.
7. Manual verification pass (see below).

## Verification

- `dotnet build` / `dotnet test` from `redesign/MyMoneyForecast/` — clean, 0 warnings, all tests (existing 27 + new ones) passing.
- Manual click-through (same convention already established for every other window in this app, since there's no WPF UI-automation tooling available in this environment): launch the app, open the Forecast tab, enter a balance/date, click Forecast, confirm both grids populate sensibly; restart the app and confirm the balance/date pre-fill from the saved value; verify Export/Import still work unchanged (the new table rides along automatically).

## Effort estimate

~9-10 new files (6 domain, 1 persistence, 1-2 UI) + edits to `MainWindow.xaml`/`.xaml.cs` and `PatternDatabase.cs`. ~18-22 new test cases, bringing the suite from 27 to roughly 45-49.

## Risks

- `RecurrenceRule.GetOccurrences` hasn't been exercised yet with several patterns combined over a multi-month range at once — this is the first consumer to do that.
- Sign-convention bugs when combining `FinancialPattern` (signed) and `EarMarkPattern` (always-negative-in-practice) amounts — mitigate by testing the already-in-progress and shortfall oracles explicitly, not just the happy path.
- Hand-built (non-`OneTimeGoalFactory`) recurring `EarMarkPattern`s have simpler shortfall math than a real amortization schedule would — acceptable for the dominant one-time-goal case, worth flagging if the user builds one by hand later.
