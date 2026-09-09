# 22 — `PlanHealthState`: the Earmark form's plan-health content

**Status: IN PROGRESS.** The detailed working record for the Earmark form's plan-health content —
too large to keep inside [21](21-form-architecture.md). Nothing here is a settled spec ready to
implement wholesale — treat each numbered section's own SETTLED/OPEN markers as the truth. Substantial
work has since landed that isn't written up as its own section here (`IsFirstOccurrencePending`/
`FirstOccurrenceShortfall`, the RRule preview's health highlighting, the Summary chart's third/fourth
lines); the reasoning for each lives in the code's own comments at its site (`EarmarkFormPanel.xaml.cs`,
`SummaryRegion.xaml.cs`, `PlanHealthMessages.cs`) and in the settled-designs mockup, not folded back
into this document's section structure.

**Reading list:** [21](21-form-architecture.md) (the form architecture this feeds into, especially its
Earmark section), [14](14-stage1-allocation-model.md) (the milestone reset-at-release fix this all
depends on), `PlanHealthState.cs` + the `PlanHealthState`-tagged tests in
`TransactionLogBookFactoryTests.cs` (the code as it stands right now), and
[earmark-form-layout-mockups.html](mockups/earmark-form-layout-mockups.html) (the Summary A/B/C
variants — read the actual file, don't rely on this document's paraphrase of it).

## First-payment coverage warning (added 2026-09-04)

The first-payment warning (`FirstOccurrenceShortfall`, above) used to say only "$X short for the
first payment," which reads as "you can't afford this" when it usually means the opposite. It's now
**scoped to one thing — the single most-immediate problem, the first pending payment** — and says
which of two genuinely different situations it is (the app's own "can I afford this?" vs "am I on
track for my goals?"). It deliberately does NOT speak to long-run health or whether the plan is
structurally sound (`IsChronicShortfall`); those have their own homes (the trajectory line and the
RRule preview, §6b). Author's own call, 2026-09-04: this message is *only* the short-term problem.

- **Not set aside yet, but the money's (probably) there** — the plan hasn't earmarked the whole
  payment by its due date, *but free cash on that day covers the gap*. Not a real problem — a nudge to
  set the funds aside.
- **Genuinely short** — even free cash can't cover the not-yet-earmarked part. The urgent case.

**Two shapes** (`PlanHealthMessages.FirstPaymentCoverageLine`), each a plain-language verdict plus a
compact facts line (two normal-line-break lines, no special styling):

| Case | Condition entering the payment day | Message |
| --- | --- | --- |
| On track | set-aside ≥ payment | *(nothing)* |
| Money's (probably) there | free ≥ the gap | "You'll probably have the free cash — it just isn't set aside." · `$185 of $368 set aside · up to $900 could be allocated` |
| Genuinely short | free < the gap | "The Mar 8 payment falls $133 short, even after free cash." · `$185 of $368 set aside · only $50 free` |

The reassuring case reframes free cash as the actionable nudge (**"up to $X could be allocated"**), not
a bare "$X free"; the urgent case names the **date** (its urgency) and drops the total-in-account
figure entirely (the author found `$900 free of $1,355` too much — the point is just that funds exist).

**Standing principle — tentative when reassuring, firm when warning.** The free-funds figure is
deliberately *naive*: it can't see another unallocated expense landing the same day and eyeing the
same cash. So the reassuring case hedges ("probably") — which doubles as a small nudge that *setting
funds aside* is what actually buys certainty (the whole point of the program) — while a shortfall it
does find stays firm, because competing expenses only make a shortfall worse, never better. We chose
NOT to compute same-day contention (author, 2026-09-04): honest hedging beats a false precision that
still couldn't promise the cash. (Note the milestone is the *plan's own* schedule sum, not the bill —
FundJar.cs:30 — so it can sit below the bill; but this warning, the trajectory line, and the chronic
check all compare against the real `goal.Amount`, so a faulty plan can't feed them bad numbers.)

**Data behind it** (`PlanHealthState`, computed in `TransactionLogBookFactory.CalculatePlanHealthStates`
off the snapshot entering the payment day): `FirstOccurrenceShortfall` (the set-aside gap),
`FirstOccurrenceFreeFunds` (`ExpectedFreeAmount`), `FirstOccurrenceDate` (the payment's day, for the
urgent date stamp). Tests: `FirstOccurrenceFreeFunds_reports_the_free_cash_entering_the_first_payment_day`
(`TransactionLogBookFactoryTests`) and the `FirstPaymentCoverageLine_*` cases (`PlanHealthMessagesTests`).

**Where it shows.** On the Expense form — and in the Earmark form's **One-off** mode — it's the Summary
aside's own **third line**, under the `Toward the goal:` region, separated by an empty gap, with no
label of its own (author, 2026-09-04: the remade message belongs under "Toward the goal," not by the
save buttons). In the Earmark form's **Savings-plan** mode, though, it's the **STARTING POINT column's**
own live amber warning (`StartingShortfallWarningText`) instead — that column sits right beside the
amount being typed, and showing the same sentence in the aside too would only duplicate it (author,
2026-09-05, when the aside went live and the two collided). The aside there keeps its other two live
lines (Fund jar, today / Toward the goal); the chart's release highlight stays either way, tying to
whichever warning shows by its shared amber color. The Expense form's **status indicator** next to the
save buttons keeps its own compact `ExpenseStatusLabel` ("Currently overfunded" etc.) — a coarser
register for the same states. All of these read live now (2026-09-05): the aside folds the typed-but-
unsaved plan into a what-if forecast (`ForecastOptions.WithProposedPlan`, or `WithManualEarmark` for a
one-off) and reads real free funds off it — it used to sit on the saved reading, which froze a mandatory
bill's auto-earmark health while its plan was being designed. Only a headless/no-forecast path falls
back to the plain "$X short" gap.

### The Summary aside split (2026-09-04)

The aside was one overloaded slot (jar-state OR trajectory, plus a second fact); it's now **two
labeled, separated regions** so the two rulers can't be confused (author noticed the first-payment
message read as if it were part of "Fund jar, today"):

