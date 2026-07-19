# Project Goals & User-Facing Questions

*Why* the system needs to be this complicated — the questions the program exists to answer, which the class model and assumptions all serve. Source material: the four core questions, plus `project goals.txt.txt`, `planning.txt`, and `planning2simple.txt`'s "Show / Warn / Maintain / Plan" framing (see [01-glossary-of-terms.md](01-glossary-of-terms.md#domain-vocabulary) for that framing's short definition).

## The four core "Plan"-side questions

These are the questions you described as the heart of the program:

1. **"How much free (unallocated) money do I have?"**
2. **"Do I have enough to buy X?"**
3. **"Am I on track to reach my finance goals I have set?"**
4. **"If I buy X anyways, how will I have to readjust my finance goals to compensate?"**

### Q1 — How much free money do I have?

Answered directly by `AccountTransactionPage.current_free_amount`. This is deliberately **not** a cascade-computed value (see [01-glossary-of-terms.md](01-glossary-of-terms.md#classes)) — it's calculated fresh, right now, from three things:
- Today's `BalanceSnapshot.full_amount` (what's actually in the account after today's real transactions)
- Minus `current_unpaid_expected` (bills you know are coming any day now that haven't hit yet) — *except* for the portion of an unpaid bill that's already sitting in a `FundJar`, since that money is already spoken for either way
- Minus everything currently sitting in every `FundJar`, including the safety cushion (the `finance_id = None` jar)

Governing assumptions: `3.5.a1`, `1.2.3.5.a1`, `3.6`/`1.2.3.6.a1` (current_unpaid_expected), `3.7.a1`/`3.7.a2` (safety cushion). See [03-assumptions-glossary.md, Chapter 9](03-assumptions-glossary.md#chapter-9-page-identity-bounds--top-level-maintenance-summary).

### Q2 — Do I have enough to buy X?

Not a single stored property — this is a **comparison**: is `current_free_amount` ≥ the cost of X? The interesting design work here isn't the comparison itself, it's what happens around it, which is where the `planning.txt`/`planning2simple.txt` "Warn" category comes in:
- "perhaps shows when a user is very close to running out of free spending money"
- Contextual warnings before a purchase would dip under the safety cushion, or take funds away from a fund jar needed for a mandatory upcoming bill

This is where `FinancialPattern.priority`/`mandatory` and the fund-jar allocation-priority order (see [03-assumptions-glossary.md, Chapter 16](03-assumptions-glossary.md#chapter-16-earmarks) and the `EarMarkEvent` priority rules in [01-glossary-of-terms.md](01-glossary-of-terms.md#classes)) matter: buying X might be affordable in raw dollars but still eat into money that was earmarked for something higher-priority.

### Q3 — Am I on track to reach my goals?

This is the one property built specifically to answer this question: `FundJar.milestone_amount` — "how much you *should* have saved by this date" — compared against `FundJar.current_amount`/`expected_amount` — "how much you actually have." The gap between them *is* the answer to "am I on track."

Governing assumptions: `3.13.5.4.a1` (milestone calculation), plus `10.4.a1`/`10.4.a2`/`10.4.a3` (the milestone rules for the different jar types — the latter two live only in the chart), fed by the `EarMarkPattern`/`EarMarkEvent` history. "Which goal" is identified purely by `finance_id` — see [Finance ID](01-glossary-of-terms.md#domain-vocabulary) in the glossary.

### Q4 — If I buy X anyway, how do I readjust my goals?

**This is the weakest-documented of the four**, worth being upfront about rather than papering over. `planning2simple.txt`'s "Plan" section names the problem directly but doesn't resolve it into assumptions the way Q1-Q3 got resolved:

> "Okay if we have little pools of money set aside for several different things. But then some sudden expense occurs out of nowhere. And all that money we had record as 'set aside' is now gone. What things do we need to do? 1. We will need to change our money allocation plan for this goal. 2. We could also move our goal back if it's not mandatory."

The only concrete mechanism sketched anywhere is in `planning.txt`'s "Unsorted Notes": *replanning* an earmark pattern gives the user a choice between starting a goal's savings over from zero, or keeping the existing fund jar balance and wiping/regenerating the future repeated earmarks around it. That's a real design idea, but it never got turned into numbered assumptions the way, say, deallocation days did (`3.13c.a6` through `3.13c.a10` — see [03-assumptions-glossary.md, Chapter 11](03-assumptions-glossary.md#chapter-11-balance-record)).

**Practical implication:** there is no rich cluster of assumptions answering Q4 the way there is for Q1/Q3. Making the program actually answer "how do I readjust" is new design work, not buried documentation — most likely starting from the deallocation-day mechanism, since deallocation is the *forced, automatic* version of the same problem (money got taken out of a jar unexpectedly; Q4 asks the user to make the same call deallocation makes automatically).

## The secondary goal: tracking regular expenses & upcoming bills

You described this as separate from the planning questions above but still important. It's the "Show" and "Warn" side of the same four-category framing:

- *"When is my next bill coming up?"* → `FinancialPattern.date_pattern` (the rrule) generating `ExpectedTransaction`s ahead of time.
- *"Which bills have already come through, which haven't?"* → the `ExpectedTransaction` ↔ `ActualTransaction` pairing/fulfillment mechanism (see [Pairing / Fulfillment](01-glossary-of-terms.md#domain-vocabulary)), surfaced as a checkmark, or a warning icon when the pairing reveals a deviation.
- *"What should I expect to come in soon that hasn't yet?"* → `current_unpaid_expected`, plus the deviation-warning logic once an expected transaction's `amount_tolerance`/`date_tolerance` window has passed (`7.7.a1`, `7.8.a1`).

This maps most directly onto [03-assumptions-glossary.md, Chapters 14-15](03-assumptions-glossary.md#chapter-14-actual-transactions) (Actual/Expected Transactions) and the "transactions are paired" cascade step in [06-assumption-dependency-graph.md](06-assumption-dependency-graph.md#process-regions--the-cascade-steps).
