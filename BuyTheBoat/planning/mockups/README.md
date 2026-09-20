# Forecast tab redesign — mockups (2026-07-11)

Layout explorations for the Forecast tab redesign, produced per
the forecast-tab design (planning/08 retired → see planning/11 + the code)
(the design philosophy every variant must honor) and the mockups-first plan.
All data is representative sample data, not a live forecast.

**CHOSEN (author, 2026-07-11):**
- **Overview → B, the calendar / month grid** (days as calendar cells,
  free-balance health tints, event dots, today framed).
- **Selected day → B, the two-pane layout** — but with the panes **reversed**
  from the mockup: **"What happened today" goes on the LEFT**, the fund jars
  on the right.

The other variants remain here as the design record of what was considered.

## What's in this folder

| File | What it is |
|---|---|
| `forecast-overview-mockups.html` | **Overview region** — 4 layouts (A evolved table · B calendar grid · C horizontal river · D day-card stream) on one shared scenario, each with pros/cons. Self-contained: open in any browser; adapts to light/dark. |
| `forecast-selectedday-mockups.html` | **Selected-day region** — 3 layouts (A type-grouped sections · B two-pane · C summary-led) on one composite deallocation day. Also self-contained. |
| `overview-mockups-light.png` / `selectedday-mockups-light.png` | Full-page screenshots (light theme) — the quick-share format. |
| `overview-mockups.pdf` / `selectedday-mockups.pdf` | Print-to-PDF versions (pagination is browser-decided; the PNGs/HTML are the faithful renders). |

Hosted copies (same content, Claude artifacts):
- Overview: https://claude.ai/code/artifact/838377df-7573-4cc8-b8a1-6654ee84bb70
- Selected day: https://claude.ai/code/artifact/70988a40-85e6-4515-a87d-d610b3a25351

## The shared scenario

$2,400 as of Jun 30 · $1,700 biweekly paycheck · rent $1,200/mo · electric
$140/mo · $500 safety cushion · a $3,000 Japan-trip goal running behind · a
surprise $1,900 car repair on Jul 16 (a deallocation day) · "today" = Jul 11.
The selected-day sample is a composite deallocation day exercising every
per-type case at once: a paycheck + the allocations it funds, a covered bill,
a raided goal + cushion, and a behind long-term goal.

## What every variant must show (from the philosophy)

- Overview: per-day free + **total** allocated (no per-jar breakdown), a sense
  of money in/out, all snapshots present, attention flags, and **today
  visually distinct** (§2.g).
- Selected day: every transaction, every jar, the free amount, and Q3
  "am I on track?" answered **per expense type** (§3.III).

Once a direction is picked (one per surface, or a hybrid), implementation
proceeds per the plan's Phase I; these files stay as the design record.

## Multiple-accounts round (2026-07-22)

New explorations of the overview and selected day once multiple accounts exist
(see `Account`/`Transfer` in the code; planning/10 retired). Same
design system as above; the shared multi-account scenario is Checking / Bills /
Savings, with a car repair on Jul 16 that leaves **Checking short** while the
household total stays positive.

| File | What it is |
|---|---|
| `forecast-overview-multiaccount-mockups.html` | Overview, round 1 — 4 options (A household calendar · B calendar + account dots · C swimlanes · D calendar + filter). |
| `forecast-overview-multiaccount-mockups-2.html` | Overview, round 2 — 3 options (E rich month calendar · F agenda list · G compact free-forward), folding in the author's round-1 notes. |
| `forecast-selectedday-multiaccount-mockups.html` | Selected day, round 1 — 4 options (A stacked sections · B columns · C header + cards · D grouped two-pane). **Chosen: D.** |

**CHOSEN — overview (author, 2026-07-22): E · Rich month calendar** (from
`forecast-overview-multiaccount-mockups-2.html`). Its settled parts, all present
in every cell:

- the **account filter** (All / Checking / Bills / Savings), re-scoping every number;
- two **labeled numbers** — **Total** (money that day) and **Free**;
- the day's **top event by name** ("Paycheck", "Electric bill") — *not* an unlabeled "set aside" figure;
- a per-account **flow strip** — account letter + **↑** money in / **↓** money out / **•** no change;
- a colorblind-safe **warning** — ⚠ symbol **+ words** ("an account is short / thin"), never color alone;
- an explicit **event count** ("3 events" / "No events") kept **top-right**, not dots.

**CHOSEN — selected day (author, 2026-07-22): D · Grouped two-pane** (from
`forecast-selectedday-multiaccount-mockups.html`). Keeps the single-account
two-pane — **what happened today** (left) | **fund jars** (right) — but **groups
both panes by account**; a short account's "Cover from another account →" lever
sits in its group. **Why:** the other options (stacked sections, columns, cards)
gave every account equal space, but in practice one or two accounts carry most of
a day's activity — grouping the two-pane by account gives each account exactly the
space it needs, and stays closest to the two-pane already chosen for one account.

Full write-up of both chosen directions and the refinement reasoning:
[`../21-forms-and-ui.md`](../21-forms-and-ui.md).
