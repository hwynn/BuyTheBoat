# 13 — "Adjusting the Plan": phase charter

**Status: ADOPTED — Stages 0–6 DESIGN-COMPLETE 2026-07-30; the phase itself is still OPEN, now inside its UI-implementation stretch.** (1, 2, and 3's domain/engine work is complete and tested; Stage 4 built items 8, 9, 22, and 24's detection, with items 23 and cross-account funding settled/fixed; Stage 5 audited the whole action catalog, closing items 7/15/21, then designed **and built** its three genuine gaps — C1, B12 (named `AutoRenew`), C10's detection — plus the transfer create-flow's own small gaps (`TransferBreakOffFactory.Renew`, the `AutoRenew` leg-lockstep check, `TransferTruncation`); 264 tests green; Stage 6 worked its full 7-state "concerning states" inventory one state at a time, closing item 19, and deferred item 20's shortcut inventory plus the "Change this…" wording to the UI pass by author's choice).

**Reframed 2026-07-31, by the author:** the "one dedicated UI pass" every stage's wiring was deferred to is **not a separate follow-on phase — it is part of this same phase.** It has its own two docs: [20](20-ui-phase-inventory.md) (the raw inventory — existing UI control-by-control, every possible action, one candidate clustering) and [21](21-form-architecture.md) (**the currently active document** — Philosophies 4–7, the three-form system, the mockups, and a form-by-form content inventory worked one form at a time: Account is fully done — consistent/contextual tables and its 4-region layout; Expense has its consistent and contextual content settled, **layout is the next open step**; Earmark and Transfer haven't been started). Nothing in Stages 0–6's own sequence is still being designed; what remains — the UI pass, plus two small unconfirmed loose ends that block nothing (C10's proposed 10%-of-`AmountNeeded` threshold, and F37, a narrow accepted-as-is edge case) — is what the phase's still-OPEN status now refers to. See the [status table](#status) at the bottom for the live, per-stage tracker.* (This charter is still the map, not the answers — each stage's answers live in its own numbered doc, 14+.)*

This is the charter for the phase that follows multiple accounts. It exists because the phase is
too large to hold in one head, one context window, or one sitting: it is ~24 separate design
questions, most of which would be a full session on their own. This document is the **map and the
tracker** — what the phase contains, what order it gets worked in, and how a cold session picks up
where the last one stopped.

**It deliberately answers none of the design questions it lists.** Every "?" below is live.

---

## Naming, so the vocabulary stays straight

The project already uses "phase" at the top level (the multiple-accounts phase, the deallocation
phase). Nesting phases inside phases would be unreadable, so:

| Level | Term | Granularity |
|---|---|---|
| 1 | **Phase** | This whole body of work: "Adjusting the Plan." |
| 2 | **Stage** | One numbered planning doc (14, 15, …), one or two sessions. |
| 3 | **Item** | A lettered decision inside a stage — the same convention [10-multiple-accounts.md](10-multiple-accounts.md) uses (item 1-A, item 2-D, …). |

### Term rulings (author, 2026-07-24)

- **"The jar of a paired transaction"** — or **"the jar of an assumed-paired transaction"** while the
  assumed-pairing paradigm holds — is how deallocation's Step A input is referred to. **Not renamed
  away from "paired":** the word is the author's own, from `DeallocationProof.ods`, and it is used
  *only* in deallocation, where being verbose is fine and being reminded of the math is a feature.
  The `PairedTransaction` type keeps its name; the qualification does the disambiguating work.
  *(Background: "paired" and "has a jar" picked out the same transactions in the original design,
  because only earmarked goals had jars. Automatic funding broke that equivalence — which is where
  [F20](14-stage1-allocation-model.md) lives.)*
- **"Pre-funded" is RESERVED — do not use it for anything else.** The author is holding it for a
  distinct concept to be worked in a later stage: *an earmark pattern that has a non-repeated
  (implicit or manual) earmark created for it on the very first day it exists.* Most likely stage 4
  territory (items 22–24). It was proposed here as a rename for the above and deliberately declined.

