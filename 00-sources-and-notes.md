# Source Inventory & Provenance Notes

This document catalogs every source file read while reconstructing the design documentation for mymoneyforecast, their last-modified dates, and notes about which sources supersede others. This is a research index, not a design document — see [01-glossary-of-terms.md](01-glossary-of-terms.md), [02-uml-diagram.md](02-uml-diagram.md), and [03-assumptions-glossary.md](03-assumptions-glossary.md) for the actual reconstructed content.

**No source files were modified to produce this. Everything in `redesign/` is new.**

## Timeline of all source files

### `assumptions/` folder — text files (the assumption definitions)

| File | Last Modified | Role |
|---|---|---|
| `a02.txt` | 2021-08-10 | Base/leaf-class assumptions (unsuffixed IDs) |
| `assumptionNotes.txt` | 2021-08-28 | Meta-notes about reduction/merging + the supersession claim (see below) |
| `allReduced0.txt` | 2021-08-28 | Algorithmic dependency-layering pass #1 |
| `allReduced-1.txt` | 2021-08-28 | Algorithmic dependency-layering pass (variant) |
| `allReducedFoo.txt` | 2021-08-28 | Layering pass + hand-curated reduction ("careful removal") + a further "extremely reduced" pass, all in one file |
| `allFoo.txt` | 2021-09-11 | Flattened assumption→requirements map (unsuffixed IDs) |
| `allReduced.txt` | 2021-09-11 | Largest/most complete unsuffixed flattened assumption set (386 lines) |
| `a03.txt` | 2021-09-28 | AccountTransactionPage-internal assumptions, introduces the b/c/f/p/n instance-suffix notation |
| `a01.txt` | 2021-10-03 (4:11 PM) | Cross-page flow + derived-calculation assumptions, rewritten to use the mature suffix notation from `a03.txt` |

### `assumptions/` folder — uxf charts & supporting scripts

Content has since been parsed for `assumptionChartSimpleLines.uxf`, `assumptionChartTests.uxf`, and (box inventory only) `assumptionChart.uxf` and `assumptionChartTiers1.uxf` — see [05-assumption-dependency-graph.md](05-assumption-dependency-graph.md). The rest of the `assumptionChartSimpleLines1-8.uxf`/`NoLines*` family below is still dates-only.

| File | Last Modified | Note |
|---|---|---|
| `newKindOfHell.PNG` | 2021-08-13 | |
| `graphexample.png` | 2021-08-15 | |
| `findLongestPath.py`, `findLongestPath2.py` | 2021-08-15 | Topological analysis scripts used to produce the `allReduced*` layering |
| `stratifyAssumptions0.py`, `stratifyAssumptions.py` | 2021-08-15 / 08-16 | Same purpose |
| `assumptionChartSimpleLines.png`, `...Labelled.png` | 2021-08-22 | Rendered snapshots of the chart |
| `assumptionChart.uxf`, `assumptionChartNoLines*.uxf`, `assumptionChartTiers1.uxf`, `assumptionChartSimpleLines1-5.uxf` | 2021-08-28 | Early chart iterations |
| `assumptionChartSimpleLines6.uxf` | 2021-09-11 | |
| `assumptionChartSimpleLines7.uxf` | 2021-09-14 | |
| `assumptionChartSimpleLines8.uxf` | 2021-09-28 | Same day as `a03.txt` |
| `assumptionChartTests.uxf` | 2021-10-03 (4:09 PM) | Minutes before `a01.txt`'s final save |
| **`assumptionChartSimpleLines.uxf`** | **2021-10-03 (10:03 PM)** | **Same day as `a01.txt`, and the last edit of the evening — see finding below** |
| `assumptionChartSimpleLinesChecks.uxf` | **2022-05-16** | **Newest file in the entire assumptions folder — postdates even `class documentation.ods`. See open question below.** |

### External notes (outside `assumptions/`)

