# Assumption Open Questions (for the original author)

Every place where the redesign documentation's understanding of the old assumption system rests on **inference rather than a concrete source**, gathered into one answerable list. Compiled 2026-07-17 from the flagged uncertainties in [00-sources-and-notes.md](00-sources-and-notes.md), [03-assumptions-glossary.md](03-assumptions-glossary.md), [05-assumption-dependency-graph.md](05-assumption-dependency-graph.md), plus new ones surfaced while producing the verbatim chart transcription ([06-assumption-chart-full-text.md](06-assumption-chart-full-text.md)).

**How to use this document:** each question carries an `**Answer:**` line. **As of 2026-07-18 all 35 are answered** (see the Status and Bookkeeping-log sections at the bottom) — over three batches, dated inline. Some answers are firm rulings; some are "grey area / no original plan," in which case the current C# behavior is recorded as *ruled-by-default* rather than *author-confirmed*; a few are partial. Downstream docs (00/03/05/06 and the eventual reconciled master graph) cite this file as the concrete source. Observations that span many questions are gathered in the "Author observations — batch 1/2/3" sections just below.

**Priority key:** ★ = changes how the reconciled assumption set/graph gets built · ◆ = affects specific assumptions' meaning · ○ = cosmetic/typo confirmation.

**Already resolved — not re-asked here:** the deallocation math questions Q1–Q5 (planning/06, resolved 2026-07-10), the safety-cushion behavior (priority 0, M=0 floor, amount editable), the rationale for "only an actual event can implicitly pull money out" (actuals were to be the deallocation trigger → assumed-pairing philosophy, planning/05), `HasNegativeFreeBalance` semantics, and the BalanceSnapshot-as-per-day-answers framing (glossary editorial notes dated 2026-07-10).

---

## Author observations — batch 1 (2026-07-18)

Free-form rulings the author gave alongside the batch-1 answers below; recorded here because they apply across many assumptions, not just one question.

