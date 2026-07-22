# 10 — Multiple Accounts

**Status:** design in progress (started 2026-07-21). Item 1 (the account entity) is settled; items 2–6 are being worked in order. This is the philosophy-3 "plan before you build" pass — see [design-philosophies.md](../../design-philosophies.md).

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
2. **Attaching patterns to accounts** — every bill / paycheck / goal knows its account. **Settled below.**
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

## Item 2 — Attaching patterns to accounts  ·  SETTLED 2026-07-21

- **A. One account reference on `FinancialPattern`.** A bill / paycheck / goal moves money in exactly one account (bill = where charged, paycheck = where deposited, goal = where the savings sit). An `EarMarkPattern` and its events carry **no account of their own** — they identify their finance pattern by `finance_id`, and that pattern must live in the account that contains them (`3.10.a3`), so an earmark's account is *reached through the `finance_id` link*, never stored separately (nothing to drift). The cushion jar (`finance_id = null`) is the documented exception — it belongs to the account directly, not via a finance pattern.
- **B. Required, defaulting to the default account — and always _visible_.** Every pattern belongs to exactly one account. The create/edit UI must **show the account selector with the default account visibly pre-selected**, never a silent default — it should never be a mystery where a pattern's money lands (philosophy 2 + the transparency requirement).
- **C. A transfer is a pair of one-account patterns, not a two-account pattern.** So `FinancialPattern` needs only the single account field; the transfer (item 3) is built from two ordinary legs, presented to the user as one "transfer." Internally they stay independent finance patterns; the user never sees two rows. Their auto-generated descriptions can read like "Transfer from Checking" / "Transfer to Savings" (item-3 detail).
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

**Pattern → account.** `EnsureColumn` adds `AccountId INTEGER NOT NULL DEFAULT 1` to `FinancialPatterns`; the `DEFAULT 1` *is* the backfill (existing patterns → primary, Id 1). The FK to `Accounts` is declared but app-guarded. `EarMarkPatterns` is untouched — it reaches its account via `FinanceId` (item 2-A).

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
