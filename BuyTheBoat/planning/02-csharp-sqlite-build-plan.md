# Build Plan: C# + SQLite

Decision made: **C# + SQLite**, per [01-tech-stack-and-testing-strategy.md](01-tech-stack-and-testing-strategy.md).
This doc was the concrete starting point — solution layout, package choices, and a phased build
order ending in a specific first test to write.

**Status, 2026-08-08: superseded by the actual build.** The scaffolding-stage content this doc used
to carry in full — the phased build order, "what actually got built" as of the first scaffolding
pass (2/2 smoke tests, nothing committed yet) — described a project that's since grown into a real
WPF app with hundreds of tests across `MyMoneyForecast.Domain`, `.Persistence`, and `.App`; that
history isn't useful to read anymore, and is deleted rather than kept as dead weight. What's kept
below, per the same "record for a possible future Python attempt" note this doc always carried: the
still-governing architecture decision (domain stays pure, no ORM) and the one worked test example
explicitly flagged as language-agnostic spec material.

*Aside for the record: you mentioned maybe attempting a second, parallel Python implementation after
this one, just because the original plan was "cute." Noted — nothing here blocks that; the domain
model and the test example below are language-agnostic by construction, so they'd carry over as a
spec for that attempt too, whenever/if it happens. It would get its own sibling folder under
`redesign/`, the same way this one is `redesign/MyMoneyForecast/`.*

## Architecture: keep the domain pure

`MyMoneyForecast.Domain` stays plain C# with zero persistence references — every domain class
validates its own invariants and has no idea a database exists. `MyMoneyForecast.Persistence` holds
repository interfaces and SQLite-backed implementations that translate between rows and fully-formed
domain objects. Deliberately not EF Core: EF Core wants to own your entity classes (change tracking,
lazy-loading proxies, navigation properties shaped around how *it* wants to query), and this domain
already has a precise, hard-won shape — properties deliberately immutable after creation
(`init`-only), deliberately nullable-until-a-specific-day-arrives, deliberately validated in the
constructor. This is still the real architecture (`src/MyMoneyForecast.Domain`,
`src/MyMoneyForecast.Persistence` as they exist today), not just a plan for it.

This also has a direct, practical payoff for the testing pyramid in
[01](01-tech-stack-and-testing-strategy.md#testing-strategy-given-assumptions-can-flex): layers 1-2
(invariant + calculation tests, `MyMoneyForecast.Domain.Tests`) never touch SQLite at all — pure
in-memory object construction. Only layer 3 (`MyMoneyForecast.Scenario.Tests`) needs a real,
temporary SQLite file, because only those tests care whether persistence round-trips correctly.

## A worked test example (language-agnostic, per the note above)

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

Numbers borrowed directly from the worked example in `class documentation.ods`'s `Sheet1` — see
[00-sources-and-notes.md](../../00-sources-and-notes.md#class-documentationods--sheet-inventory) —
so the expected answer is already known-correct rather than invented. (This exact test, or its
direct descendant, is long since written and passing — this is kept as an example of a
known-correct scenario, not a to-do.)
