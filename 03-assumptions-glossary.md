# Assumptions Glossary

Every assumption below is reproduced **verbatim** from its source file (whitespace/indentation normalized for markdown readability only — no words, numbers, or punctuation changed). Each chapter is a direct excerpt, cited by source file and original line range, so you can go back and check it against the original at any time.

This reproduces the assumption text from the `.txt` planning files (`a01.txt`/`a02.txt`/`a03.txt`), which carry prose descriptions the chart doesn't. Where a `.txt` requirement set differs from the same assumption's set in `assumptionChartSimpleLines.uxf`, the chart is authoritative (per `assumptionNotes.txt`) — the chart's box text is reproduced in [05-assumption-chart-full-text.md](05-assumption-chart-full-text.md) and the resulting graph in [06-assumption-dependency-graph.md](06-assumption-dependency-graph.md). This document is the readable prose companion to those.

Each entry keeps the original two-line shape from the source files:
```
{requirement-ids}
-assumption.id: description text
```
The `{...}` line lists the other assumption IDs (by their bare ID) that must hold before this one can. `{none}` / `{}` / `{whatever}` are all reproduced exactly as written in the source — they are **not** equivalent to each other in the original files (see [Appendix C](#appendix-c-formatting-quirks-preserved-as-is) for what each seems to mean).

## Contents

- [How to read an assumption ID](#how-to-read-an-assumption-id)
- **Part I — Foundational class assumptions** *(source: `a02.txt`)*: [Ch.1 TransactionLogPage](#chapter-1-transactionlogpage) · [Ch.2 FinancialPattern](#chapter-2-financialpattern) · [Ch.3 EarMarkPattern](#chapter-3-earmarkpattern) · [Ch.4 ActualTransaction](#chapter-4-actualtransaction) · [Ch.5 ExpectedTransaction](#chapter-5-expectedtransaction) · [Ch.6 EarmarkEvent](#chapter-6-earmarkevent) · [Ch.7 BalanceSnapshot](#chapter-7-balancesnapshot) · [Ch.8 FundJar](#chapter-8-fundjar)
- **Part II — AccountTransactionPage internal consistency** *(source: `a03.txt`)*: [Ch.9 Identity, bounds & top-level summary](#chapter-9-page-identity-bounds--top-level-maintenance-summary) · [Ch.10 Initial Snapshot](#chapter-10-initial-snapshot) · [Ch.11 Balance Record](#chapter-11-balance-record) · [Ch.12 full_amount & expected amounts](#chapter-12-balance_recordcfull_amount--expected-amounts) · [Ch.13 Fund Jars](#chapter-13-fund-jars) · [Ch.14 Actual Transactions](#chapter-14-actual-transactions) · [Ch.15 Expected Transactions](#chapter-15-expected-transactions) · [Ch.16 Earmarks](#chapter-16-earmarks)
- **Part III — TransactionLogBook & cross-page consistency** *(source: `a01.txt`)*: [Ch.17 Pattern continuity across pages](#chapter-17-log_pages-finance-pattern--earmark-pattern-continuity-across-pages) · [Ch.18 Derived calcs & initial-snapshot inheritance](#chapter-18-accounttransactionpage-derived-calculations--initial-snapshot-inheritance) · [Ch.19 Expected/Actual pairing completeness](#chapter-19-expectedactual-transaction-pairing-completeness)
- [Appendix A: Reduction & merge candidates](#appendix-a-reduction--merge-candidates)
- [Appendix B: Use cases](#appendix-b-use-cases)
- [Appendix C: Formatting quirks preserved as-is](#appendix-c-formatting-quirks-preserved-as-is)

---

## How to read an assumption ID

Verbatim from `planning2simple.txt` (its own legend section):

> An assumption is listed under the lowest class that has all the information needed to determine its truth value.
> The first number of an assumption corresponds to the # of the class mentioned above.
> The proceeding numbers of an assumption correspond to the property# of the assumption.
> If a property is a list, all assumptions under it can refer to the list itself or all items inside the list.
> after the property#s, the final item in the id of the assumption is 'a' followed by a number.
> example: 3.8.4.a1 is the first assumption about the date_tolerance(prop 4) of an item in finance_patterns (prop 8) of an instance of AccountTransactionPage (class 3)
>
> a class-instance is "self-valid" if it satisfies all assumptions listed under its class type
> if instance-A (of class A) derives a value from a property of instance-B (class of B), then we assume that instance-B is self-valid
> if class P has a property of class C and class P needs a non-None value in the property to be self-valid, instance-C must be self-valid before instance-P can be self-valid.
>
> rules x -> (y)
>     assumptions under x are all preconditions for assumption Y
>
> {a, b, c}
>     d: ....
>     assumptions a, b, and c are preconditions for assumption d.
>     note: a, b, and c, being true does not mean d is true. Additional action may need to be taken to make d true.
>         if a, b, and c are true, such an additional action can be performed immediately.
>
> {a,!b}
>     c: ...
>     a must be true and b must not be true for assumption c to be true.
>
> thing
>     [s,t,r]
>     thing directly breaks assumptions s, t, and r
>
> thing
>     &[s,t,r]
>     assumptions s,t, and r will be broken by thing or after thing somehow
>
> 1.2.a3   assumption 3 for the 2nd property of class 1
> 2.5.3.a1 assumption 1 for the 3rd property of the 5th property of class 2
>     single Previous
>     single Next
>        all Before
>        all Following
>     note the capitalized letters. These can be used in the assumption numbers (after the number of the property that is the instance) to indicate the rule only applies to certain instances.
>         the generic version of this is "c" which stands for Current. And only applies to a single instance. This is only used for balancesnapshots and pages.
>     example:    1.2c.a3 means assumption 1.2.a3 must be true for a single "current" instance of the 2nd property in class 1.
>                 1.2b.a3 means assumption 1.2.a3 must be true for all instances of the 2nd property in class one before the "current" instance
>         note: "current" doesn't mean the instance of the current day. It's just a single instance that exists in a list as a reference point for assumptions about that class.
>
>     x means we haven't decided on a number to give this.
>
> if rule 1.2b.a3, 1.2c.a3, and 1.2f.a3 are all true, 1.2.a3 is true and vice versa.

Class numbers used throughout, for reference: **1**=TransactionLogBook, **2**=TransactionLogPage, **3**=AccountTransactionPage, **4**=FinancialPattern, **5**=EarMarkPattern, **6**=ActualTransaction, **7**=ExpectedTransaction, **8**=EarMarkEvent, **9**=BalanceSnapshot, **10**=FundJar. See [Naming quirks & open questions](01-glossary-of-terms.md#naming-quirks--open-questions) in the glossary for two places (`FinancialPattern`, `ActualTransaction`) where the ODS's later property numbering drifted from this scheme.

---

## Part I — Foundational class assumptions
*Source: `a02.txt` (2021-08-10) — the base, unsuffixed leaf-class assumptions.*

### Chapter 1: TransactionLogPage
*(`a02.txt`, lines 1-7)*
```
{none}
-2.1.a1 start_date cannot be None. Must be before end_date.
{none}
-2.2.a1 end_date cannot be None. Must be after start_date
{3.a1}
-2.3.a1 account_pages cannot be None. Can be empty dictionary
```

### Chapter 2: FinancialPattern
*(`a02.txt`, lines 10-14)*
```
{none}
-4.1.a1: finance_id cannot be None
{none}
-4.2.a1: source cannot be None
```

### Chapter 3: EarMarkPattern
*(`a02.txt`, lines 16-22)*
```
{none}
-5.1.a1: self.finance_id cannot be None.
{none}
-5.2.a1: self.date_pattern cannot be None
{none}
-5.3.a1: self.amount cannot be None.
```

### Chapter 4: ActualTransaction
*(`a02.txt`, lines 24-32)*
```
{none}
-6.1.a1: self.source cannot be None.
{none}
-6.4.a1: If self.paired_expected_date == None then self.paired_finance_id == None.
{none}
-6.5.a1: self.made_in_bulk cannot be None
{none}
-6.6.a1: self.amount cannot be None.
```

### Chapter 5: ExpectedTransaction
*(`a02.txt`, lines 34-52)*
```
{none}
-7.1.a1: self.finance_id cannot be None.
{none}
-7.3.a1: cancelled cannot be None
{none}
-7.4.a1: If self.paired_actual_date == None then self.paired_amount == None. If self.paired_actual_date != None then self.paired_amount != None
{none}
-7.4.a2: self.paired_amount == None if self.cancelled == true
{none}
-7.5.a1: if self.paired_amount == None then self.paired_actual_date == None. if self.paired_amount != None then self.paired_actual_date != None.
{none}
-7.5.a2: self.paired_actual_date == None if self.cancelled == true
{none}
-7.6.a1: self.expected_amount cannot be None.
{none}
-7.7.a1: self.amount_tolerance cannot be None, and cannot have a negative value for either number.
{none}
-7.8.a1: self.date_tolerance cannot be None. and cannot have a negative value for either number.
```

### Chapter 6: EarmarkEvent
*(`a02.txt`, lines 54-66 — note the source file itself titles this section "EarmarkEvent," while other sources use "EarMarkEvent"; see [Naming quirks](01-glossary-of-terms.md#naming-quirks--open-questions))*
```
{none}
-8.1.a1: finance_id cannot be None if self.repeated_earmark == True
{none}
-8.2.a1: earmark_date cannot be None.
{none}
-8.3.a1: repeated_earmark cannot be None. default is false.
{3.13.8.4.a1, 3.13b.8.4.a2, 3.13c.8.4.a2, 3.13f.8.4.a2, 8.4.a2}
-8.4.a1: expected_amount cannot be None.
{none}
-8.4.a2: self.expected_amount must be = an explicitly given amount from user if self.repeated_earmark is false on a normal day
{none}
-8.5.a1: self.explicit_amount must be None if self.repeated_earmark == true
```

### Chapter 7: BalanceSnapshot
*(`a02.txt`, lines 68-82)*
```
{none}
-9.1.a1: snapshot_date can be None. If it is None, actual_transactions, expected_transactions, and earmark_events are empty lists.
{10.3.a1, 10.4.a1}
-9.5.a1: self.fund_jars must have a single fund jar with a finance_id of None
{none}
-9.5.1.a1: all funds jars in self.fund_jars must have a unique finance_id.
{none}
-9.6.2.a1: self.actual_transactions[all].occurred_date == self.snapshot_date
{none}
-9.7.6.a1: self.expected_transactions[all].expected_amount == self.snapshot_date
{none}
-9.8.2.a1: self.earmark_events[all].earmark_date == self.snapshot_date
{none}
-9.8.6.a1: self.earmark_events[any].actual_amount must be = None if this is an isolated earmark and there is also a repeated earmark on this day.
```
> Note: `9.7.6.a1` as written compares `expected_transactions[all].expected_amount` to `self.snapshot_date` — almost certainly meant to say `expected_date`, by analogy with `9.6.2.a1` (`occurred_date`) and `9.8.2.a1` (`earmark_date`) which follow the identical pattern one property over. Reproduced exactly as written; flagging so it isn't propagated as intentional when the assumption graph gets rebuilt.

### Chapter 8: FundJar
*(`a02.txt`, lines 84-88)*
```
{none}
-10.3.a1: self.expected_amount is = None if self.finance_id is None.
{none}
-10.4.a1: self.milestone_amount should be = None if self.finance_id is None
```

---

## Part II — AccountTransactionPage internal consistency
*Source: `a03.txt` (2021-09-28) — introduces the b/c/f/p instance-suffix notation. Section headers below are the file's own (`---Section---` markers), reproduced as chapter titles.*

### Chapter 9: Page identity, bounds & top-level maintenance summary
*(`a03.txt`, lines 1-71 — includes the file's two un-headed lead assumptions plus its core-properties block)*
```
{
	1.2.3c.12.a2: the initial snapshot for this page c is maintained,
	3.13.2.a4: balancesnapshots inside self.balance_record have the correct value for full_amount
	3.13.3.a1: self.balance_record[i].expected_amount = (self.balance_record[i-1].full_amount if self.balance_record[i-1].full_amount != None else self.balance_record[i-1].expected_amount) + SUM(self.balance_record[i].expected_transactions[all].expected_amount)
	3.13.7.a5: expected transactions are maintained,
	3.13.6.a3: actual transactions are maintained,
	3.13.8.a8: has all earmarks including implicit earmarks. All earmarks are maintained,
	3.13.5.a5: all fund jars are maintained,
	3:13.a9: have expected amounts up to date,
}
-3.a1: this page is maintained

{
	3.13.5.4.a1: self.milestone_amount should be = sum of all expected values of repeated earmarks up to and including this date (unless it has a finance_id of None)
	3.5.a1: current_free_amount = None if today is not between self.start_date and self.end_date,
	1.2.3.5.a1: current_free_amount is correct if today is on current page,
	1.2.3.6.a1: current_unpaid_expected = total expected_amounts of unfufilled uncancelled expected transactions that occured today or earlier (that are in active pages)
	3.7.a1: current_safety_cushion = None if today is not between self.start_date and self.end_date,
	3.7.a2: current_safety_cushion = money in today's fund jar with finance_id == None
}
-3.a2: this page has updated conciously-applied calculations (fundjar.milestone_amount, page.current_free_amount, page.current_unpaid_expected, current_safety_cushion, )

{none}
-3.1.a1: account cannot be None
{none}
-3.2.a1: start_date cannot be None
{none}
-3.2.a2: start_date must be a date before end_date
{none}
-3.3.a1: end_date cannot be None
{none}
-3.3.a2: end_date must be a date after start_date
{none}
-3.4.a1: expired cannot be None

{3.2.a1, 3.2.a2, 3.3.a1, 3.3.a2}
-3.5.a1: current_free_amount = None if today is not between self.start_date and self.end_date
{1.2.3c.12.a2}
-3.7.a1: current_safety_cushion = None if today is not between self.start_date and self.end_date
{1.2.3c.12.a2}
-3.7.a2: current_safety_cushion = money in today's fund jar with finance_id == None

{none}
-3.8.a1: ideal_safety_cushion cannot be None

{none}
-3.9.a1: safety_priority cannot be None

{3.2.a1, 3.2.a2, 3.3.a1, 3.3.a2}
-3.10.a1: self.finance_patterns should only contain patterns that end after self.start_date and begin at or before self.end_date

{none}
-3.10.a2: self.finance_patterns cannot contain any duplicate ids

{	1.2.3c.11.a4: all earmark patterns for this page c are perfect. (but not the events in the page),
	8.1.a1, 8.2.a1, 8.3.a1, 8.4.a1, 8.4.a2, 8.5.a1, 3.13.8.1.a3, 3.13.5.a2
	10.1.a1, 10.2.a1, 10.3.a1, 10.4.a1,
	7.1.a1, 7.3.a1, 7.4.a1, 7.4.a2, 7.5.a1, 7.5.a2, 7.7.a1, 7.8.a1, 3.13.7.a2, 3.13.7.a4
	1.2.3c.10.a4, 4.1.a1}
-3.10.a3: if an EarMarkPattern, EarMarkEvent, FundJar, or ExpectedTransaction exists in our page with a finance_id of x and our page has no finance pattern with a finance_id of x, that EarMarkPattern, EarMarkEvent, FundJar, or ExpectedTransaction shouldn't exist

{5.1.a1: self.finance_id cannot be None}
-3.11.1.a1: all earmark patterns self.earmark_patterns should have unique finance_ids.

{5.2.a1: self.date_pattern cannot be None}
-3.11.2.a1: self.earmark_patterns should only contain patterns that end after self.start_date and begin at or before self.end_date

-3.11.2.a2: an earmark pattern's date_pattern cannot extend beyond or before the date_pattern of it's associated finance_pattern.
```
> Note: `3.10.a3`'s requirement set references `10.1.a1` and `10.2.a1` (FundJar properties 1 and 2), which don't appear as defined assumptions anywhere in `a01`/`a02`/`a03`. Reproduced as written — likely FundJar properties that never got a dedicated assumption written for them (FundJar's own chapter, [Chapter 8](#chapter-8-fundjar), only covers `10.3.a1`/`10.4.a1`).
> Note: `3.11.2.a2` above has no `{...}` requirement line before it in the source file at all — reproduced exactly as found.

### Chapter 10: Initial Snapshot
*(`a03.txt`, lines 73-95, under the file's own `---Initial Snapshot--------` header)*
```
{1.2.3c.12.a2}
-3.12.a1: initial_snapshot cannot be None

{none}
-3.12.1.a1: self.initial_snapshot.snapshot_date = None

{	1.2.3c.11.a4: all earmark patterns for this page c are perfect. (but not the events in the page),
	3.13.5.a1: if the day exists within EarMarkPattern A's date_patern(any day between start and end), a fund jar with A's finance_id must exist on that day,
	3.13.5.a2: a fund jar can only exist on days that fall within its earmark pattern's rrule (unless the fund jar's finance_id is None),
}
-3.12.5.a1: initial_snapshot.fund_jars cannot have a fund jar that is not in our ?initial finance_patterns? (except the fund jar with None as a finance_id)

{1.2.3c.11.a4, 3.13.5.a2}
-3.12.5.a2: no fund jar can exist here if it has a finance pattern that starts after (or on?) ?self.start_date?

{none}
-3.12.6.a1: self.initial_snapshot.actual_transactions == []
{none}
-3.12.7.a1: self.initial_snapshot.expected_transactions == []
{none}
-3.12.8.a1: self.initial_snapshot.earmark_events == []
```
> The `?...?` markers (e.g. `?initial finance_patterns?`, `(or on?) ?self.start_date?`) are the author's own in-line uncertainty markers in the source — reproduced as written, not a transcription artifact.

### Chapter 11: Balance Record
*(`a03.txt`, lines 97-131, under `---Balance Record----`)*
```
{3.13.6.a2, 3.13.7.a4, 3.13.8.a7, 3.13.5.a3, 3.10.a3}
-3.13.a2: all obsolete events of all kinds have been removed, obsolete fund jars removed, and all events exist that should (not including fund jars) (not including implicit earmarks which may be added later)

{	3.13.a2: all obsolete events of all kinds have been removed, obsolete fund jars removed, and all events exist that should (not including fund jars) (not including implicit earmarks which may be added later),
	1.2.3.13.a3: this current accounttransaction page c is ready to remove balance snapshops with no...
}
-3.13.a3: each balance snapshot inside balance_record has at least one event (earmark, actual event, or expected event). All other balance snapshots have been removed.

{3.13.6.a2, 3.13.a3}
-3.13.a4: full_amount can be safely calculated for balance snapshops

{3.13b.6.a1, 3.13b.6.a2, 3.13b.7.a3, 3.13b.7.a4, 3.13c.6.a1, 3.13c.6.a2, 3.13c.7.a3, 3.13c.7.a4, 3.13f.6.a1, 3.13f.6.a2, 3.13f.7.a3, 3.13f.7.a4}
-3.13.a5: we have enough information to pair actual and expected events

{3.13p.2.a1, 3.13.5.a4, 3.13p.5.2.a5, 3.8.a1, 3.9.a1, 3.13c.8.a5, 3.13.6.a2, 1.2.3.13.a2}
-3.13c.a6: we know whether or not this is a deallocation day. And implicit earmarks are cleared.

{3.13c.a6, 3.13c.8.a7, 1.2.3.13.a2, 9.5.a1, 9.5.1.a1,}
-3.13c.a7: if this is a deallocation day, we have added the new implicit isolated earmarks to earmark_events
	&[3.13c.8.1.a1, 3.13c.8.a5]

{3.13c.a7, 3.13c.8.1.a1, !3.13c.8.a5, 3.13c.8.4.a2}
-3.13c.a8: if this is a deallocation day, the newly added earmarks are now properly merged into existing isolated earmarks if they existed.

{7.6.a1, 3.13.5.3.a1, 3.13.8.6.a1, 3.13.8.6.a2}
-3:13.a9: have expected amounts up to date

{3.2.a1, 3.2.a2, 3.3.a1, 3.3.a2}
-3.13.1.a1: all BalanceSnapshots inside self.balance_record should have a snapshot_date that is => self.start_date  and is =< self.end_date
```
> `3:13.a9` uses a colon instead of a period — reproduced exactly as written; it's the same assumption referred to as `3.13.a9` everywhere else it's cited as a requirement.
>
> Author's own note attached to this section (appears twice, verbatim, in the source): "Note: balance_record is a dictionary. So for these assumptions, i is a date, and i-1 is the nearest previous date in the dictionary."

### Chapter 12: `balance_record[c].full_amount` & expected amounts
*(`a03.txt`, lines 135-162, under `---balance_record[c].full_amount------------`)*
```
{3.13c.2.a2, 3.13c.2.a3}
-3.13c.2.a1: balancesnapshot c has proper full_amount

{3.13.a4, 1.2.3.12.2.a1, 1.2.3.12.2.a2, 3.13p.2.a2, 3.13p.2.a3}
-3.13c.2.a2: if balancesnapshot c in self.balance_record occurs after today, c.full_amount = None

{3.13.a4, 1.2.3.12.2.a1, 1.2.3.12.2.a2, 3.13.6.a2, 3.13p.2.a1}
-3.13c.2.a3: if balancesnapshot c in self.balance_record occurs today or before, c.full_amount = p.full_amount + sum(x.amount for x in c.actual_transactions)

{3.13c.2.a1, 3.13f.2.a1}
-3.13.2.a4: balancesnapshots inside self.balance_record have the correct value for full_amount

{	1.2.3c.12.a1,
	3.13p.2.a1,
	3.13p.3.a1,
	3.13.7.a4,
	3.13.a3
}
-3.13.3.a1: self.balance_record[i].expected_amount = (self.balance_record[i-1].full_amount if self.balance_record[i-1].full_amount != None else self.balance_record[i-1].expected_amount) + SUM(self.balance_record[i].expected_transactions[all].expected_amount)

{	1.2.3.12.3.a1, 1.2.3.12.3.a2,
	1.2.3.12.4.a1, 1.2.3.12.4.a2,
	3.13.3.a1,
	3.13.5.3.a1
	1.2.3.12.5.a1, 1.2.3.12.5.a2
}
-3.13.4.a1: self.balance_record[i].expected_free_amount = self.balance_record[i].expected_amount - SUM(self.balance_record[i].fund_jars[all].expected_amount)
```

### Chapter 13: Fund Jars
*(`a03.txt`, lines 164-210, under `---Fund Jars-----------`)*
```
{	1.2.3c.11.a4: all earmark patterns for this page c are perfect. (but not the events in the page)}
-3.13.5.a1: if the day exists within EarMarkPattern A's date_patern(any day between start and end), a fund jar with A's finance_id must exist on that day.

{	1.2.3c.11.a4: all earmark patterns for this page c are perfect. (but not the events in the page)}
-3.13.5.a2: a fund jar can only exist on days that fall within its earmark pattern's rrule (unless the fund jar's finance_id is None)

{3.13.1.a1, 1.2.3c.11.a4, 3.13.5.a2}
-3.13.5.a3: all obsolete fund jars removed

{3.13.8.a7, 3.13.8.a6, 3.13.5.a1, 3.13.5.a2}
-3.13.5.a4: has all fund jars (the amount might not be accurate)

{3.13b.5.2.a5,3.13c.5.2.a5,3.13f.5.2.a5,
3.13.5.3.a1, 10.3.a1}
-3.13.5.a5: all fund jars are maintained
	&[1.2.3n.12.5.a1, 1.2.3n.12.5.a2: initial snapshot fund jars are deepcopy of last page's last balancesnapshot]

{	3.13c.2.a2: if balancesnapshot c in self.balance_record occurs after today, c.full_amount = None,
}
-3.13.5.2.a1: self.balance_record[any].fund_jars[all].current_amount must be = None if day has not occurred

{3.13.5.2.a5}
-3.13.5.2.a2: self.balance_record[any].fund_jars[all].current_amount cannot be None if it is the current day or before.
{whatever}
-3.13.5.2.a3: self.balance_record[any].fund_jars[all].current_amount = ?normal_fund_daily_distribution? if the day has occurred and it is a normal day

{3.13c.a8}
-3.13c.5.2.a4: self.balance_record[i].fund_jars[j].current_amount = self.balance_record[i].fund_jars[j-1].current_amount + sum( expected_anount of today's earmarks for this fund jar) after ?deallocation_fund_distribution? if the day has occurred and it is a deallocation day.

{3.13c.5.2.a3, 3.13c.5.2.a4}
-3.13c.5.2.a5: fund jars of balancesnapshot c have proper current_amount

{3.13.5.2.a5: fund jars of balancesnapshot c have proper current_amount}
-3.13.5.3.a1: self.expected_amount is = the expected_amount of the previous day (or current_amount of previous day if one exists) plus the expected_amount of all earmark_events with the same financial_id on that day
	&[1.2.3n.12.5.a1, 1.2.3n.12.5.a2: initial snapshot fund jars are deepcopy of last page's last balancesnapshot]

{
	1.2.3c.12.a2,
	1.2.3c.12.a3: the initial snapshot for this page has updated conciously-applied calculations (fundjar.milestone amount, page.current_free_amount, page.current_unpaid, current_safety_cushion, )
	3.13.8.a8: has all earmarks including implicit earmarks. All earmarks are maintained.,
	3.13.5.a5: all fund jars are maintained,
	3.a1
}
-3.13.5.4.a1: self.milestone_amount should be = sum of all expected values of repeated earmarks up to and including this date (unless it has a finance_id of None)
	&[1.2.3n.12.5.a1, 1.2.3n.12.5.a2: initial snapshot fund jars are deepcopy of last page's last balancesnapshot]
```
> **Note, flagged 2026-08-05 (author):** as written, this drops the word **unpaired** — it should read "sum of all *unpaired* expected values of repeated earmarks." Without it, the formula is a lifetime-climbing total that never accounts for an earmark a past release already consumed — an absurd result on its own terms: needing $1,500 saved against a $500 bill that's already been paid twice. `3.13.a10` (chart-only — see [06](06-assumption-dependency-graph.md#ch11-balance-record)) already deals in exactly this paired/unpaired distinction for a fund-jar-linked expected transaction, the strongest evidence this is a dropped word rather than original intent. Reproduced verbatim above per this document's own rule regardless.

### Chapter 14: Actual Transactions
*(`a03.txt`, lines 212-224, under `---Actual Transactions---`)*
```
{3.13.1.a1}
-3.13.6.a1: all obsolete actual transactions removed
	&[1.2.3.13.a2: all are paired]

{3.13.6.a1, 6.1.a1,6.5.a1,6.6.a1, 9.6.2.a1}
-3.13.6.a2: has all actual

-3.13.6.3.a1: no two actual transactions in a snapshot can have the same paired_finance_id and paired_expected_date

{1.2.3.13.a2, 3.13.6.a2, 3.13.6.3.a1}
-3.13.6.a3: actual transactions are maintained
```

### Chapter 15: Expected Transactions
*(`a03.txt`, lines 226-250, under `---Expected Transactions---`)*
```
{1.2.3c.10.a4: all finance patterns for this page c are perfect}
-3.13.7.a1: if a date from finance_pattern's rrule exists on this page, a repeated expected transaction with this finance_id must exist in the balance record of that day.
{1.2.3c.10.a4: all finance patterns for this page c are perfect}
-3.13.7.a2: an expected transaction cannot exist on a day not specified by it's financial pattern's date_pattern.
-3.13.7.1.a1: no two expected transactions in a snapshot can have same finance_id

{3.13.1.a1, 1.2.3c.10.a4, 3.13.7.6.a1, 3.13.7.a2, 3.13.7.1.a1}
-3.13.7.a3: all obsolete expected transactions removed or get amounts updated
	&[1.2.3.13.a2: all are paired]

{1.2.3c.10.a4, 3.13.7.a3, 7.1.a1, 7.7.a1, 7.8.a1, 9.7.6.a1, 3.13.7.a1}
-3.13.7.a4: has all expected transactions

{1.2.3.13.a2, 3.13.7.a4}
3.13.7.a5: expected transactions are maintained

{	1.2.3c.10.a4: all finance patterns for this page c are perfect}
-3.13.7.6.a1: an expected transactions expected_amount = the amount from its financialpattern
```
> `3.13.7.a5` is missing its leading `-` in the source (every other assumption ID in the file is prefixed with a dash) — reproduced exactly as written.

### Chapter 16: Earmarks
*(`a03.txt`, lines 251-297, under `---Earmarks---`)*
```
{	1.2.3c.11.a4: all earmark patterns for this page c are perfect. (but not the events in the page)}
-3.13.8.a1: an earmark, even an isolated earmark, cannot exist on a page that does not have an earmark pattern for it, (unless the earmark's finance_id is None)
{	1.2.3c.11.a4: all earmark patterns for this page c are perfect. (but not the events in the page)}
-3.13.8.a2: an earmark cannot exist outside the date_pattern of its earmark pattern (unless the earmark's finance_id is None)
{	1.2.3c.11.a4: all earmark patterns for this page c are perfect. (but not the events in the page)}
-3.13.8.a3: if day of the rrule pattern is on this page, a repeated earmark with this finance_id must exist in the balance record of that day.
{	3.13c.8.5.a1}
-3.13c.8.a4: all isolated earmarks on balancesnapshot c have expected_amount = explicit_amount.

{3.13c.8.a4}
-3.13c.8.a5: implicit earmarks on balancesnapshot c have been cleared. Any isolated earmark with an expected_amount of 0 has been deleted.
{3.13.1.a1, 1.2.3c.11.a4, 3.13.8.a1, 3.13.8.a2, 3.13.8.4.a1}
-3.13.8.a6: all obsolete earmarks removed. (implicit earmarks are not cleared)

{1.2.3c.11.a4, 3.13.8.a6, 9.8.2.a1, 9.8.6.a1,
8.1.a1, 8.2.a1, 8.3.a1, 8.4.a1, 8.4.a2, 8.5.a1,
3.13.8.a3, 3.13.8.1.a1, 3.13.8.1.a2, 3.13.8.1.a3,
3.13.8.3.a1, 3.13.8.4.a1, 3.13.8.5.a1, 3.13c.8.a5}
-3.13.8.a7: has all earmarks (not including implicit earmarks which may be added later)

{3.13b.a8, 3.13c.a8, 3.13f.a8}
-3.13.8.a8: has all earmarks including implicit earmarks. All earmarks are maintained.

{none}
-3.13.8.1.a1: only one isolated earmark with finance_id x can exist on a single day.
{none}
-3.13.8.1.a2: only one repeated earmark with finance_id x can exist on a single day.
{	1.2.3c.11.a4}
-3.13.8.1.a3: self.finance_id must be = the finance id of the expected transaction this earmark is saving for.
{	1.2.3c.11.a4}
-3.13.8.3.a1: self.repeated_earmark must be = true if it's repeated (generated from a pattern), false if not.
{	1.2.3c.11.a4}
-3.13.8.4.a1: self.expected_amount must be = earmark pattern's amount if self.repeated_earmark is true

{3.13c.a7}
-3.13c.8.4.a2: self.expected_amount must be = an explicitly given amount from user plus ?deallocation_implicit_amount?, if self.repeated_earmark is false on a deallocation day
{none}
-3.13.8.5.a1: self.explicit_amount must be = an explicitly given amount from user
{3.13.5.3.a1}
-3.13.8.6.a1: self.actual_amount must be = the difference between this fund jar's actual amount and the fund jar's actual amount on the previous date, if this is a repeated earmark
{3.13.5.3.a1}
-3.13.8.6.a2: self.actual_amount must be = the difference between this fund jar's actual amount and the fund jar's actual amount on the previous date, if this is an isolated earmark and no repeated earmark is on this day
```

---

## Part III — TransactionLogBook & cross-page consistency
*Source: `a01.txt` (2021-10-03, the last-touched of the three assumption files) — rewritten to use `a03.txt`'s suffix notation.*

### Chapter 17: log_pages, finance pattern & earmark pattern continuity across pages
*(`a01.txt`, lines 1-32)*
```
TransactionLogBook:
	{2.1.a1, 2.2.a1, 2.3.a1}
	-1.2.a1 log_pages cannot by null. It can be an empty list. between pages. (page X is immediately before page Y)
	{2.1.a1, 2.2.a1}
	-1.2.3.a1 If page X and page Y must have the same number of account pages and the same sets of accountnames. all pages must have the same accounts.

----finance pattern:
	{4.1.a1, 4.2.a1, 1.2.3b.10.a4, 1.2.3.a1}
	-1.2.3.10.a1: if a finance pattern in non-expired page X has an rrule that extends the pattern into page Y, then page Y should have the same finance pattern. both patterns should have the same values for all their properties.
	{4.1.a1, 4.2.a1, 1.2.3b.10.a4, 1.2.3.a1}
	-1.2.3.10.a2: if a finance pattern in non-expired page X has an rrule that ends before the first day of page Y, page Y should not have that finance pattern.
	{4.1.a1, 4.2.a1, 1.2.3b.10.a4, 1.2.3.a1}
	-1.2.3.10.a3: if a finance pattern in non-expired page X has the same finance id as a finance pattern in page Y, both finance patterns must have the exact same rrule. the rrule must have the same start date and end date.
	{1.2.3c.10.a1, 1.2.3c.10.a2, 1.2.3c.10.a3, 3.10.a1, 3.10.a2}
	-1.2.3c.10.a4: all finance patterns for this page c are perfect (but not the events in the page)

----earmark pattern:
	{5.1.a1, 5.2.a1, 5.3.a1, 1.2.3c.10.a4}
	-1.2.3c.11.a1: if an earmark pattern in non-expired page X has an rrule that extends the pattern into page Y, then this page Y should have the same earmark pattern. both patterns should have the same values for all their properties.
	{1.2.3c.10.a4}
	-1.2.3c.11.a2: if a earmark pattern in non-expired page X has an rrule that ends before the first day of page Y, this page Y should not have that earmark pattern.
	{	5.1.a1, 5.2.a1, 5.3.a1, 1.2.3c.10.a4
		3.11.2.a1: self.earmark_patterns should only contain patterns that end after self.start_date and begin at or before self.end_date,
		3.11.2.a2: an earmark pattern's date_pattern cannot extend beyond or before the date_pattern of it's associated finance_pattern.
	}
	-1.2.3c.11.a3: if a earmark pattern in non-expired page X has the same finance id as a earmark pattern in this page Y, both earmark patterns must have the exact same rrule.
					the rrule must have the same start date and end date.
	{	1.2.3c.11.a1, 1.2.3c.11.a2, 1.2.3c.11.a3,
		3.11.1.a1,
		1.2.3b.11.a4}
	-1.2.3c.11.a4: all earmark patterns for this page c are perfect. (but not the events in the page)
```
> `1.2.3c.11.a4`'s requirement set cites `1.2.3b.11.a4` — the **Before**-scoped version of itself (all earmark patterns being perfect for every page *before* the current one) — which is how the recursive "every page up the chain must already be consistent" guarantee is expressed. Same pattern would apply to `1.2.3c.10.a4` even though its own listed requirement set here cites the plain, unsuffixed page-local versions (`1.2.3c.10.a1/a2/a3`) rather than a `1.2.3b.10.a4` self-reference — reproduced exactly as written.

### Chapter 18: AccountTransactionPage derived calculations & initial-snapshot inheritance
*(`a01.txt`, lines 37-117, under the file's own `-----AccountTransactionPage` header)*
```
1.1.a1: The distance between start and end date for each non-expired page, must be equal to self.page_length months.

{1.2.3c.6.a1}
-1.2.3.5.a1: if today is in Y, Y.current_free_amount == Y.balance_record[?today?].full_amount - (Y.current_unpaid_expected) + ?money_in_fund_jars_ready_for_unpaid_bills(unpaid_bill_finance_ids, unpaid_bill_amounts)? - SUM(Y.balance_record[?today?].fund_jars[all].current_amount)
		rewording:current_free_amount ==  (full_amount of today's balance snapshot) - (money in today's fund jars)  - (current_unpaid_expected - money set aside for an unpaid bill in a fund jar)
{1.2.3.13.a2, 1.2.3c.12.a2, 1.2.3p.6.a1}
-1.2.3.6.a1: current_unpaid_expected = total expected_amounts of unfufilled uncancelled expected transactions that occured today or earlier (that are in active pages)

{1.2.3c.12.2.a1, 1.2.3c.12.2.a2, 1.2.3c.12.5.a1, 1.2.3c.12.5.a2, 9.1.a1,
3p.13.2.a4: balancesnapshots inside self.balance_record have the correct value for full_amount,
9.5.a1. 9.5.1.a1,
9.6.2.a1, 9.7.6.a1, 9.8.2.a1, 9.8.6.a1,
3.12.a1,
1.2.3c.10.a4, 3.12.5.a1,
1.2.3c.11.a4, 3.12.5.a2,
3.12.1.a1, 3.12.6.a1, 3.12.7.a1, 3.12.8.a1}
-1.2.3c.12.a1: the initial snapshot for this page c is good except for expected amounts

{1.2.3c.12.a1: the initial snapshot for this page c is good except for expected amounts,
1.2.3c.12.3.a1: if no previous AccountTransactionPage X exists, Y.initial_snapshot.expected_amount == None,
1.2.3c.12.3.a2: if a previous AccountTransactionPage X exists, Y.initial_snapshot.expected_amount == X.balance_record[-1].expected_amount,
1.2.3c.12.4.a1,
1.2.3c.12.4.a2,
9.5.a1,
9.5.1.a1,
1.2.3c.12.5.a2,
3p.a1: this page is maintained,
}
-1.2.3c.12.a2: the initial snapshot for this page c is maintained

{3p.a2, 1.2.3.12.5.a1, 1.2.3.12.5.a2}
-1.2.3c.12.a3: the initial snapshot for this page has updated conciously-applied calculations (fundjar.milestone amount, page.current_free_amount, page.current_unpaid, current_safety_cushion, )

{whatever}
-1.2.3.12.2.a1: if no previous AccountTransactionPage X exists, and today is on or after Y.start_date, Y.initial_snapshot.full_amount cannot be None.
if no previous AccountTransactionPage X exists, and today is before Y.started_date, Y.initial_snapshot.full_amount = None

{whatever}
-1.2.3.12.2.a2: if a previous AccountTransactionPage X exists, Y.initial_snapshot.full_amount == X.balance_record[-1].full_amount

{whatever}
-1.2.3.12.5.a1: if no previous AccountTransactionPage X exists, Y.initial_snapshot.fund_jars should include a fund jar for each earmark pattern that starts before Y.start_date. and a fund jar with a finance_id of None
{whatever}
-1.2.3.12.5.a2: if a previous AccountTransactionPage X exists, Y.initial_snapshot.fund_jars == X.balance_record[-1].fund_jars.?deepcopy? but no date.
-1.2.3.12.5.a4: if no previous AccountTransactionPage X exists, and today is in or after Y, the current_amount of all fund jars in Y.initial_snapshot.fund_jars cannot be None
-1.2.3.12.5.a5: if no previous AccountTransactionPage X exists, and today occurs before Y, the current_amount of all fund jars in Y.initial_snapshot.fund_jars is None
-1.2.3.12.5.a6: if no previous AccountTransactionPage X exists, and today is in or after Y, the expected_amount of all fund jars in Y.initial_snapshot.fund_jars = None
-1.2.3.12.5.a7: if no previous AccountTransactionPage X exists, and today occurs before Y, the expected_amount of all fund jars in Y.initial_snapshot.fund_jars cannot be None

{whatever}
-1.2.3.12.3.a1: if no previous AccountTransactionPage X exists, and today is in or after Y, Y.initial_snapshot.expected_amount == None

{3p.13.5.3.a1: self.expected_amount is = the expected_amount of the previous day (or current_amount of previous day if one exists) plus the expected_amount of all earmark_events with the same financial_id on that day}
-1.2.3.12.3.a2: if a previous AccountTransactionPage X exists, Y.initial_snapshot.expected_amount == X.balance_record[-1].expected_amount
-1.2.3.12.3.a3: if no previous AccountTransactionPage X exists, and today occurs before Y, initial_snapshot.expected_amount cannot be None

{whatever}
-1.2.3.12.4.a1: if no previous AccountTransactionPage X exists, Y.initial_snapshot.expected_free_amount == None
{3p.13.4.a1}
-1.2.3.12.4.a2: if a previous AccountTransactionPage X exists, Y.initial_snapshot.expected_free_amount == X.balance_record[-1].expected_free_amount

{3.13b.a5, 3.13c.a5, 3.13f.a5}
-1.2.3.13.a1: all can be paired

{1.2.3.a1, 1.2.3.13.a1, 1.2.3.13.7.a1, 1.2.3.13.7.a2, 1.2.3.13.6.a1, 1.2.3.13.6.a2, 6.4.a1, 7.4.a1, 7.4.a2, 7.5.a1, 7.5.a2}
-1.2.3.13.a2: all expected-actual transaction pairs have been created and are properly matched

{3c.13.a2}
-1.2.3c.13.a3: this current accounttransaction page c is ready to remove balance snapshops with no events in them.
```
> Several entries (`1.1.a1`, `1.2.3.12.5.a4-a7`, `1.2.3.12.3.a3`) have no `{...}` requirement line before them at all in the source — reproduced exactly as found, not omitted in error.
> `{whatever}` appears repeatedly through the initial-snapshot cluster — see [Appendix C](#appendix-c-formatting-quirks-preserved-as-is) for what that placeholder likely signals versus `{none}`.

### Chapter 19: Expected/Actual transaction pairing completeness
*(`a01.txt`, lines 120-149)*
```
ExpectedTransaction:
	unpaired:
	{3.13.6.a2, 3.13.7.a4}
	-1.2.3.13.7.a1: if self.paired_amount == None and self.paired_actual_date == None and we are not expired, there should NOT exist an ActualTransaction A in an active page such that:
				self.paired_amount == A.amount
				self.paired_actual_date == A.occurred_date
				self.expected_date == A.paired_expected_date
				self.finance_id == A.paired_finance_id
	{3.13.6.a2, 3.13.7.a4}
	-1.2.3.13.7.a2: if self.paired_amount != None and self.paired_actual_date != None and we are not expired, there should exist an ActualTransaction A in an active page such that:
				self.paired_amount == A.amount
				self.paired_actual_date == A.occurred_date
				self.expected_date == A.paired_expected_date
		self.finance_id == A.paired_finance_id
---ActualTransaction:
	#has pair property, paired, cancelled, expired, has no pair property, unpaired, not cancelled, not expired
	--unpaired:
	{3.13.6.a2, 3.13.7.a4}
	-1.2.3.13.6.a1: if self.paired_finance_id == None and self.paired_expected_date == None and we are not expired, there should NOT exist an ExpectedTransaction E in an active page such that:
		E.paired_amount == self.amount
		E.paired_actual_date == self.occurred_date
		E.expected_date == self.paired_expected_date
		E.finance_id == self.paired_finance_id
	{3.13.6.a2, 3.13.7.a4}
	-1.2.3.13.6.a2: paired:if self.paired_finance_id != None and self.paired_expected_date != None and we are not expired, there should exist an Expectedtransaction E in an active page such that:
		E.cancelled == false
		E.paired_amount == self.amount
		E.paired_actual_date == self.occurred_date
		E.expected_date == self.paired_expected_date
		E.finance_id == self.paired_finance_id
```

---

## Appendix A: Reduction & merge candidates

Not assumptions themselves — the author's own meta-analysis of the assumption set, verbatim from `assumptionNotes.txt` (2021-08-28). Full context and lineage of these reduction files is in [00-sources-and-notes.md](00-sources-and-notes.md#reduction-file-lineage-the-allfooallreduced-family); only the two most information-dense excerpts are reproduced here rather than the full multi-hundred-line dependency dumps (which duplicate, without prose, what Parts I-III above already give you with prose).

**Assumptions flagged as safe to merge into other test functions** (same requirements and use as other assumptions):
```
{'1.2.3.12.4.a2', '3.13.8.6.a2', '1.2.3.12.2.a2', '3.11.2.a2', '3.13.8.1.a2', '8.4.a2', '8.3.a1', '1.2.3.12.5.a1', '7.8.a1', '8.1.a1', '1.2.3.12.3.a1', '8.5.a1',
'1.2.3.13.7.a2', '7.4.a2', '3.12.7.a1', '7.5.a2', '1.2.3.13.6.a2', '7.7.a1', '7.6.a1', '6.5.a1', '10.4.a1', '3:13.a9', '8.2.a1', '6.6.a1', '1.2.3.10.a3',
'3.13.5.a1', '1.2.3.12.3.a2', '1.2.3.10.a2', '7.5.a1', '3.12.8.a1', '1.2.3.13.7.a1'}
```
> "These share the exact same requirements and use as other functions. And thus when we make test functions out of assumptions, these can be merged into the test functions of other assumptions. It's not specified which of these assumptions should be merged with what other assumptions. You can figure that out when the time comes."

**The "extremely reduced" rollup** (from `allReducedFoo.txt`'s final pass) — the most aggressively consolidated version found anywhere in the notes, including a synthetic placeholder assumption (`0.999.x`) that bundles several low-value axioms together:
```
{3.4.a1, 1.2.a1, 3.13.5.2.a2, 3.1.a1, 3.13.4.a1, 3.13.5.2.a1, 3.a2}
-0.999.x
```
This appears at the top of a much longer flattened set in `allReducedFoo.txt` (its "extremely reduced" section) that otherwise reproduces the same IDs already given prose in Parts I-III above, just without descriptions — see that file directly if you need the bare dependency-only form.

**First-pass stratification layers** (`assumptionNotes.txt`, algorithmically peeling off assumptions "not required by any other assumption," repeated in layers):
```
['1.2.a1', '3.a2', '3.1.a1', '3.4.a1', '3.13.4.a1', '3.13.5.2.a1', '3.13.5.2.a2']
['1.2.3.5.a1', '2.3.a1', '3.5.a1', '3.7.a1', '3.7.a2', '3.13.5.4.a1']
['1.2.3.6.a1', '1.2.3.12.a3', '3.a1']
['3.13.a9', '3.13.2.a4', '3.13.3.a1', '3.13.5.a5', '3.13.6.a3', '3.13.7.a5', '3.13.8.a8']
['3.13.2.a1', '3.13.8.6.a1']
['3.13.2.a2', '3.13.2.a3', '3.13.5.3.a1']
['3.13.a4', '3.13.5.2.a5']
['3.13.a3', '3.13.5.2.a3', '3.13.5.2.a4']
['1.2.3.13.a3', '3.13.a8']
['3.13.a2']
['3.10.a3', '3.13.5.a3']
['7.3.a1']
[]
```
> The author's own comment on this result: "many more assumptions are left. an unacceptable amount." — this is why the later, more aggressive reduction passes (`allFoo.txt`, `allReduced.txt`, then the hand-curated pass inside `allReducedFoo.txt`) exist.

---

## Appendix B: Use cases

Verbatim from `planning2simple.txt` — worked starting-point scenarios rather than a systematic catalog (only three exist):
```
{1.1a1}
UC.0.1 we have empty transactionlogbook. We don't even have dates or a page.

{UC.0.1, 2.1.a1, 2.2.a1, 2.3.a1}
UC.0.2 we have a SINGLE PAGE with no accounts. But we have dates.

{UC.0.1, 1.1.3.12.2.a1, 1.1.3.12.5.a1, 1.1.3.12.3.a1, 1.1.3.12.4.a1, 2.1.a1, }
initial_snapshot.fund_jars just starts with the a single fund jar with a finance_id of None
UC 0.3 we have a SINGLE PAGE with one account. It has no events of any kind, no patterns or initial values.
```
> `UC.0.3`'s requirement set uses `1.1.3.12.x.aY` (class **1**.1 = TransactionLogBook.log_pages, not `1.2.3.12.x` = TransactionLogBook.log_pages.account_pages.initial_snapshot as used everywhere else). Reproduced as written; likely a typo for `1.2.3.12.x.aY` given the assumption text on the next line is directly about `initial_snapshot.fund_jars` (a class-3 property), not `log_pages` (a class-1 property) directly.

---

## Appendix C: Formatting quirks preserved as-is

A few notational patterns recur throughout Parts I-III worth explaining once instead of re-flagging every time:

- **`{none}` vs `{}` vs `{whatever}`** — all three appear across the source files and are reproduced exactly as found rather than normalized to one spelling. `{none}` is used consistently in `a02.txt` for assumptions with genuinely no prerequisites (base leaf properties like "cannot be None" checks). `{whatever}` appears in `a01.txt`'s initial-snapshot cluster and reads like a placeholder the author hadn't filled in yet, rather than a deliberate "no requirements" marker — treat assumptions with `{whatever}` as having an **undetermined**, not confirmed-empty, requirement set. `{}` (bare empty braces) shows up mainly in the `allFoo`/`allReduced*` dependency-only files, which are machine-processed output rather than hand-authored text.
- **`?word?` markers** (e.g. `?today?`, `?deepcopy?`, `?normal_fund_daily_distribution?`) are the author's own inline flags for values or functions that hadn't been pinned down yet at time of writing — not placeholders inserted during this reconstruction.
- **`&[...]` and plain `[...]` after an assumption** — per the legend in [How to read an assumption ID](#how-to-read-an-assumption-id): plain `[s,t,r]` means the thing being described directly breaks assumptions s/t/r; `&[s,t,r]` means those assumptions will be broken by it or sometime after. Both forms appear attached to assumption definitions in `a01.txt`/`a03.txt` (e.g. `3.13c.a7`'s `&[3.13c.8.1.a1, 3.13c.8.a5]`), reproduced in place exactly where they appeared.
- **Instance-suffix letters** (`b`/`c`/`f`/`p`/`n` — Before/Current/Following/Previous/Next) are only used in `a03.txt` and `a01.txt`; `a02.txt` predates the notation and only ever uses bare numeric IDs. See the [glossary's vocabulary section](01-glossary-of-terms.md#domain-vocabulary) for the short version, or [How to read an assumption ID](#how-to-read-an-assumption-id) above for the full original legend.
- **Minor typos preserved verbatim, not corrected**: "cannot by null" (`1.2.a1`), "conciously-applied" (throughout), "unfufilled"/"fufillment" (throughout), "balancesnapshops" (several places, meant "balancesnapshots"), "expected_anount" (`3.13c.5.2.a4`), "finaicial_id" appearing as "financial_id" inconsistently vs. the class property's actual name `finance_id` in a couple of prose descriptions (e.g. `3.13.5.3.a1`, `1.2.3.12.3.a2` both say "financial_id" in running text while meaning `finance_id`). None of these were fixed, per the instruction to use the same text found in the source.
