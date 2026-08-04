# 20 — The UI phase: inventory and clustering

**Status: RAW INVENTORY EXPANDED 2026-07-30, not yet clustered by the author.** Stages 0–6 of the
["Adjusting the Plan" phase](13-adjusting-the-plan-charter.md) are design-complete (see the charter's
status table). This document is the starting point for the next, separate effort: giving everything
that phase decided an actual screen. Not itself a numbered stage of that phase — this is where its
output gets implemented.

**Locked, out of scope — corrected 2026-07-30:** only the Forecast tab's **zoomed-out overview** and
**selected-day** panels themselves — their contents, layout, and size
([08](08-forecast-tab-design-philosophy.md) §2–3) — already carry their own "IS meant to / IS NOT
meant to" design-goals treatment and are not being redesigned here. **The Forecast tab's own top
controls/toolbar (§1) are explicitly OPEN** — the author's first example: the "Across all accounts"
balance/cushion summary that lives there predates multi-account support and may not be the right
shape anymore now that accounts are independent silos. A button that *opens* something from inside a
locked panel (e.g. "Adjust funds…") is not itself locked either way — only the panel's own layout and
goals are.

**Everything else is open**, including the export (§4 of the same doc) and the shape of the four
other tabs (Accounts, Transfers, Bills & Paychecks, Allocations) and every create/edit window.

**Revision note (this version):** the first draft of this document went straight to a clustering (by
user-facing capability). The author asked for the raw material first, at more granularity, in
*separate* lists — so a different clustering can be chosen. This version adds two raw inventories
(below) ahead of that first clustering attempt, which is kept afterward as one candidate, not the
answer. This version also corrects the locked-scope error above and adds a section on other
pre-multi-account assumptions worth a second look, in the same spirit as the balance/cushion summary.

---

## List 1 — Every existing piece of UI, as it stands today

Grounded directly in the `.xaml` files (2026-07-30), control by control, not summarized at the
tab level.

### MainWindow — outside any tab

- Title bar: "MyMoneyForecast"
- **Export Data…** button
- **Import Data…** button
- A `TabControl` with five tabs (below)

### Forecast tab — **top controls OPEN, the two panels below them LOCKED**

- **Top controls — OPEN, corrected 2026-07-30:** "As of" date picker, "Show forecast through" date
  picker, a read-only **"Across all accounts"** balance/cushion summary (household-wide sums —
  `AccountsSummaryText`, `MainWindow.xaml.cs:213-218`: `"{total:C} in {N} accounts · {cushion:C}
  cushion"` — collapses every account into one number each, a pre-multi-account shape), the
  **Forecast** button (pending-state affordance), **Export as Spreadsheet…** button.
- **Overview — LOCKED:** the calendar/timeline itself. Its own header row includes an **account
  filter** (`AccountFilterComboBox` — "All accounts" or one specific account, re-scopes every number
  below) — this control's *placement* is inside the locked panel (part of the overview's own header,
  `MainWindow.xaml:284-294`), not the open top controls, even though it's account-related.
- **Selected day — LOCKED:** header, a **DEALLOCATION DAY** chip (shown/hidden), the **Adjust
  funds…** button (opens `ManualEarmarkWindow` — the button's *placement* is locked, the window it
  opens is not), a
  "FREE TO SPEND" readout, the event list (left) + jar list (right) — locked.

### Accounts tab

- Grid columns: Account, Current balance, Safety cushion.
- Buttons: **Add Account…**, **Edit Selected…**, **Delete Selected**.
- Opens `AccountWindow`: Account name (textbox), Current balance (textbox), Safety cushion — optional
  (textbox). Save / Cancel.

### Transfers tab

