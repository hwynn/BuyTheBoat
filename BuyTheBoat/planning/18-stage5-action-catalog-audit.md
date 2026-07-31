# 18 — Stage 5: The action catalog, audited

**Status: DONE 2026-07-30.** Items **7, 21** (charter). Stage 5 of the
["Adjusting the Plan" phase](13-adjusting-the-plan-charter.md). **Update, same day:** the three
genuine gaps this audit surfaced (C1, B12, C10) are now **designed** — see their resolutions under
[Genuine gaps](#genuine-gaps-this-audit-found--not-audited-away-carried-forward) below — worked one
at a time with the author. **A separate, related thread the same day:** the transfer create-flow's
structure is settled and three small transfer-specific gaps are identified — see
[Transfers](#transfers--the-create-flow-structure-and-whats-actually-missing) below. Nothing in
either update is built yet.

**The stage in one paragraph:** [13b — the user action catalog](13b-user-action-catalog.md) and the
system-action table inside it were populated with **rows only** at Stage 0, cells left blank for
whichever stage settled them. This stage is the audit the charter promised: go through every row,
close what stages 1–4 actually decided, and — the entire point — make sure every remaining blank is
**labelled** as one of three things, never left to look like an oversight: answered (✔), a genuine
gap still open, or out of this phase's scope entirely. Also closes item 21 directly (an entry-point
question, not a mechanism one) and item 15 (every system action now says what the user sees).

### Reading list
1. [13b — the user action catalog](13b-user-action-catalog.md) itself — this stage's entire subject.
2. [13 — charter](13-adjusting-the-plan-charter.md), items 7, 15, 21, and the Stage 5 description.
3. [09](09-manual-earmarks.md), [16](16-stage3-break-off.md), [17](17-stage4-allocation-only-changes.md)
   — where most of the content filled in below actually comes from.

---

## What this stage did

Went through every row in [13b](13b-user-action-catalog.md), table by table:

- **Tables A (Accounts), E (Transfers), F (Forecast controls)** — audited as **out of phase**: plain
  CRUD and controls that predate "Adjusting the Plan" and that no stage 1–4 item touches. These were
  never gaps; the blank Stage column just meant "settled before this catalog existed." Labelled
  explicitly so a future reader doesn't mistake "blank" for "forgotten."
- **Table B (Bills & paychecks)** — filled B5/B6/B7/B9 using **F24**'s taxonomy (a plain, uniform edit
  vs. a deliberate forward-only change — settled in Stage 3, generalizes cleanly to priority and
  description too, confirmed in code for B7/B8's "no retroactive effect" claim); B10 now names the
  cross-account interaction F33 fixed; B14/B15 filled in full from Stage 3's `BreakOffFactory`/
  `PatternTruncation` — both **domain-complete, UI not wired in**. B12 stays an honest, un-audited-away
  gap (the "ongoing" pattern's UI trigger is still undesigned, not just unbuilt).
- **Table C (Allocations)** — the busiest table, since it's where items 8/9/22/23/24 all live. C2
  turned out to already be item 9's real entry point (checked in code: no gate against an
  already-earmarked goal). C3/C4 get the same plain-edit-vs-Restructure taxonomy as B5/B6, one level
  down. C7 (item 8), C9 (item 22) and C11 (item 23) are filled in full. **C10 (item 24) is marked
  partial, honestly** — detection is built, but nothing acts on it yet, and that gap *is* the
  eventual warn-about, not a bug to hide. **C1 stays open** — `OneTimeGoalFactory` was never unified
  onto `AllocationPlanProposer`, a refinement Stage 1 flagged and nobody has picked up.
- **Table D (Manual earmarks)** — filled D1–D5 from [09](09-manual-earmarks.md)'s own settled rulings.
  **Closes item 21**: two independent entry points already exist (the selected-day pane's "Adjust
  funds…" button, and the Allocations tab's management grid), so the discoverability question the
  charter asked has a real, already-built answer — not a gap at all.
- **System actions S1–S7** — filled every "what the user sees" cell, which the table's own text names
  as item 15's completion criterion. **S1's own content was stale** — it still described W6's
  computed A/B ramp, which Stage 1's 2026-07-24 revision retired; corrected rather than left to
  mislead, per this project's "superseded wording struck and labelled, not deleted" convention. **Item
  15 is now complete.**

**Items 7, 15, and 21 are closed by this pass.** Item 7 itself (*"every action, what we do implicitly,
what's left to the user"*) is as complete as it can be for a phase still missing its UI — every row
either has a real answer or an honestly-labelled gap.

## Genuine gaps this audit found — not audited away, carried forward

Distinguishing these from UI-only deferrals (below) matters: these need more **design**, not just
screen time. **All three DESIGNED 2026-07-30**, worked one at a time with the author (explain, an
example, a question each) — not yet built.

| # | Gap | Where it lives | Status |
|---|---|---|---|
| 1 | `OneTimeGoalFactory` was never unified onto `AllocationPlanProposer` — two code paths do overlapping work | C1, flagged since Stage 1's revision | **Designed** — see below |
| 2 | The "ongoing" pattern's UI trigger (how a user actually marks something open-ended, given the mechanism is renewal, not a flag) | B12 | **Designed** — see below |
| 3 | Item 24's system-triggered entry point and its nudge wording | C10 — mechanism (item 22) and detection (`OverfundedAmount`) are both built; nothing decides *when* to offer it or *how* to word it | **Designed** — see below |

### C1 — resolved: unify onto the proposer, but keep today's fallback for the no-income case

**The tension:** `AllocationPlanProposer` paces against a clean single income when one exists (an
improvement over today's flat installment), but its *other* fallback — front-load the entire amount
in one contribution, today — is right for a bill and wrong for a one-time goal. Reserving an entire
multi-year vacation fund's cost on day one, just because the household has two earners or the goal
sits in an account with no income filed under it, is a real behavior regression from what
`OneTimeGoalFactory` does today (spread it evenly).

**Author's ruling: keep today's even-spread behavior as the fallback.** Concretely: when
`AllocationPlanProposer` is asked to propose a plan for a single-occurrence outflow and there's no
clean single income to pace against, it should spread the amount evenly across the remaining time to
the due date — mirroring `OneTimeGoalFactory.BuildSavingsDatePattern`'s existing logic — **instead of**
front-loading. This is a genuine addition to the proposer's fallback logic, not pure reuse as-is; the
paced (Shape A) case is unaffected and is exactly why unifying is worth doing at all.

**Still open, not yet decided:** whether `OneTimeGoalFactory`'s user-facing `SavingsFrequency`
override (Weekly / Every other week / Monthly, shown directly in the create window today) survives
as an advanced choice, moves behind an "advanced" toggle (its own code comment already says it "is
only meant to be user-controlled behind an advanced opt-in" — which the current UI doesn't actually
do), or is dropped now that the proposer picks a cadence automatically. Not asked yet — a build-time
detail once this is implemented.

### B12 — resolved: a minimal "auto-renew" marker

**Author's ruling: add a small new property gating the (still unbuilt) scheduled renewal check** —
set invisibly when the user answers "it just keeps going," never exposed as its own control. Narrower
than the originally-rejected `ongoing` flag: this one only decides whether a background check should
consider a pattern for silent renewal; it never touches the rrule, the math, or any occurrence
generation. **This is a new domain property and therefore a divergence**, in the same category as
`ActiveFrom` — needs a `DIVERGENCE(auto-renew)`-style tag at the code site and a
[05 registry](05-original-structure-restructure.md#divergence-registry) row when built.

**Named 2026-07-30 (multiple-choice, per the standing rule): `AutoRenew`.** **Built same day:** the
property lives on `FinancialPattern`/`FinancialPatternOptions` and its `FinancialPatterns` SQLite
column (`INTEGER NOT NULL DEFAULT 0` — every pre-existing pattern defaults to not-opted-in), with the
`DIVERGENCE(auto-renew)` tag and the [05 registry](05-original-structure-restructure.md#divergence-registry)
row both in place. `WithActiveFrom`/`WithUntil` carry it through unchanged, and — the one non-mechanical
part of this build — `BreakOffFactory`'s successor construction (both `BreakOff` itself and `Renew`'s
relabeling step) now copies it from the predecessor rather than defaulting every successor back to
`false`. This wasn't asked about separately: `Renew` exists *specifically* to extend a pattern that
"keeps going," so if the marker didn't survive its own relabeling step, a pattern would stop qualifying
after exactly one renewal — silently defeating the feature. Tested directly (`BreakOffFactoryTests`,
`PatternRepositoryTests`). 206 domain + 52 scenario tests passing, 0 warnings.

**Still open, not yet decided:** the initial segment length a "keeps going" pattern gets at creation
(mirrors `SegmentYears`, currently caller-supplied to `Renew` itself — does creation need its own
default, or does the scheduled trigger supply one when it eventually calls `Renew`?); the creation-time
UI stub that actually sets `AutoRenew = true`; and the scheduled trigger itself (S7 — still just "not
yet wired into the app," unaffected by this ruling).

### C10 — resolved: an active nudge once over-funded crosses a threshold

**Author's ruling: an active, specifically-surfaced nudge** — not just a quiet number next to the
goal's existing status — once `GoalShortfall.OverfundedAmount` crosses a threshold, with the
"stop contributing" lever (item 22) right there. Sits at the informational tier on Item C's severity
ladder, alongside the cushion-not-whole state.

**Still open, not yet decided — proposed default, not asked:** the threshold itself. A **percentage
of `AmountNeeded` (proposed: 10%)** reads better than a flat dollar figure across wildly different
goal sizes ($50 over on a $200 goal vs. a $50,000 one aren't equally "worth mentioning"), and avoids
nudging over a few cents of rounding drift. Flagged as a reasoned default, not a ruling — worth a
second look once this is actually visible on screen, the same way item D's window-height issue only
surfaced once it was.

## Transfers — the create-flow structure, and what's actually missing

**Found 2026-07-30 outside the Stage 5 audit itself** (a direct question — "is there a plan for
regular account transfers?" — not one of the catalog's own rows), but the same kind of loose end, so
recorded here rather than left to scatter. Checked directly against the code first: recurring
transfers already work end to end today — `Transfer.DatePattern` is a full `RecurrenceRule` (not a
single date), `CreateTransferWindow` already uses the same rule editor as everything else, and the
withdrawal leg's reservation already paces per cycle (`AllocationPlanProposer.ProposeFrontLoaded`'s
multi-occurrence branch, confirmed in Stage 1's own revision). What was missing was the *entry-point
shape* and a few small gaps the question surfaced.

### The create-flow structure — SETTLED 2026-07-30

**One entry point** ("Create Transfer…"), not the separate-top-level-buttons shape bills use.
Deliberately different from bills/goals: *"unlike bills or goals, transfers don't have distinct
easily identifiable profiles... we can just have one button."* It opens a **simple one-time form**
(from, to, amount, date — no schedule complexity shown at all). Two buttons near the top escalate:
one to a **simple-recurring** mode of the same form, one to today's already-built **advanced** form
(`CreateTransferWindow`, unchanged). Implementation-wise this mirrors `CreateFinancialPatternWindow`'s
existing `forcedMandatory`-flag mode switch (one class, multiple modes) — just with an in-form link
between modes instead of separate top-level buttons, since there's no bill/goal-style taxonomy
justifying separate entry points here.

**"Just keep going" applies to recurring transfers too — SETTLED, at parity with bills, not behind
them.** Bills can't fully do this yet either: the stub exists in `CreateFinancialPatternWindow`, but
it does nothing until B12's marker (above) is actually built. Building "keeps going" for transfers
and finishing it for bills are the same piece of unbuilt work.

**No third "stop" answer for transfers — SETTLED, with a stated principle, not just a one-off
answer:** *"We don't need to give much personality to transferring money between accounts. It's a
mystery to us why the money is being transferred, so we don't have any shortcuts to offer the user
beyond making it easy to create a transfer once or on a schedule."* A recurring transfer gets exactly
two answers — **on a date I know** / **it just keeps going** — never the loan-payoff-shaped third
answer bills get, even though the arithmetic (`PayoffEstimator`) would technically transfer over (a
target total moved via recurring transfers is the identical shape to a loan). **This is a standing
principle for future transfer questions, not just this one:** the app doesn't know *why* a transfer
exists, so it doesn't get income-aware, goal-aware, or debt-aware shortcuts — only the two universal
schedule questions every recurring thing needs.

### Mechanical gaps found by reading the code — all three now built (2026-07-30)

| # | Gap | What it needed | Status |
|---|---|---|---|
| 1 | `TransferBreakOffFactory` has no `Renew` | Mirrors its own `BreakOff`: call the leg-level `BreakOffFactory.Renew` on both legs with one shared renewal date, same lockstep pattern already used for `BreakOff` | **Built** — `TransferBreakOffFactory.Renew(TransferRenewalRequest)`, deliberately narrower than `TransferBreakOffRequest` the same way `RenewalRequest` narrows `BreakOffRequest` (no amount/schedule-shape input — a renewal changes neither) |
| 2 | The B12 "auto-renew" marker must be set identically on both of a transfer's legs, never independently | Not a new decision — the same lockstep discipline `TransferBreakOffFactory` already enforces for amount and schedule, applied to the new marker | **Built** — both `BreakOff` and `Renew` now throw if the two legs' `AutoRenew` disagree, same shape as the existing amount-drift checks |
| 3 | No standalone "end this transfer early, no successor" factory | `Transfer.WithUntil` exists but is only used internally as break-off's predecessor half; ordinary bills get this for free from `PatternTruncation.EndOn` serving both jobs. Transfers need the analogous standalone version | **Built** — `TransferTruncation.EndOn`, mirroring `PatternTruncation.EndOn` and reusing `TransferBreakOffFactory`'s own leg-drift validation |

None of these three were design forks — direct, mechanical extensions of patterns already built and
proven for ordinary bills. Tested in `TransferTruncationTests` and `TransferBreakOffFactoryTests`
(`Renew_*` and `*_disagree_on_auto_renew` cases). Still not built: the creation-time UI stub that sets
`AutoRenew`, and the scheduled trigger itself (S7) — both UI/architecture decisions, out of scope for
this pass.

## The UI/wiring backlog — consolidated

**The thing to keep a note of.** Every stage from 1 through 4 built and tested a mechanism, then
deferred its screen — this is the running total of that deferral, gathered into one list instead of
scattered across four planning docs. None of these are design gaps (the data each screen needs is
already settled); they're screens and entry points nobody has drawn yet.

| # | What's missing | What it needs | Data contract status |
|---|---|---|---|
| 1 | **"Change starting on a date"** (break-off, item 4/B14) | An entry point on a bill/paycheck row + a confirm screen (cut date, pre-filled successor plan, carried-over balance labelled "projected" or plain per 4-B) | Fully settled ([16, item 4-D](16-stage3-break-off.md#4-d--preview-and-confirm--data-contract-settled-not-wired-into-the-app--deferred-to-the-ui-phase)) |
| 2 | **"Stop this on a date"** (truncate, item 16/B15) | An entry point + the same schedule-driven end-date input already built for the loan-payoff question | Fully settled |
| 3 | **"Restructure the plan"** (item 8/C7) | An entry point on an earmark-pattern row + a dialog for the cut date and new amount/schedule | Fully settled ([17](17-stage4-allocation-only-changes.md)) |
| 4 | **"Stop contributing"** (item 22/C9, targets `RestructureFactory.StopContributing`) | Likely the *same* dialog as #3, with a "stop entirely" option, rather than a separate screen | Fully settled |
| 5 | **Declining a proposed Allocation Plan** (Stage 2's `ProposeEmpty` consumer) | A "remove this plan" affordance that lands on the empty-plan-plus-jar shape, not a hard delete | Fully settled |
| 6 | **A scheduled trigger for pattern renewal** (S7, `BreakOffFactory.Renew`) | Not a screen — an app-layer background check ("has a year (or `SegmentYears`) passed since this pattern's start or last renewal, and is `AutoRenew` set?") that calls `Renew` automatically. Covers transfers too now: `TransferBreakOffFactory.Renew` is the identical shape, one shared call per transfer instead of two independent ones | Fully settled |
| 7 | **The stale-pattern edit redirect** (`FindCurrentSegment`) | Wiring "Edit" on any pattern to resolve to its current segment first — the rule (silent redirect) is decided, just not called from anywhere | Fully settled |
| 8 | **Surfacing `OverfundedAmount`** (item 24) | An **active, specifically-surfaced nudge** (C10's ruling, not just a quiet number) once it crosses a threshold (proposed default 10% of `AmountNeeded`, unconfirmed), with the "stop contributing" lever (item 22) right there | Fully settled |
| 9 | **Item D's known rendering issues** (Stage 2, the loan-payoff bill form) | The window is confirmed too tall for a laptop screen; several smaller uncertainties are listed in [15](15-stage2-pattern-lifetime.md#item-d--the-form-family--built-ui-unverified-2026-07-29--uncertainties-listed-below) itself | Built, just unverified on screen |
| 10 | **Item 18's confirmation wording** (delete) | Already done, for the record — names the amount being freed. Listed here only so the backlog doesn't look like it skipped it | Done, UI-unverified only |
| 11 | **B12's creation-time "it just keeps going" checkbox** | The stub already exists in `CreateFinancialPatternWindow` but does nothing (planning/18, transfers section) — needs to actually set `AutoRenew = true` on save. Same checkbox, same wiring, for both bills and transfers | Fully settled — the marker exists now |
| 12 | **The transfer create-flow's settled 3-tier structure** | One **"Create Transfer…"** entry point → simple one-time form → two escalation buttons near the top (simple-recurring / today's advanced `CreateTransferWindow`, unchanged) — mirrors `CreateFinancialPatternWindow`'s `forcedMandatory` mode-switch, but with in-form links instead of separate top-level buttons | Fully settled (2026-07-30) |
| 13 | **Entry points for the transfer-specific factories** (`TransferBreakOffFactory.BreakOff`/`Renew`, `TransferTruncation.EndOn`) | Same shape as rows 1/2/6, but on a transfer row instead of a bill/paycheck row — whether that's the identical screen generalized or a transfer-specific variant is a layout call for the UI pass, not a data-contract question | Fully settled |

**Everything above is domain/engine-complete and tested.** None of it is blocked on a design decision
— it's blocked on someone sitting down with the UI (a mockup round, mirroring how the multi-account
phase handled its own UI item 5) and deciding layout, wording, and entry-point placement, then wiring
the calls this whole phase already built.

---

## Status table update

See [13's own status table](13-adjusting-the-plan-charter.md#status) for the live entry.
