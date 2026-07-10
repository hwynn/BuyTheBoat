# Tech Stack & Testing Strategy — Options

> **Decided: C# + SQLite.** See [02-csharp-sqlite-build-plan.md](02-csharp-sqlite-build-plan.md) for the concrete follow-up. Keeping the options analysis below as-is for the record — it's why we landed here, and the Python + XML path is still described in case the "second implementation, just for fun" idea mentioned alongside the decision ever happens (if it does, it'll get its own sibling folder under `redesign/`, the same way this one has `redesign/MyMoneyForecast/`).

Three things changed since [04-project-goals-and-user-questions.md](../../04-project-goals-and-user-questions.md) and [05-assumption-dependency-graph.md](../../05-assumption-dependency-graph.md) were written, and they all push in a compatible direction:
1. Language is open — Python or C#.
2. Storage is open — XML or a local database.
3. **The assumptions are no longer the spec — they're a guide.** "The assumptions are guidelines for logic flow and testing. But they can change a little bit as long as the application achieves the basic goals." That's a real shift from treating all ~150 assumption IDs as things that must each individually hold, to treating the four core questions ([04](../../04-project-goals-and-user-questions.md)) as the actual target and the assumption graph as hard-won guidance on *order of operations and edge cases*, not a checklist to satisfy 1:1.

