# 19 — Stage 6: Warnings, levers, and the shortcut surface

**Status: OPEN — started 2026-07-30.** Items **19, 20** + the "thin" presentation. Stage 6 of the
["Adjusting the Plan" phase](13-adjusting-the-plan-charter.md) — **last on purpose: the user-facing
consumer of everything Stages 1–5 built.** Two governing policy questions are **SETTLED** below; the
per-state lever design (the bulk of item 19) and the shortcut inventory (item 20) are not yet done.

**The stage in one paragraph:** almost everything this stage needs already exists as raw material —
every earlier stage deferred its own "should the user be told?" question here rather than answer it
piecemeal. This stage's job is to catalog those states once, decide what lever (if any) goes with
each, and settle a policy for keeping the non-Forecast tabs from turning into button soup once they
get reworked (the Forecast tab itself is settled and out of scope — see the charter).

### Reading list
1. [13 — charter](13-adjusting-the-plan-charter.md), the Stage 6 section and items 19/20.
2. [11 — UI design & decisions](11-ui-design-and-decisions.md) — the **standing UI principles**
   (§ "Standing UI principles distilled from the above") this stage designs against directly.
3. [14 — stage 1](14-stage1-allocation-model.md), Item C (the "thin" → cushion-not-whole meaning,
   deferring wording/symbol here) and the **F12 dormancy family**.
4. Wherever else a state below was first flagged — cited inline per row.

---

## Policy questions — SETTLED 2026-07-30

### 1 — Keeping button count sane (item 20)

**Ruling: consolidate related actions behind one entry point with a follow-up question**, rather than
a numeric cap or an unbounded "one button per justified state" rule. (Asked as a 3-way choice; the
author expressed no preference between them, so this is my recommendation, adopted by default —
flag it if this isn't the right call once it's visible.)

**Concretely:** a single **"Change this…"** entry point per pattern, which then asks a short,
plain-language follow-up — mirrors how "Create Bill…" already hides skippability and "when does this
stop" behind one flow, rather than exposing them as separate controls:

> **Change this…**
> · Starting on a date, going forward, permanently → **break off** ([16](16-stage3-break-off.md), B14)
> · Only the amount I'm putting toward it, bill unchanged → **Restructure the plan** ([17](17-stage4-allocation-only-changes.md), C7)
> · Stop it entirely from a date → **truncate** ([16](16-stage3-break-off.md), B15) or **stop contributing** ([17](17-stage4-allocation-only-changes.md), C9)

Three already-built mechanisms, one visible button. The wording above is a first pass, not final —
the exact phrasing of the follow-up question is part of the remaining design work below.

### 2 — The cushion-not-whole wording + symbol (Item C's deferred half)

**Ruling: "Dipped into your safety cushion," paired with a distinct symbol from "short"'s ⚠ — a
half-filled circle (◑).** Reuses "safety cushion," already established vocabulary on the Accounts
tab — no new term. Satisfies [11](11-ui-design-and-decisions.md)'s standing rule that meaning is
never color-only: ◑ is a different *shape*, not just a different color, from ⚠.

---

## The "concerning states" inventory (item 19) — draft, not final

Compiled from everything every earlier stage deferred here rather than answer piecemeal. **The
charter is explicit the author wants more than this list** — treat it as a draft. Each state's lever
(if any) and exact wording are the remaining design work.

