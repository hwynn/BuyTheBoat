# Design Philosophies

The standing principles that govern **how we design the redesign** — the counterpart to the four core user questions in [04-project-goals-and-user-questions.md](04-project-goals-and-user-questions.md). Where the core questions say *what* the app must answer, these say *how* it should behave while answering them.

These are forward-looking and apply to every implementation equally. They are deliberately **not** part of the `00-06` reconstruction set: those docs record what the *original* design said, whereas these govern where we follow it, extend it, or overrule it — which is philosophy 3's whole subject.

*Author-stated 2026-07-21. The author's own read at that point: we're already largely meeting these; #2 and #3 just haven't been exercised much yet, because the UI and cross-account features aren't fleshed out.*

---

## Philosophy 1 — Inform and equip the user; don't automate away their agency

The tool is not a magic wand that resolves a budget on its own. We assume the user knows what they want and can work things out. Our job is to **present the information and the tools** that make doing what they want easier — not to make their decisions for them.

Concretely: **prefer giving the user a button or an explicit choice over doing something automatically**, most of the time. The one firm requirement on any such control is that **its effect must be legible** — the user has to be able to tell what pressing it will do, so they can judge whether it helps them.

This is already why:

- **Explicit (manual) earmarks exist** — so a user can exert manual control over their funds instead of the program doing everything for them (see [manual-earmarks planning](MyMoneyForecast/planning/09-manual-earmarks.md)).
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

*Worked example:* one old assumption held that a bill's milestone amount is always the negative of the bill's total — i.e. the instant a bill is paid, the user is expected to already have the full *next* occurrence saved, even if paychecks are scheduled in between. That is overly cautious and annoying, and it can go. (The redesign already leans the other way: the bill-accrual A/B ramp reserves toward the next occurrence *between* paychecks rather than demanding it all up front — see the `positive-implicit` divergence in [05-original-structure-restructure.md](MyMoneyForecast/planning/05-original-structure-restructure.md#divergence-registry). That ramp is this philosophy already in effect.)

The freedom is real but not free: **creative abstractions and assumption changes both require planning before they are locked in.** "The docs don't forbid it" is not a design; "here is the abstraction, here is what it costs, here is how the user sees it" is.

**Practical test:** *Is this the best experience for the user, or just the most literal reading of the 2021 docs?* When those diverge, the user wins — after we have thought it through.
