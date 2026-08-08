# 06 — Deallocation math (from `DeallocationProof.ods`)

Reconstructed 2026-07-10 from `DeallocationProof.ods` (sheets `explanation`,
`DeallocationPieces`; test vectors in `DeallTest`). **Status, 2026-08-08: the
engine this documents is fully built** — see
[07-deallocation-implementation-plan.md](07-deallocation-implementation-plan.md)
for the staged implementation (Steps 1-3, all done). This doc remains the
authoritative math reference: the formulas, symbols, and goal invariants below
are the test oracles the implementation was built and verified against, not a
to-do list. Verbatim formulas are quoted from `DeallocationPieces`; prose
examples are reproduced from `explanation`.

> **Standalone-terms warning (from the sheet itself):** "Some of the terms
> used in the context of this problem are used differently [than] they are in
> other parts of our documentation. Consider this documentation standalone
> notes for a math problem that will be solved by a function in the larger
> program." So `current funds`, `free funds`, etc. below are scoped to this
> math problem.

Open questions for the author are collected at the bottom and also flagged
inline as **[Q1]**…**[Q5]**.

## The problem

Money is divided into **fund jars** (savings for a boat, a vacation, upcoming
bills); whatever current funds aren't in a jar are **free funds**. On most
days a big expense comes out of free funds. But sometimes an expense is bigger
than free funds — and because the jars are just bookkeeping over one real
account balance (not separate accounts), the jars would keep claiming
yesterday's totals even though the money isn't there anymore.

A day where that happens is a **deallocation day**. To fix it we must pull
money back out of jars — and the only way to move jar money is with an
**earmark** — so the whole job is: *compute the amounts of the (negative)
earmarks that rebalance the jars down to what we actually have.*

Rule of thumb (from the sheet): all earmarks made on a deallocation day must
total the amount that has to be withdrawn from jars. (The debt case is an
exception — see **[Q2]**.)

### When is it a deallocation day?

Using the symbols below, the day needs deallocation when:

```
c – Σfx + Σapx + au < 0
```

i.e. *(free funds from yesterday) + (all of today's transactions) < 0* —
spending outran free funds. Equivalently `c – Σfx < –Σapx – au`.

## Symbols (from `DeallocationPieces`, row 1 header row)

Financial ids are **ordered by increasing priority: index 1 = lowest
priority, index n = highest.** On a deallocation day we drain the least
important jars first.

Notation convention (per the author): a trailing **1/2/3** = a specific
instance (`er1`, `er2` are two different repeated earmarks); **n** = the last
instance; **x** = "any one instance." When the same `x` repeats across
symbols in one formula it's the *same* index (`apx = erx` means `ap1 = er1`,
`ap2 = er2`, …). **Σ** = sum over all instances (`Σfx = f1 + f2 + … + fn`; the
`x` after Σ is a dummy, not a specific instance).

| Symbol | Property | Known at start? | Sign meaning |
|---|---|---|---|
| `er` (er1…ern) | repeated earmark, `expected_amount` | yes | + fund gets money · − money taken out |
| `ei` (ei1…ein) | isolated earmark, `expected_amount` | yes | + / − same |
| `f` (f1…fn) | fund jar `current_amount` (from previous day), `0 ≤ fx` | yes | + money allocated to this fund |
| `c` | current funds = `full_amount` (previous day), `0 ≤ c` | yes | + money in the account |
| `ap` (ap1…apn) | **actual paired** transaction amount | yes | + goal was to add to this fund · − we bought what we saved for |
| `au` | **actual unpaired** transaction amount (single total) | yes | + net money added · − net money withdrawn |
| `p` (p1…pn) | **paired earmark** `expected_amount` (Step A output) | **no** (computed) | +/− fund given/taken by our goal |
| `b` (b1…bn) | **balancing earmark** `expected_amount` (Step B output) | **no** (computed) | +/− |
| `Na` | needs reallocation *after* step A | no | (defined `Na = Σpx ???` — see **[Q4]**) |
| `Fa` (Fa1…Fan) | jar remaining *after* step A, `Fax = fx + px`, `0 ≤ Fax` | no | |
| `Nb` (Nb1…Nbn+1) | needs reallocation, per Step-B step; `Nbn+1 = 0` | no | − money still to pull from jars |
| `Fb` (Fb1…Fbn) | jar remaining *after* step B, `Fbx = Fax + bx`, `0 ≤ Fbx` | no | |

Baseline assumptions the sheet states: `c ≥ Σfx` (can't have more allocated
than we hold); free funds yesterday `= c – Σfx`.

## Two steps

