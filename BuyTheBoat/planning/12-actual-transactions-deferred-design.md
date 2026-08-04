# 12 — Actual transactions: deferred design register

`ActualTransaction` — real, observed money movement, as opposed to the
`ExpectedTransaction`s the patterns generate — is **deliberately not built.**
It has been shelved since [03](03-data-entry-uis.md), and every phase since has
been designed *around* its absence.

That absence keeps producing design decisions anyway. Nearly every feature built
so far has raised a "…and this will need to change when actuals exist" thought,
and until now those were left wherever they happened to be said: a line in a
planning doc, a code comment, a tag. **This file is where they live now.**

---

## How to use this document

**When a consideration about actual transactions comes up — in any session, on
any feature — append it here.** Not just when it's an interesting one. The whole
point is that nobody should have to re-derive this list, or re-read eleven
planning docs, on the day import is finally picked up.

Three rules for entries:

1. **Record the consideration, not a decision.** Nothing here is settled; none
   of it has been built or tested against reality. An entry that reads like a
   ruling will mislead whoever picks this up. Write what was noticed and why it
   matters.
2. **Say where it came from.** Which doc, which feature discussion, which date.
   Half the value of an old note is knowing what context produced it.
3. **Say what it blocks or changes.** "Revisit X" is worth more than "consider
   X."

When actuals are eventually built, this document becomes the input to a real
design pass — not the design itself.

---

## Status today

- [`ActualTransaction.cs`](../src/MyMoneyForecast.Domain/ActualTransaction.cs)
  exists as a **pure stub**: a `sealed record` with the documented property set,
  which nothing constructs. It exists only to mark where import lands and what
  pairing will key on.
- `BalanceSnapshot.ActualTransactions` is always empty.
- `ExpectedTransaction.PairedAmount` / `PairedActualDate` exist but are
  permanently null.
- There is **no pairing mechanism at all** — `pair_actual_event`,
  `unpair_actual_event`, `ScanForMatchs` from the documented original model have
  no C# analogue.

## Why it's shelved

