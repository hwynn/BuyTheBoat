# Build Plan: C# + SQLite

Decision made: **C# + SQLite**, per [01-tech-stack-and-testing-strategy.md](01-tech-stack-and-testing-strategy.md). This doc turns that into an actual, concrete starting point — solution layout, package choices, and a phased build order ending in a specific first test to write.

> **Status: scaffolded and green.** The solution described below exists as the `MyMoneyForecast/` folder this planning doc now lives inside (`redesign/MyMoneyForecast/`) — originally scaffolded at the repo root and then moved here so the whole rework lives under `redesign/`. `dotnet build` and `dotnet test` both pass with zero warnings. See [What actually got built](#what-actually-got-built) for the as-built structure and the handful of small deviations from the plan below (all minor — package/format choices, not architecture). Next real work is [the first test](#the-first-test-to-write) — still not written, this was scaffolding only.

*Aside for the record: you mentioned maybe attempting a second, parallel Python implementation after this one, just because the original plan was "cute." Noted — nothing here blocks that; the domain model and test scenarios in this doc are language-agnostic by construction, so they'd carry over as a spec for that attempt too, whenever/if it happens. It would get its own sibling folder under `redesign/`, the same way this one is `redesign/MyMoneyForecast/`.*

## Contents
- [Project structure](#project-structure)
- [Package choices](#package-choices)
- [Architecture: keep the domain pure](#architecture-keep-the-domain-pure)
- [Phased build order](#phased-build-order)
- [The first test to write](#the-first-test-to-write)
- [What actually got built](#what-actually-got-built)

## Project structure

```
MyMoneyForecast.sln
src/
  MyMoneyForecast.Domain/          <- pure domain model. No SQLite/EF/IO references.
  MyMoneyForecast.Persistence/     <- SQLite repositories + schema
  MyMoneyForecast.App/             <- entry point (console/CLI for now — see note below)
tests/
  MyMoneyForecast.Domain.Tests/    <- pyramid layers 1-2 (invariants + calculations). No DB.
  MyMoneyForecast.Scenario.Tests/  <- pyramid layer 3 (acceptance). Domain + Persistence together.
```

One class per file in `Domain/`, matching [02-uml-diagram.md](../../02-uml-diagram.md) exactly: `TransactionLogBook.cs`, `TransactionLogPage.cs`, `AccountTransactionPage.cs`, `FinancialPattern.cs`, `EarMarkPattern.cs`, `ActualTransaction.cs`, `ExpectedTransaction.cs`, `EarMarkEvent.cs`, `BalanceSnapshot.cs`, `FundJar.cs`.

**No UI decision is being made here.** `project goals.txt.txt` flagged "this thing will need a UI... what features don't need the UI?" as an open question back in 2020, and it's still open. `MyMoneyForecast.App` starts as a console/CLI harness purely so the domain and persistence layers have something to run inside — good enough to manually poke at scenarios while the domain logic is still being built, not a real interface. Worth its own separate discussion once the domain answers the four core questions correctly.

**Where does this live in the repo?** Originally proposed as a new top-level folder sibling to `assumptions/`, `classes/`, `redesign/`. Superseded: everything for this rework lives under `redesign/` now, so the solution is at `redesign/MyMoneyForecast/` instead — this doc, its `planning/` subfolder, and the actual `.slnx`/`src/`/`tests/` all live together as one self-contained unit.

## Package choices

| Concern | Choice | Why |
|---|---|---|
| Test framework | xUnit | Modern default for new .NET projects; `[Theory]`/`[InlineData]` covers the date/rrule edge cases cheaply. |
| Assertions | ~~FluentAssertions~~ **Shouldly** | See [What actually got built](#what-actually-got-built) — swapped during scaffolding to sidestep FluentAssertions' commercial-licensing tier entirely. Same idea either way: readable one-line failure messages. |
| Dates | `DateOnly` (built into .NET, no package) | Every date in this domain — `start_date`, `expected_date`, `earmark_date` — is a calendar day with no time-of-day or timezone component. `DateOnly` says that directly and sidesteps `DateTime` timezone footguns entirely; no need for NodaTime here. |
| Recurrence rules | `Ical.Net` (NuGet) — **needs a spike first, not yet added** | Closest .NET equivalent to `dateutil.rrule`. Before committing: confirm it supports everything the assumption catalog actually needs — `BYMONTHDAY` lists (`[9, 25]` in the ODS's worked example), and specifically `UNTIL` vs. `COUNT` (see [05, chart-only content](../../05-assumption-dependency-graph.md#chart-only-content-not-in-any-txt-file) — the chart adds a rule that `EarMarkPattern.date_pattern` must use `UNTIL`, not `COUNT`, which implies whatever library is used needs to make that distinction easy to enforce). |
| SQLite access | `Microsoft.Data.Sqlite` + hand-written SQL (optionally `Dapper` for mapping convenience) — **not EF Core** | See [Architecture](#architecture-keep-the-domain-pure) below for why. |

## Architecture: keep the domain pure

Recommending against EF Core for this, specifically: EF Core wants to own your entity classes (change tracking, lazy-loading proxies, navigation properties shaped around how *it* wants to query) and this domain already has a very precise, hard-won shape described across [01](../../01-glossary-of-terms.md) and [03](../../03-assumptions-glossary.md) — properties that are deliberately immutable after creation (`init`-only), deliberately nullable-until-a-specific-day-arrives (`current_amount`, `full_amount`), deliberately validated in the constructor. Fighting an ORM's opinions about entity shape to preserve that isn't worth it for a single-user local app at this scale.

Instead: `MyMoneyForecast.Domain` stays plain C# with zero persistence references — every domain class validates its own invariants and has no idea a database exists. `MyMoneyForecast.Persistence` holds repository interfaces (`ITransactionLogBookRepository`) and SQLite-backed implementations that translate between rows and fully-formed domain objects. This has a direct, practical payoff for the testing pyramid in [01](01-tech-stack-and-testing-strategy.md#testing-strategy-given-assumptions-can-flex): **layers 1-2 (invariant + calculation tests) never touch SQLite at all** — pure in-memory object construction, fast, no test-database setup/teardown. Only layer 3 (scenario tests) needs a real (temporary, per-test) SQLite file, because only those tests care whether persistence round-trips correctly.

## Phased build order

Revises the "bottom-up through every leaf class" ordering from [01](01-tech-stack-and-testing-strategy.md#suggested-build-order) into a **walking skeleton**: get the thinnest possible slice answering one real question end-to-end (domain → persistence → test) before fleshing out breadth. Classic TDD advice, and it matches "focus on achieving the core planning goals" better than building every class to completion in isolation before anything actually answers a question.

1. **Scaffolding** — solution, the five projects above, package references, one trivial passing test in each test project (proves the pipeline works, nothing more). **Done — see below.**
2. **Walking skeleton for Q1 ("how much free money do I have")** — deliberately the *smallest* version: a single `AccountTransactionPage` with an initial balance, a handful of `ActualTransaction`s, no fund jars, no safety cushion, no earmarks yet. Just enough `FinancialPattern`/`ExpectedTransaction`/`BalanceSnapshot` to compute `full_amount` day-to-day. First scenario test: *given an opening balance and a couple of actual transactions, `CurrentFreeAmount` comes out correct.* Backed by real SQLite persistence from the start, even though the schema is tiny — proves the whole stack, not just the math. **Next up.**
3. **Extend for Q3 ("am I on track for my goals")** — add `EarMarkPattern`, `EarMarkEvent`, `FundJar`, and the milestone-amount calculation. Second scenario test: the boat-fund example from `things to test.txt` / `class documentation.ods`'s worked sheet, since it's already a complete worked numeric example with a known correct answer.
4. **Extend for Q2 ("do I have enough to buy X")** — safety cushion, `priority`/`mandatory`, and the allocation-order logic that decides what happens when free money is tight.
5. **Fill in the harder edge cases** — cross-page flow (Ch.17-19 in [03](../../03-assumptions-glossary.md)), deallocation days, expected/actual pairing and fulfillment. This is where most of the *volume* of the old assumption catalog lives, and where the dependency graph in [05](../../05-assumption-dependency-graph.md) earns its keep as a guide to build order — but it's explicitly not needed for a program that already answers Q1/Q3/Q2 for the common case. `DeallocationProof.ods` (repo root) is worth a look when this phase starts — it wasn't cataloged in the `redesign/00-05` reconstruction and looks directly relevant to `3.13c.a6`-`a8`.
6. **Q4 and beyond** — goal-readjustment-after-an-unplanned-purchase (flagged in [04](../../04-project-goals-and-user-questions.md#q4--if-i-buy-x-anyway-how-do-i-readjust-my-goals) as needing new design, not excavation), the secondary bill-tracking goal, and the UI question. Deliberately last.

## The first test to write

Concretely, this is the test I'd write first (red, then make it pass) — still unwritten as of the scaffolding pass:

```csharp
[Fact]
public void CurrentFreeAmount_reflects_opening_balance_minus_actual_transactions()
{
    var page = new AccountTransactionPageBuilder()
        .WithInitialBalance(1000m)
        .WithActualTransaction(date: new DateOnly(2019, 5, 2), amount: -60m)   // soap
        .WithActualTransaction(date: new DateOnly(2019, 5, 9), amount: 300m)  // paycheck
        .Build();

    var result = page.CurrentFreeAmount(asOf: new DateOnly(2019, 5, 9));

    result.ShouldBe(1240m);
}
```

(Numbers borrowed directly from the worked example in `class documentation.ods`'s `Sheet1` — see [00-sources-and-notes.md](../../00-sources-and-notes.md#class-documentationods--sheet-inventory) — so the expected answer is already known-correct rather than invented.) A `Builder` test-helper is worth introducing from test #1, given how many constructor parameters these domain objects are documented to need — it'll pay for itself immediately.

## What actually got built

.NET version was resolved without needing to block on it: targets **`net10.0`** — the only SDK installed (`10.0.301`) and current LTS anyway.

Structure matches the plan above exactly (five projects, same names, same reference graph: `Persistence` → `Domain`; `App` → both; `Domain.Tests` → `Domain`; `Scenario.Tests` → both). `dotnet build` and `dotnet test` are clean — 0 warnings, 2/2 smoke tests passing. Three small deviations from the plan worth knowing about, none architectural:

- **`MyMoneyForecast.slnx`, not `.sln`.** The .NET 10 SDK's `dotnet new sln` defaults to the newer XML solution format now. Works identically with `dotnet build`/`dotnet test`/`dotnet sln add`.
- **Shouldly instead of FluentAssertions.** FluentAssertions' newer major versions require a paid commercial license above a certain revenue threshold — almost certainly irrelevant for a personal project, but Shouldly (`result.ShouldBe(...)` instead of `result.Should().Be(...)`) is unambiguously MIT-licensed forever, reads just as clearly, and sidesteps the question entirely. Easy to swap later if you'd rather use FluentAssertions anyway.
- **Pinned `SQLitePCLRaw.bundle_e_sqlite3` to `3.0.3` explicitly in `MyMoneyForecast.Persistence`.** The version `Microsoft.Data.Sqlite` pulls in transitively (`2.1.11`) has a known high-severity advisory ([GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)); pinning the newer version resolves it with no code changes needed.

A `.gitignore` (`bin/`, `obj/`, `*.user`, `.vs/`) lives at `MyMoneyForecast/.gitignore` (i.e. `redesign/MyMoneyForecast/.gitignore`) — there wasn't one anywhere in the repo before, and without it the first `git add` would have swept up compiled build output (a `.vs/` folder has already appeared from opening the solution in Visual Studio, correctly ignored). Nothing has been committed; that's still yours to do whenever you're ready.

Two placeholder "smoke test" files exist purely to prove each test project's pipeline runs (`SmokeTests.cs` in both `Domain.Tests` and `Scenario.Tests` — the latter does a real in-memory SQLite round-trip, so it's already proven the persistence stack works end to end, not just that xUnit runs). Both are explicitly commented as placeholders to delete once real tests land — [the first test above](#the-first-test-to-write) is still the next real step, not yet written.
