# 21 — Forms & UI: design reasoning

The durable "why" behind the forms and the forecast tab — consolidated from the former docs 11
(forecast-tab UI), 21 (form architecture), 22 (plan-health content), and 23 (form behavior). **The
forms, regions, and plan-health content are all built and live in the code** (`ExpenseFormPanel`,
`AccountFormPanel`, `EarmarkFormPanel`, `TransferFormPanel`, `RecurrenceRuleEditor`, `SummaryRegion`,
`PlanHealthMessages`, `TransactionLogBookFactory.CalculatePlanHealthStates`), with the settled visual
record in [mockups/settled-designs.html](mockups/settled-designs.html). This document is the reasoning —
the goals/non-goals, the standing rules, and the deferred items — not a to-build spec; the code and the
mockup are the live truth for layout and wording.

Serves [design-philosophies.md](design-philosophies.md) throughout — especially #1
(inform-don't-automate), #2 (speak the user's language), and #4–7 (the form-shape philosophies).

---

## The forms

- **Three forms, one per core thing the user manages** — **Expense** (a `FinancialPattern`: a bill or
  a paycheck — a paycheck is a "positive expense," imperfect but adopted), **Account** (an `Account`),
  **Earmark** (an `EarMarkPattern`). Each is its own always-selectable tab (Philosophy 5) and does
  double duty for create and edit (Philosophy 7).
- **Transfer is a fourth, separate form** — a transfer is a linked pair (a `Transfer` plus its
  withdrawal/deposit `FinancialPattern` legs), special enough not to fold into Expense. It does double
  duty for create and edit too.
- **Earmark has two modes** — a characterization toggle: **Savings plan** (editing the `EarMarkPattern`
  itself) vs. **One-off adjustment** (a single isolated `EarMarkEvent` / `ManualEarmark` alongside it).
  This is why there's no separate manual-earmark window.
- **The instance-picker is a goal-picker**, not a plain search box — picking a goal *is* the
  inheritance mechanism (it loads whatever plan exists for that goal).
- The list/management tab for each thing stays separate from its form tab. Consolidating the four
  list tabs into one hub (keeping the four forms standing) is deferred, not rejected.

## Form layout — three regions, most-important highest

1. **Instance-information block** — which instance is being edited (blank / loaded-untouched /
   loaded-with-unsaved-edits). Carries a "modified since last saved" indicator (forms deliberately
   don't autosave across tab switches), a **"Discard unsaved changes"** control (worded to read as
   discarding *edits*, not deleting the instance), and a "clear the form" control (confirms first if
   edits exist). **No delete button on the form** — deletion, with its warnings, lives on the
   list/management tab. The form auto-clears after a successful save. A shortcut that opens the form to
   a specific instance sets the picker's value *programmatically*, without visibly opening its popup.
2. **Characterization-field block** — the fields deciding what *kind* of thing this is (bill / loan /
   one-time goal / …), plus a live, non-interactive **characterization text** reusing the UI's own
   shortcut names. It confirms what a shortcut set, and shows the *same* label once a hand-built form
   happens to match a named shape (quiet "you're doing it right"); shows **"Custom"** when nothing
   matches — never blank.
3. **Everything else.**

