# Assumption Dependency Graph

The directed dependency graph of the original design's assumptions, built from the requirement sets (`{...}`) each assumption carries in `assumptionChartSimpleLines.uxf` — the chart the author maintained as the authoritative record. Each assumption lists the other assumptions that must already hold before it can. Verbatim assumption text and the raw `{...}` blocks are in [05-assumption-chart-full-text.md](05-assumption-chart-full-text.md); this document is the *structure* — what depends on what, and in what order things become true.

**Shape of the graph:** 154 assumptions parsed from the 118 boxes (boxes bundle several assumptions). Every assumption named as a prerequisite is itself defined — the graph is self-contained. It is a directed acyclic graph: every assumption places into a topological layer, once the deliberate **cross-instance couplings** (a page or snapshot depending on its *previous*/*next*/*before*/*following* instance) are set aside. Those couplings aren't cycles — they are the cascade that recomputes each instance from the one before it.

## How to read this
- **Requires** = this assumption's own `{...}` set (what must already hold). **Required by** = the reverse (what breaks if this changes).
- `·L`n = the assumption's topological layer (L0 = axioms with no prerequisites; higher = later). This is the order to make things true — and to write/verify them — in.
- **⇄ cross-instance** lists couplings to another page/snapshot instance, via the `b`/`c`/`f`/`p`/`n` scope grammar: `p`=previous, `n`=next, `b`=all-before, `f`=all-following, `c`=current. These are the cascade edges — they make the model a forward recomputation rather than a static check.
- Scope suffixes are normalized to the base id for graph structure (so `1.2.3c.11.a4`, `1.2.3b.11.a4` are one node); the raw scoped form is preserved in the cross-instance list.

- Counts: **154** assumptions · **49** root axioms · **19** `{whatever}`/undetermined · **13** top-level sinks · **32** cross-instance couplings · **21** layers.

---

## The graph, by chapter

### Ch.1 TransactionLogPage

- **`2.1.a1`·L0** — start_date cannot be None. Must be before end_date.
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.a1`·L1, `1.2.3.a1`·L1
- **`2.2.a1`·L0** — end_date cannot be None. Must be after start_date
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.a1`·L1, `1.2.3.a1`·L1
- **`2.3.a1`·L0** — account_pages cannot be None. Can be empty dictionary
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.a1`·L1

### Ch.2 FinancialPattern

- **`4.1.a1`·L0** — finance_id cannot be None
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.10.a1`·L2, `1.2.3.10.a2`·L2, `1.2.3.10.a3`·L2, `3.10.a3`·L7
- **`4.2.a1`·L0** — source cannot be None
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.10.a1`·L2, `1.2.3.10.a2`·L2, `1.2.3.10.a3`·L2

### Ch.3 EarMarkPattern

- **`5.1.a1`·L0** — self.finance_id cannot be None
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.11.a1`·L4, `1.2.3.11.a3`·L4, `3.11.1.a1`·L1
- **`5.2.a1`·L0** — self.date_pattern cannot be None
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.11.a1`·L4, `1.2.3.11.a3`·L4, `3.11.2.a1`·L1
- **`5.3.a1`·L0** — self.amount cannot be None.
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.11.a1`·L4, `1.2.3.11.a3`·L4

### Ch.4 ActualTransaction

- **`6.1.a1`·L0** — self.source cannot be None.
  - requires: *{none}* (no prerequisites)
  - required by: `3.13.6.a2`·L3
- **`6.4.a1`·L0** — If self.paired_expected_date == None then self.paired_finance_id == None.
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.13.a2`·L9
- **`6.5.a1`·L0** — self.made_in_bulk cannot be None
  - requires: *{none}* (no prerequisites)
  - required by: `3.13.6.a2`·L3
- **`6.6.a1`·L0** — self.amount cannot be None.
  - requires: *{none}* (no prerequisites)
  - required by: `3.13.6.a2`·L3

### Ch.5 ExpectedTransaction

- **`7.1.a1`·L0** — self.finance_id cannot be None.
  - requires: *{none}* (no prerequisites)
  - required by: `3.10.a3`·L7, `3.13.7.a4`·L6
- **`7.3.a1`·L0** — cancelled cannot be None
  - requires: *{none}* (no prerequisites)
  - required by: `3.10.a3`·L7
- **`7.4.a1`·L0** — If self.paired_actual_date == None then self.paired_amount == None. If self.paired_actual_date != None then self.paired_amount != None
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.13.a2`·L9, `3.10.a3`·L7
- **`7.4.a2`·L0** — self.paired_amount == None if self.cancelled == true
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.13.a2`·L9, `3.10.a3`·L7
- **`7.5.a1`·L0** — if self.paired_amount == None then self.paired_actual_date == None. if self.paired_amount != None then self.paired_actual_date != None.
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.13.a2`·L9, `3.10.a3`·L7
- **`7.5.a2`·L0** — self.paired_actual_date == None if self.cancelled == true
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.13.a2`·L9, `3.10.a3`·L7
- **`7.6.a1`·L0** — self.expected_amount cannot be None.
  - requires: *{none}* (no prerequisites)
  - required by: `3.13.a9`·L17
- **`7.7.a1`·L0** — self.amount_tolerance cannot be None, and cannot have a negative value for either number.
  - requires: *{none}* (no prerequisites)
  - required by: `3.10.a3`·L7, `3.13.7.a4`·L6
- **`7.8.a1`·L0** — self.date_tolerance cannot be None. and cannot have a negative value for either number.
  - requires: *{none}* (no prerequisites)
  - required by: `3.10.a3`·L7, `3.13.7.a4`·L6

### Ch.6 EarMarkEvent

- **`8.1.a1`·L0** — finance_id cannot be None if self.repeated_earmark == True
  - requires: *{none}* (no prerequisites)
  - required by: `3.10.a3`·L7, `3.13.8.a7`·L8
- **`8.2.a1`·L0** — earmark_date cannot be None.
  - requires: *{none}* (no prerequisites)
  - required by: `3.10.a3`·L7, `3.13.8.a7`·L8
- **`8.3.a1`·L0** — repeated_earmark cannot be None. default is false.
  - requires: *{none}* (no prerequisites)
  - required by: `3.10.a3`·L7, `3.13.8.a7`·L8
- **`8.4.a1`·L1** — expected_amount cannot be None.
  - requires: `8.4.a2`·L0
  - required by: `3.10.a3`·L7, `3.13.8.a7`·L8
- **`8.4.a2`·L0** — self.expected_amount must be = an explicitly given amount from user if self.repeated_earmark is false on a normal day
  - requires: *{none}* (no prerequisites)
  - required by: `3.13.8.a7`·L8, `8.4.a1`·L1
- **`8.5.a1`·L0** — self.explicit_amount must be None if self.repeated_earmark == true
  - requires: *{none}* (no prerequisites)
  - required by: `3.10.a3`·L7, `3.13.8.a7`·L8

### Ch.7 BalanceSnapshot

- **`9.1.a1`·L0** — snapshot_date can be None. If it is None, actual_transactions, expected_transactions, and earmark_events are empty lists.
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.12.a1`·L8
- **`9.5.a1`·L1** — self.fund_jars must have a single fund jar with a finance_id of None
  - requires: `10.3.a1`·L0, `10.4.a1`·L0
  - required by: `1.2.3.12.a1`·L8, `1.2.3.12.a2`·L9, `3.13.a7`·L10
- **`9.5.1.a1`·L0** — all funds jars in self.fund_jars must have a unique finance_id.
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.12.a1`·L8, `1.2.3.12.a2`·L9, `3.13.a7`·L10
- **`9.6.2.a1`·L0** — self.actual_transactions[all].occurred_date == self.snapshot_date
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.12.a1`·L8, `3.13.6.a2`·L3
- **`9.7.6.a1`·L0** — self.expected_transactions[all].expected_amount == self.snapshot_date
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.12.a1`·L8, `3.13.7.a4`·L6
- **`9.8.2.a1`·L0** — self.earmark_events[all].earmark_date == self.snapshot_date
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.12.a1`·L8, `3.13.8.a7`·L8
- **`9.8.6.a1`·L0** — self.earmark_events[any].actual_amount must be = None if this is an isolated earmark and there is also a repeated earmark on this day.
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.12.a1`·L8, `3.13.8.a7`·L8

### Ch.8 FundJar

- **`10.3.a1`·L0** — self.expected_amount is = None if self.finance_id is None.
  - requires: *{none}* (no prerequisites)
  - required by: `3.10.a3`·L7, `3.13.5.a5`·L16, `9.5.a1`·L1
- **`10.4.a1`·L0** — self.milestone_amount should be = None if self.finance_id is None
  - requires: *{none}* (no prerequisites)
  - required by: `9.5.a1`·L1
- **`10.4.a2`·L0** — if the finance id for this fund jar is for a repeated negative expected transaction, self.milestone_amount should be -1*amount from the finance_pattern
  - requires: *{whatever}* (no prerequisites)
  - required by: *(nothing — a top-level goal)*
- **`10.4.a3`·L0** — if the finance id for this fund jar is for a repeated positive expected transaction, self.milestone_amount should be none
  - requires: *{whatever}* (no prerequisites)
  - required by: *(nothing — a top-level goal)*

### Ch.9 Page identity, bounds & top-level summary

- **`3.a1`·L18** — this page is maintained
  - requires: `1.2.3.12.a2`·L9, `3.13.2.a4`·L15, `3.13.3.a1`·L12, `3.13.5.a5`·L16, `3.13.6.a3`·L10, `3.13.7.a5`·L10, `3.13.8.a8`·L13, `3.13.a9`·L17
  - required by: `3.13.5.4.a1`·L19
- **`3.1.a1`·L0** — account cannot be None
  - requires: *{none}* (no prerequisites)
  - required by: *(nothing — a top-level goal)*
- **`3.a2`·L20** — this page has updated conciously-applied calculations (fundjar.milestone_amount, page.current_free_amount, page.current_unpaid_expected, current_safety_cushion, )
  - requires: `1.2.3.5.a1`·L11, `1.2.3.6.a1`·L10, `3.5.a1`·L1, `3.7.a1`·L10, `3.7.a2`·L10, `3.13.5.4.a1`·L19
  - required by: *(nothing — a top-level goal)*
- **`3.2.a1`·L0** — start_date cannot be None
  - requires: *{none}* (no prerequisites)
  - required by: `3.5.a1`·L1, `3.10.a1`·L1, `3.13.1.a1`·L1
- **`3.2.a2`·L0** — start_date must be a date before end_date
  - requires: *{none}* (no prerequisites)
  - required by: `3.5.a1`·L1, `3.10.a1`·L1, `3.13.1.a1`·L1
- **`3.3.a1`·L0** — end_date cannot be None
  - requires: *{none}* (no prerequisites)
  - required by: `3.5.a1`·L1, `3.10.a1`·L1, `3.13.1.a1`·L1
- **`3.3.a2`·L0** — end_date must be a date after start_date
  - requires: *{none}* (no prerequisites)
  - required by: `3.5.a1`·L1, `3.10.a1`·L1, `3.13.1.a1`·L1
- **`3.5.a1`·L1** — current_free_amount = None if today is not between self.start_date and self.end_date
  - requires: `3.2.a1`·L0, `3.2.a2`·L0, `3.3.a1`·L0, `3.3.a2`·L0
  - required by: `3.a2`·L20
- **`3.7.a1`·L10** — current_safety_cushion = None if today is not between self.start_date and self.end_date
  - requires: `1.2.3.12.a2`·L9
  - required by: `3.a2`·L20
- **`3.7.a2`·L10** — current_safety_cushion = money in today's fund jar with finance_id == None
  - requires: `1.2.3.12.a2`·L9
  - required by: `3.a2`·L20
- **`3.8.a1`·L0** — ideal_safety_cushion cannot be None
  - requires: *{none}* (no prerequisites)
  - required by: `3.13.a6`·L8
- **`3.9.a1`·L0** — safety_priority cannot be None
  - requires: *{none}* (no prerequisites)
  - required by: `3.13.a6`·L8
- **`3.10.a1`·L1** — self.finance_patterns should only contain patterns that end after self.start_date and begin at or before self.end_date
  - requires: `3.2.a1`·L0, `3.2.a2`·L0, `3.3.a1`·L0, `3.3.a2`·L0
  - required by: `1.2.3.10.a4`·L3
- **`3.10.a2`·L0** — self.finance_patterns cannot contain any duplicate ids
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.10.a4`·L3
- **`3.10.a3`·L7** — if an EarMarkPattern, EarMarkEvent, FundJar, or ExpectedTransaction exists in our page with a finance_id of x and our page has no finance pattern with a finance_id of x, that EarMarkPattern, EarMarkEvent, FundJar, or ExpectedTransaction shouldn't exist
  - requires: `1.2.3.10.a4`·L3, `1.2.3.11.a4`·L5, `3.13.5.a2`·L6, `3.13.7.a2`·L4, `3.13.7.a4`·L6, `3.13.8.1.a3`·L6, `4.1.a1`·L0, `7.1.a1`·L0, `7.3.a1`·L0, `7.4.a1`·L0, `7.4.a2`·L0, `7.5.a1`·L0, `7.5.a2`·L0, `7.7.a1`·L0, `7.8.a1`·L0, `8.1.a1`·L0, `8.2.a1`·L0, `8.3.a1`·L0, `8.4.a1`·L1, `8.5.a1`·L0, `10.3.a1`·L0
  - required by: `3.13.a2`·L9
- **`3.11.1.a1`·L1** — all earmark patterns self.earmark_patterns should have unique finance_ids.
  - requires: `5.1.a1`·L0
  - required by: `1.2.3.11.a4`·L5
- **`3.11.2.a1`·L1** — self.earmark_patterns should only contain patterns that end after self.start_date and begin at or before self.end_date
  - requires: `5.2.a1`·L0
  - required by: `1.2.3.11.a3`·L4
- **`3.11.2.a2`·L0** — an earmark pattern's date_pattern cannot extend beyond or before the date_pattern of it's associated finance_pattern.
  - requires: *{whatever}* (no prerequisites)
  - required by: `1.2.3.11.a3`·L4

### Ch.10 Initial Snapshot

- **`3.12.a1`·L0** — initial_snapshot cannot be None
  - requires: *{whatever}* (no prerequisites)
  - required by: `1.2.3.12.a1`·L8
- **`3.12.1.a1`·L0** — self.initial_snapshot.snapshot_date = None
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.12.a1`·L8
- **`3.12.5.a1`·L7** — initial_snapshot.fund_jars cannot have a fund jar that is not in our ?initial finance_patterns? (except the fund jar with None as a finance_id)
  - requires: `3.13.5.a1`·L6, `3.13.5.a2`·L6
  - required by: `1.2.3.12.a1`·L8
- **`3.12.5.a2`·L7** — no fund jar can exist here if it has a finance pattern that starts after (or on?) ?self.start_date?
  - requires: `3.13.5.a2`·L6
  - required by: `1.2.3.12.a1`·L8
- **`3.12.6.a1`·L0** — self.initial_snapshot.actual_transactions == []
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.12.a1`·L8
- **`3.12.7.a1`·L0** — self.initial_snapshot.expected_transactions == []
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.12.a1`·L8
- **`3.12.8.a1`·L0** — self.initial_snapshot.earmark_events == []
  - requires: *{none}* (no prerequisites)
  - required by: `1.2.3.12.a1`·L8

### Ch.11 Balance Record

- **`3.13.1.a1`·L1** — all BalanceSnapshots inside self.balance_record should have a snapshot_date that is => self.start_date  and is =< self.end_date
  - requires: `3.2.a1`·L0, `3.2.a2`·L0, `3.3.a1`·L0, `3.3.a2`·L0
  - required by: `3.13.5.a3`·L7, `3.13.6.a1`·L2, `3.13.7.a3`·L5, `3.13.8.a6`·L7
- **`3.13.a2`·L9** — all obsolete events of all kinds have been removed, obsolete fund jars removed, and all events exist that should (not including fund jars) (not including implicit earmarks which may be added later)
  - requires: `3.10.a3`·L7, `3.13.5.a3`·L7, `3.13.6.a2`·L3, `3.13.7.a4`·L6, `3.13.8.a7`·L8
  - required by: `1.2.3.13.a3`·L10, `3.13.a3`·L11
- **`3.13.a3`·L11** — each balance snapshot inside balance_record has at least one event (earmark, actual event, or expected event). All other balance snapshots have been removed.
  - requires: `1.2.3.13.a3`·L10, `3.13.a2`·L9
  - required by: `3.13.3.a1`·L12, `3.13.a4`·L12
- **`3.13.a4`·L12** — full_amount can be safely calculated for balance snapshops
  - requires: `3.13.a3`·L11
  - required by: `3.13.2.a2`·L13, `3.13.2.a3`·L13
- **`3.13.a5`·L7** — we have enough information to pair actual and expected events
  - requires: `3.13.6.a1`·L2, `3.13.6.a2`·L3, `3.13.7.a3`·L5, `3.13.7.a4`·L6
  - ⇄ cross-instance: `3.13b.6.a1`, `3.13b.6.a2`, `3.13b.7.a3`, `3.13b.7.a4`, `3.13f.6.a1`, `3.13f.6.a2`, `3.13f.7.a3`, `3.13f.7.a4`
  - required by: `1.2.3.13.a1`·L8
- **`3.13.a6`·L8** — we know whether or not this is a deallocation day. And implicit earmarks are cleared.
  - requires: `3.8.a1`·L0, `3.9.a1`·L0, `3.13.5.a4`·L7, `3.13.6.a2`·L3, `3.13.8.a5`·L2
  - ⇄ cross-instance: `3.13p.2.a1`, `3.13p.5.2.a5`
  - required by: `3.13.a7`·L10
- **`3.13.a7`·L10** — if this is a deallocation day, we have added the new implicit isolated earmarks to earmark_events
  - requires: `1.2.3.13.a2`·L9, `3.13.a6`·L8, `3.13.8.a7`·L8, `9.5.a1`·L1, `9.5.1.a1`·L0
  - required by: `3.13.a8`·L12, `3.13.8.4.a2`·L11
- **`3.13.a8`·L12** — if this is a deallocation day, the newly added earmarks are now properly merged into existing isolated earmarks if they existed.
  - requires: `3.13.a7`·L10, `3.13.8.1.a1`·L0, `3.13.8.4.a2`·L11, `3.13.8.a5`·L2
  - required by: `3.13.5.2.a4`·L13, `3.13.8.a8`·L13
- **`3.13.a9`·L17** — have expected amounts up to date
  - requires: `3.13.8.6.a1`·L16, `3.13.8.6.a2`·L16, `7.6.a1`·L0
  - required by: `3.a1`·L18
- **`3.13.a10`·L0** — if this is not a deallocation day, and an expected transaction transaction with a fund jar has a paired actual transaction on this day,
  - requires: *{whatever}* (no prerequisites)
  - required by: *(nothing — a top-level goal)*

### Ch.12 full_amount & expected amounts

- **`3.13.2.a1`·L14** — balancesnapshot c has proper full_amount
  - requires: `3.13.2.a2`·L13, `3.13.2.a3`·L13
  - required by: `3.13.2.a4`·L15
- **`3.13.2.a2`·L13** — if balancesnapshot c in self.balance_record occurs after today, c.full_amount = None
  - requires: `1.2.3.12.2.a1`·L0, `1.2.3.12.2.a2`·L0, `3.13.a4`·L12
  - ⇄ cross-instance: `3.13p.2.a2`, `3.13p.2.a3`
  - required by: `3.13.2.a1`·L14, `3.13.5.2.a1`·L14
- **`3.13.2.a3`·L13** — if balancesnapshot c in self.balance_record occurs today or before, c.full_amount = p.full_amount + sum(x.amount for x in c.actual_transactions)
  - requires: `1.2.3.12.2.a1`·L0, `1.2.3.12.2.a2`·L0, `3.13.a4`·L12, `3.13.6.a2`·L3
  - ⇄ cross-instance: `3.13p.2.a1`
  - required by: `3.13.2.a1`·L14
- **`3.13.2.a4`·L15** — balancesnapshots inside self.balance_record have the correct value for full_amount
  - requires: `3.13.2.a1`·L14
  - ⇄ cross-instance: `3.13f.2.a1`
  - required by: `3.a1`·L18
- **`3.13.3.a1`·L12** — self.balance_record[i].expected_amount = (self.balance_record[i-1].full_amount if self.balance_record[i-1].full_amount != None else self.balance_record[i-1].expected_amount) + SUM(self.balance_record[i].expected_transactions[all].expected_amount)
  - requires: `1.2.3.12.a1`·L8, `3.13.a3`·L11, `3.13.7.a4`·L6
  - ⇄ cross-instance: `3.13p.2.a1`, `3.13p.3.a1`
  - required by: `3.a1`·L18, `3.13.4.a1`·L16
- **`3.13.4.a1`·L16** — self.balance_record[i].expected_free_amount = self.balance_record[i].expected_amount - SUM(self.balance_record[i].fund_jars[all].expected_amount)
  - requires: `1.2.3.12.3.a1`·L0, `1.2.3.12.3.a2`·L0, `1.2.3.12.4.a1`·L0, `1.2.3.12.4.a2`·L0, `1.2.3.12.5.a1`·L0, `1.2.3.12.5.a2`·L0, `3.13.3.a1`·L12, `3.13.5.3.a1`·L15
  - required by: *(nothing — a top-level goal)*

### Ch.13 Fund Jars

- **`3.13.5.a1`·L6** — if the day exists within EarMarkPattern A's date_patern(any day between start and end), a fund jar with A's finance_id must exist on that day.
  - requires: `1.2.3.11.a4`·L5
  - required by: `3.12.5.a1`·L7, `3.13.5.a4`·L7
- **`3.13.5.a2`·L6** — a fund jar can only exist on days that fall within its earmark pattern's rrule (unless the fund jar's finance_id is None)
  - requires: `1.2.3.11.a4`·L5
  - required by: `3.10.a3`·L7, `3.12.5.a1`·L7, `3.12.5.a2`·L7, `3.13.5.a3`·L7, `3.13.5.a4`·L7
- **`3.13.5.2.a1`·L14** — self.balance_record[any].fund_jars[all].current_amount must be = None if day has not occurred
  - requires: `3.13.2.a2`·L13
  - required by: *(nothing — a top-level goal)*
- **`3.13.5.2.a2`·L15** — self.balance_record[any].fund_jars[all].current_amount cannot be None if it is the current day or before.
  - requires: `3.13.5.2.a5`·L14
  - required by: *(nothing — a top-level goal)*
- **`3.13.5.2.a3`·L0** — self.balance_record[any].fund_jars[all].current_amount = ?normal_fund_daily_distribution? if the day has occurred and it is a normal day
  - requires: *{whatever}* (no prerequisites)
  - required by: `3.13.5.2.a5`·L14
- **`3.13.5.2.a4`·L13** — self.balance_record[i].fund_jars[j].current_amount = self.balance_record[i].fund_jars[j-1].current_amount + sum( expected_amount of today's earmarks for this fund jar) after ?deallocation_fund_distribution? if the day has occurred and it is a deallocation day.
  - requires: `3.13.a8`·L12
  - required by: `3.13.5.2.a5`·L14
- **`3.13.5.2.a5`·L14** — fund jars of balancesnapshot c have proper current_amount
  - requires: `3.13.5.2.a3`·L0, `3.13.5.2.a4`·L13
  - required by: `3.13.5.2.a2`·L15, `3.13.5.3.a1`·L15, `3.13.5.a5`·L16
- **`3.13.5.a3`·L7** — all obsolete fund jars removed
  - requires: `1.2.3.11.a4`·L5, `3.13.1.a1`·L1, `3.13.5.a2`·L6
  - required by: `3.13.a2`·L9
- **`3.13.5.3.a1`·L15** — self.expected_amount is = the expected_amount of the previous day (or current_amount of previous day if one exists) plus the expected_amount of all earmark_events with the same financial_id on that day
  - requires: `3.13.5.2.a5`·L14
  - required by: `3.13.4.a1`·L16, `3.13.5.a5`·L16, `3.13.8.6.a1`·L16, `3.13.8.6.a2`·L16
- **`3.13.5.a4`·L7** — has all fund jars (the amount might not be accurate)
  - requires: `3.13.5.a1`·L6, `3.13.5.a2`·L6
  - required by: `3.13.a6`·L8
- **`3.13.5.4.a1`·L19** — self.milestone_amount should be = sum of all expected values of repeated earmarks up to and including this date (unless it has a finance_id of None. or if the finance id is for a repeated expected transaction)
  - requires: `1.2.3.12.a2`·L9, `1.2.3.12.a3`·L1, `3.a1`·L18, `3.13.5.a5`·L16, `3.13.8.a8`·L13
  - required by: `3.a2`·L20
- **`3.13.5.a5`·L16** — all fund jars are maintained
  - requires: `3.13.5.2.a5`·L14, `3.13.5.3.a1`·L15, `10.3.a1`·L0
  - ⇄ cross-instance: `3.13b.5.2.a5`, `3.13f.5.2.a5`
  - required by: `3.a1`·L18, `3.13.5.4.a1`·L19

### Ch.14 Actual Transactions

- **`3.13.6.a1`·L2** — all obsolete actual transactions removed
  - requires: `3.13.1.a1`·L1
  - required by: `3.13.a5`·L7, `3.13.6.a2`·L3
- **`3.13.6.a2`·L3** — has all actual
  - requires: `3.13.6.a1`·L2, `6.1.a1`·L0, `6.5.a1`·L0, `6.6.a1`·L0, `9.6.2.a1`·L0
  - required by: `1.2.3.13.6.a1`·L7, `1.2.3.13.6.a2`·L7, `1.2.3.13.7.a1`·L7, `1.2.3.13.7.a2`·L7, `3.13.a2`·L9, `3.13.2.a3`·L13, `3.13.a5`·L7, `3.13.a6`·L8, `3.13.6.a3`·L10
- **`3.13.6.a3`·L10** — actual transactions are maintained
  - requires: `1.2.3.13.a2`·L9, `3.13.6.a2`·L3
  - required by: `3.a1`·L18
- **`3.13.6.3.a1`·L0** — no two actual transactions in a snapshot can have the same paired_finance_id and paired_expected_date
  - requires: *(no set)* (no prerequisites)
  - required by: *(nothing — a top-level goal)*

### Ch.15 Expected Transactions

- **`3.13.7.a1`·L4** — if a date from finance_pattern's rrule exists on this page, an expected transaction with this finance_id must exist in the balance record of that day.
  - requires: `1.2.3.10.a4`·L3
  - required by: `3.13.7.a4`·L6
- **`3.13.7.1.a1`·L0** — no two expected transactions in a snapshot can have same finance_id
  - requires: *(no set)* (no prerequisites)
  - required by: `3.13.7.a3`·L5
- **`3.13.7.a2`·L4** — an expected transaction cannot exist on a day not specified by it's financial pattern's date_pattern.
  - requires: `1.2.3.10.a4`·L3
  - required by: `3.10.a3`·L7, `3.13.7.a3`·L5
- **`3.13.7.a3`·L5** — all obsolete expected transactions removed or get amounts updated
  - requires: `1.2.3.10.a4`·L3, `3.13.1.a1`·L1, `3.13.7.1.a1`·L0, `3.13.7.a2`·L4, `3.13.7.6.a1`·L4
  - required by: `3.13.a5`·L7, `3.13.7.a4`·L6
- **`3.13.7.a4`·L6** — has all expected transactions
  - requires: `1.2.3.10.a4`·L3, `3.13.7.a1`·L4, `3.13.7.a3`·L5, `7.1.a1`·L0, `7.7.a1`·L0, `7.8.a1`·L0, `9.7.6.a1`·L0
  - required by: `1.2.3.13.6.a1`·L7, `1.2.3.13.6.a2`·L7, `1.2.3.13.7.a1`·L7, `1.2.3.13.7.a2`·L7, `3.10.a3`·L7, `3.13.a2`·L9, `3.13.3.a1`·L12, `3.13.a5`·L7, `3.13.7.a5`·L10
- **`3.13.7.a5`·L10** — expected transactions are maintained
  - requires: `1.2.3.13.a2`·L9, `3.13.7.a4`·L6
  - required by: `3.a1`·L18
- **`3.13.7.6.a1`·L4** — an expected transactions expected_amount = the amount from its financialpattern
  - requires: `1.2.3.10.a4`·L3
  - required by: `3.13.7.a3`·L5

### Ch.16 Earmarks

- **`3.13.8.a1`·L6** — an earmark, even an isolated earmark, cannot exist on a page that does not have an earmark pattern for it, (unless the earmark's finance_id is None)
  - requires: `1.2.3.11.a4`·L5
  - required by: `3.13.8.a6`·L7
- **`3.13.8.1.a1`·L0** — only one isolated earmark with finance_id x can exist on a single day.
  - requires: *{none}* (no prerequisites)
  - required by: `3.13.a8`·L12, `3.13.8.a7`·L8
- **`3.13.8.1.a2`·L0** — only one repeated earmark with finance_id x can exist on a single day.
  - requires: *{none}* (no prerequisites)
  - required by: `3.13.8.a7`·L8
- **`3.13.8.1.a3`·L6** — self.finance_id must be = the finance id of the expected transaction this earmark is saving for.
  - requires: `1.2.3.11.a4`·L5
  - required by: `3.10.a3`·L7, `3.13.8.a7`·L8
- **`3.13.8.a2`·L6** — an earmark cannot exist outside the date_pattern of its earmark pattern (unless the earmark's finance_id is None)
  - requires: `1.2.3.11.a4`·L5
  - required by: `3.13.8.a6`·L7
- **`3.13.8.a3`·L6** — if day of the rrule pattern is on this page, a repeated earmark with this finance_id must exist in the balance record of that day.
  - requires: `1.2.3.11.a4`·L5
  - required by: `3.13.8.a7`·L8
- **`3.13.8.3.a1`·L6** — self.repeated_earmark must be = true if it's repeated (generated from a pattern), false if not.
  - requires: `1.2.3.11.a4`·L5
  - required by: `3.13.8.a7`·L8
- **`3.13.8.a4`·L1** — all isolated earmarks on balancesnapshot c have expected_amount = explicit_amount.
  - requires: `3.13.8.5.a1`·L0
  - required by: `3.13.8.a5`·L2
- **`3.13.8.4.a1`·L6** — self.expected_amount must be = earmark pattern's amount if self.repeated_earmark is true
  - requires: `1.2.3.11.a4`·L5
  - required by: `3.13.8.a6`·L7, `3.13.8.a7`·L8
- **`3.13.8.4.a2`·L11** — self.expected_amount must be = an explicitly given amount from user plus ?deallocation_implicit_amount?, if self.repeated_earmark is false on a deallocation day
  - requires: `3.13.a7`·L10
  - required by: `3.13.a8`·L12
- **`3.13.8.a5`·L2** — implicit earmarks on balancesnapshot c have been cleared. Any isolated earmark with an expected_amount of 0 has been deleted.
  - requires: `3.13.8.a4`·L1
  - required by: `3.13.a6`·L8, `3.13.a8`·L12, `3.13.8.a7`·L8
- **`3.13.8.5.a1`·L0** — self.explicit_amount must be = an explicitly given amount from user
  - requires: *{none}* (no prerequisites)
  - required by: `3.13.8.a4`·L1
- **`3.13.8.a6`·L7** — all obsolete earmarks removed. (implicit earmarks are not cleared)
  - requires: `1.2.3.11.a4`·L5, `3.13.1.a1`·L1, `3.13.8.a1`·L6, `3.13.8.a2`·L6, `3.13.8.4.a1`·L6
  - required by: `3.13.8.a7`·L8
- **`3.13.8.6.a1`·L16** — self.actual_amount must be = the difference between this fund jar's actual amount and the fund jar's actual amount on the previous date, if this is a repeated earmark
  - requires: `3.13.5.3.a1`·L15
  - required by: `3.13.a9`·L17
- **`3.13.8.6.a2`·L16** — self.actual_amount must be = the difference between this fund jar's actual amount and the fund jar's actual amount on the previous date, if this is an isolated earmark and no repeated earmark is on this day
  - requires: `3.13.5.3.a1`·L15
  - required by: `3.13.a9`·L17
- **`3.13.8.a7`·L8** — has all earmarks (not including implicit earmarks which may be added later)
  - requires: `1.2.3.11.a4`·L5, `3.13.8.1.a1`·L0, `3.13.8.1.a2`·L0, `3.13.8.1.a3`·L6, `3.13.8.a3`·L6, `3.13.8.3.a1`·L6, `3.13.8.4.a1`·L6, `3.13.8.a5`·L2, `3.13.8.a6`·L7, `8.1.a1`·L0, `8.2.a1`·L0, `8.3.a1`·L0, `8.4.a1`·L1, `8.4.a2`·L0, `8.5.a1`·L0, `9.8.2.a1`·L0, `9.8.6.a1`·L0
  - required by: `3.13.a2`·L9, `3.13.a7`·L10
- **`3.13.8.a8`·L13** — has all earmarks including implicit earmarks. All earmarks are maintained.
  - requires: `3.13.a8`·L12
  - ⇄ cross-instance: `3.13b.a8`, `3.13f.a8`
  - required by: `3.a1`·L18, `3.13.5.4.a1`·L19

### Ch.17 log_pages & pattern continuity across pages

- **`1.2.a1`·L1** — log_pages cannot by null. It can be an empty list.
  - requires: `2.1.a1`·L0, `2.2.a1`·L0, `2.3.a1`·L0
  - required by: *(nothing — a top-level goal)*
- **`1.2.3.a1`·L1** — If page X and page Y must have the same number of account pages and the same sets of accountnames. all pages must have the same accounts.
  - requires: `2.1.a1`·L0, `2.2.a1`·L0
  - required by: `1.2.3.10.a1`·L2, `1.2.3.10.a2`·L2, `1.2.3.10.a3`·L2, `1.2.3.13.a2`·L9
- **`1.2.3.10.a1`·L2** — if a finance pattern in non-expired page X has an rrule that extends the pattern into page Y, then page Y should have the same finance pattern.
  - requires: `1.2.3.a1`·L1, `4.1.a1`·L0, `4.2.a1`·L0
  - ⇄ cross-instance: `1.2.3b.10.a4`
  - required by: `1.2.3.10.a4`·L3
- **`1.2.3.10.a2`·L2** — if a finance pattern in non-expired page X has an rrule that ends before the first day of page Y, page Y should not have that finance pattern.
  - requires: `1.2.3.a1`·L1, `4.1.a1`·L0, `4.2.a1`·L0
  - ⇄ cross-instance: `1.2.3b.10.a4`
  - required by: `1.2.3.10.a4`·L3
- **`1.2.3.10.a3`·L2** — if a finance pattern in non-expired page X has the same finance id as a finance pattern in page Y, both finance patterns must have the exact same rrule.
  - requires: `1.2.3.a1`·L1, `4.1.a1`·L0, `4.2.a1`·L0
  - ⇄ cross-instance: `1.2.3b.10.a4`
  - required by: `1.2.3.10.a4`·L3
- **`1.2.3.10.a4`·L3** — all finance patterns for this page c are perfect (but not the events in the page)
  - requires: `1.2.3.10.a1`·L2, `1.2.3.10.a2`·L2, `1.2.3.10.a3`·L2, `3.10.a1`·L1, `3.10.a2`·L0
  - required by: `1.2.3.11.a1`·L4, `1.2.3.11.a2`·L4, `1.2.3.11.a3`·L4, `1.2.3.12.a1`·L8, `3.10.a3`·L7, `3.13.7.a1`·L4, `3.13.7.a2`·L4, `3.13.7.a3`·L5, `3.13.7.a4`·L6, `3.13.7.6.a1`·L4
- **`1.2.3.11.a1`·L4** — if an earmark pattern in non-expired page X has an rrule that extends the pattern into page Y, then this page Y should have the same earmark pattern.
  - requires: `1.2.3.10.a4`·L3, `5.1.a1`·L0, `5.2.a1`·L0, `5.3.a1`·L0
  - required by: `1.2.3.11.a4`·L5
- **`1.2.3.11.a2`·L4** — if a earmark pattern in non-expired page X has an rrule that ends before the first day of page Y, this page Y should not have that earmark pattern.
  - requires: `1.2.3.10.a4`·L3
  - required by: `1.2.3.11.a4`·L5
- **`1.2.3.11.a3`·L4** — if a earmark pattern in non-expired page X has the same finance id as a earmark pattern in this page Y, both earmark patterns must have the exact same rrule.
  - requires: `1.2.3.10.a4`·L3, `3.11.2.a1`·L1, `3.11.2.a2`·L0, `5.1.a1`·L0, `5.2.a1`·L0, `5.3.a1`·L0
  - required by: `1.2.3.11.a4`·L5
- **`1.2.3.11.a4`·L5** — all earmark patterns for this page c are perfect. (but not the events in the page)
  - requires: `1.2.3.11.a1`·L4, `1.2.3.11.a2`·L4, `1.2.3.11.a3`·L4, `3.11.1.a1`·L1
  - ⇄ cross-instance: `1.2.3b.11.a4`
  - required by: `1.2.3.12.a1`·L8, `3.10.a3`·L7, `3.13.5.a1`·L6, `3.13.5.a2`·L6, `3.13.5.a3`·L7, `3.13.8.a1`·L6, `3.13.8.1.a3`·L6, `3.13.8.a2`·L6, `3.13.8.a3`·L6, `3.13.8.3.a1`·L6, `3.13.8.4.a1`·L6, `3.13.8.a6`·L7, `3.13.8.a7`·L8

### Ch.18 Derived calcs & initial-snapshot inheritance

- **`1.2.3.5.a1`·L11** — if today is in Y, Y.current_free_amount == Y.balance_record[?today?].full_amount - (Y.current_unpaid_expected) + ?money_in_fund_jars_ready_for_unpaid_bills(unpaid_bill_finance_ids, unpaid_bill_amounts)? - SUM(Y.balance_record[?today?].fund_jars[all].current_amount)
  - requires: `1.2.3.6.a1`·L10
  - required by: `3.a2`·L20
- **`1.2.3.6.a1`·L10** — current_unpaid_expected = total expected_amounts of unfufilled uncancelled expected transactions that occured today or earlier (that are in active pages)
  - requires: `1.2.3.12.a2`·L9, `1.2.3.13.a2`·L9
  - ⇄ cross-instance: `1.2.3p.6.a1`
  - required by: `1.2.3.5.a1`·L11, `3.a2`·L20
- **`1.2.3.12.a1`·L8** — the initial snapshot for this page c is good except for expected amounts
  - requires: `1.2.3.10.a4`·L3, `1.2.3.11.a4`·L5, `1.2.3.12.2.a1`·L0, `1.2.3.12.2.a2`·L0, `1.2.3.12.5.a1`·L0, `1.2.3.12.5.a2`·L0, `3.12.a1`·L0, `3.12.1.a1`·L0, `3.12.5.a1`·L7, `3.12.5.a2`·L7, `3.12.6.a1`·L0, `3.12.7.a1`·L0, `3.12.8.a1`·L0, `9.1.a1`·L0, `9.5.a1`·L1, `9.5.1.a1`·L0, `9.6.2.a1`·L0, `9.7.6.a1`·L0, `9.8.2.a1`·L0, `9.8.6.a1`·L0
  - ⇄ cross-instance: `3p.13.2.a4`
  - required by: `1.2.3.12.a2`·L9, `3.13.3.a1`·L12
- **`1.2.3.12.a2`·L9** — the initial snapshot for this page c is maintained
  - requires: `1.2.3.12.a1`·L8, `1.2.3.12.3.a1`·L0, `1.2.3.12.3.a2`·L0, `1.2.3.12.4.a1`·L0, `1.2.3.12.4.a2`·L0, `1.2.3.12.5.a2`·L0, `9.5.a1`·L1, `9.5.1.a1`·L0
  - ⇄ cross-instance: `3p.a1`
  - required by: `1.2.3.6.a1`·L10, `3.a1`·L18, `3.7.a1`·L10, `3.7.a2`·L10, `3.13.5.4.a1`·L19
- **`1.2.3.12.2.a1`·L0** — if no previous AccountTransactionPage X exists, and today is on or after Y.start_date, Y.initial_snapshot.full_amount cannot be None.
  - requires: *{whatever}* (no prerequisites)
  - required by: `1.2.3.12.a1`·L8, `3.13.2.a2`·L13, `3.13.2.a3`·L13
- **`1.2.3.12.2.a2`·L0** — if a previous AccountTransactionPage X exists, Y.initial_snapshot.full_amount == X.balance_record[-1].full_amount
  - requires: *{whatever}* (no prerequisites)
  - required by: `1.2.3.12.a1`·L8, `3.13.2.a2`·L13, `3.13.2.a3`·L13
- **`1.2.3.12.a3`·L1** — the initial snapshot for this page has updated conciously-applied calculations (fundjar.milestone amount, page.current_free_amount, page.current_unpaid, current_safety_cushion, )
  - requires: `1.2.3.12.5.a1`·L0, `1.2.3.12.5.a2`·L0
  - ⇄ cross-instance: `3p.a2`
  - required by: `3.13.5.4.a1`·L19
- **`1.2.3.12.3.a1`·L0** — if no previous AccountTransactionPage X exists, and today is in or after Y, Y.initial_snapshot.expected_amount == None
  - requires: *{whatever}* (no prerequisites)
  - required by: `1.2.3.12.a2`·L9, `3.13.4.a1`·L16
- **`1.2.3.12.3.a2`·L0** — if a previous AccountTransactionPage X exists, Y.initial_snapshot.expected_amount == X.balance_record[-1].expected_amount
  - requires: *(none stated)*
  - ⇄ cross-instance: `3p.13.5.3.a1`
  - required by: `1.2.3.12.a2`·L9, `3.13.4.a1`·L16
- **`1.2.3.12.3.a3`·L0** — if no previous AccountTransactionPage X exists, and today occurs before Y, initial_snapshot.expected_amount cannot be None
  - requires: *(no set)* (no prerequisites)
  - required by: *(nothing — a top-level goal)*
- **`1.2.3.12.4.a1`·L0** — if no previous AccountTransactionPage X exists, Y.initial_snapshot.expected_free_amount == None
  - requires: *{whatever}* (no prerequisites)
  - required by: `1.2.3.12.a2`·L9, `3.13.4.a1`·L16
- **`1.2.3.12.4.a2`·L0** — if a previous AccountTransactionPage X exists, Y.initial_snapshot.expected_free_amount == X.balance_record[-1].expected_free_amount
  - requires: *(none stated)*
  - ⇄ cross-instance: `3p.13.4.a1`
  - required by: `1.2.3.12.a2`·L9, `3.13.4.a1`·L16
- **`1.2.3.12.5.a1`·L0** — if no previous AccountTransactionPage X exists, Y.initial_snapshot.fund_jars should include a
  - requires: *{whatever}* (no prerequisites)
  - required by: `1.2.3.12.a1`·L8, `1.2.3.12.a3`·L1, `3.13.4.a1`·L16
- **`1.2.3.12.5.a2`·L0** — if a previous AccountTransactionPage X exists, Y.initial_snapshot.fund_jars == X.balance_record[-1].fund_jars.?deepcopy? but no date.
  - requires: *{whatever}* (no prerequisites)
  - required by: `1.2.3.12.a1`·L8, `1.2.3.12.a2`·L9, `1.2.3.12.a3`·L1, `3.13.4.a1`·L16
- **`1.2.3.12.5.a4`·L0** — if no previous AccountTransactionPage X exists, and today is in or after Y, the current_amount of all fund jars in Y.initial_snapshot.fund_jars cannot be None
  - requires: *(no set)* (no prerequisites)
  - required by: *(nothing — a top-level goal)*
- **`1.2.3.12.5.a5`·L0** — if no previous AccountTransactionPage X exists, and today occurs before Y, the current_amount of all fund jars in Y.initial_snapshot.fund_jars is None
  - requires: *(no set)* (no prerequisites)
  - required by: *(nothing — a top-level goal)*
- **`1.2.3.12.5.a6`·L0** — if no previous AccountTransactionPage X exists, and today is in or after Y, the expected_amount of all fund jars in Y.initial_snapshot.fund_jars = None
  - requires: *(no set)* (no prerequisites)
  - required by: *(nothing — a top-level goal)*
- **`1.2.3.12.5.a7`·L0** — if no previous AccountTransactionPage X exists, and today occurs before Y, the expected_amount of all fund jars in Y.initial_snapshot.fund_jars cannot be None
  - requires: *(no set)* (no prerequisites)
  - required by: *(nothing — a top-level goal)*
- **`1.2.3.13.a1`·L8** — all can be paired
  - requires: `3.13.a5`·L7
  - ⇄ cross-instance: `3.13b.a5`, `3.13f.a5`
  - required by: `1.2.3.13.a2`·L9
- **`1.2.3.13.a2`·L9** — all expected-actual transaction pairs have been created and are properly matched
  - requires: `1.2.3.a1`·L1, `1.2.3.13.a1`·L8, `1.2.3.13.6.a1`·L7, `1.2.3.13.6.a2`·L7, `1.2.3.13.7.a1`·L7, `1.2.3.13.7.a2`·L7, `6.4.a1`·L0, `7.4.a1`·L0, `7.4.a2`·L0, `7.5.a1`·L0, `7.5.a2`·L0
  - required by: `1.2.3.6.a1`·L10, `3.13.6.a3`·L10, `3.13.a7`·L10, `3.13.7.a5`·L10
- **`1.2.3.13.a3`·L10** — this current accounttransaction page c is ready to remove balance snapshops with no events in them.
  - requires: `3.13.a2`·L9
  - required by: `3.13.a3`·L11

### Ch.19 Expected/Actual pairing completeness

- **`1.2.3.13.6.a1`·L7** — if self.paired_finance_id == None and self.paired_expected_date == None and we are not expired, there should NOT exist an ExpectedTransaction E in an active page such that:
  - requires: `3.13.6.a2`·L3, `3.13.7.a4`·L6
  - required by: `1.2.3.13.a2`·L9
- **`1.2.3.13.6.a2`·L7** — paired:if self.paired_finance_id != None and self.paired_expected_date != None and we are not expired, there should exist an Expectedtransaction E in an active page such that:
  - requires: `3.13.6.a2`·L3, `3.13.7.a4`·L6
  - required by: `1.2.3.13.a2`·L9
- **`1.2.3.13.7.a1`·L7** — if self.paired_amount == None and self.paired_actual_date == None and we are not expired, there should NOT exist an ActualTransaction A in an active page such that:
  - requires: `3.13.6.a2`·L3, `3.13.7.a4`·L6
  - required by: `1.2.3.13.a2`·L9
- **`1.2.3.13.7.a2`·L7** — if self.paired_amount != None and self.paired_actual_date != None and we are not expired, there should exist an ActualTransaction A in an active page such that:
  - requires: `3.13.6.a2`·L3, `3.13.7.a4`·L6
  - required by: `1.2.3.13.a2`·L9

---

## Topological layers (write/verify order)

L0 has no prerequisites; each later layer depends only on earlier ones (cross-instance couplings excluded, since they point at *other* instances). This is the clean recomputation of the stratification the author's `stratifyAssumptions.py`/`findLongestPath.py` scripts were after (see [03 App. A](03-assumptions-glossary.md#appendix-a-reduction--merge-candidates)).

- **L0** (70): `1.2.3.12.2.a1`, `1.2.3.12.2.a2`, `1.2.3.12.3.a1`, `1.2.3.12.3.a2`, `1.2.3.12.3.a3`, `1.2.3.12.4.a1`, `1.2.3.12.4.a2`, `1.2.3.12.5.a1`, `1.2.3.12.5.a2`, `1.2.3.12.5.a4`, `1.2.3.12.5.a5`, `1.2.3.12.5.a6`, `1.2.3.12.5.a7`, `2.1.a1`, `2.2.a1`, `2.3.a1`, `3.1.a1`, `3.2.a1`, `3.2.a2`, `3.3.a1`, `3.3.a2`, `3.8.a1`, `3.9.a1`, `3.10.a2`, `3.11.2.a2`, `3.12.a1`, `3.12.1.a1`, `3.12.6.a1`, `3.12.7.a1`, `3.12.8.a1`, `3.13.5.2.a3`, `3.13.6.3.a1`, `3.13.7.1.a1`, `3.13.8.1.a1`, `3.13.8.1.a2`, `3.13.8.5.a1`, `3.13.a10`, `4.1.a1`, `4.2.a1`, `5.1.a1`, `5.2.a1`, `5.3.a1`, `6.1.a1`, `6.4.a1`, `6.5.a1`, `6.6.a1`, `7.1.a1`, `7.3.a1`, `7.4.a1`, `7.4.a2`, `7.5.a1`, `7.5.a2`, `7.6.a1`, `7.7.a1`, `7.8.a1`, `8.1.a1`, `8.2.a1`, `8.3.a1`, `8.4.a2`, `8.5.a1`, `9.1.a1`, `9.5.1.a1`, `9.6.2.a1`, `9.7.6.a1`, `9.8.2.a1`, `9.8.6.a1`, `10.3.a1`, `10.4.a1`, `10.4.a2`, `10.4.a3`
- **L1** (11): `1.2.a1`, `1.2.3.a1`, `1.2.3.12.a3`, `3.5.a1`, `3.10.a1`, `3.11.1.a1`, `3.11.2.a1`, `3.13.1.a1`, `3.13.8.a4`, `8.4.a1`, `9.5.a1`
- **L2** (5): `1.2.3.10.a1`, `1.2.3.10.a2`, `1.2.3.10.a3`, `3.13.6.a1`, `3.13.8.a5`
- **L3** (2): `1.2.3.10.a4`, `3.13.6.a2`
- **L4** (6): `1.2.3.11.a1`, `1.2.3.11.a2`, `1.2.3.11.a3`, `3.13.7.a1`, `3.13.7.a2`, `3.13.7.6.a1`
- **L5** (2): `1.2.3.11.a4`, `3.13.7.a3`
- **L6** (9): `3.13.5.a1`, `3.13.5.a2`, `3.13.7.a4`, `3.13.8.a1`, `3.13.8.1.a3`, `3.13.8.a2`, `3.13.8.a3`, `3.13.8.3.a1`, `3.13.8.4.a1`
- **L7** (11): `1.2.3.13.6.a1`, `1.2.3.13.6.a2`, `1.2.3.13.7.a1`, `1.2.3.13.7.a2`, `3.10.a3`, `3.12.5.a1`, `3.12.5.a2`, `3.13.a5`, `3.13.5.a3`, `3.13.5.a4`, `3.13.8.a6`
- **L8** (4): `1.2.3.12.a1`, `1.2.3.13.a1`, `3.13.a6`, `3.13.8.a7`
- **L9** (3): `1.2.3.12.a2`, `1.2.3.13.a2`, `3.13.a2`
- **L10** (7): `1.2.3.6.a1`, `1.2.3.13.a3`, `3.7.a1`, `3.7.a2`, `3.13.6.a3`, `3.13.a7`, `3.13.7.a5`
- **L11** (3): `1.2.3.5.a1`, `3.13.a3`, `3.13.8.4.a2`
- **L12** (3): `3.13.3.a1`, `3.13.a4`, `3.13.a8`
- **L13** (4): `3.13.2.a2`, `3.13.2.a3`, `3.13.5.2.a4`, `3.13.8.a8`
- **L14** (3): `3.13.2.a1`, `3.13.5.2.a1`, `3.13.5.2.a5`
- **L15** (3): `3.13.2.a4`, `3.13.5.2.a2`, `3.13.5.3.a1`
- **L16** (4): `3.13.4.a1`, `3.13.5.a5`, `3.13.8.6.a1`, `3.13.8.6.a2`
- **L17** (1): `3.13.a9`
- **L18** (1): `3.a1`
- **L19** (1): `3.13.5.4.a1`
- **L20** (1): `3.a2`

## Root axioms (genuine `{none}` leaves — no prerequisites)

`2.1.a1`, `2.2.a1`, `2.3.a1`, `3.1.a1`, `3.2.a1`, `3.2.a2`, `3.3.a1`, `3.3.a2`, `3.8.a1`, `3.9.a1`, `3.10.a2`, `3.12.1.a1`, `3.12.6.a1`, `3.12.7.a1`, `3.12.8.a1`, `3.13.8.1.a1`, `3.13.8.1.a2`, `3.13.8.5.a1`, `4.1.a1`, `4.2.a1`, `5.1.a1`, `5.2.a1`, `5.3.a1`, `6.1.a1`, `6.4.a1`, `6.5.a1`, `6.6.a1`, `7.1.a1`, `7.3.a1`, `7.4.a1`, `7.4.a2`, `7.5.a1`, `7.5.a2`, `7.6.a1`, `7.7.a1`, `7.8.a1`, `8.1.a1`, `8.2.a1`, `8.3.a1`, `8.4.a2`, `8.5.a1`, `9.1.a1`, `9.5.1.a1`, `9.6.2.a1`, `9.7.6.a1`, `9.8.2.a1`, `9.8.6.a1`, `10.3.a1`, `10.4.a1`

## Undetermined-prerequisite nodes (`{whatever}` / no set)

The author marked these with `{whatever}` (or left no set) — a placeholder meaning the prerequisites were never pinned down (the requirement-set analogue of the `?...?` markers used elsewhere for values that were left for later). They are **not** axioms — treat their prerequisites as open. They land at L0 in the layering only because no set was recorded:

`1.2.3.12.2.a1`, `1.2.3.12.2.a2`, `1.2.3.12.3.a1`, `1.2.3.12.3.a3`, `1.2.3.12.4.a1`, `1.2.3.12.5.a1`, `1.2.3.12.5.a2`, `1.2.3.12.5.a4`, `1.2.3.12.5.a5`, `1.2.3.12.5.a6`, `1.2.3.12.5.a7`, `3.11.2.a2`, `3.12.a1`, `3.13.5.2.a3`, `3.13.6.3.a1`, `3.13.7.1.a1`, `3.13.a10`, `10.4.a2`, `10.4.a3`

## Top-level sinks (nothing depends on them)

The ultimate goals plus a few addendum sub-rules that no other assumption cites:

`1.2.a1`, `1.2.3.12.3.a3`, `1.2.3.12.5.a4`, `1.2.3.12.5.a5`, `1.2.3.12.5.a6`, `1.2.3.12.5.a7`, `3.1.a1`, `3.13.5.2.a1`, `3.13.5.2.a2`, `3.13.6.3.a1`, `3.13.a10`, `10.4.a2`, `10.4.a3`

## Cross-instance cascade couplings (the `b`/`c`/`f`/`p`/`n` edges)

These are dependencies on a *different* page or snapshot instance — the spine of the forward cascade. `dependent  ⇄  raw-scoped-prerequisite`:

- `1.2.3.6.a1`  ⇄  `1.2.3p.6.a1`
- `1.2.3.10.a1`  ⇄  `1.2.3b.10.a4`
- `1.2.3.10.a2`  ⇄  `1.2.3b.10.a4`
- `1.2.3.10.a3`  ⇄  `1.2.3b.10.a4`
- `1.2.3.11.a4`  ⇄  `1.2.3b.11.a4`
- `1.2.3.12.a1`  ⇄  `3p.13.2.a4`
- `1.2.3.12.a2`  ⇄  `3p.a1`
- `1.2.3.12.a3`  ⇄  `3p.a2`
- `1.2.3.12.3.a2`  ⇄  `3p.13.5.3.a1`
- `1.2.3.12.4.a2`  ⇄  `3p.13.4.a1`
- `1.2.3.13.a1`  ⇄  `3.13b.a5`
- `1.2.3.13.a1`  ⇄  `3.13f.a5`
- `3.13.2.a2`  ⇄  `3.13p.2.a2`
- `3.13.2.a2`  ⇄  `3.13p.2.a3`
- `3.13.2.a3`  ⇄  `3.13p.2.a1`
- `3.13.2.a4`  ⇄  `3.13f.2.a1`
- `3.13.3.a1`  ⇄  `3.13p.2.a1`
- `3.13.3.a1`  ⇄  `3.13p.3.a1`
- `3.13.a5`  ⇄  `3.13b.6.a1`
- `3.13.a5`  ⇄  `3.13b.6.a2`
- `3.13.a5`  ⇄  `3.13b.7.a3`
- `3.13.a5`  ⇄  `3.13b.7.a4`
- `3.13.a5`  ⇄  `3.13f.6.a1`
- `3.13.a5`  ⇄  `3.13f.6.a2`
- `3.13.a5`  ⇄  `3.13f.7.a3`
- `3.13.a5`  ⇄  `3.13f.7.a4`
- `3.13.5.a5`  ⇄  `3.13b.5.2.a5`
- `3.13.5.a5`  ⇄  `3.13f.5.2.a5`
- `3.13.a6`  ⇄  `3.13p.2.a1`
- `3.13.a6`  ⇄  `3.13p.5.2.a5`
- `3.13.8.a8`  ⇄  `3.13b.a8`
- `3.13.8.a8`  ⇄  `3.13f.a8`

---

## Notes on the chart's notation and coverage

- **Parenthesized requirement IDs** in a `{...}` set (e.g. `(9.5.a1)`) are redundant — already implied transitively by another member of the set — so they're folded in as ordinary prerequisites with no special marking. Dropping them would not change reachability.
- **A second `{...}` block** on some boxes is a reduced restatement of the first (a strict subset once scope suffixes are normalized). This graph uses the first (full) block.
- **`3:13.a9`** (with a stray colon) is the same assumption as `3.13.a9`, and is merged.
- **`3.4.a1` ("expired cannot be None")** is not in the chart — `expired` simply shields old pages from cascades — so it is absent here.
- **`10.1.a1`/`10.2.a1`** appear in `a03.txt`'s `3.10.a3` requirement list but were never given a definition and are dropped from the chart's `3.10.a3` box, so they are absent here.
- **Assumptions that live only in the chart** (not the `.txt` files): `10.4.a2`/`10.4.a3` (milestone rules for a jar tied to a repeated expected transaction), `3.13.a10` (the normal-day implicit earmark), and the month-boundary rules `2.1.a2`/`2.2.a2` (their text sits unlabeled in the `2.1.a1`/`2.2.a1` boxes; the tests chart is where they get IDs).
- **The cross-instance scope tags** (`b`/`c`/`f`/`p`/`n`) come from the box text's own scoped IDs — the same information the chart draws as orange (previous) and red (next) arrows.
- Where a box's `{...}` set differs from the same assumption's set in the `.txt` files, this graph uses the **chart set** (the author's authoritative record); the `.txt` variants are reproduced verbatim in [03-assumptions-glossary.md](03-assumptions-glossary.md).
- A few bundled sub-rules carry **no set of their own** in the chart (`1.2.3.12.5.a4`–`a7`, `1.2.3.12.3.a3`, `3.13.6.3.a1`, `3.13.7.1.a1`); they show as sinks with "(none stated)" — addenda, not missing data.

---

## Process regions — the cascade steps

On top of the dependency graph, the author drew dotted outlines grouping related assumptions, each labelled with a plain-language note. Each region is one step of the cascade update — the workflow that walks a page's balance record forward, making each cluster of assumptions true in turn. The regions (boxes named by their first assumption; a box may bundle more):

| Cascade step (region label) | Assumptions in the step |
|---|---|
| Finance patterns in this page are perfect | `1.2.3.10.a1`, `1.2.3c.10.a4`, `3.10.a1`, `3.10.a2`, `3.11.2.a1` |
| Earmark patterns in this page are perfect | `1.2.3c.11.a1`, `1.2.3c.11.a2`, `1.2.3c.11.a3`, `3.11.1.a1`, `3.13.1.a1`, `3.11.2.a1`, `1.2.3c.11.a4` *(a fine-dotted outer group wraps this step and the finance-pattern step above)* |
| Loop over pages and fix patterns | contains the two pattern steps above, plus the fund-jar and deallocation work below |
| New events created, old events removed | `3.13.8.a2`, `3.9.a1`, `7.1.a1`, `3.13.6.a1`, and the assumptions above them in the same column |
| The whole page is cleaned of obsolete events and balance snapshots | `3.13.a2`, `1.2.3c.13.a3`, `3.13.a3` |
| This balance snapshot has full_amount | `3.13c.2.a1`, `3.13c.2.a2`, `3.13c.2.a3`, `1.2.3.12.2.a1`, `3.13.a4` |
| If this is a deallocation day, the implicit earmarks are made | `3.13c.a6`, `3.13c.a7`, `3.13c.8.4.a2`, `3.13c.a8` |
| All\* earmarks created on this page *(\* except implicit earmarks, added later on deallocation days)* | `3.13.8.5.a1`, `3.13c.8.a4`, `3.13.8.a6`, `3.13.8.3.a1`, `3.13.8.a3`, `3.13c.8.a5`, `3.13.8.1.a1`, `3.13.8.a7` |
| Transactions are paired | `3.13.a5`, `6.4.a1`, `1.2.3.13.a1`, `1.2.3.13.6.a1`, `1.2.3.13.a2` |
| Initial snapshot is good except for expected amount | `3.12.1.a1`, `3.12.a1`, `9.1.a1`, `3.13.2.a4`, `9.8.2.a1`, `9.8.6.a1`, `1.2.3c.12.a1` |
| Loop over all pages, updating expected_amount / expected_free_amount on initial snapshots | the cross-page rollup, ending with `current_unpaid_expected`, `current_free_amount`, `current_safety_cushion` and each jar's `milestone_amount` (the consciously-applied calculations, run last) |
| Fund jar validity at the initial snapshot | `3.12.5.a1`, `9.5.1.a1`, `9.5.a1` |

## The author's test-bundling plan

The project was meant to be testing-first. A sibling chart (`assumptionChartTests.uxf`) grouped assumptions into test bundles — one test function verifying a related cluster together — with a colour code for the order to write them in: **green** = no unmet dependencies (write first), **grey** = intermediate, **pink** = end-of-chain completeness checks (write last). It covers 64 of the assumptions in 24 bundles:

| Test | Bundles | Order |
|---|---|---|
| `2.T1` | `2.1.a1`, `2.2.a1` + month-boundary `2.1.a2`/`2.2.a2` | |
| `4.T1` | `4.1.a1`, `4.2.a1` | |
| `5.T1` | `5.1.a1`, `5.2.a1`, `5.3.a1` | green |
| `6.T1` | `6.1.a1`, `6.5.a1`, `6.6.a1` | green |
| `7.T01` | `7.1.a1`, `7.6.a1`, `7.7.a1`, `7.8.a1` | green |
| `7.T02` | `7.3.a1`, `7.4.a1`, `7.4.a2`, `7.5.a1`, `7.5.a2` | |
| `8.T1` | `8.1.a1`–`8.5.a1` | |
| `9.T01` | `9.7.6.a1` | |
| `9.T02` | `9.6.2.a1` | |
| `10.T1` | `10.3.a1`, `10.4.a1` + `10.4.a2`/`10.4.a3` | |
| `1.T01` | `1.2.3.a1`, `1.1.a1` (requires `2.T1`) | |
| `1.T02` | finance-pattern cross-page continuity (requires `4.T1`, `1.T01`) | |
| `1.T04` | earmark-pattern cross-page continuity (requires `1.T02`, `3.T04`) | grey |
| `3.T01` | `3.2.a1`, `3.2.a2`, `3.3.a1`, `3.3.a2`, `3.5.a1`, `3.8.a1`, `3.9.a1` | green |
| `3.T02` | `3.10.a1`, `3.10.a2` (requires `3.T01`) | |
| `3.T03` | `3.13.7.6.a1`, `3.13.7.a2` (requires `1.T02`) | |
| `3.T04` | `3.11.2.a1`, `3.11.2.a2` (requires `5.T1`) | |
| `3.T05` | `3.13.1.a1` (requires `3.T01`) | |
| `3.T06` | `3.13.8.a1`, `3.13.8.a2`, `3.13.8.4.a1`, `3.13.8.a6` | |
| `3.T07` | `3.13.8.3.a1`, `3.13.8.a3` (requires `1.2.3c.11.a4`) | |
| `3.T08` | `3.13.8.5.a1`, `3.13c.8.a4`, `3.13c.8.a5`, `3.13.8.1.a1`, `3.13.8.1.a2` | |
| `3.T09` | `3.13.8.a7` (requires `3.T06`, `3.T07`, `3.T08`) | pink |
| `3.T10` | `3.13.6.a1`, `3.13.6.a2`, `3.13.6.3.a1` | pink |
| `3.T11` | `3.13.7.a1`, `3.13.7.1.a1`, `3.13.7.a3` (requires `1.2.3c.10.a4`, `3.T03`) | |

A companion file at the project root, `things to test.txt`, holds the worked *scenarios* those tests would simulate (e.g. saving toward a boat, with variations for early/late and paired/unpaired purchases), and `class documentation.ods` supplies numeric examples to check results against.
