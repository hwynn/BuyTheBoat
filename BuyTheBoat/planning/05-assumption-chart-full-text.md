# Assumption Chart Full Text (verbatim)

Complete, mechanically-extracted transcription of **every element** of the two content-bearing UMLet charts in `assumptions/`:

1. **`assumptionChartSimpleLines.uxf`** (last modified 2021-10-03 10:03 PM) — the file `assumptionNotes.txt` calls "the most up to date and correct documentation." 118 boxes, 193 edges, 14 dotted regions, 15 text annotations.
2. **`assumptionChartTests.uxf`** (2021-10-03 4:09 PM, ~6 hours earlier the same day) — the test-bundling plan. 88 boxes (24 `T`-numbered test bundles + 64 assumption boxes), 147 edges, and the *same* 14 regions / 15 text annotations as the final chart.

This is the source companion to [03-assumptions-glossary.md](03-assumptions-glossary.md) (which reproduces the `a01/a02/a03.txt` assumption files verbatim) and [06-assumption-dependency-graph.md](06-assumption-dependency-graph.md) (which gives the graph structure derived from this text). The chart is the record the author maintained as authoritative; it bundles multiple assumptions per box, and its requirement sets sometimes differ from the `.txt` files. Extracted with [`uxf_graph_tool.py`](uxf_graph_tool.py); **no source file was modified.**

**Transcription policy** (same as 03): text inside the fenced blocks is exactly what the chart element contains — including typos, UMLet style tags (`bg=pink`, `layer=1`, `style=wordwrap`), stray punctuation, and inconsistent whitespace (only trailing newlines trimmed). Editorial remarks appear *outside* the fences only.

**How to read a box:** the heading names the box by its first/label ID, but many boxes *bundle* several assumptions — every `ID` + description pair inside the fence is its own assumption. `{...}` lines are requirement sets. Style-tag lines (`bg=...`, `layer=...`, `fg=...`) are part of the box's stored attributes. Two notation quirks appear only in the chart: a **parenthesized** ID inside a `{...}` set marks a prerequisite already implied by another member (redundant, shown for convenience); a **second `{...}` block** on a box is a reduced restatement of the first. The colour/scope tags on the drawn arrows encode the `b`/`c`/`f`/`p`/`n` instance grammar (orange = previous instance, red = next) — the same couplings captured in [06](06-assumption-dependency-graph.md).

Chapters mirror [03-assumptions-glossary.md](03-assumptions-glossary.md) so you can flip between a box and the `.txt` original of the same assumption.

---

## Part 1 — `assumptionChartSimpleLines.uxf` (the authoritative chart)

### Chart-level inventory

- **Boxes:** 118 &nbsp; **Edges:** 193 (the dependency structure they render is in [06-assumption-dependency-graph.md](06-assumption-dependency-graph.md)) &nbsp; **Dotted regions:** 14 &nbsp; **Text annotations:** 15

### Ch.1 TransactionLogPage

**Box `2.1.a1`** *(box at x=0, y=354)*
```
2.1.a1
start_date cannot be None. Must be before end_date.
start_date should be the first day of a month
{none}
```

**Box `2.2.a1`** *(box at x=0, y=372)*
```
2.2.a1 
end_date cannot be None. Must be after start_date
end_date should be the last day of a month
{none}
```

**Box `2.3.a1`** *(box at x=3030, y=594)*
```
2.3.a1 
account_pages cannot be None. Can be empty dictionary
{none}
```

### Ch.2 FinancialPattern

**Box `4.1.a1`** *(box at x=78, y=378)*
```
4.1.a1 
finance_id cannot be None
{none}
```

**Box `4.2.a1`** *(box at x=78, y=396)*
```
4.2.a1 
source cannot be None
{none}
```

### Ch.3 EarMarkPattern

**Box `5.1.a1`** *(box at x=156, y=294)*
```
5.1.a1
self.finance_id cannot be None
{none}
```

**Box `5.2.a1`** *(box at x=156, y=276)*
```
5.2.a1
self.date_pattern cannot be None
self.date_pattern rrule must have until property, not count property
{none}
```

**Box `5.3.a1`** *(box at x=156, y=312)*
```
5.3.a1
self.amount cannot be None.
{none}
```

### Ch.4 ActualTransaction

**Box `6.1.a1`** *(box at x=588, y=288)*
```
6.1.a1
self.source cannot be None.
{none}
6.5.a1
self.made_in_bulk cannot be None
{none}
6.6.a1
self.amount cannot be None.
{none}
```

**Box `6.4.a1`** *(box at x=846, y=504)*
```
6.4.a1 
If self.paired_expected_date == None then self.paired_finance_id == None.
{none}
```

### Ch.5 ExpectedTransaction

**Box `7.1.a1`** *(box at x=504, y=444)*
```
7.1.a1
self.finance_id cannot be None.
{none}
7.7.a1
self.amount_tolerance cannot be None, and cannot have a negative value for either number.
{none}
7.8.a1
self.date_tolerance cannot be None. and cannot have a negative value for either number.
{none}
```

**Box `7.3.a1`** *(box at x=594, y=474)*
```
7.3.a1
cancelled cannot be None
{none}
```

**Box `7.4.a1`** *(box at x=594, y=492)*
```
7.4.a1
If self.paired_actual_date == None then self.paired_amount == None. If self.paired_actual_date != None then self.paired_amount != None
{none}
7.4.a2
self.paired_amount == None if self.cancelled == true
{none}
7.5.a1
if self.paired_amount == None then self.paired_actual_date == None. if self.paired_amount != None then self.paired_actual_date != None. 
{none}
7.5.a2
self.paired_actual_date == None if self.cancelled == true
{none}
```

**Box `7.6.a1`** *(box at x=2238, y=402)*
```
7.6.a1
self.expected_amount cannot be None.
{none}
```

### Ch.6 EarmarkEvent

**Box `8.1.a1`** *(box at x=750, y=684)*
```
8.1.a1
finance_id cannot be None if self.repeated_earmark == True
{none}
8.2.a1
earmark_date cannot be None.
{none}
8.3.a1
repeated_earmark cannot be None. default is false.
{none}
8.5.a1
self.explicit_amount must be None if self.repeated_earmark == true
{none}
```

**Box `8.4.a1`** *(box at x=750, y=660)*
```
8.4.a1
expected_amount cannot be None.
{ 
8.4.a2}
```

**Box `8.4.a2`** *(box at x=672, y=660)*
```
8.4.a2
self.expected_amount must be = an explicitly given amount from user if self.repeated_earmark is false on a normal day
{none}
```

### Ch.7 BalanceSnapshot

**Box `9.1.a1`** *(box at x=2334, y=330)*
```
9.1.a1
snapshot_date can be None. If it is None, actual_transactions, expected_transactions, and earmark_events are empty lists.
{none}
```

**Box `9.5.1.a1`** *(box at x=1638, y=408)*
```
9.5.1.a1
all funds jars in self.fund_jars must have a unique finance_id.
{none}
```

**Box `9.5.a1`** *(box at x=1290, y=402)*
```
9.5.a1
self.fund_jars must have a single fund jar with a finance_id of None
{10.3.a1, 10.4.a1}
```

**Box `9.6.2.a1`** *(box at x=594, y=366)*
```
9.6.2.a1
self.actual_transactions[all].occurred_date == self.snapshot_date
{none}
```

**Box `9.7.6.a1`** *(box at x=504, y=360)*
```
9.7.6.a1
self.expected_transactions[all].expected_amount == self.snapshot_date
{none}
```

**Box `9.8.2.a1`** *(box at x=2412, y=312)*
```
9.8.2.a1
self.earmark_events[all].earmark_date == self.snapshot_date
{none}
```

**Box `9.8.6.a1`** *(box at x=2412, y=330)*
```
9.8.6.a1
self.earmark_events[any].actual_amount must be = None if this is an isolated earmark and there is also a repeated earmark on this day.
{none}
```

### Ch.8 FundJar

**Box `10.3.a1`** *(box at x=594, y=546)*
```
10.3.a1
self.expected_amount is = None if self.finance_id is None.
{none}
```

**Box `10.4.a1`** *(box at x=594, y=564)*
```
10.4.a1
self.milestone_amount should be = None if self.finance_id is None
{none}
10.4.a2
if the finance id for this fund jar is for a repeated negative expected transaction, self.milestone_amount should be -1*amount from the finance_pattern
{whatever}
10.4.a3
if the finance id for this fund jar is for a repeated positive expected transaction, self.milestone_amount should be none
{whatever}
```

### Ch.9 Page identity, bounds & top-level summary

**Box `3.1.a1`** *(box at x=2946, y=594)*
```
3.1.a1
account cannot be None
{none}
```

**Box `3.2.a1`** *(box at x=78, y=438)*
```
3.2.a1
start_date cannot be None
{none}
```

**Box `3.2.a2`** *(box at x=78, y=456)*
```
3.2.a2
start_date must be a date before end_date
start_date should be the first day of a month
{none}
```

**Box `3.3.a1`** *(box at x=78, y=474)*
```
3.3.a1
end_date cannot be None
{none}
```

**Box `3.3.a2`** *(box at x=78, y=492)*
```
3.3.a2
end_date must be a date after start_date
end_date should be the last day of a month
{none}
```

**Box `3.5.a1`** *(box at x=2946, y=570)*
```
3.5.a1
current_free_amount = None if today is not between self.start_date and self.end_date
{3.2.a1, 3.2.a2, 3.3.a1, 3.3.a2}
```

**Box `3.7.a1`** *(box at x=2946, y=498)*
```
3.7.a1
current_safety_cushion = None if today is not between self.start_date and self.end_date
{1.2.3c.12.a2}
```

**Box `3.7.a2`** *(box at x=2946, y=516)*
```
3.7.a2
current_safety_cushion = money in today's fund jar with finance_id == None
{1.2.3c.12.a2}
```

**Box `3.8.a1`** *(box at x=672, y=492)*
```
3.8.a1
ideal_safety_cushion cannot be None
{none}
```

**Box `3.9.a1`** *(box at x=672, y=510)*
```
3.9.a1
safety_priority cannot be None
{none}
```

**Box `3.10.a1`** *(box at x=168, y=366)*
```
3.10.a1
self.finance_patterns should only contain patterns that end after self.start_date and begin at or before self.end_date
{3.2.a1, 3.2.a2, 3.3.a1, 3.3.a2}
```