**Phase name — "Adjusting the Plan."** Every stage is some form of *the user changed their mind (or
reality did), and the plan has to change with them* — which is [core question
Q4](../../04-project-goals-and-user-questions.md#q4--if-i-buy-x-anyway-how-do-i-readjust-my-goals),
the one the original documentation never resolved. The earlier working name for this phase was
"cascade-tweaking," which undersold it: almost none of this is cascade math, and most of it is
allocation semantics and user-facing adjustment machinery.

---

## Standing rule — design in the class documentation's own terms

**Author, at the phase's opening and re-stated 2026-07-24:** design around concepts from the **class
documentation** rather than abstractions over it, wherever possible, and especially throughout this
phase. The required knowledge was named up front: *"the entirety of the project goals, project
philosophies, class documentation, all of the assumptions, directed graph that links them, the fact
that satisfying enough assumptions keeps our calculations in balance, and what 'state' or process
we're in by satisfying certain assumptions."*

This is not about wording — it is about **where answers come from**. The class model and the
assumption set already encode most of the design, *including the order operations must happen in*.

- Frame a problem in `FinancialPattern` / `EarMarkPattern` / `EarMarkEvent` / `FundJar` /
  `BalanceSnapshot` and in assumption IDs, not in a category invented for the conversation.
- **The dependency order is design guidance, not just validation.** If the documented cascade clears
  X before deciding Y, an implementation that decides Y from X is wrong even when the arithmetic
  balances.
- New abstractions remain allowed (philosophy 3a) — as a **recorded divergence on top of** the model,
  never as the language the design is thought in.
- **Read [03](../../03-assumptions-glossary.md) and [06](../../06-assumption-dependency-graph.md);
  do not grep them.** Grep-only working is exactly how the drift happened.

**Cost, demonstrated:** [F20](14-stage1-allocation-model.md) was diagnosed twice in invented
vocabulary before reading 03 showed the documented cascade already prevents it
(`3.13c.8.a4` → `3.13c.8.a5` → `3.13c.a6`). Full memory entry: `memory/feedback_design_in_class_documentation_terms.md`.

## The two standing constraints this whole phase designs against

Both are stated by the author. They are not open questions — they are the walls.

### Constraint 1 — patterns are linear, gapless, and single-valued

A `FinancialPattern` or `EarMarkPattern` carries **one** `Amount` and **one** `RecurrenceRule`
(one frequency, one interval, one start, one until). There is no curve, no step, no gap, no
per-occurrence override. Confirmed in code: `FinancialPattern.Amount` / `EarMarkPattern.Amount` are
scalars, and `RecurrenceRule` exposes exactly `Frequency` / `Interval` / `ByDay` / `ByMonthDay` /
`Start` / `Until`. **Any change to what a pattern does means its occurrences are regenerated
wholesale.** That is not going to change, so every feature in this phase has to be composed out of:
splitting patterns, adding patterns, one-off manual earmarks, or values computed rather than stored.

This is where [design philosophies](../../design-philosophies.md) **2** (speak the user's language)
and **3(a)** (build features by abstracting over the original tools) do the heavy lifting: the user
should be shown "change my electric bill starting in March," not "your rrule was truncated and a
second pattern was created."

A companion registry of the sanctioned workarounds — one entry per trick, with what it costs — is a
**Stage 0** deliverable, and grows as later stages invent more.

### Constraint 2 — every rrule must terminate

`RecurrenceRule` accepts `Count` only as entry sugar and resolves it to an `Until` at construction;
`Until` is the only bound that exists on a constructed rule (this structurally enforces the
chart-only "must use until, not count" rule, and mirrors `mini_fund_project`'s
`count_to_until_rrule`). So there is **no representable "forever."** This is the mechanical root of
item 10 below — asking a user when their electricity should stop.

---

## The item inventory

Everything the author raised, itemized so nothing gets lost across context resets. The author's own
framing is preserved; the stage column is this document's proposal.

| # | Item | Stage |
|---|---|---|
| 1 | **Redefine free funds** — every expected transaction should have a fund jar that counts against free funds (from talking to potential users). Implies changes we have to trace. | 1 |
| 2 | **Review what "Mandatory" means** — possibly less useful after item 1; if kept, its help to the user must be legible. The author has been confused by it in their own use. | 1 |
| 3 | **The linearity constraint** (Constraint 1 above) — write it down once, as the wall everything else designs against. | 0 |
| 4 | **"Break off" a finance pattern at a date** — raises, a bill's amount or schedule changing. Cut the rrule at the change point, continue as a new pattern, hand the existing jar balance across (implicit earmark), split the earmark pattern to match, and show the user the whole plan to confirm before doing it. Paychecks are the easy case (no jar), but may disturb coinciding earmark patterns → optional prompt. | 3 |
| 5 | **Break off for an identity-only change** — the biller's description/source string changes, so nothing pairs. Needs a contextual "we found this new transaction, is this your electric bill?" offer. **Actuals-dependent → register only.** | 3 (→ [12](12-actual-transactions-deferred-design.md)) |
| 6 | **Which *other* changes deserve the same treatment** — the author's list of use cases is explicitly not comprehensive. | 3 |
| 7 | **The systematic action review** — every action a user can take, what we do implicitly in response, what explicit choice that leaves them. | 0 (skeleton) → 5 (audit) |
| 8 | **Break off an *earmark* alone**, without touching its finance pattern — e.g. start allocating more toward an unchanged one-time goal halfway through. | 4 |
| 9 | **Two earmark patterns for one finance pattern** — a household partner's paycheck starts funding the same bill at a different amount. Currently forbidden. | 4 |
| 10 | **Determinate vs. indeterminate bills** — "how long do you want electricity for?" is not a question. Wants: a classification, simpler per-kind forms, a payoff-date helper for loans (rough — interest and fees), internal auto-renewal for open-ended bills (likely a new boolean), and the full form retained as an "advanced" option. *Better terms than determinate/indeterminate are wanted.* | 2 |
| 11 | **Do we auto-create an earmark pattern when a user makes a bill?** What choices does the user get? | 1 |
| 12 | **Do we auto-create one for a one-time expected transaction** ("buy a new television")? What choices? | 1 |
| 13 | **Speculative expenses must stay cheap** — creating "buy a TV" to see what it does to the forecast, then deleting it, has to be practical. This is how the program answers Q4. | 1 |
| 14 | **Keep the "no paycheck first ⇒ reserve it now" trick** — already built (see below); remember it when designing similar cases. | 1 |
| 15 | **Catalog every place the system makes an implicit earmark** — beyond deallocation days and the break-off hand-off. | 1 |
| 16 | **"I'm cancelling Netflix next month"** — editing the rrule's end date stays valid but is clunky; wants an explicit "end this at a date" action near the break-off action. | 3 |
| 17 | **The user cancelled a bill in the past and never told us** — should be recoverable, most likely as an option on the unpaired-transaction warning. **Actuals-dependent → register only.** | 3 (→ [12](12-actual-transactions-deferred-design.md)) |
| 18 | **Deleting a bill terminates its earmark pattern** and frees the funds — any implications not already covered? | 3 |
| 19 | **What states should concern the user**, and what contextual actions do we offer for each? Shortcuts to things possible elsewhere but fiddly (up to and including "open this pattern's editor with a suggested change"). The author wants *more* suggestions here than the ones listed. Control stays with the user; the effect must be legible. | 6 |
| 20 | **What's clunky when the user is *not* worried** and just wants to edit — more "Create Bill…"-shaped shortcuts, without turning a screen into button soup. | 6 |
| 21 | **Where does a user make an explicit earmark?** Allowed on any day, including future days with no balance snapshot yet ("set half this paycheck aside for the credit card"; "take some grocery money for an Xbox"). | 5 |
| 22 | **A goal met early** — a big manual earmark fills a 3-year vacation jar ahead of schedule, and contributions keep piling in past what's needed. Notify? Stop contributing early *while keeping the money allocated* until the transaction happens? What about a loan, which is a repeated expected transaction? | 4 |
| 23 | **Allocating toward something that doesn't exist yet** — a $7,000 bonus set aside for a healthcare plan that hasn't started. No jar without a finance pattern; no finance pattern without scheduled occurrences. Is this feasible at all, or is "move it to another account" the honest answer? | 4 |
| 24 | **Deferring allocations** — the same healthcare case, one step later: a naive auto-generated earmark pattern would run $7,000 ahead forever. The system should notice a jar that is (or starts) over-funded and offer to defer contributions until they're needed. Useful well beyond this one case. | 4 |

**Also folded in, already parked elsewhere:**

- The three open sub-questions on "a jar for every upcoming expected transaction," parked in
  [10-multiple-accounts.md § Parked](10-multiple-accounts.md#parked-for-the-cascade-tweaking-phase)
  — outflows only? does a jar reserve real money for non-mandatory expenses, or is it a *visible
  allocation* while mandatory/priority still governs reserving? is an auto-created bill's accrual a
  real editable earmark pattern or a computed jar? → **Stage 1**, where they are the same question
  as items 1/2/11.
- **Cross-account funding interactions** (also parked in 10): a manual earmark that pre-allocated a
  specific account's anticipated income when the bill moves accounts, and the standing implicit
  assumption that a bill's funding paychecks land in the same account. → **Stage 4**.
- **The "thin" warning state** — mockup E showed it, no threshold was ever defined, and 10 deferred
  it here on the grounds that defining "thin" is an allocation question, not a rendering one. →
  definition in **Stage 1**, presentation in **Stage 6**.

---

## What already exists that these items build on

Confirmed against the code and docs, so no stage re-derives it:

- **Item 14 is already built.** It is the `B` branch of `BillAccrualAt` in
  `TransactionLogBookFactory.cs`, tagged `DIVERGENCE(positive-implicit)`: if no income lands between
  tomorrow and a bill's next due date, the whole amount is reserved immediately; otherwise the jar
  ramps linearly through the cycle (`A`). Registry entry in
  [05 § Divergence registry](05-original-structure-restructure.md#divergence-registry).
- **Jars without an earmark pattern already exist.** `GetAutomaticallyEarmarkedBills` gives every
  mandatory pattern with no `EarMarkPattern` an auto-reserving jar with a null milestone. So item 1
  is an *extension* of an existing divergence, not a brand-new one.
- **Manual (explicit) earmarks are built** — Add / Withdraw / Move, with the rulings in
  [09-manual-earmarks.md](09-manual-earmarks.md) (do not re-litigate them; items 8/22/23/24 press on
  ruling 1 in particular, which ties a jar's lifetime to its earmark pattern's span).
- **Two precedents for "one user action, several linked patterns"** — `OneTimeGoalFactory` (goal +
  savings plan) and `TransferFactory` (a withdrawal + a deposit + a `Transfer` record). Item 4's break-off is the
  third of that family, and should be designed knowing it.
- **The standing UI principles** distilled in
  [11 § C](11-ui-design-and-decisions.md#standing-ui-principles-distilled-from-the-above) — meaning
  is never colour-only; prefer a plain word or number; a surfaced problem comes with a lever;
  pre-fill a form as far as it honestly goes, then stop at the confirm, scoped no wider than the
  problem. Items 19/20 are governed by these.

## Where this phase puts pressure on the original assumptions

The author's request was explicit that this work be done knowing the assumption set and its
dependency graph. This is the map of *which* assumptions each stage is likely to bend or break —
useful because [06's dependency graph](../../06-assumption-dependency-graph.md) tells you what else
moves when one does. **Listing them is not a decision to break them**; philosophy 3(b) requires a
planning pass first, which is what the stages are.

| Assumption | What it says | Which item presses on it |
|---|---|---|
| `3.13.5.a2` | A fund jar can only exist on days inside its earmark pattern's rrule | 1, 11, 12 (already bent by automatically funded expense jar) |
| `3.13.8.a1` | An earmark can't exist on a page with no earmark pattern for it | 1, 23 |
| `3.11.1.a1` | Earmark patterns have unique finance_ids — one per goal | **9** (breaks it head-on) |
| `3.11.2.a2` | An earmark pattern can't extend beyond its finance pattern's rrule | 4, 8, 23 |
| `8.1.a1`, `3.13.8.1.a3` | An earmark's finance_id is its expected transaction's | 23 |
| `1.2.3.5.a1`, `3.5.a1` | The `current_free_amount` formula | **1** (this is Q1's headline number) |
| `10.4.a2`, `10.4.a3` | Milestone rules for a jar tied to a repeated expected transaction | 1, 22 |
| `3.13.7.a2` | An expected transaction can't exist on a day its pattern doesn't specify | 4, 16 |
| `1.2.3.10.a3` | Two pages sharing a finance_id must have the *exact same* rrule | 4 (once multi-page books are real) |
| `4.2.a1` + source uniqueness | `source` cannot be None; no two patterns share one | 5 |
| chart-only | An rrule must use `until`, not `count` | 10 |
| ODS | "We will **never implicitly add** to a fund jar's expected amount" | 1 (already diverged: `positive-implicit`) |

Cascade steps most affected (from [06 § Process regions](../../06-assumption-dependency-graph.md#process-regions--the-cascade-steps)):
*new events created / old removed*, *all earmarks created on this page*, *if this is a deallocation
day the implicit earmarks are made*, and the final *consciously-applied calculations* pass.

**Resolutions so far (Stage 1 + Stage 2 settled items, audited 2026-07-29).** *Listing pressure was
never a decision to break — here is where each has actually landed. Per-item grounding in
[15 § Grounding audit](15-stage2-pattern-lifetime.md#grounding-audit-2026-07-29).*

- `3.13.5.a2`, `3.13.8.a1` — **preserved, re-anchored.** Stage 1's Allocation Plan model gives every
  outflow a real `EarMarkPattern`, and **`ActiveFrom`** lets that pattern's *active span*
  `[ActiveStart, Until]` reach back before its first occurrence, so both now read "within the earmark
  pattern's **active span**." The jar always sits inside a real pattern — *not* bent for items 1/11/12
  after all. Item 23 (allocate toward the not-yet-scheduled) still presses.
- `3.11.2.a2` — **preserved literally, both directions**, against the active span
  (`EarMarkPattern.Create`, `DIVERGENCE(active-from)`). The save-in-advance face of items 4/8/23 is
  resolved by extending the finance span first, not by relaxing the check. Break-off (4, Stage 3) and
  a second earmark pattern (8/9, Stage 4) still press.
- `3.13.7.a2` / `3.13.7.a1` — **explicitly preserved.** `ActiveFrom` widens the active span but **not**
  the occurrence range: no expected transaction is generated in the `[ActiveFrom, Start)` lead-in.
  Items 4/16 (Stage 3) still press.
- `10.4.a2` — **discarded** (Stage 1 revision): a bill jar gets a real `3.13.5.4.a1` milestone. Item C's
  payoff helper only sets a bill's **end date**, touching no milestone. Item 22 (Stage 4) still presses.
  **Reconsideration flagged 2026-08-02** — the discard may have swapped one bad shape (always the full
  amount) for a different bad one (climbs forever, never resets across a bill's own payments) rather
  than the ramping fix its own cited precedent suggests. See
  [planning/14's fuller note](14-stage1-allocation-model.md#assumption-reconciliation-the-point-of-designing-in-the-models-own-terms).
  Queued for review after the current UI-implementation pass — not resolved, not blocking it.
- chart-only "`until`, not `count`" — **honored, not pressed.** Item C *produces* an `Until` via the
  `Count → Until` resolution (W3); the deferred "ongoing" answer is where this rule actually bites.
- `1.2.3.10.a3` / `1.2.3c.11.a3` — **extended (our own version), deferred** with page-jumping, because
  `ActiveFrom` lives on `RecurrenceRule` and joins the "same rrule across pages" match.
- `1.2.3.5.a1`/`3.5.a1`, `3.11.1.a1`, `8.1.a1`/`3.13.8.1.a3`, `4.2.a1`, ODS positive-implicit —
  **untouched by Stage 2**; their pressure sits in Stage 1 (settled) or Stages 3–5.

---

## The stages, in order

The ordering rule: **settle what a jar is and what free funds mean before designing anything that
manipulates them.** Two-thirds of the items are levers over the allocation model; building a lever
before the thing it moves is defined guarantees a redo.

### Stage 0 — Charter, constraints, and the action-catalog skeleton  ·  *small*  ·  **DONE 2026-07-23**
**Items 3, 7 (skeleton).** This document, plus two artifacts every later stage writes into:

- **[13a — Linearity workaround registry](13a-linearity-workaround-registry.md)** — Constraint 1's
  sanctioned tricks, W1–W9, each with how it evades the constraint, **what it costs**, and what it
  still can't do. Seven are already in use; two are proposed by stages 2 and 3.
- **[13b — User action catalog](13b-user-action-catalog.md)** — every action a user can take (read
  off the real UI surface, plus the actions stages 2–4 propose), as **rows only**, with the
  analysis columns blank. Plus a second table, **S1–S7**, for the actions the *system* takes that no
  user triggers — which is where item 15 gets closed out.

Stages fill cells in as they land; Stage 5 audits, at which point every remaining blank is either a
gap to design or an explicit deferral. This is what makes item 7 possible without one heroic sitting.

### Stage 1 — The allocation model: a jar for everything  ·  *large, split into two sittings*
**Items 1, 2, 11, 12, 13, 14, 15** + planning/10's three parked sub-questions + the "thin"
definition.
- **1a — semantics:** what gets a jar (outflows only? the two patterns behind a transfer? non-mandatory? paychecks?),
  what "free funds" means afterward, whether a jar reserves real money or is a visible allocation,
  and what survives of **Mandatory**.
- **1b — machinery and defaults:** auto-created earmark patterns (real editable pattern vs. computed
  jar), what the user is asked when creating a bill or a one-time expense, keeping speculative
  create-then-delete cheap, and the **implicit-earmark registry** (every place the system earmarks
  on the user's behalf, item 15 — a written list, not prose).

> **Recommendation: implement Stage 1 before designing Stage 3 onward.** It changes the headline
> number and the meaning of every jar, 147 tests pin the current behaviour, and every later stage
> reasons about jars. Designing 3–6 against numbers we have actually seen beats designing them
> against numbers we imagine. Stages 2 and 6 are the only ones that could safely be designed
> before that implementation lands.

### Stage 2 — Pattern lifetime and the form family  ·  *medium*
**Item 10.** Deliberately *before* the break-off stage: "where does this pattern end" has to be
answered before "how do we cut one in half," and an open-ended bill directly challenges the
jar-lifetime ruling Stage 1 settles. Covers the classification and its user-facing names, the
auto-renewal mechanism for open-ended bills, the loan payoff-date helper and how its roughness is
communicated, and how the simple forms relate to the existing advanced one.

### Stage 3 — Changing a pattern at a point in time  ·  *large*
**Items 4, 6, 16, 18** (+ 5 and 17 recorded into [12](12-actual-transactions-deferred-design.md),
not designed). The break-off mechanism end to end: identity across the cut, the jar hand-off, the
earmark pattern's parallel split, the paycheck case, the preview-and-confirm flow, ending a pattern
early, deleting one outright, and **the taxonomy** — which kinds of change need a split, which are a
plain edit, and which need something else again (item 6). A better user-facing name than "break off"
gets chosen here.

### Stage 4 — Changing an allocation without changing the bill  ·  *large*
**Items 8, 9, 22, 23, 24** + planning/10's cross-account funding interactions. The earmark-side
counterpart to Stage 3, and the closest thing to a direct answer to Q4. Items 22/23/24 are one
family — *the jar's fill schedule doesn't match the naive linear plan* — and are likely to share a
mechanism. Item 9 is the one assumption break big enough to deserve its own decision record.

### Stage 5 — The action catalog, audited  ·  *medium*
**Items 7, 21.** Fill in and close out the Stage 0 skeleton against everything stages 1–4 decided.
Its value is as a **completeness check**: every blank cell is either a gap to design now or an
explicit deferral. Item 21 (where explicit earmarks get made, on which days, and whether that's
discoverable) is answered here because it is an entry-point question, not a mechanism question.

### Stage 6 — Warnings, levers, and the shortcut surface  ·  *medium*
**Items 19, 20** + the "thin" presentation. Last on purpose: it is the user-facing consumer of
everything above.

**How much UI change each tab will tolerate (author, 2026-07-24):** the **Forecast tab is settled** —
the author is happy with it, so changes there need a strong reason. **Every other tab is open**, and
is expected to get a UI rework once this phase is finished anyway, so adding a control there (for
instance stage 1's "Set Up Savings Plan…" button) is cheap. Useful when weighing charter item 20's
button-soup warning: the warning bites hardest on the Forecast tab. The catalog of states worth flagging, the contextual lever for each, and the
manual-editing shortcuts — governed by the standing UI principles, and by a rule (to be decided
here) for keeping the number of buttons on a screen sane.

### Then: implementation of stages 2–6
Design-first for these, because they interlock — a single "change this thing starting on a date"
flow plausibly serves stages 2, 3, 4 and 6, and that only becomes visible once all four are on
paper.

---

## How a session picks this up cold

Each stage gets its own numbered doc, following [10-multiple-accounts.md](10-multiple-accounts.md)'s
conventions exactly, because they work: a **status line** at the top, **items lettered A, B, C…**,
each marked `SETTLED <date>` as it lands, superseded wording struck and labelled rather than
deleted, and a **Parked** section at the bottom for anything punted.

Every stage doc opens with a **Reading list** naming the 3–5 documents needed to work it — never
"read everything." The baseline for any stage is:
[GUIDE.md](../../GUIDE.md) tiers 1–2 → this charter → the stage's own doc → its named prerequisites.

**Standing instructions for this phase:**
- Anything actuals-dependent gets appended to
  [12-actual-transactions-deferred-design.md](12-actual-transactions-deferred-design.md) and is
  **not** designed here (items 5 and 17 especially).
- Any new departure from the original design gets a `DIVERGENCE(<topic>)` tag at the code site and a
  row in [05's registry](05-original-structure-restructure.md#divergence-registry).
- Any new workaround for Constraint 1 gets an entry in
  [13a](13a-linearity-workaround-registry.md), with its cost stated.
- Every stage fills the [13b](13b-user-action-catalog.md) rows it decided — **only** those; a
  guessed cell defeats the audit.
- Every stage updates this document's status table before it ends.

## Status

| Stage | State |
|---|---|
| 0 — Charter, constraints, catalog skeleton | **DONE 2026-07-23** — charter + [13a](13a-linearity-workaround-registry.md) (W1–W9) + [13b](13b-user-action-catalog.md) (43 rows, cells blank) |
| 1 — The allocation model | **DESIGN COMPLETE 2026-07-23** — [14](14-stage1-allocation-model.md), items A–D all settled. Every outflow reserves along the standing fill rule; income never does; transfers reserve per-account but not household-wide; `Mandatory` becomes skippability and protects a jar from deallocation; the middle warning state means the safety cushion is not whole; no savings plan is ever created automatically, and deleting a goal removes its plan after confirming. **IMPLEMENTED 2026-07-24** — 155 tests green (was 147), 0 warnings. One defect found and deliberately left for the author: **F20** in [14](14-stage1-allocation-model.md). **REVISED & RE-IMPLEMENTED 2026-07-24** — items A and D reopened under philosophy 3(b): every outflow funds via a pre-filled, removable **Allocation Plan** (a real `EarMarkPattern`), the computed A/B ramp is **retired**, `10.4.a2` is discarded, and **F20 dissolves** (a jar always has a plan behind it, so "has a jar" = "has an earmark pattern" again). Rebuilt end to end — proposer (`AllocationPlanProposer`), ramp deletion, F21 shortfall (occurrence-scaled need + isolated earmarks), App creation wiring (bills and transfers propose and persist a plan), and a transfer-delete cascade fix — **163 tests green** (121 domain + 42 scenario), 0 warnings; F20's regression test restored to `50m`. Only UI-polish refinements remain (in-window plan preview, unify the goal flow onto the proposer, the stale "Set Up Savings Plan" button). See the [14 Revision](14-stage1-allocation-model.md#revision-2026-07-24--allocation-plans-replace-the-ramp). |
| 2 — Pattern lifetime & form family | **Design settled 2026-07-24** ([15](15-stage2-pattern-lifetime.md)) — one plain question, *"when does this stop?"*, three answers (keeps going / ends on a known date / ends when paid off), no category names shown, applies to income too; the payoff helper is owed ÷ payment stated as a floor; the question lives in the existing bill form. **Revised 2026-07-28** — the *keeps-going* (ongoing) branch is **deferred & reopened**: horizon-extension (W9) dropped in favour of periodic break-off. **Resolved & built 2026-07-29** — `BreakOffFactory.Renew` (in [16](16-stage3-break-off.md), since it landed once Stage 3 existed): a renewed segment is kept (truncated, visible, same as a manual break-off); the renewal itself is silent but leaves a plain "(renewed ...)" trace in `Description` (not a new field); fixed cadence, on a caller-supplied number of years (not hardcoded — a rare, multi-year cadence per the author); `Source` is reused verbatim (settling F23's eventual resolution for renewal specifically — the predecessor becomes exempt from uniqueness once superseded). Full rationale in [15's Ongoing/renewal section](15-stage2-pattern-lifetime.md#ongoing--renewal-resolved-and-built-author-2026-07-29). **Partly built:** the early-allocation **`ActiveFrom`** lead-in — Steps 1–2 done 2026-07-28 (field, helpers, persistence, migration, and the restored earmark-before-goal check; Step 3's empty/declined-plan factory `ProposeEmpty` done 2026-07-29, its decline-flow UI deferred) — and the **item C payoff helper** (`PayoffEstimator`, domain, done 2026-07-29), and the **item D** bill-form "when does this stop?" question (done 2026-07-29, **UI unverified** — uncertainty list in [15](15-stage2-pattern-lifetime.md)). **Remaining:** deferred UI (item D rendering + window height, Step 3's decline flow, Stage 1 polish), wiring a scheduled trigger for renewal, and deciding what the edit flow does with a stale (superseded) pattern — `BreakOffFactory.FindCurrentSegment` (4 tests, same day) makes detection free via `Source`, but the UI response (redirect / warn / hide) isn't chosen yet. Suite 224 green (179 domain + 45 scenario). **Grounding audit done 2026-07-29** — per-item in [15](15-stage2-pattern-lifetime.md#grounding-audit-2026-07-29); pressure-map resolutions above. |
| 3 — Changing a pattern at a point | **DOMAIN BUILT 2026-07-29** — [16](16-stage3-break-off.md). Design settled same day: the taxonomy (item 6 — "does the user want this true only from a date forward" is the real test), break-off (item 4 — no link record; jar hand-off seeds `StartingAllocation`; the split truncates the predecessor and freshly proposes the successor's plan; paycheck case needs only the cut; user-facing name **"Change starting on a date"**), truncating (item 16), and deleting (item 18) all settled, plus findings **F24** (an edit ≠ an automatic break-off) and **F25** (the cut date may be past or future). **Built the same day:** `BreakOffFactory` (item 4, 13 tests), `PatternTruncation` (item 16, the shared predecessor/truncate mechanism, 6 tests), `TransferBreakOffFactory` (F26 — a transfer is three linked things, not one; resolved and built the same day once confirmed no other stage owned it, 8 tests), `BreakOffFactory.Renew` (Stage 2's deferred ongoing/renewal mechanism, resolved with the author question-by-question and built the same day, 7 tests), and `BreakOffFactory.FindCurrentSegment` (a same-day addendum — the author caught that a rare renewal cadence leaves real time where editing a stale predecessor silently misses the live segment; detection is free via `Source` reuse, 4 tests) in the domain — 40 new tests, 179 domain / 45 scenario green, 0 warnings, no divergence introduced; item 18's confirmation dialog now names the amount being freed (UI, unverified). **Still open:** item 4-D's confirm screen, item 16/18's UI entry points, a scheduled trigger for renewal, and the edit flow's stale-pattern redirect (**settled: silent — "Edit" always opens the current segment via `FindCurrentSegment`**) are all **not wired into the app** — deferred to the UI phase; **F23** (`Source` uniqueness) parked on actuals. Items 5/17 registered in [12](12-actual-transactions-deferred-design.md). |
| 4 — Changing an allocation alone | **OPEN 2026-07-30** — [17](17-stage4-allocation-only-changes.md). **F27 SETTLED**: items 8, 9, and 22 all reduce to one question — can more than one `EarMarkPattern` share a `finance_id`, which `3.11.1.a1` forbids today — and the author's ruling relaxes it (jar aggregates every pattern's events). **Item 8 — "Restructure the plan," BUILT 2026-07-30**: `RestructureFactory` (domain, 7 tests) — no jar hand-off needed at all (**F28** — same `finance_id` means the jar never restarts, unlike break-off), user specifies the new rate directly (no re-proposal), user-facing name "Change my savings plan starting on a date." Building it surfaced **F34** (the engine's initial-jar-seed and `CalculateGoalShortfalls` both assumed 0-or-1 `EarMarkPattern` per finance_id, silently overwriting/duplicating instead of summing — fixed, 2 regression tests) and **F35** (the schema migration needed to allow multiple plans per finance_id broke `ManualEarmarks`' declared FK reference — repointed at `FinancialPatterns`, the more correct target anyway; also surfaced that FK enforcement is actually ON by default in this setup, not off as this codebase assumed elsewhere). UI entry point not wired in — domain/persistence/engine only, same deferral as every other stage's mechanism so far. **Item 9 — BUILT 2026-07-30**: the creation path needed no UI change at all — checked `MainWindow.xaml.cs` directly and confirmed `OnAddEarMarkPatternClick` passes every goal unfiltered (the only block is the separate "materialize" button, a different entry point); combined with item 8's schema fix, a second concurrent plan already saved correctly. The one missing piece, **F30** (same-day occurrences from two patterns merging into one event, summing amounts, so `3.13.8.1.a2` holds literally) is now built (`MergeOrAppendRepeatedEarmark`, mirroring the existing isolated-earmark merge) and tested. **F37** — noted, not fixed: the composite key assumes two plans never share an *identical* start date; a genuine same-day collision would silently overwrite. Narrow and unlikely in practice (concurrent funders set up at different real moments); a true surrogate key would close it if ever needed, not built without evidence it is. **Item 22 — BUILT 2026-07-30**: `RestructureFactory.StopContributing`, a thin wrapper over item 8's mechanism (F31, the same relationship `BreakOffFactory.Renew` has to `BreakOff`), needing one small generalization to `Restructure`'s own validation (**F36** — checked against the successor's *active-span* start, not its literal `Start`, so its one occurrence can sit at the goal's due date while its active span still begins on the cut date). Confirmed directly (a full forecast-level test, not just reasoning) that it works the same for a repeating goal (a loan) as a one-time one — the jar stays alive and keeps releasing on schedule with no new inflow. **Item 24's detection is BUILT 2026-07-30**: `GoalShortfall.OverfundedAmount`, the mirror of `ShortfallAmount` (no new engine computation, reads the same two existing fields) — correct for a one-time goal, accepted as gross-only/incomplete for a repeating one (F32). Its own system-triggered entry point and the notify/nudge UI are both Stage 6's, per established precedent — this only exposes the number. **Item 23 SETTLED** — author's ruling: create the real goal now with a best-guess date (reuses `ActiveFrom`, no new mechanism); correcting the date later is a plain edit (F24), not a "Restructure." **Cross-account funding: F33 found and FIXED** — `AutoCreateAllocationPlan`'s income scan was household-wide; fixed via `FinancialPatternRepository.GetByAccountExcludingTransferPatterns(accountId)`. **Suite: 245 tests green (was 224 before this stage's work), 0 warnings.** Items 8, 9, 22, 24's detection, and F33 are built; item 24's own entry-point/nudge and UI wiring for everything are what's left. |
| 5 — Action catalog audit | **DONE 2026-07-30** — [18](18-stage5-action-catalog-audit.md). Every row in [13b](13b-user-action-catalog.md) closed out: tables A/E/F marked out-of-phase (pre-existing, untouched by this phase); table B filled B5/B6/B7/B9 (F24's plain-edit taxonomy, generalizing to priority/description) and B14/B15 (Stage 3's break-off/truncate, domain-complete); table C filled items 8/9/22/23/24's rows, with C1 (proposer unification) and C10 (item 24's system trigger) left as **honest, un-audited-away gaps**, not hidden. **Item 21 closed** (table D — two entry points already exist for manual earmarks: the day pane and the Allocations grid). **Item 15 closed** (every S1–S7 row now says what the user sees; S1's stale reference to the retired W6 ramp corrected). **Item 7 is as complete as it can be pending UI.** Deliverable: a consolidated 10-item UI/wiring backlog in [18](18-stage5-action-catalog-audit.md#the-uiwiring-backlog--consolidated) — everything in it is domain/engine-complete and tested; none of it is blocked on a design decision, only on a UI pass. **Update, same day: C1, B12, and C10 — the three genuine gaps — designed** (worked one at a time with the author): C1 unifies onto `AllocationPlanProposer` but keeps today's even-spread fallback for a one-time goal with no clean income (front-loading stays wrong for a long-dated discretionary goal); B12 adds a small new "auto-renew" marker (a real but narrow divergence) to gate silent renewal, since that was already ruled automatic and something has to decide which patterns qualify; C10 becomes an active, threshold-gated nudge (proposed default: 10% of `AmountNeeded`, not yet confirmed) rather than a passive number. **Second update, same day — all three built:** **C1 built** — `AllocationPlanProposer.ProposeSpreadEvenly` is now the single-occurrence/no-income default, with an explicit `spreadEvenlyWithNoIncome` opt-out threaded through `BreakOffRequest` so a transfer's withdrawal (called with no income list) keeps reserving immediately in full instead of silently inheriting the new spread. **B12 named `AutoRenew`** (multiple-choice, per the standing naming rule) **and built**: lives on `FinancialPattern`/`FinancialPatternOptions` and its SQLite column (`DIVERGENCE(auto-renew)` tagged, [05 registry](05-original-structure-restructure.md#divergence-registry) row added), carried through `WithActiveFrom`/`WithUntil`, and copied onto every `BreakOffFactory` successor (both `BreakOff` and `Renew`'s relabeling step) — without that last part a "keeps going" pattern would stop qualifying after exactly one renewal. **The three transfer-specific gaps [18](18-stage5-action-catalog-audit.md#mechanical-gaps-found-by-reading-the-code--all-three-now-built-2026-07-30) surfaced by the same-day transfer question are also built**: `TransferBreakOffFactory.Renew`, a leg-lockstep check that both legs agree on `AutoRenew` (same shape as the existing amount-drift checks, added to both `BreakOff` and `Renew`), and `TransferTruncation.EndOn` (the standalone "end this transfer early" factory, mirroring `PatternTruncation.EndOn`). C10's threshold itself remains an unconfirmed proposed default; its nudge UI, B12's creation-time UI stub, and the scheduled trigger (S7) are all still open — UI/architecture decisions out of scope for this pass. **Suite: 264 tests green (was 245 before this round), 0 warnings.** |
| 6 — Warnings, levers, shortcuts | **OPEN 2026-07-30** — [19](19-stage6-warnings-levers-shortcuts.md). Two policy questions settled: **item 20**'s button-sanity rule (consolidate related actions behind one "Change this…" entry point with a follow-up question, rather than a numeric cap — author expressed no preference between the options offered, so this is the recommendation, adopted by default) and the **cushion-not-whole wording** ("Dipped into your safety cushion," symbol ◑, distinct from "short"'s ⚠). **Update, same day: item 19's whole 7-state "concerning states" inventory is now resolved**, worked one state at a time with the author: cushion-not-whole (1) and both stale-pacing states (3/4, moved-accounts and paycheck-break-off) and the tentative-guessed-date state (6) all get **no lever and no new detection** — the author's consistent call is to trust the user to notice and reach for the tools already built (`RestructureFactory`, plain edits), with states 3/4's real fix explicitly deferred to a future actuals-based mismatch warning ([12](12-actual-transactions-deferred-design.md#forecast-tab-ui)); goal-overfunded (2) already has its lever from C10 (`RestructureFactory.StopContributing`); ending-a-pattern (5) gets the freed amount named, reusing item 18's own already-built wording — which surfaced a new **standing UI principle: never frame a jar release as "getting money back,"** since it was always the user's ([11](11-ui-design-and-decisions.md#standing-ui-principles-distilled-from-the-above)); F12's dormancy family (7) is split — unskippable-by-default and no-cushion-set get passive, in-context treatment (no new mechanism, just better copy and reachability on surfaces that already exist, plus an active shortcut riding on an *already-firing* warning where one exists — a second new standing principle), while the materialize-button and its absence from create forms are deferred whole-cloth to a future dedicated UI stage. **Final update, same day: item 20's shortcut inventory and the "Change this…" flow's exact wording are ALSO deferred to that same future UI stage, by author's choice** — nothing left in Stage 6 is being designed further in this pass. **Stage 6 is therefore DESIGN-COMPLETE 2026-07-30**, closing out this phase's stage sequence (0–6). |
