# 10 — Multiple Accounts

**Status:** design **COMPLETE** (2026-07-22) — items 1, 2, 3, 4 and 6 are settled below, and item 5's layouts are chosen (see [11-ui-design-and-decisions.md](11-ui-design-and-decisions.md)). This was the philosophy-3 "plan before you build" pass — see [design-philosophies.md](../../design-philosophies.md).

**Implementation COMPLETE (2026-07-23) — milestones 1–5 built and verified.** Solution builds clean (0 warnings), **147 tests green** (109 domain + 38 scenario); milestones 1–3 were driven end-to-end in the running app, and milestone 4's engine is proven by tests (the single-account path is byte-identical — all prior numbers held after the restructure).

- **1 · Account foundation + management UI** — the `Account` type, `Accounts` table + `AccountRepository`, the seed migration ("primary", carrying the old single balance/cushion forward), and the Accounts tab. The Forecast tab's single balance/cushion inputs became a read-only "Across all accounts" summary; the forecast still runs on the household totals until item 4.
- **2 · Filing patterns under accounts** — a storage-only `AccountId` column (domain `FinancialPattern` untouched), `Save(pattern, accountId)`, `GetAllByAccount()` regrouping into per-account lists, an always-visible account picker in the create/edit windows, and an Account column in the grid.
- **3 · Transfers** — the `Transfer` type + `TransferFactory` (paired legs), the `Transfers` table + `TransferId` leg back-reference + `TransferRepository`, the Schedule-Transfer window + Transfers tab. Legs are hidden from the pattern list but live in the engine (they are what move the money); deleting a transfer removes both legs; account-delete is guarded against both filed patterns and transfers.
- **4 · Engine partition (domain)** — the single-account cascade was extracted into `BuildAccountPage`; `CreateForecast` now runs it once per account (from `ForecastOptions.Accounts`) and rolls the results up. `ForecastResult` gained `Accounts` (per-account pages + each account's first-short date) and `Household` (per-day summed free + set-aside, plus `ShortAccounts` / `AnyAccountShort` — the "enough in the right account" signal). The single-account path (no `Accounts`) synthesizes one "Primary" account, so its output is byte-identical and every prior test passed unchanged.

- **5 · Forecast views — core done and verified.** The App now feeds the engine per-account data (`BuildAccountInputs` → `ForecastOptions.Accounts`). The **overview** renders as the household calendar (per-day summed free + set-aside; a day's attention tint fires when *any* account is short — a synthesized household snapshot per day keeps `DayCellRow` unchanged). The **selected day** is account-first (grouped two-pane): both panes group by account under account headers, and the header names any short account next to the household free-to-spend. Driven end-to-end on a crafted two-account DB (Checking short on a $2,500 repair, Savings flush): the calendar flagged the short day, the header read "Checking short", household free was correct ($2,000), and both panes grouped correctly. 147 tests still green; single-account output unchanged.

- **5b · Rich overview cell + account filter — done and verified (2026-07-23).** The calendar cell now matches mockup E: two **labeled** numbers (Total + Free), the day's **top event by name** (highest-priority expected transaction), an explicit **event count** top-right ("3 events" / "No events"), a per-account **flow strip** (letter + ↑ in / ↓ out / • none), and a **⚠ + words** warning. An **account filter** ("All accounts" + one entry per account) re-scopes every number; a day with no activity for the filtered account correctly goes inactive. Verified by driving the app: household Jul 24 `total $7,700 / free $6,700`, Jul 28 `total $3,200 / free $2,200, an account is short`; filtered to Checking the same days read `$2,700/$2,700` and `$200/$200`, and Savings-only days go faint. 147 tests still green.
  - Fixed along the way: event-less cells were rendering bare "TOTAL"/"FREE" labels with no figures (noise) — the number row is now hidden on inactive days. The taller cell needed room, so the window is 820 tall, the selected-day pane 205, and the cell 86.

- **5c · "Cover from another account →" lever — done and verified (2026-07-23).** A short account's group header in the selected day's **fund-jars** pane carries the fix: a button reading "Cover $800 from another account →" that opens the transfer form pointed **into** that account, **from** another one, for **exactly** the amount it is short, as a **one-time** transfer dated the short day. The user still presses Create — we surface the problem and make the fix easy, we don't move their money (philosophy 1). The lever lives in the jars pane because every account always appears there; the events pane only lists accounts that had activity that day, so a short account could have no header to hang it on. Creating the transfer re-runs the forecast, so the flag clears immediately.
  - Verified end-to-end on a crafted DB (Checking short $800 on Jul 28): the lever appeared on Checking's header only, opened with Savings → Checking / 800 / `FREQ=YEARLY;UNTIL=20260728` (one occurrence), and after Create the day's warning cleared, both legs showed as "Transfer from Savings" +$800 and "Transfer to Checking" −$800, and the household total was unchanged (a transfer moves money, it doesn't create any). 147 tests still green.
  - Caught in review: the form's standing default is *monthly until +6 months*, so without an explicit one-time schedule the lever would have quietly signed the user up for $800 every month. It now pre-loads a single occurrence.
  - Known limitation: WPF's `GroupItem` automation peer drops header content, so this button is invisible to UIAutomation (and to screen readers). It renders and clicks correctly — verified by screenshot and a coordinate click — but any future automated UI test cannot reach it. Worth revisiting for accessibility.

**Milestone 5 is complete — the Forecast tab now matches the chosen mockups.** Everything in items 1–6 that was scoped for this phase is built and verified.

**Deferred to the next phase (unresolved design choice):** the warning currently words only the definite **"short"** case. Mockup E also showed a **"thin"** state (free positive but low), which has no defined threshold anywhere — deciding what "thin" means is a cascade/allocation question, not a rendering one, so it belongs with the cascade-tweaking work rather than being invented here.

> **Build/verify notes for future sessions:** `dotnet test` does not build the WPF App (build it explicitly before driving the app). Windows PowerShell 5.1 can't load the net10 assemblies, so craft test DBs with Python's `sqlite3` (raw SQL), not the domain types. Scripting DB cleanup: use `[System.IO.File]::Delete(...)`, not `Remove-Item` on a variable path (a safety guard blocks it).

> **Build note for future sessions:** `dotnet test` does **not** build the WPF App project (it is not a dependency of the test projects), so App/XAML changes go uncompiled and the launched `.exe` will be stale. Build `src/MyMoneyForecast.App/MyMoneyForecast.App.csproj` (or the whole solution) explicitly before driving the app.

Real people keep several bank accounts, each with its own balance; different bills pay from different accounts, and money moves between them (manually or on a schedule). The app currently models exactly one account — a single `CurrentBalance` row feeding a hardcoded `"Primary"` page. This is the plan to make it many.

## Why this comes before the cascade-tweaking ("fine-tuning") phase

Decided 2026-07-21. Multiple accounts is a **structural** change to where money lives and moves; the cascade-tweaking phase (goal readjustment / Q4, earmark-pattern restructuring, the "jar for every expected transaction" semantics) is the **nuanced allocation logic that consumes** the balance/solvency picture accounts define. Building the consumer first would mean defining "am I short?" against a single pool and then redoing it once "short" becomes per-account. So: settle the structural substrate (accounts) first, then build the allocation nuance on top of it, once. (The same reasoning is recorded in the `project_next_phase.md` memory.)

## The foundation: the per-account silo model

The original design is per-account top to bottom — confirmed against [02-uml-diagram.md](../../02-uml-diagram.md) and [03-assumptions-glossary.md](../../03-assumptions-glossary.md):

- An `AccountTransactionPage` is a **self-contained silo**: its own balance seed (`initial_snapshot`), `current_free_amount`, safety cushion (`ideal_safety_cushion` / `safety_priority`), `finance_patterns`, `earmark_patterns`, and its own day-by-day `balance_record` cascade. `FundJar`s hang off each per-account `BalanceSnapshot`.
- The `TransactionLogBook` only aggregates the pages; `1.2.3.a1` keeps the set of account names identical across pages over time.
- **Consequence for the engine:** multi-account = run today's single-account cascade **once per account**. No new cascade *semantics* — the current single-account behavior is the N=1 case. This is why deferring the cascade-tweaking questions is clean.

**The load-bearing requirement (author, 2026-07-21):** because we can't import real bank data, the user enters each account's current balance by hand, and **"having enough money" means "having enough in the *right* account."** A combined balance that covers a bill is not enough if the specific account it charges is short — the bank won't move money to cover it, or not without a fee. Per-account solvency is the entire point of the feature; the core questions (Q1 "free money," Q2 "can I afford X") are per-account questions whose household total is a derived sum.

**Goals and bills are single-account.** A goal is one expected transaction; an expected transaction exists only because a finance pattern generates it (`3.10.a3`, `3.13.7.a2`); `finance_patterns` belongs to one `AccountTransactionPage`. So a goal (and its jar) lives entirely in one account — no spanning, no pooling. Users don't save for one car across three accounts.

**Transfers are the one cross-account mechanism.** There is no "transfer" in the original design; we build one as a philosophy-3 abstraction — a **paired expected transaction** (withdraw from A + deposit the same amount into B, same day), shown to the user as a single "transfer," never as two bill/paycheck rows. Detailed in item 3.

## Scope, in dependency order

1. **The account entity** — settled below.
2. **Filing patterns under accounts** — every bill / paycheck / goal lives in exactly one account's page. **Settled below.**
3. **Transfers** — the paired-leg abstraction as a first-class user concept. **Settled below.**
4. **Engine** — `TransactionLogBookFactory` partitions patterns by account, builds N `AccountTransactionPage`s, and returns per-account results plus a household total. **Settled below.**
5. **UI** — one unified view of all accounts (no per-account tabs). The forecast *display* is largely decided in item 4-C (overview = household summary + "any account short" flag; selected day = account-first sections); item 5's remaining scope is the **account-management surface** (add / rename / delete — incl. item 1-A's "where does the user rename"), **per-account balance / cushion entry** in the top controls, and the **transfer creation window**. *(pending)*
6. **Persistence / migration** — accounts table, account reference on patterns, transfers table; migrate the existing single balance into the first account. **Settled below.**

## Item 1 — The account entity  ·  SETTLED 2026-07-21

Replaces the single `CurrentBalance` row with a real, multi-row account.

- **A. Identity — surrogate integer id (hidden) + a unique display name.** The id is what patterns/transfers reference (item 2), so renaming an account never has to cascade to its bills. The **name is unique** and is all the user ever sees or types (philosophy 2). The original design used the name itself as the id only because it had to match bank-export filenames, and import is deferred — so we are free to add the id. There is **always at least one account**; on first run / migration the default is named **"primary"**, renamable later (where the user does that is an item-5 question).
- **B. Balance & as-of — per-account balance, one global as-of date (today).** Each account holds its own hand-entered current balance; there is a single as-of date for the whole forecast, and every account's cascade seeds from it. The user reads all their balances off their bank in one sitting and enters them as of "now," so there is no reason for staggered seed dates, and the entered number stays equal to today's balance (no projection gap). *Per-account as-of dates ("I reconciled savings last Tuesday") were considered and deliberately left out of v1 — higher friction, unnecessary given the entry flow.*
- **C. Horizon — stays global.** One forecast window across all accounts; not an account attribute.
- **D. Safety cushion — per-account, default 0.** The model puts the cushion on the account, and in a silo world a single global cushion has no home. Behavior is unchanged from the current single-account cushion — just one per silo. Default 0, and the cushion UI is **hidden for accounts the user hasn't given one** (philosophy 2). Cushion *behavior* tweaks remain in the cascade phase.
- **E. Account type — none.** An account is just a named balance silo. The only thing a type would drive is which account earns interest, which is out of scope. (Credit cards — see "Noted for later.")
- **F. Lifecycle — add / rename / delete.** CRUD exists; what deletion does to attached bills / goals / transfers is deferred to items 2 & 6. Create UIs pre-select a default account (the first one).

## Item 2 — Filing patterns under accounts  ·  SETTLED 2026-07-21 (A corrected 2026-07-23)

- **A. No account property is added to `FinancialPattern` — the association is _containment_.** The class documentation (`class documentation.ods`, Properties sheet) defines `FinancialPattern` with exactly ten properties — `finance_id`, `source`, `date_pattern`, `amount`, `amount_tolerance`, `date_tolerance`, `priority`, `mandatory`, `notifications`, `description` — and **no account**. A pattern belongs to an account by living in that account's `AccountTransactionPage.finance_patterns` list: the account is *where the pattern is filed*, not something the pattern carries. `EarMarkPattern` is the same — no account of its own, reached through `finance_id` → its finance pattern (`3.10.a3`). The cushion jar (`finance_id = null`) is the documented exception; it belongs to the account directly.
  - **What that means for this implementation.** Pages are never persisted — the engine rebuilds the onion in memory on every run — so on disk there is only one flat `FinancialPatterns` table holding every account's patterns side by side, and nothing there expresses containment. Storage therefore records *which account each pattern is filed under* (item 6); the repository hands patterns back **grouped per account**; and the factory fills each page's `FinancePatterns` from its own group. From that point on the in-memory model uses pure containment, exactly as documented. **The domain type is not modified.**
  - *(Superseded wording: this item previously said "one account reference on `FinancialPattern`," which wrongly implied a new domain property. The stored account id is a storage-level filing record, not a class property.)*
- **B. Required, defaulting to the default account — and always _visible_.** Every pattern belongs to exactly one account. The create/edit UI must **show the account selector with the default account visibly pre-selected**, never a silent default — it should never be a mystery where a pattern's money lands (philosophy 2 + the transparency requirement).
- **C. A transfer is a pair of one-account patterns, not a two-account pattern.** So no pattern ever names two accounts; the transfer (item 3) is built from two ordinary legs, each filed under one account, presented to the user as one "transfer." Internally they stay independent finance patterns; the user never sees two rows. Their auto-generated descriptions can read like "Transfer from Checking" / "Transfer to Savings" (item-3 detail).
- **D. Reassignment allowed.** Moving a bill / goal to another account after creation is permitted, and the user should have a convenient way to do it. It is structurally clean because jars recompute fresh each forecast — nothing is stuck in the old silo. *The messy interactions it exposes are deferred to the cascade-tweaking phase — see Parked.*
- **E. UI — an account picker showing names** in the create/edit windows (philosophy 2).
- **F. Existing patterns → the "primary" account** on migration — principle noted; mechanics in item 6.

## Item 3 — Transfers  ·  SETTLED 2026-07-21

A transfer is presented to the user as a single object but stored as **two persisted `FinancialPattern` legs plus a thin `Transfer` record that links and validates them** — deliberately *not* generated at forecast time.

- **A. User-facing shape — one "transfer": from account, to account, amount, schedule** (a one-off date or a recurring rule). The user sees and manages one object.
- **B. Storage — two real persisted `FinancialPattern` legs + a `Transfer` record (author decision 2026-07-21, over the generate-at-forecast alternative).** The outflow leg (negative, in the *from* account) and the inflow leg (positive, in the *to* account) are ordinary persisted finance patterns; the `Transfer` holds from / to / amount / schedule and references its two legs. **Why this beats generating legs in the engine:** the whole cascade is already driven by persisted `FinancialPattern`s → `ExpectedTransaction`s, so real leg patterns flow through expected-transaction generation, per-account solvency, deallocation, and timeline display with **zero new cascade code** — no synthesize-patterns step. Precedent: `OneTimeGoalFactory` already creates and persists a linked pattern pair (bill + earmark) from one user action; a transfer is the two-account analogue.
- **C. Validation via an early evaluation sweep**, not inside the cascade. The `Transfer` record's job is to verify its two legs stay consistent (equal-and-opposite amounts, matching dates); the sweep runs before a forecast (a good guard anyway, since Import/Export copies the DB wholesale and external edits are possible). Drift is caught — and can be flagged/repaired — there.
- **D. One-off vs. recurring reuses existing machinery** — a one-off transfer is a single-occurrence rule, a recurring one an rrule (same split as one-time goal vs. repeating pattern). No new scheduling concept.
- **E. Auto-described legs** — "Transfer to Savings" (outflow) / "Transfer from Checking" (inflow).
- **F. Validation rules** — from ≠ to, amount > 0, both accounts exist.
- **G. Solvency pressure falls out for free** — the outflow leg is just a negative expected transaction on the *from* account's cascade; if it drops that account below its reserved jars, the account's existing deallocation handles it. No new engine semantics.

**Two consequences of this choice, handled at later items (not now):**
- The two leg patterns must be **hidden from the normal bill/pattern lists and surfaced only as the unified transfer** (item 5, presentation). Editing is done on the `Transfer` (which rewrites both legs); legs are not edited directly — which is what keeps them from drifting in normal use, with the sweep (C) as backstop.
- The data model needs to **recognize a pattern as a transfer leg** — a link from `Transfer` to its two legs, and/or a back-reference on the leg (item 6, persistence).

**Parked (cascade-tweaking):** whether we *pre-reserve/accrue* toward an upcoming scheduled transfer (a jar filling for it) — instinct is no (moving your own money, not spending it). The leg's `mandatory` flag is the natural knob (non-mandatory ⇒ no auto-accrual today), so this stays a *decision*, not new machinery.

## Item 4 — The engine  ·  SETTLED 2026-07-21

The per-account silo model becomes real code: the factory builds one `AccountTransactionPage` per account and runs the existing cascade on each. **No new cascade _semantics_** — N accounts is just N instances of today's single-account engine.

- **A. Nested iteration.** `TransactionLogBook.log_pages` (pages over *time*) → each page's `account_pages` (one page per *account*) → `balance_record` (days). The account loop nests **inside** the page level. Today one page is built (the `page-length` divergence), so the outer loop is length-1; real multi-page over time (fixed `PageLength` + cross-page runoff) is a *separate* deferred feature — the account partitioning must compose with it, not sit above it.
- **B. `ForecastResult` gains a per-account dimension + a household total.** Today it describes one account's timeline; now it carries each account's timeline (its per-day free, set-aside, jars, events) **plus** a derived household aggregate. "One forecast" becomes "one per account, plus a total."
- **C. Display (the household-vs-per-account question), resolved against [08 §2/§3](../08-forecast-tab-design-philosophy.md):**
  - **Overview (calendar) — a compact per-day summary** (§2's non-goal forbids per-account detail here). Per-day **free = household sum** (may go negative, same red cue as today); per-day **set-aside = household sum**; **attention flag = "is any account short today?"** — a generic *look-closer* that fires even when the household sum is positive, and **does not name the account in the cell**.
  - **Selected day — account-first** (§3 is where per-account truth belongs). Each account gets its **own section** carrying its transactions, its jars, its per-type Q3 health, and its own cushion; the header shows **household free-to-spend with a per-account breakdown**.
  - **Short-account detail:** which account and how short; **what it did automatically** — raided its *own* jars in priority order (existing per-account deallocation; goes negative if its jars can't cover, i.e. debt) — *unchanged*; and the philosophy-1 lever **"Cover from another account →"**: a pre-filled transfer *into* the short account (amount = shortfall, editable). On the next forecast it just recomputes — the transfer leg feeds that account before the shortfall, so it is no longer short and its jars stay intact. No un-raid logic; the stateless recompute does it. Same shape as the existing "Adjust the plan →" nudge, aimed at a transfer.
- **D. Performance** — running the cascade per account is linear in account count; no new concern.

**Engine outputs pinned:** per-account results + a per-day "is any account short?" signal + the household sum — so every display choice above is fed; item 5 is just how they render.

**Boundaries (kept out of cascade-tweaking):** each account auto-stays-solvent by raiding **its own** jars only — there is **no automatic cross-account movement** (moving money between accounts is always the user's explicit choice, philosophy 1). Any smarter "prefer a transfer over raiding jars" *automatic* logic is parked.

## Item 5 — UI  ·  DEFERRED TO A MOCKUP ROUND (last)

The forecast *display* is already decided in item 4-C (overview = household summary + "any account short" flag; selected day = account-first sections with the short-account "Cover from another account →" lever). What remains for item 5 — the **account-management surface** (add / rename / delete, including where the user renames, per item 1-A), **per-account balance / cushion entry** in the top controls, and the **transfer creation window** — will be worked as a round of mockups after the data model (author, 2026-07-22), mirroring how the forecast tab itself was designed (doc 08 + its mockups).

## Item 6 — Persistence & migration  ·  SETTLED 2026-07-22

Built against the existing conventions: decimals stored as invariant-culture TEXT, dates as `yyyy-MM-dd` TEXT, booleans as 0/1, **FKs declared but app-enforced** (SQLite's FK enforcement is off; integrity lives in code, like the `HasLinkedEarMarkPattern` guard), migrations via the `EnsureColumn` ALTER-TABLE helper, and Import/Export as a wholesale file copy.

**New tables**
- **`Accounts`** — `Id INTEGER PRIMARY KEY` (hidden surrogate, item 1-A), `Name TEXT NOT NULL UNIQUE` (the unique-name rule, DB-enforced — SQLite honors `UNIQUE`), `Balance TEXT NOT NULL`, `IdealSafetyCushion TEXT NOT NULL DEFAULT '0'` (per-account, item 1-D).
- **`Transfers`** — `Id`, `FromAccountId`, `ToAccountId`, `Amount TEXT`, plus the standard rrule columns for the schedule. **Owned by the book, not an account** (a transfer spans two accounts).

**Global settings live on `TransactionLogBook`** (author, 2026-07-22). The book is the root of the onion; the global forecast settings (`AsOfDate`, `HorizonEndDate`, and `PageLength` once multi-page is real) are its own state, and `Transfers` are book-level too. Persisted as a **single-row `TransactionLogBook` table** — this replaces the single-purpose `CurrentBalance` row; no separate "settings" table invented.

**Recording which account a pattern is filed under.** *Storage only* — no account property is added to the domain `FinancialPattern` (see item 2-A). The column exists because pages are never persisted, so the flat table is the only place the page's containment can be recorded and rebuilt from on load. `EnsureColumn` adds `AccountId INTEGER NOT NULL DEFAULT 1` to `FinancialPatterns`; the `DEFAULT 1` *is* the backfill (existing patterns → primary, Id 1). The FK to `Accounts` is declared but app-guarded. `EarMarkPatterns` needs nothing at all — it reaches its account via `FinanceId` → its finance pattern (item 2-A).

**Transfer legs.** A nullable `TransferId` back-reference on `FinancialPatterns`. A transfer's legs are found by `WHERE TransferId = ?` (out-leg: From account, negative amount; in-leg: To account, positive). The **pattern-list UI hides legs** (`WHERE TransferId IS NULL`); the **engine reads all patterns including legs** (they feed the cascade). The `Transfers` row is the canonical definition the validation sweep (item 3-C) checks the legs against.

**Migration** (startup, idempotent): create `Accounts`, `Transfers`, and the single-row `TransactionLogBook` settings; add `AccountId` (with its DEFAULT-1 backfill) and `TransferId` to `FinancialPatterns`; then a one-time guarded step — if `Accounts` is empty, create "primary" as Id 1, seeding its Balance + cushion from the old `CurrentBalance` row, and seed the `TransactionLogBook` row's AsOfDate + HorizonEndDate from that row. Fresh installs get a $0 primary + default dates. `CurrentBalance` becomes a dead table afterward (SQLite can't cleanly drop it; harmless). Old backups imported later upgrade on the post-import restart, so **Import/Export is unchanged** — and `LooksLikeValidDatabaseFile` still only requires `FinancialPatterns` + `EarMarkPatterns`, which old and new files both have.

**Deletion policy** (the item 1-F / item 2 deferral, now settled): deleting an account that still holds bills / goals / transfers is **blocked** with an app-level guard (same shape as `HasLinkedEarMarkPattern`) — the user reassigns or removes those first. Chosen over cascade-delete: it happens rarely enough that the friction is acceptable, and we never silently delete the user's bills (philosophy 1). Deleting a **transfer leg** directly is likewise blocked — it is managed through its `Transfer`.

**New repositories:** `AccountRepository` (CRUD + the delete guard) and `TransferRepository` (save = insert the `Transfer` + its two legs; delete = remove both; hosts or feeds the validation sweep). `FinancialPatternRepository` gains `AccountId` / `TransferId` and a leg-excluding query for the pattern list.

## Parked for the cascade-tweaking phase

The **"a jar for every upcoming expected transaction"** idea (author, 2026-07-21) and its three open sub-questions — all of which apply identically regardless of account count, so they are not part of this feature:

1. Outflows only (a paycheck has nothing to reserve *for*)?
2. Does a jar for *everything* reserve money out of free balance for non-mandatory expenses too (changing today's "no jar for a non-mandatory pattern without an explicit earmark"), or is the jar a *visible allocation* while mandatory / priority still governs what is actually reserved?
3. Is an auto-created bill's accrual a user-adjustable lever (spread it, or opt out) — i.e. a real editable earmark pattern per bill vs. a computed jar under the hood?

Plus, surfaced by pattern reassignment (item 2-D): **cross-account funding interactions** — what happens to a manual earmark that pre-allocated funds for a bill/goal from a *specific* account's anticipated income (e.g. a Christmas bonus) when that bill moves accounts; and the standing implicit assumption that a bill/goal's regular funding paychecks land in the *same* account it lives in. Cross-account allocation nuance, not structure.

## Noted for later (viable, out of scope now)

- **Single-account filter** on the forecast view — the default shows all accounts together; a filter to view one at a time is a possible future addition (author, 2026-07-21).
- **Credit-card account** — mechanically doable with the existing classes, and a wanted future feature. Interim: the user models a card as an ordinary bill and/or goal inside some other account. (Author flagged it as wanted-later, 2026-07-21.)
