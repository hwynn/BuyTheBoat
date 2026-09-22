# MyMoneyForecast — Documentation Guide

A map of every design/planning document in this rework, in **descending order of importance for understanding the project**. For each file: what it explains, and *when* you would want to read it — based on how fresh you are to the project, or the specific task you are doing. Read the top tiers to orient; jump straight to a file when a task calls for it. You will not need all of this at once.

**The one distinction that prevents confusion** — there are three doc families here, and the numbers in two of them overlap:

- `redesign/00–06` + `design-philosophies.md` — the reconstructed **original** design (what the 2020–2022 source docs specified) plus forward-looking principles. Backward-looking, *except* `design-philosophies.md`.
- `redesign/MyMoneyForecast/planning/` (docs 06, 12, 13, 21, 26, 28) — the **new C# implementation's** planning and design. Forward-looking.
- `redesign/memory/` — portable session memory, loaded as context each session; `MEMORY.md` is its index.

So "06" means two different documents depending on the folder (`redesign/06-assumption-dependency-graph.md` vs `planning/06-deallocation-math.md`) — **always mind the path.**

**Before touching any form's UI** — the settled design for the forms and the forecast tab (each
region's goals, the standing UI + form-behavior rules, the plan-health content) lives in
[planning/21-forms-and-ui.md](21-forms-and-ui.md) and
[design-philosophies.md](design-philosophies.md) (Philosophy 4 especially). **Re-read the specific
section for what you're about to touch immediately before writing the code** — not from memory of an
earlier read this session, which is exactly the gap that let past UI code drift from its own settled
design (see [[feedback-reground-in-source-before-building]] in `memory/`). The built forms and regions
themselves live in the code and `planning/mockups/settled-designs.html`.

---

## Tier 1 — Orientation (read first if you are new to the project)

- **[04-project-goals-and-user-questions.md](04-project-goals-and-user-questions.md)** — the four core user questions the whole app exists to answer (how much free money · can I afford X · am I on track for goals · how do I readjust). *This is the lens for evaluating any feature.* Read before judging any design.
- **[design-philosophies.md](design-philosophies.md)** — the standing principles, now **seven** (1–3 original: inform and equip without automating away agency; speak the user's language; the old docs inform but don't dictate. 4–7 added 2026-07-30 during the UI-implementation stretch of the "Adjusting the Plan" phase: put information where the decision happens; every form stays reachable on its own tab; a shortcut shows its own path; a small, reusable set of forms). Read early — they silently govern every decision.
- **[GUIDE.md](GUIDE.md)** — this file.
- **[memory/reference_redesign_folder.md](../../memory/reference_redesign_folder.md)** — a one-screen description of what lives where inside `redesign/`.

## Tier 2 — The domain model (what the app *is*)

- **[01-glossary-of-terms.md](01-glossary-of-terms.md)** — all 10 classes, every property and method, and the domain vocabulary (finance_id, fund jar, earmark, deallocation, safety cushion, …). Read whenever a term or class is unfamiliar.
- **[02-uml-diagram.md](02-uml-diagram.md)** — the class diagram plus a "who owns what" containment view. Read when you need the structure — e.g. which class a property lives on, or what is per-account vs. book-level.

## Tier 3 — Current & recent design work (read before touching these areas)

