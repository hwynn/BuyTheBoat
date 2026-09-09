# 13b — User action catalog

**AUDITED — Stage 5, 2026-07-30.** Rows are complete; cells get filled as stages settle them. A row
marked **✔** in the Stage column has been answered by that stage; a bare stage number with no ✔ means
a genuine, still-open gap (not a guess); **out of phase** means the action predates and is untouched
by "Adjusting the Plan," settled in an earlier phase. Every blank cell in this document has now been
classified as one of those; the consolidated list of what's designed-but-unbuilt (mostly UI
wiring) is the UI backlog in [13](13-adjusting-the-plan-charter.md#the-uiwiring-backlog).

This is [charter](13-adjusting-the-plan-charter.md) item 7: *every action a user can take, what we
do implicitly in response, and what explicit choice that leaves them.* It is too big to answer in
one sitting, so it is built the other way round — the **rows** are enumerated once, up front (this
document), and the **cells** get filled in by whichever stage answers them. Stage 5 audits the
result; at that point **every remaining blank is either a gap to design or an explicit deferral**,
and neither can hide.

**The rows below are the real surface**, read off `MainWindow.xaml` and the create/edit windows as
of 2026-07-23 — not an imagined inventory — plus the actions stages 2–4 propose to add, marked
*(proposed)*.

### The columns, and what belongs in each

| Column | What goes here |
|---|---|
| **Implicit response** | What the system does on its own — jars created/destroyed, earmarks written, occurrences regenerated, other patterns touched. |
| **What we ask** | The explicit question(s) put to the user at the time, if any. |
| **Left to the user** | What they must go do themselves afterward, and where. |
| **Warn about** | The state this can leave them in that is worth flagging (feeds stage 6). |
| **Stage** | Which stage answers this row. Pre-filled where the charter already assigns it. |

**Filling rule:** a stage fills only the rows it actually decided. Guessing a cell to make the table
look finished defeats the audit.

---

## A · Accounts

**A1–A3, A5 audited (Stage 5, 2026-07-30): out of this phase's scope, not gaps.** Plain account CRUD,
fully settled by the earlier multiple-accounts phase (built; see the account/persistence code) — nothing in
"Adjusting the Plan" touches a plain rename, balance edit, or delete. The dash in Stage was never a
gap; it means "answered before this phase existed."

| # | Action | Implicit response | What we ask | Left to the user | Warn about | Stage |
|---|---|---|---|---|---|---|
| A1 | Add an account | none beyond creating the row | name (unique), starting balance | — | — | out of phase — multi-account phase (built) |
| A2 | Rename an account | none — patterns reference the account by id, never by name | the new name | — | — | out of phase — multi-account phase (built) |
| A3 | Edit an account's current balance | none — takes effect on the next forecast run (W4: nothing is cached) | the new balance | — | — | out of phase — multi-account phase (built) |
| A4 | Set / change an account's safety cushion | reserved ahead of every jar and drained before them; **setting one above 0 also activates the middle warning state** ([14 item C](14-stage1-allocation-model.md)) — at 0 that state never fires | the cushion amount | — | days where the cushion is not whole | 1 ✔ |
| A5 | Delete an account | blocked while it holds patterns or transfers (an app-level delete guard) | — | reassign or remove them first | — | out of phase — multi-account phase (built) |

## B · Bills & paychecks (finance patterns)

| # | Action | Implicit response | What we ask | Left to the user | Warn about | Stage |
|---|---|---|---|---|---|---|
| B1 | Create a bill (the shortcut form) | a jar that fills automatically by the standing rule; **no savings plan is created** ([14 item D](14-stage1-allocation-model.md)) | nothing about savings | press "set up a savings plan" on the pattern later, if they want to control the filling | — | 1 ✔ |
| B2 | Create a pattern (advanced form) | a jar that fills automatically by the standing rule; **no savings plan is created** ([14 item D](14-stage1-allocation-model.md)) | skippable / unskippable (hidden for income) | press "set up a savings plan" on the pattern later, if they want to control the filling | — | 1 ✔ |
| B3 | Create a one-off expected expense ("buy a TV") | a jar that fills automatically by the standing rule; **no savings plan is created** ([14 item D](14-stage1-allocation-model.md)) | nothing about savings | press "set up a savings plan" on the pattern later, if they want to control the filling | — | 1 ✔ |
| B4 | Delete a speculative expense again | removed outright — nothing else was created, so nothing blocks it | — | — | — | 1 ✔ |
| B5 | Change a pattern's **amount** | **a plain, uniform edit** — applies retroactively across the whole pattern's history and future (Constraint 1/W4), the same as it always has (**F24**, [16](16-stage3-break-off.md)); a genuinely forward-only rate change is B14 instead, a separate deliberate action | — | if a forward-only change was wanted, use B14 | — | 3 ✔ |
| B6 | Change a pattern's **schedule** | same as B5 — a plain, uniform edit (**F24**); B15 (truncate) or B14 (break off) are the deliberate, forward-only actions | — | — | — | 3 ✔ |
| B7 | Change **priority** | **no retroactive effect** — verified in code (`TransactionLogBookFactory` reads `Priority` off the *current* pattern object when building each day; nothing is cascaded or stored from a previous run, [16](16-stage3-break-off.md)) — takes effect in the next deallocation ordering only | — | — | — | 1 ✔ |
| B8 | Change **skippable / unskippable** (was "mandatory") | changes deallocation ordering — unskippable jars are drained only after every skippable one is empty ([14 item B](14-stage1-allocation-model.md)); same "no retroactive effect" guarantee as B7, verified together | a plain radio pair, advanced form only; hidden for income; defaults to "I have to pay this" | — | — | 1 ✔ |
| B9 | Change **description / source** | a plain, uniform edit — a label correction, never baked into allocation math (**F24**'s same taxonomy) | — | — | — | 3 ✔ |
| B10 | Move a pattern to a different account | jars recompute fresh — nothing is stuck in the old silo | — | — | **an existing plan doesn't auto-adjust to the new account's income** — if it was paced against the old account, "Restructure the plan" (B14/C7) is how to re-pace it (F33) | 4 ✔ |
| B11 | Delete a pattern | **its savings plan is removed with it** — was blocked, now confirms first ([14 item D-2](14-stage1-allocation-model.md)) | "this will also remove its savings plan — continue?" | — | — | 1 ✔ / 3 |
| B12 | Say when a bill or paycheck stops | the *on a date* and *paid off* answers set a real end date; **ongoing deferred** 2026-07-28 (was: a hidden horizon-extended end date propagated to the savings plan) — [15](15-stage2-pattern-lifetime.md) | one question; **two answers built** (on a date / paid off), the "keeps going" third stubbed pending the deferred design; no category names | — | — | 2 (partial — genuine gap, not audited away: the ongoing mechanism's UI trigger is still undesigned) |
| B13 | Ask for a loan's payoff date | computes owed ÷ payment, rounded up, and sets it as the end date — a one-time estimate that does **not** re-derive if the payment changes | total owed, regular payment | re-run it themselves if the payment changes | the date is a **floor** — interest and fees push it later | 2 ✔ |
| B14 | Change a pattern **starting on a date** — break off | truncates the predecessor (new `Until` = day before the cut, kept not deleted); creates a genuinely new pattern (new `finance_id`) continuing from the cut date; its savings plan is **freshly proposed**, never copied, seeded with the jar balance carried across via `StartingAllocation` ([16 item 4](16-stage3-break-off.md)) | the new amount/schedule from the cut date forward, pre-filled by the proposer, editable before confirming | review the pre-filled confirm screen | for a paycheck, other bills' plans paced against it don't auto-adjust (deferred to Stage 6) | 3 ✔ (domain; **UI not wired in**) |
| B15 | End a pattern at a date — "cancel Netflix next month" | sets the pattern's `Until`; its savings plan (if any) truncates to match, never later; whatever was in the jar becomes ordinary free balance automatically — no jar destruction, just containment ([16 item 16](16-stage3-break-off.md)) | the end date | — | whether the user is *told* the freed amount is deferred to Stage 6 | 3 ✔ (domain; **UI not wired in**) |

## C · Allocations (earmark patterns)

| # | Action | Implicit response | What we ask | Left to the user | Warn about | Stage |
|---|---|---|---|---|---|---|
| C1 | Create a one-time goal (the shortcut form) | creates goal + savings plan as one action (`OneTimeGoalFactory`) — **not yet unified onto `AllocationPlanProposer`**, a known open refinement from Stage 1 ([14](14-stage1-allocation-model.md)), still outstanding | description, amount, due date, start date, priority | — | — | 1 (partial — genuine gap: `OneTimeGoalFactory`/proposer unification still open) |
| C2 | Create an earmark pattern (advanced form) | creates a new `EarMarkPattern` linked to the chosen goal by `finance_id`; **checked 2026-07-30: no restriction against a goal that already has one** — this is item 9's actual entry point, confirmed working end to end once F27/F34's relaxation landed | goal, amount, schedule, starting allocation | — | — | 1 ✔ / 4 ✔ |
| C3 | Change an earmark pattern's **amount** | **a plain, uniform edit**, via the existing "Edit Savings Goal" window — the same taxonomy as B5/B6, one level down (data-entry correction, applies uniformly); a genuinely forward-only rate change is C7 ("Restructure the plan") instead | — | if a forward-only change was wanted, use C7 | — | 4 ✔ |
| C4 | Change an earmark pattern's **schedule** | same as C3 — a plain, uniform edit | — | — | — | 4 ✔ |
| C5 | Set a starting allocation on a new plan | seeds the jar before the pattern's own occurrences run (`StartingAllocation`) — confirmed this is exactly why `RestructureFactory`'s successor (items 8/22) deliberately leaves it at 0 (F28: same `finance_id`, the cascade already carries the balance forward) | the amount | — | — | 1 ✔ |
| C6 | Delete an earmark pattern | the jar **returns to filling automatically** — the outflow rejoins the automatic rule it had opted out of | — | — | — | 1 ✔ / 3 |
| C7 | Change an allocation **starting on a date**, bill unchanged — "Restructure the plan" | truncates the current plan (kept, not deleted, same `finance_id` — **F27/F28**) and creates a new one from the cut date at the **user's own specified rate** (no re-proposal, item 8-C); no jar hand-off needed — same `finance_id` means the ordinary cascade carries the balance across for free (`RestructureFactory.Restructure`, [17](17-stage4-allocation-only-changes.md#item-8--restructure-the-plan--built-2026-07-30)) | the new amount/schedule from that date | — | — | 4 ✔ (domain; **UI not wired in**) |
| C8 | Add a **second** earmark pattern to one goal | **now allowed** (F27's relaxation of `3.11.1.a1`) — the shared jar aggregates every pattern's events (F34); concurrent same-day occurrences merge into one, summing amounts (F30); rides the **existing** C2 window unchanged, no new UI | — | — | none surfaced yet — the narrow same-identical-start-date edge case (F37) is accepted, not user-facing | 4 ✔ |
| C9 | Stop contributing early to a goal already met — "Restructure the plan," targeting zero | truncates the current plan and replaces it going forward with an empty (zero-rate) continuation whose active span reaches the goal's own due date — the jar stays alive, un-added-to, releasing on schedule exactly as before (**F31**, `RestructureFactory.StopContributing`, [17](17-stage4-allocation-only-changes.md#item-22--a-goal-met-early--built-2026-07-30-domain-ui-not-wired-in)) | the cut date | resuming later is just C7 again, with a real rate | should the user be *told* their goal is ahead of schedule? — deferred to Stage 6 | 4 ✔ (domain; **UI not wired in**) |
| C10 | Defer contributions on an over-funded jar | **mechanism is C9's** (F31); **detection now exists** — `GoalShortfall.OverfundedAmount`, correct for a one-time goal, gross-only for a repeating one ([17](17-stage4-allocation-only-changes.md#item-24--deferring-allocations--detection-built-2026-07-30-one-time-goals-mechanism-is-item-22s-nudge-is-stage-6s)) — but **nothing acts on the signal yet**: no system-triggered offer exists | n/a yet | currently must notice themselves and use C9 | this whole row **is** the future warn-about — feeds Stage 6 | 4 (partial — detection built; system-triggered entry point + nudge still open) |
| C11 | Allocate toward something that hasn't started yet | **no new mechanism** — author's ruling: create the real goal now with a best-guess date, `ActiveFrom` reaching back to today; correcting the guessed date later is a **plain edit** (F24), not a "Restructure" ([17](17-stage4-allocation-only-changes.md#item-23--allocating-toward-something-that-doesnt-exist-yet--settled-2026-07-30)) | the ordinary creation questions, same as C1/C2 | edit the date later once the real start is known | a reminder to firm up the date is Stage 6's | 4 ✔ (design only — needs no build; already usable with existing tools) |
| C12 | **Set up a savings plan for an existing bill or expense** *(new — [14 item D-1](14-stage1-allocation-model.md))* | creates a real savings plan seeded from where the jar already stands, so no money moves; the outflow stops filling automatically and follows the plan instead | the plan's amount and schedule | — | the plan is now theirs to keep on track — it no longer adapts on its own | 1 ✔ |

## D · Manual (explicit) earmarks

Rulings already settled (in the code — `ManualEarmark` / `EarmarkFormPanel`'s one-off mode) — **do not re-litigate**;
these rows record what the *system* does around them. **Audited (Stage 5, 2026-07-30) — closes
charter item 21** ("where does a user make an explicit earmark, and is that discoverable").
**Answer: two independent, already-built entry points** — contextual creation from the selected-day
pane's "Adjust funds…" button (pre-filled with that date), and a management grid ("Manual
adjustments") on the Allocations tab, both driving the same Add/Withdraw/Move dialog. Discoverable
from wherever the user is already looking (a specific day, or the allocations list), not buried
behind one path only.

| # | Action | Implicit response | What we ask | Left to the user | Warn about | Stage |
|---|---|---|---|---|---|---|
| D1 | Add money to a jar | pre-as-of folds into the jar's seed; in-window becomes an isolated event | action, jar, amount, date (pre-filled from the selected day, if entered that way) | — | over-add allowed but warned (ruling 3) | 5 ✔ |
| D2 | Withdraw from a jar to free | same dialog, action = Withdraw | action, jar, amount, date | — | over-withdrawal blocked (ruling 2) | 5 ✔ |
| D3 | Move money between jars | composed as −A / +B on one day | source jar, target jar, amount, date | — | net-zero on free, so the over-add warning can't apply | 5 ✔ |
| D4 | Edit / delete a manual earmark | the same dialog, pre-filled for editing; deleting just removes the row | same fields as D1–D3 | — | a stored withdrawal can later be floored if data drifted; flagged in place | 5 ✔ |
| D5 | Make one on a future day with no snapshot yet | stored regardless — applies once the forecast reaches that day; no snapshot needs to exist yet at creation time | same dialog | — | — | 5 ✔ |

## E · Transfers

**E1/E2 audited (Stage 5, 2026-07-30): out of phase**, settled by the multiple-accounts phase (transfers, built) — Stage 3's
`TransferBreakOffFactory` (E-row candidates would be B14/B15's transfer analogue) is a genuinely
different action, not these.

| # | Action | Implicit response | What we ask | Left to the user | Warn about | Stage |
|---|---|---|---|---|---|---|
| E1 | Schedule a transfer | creates a withdrawal + a deposit pattern + a `Transfer` record; both hidden from lists | from, to, amount, schedule | — | — | out of phase |
| E2 | Delete a transfer | removes both patterns | — | — | — | out of phase |
| E3 | Use the "Cover $X from another account →" lever | pre-fills a one-time transfer into the short account for exactly the shortfall | the user still presses Create | — | — | 6 |
| E4 | Whether a scheduled transfer accrues a jar | **the from-account reserves along the A/B ramp; the household view adds it back into free** ([14 item A-1](14-stage1-allocation-model.md)) | — | — | — | 1 ✔ |

## F · Forecast controls & data

**Audited (Stage 5, 2026-07-30): all out of this phase's scope.** Forecast controls and data
import/export are untouched by "Adjusting the Plan" — every row below was already working before
Stage 0 started and no stage 1–4 item bears on any of them.

| # | Action | Implicit response | What we ask | Left to the user | Warn about | Stage |
|---|---|---|---|---|---|---|
| F1 | Change the as-of date | every account's cascade reseeds from it (W4) | the new date | — | — | out of phase |
| F2 | Change the horizon | recomputes the window's end | the new date | — | — | out of phase |
| F3 | Run a forecast | rebuilds the whole onion in memory (W4) | — | — | — | out of phase |
| F4 | Select a day | shows that day's detail pane | — | — | — | out of phase |
| F5 | Filter the overview to one account | re-scopes every number in the calendar | which account | — | — | out of phase |
| F6 | Export the forecast as a spreadsheet | writes the Timeline + Goals sheets from the current `ForecastResult` | — | — | — | out of phase |
| F7 | Export / import the data file | wholesale file copy; migrations run on the next start | — | — | — | out of phase |

---

## Actions the system takes that no user action triggers

The other half of item 7 — and the reason item 15 (catalog every implicit earmark) exists. Same
filling rule.

**Audited (Stage 5, 2026-07-30) — closes charter item 15.** Every row now has a filled "what the user
sees" cell. **S1's own content was stale** — it described W6's computed A/B ramp, which Stage 1's
2026-07-24 revision retired; corrected below rather than left to mislead, per the "superseded wording
struck and labelled, not deleted" convention.

| # | System action | When it fires | What the user sees | Can they override it? | Stage |
|---|---|---|---|---|---|
| S1 | Reserve toward **any outflow**, via its Allocation Plan | at creation time (a proposer picks a default plan, scoped to the outflow's own account — F33); every day thereafter via that plan's own scheduled contributions. ~~W6's computed A/B ramp~~ — **retired, Stage 1 revision 2026-07-24**: every reservation is now a real, persisted `EarMarkPattern`, not a hidden per-day curve | the proposed plan appears on the Allocations tab immediately | yes — edit the plan's amount/schedule, or remove it (the outflow then just shows short) | 1 ✔ |
| S2 | Deallocation — drain the cushion, then every **skippable** jar, then unskippable ones, by priority within each group | when spending overdraws free funds | the day's detail pane shows which jars were drained (a "Deallocation"-labeled event), and separately whether the safety cushion dipped below target (item C's quieter, distinct symbol) | not the drain itself — but priority and skippable/unskippable are user-set, and decide the order | 1 ✔ |
| S3 | Release a goal's jar on its own occurrence | on the goal's due date | the jar's balance visibly drops to (or toward) zero that day, alongside the goal's own transaction | no — this is the goal being paid, nothing to override | 1 ✔ |
| S4 | Floor a jar at zero | any day a jar would go negative | flagged in place | no — it's a floor, not a choice; the underlying manual earmark or plan can be edited afterward | 5 ✔ |
| S5 | Merge an implicit earmark into a manual one | same jar, same day | one combined amount — the merge itself is invisible as a separate event; `ExplicitAmount` preserves what the user actually entered underneath | no — automatic, by design (`3.13c.8.4.a2`) | 1 ✔ |
| S6 | Hand a jar balance across a break-off | at the moment a break-off ("Change starting on a date," B14) is confirmed | the confirm screen shows the carried-over balance, labelled "as currently projected" (future cut) or presented plainly (past cut, no locked ledger to defer to) — [16 item 4-B](16-stage3-break-off.md) | yes — shown before confirming, editable like the rest of the successor's plan | 3 ✔ (mechanism; **UI not wired in**) |
| S7 | Auto-renew an open-ended bill's rule | on a fixed cadence, once every `SegmentYears` from the pattern's original start ([16, `BreakOffFactory.Renew`](16-stage3-break-off.md)) | nothing at the moment it happens — silent by ruling — but a plain "(renewed *date*)" trace is appended to the pattern's `Description` afterward, visible on inspection | not the renewal itself (nothing to decide, per the author's ruling) — the renewed segment can be edited afterward like any pattern | 2 ✔ (mechanism; **the scheduled trigger that fires it is not wired into the app**) |

**Item 15 is now complete** — every row has a filled "what the user sees" cell, closing the
completeness criterion this table itself set.