- **Step A — paired earmarks (`p`).** For each transaction that a specific jar
  was saving for (a *paired* transaction, e.g. we bought the boat), pull its
  cost out of that jar. If the jar can't cover it, take all the jar has; the
  remainder rolls into Step B.
- **Step B — balancing earmarks (`b`).** Cover everything left — the unpaired
  spending, the leftovers from Step A, and compensation for existing scheduled
  earmarks — by draining jars in priority order (lowest first) until nothing
  more is needed.

### Step A formulas

```
px  = Max((0 – fx), apx)          # take min(jar balance, cost) toward the goal
Fax = fx + px                     # what's left in that jar after Step A
```

`Max((0 – fx), apx)`: both `(0 – fx) = –fx` (empty the whole jar) and `apx`
(the transaction) are ≤ 0; `Max` picks the smaller magnitude, so you take the
whole jar only when the cost exceeds it. Jars with no paired transaction have
`apx = 0 ⇒ px = 0` (untouched by Step A). **[Q3]**

### Step B formulas

```
Nb1     = c + au + Σapx – ΣFax   =   c + au + Σapx – (Σfx + Σpx)   # total still to pull
bx      = Max((0 – Fax), Nbx) – (erx + eix)      # balancing earmark for jar x
Nb(x+1) = Nbx – bx – (erx + eix)                 # remaining need after jar x
Nbn+1   = 0     (if  –Σapx – au ≤ c)             # terminates; else the debt case
```

Read `Max((0 – Fax), Nbx)` as "pull the lesser of (the whole jar) and (what's
still needed)." The balancing earmark `bx` is that withdrawal **minus the
existing earmarks already scheduled for that jar** (`erx + eix`) — the new
earmark only has to make up the difference, because the existing ones already
move money.

**Step B iterates over EVERY jar in priority order — it does not stop when
`Nb` reaches 0** (confirmed by the author, 2026-07-10; resolves Q1 below).
Once the need is met, `Max((0 – Fax), Nbx)` evaluates to 0, so each remaining
jar gets `bx = 0 – (erx + eix) = –(erx + eix)`: a balancing earmark that
**cancels any scheduled earmark that jar still carries** (a planned
contribution we can no longer afford on a deallocation day), and is a no-op
(`bx = 0`) for jars with no scheduled earmark. The worked examples only
*look* like they stop early because their trailing jars had nothing scheduled.
**[Q5]** (the general `M`-minimum, and the safety cushion) still lives here.

### End goals (invariants — good as test assertions)

These are the authoritative statements from the `DeallTest` sheet (they're
slightly more complete than `DeallocationPieces` — notably Goal 1 carries a
`+ Nbn+1` term that the other sheet dropped, which is what makes it hold in
the debt case too):

```
Goal 1 — "All earmarks on this day must have a total amount equal to the total
          amount we need to withdraw from fund jars (not including debt)":
    Σerx + Σeix + Σpx + Σbx + Nbn+1 = c – Σfx + Σapx + au

Goal 2.1 — "If we are not in debt, the amount left to deallocate at the end
            should be 0":
    if  –Σapx – au ≤ c   then   Nbn+1 = 0

Goal 2.2 — "If we are in debt, the amount we cannot deallocate at the end
            should be the current funds plus all actual transactions":
    if  –Σapx – au > c    then   Nbn+1 = c + Σapx + au

Goal 3.x — "For each fund jar, the amount remaining should be the initial
            amount plus all the earmarks":
    fx + erx + eix + px + bx = Fbx
```

Goal 1 counts **every** earmark on the day — the pre-existing scheduled ones
(`er`, `ei`) *and* the new deallocation ones (`p`, `b`) — plus whatever
couldn't be deallocated (`Nbn+1`, nonzero only in debt). Goal 2.2 pins the
debt case exactly: the un-deallocatable remainder equals `c + Σapx + au`,
i.e. how far the real balance goes negative.

## Worked example 1 — simple (no paired transactions)

Start: `c = 200`; jars `f = [50, 0, 120, 10]`; free `= 20`; one unpaired
transaction `au = –100`. Free funds (20) can't absorb the whole 100, so
**80 must come out of jars**. No existing earmarks (`er = ei = 0`). Step A does
nothing (no paired transactions), so Step B runs on the original jars.

Step B drains jars in priority order (jar 1 first), tracking *still needed*
counting up toward 0 and accumulating the earmark list:

| | start | → after jar 1 | → after jar 2 | → after jar 3 (finished) |
|---|---|---|---|---|
| still needed | −80 | −30 | −30 | 0 |
| earmarks made | [] | [(1, −50)] | [(1, −50)] | [(1, −50), (3, −30)] |
| jar 1 | 50 | 0 | 0 | 0 |
| jar 2 | 0 | 0 | 0 | 0 |
| jar 3 | 120 | 120 | 120 | 90 |
| jar 4 | 10 | 10 | 10 | 10 |

Jar 1 gives all 50; jar 2 is empty (skipped); jar 3 gives the last 30. Final
jars total `0+0+90+10 = 100 = c + au = 200 − 100`. ✓

## Worked example 2 — complex (a paired transaction)

Big spending day *and* we bought the boat (jar 4) we were saving for — a bit
pricier than expected. Start: `c = 700`; jars `f = [50, 0, 120, 500]`; free
`= 30`; unpaired `au = –100`; **paired** transaction on jar 4 `ap4 = –550`.

**Step A** (pull the boat's cost from jar 4; `p4 = Max(–500, –550) = –500`, so
the whole jar goes and 50 of the cost is left over):

| | start | → after Step A |
|---|---|---|
| needed paired 4 | −550 | −50 (jar 4 only had 500) |
| other needed | −70 | −70 (the −100 unpaired, less 30 free) |
| earmarks made | [] | [(4, −500)] |
| jar 4 | 500 | 0 |

**Step B** (`Nb1 = c + au + Σapx − (Σfx + Σpx) = 700 − 100 − 550 − (670 − 500)
= −120` — the leftover 50 from the boat plus the 70 unpaired):

| | start | → after jar 1 | → after jar 2 | → after jar 3 (finished) |
|---|---|---|---|---|
| still needed | −120 | −70 | −70 | 0 |
| earmarks made | [(4,−500)] | [(4,−500), (1,−50)] | [(4,−500), (1,−50)] | [(4,−500), (1,−50), (3,−70)] |
| jar 1 | 50 | 0 | 0 | 0 |
| jar 2 | 0 | 0 | 0 | 0 |
| jar 3 | 120 | 120 | 120 | 50 |
| jar 4 | 0 | 0 | 0 | 0 |

Final jars total `0+0+50+0 = 50 = c + au + ap4 = 700 − 100 − 550`. ✓

## General deallocation formula (Step B, one jar, abstracted)

The sheet restates one jar-step in neutral notation (**different letters** from
above): `N` needed overall, `A` available in this jar, `M` minimum the jar may
go to, `W` actually withdrawn, `R` remaining, `S` still needed after.

```
W = Max((M – A), N)         # the core rule
R = A + W
S = N – W
N = W + S
Min(M, R) = M               # remaining never goes below the minimum
```

`M` is the **floor a jar's balance may not drop below** — the line
`Min(M, R) = M` says the remaining `R` is never less than `M`. Because fund
jars can't go negative, **`M = 0` in every case here** (confirmed by the
author, 2026-07-10): `W = Max((0 – A), N)` empties a jar to 0 but never past
it. The "regardless of sign of N and M" generality is unused by deallocation —
it's there only so the same helper could be reused for the filling direction,
or a future "don't drain below X" floor, neither of which exists. Mapping to
Step B: `A = Fax`, `M = 0`, `N = Nbx`, `W = Max((0–Fax), Nbx)`, `S = Nb(x+1)`,
and the emitted earmark `bx = W – (erx + eix)`.

## Question resolutions (author, 2026-07-10)

- **[Q2 — RESOLVED] The debt case.** When spending exceeds *current* funds
  (`–Σapx – au > c`), money that isn't there can't be allocated: every fund
  jar drains to 0 (via implicit negative earmarks) and the free balance goes
  negative. Goal 2.2 quantifies exactly how negative: `Nbn+1 = c + Σapx + au`.
- **[Q3 — RESOLVED] `apx = 0` for a jar with no paired transaction**, so
  `px = 0` and Step A only touches paired jars. Human-readable: a paired
  transaction means *you paid the bill this jar was saving for, so its
  set-aside money is genuinely spent and released*; if unpaired, the bill
  hasn't come in, the money stays set aside, and Step A leaves the jar alone.
- **[Q4 — RESOLVED] `Na = Σpx ???`** was uncertain/unfinished scratch — ignore
  it. The formulas use `Nb1` directly.
- **[Q5 — RESOLVED]** the `M` minimum is the non-negativity floor, always `0`
  (see the General Formula section). The safety cushion is always the
  lowest-priority jar — see the dedicated section below.

### [Q1 — RESOLVED: Intention 2] Existing earmarks on not-yet-drained jars ARE cancelled