- **[MyMoneyForecast/planning/13-adjusting-the-plan-charter.md](13-adjusting-the-plan-charter.md)** — the **"Adjusting the Plan"** phase record (answers Q4: the plan has to change when the user or reality does). **The phase's capabilities are built** — every change mechanism is reachable through ordinary form editing. Self-contained: what each stage delivered and where it lives in code, the linearity-workaround costs and the automatic-behavior list (the former 13a/13b registries, folded in), and the deferred/declined items. The former per-stage/editing docs (14–17, 19, 25, 27) are gone — their design is in the code and summarized here. Read it first for anything about changing a saved bill, paycheck, goal, or plan.
- **[MyMoneyForecast/planning/21-forms-and-ui.md](21-forms-and-ui.md)** — the durable design reasoning for the forms **and** the forecast tab (consolidates the former 11/21/22/23): the three-form system + region layout, the forecast-tab region goals/non-goals + the multi-account rule, the standing UI principles, the form-behavior rules (Downward-only editing, Forced/Suggested, Saved/Working state, break-off-vs-alter), and the Earmark plan-health content (the `IsWorthWarningAbout` rules, the shortfall ladder). **Read before any form or forecast-tab UI change.** The built forms and regions themselves live in the code (`ExpenseFormPanel`/`AccountFormPanel`/`EarmarkFormPanel`/`TransferFormPanel`, `RecurrenceRuleEditor`, `SummaryRegion`, `PlanHealthMessages`) and `planning/mockups/settled-designs.html`.
- **Accounts & transfers** *(retired to the code)* — the per-account "silo" model, transfers-as-paired-patterns, and the engine partition live in the code: `Account`, `Transfer`, `TransferFactory`, and the per-account pages built in `TransactionLogBookFactory`.
- **The forecast engine** *(retired to the code)* — the `TransactionLogBookFactory` "onion," the assumed-pairing philosophy, and the divergence registry now live in the code; regenerate the registry with `grep -rn "ASSUMED-PAIRING\|DIVERGENCE" src/`. Read `TransactionLogBookFactory` before touching the engine/cascade.

## Tier 4 — The original assumptions (look things up; don't read cover-to-cover)

- **[03-assumptions-glossary.md](03-assumptions-glossary.md)** — every original assumption, verbatim, in 19 chapters. Reference when you need the exact wording or intent of a specific rule (e.g. `3.13.5.4.a1`, the milestone formula).
- **[06-assumption-dependency-graph.md](06-assumption-dependency-graph.md)** — the assumptions as an acyclic dependency graph plus the cascade "process regions." Use for any *dependency* question ("what must hold before X is valid").
- **[05-assumption-chart-full-text.md](05-assumption-chart-full-text.md)** — verbatim transcription of the assumption-chart boxes; the source companion to 03 and 06.
- **[00-sources-and-notes.md](00-sources-and-notes.md)** — provenance and dates of every original source file, and which supersede which. Read when you need to trust or trace a reconstructed claim back to its origin.

## Tier 5 — Feature & engine deep-dives (read for that specific feature)

- **[MyMoneyForecast/planning/06-deallocation-math.md](06-deallocation-math.md)** — the deallocation distribution math (Step A/B, verbatim formulas, worked examples, reusable test oracles). Read before touching deallocation, the safety cushion, or priority reallocation.

The other feature deep-dives were retired once their content moved into the code's own doc comments —
read the relevant class/method docs in `src/` (and the tests, the behavioral oracles): the deallocation
implementation (`DeallocationCalculator`), manual earmarks (`ManualEarmark`, `EarmarkFormPanel`'s
one-off mode), the data-entry/CRUD windows and import/export (the form panels + the repositories), and
the forecast-tab region goals (now in [21-forms-and-ui](21-forms-and-ui.md)).
The C#-vs-SQLite / pure-domain-no-ORM stack decision is simply the shape of `src/` now.

## Tier 6 — Mockups & live memory

- **[MyMoneyForecast/planning/mockups/](mockups/)** — HTML/PNG layout explorations plus a `README.md` recording the chosen directions. Open the `.html` files in a browser to see the UI options; read the README for what was picked and why. (Multi-account rounds: `*-multiaccount-*.html`.)
- **[memory/MEMORY.md](../../memory/MEMORY.md)** — the index of portable memory files (project facts, user feedback, references). Skim it when starting fresh; it points to the detail files. Memory reflects what was true when written — verify a named file/flag still exists before relying on it.

## The code

- **`redesign/MyMoneyForecast/src/`** — the C# implementation: `.Domain` (pure model, no persistence), `.Persistence` (SQLite repositories), `.App` (WPF UI). Read the code when a doc's claim needs confirming against reality, or before editing a specific class. **The tests in `redesign/MyMoneyForecast/tests/` are the behavioral oracles** — the truest statement of what the engine actually does.

---

*Housekeeping note:* a few stray `*-squid.md` / `*-pumpkin.md` / `*-wren*.md` files at the `redesign/` root are archived plan-mode snapshots from past sessions — historical, not needed for current work.
