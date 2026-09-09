# 16 — Stage 3: Changing a pattern at a point in time

Stage 3 of the ["Adjusting the Plan" phase](13-adjusting-the-plan-charter.md); domain built and tested.

**What it settled.** A repeating pattern's amount or schedule changes mid-life (rent goes up, a job
changes pay). The change applies *going forward* while what already happened stays computed the way it
was reasoned about at the time. Constraint 1 (one `Amount`, one `RecurrenceRule` per pattern) means
that can only be expressed by **ending the old pattern at the change date and starting a new one** — a
genuine new `finance_id`, not an edit ("break-off"). The stage settled the taxonomy (item 6),
break-off (4), truncation (16), deletion (18), breaking off a transfer, and the ongoing pattern's
periodic-renewal reuse of break-off. "Break off" stays the internal term; the user-facing wording was
chosen separately (4-F).

**Built — see the code:** `BreakOffFactory` (+ `Renew`, `FindCurrentSegment`), `PatternTruncation`,
`TransferBreakOffFactory`, `TransferTruncation`; all domain, tested.

**Still open:** item 4-D's **preview-and-confirm screen** — data contract settled, no UI wired
(charter's UI/wiring backlog); truncation/delete legibility deferred to Stage 6. `F23` (`Source`
uniqueness) and items 5/17 are parked pending actual transactions → [12](12-actual-transactions-deferred-design.md).