| File | Last Modified | Role |
|---|---|---|
| `project goals.txt.txt` | 2020-12-30 | Earliest document — high-level feature brainstorm ("Show/Warn/Maintain/Plan" framing), rrule research links |
| `fakexmltest.txt` | 2021-03-20 | Early XML shape sketch: nested `[properties]` outline for the class hierarchy |
| `planning2simple.txt` | 2021-06-27 | Expanded "Show/Warn/Maintain/Plan" feature breakdown + the assumption ID **legend/grammar** + use-case (`UC.0.x`) starter examples |
| `planning.txt` | 2021-07-25 | Earlier draft of the same Show/Warn/Maintain/Plan material, plus a "Class Planning" / "Unsorted Notes" section and a large `TransactionLogBook` responsibility outline |
| `psuedo_functions.txt` | 2021-09-19 | Pseudocode for methods (`create_earmark`, `adjust_snapshots`, `cascade_page_balance_record`, bulk transaction loading, etc.) |
| **`class documentation.ods`** | **2022-01-29** | **Newest and most authoritative source for class/property definitions** — full prose descriptions, types, mandatory flags, and cascade-update metadata for every class |

Not read (out of the requested scope, but noted for completeness): `things to test.txt` (43 lines) at the project root appears to be a close relative of the "things to test" section already captured inside `planning2simple.txt` (lines ~101-135). Flagging its existence in case it contains a later revision of that list.

## Key cross-reference finding: which assumption files are authoritative?

**`assumptionNotes.txt` says, in its own words:**

> "Also a01.txt, a02.txt, and a03.txt are the original files for the assumption planning. However, these files have too many unneeded requirements for assumptions. The most up to date and correct documentation is the assumptionChartSimpleLines.uxf file."

This is an explicit statement from you (the original author) that the `.uxf` chart — not the `.txt` files — is the ground truth for the assumption dependency graph. This directly shapes how [03-assumptions-glossary.md](03-assumptions-glossary.md) should be read: it's built from the `.txt` files because that's what was asked for at this stage, but it should be treated as **the raw/verbose material to be checked against the chart**, not as the final word, once we get to the graph-recreation step.

**However, there's a wrinkle worth flagging before we lean on that quote too heavily:** `assumptionNotes.txt` is dated **2021-08-28**, but `a03.txt` (2021-09-28) and `a01.txt` (2021-10-03) were both **edited a month or more after** that note was written. So the note's claim that "a01/a02/a03 have too many unneeded requirements" was made *before* the final revisions to two of those three files existed. Two readings are both plausible, and I don't have enough information to pick between them:

