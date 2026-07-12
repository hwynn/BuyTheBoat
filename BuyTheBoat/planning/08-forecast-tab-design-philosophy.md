# 08 — Forecast tab design philosophy

Human-readable design principles for the **Forecast tab** (the app's default,
first tab), stated by the author 2026-07-11. This is the durable "why" behind
the layout — a reference for short-term planning and long-term
maintainability/readability. When a change to this tab is proposed, it should be
justifiable against these principles; when the UI code is read, these are the
intentions it serves.

The tab exists to answer the project's four core questions (see
[`../../04-project-goals-and-user-questions.md`](../../04-project-goals-and-user-questions.md)):
**Q1** how much free money do I have · **Q2** can I afford X · **Q3** am I on
track for my goals · **Q4** how do I readjust after an unplanned purchase. No
single region answers all four; the tab answers them *together*, split across
three regions plus an export.

The tab has three on-screen regions — **top controls**, the **forecast /
overview**, and the **selected day** — plus the **exported spreadsheet**, which
is a static stand-in for the whole thing. Each has a distinct job.

---

## 1. Top controls

The controls belong at the top and are broadly fine as they are.

- **Forecast range (start / end).** Controlling the start and end of the
  forecast window is a core control and belongs here. The **"Forecast" button**
  initiates the jump to that range.
  - **The button reflects pending changes.** It should *look* enabled only when
    the currently-shown forecast is **not** already using the range in the
    start/end fields. So: right after a forecast runs, the button reads
    disabled/inactive; the moment the user edits a start or end date (so the
    fields no longer match what's on screen), it reads enabled/active. It's a
    "there's a change to apply" affordance, not an always-live button.
- **Export to spreadsheet** stays here — it lets the user save the current
  forecast in another format for viewing (see §4).
- **Current balance** and **safety cushion** are fine here *for now*, but may
  move to a different tab in the future — they're forecast *inputs*, not part of
  the range control, so their placement is not load-bearing.

---

## 2. Forecast / overview

A table is fine for now. Its whole value is **showing a lot of information at
once**. It is *not* expected to answer every core question on its own — the user
can always select a day and drop into the **selected day** region (§3) for the
full picture.

### It IS meant to show

Information that is **best understood across multiple days at once**:

- **a. Upcoming expenses** — especially mandatory ones (bills). Good to see
  coming in advance.
- **b. Paychecks.**
- **c. Allocations** — when and where money gets stashed over time.
- **d. Every balance snapshot.** Design intent to state plainly: **all**
  balance snapshots in the window are viewable here. Even as the overview gets
  boiled down for readability, seeing every snapshot in the page's range is
  *always* an option. We may later offer ways to hide/filter some — but never at
  the cost of being able to see them all.
- **e. General trends** — money coming in, being set aside, and going out (the
  last once actual/paired transactions exist). The long view lets the user sense
  whether finances are getting healthier or worse over time. **This does not
  imply a line graph** or any fancy chart. Showing the **free amount** and
  **total allocated amount** each day — displayed well alongside money coming
  in, going out, and imminently needed — already lets the user read general
  health at a glance.
- **f. Paired transactions** (once actuals exist) — seeing at a glance that an
  expected bill or paycheck *actually came in*. We already show expected
  transactions; a paired one gets a **slightly different style** to signal it's
  "fulfilled."
- **g. Which day is *today*.** The element representing today — whatever row,
  cell, card, or tick a given layout uses — must be **visually distinct** from
  every other displayed day (a border, bevel, glossiness, tint, or similar), so
  the user can instantly locate "now" in the forecast. This holds no matter how
  the overview is laid out (table, calendar, timeline, cards, …).

And it should **highlight days worth a closer look**:

- **a.** Funds for something are running short, or there may not be enough for
  an upcoming goal.
- **b.** (once actuals exist) A bill in the near past that still hasn't been
  paid, or came in at an unexpectedly different cost — worth attention.

### It is NOT meant to

- **Show where all money is allocated on each day.** Money will ideally sit in
  many jars — too many for this simple per-day view.
- **Display elaborate graphs to look pretty.**
- **Show every single actual transaction.** Once actuals exist they'll be
  numerous; the **total amount spent** that day is enough here.

---

## 3. Selected day

This region leverages the **full potential of the balance snapshot**. Together
with the overview's context, it's where all of the user's questions get
answered.

### It IS meant to show

- **I. Every transaction that occurred.** Actual and expected transactions may
  be *summarized* in the overview, but this view **lists everything**.
- **II. Where all our money is allocated.** There's room here for **every fund
  jar** and how much is in each.
- **III. The health of the funds** — clearly answering **Q3 "Am I on track to
  reach my goals?"**. Q3 is general, but it takes a slightly different shape per
  kind of expense, and the display can differ for each:
  - **a. One-time optional goal** (usually far in the future). For most of its
    life we won't have enough to fully afford it, so **"am I on track setting
    money aside?"** is what matters. Show the **milestone amount** explicitly;
    we may not need to show the full amount due. Express the target as a
    **relative summary** when it's far off — "6 months away", "late next year" —
    and only show a **formatted date** once it's close (within a couple of
    months). **Highlight** when falling behind. Consider prompting, somewhere in
    the view, the goals the user might want to **restructure the earmark
    pattern** for (or add explicit earmarks to) in order to get back on track
    (this is the Q4 on-ramp).
  - **b. A repeated and/or mandatory goal / bill / expected transaction.**
    Show the **due date with more precision** by default. (Month still spelled
    as a word, not a number.)
  - **c. A repeated *and* mandatory expense — i.e. a regular bill.** Like a
    long-term goal, we care whether we've set enough aside and are "on track"
    against the milestone — but we *also* want to know whether we've allocated
    enough to **pay the full bill right now**. Here **styling beats showing the
    milestone number**: show the allocated amount, in a **positive style/color**
    when it's on track with the milestone, and a **different, more-positive
    style/color** once enough is allocated to cover the full amount immediately.
    Show the **amount due**.
  - **d. Paychecks** are simple — they *give* money, nothing to save up for. The
    **amount and date** are sufficient, with a **positive style/color** to set
    them apart. But a paycheck often triggers or coincides with allocations, so
    the user naturally asks *"how much did I get, and where did it go?"* — so a
    paycheck should sit **directly before/above** the allocations funded from it.