**Box `3.10.a2`** *(box at x=168, y=384)*
```
3.10.a2
self.finance_patterns cannot contain any duplicate ids
{none}
```

**Box `3.10.a3`** *(box at x=672, y=384)*
```
3.10.a3
if an EarMarkPattern, EarMarkEvent, FundJar, or ExpectedTransaction exists in our page with a finance_id of x and our page has no finance pattern with a finance_id of x, that EarMarkPattern, EarMarkEvent, FundJar, or ExpectedTransaction shouldn't exist
{1.2.3c.11.a4, 8.1.a1, 8.2.a1, 8.3.a1, 8.4.a1, 
8.5.a1, 3.13.8.1.a3, 3.13.5.a2
10.3.a1,
7.1.a1, 7.3.a1, 7.4.a1, 7.4.a2, 7.5.a1, 7.5.a2, 
7.7.a1, 7.8.a1, 3.13.7.a2, 3.13.7.a4
1.2.3c.10.a4, 4.1.a1}
bg=pink
```

**Box `3.11.1.a1`** *(box at x=420, y=432)*
```
3.11.1.a1
all earmark patterns self.earmark_patterns should have unique finance_ids.
{5.1.a1}
```

**Box `3.11.2.a1`** *(box at x=336, y=438)*
```
3.11.2.a1
self.earmark_patterns should only contain patterns that end after self.start_date and begin at or before self.end_date
{5.2.a1}
3.11.2.a2
an earmark pattern's date_pattern cannot extend beyond or before the date_pattern of it's associated finance_pattern.
{whatever}
```

**Box `3.a1`** *(box at x=2778, y=504)*
```
3.a1
this page is maintained
{1.2.3c.12.a2, 3.13.2.a4, 3.13.3.a1, 
3.13.7.a5,
3.13.6.a3, 3.13.8.a8, 3.13.5.a5, 3.13.a9}
{3.13.2.a4, 3.13.3.a1, 3.13.7.a5, 
3.13.6.a3, 
3.13.8.a8, 3.13.5.a5, 3.13.a9}
bg=light_gray
```

**Box `3.a2`** *(box at x=3024, y=504)*
```
3.a2
this page has updated conciously-applied calculations (fundjar.milestone_amount, page.current_free_amount, page.current_unpaid_expected, current_safety_cushion, )
{3.13.5.4.a1, 3.5.a1, 1.2.3.5.a1, 
(1.2.3.6.a1), 3.7.a1, 3.7.a2}
bg=light_gray
```

### Ch.10 Initial Snapshot

**Box `3.12.1.a1`** *(box at x=2334, y=294)*
```
3.12.1.a1
self.initial_snapshot.snapshot_date = None
{none}
3.12.6.a1
self.initial_snapshot.actual_transactions == []
{none}
3.12.7.a1
self.initial_snapshot.expected_transactions == []
{none}
3.12.8.a1
self.initial_snapshot.earmark_events == []
{none}
```

**Box `3.12.5.a1`** *(box at x=1026, y=372)*
```
3.12.5.a1
initial_snapshot.fund_jars cannot have a fund jar that is not in our ?initial finance_patterns? (except the fund jar with None as a finance_id)
{3.13.5.a1, 3.13.5.a2}
3.12.5.a2
no fund jar can exist here if it has a finance pattern that starts after (or on?) ?self.start_date?
{3.13.5.a2}
```

**Box `3.12.a1`** *(box at x=2334, y=312)*
```
3.12.a1
initial_snapshot cannot be None
{whatever}
```

### Ch.11 Balance Record

**Box `3.13.1.a1`** *(box at x=420, y=456)*
```
3.13.1.a1
all BalanceSnapshots inside self.balance_record should have a snapshot_date that is => self.start_date  and is =< self.end_date
{3.2.a1, 3.2.a2, 3.3.a1, 3.3.a2}
```

**Box `3.13.a2`** *(box at x=1206, y=600)*
```
3.13.a2
all obsolete events of all kinds have been removed, obsolete fund jars removed, and all events exist that should (not including fund jars) (not including implicit earmarks which may be added later)
{3.13.6.a2, 3.13.7.a4, 3.13.8.a7, 
3.13.5.a3, 3.10.a3}
```

**Box `3.13.a3`** *(box at x=1350, y=600)*
```
3.13.a3
each balance snapshot inside balance_record has at least one event (earmark, actual event, or expected event). All other balance snapshots have been removed.
{3.13.a2, 1.2.3.13.a3}
```

**Box `3.13.a4`** *(box at x=1446, y=636)*
```
3.13.a4
full_amount can be safely calculated for balance snapshops
{3.13.a3}
```

**Box `3.13.a5`** *(box at x=756, y=522)*
```
3.13.a5
we have enough information to pair actual and expected events
{3.13b.6.a1, 3.13c.6.a1, 3.13f.6.a1, 
3.13b.6.a2, 3.13c.6.a2, 3.13f.6.a2, 
3.13b.7.a3, 3.13c.7.a3, 3.13f.7.a3, 
3.13b.7.a4, 3.13c.7.a4, 3.13f.7.a4}
```

**Box `3:13.a9`** *(box at x=2334, y=402)*
```
3:13.a9
have expected amounts up to date
{7.6.a1, 3.13.8.6.a1, 3.13.8.6.a2}
```

**Box `3.13c.a6`** *(box at x=1524, y=534)*
```
3.13c.a6
we know whether or not this is a deallocation day. And implicit earmarks are cleared.
{3.13p.2.a1, 3.13.5.a4, 3.13p.5.2.a5, 
3.8.a1, 3.9.a1, 3.13c.8.a5, 3.13.6.a2}
bg=pink
```

**Box `3.13c.a7`** *(box at x=1746, y=588)*
```
3.13c.a7
if this is a deallocation day, we have added the new implicit isolated earmarks to earmark_events
{3.13c.a6, 3.13c.8.a7, 1.2.3.13.a2, 
9.5.a1, 9.5.1.a1}
&[3.13c.8.1.a1, 3.13c.8.a5]

3.13c.a10
{whatever}
&[3.13c.8.1.a1, 3.13c.8.a5]
if this is not a deallocation day, and an expected transaction transaction with a fund jar has a paired actual transaction on this day,
create an implicit earmark for that fund jar with the actual transaction's amount
```

**Box `3.13c.a8`** *(box at x=1914, y=588)*
```
3.13c.a8
if this is a deallocation day, the newly added earmarks are now properly merged into existing isolated earmarks if they existed.
{3.13c.a7, 3.13c.8.1.a1, !3.13c.8.a5, 
3.13c.8.4.a2}
```

### Ch.12 balance_record full_amount & expected amounts

**Box `3.13.2.a4`** *(box at x=2334, y=366)*
```
3.13.2.a4
balancesnapshots inside self.balance_record have the correct value for full_amount
{3.13c.2.a1, 3.13f.2.a1}
layer=1
```

**Box `3.13.3.a1`** *(box at x=2586, y=438)*
```
3.13.3.a1
self.balance_record[i].expected_amount = (self.balance_record[i-1].full_amount if self.balance_record[i-1].full_amount != None else self.balance_record[i-1].expected_amount) + SUM(self.balance_record[i].expected_transactions[all].expected_amount)
{1.2.3c.12.a1, 3.13p.2.a1, 
3.13p.3.a1, 3.13.7.a4, 3.13.a3}
layer=1
```

**Box `3.13.4.a1`** *(box at x=2688, y=576)*
```
3.13.4.a1
self.balance_record[i].expected_free_amount = self.balance_record[i].expected_amount - SUM(self.balance_record[i].fund_jars[all].expected_amount)
{1.2.3.12.3.a1, 1.2.3.12.3.a2,
1.2.3.12.4.a1, 1.2.3.12.4.a2,
3.13.3.a1, 3.13.5.3.a1
1.2.3.12.5.a1, 1.2.3.12.5.a2
}
```

**Box `3.13c.2.a1`** *(box at x=1632, y=576)*
```
3.13c.2.a1
balancesnapshot c has proper full_amount
{3.13c.2.a2, 3.13c.2.a3}
```

**Box `3.13c.2.a2`** *(box at x=1542, y=594)*
```
3.13c.2.a2
if balancesnapshot c in self.balance_record occurs after today, c.full_amount = None
{3.13.a4, 1.2.3.12.2.a1, 1.2.3.12.2.a2, 
3.13p.2.a2, 3.13p.2.a3}
layer=1
```

**Box `3.13c.2.a3`** *(box at x=1542, y=618)*
```
3.13c.2.a3
if balancesnapshot c in self.balance_record occurs today or before, c.full_amount = p.full_amount + sum(x.amount for x in c.actual_transactions)
{3.13.a4, 1.2.3.12.2.a1, 1.2.3.12.2.a2, 
3.13.6.a2, 3.13p.2.a1}
layer=1
```

### Ch.13 Fund Jars

**Box `3.13.5.2.a1`** *(box at x=2196, y=618)*
```
3.13.5.2.a1
self.balance_record[any].fund_jars[all].current_amount must be = None if day has not occurred
{3.13c.2.a2}
layer=1
```

**Box `3.13.5.2.a2`** *(box at x=2196, y=588)*
```
3.13.5.2.a2
self.balance_record[any].fund_jars[all].current_amount cannot be None if it is the current day or before.
{3.13.5.2.a5}
layer=1
```

**Box `3.13.5.2.a3`** *(box at x=1998, y=606)*
```
3.13.5.2.a3
self.balance_record[any].fund_jars[all].current_amount = ?normal_fund_daily_distribution? if the day has occurred and it is a normal day
{whatever}
layer=1
```

**Box `3.13.5.3.a1`** *(box at x=2160, y=450)*
```
3.13.5.3.a1
self.expected_amount is = the expected_amount of the previous day (or current_amount of previous day if one exists) plus the expected_amount of all earmark_events with the same financial_id on that day
{3.13.5.2.a5}
&[1.2.3n.12.5.a1, 1.2.3n.12.5.a2]
layer=1
```

