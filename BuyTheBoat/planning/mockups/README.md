# Forecast tab redesign — mockups (2026-07-11)

Layout explorations for the Forecast tab redesign, produced per
[`../08-forecast-tab-design-philosophy.md`](../08-forecast-tab-design-philosophy.md)
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
