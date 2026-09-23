# 13 — "Adjusting the Plan": phase charter

The map and surviving record for the **"Adjusting the Plan"** phase — the work answering
[Q4](04-project-goals-and-user-questions.md): *the user (or reality) changed their mind, and the
plan has to change with them.* **The phase's capabilities are built** — every change mechanism is
reachable through ordinary form editing. The per-stage design docs (14–17, 19) are retired; their
design lives in the code, and this charter is the record that spans them.

## Vocabulary

| Level | Term | Granularity |
|---|---|---|
| 1 | **Phase** | this whole body of work |
| 2 | **Stage** | one numbered doc (14, 15, …), one or two sittings |
| 3 | **Item** | a lettered decision inside a stage |

- **"Pre-funded" is RESERVED — do not reuse the word.** It is held for a future concept: *an earmark
  pattern that has a non-repeated (implicit or manual) earmark created for it on the very first day it
  exists* (likely Stage 4 territory).
- Deallocation's Step A input is **"the jar of a paired transaction"** — "paired" is kept deliberately
  (the author's term from `DeallocationProof.ods`, used only inside deallocation); `PairedTransaction`
  keeps its name.

## Standing rule — design in the class documentation's own terms

Frame every problem in the class model (`FinancialPattern` / `EarMarkPattern` / `EarMarkEvent` /
`FundJar` / `BalanceSnapshot`) and in assumption IDs, not a category invented for the conversation —
**the documented dependency order is design guidance, not just validation.** New abstractions are
allowed (philosophy 3a) as a recorded `DIVERGENCE(...)` on top of the model, never as the language the
design is thought in. Read [03](03-assumptions-glossary.md) and
[06](06-assumption-dependency-graph.md); don't grep them. Full rationale:
`memory/feedback_design_in_class_documentation_terms.md`.

## The two standing constraints — the walls this phase designs against

1. **Patterns are linear, gapless, single-valued.** A `FinancialPattern` / `EarMarkPattern` carries
   one `Amount` and one `RecurrenceRule` (frequency / interval / start / until — no curve, step, gap,
   or per-occurrence override). Any change regenerates its occurrences wholesale, so every feature is
   composed from: splitting patterns, adding patterns, one-off manual earmarks, or values computed
   rather than stored. Each sanctioned workaround, with its cost, is in the **Linearity workarounds**
   section below.
2. **Every rrule must terminate.** `RecurrenceRule` accepts `Count` only as entry sugar and resolves
   it to an `Until`; there is no representable "forever." This is the mechanical root of the
   "when does this stop?" question (Stage 2).

## Standing conventions for the phase

- Anything actuals-dependent is appended to [12](12-actual-transactions-deferred-design.md) and **not**
  designed here.
- Any departure from the original design gets a `DIVERGENCE(<topic>)` tag at the code site
  (`grep -rn DIVERGENCE src/` is the live registry; [12](12-actual-transactions-deferred-design.md)
  summarizes the actuals-relevant ones).
- Any new Constraint-1 workaround gets a costed entry in the **Linearity workarounds** section below,
  plus a `DIVERGENCE(<topic>)` tag at its code site.
- **UI-change budget:** the Forecast tab is settled (changes need a strong reason); every other tab is
  open and due for a rework, so adding a control there is cheap.

## Status — what each stage delivered (all built; design in the code)

| Stage | Delivered, and where it lives |
|---|---|
| 0 — Charter, constraints, catalog | This doc — the **Linearity workarounds** and **What the system does on its own** sections below absorbed the former 13a/13b registries. |
| 1 — Allocation model | Allocation Plans, income-never-a-jar, skippability, the "thin"/cushion-not-whole state — `AllocationPlanProposer`, `EarMarkPattern`. |
| 2 — Pattern lifetime & form family | "When does this stop?" (keeps-going / ends-on-a-date / paid-off), `RecurrenceRule.ActiveFrom`, `AutoRenew` + forecast-time renewal, the loan-payoff helper — `PayoffEstimator`, `BreakOffFactory.Renew`, `MainWindow.RenewOngoingPatternsToHorizon`. |
| 3 — Change a pattern at a point | Break-off, truncation, deletion, transfer break-off, periodic renewal — `BreakOffFactory`, `PatternTruncation`, `TransferBreakOffFactory`, `TransferTruncation`. Reached by editing a pattern and saving: the save-confirmation performs the break-off/truncation. |
| 4 — Change an allocation alone | Multiple `EarMarkPattern`s per one `finance_id` (the F27 relaxation of `3.11.1.a1`), restructure a plan, stop contributing, overfunded detection — `RestructureFactory`, `GoalShortfall.OverfundedAmount`. Reached by editing an earmark plan and saving. |
| 5 — Action catalog audit | Complete — every action reached a built stage or an explicit deferral; the running app is the live record of what the UI does. |
| 6 — Warnings, levers, shortcuts | **DROPPED.** The Stage 1–4 change mechanisms are all reachable through ordinary form editing, so a dedicated shortcut/lever/warning layer (a "Change this…" entry point, per-state levers, warning wording) was judged optional polish and dropped. Re-plan from scratch if user testing shows a need. |

---

## Linearity workarounds

**The constraint (Constraint 1 above):** a pattern carries one amount and one recurrence rule — no
curve, step, gap, or per-occurrence override; any change regenerates its occurrences wholesale. The
sanctioned tricks the phase composes features from, and what each **costs** (the cost is the point —
these are all trades):

| Workaround | How | Cost |
|---|---|---|
| Compose one concept from several patterns | one action creates + links patterns, shown as one thing (`OneTimeGoalFactory`, `TransferFactory`) | the pieces must be kept consistent and hidden from raw lists; per-pattern engine behavior (priority, mandatory, account filing) still applies to each separately |
| Single-occurrence rrule as "a one-time thing" | `Count = 1` — an ordinary pattern with one occurrence | ~none (the documented model's own approach); everything still needs a synthesized `finance_id` |
| `Count` resolved to `Until` at construction | entered as a count, stored as the equivalent end date | the user's intent isn't retained — if the start later moves, the count doesn't re-derive |
| Compute per day instead of storing | the whole onion is rebuilt in memory each run; only patterns, balances, and manual earmarks persist | no history, nothing frozen; `Expired` is permanently false; every edit is retroactive by default. Buys clean account-moves, no-stale recompute, and byte-copy import/export |
| Implicit isolated earmark (the escape hatch) | a one-off `EarMarkEvent` no pattern generates — deallocation (negative) and the positive auto-funding (`DIVERGENCE(positive-implicit)`) | invisible as a plan (money moves with no editable schedule); two writers to the same jar/day merge silently |
| User-authored one-offs on a pattern | manual earmarks (Add / Withdraw / Move) deviate on a single day without touching the pattern (`ManualEarmark`) | bounded to the pattern's span; jars floored at 0; a drifted plan can leave a stored withdrawal oversized (floored + flagged in place) |
| Split-and-continue ("break off") | cut the rrule at a date and continue in a new pattern, handing the jar balance across (`BreakOffFactory`) | identity splits — one real-world bill becomes two `finance_id`s that must be re-joined to reason about it over time |
| Internal auto-renewal of an open-ended rule | `AutoRenew` keeps a real `Until` rolled forward to the horizon each run (`BreakOffFactory.Renew`) | the stored `Until` must never be shown raw; a plan on an ongoing pattern must be ongoing too |

## What the system does on its own

The non-obvious automatic behaviors (everything else the app does is ordinary CRUD, embodied in the app
itself):

- **Reserve toward any outflow** via a proposed Allocation Plan at creation, then that plan's scheduled
  contributions — editable, or removable (the outflow then just shows short).
- **Deallocation:** when spending overdraws free funds, drain the cushion, then skippable jars, then
  unskippable ones, by priority within each group. Priority and skippable/unskippable are user-set and
  decide the order.
- **Release** a goal's jar on its due date (the balance drops toward zero as the goal is paid).
- **Floor** a jar at zero on any day it would go negative (flagged in place).
- **Merge** an implicit earmark into a manual one on the same jar/day — `ExplicitAmount` keeps what the
  user actually entered underneath.
- **Hand a jar balance across a break-off**, shown and editable on the confirm screen.
- **Auto-renew** an open-ended pattern's rule every `SegmentYears` — silent, with a "(renewed *date*)"
  trace appended to the description.
