# 23 — Form behavior: defaults, inheritance, and the saved/working-state split

**Status: all five items (A–E) SETTLED.** Three things deliberately left open, none blocking: D's
exact "changed from saved" styling (parked until helper regions are laid out); the
warnings/confirmation mechanism for Forced downward changes (parked; first concrete case is Start
moving forward past existing history); and the `MaybeStartingEarmark` auto-save inconsistency (flagged,
not fixed, per the author's call).

The Starting-point region is both informational AND a shortcut to make/edit the isolated earmark at an
`EarmarkPattern`'s own start (that second half isn't built yet). Two booleans govern it (one-line doc
comment on each in `EarmarkFormPanel`): `ShowFundStartPointRegion` — whether the region appears at all
(for now true in Savings-plan mode, false in One-off, expected to get more restrictive over time) — and
`UsersCanEditFundStartPoint` — whether the starting isolated earmark's own amount can be edited
(requires `ShowFundStartPointRegion`, locks once the pattern's `ActiveStart` is in the past; defined,
not yet wired). `ShowFundStartPointRegion` is the *only* visibility gate: whenever it's true the region
always renders, in one of three content states — a real nonzero total ("as of [date]"); a saved plan
with nothing at day one ("savings plan started from 0"); or no saved plan yet ("no savings plan saved
yet").

Author's framing: the assumption set governs *saved* information, but a form spends most of its life
holding *unsaved* edits — a case the assumptions say essentially nothing about, and 21's content
inventory never separately named. Five problems (A–E), worked one at a time; `SETTLED`/`PROPOSED`/`OPEN`
markers are literal.

**Reading list:** [21](21-form-architecture.md) (the form architecture these forms live inside — the
three-region layout, the helper-region concept, Philosophy 6's shortcut-transparency mechanics),
[22](22-plan-health-state.md) (the one big worked example of "derived content sourced from a forecast
run," which item D below generalizes into a standing rule), [01](../../01-glossary-of-terms.md) and
[03](../../03-assumptions-glossary.md) (the class hierarchy and the specific assumption IDs the
standing rule below is grounded in — cited, not invented).

---

## The standing rule (author, 2026-08-06 — certain, not open for debate)

The class diagram already dictates a hierarchy: `FinancialPattern` → `EarMarkPattern` →
`EarMarkEvent` (top to bottom — see [01](../../01-glossary-of-terms.md#class-hierarchy-at-a-glance)).
**Saving a change to a higher class may imply changes to instances of a lower class; saving a change
to a lower class may never imply a change to a higher one.** Concretely: editing a `FinancialPattern`
can force its `EarMarkPattern`(s) to change (or become invalid) to stay assumption-satisfying; editing
an isolated `EarMarkEvent` must never be able to force a change to the `EarMarkPattern` it sits on. Any
edit that *would* require altering the class above it must be made **structurally impossible** — by
the UI and, ultimately, the database — not merely discouraged. The author's own example: an isolated
earmark's date can never be moved to before its parent `EarMarkPattern` exists.

This isn't a new invention — it's already the exact content of several existing assumptions, which is
why "structurally impossible" is achievable rather than aspirational:

- **`3.13.8.a1`/`3.13.8.a2`** — an earmark (even isolated) can't exist without its earmark pattern, and
  can't fall outside that pattern's own date span. This *is* the author's own worked example, already
  codified. Already enforced in code today: `ManualEarmark.Create`'s span check
  (`redesign/MyMoneyForecast/src/MyMoneyForecast.Domain/ManualEarmark.cs`) throws on exactly this.
- **`3.11.2.a2`** — an earmark pattern's span can't extend beyond its finance pattern's. Already
  enforced: `EarMarkPattern.Create`'s `ActiveStart`/`Until` checks.
- **`3.13.8.1.a3`** — an earmark's `finance_id` must equal the goal it's saving for's `finance_id`.
- **`3.10.a3`** — the general statement: an `EarMarkPattern`/`EarMarkEvent`/`FundJar`/
  `ExpectedTransaction` can't exist referencing a `finance_id` the page has no `FinancialPattern` for.

All four are phrased as constraints *the lower class* must satisfy relative to the higher one — none
of them is phrased the other way around. That asymmetry in the original assumption set is precisely
the hierarchy the author is restating here for the *forms*, not a new rule layered on top of an old
one.

**Naming this rule** — it will get cited constantly for the rest of this document and probably the
whole UI-implementation pass, so it earns a short name rather than a re-explanation every time:

**Options (pick one, or veto all and propose your own):**
1. **"Downward-only editing"** — plain, describes the direction directly. *(recommended)*
2. **"The hierarchy rule"** — shorter, less self-explanatory out of context.
3. **"One-way cascade"** — reuses "cascade," already a loaded term in this project for the engine's
   own day-by-day recompute; risks being confused with that.

Used as **"Downward-only editing"** for the rest of this document until you say otherwise.

## A second standing rule, found while working item A (author, 2026-08-06)

Downward-only editing says *which direction* a change is allowed to flow. It doesn't say *when* a
downward change actually gets written to disk — and that turned out to need its own rule, found while
working item A's "does a suggested value overwrite what's saved" question:

**Forced** *(saves immediately, no separate confirmation)* — a value that, if left alone, would leave
some instance violating an assumption. **Suggested** *(stays pending/unsaved until the user's own
Save)* — a value that's recommended but not required; ignoring it produces valid data, just a worse
outcome (typically: a `PlanHealthState` warning).

The test is literal, not judgment-based: **is there an assumption ID that would actually be violated
without this value?** If yes, Forced. If the only consequence is a warning state, Suggested. The two
rules turn out to be one fact seen from two angles — **every Forced case is exactly a Downward-only-
editing cascade** (a `FinancialPattern` edit that would leave an existing `EarMarkPattern` violating
`3.11.2.a2` if nothing adjusted). Nothing else identified so far in this document is actually Forced.

**Naming options:** **"Forced" / "Suggested"** *(recommended, running with this)*, or "Required" /
"Recommended," or "Assumption-driven" / "Warning-driven."

**Finding, flagged, not fixed (author, 2026-08-06): today's shipped code is on the wrong side of its
own rule.** `AllocationPlanProposer.MaybeStartingEarmark`'s starting earmark saves immediately
(`MainWindow.AutoCreateAllocationPlan`, `_manualEarmarks.Save(starting)`), but no assumption requires
it to exist — a plan with none is fully valid, it just reads short until its own accrual catches up.
By the rule above that's Suggested, not Forced. **Author's ruling: flag it as a follow-up, don't touch
the tested engine code as part of this conversation.** Counter-consideration worth keeping on record
for whoever picks this up: the urgency that motivates the mechanism in the first place (a mandatory
bill due before the user is likely to revisit the form) is itself an argument for keeping it auto-saved
rather than risking it going unconfirmed — not obviously wrong to leave as a deliberate exception,
just not decided.

## Deferred — not designed now

**The warnings/confirmation mechanism for implicit downward changes (author, 2026-08-06, explicitly
parked).** The moment Downward-only editing forces a change onto a lower-class instance (a
`FinancialPattern` edit invalidating or reshaping its `EarMarkPattern`), Philosophy 1 (inform, don't
automate away agency) is at stake — the user could cause a cascading change without realizing it. The
fix is presumably a warning/confirmation at save time naming what's about to implicitly change. **Not
designed here** — parked per the author's own instruction, to be picked up later in this same problem
sequence (most likely bundled with item E, "Break off vs. alter," since that's where implicit
downstream changes are most consequential — not yet decided).

**PICKED UP 2026-08-11 — see [planning/25](25-editing-patterns-with-history.md).** Landed bundled with
item E, exactly as guessed above. A new assumption
(`1.2.3.10.a5`, [03-assumptions-glossary.md, Ch.20](../../03-assumptions-glossary.md#chapter-20-editing-a-finance-pattern-with-existing-history))
locks a `FinancialPattern`'s other properties once it has an expected transaction on or before the
as-of date; a new standing UI rule requires a confirmation before a form save would alter, delete, or
remake one anyway. The mechanism that satisfies both, once confirmed, turns out to be item 4's own
`BreakOffFactory.BreakOff` ([16](16-stage3-break-off.md)) reused almost as-is — full design in 25.

---

## Item B — the isolated-earmark entry point — SETTLED 2026-08-06

**Narrower than it first reads.** Most of "how do we start editing/making an isolated earmark event"
was already answered in 21: the Savings-plan/One-off-adjustment toggle is the door in; "+ New Earmark"
starts a blank one; and item 10 in 21's Earmark content table already gives Savings-plan mode "an
overview, with shortcuts to edit ones that haven't happened yet" — a list-based way to find an existing
one without even switching modes. B's actual gap: once already *in* One-off mode, how do you reach a
*different* existing entry without backing out to that list.

**The date-picker doubles as a selector.** Days that already carry an isolated earmark on this goal are
visually marked in the picker — reusing `RecurrenceRuleEditor`'s existing `SetHighlight(dates, caption)`
mechanism (planning/22 §6b) with a different date set, not a new control. Clicking a marked day loads
that entry (what specifically loads is item C's question, not this one); clicking an unmarked in-range
day starts a new one, blank; an out-of-range day can't be clicked at all (A4's validate-in-place rule,
restated here rather than re-derived).

**Switching entries with unsaved edits present: reuses the existing "Clear form" rule.** Clicking a
different marked day while the form has unsaved work asks for confirmation first, the same as Clear
form already does — one rule for "about to lose unsaved work," not a second one invented for this
specific control.

**Marked/selectable days: restricted to the future, matching item 10.** Item 10's own overview only
offers shortcuts for isolated earmarks that haven't happened yet; the picker inherits the same
restriction rather than defining "selectable" two different ways in one form. (Past entries simply
aren't marked — not shown as view-only, not specially called out; if that turns out to be worth
revisiting, it's a small, contained change to this one rule.)

---

## Item C — isolated-earmark inheritance from its selected `EarMarkPattern` — SETTLED 2026-08-06

**Already built, ahead of this document — cited rather than designed.** `EarmarkFormPanel.LoadOneOff(editTarget, initialDate, preselectFinanceId)`
is the real, live method that populates One-off-adjustment mode, and it already implements almost
everything C was going to ask:

| Field | New (blank) | Existing (`editTarget` loaded) |
|---|---|---|
| Add / Withdraw / Move | Defaults to Add, Move enabled | Inferred from the saved amount's sign (`>= 0` → Add, else → Withdraw); Move's own combo option is *disabled* — a single stored row can't represent a two-legged operation, so editing into one isn't offered |
| Date | `initialDate` if supplied (item B's picker, or "+ New Earmark"'s "today"), else today; editable | The saved date; **locked** (identity-key reasoning, see the A4 table update above) |
| Which goal | Whatever was selected/preselected; changeable via "Change goal" | The saved `FinanceId`; "Change goal" itself is **disabled** — same identity-key reasoning |
| Amount | Blank | The saved amount (`Math.Abs`, since sign lives in the Add/Withdraw choice instead) |

This is exactly A1-a's rule applied here: an existing entry shows Saved state; a new one gets sensible,
context-aware defaults, not a copy of anything. Nothing about the parent `EarMarkPattern`'s own fields
(its `Amount`, its recurrence) gets copied onto a one-off entry — confirms the suspicion flagged back
in item A that a literal copy would be actively misleading here (a per-occurrence rate has no natural
relationship to a single adjustment).

**Addendum, SETTLED 2026-08-07 — the Summary region and its warnings apply in One-off mode too, not
just Savings-plan mode.** Author's own correction: a one-off adjustment is often made specifically to
*fix* a problem `PlanHealthState` flagged (a "will miss"/"already missing" plan), so the information
needed to size that fix belongs right where the fix is being made — Philosophy 4, not a Savings-plan-
mode-only concern. Not implemented yet (the Summary region itself still reads placeholder data, not
real `PlanHealthState`, until that's wired in) — recorded here so the scope is right the moment it is:
whatever surfaces jar/health state has to render in *both* modes.

**The gap this doesn't cover yet — RESOLVED IN DESIGN 2026-08-06 (implementation still pending, the
shortcut itself isn't built):** the future "already missing"/"will miss" shortcut (21, still unbuilt)
wants Amount to start on a *Suggested* catch-up figure, not blank — `LoadOneOff` needs a new parameter
for that. Nothing extra to design beyond adding it: once the Suggested amount is sitting in the field,
item D's "everything derived reads live" rule already covers making item 15's "what can I allocate
today" region (and anything else derived) reflect it — one general rule doing its normal job, not a
second mechanism for suggestions specifically.

---

## Item E — break off vs. alter — SETTLED 2026-08-06

**The governing rule was already settled — planning/16, 2026-07-29, cited not re-derived.** The test:
*does the user want this true only from a date forward, while the past is reasoned about the way it
already was — or is uniform-everywhere correct/wanted?* Three buckets, not two — **plain edit**
(uniform change; includes a *correction* to Amount/schedule, and extending `Until` further out),
**break off** (Amount/schedule genuinely changes as of a date, the pattern continues), **truncate**
(the pattern stops, no continuation). Critically: **"the real branch point is not 'did amount or
schedule change,' it's 'does the user want the past preserved or corrected'"** — these are two
separate, *deliberately chosen* actions; the system never infers which one was meant from what
changed.

**The author's own Start-date example, resolved by that test:** a plain "I mis-entered the start
date" is a correction — plain edit, mutate in place, same bucket as extending `Until`. **New finding
surfaced by working through it:** moving `Start` *forward* can orphan something that already exists
earlier (a `ManualEarmark`, or an `EarMarkPattern`'s own span) — not a correction-vs-change question,
a Downward-only-editing one. **Until the deferred warnings mechanism exists: blocked structurally,
same treatment as the date-picker's out-of-range dates (A4)** — the edit simply can't be saved, no
warning UI needed yet.

**Cross-class consistency — already architected, not just promised.** `RestructureFactory`'s own
header calls itself "the earmark-side counterpart to `BreakOffFactory`'s 'Change starting on a
date,' scoped one level down" — same test, same shape, built as a pair on purpose:

| | `BreakOffFactory` (`FinancialPattern`) | `RestructureFactory` (`EarMarkPattern`) |
|---|---|---|
| Successor's identity | New `FinanceId` — a genuinely new bill/goal | Same `FinanceId` — the goal never changed |
| Jar balance | Hands off via `StartingAllocation` | Nothing to hand off — same jar, carries for free |
| Successor's numbers | Freshly re-proposed (`AllocationPlanProposer`) | User-specified directly, no re-proposal |

The difference tracks a real distinction (a bill/goal changing vs. only how it's funded changing), not
an inconsistency in how the rule is applied.

**The genuinely new design work — the entry point, since both factories are fully built and tested
but wired to nothing:**

- **"Change starting on a date" (and its sibling, "Stop this on a date" for Truncate) is a
  button/action, not a persistent mode** like Savings-plan/One-off — a point-in-time operation the
  user commits to, not an ongoing alternate view of the form. Gated to an existing, recurring
  instance, matching 21's own instance-information-block hook.
- **Once invoked, the form reuses its existing Amount/Schedule fields for the successor's new
  values** — no duplicate field set — **but this must be unmistakable, not silent:** the
  instance-information-block's own status text extends to name it ("Editing: Rent — changing
  starting Mar 1, 2027"), and a small labeled divider sits directly above the repurposed fields
  ("Starting [cut date]:") with the predecessor's own prior rate shown right beside it as a plain
  caption ("Until [cut date − 1]: $X/month") — old and new each labeled, nothing repurposed silently.

---

## Working order

The author's own instruction: order to minimize how much of a *previous* problem's resolution has to
stay in your head to work the next one; large-scale problems last, but before anything that actually
depends on one of them.

| # | Item | Why here |
|---|---|---|
| A | Default values & inheritance (+ naming the saved/working-state split) | Most foundational — everything below either reuses its vocabulary or extends its answer directly. Self-contained: only needs the class hierarchy + Downward-only editing, both already in hand. |
| B | Isolated-earmark entry point (the date-picker-as-selector idea) | A navigation/UI-flow question, largely independent of A's *content*. Placed right after A mainly because C extends it directly. |
| C | Isolated-earmark inheritance from its selected `EarMarkPattern` | A direct, narrow extension of A's general answer — and needs B's entry point settled first ("what pre-fills" presupposes "how you got there"). |
| D | Derived/informational content — saved state or working state? | Touches *every* helper region on *every* form (planning/21 §Helper regions), a bigger blast radius than B/C — ranked as the larger of the two remaining problems. Needs A's vocabulary. |
| E | Break off vs. alter in place | The single largest, most consequential decision here — a genuine new mechanism, must land the same way for two different classes, and it's the one item the deferred warning-mechanism above is likely to attach to. Last, per the rule. Nothing else on this list depends on it, so nothing is stranded waiting behind it — if that changes as A–D get worked, the dependent item moves to *after* E, not before. |

(Author's own bullets, for traceability: A = "default values shown…", B = "how do we start
editing… isolated earmark event", C = "how would the isolated earmark version… inherit values", D =
"some information in the form isn't editable…", E = "how would the user decide if they were
'breaking off'…".)

---

## Item A — Default values and inheritance

### A0. Naming the split this whole item (and D) turns on

Every question in this item collapses once one distinction is named: a form field's value can come
from two different places — what's actually **in the database** for the instance being edited, or
what's **currently sitting in the form**, which starts out equal to the saved value and can diverge
the moment the user types. Planning/21 already uses "unsaved edits" informally (the
instance-information-block's "modified since last saved" indicator); this needs a crisper *pair* of
names, since item D is going to ask, field by field, which of the two a given piece of content reads
from.

**Options:**
1. **"Saved state" / "Working state"** *(recommended)* — "working" reads as "in progress," pairs
   cleanly.
2. **"Saved state" / "Draft state"** — "draft" is maybe the most immediately intuitive to a user, but
   this document is internal design vocabulary, not a UI label (same distinction 21 already draws for
   "Savings Plan" — see [[feedback-design-in-class-documentation-terms]]), so plain-language-to-a-user
   isn't the deciding factor.
3. **"Persisted values" / "Live values"** — more technical, closer to the codebase's own vocabulary
   (`PatternDatabase`, `Save()`), less approachable in prose.

**SETTLED as Saved state / Working state**, 2026-08-06 (proposed, not vetoed).

### A1. Does what a form shows match what's saved?

Decomposes into three genuinely different cases, only one of which is actually still open:

- **A1-a — Editable fields, on load. REVISED 2026-08-06 — not quite "obvious" after all.** The precise
  version, corrected by the author: loading an existing instance shows its Saved state for a field
  **unless the system is deliberately offering a Forced or Suggested value for that specific field** —
  in which case the field starts on that value instead, and this counts as an unsaved (Working-state)
  difference from what's on record from the moment the form opens. A Forced value gets written to disk
  immediately regardless (see the standing rule above); a Suggested one sits exactly like any other
  unsaved edit — including that **"Discard unsaved changes"** (§ the reset-to-saved control, below)
  is what clears it back to the true Saved state. So the field-population rule isn't "always saved" —
  it's "saved, unless something is actively suggesting otherwise for a specific, named reason."
- **A1-b — Text computed live from the form's own visible fields** (the characterization text — "Bill,"
  "Loan," "Custom," per 21's characterization-field-block). This is mechanically Working-state *by
  construction*: it has no other input than whatever the fields currently say, so on load it happens to
  equal Saved state and diverges the instant the user changes anything. Not a separate design decision
  either — there's nowhere else for it to read from. **PROPOSED as obvious.**
- **A1-c — Content that requires an *external* computation the form's own fields don't carry** (a
  forecast run — jar balances, milestones, `PlanHealthState`, the Summary region's whole narrative).
  *This* is where Saved-vs-Working is a genuine, unforced choice — re-run `CreateForecast` against the
  user's in-progress, unsaved edits (a "what if I saved this" preview), or keep showing the
  last-actually-saved instance's real numbers until a save happens? **This is exactly item D, not a
  new question — A1 discovers its own hardest sub-question is D**, which is one more reason D deserved
  its own slot in the working order above rather than being resolved as a one-liner here.

### A2. Inheritance, `FinancialPattern` → `EarMarkPattern`

**Already mostly settled, cited rather than re-derived:** planning/21's Step 1 revision removed
"Saving toward" as a field entirely — the instance-picker *is* the inheritance mechanism (picking a
goal loads whatever plan exists for it). For a genuinely **new** plan (the F27 "start a concurrent
plan" popup control, or a goal that has no plan yet), the open question is what its own fields
(`Amount`, the recurrence) should default to.

**PROPOSED:** reuse `AllocationPlanProposer.Propose()` **live**, the exact function that already
auto-proposes a plan at bill-creation time — not a literal copy of the goal's own `Amount`/schedule
(the goal's `Amount` is the total bill; the plan's is a *periodic contribution rate* — copying it
verbatim would usually be wrong, not just unhelpful). This is Philosophy 6 (pre-fill as far as it
honestly goes) served by a mechanism that already exists, not a new one. **OPEN — needs your
confirmation**, since it's a real design choice, not a forced one like A1-a/b.

### A3. Inheritance, `EarMarkPattern` → `EarMarkEvent` (isolated)

**Deliberately not resolved here — this is item C**, once B has settled *how* the user arrives at
editing/creating a specific isolated event. Flagging only the general shape now: unlike A2, a literal
copy of the parent plan's own `Amount` is actively misleading for an isolated adjustment (the plan's
`Amount` is a recurring rate; a one-off Add/Withdraw/Move has no natural relationship to it), so
whatever C proposes will likely lean on context (today's date, the currently-computed free amount) more
than on copying the parent's stored fields at all.

### A4. When should fields be disabled?

Re-framing before answering: the question bundles three different real needs, and "disabled" (a
visible-but-locked input control) is the right tool for only one of them — worth naming the split
explicitly rather than reaching for "disabled" as a catch-all:

| Need | The actual tool | Precedent already in the docs |
|---|---|---|
| A field is irrelevant given the current mode/characterization (Skippable when Direction = Income) | **Hide it** | Already the settled convention — planning/21's Step 2 tables use "hidden when…" throughout |
| A field shows a *different* instance's data for context (Earmark's "linked goal's own detail") | **Plain read-only text, not an input control at all** | Already the pattern `AccountWindow`'s consistent-info captions and 21's "read-only" items use |
| A field is normally editable but structurally constrained right now (Downward-only editing — an isolated event's date can't precede its parent plan's span) | **Keep it editable; validate/range-limit in place** | New, follows directly from the standing rule above |

**PROPOSED:** a literal disabled-but-visible input control shouldn't be the default answer to "should
this be locked" — reach for one of the three rows above first. **OPEN** only in the sense that a
genuine case demanding a true disabled control might still turn up in a later item (B/C/E are the most
likely candidates); none has yet.

### A5. "Opening up an existing pattern from somewhere else"

**Already settled — cited, not re-opened.** Planning/21's instance-information-block section already
rules this: a shortcut sets the instance-picker's value *programmatically*, and Philosophy 6's own
highlighting/staggered-reveal mechanic is what makes *how the user got there* legible — none of that
changes *what values* get shown. Combined with A1-a, the answer is unconditional: an existing instance
opened via a shortcut shows exactly the same Saved-state values a manual open would; only the arrival
is visibly different, not the content.

### A6. The reset-to-saved control — SETTLED 2026-08-06

The same control planning/21 already called for and left the wording open on ("a 'discard unsaved
edits' control... needs care so it reads clearly as discarding the *edits*, not deleting the
*instance*"). Author's own justification, worth keeping on record since it's better than anything
written down for this before: resets every field to Saved state, which also clears any Suggested
value sitting in a field and brings back whatever warning that Suggested value was quietly resolving —
so a user who made a pile of changes and lost track of why can back all the way out, then re-apply
their own edits one at a time, watching the (now-live, see item D below) derived content react to
each one, instead of trying to remember what they were doing.

**Wording: "Discard unsaved changes."**

## Item D — settled early, out of formal sequence

Not reached in its own turn (queued after B/C in the working order above) — the author volunteered a
ruling while working item A, since it followed directly from A1's own decomposition (A1-c *is* item D
— see above). Recorded here rather than held back artificially.

**SETTLED 2026-08-06: every piece of derived/informational content reads live, from Working state —
not just the Summary region, Current jar state included.** No exception carved out for "this is a
statement of fact about what's really saved" — one rule, everywhere.

**Addition, SETTLED 2026-08-06: derived text that currently differs from what it would show under
Saved state should be visually flagged as changed** — not just showing the new number, but
communicating that it's a *consequence* of a pending edit, not settled fact. Mechanically, this is
**not a new thing to build** — it's the exact mechanism planning/22 §6b already scoped for the RRule
preview's transient "fixed" message (hold the state from when the form opened, diff it against a live
recompute as the user types), generalized from "did this one warning clear" to "does any piece of
derived text differ from its saved-state counterpart at all." **Not yet designed:** the actual
styling — per the standing "meaning is never color-only" principle
([11 § C](11-ui-design-and-decisions.md#standing-ui-principles-distilled-from-the-above)), it can't be
color alone; exact treatment deferred to when helper regions are actually laid out, not needed to
settle the rule itself.

---

## Item A — where it actually landed

**Adopted, no objection raised:** the "Downward-only editing" and "Saved state/Working state" names
(used throughout this document without being vetoed); the A4 framework itself (hide / plain-text /
validate-in-place beats a literal disabled input control).

**A4, made concrete — which fields, in which category:**

| Category | Fields | Status |
|---|---|---|
| Hide when mode-irrelevant | Everything planning/21's Step 2 tables already mark "hidden when…" | Already fully specified — nothing new from this item |
| Plain read-only text, not an input | Earmark's linked-goal detail; Account's cushion-context + "Referenced by…"; Expense's proposed "Currently saved toward this" | Already content-specified by 21 as "read-only"/"Info" — this item's only addition is confirming they render as plain text, never as a locked textbox |
| Validate-in-place (Downward-only editing) | An isolated `EarMarkEvent`'s own Date (can't leave its parent `EarMarkPattern`'s span); an `EarMarkPattern`'s own recurrence Start/Until (can't leave its parent `FinancialPattern`'s active span) | Both already enforced at the domain layer (`ManualEarmark.Create`, `EarMarkPattern.Create`). **Form-level UI treatment SETTLED 2026-08-06: the date-picker refuses to show out-of-range dates at all — an invalid value must be impossible to enter, not merely rejected after the fact.** Same "make it structurally impossible" spirit as the standing rule itself, now extended from the database down to the picker control. |
| Locked because it's part of the row's identity key — **found while working item C, a real 4th category A4 missed** | An isolated earmark's own Date and which goal it belongs to, once an *existing* entry is loaded for editing | **Already built, not proposed:** `EarmarkFormPanel.LoadOneOff` sets `EarmarkDatePicker.IsEnabled = false` and `ChangeGoalButton.IsEnabled = editTarget is null` the moment `editTarget` is non-null. Matches the table's own composite key (`PRIMARY KEY (FinanceId, EarmarkDate)`) exactly — changing either isn't an edit to the loaded row, it's deleting one row and creating a different one, so the UI simply doesn't offer it as an in-place edit. |

Forced/Suggested timing doesn't actually belong in "which fields get disabled" at all, on reflection —
it's not about locking a field on *this* form, it's about whether a value gets silently written on
*save*. Its territory is item E and the deferred warnings-mechanism, not this item.

**A2 — SETTLED 2026-08-06.** A from-scratch `EarMarkPattern`'s Amount/Recurrence fields pre-fill with
`AllocationPlanProposer.Propose()`'s live suggestion — the same one "Save and Plan" would have
produced, regardless of which door the user actually came through. Per A1-a's revised rule this is a
**Suggested** value: it counts as an unsaved (Working-state) difference from the moment the form opens
(there is, after all, no saved plan yet at all — the whole thing is Working state until the user's own
Save), and **"Discard unsaved changes"** clears it back to a genuinely blank, plan-less state. What
`Propose()` actually does, for the record: it replaced the old invisible day-by-day reservation "ramp"
(a Philosophy-1 violation — computed, not editable) with a real proposed plan, shaped one of two ways —
**paced against a single clean paycheck** when one exists (same rhythm, sized so contributions match
consumption over the window; plus a one-time catch-up if the bill's first due date beats the first
paced contribution — the starting earmark), or, when there's no single paycheck to pace against,
**the outflow's own full amount reserved each cycle** (a repeating bill) or **spread evenly in monthly
installments to the due date** (a one-time goal).