**Box `3.13.5.4.a1`** *(box at x=2946, y=534)*
```
3.13.5.4.a1
self.milestone_amount should be = sum of all expected values of repeated earmarks up to and including this date (unless it has a finance_id of None. or if the finance id is for a repeated expected transaction)
{(1.2.3c.12.a2), 1.2.3c.12.a3, 
(3.13.8.a8), (3.13.5.a5), 3.a1}
&[1.2.3n.12.5.a1, 1.2.3n.12.5.a2]
layer=1
```

**Box `3.13.5.a1`** *(box at x=594, y=384)*
```
3.13.5.a1
if the day exists within EarMarkPattern A's date_patern(any day between start and end), a fund jar with A's finance_id must exist on that day.
{1.2.3c.11.a4}
3.13.5.a2
a fund jar can only exist on days that fall within its earmark pattern's rrule (unless the fund jar's finance_id is None)
{1.2.3c.11.a4}
```

**Box `3.13.5.a3`** *(box at x=672, y=408)*
```
3.13.5.a3
all obsolete fund jars removed
{3.13.1.a1, 1.2.3c.11.a4, 3.13.5.a2}
layer=1
```

**Box `3.13.5.a4`** *(box at x=672, y=468)*
```
3.13.5.a4
has all fund jars (the amount might not be accurate)
{3.13.5.a1, 3.13.5.a2}
layer=1
bg=pink
```

**Box `3.13.5.a5`** *(box at x=2334, y=450)*
```
3.13.5.a5
all fund jars are maintained
{(3.13b.5.2.a5), (3.13c.5.2.a5), (3.13f.5.2.a5),
3.13.5.3.a1, (10.3.a1)}
layer=1
bg=light_gray
```

**Box `3.13c.5.2.a4`** *(box at x=1998, y=588)*
```
3.13c.5.2.a4
self.balance_record[i].fund_jars[j].current_amount = self.balance_record[i].fund_jars[j-1].current_amount + sum( expected_amount of today's earmarks for this fund jar) after ?deallocation_fund_distribution? if the day has occurred and it is a deallocation day.
{3.13c.a8}
layer=1
```

**Box `3.13c.5.2.a5`** *(box at x=2076, y=588)*
```
3.13c.5.2.a5
fund jars of balancesnapshot c have proper current_amount
{3.13c.5.2.a3, 3.13c.5.2.a4}
layer=1
```

### Ch.14 Actual Transactions

**Box `3.13.6.a1`** *(box at x=588, y=306)*
```
3.13.6.a1 
all obsolete actual transactions removed
{3.13.1.a1}
&[1.2.3.13.a2]

3.13.6.3.a1: no two actual transactions in a snapshot can have the same paired_finance_id and paired_expected_date
&[1.2.3.13.a2]
```

**Box `3.13.6.a2`** *(box at x=672, y=354)*
```
3.13.6.a2 
has all actual
{3.13.6.a1, 6.1.a1,6.5.a1,6.6.a1, 9.6.2.a1}
bg=pink
layer=1
```

**Box `3.13.6.a3`** *(box at x=2334, y=468)*
```
3.13.6.a3
actual transactions are maintained
{1.2.3.13.a2, 3.13.6.a2}
bg=light_gray
layer=1
```

### Ch.15 Expected Transactions

**Box `3.13.7.6.a1`** *(box at x=420, y=318)*
```
3.13.7.6.a1
an expected transactions expected_amount = the amount from its financialpattern
{1.2.3c.10.a4}
layer=1
```

**Box `3.13.7.a1`** *(box at x=504, y=378)*
```
3.13.7.a1
if a date from finance_pattern's rrule exists on this page, an expected transaction with this finance_id must exist in the balance record of that day. 
{1.2.3c.10.a4}
layer=1
```

**Box `3.13.7.a2`** *(box at x=420, y=336)*
```
3.13.7.a2
an expected transaction cannot exist on a day not specified by it's financial pattern's date_pattern.
{1.2.3c.10.a4}
3.13.7.1.a1: no two expected transactions in a snapshot can have same finance_id
layer=1
```

**Box `3.13.7.a3`** *(box at x=504, y=396)*
```
3.13.7.a3
all obsolete expected transactions removed or get amounts updated

{3.13.1.a1, 1.2.3c.10.a4, 3.13.7.6.a1, 3.13.7.a2, 3.13.7.1.a1}
&[1.2.3.13.a2: all are paired]
layer=1
{3.13.1.a1, 3.13.7.6.a1, 3.13.7.a2}
```

**Box `3.13.7.a4`** *(box at x=594, y=432)*
```
3.13.7.a4
has all expected transactions
{1.2.3c.10.a4, 3.13.7.a3, 7.1.a1, 
7.7.a1, 7.8.a1, 9.7.6.a1, 3.13.7.a1}
bg=pink
layer=1
```

**Box `3.13.7.a5`** *(box at x=2334, y=486)*
```
3.13.7.a5
expected transactions are maintained
{1.2.3.13.a2, 3.13.7.a4}
bg=light_gray
layer=1
```

### Ch.16 Earmarks

**Box `3.13.8.1.a1`** *(box at x=1032, y=684)*
```
3.13.8.1.a1
only one isolated earmark with finance_id x can exist on a single day. 
{none}
3.13.8.1.a2
only one repeated earmark with finance_id x can exist on a single day. 
{none}
```

**Box `3.13.8.1.a3`** *(box at x=594, y=450)*
```
3.13.8.1.a3
self.finance_id must be = the finance id of the expected transaction this earmark is saving for.
{1.2.3c.11.a4}
```

**Box `3.13.8.3.a1`** *(box at x=1032, y=624)*
```
3.13.8.3.a1
self.repeated_earmark must be = true if it's repeated (generated from a pattern), false if not. 
{1.2.3c.11.a4}
```

**Box `3.13.8.4.a1`** *(box at x=594, y=528)*
```
3.13.8.4.a1
self.expected_amount must be = earmark pattern's amount if self.repeated_earmark is true
{1.2.3c.11.a4}
```

**Box `3.13.8.5.a1`** *(box at x=870, y=660)*
```
3.13.8.5.a1
self.explicit_amount must be = an explicitly given amount from user
{none}
```

**Box `3.13.8.6.a1`** *(box at x=2238, y=420)*
```
3.13.8.6.a1
self.actual_amount must be = the difference between this fund jar's actual amount and the fund jar's actual amount on the previous date, if this is a repeated earmark
{3.13.5.3.a1}
3.13.8.6.a2
self.actual_amount must be = the difference between this fund jar's actual amount and the fund jar's actual amount on the previous date, if this is an isolated earmark and no repeated earmark is on this day
{3.13.5.3.a1}
```

**Box `3.13.8.a1`** *(box at x=594, y=588)*
```
3.13.8.a1
an earmark, even an isolated earmark, cannot exist on a page that does not have an earmark pattern for it, (unless the earmark's finance_id is None)
{1.2.3c.11.a4}
layer=1
```

**Box `3.13.8.a2`** *(box at x=594, y=606)*
```
3.13.8.a2
an earmark cannot exist outside the date_pattern of its earmark pattern (unless the earmark's finance_id is None)
{1.2.3c.11.a4}
layer=1
```

**Box `3.13.8.a3`** *(box at x=1032, y=642)*
```
3.13.8.a3
if day of the rrule pattern is on this page, a repeated earmark with this finance_id must exist in the balance record of that day.
{1.2.3c.11.a4}
layer=1
```

**Box `3.13.8.a6`** *(box at x=1032, y=600)*
```
3.13.8.a6
all obsolete earmarks removed. (implicit earmarks are not cleared)
{3.13.1.a1, 1.2.3c.11.a4, 3.13.8.a1, 
3.13.8.a2, 3.13.8.4.a1}
layer=1
```

**Box `3.13.8.a7`** *(box at x=1122, y=624)*
```
3.13.8.a7
has all earmarks (not including implicit earmarks which may be added later)
{1.2.3c.11.a4, 
3.13.8.a6, 9.8.2.a1, 9.8.6.a1, 
8.1.a1, 8.2.a1, 8.3.a1, 8.4.a1, 8.4.a2, 8.5.a1, 
3.13.8.4.a1,
3.13.8.a3, 3.13.8.1.a1, 3.13.8.1.a2, 3.13.8.1.a3, 
3.13.8.3.a1, 3.13c.8.a5}
layer=1
bg=pink
```

**Box `3.13.8.a8`** *(box at x=2334, y=504)*
```
3.13.8.a8
has all earmarks including implicit earmarks. All earmarks are maintained.
{3.13b.a8, 3.13c.a8, 3.13f.a8}
layer=1
```

**Box `3.13c.8.4.a2`** *(box at x=1830, y=588)*
```
3.13c.8.4.a2
self.expected_amount must be = an explicitly given amount from user plus ?deallocation_implicit_amount?, if self.repeated_earmark is false on a deallocation day
{3.13c.a7}
```

**Box `3.13c.8.a4`** *(box at x=948, y=660)*
```
3.13c.8.a4
all isolated earmarks on balancesnapshot c have expected_amount = explicit_amount. 
{3.13c.8.5.a1}
layer=1
```

**Box `3.13c.8.a5`** *(box at x=1032, y=660)*
```
3.13c.8.a5
implicit earmarks on balancesnapshot c have been cleared. Any isolated earmark with an expected_amount of 0 has been deleted.
{3.13c.8.a4}
layer=1
```

### Ch.17 log_pages & pattern continuity across pages

**Box `1.2.3.10.a1`** *(box at x=168, y=348)*
```
1.2.3.10.a1
if a finance pattern in non-expired page X has an rrule that extends the pattern into page Y, then page Y should have the same finance pattern.
both patterns should have the same values for all their properties.
{4.1.a1, 4.2.a1, 1.2.3b.10.a4, 1.2.3.a1}
1.2.3.10.a2
if a finance pattern in non-expired page X has an rrule that ends before the first day of page Y, page Y should not have that finance pattern.
{4.1.a1, 4.2.a1, 1.2.3b.10.a4, 1.2.3.a1}
1.2.3.10.a3
if a finance pattern in non-expired page X has the same finance id as a finance pattern in page Y, both finance patterns must have the exact same rrule.
the rrule must have the same start date and end date.
{4.1.a1, 4.2.a1, 1.2.3b.10.a4, 1.2.3.a1}
```

**Box `1.2.3.a1`** *(box at x=78, y=360)*
```
1.2.3.a1 
If page X and page Y must have the same number of account pages and the same sets of accountnames. all pages must have the same accounts.
{2.1.a1, 2.2.a1}
```

