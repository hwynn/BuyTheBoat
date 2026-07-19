# 09 — Manual (explicit) earmarks

Design record for user-created isolated earmarks — the first of two
prerequisites for Q4 ("how do I readjust a goal that's fallen behind?"; the
second, restructuring an earmark pattern's rrule/amount, is its own future
design). Decisions here were made with the author 2026-07-12.

**STATUS: IMPLEMENTED (2026-07-12).** `ManualEarmark` (Domain, span-validating
`Create`), `ManualEarmarks` table + `ManualEarmarkRepository` (the rewrite's
first persisted event; dies with its pattern), `ForecastOptions.ManualEarmarks`
folded into the cascade (seed for ≤ as-of, isolated events with
`ExplicitAmount` in-window, `ForecastResult.FlooredManualEarmarks` for the
flag-in-place UI), `ManualEarmarkWindow` (Add/Withdraw/Move dialog with live
day balances, blocked over-withdrawals, warned over-adds), the selected-day
"Adjust funds…" button, the violet MANUAL chip + amber floored warning +
calendar attention tint, and the Allocations tab's "Manual adjustments" grid.
Verified end-to-end; suite at 112 (93 domain + 19 scenario).

**The feature in one sentence:** on top of the automatic allocation an
`EarMarkPattern` provides, the user can explicitly add money to a fund jar on
a chosen day, pull money back out to the free balance, or move money between
jars — giving fine manual control of the jars.

## Why the original design already supports this

Isolated earmarks with user-owned amounts are documented machinery, not an
invention of this rewrite:

- *"Every earmark needs a date, which is what lets them be **created manually
  at will**."* (`01-glossary-of-terms.md`, `EarMarkEvent.earmark_date`)
- *"false for **manual** or implicit... earmarks"* (`repeated_earmark`).
- **`8.4.a2`**: *"expected_amount must be = an explicitly given amount from
  user if repeated_earmark is false on a normal day."*
- **`3.13.8.5.a1`**: *"explicit_amount must be = an explicitly given amount
  from user"* — the field exists to keep the user's portion separate from
  system adjustments (`reverse_implicit_earmarks` machinery).
- *"**A negative amount pulls money out of the jar into free balance.**"*
  (glossary, `expected_amount`) — manual withdrawals are explicitly sanctioned.
