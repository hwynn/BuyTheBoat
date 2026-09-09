# 15 — Stage 2: Pattern lifetime and the form family

Stage 2 of the ["Adjusting the Plan" phase](13-adjusting-the-plan-charter.md); design settled, built.

**What it settled.** A repeating pattern (bill or paycheck) answers one plain question — *"when does
this stop?"* — with three answers the user never sees categorized:
- **it keeps going** — an "ongoing" flag whose internal end date is quietly extended to the horizon
  plus a cycle, never shown; renewed on schedule via `BreakOffFactory.Renew` (`AutoRenew` marks it);
- **it ends on a date I know** — a plain until date;
- **it ends when I've paid it off** — a payoff date computed from balance ÷ payment, stated as a floor.

The question lives inside the existing bill form — no new buttons. The distinct **`ActiveFrom`**
lead-in property was designed here too, so a plan (and its jar) can start before its first occurrence.

**Built — see the code:** `PayoffEstimator`, `RecurrenceRule.ActiveFrom`, `BreakOffFactory.Renew` /
`FindCurrentSegment`, the `AutoRenew` field, the bill/payoff form family. Covers charter item 10.

**Still open:** Item D's form is built but has UI-layer uncertainties (notably the loan-payoff window
too tall for a laptop) — tracked in the charter's UI/wiring backlog. Final wording of the "when does
this stop?" question follows [11's UI principles](11-ui-design-and-decisions.md). Actuals notes →
[12](12-actual-transactions-deferred-design.md).