1. **`assumptionNotes.txt` braces semantics (confirms the standing reading):** in the "entire reduced assumption set and their requirements" section, `{...}` is an **unordered set**, and the set above a `-`-prefixed assumption means that assumption can only be true if **every** assumption in the set is true.
2. **Every assumption is supposed to introduce one new requirement of its own.** Not always achieved — some (e.g. "all earmark patterns for this page c are perfect") are effectively just a **union** of the assumptions they depend on. Formally: *an assumption being true means the union of the assumptions it relies on is true AND its own requirement is true.* (This gives the milestone/summary assumptions their precise semantics: union nodes with an empty-or-thin own-requirement.)
3. **Worked ID-grammar example, author-narrated (via `1.2.3c.11.a4`):** first number = class per the ODS Properties sheet (1 = TransactionLogBook); next = property number (2 = `log_pages` — the sheet's adjacent column even lists the `1.2` prefix); next = property within the element type (3 = `account_pages` on TransactionLogPage); the letter narrows an array/collection reference to one instance — **`c` = "current" as an arbitrary iteration index (a reference point in the collection), *not* the current day**; remaining number = property of that class (11 = `earmark_patterns` on AccountTransactionPage); `a` = "assumption", `aN` = Nth assumption about that property. Cross-referencing rules ↔ classes ↔ properties with the sheet is the intended way to decode any ID. *(Bookkeeping note: the author's message quoted the description of the sibling `1.2.3c.10.a4` ("all **finance** patterns...") while walking through `11.a4`, but the derivation itself lands on property 11 = `earmark_patterns`, which matches the chart's actual `11.a4` text ("all **earmark** patterns...") — the walkthrough validates the grammar; the quote was a slip.)*
4. **Orange/red arrows (confirms and sharpens 05's legend):** an arrow pointing at a target means **the target relies on the arrow's source** being true first. Orange = the dependency reaches **backward through the parent collection** (a cascade moving through instances of the same type inside their parent array) — `p` = **previous** instance; e.g. `1.2.3c.11.a4`'s orange self-loop means the previous `log_pages` entry's account page must satisfy it before the current one can. Red = the same reaching **forward**; `n` = **next** instance. So the scope letters index whatever collection the tagged property iterates (pages in `log_pages`, snapshots in `balance_record`), which is why `3p`/`3n` and `13p`/`13c` tags both occur.

## Author observations — batch 2 (2026-07-18)

5. **The `?...?` markers, general ruling (governs Q16–Q23):** the original design "only had solid plans for the classes as a data structure" — the author "didn't have a firm grasp of what methods [would be needed]." A `?name?` means *the author wasn't sure how that information would be obtained; it was probably going to come from a method that didn't exist yet.* Function-shaped markers like `?money_in_fund_jars_ready_for_unpaid_bills(unpaid_bill_finance_ids, unpaid_bill_amounts)?` are cases where a function was anticipated and its *argument shape* was known, but not its body. So the `?...?` questions largely resolve to **"deliberate grey areas in a rigid assumption design"** — where the C# rewrite has already made a choice, that choice stands as the ruling-by-default, and where no choice has been needed yet, the formula is a *new-design* decision, not archaeology.
6. **Red arrows = forward re-validation (author, via the `3.13c.a7` walkthrough):** `3.13c.a7` is about adding new implicit earmarks on a deallocation day — which "would require another cascade to update calculations into the future." Its red arrows lead to assumptions that **need to be met again by the cascade process** (the boxes of `3.13c.8.a5` and `3.13.8.1.a1`). Other red arrows are "probably for something similar. Something happening in a property for a certain day/page, and that effect requires that we check if an assumption in a future day/page is true." Interpretive note (pending confirmation): this makes a red edge the *other side* of the same cascade coupling an orange edge expresses — "A requires B-of-previous" (orange, drawn from the dependent) ≡ "B invalidates A-of-next" (red, drawn from the source) — and matches the `&[...]` "will be broken by this" text notation.

## Author observations — batch 3 (2026-07-18)

**⚠ Methodology correction — the automated edge/region reading in [05](05-assumption-dependency-graph.md) is less reliable than 05 presents it.** Three findings from this batch, in descending importance:

7. **Arrows can terminate on *other arrows*, not just boxes (author, Q29) — this invalidates 05's "33 dangling edges" and much of its low-confidence flagging.** The author: *"we don't have 33 arrows pointing nowhere near a box. All the arrows point to boxes. Some arrows are just lines that point to lines going to other boxes. Like how `9.7.6.a1`, `9.6.2.a1`, `3.12.5.a1` all have lines pointing to an arrow. They all share the same arrow so the chart doesn't get too messy."* This is an edge-**bundling** technique: several source lines merge into one shared arrow that then hits the target box. `uxf_graph_tool.py` only ever resolved a polyline endpoint to the nearest **box**, so any endpoint landing on another line (a junction) got mis-snapped to whatever box was nearest — manufacturing both spurious edges and phantom "dangling" endpoints. **Verified mechanically 2026-07-18:** the far ends of the `9.6.2.a1` and `9.7.6.a1` lines sit ~10–12 units from *another relation's* polyline (a junction), not from a box; and with a sane distance cap `1.2.a1` has exactly **2** incoming arrows (author: "only 2"), not the ~17 that 05's uncapped nearest-box search attributed to it. **Consequence:** treat 05's edge list — especially every `(LOW CONFIDENCE)` edge and the entire [Discrepancy #1 dangling-edge list](05-assumption-dependency-graph.md#discrepancies--open-questions) — as **suspect pending a junction-aware re-parse**. The **box text** (requirement sets in `{...}`, transcribed in [06](06-assumption-chart-full-text.md)) is unaffected and remains the reliable dependency source; the `.txt` files likewise. *Fixing the tool to follow line→line junctions, then regenerating 05's graph, is now a queued task (see bottom).*
8. **The dotted regions are a visualization aid for the author, not a spec (author, Q11/Q35).** *"It was mostly a visualization tool for my benefit."* They **bundle related assumptions with a note describing a process those assumptions could form together** — but the author *"has no idea when the user can interact with things in this chart... I don't think the regions are the most reliable way to get that,"* and *"our strategy for making the assumptions true might group the logic a little differently."* So: the "moments outside all regions = user-interaction windows" hypothesis (05) is **not confirmed and should not be leaned on**; interaction timing is open, new-design territory. The regions are recorded below for **intent preservation only**.
9. **The geometric label↔region pairing and membership in 05/06 were substantially wrong.** The author corrected nearly every region (below). Root cause: nested/overlapping/close-packed outlines defeat a "nearest label" + "point-in-bbox" heuristic. **The author-corrected memberships below supersede the geometric tables in [05](05-assumption-dependency-graph.md#region-membership) and [06](06-assumption-chart-full-text.md).**

### Author-corrected region memberships (authoritative, 2026-07-18)

Boxes named by their **first assumption** (each box may bundle more; see [06](06-assumption-chart-full-text.md) for full box contents). This is a record of **original intent**, not a binding grouping for the rewrite. Where the author left a group unfinished ("too tired to list"), that's marked.

| Region note (verbatim) | Member boxes (by first assumption) | Notes |
|---|---|---|
| **This Balance Snapshot has full_amount** | `3.13c.2.a1`, `3.13c.2.a2`, `3.13c.2.a3`, `1.2.3.12.2.a1`, `3.13.a4` | 5 boxes. |
| **all\* earmarks created on this page** | `3.13.8.5.a1`, `3.13c.8.a4`, `3.13.8.a6`, `3.13.8.3.a1`, `3.13.8.a3`, `3.13c.8.a5`, `3.13.8.1.a1`, `3.13.8.a7` | 8 boxes. |
| **Loop over pages and fix patterns** *(a loop/container)* | Contains the **deallocation** region + the **full_amount** region, **plus** `3.13c.8.a4`, `3.13c.8.a5`, `3.13.8.1.a1`, `3.13c.5.2.a4`, `3.13c.5.2.a5`, `3.13.5.2.a3`, `3.13.5.2.a2`, `3.13.5.2.a1` | Author-stated. *(The note text reads oddly against fund-jar/deallocation contents — recorded as given; the label may sit near a different cluster than its members. "Loop over balance snapshots in page" is a nearby unassigned label that may belong to this loop.)* |
| **The whole page is cleaned of obsolete events and balance snapshots** | `3.13.a2`, `1.2.3c.13.a3`, `3.13.a3` | 3 boxes. |
| **If this is a deallocation day, the implicit earmarks are made.** | `3.13c.a6`, `3.13c.a7`, `3.13c.8.4.a2`, `3.13c.a8` | 4 boxes. **No outer/second copy of this region** (corrects 05's "outer" version). |
| **Finance patterns in this page are perfect** | `1.2.3.10.a1`, `1.2.3c.10.a4`, `3.10.a1`, `3.10.a2`, `3.11.2.a1` | 5 boxes. **An outer FINE-dotted group encloses this region *and* the "Earmark patterns…" region below.** |
| **transactions are paired** | `3.13.a5`, `6.4.a1`, `1.2.3.13.a1`, `1.2.3.13.6.a1`, `1.2.3.13.a2` | 5 boxes. No outer box; sits close to "Earmark patterns…" and "New events created…", which caused 05's over-merge. |
| **Earmark patterns in this page are perfect** | `1.2.3c.11.a1`, `1.2.3c.11.a2`, `1.2.3c.11.a3`, `3.11.1.a1`, `3.13.1.a1`, `3.11.2.a1`, `1.2.3c.11.a4` | 7 boxes. |
| **New events created. Old events removed.** | *(partial — author didn't fully list)* includes `3.13.8.a2`, `3.9.a1`, `7.1.a1`, `3.13.6.a1`, and all boxes above these in the same column | **05/06 missed this region entirely.** |
| **initial snapshot is good except for expected amount** | `3.12.1.a1`, `3.12.a1`, `9.1.a1`, `3.13.2.a4`, `9.8.2.a1`, `9.8.6.a1`, `1.2.3c.12.a1` | 7 boxes (the tests-chart geometry matched this; the SimpleLines geometry wrongly added `1.2.3.12.5.a2`/`3.13.3.a1`/`3:13.a9`). |
| **Loop over all pages, updating expected_free_amount on initial snapshots…** *(right side)* — carries a **second note**: *"also calculate current_unpaid_expected for each page / current_free_amount for today's page / current_safety_cushion for today's page / and milestone_amount for each fund jar"* | **14 boxes** *(author too tired to enumerate)* | Distinct from the left-side "expected_amount" loop below; 05 wrongly merged the two. |
| **Loop over all pages, updating expected_amount on initial snapshots. Loop over all budget snapshots in each page, updating expected_amount** *(left side)* | *(not enumerated)* | A **separate** sibling group from the one above. |
| **Fund Jar.. stuff** | `3.12.5.a1`, `9.5.1.a1`, `9.5.a1` | 3 boxes. |

---

## A. Authority & provenance

### Q1 ★ When the chart and the final `.txt` revisions disagree, which wins?

`assumptionNotes.txt` (2021-08-28) says a01/a02/a03 "have too many unneeded requirements" and that `assumptionChartSimpleLines.uxf` is "the most up to date and correct documentation." But `a03.txt` (2021-09-28) and `a01.txt` (2021-10-03) were both revised **a month or more after** that note — and the chart's own last save is the same evening as a01's (2021-10-03). Concrete conflicts exist in both directions, e.g.:

| Assumption | `.txt` requirement set | Chart box requirement set |
|---|---|---|
| `3.12.a1` | `{1.2.3c.12.a2}` | `{whatever}` |
| `3.13.a4` | `{3.13.6.a2, 3.13.a3}` | `{3.13.a3}` |
| `8.4.a1` | `{3.13.8.4.a1, 3.13b.8.4.a2, 3.13c.8.4.a2, 3.13f.8.4.a2, 8.4.a2}` | `{8.4.a2}` |
| `3.13c.a6` | `{..., 3.13.6.a2, 1.2.3.13.a2}` | same minus `1.2.3.13.a2` |
| `3.12.5.a1` | `{1.2.3c.11.a4, 3.13.5.a1, 3.13.5.a2}` | `{3.13.5.a1, 3.13.5.a2}` |
| `7.1.a1` | `{none}` (a02) | six real prerequisites |

Two readings from 00-sources-and-notes.md:
- **(a) The note stayed true — chart wins.** The txt files and the chart were being hand-kept in sync through Sep–Oct, the chart receiving the final trims. *Implication: the smaller chart sets are deliberate reduction (dropping requirements that are implied transitively or unneeded); the reconciled master adopts chart sets, and the txt sets are recorded as historical fuller forms.*
- **(b) The note went stale — the Oct txt revisions win where they conflict.** *Implication: the chart's set differences are editing accidents/lag; the master adopts txt sets, and the chart is authoritative only for content the txt files lack (the chart-only assumptions, month-boundary rules, etc.).*
- **(c) Neither globally — judge per case** (e.g. chart wins on prose/description text, txt wins on requirement sets, or vice versa).

**Answer:** *(author, 2026-07-18)* **(c) — case-by-case.** Every conflict gets presented individually, always with provenance (which file each competing version comes from), and ruled on its merits. No blanket winner.

### Q2 ◆ What was `assumptionChartSimpleLinesChecks.uxf` (2022-05-16, completely empty) going to be?

It's the newest file in the whole `assumptions/` folder, seven months after everything else, and contains zero elements. 05's guess: it was opened to redo the test-bundling merge properly — apply `assumptionChartTests.uxf`'s test-group idea to the *completed* 118-assumption chart — and never got filled in. Do you remember? *If the guess is right: nothing is lost; the Part-2 transcription in 06 is the closest artifact to its intent. If it was something else: tell us what, so we know whether content is missing somewhere.*

**Answer:** *(author, 2026-07-18)* Confirmed: **it was never filled in — nothing is missing; ignore the file.**

### Q3 ○ Was the test-planning effort (`assumptionChartTests.uxf`, 24 bundles covering 64 of 118 assumptions) abandoned mid-way?

It was never carried into the final chart (zero T-boxes there). Purely historical bookkeeping — the C# rewrite has its own suite — but it settles whether the missing 54 assumptions' bundles ever existed anywhere.

**Answer:** *(author, 2026-07-18)* Confirmed: **the attempt was never finished.** The 24 bundles in 06 Part 2 are all that ever existed.

### Q4 ◆ Property-numbering drift: confirm assumption IDs always use the *old* (assumption-file) numbering, never the ODS renumbering.

`a02.txt` says `4.1` = FinancialPattern.finance_id, `4.2` = source; `class documentation.ods` (Jan 2022) instead numbers amount=4.1, finance_id=4.5, source=4.9. Similar drift on ActualTransaction (`6.1` source vs occurred_date), and EarMarkEvent/BalanceSnapshot/FundJar are shifted a whole class number in the ODS (its "class 8" carries `9.x` property IDs, etc.).

- **(a) Confirmed — IDs are frozen to the assumption files' scheme; the ODS renumbering was a spreadsheet artifact never retrofitted into assumptions.** *Implication: the reconciled master keys everything to the assumption-file scheme and carries a one-time mapping table to the ODS.*
- **(b) Not that simple — some later assumptions were written against ODS numbering.** *Implication: affected IDs need re-mapping before the graph is trusted.*

**Answer:** *(author, 2026-07-18)* The first number was always *meant* to track the class, but **classes were added and primary numbers changed at several points, and some assumptions got stuck with the old names** during those transitions. Operationally that's (a) with a caveat: read IDs against the assumption-era numbering, expect stragglers, and cross-reference the ODS Properties sheet (which lists prefixes like `1.2` next to properties — see observation 3 above) whenever a prefix looks off.

---

## B. Chart notation semantics (all chart-only, none documented in any legend)

### Q5 ★ What do **parenthesized IDs inside requirement sets** mean?

Found only in chart boxes (see 06), e.g. `1.2.3c.12.a2`'s set contains `(1.2.3c.12.3.a1)`, `(1.2.3c.12.3.a2)`, `(9.5.a1)`, `(9.5.1.a1)`; `3.13.5.4.a1` has `(1.2.3c.12.a2)`, `(3.13.8.a8)`, `(3.13.5.a5)`; `3.a2` has `(1.2.3.6.a1)`; `3.13.5.a5` has `(3.13b.5.2.a5)`, `(3.13c.5.2.a5)`, `(3.13f.5.2.a5)`, `(10.3.a1)`.

Mechanical evidence for one reading: in every case checked, the parenthesized ID is **already implied transitively by another (unparenthesized) member of the same set** — e.g. in `3.13.5.4.a1`'s set, `3.a1` is listed and `3.a1`'s own requirements include all three parenthesized items; in `3.a2`'s set, `1.2.3.5.a1` is listed and itself requires `1.2.3.6.a1`.

- **(a) "Redundant — kept for readability but implied via another listed requirement."** *Implication: the reduced graph can drop them with zero information loss; they're documentation sugar.*
- **(b) "Soft/optional requirement"* or **(c) "uncertain — to verify."** *Implication: they must be kept and flagged as semantically different from plain members.*

**Answer:** *(author, 2026-07-18)* **(a) — redundant but shown.** Best guess: *"in case I wanted to reduce this and remember what assumptions I could omit to save space."* → the reconciled/reduced graph can drop parenthesized IDs with zero information loss; they're documentation sugar marking an already-transitively-implied prerequisite.

### Q6 ★ What is the **second `{...}` block** some boxes carry?

`3.a1`, `1.2.3c.12.a1`, `1.2.3c.12.a2`, and `3.13.7.a3` each list *two* requirement sets back-to-back (see their boxes in 06). Mechanical evidence: the second block matches the hand-reduced sets in `allReducedFoo.txt`'s "careful removal of unneeded assumptions" pass — exactly, in the cases of `1.2.3c.12.a1` (13 items, unsuffixed) and `3.13.7.a3` (`{3.13.1.a1, 3.13.7.6.a1, 3.13.7.a2}`), and near-exactly for `3.a1` (the 7-item set minus `1.2.3c.12.a2`).

- **(a) First block = full requirement set; second = the reduced/operational set from the reduction passes.** *Implication: the chart deliberately carries both graphs; the reconciled master should record both, and "which set do tests target" becomes a meaningful choice.*
- **(b) Something else / leftover from editing.** *Implication: treat second blocks as noise.*

**Answer:** *(author, 2026-07-18)* **(a) — first block = full set, second = reduced set; no new information in the second.** The author asked "is there any significant change between the two? It sounds like redundant information was removed" — and that's exactly right, **verified mechanically**: for all four boxes the second block is a strict subset of the first once suffixes are normalized (the only apparent "additions" — `1.2.3.10.a4`, `1.2.3.12.a1`, `1.2.3.12.4.a1` — are just the unsuffixed spellings of first-block members `1.2.3c.10.a4`, `1.2.3c.12.a1`, `1.2.3c.12.4.a1`). The second block matches `allReducedFoo.txt`'s hand-reduced pass. → **Reconciled master: keep block 1 as the authoritative full set; block 2 is the reduction-pass output, useful only if a minimal test set is later wanted.**

### Q7 ◆ Line-style legend — confirm the inferred key.

From 05 (inferred from data, never stated in any source): `lt=<-` arrows point from the **dependent** assumption back to its **prerequisite**; a bare letter tag like `3p`/`13c`/`13b, c, f` scopes the dependency per the b/c/f/p/n instance grammar; **orange** edges = the dependency crosses into the **Previous** page; **red** = **Next/Current** page. Correct?

**Answer:** *(author, 2026-07-18)* **Confirmed, with a sharper framing** — see observation 4 above. Arrow target relies on arrow source. Orange = the cascade reaching backward through the parent collection, `p` = previous instance (not limited to pages — whatever collection the tagged property iterates); red = forward, `n` = next instance.

**REOPENED SUB-PART (batch 2):** the author asked *"What chart has those?"* about the multi-letter tags. They're edge labels in **`assumptionChartSimpleLines.uxf`** — four edges carry the label `13b, c, f` (style text on the Relation element). Clearest example: the arrow from `1.2.3.13.a1` ("all can be paired") to `3.13.a5`, labeled `13b, c, f` — which is exactly the drawn form of `a01.txt`'s textual requirement set for `1.2.3.13.a1`: `{3.13b.a5, 3.13c.a5, 3.13f.a5}`. Question still open: confirm a `13b, c, f` tag means "the source must hold for the **B**efore, **C**urrent, and **F**ollowing instances of property 13 (`balance_record` snapshots)" — one arrow compressing three (or twelve) textual b/c/f requirements.

**Answer (sub-part):** *(author, 2026-07-18)* **Confirmed.** *"That does sound correct. I remember we had to calculate a couple of cascades for all of this. We need to have the expected transactions and the pairings figured out for past, present, and future for everything before we go back over a second time and tinker with the more delicate calculations, like the fund jars."* → `13b, c, f` = the source must hold for the **B**efore/**C**urrent/**F**ollowing `balance_record` instances; and it reflects the deliberate **multi-pass** order — resolve all expected transactions + pairings across past/present/future first, *then* a second pass for the delicate fund-jar math.

### Q8 ◆ The two **unscoped** `bg=red/fg=red` edges — meaningful or accident?

`3.13.8.a7 ← 3.13.6.a1` and `3.13.a2 ← 7.1.a1` are the only red edges with no scope letter, and both also have shaky geometry (endpoints >100 units off). Every other red/orange edge carries a scope tag. 05 floated the delightful possibility that red-without-letter = "flagged as suspect by the author."

- **(a) They were a self-flag for "this edge looks wrong."** *Implication: drop both from the graph.*
- **(b) Styling accident; the edges are real.** *Implication: keep them (both are plausible per the txt catalog).*

**Answer:** *(author, 2026-07-18)* **Effectively (b), reframed** — see observation 6. Red arrows mark forward re-validation: something happening on this day/page means an assumption on a future day/page must be checked again. The two unscoped ones are "probably for something similar" → **keep both, as re-validation edges** (hedged: "probably").

**Geometric follow-up (recorded 2026-07-18, from re-parsing the raw UXF):** the author's walkthrough said `3.13c.a7`'s two red arrows lead to `3.13c.8.a5` and `3.13.8.1.a1`. The file's two `13c`-tagged red arrows do fan out from **one shared tail point** to arrowheads at that two-box stack — so they are **not duplicates** (revises 05's discrepancy #3 for this pair) — but the shared tail sits at (2306, 598): on the deallocation row, yet ~530 units *right* of `3.13c.a7`'s box, immediately beside `3.13.5.2.a2` (which is how 05 resolved the source). Semantically the arrows match `3.13c.a7`'s own `&[3.13c.8.1.a1, 3.13c.8.a5]` marker exactly. **Residual ruling needed:** treat their source as `3.13c.a7` (author's reading; tail presumably dragged during editing) or as `3.13.5.2.a2` (raw geometry)?

**Answer (residual):** *(author, 2026-07-18 — RESOLVED, and it corrects the geometry)* The author read the actual arrows off the chart, which supersede both 05's geometry and my "shared tail beside `3.13.5.2.a2`" guess. **The five red arrows in `assumptionChartSimpleLines.uxf`:**
- `3.13.7.a3` → `1.2.3.13.a2` *(unlabeled)*
- `3.13.6.a1` → `1.2.3.13.a2` *(unlabeled)*
- `3.13c.a7` → `3.13c.8.a5` *(label `13c`)*
- `3.13c.a7` → `3.13.8.1.a1` *(label `13c`)*
- `3.13.5.3.a1` → `1.2.3.12.5.a2` *(label `3n`)*
- `3.13.5.4.a1` → `1.2.3.12.5.a2` *(label `3n`)*

So the two `13c` reds **do** originate at `3.13c.a7` (author's reading — matches its own `&[3.13c.8.1.a1, 3.13c.8.a5]` text; the geometry mis-snapped the tail). And the two "unscoped" reds are **not** `3.13.8.a7←3.13.6.a1` / `3.13.a2←7.1.a1` as 05 claimed — they are `3.13.6.a1`/`3.13.7.a3` → `1.2.3.13.a2`, which exactly encode those two boxes' `&[1.2.3.13.a2: all are paired]` text markers (obsolete removal must precede pairing). A clean demonstration of the geometry defect in observation 7.

### Q9 ◆ `layer=1` / `layer=2` tags on boxes and edges — z-order or stratification?

`layer=1` sits on most of the Balance Record / Fund Jars / Actual / Expected / Earmarks boxes; `layer=2` only on some orange/red cross-page edges. Was this (a) purely UMLet visual stacking, or (b) meant to encode the dependency **layers** the `stratifyAssumptions.py` scripts computed? *Implication of (b): the tags are computation-order metadata worth preserving in the master doc.*

**Answer:** *(author, 2026-07-18)* **(a) — "just the z axis for the chart to keep things readable. Nothing more."** The tags carry no meaning; ignore them in the reconciled graph.

### Q10 ○ Box colors — confirm both charts' color codes.

Main chart: `bg=pink` = the "has all X" completeness checks; `bg=light_gray` = the "X is maintained / is perfect" milestone summaries. Tests chart: `bg=green` = tests with no unmet dependencies (write these first), `bg=pink` = end-of-chain completeness tests, `bg=light_gray` = in between — a traffic-light for test-writing order. Correct?

**Answer:** *(author, 2026-07-18 — PARTIAL, pink only, offered as a best guess)* **Pink = a good logical chokepoint — an ideal place to actually test.** Too many assumptions exist to test each before its dependents; but after a pink assumption is supposedly met, that's the ideal spot to verify it *and the assumptions it depended on* in one go. Testing-only — never run during normal operation. (This refines 05's guess: pink isn't just labeling "has all X" checks, it marks *test checkpoints* — those tend to coincide with the completeness checks, which is why 05's pattern-match worked.)

*(batch 2)* **`bg=light_gray` answered:** gray sits on "perfect/maintained" nodes at the end of a chain or process, but pink marks the ends of *even larger* processes — so **gray is an in-between tier: white < gray < pink in process scale** (best guess, consistent with the chokepoint reading). **Still open — the green boxes:** the author doesn't see green in the chart they're checking; green exists only in **`assumptionChartTests.uxf`** (`bg=green` on test-bundle boxes `3.T01`, `5.T1`, `6.T1`, `7.T01`). Guess to confirm once viewed: green = tests with no unmet dependencies — the intended starting point for test-writing.

**Answer (green, tests chart):** *(author, 2026-07-18)* **Green = tests we're certain we want to make.** *"Any box that starts with 'T' is a test and will include the assumptions we want to verify with the test. I'm not certain why some are green... I guess the green ones are ones we're certain we really want to make tests for."* (Best guess — consistent with, but not the same as, 05's "no unmet dependencies" reading; the author frames it as certainty-to-test, not dependency-readiness.)

### Q11 ★ Process regions — confirm the central interpretation.

The 14 dotted outlines + their plain-language labels (verbatim in 06). 05's reading: **each region is one atomic step of the cascade-update process; regions nest like loops; and the "time periods during which the user can interact with the system" are the moments *outside* all regions — between cascade runs, when the record is fully self-valid.** Is that what the regions meant to you?

*(Batch-1 observations lean supportive — "large sections of the assumption graph could theoretically be used as logical workflows," and the pink-chokepoint ruling implies segment boundaries — but the region outlines themselves weren't addressed, so this stays open.)*

**Answer:** *(author, 2026-07-18 — PARTIAL)* Confirmed the basic mechanism: *"dotted lines encompassing multiple assumptions... bundle similar/related assumptions together along with a note. The note does describe these related assumptions that could be part of a process together."* The author quoted one bundle from memory — "initial snapshot is good except for expected amount" containing `3.12.1.a1`, `3.12.a1`, `9.1.a1`, `3.13.2.a4`, `9.8.2.a1`, `9.8.6.a1`, `1.2.3c.12.a1` — and asked for the full parsed region list to double-check. *(The mechanical parse of that same region also contains `1.2.3.12.5.a2`, `3.13.3.a1`, and `3:13.a9` — three boxes the author didn't name; membership is geometric, so boxes may sit inside an outline without being intended members. Flagged for the double-check.)* **Still open after the double-check: whether "the moments outside all regions" = the user-interaction windows.**

**Answer (interaction-window interpretation):** *(author, 2026-07-18)* **NOT CONFIRMED — abandon this reading as a source.** *"I have no idea when the user can interact with things in this chart. That's worth figuring out, but I don't think the regions are the most reliable way to get that."* → The regions are a visualization aid (observation 8), not an interaction-timing spec. **Interaction timing is a genuinely open, new-design question** — not something the chart settles. The 05 hypothesis ("moments outside all regions = interaction windows") is retired.

### Q12 ○ One region outline encloses empty canvas — editing artifact?

Correction found while producing 06: the empty region is the one labeled **"also calculate current_unpaid_expected for each page / current_free_amount for today's page / current_safety_cushion for today's page / and milestone_amount for each fund jar"** (05 had attributed the emptiness to the "Loop over all pages, updating expected_free_amount" region, which actually holds 25 boxes). In the tests chart the same label's region holds 9 boxes (`1.2.3.5.a1`, `1.2.a1`, `2.3.a1`, `3.1.a1`, `3.7.a1`, `3.7.a2`, `3.13.4.a1`, `3.13.5.4.a1`, `3.a2`). Presumably the boxes moved out from under the outline during editing and the tests-chart membership is the intended one?

**Answer:** *(author, 2026-07-18)* **Confirmed — the SimpleLines geometry mis-read this; the tests-chart membership is accurate.** *"Something went wrong with your read of that region in this file. The one you read in the test file is indeed more accurate."* Note (from the corrected region list above): this "also calculate…" text is actually a **second note on the expected_free_amount rollup region** (a right-side group of 14 boxes), and 05 wrongly merged that right-side group with the adjacent left-side "expected_amount" loop.

---

## C. Garbled / truncated source text — what was meant?

### Q13 ◆ `1.2.a1` — the lost middle sentence.

`a01.txt`: *"log_pages cannot by null. It can be an empty list. between pages. (page X is immediately before page Y)"*. The chart box drops everything after "empty list." The stranded fragment *"between pages. (page X is immediately before page Y)"* reads like the tail of a deleted sentence — plausibly something like "There can be no gaps **between pages**" (contiguity) or a definition of page ordering. What was the full rule?

*Implications: if contiguity/no-gaps was intended, that's a real structural assumption the multi-page rewrite must eventually enforce (pages form an unbroken, ordered timeline); if it was only defining the phrase "immediately before" for use elsewhere, nothing is missing.*

**Answer:** *(author, 2026-07-18)* **No lost rule — use the chart version** (`log_pages cannot be null; can be an empty list`). *"Don't worry about it. The one without that incomplete phrase is the one to use."* The stranded "between pages…" fragment was editing cruft; **no contiguity assumption to preserve.**

### Q14 ◆ `1.2.3.a1` — the stranded "If".

*"If page X and page Y must have the same number of account pages and the same sets of accountnames. all pages must have the same accounts."* Is the final meaning simply **"all pages must have the same set of account names"** (the "If page X and page Y" opener being an edit leftover), or was there a real conditional (e.g. only adjacent pages, only non-expired pages)?

**Answer:** *(author, 2026-07-18)* **Simple version — no hidden conditional.** *"Ignore the 'must' in this,"* leaving: *"If page X and page Y have the same number of account pages and the same sets of accountnames. all pages must have the same accounts."* → the rule is **every page has the same accounts (same count and same set of account names)**; the "If page X and page Y…" clause just frames the pairwise comparison, it is not an adjacency/expiry restriction.

### Q15 ○ `1.2.3.13.a3` truncation — confirm the full text.

`a03.txt` cites it mid-requirement-set as *"this current accounttransaction page c is ready to remove balance snapshops with no..."* — the chart/a01 complete it: *"...with no events in them."* Just confirming the completion is right.

**Answer:** *(author, 2026-07-18)* **Confirmed** — "...with no events in them." completes it.

---

## D. The author's own `?...?` uncertainty markers — resolve them now

The docs preserve these as written; each one is a decision that was never made. The C# rewrite has already had to make several of them de facto — flagging where.

### Q16 ◆ `?today?` in `1.2.3.5.a1` (`Y.balance_record[?today?]`).

`balance_record` only has snapshots on event days. If *today* has no events (no snapshot), which snapshot feeds `current_free_amount` — the nearest one **at-or-before** today? *(That's what the C# rewrite effectively does; confirming it matches your intent.)*

**Answer:** *(author, 2026-07-18, via observation 5)* No decided semantics ever existed — `?today?` meant "not sure how I was going to get this information; probably a method." **Grey area → the C# rewrite's nearest-at-or-before lookup stands as the ruling-by-default.**

### Q17 ◆ `?money_in_fund_jars_ready_for_unpaid_bills(unpaid_bill_finance_ids, unpaid_bill_amounts)?` in `1.2.3.5.a1`.

The double-count correction: money already in a jar for an unpaid bill shouldn't be subtracted twice. What's the exact per-bill amount to add back — **min(jar's current_amount, that bill's unpaid amount)**, or the whole jar balance, or something else? *Implication: with min(), an over-funded jar only offsets up to the bill; with whole-jar, an over-funded jar could inflate free money past what the bill needs.*

**Answer:** *(author, 2026-07-18, via observation 5)* A function was anticipated and its **argument shape** was known (`unpaid_bill_finance_ids`, `unpaid_bill_amounts`) — its body never was. **The exact formula is a new-design decision, not archaeology.** Flagged for whenever real unpaid-bill tracking gets built (currently moot: under assumed-pairing, `current_unpaid_expected` is pinned to 0). Working recommendation when that day comes: min(jar, unpaid amount) per bill, per the implication above.

### Q18 ◆ `?initial finance_patterns?` in `3.12.5.a1`.

*"initial_snapshot.fund_jars cannot have a fund jar that is not in our ?initial finance_patterns?"* — but jars are keyed to **earmark** patterns everywhere else. Did you mean (a) the page's **earmark_patterns** (jar exists only if an earmark pattern for that finance_id exists), or (b) literally **finance_patterns** (jar exists only if the goal's FinancialPattern exists — a weaker gate, since `3.10.a3` already ties earmark patterns to finance patterns)? And what does "initial" scope — the patterns as of the page's creation?

**Answer:** *(author, 2026-07-18)* **(b) — it probably did mean finance patterns**, "because every earmark pattern has an expected transaction, with a finance id. Even one time goals are still expected transactions. Their date pattern just happens to only have a single occurrence." The residual "initial" scope reads together with Q19's ruling: the initial snapshot inherits from the **previous page's** state, so "initial finance_patterns" ≈ the patterns carried over from before this page, not ones newly starting on it.

### Q19 ◆ `3.12.5.a2`'s "(or on?)" — the boundary day.

*"no fund jar can exist here if it has a finance pattern that starts after (or on?) ?self.start_date?"* — if a pattern starts exactly **on** the page's start_date, may its jar appear in the *initial* (dateless) snapshot, or does it first appear in the day-1 balance-record snapshot? *(Strictly-before → initial snapshot only carries jars from patterns that predate the page; on-or-before → day-of patterns get initial jars too.)*

**Answer:** *(author, 2026-07-18, offered as a guess)* **Strictly-before** — "we only want initial snapshots to come from patterns on the previous page. I don't think the initial snapshots even have a date. So they shouldn't inherit a fund jar from a new pattern that happens to start on the first day of this page." The "(or on?)" resolves to: **exclude** jars whose pattern starts on `self.start_date`; they first appear in the day-1 balance-record snapshot.

### Q20 ◆ `?normal_fund_daily_distribution?` in `3.13.5.2.a3`.

The normal-day formula for a jar's `current_amount` was never pinned down. Is it: **previous day's current_amount + today's earmark events' amounts for that jar, floored at 0** — i.e. what the archived Python's `max(..., 0)` and the C# rewrite's per-day `max(0, prev + events)` both do? Or was something fancier intended (e.g. proportional distribution when free funds can't cover all of today's scheduled earmarks — the "first come first served" allocation rule from the ODS)?

**Answer:** *(author, 2026-07-18)* "I'm fairly certain I didn't have a solid plan for this one... let's call this a **grey area**." → No original formula existed. **The C# per-day `max(0, prev + events)` (now also priority-aware and capped at available funds since the Step-2 deallocation integration) stands as the ruling-by-default.**

### Q21 ○ `?deallocation_fund_distribution?` in `3.13c.5.2.a4`.

Confirm this is exactly the two-step Step A (paired `p`) / Step B (balancing `b`) process now fully documented in `MyMoneyForecast/planning/06-deallocation-math.md` from `DeallocationProof.ods`.

**Answer:** *(author, 2026-07-18)* **Confirmed** — "that is the deallocation process described in that spreadsheet."

### Q22 ○ `?deallocation_implicit_amount?` in `3.13c.8.4.a2`.

Confirm: the implicit portion merged into an isolated earmark on a deallocation day = that jar's computed give-back (`p`/`b`) from the deallocation math, merged per the "expected = explicit + implicit, explicit_amount keeps the user's number" rule the C# rewrite already implements.

**Answer:** *(author, 2026-07-18)* Not entirely sure — it could be the implicit earmark **before** the same-day merge (as produced directly by the deallocation process) or **after** merging with an existing same-finance_id implicit. **Recorded as: functionally equivalent either way** — since `3.13.8.1.a1` allows only one isolated earmark per finance_id per day, the implicit contributions necessarily merge, so `expected_amount = explicit + (total implicit delta)` regardless of which stage the term named. The C# implementation (merge give-back into the isolated event, `ExplicitAmount` preserved) is consistent with both readings.

### Q23 ○ `?deepcopy?` in `1.2.3.12.5.a2`.

*"Y.initial_snapshot.fund_jars == X.balance_record[-1].fund_jars.?deepcopy? but no date"* — plain deep copy of the previous page's closing jars, dates stripped. Confirm.

**Answer:** *(author, 2026-07-18)* **Confirmed.**

---

## E. Missing / undefined IDs

### Q24 ◆ `10.1.a1` and `10.2.a1` are cited but never defined anywhere.

`3.10.a3`'s requirement set in `a03.txt` lists them; no file or chart defines them. FundJar's properties 1/2 are `finance_id`/`current_amount`. Were assumptions for them (a) written somewhere now lost, or (b) never written (the citation anticipating assumptions you expected to add)? If you remember intent: presumably `10.1.a1` ≈ "finance_id [rules]" and `10.2.a1` ≈ "current_amount cannot go below 0 / is None until the day occurs"?

**Answer:** *(author, 2026-07-18)* **They're `BalanceSnapshot` properties, not FundJar** — `10.1.a1` = `snapshot_date`, `10.2.a1` = `full_amount` (author's recollection; my "FundJar props 1/2" framing in the question was wrong). Purpose uncertain: *"maybe these blank properties serve as chokepoints of some kind. That's my best guess."* Never defined; not restored to the reconciled set unless a real rule surfaces. **This is a concrete Q4-drift artifact:** in `3.10.a3`'s single requirement list, `10.1`/`10.2` use the **ODS scheme** (where `BalanceSnapshot` properties are prefixed `10.x`) while `10.3.a1`/`10.4.a1` in the *same list* are **FundJar** (assumption-file scheme) — the "same class-number prefix, two different classes" collision the author warned about in [Q4](#q4-)/observation batch-1.

### Q25 ◆ `3.4.a1` ("expired cannot be None") is in the original chart and `a03.txt` but missing from the final chart.

Every other assumption survived the 146→118-box reduction (as own box or bundled text). (a) Accidental drop → restore it to the reconciled set; (b) deliberate removal → why?

**Answer:** *(author, 2026-07-18)* **Not a concern — omission was fine.** *"I'm assuming it wasn't important enough to mention. Dropping expired is one of the first things we did in our redesign... It's supposed to just be a property that shields really old finance logs from being subject to the cascades and the patterns."* So `expired`'s invariant (`cannot be None`) is trivially handled by the C# rewrite's non-nullable `bool`; no need to reinstate `3.4.a1` as a load-bearing assumption. (`expired` itself is real and understood — it freezes old pages out of cascades; it's just always `false` today since nothing persists across runs — see planning/05.)

### Q26 ○ Why is there no `3.6.aX` for `current_unpaid_expected` — only the book-scoped `1.2.3.6.a1`?

Inference: its definition needs cross-page information ("...that are in active pages"), so it can't be stated as a page-local (class-3) assumption at all. Confirm?

**Answer:** *(author, 2026-07-18)* **Confirmed** — *"sometimes we define the rules on an outer scope for it. That's on purpose."* The book-scoped `1.2.3.6.a1` is the intended home; there is no page-local `3.6.aX` by design.

### Q27 ○ `2.1.a2` / `2.2.a2` — confirm these chart-only IDs are real.

The month-boundary rules ("start_date should be the first day of a month" / "end_date should be the last day of a month") appear as unlabeled extra lines in the final chart's `2.1.a1`/`2.2.a1`/`3.2.a2`/`3.3.a2` boxes, and get their own IDs `2.1.a2`/`2.2.a2` **only** inside the tests chart's `2.T1` box. Confirm they're intended as real assumptions for both TransactionLogPage and AccountTransactionPage (the 3.x copies never got IDs — presumably 3.2.a3/3.3.a3 by the same scheme?).

**Answer:** *(author, 2026-07-18)* **Use the tests-chart IDs — `2.1.a2`/`2.2.a2` are real.** For the AccountTransactionPage copies: *"I'm not sure if ones for [class] 3 were intended... maybe I didn't feel like they were worth mentioning. If they wouldn't conflict with anything, you can add them."* → adopt `3.2.a3`/`3.3.a3` (first-of-month / last-of-month for the page bounds) in the reconciled set, flagged as author-permitted-not-original.

---

## F. Specific one-off discrepancies (mostly confirmations)

### Q28 ○ `3.13.1.a1` — box text vs. drawn edge.

Its box text says `{3.2.a1, 3.2.a2, 3.3.a1, 3.3.a2}` (matches a03); the drawn arrow instead points at `5.2.a1`. Confirm the arrow is a drawing mistake and box text wins. *(This softens 05's discrepancy #4, which was written before the box text was extracted.)*

**Answer:** *(author, 2026-07-18)* **The arrow does not exist — 05 mis-read it.** *"`5.2.a1` has no arrows pointing at it."* (Verified: `5.2.a1` is a `{none}` leaf; its 2 in-box endpoints are outgoing tails, 0 incoming.) So 05's "`3.13.1.a1` requires `5.2.a1`" edge (its Discrepancy #4) is a resolution artifact, not a real chart edge. **`3.13.1.a1`'s box text `{3.2.a1, 3.2.a2, 3.3.a1, 3.3.a2}` stands; Discrepancy #4 is withdrawn.**

### Q29 ◆ The 33 dangling edges — do you remember the deleted cluster?

15 of them resolve nearest to `1.2.a1` only because a large stretch of canvas (x≈3200–4800) is empty; 05's guess is a cluster of boxes was deleted wholesale during the 146→118 reduction and the arrows never cleaned up. If you remember what lived there, say so; otherwise we accept 05's triage (those 15 = cruft; the short-range rest = trusted where they match the txt catalog).

**Answer:** *(author, 2026-07-18)* **The premise is wrong — there are no dangling edges.** *"`1.2.a1` only has 2 arrows pointing at it. And no we don't have 33 arrows pointing nowhere near a box. All the arrows point to boxes. Some arrows are just lines that point to lines going to other boxes."* → 05's "33 dangling edges" are the **line→line junction** artifact (observation 7): the parser resolved shared-arrow tails to the nearest box instead of to the arrow they join. **No deleted cluster; no cruft to triage.** The fix is a junction-aware re-parse (queued), not accepting 05's dangling-edge triage. See observation 7 for the mechanical confirmation.

### Q30 ○ Duplicate edges — redraw artifacts?

`3.13c.2.a2 ← 3.13c.2.a1` drawn twice (different style tags), `3.13.8.1.a1 ← 3.13.5.2.a2` drawn twice (same tag). Confirm meaningless duplicates.

*(Update 2026-07-18: the second pair is **resolved — not a duplicate**. Re-parsing the raw UXF shows two red arrows fanning out from one shared tail point to two adjacent targets — per the author's Q8 walkthrough, the boxes of `3.13.8.1.a1` and `3.13c.8.a5`; the resolver had snapped both arrowheads to the nearer box. See Q8's geometric follow-up for the source question. Only the `3.13c.2.a2 ← 3.13c.2.a1` orange pair remains in question here.)*

**Answer:** *(author, 2026-07-18)* **Drawn once — don't worry about it.** *"I only see it drawn once. So maybe there is a duplicate on another layer."* → treat as a single edge; the phantom second copy is a rendering/parse artifact. (Consistent with observation 7 — the geometric parse over-counts.)

### Q31 ○ Typo/alias confirmations (one yes covers all six).

1. `9.7.6.a1` "expected_transactions[all].**expected_amount** == self.snapshot_date" — meant **expected_date** (the tests chart reproduces the same typo).
2. `3:13.a9` (colon) = `3.13.a9`.
3. UC.0.3's requirement IDs `1.1.3.12.x.aY` = typos for `1.2.3.12.x.aY`.
4. `a02.txt`'s "EarmarkEvent" header = `EarMarkEvent` (ODS says "earmark"/"earmark event" are synonyms).
5. "financial_id" in some prose = `finance_id`.
6. `1.2.3c.12.a1`'s chart set contains "`9.5.a1. 9.5.1.a1`" — the period is a comma typo.

**Answer:** *(author, 2026-07-18 — PARTIAL)* **Items 2, 4, 5, 6 confirmed typos.** **Items 1 and 3 NOT confirmed** — author "not sure about 1 and 3." So they stay flagged as *likely-but-unconfirmed*:
- **#1** (`9.7.6.a1` `expected_amount == snapshot_date`): still reads as a type mismatch (amount vs. date) that "should" be `expected_date` by analogy with its siblings `9.6.2.a1`/`9.8.2.a1` — but do **not** silently correct it; carry it verbatim with the flag.
- **#3** (UC.0.3's `1.1.3.12.x` for `1.2.3.12.x`): unconfirmed; carry verbatim. *(Note: this overlaps the Q4 numbering drift — a `1.1` prefix could be a genuine early-scheme artifact rather than a slip, which is exactly why the author can't be sure.)*

### Q32 ◆ `3.13.7.a1` wording drift — "a repeated expected transaction" (a03) vs. "an expected transaction" (both charts).

Does the pattern-occurrence-day rule require specifically a **repeated** expected transaction, or is any expected transaction with that finance_id acceptable? *(Likely moot if every expected transaction with a finance_id counts as "repeated" per the ODS's "even one-time payments get a synthesized FinancialPattern" rule — but confirming which text is final.)*

**Answer:** *(author, 2026-07-18)* **Chart wording wins — any expected transaction with the finance_id satisfies the day; no "repeated" qualifier needed.** *"I guess any expected transaction with a financial [pattern] satisfies the day then."*

### Q33 ◆ `3.13.5.4.a1`'s chart-only exclusion + `10.4.a2`/`10.4.a3` — confirm the milestone scheme.

The chart adds "(...or if the finance id is for a repeated expected transaction)" to the milestone rule, and `10.4.a2`/`10.4.a3` (chart-only) say: repeated **negative** expected transaction → `milestone_amount = -1 × finance_pattern.amount`; repeated **positive** → None. Reading: **running-sum milestones are only for one-time goals; a recurring bill's jar gets a flat milestone equal to one bill cycle's amount; income jars get none; cushion gets none.** Confirm?

**Answer:** *(author, 2026-07-18)* **Mostly confirmed — but keep the rules separate, don't merge them; and note the caveats:**
- The **cushion** is *not* part of this at all (the deallocation/milestone rules never mention it) — **keep `3.13.5.4.a1`, `10.4.a1`, `10.4.a2`, `10.4.a3` as distinct rules**, don't fold them into one combined statement.
- **"Income jars" aren't a real/useful concept** — *"I don't think income jars are really a thing... if income has a fund jar, that's why the milestone amount is none"* (i.e. `10.4.a3`: repeated positive → milestone None *is* the "income" case; there's no separate income-jar notion to model).
- **Repeated bills → flat milestone = one cycle's amount** (`10.4.a2`, `-1 × pattern.amount`): confirmed as the original intent — *"I guess it does mean repeated bills have fund jars with a flat milestone amount. I guess that makes sense."* **Caveat to record (author):** for users with very large payments or many small paychecks this flat-milestone assumption "might not be all that useful in our redesign." → carry it as documented original intent, but flag it as a candidate for rethinking when milestones are revisited.
- Running-sum milestones for one-time goals: unchanged/confirmed.

---

## G. Process-step annotations

### Q34 ○ "all* earmarks created on this page" — what does the asterisk mean?

Presumably "all except implicit earmarks, which may be added later (on deallocation days)" — matching `3.13.8.a7`'s "(not including implicit earmarks which may be added later)"?

**Answer:** *(author, 2026-07-18)* **"No idea."** Left unresolved — minor. The 05 guess (asterisk ≈ "except implicit earmarks added later," by analogy with `3.13.8.a7`'s parenthetical) is a reasonable default but is **not** author-confirmed.

### Q35 ◆ The "also calculate ..." annotation — confirm the consciously-applied pass runs last.

The label *"also calculate current_unpaid_expected for each page / current_free_amount for today's page / current_safety_cushion for today's page / and milestone_amount for each fund jar"* sits at the far right of the canvas next to the cross-page rollup regions. Confirm the intended order: cascade everything (per page, then cross-page rollup) **first**, then run the consciously-applied calculations (`3.a2`/`1.2.3c.12.a3`) as the final step, at interaction time.

**Answer:** *(author, 2026-07-18)* **Author declined to rely on chart-reading here** — *"It was mostly a visualization tool for my benefit. I think you can just use the assumptions and their listed dependencies alone to piece together the order those calculations are supposed to be made. Try it."*

**Derived purely from the requirement sets (no chart geometry used):**

The four consciously-applied calcs are the members of `3.a2`'s requirement set `{3.13.5.4.a1, 3.5.a1, 1.2.3.5.a1, 1.2.3.6.a1, 3.7.a1, 3.7.a2}`. Their inter-dependencies (from the `.txt`/`allReduced` sets):
- `1.2.3.6.a1` (current_unpaid_expected) ← `{1.2.3.13.a2` (pairing complete)`, 1.2.3c.12.a2` (initial snapshot maintained)`, 1.2.3p.6.a1` (previous page's unpaid)`}`
- `3.7.a1`/`3.7.a2` (current_safety_cushion) ← `{1.2.3c.12.a2}`
- `3.13.5.4.a1` (milestone_amount) ← `{1.2.3c.12.a2, 1.2.3c.12.a3, 3.13.8.a8, 3.13.5.a5, 3.a1}` (needs all earmarks + all jars maintained + page maintained)
- `1.2.3.5.a1` (current_free_amount) ← `{1.2.3.6.a1}` — **and its formula reads today's `full_amount`, every fund jar's `current_amount`, the cushion jar, and `current_unpaid_expected`**

Topologically, that forces this order **after** the full cascade + pairing (`3.a1`, "page is maintained"):
1. **current_unpaid_expected**, **current_safety_cushion**, **milestone_amount** — mutually independent; each needs only cascade outputs + pairing + the maintained snapshot.
2. **current_free_amount** — strictly **last**, because it is the only one that consumes another calc's output (`current_unpaid_expected`) *and* reads the finalized jar/cushion values.

And `3.a2` (the "all consciously-applied calcs done" node) sits above all four and is itself required by `1.2.a1` (the whole book) — so **yes, the consciously-applied pass runs last, after the per-page and cross-page cascade**, exactly as the annotation suggests. This confirms the intended order **from the dependency graph alone**, independent of how the chart was drawn.

---

## Status (2026-07-18)

**All 35 questions have author answers** (a few are "grey area / no plan" or partial, recorded as such). Remaining genuinely-open items are new-design decisions, not archaeology:
- **Interaction timing** (Q11) — when the user may edit vs. when the record is mid-cascade. Open; regions don't settle it.
- **`?...?` formulas with no C# default yet** (Q17 unpaid-bill offset) — deferred until real unpaid-bill tracking.
- **Q1 per-case chart-vs-txt conflicts** — to be adjudicated one at a time as the reconciled master catalog is built.
- Two unconfirmed typos (Q31 #1, #3) — carry verbatim, flagged.

## Queued follow-up work (surfaced by this Q&A)

1. **★ DONE (2026-07-18) — resolved by pivoting from geometry to box text.** The junction-aware re-parse was attempted; following the shared-arrow junctions proved **too fragile** (dense canvas + 12px-tall packed boxes make even a corrected geometric parse noisy — direct both-box edges matched the box text only ~51%). So the graph was rebuilt from the **box-text requirement sets** — the chart's actual, author-maintained content, which parses unambiguously. Result: **[08-assumption-graph-reconciled.md](08-assumption-graph-reconciled.md)** — 154 assumptions, **zero dangling references**, an acyclic DAG in **21 topological layers**, 32 cross-instance cascade couplings separated out. Regenerable via `build_assumption_graph.py`. **This is the authoritative dependency graph now; 05's drawn-edge list is retired.**
2. **Largely DONE via 08 — remaining: numbering/`.txt` reconciliation.** 08 is the reconciled graph (chart box text as source, chart-only assumptions folded in, `3:13.a9` typo merged, `{whatever}` nodes flagged as undetermined-not-axiom). Still outstanding for a full "master catalog": re-key to assumption-file numbering with an ODS-scheme mapping (Q4), attach prose for `.txt`-only variants, and adjudicate the per-case Q1 chart-vs-`.txt` conflicts (08 uses the chart set throughout).
3. **DONE — corrections propagated.** Banners on **05** (edge/region data retired → points to 08) and **06** (region tables → point to the author-corrected table above); **00** references 08; Discrepancy #4 withdrawn, #1 reframed as a parse artifact; interaction-window reading retired.

## Bookkeeping log

- **Batch 1 (2026-07-18):** Q1–Q4, Q7 (arrows), Q10 (pink) + observations 1–4.
- **Batch 2 (2026-07-18):** Q9, Q16–Q23, Q8 (reframed) + observations 5–6; region double-check requested.
- **Batch 3 (2026-07-18):** Q5, Q6, Q7-subpart, Q8-residual, Q10 (gray/green), Q11–Q15, Q24–Q35 + observations 7–9 + the author-corrected region table. All questions now answered.
