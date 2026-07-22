# MyMoneyForecast — Documentation Guide

A map of every design/planning document in this rework, in **descending order of importance for understanding the project**. For each file: what it explains, and *when* you would want to read it — based on how fresh you are to the project, or the specific task you are doing. Read the top tiers to orient; jump straight to a file when a task calls for it. You will not need all of this at once.

**The one distinction that prevents confusion** — there are three doc families here, and the numbers in two of them overlap:

- `redesign/00–06` + `design-philosophies.md` — the reconstructed **original** design (what the 2020–2022 source docs specified) plus forward-looking principles. Backward-looking, *except* `design-philosophies.md`.
- `redesign/MyMoneyForecast/planning/01–11` — the **new C# implementation's** planning and design. Forward-looking.
- `redesign/memory/` — portable session memory, loaded as context each session; `MEMORY.md` is its index.

So "05" means two different documents depending on the folder — **always mind the path.**

---

## Tier 1 — Orientation (read first if you are new to the project)

- **[04-project-goals-and-user-questions.md](04-project-goals-and-user-questions.md)** — the four core user questions the whole app exists to answer (how much free money · can I afford X · am I on track for goals · how do I readjust). *This is the lens for evaluating any feature.* Read before judging any design.
- **[design-philosophies.md](design-philosophies.md)** — the three standing principles: (1) inform and equip, don't automate away agency; (2) speak the user's language, not the program's; (3) the old docs inform the design but don't dictate it. Read early — they silently govern every decision.
- **[GUIDE.md](GUIDE.md)** — this file.
- **[memory/reference_redesign_folder.md](memory/reference_redesign_folder.md)** — a one-screen description of what lives where inside `redesign/`.

## Tier 2 — The domain model (what the app *is*)

- **[01-glossary-of-terms.md](01-glossary-of-terms.md)** — all 10 classes, every property and method, and the domain vocabulary (finance_id, fund jar, earmark, deallocation, safety cushion, …). Read whenever a term or class is unfamiliar.
- **[02-uml-diagram.md](02-uml-diagram.md)** — the class diagram plus a "who owns what" containment view. Read when you need the structure — e.g. which class a property lives on, or what is per-account vs. book-level.

## Tier 3 — Current & recent design work (read before touching these areas)

- **[MyMoneyForecast/planning/10-multiple-accounts.md](MyMoneyForecast/planning/10-multiple-accounts.md)** — **the currently active feature.** The per-account "silo" model, transfers-as-paired-patterns, the engine partition, persistence/migration, and every item-by-item decision. Read before any accounts or transfers work.
- **[MyMoneyForecast/planning/11-ui-design-and-decisions.md](MyMoneyForecast/planning/11-ui-design-and-decisions.md)** — the forecast-tab UI: each region's goals **and non-goals**, the chosen multi-account layouts (overview = rich month calendar; selected day = grouped two-pane), and the log of refinement choices with reasoning. Read before any forecast-tab UI change.
- **[MyMoneyForecast/planning/05-original-structure-restructure.md](MyMoneyForecast/planning/05-original-structure-restructure.md)** — how the forecast engine is actually built: the `TransactionLogBookFactory` "onion," the assumed-pairing philosophy, and the divergence registry (regenerate with `grep -rn "ASSUMED-PAIRING\|DIVERGENCE" src/`). Read before touching the engine/cascade.

## Tier 4 — The original assumptions (look things up; don't read cover-to-cover)

- **[03-assumptions-glossary.md](03-assumptions-glossary.md)** — every original assumption, verbatim, in 19 chapters. Reference when you need the exact wording or intent of a specific rule (e.g. `3.13.5.4.a1`, the milestone formula).
- **[06-assumption-dependency-graph.md](06-assumption-dependency-graph.md)** — the assumptions as an acyclic dependency graph plus the cascade "process regions." Use for any *dependency* question ("what must hold before X is valid").
- **[05-assumption-chart-full-text.md](05-assumption-chart-full-text.md)** — verbatim transcription of the assumption-chart boxes; the source companion to 03 and 06.
- **[00-sources-and-notes.md](00-sources-and-notes.md)** — provenance and dates of every original source file, and which supersede which. Read when you need to trust or trace a reconstructed claim back to its origin.

## Tier 5 — Feature & engine deep-dives (read for that specific feature)

- **[MyMoneyForecast/planning/06-deallocation-math.md](MyMoneyForecast/planning/06-deallocation-math.md)** — the deallocation distribution math (Step A/B, verbatim formulas, worked examples, reusable test oracles). Read before touching deallocation, the safety cushion, or priority reallocation.
- **[MyMoneyForecast/planning/07-deallocation-implementation-plan.md](MyMoneyForecast/planning/07-deallocation-implementation-plan.md)** — the staged plan for building deallocation (status: done). Read for the history and decisions behind the current cushion + deallocation code.
- **[MyMoneyForecast/planning/09-manual-earmarks.md](MyMoneyForecast/planning/09-manual-earmarks.md)** — the manual (explicit) earmarks design: possibility matrix, validation policy, UI flows. Read before touching manual earmarks.
- **[MyMoneyForecast/planning/08-forecast-tab-design-philosophy.md](MyMoneyForecast/planning/08-forecast-tab-design-philosophy.md)** — the original single-account forecast-tab philosophy. Consolidated for UI *decisions* by doc 11, but the fuller prose on each region's job lives here.
- **[MyMoneyForecast/planning/03-data-entry-uis.md](MyMoneyForecast/planning/03-data-entry-uis.md)** — the pattern/goal CRUD windows plus data import/export. Read before touching the entry windows.
- **[MyMoneyForecast/planning/01-tech-stack-and-testing-strategy.md](MyMoneyForecast/planning/01-tech-stack-and-testing-strategy.md)** & **[02-csharp-sqlite-build-plan.md](MyMoneyForecast/planning/02-csharp-sqlite-build-plan.md)** — why C# + SQLite, the testing pyramid, the walking-skeleton build order. Read when setting up, or when questioning the stack.
- **[MyMoneyForecast/planning/04-forecast-timeline-tab.md](MyMoneyForecast/planning/04-forecast-timeline-tab.md)** — early "planned, not built" notes for the forecast tab. **Largely superseded** by 05 / 08 / 11; historical only.

## Tier 6 — Mockups & live memory

- **[MyMoneyForecast/planning/mockups/](MyMoneyForecast/planning/mockups/)** — HTML/PNG layout explorations plus a `README.md` recording the chosen directions. Open the `.html` files in a browser to see the UI options; read the README for what was picked and why. (Multi-account rounds: `*-multiaccount-*.html`.)
- **[memory/MEMORY.md](memory/MEMORY.md)** — the index of portable memory files (project facts, user feedback, references). Skim it when starting fresh; it points to the detail files. Memory reflects what was true when written — verify a named file/flag still exists before relying on it.

## The code

- **`redesign/MyMoneyForecast/src/`** — the C# implementation: `.Domain` (pure model, no persistence), `.Persistence` (SQLite repositories), `.App` (WPF UI). Read the code when a doc's claim needs confirming against reality, or before editing a specific class. **The tests in `redesign/MyMoneyForecast/tests/` are the behavioral oracles** — the truest statement of what the engine actually does.

---

*Housekeeping note:* a few stray `*-squid.md` / `*-pumpkin.md` / `*-wren*.md` files at the `redesign/` root are archived plan-mode snapshots from past sessions — historical, not needed for current work.