- **`Fund jar, today:`** — `PlanHealthMessages.JarStateLine`, just "$X saved of $Y milestone", no
  delta (the gap is self-evident from the two numbers; the Concerning popup keeps the delta-carrying
  `CurrentJarStateLine`). Only about right now. **This trims §6a's own settled "— $Z short" append
  for the aside.**
- **`Toward the goal:`** — `SummaryFutureLine` (its shortfall now reads "$Z short **in the long
  run**", to set it apart from the immediate one) or the chronic phrase.

And the whole informational column was **lifted** to the top-right, aligned with the narrative, so it
isn't pushed down below a full-width summary band (the narrative + chart now share the left column).
**This supersedes §6c's "168px, two facts already reads as full" budget** — the regions are labeled
and separated now, and the heavy first-payment message left for the status indicator / its own line.

### The shortfall ladder — shared with the forecast (2026-09-04)

The "set aside vs. genuinely short" split above is really the bottom and top of a **three-rung ladder**
of *how bad a shortfall is / whether it can be fully fixed*. The forecast's selected-day view
(planning/11 §A/§B) surfaces the same kind of problem, so the two pages **share one
vocabulary** — `short`, `free`, `set aside`, `transfer` — even though the forms *narrate* (full
sentences) and the forecast *states* (blunt fragments). Author, 2026-09-04: keeping the words
consistent matters; the sentence shape can differ per page.

| Rung | Meaning | Which page | Fix |
| --- | --- | --- | --- |
| 1 — here, not set aside | Money's free in *this* account, just not allocated | Forms (the reassuring case above); the forecast **nudges** toward it | Set it aside |
| 2 — in another account | Money exists in the household, wrong account | **Forecast only** (the forms are single-account) | Transfer, then set aside |
| 3 — genuinely short | Not enough anywhere | Both | Earn / cut / reschedule |

On the forecast a day is flagged when an **account's free funds go negative** (`ExpectedFreeAmount < 0`).
Which rung that is falls out of the *other* accounts' free cash on the same day, so the app already has
what it needs to classify and to name a donor:

- **Rung 2** (`AccountGroupKey.CanCoverElsewhere`): another account holds enough free to cover the gap.
  Header line two reads *"In another account — the money's in {donor}."* — the donor is named only when
  **one** account alone covers the whole gap; spread across several, it stays generic (*"…in your other
  accounts."*). The lever shows.
- **Rung 3**: no account can cover it. Line two reads *"No other account can cover it."* and the lever
  **disappears** — offering "move money in" when none can be would be a lie.

The account's group header (right pane, where the lever already lived) now co-locates the whole story
rather than burying the amount in the button: **line one** = `{account}  {balance}  Short $X` — the
account's own balance ("how much money is in the account", always shown, red when overdrawn) sits right
after the name, and the `Short $X` how-bad in red to *its* right when short; **line two** = the rung
line; **the lever** = the fix. The lever's own label changed from the vague
`Cover $X from another account →` to **`Move $X in →`** — "cover" hid the *direction* (the app pre-fills
only the destination + amount; the user picks the source in the transfer dialog). "Cover" survives as a
plain verb in the rung-3 *sentence* ("no other account can **cover** it"), where there's no direction to
mistake. The top header names the **type** at a glance: *"…· {account} short on cash"* (distinct from a
savings goal being *behind*, which the jars already show).

**The rung-1 nudge on the forecast.** Not allocating never makes a day *short* — it makes free funds go
*up*, so "free" **overstates** what's truly spare. When the selected day has spare cash
(`householdFree > 0`) but the user has funded **no savings plan of their own** (no jar tied to an
`EarMarkPattern` holds money — auto-reserved mandatory bills don't count), a reminder sits under FREE TO
SPEND: *"Set money aside for your goals so this reflects what's really spare."* A general nudge, not
per-expense math (author's pick, 2026-09-04). Also this pass: the **safety-cushion row is omitted**
entirely when the account has no cushion set, buying back the space the rung lines cost.

**Code + tests.** `AccountGroupKey` (PatternRows.cs) carries `Shortfall`, `CanCoverElsewhere`,
`DonorName` and derives `ShortText` / `MoveInText` / `RungText` / `ShowCoverButton`; the donor-detection
that feeds it is `MainWindow.ShowDayDetail` (a per-day `freeByAccount` pre-pass). The wording is pinned
by `AccountGroupKeyTests`; the donor logic and nudge trigger are WPF-side (no UI test project, verified
by build + run).

---

## 1. What `PlanHealthState` is

One record per Savings Plan (every `EarMarkPattern` sharing one `finance_id` — see 21's own
"Terminology, SETTLED 2026-08-03"), computed fresh inside `TransactionLogBookFactory.CreateForecast`
alongside `GoalShortfall` (which it nests, not duplicates), exposed as
`ForecastResult.PlanHealthStates`. Built so the Expense and Earmark forms both read one
already-computed answer instead of each recalculating it — the original motivation, verbatim: *"the
FinancePattern form is going to need to start answering these questions, and then the EarmarkPattern
form is going to need to calculate the answer for them again for display purposes... best to keep all
this information in one place."*

**Architecture, confirmed:** an immutable record, recomputed each time (not a mutable instance held
and updated in place) — matches `GoalShortfall`/`FundJar`/`ForecastResult`'s existing pattern.
"Updating" it means calling `CreateForecast` again and getting a new value, never mutating fields.

## 2. The properties — full inventory, current as of this document

| Property | Type | Status | Author's question |
|---|---|---|---|
| `FinanceId` | `int` | Built | — (key only) |
| `Shortfall` | `GoalShortfall` (nested) | Built | "Will the fund jars be underfunded in the future... on the day the expense actually happens?" |
| `CurrentShortfallAmount` | `decimal` | Built | "Are the fund jars currently underfunded compared to the milestone amount?" |
| `CurrentOverfundedAmount` | `decimal` | Built | "Do we currently have more funds allocated to this fund jar than our milestone amount?" |
| `IsChronicShortfall` | `bool` | Built | "Will a one time earmark fix the shortfall indefinitely? Or is this a chronic problem?" — **only meaningful for a repeating `FinancialPattern` (confirmed 2026-08-05)**, pointless for a one-time expense |
| `IsWorthWarningAbout` | `bool` | **Built 2026-08-05** — full rule, §5 | "Is the shortage worth warning the user about?" |
| `MostImportantHealthState` | `PlanHealthCategory` | Built | "If multiple of these abnormal health states exist, which one is the most important?" |
| `UnderfundedReleaseDates` | `IReadOnlyList<DateOnly>` | **Built, but to the OLD definition — see §7, redefinition not yet implemented** | "Is there a history of past short closes? Has this already happened?" |
| `ProjectedShortfallStartDate` | `DateOnly?` | **Stub** (defaults to `Shortfall.DueDate` when `ShortfallAmount > 0`, else null) | "When do the projected milestone misses start, so the RRule preview can caption itself?" |
| ~~`CanSkipNextPayment`~~ | — | **Removed 2026-08-05** | Folded into the not-yet-built dramatic-excess threshold (§5) — 2× the *smallest* repeated `EarMarkPattern.Amount` in the Savings Plan (not the goal's own amount — a plan can have more than one concurrent funder at different rates, F27) |

**Ranking rule (`MostImportantHealthState`), confirmed:** a shortage today (`CurrentShortfallAmount > 0`)
always wins; any future shortage (`Shortfall.ShortfallAmount > 0`) beats any excess regardless of
timing; an excess only surfaces when no shortage exists anywhere. Among excesses, today beats a later
one — confirmed by the `MostImportantHealthState_prefers_a_current_overfund_over_a_later_one...` test,
though this specific sub-ordering was this file's own inferred extension before that test locked it in.

**`IsChronicShortfall`'s derivation** (why it needs no simulation): compare the Savings Plan's own
scheduled-contribution total (excluding `StartingAllocation` and manual earmarks — those are exactly
the one-time interventions being tested against) to `Shortfall.AmountNeeded`. If the rate alone would
cover it, any current shortfall is a one-off a catch-up fixes for good; if not, it's structural and
recurs regardless of catch-ups. Mathematically shown equivalent to "would a one-time earmark fix this
indefinitely" — see the worked derivation in chat, 2026-08-04, if the reasoning ever needs re-deriving.

## 3. Architecture constraints and open capability gaps

- **Reuse principle (standing, confirmed repeatedly):** every new question this class answers should
  reuse `CreateForecast`'s existing machinery (the day-by-day `BalanceRecord`, `GoalShortfall`'s own
  formulas) rather than parallel logic. Where a genuinely new computation is needed, it should still be
  the smallest possible addition (a static sum, a filtered re-run), not a second engine.
- **"Beyond `HorizonEndDate`," confirmed intent (2026-08-04):** a Savings Plan is a long-term thing that
  usually outlives a single forecast page/run, and looking ahead here saves the user a trip to the
  Forecast tab. **Process agreed:** any new parameter added to an existing function to support this gets
  proposed to the author individually first — a plain description of the new capability it adds — before
  being added. Not yet actioned.
- **The forward per-occurrence projection gap — BUILT 2026-08-05.** `pageByFinanceId` (a
  `finance_id → AccountTransactionPage` map, built the same way `jarsByFinanceId` already was) threads
  each account's full `BalanceRecord` into `CalculatePlanHealthStates`. One parameter satisfied this
  *and* the account-free-funds item below, since a `BalanceSnapshot` already carries both a jar's
  day-by-day state and `ExpectedFreeAmount` together. Powers `IsWorthWarningAbout` (§5) and the real
  `ProjectedShortfallStartDate` (§2). Caveat unchanged: only covers whatever horizon the caller actually
  requests, which is why the six-month lookahead (§5) needs the caller to request at least that much.
- **The backward-history gap (blocks §7's redefinition) — still open.** Dates *before* `AsOfDate` are
  never walked day-by-day; there is no day-by-day *past* to inspect. Agreed fallback if a retrofit
  (rerunning the cascade with an artificially earlier `AsOfDate`) doesn't pan out: a `TODO`-tagged
  stub property, not left half-designed indefinitely. Not yet attempted either way — genuinely bigger
  than the item above, not a quick follow-on.
- **A separate, deliberately independent mechanism (NOT part of `PlanHealthState`):** "can we afford
  this proposed edit to Amount/recurrence rate" — a feasibility check that applies even to a *currently
  healthy* plan the moment the user starts editing `Amount` or the recurrence rate, independent of
  whether any `PlanHealthState` warning is showing. Being short on funds for the edit wouldn't block
  saving, just inform. Lives in the Earmark form itself, not designed further yet.
- **Realization (2026-08-04) that widens what's editable:** editing `Amount`/recurrence rate on an
  in-progress `EarMarkPattern` isn't a simple in-place mutation — it implies a break-off-at-today
  operation (`RestructureFactory`, already built, Stage 4 item 8), which already exists. So more of the
  Savings-plan-mode fields are legitimately editable than first assumed; this is what makes "cranking up
  a savings plan" (§6) a real, already-buildable lever, not a future one.

## 4. Two TODO candidates for future `PlanHealthState` data (not built, not scoped, just parked)

Raised while discussing what "Amount per occurrence" can and can't fix on its own (2026-08-04):

- **Is this jar being "raided" by something else** (a deallocation or manual withdrawal caused by a
  *different* goal's own shortage)? If so, the fix isn't this plan's rate, it's whatever's doing the
  raiding.
- **Is a *different* Savings Plan (a different `finance_id`) overfunded enough that turning it down
  would free up room for this one?** A household-wide, cross-plan comparison — bigger in scope than
  `PlanHealthState`'s current per-`finance_id` design.

Both would require leaving the current form to act on, so whatever surfaces them should be brief.
Candidate location: near or inside the Summary region (§6).

## 5. `IsWorthWarningAbout` — BUILT 2026-08-05

`DetermineIsWorthWarningAbout` in `TransactionLogBookFactory.cs`, 9 new tests (both non-repeated
branches, both repeated branches, both excess branches, plus the today-short-circuit). Required
threading two new capabilities into `CalculatePlanHealthStates` — the per-occurrence forward walk and
account-level projected free funds (§3) — which turned out to be one and the same thread: each
account's own `BalanceRecord` (already fully computed by the time `CalculatePlanHealthStates` runs)
carries both a jar's day-by-day state and `ExpectedFreeAmount` together, so one new
`finance_id → AccountTransactionPage` parameter satisfied both asks. **Repeated rule 1 folds into rule
3 in the actual implementation** — "next occurrence soon and short" is always a subset of "some
occurrence inside six months is short," so it needs no separate branch; this is a code-organization
simplification, not a behavioral deviation from the spec. Full spec, kept below verbatim for reference:

Author's rules, 2026-08-04, kept separate for non-repeated vs. repeated because the two genuinely
differ:

**Non-repeated (one-time) expected transaction:**
1. Projected `expected_amount` meets/exceeds `milestone_amount` by the time the transaction happens →
   no warning; the goal will be met.
2. Short, and the transaction is ≤2 months away → warn, regardless of size.
3. Short, transaction >2 months away, **and** the shortfall is <half of the account's projected free
   funds on that date → ignore.
4. Short, transaction >2 months away, and the shortfall is ≥half of projected free funds → warn
   (inferred as the symmetric completion of rule 3; confirmed correct via the repeated case's own
   explicit statement of the same boundary, 2026-08-05).

**Repeated expected transaction:**
1. Next occurrence ≤2 months away and even slightly short → warn.
2. Next occurrence >2 months away, and the shortfall is ≥half of projected account free funds that day
   → warn.
3. **Any** occurrence within the next six months is short → warn, independent of 1/2 (confirmed
   2026-08-05: this does *not* mean "ignore" the way the non-repeated far-away case can — a future
   occurrence being short is always worth surfacing for a repeating pattern).
4. The "half of free funds" check is **one shared calculation**, reused in both branches — not two
   separate computations (confirmed 2026-08-05).

**Excess/overfunded case (both kinds):** worth warning when the projected excess on the date of the
next expected transaction exceeds **double the smallest repeated `EarMarkPattern.Amount`** in the
Savings Plan. This is also `CanSkipNextPayment`'s replacement (§2).

**Constants:** 2 months, 6 months, and the "half" ratio must be named, easily-adjustable constants, not
literals. Hunting down *other* pre-existing magic numbers elsewhere in the codebase is explicitly
deferred to a future review pass, not this one.

## 6. Per-region content design — Earmark form

Cross-reference: [21](21-form-architecture.md)'s own Earmark content-inventory table (Step 2), items 3
and 9 particularly.

**What each region is *for*, in plain terms — added 2026-08-05 after repeatedly losing track of which
region carries which job mid-design.** Not technical (no property names) on purpose — this is the
question each region answers, not how it answers it:

| Region | What it tells the user |
|---|---|
| Current jar state | Where does my fund jar stand *right now*, against what it should hold today? |
| RRule preview | Which upcoming dates are at risk — and is this a one-time dip or does it keep happening? |
| Summary | What is this savings plan, overall — what I'm saving for, the plan behind it, and how the whole trajectory looks against the goal? |
| Expense's status indicator | Does the linked plan need attention, and how urgently? |

### 6a. Current jar state (existing helper region) — SETTLED

Additive, not a replacement: the existing "$X saved of $Y milestone" stays, and gets appended with
the delta — `CurrentShortfallAmount`/`CurrentOverfundedAmount` supply the number and the word:

> "$800 saved of $960 milestone — $160 short"

**Gating, confirmed:** only appears when `MostImportantHealthState` is `AlreadyMissing` or
`CurrentlyOverfunded` — the two "right now" categories. For `WillMiss`/`WillBeOverfunded`/`Healthy`,
the plain numbers stand alone, no addition.

**Verbosity:** brief in Savings-plan/EarMarkPattern mode (editing `Amount` only affects future cycles,
can't retroactively fix today's gap — not directly actionable here); fuller in One-off/EarMarkEvent
mode (a catch-up entry *is* the direct fix, made right here).

### 6b. RRule preview (existing helper region, Savings-plan mode only) — SETTLED 2026-08-05

**Mechanism, SETTLED (author):** whatever function builds this helper region takes two optional
parameters, matching the inject-into-free-space pattern from §8: (1) a list or range of dates that get
an altered highlight color, for the occurrences that fall short; (2) an optional message shown below
the (collapsed) occurrence list, in the vertical space freed up there (confirmed collapsible,
2026-08-05). Should update live as the user edits the form, mirroring the RRule editor's own existing
occurrence-preview behavior (the original exemplar for Philosophy 4).

**Gating, SETTLED:** mirrors Current jar state but for the *other* two categories —
`WillMiss`/`WillBeOverfunded`. Every one of the four non-`Healthy` categories now has exactly one home:
today's two in Current jar state, the future two here. `Healthy` shows neither a highlight nor a
caption — absence already reads as "fine," the same logic the transient message below relies on.

**Caption wording, SETTLED.** The caption doesn't need to restate which dates or how many — the
highlight parameter already carries that, so the caption only has to label what the highlighting means
(a word-supplier, not a restatement — §8), deliberately generic regardless of whether that parameter
ends up being a list or a range:

| | Shortfall | Excess |
|---|---|---|
| General caption (shown while `WillMiss`/`WillBeOverfunded` holds) | "Projected short" | "Projected overfunded" |
| Transient confirmation (see below) | "Fixed — no longer short" | "Fixed — no longer overfunded" |

**Refined 2026-08-05 — the shortfall caption itself splits on `IsChronicShortfall`:** "Keeps falling
short" (reused verbatim from the Summary aside — not a new synonym) when the shortfall is structural;
"Projected short" only for the case a single catch-up would actually resolve. Distinguishes "this will
clear up on its own" from "this recurs" right where the highlighted dates are, not just in Summary. No
excess-side equivalent — there's no `IsChronicShortfall`-style property for the overfunded case, so
"Projected overfunded" stands alone.

Supersedes the original illustrative examples ("shortages found across several dates" / "all funding
shortages resolved"), which predated the compositional-density principle (§8) and restated detail the
highlighting itself already shows.

**Data dependency, unchanged:** the highlight parameter's actual values still need the forward
per-occurrence walk (§3) to know *which* dates to pass — `ProjectedShortfallStartDate` (§2) is a stub
standing in for this until that walk exists. The function's *shape* (a list-or-range parameter) is
settled and won't need to change once that data lands; only the wiring is still blocked.

**The transient "fixed" message — wording SETTLED above, mechanism still pending:** shown when a live,
unsaved edit clears a problem that was visible when the form was opened, lasting only until save, and
**not** shown again after a save + reload (since that establishes a new "at open" baseline). This is
**not** `PlanHealthState` data — it requires the form/UI layer to hold the "at open" health state
separately and diff it against a live recompute as the user types; `PlanHealthState` itself stays
stateless. The diffing mechanism itself still waits for the actual form to exist.

### 6c. Summary region (`.summary-block` in the mockup) — the biggest open piece

**Where it lives:** [earmark-form-layout-mockups.html](mockups/earmark-form-layout-mockups.html),
introduced in the "confirmed" note just above Summary A (around line 726) as a consolidated region
replacing Goal detail / Current jar state / the proposed-plan boxes with one narrative + a small chart
+ a caption beside it. **Three variants exist in that file: Summary A** (one-time goal, "Trip to
Japan"), **Summary B** (same goal, One-off adjustment mode, a second chart line for the addition),
**Summary C** (a repeating bill, "Car insurance" — a sawtooth chart instead of a flat-goal one).

**Confirmed assignment (2026-08-04):** home for `UnderfundedReleaseDates`, `IsChronicShortfall`,
`Shortfall.AmountNeeded`-type information, especially in Savings-plan mode. In One-off mode, illustrates
`Shortfall`/`CurrentShortfallAmount`/`CurrentOverfundedAmount` and how the *proposed* isolated earmark
would move the picture — a live, hypothetical preview.

**Real space budget (confirmed by reading the mockup directly, 2026-08-04 — don't assume more room
than this):** narrative = 2–3 sentences max; chart = one compact SVG (`viewBox="0 0 680 156"`) with a
*proven* pattern for a second line (Summary B's own violet dashed addition line); aside caption
(`.summary-aside`, a fixed 168px column) = very short — Summary C already carries two facts there and
that already reads as full.

**Resolved 2026-08-05:** both pieces flagged stale here — Summary C's "Lifetime contributed" aside text
and the rationale box above it, both describing the pre-fix milestone bug — are gone. The aside slot
now carries `IsChronicShortfall`-driven content instead (§6c below); the rationale box was pure mockup
commentary, not real UI, and was simply deleted. See `settled-designs.html` for the current version.

**Settled split by pattern type, 2026-08-05:**

- **One-time goal (Summary A/B).** `IsChronicShortfall` doesn't apply (§2 — no ongoing rate to be
  chronically wrong about). The chart's own accuracy does most of the work once driven by real numbers
  — a plan projected short simply fails to reach the flat goal line by the due-date edge, no caption
  needed to say so. One addition proposed and accepted in principle: a terse aside append, same style as
  Current jar state —
  > "$1,600 saved, on pace for $2,940 of $3,200 needed — $260 short"

- **Repeating bill/goal (Summary C).** Two confirmed additions:
  1. **The chart gains a second line plotting `MilestoneAmount`'s own trajectory** (now meaningful
     post-fix), reusing the exact "second line" pattern Summary B already established for its one-off
     addition. Confirmed "very helpful," 2026-08-05.
  2. **A short accompanying phrase, SETTLED 2026-08-05 — "Keeps falling short."** Deliberately minimal
     (2026-08-05, superseding an earlier, more elaborate 4-way draft that tried to state a count and a
     dollar total per situation): it does no more than flag *that* the shortfall recurs across multiple
     expected transactions — not how many, not by how much — since the new milestone line already shows
     magnitude and frequency visually. Reuses "short" from Current jar state's own settled wording
     (above) rather than introducing "shortfall" as a new UI word.
     **Placement, SETTLED:** the aside's own second fact slot — the one "Lifetime contributed"
     currently (wrongly) occupies — keeping the aside at its established two-fact budget rather than
     growing it. **Gating, SETTLED:** `IsChronicShortfall` alone isn't the trigger — it's a modifier on
     an already-showing shortfall (`MostImportantHealthState` is `AlreadyMissing` or `WillMiss`), not an
     independent announcement.

**Deliberately excluded, not forgotten (from the mockup's own note):** account/priority (formerly part
of "Goal detail") aren't restated in the narrative — whether they need a home elsewhere is open, not
dropped on purpose.

**Revised 2026-08-07 (author's own report: no visible milestone line for a plan clearly contributing
regularly) — two corrections to this section, both resolved by reading `settled-designs.html` directly
rather than this section's own prose:**

1. **The milestone chart line is NOT scoped to repeating patterns only**, contrary to this section's own
   "Repeating bill/goal (Summary C)" framing above. `settled-designs.html`'s own one-time-goal variant
   (the healthy "Trip to Japan" case) shows three chart lines, not two — Goal / "Savings plan (proposed —
   rough, live estimate)" / "Milestone (committed plan)" — the milestone line included. Code now draws it
   for every goal with a savings plan, one-time or repeating (`EarmarkFormPanel.UpdateSummary`,
   `SummaryRegion.DrawChart`).
2. **Both the actual and milestone lines are real, walked, stepped trajectories now, not one point
   extrapolated backward as a straight segment** — the old approach could degenerate to a fully invisible
   line when a plan's contribution and release land the same day (today's real value legitimately $0,
   same as the actual line's own $0). Split at Today, not just for now: `TransactionLogBookFactory`
   only ever cascades day-by-day balances forward from `AsOfDate` (§7's own "backward-history gap" below
   — no historical `BalanceRecord` exists before today), so Start-to-Today stays the older straight-line
   placeholder while Today-onward is genuine per-occurrence, per-reset data off the already-saved
   forecast. The "proposed — live estimate" third line above is still NOT built (same live-hypothetical
   scope this document already carries elsewhere, planning/23 item A).

**Revised again 2026-08-07 (same conversation, author's own follow-up) — one correction to point 2 above,
plus a second, separate bug found investigating it:**

3. **The milestone line's "permanently split at Today" was wrong — that split only genuinely applies to
   the actual line.** `MilestoneAmount`'s reset-at-release rule (3.13.5.4.a1) is pure pattern math with NO
   dependency on starting balance, `ManualEarmarks`, or any other real transaction history — unlike
   `ExpectedAmount`, it was never actually blocked by the backward-history gap, only by nothing having
   computed it that way yet. New: `TransactionLogBookFactory.ComputeMilestoneTrajectory` (a second,
   independent implementation of the same reset-at-release rule `BuildAccountPage`'s own per-day loop
   applies — deliberately not a shared refactor, see that method's own header comment for why; covered by
   its own tests, verified to agree with the loop's tested reset points before relying on it). The
   milestone line now spans the whole plan, Start through the due date, real throughout — no split.
4. **Separate bug, found while verifying the above against goals outside the seed data's first
   account:** `ForecastResult.GetTimeline()` (no args) only ever looks at `PrimaryAccountPage`, which
   silently resolves to whichever account happens to be first when — as in any real multi-account
   household — none is literally named "Primary". Every goal on a later account (Car Insurance Co on
   "Joint Household", Trip to Japan on "Savings" in the seed data) got an always-empty result from it,
   not an occasional miss: no chart lines, no `PlanHealthMessages`-composed aside text, wrong "free
   balance" reads for the One-off over-commit check — all silently falling back to placeholder text
   instead. Fixed with a new `GetTimeline(int financeId)` overload that searches every account's own page
   for the id instead of assuming Primary; `EarmarkFormPanel`'s `GetJarTrajectory` and `BalancesOn` both
   switched to it.

## 7. `UnderfundedReleaseDates` — redefinition status (code has NOT caught up to this)

- **What's actually built right now:** dates on which a goal's own *release* came up short of the full
  amount (extends the existing `FlooredManualEarmarks`-style floor detection to system releases, not
  just manual withdrawals).
- **The redefinition the author actually wants (2026-08-05), not yet implemented:** a count, within a
  fixed lookback (~4 months, itself a named constant — "keep in mind we intend a future version of this
  program to keep a history"), of times a *past* occurrence's `expected_amount` **would have been**
  insufficient **if not for** an isolated (manual) earmark making up the difference. Explicitly
  acknowledges the system only tracks *allocation*, not real-world payment — it's possible funds were
  never fully allocated for a transaction but the real payment happened anyway regardless (no
  `ActualTransaction` exists to know either way).
- **Blocked on** the backward-history gap in §3. **Agreed handling:** try a retrofit of an existing
  function first; if that doesn't pan out, stub it (TODO + default value) rather than leave it half-built.
- This is the one place in this whole document where the **code's current behavior and the settled
  design have diverged** — worth fixing or at minimum re-flagging before anyone treats
  `UnderfundedReleaseDates`'s current values as meaningful.

## 8. New standing principles from this pass (candidates for [11 § C](11-ui-design-and-decisions.md#standing-ui-principles-distilled-from-the-above), not yet logged there)

- **Actionability sets the space budget, not importance alone (2026-08-05).** Information practically
  actionable *within the current form/mode* earns more layout space; information only actionable by
  navigating elsewhere — a different form, or a different mode of the *same* form — should be stated
  briefly. Explicitly not a Philosophy-1 violation: we're not choosing the user's fix, we're choosing
  how much room a fact gets in a specific layout based on whether a lever for it exists right there.
- **A mode can be a "gateway" to another mode of the same form.** Savings-plan mode needs enough
  information for the user to realize "maybe I should switch to One-off mode" — but since that action
  isn't available without switching, it stays brief there, the same as information requiring a
  different form entirely.
- **Compositional density, not one-sentence-per-property (2026-08-05).** Multiple `PlanHealthState`
  properties should weave into *one* short statement, not stack as separate clauses. Two roles a
  property can play: a **word-supplier** (contributes a number, a plural, a word choice like
  "short"/"surplus") or a **template-selector** (decides *which* sentence shape applies at all, without
  necessarily appearing as visible text itself — e.g. `IsChronicShortfall` choosing whether "recurring"
  framing is used). No universal template — different information clusters may need different
  strategies, decided one at a time (matches Current jar state's clean two-word-supplier construction
  vs. Summary C's messier multi-case one).
- **Replace vs. inject, a per-region call.** Sometimes new warning content **replaces** existing default
  text (Summary C's stale "Lifetime contributed" line is a live candidate); sometimes it's **added**
  alongside existing content (Current jar state keeps its "$X of $Y" and appends the delta). Whatever
  functions build these helper regions will need optional parameters for this, proposed case-by-case
  the same way the `CreateForecast` extensions are (§3).

## 9. Suggested future shortcuts (deferred, per the standing "informational content before shortcuts" rule)

- A shortcut to adjust/restructure an earmark pattern to stop a chronic shortfall from recurring.
- The "starting earmark" field's own existence as a form field (21's item 7) — author is "on the fence";
  explicitly deferred to be decided *after* this whole informational-content pass is complete, not now.

## 10. Where the code actually stands right now

`PlanHealthState.cs` (new), `ForecastResult.cs` (`PlanHealthStates` field added) — both still
uncommitted as of this writing. `TransactionLogBookFactory.cs`/`TransactionLogBookFactoryTests.cs` carry
both the milestone reset-at-release fix (committed, `f4fae5a`) and all the `PlanHealthState` build-out
(uncommitted) — see that commit's own message for what's already landed. 283 tests green (231 domain +
52 scenario), 0 warnings, last verified 2026-08-05.

**Updated 2026-08-05 — two pieces of §6 now have real code, not just content design:**
`RecurrenceRuleEditor` (the RRule preview control) takes two new optional capabilities —
`SetHighlight(dates, caption)` — matching §6b's settled mechanism; and `IsWorthWarningAbout` (§5) is
fully built, no longer a placeholder. Neither is wired into an actual host form yet, since the new
unified Earmark form's own middle layout still doesn't exist (planning/21 Step 3) — "Current jar state"
and the Summary region specifically still have nowhere real to build into. `RecurrenceRuleEditor` has no
automated coverage (this project has no WPF UI test project) — verified by a clean build only.