- **`3.13.8.1.a1`** + the merge rule (*"a second isolated earmark for the same
  finance_id/day just adds onto the first's amount"*) — one manual earmark per
  jar per day; coexists with that day's repeated earmark.
- **`3.13c.8.4.a2`**: on a deallocation day the isolated earmark's expected
  amount = *"an explicitly given amount from user plus
  ?deallocation_implicit_amount?"* — the model anticipated system give-backs
  merging into a user's manual earmark, with `explicit_amount` preserved.
- The famous *"we will **never implicitly** add to a fund jar's expected
  amount"* rule hinges on *implicitly* — user-explicit additions are the
  sanctioned positive path that rule protects.

There is **no transfer primitive** anywhere in the model. A jar-to-jar move is
a composition: a negative manual earmark on the source + a positive one on the
target, same day. Nothing forbids it (the "first come first served / won't
draw on other jars" rule constrains *implicit* behavior only), so the UI's
"Move" action is sugar over that pair.

## Author rulings (2026-07-12 — do not re-litigate)

1. **The earmark pattern's span IS the fund jar's lifetime.** Manual earmarks
   are only valid on days within the pattern's `[Start..Until]`
   (`3.13.8.a1`/`3.13.8.a2` read strictly), enforced by both the UI and the
   domain type.
2. **Jars never go below 0** — a user must not "create" money from nothing.
   Withdrawals/moves exceeding the jar's balance on that day are **blocked**.
3. **Over-adds warn but are allowed**: adding more than that day's free
   balance over-commits the plan (free goes negative — the same honest signal
   an unaffordable bill gives), and the deallocation engine will claw the
   excess back from the lowest-priority jars on the next event day, possibly
   not the jar that was added to. The dialog says so and lets the user
   proceed (they may know money is coming the forecast doesn't).
4. **Move is first-class** in the dialog (not two-step-only).
5. **UI lives in both places**: contextual creation from the selected-day
   pane, and a management grid on the Allocations tab.

## Possibility matrix

**Possible:**

| Action | Rules |
|---|---|
| Add to a jar | Any earmarked goal, any day within its pattern span. Days ≤ the as-of date fold into the jar's seed value (like `StartingAllocation`, but dated); days past the current horizon are stored and apply when a forecast reaches them. |
| Withdraw to free | Same day rules; amount ≤ the jar's balance on that day (ruling 2). |
| Move A → B | Two earmarks on one day (−A, +B); both jars alive that day; source covers the amount. Net-zero on free, so the over-add warning can't apply. |
| Edit / delete | Manual earmarks are the user's persisted data, managed like patterns. |
| Same-day coexistence | One repeated + one isolated earmark per jar per day (documented layout). A second manual entry on an occupied day edits/merges into the existing one (DB PK enforces). |
| Deallocation-day merge | A system give-back merges into the manual event: `ExpectedAmount` = explicit + implicit, `ExplicitAmount` stays the user's number (3.13c.8.4.a2). |

**Not possible:**

| Forbidden | Why |
|---|---|
| Jar below 0 | Ruling 2 — no money from nothing. Blocked at entry; the cascade's `max(0, …)` floor is the mechanical backstop. |
| Days outside the pattern span | Ruling 1 (3.13.8.a1/a2) — the jar doesn't exist there. |
| Jars with no `EarMarkPattern` | Auto-bill reserve jars and the safety cushion are system-managed (the cushion's amount field is its control; a manual add above the cushion's firm target would be undone by the next fill delta anyway). Honors 3.13.8.a1 strictly — no new divergence. |
| Editing repeated (pattern-generated) events | Immutable per the docs; changing the plan itself is the restructuring feature (next design). |
| Milestone movement | A manual catch-up closes the gap to the milestone — it does not redefine it. `milestone_amount` counts scheduled repeated contributions only (3.13.5.4.a1). |

## Conservation argument

No path through this feature creates money:

- A **withdrawal** only delivers what the jar actually holds — the cascade
  floor (`max(0, prev + events)`) under-delivers an oversized pull, and the
  free balance is *derived* from the jar totals, so free only gains what
  actually left the jar.
- An **add** only re-labels free money as set-aside. If free can't cover it,
  free goes negative (over-committed — an honest signal, not counterfeit
  money) and deallocation restores the invariant on the next event day.

Validation is therefore about **UX honesty**, not conservation — the math is
safe even when data drifts.

## Validation policy

**At entry (dialog, against the currently-shown forecast):**
- Withdraw / Move-source amount > that day's jar balance → **blocked**.
- Add amount > that day's free balance → **warning, allowed** (ruling 3).
- Date picker constrained to the chosen jar's pattern span (Move: the
  intersection of both spans); jar picker only offers jars alive on the date.
- Amount must be nonzero; the domain type also enforces the span, so nothing
  invalid can be constructed even bypassing the UI.

**After the fact (data drifts):** editing a pattern, the balance, or another
earmark can make a stored withdrawal oversized for its day. The cascade floors
it (safe); the engine reports each clamped user event
(`ForecastResult.FlooredManualEarmarks`), and the UI **flags in place**: the
day gets the calendar attention tint, and the earmark's row in the day detail
gets the warning style ("only $X could be moved"). A dedicated all-problems
list (e.g. on the Allocations tab) is a possible follow-up for very long
horizons where a flagged month may never be scrolled to.

## UI flows

- **Create from the day** (primary flow): select a day in the calendar → the
  selected-day pane's **"Adjust funds…"** button opens the dialog pre-filled
  with that date. Dialog: action (Add / Withdraw / Move), jar, amount, target
  jar (Move), and a live line showing the day's jar balance and free amount.
  OK saves and re-runs the forecast.
- **Manage from the Allocations tab**: a "Manual adjustments" grid (Date,
  Target, Amount) with Add / Edit / Delete — the same dialog, blank.
- **Rendering**: manual events get a distinct **MANUAL** chip in the day
  detail ("PULLED" stays deallocation-only); floored ones get the warning
  style; the calendar tints affected days.
- **Q4 tie-in (future)**: the "Adjust the plan →" nudge on a behind goal will
  pre-fill this dialog (add funds to catch up) and/or the restructuring flow.

## Architecture note

This is the rewrite's **first persisted event** (until now: patterns + one
balance row). `ManualEarmarks` table (PK `(FinanceId, EarmarkDate)` = the
one-per-day rule), `ManualEarmarkRepository`, `ForecastOptions.ManualEarmarks`,
and the factory folds them in — pre-as-of into the seed, in-window as ordinary
isolated `EarMarkEvent`s with `ExplicitAmount` set, which the existing
snapshot/deallocation/merge machinery handles unchanged. Export/Import's
byte-for-byte file copy carries the table automatically.
