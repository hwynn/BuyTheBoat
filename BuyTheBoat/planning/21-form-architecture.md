# 21 — Form architecture: the three-form system

**Status: DRAFTED 2026-07-30, IN PROGRESS — not a finished spec.** Elaborates
[design-philosophies.md](../design-philosophies.md)'s Philosophies 4–7, added the same day at the
start of the UI-implementation pass that follows the ["Adjusting the Plan"
phase](13-adjusting-the-plan-charter.md) ([20](20-ui-phase-inventory.md) is that pass's raw
inventory; this document is the shape the forms in that inventory should take). Author-authored
draft, transcribed and organized here — genuinely unresolved points are marked **OPEN**, not quietly
answered.

**Where each form actually stands, 2026-08-02 (read this before anything else in the document):**
- **Account** — fully done: content, layout, everything (§ Account).
- **Expense** — content (Steps 1–2) and layout (top/bottom + the recurrence split) settled; a
  genuine **Advanced mode** is now designed (content only, its switch control not yet placed); the
  **save controls** (Save / Save and Plan) and **four plan-health states** are settled. Not done:
  Step 3's exact geometry for the plain fields region, and break-off/restructure UI.
- **Earmark** — the biggest structural finding of the whole document: "Saving toward" doesn't
  survive as a field, the instance-picker becomes a goal-picker instead, and the form has two real
  modes (Savings plan / One-off adjustment) rather than Expense's single-mode-with-toggles shape.
  Content (Steps 1–2) and the top/bottom layout are settled. **Not done, and the actual next step:**
  designing the informational content for the **four plan-health use cases** (will-miss / already-
  missing / overfunded / will-be-overfunded) — per the standing principle in Step 2, informational
  content comes before any shortcut. Also not done: the layout for everything between top and
  bottom, the instance-picker popup's real design, and break-off/restructure UI.
- **Transfer** — not started at all.
- Two topics identified but explicitly *not* designed anywhere in this document yet: **break-off/
  restructure UI** (both forms) and the **instance-picker popup** (both forms need it; Earmark's use
  is now more demanding than a plain search box — see its Step 1).

**Naming note — SETTLED 2026-07-30:** the generic term covering both a bill and a paycheck (domain
name `FinancialPattern`) is **"Expense."** The author's own words: *"It's dumb, but we can consider a
paycheck to be a positive expense for now. It's not that important."* Deliberately imperfect,
adopted anyway — not worth more discussion than that.

---

## The three forms (Philosophy 7)

One form per core thing, each on its own always-selectable tab (Philosophy 5) — **separate from that
thing's own list/management tab, SETTLED 2026-07-30** (see "The tab bar" below) — each doing double
duty for creating a new instance and editing an existing one:

1. **Expense** — a `FinancialPattern`: a bill or a paycheck (a paycheck counts as a "positive
   expense" — imperfect, accepted anyway).
2. **Account** — an `Account`.
3. **Earmark** — an `EarMarkPattern`.

**Transfers get their own, separate, dedicated form/tab — not one of the three above.** A transfer is
technically a linked pair (a `Transfer` record plus its withdrawal/deposit `FinancialPattern` legs),
special enough under the hood that the author ruled it doesn't belong inside the **Expense** form's
code, even though the two forms will look and behave similarly in many ways. Not much code will be
shared between them.

---

## The tab bar this adds up to

**A form's tab is always separate from its list/management tab — SETTLED 2026-07-30.** Combined with
the three forms above, Transfers' own form, and today's existing tabs
([20](20-ui-phase-inventory.md)'s List 1 has the current, 5-tab state), the proposed top-level bar
becomes:

| Tab | Kind | Status |
|---|---|---|
| Forecast | (unchanged) | existing |
| Accounts | list/management | existing (today's Accounts tab) |
| **Account** | form (create/edit) | **new** |
| Expenses *(name TBD — today's "Bills & Paychecks")* | list/management | existing, may want renaming to match "Expense" |
| **Expense** | form (create/edit) | **new** |
| Earmarks *(name TBD — today's "Allocations (Earmark Patterns)")* | list/management | existing, may want renaming to match "Earmark" |
| **Earmark** | form (create/edit) | **new** |
| Transfers | list/management | existing (today's Transfers tab) |
| **Transfer** | form (create/edit, the settled 3-tier structure) | **new** |

**Nine tabs — SETTLED 2026-07-30: sit with it for now, revisit once something is actually on
screen.** Consolidation (a candidate floated: merge the four list/management tabs into one hub,
keep the four forms standing per Philosophy 5) is deliberately deferred, not rejected — worth
another look once the bar exists to actually look at, not before.

Two things still worth flagging:

- **Four near-identical name pairs sit side by side** (Account/Accounts, Expense/Expenses,
  Earmark/Earmarks, Transfer/Transfers) — singular vs. plural is a thin visual distinction for a tab
  bar someone is scanning quickly. Worth a real look once this is actually on screen — different
  wording, icons, or grouping could all address it, no opinion offered here.
- ~~Manual earmarks are not addressed by any of this~~ — **RESOLVED 2026-08-02, see the Earmark
  content inventory below.** They fold into the unified Earmark form as a second mode, not a
  separate window: a characterization toggle (**Savings plan** / **One-off adjustment**) decides
  whether the form is editing the `EarMarkPattern` itself or a single isolated `EarMarkEvent`
  (`ManualEarmark`) that coexists alongside it. `ManualEarmarkWindow` is retired once this is built.

---

## Form layout, top to bottom

Three regions, in order of importance (most important sits highest):

### 1. The instance-information-block (internal name only)

The topmost region. Controls and displays **which instance of the form's type the user is currently
editing** — blank/new, an existing one loaded and untouched, or an existing one with unsaved edits.

- **Selecting an existing instance** needs more than a plain dropdown — e.g. a dropdown of every
  isolated earmark would be far too long. Some intermediate control (a searchable popup, most
  likely) is needed instead.
- **Break-off mode** (the "Change starting on a date" mechanism, [16](16-stage3-break-off.md)) is
  only offered here when an existing, **recurring** instance is currently selected — never for a
  blank form, never for a non-recurring one.
- **A "modified since last saved" indicator** is required. Forms deliberately do **not** autosave
  across tab switches (see "Why no autosave," below) — a user might open the bill form, hop to the
  Forecast tab to check something, and come back — so the block must show whether there are unsaved
  edits sitting there.
- **A "discard unsaved edits" control** must exist, and its name needs care so it reads clearly as
  discarding the *edits*, not deleting the *instance* being edited. Exact wording: **OPEN**.
- **A "clear the form" control** — starts a brand new, blank instance. If unsaved edits exist, this
  asks for confirmation first.
- **No delete button on the form — SETTLED 2026-07-30.** Deleting an instance can come with a lot of
  warning/contextual information (as item 18's confirmation already does for a bill) — that lives on
  the separate list/management tab instead, next to the row being deleted, not on this form. Keeps the
  form itself purely about editing.
- **The form clears itself automatically after a successful save — SETTLED 2026-07-30.** The user has
  just finished with this instance; starting the next visit blank is the right default. The manual
  "clear" control (above) still exists for clearing mid-edit, before any save.
- **Programmatic instance selection:** when a shortcut opens this form to edit a specific existing
  instance, the instance-picker's value is set **programmatically**, without ever visibly opening its
  popup — true even when Philosophy 6's populate-with-a-delay behavior (below) is turned on for every
  other field.

### 2. The characterization-field-block

Second region — second in importance because it's the next set of controls the user interacts with,
and because it transforms everything below it.

- Holds the fields that decide what *kind* of thing this is, when the form isn't in full
  advanced/generic mode (e.g., is this a "bill"? a "loan"? a "one-time goal"?).
- Holds a non-interactive **characterization text** — a small, friendly, computed label (reusing
  names already used elsewhere in the UI's own shortcut buttons: "bill," "loan," "one-time goal,"
  etc.) derived live from whatever the block's own fields currently say.
  - When a shortcut button opens the form pre-set, this text confirms what it set.
  - When a user instead builds the exact same shape by hand from the advanced/generic form, the
    **identical text appears once their manual choices happen to match a named shape** — quiet
    confirmation they're "doing it the right way," in service of Philosophy 1/4 (equipping the user
    with information right where they're deciding, even when they chose the unassisted path).
  - **When nothing recognized matches — SETTLED 2026-07-30: shows "Custom."** The block always shows
    something, rather than going blank the moment a user steps outside every named shape.

### 3. Everything else

Not yet organized into named sub-blocks.

---

## Helper regions (internal name only)

Contextual, modular blocks that appear only when relevant. **Broadened 2026-07-30:** originally
scoped to blocks driven only by a characterization-field-block choice (the RRule preview, the
loan-payoff estimator); now covers *any* conditionally-appearing contextual content, including
blocks driven by other things — e.g. whether an existing instance is selected at all (Account's own
two, below). One term for "shows up only sometimes, gives helpful context, isn't a core field,"
regardless of what triggers it.

**Known today:**
- The recurrence-rule editor + its occurrence preview (already built) — appears whenever the thing
  being made/edited is recurring.
- The loan-payoff estimator (`PayoffEstimator`) — appears only when the thing is recurring **and**
  set to an indeterminate length (a loan).

**More exist or are planned, not yet enumerated** — the author knows there are others but couldn't
list them at the time of writing. Two candidates found while re-checking existing code were flagged
OPEN here on 2026-07-30; **both are now resolved, as of the Earmark content inventory, 2026-08-02:**
- `ManualEarmarkWindow`'s existing `BalanceInfoText` — confirmed as a real helper region, and then
  superseded by something richer: Earmark Step 2's "What can I allocate today?" region (One-off
  adjustment mode) folds it into free-funds/total-allocated/priority-grouped/same-day-earmark/
  deallocation-warning content, replacing the plain port rather than sitting alongside it.
- **The "in-window plan preview"** — confirmed and built into content: Earmark Step 2's "Arriving via
  Save and Plan — Proposed Allocation Plan review" row (Savings-plan mode).

**Confirmed under the broadened definition:** Account's two (below) — the cushion's current amount
vs. target, and "referenced by N Expenses, M Transfers" — both conditional on an existing instance
being selected, not on any characterization choice (Account has none).

**Layout constraint (the only one settled):** a helper region is never positioned above, or at the
same level as, the instance-information-block or the characterization-field-block.

**Reframed 2026-07-30: exact placement is not one question with one answer — it'll vary case by
case**, per the author. The real remaining work is two-part, per form: **(1)** enumerate every helper
region that form needs (the full list is still open — see the two candidates above, and "more exist,
not yet enumerated"), then **(2)** for each one, work out whichever spot actually helps most, given
that specific region's own content and how often it's likely to matter. Not a single layout rule to
settle once — a per-region design pass, once the full inventory exists.

**One real rule for that per-region pass, though — SETTLED 2026-07-30: a helper region's orientation
follows its own triggering fields' orientation, and it sits directly adjacent to them.** If the
fields that set up a recurrence run horizontally (e.g., along the bottom of the form), the
recurrence preview goes horizontally, directly under them. If those same fields sit in a vertical
column (e.g., the form is laid out in columns, recurrence fields in the right-hand one), the preview
goes vertical, directly beside that column. The point is that the relationship between a helper
region and the fields driving it should be *visually obvious* from adjacency and matching shape
alone — never a helper region floating somewhere unrelated to its own trigger fields, regardless of
which of variants A/B/C/G's general placements (sidebar/stacked/inline/collapsed) gets picked.

**Code-organization requirement:** whatever builds helper regions should be clustered together (in
the same file, where feasible), and should mention the term "helper region" somewhere in its own
description — so a later reader can find every one of them from any single one. Helper regions should
be a relatively modular addition, both in the form's visual layout and in the code that builds it.

---

## Form content inventory — consistent vs. contextual, one form at a time

**Why this exists, 2026-07-30:** the mockup round skipped straight to layout without actually
enumerating every field and every piece of helpful information each form needs — the recurrence
input fields themselves got missed entirely until the author caught it. Layout choices made on an
incomplete inventory aren't worth much. This section fixes that, **one form at a time, easiest
first** (Account → Expense → Earmark → Transfer), checked against the author's own feedback on each
before moving to the next.

**Two categories, one small table each per form:**
- **Consistent** — always present for this form, every time, regardless of what the user is doing.
- **Contextual** — only present in a specific circumstance; the circumstance is named, not implied.

Each row is checked against the standing [design philosophies](../design-philosophies.md) where one
is clearly load-bearing, not decorated with a citation on every line.

### Account — first pass

The simplest of the four by a wide margin: the entire domain type is `Id`, `Name`, `Balance`,
`IdealSafetyCushion` (`Account.cs`). Grounded directly in `AccountWindow.xaml` for what's already
built, not guessed.

**Consistent**

| # | Field / info | Kind | Why it's always here |
|---|---|---|---|
| 1 | Account name | Field (text) | Every account has one; no circumstance omits it |
| 2 | Current balance | Field (currency) | Every account has one; core to Q1 (how much free money do I have) |
| 3 | Safety cushion | Field (currency, optional value) | Always present as a *field*, even though its value is often $0/blank — Stage 6 also settled this field gets explanatory copy (row 6) |
| 4 | "Whatever you call it — each account needs its own name" | Info (static caption) | Already built (`AccountWindow.xaml:19-20`) — Philosophy 2, plain language over a bare label |
| 5 | "What this account holds right now. Can be negative if overdrawn." | Info (static caption) | Already built (`AccountWindow.xaml:24-25`) |
| 6 | "Money held back so it isn't counted as free to spend. 0 means none." | Info (static caption) | Stage 6's settled discoverability fix for F12 — was previously just "(optional)" with no explanation |

**Contextual**

| # | Field / info | Shows when… | Why |
|---|---|---|---|
| 1 | The cushion's own current amount — **cushion only, not a total across the account's other jars** (e.g. "$380 of $500 cushion currently held") | Editing an **existing** account only — a brand-new account has no forecast history to read this from | Philosophy 4 — the field being edited is the cushion *target* specifically, so the relevant context is how close the cushion itself is to that target. The account's overall committed total (bills, goals, etc.) already lives on the Allocations tab and would be noise here — it answers a different question ("can this account afford a bigger cushion") that isn't what this field asks. Needs a forecast read, not a stored value — same dependency `ManualEarmarkWindow`'s `BalanceInfoText` already has. |
| 2 | "Referenced by: 3 Expenses, 1 Transfer" (read-only) | Editing an **existing** account only | Philosophy 1 — tells the user what's riding on this account before they touch its name or cushion, without needing a delete gate *on this form* (deletion itself lives on the Accounts list tab, already settled) |

**Deliberately excluded, not forgotten:** delete, and any "can't delete while it holds patterns"
warning — both belong on the Accounts **list** tab per the already-settled form/list split, not on
this form at all.

**Both contextual items confirmed helpful — SETTLED 2026-07-30.** No characterization-field-block for
this form either — settled the same day: an account has no "kind" to characterize (confirms what
mockup variant D already guessed).

#### Layout — SETTLED 2026-07-30

Four regions total, the content-inventory tables above sorted into them:

1. **Instance-information-block** — unchanged from the general pattern.
2. **The fields region** (what "everything else" is called when there's no characterization-field-
   block ahead of it) — Account name, Current balance, Safety cushion, each still carrying its own
   consistent-info caption inline, exactly as `AccountWindow` already does. **Current balance and
   safety cushion share an axis** (row or column — which one is still open) since they're related
   figures. **Safety cushion sits at the edge of this region** — deliberately, to leave room for
   region 3 to sit directly beside or underneath it without crowding into the middle of the field
   layout.
3. **Cushion-context helper region** — small, attached to the safety cushion field specifically
   (beside or underneath it — open which). Holds contextual item 1 (the cushion's own current amount
   vs. target).
4. **"Referenced by" helper region** — standalone; doesn't attach to any one field, since it's about
   the account as a whole. Holds contextual item 2.

No new region for the consistent-info captions — they stay exactly where they've always been, inline
with their own field inside region 2.

### Expense — second pass, taken in steps (more information, more choices than Account)

**Working assumption, stated up front rather than assumed silently:** there is no separate
"simple form" / "advanced form" split anymore. Philosophy 7 calls for one form per type doing double
duty for create and edit; "Create Bill…" becomes a *shortcut* that opens this one form pre-filled and
highlighted (Philosophy 6), not a genuinely smaller form. A field is either always there, or
contextual on something real (direction, repeats, an existing instance) — never on which button
opened the form.

**Amended 2026-08-02:** "no separate window" still holds — a genuine **Advanced mode** survives, but
as a mode switch *inside* this one form, not a second window. See its own subsection below.

**Correction this assumption caught:** earlier mockups (variants A/B/C) placed **Skippable/
unskippable** in the characterization-field-block next to Direction. Re-checked against what the
characterization text is actually for — computing a name like "bill" or "loan" — skippable doesn't
drive that name the way Direction and Repeats do (there's no separate shortcut for "a skippable
bill"). It belongs in the fields region instead, and it's contextual (B-4: hidden for income), not
consistent — moved to the contextual step.

#### Step 1 — consistent fields, SETTLED 2026-07-30

**Characterization-field-block**

| Field | Why it's consistent |
|---|---|
| Direction (Expense / Income) | Drives the sign and the bill-vs-paycheck naming |
| Repeats? (One time / Repeating) | Gates most of the form's contextual complexity |
| Characterization text (computed) | Always shows something — "Bill," "Paycheck," "Custom," etc. |

**Fields region ("everything else")**

| Field | Why it's consistent | Proximity |
|---|---|---|
| Source | Required (4.2.a1) | **Tightly paired with Description** — closely linked, keep adjacent |
| Description | Optional value, field always present | Tightly paired with Source (above) |
| Account | Every expense is filed under one | **Loosely associated with Source and Priority** — which account got chosen can trace back to what the expense *is* (Source) and how important it is (Priority); near that pair, not as tightly bound as Source↔Description |
| Amount | Core numeric value | **As prominent as Source/Description** — equal visual weight, not a smaller/secondary field, even though it isn't part of either proximity cluster |
| Priority | Always relevant to deallocation ordering | Loosely associated with Account (see above) |

Two informal clusters emerging: **Source + Description** (tight), **Account + Priority** (loose,
near the first cluster). Amount stands apart from both but needs matching prominence. Exact
row/column arrangement not decided yet — same "relationship is settled, geometry isn't" treatment
Account's own axis-sharing got.

#### Step 2 — contextual fields/info, grouped by trigger, SETTLED 2026-07-30

**Trigger: Direction = Expense** (hidden when Income — B-4's existing rule)

| Field | Why |
|---|---|
| Skippable / unskippable | "Money coming in is never something you skip paying" (B-4) |

**Trigger: Repeats? = One time — RESOLVED 2026-08-02, closing the gap flagged during Earmark
planning.** Not "nothing to configure," as this table previously (wrongly) implied — a one-time
Expense still needs its own date, just not the rest of the recurrence machinery.

| Field / info | Why |
|---|---|
| Due date | A single plain date field, positioned near **Amount**, not inside a recurrence section — there's nothing else to configure for a single occurrence. Internally still produces a real `RecurrenceRule` with one occurrence (`Count=1` → `Until=Start`), the same convention the domain already uses for isolated expected transactions |

**Genuinely open, not resolved by the above:** whether an *advanced* escape hatch still exists —
letting a user who wants to hand-build a technically-one-occurrence rule use the full
`RecurrenceRuleEditor` instead of the plain Due-date field. Flagged by the author as "in question," not decided either way.

**Trigger: Repeats? = Repeating** (hidden when One time)

| Field / info | Why |
|---|---|
| Stops… (On a date / Paid off / Keeps going) | A one-time thing doesn't stop, it just happens once |
| Frequency, Every N, On-these-days *(weekly)* / On-this-day-of-month *(monthly)*, Starts | The actual recurrence definition |
| Recurrence preview (helper region) | Illustrates whatever the fields above say |

**Correction worth flagging:** the recurrence editor's own raw "Ends: On date / After N occurrences"
radio does **not** show here as a separate question. The already-built form already does this right —
*"the simple bill form asks 'when does this stop?' and supplies the end date to the schedule editor
itself, so the editor's own Ends controls step aside."* The higher-level Stops… answer resolves
`Ends` directly; asking the raw radio too would be asking the same thing twice.

**Trigger: Repeats? = Repeating AND Stops… = On a date**

| Field | Why |
|---|---|
| End date | Feeds `Ends` directly — the one case where the user names the date themselves |

**Trigger: Repeats? = Repeating AND Stops… = Paid off**

| Field / info | Why |
|---|---|
| Total still owed | Input to the payoff estimate |
| Loan payoff estimate (helper region) | Computes the payoff date **from** the amount owed — and that computed date is what feeds `Ends`, same role the plain date field plays in the "On a date" case above |

**Trigger: Repeats? = Repeating AND Stops… = Keeps going**

Nothing additional and visible — per B12's own ruling, `AutoRenew` is set invisibly by choosing this
option, never exposed as its own control.

**Trigger: creating a NEW Expense, Direction = Expense** (never for Income — Stage 1: income never
gets a plan; never on an edit — a plan is proposed once, at creation)

| Field / info | Why |
|---|---|
| Proposed Allocation Plan preview + "remove this plan" | Cluster D from [20](20-ui-phase-inventory.md) — reviewing/declining what `AllocationPlanProposer` pre-filled before committing |

**Trigger: editing an EXISTING Expense that has a linked savings plan** — **PROPOSED, not yet
confirmed**, by direct analogy to Account's own "cushion currently held" contextual item:

| Field / info | Why |
|---|---|
| "Currently saved toward this: $X of $Y" | Same Philosophy-4 reasoning as Account's cushion context — relevant right where the user might be about to change the amount or priority. Needs a forecast read, same dependency as Account's version. **Flagging this as my own inference, not something already settled** — confirm or reject before it's treated as real. |

**Cross-referenced, not re-derived here:** the instance-information-block's own contextual states
(blank / editing / unsaved / break-off) already cover "Change starting on a date…" only appearing
for an existing, recurring instance — that's general-pattern behavior from variant H, not new to
Expense specifically.

#### Advanced mode, SETTLED 2026-08-02 (content only — the switch control itself not yet placed)

Historical grounding for why this exists at all: the program's earliest functional UI had no
characterization-field-block — `Mandatory` was a plain checkbox, and "is this repeating" was never
asked, only *derived* by counting a pattern's own occurrences. Advanced mode is a deliberate return
to that paradigm, as a mode switch inside the one Expense form (not a second window — Philosophy 7
still holds): every field shows regardless of whether the characterization would normally hide it,
and whatever can be derived from the raw fields is derived rather than asked twice.

- **`Repeats?` stops doing anything in advanced mode** — disabled or removed (not yet decided which).
  Its only job was gating which fields show; advanced mode doesn't gate fields at all, and "is this
  repeating" is answered by counting the RRule's own occurrences instead, exactly like the original.
- **The recurrence preview may get replaced, not just left alone**, when the hand-built RRule
  resolves to exactly one occurrence — a calendar showing a single date reads oddly; some other
  helper region plainly stating "this is a one-time expense" (computed, not asked) is likely
  clearer. Exact replacement content not designed.
- **A dedicated control to switch into advanced mode is needed** — not yet placed anywhere. The one
  hard requirement so far: switching shouldn't move the rest of the layout around drastically.

**Still open:** where the mode switch itself lives, and whether Direction/Stops… (the
characterization-field-block's other controls) behave the same way `Repeats?` does in advanced mode
or differently.

#### The save controls, SETTLED 2026-08-02

Two buttons, at the **bottom of the form** — deliberately not the instance-information-block: that
block's own buttons are about *which instance you're looking at*, these are about *committing what
you just did*, and sit at the opposite end so the user has seen the whole form before reaching them.
Applies equally to creating a new Expense and editing an existing one.

- **Save** — commits and returns to the Forecast tab.
- **Save and Plan** (name pending) — commits, then jumps to the Earmark form with this Expense's
  linked plan already loaded in the instance picker (Philosophy 6 — programmatic selection, no
  popup). Relevant on an edit as much as a create: many changes to an Expense meaningfully affect its
  linked plan, and a follow-up visit there is expected, not merely offered. **A real `EarMarkPattern`
  already exists by the time this jump happens** — Stage 1's own ruling gives every outflow a
  proposed (or, if declined, an empty) plan *at creation*, regardless of which save button is
  pressed — so the Earmark form never lands on a blank "new" state here, only on the plan that was
  just created or just affected.
- **Styling:** both buttons read muted/inactive until there's an unsaved change, then active — the
  same pending-change convention the Forecast tab's own "Forecast" button already uses. **Save and
  Plan** additionally gets an extra outline when the pending change is one that would meaningfully
  affect the linked plan, distinguishing "you may want to go check on this" from a routine edit.
- Earmark's own save is a single plain button, returning to the Forecast tab — no onward hop from
  there to anywhere else (considered and declined, see the Earmark section below).

**Four plan-health states, SETTLED 2026-08-02 (content only — not yet designed as UI beyond this):**
the only actionable information Expense itself needs to carry is *which* of four states the linked
plan is in — **will miss its goal**, **already missing it**, **currently overfunded**, or **will be
overfunded** — surfaced as a small status indicator (the state named in plain text, not only implied
by styling), plus the same **Save and Plan** emphasis-outline above, now *also* triggered by the plan
being in one of these states, not only by the current edit's own effect. The text matters on its
own, not just as decoration for the button: it's what tells the user *why* Save and Plan looks
different, and whether the plan can honestly be skipped this time. Expense does not explain or offer
to fix the problem itself — it just names the state; every actual fix lives on Earmark's side, and
which one to design toward is intentionally not decided yet (see below). Designing the actual
Earmark-side helper regions for these four states is queued as the next content pass, following the
standing principle below.

**Not the same thing as charter item 24's deferred nudge.** Item 24's own deferred piece is the
*proactive* half — the system reaching out to the user unprompted, still parked at a future UI stage.
What's described here is *passive*: information shown only once the user has already navigated to
this specific Expense, exactly the "discoverability is passive by default" principle already
standing in [11 § C](11-ui-design-and-decisions.md#standing-ui-principles-distilled-from-the-above).
So this is in-scope for the current pass; only the active/unprompted version stays deferred.
**Also worth being precise about, since it's easy to conflate the two:** item 24 is specifically
about the *overfunded* case, not the short one. A different idea, clarified 2026-08-02: a **future
shortcut from the "already missing"/"will miss" states directly into creating an isolated earmark**
on the Earmark form — the informational-region version of what `AllocationPlanProposer
.MaybeStartingEarmark` already automates at creation time, but for a *drifted existing* pattern
instead of a brand-new one. Flagged now, ahead of the shortcut-design phase generally, because it's
expected to be common and it's a direct answer to Q1 ("how much money do I have free to spend") —
an uncovered mandatory expense sitting nearby corrupts that number until someone addresses it.
**A freshly-created pattern can already be in the "missing" state**, not only a drifted existing
one — specifically `AllocationPlanProposer.ProposeEmpty` (the declined-plan path), which creates a
real, zero-contribution `EarMarkPattern` that reads as fully short the moment it's created. Every
other proposer path is constructed to exactly cover the goal, so it reads as funded at creation by
design.

**Not yet done:** Step 3 (layout/regions, once this step is confirmed).

### Earmark — third pass, and a structural distinction Expense's own pattern doesn't carry over

**Worth stating precisely, since it very nearly got missed:** Expense and `FinancialPattern` are the
same thing, no exceptions — every expected transaction, even a one-time one, is backed by its own
`FinancialPattern`, and editing one always means editing the other (`FinancialPattern`'s own
generated occurrences aside — those aren't user-edited directly). Earmark is genuinely different.
Isolated `EarMarkEvent`s (`repeated_earmark = false`, the `ManualEarmark` class in code) exist
*independently* of the `EarMarkPattern` they sit alongside — creating or editing one touches no
pattern at all, though the pattern has to already exist (`3.13.8.a1`). So the unified Earmark form
has two real modes, not one: editing the pattern's own repeating schedule, or editing a single dated
entry that coexists with it — never a plain either/or, since both can and often do exist for the
same goal at once.

**Naming, SETTLED 2026-08-02:** the mode toggle is **"Savings plan"** / **"One-off adjustment"** —
deliberately not phrased as an exclusive choice (unlike Expense's `Repeats?`), and echoing
`ManualEarmarkWindow`'s own existing shipped copy ("a one-off, manual adjustment **on top of** the
automatic savings plan") rather than inventing new wording.

**Terminology, SETTLED 2026-08-03 — the same word, two separate registers.** As a UI label, "Savings
plan" names only the toggle above (editing the `EarMarkPattern` — encompassing every repeated earmark
it generates — versus an isolated earmark that exists on it with `repeated_earmark = false`).
Separately, in internal design discussion (this document included), **"Savings Plan"** means the full
set of `EarMarkPattern`s sharing one `FinancialPattern`'s `finance_id` — possibly more than one, now
that F27 (charter item 9) allows a second concurrent pattern or a break-off/restructure chain's
predecessor + successor. A term for that collection is needed precisely because the original design
never allowed more than one `EarMarkPattern` per `finance_id` (`3.11.1.a1`) — there was nothing to
collectively name before. The two uses of the word don't need to match or be otherwise disambiguated:
UI label wording is a separate concern from internal design terms (see
[[feedback-design-in-class-documentation-terms]]).

#### Step 1 — consistent fields, REVISED 2026-08-02 — "Saving toward" removed

**"Saving toward" does not survive as its own field.** The instance-picker itself takes over that
job: it's a goal picker first — searchable by the linked Expense's own name, the same mechanism as
Expense's own instance-picker popup — and picking a goal loads whatever `EarMarkPattern` exists for
it, defaulting to Savings-plan mode. This also resolves the safety-cushion gap flagged in the prior
pass: the cushion (`finance_id = None`, no linked `FinancialPattern` at all) becomes its own special,
always-available entry in that same picker, letting the user set up manual or repeated earmarks
against it like any other "goal."

**A disambiguation step is genuinely needed, confirmed against the code, 2026-08-02:**
`RestructureFactory.Restructure` (`RestructureFactory.cs:93-113`) really does create two
`EarMarkPattern` records — a truncated predecessor and a successor — sharing one `finance_id`, so a
plan broken up mid-life is a real, already-built shape, not a hypothetical. Expense's own equivalent
problem has a settled answer (`BreakOffFactory.FindCurrentSegment`, "Edit" always silently opens the
current segment) — but that mechanism is keyed on `Source`, a property `FinancialPattern` has and
`EarMarkPattern` does not, so it doesn't carry over automatically. Nothing today decides "which
segment is current" for Earmark the way it's decided for Expense — which means offering the user an
explicit choice, rather than silently guessing, isn't just permitted here, it may be the *right* call
specifically because Expense's silent-redirect precedent doesn't actually apply. **A new helper
region follows from this, not yet designed:** editing an early (superseded) segment has a real
knock-on effect on its successor — per `RestructureFactory.cs:39-44`'s own comment, the jar never
restarts across a cut, so a changed predecessor amount changes the jar balance the successor's own
accumulation continues from, even though the successor's own fields never change.

**Resolved 2026-08-02 — a popup, only when ambiguous, plus a separate rare-case control living
inside it:**
- Arriving via **Save and Plan** lands directly on the linked plan *unless* more than one
  `EarMarkPattern` exists for that goal (overlapping — the multi-income case — or end-to-end,
  break-off's own shape) — only then does a popup ask which one to start editing. The everyday case
  (one plan) never sees this popup at all.
- The same popup is the general-purpose one used any time a goal is picked directly (not just this
  disambiguation moment) — the instance-picker popup itself (round 1's rough sketch) still needs real
  design, queued alongside break-off, below.
- **A control to start a brand-new, genuinely *concurrent* `EarMarkPattern`** (the rare
  multiple-income-streams case, item 9's original other justification) lives inside that same
  popup — small, clearly labeled, deliberately *not* a dedicated button on the Earmark form itself.
  Reasoning: the form's own prominent buttons ("+ New Earmark," "Clear form") could otherwise read as
  implying a fresh start that erases the existing plan, which this explicitly isn't. Placing it in
  the popup instead is acceptable specifically because the popup is rarely opened at all — Save and
  Plan pre-populates the goal automatically in the common case.

| # | Field / info | Kind | Why it's always here |
|---|---|---|---|
| 1 | Savings plan / One-off adjustment | Field (characterization toggle) | Decides whether this instance edits the `EarMarkPattern` or a single isolated `EarMarkEvent` |
| 2 | Characterization text (computed) | Info | Mirrors Expense's own — worded so it stays clear the pattern still exists even while the form is in one-off mode |

#### Step 2 — contextual fields/info, grouped by trigger, REVISED 2026-08-02

**Trigger: a goal is loaded via the instance picker** (either mode)

| # | Field / info | Shows when… | Why |
|---|---|---|---|
| 3 | The linked goal's own detail, read-only (amount, due date/schedule, account, priority) | A goal is loaded | Philosophy 4 — sizing any contribution or adjustment against this goal needs its numbers right here |

**Trigger: toggle = Savings plan**

| # | Field / info | Shows when… | Why |
|---|---|---|---|
| 4 | Amount per occurrence | Savings-plan mode | `EarMarkPattern.Amount`, required (`EarMarkPattern.cs:11`) |
| 5 | Recurrence fields (Frequency/Every/Starts/Ends, shared `RecurrenceRuleEditor`) | Savings-plan mode | `EarMarkPattern.DatePattern`, required (`EarMarkPattern.cs:10`) — always a genuine repeating schedule, since a one-time plan was considered and explicitly deferred (see below) |
| 6 | Recurrence preview (helper region) | Savings-plan mode | Nothing to preview in one-off mode — there's no rrule for a single dated entry |
| 7 | Starting earmark — system-proposed or user-declared "already saved" | A proposal exists (`AllocationPlanProposer`'s `StartingEarmark` is non-null), or no starting entry exists yet | One mechanism, two flavors: `AllocationPlanProposer.MaybeStartingEarmark` (system, with its own reasoning) and a user-initiated shortcut (a plain declaration) both resolve to one `ManualEarmark.Create` call — each call site comments which case it represents |
| 8 | Arriving via **Save and Plan** — Proposed Allocation Plan review + accept/adjust/remove | Arriving via that shortcut, or an existing goal with no plan yet | `AllocationPlanProposer`'s own output — closes what was an open "in-window plan preview" item |
| 9 | Current jar state — "$X saved of $Y milestone" | Editing an **existing** `EarMarkPattern` only | Same reasoning as Account's own cushion-context helper |
| 10 | Existing manual earmarks on this pattern — an overview, with shortcuts to edit ones that haven't happened yet | Savings-plan mode, at least one exists | **NEW 2026-08-02.** Finds and adjusts a one-off already made, without leaving the pattern view |

**Trigger: toggle = One-off adjustment**

| # | Field / info | Shows when… | Why |
|---|---|---|---|
| 11 | What do you want to do? (Add / Withdraw / Move) | One-off mode | Already built, `ManualEarmarkWindow.xaml:18-23` |
| 12 | Move-to fund picker | One-off mode AND action = Move | Already built, `ManualEarmarkWindow.xaml:28-31` |
| 13 | Amount (this entry) | One-off mode | `ManualEarmark.Amount` |
| 14 | Date | One-off mode | `ManualEarmarkOptions.Date` — a single day, no rrule |
| 15 | **"What can I allocate today?"** — one merged region, beside Amount: free funds this day, total allocated, this goal's priority, other goals' allocations summed and grouped by priority *relative to this one* (more / equally / less important — never named individually), whether another isolated earmark already exists on the selected date, and whether the proposed amount would trigger a deallocation day (with a plain explanation of what that means) | One-off mode | **REVISED 2026-08-02 — merges what were two separate rows, plus a new deallocation warning.** All of it answers the same question, "how much do I actually have to work with today": priority alone isn't actionable, but combined with same-day totals it is; the same-day-earmark notice isn't actionable either (`3.13.8.1.a1` merges same-day isolated earmarks into one) but explains afterward why the total isn't what was typed; the deallocation check is the one genuinely consequential warning in the group. Supersedes the plain `BalanceInfoText` port (`ManualEarmarkWindow.xaml:39-41`) entirely, not alongside it |

**Explicitly considered and declined, not forgotten:**
- A non-overlap check between sibling `EarMarkPattern`s sharing a `finance_id` — multiple concurrent
  patterns are fine if they don't cause problems; F30 (charter, item 9) already handles same-day
  merging and stays a live mechanism, not dead code.
- A "fund a new goal instead →" control inside the instance picker — at most a passive tip in the
  instance-information-block, no dedicated control, per the standing discoverability-is-passive
  principle ([11 § C](11-ui-design-and-decisions.md#standing-ui-principles-distilled-from-the-above)).
- A "one-time plan" (a single lump sum instead of steady payments) for an `EarMarkPattern` —
  genuinely liked, but deferred as a future feature; see
  `redesign/memory/project_deferred_features_backlog.md`. **Clarified 2026-08-02:** the *manual*
  version of deferral is already achievable with what's built — over-fund normally, then
  `RestructureFactory.StopContributing` (possibly more than once, to pause and later resume) — the
  same tool item 22 already uses. Only the *automatic*, system-nudged version (charter item 24's own
  entry point) is the real remaining gap.

**Four plan-health use cases, identified 2026-08-02 — content design queued as the next step, per
the standing principle below:** will-miss-goal, already-missing-goal, currently-overfunded,
will-be-overfunded (see Expense's own save-controls section, above, for how these surface there).
Earmark is where the user actually acts on any of them. **Deliberately not prescribing which
mechanism is "the" fix here** (author correction, 2026-08-02) — a manual earmark, `Restructure`,
`StopContributing`, or editing the Expense's own due date are all *possible* paths that already exist
with no new mechanism needed, but the job of this pass is showing the state clearly enough that the
user can reach for whichever tool they'd actually choose, using the form's own normal controls — not
steering them toward one answer. Those mechanisms are candidates for *shortcuts* later, once the
information design itself is settled, not part of the information design.
- Applies by default to an **existing** pattern whose situation drifted — plus one fresh-pattern case,
  a declined proposal (`AllocationPlanProposer.ProposeEmpty`), which is short from the moment of
  creation.

**Engine backing built 2026-08-04 — `PlanHealthState`, one per Savings Plan, computed inside
`CreateForecast` alongside `GoalShortfall` (which it nests, not duplicates).** Answers every question
this section needed a number for, all reusing existing computations rather than a parallel one:
`CurrentShortfallAmount`/`CurrentOverfundedAmount` (today's `ExpectedAmount` vs. the reset-at-release
`MilestoneAmount`, [14](14-stage1-allocation-model.md)), `IsChronicShortfall` (a static check — would
the Savings Plan's own scheduled contributions, run to completion, structurally reach `AmountNeeded`
on their own, independent of anything that's already happened — a one-time catch-up only helps when
this is true), `CanSkipNextPayment` (the excess covers a whole extra occurrence),
`UnderfundedReleaseDates` (the empirical "has this already happened" record — extends the existing
`FlooredManualEarmarks` floor-detection to a goal's own release, not just manual withdrawals), and
`MostImportantHealthState` (a `PlanHealthCategory`: `AlreadyMissing`/`WillMiss`/`CurrentlyOverfunded`/
`WillBeOverfunded`/`Healthy` — the author's ranking rule: today-short always wins, any future shortage
beats any excess regardless of timing, excess only surfaces when no shortage exists anywhere).
`IsWorthWarningAbout` is a deliberate placeholder (always true) — the false-positive filter is a
parked author idea, not designed yet. 7 new tests, 273 total green, 0 warnings. **Still not done:**
the actual informational content/copy for the four states (this section's own stated next step) and
the instance-picker popup's highlighting behavior (planning/21 discussion, 2026-08-03) — this only
supplies the numbers.

**Standing principle, added 2026-08-02 (author):** design the *informational* helper regions for a
state first — what's wrong, what the user could do about it — before any shortcut that acts on it.
*"If the forms are so confusing that the shortcuts we provide are the only way the user can do what
they want, the user isn't actually in control."* Recorded alongside the other standing UI principles
in [11 § C](11-ui-design-and-decisions.md#standing-ui-principles-distilled-from-the-above).

#### Step 3 — layout, IN PROGRESS

**Top and bottom, SETTLED 2026-08-02** (mockups:
[earmark-form-layout-mockups.html](mockups/earmark-form-layout-mockups.html), variant X):

- The instance-information-block and characterization-field-block merge into **one combined row**,
  side by side rather than stacked — left half (blue) carries the instance picker + unsaved-changes
  status; right half (green) carries just the Savings-plan/One-off-adjustment toggle. Stacking two
  full bands wasted space here: Earmark's characterization content is thin enough that it needs no
  computed "This looks like…" sentence the way Expense does — the toggle already states its own two
  possible values directly, so a sentence restating them adds nothing.
- The goal's identity is stated **once**, on the instance side — the instance picker itself is now
  the goal picker (Step 1, above), so there is no second field to repeat it.
- **"Clear form"** (full reset — drops the selected goal too, Step 1's picker starts over) gets its
  own small neutral zone, right of both colored halves, belonging to neither — it acts on the whole
  form, and sits deliberately far from the instance picker to reduce mis-click risk. Clearing unsaved
  edits still asks for confirmation first — the general rule from the first mockup round.
- **"+ New Earmark"** (confirmed 2026-08-02, a genuinely different button, not an alternate label for
  Clear form — moves *forward*, from viewing an existing pattern to a blank isolated earmark on the
  *same* goal, default date = today) **lives in the characterization-field-block itself, SETTLED
  2026-08-02** — not the neutral zone. It's related to the Savings-plan/One-off toggle (functionally,
  clicking it is a shortcut to "switch to One-off, and start that side blank"), so it sits with the
  toggle rather than sharing Clear form's zone. Resolves the prior open question about how the two
  buttons would share one space — they don't; Clear form keeps its own isolated zone precisely so it
  reads as the drastic, separate action, and only appears in some states (Savings-plan mode, an
  existing pattern loaded) rather than always.
- The save row (bottom) repeats the same instance identity in its own free space, next to the single
  **Save** button — settled earlier in this pass, unchanged by the above.

**Not yet done:** everything between the two (goal detail, amount, recurrence, starting-earmark and
proposed-plan review, current jar state, the one-off-mode field set, the two new helper regions
above, and the four plan-health states' own informational content) — plus two topics identified
2026-08-02 that haven't been started at all: the **break-off/restructure UI** for both Expense and
Earmark (acknowledged as non-trivial — what information the user needs for that case specifically is
still an open question), and the **instance-picker popup's own real design** (round 1 only sketched
its shape; it now needs to work as a genuine goal-picker for Earmark, not just an Expense-picker).

---

## Philosophy 6 in practice — the two shortcut-transparency mechanics

### 1. Highlighting

When a shortcut lands the user on this form pre-configured, two things visibly highlight: the
top-level **tab** the form lives on, and whichever option(s) are selected in the
**characterization-field-block**. The characterization text (above) reinforces the same thing in
words. Together, these make a shortcut's landing state legible at a glance, not just functional.

### 2. Timing

Functionally, opening the form via a shortcut is just "clear the form, then set some values." To make
*how it got there* visible without literally animating every keystroke (too complicated, too
annoying), values are instead inserted **one at a time — most important field first, least important
last — with a very small delay between each,** so the change is quick but still visually noticeable.
**The whole sequence should take no more than 200 milliseconds.**

**"Most to least important" — SETTLED at the block level, 2026-07-30: layout order IS importance
order.** Every field in the instance-information-block outranks every field in the
characterization-field-block, which outranks every field below it — populate top block first, then
the next, in on-screen order. **The exact field-by-field sequence within (and across) blocks is not
yet decided** — the author wants to see the actual UI layout before making that finer-grained call,
since which fields end up adjacent or grouped will shape what order reads naturally. Revisit once a
concrete layout exists for any given form.

Whatever high-level function starts this populate-the-form process **must take an optional parameter
that adjusts or fully disables this delay** — some users will find the staggered reveal annoying and
just want every value set at once.

**Exception:** even with this delay turned on, the instance-information-block's own instance-picker
field (see above) is set without ever visibly opening its popup — that one field's programmatic
setting is not part of the staggered reveal.

---

## Open questions collected in one place

- ~~The generic term for a bill/paycheck~~ — **settled: "Expense."**
- ~~Whether the form has its own delete button~~ — **settled: no, the list/management tab only.**
- The exact wording for "discard unsaved edits," distinct enough from anything that sounds like
  deleting the instance.
- ~~When a form should implicitly clear itself~~ — **settled: after a successful save.**
- The full enumeration of helper regions, per form — **Account and Expense done; Earmark's own list
  is now long and largely settled (see its Step 2), though its four plan-health states still need
  their own content designed; Transfer not started.** Then, per region found, the case-by-case work
  of deciding where it actually helps most to sit remains — not one layout question, a per-region
  design pass, form by form.
- **The exact field-by-field populate order for the timing mechanic.** Block-level order is settled
  (instance-information-block → characterization-field-block → everything else, top to bottom); the
  finer order within/across blocks waits on seeing an actual laid-out form, per form.
- ~~Whether a form's tab is the same tab as its list/management view~~ — **settled: always separate.**
  See "The tab bar" below for what this means concretely.
- **How a list/management tab surfaces which items were created or edited most recently**, so a user
  can find something they just made. Several possible approaches exist; none chosen — explicitly not
  to be locked down yet.
- **Where (if anywhere) a shortcut for a "funds are thin"-style Forecast-tab warning would go.**
  Unlike every other shortcut described so far, this one doesn't target one specific instance — it's
  ambiguous which fund or account should actually be adjusted. ~~Tentative idea: let the user copy the
  date...~~ — **settled below: the date text itself becomes selectable/copyable, no new control
  needed.**

---

## Philosophy 4 in practice — two new cases needing design

The recurrence-rule editor's occurrence preview is the one case already built. Two more were
identified this session, both undesigned:

- ~~Creating an isolated manual earmark on a specific day~~ — **RESOLVED 2026-08-02, without either
  of us noticing at the time it was the same open item from here.** This is exactly Earmark Step 2's
  "What can I allocate today?" region (One-off adjustment mode): free funds, total allocated, and —
  answering this section's own "will this cause a problem" question directly — a deallocation-day
  warning with a plain explanation of what that means. Left as a pointer here on purpose, as a
  concrete example of why this document's own status line matters: the two sections describing the
  same need sat far enough apart that solving it once didn't prevent almost re-deriving it.
- **The Forecast tab's existing "funds are thin"-style warnings** are actionable in principle, but as
  noted above, it's unclear where their own shortcut would even go, since — unlike a shortcut into
  one specific bill or goal — there's no one obvious fund or account the shortcut should target.
  **SETTLED 2026-07-30 — no shortcut, no new control, at all:** the selected-day panel's date display
  becomes plain, natively selectable text (Ctrl+C copies it) instead of whatever renders it today,
  so a user can grab the date themselves and paste it into whichever form they choose to open. This
  is a completely normal WPF technique, not a workaround: a locked-down, read-only `TextBox` (styled
  borderless/transparent to look identical to a plain label) supports selection and copying natively,
  where a plain `TextBlock` does not. The project already does exactly this —
  `RecurrenceRuleEditor.xaml:58`'s `RruleStringTextBox` is a read-only `TextBox` for the same reason.
  **Doesn't touch the lock:** nothing about what the panel shows or how it's organized changes; only
  the underlying control type for one piece of text does, styled to be visually identical.

---

## Clarification: "speculative expense" is not a distinct thing

[13b](13b-user-action-catalog.md)'s rows B3/B4 use "a one-off expected expense" / "a speculative
expense" as labels — this is not, and was never meant to be, a separate mechanism or kind of
`FinancialPattern`. Any expense (or earmark) can be created purely to see its effect on the forecast,
then deleted afterward to put things back — an ordinary, if slightly informal, use of the create/
delete tools that already exist. Nothing new needs building for this case; the user can be trusted to
use the existing tools this way without special support.
