# 15 — Stage 2: Pattern lifetime and the form family

**Status: DESIGN COMPLETE 2026-07-24** — items A, C, D settled; item B's mechanism follows from
F17/F18 without a separate decision. Implementation not started. **Update 2026-07-28:** the *ongoing / indeterminate-length* branch (item A's "keeps going" answer, item B, F17–F19, W9) is **deferred and its mechanism reopened** (see the *Ongoing: deferred and reframed* section below); the distinct `ActiveFrom` early-allocation mechanism was designed here instead, and near-term buildable scope is the payoff helper (C) and the form (D). Stage 2 of the
["Adjusting the Plan" phase](13-adjusting-the-plan-charter.md); covers charter **item 10** entire.

**The stage in one paragraph:** a repeating pattern — bill *or* paycheck — answers one plain
question, *"when does this stop?"*, with three answers: it keeps going, it ends on a date I know, or
it ends when I've paid it off. The user never sees a category name. "Ongoing" is a real flag on the
pattern whose internal end date is quietly extended to the forecast horizon plus a cycle and never
shown; the payoff answer computes an end date from what is owed divided by the payment and says
plainly that it is a floor. The question lives inside the bill form that already exists — no new
buttons.

**Designed before Stage 1 is implemented, deliberately.** The charter identified stages 2 and 6 as
the only two safe to design ahead of that build, because neither consumes the allocation figures
Stage 1 changes. This stage is about *when a pattern starts and stops* — orthogonal to what its jar
does.

### Reading list
1. [13 — charter](13-adjusting-the-plan-charter.md), especially **Constraint 2** (every rrule must terminate).
2. [13a — workaround registry](13a-linearity-workaround-registry.md), entries **W3** (count resolved to until) and **W9** (auto-renewal, proposed here).
3. [14 — stage 1](14-stage1-allocation-model.md) — item D in particular; the savings-plan button interacts with this stage (see F18).
4. [03 — data entry UIs](03-data-entry-uis.md) — the existing form family and the reasoning behind its shortcuts.

---

## The problem

The author's framing, which is the whole item:

> For a car payment an end date makes sense — eventually it's paid off. For an electric bill, asking
> for one is like asking **"how long do you want to have electricity for?"** There's no good answer.
> Users would just pick some arbitrary far-off date and have to remember to renew it later.

**Constraint 2 is why this is hard.** `RecurrenceRule` accepts `Count` only as entry sugar and
resolves it to an `Until` at construction; `Until` is the only bound a constructed rule has. There
is **no representable "forever"** — so "ongoing" has to be built, not just allowed.

## Item A — The classification  ·  **OPEN**

The author asked for better terms than "determinate / indeterminate." The stronger move is to
**not name the categories to the user at all**, and instead ask a plain question whose answers imply
the category — philosophy 2 applied properly, and the same move that replaced the `+/−` amount sign
with "Expense / Income."

> **When does this stop?**
> · It doesn't — it just keeps going · On a date I know · When I've paid it off

That is **three** shapes, not the author's two, because "determinate" splits into two genuinely
different user situations:

| Shape | Example | What the user knows | What we do |
|---|---|---|---|
| **Ongoing** | electricity, rent, Netflix | nothing about an end | keep it alive internally (item B) |
| **Ends on a known date** | a 36-month lease | the date, or the number of payments | today's behaviour, unchanged |
| **Ends when paid off** | a $12,000 loan at $350/month | the total owed and the payment | compute the end for them (item C) |

The middle one is what the form already does. So this stage adds machinery for the first and third
only — the classification exists to route the user to the right one.

**F16 · The same problem applies to income, and the author's framing didn't cover it.** "How long
will you have this job?" is exactly as unanswerable as the electricity question, and a paycheck
pattern needs an `Until` today just like a bill does. Under stage 1 income never gets a jar, but it
absolutely drives the fill rule (the "is a paycheck coming before this is due" test reads income
occurrences). **An expiring salary would silently change how every bill reserves.** Whether the
classification applies to income is a real decision, not an oversight to paper over.

## Item B — How "ongoing" works  ·  **OPEN (mechanism largely determined)**

A flag on the pattern, as the author guessed. The stored `Until` becomes an internal implementation
detail that gets extended, and is **never shown to the user raw** (W9's standing cost).

**F17 · The obvious implementation is ruled out — do not use a far-future sentinel date.** Verified
in `TransactionLogBookFactory.BuildAccountPage` (2026-07-24): occurrence lists are materialized over
each pattern's **entire lifetime**, not the forecast window — deliberately, because accrual toward a
due date beyond the horizon needs to know that date:

```
bill.DatePattern.GetOccurrences(bill.DatePattern.Start, bill.DatePattern.Until).ToList()
```

So setting `Until` to 2999 to mean "forever" would materialize hundreds of thousands of dates per
pattern, every forecast run. **And stage 1 compounds it** — item A makes *every* outflow without a
savings plan take this path, not just mandatory ones.

**Therefore:** extend `Until` to **the forecast horizon plus at least one full cycle** — far enough
that the fill rule can always see the next occurrence past the window, and no further. Recomputed
per run, so it tracks the horizon as the user moves it. This is a bounded, cheap operation and it
keeps the sentinel out of the data entirely.

**F18 · An ongoing pattern's savings plan must be ongoing too.** `EarMarkPattern.Create` rejects a
plan whose `Until` is later than its goal's. If a user presses stage 1's "set up a savings plan"
button on an ongoing bill, the plan gets today's internal end date — and next run, when the bill's
end is pushed out, **the plan's is not, so it silently expires and the jar stops filling.** The flag
has to propagate to the plan, or the plan needs its own ongoing notion. This is the sharpest
interaction between stages 1 and 2 and is easy to miss.

**Open:** what the *form* shows in place of an end date, and whether the computed occurrences are
still previewed (the author explicitly wanted the user to be able to verify the dates look right).

## Item C — The payoff-date helper  ·  **OPEN**

For "ends when I've paid it off": the user knows the **total owed** and the **regular payment**, and
we compute the end date. The author was explicit that interest and fees make this approximate and
that the form must say so.

The fork is **how approximate**:

- **Two inputs** (owed ÷ payment, rounded up). Simplest, asks least — and always **under**states the
  term, because it ignores interest entirely. On a real loan that error is large, not marginal: it
  is the whole reason a 30-year mortgage isn't paid off in 20.
- **Three inputs** (add the interest rate) using the standard amortization term. Materially more
  accurate, one more field, and the rate is a number people can read off a statement.

**Whichever is chosen, W3 applies:** the computed date is stored as a plain `Until` and does **not**
re-derive. Change the payment later and the end date stays where it was. Either the inputs get
stored so it can recompute, or the form says plainly that this is a one-time estimate.

## Item D — The form family  ·  **OPEN**

The author wants simpler per-kind forms with the current one retained as "advanced." That shape
already exists — "Create Bill…" beside "Add New (advanced)…" — so this is about how far to extend
it.

The live constraint is the author's own warning in charter item 20: **don't turn a screen into
button soup.** Adding a button per lifetime shape would do exactly that. The alternative is to keep
the two buttons and put the "when does this stop?" question *inside* the simple bill form, swapping
the end-date section based on the answer — one more question on a form the user is already filling
in, rather than a new entry point to choose between.

---

## Findings (continuing the phase's numbering from [14](14-stage1-allocation-model.md))

| # | Finding | Bears on |
|---|---|---|
| F16 | The unanswerable-end-date problem applies to **income** too, and an expiring salary would silently change how every bill reserves | item A |
| F17 | A far-future sentinel `Until` is ruled out — occurrence lists are materialized over each pattern's whole lifetime, and stage 1 puts every outflow on that path | item B |
| F18 | A savings plan attached to an ongoing pattern silently expires unless the flag propagates to it | item B / stage 1 item D |

## The rulings  ·  **SETTLED 2026-07-24**

| # | Ruling |
|---|---|
| **A-1** | **Three answers to one plain question** — *"When does this stop?"* · it doesn't, it just keeps going · on a date I know · when I've paid it off. **No category name is ever shown to the user.** |
| **A-2** | **Income is classified too.** A paycheck answers the same question; "how long will you have this job?" is as unanswerable as the electricity one, and F16 makes it mechanically load-bearing. |
| **C-1** | **Two inputs — owed ÷ payment — stated plainly as a floor.** No interest rate is asked for. |
| **D-1** | **The question lives inside the existing simple bill form**, swapping the end-date section based on the answer. The two existing buttons stay; no new entry points. |

### Why C-1 is better than the recommendation it beat

The recommendation was to take an interest rate for a materially closer estimate. The chosen option
is stronger on a point that matters more here: **a number honestly labelled as a lower bound is
safer than a number that looks precise and isn't.** An amortization figure still ignores fees, rate
changes and overpayments, but it *presents* as exact — and this app's whole posture is a rough
forecast the user stays in control of. "You'll be paying **at least** until March 2029 — interest and
fees will push this later" is unambiguous in a way "March 2031" is not.

It also costs nothing in capability: a user who wants a better date can type one directly, because
*"on a date I know"* is right there as the sibling answer. The helper is a convenience, not the only
route to an end date.

**W3 still applies** and the form must not pretend otherwise: the computed date is stored as an
ordinary `Until` and does **not** re-derive. Change the payment later and the date stays put.

## What follows without further decisions

- **The occurrence preview stays for ongoing patterns.** The author's own framing asked for it
  explicitly — *"still show the user the RRule results so they can verify that's when the bill does
  happen"* — so an ongoing bill shows its upcoming dates even though it shows no end date.
- **The internal end date is extended to the forecast horizon plus at least one full cycle**, per
  **F17**, and recomputed per run so it tracks the horizon. Never a far-future sentinel, and never
  shown to the user.
- **The ongoing flag propagates to any savings plan attached to the pattern**, per **F18**, or the
  plan silently expires and its jar stops filling.

**F19 · The ongoing flag is a new domain property, and that is a divergence.** The documented class
model defines `FinancialPattern` with exactly ten properties and no such flag
([01-glossary](../../01-glossary-of-terms.md#financialpattern)). Unlike the account association —
which [10 item 2-A](10-multiple-accounts.md#item-2--filing-patterns-under-accounts--settled-2026-07-21-a-corrected-2026-07-23)
resolved as *containment*, deliberately adding nothing to the type — "does this end?" is intrinsic to
the pattern and has nowhere else to live. So this one genuinely is a new property, which makes it a
philosophy-3(b) change: it needs a `DIVERGENCE(pattern-lifetime)` tag at the code site and a row in
[05's registry](05-original-structure-restructure.md#divergence-registry) when built.

## These assumptions do not break (author, 2026-07-28)

Grounding F18 against the model raised two assumptions that must be **preserved — not broken, and not designed around by relaxing them.** This corrects an earlier framing that treated the code's current relaxation of `3.11.2.a2` as settled; it is not. **The resolution is the `ActiveFrom` property (below).**

- **`3.11.2.a2` holds — an earmark pattern's rrule cannot extend beyond *or before* its finance pattern's rrule.** Saving for something in advance does **not** license an earmark that begins before its finance pattern; instead **the finance pattern must itself span the saving period** — which is exactly what `ActiveFrom` gives it. Break-off ([W8](13a-linearity-workaround-registry.md), stage 3) remains the route for genuinely *splitting* a pattern; the brittle "contort the rrule to stretch its start" idea is **rejected** in favor of `ActiveFrom`.
- **`3.13.5.a2` holds — a fund jar can only exist on days inside its earmark pattern's rrule** (unless `finance_id` is None). This is the assumption that makes **F18 mandatory rather than merely nice**: a jar legally cannot outlive its plan, so extending an ongoing bill *without* extending its plan makes the jar die at the plan's end — stranding the accrued balance and leaving the ongoing bill unfunded. Propagating "ongoing" to the plan keeps the plan's rrule (and therefore the jar) alive alongside the bill. Where a jar must live *before* the first occurrence (saving in advance), `ActiveFrom` extends the finance span so the earmark — and its jar — sit legitimately inside it.

### Resolution — the `ActiveFrom` property (settled 2026-07-28)

**No assumption ties a finance pattern's span-start to its first occurrence.** `3.13.7.a1` / `3.13.7.a2` govern only *occurrences* (a pattern day requires an expected transaction; an expected transaction cannot fall on a non-pattern day); a latent lead-in has no occurrences, so both hold vacuously. "`Start` == first occurrence" today is RFC 5545 / Ical.Net mechanics plus how we build patterns — **not** a domain rule. So a pattern may carry a latent lead-in with every assumption literally intact.

**`ActiveFrom`** *(author-approved name, 2026-07-28)* — a date on a `RecurrenceRule` marking when its pattern becomes active for allocation and jar-lifetime, **defaulting to its first occurrence** and settable earlier to fund ahead **without adding an occurrence.** The containment assumptions read the pattern's active span as `[ActiveFrom ?? Start, Until]` (behind a helper — see below).

- **A field on `RecurrenceRule`, sitting *before* `Start` (outside the occurrence range)** — confirmed with the author 2026-07-28. The rrule keeps its own `[Start, Until]` tightly bounding occurrences; `ActiveFrom` is a separate marker to its left, and `GetOccurrences` **ignores it**. (Moving `Start` itself would drag a `Count=1` goal's only occurrence onto the early date, and risk a phantom first occurrence for bills under Ical.Net's non-matching-DTSTART behavior — which is why it sits outside, not within, the range.)
- **Both pattern types carry it** — living on the shared `RecurrenceRule` means `FinancialPattern` and `EarMarkPattern` both have it. The earmark needs it too: `3.13.5.a2` requires the *earmark* pattern's span to cover an early jar, and `3.11.2.a2` then requires the finance pattern's span to cover the earmark's — so the finance lead-in is extended first (the extend-first ruling below).
- **Active span behind a helper** — containment / jar-lifetime now read `[ActiveFrom ?? Start, Until]`, not the raw rrule. Per the author, wrap the "is this date inside the pattern's active span?" test in a small helper or two rather than inlining `ActiveFrom ?? Start` everywhere; it is a trivial comparison, no real performance cost.
- **The lead-in generates no expected transactions — and the assumption for that already exists (`3.13.7.a2`; noted 2026-07-28).** `ActiveFrom` extends the *active span* (jar/earmark containment) but **not** the rrule's occurrence range: repeated expected transactions are still created only on the rrule's own occurrences, never in the `[ActiveFrom, Start)` lead-in. This is exactly `3.13.7.a2` ("an expected transaction cannot exist on a day the date_pattern doesn't specify"), with `3.13.7.a1` giving the converse — it was self-evident before `ActiveFrom` and only needs stating now that a pattern's active span can exceed its occurrence range. **Deliberate asymmetry:** `ActiveFrom` **joins** the cross-page *identity* rule (our extended `1.2.3.10.a3`) but **not** the *ET-generation* rule (`3.13.7.a2` stays bound to the bare rrule).
- **Default is a no-op** — equals the first occurrence unless deliberately stretched, so a normal bill is unchanged and the 163 existing tests stay green.
- **Used sparingly — only when the rrule doesn't already reach today (author, 2026-07-28).** For a pattern created **without user-defined starting funds**, do **not** set `ActiveFrom` if its rrule already encompasses the current date (`Start ≤ today`): the jar exists today via the rrule anyway, and a plan starting today already fits. Set `ActiveFrom` (to the creation as-of date) **only** when the rrule starts in the future — which is exactly when it's needed (a future-dated outflow we want visible/fundable now, or the empty declined-plan pattern below). This refines the earlier over-broad "defaults to present for every new outflow" sketch: present-reaching is the *goal*, but `ActiveFrom` is the *tool* only when the rrule doesn't get there on its own.
- **Advanced-form only** — hidden on "Create Bill…" / "Create One-Time Goal…", shown on the advanced pattern form (Stage 2 item D / [03](03-data-entry-uis.md)).
- **New domain property → a divergence** (same category as the `ongoing` flag, F19): earns a `DIVERGENCE(active-from)` tag + a [05 registry](05-original-structure-restructure.md#divergence-registry) row when built. It **supersedes** the earlier "clever rrule-stretch" candidate and **closes the `3.11.2.a2` discrepancy TODO**. The audit (2026-07-28) found the relaxation is leaned on across **all four creation paths** — the proposer, bills, transfers, and `OneTimeGoalFactory` — not just one-time goals, whenever an outflow's first occurrence is after the as-of date. So restoring `EarMarkPattern.Create`'s lower-bound check requires each path to set `ActiveFrom` to the creation as-of date, and by the sparing rule **only when the outflow is future-dated** (an outflow whose rrule already covers today needs none).
- **Extend-first ordering; `3.13.5.a2` cleanup stays with `BalanceSnapshot` (author, 2026-07-28).** `ActiveFrom` creates a tempting second response when a jar predates its pattern — *extend the pattern to cover it* — which would move the "is this jar valid?" job off `BalanceSnapshot`. **Ruling:** keep `3.13.5.a2`'s enforcement unchanged — a jar outside its earmark pattern's active span is **in violation and removed** — and require any early-allocation shortcut to **extend the span first, then add** (the finance pattern's lead-in before the earmark's, before the jar/isolated earmark). The pattern is always already wide enough when the jar lands, so the snapshot never sees a violation and never decides whether to extend. This falls out of the cascade order — "patterns are perfect" precedes jar creation/cleanup ([06 § process regions](../../06-assumption-dependency-graph.md#process-regions--the-cascade-steps)).
- **Cross-page identity — our own version of the assumption** — because `ActiveFrom` lives on the `RecurrenceRule`, it is automatically part of the "same rrule across pages" match (`1.2.3.10.a3` for finance patterns, `1.2.3c.11.a3` for earmarks). Author (2026-07-28): this gives us a version of those assumptions that differs from the original, which compared only the bare rrule. Deferred with the rest of page-jumping.
- **A declined plan still gets an implicit empty plan + jar (author, 2026-07-28).** Firming up revision decision 1 ("an `EarMarkPattern` even one with no occurrences"): when the user removes/declines the proposed plan, the outflow **still** gets an `EarMarkPattern` with **no contributions**, plus a fund jar that must be visible **from today** — the jar exists (top-up-able, and the shortfall computes as the full unfunded amount) even though it reserves nothing and free funds correctly still count the money as unspent. Reaching today follows the sparing rule above: automatic when the outflow's rrule already covers today, and via `ActiveFrom` only when it's future-dated. **The empty plan is a plain `Count = 1` rrule at the outflow's `Until` with `Amount = 0`, plus `ActiveFrom` for the span** (settled 2026-07-28). `ActiveFrom` does the span work; the rrule stays a completely ordinary single-occurrence rule — **no new type surface.** A first-class empty-rrule abstraction was considered and **rejected** (keep `RecurrenceRule` plain and exposed), as was the fragile non-matching-DTSTART trick. The one accepted consequence: a plain rrule always has ≥1 occurrence, so the empty plan carries exactly one **`$0` earmark event** at `Until` (filterable in the UI) — the sole cost of keeping the rrule plain. **Exception:** the user may *explicitly* choose a pattern that does **not** reach the present. *This closes the earlier gap — a declined plan no longer leaves "no jar to top up"; there is always a jar.*
- **Early-allocation & related UI — capabilities noted, screens unsettled.** The non-forecast UI is unsettled and expected to be reworked, so these are recorded as required *capabilities*, not screens (charter item 20 / stage 6; rows in [13b](13b-user-action-catalog.md)): (a) **adding funds before a pattern's first occurrence** — post-creation this is just the existing manual "Add funds" ([09](09-manual-earmarks.md)) on any day in the plan's span (no new mechanism, since the plan already spans from the creation as-of date); only a genuinely *pre-plan-start* add needs the extend-then-add path; (b) **mixing a lump-sum starting earmark with a regular savings plan** (both, or either, validly fund a goal); (c) a clear way to **decline the proposed plan** while keeping the empty plan + jar above.
- **Break-off's demand shifts both ways (note for stage 3).** `ActiveFrom` absorbs *early-allocation* cases that looked like splits (item 8, "allocate more toward an unchanged goal partway through," is a likely span-stretch, not a split) — so break-off is needed *less* there. But the deferred *indeterminate-length* mechanism (below) may itself **use** break-off for periodic renewal — needed *more* there. Net (author, 2026-07-28): break-off stays for genuine mid-life changes and possibly for renewing open-ended patterns; it is just no longer the tool for early allocation.
- **Implementation scope (open):** fold the field in when the advanced form is next touched; wire the "fund in advance" UX with **item 23** ("allocate toward something not yet scheduled"), which this mechanism is the general answer to.

**Deferred — the multi-page tension.** `1.2.3.10.a3` / `1.2.3c.11.a3` require a pattern to carry an identical rrule across every page it spans, which a per-run, horizon-extended `Until` (W9 / F17) would challenge. Per the author (2026-07-28) this **waits until cross-page / page-jumping work resumes** — currently on hold — and is not a stage-2 blocker while books are single-page.

### Implementation plan — `ActiveFrom` (Step 1 DONE 2026-07-28; Steps 2–3 pending)

The build sequence, ordered so the tree stays green until the one enforcement flip. **"Step" here is a build increment — *not* a charter Phase or Stage** (that reuse caused confusion 2026-07-28 and was renamed).

**Step 1 — Safe prep, nothing enforced (green throughout).**
- Add `ActiveFrom : DateOnly?` to `RecurrenceRuleOptions` / `RecurrenceRule` (default null); `Create` validates `ActiveFrom ≤ Start` when set. `GetOccurrences` is untouched.
- Helpers: `RecurrenceRule.ActiveStart` (`ActiveFrom ?? Start`) and `ActiveSpanContains(date)`, each with a `[CALC]` summary.
- Persistence: a nullable `ActiveFrom` column on both pattern tables; round-trip it.
- Populate it in the four creation paths via the sparing conditional (`if outflow.DatePattern.Start > asOfDate → ActiveFrom = asOfDate`): bill creation (`MainWindow.xaml.cs`), the transfer withdrawal, `OneTimeGoalFactory` (`goal.ActiveFrom = StartSavingDate`), and the declined-plan path (Step 3).
- Nothing checks `ActiveFrom` yet, so every existing test stays green.

**DONE 2026-07-28 — 170 tests green (126 domain + 44 scenario), App builds 0 warnings.** Landed: the `RecurrenceRule` field + `ActiveFrom ≤ Start` validation + `ActiveStart` / `ActiveSpanContains` helpers; persistence — the `ActiveFrom` column went on **three** tables, not the "both" the plan said (`FinancialPatterns`, `EarMarkPatterns`, *and* `Transfers`, since all three round-trip through the shared `RecurrenceRuleColumns`; the Transfers one was caught by a failing test); and `OneTimeGoalFactory` setting `ActiveFrom = StartSavingDate`. **Moved out of Step 1:** the App bill/transfer wiring → Step 2 (WPF UI, not unit-testable, inert until enforcement); the declined-plan path → Step 3.

**Step 2 — Flip on enforcement (the risky increment).**
- Restore the lower-bound check in `EarMarkPattern.Create`: reject when `earmark.DatePattern.ActiveStart < goal.DatePattern.ActiveStart` (the `Until` check already exists). `3.11.2.a2` now holds literally against the active span.
- **Test churn:** the ~9 `AllocationPlanProposer` tests and the `OneTimeGoalFactory` tests construct future-dated outflows directly, so they must set `ActiveFrom` on those outflows or fail the restored check — the bulk of the diff.
- **Migration (correctness gate):** existing persisted goals with save-in-advance earmarks are earmark-before-goal with no `ActiveFrom`. First check whether `EarMarkPatternRepository` re-validates via `Create` on load; either way, backfill `goal.ActiveFrom = its earmark's Start` for those rows (dev-data volume, but required).
- Steps 1 and 2 can be one commit, but keeping the flip isolated makes it reviewable.

**Step 3 — The empty (declined) plan.**
- Domain (landable now): a factory that builds the empty plan — `Count = 1` rrule at `outflow.Until`, `Amount = 0`, `ActiveFrom = asOfDate` — plus setting the outflow's `ActiveFrom`. Unit-testable on its own; emits the single `$0` earmark event at `Until`.
- UI (deferred): the "decline the proposed plan → keep the empty plan" flow rides the unsettled non-forecast UI.

**Cross-cutting:** a `DIVERGENCE(active-from)` tag at the `RecurrenceRule` / `EarMarkPattern.Create` sites + a [05 registry](05-original-structure-restructure.md#divergence-registry) row; update [03-data-entry](03-data-entry-uis.md)'s 2026-07-07 relaxation note to point at the restored check. The F20 regression test (pinned `50m`) should stay green — `ActiveFrom` never touches deallocation.

**New / changed tests:** `RecurrenceRule` (construction, `ActiveFrom ≤ Start`, `GetOccurrences` ignores it, `ActiveSpanContains`); persistence round-trip; `EarMarkPattern.Create` (rejects earmark before the goal's active span, accepts when covered); the sparing conditional; the empty-plan factory; and the proposer / goal test updates.

## Ongoing / indeterminate-length: DEFERRED and reframed (author, 2026-07-28)

The *"it just keeps going"* answer (item A) and its mechanism (item B, F17–F19, W9) are **deferred**, and the mechanism is **reopened**. The earlier design is kept above as the record, but is not what will be built.

- **Not symmetric to `ActiveFrom`, despite both "stretching a span."** `ActiveFrom` extends the *active span* (containment) and **adds no occurrences**; making a pattern open-ended is about the *occurrence-generating rrule* — to schedule more future occurrences you must actually change the rrule. Genuinely different problems; do not conflate them.
- **The horizon-extension approach (W9) is reconsidered.** Rolling `Until` to the horizon each run means the rrule accumulates occurrences from its fixed `Start` forward — *hundreds of past occurrences* over years. The author's alternative: **break off / renew the pattern periodically** so each segment's rrule stays short and bounded. An open-ended pattern would then be a *consumer* of break-off (stage 3), not a horizon-extended sentinel.
- **We still need a way to record that a finance pattern has an indeterminate ending** — some marker distinct from `ActiveFrom` — but its behaviour (renew vs. extend) is unsettled.
- **Deferred because it reaches into other stages** (break-off / stage 3, and the cross-page horizon work already on hold). Until it is taken up, an open-ended bill is entered by picking a concrete end date and extending it as it approaches.

**Consequence for Stage 2's scope:** the near-term buildable part of *"when does this stop?"* is the two **determinate** answers — *ends on a known date* (today's behaviour) and *ends when paid off* (item C's payoff helper) — plus the form question (item D). The third answer waits on the deferred indeterminate-length design.

## Still open — carried elsewhere, not decided here

- **Ending an ongoing bill for real** ("I'm cancelling Netflix next month") is charter **item 16**,
  owned by **stage 3**. This stage only has to not make it harder.
- Final wording of the question and its three answers, per
  [11's UI principles](11-ui-design-and-decisions.md#standing-ui-principles-distilled-from-the-above).
