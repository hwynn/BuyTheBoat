# Manual (explicit) earmarks — design doc + implementation

## Context

First of two prerequisites for Q4 ("adjust a goal that's fallen behind"):
users need fine, explicit control of fund jars on top of the automatic
earmark-pattern plans — add $300 to the Japan jar on a chosen day, pull money
back to free, move money between jars. The original design supports this
directly (isolated earmarks with user-owned `explicit_amount` are documented
machinery — 8.4.a2, 3.13.8.5.a1, 3.13.8.1.a1, 3.13c.8.4.a2, and the glossary's
"created manually at will" / "a negative amount pulls money out of the jar
into free balance"). Author rulings this session: **the earmark pattern's span
IS the jar's lifetime** (manual earmarks only on days the jar exists, enforced
in UI and domain); **jars never go below 0** (no money from nothing);
**over-adds warn but are allowed** (free goes negative — the same honest
over-committed signal bills give; deallocation claws back from lowest-priority
jars); **Move between jars is first-class**; **UI = both** contextual creation
from the selected day and a management grid on the Allocations tab.

This is also the rewrite's **first persisted event** (everything stored today
is patterns + one balance row) — a deliberate architectural step; Export/Import
(byte-copy) carries it automatically.

## Step A — Design doc: `planning/09-manual-earmarks.md`

Written first, as the durable record (like 06/07/08). Contents:

**Possibility matrix.** POSSIBLE: add to any earmarked goal's jar on any day
within its pattern's [Start..Until] (incl. days ≤ as-of → fold into the seed
like `StartingAllocation`; days past the current horizon → stored, apply when
the horizon reaches them); withdraw back to free (≤ that day's jar balance);
Move A→B (two earmarks, same day, both jars alive, source covers it; net-zero
on free so no over-add warning); edit/delete; one manual earmark per
(finance_id, day) — a second merges per the documented rule (UI edits the
existing one; DB PK enforces); coexists with the repeated earmark that day; on
a deallocation day the system give-back merges INTO it (expected = explicit +
implicit per 3.13c.8.4.a2, `ExplicitAmount` stays the user's number — the
docs anticipated exactly this). NOT POSSIBLE: jar below 0; days outside the
pattern span (3.13.8.a1/a2, author-ratified); jars with no `EarMarkPattern` —
auto-bill reserve jars and the safety cushion are system-managed (cushion is
controlled by its amount field; this honors 3.13.8.a1 strictly, no new
divergence); editing pattern-generated (repeated) events — that's feature #2
(plan restructuring).

**Conservation argument** (why no path creates money): a withdrawal delivers
min(requested, jar) via the cascade floor; an add only re-labels free money
(free can go negative = over-committed; next event day's deallocation claws
back). Validation is therefore UX honesty, not conservation.

**Validation policy.** At entry: withdraw/Move-source exceeding that day's jar
balance → **blocked**; add exceeding that day's free → **warn + allow** (with
the claw-back consequence explained); date constrained to the chosen jar's
span. After the fact (data drifts — a pattern edit can invalidate a stored
withdrawal): the cascade floors it (safe), and the UI **flags in place** —
the day gets the calendar attention tint and the earmark's detail row gets a
warning style ("only $X could be moved"). (A dedicated warnings list is noted
as a follow-up for very long horizons.)

**UI flows** (per the section below) + the Q4 tie-in: the "Adjust the plan →"
nudge will later pre-fill this dialog — noted, not built.

## Step B — Domain + persistence

- **`ManualEarmark`** (new, `MyMoneyForecast.Domain`): record with `FinanceId`,
  `Date`, `Amount`; `Create(options, EarMarkPattern pattern)` validates amount
  ≠ 0 and date within `pattern.DatePattern.Start..Until` (same
  validate-against-partner shape as `EarMarkPattern.Create(options, goal)`).
  Balance-dependent checks stay UI-side (they're dynamic).
- **Persistence**: `ManualEarmarks` table — `FinanceId INTEGER NOT NULL
  REFERENCES EarMarkPatterns(FinanceId), EarmarkDate TEXT NOT NULL, Amount
  TEXT NOT NULL, PRIMARY KEY (FinanceId, EarmarkDate)` (PK = the one-per-day
  merge rule). `ManualEarmarkRepository` (GetAll/Upsert/Delete); deleting an
  earmark pattern also deletes its manual earmarks (repository-level, matching
  how pattern deletion is handled today). Migration via the existing
  `CREATE TABLE IF NOT EXISTS` block in `PatternDatabase.Initialize`.
- **Engine** (`TransactionLogBookFactory`): `ForecastOptions.ManualEarmarks`
  (default `[]`). Dates ≤ AsOfDate fold into the seed jar value (alongside
  `StartingAllocation` + scheduled contributions in the seeding loop);
  **milestones are NOT moved** (3.13.5.4.a1 counts scheduled/repeated
  contributions only — a manual catch-up closes the gap to the milestone, it
  doesn't redefine it). Dates in (AsOfDate..Horizon] become isolated
  `EarMarkEvent`s (`RepeatedEarmark=false, ExpectedAmount=amount,
  ExplicitAmount=amount`) in `earmarkEventsByDate` — snapshot creation,
  deallocation inputs (`er+ei`), the floor loop, and give-back merging
  (`MergeOrAppendIsolatedEarmark` preserves `ExplicitAmount` via `with`) all
  work unchanged. NEW: during the floor loop, when a user event
  (`ExplicitAmount != 0`) gets clamped, record it — surfaced as
  `ForecastResult.FlooredManualEarmarks: IReadOnlyList<(DateOnly, int)>` for
  the flag-in-place UI.
- **Tests** (Domain.Tests + Scenario.Tests): span validation; jar rises on the
  earmark day; pre-as-of folds into seed; milestone unaffected; withdrawal
  reduces jar / floors + reports when oversized; Move = net-zero free; merge
  with a deallocation give-back keeps `ExplicitAmount`; repository round-trip,
  PK upsert, delete-with-pattern.

## Step C — UI

- **Dialog** `ManualEarmarkWindow` (new, matches Create*Window conventions):
  action (Add / Withdraw / Move), jar picker (earmarked goals whose span
  covers the date), date picker (constrained to the chosen jar's span; for
  Move, the intersection of both spans), amount, target-jar picker (Move
  only), and a live info line from `_lastForecast` — that day's jar balance +
  free. Blocks oversized withdrawals/moves; warns on over-adds. OK → upsert +
  re-run the forecast.
- **Selected-day pane**: an "Adjust funds…" button beside FREE TO SPEND opens
  the dialog pre-filled with the selected date. Manual events render with a
  distinct **MANUAL** chip (new ChipKind; PULLED stays deallocation-only);
  floored ones get the warning style + "only $X could be moved".
- **Calendar**: days with a floored manual earmark get the attention tint
  (feed `FlooredManualEarmarks` into `BuildCalendarMonths`/`DayCellRow`).
- **Allocations tab**: a "Manual adjustments" grid (Date, Target, Amount) with
  Add/Edit/Delete buttons, mirroring the pattern grids' conventions.
- Update `planning/08`'s code map + the mockups README is unaffected.

## Step D — Verification

- `dotnet build` 0 warnings; suite green (99 + new).
- UIAutomation drive (crafted DB, backup/restore): open the dialog from a
  selected day, add $300 to the goal jar → jar bump visible in the calendar
  cell + detail row + MANUAL chip; attempt an oversized withdrawal → blocked;
  create a valid withdrawal → free rises that day; Move between two goals →
  net-zero free, both jars change; screenshot. Update memory + `07`/roadmap
  docs (feature #2, plan restructuring, stays queued next).