**Helper regions** — contextual blocks that appear only when relevant (the RRule preview, the
loan-payoff estimator, the plan-health regions, Account's context panels). One term for "shows up only
sometimes, gives helpful context, isn't a core field," whatever triggers it.

## Forecast tab — the regions' jobs (goals *and* non-goals)

The tab answers the four core questions ([04](04-project-goals-and-user-questions.md)) *together*,
split across regions. Each region's non-goal is another region's job.

- **Top controls** — *does:* the forecast range + Forecast button (enabled only when the fields differ
  from what's on screen), export, per-account balance/cushion inputs. *Does not:* the balance/cushion
  inputs' placement isn't load-bearing.
- **Overview (calendar)** — *does:* what's best seen across many days — upcoming bills/paychecks,
  allocations over time, **every** snapshot in the window, per-day **free** + **total** and money
  in/out (not a line graph), today made distinct, and a **flag** on days worth a look. *Does not:*
  show per-day allocation detail (too many jars), draw decorative graphs, or list every transaction.
  **See-all is never removed** — filters may hide snapshots, but the ability to see them all stays.
- **Selected day** — *does:* the full snapshot — every transaction, where all money is allocated, the
  per-expense health answering Q3 (goal → milestone + due + "adjust the plan" nudge; bill → on-track
  vs. covered + amount due; paycheck → amount/date above what it funds), the free amount. *Does not:*
  be the many-days trend (that's the overview).
- **Exported spreadsheet** — the static stand-in: per day, all detail and every transaction.

**Multi-account rule (the one that shapes it all):** "enough money" means enough **in the right
account** — the household total can look fine while one account is short. So per-account solvency must
be visible: the **overview** shows a household summary and flags a day when *any* account is short
(without naming it in the cell); the **selected day** carries the per-account truth, grouped by account,
with a short account's **"Move $X in →"** lever (a pre-filled one-time transfer) in its group.

## Standing UI principles

Distilled from the forecast-tab and plan-health passes; apply to all UI work:

- **Meaning is never color-only** — pair color with a symbol, letter, or word (accessibility).
- **Prefer a plain human word or number** over an internal figure or code the user must decode (Phil 2).
- **Give each account the space its activity warrants**, not a fixed equal share.
- **A problem the app surfaces comes with an easy, explicit lever to fix it** (Phil 1) — e.g. the
  cross-account "Move $X in" transfer on a short day.
- **Pre-fill a form as far as it can honestly go, then stop at the confirm** — and keep the pre-fill
  scoped to the problem (covering one short day pre-loads a *single* occurrence, not the monthly
  default). Pre-filling is help; submitting would be deciding for the user.
- **Never frame a jar releasing money as "getting it back"** — it was always the user's money, just
  reserved; say it returns to free balance.
- **Discoverability is passive by default** — the app makes a feature findable; active surfacing is
  reserved for a moment it's *already* warning about something, as a free on-topic shortcut, never a
  new proactive interruption.
- **Design the informational helper region for a state before any shortcut that acts on it** — show
  what's wrong and what the user could do with the form's own controls first; a shortcut only saves
  steps. "If the shortcuts are the only way the user can do what they want, they aren't in control."
- **Actionability sets the space budget, not importance alone** — information actionable *in this form
  or mode* earns more room; information only actionable elsewhere is stated briefly.
- **Compositional density** — weave several facts into one short statement, don't stack a sentence per
  property. A property either supplies a word (a number, a plural, "short"/"surplus") or selects which
  sentence shape applies. No single template; work each cluster out on its own.

## Form-behavior rules

- **Downward-only editing** *(the class hierarchy `FinancialPattern` → `EarMarkPattern` →
  `EarMarkEvent`)*: saving a change to a higher class may imply changes to a lower one; saving a change
  to a lower class may **never** imply a change to a higher one. An edit that *would* require altering
  the class above it is made **structurally impossible** (UI and DB), not merely discouraged. This is
  already the content of assumptions `3.13.8.a1`/`3.13.8.a2` (an earmark can't exist without, or
  outside the span of, its pattern), `3.11.2.a2` (an earmark pattern's span ⊆ its finance pattern's),
  `3.13.8.1.a3`, and `3.10.a3` — all phrased as constraints the *lower* class must satisfy. Enforced
  today in `ManualEarmark.Create` and `EarMarkPattern.Create`.
- **Forced vs. Suggested** *(when a downward change is written)*: a value that, left alone, would leave
  some instance violating an assumption is **Forced** — saved immediately, no separate confirmation. A
  value that's merely recommended (ignoring it produces valid data, just a `PlanHealthState` warning)
  is **Suggested** — stays pending until the user's own Save. The test is literal: *is there an
  assumption ID that would be violated without this value?* Every Forced case is exactly a
  Downward-only-editing cascade.
- **Saved state vs. Working state**, and **all derived content reads live from Working state** — the
  Summary, current jar state, milestones, everything: re-run the forecast against the in-progress
  unsaved edits (a "what if I saved this" preview), never freeze on the last-saved numbers. Derived
  text that differs from its saved-state counterpart should be visibly flagged as a *consequence of a
  pending edit* (styling deferred; can't be color-only).
- **Break off vs. alter** *(three buckets, never inferred from what changed)*: **plain edit** (uniform
  change, incl. a correction or extending `Until`), **break off** (amount/schedule changes as of a
  date, the pattern continues under a new segment), **truncate** (the pattern stops). The branch point
  is "does the user want the past preserved or corrected," a deliberately chosen action. `BreakOffFactory`
  (finance side, new `FinanceId`, jar handed off via `StartingAllocation`) and `RestructureFactory`
  (earmark side, same `FinanceId`, jar carries for free) are the built pair.
- **When to lock a field** (reach for these before a literal disabled control): **hide** it when
  mode-irrelevant; show **plain read-only text** (not an input) for another instance's context;
  **validate/range-limit in place** for a Downward-only constraint (the date-picker refuses to show an
  out-of-range date at all); a true disabled control is right only for a loaded row's **identity-key**
  fields (an existing isolated earmark's Date and goal — changing either would be a different row, not
  an edit).

## Plan-health content (the Earmark form's helper regions)

What each region tells the user (plain terms, not property names):

| Region | Question it answers |
|---|---|
| Current jar state | Where does my fund jar stand *right now*, against what it should hold today? |
| RRule preview | Which upcoming dates are at risk — a one-time dip or a recurring one? |
| Summary | What is this plan overall — what I'm saving for, and how the whole trajectory looks vs. the goal? |
| Expense's status indicator | Does the linked plan need attention, and how urgently? |

**`IsWorthWarningAbout`** (built; the rule, kept because it isn't obvious from the field). Constants — 2
months, 6 months, and the "half" ratio — must be named, adjustable constants, not literals.
- *One-time expected transaction:* meets the milestone by its date → no warning; short and ≤2 months
  away → warn; short and >2 months away → warn only if the shortfall is ≥ half the account's projected
  free funds that day.
- *Repeated:* next occurrence ≤2 months away and even slightly short → warn; >2 months and shortfall ≥
  half that day's projected free → warn; **any** occurrence within six months short → warn regardless.
  The "half of free funds" check is one shared calculation across both branches.
- *Excess:* warn when the projected excess on the next occurrence's date exceeds **double the smallest
  repeated `EarMarkPattern.Amount`** in the plan.

**The shortfall ladder** — shared vocabulary between the forms (which *narrate*) and the forecast (which
*states*): the words `short`, `free`, `set aside`, `transfer` stay consistent, the sentence shape can
differ per page.

| Rung | Meaning | Which page | Fix |
|---|---|---|---|
| 1 — here, not set aside | Money's free in *this* account, just not allocated | Forms; the forecast nudges toward it | Set it aside |
| 2 — in another account | Money's in the household, wrong account | Forecast only (forms are single-account) | Transfer, then set aside |
| 3 — genuinely short | Not enough anywhere | Both | Earn / cut / reschedule |

## Open / deferred (forms & plan-health)

- **Free-funds warnings on the selected-day account header — two, not three.** The header
  carries **short** (`free < 0`, red) and **thin** (`0 ≤ free < ThinnessMultiplier × ideal
  cushion`, amber "Funds running low"), the two rungs of the same free-funds axis.
  `ThinnessMultiplier` (default 1.0, in `MainWindow`) is the only knob; thin is skipped when no
  cushion is set (its threshold would be 0). The overview calendar was left alone. Thin has no
  domain-level `HouseholdDay` list yet (computed in the code-behind against the same free figure
  the pane shows); promote it beside `ShortAccounts` if the overview later needs it.
- **Reserve-low (cushion below target) was folded into thin, not shown separately.** Seeding
  through the real engine showed the two co-occur in practice: the cushion always funds to its
  full ideal (priority 0), so it only drops below target when a **deallocation drains it** to
  avoid going short — which leaves free near zero, i.e. thin. Any refill happens at priority 0
  before free can climb back over the thin threshold, so a reserve dip is never visible on its
  own. The domain's `CushionDippedAccounts` signal still exists (unused by the UI) if a future
  pass wants the sharper "your reserve is actually breached" wording as a variant of the thin
  message; the free-funds warning covers every case on its own for now.

- **`UnderfundedReleaseDates` redefinition** — the wanted version is a lookback count of past
  occurrences that *would have* been short but for a manual earmark. Blocked on the backward-history
  gap (the engine only walks forward from the as-of date), which **actual transactions**
  ([12](12-actual-transactions-deferred-design.md)) would provide; deferred with them. The current
  build is the older "dates a release came up short" definition.
- **The transient "fixed" message** on the RRule preview (clears an at-open warning as the user types)
  — its wording constants exist (`PlanHealthMessages.FixedNoLonger*`) but nothing diffs at-open vs.
  live health to show them yet.
- **`MaybeStartingEarmark` auto-saves its starting earmark** though no assumption requires it — by the
  Forced/Suggested rule that's Suggested (should stay pending), not Forced. Left auto-saved
  deliberately (the urgency that motivates it argues for not risking it going unconfirmed); flagged as
  a known exception, not decided.
