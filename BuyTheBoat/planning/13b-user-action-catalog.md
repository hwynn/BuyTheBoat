# 13b — User action catalog

**IN PROGRESS.** Rows are complete; cells get filled as stages settle them. A row marked **✔** in the
Stage column has been answered by that stage; `—` means genuinely not yet decided, never a guess.

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

| # | Action | Implicit response | What we ask | Left to the user | Warn about | Stage |
|---|---|---|---|---|---|---|
| A1 | Add an account | — | — | — | — | — |
| A2 | Rename an account | — | — | — | — | — |
| A3 | Edit an account's current balance | — | — | — | — | — |
| A4 | Set / change an account's safety cushion | reserved ahead of every jar and drained before them; **setting one above 0 also activates the middle warning state** ([14 item C](14-stage1-allocation-model.md#item-c--the-rulings)) — at 0 that state never fires | — | — | days where the cushion is not whole | 1 |
| A5 | Delete an account | blocked while it holds patterns or transfers ([10 item 6](10-multiple-accounts.md#item-6--persistence--migration--settled-2026-07-22)) | — | reassign or remove them first | — | — |

## B · Bills & paychecks (finance patterns)

| # | Action | Implicit response | What we ask | Left to the user | Warn about | Stage |
|---|---|---|---|---|---|---|
| B1 | Create a bill (the shortcut form) | a jar that fills automatically by the standing rule; **no savings plan is created** ([14 item D](14-stage1-allocation-model.md#item-d--the-rulings)) | nothing about savings | press "set up a savings plan" on the pattern later, if they want to control the filling | — | 1 ✔ |
| B2 | Create a pattern (advanced form) | a jar that fills automatically by the standing rule; **no savings plan is created** ([14 item D](14-stage1-allocation-model.md#item-d--the-rulings)) | skippable / unskippable (hidden for income) | press "set up a savings plan" on the pattern later, if they want to control the filling | — | 1 ✔ |
| B3 | Create a one-off expected expense ("buy a TV") | a jar that fills automatically by the standing rule; **no savings plan is created** ([14 item D](14-stage1-allocation-model.md#item-d--the-rulings)) | nothing about savings | press "set up a savings plan" on the pattern later, if they want to control the filling | — | 1 ✔ |
| B4 | Delete a speculative expense again | removed outright — nothing else was created, so nothing blocks it | — | — | — | 1 ✔ |
| B5 | Change a pattern's **amount** | — | — | — | — | 3 |
| B6 | Change a pattern's **schedule** | — | — | — | — | 3 |
| B7 | Change **priority** | — | — | — | — | 1 |
| B8 | Change **skippable / unskippable** (was "mandatory") | changes deallocation ordering — unskippable jars are drained only after every skippable one is empty ([14 item B](14-stage1-allocation-model.md#item-b--the-rulings)) | a plain radio pair, advanced form only; hidden for income; defaults to "I have to pay this" | — | — | 1 ✔ |
| B9 | Change **description / source** | — | — | — | — | 3 |
| B10 | Move a pattern to a different account | jars recompute fresh — nothing is stuck in the old silo | — | — | — | 4 |
| B11 | Delete a pattern | **its savings plan is removed with it** — was blocked, now confirms first ([14 item D-2](14-stage1-allocation-model.md#what-d-2-means-concretely)) | "this will also remove its savings plan — continue?" | — | — | 1 ✔ / 3 |
| B12 | Say when a bill or paycheck stops | **ongoing** keeps a hidden end date extended to the horizon plus a cycle, and propagates to any savings plan; the other answers set a real end date ([15](15-stage2-pattern-lifetime.md)) | one question, three answers — no category names | — | — | 2 ✔ |
| B13 | Ask for a loan's payoff date | computes owed ÷ payment, rounded up, and sets it as the end date — a one-time estimate that does **not** re-derive if the payment changes | total owed, regular payment | re-run it themselves if the payment changes | the date is a **floor** — interest and fees push it later | 2 ✔ |
| B14 | Change a pattern **starting on a date** — break off *(proposed)* | — | — | — | — | 3 |
| B15 | End a pattern at a date — "cancel Netflix next month" *(proposed)* | — | — | — | — | 3 |

## C · Allocations (earmark patterns)

| # | Action | Implicit response | What we ask | Left to the user | Warn about | Stage |
|---|---|---|---|---|---|---|
| C1 | Create a one-time goal (the shortcut form) | creates goal + savings plan as one action (`OneTimeGoalFactory`) | description, amount, due date, start date, priority | — | — | 1 |
| C2 | Create an earmark pattern (advanced form) | — | — | — | — | 1 |
| C3 | Change an earmark pattern's **amount** | — | — | — | — | 4 |
| C4 | Change an earmark pattern's **schedule** | — | — | — | — | 4 |
| C5 | Set a starting allocation on a new plan | seeds the jar before the pattern's own occurrences run (`StartingAllocation`) | — | — | — | 1 |
| C6 | Delete an earmark pattern | the jar **returns to filling automatically** — the outflow rejoins the automatic rule it had opted out of | — | — | — | 1 ✔ / 3 |
| C7 | Change an allocation **starting on a date**, bill unchanged *(proposed)* | — | — | — | — | 4 |
| C8 | Add a **second** earmark pattern to one goal *(proposed)* | currently forbidden — `3.11.1.a1` | — | — | — | 4 |
| C9 | Stop contributing early to a goal already met *(proposed)* | — | — | — | — | 4 |
| C10 | Defer contributions on an over-funded jar *(proposed)* | — | — | — | — | 4 |
| C11 | Allocate toward something that hasn't started yet *(proposed)* | — | — | — | — | 4 |
| C12 | **Set up a savings plan for an existing bill or expense** *(new — [14 item D-1](14-stage1-allocation-model.md#item-d--the-rulings))* | creates a real savings plan seeded from where the jar already stands, so no money moves; the outflow stops filling automatically and follows the plan instead | the plan's amount and schedule | — | the plan is now theirs to keep on track — it no longer adapts on its own | 1 ✔ |

## D · Manual (explicit) earmarks

Rulings already settled in [09-manual-earmarks.md](09-manual-earmarks.md) — **do not re-litigate**;
these rows record what the *system* does around them.

| # | Action | Implicit response | What we ask | Left to the user | Warn about | Stage |
|---|---|---|---|---|---|---|
| D1 | Add money to a jar | pre-as-of folds into the jar's seed; in-window becomes an isolated event | amount, jar, date | — | over-add allowed but warned (ruling 3) | 5 |
| D2 | Withdraw from a jar to free | — | — | — | over-withdrawal blocked (ruling 2) | 5 |
| D3 | Move money between jars | composed as −A / +B on one day | — | — | — | 5 |
| D4 | Edit / delete a manual earmark | — | — | — | a stored withdrawal can later be floored; flagged in place | 5 |
| D5 | Make one on a future day with no snapshot yet | — | — | — | — | 5 |

## E · Transfers

| # | Action | Implicit response | What we ask | Left to the user | Warn about | Stage |
|---|---|---|---|---|---|---|
| E1 | Schedule a transfer | creates a withdrawal + a deposit pattern + a `Transfer` record; both hidden from lists | from, to, amount, schedule | — | — | — |
| E2 | Delete a transfer | removes both patterns | — | — | — | — |
| E3 | Use the "Cover $X from another account →" lever | pre-fills a one-time transfer into the short account for exactly the shortfall | the user still presses Create | — | — | 6 |
| E4 | Whether a scheduled transfer accrues a jar | **the from-account reserves along the A/B ramp; the household view adds it back into free** ([14 item A-1](14-stage1-allocation-model.md#item-a--which-expected-transactions-get-a-jar-and-does-that-jar-reserve--open)) | — | — | — | 1 ✔ |

## F · Forecast controls & data

| # | Action | Implicit response | What we ask | Left to the user | Warn about | Stage |
|---|---|---|---|---|---|---|
| F1 | Change the as-of date | — | — | — | — | — |
| F2 | Change the horizon | — | — | — | — | — |
| F3 | Run a forecast | rebuilds the whole onion in memory (W4) | — | — | — | — |
| F4 | Select a day | — | — | — | — | — |
| F5 | Filter the overview to one account | re-scopes every number in the calendar | — | — | — | — |
| F6 | Export the forecast as a spreadsheet | — | — | — | — | — |
| F7 | Export / import the data file | wholesale file copy; migrations run on the next start | — | — | — | — |

---

## Actions the system takes that no user action triggers

The other half of item 7 — and the reason item 15 (catalog every implicit earmark) exists. Same
filling rule.

| # | System action | When it fires | What the user sees | Can they override it? | Stage |
|---|---|---|---|---|---|
| S1 | Reserve toward **any outflow** (W6's A/B accrual) | every day, per outflow with no earmark pattern — *was* mandatory-only until [14 item A](14-stage1-allocation-model.md#item-a--settled-2026-07-23) | — | — | 1 |
| S2 | Deallocation — drain the cushion, then every **skippable** jar, then unskippable ones, by priority within each group | when spending overdraws free funds | — | — | 1 |
| S3 | Release a goal's jar on its own occurrence | on the goal's due date | — | — | 1 |
| S4 | Floor a jar at zero | any day a jar would go negative | flagged in place | — | 5 |
| S5 | Merge an implicit earmark into a manual one | same jar, same day | — | — | 1 |
| S6 | Hand a jar balance across a break-off *(proposed)* | — | — | — | 3 |
| S7 | Auto-renew an open-ended bill's rule *(proposed)* | — | — | — | 2 |

**Item 15 is complete when S1–S7 (plus anything later stages add) each have a filled "what the user
sees" cell.** That column is the whole point: an implicit action nobody can see is the standing cost
recorded against [W5 in the workaround registry](13a-linearity-workaround-registry.md#w5--the-implicit-isolated-earmark--the-general-escape-hatch).
