# 20 — UI phase: leftover open questions

This file began as the UI-phase raw inventory (every existing control; every possible user action;
a candidate clustering). That inventory is removed as redundant — the running app and
[13b](13b-user-action-catalog.md) are the live record of what exists, and
[21](21-form-architecture.md) carried the clustering forward into the real form work. What remains
is the handful of open UI questions that inventory surfaced and that nothing else owns yet.

## Open questions worth a second look

The Forecast tab's top-controls balance/cushion summary collapses every account into one number, a
shape that predates multi-account support and may hide exactly the kind of per-account trouble the
Accounts tab now shows plainly (it already has its own Balance and Safety cushion columns, per
account — the summary may now be pure duplication, not a simplification). Others in the same spirit:

- **The cushion half of that summary is arguably worse than the balance half.** Item C's whole
  "cushion not whole" state ([14](14-stage1-allocation-model.md)) is inherently
  per-account — one account's cushion can be thin while another's is fully topped up. A single
  summed cushion figure can read "fine" while one specific account is exactly the state Stage 1
  built a warning for. The strongest case for breaking the summary apart, not just moving it.
- **Cluster E's "active shortcut" tension may now have an obvious resolution.** The two Stage 6
  ideas (a deallocation-drain event suggesting "mark this skippable?"; a negative-free-balance day
  suggesting "set a cushion?") were flagged as maybe needing to live *inside* the locked
  selected-day/overview panels. With the top controls confirmed open, they have a home that doesn't
  touch the lock at all — a small status/nudge area in the toolbar.
- **E3, "Cover $X from another account →"** is inherently a multi-account-era concept — it didn't
  exist before there was more than one account to cover from. Worth asking whether it belongs in the
  top controls (surfaced globally, account-aware) rather than wherever it was originally sketched.
- **The Accounts tab's own grid** has the reverse question: now that per-account Balance and Safety
  cushion are already columns there, does that tab want a small per-account status indicator (⚠/◑,
  item C's own symbols) alongside them, rather than those symbols only appearing inside the locked
  selected-day view?

## Genuinely unaudited — decide before folding into any UI work

- **E3** ("Cover from another account") and **E9** (transfer-plan restructuring) have never been
  audited as user actions — worth a deliberate decision rather than being folded in silently.