Two reasons, from [03](03-data-entry-uis.md#the-four-data-types): too much data
to hand-enter, and no clean source of real transaction data (spreadsheet paste,
file upload, and bank API are all future work, none started).

A consequence worth remembering: **this also makes `ExpectedTransaction`-driven
pairing behavior untestable** — expected-vs-actual pairing needs both sides, so
none of it can have tests until import exists.

The counterweight, from [03](03-data-entry-uis.md): MiniFundJars is a validated
proof that the four core questions *can* be answered with only patterns + rrules
+ a balance + a date. Actuals raise the ceiling; they aren't load-bearing for
Q1–Q3.

---

## The assumed-pairing debt — the concrete revisit list

The governing philosophy while actuals don't exist, quoted in
[05](05-original-structure-restructure.md): *expected events are assumed to pair
with matching actual events, and decisions that would normally wait for pairing
are made assuming that pairing will happen.*

**Author-supplied rationale (2026-07-10)** for the original "only an actual
event can implicitly pull money out of a fund jar" rule — the historical docs
state the rule but never the reason: *actual transactions were meant to be the
driving force that triggers a deallocation day.* Until import exists, expected
transactions stand in as that trigger.

Every code site leaning on this is tagged. **Regenerate the live list with:**

```bash
grep -rn "ASSUMED-PAIRING\|DIVERGENCE" redesign/MyMoneyForecast/src/
```

Tags as of this writing, all of which are revisit sites for import
(registry and full descriptions in [05](05-original-structure-restructure.md)):

| Tag | What has to be reconsidered when actuals land |
|---|---|
| `(import)` | The stub class itself; every always-empty transaction list. |
| `(fulfillment)` | Pairing fields are dormant-null; all math proceeds as if fulfillment is certain. |
| `(actual-amount)` | `EarMarkEvent.ActualAmount` — "how much actually moved" is unknowable without actuals, always null. |
| `(unpaid-expected)` | `current_unpaid_expected` pinned to 0 — everything on/before the as-of date is assumed already fulfilled and reflected in the entered balance. **This assumption dies the moment a real unpaid bill can be observed.** |
| `(as-of-day-settled)` | A snapshot dated exactly on the as-of date contributes no deltas, because the entered balance already includes that day. |
| `(as-of-day)` | Jar `CurrentAmount` equals `ExpectedAmount` on the seed snapshot, treating everything to date as settled. |
| `(3.13c.a10)` | The implicit jar withdrawal the original design triggers off a **paired actual** is triggered off the expected occurrence directly. |

---

## Considerations already raised, by area

### Deallocation

- **Expected transactions currently play the actuals' role in draining jars**
  ([07](07-deallocation-implementation-plan.md), Step 3 note 3). In the original
  design an actual transaction — even a surprise Xbox — could drain funds saved
  for other things. Without actuals, *expected future* transactions do that
  instead. When actuals arrive, the trigger has to move back, and the test that
  pins current behavior (next month's rent visibly drawing funds out of the
  vacation jar) is describing the stand-in, not the target.
- The deallocation proof in [06](06-deallocation-math.md) is **written in terms
  of actual transactions** ("the amount we cannot deallocate should be the
  current funds plus all actual transactions"). The mapping table in
  [07](07-deallocation-implementation-plan.md#assumed-pairing-mapping-proof-term--our-engine)
  translates each proof term to its expected-transaction stand-in — that table
  is a ready-made checklist of what reverts to its literal meaning.
- **[DEFERRED, from 07]** the meaning of `HasNegativeFreeBalance` after
  deallocation. Author wants more explanation when the time comes.
- **The reset-at-release milestone fix (2026-08-03, planning/14) inherits the same dependency as
  `(3.13c.a10)`.** `FundJar.MilestoneAmount` now resets to "scheduled contributions since the last
  release" instead of a lifetime sum (fixing a defect where a repeating goal's milestone climbed
  forever). The reset is keyed off exactly the same signal the existing `(3.13c.a10)` release mechanism
  already uses — "does this finance id have its own expected transaction today" — because that's the
  only signal available without actuals. *What it blocks:* once actual-transaction pairing exists, this
  reset trigger has to move alongside `(3.13c.a10)`'s own release trigger, from "the expected occurrence's
  date arrived" to "the expected occurrence was paired with a real actual transaction" — the same code
  change moves both, since they're now driven by the identical signal
  (`AppendDeallocationOrGoalReleases`'s `ReleasedFinanceIds`).

### Forecast tab UI

- **Paired transactions get a slightly different style**
  ([08](08-forecast-tab-design-philosophy.md), goal *f*) — so the user can see
  at a glance that an expected bill or paycheck *actually came in*. Expected
  transactions are already shown; "fulfilled" is a styling variant, not a new
  element.
- **A new warning class becomes possible** ([08](08-forecast-tab-design-philosophy.md),
  warning *b*): a bill in the near past that still hasn't been paid, or that came
  in at an unexpectedly different cost. **This is now load-bearing for a Stage 6 decision
  ([19](19-stage6-warnings-levers-shortcuts.md), states 3/4, author's ruling 2026-07-30):**
  a plan's pacing going stale — because its bill moved accounts, or because the paycheck it
  was paced against changed — gets **no dedicated detection or lever at all** right now, not
  even an informational one. The author's reasoning: the user should notice a change that
  drastic themselves, and already has the tools (`RestructureFactory`, break-off) to fix it.
  Real detection belongs here instead, once actuals exist to notice the mismatch against —
  this is that detection's first concrete use case, not just a hypothetical one.
- **Explicit non-goal: do not show every actual transaction**
  ([08](08-forecast-tab-design-philosophy.md)). Once they exist they'll be
  numerous; the **total spent that day** is the right granularity for the
  per-day view.
- **Should the timeline be labeled as a pure forecast today?**
  ([04](04-forecast-timeline-tab.md), open question.) With actuals shelved the
  timeline is entirely `ExpectedTransaction`-driven and nothing is reconciled
  against real bank activity — worth deciding whether the UI says so, so it
  doesn't read as more certain than it is. *This is a decision for now, not for
  later — it stops being needed once actuals land.*

### Accounts

- **Account identity was shaped by import** ([10](10-multiple-accounts.md), item
  A). The original design used the account *name* as its id only because it had
  to match bank-export filenames. Import being deferred is precisely what freed
  the new design to add a hidden surrogate integer id. When import lands, the
  export-file → account mapping needs to be solved explicitly, because the name
  no longer carries it.
- **Per-account as-of dates were deliberately left out of v1**
  ([10](10-multiple-accounts.md), item B) — the rejected use case was *"I
  reconciled savings last Tuesday."* Reconciliation is an actuals concept, so
  this decision is worth revisiting then; the v1 reasoning (one sitting, read
  all balances off the bank, no projection gap) assumes hand entry.

### Import & data entry

- Three candidate sources named, none chosen or started
  ([03](03-data-entry-uis.md)): **spreadsheet paste, file upload, bank API.**
- **Duplicate detection is a known requirement** — `psuedo_functions.txt` states
  it as "given an actual transaction, check if it has a duplicate."
  [01](01-tech-stack-and-testing-strategy.md) notes C# `record` types give the
  structural equality this needs for free, and `ActualTransaction` is already
  declared a `record` for that reason.
- `MadeInBulk` exists on the type to distinguish batch import from hand entry —
  nothing reads it yet.

### Vocabulary — "paired" already means two different things

- **Raised 2026-07-24, while implementing stage 1** (planning/14, finding F20). The word **paired**
  is currently doing two unrelated jobs:
  1. **Expected↔Actual pairing** — "this expected transaction will be matched by a real one." The
     assumed-pairing philosophy, and what this whole document is about.
  2. **Deallocation's `ap` term** — "this purchase is funded by its own jar" (06's Step A).
  Today only meaning 2 is implemented, so the collision is latent. **The moment actuals land, both
  are live at once and the overload becomes a real hazard** — code reading `paired` will mean one or
  the other, and a reader can't tell which without checking. *What it blocks:* nothing yet. *What to
  do:* give meaning 2 a distinct name before import work starts — F20's fix touches exactly this
  code, so whoever takes that is the natural person to rename it.
- **The assumed-pairing philosophy is load-bearing for F20's fix.** Because every expected outflow is
  assumed to occur, a jar saving for one should always fund it, so a release should never be
  cancelled. **When actuals arrive and pairing becomes real, that argument weakens** — an expected
  transaction that never pairs is exactly the case where *not* draining its jar might be right.
  Revisit F20's resolution then.

### Pattern identity & break-off (stage 3)

- **Item 5 — identity-only change: the biller's description/source string changes, nothing else does,
  so nothing pairs.** *Raised: [13's charter](13-adjusting-the-plan-charter.md), item 5, scoped in
  [16](16-stage3-break-off.md), 2026-07-29.* A bank starts describing the same real-world electric bill
  with new statement text (a merger, a rebrand) — the pattern hasn't changed in any way the user cares
  about, but automatic `Source` matching breaks. *What it blocks:* needs a contextual "we found this new
  transaction, is this your electric bill?" offer once pairing exists; no mechanism to design without
  actuals to observe the mismatch against.
- **Item 17 — a bill cancelled in the past and never told to the app.** *Raised: charter item 17, scoped
  in [16](16-stage3-break-off.md), 2026-07-29.* The user stopped paying something (or it stopped being
  charged) without ever recording it — the pattern keeps generating expected transactions that will
  never be fulfilled. *What it blocks:* most likely surfaces as an option on the unpaired-transaction
  warning ("this bill hasn't shown up in N cycles — did it end?"), which needs unpaired-actual detection
  to exist first.
- **F23 — `Source` uniqueness is documented but unenforced, and a break-off's kept predecessor will
  collide with it.** *Raised: [16](16-stage3-break-off.md), 2026-07-29, while scoping item 4's identity
  question.* `4.2.a1` requires `Source` non-null; the pressure map separately states "no two patterns
  share one," but neither `FinancialPattern.Create` nor the persistence layer enforces uniqueness today
  (confirmed in code). Stage 3's break-off mechanism may keep a bounded predecessor pattern around for
  history while the successor continues, and the natural choice — the successor reusing the exact same
  `Source` string, so future bank statements keep matching the same biller text — collides with
  uniqueness the moment it's enforced. *What it blocks:* whether the successor needs a distinct `Source`
  (and if so, how continued bank-statement matching is meant to work at all) can't be settled until
  actual-transaction matching's own rules exist; until then this is dormant, not broken.

### Persistence & the original model's missing surface

- Methods from the documented original model with **no C# analogue yet**; per
  `psuedo_functions.txt`'s placement they belong on `AccountTransactionPage`:
  `pair_actual_event(finance_id, date)`, `unpair_actual_event(...)`,
  `ScanForMatchs()` (searches loose actuals for an unpaired expected to match,
  possibly reaching into adjacent transaction logs), and
  `add_bulk_transactions(transactions)`.
- `is_deallocation_day()` is documented as *"total actual transactions for the
  day exceed free funds available"* — currently satisfied by the expected-
  transaction stand-in.
- `force_expire()` is documented to strip pairings that point outside the page
  while leaving the transactions themselves untouched — only meaningful once
  pairings and multi-page books both exist.
- **`ExpectedTransaction.Cancelled` is respected by the cascade but nothing sets
  it** ([05](05-original-structure-restructure.md)). Per-occurrence editing is
  future work, and it overlaps this area: "this bill didn't actually happen" is
  adjacent to "this bill was paired to nothing."
- Pairing is **symmetric** in the documented model
  ([01-glossary](../../01-glossary-of-terms.md#actualtransaction)):
  `paired_finance_id` and `paired_expected_date` are both set or both null, and
  the `ExpectedTransaction` side mirrors it. Whatever implements pairing has to
  maintain both sides.
- `Source` is the string matched against `FinancialPattern.Source` for automatic
  pairing — the matching rule itself is unspecified.
- `OccurredDate` **cannot be in the future** — a user can't record a transaction
  in advance. A validation rule, already documented, not yet enforced anywhere.

### Build sequencing

[02](02-csharp-sqlite-build-plan.md) puts "expected/actual pairing and
fulfillment" in **phase 5**, alongside cross-page flow and deallocation days,
and explicitly notes it is *not* needed for a program that already answers
Q1/Q3/Q2 for the common case.

---

## Open questions

None of these have been answered anywhere:

- What is the automatic matching rule — how close in date and amount must an
  actual be to an expected before they pair? What happens on ambiguity (two
  candidate expecteds)?
- Does the user confirm every automatic pairing, or only ambiguous ones?
  ([design philosophy 1](../../design-philosophies.md): inform and equip, don't
  automate away agency — which argues against silent auto-pairing.)
- What does the entered per-account balance *mean* once actuals exist — is it
  still hand-entered, or derived from imported transactions? The whole
  `(unpaid-expected)` and `(as-of-day-settled)` family of assumptions rests on
  "the entered balance already includes everything up to today."
- Does an unpaired actual create anything (a one-off pattern? nothing?), or just
  sit there affecting the balance?
- How far back does import reach, and does importing history change what the
  forecast shows for past days?

---

## Related documents

- [05-original-structure-restructure.md](05-original-structure-restructure.md) —
  the assumed-pairing philosophy and the full tag registry.
- [06-deallocation-math.md](06-deallocation-math.md) /
  [07-deallocation-implementation-plan.md](07-deallocation-implementation-plan.md)
  — the proof written in actuals terms, and its stand-in mapping.
- [08-forecast-tab-design-philosophy.md](08-forecast-tab-design-philosophy.md) —
  the "once actuals exist" UI goals.
- [01-glossary-of-terms.md](../../01-glossary-of-terms.md#actualtransaction) —
  the original documented shape of `ActualTransaction` and the pairing
  vocabulary.
- [16-stage3-break-off.md](16-stage3-break-off.md) — items 5 and 17 registered above came from scoping
  this stage; F23 (`Source` uniqueness) was found there too.
