# 13 — "Adjusting the Plan": phase charter

The map and tracker for the **"Adjusting the Plan"** phase — the work answering
[Q4](../../04-project-goals-and-user-questions.md): *the user (or reality) changed their mind, and the
plan has to change with them.* Stages 0–6 are **design-complete**; what remains is UI wiring (see the
backlog at the end). Each stage has its own doc (14–19); this file holds only what spans them.

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
design is thought in. Read [03](../../03-assumptions-glossary.md) and
[06](../../06-assumption-dependency-graph.md); don't grep them. Full rationale:
`memory/feedback_design_in_class_documentation_terms.md`.

## The two standing constraints — the walls this phase designs against

1. **Patterns are linear, gapless, single-valued.** A `FinancialPattern` / `EarMarkPattern` carries
   one `Amount` and one `RecurrenceRule` (frequency / interval / start / until — no curve, step, gap,
   or per-occurrence override). Any change regenerates its occurrences wholesale, so every feature is
   composed from: splitting patterns, adding patterns, one-off manual earmarks, or values computed
   rather than stored. Each sanctioned workaround, with its cost, is an entry in
   [13a](13a-linearity-workaround-registry.md).
2. **Every rrule must terminate.** `RecurrenceRule` accepts `Count` only as entry sugar and resolves
   it to an `Until`; there is no representable "forever." This is the mechanical root of the
   "when does this stop?" question (Stage 2).

## Standing conventions for the phase

- Anything actuals-dependent is appended to [12](12-actual-transactions-deferred-design.md) and **not**
  designed here.
- Any departure from the original design gets a `DIVERGENCE(<topic>)` tag at the code site
  (`grep -rn DIVERGENCE src/` is the live registry; [12](12-actual-transactions-deferred-design.md)
  summarizes the actuals-relevant ones).
- Any new Constraint-1 workaround gets a costed entry in [13a](13a-linearity-workaround-registry.md);
  every user action is a row in [13b](13b-user-action-catalog.md).
- **UI-change budget:** the Forecast tab is settled (changes need a strong reason); every other tab is
  open and due for a rework, so adding a control there is cheap.

## Status

| Stage | State |
|---|---|
| 0 — Charter, constraints, catalog skeleton | **DONE.** This doc + [13a](13a-linearity-workaround-registry.md) (W1–W9) + [13b](13b-user-action-catalog.md). |
| 1 — Allocation model | **BUILT** — [14](14-stage1-allocation-model.md). Allocation Plans, income-never-a-jar, skippability, "thin". Open: the goal-creation flow isn't unified onto the proposer, and "Set Up Savings Plan…" still opens the old popup. |
| 2 — Pattern lifetime & form family | **BUILT** — [15](15-stage2-pattern-lifetime.md). "When does this stop?", `ActiveFrom`, `AutoRenew`/`Renew`, payoff helper. Open: the decline-a-plan UI; item D's form uncertainties. |
| 3 — Change a pattern at a point | **DOMAIN BUILT** — [16](16-stage3-break-off.md). Break-off / truncation / delete / transfer break-off / renewal. Open: 4-D confirm screen + entry points (backlog below). |
| 4 — Change an allocation alone | **OPEN, mostly built** — [17](17-stage4-allocation-only-changes.md). F27 relaxed (multiple plans per `finance_id`); items 8/9/22 and 24's detection built (`RestructureFactory`). Open: item 24's nudge + all UI wiring. |
| 5 — Action catalog audit | **DONE** — folded into [13b](13b-user-action-catalog.md); the UI/wiring backlog it produced is below. |
| 6 — Warnings, levers, shortcuts | **OPEN** — [19](19-stage6-warnings-levers-shortcuts.md). Policies settled (button-sanity via one "Change this…" entry point; cushion-not-whole wording ◑). Open: per-state levers, shortcut inventory, and the "Change this…" wording — deferred to a dedicated UI stage. |

## The UI/wiring backlog

Every stage from 1–4 built and tested a mechanism, then deferred its screen. This is the running total
— none are design gaps (the data each screen needs is settled), just screens nobody has drawn yet.
**Snapshot from the Stage-5 audit; some rows have since been built during the form / save-confirmation
work (docs 21–28, [24](24-app-layer-known-gaps.md)) — verify a row against the code before acting on it.**

| # | What's missing | What it needs |
|---|---|---|
| 1 | "Change starting on a date" (break-off, item 4) | an entry point on a bill/paycheck row + a confirm screen (cut date, pre-filled successor plan, carried-over balance) — [16](16-stage3-break-off.md) 4-D |
| 2 | "Stop this on a date" (truncate, item 16) | an entry point + the schedule-driven end-date input already built for loan-payoff |
| 3 | "Restructure the plan" (item 8) | an entry point on an earmark-pattern row + a cut-date / new-rate dialog — [17](17-stage4-allocation-only-changes.md) |
| 4 | "Stop contributing" (item 22, `RestructureFactory.StopContributing`) | likely the same dialog as #3 with a "stop entirely" option |
| 5 | Declining a proposed Allocation Plan (`ProposeEmpty` consumer) | a "remove this plan" affordance landing on the empty-plan-plus-jar shape, not a hard delete |
| 6 | Scheduled trigger for pattern renewal (S7, `BreakOffFactory.Renew`) | an app-layer background check ("has `SegmentYears` passed and is `AutoRenew` set?") that calls `Renew`; covers transfers via `TransferBreakOffFactory.Renew` |
| 7 | Stale-pattern edit redirect (`FindCurrentSegment`) | **built — see [24](24-app-layer-known-gaps.md)** (wire "Edit" to resolve to the current segment) |
| 8 | Surfacing `OverfundedAmount` (item 24) | an active nudge once it crosses a threshold (proposed 10% of `AmountNeeded`, unconfirmed) with the "stop contributing" lever |
| 9 | Item D's loan-payoff form | confirmed too tall for a laptop; smaller uncertainties in [15](15-stage2-pattern-lifetime.md) |
| 10 | B12's creation-time "it just keeps going" checkbox | set `AutoRenew = true` on save (the marker exists; the checkbox is a stub) |
| 11 | Transfer create-flow + transfer-factory entry points | a dedicated 3-tier "Create Transfer…" form, and entry points for `TransferBreakOffFactory` / `TransferTruncation` — [21](21-form-architecture.md) |

Everything above is domain/engine-complete and tested; it is blocked on a UI pass (layout, wording,
entry-point placement), not a design decision.