| # | State | First flagged | Proposed severity tier* | Lever |
|---|---|---|---|---|
| 1 | The safety cushion isn't whole | [14, Item C](14-stage1-allocation-model.md#item-c--what-thin-means--settled-2026-07-23) | Informational (settled — this is the tier itself) | **None — settled 2026-07-30.** The refill is already fully automatic (the one remaining system-authored positive event with no user confirmation); the only conceivable lever, "adjust your cushion target," would read as nudging the user toward accepting less safety at the exact moment they're told their cushion is thin. Purely informational, nothing to click. |
| 2 | A goal is over-funded / ahead of schedule | items 22/24, [17](17-stage4-allocation-only-changes.md) | Informational (proposed) | **"Stop contributing" — already settled and built via C10** ([18](18-stage5-action-catalog-audit.md#c10--resolved-an-active-nudge-once-over-funded-crosses-a-threshold)): an active nudge once `GoalShortfall.OverfundedAmount` crosses a threshold (proposed 10% of `AmountNeeded`, unconfirmed), with `RestructureFactory.StopContributing` (item 22) right there. |
| 3 | A plan didn't re-pace after its bill moved accounts | B10, F33 ([18](18-stage5-action-catalog-audit.md)) | **N/A — not surfaced at all, settled 2026-07-30** | **None — no detection built either.** Author's ruling: the user should notice a change this drastic themselves, and already has the tools (`RestructureFactory`, "Restructure the plan") to fix it. Real detection is deferred entirely to the future actuals-based mismatch warning ([12](12-actual-transactions-deferred-design.md#forecast-tab-ui) — "a bill... that came in at an unexpectedly different cost"), not built here. |
| 4 | A plan didn't re-pace after a paycheck break-off | item 4-E ([16](16-stage3-break-off.md)) | **N/A — not surfaced at all, settled 2026-07-30** | **None — same ruling as #3, same day.** Trust the user; they're in control; existing tools (break-off, restructure) are enough. Same deferral to [12](12-actual-transactions-deferred-design.md#forecast-tab-ui)'s future warning class. |
| 5 | Whether ending a pattern names the freed amount | item 16's legibility question ([16](16-stage3-break-off.md)) | Informational (settled — yes, name it, 2026-07-30) | **Name the freed amount, inheriting item 18's already-built precedent** — truncate (B15) and break-off (B14) confirm screens should say how much frees up, same as delete already does. **Wording ruling (2026-07-30):** never "you'll get $X back" — the money was always the user's, just reserved; "back" wrongly implies otherwise. Reuse item 18's actual phrasing, "...will free up $X currently set aside" (`MainWindow.xaml.cs:1162`), which already gets this right. Elevated to a [standing UI principle](11-ui-design-and-decisions.md#standing-ui-principles-distilled-from-the-above). |
| 6 | A tentative best-guess date needs firming up | item 23 ([17](17-stage4-allocation-only-changes.md)) | **N/A — not surfaced at all, settled 2026-07-30** | **None — same trust-the-user ruling as #3/#4.** No point adding a new "this date is tentative" marker (the domain has no such concept today — `Start`/`Until` are plain dates) just to power a reminder. The user typed the guess in themselves; correcting it later is already a plain edit. |
| 7a | Dormancy family, part 1 — **unskippable-by-default** and **no cushion set** | **F12** (entries 1–2), [14](14-stage1-allocation-model.md) | N/A — feature-discovery, not a financial-state warning (settled 2026-07-30) | **Passive, reusing existing surfaces — settled 2026-07-30.** No new nudge mechanism: better in-context copy on what's already there, plus an active shortcut riding on an *already-firing* warning where one exists. See below. |
| 7b | Dormancy family, part 2 — the easy-to-miss **"materialize a plan" button**, its **absence from create forms** | **F12** (entries 3–4), [14](14-stage1-allocation-model.md) | N/A | **Deferred to a future dedicated UI stage — settled 2026-07-30.** Not part of this pass; #4 (absence from create forms) may get a brief mention there rather than full treatment. |
| 8 | A **mandatory** bill's next occurrence is due within a week and the repeated earmarks already in place won't cover it in time | raised while grounding [26](26-editing-an-earmark-pattern.md), 2026-08-15 | Short (needs action) — real risk of money genuinely missing on a due date | **Not designed — flagged, not built.** Two layers, not yet disentangled: (1) whether the domain-layer auto-reserve mechanism `EarMarkEvent.cs`'s own comment describes ("POSITIVE implicit events to auto-reserve mandatory bills ahead of their due dates... endorsed by the design's author, 2026-07-09/10") is still live — traced through the current cascade and found no call site creates one anywhere in `MyMoneyForecast.Domain` today; reads as retired by the Allocation-Plan-model rewrite, but that's not confirmed by the author, just flagged. (2) At minimum, per the author (2026-08-15): forecast-tab detection of this exact condition, plus a shortcut button so the user can correct it and have "expected free amount" read accurately — the near-term ask regardless of how (1) resolves. Author's own words if this is genuinely missing: "that's a problem." Not related to planning/26's own deferring-funding work — surfaced there only in passing while grounding a different question. |
| 9 | A goal/bill's savings plan is projected to fall short (`PlanHealthCategory.WillMiss`/`AlreadyMissing`) — has NO lever at all today, unlike its own over-funded mirror (state #2) | state itself: [22](22-plan-health-state.md); the lever gap, and the mechanism-C priority rule below, raised while grounding [26](26-editing-an-earmark-pattern.md), 2026-08-15 | Short (needs action) — this is literally the tier's own definition | **Not designed — flagged, not built. Checked, not assumed: `PlanHealthMessages.cs` (`SummaryFutureLine`/`CurrentJarStateLine`/`FirstOccurrenceShortfallLine`) is purely informational text today — grepped the App project for any existing suggested-fix/shortcut wiring for a shortfall and found none. Every lever this row could offer (raise the allocation rate, add a manual top-up earmark, restore a skipped date) would be entirely new.** The author's own explicit ordering rule for whenever this DOES get built (2026-08-15, prompted directly by mechanism C now existing — [26](26-editing-an-earmark-pattern.md)'s skip-dates control): **don't default straight to "raise your rate" or "add a manual earmark."** First check whether any `EarMarkPattern` funding this goal has `ExcludedDates` that could plausibly explain the shortfall. Precisely, three conditions, all required: (1) the plan has skipped dates at all; (2) no OTHER glut (`FundJar.HasGlut`) is already covering for the skip — i.e., the skip is genuinely contributing to this real shortfall, not harmlessly absorbed elsewhere, so restoring it would actually be the fix, not a no-op; (3) restoring those skipped dates would cover the shortfall on its own. When all three hold, the suggested fix should be **"bring those skipped contributions back into play"** — not asking for new money the user never agreed to when they can just resume what they already had, deliberately, paused. **Nothing here exists yet, on either surface the author named (the Earmark form's own Summary aside, or the Forecast tab — 7a's own "levers only added value where the app is already saying something" principle applies equally here) or in the domain layer** — no helper today can even answer "would restoring this pattern's skipped dates cover a given shortfall," let alone act on it; `ExcludedDates` (`RecurrenceRule.cs`) is not read by `GoalShortfall`, `PlanHealthState`, or anything else outside `RecurrenceRule` itself (confirmed by grep, 2026-08-15). |

*Per the severity ladder [Item C](14-stage1-allocation-model.md#item-c--what-thin-means--settled-2026-07-23)
already established: **short** (needs action) sits above **cushion-not-whole**/**informational**
(worth knowing, nothing wrong). States 2–6 are proposed at the informational tier by default — none
of them represent money actually missing — pending confirmation once each one's actual screen
presence is designed.

### 7a in detail — passive by default, active only as a shortcut riding on an existing warning

**General principle, settled 2026-07-30 (elevated to a [standing UI principle](11-ui-design-and-decisions.md#standing-ui-principles-distilled-from-the-above)):**
discoverability is passive most of the time — the app makes it *possible* to find a dormant feature,
it doesn't interrupt to announce one. Active surfacing is reserved for when the app is *already*
showing a warning for an unrelated reason — that's a free, on-topic moment to also offer a shortcut,
not a reason to invent a new proactive trigger.

Concretely, for F12's first two entries:

- **Unskippable-by-default:** *Passive* — the skippable/unskippable radio (already built per B8) needs
  to be reachable from "Change this…" on an existing pattern, not just buried in the advanced create
  form, with a short explanatory line next to it ("Skippable bills are the first to be delayed if
  money gets tight"). *Active shortcut:* the moment deallocation actually drains a jar is an
  already-firing event — that day's detail is where a "nothing is marked skippable yet — review your
  bills?" shortcut would ride along, not a new standalone nudge.
- **No cushion set:** *Passive* — `AccountWindow`'s existing `IdealSafetyCushion` field gets
  explanatory copy ("money always kept aside as a buffer — 0 means none is tracked"), nothing new
  built. *Active shortcut:* the existing red "Free Balance" warning cell for a negative-balance day
  (`PatternRows.cs:65`) is the natural place to ride a "a safety cushion would catch this earlier —
  set one?" shortcut, since that's exactly the situation a cushion exists to buffer.

Both reuse UI surfaces that already exist — no new mechanism, no new domain state, matching how
lightly #1–#6 above resolved.

## Not yet designed

- **Item 20's shortcut inventory** — which specific actions (beyond what already exists) earn a
  "Create X…"-shaped shortcut on a non-Forecast tab, now that the consolidation rule is set.
- **The "Change this…" follow-up question's exact wording** — sketched above, not finalized.
- **7b (F12's entries 3–4)** — deferred whole-cloth to a future dedicated UI stage, not this pass.
- **State #8** — the mandatory-bill-coming-due-underfunded gap above. Whether the domain-layer
  auto-reserve mechanism needs rebuilding at all is itself unresolved, let alone the forecast-tab
  detection/shortcut-button lever on top of it.
- **State #9** — a goal-shortfall lever doesn't exist at all yet (unlike #2's own built over-funded
  mirror); when it gets built, it has to check for restorable skipped dates (mechanism C) before ever
  suggesting more money.

Seven of the nine states in the inventory above are resolved or explicitly deferred — item 19's
per-state work is done pending the two open items above, plus #8 and #9, both added 2026-08-15 and
both still fully open.