1. The note is simply stale — a01/a03 kept evolving after Aug 28 and nobody updated the note, so the txt files (particularly their final Oct revisions) may actually be closer to the chart than the note implies.
2. The txt files and the `.uxf` chart were being hand-kept in sync during Sep–Oct (note `assumptionChartSimpleLines.uxf`'s own last edit is 2021-10-03 at 10:03 PM — the **same day as `a01.txt`**, and later in the evening), meaning the Aug 28 note's verdict still holds and the chart simply continued to receive the same conceptual updates as the text files in parallel.

Either way, **`a01.txt` + `assumptionChartSimpleLines.uxf`, both last touched 2021-10-03, are the most likely "final state" pairing** among the pre-2022 material. Worth deciding together once we're in the chart-reconciliation step.

**Second open question — now resolved:** `assumptionChartSimpleLinesChecks.uxf` (2022-05-16) is the single newest file in the whole `assumptions/` folder, but parsing it (see [05-assumption-dependency-graph.md](05-assumption-dependency-graph.md)) shows it's **completely empty** — zero elements, just the bare UMLet skeleton. It doesn't supersede anything; it looks like a file that got created (opened and immediately saved, or created by some UMLet action) and never actually filled in. Best guess, laid out in full in [05-assumption-dependency-graph.md's discrepancies section](05-assumption-dependency-graph.md#5-the-test-planning-effort-in-assumptionchartestsuxf-was-abandoned-and-assumptionchartsimplelineschecksuxf-is-empty): it was probably meant to merge the test-bundling work found in `assumptionChartTests.uxf` (2021-10-03) into the completed 118-assumption `assumptionChartSimpleLines.uxf`, seven months later, and that merge never happened.

## Reduction-file lineage (the `allFoo`/`allReduced*` family)

These files aren't independent documentation — they're working output from the two Python scripts (`findLongestPath.py`/`findLongestPath2.py`, `stratifyAssumptions.py`/`stratifyAssumptions0.py`) topologically layering the assumption dependency graph, most likely in service of the goal stated at the top of `assumptionNotes.txt`: figuring out which assumptions are so similar they could share a single test function once unit tests get written.

Reconstructed lineage, oldest to newest:
1. **`a02.txt`** (Aug 10) — hand-written foundation, unsuffixed IDs, `{none}` or trivial requirement sets only.
2. **`allReduced0.txt` / `allReduced-1.txt` / `allReducedFoo.txt`** (Aug 28, same day as `assumptionNotes.txt`) — first algorithmic stratification passes, peeling off assumptions "not required by any other assumption" in repeated layers. `allReducedFoo.txt` goes further than the other two: after the layering dump, it contains a second, hand-curated "careful removal of unneeded assumptions" pass, and then a third "extremely reduced" pass that rolls several low-value axioms up into a single placeholder assumption (`0.999.x`).
3. **`allFoo.txt` / `allReduced.txt`** (Sep 11) — a later, larger (375-386 line) re-run of the same flattening, still unsuffixed. `allReduced.txt` is the most complete single flattened requirement map found (includes an explicit `id: [requirements]` dictionary dump at the end).
4. **`a03.txt`** (Sep 28) — introduces the Before/Current/Following/Previous/Next suffix notation (`b`/`c`/`f`/`p`/`n`) documented in `planning2simple.txt`'s legend, to distinguish "this page" vs. "adjacent page" instances of an assumption.
5. **`a01.txt`** (Oct 3) — final revision, rewritten to use the same suffix notation as `a03.txt` for cross-references between them.

None of the `allFoo`/`allReduced*` files carry prose descriptions of what each assumption *means* — they're pure `{requirements} -> id` dependency listings, presumably generated/maintained as input to the Python scripts rather than as human-readable documentation. The glossary and the assumptions document both pull the actual prose descriptions from `a01.txt`/`a02.txt`/`a03.txt` and cross-check IDs (and add any assumption IDs missing prose) against the `allReduced*` family.

## `class documentation.ods` — sheet inventory

This spreadsheet has 16 sheets *(count corrected 2026-07-10 — a mechanical re-parse found 16; the list below always enumerated 16, only this total miscounted)*. Two (`Sheet10` and `Properties`) are the authoritative class/property specification and are the primary source for the glossary and UML diagram — `Properties` is the more detailed/later of the two (it adds columns for cascade-update behavior: `Initially Set?`, `mutable?`, `created in cascade event?`, `set in cascade event?`, `used for next value in cascade?`, and past/present/future visibility). The rest are the author's scratch work while reasoning through specific scenarios:

- **`Sheet10`, `Properties`** — full class/property spec with prose descriptions, types, and mandatory flags. **Primary source.**
- **`backupb`, `Sheet1`, `Sheet15`, `Sheet1_2`** — a single worked numeric example (a "boat fund" / "gameboy fund" household budget scenario for May–June 2019) tracing expected/actual/earmark events and fund jar balances day by day.
- **`instances`** — a short template listing what fields are needed to create one sample instance of each class.
- **`ce mess`** — scratch reasoning about which of several near-identical properties (`current_free_amount` vs `expected_free_amount` vs `current_safety_cushion`, etc.) should be affected by unpaid bills.
- **`problems`** — a QA checklist template (creation/deletion/mutation × scalar/vector defect questions) applied per-class, cross-referenced against a properties-by-behavior truth table.
- **`p`** — just the truth table half of the `problems` sheet (one row per class, behavior flags in columns).
- **`xmlDepth`** — maps every class property to its full XML node path (e.g. `TransactionLogBook > log_pages > TransactionLogPage > account_pages > AccountTransactionPage > ...`), i.e. the intended XML serialization shape.
- **`fund jar`** — eight hand-drawn timeline scenarios reasoning about what a fund jar's `full_amount`/`expected_amount` should be relative to when its earmark pattern starts/ends and where "today" falls.
- **`Sheet2`, `Sheet12`, `Sheet14`, `Sheet16`** — smaller scratch/pivot tables (a mini balance-check calculation, an alphabetized property-name index, a fund-jar-carry-forward pseudocode trace, and a spreadsheet-formula-reference sheet).

## How to use this alongside the other redesign documents

- [01-glossary-of-terms.md](01-glossary-of-terms.md) — classes, properties, methods, and domain vocabulary, prioritizing `class documentation.ods` (newest/richest) and falling back to the `.txt` notes where the ODS is silent.
- [02-uml-diagram.md](02-uml-diagram.md) — structural class diagram built from the same material.
- [03-assumptions-glossary.md](03-assumptions-glossary.md) — every assumption's original wording, organized into chapters by class/topic. Treat as raw material pending reconciliation against `assumptionChartSimpleLines.uxf` (and possibly `assumptionChartSimpleLinesChecks.uxf` — see open question above).