## Contents
- [Language: Python vs. C#](#language-python-vs-c)
- [Storage: XML vs. a local database](#storage-xml-vs-a-local-database)
- [Testing strategy given "assumptions can flex"](#testing-strategy-given-assumptions-can-flex)
- [Suggested build order](#suggested-build-order)
- [Open questions for you](#open-questions-for-you)

## Language: Python vs. C#

### Option A — C# / .NET

The strongest argument for switching isn't performance or tooling preference, it's that **a large fraction of the assumption set is exactly what C#'s type system enforces for free.** Look back at [03-assumptions-glossary.md](../../03-assumptions-glossary.md) — Chapters 1-8 alone are almost entirely "X cannot be None" / "X must be =" rules:

```csharp
public sealed class FinancialPattern
{
    public required int FinanceId { get; init; }          // 4.1.a1 — enforced, not tested
    public string? Description { get; init; }              // optional field stays nullable
    public required RecurrencePattern DatePattern { get; init; }
    public required decimal Amount { get; init; }
    // amount_tolerance defaults to (0,0) rather than allowing null — 4.2.a1-style rules
    // become constructor defaults instead of runtime checks.
}
```

With `<Nullable>enable</Nullable>` turned on, the compiler refuses to build code that could pass `null` into a non-nullable property — an entire category of the assumption catalog (roughly 20-25 of the ~150 IDs, mostly in Ch.1-8) stops being something you write a unit test to verify and becomes something that's simply not expressible. That's not a small win given the stated goal of not over-investing in tests for things that don't serve the four core questions.

Other things that fit this domain well:
- **`record` types** for the value-object-like classes (`ExpectedTransaction`, `ActualTransaction`, `EarMarkEvent`, `FundJar`) give structural equality for free — directly useful for the duplicate-detection logic described in `psuedo_functions.txt` ("given an actual transaction, check if it has a duplicate").
- **`init`-only properties** encode "cannot be changed after creation" (e.g. `BalanceSnapshot.SnapshotDate`, per its description in [01-glossary-of-terms.md](../../01-glossary-of-terms.md#balancesnapshot)) directly in the type, not as a convention.
- **LINQ** is a near 1:1 match for the lookup-heavy pseudocode in `psuedo_functions.txt` — `find_actual(p_date)`, `find_expected(p_date, p_finance_id)`, `earmark_exists(...)` are all one-line `.Where()`/`.FirstOrDefault()` calls instead of hand-rolled loops.
- **xUnit** for TDD — `Theory`/`InlineData` make the many date/rrule edge cases ([Chapter 17 in 03](../../03-assumptions-glossary.md#chapter-17-log_pages-finance-pattern--earmark-pattern-continuity-across-pages), cross-page pattern continuity) cheap to parameterize.
- Recurrence rules: `Ical.Net` (NuGet) is the closest equivalent to Python's `dateutil.rrule`. Worth a short spike to confirm it covers everything `project goals.txt.txt`'s rrule research called out (BYMONTHDAY lists, UNTIL vs. COUNT — note [05](../../05-assumption-dependency-graph.md#chart-only-content-not-in-any-txt-file) found a chart-only rule that `EarMarkPattern.date_pattern` specifically requires `UNTIL`, not `COUNT`) before committing.

Cost: this is a rewrite from zero, not a continuation. None of `classes/*.py` carries over.

### Option B — Python (continue existing work)

- `classes/*.py` already exist for all 10 domain classes and are a real head start, even if — per your own git history — mid-refactor.
- `dateutil.rrule` was already researched and is proven (the `recurring_ical_events` package is already vendored in the project folder).
- `pytest` is lightweight and very well suited to TDD; `pytest.mark.parametrize` covers the same ground as xUnit's `Theory`.
- `dataclasses(frozen=True)` gets you most of the way to C#'s `record` equality/immutability story, and Pydantic (if you wanted a middle ground) adds *runtime* validation closer to what C#'s nullable types give at *compile* time — worth considering as a half-step if you stay in Python but still want the assumption-catalog's "cannot be None" rules enforced somewhere other than hand-written tests.
- Cost: none of the type-system leverage described above — every nullability/shape assumption stays a test you have to write and maintain yourself, which cuts against the "don't over-invest in testing things that don't serve the core goals" instruct.

### My read

If you're genuinely undecided, the type-system argument is the deciding factor for me: it directly reduces the amount of testing work needed for exactly the category of assumption you said you're willing to be loose about, while costing you the existing Python head start (which, by your own account, isn't that far along). If the existing `classes/*.py` code represents more sunk investment than I'm giving it credit for, that tips it back toward Python + Pydantic as the middle ground. Flagging as a question below rather than deciding it here.

*(Resolved: C#. `classes/*.py` is being kept purely as an archive/reference, not built on.)*

## Storage: XML vs. a local database

### Option A — SQLite

A real relational database, single file, zero server process — about as close to "just as easy as XML" as a database gets. The interesting property: some of the assumption catalog's *structural* rules map directly onto schema constraints instead of application code:

- `9.5.1.a1` ("all fund jars must have a unique finance_id") → `UNIQUE(balance_snapshot_id, finance_id)`
- `4.1.a1` ("finance_id cannot be None") → `NOT NULL` column
- `3.10.a2` ("finance_patterns cannot contain any duplicate ids") → a unique constraint instead of a loop-and-check

This doesn't replace application-level tests for the *behavioral* assumptions (the cascade-update math, pairing logic — the ones that actually matter for the four core goals), but it does mean a chunk of the "guideline" assumptions get enforced by the storage layer as a cheap defense-in-depth, again for free, again for exactly the category of rule you said doesn't need to be precious about.

In C#: `Microsoft.Data.Sqlite` directly, or EF Core on top of it if you want migrations and LINQ-to-SQL. In Python: the `sqlite3` standard library module, or SQLAlchemy as an ORM.

### Option B — LiteDB (C# only)

An embedded, single-file, NoSQL *document* database — you store `TransactionLogBook` (or each `AccountTransactionPage`) as a serialized document, matching the nested containment tree in [02-uml-diagram.md](../../02-uml-diagram.md) far more directly than relational tables would. No schema migrations to manage, still has LINQ query support, still a single portable file like XML is today. The tradeoff versus SQLite is exactly the tradeoff between the two families in general: less upfront schema design and easier fit to the object graph, versus no constraint enforcement at the storage layer and weaker guarantees around cross-document consistency (e.g. the `finance_id` "join" between `FinancialPattern` and `EarMarkPattern`/`FundJar`/`EarMarkEvent` — see [02-uml-diagram.md](../../02-uml-diagram.md#why-so-many-relationships-are-dashed--key-based-instead-of-solid-arrows) — is enforced by application code either way, but a relational DB at least gives you the option of a real foreign key later).

### Option C — Keep XML

Zero new dependencies, human-readable, diffable in git, and the `xmlDepth` sheet in `class documentation.ods` already maps the exact intended node structure (see [00-sources-and-notes.md](../../00-sources-and-notes.md#class-documentationods--sheet-inventory)). The honest cost: every structural guarantee in the assumption catalog — every uniqueness rule, every required-relationship rule — stays entirely in application code and its tests, forever. Given you're explicitly trying to *reduce* how precious the implementation needs to be about the assumption catalog, this is the option that gives you the least outside help doing that.

### My read

SQLite, if only because the constraint-enforcement argument mirrors the type-system argument above: cheap, structural, "for free" enforcement of the boring assumptions so your tests and your judgment can focus on the four core goals. LiteDB is the better choice specifically if the nested-object-graph shape matters more to you than schema constraints — worth a real answer from you on which property you value more, since I can argue either side.

*(Resolved: SQLite.)*

## Testing strategy given "assumptions can flex"

This is the part I'd push back least on, so I'm proposing it more directly rather than laying it out as a menu. A three-layer testing pyramid resolves the tension in your framing (still TDD, but assumptions aren't gospel):

1. **Domain invariant tests** (unit level) — a *curated* subset of the old assumption catalog: the ones that are cheap, stable, and still worth guaranteeing explicitly (uniqueness, required relationships, the calculation formulas like `current_free_amount` and `milestone_amount`). If you go C#+SQLite, a good number of Chapters 1-8's assumptions move out of this layer entirely (enforced by the type system / schema instead) — this layer should end up meaningfully smaller than "one test per assumption ID."
2. **Behavioral/calculation tests** (unit or small-integration level) — the cascade-update math, pairing/fulfillment logic, deallocation-day handling: the assumptions that encode genuinely tricky, easy-to-get-wrong logic (Chapters 11-16 and 17-19 in [03](../../03-assumptions-glossary.md)). Build and test these bottom-up following the dependency graph's existing layering in [05](../../05-assumption-dependency-graph.md#the-dependency-graph-by-chapter) — that ordering is the single most valuable thing to keep from the old assumption work, independent of whether every individual assumption survives unchanged. The `assumptionChartTests.uxf` test-bundle plan ([05, Test plan](../../05-assumption-dependency-graph.md#test-plan-from-assumptioncharttestsuxf)) is a ready-made starting point for grouping these instead of writing 150 tiny tests.
3. **Scenario/acceptance tests** (top level) — built from `things to test.txt`'s worked examples (the boat-fund scenario) and the ODS's matching numeric example, written directly against the four core questions in [04](../../04-project-goals-and-user-questions.md): "given this scenario, does `current_free_amount` come out right," "does the goal-tracking answer match," etc. **These are the tests that actually prove the app achieves the basic goals**, and they're the layer that should almost never change even if the assumptions underneath get revised — which is exactly what makes this structure compatible with "assumptions can change a little as long as the application achieves the basic goals." If an assumption changes but every scenario test still passes, the change was safe by definition.

TDD still applies in the normal red-green-refactor sense at every layer — this isn't a proposal to test less rigorously, it's a proposal to point the *first* and most-protected tests at the goals rather than at the assumption catalog, and let the assumption-derived tests be the ones that are allowed to move.

## Suggested build order

Independent of language/storage choice, the natural build order is the one the dependency graph already gives you, since it's a genuine topological dependency order, not just a documentation convenience:

1. Leaf classes with no dependencies — `FinancialPattern`, `EarMarkPattern`, `ActualTransaction`, `ExpectedTransaction`, `EarMarkEvent`, `BalanceSnapshot`, `FundJar` base rules (matches the `bg=green` test bundles in [05](../../05-assumption-dependency-graph.md#what-the-color-coding-likely-means): `3.T01`, `5.T1`, `6.T1`, `7.T01`).
2. Single-page internal consistency — `AccountTransactionPage`'s own maintenance chain (Ch.9-16), which is the biggest chunk of real behavioral logic and where most of the four core questions actually get answered (`current_free_amount`, `FundJar.milestone_amount`).
3. Cross-page flow — `TransactionLogBook`/pattern continuity across pages (Ch.17-19), which is genuinely the least-load-bearing part for the four core goals in the near term (you can answer "how much free money do I have" and "am I on track" from a single page before cross-page continuity is fully built).

This also means: if time is short, **stopping after step 2 already gets you a program that answers Q1-Q3.** Q4 (goal readjustment) was already flagged in [04](../../04-project-goals-and-user-questions.md#q4--if-i-buy-x-anyway-how-do-i-readjust-my-goals) as needing new design work beyond what the assumptions ever specified, so it doesn't block on either step 1 or 2 being "done" in the old, strict sense.

See [02-csharp-sqlite-build-plan.md](02-csharp-sqlite-build-plan.md) for how this got revised into a "walking skeleton" once C# + SQLite was chosen.

## Open questions for you

Both resolved; keeping the record:

- **Language + storage direction** → **C# + SQLite.**
- **How much of the existing `classes/*.py` work is worth carrying forward** → none; it's an archive/reference only. If a Python attempt happens later, it gets its own fresh sibling folder under `redesign/`, not a revival of `classes/`.
