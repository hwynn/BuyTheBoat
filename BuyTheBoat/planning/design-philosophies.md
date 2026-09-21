# Design Philosophies

The standing principles that govern **how we design the redesign** — the counterpart to the four core user questions in [04-project-goals-and-user-questions.md](04-project-goals-and-user-questions.md). Where the core questions say *what* the app must answer, these say *how* it should behave while answering them.

These are forward-looking and apply to every implementation equally. They are deliberately **not** part of the `00-06` reconstruction set: those docs record what the *original* design said, whereas these govern where we follow it, extend it, or overrule it — which is philosophy 3's whole subject.

*Author-stated 2026-07-21. The author's own read at that point: we're already largely meeting these; #2 and #3 just haven't been exercised much yet, because the UI and cross-account features aren't fleshed out.*

*Philosophies 4–7 added 2026-07-30, at the start of the dedicated UI-implementation pass following the
["Adjusting the Plan" phase](13-adjusting-the-plan-charter.md). Full elaboration —
form layout, the two shortcut-transparency mechanics, and everything still open — lives in
[planning/21](21-forms-and-ui.md); these entries state the principle only,
matching 1–3's own level of detail.*

---

## Philosophy 1 — Inform and equip the user; don't automate away their agency

The tool is not a magic wand that resolves a budget on its own. We assume the user knows what they want and can work things out. Our job is to **present the information and the tools** that make doing what they want easier — not to make their decisions for them.

Concretely: **prefer giving the user a button or an explicit choice over doing something automatically**, most of the time. The one firm requirement on any such control is that **its effect must be legible** — the user has to be able to tell what pressing it will do, so they can judge whether it helps them.

This is already why:

- **Explicit (manual) earmarks exist** — so a user can exert manual control over their funds instead of the program doing everything for them (built — see `ManualEarmark` / `EarmarkFormPanel`'s one-off mode).
- **We surface a shortfall and offer an easy correction** ("you'll be short for X — here's how to fix it") rather than silently auto-correcting it.

**Practical test:** *Does this hand the user a clear lever, or does it quietly decide for them?* Favor the lever. Automatic behavior needs a stronger justification than a manual control does — and where it exists, it should still be visible and overridable.

## Philosophy 2 — Speak the user's language; show what helps, not every value

The user does not know our classes or our vocabulary. The class names are human-readable **for the developer's benefit**, not the user's. A user knows what an **account** is and knows they have **bills**; they do not know what a "fund jar" or an "earmark pattern" is, or how many earmark patterns live inside an account.

Keeping the user informed does **not** mean surfacing the current value of every property. *What* to show, *when* to show it, and in *what format* are real design choices to be made deliberately — not defaulted to "expose the model."

**Practical test:** *Would a person who has never seen our class diagram understand this screen?* If a label only makes sense to someone who has read the code, it is wrong.

## Philosophy 3 — The old documentation informs the design; it does not dictate it

The assumptions and class documentation are authoritative for **general organization and the limits of the program's capabilities**. They are **not** authoritative over the design as a whole, and especially not over the user experience. With occasional higher-level abstractions we can help users in situations the original documentation never spelled out.

Two distinct moves this licenses — **both requiring a planning pass before they are finalized:**

### (a) Build features by abstracting over the original tools

When the original design has no direct mechanism for something the user needs, compose one out of the pieces it does have, and present it to the user as a single coherent thing.

*Worked example — cross-account transfers (this settles our open "how do we model transfers" question):* there is **no** "transfer" property or method anywhere in the original design. We build it as a **paired expected transaction** — a withdrawal from account A and, at the same moment, a deposit of the same amount into account B. The user is **not** shown two separate bill/paycheck entries; they see and schedule one "transfer" (whatever user-facing name we settle on). This is philosophy 2 in action: it matters more that the user can easily schedule and *see* a transfer than that the underlying two-legged mechanism is exposed. Being able to view every earmark pattern separately is *less* important than a clean transfer experience.

### (b) Break or change old assumptions outright when they hurt the user

Some original assumptions are discardable.

*Worked example:* one old assumption held that a bill's milestone amount is always the negative of the bill's total — i.e. the instant a bill is paid, the user is expected to already have the full *next* occurrence saved, even if paychecks are scheduled in between. That is overly cautious and annoying, and it can go. (The redesign already leans the other way: the bill-accrual A/B ramp reserves toward the next occurrence *between* paychecks rather than demanding it all up front — see the `positive-implicit` divergence tag in the code (`grep -rn DIVERGENCE src/`). That ramp is this philosophy already in effect.)

The freedom is real but not free: **creative abstractions and assumption changes both require planning before they are locked in.** "The docs don't forbid it" is not a design; "here is the abstraction, here is what it costs, here is how the user sees it" is.

**Practical test:** *Is this the best experience for the user, or just the most literal reading of the 2021 docs?* When those diverge, the user wins — after we have thought it through.

## Philosophy 4 — Put the information where the decision gets made

Wherever the user is about to make a choice, whatever that choice depends on should already be on the
screen in front of them — not one tab over, not something they have to go recall.

We already meet this once: the recurrence-rule editor shows a live preview of the actual occurrences a
rule will produce, right where the user is setting it. That is the bar — *what does someone need to
know, right here, to make this specific choice well* — for every other action, not just that one.
[Planning/21](21-forms-and-ui.md) works through cases this hasn't been
built for yet (what a manual earmark's date needs to show; where a "funds are thin" warning's own
shortcut would even go).

**Practical test:** *If the user paused here and asked "what do I need to know before deciding?", is
the answer already visible, or would they have to go find it?*

## Philosophy 5 — Every form keeps a standing door open, not just a shortcut's

To keep the user in control, almost every form should be reachable on its own, standing tab — not only
through a contextual shortcut acting as its one narrow window in. Shortcuts (philosophy 6) are for
convenience; they must never be the *only* way in.

Concretely: the create/edit forms for the program's core things each get their own always-selectable
tab, rather than existing only as dialogs summoned from somewhere specific.

**Practical test:** *If every contextual shortcut into this form vanished tomorrow, could the user
still get here on their own?*

## Philosophy 6 — A shortcut should show its own work

A rich set of contextual shortcuts (philosophy 5's own stated exception) risks feeling like a maze —
convenient in the moment, opaque about how the user got there or how they'd get there again unaided.
Every shortcut should make its own path legible: when it lands the user somewhere, something on screen
should show *this is how we reached this point*, so it reads as a fast version of a path the user
could always walk by hand, not a separate hidden one.

**Practical test:** *After using a shortcut, could the user explain how they'd have reached the same
place themselves?*

## Philosophy 7 — A handful of reusable forms, not one per action

A small, fixed set of form *kinds* — one per core thing the user manages, each doing double duty for
both creating a new one and editing an existing one — rather than the program accumulating a new
one-off dialog for every individual action.

**Practical test:** *Before building a new form, could one of the existing few do this job instead?*
