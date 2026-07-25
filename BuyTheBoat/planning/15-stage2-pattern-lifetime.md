# 15 — Stage 2: Pattern lifetime and the form family

**Status: DESIGN COMPLETE 2026-07-24** — items A, C, D settled; item B's mechanism follows from
F17/F18 without a separate decision. Implementation not started. Stage 2 of the
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

## Still open — carried elsewhere, not decided here

- **Ending an ongoing bill for real** ("I'm cancelling Netflix next month") is charter **item 16**,
  owned by **stage 3**. This stage only has to not make it harder.
- Final wording of the question and its three answers, per
  [11's UI principles](11-ui-design-and-decisions.md#standing-ui-principles-distilled-from-the-above).