Confirmed by the author (2026-07-10): "Negative implicit earmark events are how
we have to balance things out even if we're adding them to balance our other
existing earmark events on that day." Step B walks all jars; scheduled earmarks
we can no longer afford get cancelled by a balancing earmark. Tables kept below
as the rationale.

Goal 1 counts every earmark on the day (`er + ei + p + b`), so a scheduled
allocation we can no longer afford must be undone as part of deallocation. The
question is purely about the **loop**: in the worked examples Step B visibly
*stops* once "still needed" hits 0 — but that only works because the trailing
jars there had no scheduled earmarks. When a not-yet-drained jar *does* carry
a scheduled earmark, stopping early leaves it in place and over-allocates.

Setup for both tables below: yesterday `c = 100`, jars `f = [30, 40]` (jar 1 =
lowest priority), so free funds `= 30`. Today: a scheduled `+20` earmark on
jar 2 (`er2 = +20`, e.g. a planned vacation-fund contribution) and an unpaired
`–60` spend. It's a deallocation day (`100 – 70 – 60 = –30`); after today we
actually hold `c + au = 40`, so the jars must end totalling 40.

**Intention 1 — stop as soon as the spending gap is covered:**

| | start | → after jar 1 (need met, stop) |
|---|---|---|
| still needed | −30 | 0 |
| new earmarks | [] | [(1, −30)] |
| jar 1 | 30 | 0 |
| jar 2 | 40 | 40 → **+20 scheduled still applies → 60** |

Final jars `[0, 60] = 60`, but we hold 40 — **over-allocated by 20**. Goal 1
check: `20(er) + 0(p) + (−30)(b) + 0 = −10 ≠ −30`. **Violated.**

**Intention 2 — keep going through every jar; cancel what we can't afford:**

Jar 2's balancing earmark, once need is already met, is
`b2 = Max((0 − 40), 0) − 20 = −20` — exactly cancelling the scheduled `+20`.

| | start | → after jar 1 | → after jar 2 |
|---|---|---|---|
| still needed | −30 | 0 | 0 |
| new earmarks | [] | [(1, −30)] | [(1, −30), (2, −20)] |
| jar 1 | 30 | 0 | 0 |
| jar 2 | 40 | 40 | 40 + 20 − 20 = **40** |

Final jars `[0, 40] = 40` ✓. Goal 1 check: `20 + 0 + (−30 − 20) + 0 = −30` ✓.
The planned vacation contribution is simply called off for the day.

**My reading:** Intention 2 is the one that satisfies Goal 1 and keeps the
jars honest, and it falls out of the existing `b` formula for free *if Step B
iterates over every jar in priority order instead of stopping at zero* (past
that point each jar just gets `b = −(er + ei)`, cancelling its scheduled
earmark; jars with no scheduled earmark get `b = 0`, a no-op — which is why
the examples *looked* like they stopped). **Can you confirm that's the intent
— Step B processes all jars, cancelling any now-unaffordable scheduled
earmarks on the ones past the point where the need is met?**

## The safety cushion (resolved 2026-07-10)

The deallocation sheets never mention the safety cushion — it treats all jars
uniformly — so its behavior comes from the author directly:

- The cushion is a fund jar with `finance_id = None`, **always the
  lowest-priority jar (priority 0)** — below the minimum priority (1) a user
  can assign anything else. This is **not user-editable**; the cushion is
  always index 1 in the deallocation order, so it is **always drained first**,
  before any real goal. (This lines up with the glossary's "priority 0 …
  first in line to have funds pulled back out.")
- What the user *can* edit is the cushion's **amount** — including setting it
  to 0 or hiding it entirely via a setting.
- Its whole purpose is to **occupy free funds** so the reported "free funds"
  figure reflects what you're actually comfortable spending, rather than every
  last dollar. (It's a late-added idea and may still be rough.)
- No special floor: like any jar it drains to 0 (`M = 0`). It has no
  `milestone_amount`. It is auto-managed (filled/adjusted by the system, not by
  a user-scheduled earmark pattern) — the *filling* side is allocation-cascade
  work, out of scope for this deallocation doc.

Implementation note: today's code already puts a `finance_id = null` cushion
jar on every snapshot as a $0 placeholder (see planning/05). This phase gives
it a real amount and makes it the priority-0 participant in deallocation.

## Mined but set aside

`DeallTest` also holds concrete input vectors, mid-computation values, and
validity flags (`sensible allocation`, `Not Debt`, `suitable test`) plus the
goals evaluated numerically per random column — a ready-made source of test
cases for when the engine is built. Left for the implementation phase. (The
goals themselves, extracted above, were the important part.)