**Box `1.2.3c.10.a4`** *(box at x=258, y=348)*
```
1.2.3c.10.a4 
all finance patterns for this page c are perfect (but not the events in the page)
{1.2.3c.10.a1, 1.2.3c.10.a2, 1.2.3c.10.a3, 
3.10.a1, 3.10.a2}
bg=light_gray
```

**Box `1.2.3c.11.a1`** *(box at x=420, y=372)*
```
1.2.3c.11.a1
if an earmark pattern in non-expired page X has an rrule that extends the pattern into page Y, then this page Y should have the same earmark pattern.
both patterns should have the same values for all their properties.
{5.1.a1, 5.2.a1, 5.3.a1, 1.2.3c.10.a4}
```

**Box `1.2.3c.11.a2`** *(box at x=420, y=390)*
```
1.2.3c.11.a2
if a earmark pattern in non-expired page X has an rrule that ends before the first day of page Y, this page Y should not have that earmark pattern.
{1.2.3c.10.a4}
```

**Box `1.2.3c.11.a3`** *(box at x=420, y=408)*
```
1.2.3c.11.a3
if a earmark pattern in non-expired page X has the same finance id as a earmark pattern in this page Y, both earmark patterns must have the exact same rrule.
the rrule must have the same start date and end date.
{5.1.a1, 5.2.a1, 5.3.a1, 1.2.3c.10.a4
3.11.2.a1, 3.11.2.a2}
```

**Box `1.2.3c.11.a4`** *(box at x=510, y=516)*
```
1.2.3c.11.a4
all earmark patterns for this page c are perfect. (but not the events in the page)
{1.2.3c.11.a1, 1.2.3c.11.a2, 1.2.3c.11.a3, 
3.11.1.a1, 1.2.3b.11.a4}
bg=light_gray
```

**Box `1.2.a1`** *(box at x=3096, y=552)*
```
1.2.a1 
log_pages cannot by null. It can be an empty list.
{2.1.a1, 2.2.a1, 2.3.a1}
```

### Ch.18 Derived calculations & initial-snapshot inheritance

**Box `1.2.3.5.a1`** *(box at x=2946, y=552)*
```
1.2.3.5.a1
if today is in Y, Y.current_free_amount == Y.balance_record[?today?].full_amount - (Y.current_unpaid_expected) + ?money_in_fund_jars_ready_for_unpaid_bills(unpaid_bill_finance_ids, unpaid_bill_amounts)? - SUM(Y.balance_record[?today?].fund_jars[all].current_amount) 
rewording:current_free_amount ==  (full_amount of today's balance snapshot) - (money in today's fund jars)  - (current_unpaid_expected - money set aside for an unpaid bill in a fund jar)
{1.2.3c.6.a1}
```

**Box `1.2.3.6.a1`** *(box at x=2862, y=564)*
```
1.2.3.6.a1
current_unpaid_expected = total expected_amounts of unfufilled uncancelled expected transactions that occured today or earlier (that are in active pages)
{1.2.3.13.a2, 1.2.3c.12.a2, 1.2.3p.6.a1}
```

**Box `1.2.3.12.2.a1`** *(box at x=1446, y=618)*
```
1.2.3.12.2.a1: 
if no previous AccountTransactionPage X exists, and today is on or after Y.start_date, Y.initial_snapshot.full_amount cannot be None.
if no previous AccountTransactionPage X exists, and today is before Y.started_date, Y.initial_snapshot.full_amount = None{whatever}
1.2.3.12.2.a2
if a previous AccountTransactionPage X exists, Y.initial_snapshot.full_amount == X.balance_record[-1].full_amount
{whatever}
1.2.3.12.3.a1: if no previous AccountTransactionPage X exists, and today is in or after Y, Y.initial_snapshot.expected_amount == None
{whatever}
1.2.3.12.3.a3
if no previous AccountTransactionPage X exists, and today occurs before Y, initial_snapshot.expected_amount cannot be None

1.2.3.12.5.a1
if no previous AccountTransactionPage X exists, Y.initial_snapshot.fund_jars should include a 
fund jar for each earmark pattern that starts before Y.start_date. and a fund jar with a finance_id of None{whatever}
1.2.3.12.5.a4: if no previous AccountTransactionPage X exists, and today is in or after Y, the current_amount of all fund jars in Y.initial_snapshot.fund_jars cannot be None
1.2.3.12.5.a5: if no previous AccountTransactionPage X exists, and today occurs before Y, the current_amount of all fund jars in Y.initial_snapshot.fund_jars is None
1.2.3.12.5.a6: if no previous AccountTransactionPage X exists, and today is in or after Y, the expected_amount of all fund jars in Y.initial_snapshot.fund_jars = None
1.2.3.12.5.a7: if no previous AccountTransactionPage X exists, and today occurs before Y, the expected_amount of all fund jars in Y.initial_snapshot.fund_jars cannot be None
```

**Box `1.2.3.12.3.a2`** *(box at x=2232, y=504)*
```
1.2.3.12.3.a2
if a previous AccountTransactionPage X exists, Y.initial_snapshot.expected_amount == X.balance_record[-1].expected_amount
{3p.13.5.3.a1}
1.2.3.12.3.a3: if no previous AccountTransactionPage X exists, and today occurs before Y, initial_snapshot.expected_amount cannot be None
```

**Box `1.2.3.12.4.a1`** *(box at x=2424, y=606)*
```
1.2.3.12.4.a1
if no previous AccountTransactionPage X exists, Y.initial_snapshot.expected_free_amount == None
{whatever}
```

**Box `1.2.3.12.4.a2`** *(box at x=2574, y=552)*
```
1.2.3.12.4.a2
if a previous AccountTransactionPage X exists, Y.initial_snapshot.expected_free_amount == X.balance_record[-1].expected_free_amount
{3p.13.4.a1}
```

**Box `1.2.3.12.5.a2`** *(box at x=2334, y=420)*
```
1.2.3.12.5.a2
if a previous AccountTransactionPage X exists, Y.initial_snapshot.fund_jars == X.balance_record[-1].fund_jars.?deepcopy? but no date.
{whatever}
```

**Box `1.2.3.13.a1`** *(box at x=846, y=522)*
```
1.2.3.13.a1
all can be paired
{3.13b.a5, 3.13c.a5, 3.13f.a5}
fg=black
```

**Box `1.2.3.13.a2`** *(box at x=942, y=546)*
```
1.2.3.13.a2
all expected-actual transaction pairs have been created and are properly matched
{1.2.3.a1, 1.2.3.13.a1, 1.2.3.13.7.a1, 
1.2.3.13.7.a2, 1.2.3.13.6.a1, 1.2.3.13.6.a2, 
6.4.a1, 7.4.a1, 7.4.a2, 7.5.a1, 7.5.a2}
bg=pink
```

**Box `1.2.3c.12.a1`** *(box at x=2502, y=366)*
```
1.2.3c.12.a1
the initial snapshot for this page c is good except for expected amounts
{1.2.3c.12.2.a1, 1.2.3c.12.2.a2, 1.2.3c.12.5.a1, 
1.2.3c.12.5.a2, 9.1.a1,
3p.13.2.a4, 9.5.a1. 9.5.1.a1, 
9.6.2.a1, 9.7.6.a1, 9.8.2.a1, 9.8.6.a1,
3.12.a1, 1.2.3c.10.a4, 3.12.5.a1, 
1.2.3c.11.a4, 3.12.5.a2, 
3.12.1.a1, 3.12.6.a1, 3.12.7.a1, 3.12.8.a1}
bg=light_gray
{1.2.3.12.2.a1, 9.1.a1, 
9.5.1.a1, 9.6.2.a1, 9.7.6.a1, 
9.8.2.a1, 9.8.6.a1, 3.12.a1, 
1.2.3.10.a4, 3.12.5.a1, 3.12.5.a2, 
3.12.1.a1, 3.12.6.a1}
```

**Box `1.2.3c.12.a2`** *(box at x=2706, y=534)*
```
1.2.3c.12.a2
the initial snapshot for this page c is maintained
{1.2.3c.12.a1, 
(1.2.3c.12.3.a1), 
(1.2.3c.12.3.a2),
1.2.3c.12.4.a1, 
1.2.3c.12.4.a2, 
(9.5.a1), 
(9.5.1.a1), 1.2.3c.12.5.a2, 3p.a1}
{1.2.3.12.a1, (9.5.a1), 1.2.3.12.4.a1, 
1.2.3c.12.4.a2, 3p.a1}
bg=light_gray
```

**Box `1.2.3c.12.a3`** *(box at x=2856, y=468)*
```
1.2.3c.12.a3
the initial snapshot for this page has updated conciously-applied calculations (fundjar.milestone amount, page.current_free_amount, page.current_unpaid, current_safety_cushion, )
{3p.a2, 1.2.3.12.5.a1, 1.2.3.12.5.a2}
```

**Box `1.2.3c.13.a3`** *(box at x=1278, y=600)*
```
1.2.3c.13.a3
this current accounttransaction page c is ready to remove balance snapshops with no events in them.
{3c.13.a2}
```

### Ch.19 Expected/Actual pairing completeness

**Box `1.2.3.13.6.a1`** *(box at x=846, y=546)*
```
1.2.3.13.6.a1: 
if self.paired_finance_id == None and self.paired_expected_date == None and we are not expired, there should NOT exist an ExpectedTransaction E in an active page such that:
	E.paired_amount == self.amount
	E.paired_actual_date == self.occurred_date
	E.expected_date == self.paired_expected_date
	E.finance_id == self.paired_finance_id
{3.13.6.a2, 3.13.7.a4}
1.2.3.13.6.a2: 
paired:if self.paired_finance_id != None and self.paired_expected_date != None and we are not expired, there should exist an Expectedtransaction E in an active page such that:
	E.cancelled == false
	E.paired_amount == self.amount
	E.paired_actual_date == self.occurred_date
	E.expected_date == self.paired_expected_date
	E.finance_id == self.paired_finance_id
{3.13.6.a2, 3.13.7.a4}
1.2.3.13.7.a1
if self.paired_amount == None and self.paired_actual_date == None and we are not expired, there should NOT exist an ActualTransaction A in an active page such that:
	self.paired_amount == A.amount
	self.paired_actual_date == A.occurred_date
	self.expected_date == A.paired_expected_date
	self.finance_id == A.paired_finance_id
{3.13.6.a2, 3.13.7.a4}
1.2.3.13.7.a2
if self.paired_amount != None and self.paired_actual_date != None and we are not expired, there should exist an ActualTransaction A in an active page such that:
	self.paired_amount == A.amount
	self.paired_actual_date == A.occurred_date
	self.expected_date == A.paired_expected_date
	self.finance_id == A.paired_finance_id
{3.13.6.a2, 3.13.7.a4}
```