- Grid columns: From, To, Amount, Repeats.
- Buttons: **Schedule Transfer…**, **Delete Selected**.
- Opens `CreateTransferWindow` (today's *only* form — no simple/advanced split yet): From account
  (combo), To account (combo), Amount to move (textbox), a full embedded `RecurrenceRuleEditor`
  (below). Create / Cancel.

### Bills & Paychecks tab

- Grid columns: Finance ID, Source, Description, Account, Amount, Mandatory, Priority, Repeats.
- Buttons: **Create Bill…**, **Add New (advanced)…**, **Edit Selected…**, **Delete Selected**,
  **Set Up Savings Plan…** *(this is F12 dormancy entry #3 — it already exists, 5th of 5 buttons)*.
- Opens `CreateFinancialPatternWindow` (shared by Create Bill / Add New / Edit, via a
  `forcedMandatory` mode switch):
  - Source (textbox), Description — optional (textbox), Account (combo)
  - Direction: **Expense** / **Income** radio pair (hidden + fixed in simple/forced modes)
  - Amount (textbox, label changes with direction)
  - Priority (textbox, default 5)
  - "If money got tight, could you skip this?" — **I have to pay this** / **I could skip or delay
    it** radio pair (hidden for income)
  - "When does this stop?" (simple-bill mode only): **On a date I know** radio + date picker, **When
    I've paid it off (a loan)** radio + total-owed textbox + computed payoff readout — plus a plain
    *gray sentence*, not a control: *"'It just keeps going', for bills that never end, is coming in a
    later update."*
  - Embedded `RecurrenceRuleEditor` (below)
  - Create / Cancel

### Allocations (Earmark Patterns) tab

- Grid columns: Allocation target, Amount per occurrence, Already Saved, Repeats.
- Buttons: **Create One-Time Goal…**, **Add New (advanced)…**, **Edit Selected…**, **Delete
  Selected**.
- Nested **Manual adjustments** sub-grid: Date, Fund, Amount — buttons **Add…**, **Edit Selected…**,
  **Delete Selected**.
- Opens `CreateOneTimeGoalWindow` (the simple path): What are you saving for? (textbox), Which
  account? (combo), How much do you need? (textbox), When do you need it by? (date picker), When do
  you want to start saving? (date picker) + **Start today** checkbox, How important is this? (combo,
  5 labeled priority levels), an **Advanced** expander with a savings-frequency combo (Weekly /
  Every other week / Monthly). Create / Cancel.
- Opens `CreateEarMarkPatternWindow` (the advanced path — links a plan to any existing
  Bill/Paycheck/Goal): Saving toward (combo of existing patterns), Amount per occurrence (textbox),
  Already saved — optional (textbox, default 0), embedded `RecurrenceRuleEditor`. Create / Cancel.
- Manual-adjustments sub-grid opens `ManualEarmarkWindow`: "What do you want to do?" combo (**Add
  money to a fund** / **Withdraw back to free balance** / **Move between funds**), Which fund? (combo),
  a second fund combo (Move only), On what day? (date picker), How much? (textbox), a live
  balance-context readout. Save / Cancel.

### Shared component — `RecurrenceRuleEditor`

Embedded in `CreateTransferWindow`, `CreateFinancialPatternWindow`, `CreateEarMarkPatternWindow`.
Frequency (combo: Daily/Weekly/Monthly/Yearly), Interval (textbox + unit label), "On these days" (7
weekday checkboxes), "On these day(s) of the month" (textbox), Start date (date picker), Ends: **On
date** radio + date picker OR **After this many occurrences** radio + count textbox + resolved-date
readout, a read-only RRULE-string textbox, a preview calendar, and an occurrences list.

**Six windows total:** `AccountWindow`, `CreateTransferWindow`, `CreateFinancialPatternWindow`,
`CreateOneTimeGoalWindow`, `CreateEarMarkPatternWindow`, `ManualEarmarkWindow`, plus the shared
`RecurrenceRuleEditor` control.

---

## List 2 — Every possible user action, per the design

Source: [13b](13b-user-action-catalog.md), the audited action catalog (Stage 5) — **every row that
document contains**, plus a "UI status today" column added here, plus rows 13b's own text
acknowledged conceptually but never itemized (the transfer analogues of B14/B15/C7). 13b's original
Stage/reasoning columns aren't repeated in full here — open that document for the "why" behind any
row; this table exists to answer one question per row: **does this already have a way to do it, and
where.**

**UI status legend:** ✅ built and reachable today · 🟡 mechanism built, no entry point · ⬜ not built
at all (mechanism or UI) · — out of this phase's scope, pre-existing.

### A · Accounts

| # | Action | UI status |
|---|---|---|
| A1 | Add an account | ✅ Accounts tab → `AccountWindow` |
| A2 | Rename an account | ✅ same window, Edit |
| A3 | Edit current balance | ✅ same window |
| A4 | Set/change safety cushion | ✅ same window (field exists; **no explanatory copy yet** — Stage 6, state 7a) |
| A5 | Delete an account | ✅ Accounts tab button |

### B · Bills & paychecks

| # | Action | UI status |
|---|---|---|
| B1 | Create a bill (shortcut) | ✅ "Create Bill…" |
| B2 | Create a pattern (advanced) | ✅ "Add New (advanced)…" |
| B3 | Create a one-off expense | ✅ same advanced form |
| B4 | Delete a speculative expense | ✅ "Delete Selected" |
| B5 | Change amount (plain edit) | ✅ "Edit Selected…" |
| B6 | Change schedule (plain edit) | ✅ same |
| B7 | Change priority | ✅ same |
| B8 | Change skippable/unskippable | ✅ same (advanced form only; **reachability/explanation from "Change this…" not built** — Stage 6, state 7a) |
| B9 | Change description/source | ✅ same |
| B10 | Move a pattern to a different account | ✅ same (Account combo) |
| B11 | Delete a pattern (with its plan) | ✅ "Delete Selected" + confirm |
| B12 | Say a bill/paycheck stops **on a date** / **paid off** | ✅ "When does this stop?" group |
| B12b | Say a bill/paycheck **"just keeps going"** | ⬜ text placeholder only, not a control (Cluster C) |
| B13 | Ask for a loan's payoff date | ✅ "paid off" sub-panel |
| B14 | **Break off** — change starting on a date | 🟡 `BreakOffFactory.BreakOff` — no entry point anywhere |
| B15 | **Truncate** — end at a date | 🟡 `PatternTruncation.EndOn` — no entry point |

### C · Allocations (earmark patterns)

| # | Action | UI status |
|---|---|---|
| C1 | Create a one-time goal | ✅ "Create One-Time Goal…" |
| C2 | Create an earmark pattern (advanced) | ✅ "Add New (advanced)…" |
| C3 | Change amount (plain edit) | ✅ "Edit Selected…" |
| C4 | Change schedule (plain edit) | ✅ same |
| C5 | Set a starting allocation | ✅ same (on create) |
| C6 | Delete an earmark pattern | ✅ "Delete Selected" |
| C7 | **Restructure the plan** — new rate from a date | 🟡 `RestructureFactory.Restructure` — no entry point |
| C8 | Add a second plan to one goal | ✅ rides the existing "Add New (advanced)…" window unchanged |
| C9 | **Stop contributing** early | 🟡 `RestructureFactory.StopContributing` — no entry point |
| C10 | Nudge on an over-funded goal | ⬜ `GoalShortfall.OverfundedAmount` exists; no display, no active nudge |
| C11 | Allocate toward something with no start date yet | ✅ — needs no new UI, use C1/C2 with a best-guess date |
| C12 | Set up a savings plan for an existing bill | ✅ "Set Up Savings Plan…" (hard to find — Stage 6, F12) |

### D · Manual (explicit) earmarks

| # | Action | UI status |
|---|---|---|
| D1 | Add money to a jar | ✅ two entry points — selected-day "Adjust funds…" and Allocations tab's Manual-adjustments grid |
| D2 | Withdraw from a jar to free | ✅ same dialog, "Withdraw" |
| D3 | Move money between jars | ✅ same dialog, "Move" |
| D4 | Edit/delete a manual earmark | ✅ same grid |
| D5 | Make one on a future day | ✅ same dialog |

### E · Transfers

| # | Action | UI status |
|---|---|---|
| E1 | Schedule a transfer | ✅ "Schedule Transfer…" → today's single advanced form |
| E2 | Delete a transfer | ✅ "Delete Selected" |
| E3 | "Cover $X from another account →" lever | ⬜ not built (Stage 6, item 19 — not one of the 7 states worked this session; still open) |
| E4 | Whether a transfer accrues a jar | ✅ — automatic, nothing to click |
| **E5** *(new — settled 2026-07-30)* | **Create a transfer, one button → simple form → escalate** | ⬜ today's single form doesn't match the settled 3-tier shape (Cluster B) |
| **E6** *(new)* | **Break off a transfer** — change starting on a date | 🟡 `TransferBreakOffFactory.BreakOff` — no entry point |
| **E7** *(new)* | **Truncate a transfer** — end at a date | 🟡 `TransferBreakOffFactory`/`TransferTruncation.EndOn` — no entry point |
| **E8** *(new)* | **Renew a transfer** that "keeps going" | 🟡 `TransferBreakOffFactory.Renew` — no entry point, and nothing calls it yet either way (Cluster F) |
| **E9** *(new, open question — not settled anywhere)* | **Restructure** a transfer's withdrawal-leg savings plan | **Unaudited.** `RestructureFactory` operates on any `EarMarkPattern` in principle, but whether a transfer's withdrawal plan should ever be restructured — given C1 settled it reserves immediately in full, not paced — was never explicitly asked. Flagging rather than assuming either answer. |

### F · Forecast controls & data — out of phase, all pre-existing

A1–F7 unchanged by this phase (as-of date, horizon, run/select/filter, export/import). Listed in
[13b](13b-user-action-catalog.md#f--forecast-controls--data) if needed; omitted here since nothing
here changed.

### S · Actions the system takes on its own

| # | System action | UI status |
|---|---|---|
| S1 | Reserve toward any outflow via its Allocation Plan | ✅ appears on Allocations tab immediately |
| S2 | Deallocation (drain cushion, then skippable, then unskippable) | ✅ shown in selected-day pane (locked panel) |
| S3 | Release a goal's jar on its due date | ✅ visible in the jar balance |
| S4 | Floor a jar at zero | ✅ flagged in place |
| S5 | Merge an implicit earmark into a manual one | ✅ invisible by design, `ExplicitAmount` preserves the real entry |
| S6 | Hand a jar balance across a break-off | 🟡 mechanism built (`StartingAllocation`), no confirm screen exists yet since B14 has no entry point |
| S7 | Auto-renew an "keeps going" pattern's rule | ⬜ `BreakOffFactory.Renew`/`TransferBreakOffFactory.Renew` exist; the scheduled trigger that would call them automatically does not (Cluster F) |

---

## One candidate clustering (kept from the first draft — not the only way to slice this)

This groups List 2's 🟡/⬜ rows by user-facing capability rather than by tab. Useful as a starting
point, not a conclusion.

| Cluster | Rows it covers | Touches |
|---|---|---|
| **A — "Change this…" menu** | B14, B15, C7, C9, E6, E7 (+ item 18's wording, the stale-pattern edit redirect) | Bills & Paychecks, Allocations, Transfers |
| **B — Transfer create-flow redesign** | E5 | Transfers tab |
| **C — "Keeps going" checkbox** | B12b, and the transfer-side equivalent inside Cluster B | Bill form, new transfer form |
| **D — Reviewing a proposed Allocation Plan at creation** | (Stage 2's `ProposeEmpty`, not its own row above — a creation-time affordance) | Create Bill window |
| **E — Discoverability & nudges** | A4 (copy), B8 (reachability), C10 (nudge) | Account window, Bills & Paychecks, Allocations |
| **F — Background, no screen** | S7, E8 | — |
| **G — Known small fixes / explicitly deferred here** | Item D's form-height issue; item 20's shortcut inventory; F12's remaining two entries | Bill form |
| **Unclustered / open questions** | E3 (never designed this session), E9 (never audited) | — |

---

## Other pre-multi-account (or pre-this-phase) assumptions worth a second look

The author's own example: the Forecast tab's top-controls balance/cushion summary collapses every
account into one number, a shape that predates multi-account support and may hide exactly the kind
of per-account trouble the Accounts tab now shows plainly (it already has its own Balance and Safety
cushion columns, per account — the summary may now be pure duplication, not a simplification). Other
candidates in the same spirit, found by re-reading what's open with that lens rather than assuming
the current shape is settled:

- **The cushion half of that same summary is arguably worse than the balance half.** Item C's whole
  "cushion not whole" state ([14](14-stage1-allocation-model.md#item-c--the-rulings)) is inherently
  per-account — one account's cushion can be thin while another's is fully topped up. A single
  summed cushion figure can read "fine" while one specific account is exactly the state Stage 1 built
  a warning for. This is the strongest case for breaking the summary apart, not just moving it.
- **Cluster E's "active shortcut" tension may now have an obvious resolution.** The two ideas from
  Stage 6 (a deallocation-drain event suggesting "mark this skippable?"; a negative-free-balance day
  suggesting "set a cushion?") were flagged as maybe needing to live *inside* the locked
  selected-day/overview panels, since that's where those warnings currently render. **With the top
  controls confirmed open, they have a home that doesn't touch the lock at all** — a small
  status/nudge area in the toolbar, rather than an addition to a panel whose contents are supposed to
  stay exactly as they are.
- **E3, "Cover $X from another account →"** (flagged as never-designed in List 2) is inherently a
  multi-account-era concept — it didn't exist before there was more than one account to cover from.
  Worth asking whether it belongs in the top controls (surfaced globally, account-aware) rather than
  wherever it was originally sketched.
- **The Accounts tab's own grid** has the reverse version of this question: now that per-account
  Balance and Safety cushion are already columns there, does *that* tab want a small per-account
  status indicator (⚠/◑, item C's own symbols) alongside them, rather than those symbols only ever
  appearing inside the locked selected-day view? Not the same kind of change as the toolbar items
  above (this is a grid column, not a summary), but the same underlying question: information that
  made sense as one collapsed thing before multiple accounts existed, now sitting next to a tab built
  specifically to show accounts apart from each other.

---

## Not yet done

- The author's own re-clustering of Lists 1–2, in whatever grouping is most useful for sequencing
  the work — this document supplies the raw material, not that decision.
- The "IS meant to / IS NOT meant to" treatment itself, per eventual cluster.
- E3 ("Cover from another account") and E9 (transfer plan restructuring) are genuinely unaudited —
  worth a decision before they get folded into any cluster silently.