- **IV. The expected free amount.** It may or may not appear in the overview,
  but the selected-day view looks incomplete without it.

---

## 4. The exported spreadsheet

Without the ability to select a day and pull up detail dynamically, the export
has to lay **all** of that detail out statically.

- **Per day:** the expected/current amount, the **free amount**, and a
  **breakdown of every fund jar** and how much is in each. **Colors** show
  whether we're on track. The **full amount due and the due date** are shown
  **regardless** of what kind of expense it is (unlike the on-screen views,
  which tailor this per type).
- **Transactions** (not allocations) of **all kinds are fully listed out.**
- **Presentation carries a lot of the weight:** clear column and row header
  labels count for a lot; **grouped rows or columns** are a fair way to break
  down multi-part information like a fund jar; and **colors/styles** should
  communicate at a glance whether a number is good or bad for us.
- This assumes a **single** way to organize the spreadsheet. In the future users
  may get **different layout options** when exporting.

---

## How this maps to the code

These principles live in the UI layer; the code files that own each region carry
a short comment pointing back here:

| Region | Code |
|---|---|
| Top controls + the three-region layout | `src/MyMoneyForecast.App/MainWindow.xaml` (Forecast `TabItem`) and `MainWindow.xaml.cs` (`OnForecastClick`, `RefreshForecast`, `UpdateForecastButtonState`, `SelectDay`/`ShowDayDetail`) |
| Forecast / overview (the chosen calendar) | `MonthRow` + `DayCellRow` in `src/MyMoneyForecast.App/PatternRows.cs`; `BuildCalendarMonths` in `MainWindow.xaml.cs`; the `TimelineCalendar` ListBox in `MainWindow.xaml` |
| Selected-day events (left) + jars (right) | `DayEventRow`, `JarDetailRow`, `DayDetailContext`, `DueDateText`, `GoalStatusRow` in `PatternRows.cs`; per-type classification via `ExpenseKindClassifier` in `src/MyMoneyForecast.Domain/ExpenseKind.cs` |
| Exported spreadsheet | `src/MyMoneyForecast.App/ForecastSpreadsheetExporter.cs` (still consumes `TimelineRow`, which now exists only for it) |

The forecast **data** these views render (per-day snapshots, jars, events, free
amount, `IsDeallocationDay`, goal shortfalls) comes from
`TransactionLogBookFactory` / `ForecastResult` — see
[`05-original-structure-restructure.md`](05-original-structure-restructure.md).
Where a principle here needs data the engine doesn't expose yet, that's a
domain-side follow-up, not something to fake in the view.