### Free-floating text annotations (process-step labels)

These are the plain-language labels the dotted process regions sit next to — see [06's Process regions section](06-assumption-dependency-graph.md#process-regions--the-cascade-steps) for which assumptions each labels. Reproduced verbatim (the trailing `style=wordwrap` is a stored UMLet attribute):

*(text at x=168, y=396)*
```
Finance patterns in this page are perfect
style=wordwrap
```

*(text at x=420, y=480)*
```
Earmark patterns in this page are perfect
style=wordwrap
```

*(text at x=420, y=516)*
```
Loop over pages and fix patterns
style=wordwrap
```

*(text at x=678, y=282)*
```
New events created. 
Old events removed.
style=wordwrap
```

*(text at x=924, y=504)*
```
transactions are paired
style=wordwrap
```

*(text at x=1116, y=636)*
```
all* earmarks created on this page
style=wordwrap
```

*(text at x=1224, y=630)*
```
The whole page is cleaned of obsolete events and balance snapshots
style=wordwrap
```

*(text at x=1416, y=402)*
```
Fund Jar.. stuff
style=wordwrap
```

*(text at x=1608, y=618)*
```
This Balance Snapshot has full_amount
style=wordwrap
```

*(text at x=1818, y=630)*
```
If this is a deallocation day, the implicit earmarks are made.
style=wordwrap
```

*(text at x=2022, y=648)*
```
Loop over balance snapshots in page
style=wordwrap
```

*(text at x=2304, y=522)*
```
Loop over all pages, updating 
expected_amount on initial snapshots.
Loop over all budget snapshots in each 
page, updating expected_amount
style=wordwrap
```

*(text at x=2490, y=306)*
```
initial snapshot is good except for expected amount
style=wordwrap
```

*(text at x=2508, y=600)*
```
Loop over all pages, updating expected_free_amount on initial snapshots.
Loop over all budget snapshots in each page, updating expected_free_amount
style=wordwrap
```

*(text at x=2796, y=582)*
```
also calculate current_unpaid_expected for each page
current_free_amount for today's page
current_safety_cushion for today's page
      and milestone_amount for each fund jar
style=wordwrap
```

### Dotted region outlines

The chart draws 14 dotted outlines grouping related assumptions, each next to a plain-language label. The outlines have no text of their own — their labels are transcribed above under [Free-floating text annotations](#free-floating-text-annotations). Each outline marks one step of the cascade update; the labels and the assumptions in each step are laid out in [06-assumption-dependency-graph.md → Process regions](06-assumption-dependency-graph.md#process-regions--the-cascade-steps). Single-dot (`lt=.`) vs double-dot (`lt=..`) outlines mark the nesting level where regions sit inside one another.

---

## Part 2 — `assumptionChartTests.uxf` (the test-bundling chart)

Saved the same day as the final chart, ~6 hours earlier. Two kinds of boxes:

- **24 `T`-numbered test-bundle boxes** (`2.T1`, `3.T01`...`3.T11`, etc.) — each contains the full text of every assumption that one test function would verify together. [06's test-bundling plan](06-assumption-dependency-graph.md#the-authors-test-bundling-plan) tabulates the bundles; the verbatim text below is the source of that table. Note `2.T1` is the only place anywhere in the project that *names* the chart-only month-boundary rules as **`2.1.a2`** and **`2.2.a2`**.
- **64 assumption boxes** — near-duplicates of the same boxes in `assumptionChartSimpleLines.uxf`, except that in several of them the requirement sets are **rewired to reference test IDs** (e.g. `1.2.3.10.a2`'s requirements become `{4.T1, 1.2.3b.10.a4, 1.T01}`) — the assumption graph being converted into a test-dependency graph in place.

Its 15 text annotations are byte-identical to Part 1's (verified mechanically) and are not repeated here. Its 14 dotted regions carry the *same set of labels* but are **not** identical — outlines were drawn/kept independently in each file, so memberships differ (this chart predates the final chart by ~6 hours and boxes sat in different places). Both region tables are included so they can be compared.

### Test-bundle boxes

**Box `1.T01`** *(box at x=104, y=152)*
```
1.T01
1.2.3.a1: If page X and page Y must have the same number of account 
pages and the same sets of accountnames. 
all pages must have the same accounts.
{2.T1,
1.1.a1: The distance between start and end date for each non-expired page, must be
equal to self.page_length months. 
}
```

**Box `1.T02`** *(box at x=344, y=152)*
```
1.T02

4.T1
1.T01

For every TransactionLogPage, all 4 rules are true
1.2.3.10.a2:
if a finance pattern in non-expired page X has an rrule that 
ends before the first day of page Y, page Y should not have 
that finance pattern.
1.2.3.10.a3:
if a finance pattern in non-expired page X has the same 
finance id as a finance pattern in page Y, both finance 
patterns must have the exact same rrule.
the rrule must have the same start date and end date.
1.2.3.10.a1:
if a finance pattern in non-expired page X has an rrule that 
extends the pattern into page Y, then page Y should have the 
same finance pattern.
both patterns should have the same values for all their properties.




summary:
all finance patterns for this page c are perfect (but not the events in the page)
```

**Box `1.T04`** *(box at x=576, y=376)*
```
1.T04

1.T02
3.T04
3.11.1.a1: all earmark patterns self.earmark_patterns should 
have unique finance_ids.
1.2.3c.11.a2: if a earmark pattern in non-expired page X has an 
rrule that ends before the first day of page Y, 
this page Y should not have that earmark pattern.
1.2.3c.11.a3: if a earmark pattern in non-expired page X has the 
same finance id as a earmark pattern in this page Y, both 
earmark patterns must have the exact same rrule. 
the rrule must have the same start date and end date.
1.2.3c.11.a1
if an earmark pattern in non-expired page X has an rrule that 
extends the pattern into page Y, then this page Y should have 
the same earmark pattern. both patterns should have 
the same values for all their properties.

rules for AccountTransactionPage must be true for all 
AccountTransactionPages in this TransactionLogPage, 
from previous to current
summary:
all earmark patterns for this page c are perfect. 
(but not the events in the page)

bg=light_gray
```

**Box `2.T1`** *(box at x=0, y=152)*
```
2.T1
TransactionLogPage: test for
{2.1.a1: start_date cannot be None. Must be before end_date.
,2.2.a1: end_date cannot be None. Must be after start_date
2.1.a2: start_date should be the first day of a month
2.2.a2: end_date should be the last day of a month
}
```

**Box `3.T01`** *(box at x=104, y=296)*
```
3.T01

3.2.a1: start_date cannot be None
3.3.a1: end_date cannot be None
3.2.a2: start_date must be a date before end_date
start_date should be the first day of a month
3.3.a2: end_date must be a date after start_date
end_date should be the last day of a month
3.5.a1: current_free_amount = None if today is not between 
self.start_date and self.end_date
3.8.a1: ideal_safety_cushion cannot be None
3.9.a1: safety_priority cannot be None
bg=green
```

**Box `3.T02`** *(box at x=224, y=176)*
```
3.T02
3.10.a1:
self.finance_patterns should only contain patterns that 
end after self.start_date and begin at or before self.end_date
{3.T01}

3.10.a2: self.finance_patterns cannot contain any duplicate ids
```

**Box `3.T03`** *(box at x=456, y=112)*
```
3.T03
3.13.7.6.a1 : n expected transactions expected_amount = the 
amount from its financialpattern
{1.T02}

3.13.7.a2:
an expected transaction cannot exist on a day not 
specified by it's financial pattern's date_pattern.
```

**Box `3.T04`** *(box at x=456, y=304)*
```
3.T04

5.T1
3.11.2.a1: self.earmark_patterns should only contain patterns 
that end after self.start_date and begin at or before 
self.end_date
3.11.2.a2: an earmark pattern's date_pattern cannot extend 
beyond or before the date_pattern of it's associated 
finance_pattern.
```

**Box `3.T05`** *(box at x=456, y=232)*
```
3.T05

3.13.1.a1: all BalanceSnapshots inside self.balance_record should have 
a snapshot_date that is => self.start_date  
and is =< self.end_date
{3.T01}
```

**Box `3.T06`** *(box at x=792, y=464)*
```
3.T06

3.13.8.a1: an earmark, even an isolated earmark, cannot exist on a 
page that does not have an earmark pattern for it, 
(unless the earmark's finance_id is None)
3.13.8.a2: an earmark cannot exist outside the date_pattern of its 
earmark pattern (unless the earmark's finance_id is None)
3.13.8.4.a1: self.expected_amount must be = earmark pattern's amount 
if self.repeated_earmark is true
3.13.8.a6: all obsolete earmarks removed. 
(implicit earmarks are not cleared)
```

**Box `3.T07`** *(box at x=1272, y=520)*
```
3.T07
3.13.8.3.a1: self.repeated_earmark must be = true if it's 
repeated (generated from a pattern), false if not. 
3.13.8.a3: if day of the rrule pattern is on this page, 
a repeated earmark with this finance_id must exist in the 
balance record of that day.
{1.2.3c.11.a4}
```

**Box `3.T08`** *(box at x=1272, y=568)*
```
3.T08
3.13.8.5.a1: self.explicit_amount must be = an explicitly given 
amount from user
3.13c.8.a4: all isolated earmarks on balancesnapshot c have 
expected_amount = explicit_amount.
3.13c.8.a5: implicit earmarks on balancesnapshot c have been cleared. 
Any isolated earmark with an expected_amount of 0 has been deleted.
3.13.8.1.a1
only one isolated earmark with finance_id x can exist on a single day. 
3.13.8.1.a2
only one repeated earmark with finance_id x can exist on a single day. 
```

**Box `3.T09`** *(box at x=1392, y=520)*
```
3.T09

3.T06
3.T07
3.T08
3.13.8.a7: has all earmarks (not including implicit earmarks 
which may be added later)

bg=pink
```

**Box `3.T10`** *(box at x=792, y=160)*
```
3.T10
3.13.6.a1: all obsolete actual transactions removed
3.13.6.a2: has all actual
{3.13.6.a1, 6.1.a1,6.5.a1,6.6.a1, 9.6.2.a1}
bg=pink
layer=1

3.13.6.3.a1: 
no two actual transactions in a snapshot can have the same paired_finance_id and paired_expected_date
```

**Box `3.T11`** *(box at x=568, y=208)*
```
3.T11
3.13.7.a1
if a date from finance_pattern's rrule exists on this page, 
an expected transaction with this finance_id 
must exist in the balance record of that day. 
3.13.7.1.a1: no two expected transactions in a snapshot can have same finance_id
{1.2.3c.10.a4}
3.13.7.a3
all obsolete expected transactions removed or get 
amounts updated
{3.13.1.a1, 1.2.3c.10.a4, 3.T03, 3.13.7.1.a1}
{3.13.7.6.a1, 3.13.7.a2}
```

**Box `4.T1`** *(box at x=104, y=184)*
```
4.T1

FinancialPattern
4.1.a1: finance_id cannot be None
4.2.a1: source cannot be None
```

**Box `5.T1`** *(box at x=208, y=56)*
```
5.T1
5.2.a1: self.date_pattern cannot be None
self.date_pattern rrule must have until property, not count property
5.1.a1: self.finance_id cannot be None
5.3.a1: self.amount cannot be None.
bg=green
```

**Box `6.T1`** *(box at x=680, y=72)*
```
6.T1

6.1.a1
self.source cannot be None.
{none}
6.5.a1
self.made_in_bulk cannot be None
{none}
6.6.a1
self.amount cannot be None.
{none}
bg=green
```

**Box `7.T01`** *(box at x=568, y=280)*
```
7.T01

7.1.a1: self.finance_id cannot be None.
7.7.a1: self.amount_tolerance cannot be None, and cannot have 
a negative value for either number.
7.6.a1: self.expected_amount cannot be None.
7.8.a1: self.date_tolerance cannot be None. and cannot have 
a negative value for either number.
bg=green
```

**Box `7.T02`** *(box at x=688, y=328)*
```
7.T02
7.3.a1
cancelled cannot be None
{none}
7.4.a1
If self.paired_actual_date == None then 
self.paired_amount == None. 
If self.paired_actual_date != None 
then self.paired_amount != None
{none}
7.4.a2
self.paired_amount == None if self.cancelled == true
{none}
7.5.a1
if self.paired_amount == None then 
self.paired_actual_date == None. 
if self.paired_amount != None then 
self.paired_actual_date != None. 
{none}
7.5.a2
self.paired_actual_date == None if self.cancelled == true
{none}
```

**Box `8.T1`** *(box at x=688, y=488)*
```
8.T1
8.1.a1: finance_id cannot be None if self.repeated_earmark == True
8.2.a1: earmark_date cannot be None.
8.3.a1: repeated_earmark cannot be None. default is false.
8.5.a1: self.explicit_amount must be None if self.repeated_earmark == true
8.4.a1: expected_amount cannot be None.
8.4.a2: self.expected_amount must be = an explicitly given amount from user if self.repeated_earmark is false on a normal day
```

**Box `9.T01`** *(box at x=568, y=168)*
```
9.T01

9.7.6.a1
self.expected_transactions[all].expected_amount == 
self.snapshot_date
{none}
```

**Box `9.T02`** *(box at x=688, y=176)*
```
9.T02
9.6.2.a1
self.actual_transactions[all].occurred_date == 
self.snapshot_date
{none}
```

**Box `10.T1`** *(box at x=696, y=424)*
```
10.T1
10.3.a1
self.expected_amount is = None if self.finance_id is None.
10.4.a1
self.milestone_amount should be = None if 
self.finance_id is None
{none}
10.4.a2
if the finance id for this fund jar is for 
a repeated negative expected transaction, 
self.milestone_amount should be -1*amount 
from the finance_pattern
{whatever}
10.4.a3
if the finance id for this fund jar is for a 
repeated positive expected transaction, 
self.milestone_amount should be none
{whatever}
```

### Assumption boxes (as they appear in the tests chart)

### Ch.1 TransactionLogPage

**Box `2.3.a1`** *(box at x=3936, y=480)*
```
2.3.a1 
account_pages cannot be None. Can be empty dictionary
{none}
```

### Ch.4 ActualTransaction

**Box `6.4.a1`** *(box at x=1024, y=360)*
```
6.4.a1: If self.paired_expected_date == None then self.paired_finance_id == None.
```

### Ch.7 BalanceSnapshot

**Box `9.1.a1`** *(box at x=3008, y=128)*
```
9.1.a1
snapshot_date can be None. If it is None, actual_transactions, expected_transactions, and earmark_events are empty lists.
{none}
```

**Box `9.5.1.a1`** *(box at x=2080, y=232)*
```
9.5.1.a1
all funds jars in self.fund_jars must have a unique finance_id.
{none}
```

**Box `9.5.a1`** *(box at x=1616, y=224)*
```
9.5.a1
self.fund_jars must have a single fund jar with a finance_id of None
{10.3.a1, 10.4.a1}
```

**Box `9.8.2.a1`** *(box at x=3112, y=104)*
```
9.8.2.a1
self.earmark_events[all].earmark_date == self.snapshot_date
{none}
```

**Box `9.8.6.a1`** *(box at x=3112, y=128)*
```
9.8.6.a1
self.earmark_events[any].actual_amount must be = None if this is an isolated earmark and there is also a repeated earmark on this day.
{none}
```

### Ch.9 Page identity, bounds & top-level summary

**Box `3.1.a1`** *(box at x=3824, y=480)*
```
3.1.a1
account cannot be None
{none}
```

**Box `3.7.a1`** *(box at x=3824, y=352)*
```
3.7.a1
current_safety_cushion = None if today is not between self.start_date and self.end_date
{1.2.3c.12.a2}
```

**Box `3.7.a2`** *(box at x=3824, y=376)*
```
3.7.a2
current_safety_cushion = money in today's fund jar with finance_id == None
{1.2.3c.12.a2}
```

**Box `3.10.a3`** *(box at x=792, y=200)*
```
3.10.a3
if an EarMarkPattern, EarMarkEvent, FundJar, or ExpectedTransaction exists in our page with a finance_id of x and our page has no finance pattern with a finance_id of x, that EarMarkPattern, EarMarkEvent, FundJar, or ExpectedTransaction shouldn't exist
{1.2.3c.11.a4, 8.1.a1, 8.2.a1, 8.3.a1, 8.4.a1, 
8.5.a1, 3.13.8.1.a3, 3.13.5.a2
10.3.a1,
7.1.a1, 7.3.a1, 7.4.a1, 7.4.a2, 7.5.a1, 7.5.a2, 
7.7.a1, 7.8.a1, 3.13.7.a2, 3.13.7.a4
1.2.3c.10.a4, 4.1.a1}
bg=pink
```

**Box `3.a1`** *(box at x=3600, y=360)*
```
3.a1
this page is maintained
{1.2.3c.12.a2, 3.13.2.a4, 3.13.3.a1, 
3.13.7.a5,
3.13.6.a3, 3.13.8.a8, 3.13.5.a5, 3.13.a9}
{3.13.2.a4, 3.13.3.a1, 3.13.7.a5, 
3.13.6.a3, 
3.13.8.a8, 3.13.5.a5, 3.13.a9}
bg=light_gray
```

**Box `3.a2`** *(box at x=3928, y=360)*
```
3.a2
this page has updated conciously-applied calculations (fundjar.milestone_amount, page.current_free_amount, page.current_unpaid_expected, current_safety_cushion, )
{3.13.5.4.a1, 3.5.a1, 1.2.3.5.a1, 
(1.2.3.6.a1), 3.7.a1, 3.7.a2}
bg=light_gray
```

### Ch.10 Initial Snapshot

**Box `3.12.1.a1`** *(box at x=3008, y=80)*
```
3.12.1.a1
self.initial_snapshot.snapshot_date = None
{none}
3.12.6.a1
self.initial_snapshot.actual_transactions == []
{none}
3.12.7.a1
self.initial_snapshot.expected_transactions == []
{none}
3.12.8.a1
self.initial_snapshot.earmark_events == []
{none}
```

**Box `3.12.5.a1`** *(box at x=1264, y=184)*
```
3.12.5.a1
initial_snapshot.fund_jars cannot have a fund jar that is not in our ?initial finance_patterns? (except the fund jar with None as a finance_id)
{3.13.5.a1, 3.13.5.a2}
3.12.5.a2
no fund jar can exist here if it has a finance pattern that starts after (or on?) ?self.start_date?
{3.13.5.a2}
```

**Box `3.12.a1`** *(box at x=3008, y=104)*
```
3.12.a1
initial_snapshot cannot be None
{whatever}
```

### Ch.11 Balance Record

**Box `3.13.a2`** *(box at x=1504, y=488)*
```
3.13.a2
all obsolete events of all kinds have been removed, obsolete fund jars removed, and all events exist that should (not including fund jars) (not including implicit earmarks which may be added later)
{3.13.6.a2, 3.13.7.a4, 3.13.8.a7, 
3.13.5.a3, 3.10.a3}
```

**Box `3.13.a3`** *(box at x=1696, y=488)*
```
3.13.a3
each balance snapshot inside balance_record has at least one event (earmark, actual event, or expected event). All other balance snapshots have been removed.
{3.13.a2, 1.2.3.13.a3}
```

**Box `3.13.a4`** *(box at x=1824, y=536)*
```
3.13.a4
full_amount can be safely calculated for balance snapshops
{3.13.a3}
```

**Box `3.13.a5`** *(box at x=904, y=384)*
```
3.13.a5
we have enough information to pair actual and expected events
{3.13b.6.a1, 3.13c.6.a1, 3.13f.6.a1, 
3.13b.6.a2, 3.13c.6.a2, 3.13f.6.a2, 
3.13b.7.a3, 3.13c.7.a3, 3.13f.7.a3, 
3.13b.7.a4, 3.13c.7.a4, 3.13f.7.a4}
```

**Box `3:13.a9`** *(box at x=3008, y=224)*
```
3:13.a9
have expected amounts up to date
{7.6.a1, 3.13.8.6.a1, 3.13.8.6.a2}
```

**Box `3.13c.a6`** *(box at x=1928, y=400)*
```
3.13c.a6
we know whether or not this is a deallocation day. And implicit earmarks are cleared.
{3.13p.2.a1, 3.13.5.a4, 3.13p.5.2.a5, 
3.8.a1, 3.9.a1, 3.13c.8.a5, 3.13.6.a2}
bg=pink
```

**Box `3.13c.a7`** *(box at x=2224, y=472)*
```
3.13c.a7
if this is a deallocation day, we have added the new implicit isolated earmarks to earmark_events
{3.13c.a6, 3.13c.8.a7, 1.2.3.13.a2, 
9.5.a1, 9.5.1.a1}
&[3.13c.8.1.a1, 3.13c.8.a5]

3.13c.a10
{whatever}
&[3.13c.8.1.a1, 3.13c.8.a5]
if this is not a deallocation day, and an expected transaction transaction with a fund jar has a paired actual transaction on this day,
create an implicit earmark for that fund jar with the actual transaction's amount
```

**Box `3.13c.a8`** *(box at x=2448, y=472)*
```
3.13c.a8
if this is a deallocation day, the newly added earmarks are now properly merged into existing isolated earmarks if they existed.
{3.13c.a7, 3.13c.8.1.a1, !3.13c.8.a5, 
3.13c.8.4.a2}
```

### Ch.12 balance_record full_amount & expected amounts

**Box `3.13.2.a4`** *(box at x=3008, y=176)*
```
3.13.2.a4
balancesnapshots inside self.balance_record have the correct value for full_amount
{3.13c.2.a1, 3.13f.2.a1}
layer=1
```

**Box `3.13.3.a1`** *(box at x=3344, y=272)*
```
3.13.3.a1
self.balance_record[i].expected_amount = (self.balance_record[i-1].full_amount if self.balance_record[i-1].full_amount != None else self.balance_record[i-1].expected_amount) + SUM(self.balance_record[i].expected_transactions[all].expected_amount)
{1.2.3c.12.a1, 3.13p.2.a1, 
3.13p.3.a1, 3.13.7.a4, 3.13.a3}
layer=1
```

**Box `3.13.4.a1`** *(box at x=3480, y=456)*
```
3.13.4.a1
self.balance_record[i].expected_free_amount = self.balance_record[i].expected_amount - SUM(self.balance_record[i].fund_jars[all].expected_amount)
{1.2.3.12.3.a1, 1.2.3.12.3.a2,
1.2.3.12.4.a1, 1.2.3.12.4.a2,
3.13.3.a1, 3.13.5.3.a1
1.2.3.12.5.a1, 1.2.3.12.5.a2
}
```

**Box `3.13c.2.a1`** *(box at x=2072, y=456)*
```
3.13c.2.a1
balancesnapshot c has proper full_amount
{3.13c.2.a2, 3.13c.2.a3}
```

**Box `3.13c.2.a2`** *(box at x=1952, y=480)*
```
3.13c.2.a2
if balancesnapshot c in self.balance_record occurs after today, c.full_amount = None
{3.13.a4, 1.2.3.12.2.a1, 1.2.3.12.2.a2, 
3.13p.2.a2, 3.13p.2.a3}
layer=1
```

**Box `3.13c.2.a3`** *(box at x=1952, y=512)*
```
3.13c.2.a3
if balancesnapshot c in self.balance_record occurs today or before, c.full_amount = p.full_amount + sum(x.amount for x in c.actual_transactions)
{3.13.a4, 1.2.3.12.2.a1, 1.2.3.12.2.a2, 
3.13.6.a2, 3.13p.2.a1}
layer=1
```

### Ch.13 Fund Jars

**Box `3.13.5.2.a1`** *(box at x=2824, y=512)*
```
3.13.5.2.a1
self.balance_record[any].fund_jars[all].current_amount must be = None if day has not occurred
{3.13c.2.a2}
layer=1
```

**Box `3.13.5.2.a2`** *(box at x=2824, y=472)*
```
3.13.5.2.a2
self.balance_record[any].fund_jars[all].current_amount cannot be None if it is the current day or before.
{3.13.5.2.a5}
layer=1
```

**Box `3.13.5.2.a3`** *(box at x=2560, y=496)*
```
3.13.5.2.a3
self.balance_record[any].fund_jars[all].current_amount = ?normal_fund_daily_distribution? if the day has occurred and it is a normal day
{whatever}
layer=1
```

**Box `3.13.5.3.a1`** *(box at x=2776, y=288)*
```
3.13.5.3.a1
self.expected_amount is = the expected_amount of the previous day (or current_amount of previous day if one exists) plus the expected_amount of all earmark_events with the same financial_id on that day
{3.13.5.2.a5}
&[1.2.3n.12.5.a1, 1.2.3n.12.5.a2]
layer=1
```

**Box `3.13.5.4.a1`** *(box at x=3824, y=400)*
```
3.13.5.4.a1
self.milestone_amount should be = sum of all expected values of repeated earmarks up to and including this date (unless it has a finance_id of None. or if the finance id is for a repeated expected transaction)
{(1.2.3c.12.a2), 1.2.3c.12.a3, 
(3.13.8.a8), (3.13.5.a5), 3.a1}
&[1.2.3n.12.5.a1, 1.2.3n.12.5.a2]
layer=1
```

**Box `3.13.5.a1`** *(box at x=688, y=200)*
```
3.13.5.a1: 
if the day exists within EarMarkPattern A's date_pattern(any day between start and end), a fund jar with A's finance_id must exist on that day.
3.13.5.a2: a fund jar can only exist on days that fall within its earmark pattern's rrule (unless the fund jar's finance_id is None)
```

**Box `3.13.5.a3`** *(box at x=792, y=232)*
```
3.13.5.a3
all obsolete fund jars removed
{3.13.1.a1, 1.2.3c.11.a4, 3.13.5.a2}
layer=1
```

**Box `3.13.5.a4`** *(box at x=792, y=312)*
```
3.13.5.a4
has all fund jars (the amount might not be accurate)
{3.13.5.a1, 3.13.5.a2}
layer=1
bg=pink
```

**Box `3.13.5.a5`** *(box at x=3008, y=288)*
```
3.13.5.a5
all fund jars are maintained
{(3.13b.5.2.a5), (3.13c.5.2.a5), (3.13f.5.2.a5),
3.13.5.3.a1, (10.3.a1)}
layer=1
bg=light_gray
```

**Box `3.13c.5.2.a4`** *(box at x=2560, y=472)*
```
3.13c.5.2.a4
self.balance_record[i].fund_jars[j].current_amount = self.balance_record[i].fund_jars[j-1].current_amount + sum( expected_amount of today's earmarks for this fund jar) after ?deallocation_fund_distribution? if the day has occurred and it is a deallocation day.
{3.13c.a8}
layer=1
```

**Box `3.13c.5.2.a5`** *(box at x=2664, y=472)*
```
3.13c.5.2.a5
fund jars of balancesnapshot c have proper current_amount
{3.13c.5.2.a3, 3.13c.5.2.a4}
layer=1
```

### Ch.14 Actual Transactions

**Box `3.13.6.a3`** *(box at x=3008, y=312)*
```
3.13.6.a3
actual transactions are maintained
{1.2.3.13.a2, 3.13.6.a2}
bg=light_gray
layer=1
```

### Ch.15 Expected Transactions

**Box `3.13.7.a4`** *(box at x=688, y=264)*
```
3.13.7.a4
has all expected transactions
{1.2.3c.10.a4, 3.13.7.a3, 7.1.a1, 
7.7.a1, 7.8.a1, 9.7.6.a1, 3.13.7.a1}
bg=pink
layer=1
```

**Box `3.13.7.a5`** *(box at x=3008, y=336)*
```
3.13.7.a5
expected transactions are maintained
{1.2.3.13.a2, 3.13.7.a4}
bg=light_gray
layer=1
```

### Ch.16 Earmarks

**Box `3.13.8.1.a3`** *(box at x=688, y=288)*
```
3.13.8.1.a3
self.finance_id must be = the finance id of the expected transaction this earmark is saving for.
{1.2.3c.11.a4}
```

**Box `3.13.8.6.a1`** *(box at x=2880, y=248)*
```
3.13.8.6.a1
self.actual_amount must be = the difference between this fund jar's actual amount and the fund jar's actual amount on the previous date, if this is a repeated earmark
{3.13.5.3.a1}
3.13.8.6.a2
self.actual_amount must be = the difference between this fund jar's actual amount and the fund jar's actual amount on the previous date, if this is an isolated earmark and no repeated earmark is on this day
{3.13.5.3.a1}
```

**Box `3.13.8.a8`** *(box at x=3008, y=360)*
```
3.13.8.a8
has all earmarks including implicit earmarks. All earmarks are maintained.
{3.13b.a8, 3.13c.a8, 3.13f.a8}
layer=1
```

**Box `3.13c.8.4.a2`** *(box at x=2336, y=472)*
```
3.13c.8.4.a2
self.expected_amount must be = an explicitly given amount from user plus ?deallocation_implicit_amount?, if self.repeated_earmark is false on a deallocation day
{3.13c.a7}
```

### Ch.17 log_pages & pattern continuity across pages

**Box `1.2.3.10.a2`** *(box at x=224, y=152)*
```
1.2.3.10.a2
if a finance pattern in non-expired page X has an rrule that ends before the first day of page Y, page Y should not have that finance pattern.
{4.T1, 1.2.3b.10.a4, 1.T01}
1.2.3.10.a3
if a finance pattern in non-expired page X has the same finance id as a finance pattern in page Y, both finance patterns must have the exact same rrule.
the rrule must have the same start date and end date.
{4.T1, 1.2.3b.10.a4, 1.T01}
1.2.3.10.a1
if a finance pattern in non-expired page X has an rrule that extends the pattern into page Y, then page Y should have the same finance pattern.
both patterns should have the same values for all their properties.
{4.T1, 1.2.3b.10.a4, 1.T01}
```

**Box `1.2.a1`** *(box at x=4024, y=424)*
```
1.2.a1 
log_pages cannot by null. It can be an empty list.
{2.1.a1, 2.2.a1, 2.3.a1}
```

### Ch.18 Derived calculations & initial-snapshot inheritance

**Box `1.2.3.5.a1`** *(box at x=3824, y=424)*
```
1.2.3.5.a1
if today is in Y, Y.current_free_amount == Y.balance_record[?today?].full_amount - (Y.current_unpaid_expected) + ?money_in_fund_jars_ready_for_unpaid_bills(unpaid_bill_finance_ids, unpaid_bill_amounts)? - SUM(Y.balance_record[?today?].fund_jars[all].current_amount) 
rewording:current_free_amount ==  (full_amount of today's balance snapshot) - (money in today's fund jars)  - (current_unpaid_expected - money set aside for an unpaid bill in a fund jar)
{1.2.3c.6.a1}
```

**Box `1.2.3.6.a1`** *(box at x=3712, y=440)*
```
1.2.3.6.a1
current_unpaid_expected = total expected_amounts of unfufilled uncancelled expected transactions that occured today or earlier (that are in active pages)
{1.2.3.13.a2, 1.2.3c.12.a2, 1.2.3p.6.a1}
```

**Box `1.2.3.12.2.a1`** *(box at x=1824, y=512)*
```
1.2.3.12.2.a1: 
if no previous AccountTransactionPage X exists, and today is on or after Y.start_date, Y.initial_snapshot.full_amount cannot be None.
if no previous AccountTransactionPage X exists, and today is before Y.started_date, Y.initial_snapshot.full_amount = None{whatever}
1.2.3.12.2.a2
if a previous AccountTransactionPage X exists, Y.initial_snapshot.full_amount == X.balance_record[-1].full_amount
{whatever}
1.2.3.12.3.a1: if no previous AccountTransactionPage X exists, and today is in or after Y, Y.initial_snapshot.expected_amount == None
{whatever}
1.2.3.12.3.a3
if no previous AccountTransactionPage X exists, and today occurs before Y, initial_snapshot.expected_amount cannot be None

1.2.3.12.5.a1
if no previous AccountTransactionPage X exists, Y.initial_snapshot.fund_jars should include a 
fund jar for each earmark pattern that starts before Y.start_date. and a fund jar with a finance_id of None{whatever}
1.2.3.12.5.a4: if no previous AccountTransactionPage X exists, and today is in or after Y, the current_amount of all fund jars in Y.initial_snapshot.fund_jars cannot be None
1.2.3.12.5.a5: if no previous AccountTransactionPage X exists, and today occurs before Y, the current_amount of all fund jars in Y.initial_snapshot.fund_jars is None
1.2.3.12.5.a6: if no previous AccountTransactionPage X exists, and today is in or after Y, the expected_amount of all fund jars in Y.initial_snapshot.fund_jars = None
1.2.3.12.5.a7: if no previous AccountTransactionPage X exists, and today occurs before Y, the expected_amount of all fund jars in Y.initial_snapshot.fund_jars cannot be None
```

**Box `1.2.3.12.3.a2`** *(box at x=2872, y=360)*
```
1.2.3.12.3.a2
if a previous AccountTransactionPage X exists, Y.initial_snapshot.expected_amount == X.balance_record[-1].expected_amount
{3p.13.5.3.a1}
```

**Box `1.2.3.12.4.a1`** *(box at x=3128, y=496)*
```
1.2.3.12.4.a1
if no previous AccountTransactionPage X exists, Y.initial_snapshot.expected_free_amount == None
{whatever}
```

**Box `1.2.3.12.4.a2`** *(box at x=3328, y=424)*
```
1.2.3.12.4.a2
if a previous AccountTransactionPage X exists, Y.initial_snapshot.expected_free_amount == X.balance_record[-1].expected_free_amount
{3p.13.4.a1}
```

**Box `1.2.3.12.5.a2`** *(box at x=3008, y=248)*
```
1.2.3.12.5.a2
if a previous AccountTransactionPage X exists, Y.initial_snapshot.fund_jars == X.balance_record[-1].fund_jars.?deepcopy? but no date.
{whatever}
```

**Box `1.2.3.13.a1`** *(box at x=1024, y=384)*
```
1.2.3.13.a1
all can be paired
{3.13b.a5, 3.13c.a5, 3.13f.a5}
fg=black
```

**Box `1.2.3.13.a2`** *(box at x=1152, y=416)*
```
1.2.3.13.a2
all expected-actual transaction pairs have been created and are properly matched
{1.2.3.a1, 1.2.3.13.a1, 1.2.3.13.7.a1, 
1.2.3.13.7.a2, 1.2.3.13.6.a1, 1.2.3.13.6.a2, 
6.4.a1, 7.4.a1, 7.4.a2, 7.5.a1, 7.5.a2}
bg=pink
```

**Box `1.2.3c.12.a1`** *(box at x=3232, y=176)*
```
1.2.3c.12.a1
the initial snapshot for this page c is good except for expected amounts
{1.2.3c.12.2.a1, 1.2.3c.12.2.a2, 1.2.3c.12.5.a1, 
1.2.3c.12.5.a2, 9.1.a1,
3p.13.2.a4, 9.5.a1. 9.5.1.a1, 
9.6.2.a1, 9.7.6.a1, 9.8.2.a1, 9.8.6.a1,
3.12.a1, 1.2.3c.10.a4, 3.12.5.a1, 
1.2.3c.11.a4, 3.12.5.a2, 
3.12.1.a1, 3.12.6.a1, 3.12.7.a1, 3.12.8.a1}
bg=light_gray
{1.2.3.12.2.a1, 9.1.a1, 
9.5.1.a1, 9.6.2.a1, 9.7.6.a1, 
9.8.2.a1, 9.8.6.a1, 3.12.a1, 
1.2.3.10.a4, 3.12.5.a1, 3.12.5.a2, 
3.12.1.a1, 3.12.6.a1}
```

**Box `1.2.3c.12.a2`** *(box at x=3504, y=400)*
```
1.2.3c.12.a2
the initial snapshot for this page c is maintained
{1.2.3c.12.a1, 
(1.2.3c.12.3.a1), 
(1.2.3c.12.3.a2),
1.2.3c.12.4.a1, 
1.2.3c.12.4.a2, 
(9.5.a1), 
(9.5.1.a1), 1.2.3c.12.5.a2, 3p.a1}
{1.2.3.12.a1, (9.5.a1), 1.2.3.12.4.a1, 
1.2.3c.12.4.a2, 3p.a1}
bg=light_gray
```

**Box `1.2.3c.12.a3`** *(box at x=3704, y=312)*
```
1.2.3c.12.a3
the initial snapshot for this page has updated conciously-applied calculations (fundjar.milestone amount, page.current_free_amount, page.current_unpaid, current_safety_cushion, )
{3p.a2, 1.2.3.12.5.a1, 1.2.3.12.5.a2}
```

**Box `1.2.3c.13.a3`** *(box at x=1600, y=488)*
```
1.2.3c.13.a3
this current accounttransaction page c is ready to remove balance snapshops with no events in them.
{3c.13.a2}
```

### Ch.19 Expected/Actual pairing completeness

**Box `1.2.3.13.6.a1`** *(box at x=1024, y=416)*
```
1.2.3.13.6.a1: 
if self.paired_finance_id == None and self.paired_expected_date == None 
	and we are not expired, there should NOT exist an ExpectedTransaction 
	E in an active page such that:
		E.paired_amount == self.amount
		E.paired_actual_date == self.occurred_date
		E.expected_date == self.paired_expected_date
		E.finance_id == self.paired_finance_id
{3.13.6.a2, 3.13.7.a4}
1.2.3.13.6.a2: 
paired:if self.paired_finance_id != None 
	and self.paired_expected_date != None and we are not expired, 
	there should exist an Expectedtransaction E in an active page such that:
		E.cancelled == false
		E.paired_amount == self.amount
		E.paired_actual_date == self.occurred_date
		E.expected_date == self.paired_expected_date
		E.finance_id == self.paired_finance_id
{3.13.6.a2, 3.13.7.a4}
1.2.3.13.7.a1
if self.paired_amount == None and self.paired_actual_date == None 
	and we are not expired, there should NOT exist an ActualTransaction 
	A in an active page such that:
		self.paired_amount == A.amount
		self.paired_actual_date == A.occurred_date
		self.expected_date == A.paired_expected_date
		self.finance_id == A.paired_finance_id
{3.13.6.a2, 3.13.7.a4}
1.2.3.13.7.a2
if self.paired_amount != None and self.paired_actual_date != None 
	and we are not expired, there should exist an ActualTransaction A 
	in an active page such that:
		self.paired_amount == A.amount
		self.paired_actual_date == A.occurred_date
		self.expected_date == A.paired_expected_date
		self.finance_id == A.paired_finance_id
{3.13.6.a2, 3.13.7.a4}
```

### Dotted region outlines (tests chart)

The tests chart carries the same 14 region labels as the main chart (its outlines enclose the test-bundle boxes rather than plain assumptions). See the main chart's [Process regions](06-assumption-dependency-graph.md#process-regions--the-cascade-steps) and [test-bundling plan](06-assumption-dependency-graph.md#the-authors-test-bundling-plan) in 06.

---

## Charts NOT transcribed here, and why

| File | Status |
|---|---|
| `assumptionChart.uxf` (146 boxes, the original pre-reduction chart) | Superseded by Part 1, which is a near-total superset — the only assumption present there and missing from the final chart is `3.4.a1` ("expired cannot be None"), whose text survives in `a03.txt` / [03 Ch.9](03-assumptions-glossary.md#chapter-9-page-identity-bounds--top-level-maintenance-summary). (`expired` just shields old pages from the cascade, so this rule was not carried forward.) Full transcription can be generated with the same tool if ever wanted. |
| `assumptionChartSimpleLines1-8.uxf`, `assumptionChartNoLines*.uxf` | Confirmed near-duplicate working drafts of Part 1 (see [00-sources-and-notes.md](00-sources-and-notes.md)). |
| `assumptionChartTiers1.uxf` | Zero relations — pure box layout for the `allReduced*` stratification, no unique text. |
| `assumptionChartSimpleLinesChecks.uxf` (2022-05-16, newest file in the folder) | **Empty** — zero elements; opened and never filled in. |
