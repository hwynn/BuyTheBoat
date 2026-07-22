# 11 — Forecast Tab UI: Design & Decisions

The durable "why" behind the forecast tab's user interface. Three things live here because they kept having to be re-derived from scattered places:

1. each on-screen **region's goals and non-goals**,
2. the **multi-account UI decisions** and the chosen layouts, and
3. a **log of the small refinement choices** the author made — what they liked and why — so a future rework does not lose the reasoning.

Companion docs: [08-forecast-tab-design-philosophy.md](08-forecast-tab-design-philosophy.md) is the original (single-account) philosophy this consolidates and extends; [10-multiple-accounts.md](10-multiple-accounts.md) is the multi-account feature design (its item 4-C defines the data these views render); [design-philosophies.md](../../design-philosophies.md) holds the project-wide principles (especially **#1** inform-don't-automate and **#2** speak-the-user's-language) that these UI choices serve.

---

## A. The three regions and their jobs (goals **and** non-goals)

The forecast tab answers the four core questions ([04](../../04-project-goals-and-user-questions.md)) *together*, split across three on-screen regions plus an export. Each has a distinct job — and, just as importantly, things it deliberately does **not** try to do. (Fuller prose in [08](08-forecast-tab-design-philosophy.md) §1–§4; this is the consolidated statement, kept here so it stops being hard to find.)

### Top controls
- **Does:** the forecast range (start / end) plus the "Forecast" button, whose enabled state reflects *pending* changes (it reads enabled only when the fields differ from what is currently on screen); export-to-spreadsheet; the balance and cushion inputs (per-account, once multiple accounts land).
- **Does not:** the placement of the balance/cushion inputs is not load-bearing — they may move to a different tab later.

### Overview (the calendar)
- **Does** — things best understood across many days at once: upcoming expenses (especially bills) and paychecks; allocations over time; **every** balance snapshot in the window; general trends via the **free** amount + a **total** per day and money in/out — *not* a line graph; **today** made visually distinct; and a **flag** on days worth a closer look (funds short, or not enough for a goal).
- **Does NOT:** show where all money is allocated each day (too many jars for a per-day view); draw elaborate/decorative graphs; list every actual transaction (the day's total is enough). It is **not** expected to answer every question on its own — you select a day and drop into the selected-day region.

### Selected day
- **Does** — the full snapshot: **every** transaction; **where all money is allocated** (every jar and how much); the **health of funds** answering Q3 per expense type (goal → milestone + relative due + "adjust the plan" nudge; bill → on-track vs. fully-covered styling + amount due; paycheck → amount/date, sitting above the allocations it funds); the **free amount**.
- **Does NOT:** try to be the many-days scannable trend — that is the overview's job. **Each region's non-goal is the other's job.**

### Exported spreadsheet
- **Does:** the static stand-in for the whole thing — per day, all detail laid out (expected/current amounts, free, every jar, colors for on-track, the full amount due + due date regardless of expense type); every transaction listed.

---

## B. Multi-account UI decisions (2026-07-22)

**The rule that shapes everything:** "having enough money" means **having enough in the *right* account** — the household total can look fine while one account is short (a bank will not move money to cover it, or charges a fee for doing so). So per-account solvency must be visible. Item 4-C in [10](10-multiple-accounts.md) settled the split: the **overview** shows a household summary and flags a day when *any* account is short (without naming it in the cell); the **selected day** carries the per-account truth.

### Overview — CHOSEN: "Rich month calendar" (option E)
File: [mockups/forecast-overview-multiaccount-mockups-2.html](mockups/forecast-overview-multiaccount-mockups-2.html). The month-grid calendar (the form chosen for the single-account version too), with each day cell carrying:

- the **account filter** (All / Checking / Bills / Savings), re-scoping every number in the calendar;
- two **labeled numbers** — **Total** (money that day) and **Free**;
- the day's **top event by name** (e.g. "Paycheck", "Electric bill");
- a per-account **flow strip** — account letter + **↑** money in / **↓** money out / **•** no change;
- a **warning** — a ⚠ symbol **+ words** ("an account is short / thin");
- an explicit **event count** ("3 events" / "No events"), kept top-right.

### Selected day — CHOSEN: "Grouped two-pane" (option D)
File: [mockups/forecast-selectedday-multiaccount-mockups.html](mockups/forecast-selectedday-multiaccount-mockups.html). Keeps the single-account two-pane — **what happened today** (left) | **fund jars** (right) — but **groups both panes by account**. A short account's **"Cover from another account →"** lever (a pre-filled transfer that resolves the shortfall on the next forecast) sits in its group.

**Why this over the equal-space layouts:** the other options (stacked sections, columns, cards) gave every account equal space, but in practice one or two accounts carry most of a day's activity. Grouping the two-pane by account gives each account exactly the space it needs, and stays closest to the two-pane already chosen for the single-account view.

---

## C. Refinement log — the small choices and why (2026-07-22)

Captured while fresh, per the author's request, so a rework keeps the reasoning. Each is a decision the author made this session, with what they liked and the stated why.

1. **A warning is never carried by color alone.** A warning is a symbol (⚠) **plus words** ("an account is short / thin"). *Why:* some users are colorblind, so color cannot be the sole signal for something as important as a shortfall. *(This generalizes — the flow arrows and account letters below also encode meaning by shape and letter, not only color.)*
2. **The per-account indicator shows money-flow direction, not a status square.** The first round's per-account "health squares" were confusing ("I don't know what they mean"). Replaced with the account **first-letter + ↑ money added / ↓ money removed / • neutral**. *Why:* same footprint, but "money in/out of this account" is immediately legible.
3. **The overview keeps an account filter.** *Why:* the author wants to be able to view one account at a time; default is All (household), and the chips filter to a single account.
4. **The two numbers are explicitly labeled — Total and Free.** *Why:* unlabeled, the author guessed wrong about which was which; which number is which should never have to be guessed.
5. **"Set aside" is replaced by the day's top event, by name.** *Why:* "set aside" (the total allocated) is internal jargon (philosophy 2), and this spot is a good place to briefly show *what happened* that day ("Paycheck", "Electric bill").
6. **Busy-ness is explicit text, not dots.** First shown as dots (more dots = busier); the author found dots vague when there is room to be explicit. Now "3 events" / "1 event" / "No events", kept **top-right** so it does not crowd the day number. *Why:* there is space to just say it.
7. **Overview form = the rich month calendar.** *Why:* it keeps the spatial calendar chosen originally, with everything visible on one screen.
8. **Selected-day form = grouped two-pane.** *Why:* see section B — give each account the space its activity warrants, not a fixed equal share.

### Standing UI principles distilled from the above
Apply these to future forecast-tab work:

- **Meaning is never color-only** — pair color with a symbol, letter, or word (accessibility).
- **Prefer a plain human word or number** over an internal figure or a code the user has to decode (philosophy 2).
- **Give each account the space its activity warrants**, not a fixed equal share.
- **A problem the app surfaces should come with an easy, explicit lever to fix it** (philosophy 1 — e.g. the "Cover from another account" transfer on a short day).
