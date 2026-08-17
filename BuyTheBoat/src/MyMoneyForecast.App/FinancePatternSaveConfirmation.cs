using MyMoneyForecast.Domain;
using MyMoneyForecast.Persistence;

namespace MyMoneyForecast.App;

// Orchestrates everything that has to happen between a Save button being
// clicked on the Expense form and the FinancialPattern actually landing in
// storage — redesign/MyMoneyForecast/planning/25-editing-patterns-with-history.md's
// Items B through F. Pulled into its own class/file specifically so this
// project's simplest case (ExpenseFormPanel.Save, MainWindow's own
// ExpenseForm.PatternSaved callback) doesn't have to carry the
// confirmation-and-consequence logic this phase adds. (The TODO that used to
// live at both of those call sites was resolved 2026-08-12 — see the
// "Wiring" paragraph below — so there's nothing left to find there now.)
//
// STATUS (2026-08-17, supersedes the 2026-08-12 paragraph this replaced): a
// real, working confirmation exists — EditingHistoryConfirmationWindow,
// shown through the ConfirmImplicitChanges delegate — deliberately minimal
// (plain WPF controls, not the styled cards in
// planning/mockups/editing-history-confirmation-mockups.html) but no longer
// missing any of the Item B-G questions/warnings themselves: the Concerning
// popup (AskForSuggestions/ShowSuggestion), the plan-disambiguation picker
// (AskWhichEarmarkPatternToOpen/PickEarmarkPattern), and Item E's own named
// consequence (AlterPastConsequence) all went from TODO/no-op to real,
// tested mechanisms 2026-08-17. See this class's own bottom-of-header "TODO
// (2026-08-17)" section for what's STILL genuinely open, added the same day
// on direct author request to make sure nothing found got lost to low
// context before it could be written down. When ConfirmImplicitChanges
// isn't wired at all (most tests, and any host that hasn't connected a real
// popup), DefaultConfirmationAnswer supplies the same safest,
// least-destructive answers the old placeholder logic used to hardcode.
//
// PerformSave/PerformImplicitEarmarkChanges are real for: a break-off with 0
// or 1 existing EarMarkPattern (PerformSingleSuccessorBreakOff); a break-off
// that consolidates more than one, forced or chosen (PerformMultiPlanBreakOff,
// planning/25's Item F — needed a new BreakOffFactory.BreakOff overload,
// MultiPlanBreakOffRequest, since the single-plan one only ever takes one
// predecessor plan); and a single-plan retroactive correction's own EarMarkPattern
// narrowing (NarrowSurvivingPlanIfNeeded — needed a new domain method,
// PatternTruncation.StartOn, the front-boundary counterpart to the existing
// EndOn) — but ONLY when the corrected boundary lands on or after the as-of
// date; when it lands earlier (arguably Item E's more common real-world
// trigger — "this started earlier than today," not just earlier than the
// plan's own current start), it's also a deliberate no-op, since nothing
// here can correctly read a jar balance from before the as-of date (see
// NarrowSurvivingPlanIfNeeded's own note).
//
// New 2026-08-13: a multi-plan retroactive correction's own in-place
// consolidation (ConsolidateSurvivingPlansIfNeeded) is now real too — the
// last of Item F's four combinations (single/multi plan x break-off/
// retroactive-correction) to get a mechanism. Backed by a new domain method,
// EarmarkConsolidation.Consolidate: folds every surviving EarMarkPattern for
// a goal into one, spanning from the earliest surviving plan's own start
// through the goal's own end, sized so its contributions exactly cover what
// the goal will consume in that window net of what's already banked
// (StartingAllocation + ManualEarmarks — no day-by-day forecast read needed,
// unlike NarrowSurvivingPlanIfNeeded's own absorbedBalance, so this carries
// none of that method's own "can't read before the as-of date" limitation).
// Paces to a single clear income stream the same way AllocationPlanProposer's
// own paced shape does, author-derived 2026-08-13 (redesign/MyMoneyForecast/planning/25-editing-patterns-with-history.md,
// Item F's own "in place" case).
//
// New 2026-08-14: the retroactive-correction side's own amount-only scaling
// (ScaleSurvivingPlansIfNeeded) — Item F's smallest mechanism, planning/25's
// own "amount changed, no date shift" row. When the plans are kept separate
// rather than consolidated, and neither start_date nor the recurrence shape
// moved, every surviving plan's own Amount just scales by the same ratio the
// goal's own Amount changed by (EarmarkScaling.Scale) — no boundary work at
// all, since dates never move in this case, so it's an in-place update under
// each plan's own existing (FinanceId, Start), not a delete-and-recreate.
// StartingAllocation is deliberately never scaled — already-realized money,
// not an ongoing rate. Gated on _isAmountOnlyChange, not just
// UserChoseScalePatterns, so the mechanism can't fire outside the one case
// it was designed for.
//
// UPDATED 2026-08-17 (supersedes the "Still TODO" this replaced): keeping
// multiple plans separate on the BREAK-OFF side, and on the retroactive-
// correction side for a start_date change specifically, is STILL not
// designed or built — that part hasn't changed. What changed is what
// happens when a user reaches either combination: both used to silently
// persist nothing at all (the break-off one didn't even save the
// FinancialPattern itself), which turned out to be a real, silent-failure
// bug, not a safe placeholder — found and fixed 2026-08-17 by always
// consolidating in both cases instead (PerformImplicitEarmarkChanges' own
// comment has the mechanics). See this class's own bottom-of-header "TODO
// (2026-08-17)" section for the real feature this still wants to become.
//
// Wiring (2026-08-12): MainWindow's ExpenseForm.PatternSaved callback now
// actually constructs and runs this class, replacing the plain inline save
// it used to do — the TODO that named this class as its own eventual
// migration target is resolved. Two bugs surfaced only once real,
// post-Run() navigation was exercised for the first time (never caught by
// the existing tests, which all skip planning): AskWhichEarmarkPatternToOpen
// used to look under _financeId even after a break-off, when the
// successor's own EarMarkPattern is the current one (fixed via
// _navigationFinanceId); and it read through _requestForecast(), which
// MainWindow's own EnsureForecast caches until something explicitly
// recomputes it — so it could easily have seen stale, pre-save data — fixed
// by reading the repository directly instead, matching what the original
// inline callback already did there.
//
// Sequencing (2026-08-13): a third bug, found the same way — driving
// NarrowSurvivingPlanIfNeeded through the real, now-wired Run() instead of
// calling it directly. PerformSave writes the corrected goal before
// PerformImplicitEarmarkChanges gets a chance to narrow the plan to match
// it — a real, momentary inconsistency — and reading ManualEarmarks in that
// window (to find which ones the narrowing would orphan) validates every
// row against its CURRENT plan, which throws on the very rows being
// identified, no matter which order the writes happen in afterward: an
// orphaned row is by definition incompatible with whichever plan currently
// governs it, old or new. The fix isn't tolerating that window, it's not
// having one — DetermineNarrowingPlanIfApplicable now runs immediately
// after DetermineConditions, before the confirmation even fires, and reads
// ManualEarmarks while the goal is still what it was before this Run() call
// — the only point where that read is guaranteed safe. NarrowSurvivingPlanIfNeeded
// just executes the result (_narrowingPlan) later; it no longer reads
// anything itself. (This replaced an earlier fix, a validation-free
// ManualEarmarkRepository read, that tolerated the inconsistent window
// instead of removing it.)
//
// Back-boundary invariant (2026-08-13): a fourth bug, found via the user's
// own real, manual use of the app rather than a test — editing only a
// goal's own end date (Item A's own always-Trivial field) on a FinanceId
// with more than one EarMarkPattern left the database in a state where
// EarMarkPatternRepository.GetAll() (and so MainWindow's own startup
// RefreshGrids) threw every time afterward, since nothing here checked
// whether an existing EarMarkPattern's own Until still fit inside the
// FinancialPattern's newly-shortened one (3.11.2.a2) after a plain save.
// DetermineBackTruncationsIfApplicable/ApplyBackTruncationsIfNeeded fix this
// the same "read before anything is saved" way as the front-boundary fix
// above, but run UNCONDITIONALLY — this invariant has to hold after EVERY
// save, not just the Critical/multi-plan ones the rest of this class asks
// about. Reuses PatternTruncation.EndOn, the same domain primitive a plain
// stop/break-off already uses to keep a plan's own Until from exceeding its
// goal's; a plan left with nothing but a past-the-new-Until Start gets
// deleted outright instead of truncated, since EndOn can't shorten a plan to
// end before its own start.
//
// Tests (2026-08-12): the "no tests yet" restriction is lifted — see
// MyMoneyForecast.App.Tests/FinancePatternSaveConfirmationTests.cs, plus
// PatternTruncationTests/BreakOffFactoryTests in MyMoneyForecast.Domain.Tests
// for StartOn and the multi-plan BreakOff overload directly. Most [Fact]s
// here drive Run() itself with real repositories and a real forecast, never
// a private method directly — a scenario's own data decides which branch
// fires, covering: the single-plan break-off, a forced-consolidation
// multi-plan break-off, a Trivial-only edit (never breaks off), the
// no-past-occurrence carve-out (03's own 1.2.3.10.a5 exemption — also never
// breaks off), an outflow with no existing plan yet (still gets a fresh one
// from BreakOffFactory), income (no jar at all), and — UPDATED 2026-08-17 —
// the break-off-side keep-separate fallback (A_break_off_with_multiple_
// surviving_plans_kept_separate_falls_back_to_consolidating_rather_than_a_
// silent_no_op, renamed and rewritten from what used to pin the old silent
// no-op as correct), and — now that ConfirmImplicitChanges is real — the
// retroactive-correction path driven through Run() itself with a fake
// delegate answering ChooseAlterPast = true, proving the wiring, not just
// the mechanism. NarrowSurvivingPlanIfNeeded's own edge cases (the lead-in
// gap, the before-as-of-date gap) are still tested by calling
// DetermineConditions, DetermineNarrowingPlanIfApplicable, and
// NarrowSurvivingPlanIfNeeded directly rather than through Run() — all
// three internal specifically so those tests can reach them
// (MyMoneyForecast.App.csproj grants MyMoneyForecast.App.Tests
// InternalsVisibleTo for exactly this).
//
// TODO (2026-08-17) — written down on direct author request while wrapping
// up a long session, specifically so none of this gets lost to a fresh
// session's own missing context. Four separate items, not one:
//
// 1. CRASH RISK, not yet fixed: DetermineNarrowingPlanIfApplicable's own two
//    "can't safely narrow" branches (the ActiveFrom-lead-in case and the
//    before-AsOfDate case, both below) leave the goal saved with its own
//    new, earlier ActiveStart while the plan is left at its own old one —
//    an EarMarkPattern.Create validation violation waiting to throw on the
//    very next EarMarkPatternRepository.GetAll() (including the app's own
//    next startup). A warning (NarrowingLimitationWarning) tells the user
//    this now; nothing prevents the inconsistent state itself. No safe
//    fallback is obvious — absorbing a wrong balance was already,
//    deliberately, rejected. Spawned as its own background task
//    (task_ad0129c6, "Fix crash risk: narrowing-before-AsOfDate leaves an
//    invalid plan/goal pair") with a starting direction to investigate
//    (reconsider whether "apply everywhere" should even be offered in this
//    specific combination) — not yet acted on.
//
// 2. DESIGN GOAL, not yet built: the author has confirmed "we eventually
//    want to let the user keep unconsolidated earmark patterns" — meaning
//    the 2026-08-17 fallback that always consolidates on the break-off side
//    (and for a start_date-only retroactive correction) is a SAFETY
//    measure, not the intended final behavior. The real feature — each
//    surviving plan keeps its own identity through either path — is still
//    wanted and still not designed. See this file's own open design
//    questions in redesign/memory/project_next_phase.md for the specific,
//    still-unresolved shape of it (a simpler "truncate, leave the new
//    segment with no plan yet" option was floated but never confirmed).
//
// 3. ARCHITECTURE, not yet organized: this class's own author-acknowledged
//    assessment, verbatim — "the save confirmation system is probably a
//    mess, but we can fix that later." Nine-plus independent
//    trigger/answer/description fields (IsChangeCritical, ConsolidationNeeded,
//    TouchesChainBoundary, ChangeCanCascade, TrivialFieldsCanCascade,
//    PacedBillsCanCascade, and more) each wired through their own
//    Determine*/Build*/Describe* trio and their own slice of
//    ImplicitChangeConfirmationRequest/Answer, grown incrementally across
//    many sessions rather than designed as one system. Works, is tested, but
//    a future pass should reconsider whether there's a single, smaller
//    abstraction (one "confirmation row" concept, say) that all of these
//    could be expressed through instead of each getting its own bespoke
//    field trio. Not started.
//
// 4. SMALLER, already-tracked-elsewhere gaps, restated here for
//    discoverability: AskForSuggestions' own Concerning popup is
//    deliberately minimal, not the full strategy-picker
//    planning/25's own "Still open" section describes (specific strategies
//    never designed — pre-fill Suggested/Working-state values, one default
//    suggested set or none); ScaleCheckBox's own math isn't previewed ahead
//    of the choice, just applied after; PickEarmarkPatternToOpen falls back
//    to the first plan on cancel rather than aborting navigation.
//
// One instance is meant to be created per Save click (either button).
public sealed class FinancePatternSaveConfirmation
{
    private readonly int _financeId;
    private readonly FinancialPattern _proposedPattern;
    private readonly int _accountId;

    // Func<ForecastResult> mirrors the RequestForecast pattern
    // ExpenseFormPanel/EarmarkFormPanel already use elsewhere in this
    // project — the established way this codebase gets from "a live
    // forecast" to a TransactionLogBook (ForecastResult.Book) and to
    // PlanHealthState. Reads that TransactionLogBook itself can answer
    // (AllFinancialPatterns, EarMarkPatternsFor) go through this; the three
    // repositories below are only for what the book can't do — writing, and
    // reading a goal's own ManualEarmarks (author, 2026-08-11: TransactionLogBook
    // can't recover just the user-authored ones — isolated ManualEarmarks get
    // folded into a BalanceSnapshot's day-by-day EarmarkEvents during the
    // cascade, mixed in with system-generated isolated earmarks, so there's
    // nothing clean to read there).
    private readonly Func<ForecastResult> _requestForecast;
    private readonly FinancePatternRepositories _repositories;

    // planning/27's own EarMarkPattern-editing entry point (the other
    // constructor, below) — set instead of the FinancialPattern-editing
    // fields above, never both. Run() branches on this being non-null
    // before touching anything above it, so _proposedPattern/_financeId/
    // _accountId above just carry real, harmless values in this mode
    // (_proposedPattern doubles as "the goal" — see that constructor's own
    // comment) rather than needing a null-check added to every one of their
    // many existing, already-tested call sites.
    private readonly EarMarkPattern? _proposedPlan;

    // The (FinanceId, Start) this plan is actually saved under today, if it
    // already exists — distinct from _proposedPlan's own Start, since Start
    // itself is one of the two fields this whole mechanism can change.
    // Needed to find the saved row at all; EarMarkPattern's own persistence
    // key is the composite (FinanceId, StartDate).
    private readonly DateOnly _earmarkSavedStart;

    // Internal bookkeeping only, not one of the nine named properties — which
    // specific fields changed (used to build the confirmation popup's own
    // plain-language description) and whether Amount changed with neither
    // Start/ActiveFrom nor the recurrence shape also changing (the
    // proportional-scaling offer's own trigger). All four computed in
    // DetermineConditions, read when building the popup's request.
    private bool _startChanged;
    private bool _amountChanged;
    private bool _recurrenceShapeChanged;
    private bool _isAmountOnlyChange;

    // Which FinanceId the post-save navigation (AskWhichEarmarkPatternToOpen)
    // should actually look under. Defaults to _financeId (a plain edit or
    // retroactive correction never changes identity) — but a break-off
    // means _financeId's own EarMarkPattern is now the truncated,
    // no-longer-current predecessor; PerformSingleSuccessorBreakOff/
    // PerformMultiPlanBreakOff update this to the successor's own id once
    // they know it.
    private int _navigationFinanceId;

    // Everything Item E's narrowing needs to actually DO, worked out by
    // DetermineNarrowingPlanIfApplicable before anything is saved — null
    // means narrowing doesn't apply (most edits), isn't safe yet (the
    // lead-in/before-as-of-date gaps), or there's nothing to narrow at all.
    // Split into a plan (computed early, while _repositories.ManualEarmarks
    // is still safe to read) and an execution (NarrowSurvivingPlanIfNeeded,
    // run later): reading ManualEarmarks AFTER PerformSave has already
    // written the new goal — but before the plan has caught up to match it
    // — throws on the very rows being identified, since that read validates
    // every row against its CURRENT plan. Reading here, before the goal
    // changes at all, sidesteps the problem instead of working around it
    // (2026-08-13: this replaced an earlier fix, ManualEarmarkRepository.
    // GetDatesUnvalidated, that read around the validation instead of
    // avoiding the inconsistent window it existed to route around).
    private NarrowingPlan? _narrowingPlan;

    private sealed record NarrowingPlan(
        EarMarkPattern ExistingPlan,
        DateOnly NewActiveStart,
        decimal AbsorbedBalance,
        IReadOnlyList<DateOnly> OrphanedManualEarmarkDates);

    // planning/25's Item G: the candidate plan shapes to offer alongside
    // Propose's own default, worked out by DeterminePlanShapeCandidatesIfApplicable
    // before anything is saved — same "read before PerformSave writes
    // anything" reasoning as NarrowingPlan/ConsolidationPlan's own field
    // comments. Empty means there's nothing to choose between (most edits, a
    // break-off with no existing plan to draw an alternative shape from, or
    // a genuinely concurrent set of existing plans — F27's shape, e.g. two
    // household partners — which needs its own not-yet-built mechanism to
    // continue both plans in unison, and shouldn't also offer a shape choice
    // on top of that complexity, author 2026-08-14). A sequential chain of
    // existing plans (RestructureFactory) is no longer excluded just for
    // having more than one row — RestructureFactory.FindCurrentPlan tells
    // the two cases apart.
    private IReadOnlyList<PlanShapeCandidate> _planShapeCandidates = [];

    // A candidate's own Label is display text only (not read by anything in
    // this class) — the popup shows it, the user picks one, and whichever
    // EarMarkPattern comes back as ChosenPlanShape is matched back to its
    // own full ProposedAllocationPlan (StartingEarmark included) by
    // reference in PerformSingleSuccessorBreakOff, not reconstructed.
    public sealed record PlanShapeCandidate(string Label, ProposedAllocationPlan Plan);

    // Everything Item F needs to actually DO with the surviving plans once
    // more than one exists — both the IN-PLACE consolidation shape
    // (planning/25, "collapsing the existing plans in place under that same
    // finance_id") and the amount-only scaling shape (below) share this same
    // input, worked out by DetermineConsolidationPlanIfApplicable before
    // anything is saved — same "read before PerformSave writes anything"
    // reasoning as NarrowingPlan's own field comment: EarMarkPatternRepository.GetAll()
    // re-validates each plan against its CURRENT goal, so reading the
    // surviving plans back out after PerformSave has already saved the
    // edited goal can throw on the very plans either mechanism needs. Null
    // means neither applies (most edits) or there's nothing surviving to
    // act on.
    private ConsolidationPlan? _consolidationPlan;

    private sealed record ConsolidationPlan(
        IReadOnlyList<EarMarkPattern> SurvivingPlans,
        IReadOnlyList<ManualEarmark> ManualEarmarksForThisGoal,
        IReadOnlyList<FinancialPattern> AllPatterns,
        // The live jar as of today, read HERE rather than inside
        // ConsolidateSurvivingPlansIfNeeded — 2026-08-15, found the hard way
        // via two existing tests whose numbers shifted for the wrong reason
        // (FinancePatternSaveConfirmationTests). This class's own header
        // comment on DetermineConsolidationPlanIfApplicable used to say
        // consolidation "needs no forecast-timing carve-out... neither
        // downstream mechanism's own total is built from a day-by-day
        // balance read" — true when it was written, no longer true once
        // EarmarkConsolidation started reading a live jar for glut
        // protection. Reading it late (inside ConsolidateSurvivingPlansIfNeeded,
        // which runs AFTER PerformSave) would read the jar against the
        // ALREADY-EDITED goal's own new schedule, not the one that actually
        // produced whatever glut exists — exactly the "read after PerformSave
        // writes" trap DetermineNarrowingPlanIfApplicable's own comment
        // already warns about for a different field. Captured here instead,
        // off the SAME pre-save forecast SurvivingPlans already reads.
        FundJar? CurrentJar,
        // The saved (pre-edit) goal's own Amount — only EarmarkScaling.Scale
        // needs this, to compute the ratio the goal's own Amount just
        // changed by. Captured here for the same reason everything else in
        // this record is: GetSavedPatternOrThrow() would return the WRONG
        // (already-edited) value once ScaleSurvivingPlansIfNeeded actually
        // runs, since that happens after PerformSave.
        decimal PreviousGoalAmount);

    // What ApplyBackTruncationsIfNeeded needs to fix 3.11.2.a2's back
    // boundary, worked out by DetermineBackTruncationsIfApplicable before
    // anything is saved — null means every existing EarMarkPattern already
    // fits inside the proposed pattern's own Until (most edits; always true
    // for a brand-new pattern, since DetermineConditions's own early return
    // leaves nothing here to find). Unlike _narrowingPlan, NOT gated on
    // IsChangeCritical or HasMultipleEarmarkPatterns — see this class's own
    // "Back-boundary invariant" header note for why. KNOWN GAP: doesn't
    // compose with _narrowingPlan when both would touch the very same single
    // plan (Critical, exactly one existing plan, Start moved forward AND
    // Until shortened in the same edit) — NarrowSurvivingPlanIfNeeded's own
    // call to PatternTruncation.StartOn preserves the plan's PRE-edit Until
    // rather than reclamping it, a gap that predates this field and isn't
    // fixed by it. Left as-is rather than guessed at; the reported crash
    // this field fixes can't reach it (HasMultipleEarmarkPatterns is true
    // there, so _narrowingPlan is always null).
    private BackTruncationPlan? _backTruncations;

    private sealed record BackTruncationPlan(
        IReadOnlyList<EarMarkPattern> PlansExceedingNewUntil,
        IReadOnlyList<DateOnly> OrphanedManualEarmarkDates);

    // planning/27's own Phase 1 — the saved pattern plus every other
    // same-Source FinancialPattern (concurrent ones already excluded — see
    // DetermineChainConditionsIfApplicable's own note), captured before
    // anything is saved for the same reason every field above is: PerformSave
    // writes _proposedPattern under _financeId, and PerformChainChangesIfApplicable
    // (which runs after it) needs to diff against what was ACTUALLY there
    // before, not what's there now. Null means DetermineChainConditionsIfApplicable
    // never ran, or this is a brand-new pattern.
    private ChainContext? _chainContext;

    private sealed record ChainContext(FinancialPattern Saved, IReadOnlyList<FinancialPattern> OtherPatterns);

    // The paycheck-association cascade (redesign/memory's own project_next_phase.md,
    // 2026-08-16/17 blocks — "a paycheck's own finance pattern and a bill's
    // earmark pattern paced against it") — captured before anything is
    // saved, same reasoning as every other context field above: OldIncome is
    // what _financeId's own pattern looked like BEFORE this edit, and
    // PerformPaycheckAssociationCascadeIfApplicable (which runs after
    // PerformSave) needs that, not the post-save value, to know what
    // InvalidatedPlans were paced against in the first place. Null means
    // this edit isn't to an income pattern at all, or no currently-paced
    // plan's own occurrences actually stop lining up with the edited
    // schedule. AllocationPlanProposer.IsPacedAgainst/FindPlansPacedAgainst
    // (2026-08-17) are this mechanism's own detection primitive — a live
    // date-coincidence check, my own grounded-but-unconfirmed heuristic for
    // what "paced against" means precisely, not an author-settled
    // definition (see those methods' own doc comments).
    private PaycheckAssociationContext? _paycheckAssociationContext;

    private sealed record PaycheckAssociationContext(FinancialPattern OldIncome, IReadOnlyList<EarMarkPattern> InvalidatedPlans);

    // ---- Conditions this class branches on (all nine named by the author
    // this session) — computing them for real is not built yet; each stays
    // at its default until its own logic lands. Plain comments, not XML doc
    // tags, matching how this project documents properties elsewhere
    // (PlanHealthState.cs) — the [TAG] + <summary> convention is for methods
    // and constructors only.

    // Whether the proposed edit touches a field 1.2.3.10.a5 restricts
    // (start_date, amount, or the recurrence shape) on a FinanceId that
    // already has an expected transaction on or before the as-of date —
    // planning/25 Item A's scope table. TODO: not computed yet.
    public bool MightAlterPast { get; private set; }

    // The user's answer to Item E's choice, once asked: true = apply the
    // change retroactively on the same FinanceId ("apply this everywhere");
    // false = change only from today forward (break off into a new
    // FinanceId). Meaningless until MightAlterPast is true and the question
    // has actually been asked. TODO: not asked yet — AskForGuidanceOnImplicitChanges
    // always answers false. internal set (not private), same reasoning as
    // UserChooseConsolidation/UserChoseScalePatterns below: this, and only
    // this, is the seam FinancePatternSaveConfirmationTests uses to simulate
    // "as if a real popup had answered" for the retroactive-correction path,
    // which Run() itself can never reach today.
    public bool UserChooseAlterPast { get; internal set; }

    // Whether this FinanceId's Savings Plan already has more than one
    // EarMarkPattern (F27's relaxation of 3.11.1.a1 — sequential from an
    // earlier Restructure/break-off, or concurrent, item 9's shape).
    // TODO: not computed yet.
    public bool HasMultipleEarmarkPatterns { get; private set; }

    // Item F's feasibility test result: true when the changed field(s) make
    // it impossible to keep multiple EarMarkPatterns separate (the
    // recurrence-shape case, planning/25 Item F's table) — consolidation
    // isn't offered as a choice there, it's announced. False means keeping
    // them separate is workable, and the user gets asked instead.
    // TODO: not computed yet.
    public bool ConsolidationNeeded { get; private set; }

    // The user's answer when ConsolidationNeeded is false and more than one
    // EarMarkPattern is in play: true = combine them into one fresh plan;
    // false = keep them separate (and, on Save and Plan, go on to ask which
    // one to open). TODO: not asked yet. internal set — a testing seam, same
    // reasoning as UserChooseAlterPast above; not yet exercised by a test.
    public bool UserChooseConsolidation { get; internal set; }

    // Whether "Save and Skip planning" was clicked rather than "Save and
    // Plan" — no navigation happens either way, so no plan-picking question
    // and no suggestion popup fire regardless of anything else here. Set at
    // construction, not computed.
    public bool UserSkippedPlanning { get; }

    // The user's answer to Item F's proportional-scaling offer (amount
    // changed, no date shift, multiple EarMarkPatterns survive): true =
    // scale every surviving plan's amount by the same ratio the
    // FinancialPattern's own amount changed by. TODO: not asked yet.
    // internal set — a testing seam, same reasoning as UserChooseAlterPast
    // above; not yet exercised by a test.
    public bool UserChoseScalePatterns { get; internal set; }

    // planning/25's Item G: which of _planShapeCandidates the user picked,
    // when there was a choice to make at all — null means "use Propose's
    // own default," both when _planShapeCandidates was empty (nothing to
    // choose between) and when the user was offered a choice and picked the
    // default anyway. Matched back to its own full ProposedAllocationPlan by
    // reference in PerformSingleSuccessorBreakOff. internal set — the same
    // testing seam as UserChooseAlterPast above.
    public EarMarkPattern? ChosenPlanShape { get; internal set; }

    // Whether the resulting plan (after whatever above has been resolved) is
    // worth suggesting a fix for — the Concerning popup's own trigger,
    // sourced from PlanHealthState.IsWorthWarningAbout. TODO: not computed
    // yet.
    public bool ChangeWarrantsSuggestions { get; private set; }

    // planning/27's own EarMarkPattern-chain questions — only ever computed
    // by RunForPlan (the other constructor's own entry point); stay false
    // for an ordinary FinancialPattern-editing instance. Set together: a
    // save can touch a boundary, be cascade-eligible, both, or neither.
    public bool PlanTouchesChainBoundary { get; private set; }
    public bool PlanChangeCanCascade { get; private set; }

    // planning/27's own Phase 1 — the FinancialPattern-chain mirror of the
    // two above, computed by DetermineChainConditionsIfApplicable (this
    // constructor's own path only; stays false for the EarMarkPattern-
    // editing one). Kept as separate, distinctly-named properties rather
    // than reusing PlanTouchesChainBoundary/PlanChangeCanCascade — the two
    // modes are mutually exclusive per instance, so nothing stops sharing
    // them technically, but keeping each pair's own documented meaning
    // (EarMarkPattern chain vs. FinancialPattern chain) unambiguous matters
    // more here than saving two properties, especially since a request can
    // legitimately need to tell EditingHistoryConfirmationWindow which kind
    // of chain it's showing.
    public bool TouchesChainBoundary { get; private set; }
    public bool ChangeCanCascade { get; private set; }

    // Phase 1's own third question, with no EarMarkPattern equivalent at
    // all (Priority/Mandatory/Description/AutoRenew have no analog there) —
    // round 3 of the "fourth relationship" small questions reopened these
    // trivial fields to ask too, once the row-based page made asking nearly
    // free. Unlike Amount/shape, defaults to NOT cascading (see
    // UserChoseCascadeTrivialFields below) — the one place Phase 1 doesn't
    // mirror Amount/shape's own default.
    public bool TrivialFieldsCanCascade { get; private set; }

    // planning/27's own Source row: "warn, don't block." "" whenever Source
    // didn't change, or changed on a segment with no predecessor/successor
    // to disconnect from (a standalone pattern — nothing to warn about). A
    // real sentence otherwise, naming what disconnects — shown as a plain
    // warning block, not tied to any radio choice, since there's no choice
    // to make here; the edit proceeds either way.
    public string SourceChangeWarning { get; private set; } = "";

    // "" whenever nothing to warn about. A real sentence whenever
    // DetermineNarrowingPlanIfApplicable found a genuine, known limitation:
    // a retroactive correction reaching earlier than the forecast's own
    // as-of date can't safely narrow the surviving plan's own span, since
    // there's no way to read what its balance actually held that far back
    // (JarBalanceOn only reaches AsOfDate forward — W4/13a's own
    // divergence, this project computes each day fresh rather than storing
    // history). Built 2026-08-17 to replace what used to be total silence
    // here — the edit itself still proceeds (the goal saves normally), but
    // now says plainly that the plan wasn't touched, rather than leaving
    // the user to notice a mismatch on their own later.
    public string NarrowingLimitationWarning { get; private set; } = "";

    // The paycheck-association cascade's own trigger — true when at least
    // one OTHER FinancialPattern's currently-active savings plan was paced
    // against this income's OLD schedule and no longer is, per
    // DeterminePaycheckAssociationIfApplicable. A completely different
    // relationship from TouchesChainBoundary/ChangeCanCascade above (those
    // are about _financeId's own predecessor/successor chain; this is about
    // OTHER, unrelated FinancialPatterns' own plans) — never mutually
    // exclusive with them, so this can't reuse their answer fields the way
    // UserChoseStayLinked/UserChoseCascadeForward reuse each other across
    // Phase 1/Phase 2. Only ever computed for the FinancialPattern-editing
    // constructor (stays false for RunForPlan's own EarMarkPattern-editing
    // one — a savings plan itself is never income).
    public bool PacedBillsCanCascade { get; private set; }

    // The user's answer to "stay linked in the chain, or let it break" —
    // meaningless unless PlanTouchesChainBoundary OR TouchesChainBoundary is
    // true (the EarMarkPattern-chain and FinancialPattern-chain versions of
    // the same question — RunForPlan/Run() never both run on one instance,
    // so one shared answer field serves either). internal set — a testing
    // seam, same reasoning as UserChooseAlterPast above. No explicit default
    // was ever settled the way cascading forward's was — true (stay linked)
    // is this class's own reasoned choice, matching the one option that's
    // never destructive on its own, not something stated outright.
    public bool UserChoseStayLinked { get; internal set; } = true;

    // The user's answer to "cascade forward, or just this plan/segment" —
    // meaningless unless PlanChangeCanCascade OR ChangeCanCascade is true
    // (same shared-field reasoning as UserChoseStayLinked above). Defaults
    // to true — SETTLED, cascading forward is the system default for
    // Amount/shape on either chain type. internal set, same testing-seam
    // reasoning as above.
    public bool UserChoseCascadeForward { get; internal set; } = true;

    // The user's answer to Phase 1's own trivial-fields question —
    // meaningless unless TrivialFieldsCanCascade is true. Defaults to
    // FALSE, unlike UserChoseCascadeForward above — SETTLED (round 3):
    // "default stays 'just this segment' — nothing about today's actual
    // behavior changes for anyone who accepts the row's own default." No
    // EarMarkPattern-editing counterpart exists, so this is never shared the
    // way the two answers above are. internal set, same testing-seam
    // reasoning as UserChooseAlterPast above.
    public bool UserChoseCascadeTrivialFields { get; internal set; }

    // The user's answer to the paycheck-association cascade — meaningless
    // unless PacedBillsCanCascade is true. Defaults to FALSE — "offered as a
    // suggestion (not forced)" (the settled language this mechanism is
    // named for) means declining is the safe no-op, same reasoning as
    // UserChoseCascadeTrivialFields above, and more so here: re-pacing a
    // bill's plan is a real, visible change to its own money movement, not
    // just a trivial-field copy. internal set, same testing-seam reasoning
    // as UserChooseAlterPast above.
    public bool UserChoseToRepaceBills { get; internal set; }

    // The overall Trivial/Critical/Concerning categorization's own top-level
    // flag: true when this save needs to go through the
    // confirmation-and-choice flow at all. Kept as its own property, distinct
    // from MightAlterPast, since the author's final categorization table may
    // end up computing this from more than one condition — not necessarily a
    // synonym once built out. TODO: not computed yet.
    public bool IsChangeCritical { get; private set; }

    /// <summary>[CALC] Builds the orchestrator for one Save click. Nothing is looked up or shown yet — call Run to actually do the work.</summary>
    /// <param name="financeId">The FinancialPattern being edited (or created, if this is a new one).</param>
    /// <param name="proposedPattern">The form's current field values — what would be saved if nothing here needs to ask anything first.</param>
    /// <param name="accountId">Which account the pattern is filed under.</param>
    /// <param name="userSkippedPlanning">True for "Save and Skip planning," false for "Save and Plan."</param>
    /// <param name="requestForecast">Live-forecast accessor — reaches a TransactionLogBook (for reads) and PlanHealthState.</param>
    /// <param name="repositories">The three repositories needed for whatever this class ends up writing (and for ManualEarmark reads, which the book can't answer).</param>
    public FinancePatternSaveConfirmation(
        int financeId,
        FinancialPattern proposedPattern,
        int accountId,
        bool userSkippedPlanning,
        Func<ForecastResult> requestForecast,
        FinancePatternRepositories repositories)
    {
        _financeId = financeId;
        _navigationFinanceId = financeId;
        _proposedPattern = proposedPattern;
        _accountId = accountId;
        UserSkippedPlanning = userSkippedPlanning;
        _requestForecast = requestForecast;
        _repositories = repositories;
    }

    /// <summary>[CALC] Builds the orchestrator for an EarmarkFormPanel save click instead of an Expense one — planning/27's own "stay linked or break" and "cascade forward or not" questions for a same-finance_id EarMarkPattern chain. Call Run to actually do the work, same as the other constructor.</summary>
    /// <param name="proposedPlan">The form's current field values for the savings plan — what would be saved if nothing here needs to ask anything first.</param>
    /// <param name="savedStart">The Start this plan is actually saved under today, or the same as proposedPlan's own Start for a brand-new plan.</param>
    /// <param name="goal">The FinancialPattern this savings plan funds.</param>
    /// <param name="requestForecast">Live-forecast accessor — reaches the rest of the same-finance_id chain.</param>
    /// <param name="repositories">The three repositories needed for whatever this class ends up writing.</param>
    public FinancePatternSaveConfirmation(
        EarMarkPattern proposedPlan,
        DateOnly savedStart,
        FinancialPattern goal,
        Func<ForecastResult> requestForecast,
        FinancePatternRepositories repositories)
    {
        _financeId = goal.FinanceId;
        _navigationFinanceId = goal.FinanceId;
        _proposedPattern = goal; // doubles as "the goal" in this mode — see this class's own field comment
        _accountId = 0; // unused in this mode — no FinancialPattern save happens
        UserSkippedPlanning = true; // unused in this mode — no onward navigation, mirrors Earmark's own single save button
        _proposedPlan = proposedPlan;
        _earmarkSavedStart = savedStart;
        _requestForecast = requestForecast;
        _repositories = repositories;
    }

    // Invoked once Run() decides navigation should happen — matches this
    // project's existing PatternSaved/AccountSaved/ManualEarmarksSaved
    // callback idiom (set by MainWindow) rather than giving this class a
    // direct reference to EarmarkFormPanel or the tab control. Null means
    // "open a blank Earmark form" (mirrors MainWindow's own existing
    // fallback when a pattern has no plan yet). Never invoked when
    // UserSkippedPlanning is true.
    public Action<EarMarkPattern?>? NavigateToEarmarkForm { get; set; }

    // The Concerning popup's own callback, same idiom as NavigateToEarmarkForm
    // just above — this class builds the message, MainWindow shows it, so
    // this class stays WPF-free. Built 2026-08-17, deliberately minimal: a
    // plain PlanHealthMessages.CurrentJarStateLine sentence, acknowledge-only
    // — NOT the elaborate strategy-picker (pre-fill Suggested/Working-state
    // values, one default suggested set or none) planning/25's own "Still
    // open" section describes, which the author explicitly deferred and
    // whose own specific strategies were never designed. Null (nothing
    // wired up — most tests, and any host that hasn't connected one) means
    // AskForSuggestions is a silent no-op, same as before this existed.
    public Action<string>? ShowSuggestion { get; set; }

    // AskWhichEarmarkPatternToOpen's own disambiguation callback, same
    // delegate idiom as the two above — built 2026-08-17 (EarmarkPatternPickerWindow),
    // replacing "the first match" as a real choice instead of a
    // placeholder. Null falls back to that same first-match default.
    public Func<IReadOnlyList<EarMarkPattern>, EarMarkPattern?>? PickEarmarkPattern { get; set; }

    // The Item B/C/E/F confirmation itself — one combined ask, not two
    // popups stacked (author's own call, reviewing the mockups), covering
    // both the correct-everywhere-vs-break-off choice and the multiple-plans
    // follow-up in a single dialog. Same callback idiom as
    // NavigateToEarmarkForm: this class builds the request and applies the
    // answer, but never constructs a Window itself, so it stays WPF-free —
    // MainWindow is the one that actually shows something. Null (nothing
    // wired up — most tests) falls back to DefaultConfirmationAnswer's own
    // safest, least-destructive answer to every question at once, so Run()
    // still completes end to end without a live UI.
    public Func<ImplicitChangeConfirmationRequest, ImplicitChangeConfirmationAnswer>? ConfirmImplicitChanges { get; set; }

    /// <summary>[STEP] The single entry point: works out whether anything needs asking, shows the confirmation if so, saves the FinancialPattern, performs whatever implicit EarMarkPattern/EarMarkEvent changes were decided, and (on Save and Plan) calls NavigateToEarmarkForm. See the class's own STATUS note for exactly which of PerformImplicitEarmarkChanges' branches are real versus still TODO.</summary>
    /// <returns>False if the user cancelled out of the confirmation — nothing was saved, exactly as if Save had never been clicked. True otherwise, including every case where nothing needed asking at all.</returns>
    public bool Run()
    {
        // The other constructor's own mode — a completely separate entry
        // point, never touching any of the FinancialPattern-editing logic
        // below it.
        if (_proposedPlan is not null)
        {
            return RunForPlan();
        }

        DetermineConditions();

        // planning/27's own Phase 1 — same "read before anything is saved"
        // timing as every Determine* call below, needs to run before any of
        // them touch a repository write. Independent of Items A-G's own
        // Critical/multi-plan conditions — a save can be Trivial by Items
        // A-G's own reckoning and still touch a chain boundary (e.g. a plain
        // Priority bump that also happens to change Source).
        DetermineChainConditionsIfApplicable();

        // Independent of everything above — a completely different
        // relationship (this income's own effect on OTHER patterns' plans,
        // not _financeId's own chain) — same "read before anything is
        // saved" timing as every Determine* call in this class.
        DeterminePaycheckAssociationIfApplicable();

        // Has to happen here — before ConfirmImplicitChanges even fires,
        // let alone PerformSave — while _repositories.ManualEarmarks is
        // still safe to read (see NarrowingPlan's own field comment). Cheap
        // enough to always compute when the structural preconditions hold,
        // even though most of the time UserChooseAlterPast will end up
        // false and it goes unused.
        DetermineNarrowingPlanIfApplicable();

        // Same reasoning and timing as DetermineNarrowingPlanIfApplicable
        // just above, for Item F's own in-place consolidation instead of
        // Item E's single-plan narrowing — the two are mutually exclusive
        // (one needs exactly one surviving plan, the other more than one),
        // so at most one of _narrowingPlan/_consolidationPlan is ever set.
        DetermineConsolidationPlanIfApplicable();

        // planning/25's Item G — same timing as the two Determine* calls
        // just above. No longer mutually exclusive with _consolidationPlan
        // the way it once was (2026-08-16): a sequential multi-plan
        // predecessor can populate both, since which one actually gets
        // acted on depends on UserChooseAlterPast, decided later — only a
        // genuinely concurrent set leaves this one empty.
        DeterminePlanShapeCandidatesIfApplicable();

        // Same "read before anything is saved" reasoning, but for a
        // completely different, unconditional invariant: end_date can
        // always change freely (planning/25 Item A's own carve-out — never
        // Critical, no confirmation needed), but a plain save was never
        // checking whether an existing EarMarkPattern still fits inside the
        // goal's own, possibly-just-shortened Until (3.11.2.a2). Runs
        // regardless of IsChangeCritical/HasMultipleEarmarkPatterns —
        // "exempt from asking" was never the same as "exempt from needing
        // the linked plan fixed up." Found in the field (2026-08-13): a real
        // end_date-only edit on a multi-plan goal left the goal and its
        // plans mismatched, crashing every later EarMarkPatternRepository.GetAll()
        // — including MainWindow's own startup read — until the database
        // was reset.
        DetermineBackTruncationsIfApplicable();

        // SETTLED 2026-08-11 (author): Item F's own question applies
        // regardless of which Item E path gets chosen — it is NOT
        // break-off-only. What "consolidate" means differs by path: for
        // break-off, it's Item C's existing "always fresh-propose one
        // successor" shape; for a retroactive correction (same finance_id,
        // no successor), it means collapsing the existing plans in place
        // under that same finance_id instead — a genuinely new mechanism,
        // not yet designed or built (see PerformImplicitEarmarkChanges's own
        // TODOs). One combined pass either way, per the note above.
        if (IsChangeCritical || HasMultipleEarmarkPatterns || TouchesChainBoundary || ChangeCanCascade || TrivialFieldsCanCascade || !string.IsNullOrEmpty(SourceChangeWarning) || PacedBillsCanCascade)
        {
            var answer = ConfirmImplicitChanges?.Invoke(BuildConfirmationRequest()) ?? DefaultConfirmationAnswer();
            if (!answer.Proceed)
            {
                return false;
            }

            UserChooseAlterPast = answer.ChooseAlterPast;
            UserChooseConsolidation = answer.ChooseConsolidation;
            UserChoseScalePatterns = answer.ChoseScalePatterns;
            ChosenPlanShape = answer.ChosenPlanShape;
            UserChoseStayLinked = answer.ChoseStayLinked;
            UserChoseCascadeForward = answer.ChoseCascadeForward;
            UserChoseCascadeTrivialFields = answer.ChoseCascadeTrivialFields;
            UserChoseToRepaceBills = answer.ChoseToRepaceBills;
        }

        PerformSave();

        // Runs before PerformImplicitEarmarkChanges, matching the general
        // standing rule (planning/27): when one save could raise both a
        // chain question and a funding question at once, the chain question
        // resolves first. Uses _chainContext (captured before PerformSave
        // ran), not a fresh repository read — the goal DetermineChainConditionsIfApplicable
        // diffed against is now stale the instant PerformSave writes
        // _proposedPattern under _financeId.
        PerformChainChangesIfApplicable();
        PerformImplicitEarmarkChanges();

        // Last — touches only OTHER FinancialPatterns' own plans (never
        // _financeId's own), so it has no ordering dependency on the two
        // calls just above; placed after them simply so every save this
        // Run() call might make to _financeId's own side is already settled
        // first.
        PerformPaycheckAssociationCascadeIfApplicable();

        if (!UserSkippedPlanning)
        {
            var target = AskWhichEarmarkPatternToOpen();
            if (ChangeWarrantsSuggestions)
            {
                AskForSuggestions();
            }

            NavigateToEarmarkForm?.Invoke(target);
        }

        return true;
    }

    /// <summary>[STEP] The EarMarkPattern-editing entry point (planning/27) — resolves "stay linked or break" for a Start/Until change and "cascade forward or not" for an Amount/shape change against the rest of the same-finance_id chain, then saves. Mirrors Run()'s own overall shape (work out what's needed, confirm, then act) for a savings plan instead of the goal itself.</summary>
    /// <returns>False if the user cancelled out of the confirmation — nothing was saved. True otherwise, including when nothing needed asking at all.</returns>
    private bool RunForPlan()
    {
        var proposedPlan = _proposedPlan!;
        var goal = _proposedPattern; // doubles as "the goal" in this mode — see this class's own field comment
        var forecast = _requestForecast();
        var allPlansForGoal = forecast.Book.EarMarkPatternsFor(goal.FinanceId);
        var saved = allPlansForGoal.FirstOrDefault(plan => plan.DatePattern.Start == _earmarkSavedStart);

        if (saved is null)
        {
            // A brand-new plan — nothing to compare against, so no chain
            // question to ask, the same way DetermineConditions treats a
            // brand-new FinancialPattern.
            _repositories.EarMarkPatterns.Save(proposedPlan);
            return true;
        }

        // F27 allows two shapes for more than one EarMarkPattern under one
        // finance_id: a genuine sequential chain (RestructureFactory), or
        // concurrent, overlapping funders — a different, still only
        // partially built case that planning/27 explicitly settled must NOT
        // get chain-boundary/cascade treatment. Filtering to
        // !SpansOverlap(plan, saved) here — before hasPredecessor/hasSuccessor
        // are even computed — is what keeps a concurrent plan (e.g. the
        // Storage Unit Rental seed scenario's own two funders) from being
        // mistaken for a sequential neighbor and absorbed/cascaded-onto by
        // PerformEarmarkSave below, which trusts this same filtered list.
        var otherPlans = allPlansForGoal
            .Where(plan => plan.DatePattern.Start != saved.DatePattern.Start && !RestructureFactory.SpansOverlap(plan, saved))
            .ToList();
        var hasPredecessor = otherPlans.Any(plan => plan.DatePattern.Start < saved.DatePattern.Start);
        var hasSuccessor = otherPlans.Any(plan => plan.DatePattern.Start > saved.DatePattern.Start);

        var startChanged = saved.DatePattern.Start != proposedPlan.DatePattern.Start;
        var untilChanged = saved.DatePattern.Until != proposedPlan.DatePattern.Until;
        var amountOrShapeChanged = saved.Amount != proposedPlan.Amount
            || saved.DatePattern.Frequency != proposedPlan.DatePattern.Frequency
            || saved.DatePattern.Interval != proposedPlan.DatePattern.Interval
            || !saved.DatePattern.ByDay.SequenceEqual(proposedPlan.DatePattern.ByDay)
            || !saved.DatePattern.ByMonthDay.SequenceEqual(proposedPlan.DatePattern.ByMonthDay);

        // planning/27's "fourth relationship" — only relevant once this
        // plan is the LAST one in its OWN finance_id's chain (!hasSuccessor):
        // does the GOAL ITSELF (a FinancialPattern) have a break-off
        // successor under a different finance_id, and does THAT segment
        // have a real "current" plan of its own to cascade onto? Composing
        // two already-built lookups that have never been composed across
        // this boundary before (BreakOffFactory.FindSuccessor, then
        // RestructureFactory.FindCurrentPlan on the far side) — never
        // computed at all when hasSuccessor is true, since a same-finance_id
        // successor always takes priority (forward-only means cascading
        // through this plan's OWN chain first; the cross-boundary reach only
        // matters once that chain has nowhere further to go).
        FinancialPattern? crossBoundaryGoal = null;
        EarMarkPattern? crossBoundaryTarget = null;
        if (!hasSuccessor)
        {
            crossBoundaryGoal = BreakOffFactory.FindSuccessor(goal, forecast.Book.AllFinancialPatterns());
            if (crossBoundaryGoal is { } successorGoal)
            {
                // FindCurrentPlan itself returns null for a genuinely
                // concurrent set on the far side — correctly no different
                // than any other "which plan is current" lookup elsewhere in
                // this document; no special leniency for the cross-boundary
                // case (planning/27, settled).
                crossBoundaryTarget = RestructureFactory.FindCurrentPlan(forecast.Book.EarMarkPatternsFor(successorGoal.FinanceId));
            }
        }

        PlanTouchesChainBoundary = (startChanged && hasPredecessor) || (untilChanged && hasSuccessor);
        PlanChangeCanCascade = amountOrShapeChanged && (hasSuccessor || crossBoundaryTarget is not null);

        if (PlanTouchesChainBoundary || PlanChangeCanCascade)
        {
            var request = BuildEarmarkConfirmationRequest(proposedPlan, saved, otherPlans, goal, crossBoundaryTarget, crossBoundaryGoal);
            var answer = ConfirmImplicitChanges?.Invoke(request) ?? DefaultEarmarkConfirmationAnswer();
            if (!answer.Proceed)
            {
                return false;
            }

            UserChoseStayLinked = answer.ChoseStayLinked;
            UserChoseCascadeForward = answer.ChoseCascadeForward;
        }

        PerformEarmarkSave(proposedPlan, saved, otherPlans, goal, crossBoundaryTarget, crossBoundaryGoal);
        return true;
    }

    /// <summary>[WRITES FILE] Carries out whatever RunForPlan decided — resolves the chain boundary (stay linked, via RestructureFactory.ExtendStart/ExtendUntil, or left broken) and the cascade (same-finance_id via RestructureFactory.CascadeForward, cross-boundary via the same function pointed at the far side's own goal, or just this plan), deletes any ManualEarmark a shrinking span just orphaned, then saves.</summary>
    /// <param name="proposedPlan">The form's current field values.</param>
    /// <param name="saved">The plan as it's actually saved today.</param>
    /// <param name="otherPlans">Every other EarMarkPattern sharing the same finance_id.</param>
    /// <param name="goal">The goal this Savings Plan funds.</param>
    /// <param name="crossBoundaryTarget">planning/27's "fourth relationship" — the current EarMarkPattern on the far side of a FinancialPattern-level break-off, or null when there's no successor goal, no plan on it yet, or its own plans are a genuinely concurrent set with no single "current" one.</param>
    /// <param name="crossBoundaryGoal">The far side's own goal — required to validate crossBoundaryTarget's cascaded replacement, since it belongs to a different finance_id than goal above.</param>
    private void PerformEarmarkSave(
        EarMarkPattern proposedPlan, EarMarkPattern saved, IReadOnlyList<EarMarkPattern> otherPlans, FinancialPattern goal,
        EarMarkPattern? crossBoundaryTarget, FinancialPattern? crossBoundaryGoal)
    {
        var current = proposedPlan;
        var toDelete = new List<DateOnly>();
        // Keyed by each surviving neighbor's own ORIGINAL Start — stable
        // even for ExtendUntil's own neighbor, whose Start itself moves, so
        // the cascade step below can still find and update it by where it
        // used to be.
        var toSave = new Dictionary<DateOnly, EarMarkPattern>();

        var predecessors = otherPlans.Where(plan => plan.DatePattern.Start < saved.DatePattern.Start).ToList();
        var successors = otherPlans.Where(plan => plan.DatePattern.Start > saved.DatePattern.Start).ToList();

        if (PlanTouchesChainBoundary && UserChoseStayLinked)
        {
            if (saved.DatePattern.Start != current.DatePattern.Start && predecessors.Count > 0)
            {
                var result = RestructureFactory.ExtendStart(current, predecessors, goal, current.DatePattern.Start);
                current = result.Current;
                foreach (var absorbed in result.Absorbed)
                {
                    toDelete.Add(absorbed.DatePattern.Start);
                }

                if (result.AdjustedNeighbor is { } adjusted)
                {
                    // ExtendStart's own neighbor keeps its own Start — an
                    // in-place update, not a key change.
                    toSave[adjusted.DatePattern.Start] = adjusted;
                }
            }

            if (saved.DatePattern.Until != current.DatePattern.Until && successors.Count > 0)
            {
                var result = RestructureFactory.ExtendUntil(current, successors, goal, current.DatePattern.Until);
                current = result.Current;
                foreach (var absorbed in result.Absorbed)
                {
                    toDelete.Add(absorbed.DatePattern.Start);
                }

                if (result.AdjustedNeighbor is { } adjusted)
                {
                    // ExtendUntil's own neighbor gets a NEW Start — a real
                    // key change, found by whichever original still has the
                    // adjusted one's own (unmoved) Until.
                    var original = successors.First(plan => plan.DatePattern.Until == adjusted.DatePattern.Until);
                    toDelete.Add(original.DatePattern.Start);
                    toSave[original.DatePattern.Start] = adjusted;
                }
            }
        }

        if (PlanChangeCanCascade && UserChoseCascadeForward && successors.Count > 0)
        {
            // Cascades onto whatever successors are still standing after
            // boundary resolution above — including one just date-adjusted
            // there, whose own dates CascadeForward leaves untouched, only
            // its Amount/shape change.
            var stillStanding = successors
                .Where(plan => !toDelete.Contains(plan.DatePattern.Start) || toSave.ContainsKey(plan.DatePattern.Start))
                .Select(plan => toSave.TryGetValue(plan.DatePattern.Start, out var adjusted) ? adjusted : plan)
                .ToList();

            foreach (var cascaded in RestructureFactory.CascadeForward(current.DatePattern, current.Amount, stillStanding, goal))
            {
                // CascadeForward's own output always keeps its input's
                // Start, so this is guaranteed to be a real key already in
                // toSave or among the originals — never a fresh one.
                toSave[cascaded.DatePattern.Start] = cascaded;
            }
        }
        else if (PlanChangeCanCascade && UserChoseCascadeForward && crossBoundaryTarget is not null && crossBoundaryGoal is not null)
        {
            // planning/27's "fourth relationship" — the far side belongs to
            // a DIFFERENT finance_id than everything else this method
            // touches, so it can't go through toSave/toDelete (both keyed
            // for THIS finance_id's own rows, where a key collision against
            // an unrelated goal's own Start is a real, if unlikely, risk) —
            // saved directly instead. CascadeForward's own output always
            // keeps its input's Start, so this is always an in-place update,
            // never a key change, matching why no delete is needed here.
            var cascaded = RestructureFactory.CascadeForward(current.DatePattern, current.Amount, [crossBoundaryTarget], crossBoundaryGoal).Single();
            _repositories.EarMarkPatterns.Save(cascaded);
        }

        // A "let it break" choice — or a standalone plan with no neighbor to
        // stay linked to at all, so PlanTouchesChainBoundary never even fired
        // — can shrink current's own span away from wherever it used to
        // reach. Checked generally here, not gated on PlanTouchesChainBoundary
        // specifically, so a standalone plan's own Start/Until edit gets the
        // same protection a chained one does. This is the only way an
        // EarMarkPattern-level edit can genuinely orphan a ManualEarmark —
        // absorption never does (this class's own planning/27 note: the
        // absorbing segment's final span always covers the union of what
        // both old segments covered, so nothing inside it stops being
        // covered). Computed against finalOtherPlans (post-boundary-
        // resolution — the adjusted neighbor if one exists, not its stale
        // original) rather than the raw otherPlans read at the top, so a
        // neighbor that just stretched to stay contiguous correctly still
        // counts as covering whatever it now reaches. Must run before any
        // write below: ManualEarmarkRepository.GetAll() re-validates every
        // row against whatever's currently saved, so reading here — before
        // this save touches anything — is what keeps this from throwing on
        // the very rows it's trying to identify, same reasoning
        // DetermineBackTruncationsIfApplicable's own header note already
        // gives for the FinancialPattern-level equivalent of this check.
        var finalOtherPlans = otherPlans
            .Where(plan => !toDelete.Contains(plan.DatePattern.Start) || toSave.ContainsKey(plan.DatePattern.Start))
            .Select(plan => toSave.TryGetValue(plan.DatePattern.Start, out var adjusted) ? adjusted : plan)
            .ToList();
        var finalCoverage = new List<EarMarkPattern> { current };
        finalCoverage.AddRange(finalOtherPlans);

        var orphanedManualEarmarkDates = FindOrphanedManualEarmarkDates(finalCoverage, goal.FinanceId);

        // The saved row's own key only actually changed if Start moved —
        // checked here, after boundary resolution, not assumed up front:
        // saving current under its final key and then deleting the OLD key
        // (only when they differ) is what makes this an update rather than
        // an accidental drop of a row that never actually moved.
        if (current.DatePattern.Start != saved.DatePattern.Start)
        {
            toDelete.Add(saved.DatePattern.Start);
        }

        foreach (var date in orphanedManualEarmarkDates)
        {
            _repositories.ManualEarmarks.Delete(goal.FinanceId, date);
        }

        _repositories.EarMarkPatterns.Save(current);
        foreach (var plan in toSave.Values)
        {
            _repositories.EarMarkPatterns.Save(plan);
        }

        foreach (var start in toDelete.Distinct())
        {
            _repositories.EarMarkPatterns.Delete(goal.FinanceId, start);
        }
    }

    /// <summary>[READS FILE] Builds what ConfirmImplicitChanges needs for the EarMarkPattern-editing case — planning/27's own "stay linked or break" and "cascade forward or not" questions, now with the concrete-consequence wording that document's own settled content calls for (StayLinkedWarning/LetItBreakWarning/CascadeDescription). Only ever called when at least one of PlanTouchesChainBoundary/PlanChangeCanCascade is true (RunForPlan's own gate), so Description always names at least one. [READS FILE] because the "let it break" preview dry-runs FindOrphanedManualEarmarkDates, which reads ManualEarmarks — safe here, same as everywhere else in this class, since nothing has been saved yet this Run().</summary>
    /// <param name="current">The plan as the user is currently proposing to save it.</param>
    /// <param name="saved">The plan as it's actually saved today.</param>
    /// <param name="otherPlans">Every other EarMarkPattern sharing the same finance_id (concurrent plans already excluded — see RunForPlan's own note).</param>
    /// <param name="goal">The goal this Savings Plan funds.</param>
    /// <param name="crossBoundaryTarget">planning/27's "fourth relationship" target, or null — see PerformEarmarkSave's own param doc for the full explanation.</param>
    /// <param name="crossBoundaryGoal">The far side's own goal, paired with crossBoundaryTarget.</param>
    private ImplicitChangeConfirmationRequest BuildEarmarkConfirmationRequest(
        EarMarkPattern current, EarMarkPattern saved, IReadOnlyList<EarMarkPattern> otherPlans, FinancialPattern goal,
        EarMarkPattern? crossBoundaryTarget, FinancialPattern? crossBoundaryGoal)
    {
        var predecessors = otherPlans.Where(plan => plan.DatePattern.Start < saved.DatePattern.Start).ToList();
        var successors = otherPlans.Where(plan => plan.DatePattern.Start > saved.DatePattern.Start).ToList();

        return new ImplicitChangeConfirmationRequest
        {
            IsChangeCritical = false,
            HasMultipleEarmarkPatterns = false,
            ConsolidationNeeded = false,
            ConsolidationForcedReason = "",
            ConsolidationCaveat = "",
            IsAmountOnlyChange = false,
            PlanShapeCandidates = [],
            Description = (PlanTouchesChainBoundary, PlanChangeCanCascade) switch
            {
                (true, true) => "This plan is part of a chain. Its date range touches a neighboring segment, and its amount or schedule change could carry forward too.",
                (true, false) => "This plan is part of a chain. Its date range touches a neighboring segment.",
                _ => "This plan is part of a chain. Later segments could pick up this same amount or schedule change.",
            },
            AlterPastConsequence = "", // no AlterPastSection at all for this entry point
            NarrowingLimitationWarning = "", // Item E's own narrowing never applies to this entry point
            PlanTouchesChainBoundary = PlanTouchesChainBoundary,
            PlanChangeCanCascade = PlanChangeCanCascade,
            // planning/27's own Phase 1 questions never apply to an
            // EarMarkPattern-editing request.
            TouchesChainBoundary = false,
            ChangeCanCascade = false,
            TrivialFieldsCanCascade = false,
            SourceChangeWarning = "",
            StayLinkedWarning = PlanTouchesChainBoundary ? DescribeStayLinkedConsequence(current, saved, predecessors, successors, goal) : "",
            LetItBreakWarning = PlanTouchesChainBoundary ? DescribeLetItBreakConsequence(current, saved, predecessors, successors, otherPlans, goal) : "",
            CascadeDescription = PlanChangeCanCascade ? DescribeCascadeConsequence(current, successors, crossBoundaryTarget, crossBoundaryGoal) : "",
            TrivialFieldsCascadeDescription = "",
            // The paycheck-association cascade never applies to an
            // EarMarkPattern-editing request — a savings plan itself is
            // never income, so it can never BE the paycheck side of this
            // relationship.
            PacedBillsCanCascade = false,
            PacedBillsCascadeDescription = "",
        };
    }

    /// <summary>[CALC] Previews what "stay linked" would actually do to current's own neighbors, for the confirmation row's own warning slot — "" for a plain, contiguous nudge (never destructive, matching the settled "the default option is never the dangerous one" reasoning, so nothing to warn about), or a real sentence naming which segment(s) would be absorbed (deleted outright, their own values overwritten) once the edit reaches that far. Dry-runs the exact same RestructureFactory calls PerformEarmarkSave itself will make if this branch is actually chosen; the result here is discarded after formatting, not stored, since only one of "stay linked"/"let it break" ever actually runs and re-deriving it is cheap (pure functions over short lists).</summary>
    private static string DescribeStayLinkedConsequence(
        EarMarkPattern current, EarMarkPattern saved, IReadOnlyList<EarMarkPattern> predecessors, IReadOnlyList<EarMarkPattern> successors, FinancialPattern goal)
    {
        var absorbed = new List<EarMarkPattern>();

        if (saved.DatePattern.Start != current.DatePattern.Start && predecessors.Count > 0)
        {
            absorbed.AddRange(RestructureFactory.ExtendStart(current, predecessors, goal, current.DatePattern.Start).Absorbed);
        }

        if (saved.DatePattern.Until != current.DatePattern.Until && successors.Count > 0)
        {
            absorbed.AddRange(RestructureFactory.ExtendUntil(current, successors, goal, current.DatePattern.Until).Absorbed);
        }

        if (absorbed.Count == 0)
        {
            return ""; // a plain nudge — never destructive, nothing to warn about
        }

        var ordered = absorbed.OrderBy(plan => plan.DatePattern.Start).ToList();
        var ranges = string.Join("; ", ordered.Select(plan => $"{plan.DatePattern.Start:MMM d, yyyy} – {plan.DatePattern.Until:MMM d, yyyy}"));
        return ordered.Count == 1
            ? $"This will delete the segment covering {ranges} entirely — its own amount and schedule won't be kept."
            : $"This will delete {ordered.Count} segments entirely ({ranges}) — their own amounts and schedules won't be kept.";
    }

    /// <summary>[READS FILE] Previews what "let the chain break" would actually leave behind, for the confirmation row's own warning slot — always a real sentence when PlanTouchesChainBoundary is true, since breaking the chain always leaves SOME gap or overlap. Names whether a gap or overlap forms with the predecessor/successor (RestructureFactory.SpansOverlap decides which) and how many manual earmarks a gap would strand, via a dry run of FindOrphanedManualEarmarkDates against the hypothetical "break" outcome — current's own new span plus every other plan left exactly as it stands today, since "let it break" never touches a neighbor.</summary>
    private string DescribeLetItBreakConsequence(
        EarMarkPattern current, EarMarkPattern saved, IReadOnlyList<EarMarkPattern> predecessors, IReadOnlyList<EarMarkPattern> successors,
        IReadOnlyList<EarMarkPattern> otherPlans, FinancialPattern goal)
    {
        var consequences = new List<string>();

        if (saved.DatePattern.Start != current.DatePattern.Start && predecessors.Count > 0)
        {
            var predecessor = predecessors.OrderByDescending(plan => plan.DatePattern.Start).First();
            consequences.Add(RestructureFactory.SpansOverlap(current, predecessor)
                ? $"it will overlap with the segment before it ({predecessor.DatePattern.Start:MMM d, yyyy} – {predecessor.DatePattern.Until:MMM d, yyyy})"
                : $"a gap will open before it, from {predecessor.DatePattern.Until.AddDays(1):MMM d, yyyy} to {current.DatePattern.Start.AddDays(-1):MMM d, yyyy}");
        }

        if (saved.DatePattern.Until != current.DatePattern.Until && successors.Count > 0)
        {
            var successor = successors.OrderBy(plan => plan.DatePattern.Start).First();
            consequences.Add(RestructureFactory.SpansOverlap(current, successor)
                ? $"it will overlap with the segment after it ({successor.DatePattern.Start:MMM d, yyyy} – {successor.DatePattern.Until:MMM d, yyyy})"
                : $"a gap will open after it, from {current.DatePattern.Until.AddDays(1):MMM d, yyyy} to {successor.DatePattern.Start.AddDays(-1):MMM d, yyyy}");
        }

        var hypotheticalCoverage = new List<EarMarkPattern> { current };
        hypotheticalCoverage.AddRange(otherPlans);
        var orphaned = FindOrphanedManualEarmarkDates(hypotheticalCoverage, goal.FinanceId);
        if (orphaned.Count > 0)
        {
            consequences.Add(orphaned.Count == 1
                ? $"1 manual earmark dated {orphaned[0]:MMM d, yyyy} will be deleted"
                : $"{orphaned.Count} manual earmarks will be deleted");
        }

        if (consequences.Count == 0)
        {
            // Shouldn't happen when PlanTouchesChainBoundary is true (some
            // gap/overlap always results from breaking) — never claim a
            // consequence isn't real just to force non-empty text.
            return "";
        }

        var sentence = string.Join("; ", consequences) + ".";
        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    /// <summary>[CALC] Names the date range a cascade would actually reach, for the confirmation row's own always-shown description — round 2 of planning/27's own small questions settled that whatever confirms an Amount/shape cascade "must show, plainly, the date range the direct edit itself covers and how far the cascade reaches into the future — which segments, through what date." Unlike StayLinkedWarning/LetItBreakWarning above, this isn't a warning tied to one "dangerous" option — neither Cascade choice is destructive, so it's shown under the row regardless of which one is currently selected. successors and crossBoundaryTarget are mutually exclusive by construction (RunForPlan only ever computes the cross-boundary target when this same-finance_id chain has no successor of its own), so exactly one branch below ever has anything to describe.</summary>
    /// <param name="current">The plan as the user is currently proposing to save it.</param>
    /// <param name="successors">Later plans sharing the same finance_id — empty whenever the cascade is cross-boundary instead.</param>
    /// <param name="crossBoundaryTarget">planning/27's "fourth relationship" target, or null when there isn't one.</param>
    /// <param name="crossBoundaryGoal">The far side's own goal, paired with crossBoundaryTarget — named in the sentence so the user knows this reaches beyond the bill they're currently looking at.</param>
    private static string DescribeCascadeConsequence(
        EarMarkPattern current, IReadOnlyList<EarMarkPattern> successors, EarMarkPattern? crossBoundaryTarget, FinancialPattern? crossBoundaryGoal)
    {
        var ownRange = $"This edit covers {current.DatePattern.Start:MMM d, yyyy} – {current.DatePattern.Until:MMM d, yyyy}.";

        if (successors.Count > 0)
        {
            var furthest = successors.Max(plan => plan.DatePattern.Until);
            return successors.Count == 1
                ? $"{ownRange} Cascading forward would also update the segment running through {furthest:MMM d, yyyy}."
                : $"{ownRange} Cascading forward would also update {successors.Count} later segments, through {furthest:MMM d, yyyy}.";
        }

        if (crossBoundaryTarget is not null && crossBoundaryGoal is not null)
        {
            var label = string.IsNullOrWhiteSpace(crossBoundaryGoal.Description) ? crossBoundaryGoal.Source : crossBoundaryGoal.Description;
            return $"{ownRange} This bill has since moved to a newer segment (\"{label}\") — cascading forward would also update its own savings plan, running through {crossBoundaryTarget.DatePattern.Until:MMM d, yyyy}.";
        }

        return "";
    }

    /// <summary>[READS FILE] Every ManualEarmark under a finance_id that no plan in the given final coverage set still covers (RecurrenceRule.ActiveSpanContains — the same check ManualEarmark.Create/ManualEarmarkRepository.GetAll already use to validate one). Shared by PerformEarmarkSave (the real, about-to-be-deleted list, against the actual post-save set) and DescribeLetItBreakConsequence (a preview count, against a hypothetical set, nothing deleted).</summary>
    /// <param name="finalCoverage">Every EarMarkPattern expected to exist under financeId once this save (real or hypothetical) completes.</param>
    /// <param name="financeId">Which goal's own ManualEarmarks to check.</param>
    private IReadOnlyList<DateOnly> FindOrphanedManualEarmarkDates(IReadOnlyList<EarMarkPattern> finalCoverage, int financeId) =>
        _repositories.ManualEarmarks.GetAll()
            .Where(earmark => earmark.FinanceId == financeId)
            .Where(earmark => !finalCoverage.Any(plan => plan.DatePattern.ActiveSpanContains(earmark.Date)))
            .Select(earmark => earmark.Date)
            .ToList();

    /// <summary>[CALC] The safest, least-destructive answer to the EarMarkPattern-editing questions at once — used whenever no ConfirmImplicitChanges delegate is wired up. Matches the settled defaults: stays linked (never leaves an unannounced gap or overlap) and cascades forward (the settled default for a rate/schedule change). Always proceeds — there's no one here to cancel on.</summary>
    private static ImplicitChangeConfirmationAnswer DefaultEarmarkConfirmationAnswer() => new()
    {
        Proceed = true,
        ChoseStayLinked = true,
        ChoseCascadeForward = true,
    };

    /// <summary>[CALC] Computes the nine conditions above against the current Savings Plan and the proposed edit. Everything here reads off the current, already-saved forecast — it does not yet simulate what the forecast would look like with the proposed edit actually applied, so ChangeWarrantsSuggestions in particular only catches a plan that's already concerning today, not one this specific edit would newly make concerning (the same gap planning/22 §3 already named for the Expense form's own passive status indicator). internal (not private) so FinancePatternSaveConfirmationTests can call it directly ahead of a method that Run() itself can't reach yet (NarrowSurvivingPlanIfNeeded) without needing Run()'s own placeholder guidance step to run too.</summary>
    internal void DetermineConditions()
    {
        var saved = _repositories.FinancialPatterns.GetByFinanceId(_financeId);
        if (saved is null)
        {
            // Nothing saved yet under this FinanceId — a brand-new pattern.
            // Nothing to alter, no existing Savings Plan to consolidate.
            // Everything stays at its false default.
            return;
        }

        var forecast = _requestForecast();

        _recurrenceShapeChanged =
            saved.DatePattern.Frequency != _proposedPattern.DatePattern.Frequency ||
            saved.DatePattern.Interval != _proposedPattern.DatePattern.Interval ||
            !saved.DatePattern.ByDay.SequenceEqual(_proposedPattern.DatePattern.ByDay) ||
            !saved.DatePattern.ByMonthDay.SequenceEqual(_proposedPattern.DatePattern.ByMonthDay);

        // Start and ActiveFrom both move the pattern's own active span, which
        // is what can orphan an EarMarkPattern/ManualEarmark sitting outside
        // the new, narrower one (Item E) — grouped under "start_date" for
        // that reason, even though only Start itself moves an actual
        // occurrence date.
        _startChanged =
            saved.DatePattern.Start != _proposedPattern.DatePattern.Start ||
            saved.DatePattern.ActiveFrom != _proposedPattern.DatePattern.ActiveFrom;

        _amountChanged = saved.Amount != _proposedPattern.Amount;
        var restrictedFieldChanged = _recurrenceShapeChanged || _startChanged || _amountChanged;
        _isAmountOnlyChange = _amountChanged && !_startChanged && !_recurrenceShapeChanged;

        // 1.2.3.10.a5's own trigger — an occurrence on or before the as-of
        // date is exactly "an expected transaction on an expired page in the
        // past" under today's single-page model (planning/03, Ch.20's own
        // formal-vs-practical note). Checked against the SAVED pattern's own
        // occurrences, not the proposed one — what already happened is fixed
        // regardless of what's being typed now.
        var hasPastOccurrence = saved.DatePattern.GetOccurrences(to: forecast.AsOfDate).Count > 0;

        MightAlterPast = restrictedFieldChanged && hasPastOccurrence;
        IsChangeCritical = MightAlterPast;

        var savingsPlan = forecast.Book.EarMarkPatternsFor(_financeId);
        HasMultipleEarmarkPatterns = savingsPlan.Count > 1;

        // Widened 2026-08-17 from _recurrenceShapeChanged alone — found
        // while auditing every "keep separate" path for what actually
        // happens when it's chosen: on the retroactive-correction side
        // (Item E's "apply everywhere" answer), keeping plans separate is
        // only actually BUILT for an amount-only change (ScaleSurvivingPlansIfNeeded
        // rescales each one in place, no boundary work needed since dates
        // never move). A start_date change with no accompanying shape
        // change used to sail past this check unconsolidated and reach
        // PerformImplicitEarmarkChanges' own "keep separate, start_date
        // case" branch, which was never built either — silently doing
        // nothing at all, leaving every surviving plan's own ActiveStart
        // stale against the goal's newly-moved one, which throws the very
        // next time EarMarkPatternRepository.GetAll() re-validates
        // (structurally the same "back-boundary invariant" class of bug
        // fixed 2026-08-13 for the Until side, just never checked for
        // Start). Forcing consolidation here closes that silently — same
        // mechanism the shape-changed case already used, just triggered by
        // one more condition. The break-off side's own separate,
        // still-partly-open gap (PerformImplicitEarmarkChanges' own header
        // comment) is handled at execution time instead, not here — see
        // that method's own comment for why.
        // TODO (2026-08-17, DESIGN GOAL): this forces consolidation for a
        // start_date change as a SAFETY fix, not the intended final answer
        // — the author has confirmed "we eventually want to let the user
        // keep unconsolidated earmark patterns." See this class's own
        // header TODO (2026-08-17), item 2, and the open design questions
        // in redesign/memory/project_next_phase.md for the still-unresolved
        // shape of the real feature.
        ConsolidationNeeded = HasMultipleEarmarkPatterns && (_recurrenceShapeChanged || _startChanged);

        ChangeWarrantsSuggestions = forecast.PlanHealthStates
            .FirstOrDefault(health => health.FinanceId == _financeId)
            ?.IsWorthWarningAbout ?? false;
    }

    /// <summary>[READS FILE] Works out planning/27's own Phase 1 conditions — does this edit touch a chain boundary (Start/Until reaching a predecessor/successor), can Amount/shape cascade forward, can the trivial fields (Priority/Mandatory/Description/AutoRenew) cascade forward, and does a Source change disconnect this segment from its own chain. Same "read before anything is saved" timing as DetermineConditions' own sibling Determine* methods below — stores _chainContext (the saved pattern plus every other same-Source pattern) so PerformChainChangesIfApplicable can act on it later without re-reading, the same NarrowingPlan/ConsolidationPlan/BackTruncationPlan reasoning: PerformSave writes _proposedPattern under _financeId, and a read after that would see the NEW pattern, not the one this method needs to diff against. A no-op for a brand-new pattern (DetermineConditions' own early return already left _amountChanged/_recurrenceShapeChanged at their defaults too). internal for the same reason DetermineConditions is.</summary>
    internal void DetermineChainConditionsIfApplicable()
    {
        var saved = _repositories.FinancialPatterns.GetByFinanceId(_financeId);
        if (saved is null)
        {
            return; // brand-new pattern — nothing to compare against, no chain question
        }

        var forecast = _requestForecast();
        var allPatterns = forecast.Book.AllFinancialPatterns();

        // Same F27-style guard Phase 2 needed (RestructureFactory.SpansOverlap)
        // — nothing in this project designs for two FinancialPatterns sharing
        // a Source and overlapping on purpose, but FinancialPattern.Create
        // doesn't validate against it either, so this keeps an overlapping
        // same-Source pattern from ever being mistaken for a sequential chain
        // neighbor and absorbed/cascaded onto by PerformChainChangesIfApplicable
        // below, which trusts this same filtered list.
        var otherPatterns = allPatterns
            .Where(pattern => pattern.Source == saved.Source && pattern.FinanceId != saved.FinanceId && !BreakOffFactory.SpansOverlap(pattern, saved))
            .ToList();
        _chainContext = new ChainContext(saved, otherPatterns);

        var hasPredecessor = otherPatterns.Any(pattern => pattern.DatePattern.Start < saved.DatePattern.Start);
        var hasSuccessor = otherPatterns.Any(pattern => pattern.DatePattern.Start > saved.DatePattern.Start);

        // Start's own literal move — deliberately narrower than _startChanged
        // above, which also counts ActiveFrom (Item E's own narrowing
        // trigger, unrelated to chain contiguity: a predecessor's own Until
        // connects to THIS pattern's own Start, never its ActiveFrom lead-in).
        var startChanged = saved.DatePattern.Start != _proposedPattern.DatePattern.Start;
        var untilChanged = saved.DatePattern.Until != _proposedPattern.DatePattern.Until;
        var trivialFieldsChanged = saved.Priority != _proposedPattern.Priority
            || saved.Mandatory != _proposedPattern.Mandatory
            || saved.Description != _proposedPattern.Description
            || saved.AutoRenew != _proposedPattern.AutoRenew;

        // Gated on !IsChangeCritical — a deliberate scope limit, not an
        // oversight, found while wiring this in: when IsChangeCritical is
        // true, Item C's own question fires, and its DEFAULT answer ("break
        // off") means _proposedPattern is never actually saved under
        // _financeId at all — PerformSave's own guard skips straight past
        // it, and PerformImplicitEarmarkChanges saves a TRUNCATED original
        // plus a BRAND-NEW successor starting today instead. Whatever Start/
        // Until the user typed (the one that might reach into a predecessor/
        // successor) never lands on the existing chain the way Phase 1's own
        // mechanism assumes — asking "stay linked or break" against a value
        // that's about to be discarded would be actively misleading. The
        // "correct it everywhere" answer WOULD make Phase 1's own question
        // valid too — but that answer isn't known yet here (this method runs
        // BEFORE Item C's own confirmation fires, same as every other
        // Determine* call in this class), and composing "ask a chain
        // question that only sometimes turns out to matter, depending on a
        // DIFFERENT question's own answer on the same page" was judged too
        // easy to get subtly wrong to build under this session's own time
        // pressure — narrowed to the always-safe case instead. Worth a real
        // answer from the author before widening it, not a guess.
        TouchesChainBoundary = !IsChangeCritical && ((startChanged && hasPredecessor) || (untilChanged && hasSuccessor));
        ChangeCanCascade = !IsChangeCritical && (_amountChanged || _recurrenceShapeChanged) && hasSuccessor;
        TrivialFieldsCanCascade = !IsChangeCritical && trivialFieldsChanged && hasSuccessor;

        if (saved.Source != _proposedPattern.Source && (hasPredecessor || hasSuccessor))
        {
            var direction = (hasPredecessor, hasSuccessor) switch
            {
                (true, true) => "its own predecessor and successor",
                (true, false) => "its own predecessor",
                _ => "its own successor",
            };
            SourceChangeWarning = $"Changing the source disconnects this segment from {direction} in the chain — they'll no longer be found as continuations of each other.";
        }
    }

    /// <summary>[READS FILE] Works out the paycheck-association cascade's own trigger — does this edit change an income pattern's own schedule in a way that stops some other pattern's currently-active savings plan from still being paced against it. Same "read before anything is saved" timing as every Determine* call in this class: OldIncome has to be what _financeId's own pattern looked like BEFORE this edit. A no-op for a brand-new pattern or one that wasn't already income. Detection is AllocationPlanProposer.IsPacedAgainst applied twice — once against the OLD income to find every plan that WAS paced against it, once against the proposed NEW income to drop whichever ones still are — not gated on IsChangeCritical the way DetermineChainConditionsIfApplicable's own chain questions are: unlike that mechanism, this one only ever reads _proposedPattern's own schedule (never assumes it lands verbatim under _financeId), and BreakOffFactory's own successor-schedule construction keeps the same Frequency/Interval/ByDay/ByMonthDay/Until _proposedPattern has regardless of whether the edit ends up applied in place or broken off — so the schedule this checks against is the right one to check against either way.</summary>
    internal void DeterminePaycheckAssociationIfApplicable()
    {
        var saved = _repositories.FinancialPatterns.GetByFinanceId(_financeId);
        if (saved is not { Amount: > 0m })
        {
            return; // wasn't income before this edit — nothing could have been paced against it
        }

        var forecast = _requestForecast();

        // "Current" plan only, per FinanceId — an already-superseded past
        // segment being paced against the OLD schedule isn't a live
        // association worth re-asking about, and a genuinely concurrent set
        // (FindCurrentPlan returns null) gets the same "no single answer,
        // don't guess" treatment every other lookup in this document gives
        // it.
        var currentPlans = forecast.Book.AllEarMarkPatterns()
            .GroupBy(plan => plan.FinanceId)
            .Select(group => RestructureFactory.FindCurrentPlan(group.ToList()))
            .OfType<EarMarkPattern>()
            .ToList();

        var invalidatedPlans = AllocationPlanProposer.FindPlansPacedAgainst(saved, currentPlans)
            .Where(plan => !AllocationPlanProposer.IsPacedAgainst(plan, _proposedPattern))
            .ToList();

        if (invalidatedPlans.Count == 0)
        {
            return;
        }

        _paycheckAssociationContext = new PaycheckAssociationContext(saved, invalidatedPlans);
        PacedBillsCanCascade = true;
    }

    /// <summary>[READS FILE] Works out whether Item E's narrowing would apply, and if so, everything NarrowSurvivingPlanIfNeeded needs to actually carry it out — stored in _narrowingPlan rather than acted on here. Must run before PerformSave, not from within NarrowSurvivingPlanIfNeeded itself where this logic used to live: PerformSave writes the new goal first, and reading ManualEarmarks after that — before the plan has caught up to match the goal's new span — throws on the very rows this is trying to identify, since that read validates every row against its CURRENT plan. Reading here, before the goal changes at all, sidesteps the problem instead of working around it. Runs regardless of what UserChooseAlterPast will turn out to be — that isn't known yet when this runs — and simply goes unused on any path that doesn't end up needing it. internal for the same reason DetermineConditions is: tests exercising NarrowSurvivingPlanIfNeeded directly need to call this first too, exactly mirroring what Run() itself now does.</summary>
    internal void DetermineNarrowingPlanIfApplicable()
    {
        if (!IsChangeCritical || HasMultipleEarmarkPatterns)
        {
            return;
        }

        var forecast = _requestForecast();
        var plan = forecast.Book.EarMarkPatternsFor(_financeId).SingleOrDefault();
        if (plan is null)
        {
            return; // nothing to narrow
        }

        var newActiveStart = _proposedPattern.DatePattern.ActiveStart;
        if (newActiveStart <= plan.DatePattern.ActiveStart)
        {
            return; // the plan already starts late enough — nothing excluded
        }

        if (newActiveStart <= plan.DatePattern.Start)
        {
            // newActiveStart falls inside the plan's own existing lead-in
            // (ActiveFrom < Start) rather than past its literal Start —
            // PatternTruncation.StartOn only handles moving Start itself
            // forward, not narrowing a lead-in alone. Rare (a goal whose
            // plan was already saving ahead of it) — warned about rather
            // than silently doing nothing, 2026-08-17, same reasoning as
            // the more common case just below.
            // TODO (2026-08-17, CRASH RISK): same unresolved problem as the
            // before-AsOfDate branch just below — the goal still saves with
            // this new, earlier ActiveStart, but the plan is left at its
            // own old one, which can leave plan.ActiveStart < goal.ActiveStart,
            // an EarMarkPattern.Create validation violation that throws on
            // the next EarMarkPatternRepository.GetAll(). See this class's
            // own header TODO (2026-08-17), item 1, and the spawned
            // background task it names — not yet fixed.
            NarrowingLimitationWarning = $"The correction moves this earlier than {newActiveStart:MMM d, yyyy}, but the savings plan already started saving ahead of that date — narrowing just its own lead-in isn't supported yet, so the plan itself wasn't changed.";
            return;
        }

        if (newActiveStart < forecast.AsOfDate)
        {
            // Arguably the MORE common real-world trigger for Item E, not
            // an edge case — "actually this started earlier than today's
            // date" usually means earlier than today itself, not just
            // earlier than the plan's own current start. But JarBalanceOn
            // (via GetTimeline) can only read balances from AsOfDate
            // forward — nothing before the as-of date is a locked ledger;
            // W4/13a's own divergence (this project computes each day
            // fresh rather than storing history), not something this class
            // invented. Absorbing a balance here today would silently read
            // 0m regardless of what the jar actually held (JarBalanceOn's
            // own documented fallback for a day its timeline doesn't
            // reach) — real money would read as having vanished. Still not
            // absorbing a number known to be wrong (that needs either a
            // way to ask for a forecast as of a past date, or a different
            // source for this specific number — neither exists yet) — but
            // warned about now, 2026-08-17, instead of silently doing
            // nothing at all.
            // TODO (2026-08-17, CRASH RISK): the goal saves with this new,
            // earlier ActiveStart (see the line right above — the warning
            // is honest that "the bill or goal itself still saved"), but
            // the plan is left at its own old one, which leaves
            // plan.ActiveStart < goal.ActiveStart — an EarMarkPattern.Create
            // validation violation that throws on the very next
            // EarMarkPatternRepository.GetAll(), including the app's own
            // next startup. No safe fallback found yet (absorbing a wrong
            // balance was already ruled out above, deliberately). Spawned
            // as its own background task (task_ad0129c6) rather than
            // guessed at — see this class's own header TODO (2026-08-17),
            // item 1, for the starting direction. Not yet fixed.
            NarrowingLimitationWarning = $"This correction reaches back to {newActiveStart:MMM d, yyyy}, before today ({forecast.AsOfDate:MMM d, yyyy}) — there's no way yet to read what the savings plan's balance held that far back, so it wasn't narrowed to match. The bill or goal itself still saved with the corrected date.";
            return;
        }

        // Same "today, literally" reading PerformSingleSuccessorBreakOff
        // uses for its own cut date — read at the new boundary itself, not
        // the day before.
        var absorbedBalance = JarBalanceOn(newActiveStart, _financeId);

        // Safe here — nothing has been saved yet this Run(), so every
        // EarMarkPattern still validates against its own (unchanged) goal.
        var orphanedDates = _repositories.ManualEarmarks.GetAll()
            .Where(earmark => earmark.FinanceId == _financeId && earmark.Date < newActiveStart)
            .Select(earmark => earmark.Date)
            .ToList();

        _narrowingPlan = new NarrowingPlan(plan, newActiveStart, absorbedBalance, orphanedDates);
    }

    /// <summary>[READS FILE] Works out whether Item F's own multi-plan mechanisms (in-place consolidation, or amount-only scaling) would apply, and if so, everything either ConsolidateSurvivingPlansIfNeeded or ScaleSurvivingPlansIfNeeded needs to actually carry it out — stored in _consolidationPlan rather than acted on here. Runs regardless of what UserChooseAlterPast/UserChooseConsolidation/UserChoseScalePatterns will turn out to be — none is known yet when this runs — and simply goes unused on any path that doesn't end up needing it, the same "compute eagerly, apply conditionally" shape DetermineNarrowingPlanIfApplicable already uses. Also reads today's own jar here (ConsolidationPlan.CurrentJar) for the identical reason DetermineNarrowingPlanIfApplicable reads ManualEarmarks here — EarmarkConsolidation's own glut protection (2026-08-15) needs a live balance read, and reading it after PerformSave would read it against the ALREADY-edited goal's new schedule, not the one that actually produced whatever glut exists. internal for the same reason DetermineNarrowingPlanIfApplicable is — so a test can call this directly ahead of either downstream method.</summary>
    internal void DetermineConsolidationPlanIfApplicable()
    {
        if (!IsChangeCritical || !HasMultipleEarmarkPatterns)
        {
            return;
        }

        var forecast = _requestForecast();
        var survivingPlans = forecast.Book.EarMarkPatternsFor(_financeId);
        if (survivingPlans.Count == 0)
        {
            return; // nothing to fold or scale
        }

        // Safe here — nothing has been saved yet this Run(), so every
        // ManualEarmark still validates against its own (unchanged) plan.
        var manualEarmarks = _repositories.ManualEarmarks.GetAll()
            .Where(earmark => earmark.FinanceId == _financeId)
            .ToList();

        var currentJar = forecast.GetTimeline(_financeId)
            .LastOrDefault(entry => entry.Date <= forecast.AsOfDate)?.Snapshot.FundJars
            .FirstOrDefault(jar => jar.FinanceId == _financeId);

        _consolidationPlan = new ConsolidationPlan(
            survivingPlans,
            manualEarmarks,
            forecast.Book.AllFinancialPatterns(),
            currentJar,
            GetSavedPatternOrThrow().Amount);
    }

    /// <summary>[READS FILE] planning/25's Item G: works out which alternative plan shapes — beyond AllocationPlanProposer.Propose's own default — are genuinely available for this break-off's successor, stored in _planShapeCandidates for BuildConfirmationRequest to show and PerformSingleSuccessorBreakOff/PerformMultiPlanBreakOff to apply whichever gets chosen. Runs regardless of what UserChooseAlterPast will turn out to be — not known yet when this runs, same "compute eagerly, apply conditionally" shape DetermineConsolidationPlanIfApplicable already uses — and simply goes unused if the retroactive-correction side is chosen instead, where no fresh plan ever gets proposed. internal for the same reason its siblings are — so a test can call this directly ahead of PerformSingleSuccessorBreakOff.</summary>
    internal void DeterminePlanShapeCandidatesIfApplicable()
    {
        if (!IsChangeCritical)
        {
            return;
        }

        var forecast = _requestForecast();
        var existingPlan = RestructureFactory.FindCurrentPlan(forecast.Book.EarMarkPatternsFor(_financeId));
        if (existingPlan is null)
        {
            return; // nothing to draw an alternative shape from — no plan at all, or a genuinely concurrent set
        }

        var saved = GetSavedPatternOrThrow();
        var cutDate = forecast.AsOfDate;

        // The same successor shape PerformSingleSuccessorBreakOff will build
        // independently later, via the very same BuildSuccessorSchedule —
        // identity fields (Source/Description/Priority/Mandatory/AutoRenew)
        // carried over from the SAVED pattern, only Amount/schedule from the
        // proposed edit. NextFinanceId() is safe to call again here: nothing
        // gets saved between this and PerformSingleSuccessorBreakOff's own
        // later call to it on this same Run(), so both see the same value.
        var successor = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = forecast.Book.NextFinanceId(),
            Source = saved.Source,
            Description = saved.Description,
            DatePattern = RecurrenceRule.Create(BuildSuccessorSchedule(cutDate)),
            Amount = _proposedPattern.Amount,
            Priority = saved.Priority,
            Mandatory = saved.Mandatory,
            AutoRenew = saved.AutoRenew,
        });

        var allPatterns = forecast.Book.AllFinancialPatterns();
        var carriedOverJarBalance = JarBalanceOn(cutDate, _financeId);

        // Safe here — nothing has been saved yet this Run(), so every
        // ManualEarmark still validates against its own (unchanged) plan.
        var manualEarmarks = _repositories.ManualEarmarks.GetAll()
            .Where(earmark => earmark.FinanceId == _financeId)
            .ToList();

        var candidates = new List<PlanShapeCandidate>
        {
            // carriedOverJarBalance passed through here too (2026-08-15) —
            // without it, this candidate's own preview read StartingAllocation
            // = 0 even when a real balance/glut existed, understating what
            // BreakOffFactory.BreakOff would actually save if the user picked
            // it anyway (that method already applies the real balance
            // unconditionally, regardless of which candidate is chosen — see
            // its own header comment). Same real number "Keep the same
            // schedule"/"Keep the same amount" below already show.
            new("Recommended", AllocationPlanProposer.Propose(successor, allPatterns, cutDate, carriedOverJarBalance: carriedOverJarBalance)),
        };

        if (AllocationPlanProposer.ProposeSameSchedule(successor, existingPlan, carriedOverJarBalance, manualEarmarks, allPatterns, cutDate) is { } sameSchedule)
        {
            candidates.Add(new("Keep the same schedule", sameSchedule));
        }

        if (AllocationPlanProposer.ProposeSameAmount(successor, existingPlan, carriedOverJarBalance, manualEarmarks, allPatterns, cutDate) is { } sameAmount)
        {
            candidates.Add(new("Keep the same amount", sameAmount));
        }

        // Both new methods already return null when their own result would
        // be indistinguishable from the default — so more than one entry
        // here means a real choice exists, never a choice of duplicates.
        if (candidates.Count > 1)
        {
            _planShapeCandidates = candidates;
        }
    }

    /// <summary>[READS FILE] Works out whether any existing EarMarkPattern's own Until now exceeds the proposed pattern's — 3.11.2.a2 has to keep holding after ANY save, not just the ones this class already asks about — and if so, everything ApplyBackTruncationsIfNeeded needs to fix it: which plans exceed the new Until, and which ManualEarmarks now fall after it and need deleting. Stored in _backTruncations rather than acted on here, same "read before PerformSave writes anything" reasoning as DetermineNarrowingPlanIfApplicable's own note — reading ManualEarmarks after the goal's Until has already shortened would validate every row against the CURRENT (already-too-short) goal and throw on the very rows being identified. Runs unconditionally — unlike every other Determine*/condition here, this isn't gated on IsChangeCritical or HasMultipleEarmarkPatterns, since end_date shortening is exempt from needing to ASK (planning/25 Item A) but never exempt from needing the linked plan(s) kept valid. internal for the same reason DetermineConditions/DetermineNarrowingPlanIfApplicable are — so a test can call this directly ahead of ApplyBackTruncationsIfNeeded without needing Run()'s own confirmation step.</summary>
    internal void DetermineBackTruncationsIfApplicable()
    {
        var forecast = _requestForecast();
        var newUntil = _proposedPattern.DatePattern.Until;

        var plansExceedingNewUntil = forecast.Book.EarMarkPatternsFor(_financeId)
            .Where(plan => plan.DatePattern.Until > newUntil)
            .ToList();

        if (plansExceedingNewUntil.Count == 0)
        {
            return; // every existing plan already fits — nothing to fix
        }

        // Safe here — nothing has been saved yet this Run(), so every
        // ManualEarmark still validates against its own (unchanged) plan.
        var orphanedDates = _repositories.ManualEarmarks.GetAll()
            .Where(earmark => earmark.FinanceId == _financeId && earmark.Date > newUntil)
            .Select(earmark => earmark.Date)
            .ToList();

        _backTruncations = new BackTruncationPlan(plansExceedingNewUntil, orphanedDates);
    }

    /// <summary>[CALC] Names why Item F's own consolidation is being ANNOUNCED rather than asked — "" whenever ConsolidationNeeded is false. Was a single hardcoded XAML string until 2026-08-17, when ConsolidationNeeded was widened to also force consolidation for a start_date change, not just a recurrence-shape one — the old text ("because the schedule itself is changing") would have been actively wrong for a start-only edit.</summary>
    private string DescribeConsolidationForcedReason()
    {
        if (!ConsolidationNeeded)
        {
            return "";
        }

        var whatChanged = (_recurrenceShapeChanged, _startChanged) switch
        {
            (true, true) => "the schedule and start date are",
            (true, false) => "the schedule itself is",
            _ => "the start date itself is",
        };
        return $"Because {whatChanged} changing, its savings plans will be combined into one.";
    }

    /// <summary>[CALC] Warns, ahead of the choice rather than after it, that Item C's own "break off" answer always combines multiple plans regardless of what's picked in the Consolidation row — "" whenever there's nothing to warn about (no real choice being offered at all, or the edit isn't Critical so there's no break-off/alter-past ambiguity in the first place). Found 2026-08-17: "keep separate" through a break-off was never built (PerformImplicitEarmarkChanges' own comment) and used to silently save nothing at all when picked; fixed to fall back to consolidating instead, which makes this row's own "keep separate" option genuinely misleading without a caveat explaining when it doesn't actually apply.</summary>
    private string DescribeConsolidationCaveat()
    {
        if (!HasMultipleEarmarkPatterns || ConsolidationNeeded || !IsChangeCritical)
        {
            return "";
        }

        return "If you choose to apply this only from today forward (above), these plans will always be combined into one regardless of this choice — keeping them separate through a break-off isn't supported yet.";
    }

    /// <summary>[READS FILE] Builds what ConfirmImplicitChanges needs to render the Item B/C/E/F confirmation, plus planning/27's own Phase 1 questions (chain boundary, Amount/shape cascade, trivial-fields cascade, Source-change warning) — everything DetermineConditions/DetermineChainConditionsIfApplicable already worked out, plus a plain-language description of what changed. [READS FILE] because the chain-boundary/cascade previews below dry-run BreakOffFactory calls and read EarMarkPatternsFor for the absorb warning's own plan count — safe here, same as everywhere else in this class, since nothing has been saved yet this Run(). TODO: doesn't yet name the specific amount/date that would be orphaned by a retroactive correction (Item E's own "show the consequence, not just a yes/no" — mockups/editing-history-confirmation-mockups.html, Popup 1 · B) — the generic description below is a simpler first cut.</summary>
    private ImplicitChangeConfirmationRequest BuildConfirmationRequest() => new()
    {
        IsChangeCritical = IsChangeCritical,
        HasMultipleEarmarkPatterns = HasMultipleEarmarkPatterns,
        ConsolidationNeeded = ConsolidationNeeded,
        ConsolidationForcedReason = DescribeConsolidationForcedReason(),
        ConsolidationCaveat = DescribeConsolidationCaveat(),
        IsAmountOnlyChange = _isAmountOnlyChange,
        PlanShapeCandidates = _planShapeCandidates,
        Description = BuildDescription(),
        AlterPastConsequence = DescribeAlterPastConsequence(),
        NarrowingLimitationWarning = NarrowingLimitationWarning,
        // planning/27's own EarMarkPattern-chain questions never apply to a
        // FinancialPattern-editing request — TouchesChainBoundary/
        // ChangeCanCascade below are this request's own, Phase 1 versions.
        PlanTouchesChainBoundary = false,
        PlanChangeCanCascade = false,
        TouchesChainBoundary = TouchesChainBoundary,
        ChangeCanCascade = ChangeCanCascade,
        TrivialFieldsCanCascade = TrivialFieldsCanCascade,
        SourceChangeWarning = SourceChangeWarning,
        StayLinkedWarning = TouchesChainBoundary ? DescribeChainStayLinkedConsequence() : "",
        LetItBreakWarning = TouchesChainBoundary ? DescribeChainLetItBreakConsequence() : "",
        CascadeDescription = ChangeCanCascade ? DescribeChainCascadeConsequence() : "",
        TrivialFieldsCascadeDescription = TrivialFieldsCanCascade ? DescribeChainTrivialFieldsCascadeConsequence() : "",
        PacedBillsCanCascade = PacedBillsCanCascade,
        PacedBillsCascadeDescription = PacedBillsCanCascade ? DescribePacedBillsCascadeConsequence() : "",
    };

    /// <summary>[READS FILE] Previews what "stay linked" would actually do to _proposedPattern's own neighbors, for the confirmation row's own warning slot — "" for a plain, contiguous nudge (never destructive), or a real sentence naming which segment(s) would be absorbed. Unlike the EarMarkPattern-chain case, absorbing a whole FinancialPattern genuinely deletes everything under its own now-gone FinanceId — its own EarMarkPattern chain (if any) and every ManualEarmark tied to it, not just the row itself (this document's own explicit distinction) — named here, not glossed over, matching the "show the consequence" standard. [READS FILE] to count each absorbed segment's own EarMarkPatterns.</summary>
    private string DescribeChainStayLinkedConsequence()
    {
        if (_chainContext is not { } context)
        {
            return "";
        }

        var predecessors = context.OtherPatterns.Where(pattern => pattern.DatePattern.Start < context.Saved.DatePattern.Start).ToList();
        var successors = context.OtherPatterns.Where(pattern => pattern.DatePattern.Start > context.Saved.DatePattern.Start).ToList();
        var absorbed = new List<FinancialPattern>();

        if (context.Saved.DatePattern.Start != _proposedPattern.DatePattern.Start && predecessors.Count > 0)
        {
            absorbed.AddRange(BreakOffFactory.ExtendStart(_proposedPattern, predecessors, _proposedPattern.DatePattern.Start).Absorbed);
        }

        if (context.Saved.DatePattern.Until != _proposedPattern.DatePattern.Until && successors.Count > 0)
        {
            absorbed.AddRange(BreakOffFactory.ExtendUntil(_proposedPattern, successors, _proposedPattern.DatePattern.Until).Absorbed);
        }

        if (absorbed.Count == 0)
        {
            return ""; // a plain nudge — never destructive, nothing to warn about
        }

        var forecast = _requestForecast();
        var ordered = absorbed.OrderBy(pattern => pattern.DatePattern.Start).ToList();
        var descriptions = ordered.Select(pattern =>
        {
            var planCount = forecast.Book.EarMarkPatternsFor(pattern.FinanceId).Count;
            var label = string.IsNullOrWhiteSpace(pattern.Description) ? pattern.Source : pattern.Description;
            var range = $"{pattern.DatePattern.Start:MMM d, yyyy} – {pattern.DatePattern.Until:MMM d, yyyy}";
            return planCount > 0
                ? $"\"{label}\" ({range}), along with its own {planCount} savings plan{(planCount == 1 ? "" : "s")} and any money saved toward it"
                : $"\"{label}\" ({range})";
        }).ToList();

        return ordered.Count == 1
            ? $"This will permanently delete {descriptions[0]} — none of its own values will be kept."
            : $"This will permanently delete {ordered.Count} segments: {string.Join("; ", descriptions)} — none of their own values will be kept.";
    }

    /// <summary>[CALC] Previews what "let the chain break" would actually leave behind, for the confirmation row's own warning slot — always a real sentence when TouchesChainBoundary is true. Names whether a gap or overlap forms (BreakOffFactory.SpansOverlap decides which). Doesn't need to name any EarMarkPattern/ManualEarmark consequence for _financeId's own linked plan the way the EarMarkPattern-chain version does — that's already covered unconditionally by DetermineBackTruncationsIfApplicable/DetermineNarrowingPlanIfApplicable elsewhere in this class, not something this method needs to duplicate. The NEIGHBOR itself is simply left untouched by "let it break," so there's nothing of its own to report either.</summary>
    private string DescribeChainLetItBreakConsequence()
    {
        if (_chainContext is not { } context)
        {
            return "";
        }

        var predecessors = context.OtherPatterns.Where(pattern => pattern.DatePattern.Start < context.Saved.DatePattern.Start).ToList();
        var successors = context.OtherPatterns.Where(pattern => pattern.DatePattern.Start > context.Saved.DatePattern.Start).ToList();
        var consequences = new List<string>();

        if (context.Saved.DatePattern.Start != _proposedPattern.DatePattern.Start && predecessors.Count > 0)
        {
            var predecessor = predecessors.OrderByDescending(pattern => pattern.DatePattern.Start).First();
            consequences.Add(BreakOffFactory.SpansOverlap(_proposedPattern, predecessor)
                ? $"it will overlap with the segment before it ({predecessor.DatePattern.Start:MMM d, yyyy} – {predecessor.DatePattern.Until:MMM d, yyyy})"
                : $"a gap will open before it, from {predecessor.DatePattern.Until.AddDays(1):MMM d, yyyy} to {_proposedPattern.DatePattern.Start.AddDays(-1):MMM d, yyyy}");
        }

        if (context.Saved.DatePattern.Until != _proposedPattern.DatePattern.Until && successors.Count > 0)
        {
            var successor = successors.OrderBy(pattern => pattern.DatePattern.Start).First();
            consequences.Add(BreakOffFactory.SpansOverlap(_proposedPattern, successor)
                ? $"it will overlap with the segment after it ({successor.DatePattern.Start:MMM d, yyyy} – {successor.DatePattern.Until:MMM d, yyyy})"
                : $"a gap will open after it, from {_proposedPattern.DatePattern.Until.AddDays(1):MMM d, yyyy} to {successor.DatePattern.Start.AddDays(-1):MMM d, yyyy}");
        }

        if (consequences.Count == 0)
        {
            // Shouldn't happen when TouchesChainBoundary is true (some
            // gap/overlap always results from breaking) — never claim a
            // consequence isn't real just to force non-empty text.
            return "";
        }

        var sentence = string.Join("; ", consequences) + ".";
        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    /// <summary>[CALC] Names the date range an Amount/shape cascade would actually reach across the rest of the chain, for the confirmation row's own always-shown description — same "must show, plainly, the date range... and how far the cascade reaches" requirement round 2 of the small questions settled, mirrored from the EarMarkPattern-chain case.</summary>
    private string DescribeChainCascadeConsequence()
    {
        if (_chainContext is not { } context)
        {
            return "";
        }

        var successors = context.OtherPatterns.Where(pattern => pattern.DatePattern.Start > context.Saved.DatePattern.Start).ToList();
        if (successors.Count == 0)
        {
            return "";
        }

        var furthest = successors.Max(pattern => pattern.DatePattern.Until);
        return successors.Count == 1
            ? $"This edit covers {_proposedPattern.DatePattern.Start:MMM d, yyyy} – {_proposedPattern.DatePattern.Until:MMM d, yyyy}. Cascading forward would also update the segment running through {furthest:MMM d, yyyy}."
            : $"This edit covers {_proposedPattern.DatePattern.Start:MMM d, yyyy} – {_proposedPattern.DatePattern.Until:MMM d, yyyy}. Cascading forward would also update {successors.Count} later segments, through {furthest:MMM d, yyyy}.";
    }

    /// <summary>[CALC] Names how many later segments the trivial-fields cascade (Priority/Mandatory/Description/AutoRenew) would reach — Phase 1's own third question, with no EarMarkPattern equivalent. Simpler than DescribeChainCascadeConsequence's own text since there's no "date range" concept for fields that don't affect timing at all.</summary>
    private string DescribeChainTrivialFieldsCascadeConsequence()
    {
        if (_chainContext is not { } context)
        {
            return "";
        }

        var successors = context.OtherPatterns.Where(pattern => pattern.DatePattern.Start > context.Saved.DatePattern.Start).ToList();
        return successors.Count switch
        {
            0 => "",
            1 => "This would also update the segment right after it.",
            _ => $"This would also update {successors.Count} later segments.",
        };
    }

    /// <summary>[READS FILE] Names which bill(s)/goal(s) the paycheck-association cascade would re-pace — singly by name for one, or a combined count-plus-list for several, matching the settled "singly for one associated bill or as a combined 'update all' option for several" language. [READS FILE] to resolve each invalidated plan's own FinanceId back to its owning FinancialPattern's name.</summary>
    private string DescribePacedBillsCascadeConsequence()
    {
        if (_paycheckAssociationContext is not { } context)
        {
            return "";
        }

        var billsByFinanceId = _requestForecast().Book.AllFinancialPatterns().ToDictionary(pattern => pattern.FinanceId);
        var names = context.InvalidatedPlans
            .Select(plan => billsByFinanceId.TryGetValue(plan.FinanceId, out var bill)
                ? (string.IsNullOrWhiteSpace(bill.Description) ? bill.Source : bill.Description)
                : $"finance id {plan.FinanceId}")
            .ToList();

        return names.Count == 1
            ? $"\"{names[0]}\"'s savings plan was paced against this paycheck's old schedule. Update it to match the new schedule too?"
            : $"{names.Count} savings plans were paced against this paycheck's old schedule: {string.Join(", ", names.Select(name => $"\"{name}\""))}. Update all of them to match the new schedule too?";
    }

    /// <summary>[CALC] A plain-language sentence naming what's changing and why it needs asking — no raw field names, no finance_id.</summary>
    private string BuildDescription()
    {
        var label = string.IsNullOrWhiteSpace(_proposedPattern.Description) ? _proposedPattern.Source : _proposedPattern.Description;

        var changedFields = new List<string>();
        if (_startChanged) changedFields.Add("start date");
        if (_amountChanged) changedFields.Add("amount");
        if (_recurrenceShapeChanged) changedFields.Add("schedule");
        var whatChanged = changedFields.Count > 0 ? string.Join(" and ", changedFields) : "this";

        return IsChangeCritical
            ? $"You're changing the {whatChanged} for \"{label}\", and it already has payments recorded."
            : $"\"{label}\" already has more than one savings plan.";
    }

    /// <summary>[CALC] Names the specific savings-plan consequence of choosing "apply it everywhere" — the absorbed balance and/or deleted manual earmarks _narrowingPlan already worked out — per planning/25's own "show the consequence, not just a yes/no" requirement (Item E's own long-standing TODO, closed 2026-08-17). "" whenever there's nothing concrete to name: no narrowing plan at all (Trivial edit, multi-plan goal, or NarrowingLimitationWarning's own case where narrowing couldn't happen), or a narrowing that neither absorbs a real balance nor orphans anything.</summary>
    private string DescribeAlterPastConsequence()
    {
        if (_narrowingPlan is not { } plan)
        {
            return "";
        }

        var consequences = new List<string>();
        if (plan.AbsorbedBalance > 0m)
        {
            consequences.Add($"the savings plan will absorb the {plan.AbsorbedBalance:C} already saved before {plan.NewActiveStart:MMM d, yyyy}");
        }

        if (plan.OrphanedManualEarmarkDates.Count > 0)
        {
            consequences.Add(plan.OrphanedManualEarmarkDates.Count == 1
                ? $"1 manual earmark dated {plan.OrphanedManualEarmarkDates[0]:MMM d, yyyy} will be deleted"
                : $"{plan.OrphanedManualEarmarkDates.Count} manual earmarks will be deleted");
        }

        if (consequences.Count == 0)
        {
            return "";
        }

        var sentence = string.Join("; ", consequences) + ".";
        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    /// <summary>[CALC] The safest, least-destructive answer to every implicit-change question at once — used whenever no ConfirmImplicitChanges delegate is wired up (most tests, and any host that hasn't connected a real popup). Preserves history (break off, don't alter the past), keeps existing plans separate rather than silently merging them, and never rescales money the user didn't confirm touching. Always proceeds — there's no one here to cancel on.</summary>
    private static ImplicitChangeConfirmationAnswer DefaultConfirmationAnswer() => new()
    {
        Proceed = true,
        ChooseAlterPast = false,
        ChooseConsolidation = false,
        ChoseScalePatterns = false,
    };

    /// <summary>[READS FILE] When more than one EarMarkPattern survives and the user clicked Save and Plan, asks which one to actually open next. Real for the unambiguous cases (none, or exactly one); the genuinely ambiguous case still needs the actual disambiguation popup. Reads the repository directly, not the live forecast: _requestForecast may be a cached accessor (MainWindow's own EnsureForecast caches until something explicitly recomputes), so it can't be trusted to reflect what PerformSave/PerformImplicitEarmarkChanges just wrote a moment ago. The repository has no such cache — matches what MainWindow's own pre-migration callback already did here (_earMarkPatterns.GetAll()), not a new choice. Looks under _navigationFinanceId, not _financeId directly — after a break-off, _financeId's own EarMarkPattern is the truncated, no-longer-current predecessor.</summary>
    /// <returns>The EarMarkPattern to open, or null if there isn't one yet.</returns>
    private EarMarkPattern? AskWhichEarmarkPatternToOpen()
    {
        var savingsPlan = _repositories.EarMarkPatterns.GetAll()
            .Where(pattern => pattern.FinanceId == _navigationFinanceId)
            .ToList();
        if (savingsPlan.Count <= 1)
        {
            // Nothing to disambiguate — matches the existing, already-built
            // disambiguation popup's own rule (planning/21): it only ever
            // fires when more than one EarMarkPattern exists for the goal.
            return savingsPlan.Count == 1 ? savingsPlan[0] : null;
        }

        // BUILT 2026-08-17 — more than one EarMarkPattern survives, exactly
        // the existing FinancialPatternPickerWindow-style disambiguation
        // popup's own job, incorporated here via the same delegate idiom as
        // ConfirmImplicitChanges/ShowSuggestion rather than fired
        // separately, so this class stays WPF-free. Falls back to the
        // first match — the original placeholder — whenever nothing's
        // wired up (most tests, and any host that hasn't connected one),
        // same "safest default when nothing's connected" reasoning
        // DefaultConfirmationAnswer already applies elsewhere in this class.
        return PickEarmarkPattern?.Invoke(savingsPlan) ?? savingsPlan[0];
    }

    /// <summary>[READS FILE] The Concerning popup — BUILT 2026-08-17, deliberately minimal (see ShowSuggestion's own field comment for what's still not built on top of this). Reads a fresh PlanHealthState/FundJar off _navigationFinanceId (not _financeId — after a break-off, _financeId's own state is for the truncated, no-longer-current predecessor, same reasoning AskWhichEarmarkPatternToOpen's own field comment already gives) and hands PlanHealthMessages' own existing sentence — the same wording the Earmark form's own passive Summary aside already uses — to ShowSuggestion. A silent no-op whenever there's no PlanHealthState or no FundJar to read yet (a brand-new plan with nothing computed for it), or no delegate wired up.</summary>
    private void AskForSuggestions()
    {
        var forecast = _requestForecast();
        if (forecast.PlanHealthStates.FirstOrDefault(health => health.FinanceId == _navigationFinanceId) is not { } state)
        {
            return;
        }

        if (JarOn(forecast.AsOfDate, _navigationFinanceId) is not { } jar)
        {
            return;
        }

        ShowSuggestion?.Invoke(PlanHealthMessages.CurrentJarStateLine(jar, state));
    }

    /// <summary>[WRITES FILE] Persists the FinancialPattern side of whatever Item C (planning/25's mechanism: reusing BreakOffFactory to end the old pattern and start a genuinely new one, once an edit would touch already-occurred history) decided — the proposed edit saved under the same FinanceId for a plain edit or a retroactive "correct it everywhere" choice, or nothing at all when a break-off is about to run instead. A break-off's FinancialPattern-side save is two rows under two different FinanceIds (the truncated original plus a brand-new successor), not _proposedPattern saved as-is under _financeId — PerformImplicitEarmarkChanges saves both when that's the path taken.</summary>
    private void PerformSave()
    {
        if (IsChangeCritical && !UserChooseAlterPast)
        {
            return;
        }

        _repositories.FinancialPatterns.Save(_proposedPattern, _accountId);
        ApplyBackTruncationsIfNeeded();
    }

    /// <summary>[WRITES FILE] Carries out the fix DetermineBackTruncationsIfApplicable already worked out (before anything was saved): deletes any ManualEarmark now dated after the goal's own new Until, then brings every EarMarkPattern that still exceeds it back in line — truncated via PatternTruncation.EndOn (the same domain primitive a plain stop/break-off already uses to keep a plan's Until from exceeding its goal's) when it still has some active span left inside the new range, or deleted outright when its own Start is already past the new Until (EndOn can't shorten a plan to end before it begins — that plan was never going to contribute anything under the new range at all). A no-op whenever _backTruncations is null — every existing plan already fit, or DetermineBackTruncationsIfApplicable was never called. Called from inside PerformSave, right after the FinancialPattern save it depends on — see this class's own "Back-boundary invariant" header note for why this runs unconditionally rather than only alongside Items B/E/F's own confirmation.</summary>
    private void ApplyBackTruncationsIfNeeded()
    {
        if (_backTruncations is not { } fix)
        {
            return;
        }

        foreach (var date in fix.OrphanedManualEarmarkDates)
        {
            _repositories.ManualEarmarks.Delete(_financeId, date);
        }

        foreach (var plan in fix.PlansExceedingNewUntil)
        {
            if (plan.DatePattern.Start > _proposedPattern.DatePattern.Until)
            {
                _repositories.EarMarkPatterns.Delete(plan.FinanceId, plan.DatePattern.Start);
                continue;
            }

            var truncated = PatternTruncation.EndOn(_proposedPattern, plan, _proposedPattern.DatePattern.Until).Plan!;
            _repositories.EarMarkPatterns.Save(truncated);
        }
    }

    /// <summary>[WRITES FILE] Carries out whatever Item C/D/E/F (planning/25 — the mechanism, the gap-folding rule, the retroactive-correction ruling, and the multiple-EarMarkPatterns ruling, respectively) decided. Real today for: a break-off with 0 or 1 existing EarMarkPattern (PerformSingleSuccessorBreakOff); a break-off that consolidates more than one (PerformMultiPlanBreakOff, forced or chosen); a single-plan retroactive correction's own narrowing (NarrowSurvivingPlanIfNeeded); a multi-plan retroactive correction's own in-place consolidation, forced or chosen (ConsolidateSurvivingPlansIfNeeded, backed by EarmarkConsolidation.Consolidate); and — new 2026-08-14 — that same retroactive-correction side's own amount-only scaling, when the plans are kept separate rather than consolidated (ScaleSurvivingPlansIfNeeded, backed by the new EarmarkScaling.Scale). Still TODO: keeping multiple plans separate on the BREAK-OFF side at all (with or without scaling), and keeping them separate on the retroactive-correction side for a start_date change specifically (only the amount-only case is resolved).</summary>
    /// <summary>[WRITES FILE] Carries out whatever DetermineChainConditionsIfApplicable/the confirmation decided for planning/27's own Phase 1 — resolves the chain boundary (stay linked, via BreakOffFactory.ExtendStart/ExtendUntil, or left broken), the Amount/shape cascade, and the trivial-fields cascade against the rest of the same-Source chain. A no-op whenever _chainContext is null (brand-new pattern) or none of TouchesChainBoundary/ChangeCanCascade/TrivialFieldsCanCascade are true — which, per DetermineChainConditionsIfApplicable's own !IsChangeCritical gate, also means _proposedPattern is guaranteed to have actually been saved verbatim under _financeId by the PerformSave call just before this one, not superseded by a break-off. Absorbing a neighbor here means deleting its WHOLE FinancialPattern row, plus every EarMarkPattern and ManualEarmark under its own FinanceId (EarMarkPatternRepository.Delete(financeId)'s own existing two-table cascade) — the FinancialPattern-level absorb this document's own text describes as "genuinely orphaning everything," unlike the EarMarkPattern-chain case where nothing is orphaned.</summary>
    private void PerformChainChangesIfApplicable()
    {
        if (_chainContext is not { } context || (!TouchesChainBoundary && !ChangeCanCascade && !TrivialFieldsCanCascade))
        {
            return;
        }

        var toSave = new Dictionary<int, FinancialPattern>();
        var toDelete = new List<int>();

        var predecessors = context.OtherPatterns.Where(pattern => pattern.DatePattern.Start < context.Saved.DatePattern.Start).ToList();
        var successors = context.OtherPatterns.Where(pattern => pattern.DatePattern.Start > context.Saved.DatePattern.Start).ToList();

        if (TouchesChainBoundary && UserChoseStayLinked)
        {
            if (context.Saved.DatePattern.Start != _proposedPattern.DatePattern.Start && predecessors.Count > 0)
            {
                var result = BreakOffFactory.ExtendStart(_proposedPattern, predecessors, _proposedPattern.DatePattern.Start);
                foreach (var absorbed in result.Absorbed)
                {
                    toDelete.Add(absorbed.FinanceId);
                }

                if (result.AdjustedNeighbor is { } adjusted)
                {
                    toSave[adjusted.FinanceId] = adjusted;
                }
            }

            if (context.Saved.DatePattern.Until != _proposedPattern.DatePattern.Until && successors.Count > 0)
            {
                var result = BreakOffFactory.ExtendUntil(_proposedPattern, successors, _proposedPattern.DatePattern.Until);
                foreach (var absorbed in result.Absorbed)
                {
                    toDelete.Add(absorbed.FinanceId);
                }

                if (result.AdjustedNeighbor is { } adjusted)
                {
                    toSave[adjusted.FinanceId] = adjusted;
                }
            }
        }

        if (ChangeCanCascade && UserChoseCascadeForward)
        {
            // Cascades onto whatever successors are still standing after
            // boundary resolution above — including one just date-adjusted
            // there, whose own dates CascadeForward leaves untouched, only
            // its Amount/shape change. FinanceId is stable (unlike
            // EarMarkPattern's own composite key), so toSave keyed by it
            // needs no "does the key change" handling the EarMarkPattern-
            // chain version of this method has to do.
            var stillStanding = successors
                .Where(pattern => !toDelete.Contains(pattern.FinanceId))
                .Select(pattern => toSave.TryGetValue(pattern.FinanceId, out var adjusted) ? adjusted : pattern)
                .ToList();

            foreach (var cascaded in BreakOffFactory.CascadeForward(_proposedPattern.DatePattern, _proposedPattern.Amount, stillStanding))
            {
                toSave[cascaded.FinanceId] = cascaded;
            }
        }

        if (TrivialFieldsCanCascade && UserChoseCascadeTrivialFields)
        {
            // Re-derives stillStanding rather than reusing the Amount/shape
            // cascade's own copy above — a successor could have been
            // updated by that cascade already, and this needs the LATEST
            // version (its own new Amount/shape) to build on top of, not the
            // pre-cascade original.
            var stillStanding = successors
                .Where(pattern => !toDelete.Contains(pattern.FinanceId))
                .Select(pattern => toSave.TryGetValue(pattern.FinanceId, out var adjusted) ? adjusted : pattern)
                .ToList();

            foreach (var cascaded in BreakOffFactory.CascadeTrivialFieldsForward(_proposedPattern, stillStanding))
            {
                toSave[cascaded.FinanceId] = cascaded;
            }
        }

        foreach (var pattern in toSave.Values)
        {
            var accountId = _repositories.FinancialPatterns.GetAccountId(pattern.FinanceId) ?? _accountId;
            _repositories.FinancialPatterns.Save(pattern, accountId);
        }

        foreach (var financeId in toDelete.Distinct())
        {
            // EarMarkPatternRepository.Delete(financeId) already cascades to
            // ManualEarmarks (its own established DELETE FROM ManualEarmarks;
            // DELETE FROM EarMarkPatterns pair) — this just adds the
            // FinancialPattern row itself on top, the same three-table order
            // FinancialPatternRepository.DeleteByTransferId already uses.
            _repositories.EarMarkPatterns.Delete(financeId);
            _repositories.FinancialPatterns.Delete(financeId);
        }
    }

    /// <summary>[WRITES FILE] Carries out the paycheck-association cascade: for every OTHER FinancialPattern whose currently-active savings plan DeterminePaycheckAssociationIfApplicable found no longer paced against this now-edited income, re-proposes that plan from scratch (AllocationPlanProposer.Propose) against the live, post-save state of the household, carrying its own real current jar balance forward, and deletes any ManualEarmark the new plan's own (possibly narrower) span no longer covers. A no-op whenever _paycheckAssociationContext is null or the user declined. Reads a fresh forecast rather than reusing anything captured earlier — deliberately, not an oversight: unlike PerformChainChangesIfApplicable's own _proposedPattern, this needs whatever income schedule actually ended up live, whether this edit landed in place or broke off into a brand-new successor FinanceId (see DeterminePaycheckAssociationIfApplicable's own doc comment for why either path leaves the same schedule to re-pace against). Deletes each bill's own superseded plan by its OLD (FinanceId, Start) key before saving the new one — EarMarkPattern's own persistence key is the composite (FinanceId, StartDate), so a re-propose landing on a different Start would otherwise leave the stale row behind rather than replacing it.</summary>
    private void PerformPaycheckAssociationCascadeIfApplicable()
    {
        if (_paycheckAssociationContext is not { } context || !UserChoseToRepaceBills)
        {
            return;
        }

        var forecast = _requestForecast();
        var allPatterns = forecast.Book.AllFinancialPatterns();
        var billsByFinanceId = allPatterns.ToDictionary(pattern => pattern.FinanceId);

        foreach (var oldPlan in context.InvalidatedPlans)
        {
            if (!billsByFinanceId.TryGetValue(oldPlan.FinanceId, out var bill))
            {
                continue; // the bill itself is gone — nothing left to re-pace
            }

            var carriedOverJarBalance = JarBalanceOn(forecast.AsOfDate, oldPlan.FinanceId);
            var proposal = AllocationPlanProposer.Propose(bill, allPatterns, forecast.AsOfDate, carriedOverJarBalance: carriedOverJarBalance);

            // Same "read before write" reasoning FindOrphanedManualEarmarkDates'
            // own call sites elsewhere in this class already follow — safe
            // here because nothing has touched this bill's own plan or
            // ManualEarmarks yet, even though the income's own save has
            // already happened.
            var otherExistingPlans = forecast.Book.EarMarkPatternsFor(oldPlan.FinanceId)
                .Where(plan => plan.DatePattern.Start != oldPlan.DatePattern.Start)
                .ToList();
            var finalCoverage = otherExistingPlans.Append(proposal.Plan).ToList();
            var orphanedDates = FindOrphanedManualEarmarkDates(finalCoverage, oldPlan.FinanceId);

            _repositories.EarMarkPatterns.Delete(oldPlan.FinanceId, oldPlan.DatePattern.Start);
            foreach (var date in orphanedDates)
            {
                _repositories.ManualEarmarks.Delete(oldPlan.FinanceId, date);
            }

            _repositories.EarMarkPatterns.Save(proposal.Plan);
            if (proposal.StartingEarmark is { } startingEarmark)
            {
                _repositories.ManualEarmarks.Save(startingEarmark);
            }
        }
    }

    /// <summary>[WRITES FILE] Carries out whatever Item C/D/E/F (planning/25 — the mechanism, the gap-folding rule, the retroactive-correction ruling, and the multiple-EarMarkPatterns ruling, respectively) decided. Real today for: a break-off with 0 or 1 existing EarMarkPattern (PerformSingleSuccessorBreakOff); a break-off that consolidates more than one (PerformMultiPlanBreakOff, forced or chosen); a single-plan retroactive correction's own narrowing (NarrowSurvivingPlanIfNeeded); a multi-plan retroactive correction's own in-place consolidation, forced or chosen (ConsolidateSurvivingPlansIfNeeded, backed by EarmarkConsolidation.Consolidate); and — new 2026-08-14 — that same retroactive-correction side's own amount-only scaling, when the plans are kept separate rather than consolidated (ScaleSurvivingPlansIfNeeded, backed by the new EarmarkScaling.Scale). Still TODO: keeping multiple plans separate on the BREAK-OFF side at all (with or without scaling), and keeping them separate on the retroactive-correction side for a start_date change specifically (only the amount-only case is resolved).</summary>
    private void PerformImplicitEarmarkChanges()
    {
        if (IsChangeCritical && !UserChooseAlterPast)
        {
            if (HasMultipleEarmarkPatterns)
            {
                // FIXED 2026-08-17 — used to branch on ConsolidationNeeded/
                // UserChooseConsolidation here, same as the retroactive-
                // correction side below, and silently saved NOTHING AT ALL
                // (not even the FinancialPattern itself — PerformSave's own
                // guard already skips it whenever IsChangeCritical &&
                // !UserChooseAlterPast) whenever the user picked "keep
                // separate." Keeping plans separate through a break-off
                // isn't built — each existing plan would need its own
                // successor surviving under the SAME new finance_id (F27
                // already allows more than one EarMarkPattern per
                // finance_id), not one combined fresh plan; a genuinely
                // different, more complex mechanism than PerformMultiPlanBreakOff,
                // not designed or built. Always consolidating here instead
                // closes that silent no-op — DetermineConditions' own
                // widened ConsolidationNeeded (2026-08-17) already forces
                // the ask into a plain announcement for any start_date/shape
                // change, so this is only ever reachable for an amount-only
                // edit today, and BuildConfirmationRequest's own
                // ConsolidationCaveat tells the user ahead of time that a
                // break-off always combines plans regardless of this
                // choice, so falling back here is never a surprise.
                // TODO (2026-08-17, DESIGN GOAL): this always-consolidate
                // fallback is a SAFETY fix, not the intended final answer —
                // the author has confirmed "we eventually want to let the
                // user keep unconsolidated earmark patterns," including on
                // this break-off side. See this class's own header TODO
                // (2026-08-17), item 2.
                PerformMultiPlanBreakOff();
                return;
            }

            PerformSingleSuccessorBreakOff();
            return;
        }

        if (IsChangeCritical && UserChooseAlterPast && !HasMultipleEarmarkPatterns)
        {
            NarrowSurvivingPlanIfNeeded();
            return;
        }

        if (IsChangeCritical && UserChooseAlterPast && HasMultipleEarmarkPatterns)
        {
            if (ConsolidationNeeded || UserChooseConsolidation)
            {
                ConsolidateSurvivingPlansIfNeeded();
                return;
            }

            // Keeping the plans SEPARATE, on the retroactive-correction side.
            // The amount-only case (_isAmountOnlyChange) is real now — dates
            // never move here, so there's no boundary work, only each plan's
            // own rate to optionally rescale (EarmarkScaling.Scale). Guarded
            // by _isAmountOnlyChange, not just UserChoseScalePatterns, so a
            // stray true from a test (or a future caller) can't apply scaling
            // logic to a scenario it was never designed for — the popup's own
            // ScaleCheckBox is already Collapsed, and so unreachable, whenever
            // IsAmountOnlyChange is false.
            if (UserChoseScalePatterns && _isAmountOnlyChange)
            {
                ScaleSurvivingPlansIfNeeded();
                return;
            }

            // Not scaling is a genuine, correct no-op here — reaching this
            // line at all now GUARANTEES the change was amount-only:
            // ConsolidationNeeded was widened 2026-08-17 to also force
            // consolidation for a start_date change (DetermineConditions'
            // own comment), so the branch this used to fall through to for
            // that case — each surviving plan silently left with a stale,
            // now-invalid ActiveStart, a real crash risk the next time
            // EarMarkPatternRepository.GetAll() ran — is unreachable now,
            // not just unbuilt. Each surviving plan's own dates and rate
            // really are still exactly what they were, still valid against
            // the unchanged-in-that-respect goal, whenever this line runs.
            return;
        }
    }

    /// <summary>[CALC] The successor schedule every break-off path builds the same way: the proposed edit's own recurrence shape and end date, starting exactly on the cut — or, for a Weekly pattern, re-anchored to the nearest date on or after the cut that actually preserves its own cadence, with ActiveFrom carrying "active from the cut" separately when that lands later. See this method's own body comment for why Weekly needs the second path at all.</summary>
    /// <param name="cutDate">Where the successor's schedule should start (or count as active from, for the Weekly case below).</param>
    private RecurrenceRuleOptions BuildSuccessorSchedule(DateOnly cutDate)
    {
        var reference = _proposedPattern.DatePattern;

        // Monthly/Yearly always carry an explicit ByMonthDay — RecurrenceRuleEditor
        // never shows a ByDay picker for those — so ical.net always finds
        // the right day regardless of where Start itself falls. Nothing to
        // preserve here; Start = cutDate is already correct, and every
        // existing break-off test for the common (Monthly) case depends on
        // Start landing exactly there, not on the reference's own next
        // occurrence.
        if (reference.Frequency != RecurrenceFrequency.Weekly)
        {
            return new RecurrenceRuleOptions
            {
                Frequency = reference.Frequency,
                Interval = reference.Interval,
                ByDay = reference.ByDay,
                ByMonthDay = reference.ByMonthDay,
                Start = cutDate,
                Until = reference.Until,
            };
        }

        // Weekly — two distinct ways pinning Start at cutDate silently loses
        // the pattern's own intended cadence, both found 2026-08-17 while
        // grounding the paycheck-association cascade, then confirmed with a
        // real failing test before touching anything (not just reasoned
        // through): RFC 5545 ties an omitted ByDay to DTSTART's own
        // weekday, so an empty ByDay would retarget every future occurrence
        // to cutDate's own weekday instead of the reference's — the same
        // bug class already fixed in AllocationPlanProposer.ProposePaced on
        // 2026-08-14. Making ByDay explicit alone isn't enough either,
        // confirmed empirically (RecurrenceRuleTests.Explicit_byday_alone_does_not_protect_an_intervals_own_week_phase_when_start_is_pinned_elsewhere):
        // an Interval > 1 rule's own "every Nth week" is ALSO counted from
        // DTSTART's own calendar week even when ByDay IS explicit, so
        // pinning Start at cutDate can land the whole cadence a full
        // interval-step off regardless. AllocationPlanProposer.AlignedSchedule's
        // own re-anchoring (find the reference's own next real occurrence
        // on or after cutDate, use THAT as Start) sidesteps both at once,
        // the same way it already does for ProposePaced — ActiveFrom
        // carries "active from cutDate" whenever that lands later than
        // cutDate itself, mirroring how BreakOffFactory.ValidateCutBoundaries
        // now checks the successor's own ActiveStart rather than its raw
        // Start, for exactly this reason.
        return AllocationPlanProposer.AlignedSchedule(reference, cutDate, reference.Until)
            ?? throw new InvalidOperationException("The pattern has no occurrence left in its own remaining window to break off onto.");
    }

    /// <summary>[READS FILE] The saved FinancialPattern this confirmation is editing — every break-off/narrowing path needs it, and DetermineConditions already requires it to be non-null for IsChangeCritical to be true at all.</summary>
    private FinancialPattern GetSavedPatternOrThrow() =>
        _repositories.FinancialPatterns.GetByFinanceId(_financeId)
            ?? throw new InvalidOperationException("Nothing to break off or correct — DetermineConditions already requires a saved pattern for IsChangeCritical to be true.");

    /// <summary>[WRITES FILE] The 0-or-1-existing-plan case of Item C's break-off: ends the saved pattern (and its plan, if it has one) the day before the cut, and creates a brand-new successor carrying the proposed edit's amount/schedule from the cut forward, with a freshly-proposed plan seeded from whatever the old jar held. Doesn't cover more than one existing EarMarkPattern — see PerformMultiPlanBreakOff for that case.</summary>
    private void PerformSingleSuccessorBreakOff()
    {
        var saved = GetSavedPatternOrThrow();
        var forecast = _requestForecast();

        // SETTLED 2026-08-11 (author): today, literally — not "the day after
        // the latest already-occurred expected transaction," which
        // planning/25's own text had floated as an alternate reading. One
        // known, deliberately deferred sharp edge: if the saved pattern's own
        // Start IS today (its only past occurrence), CutDate == Start here,
        // and BreakOffFactory.BreakOff throws (it requires CutDate to be
        // strictly after Start). Left unhandled until it actually comes up
        // (author), not solved here.
        var cutDate = forecast.AsOfDate;

        var predecessorPlan = forecast.Book.EarMarkPatternsFor(_financeId).SingleOrDefault();

        // planning/25's Item G — matched back to its own full
        // ProposedAllocationPlan (StartingEarmark included) by reference,
        // not reconstructed from ChosenPlanShape alone. Null whenever
        // ChosenPlanShape is null (no choice was offered, or the default was
        // picked) or doesn't match any candidate this same Run() actually
        // offered (a stray value set outside the real flow) — either way,
        // BreakOffFactory falls back to computing its own default.
        var chosenSuccessorPlan = _planShapeCandidates
            .FirstOrDefault(candidate => candidate.Plan.Plan == ChosenPlanShape)?.Plan;

        var result = BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = saved,
            PredecessorPlan = predecessorPlan,
            CutDate = cutDate,
            SuccessorFinanceId = forecast.Book.NextFinanceId(),
            SuccessorAmount = _proposedPattern.Amount,
            SuccessorSchedule = BuildSuccessorSchedule(cutDate),
            CarriedOverJarBalance = predecessorPlan is null ? 0m : JarBalanceOn(cutDate, _financeId),
            AllPatterns = forecast.Book.AllFinancialPatterns(),
            ChosenSuccessorPlan = chosenSuccessorPlan,
        });

        // The successor is now the current segment — post-save navigation
        // (AskWhichEarmarkPatternToOpen) needs to look here, not under
        // _financeId, whose own plan is now the truncated predecessor.
        _navigationFinanceId = result.Successor.FinanceId;

        _repositories.FinancialPatterns.Save(result.Predecessor, _accountId);
        _repositories.FinancialPatterns.Save(result.Successor, _accountId);

        if (result.PredecessorPlan is { } truncatedPlan)
        {
            _repositories.EarMarkPatterns.Save(truncatedPlan);
        }

        if (result.SuccessorPlan is { } successorPlan)
        {
            _repositories.EarMarkPatterns.Save(successorPlan);
        }

        if (result.SuccessorStartingEarmark is { } startingEarmark)
        {
            _repositories.ManualEarmarks.Save(startingEarmark);
        }
    }

    /// <summary>[WRITES FILE] The more-than-one-existing-plan case of Item C's break-off, when Item F's own question resolves to consolidating them (forced by ConsolidationNeeded, or chosen via UserChooseConsolidation): every surviving plan is truncated, and the successor still gets exactly one freshly-proposed plan (or the user's own chosen shape, when Item G offered one), seeded from the finance_id's one combined jar balance. Otherwise identical to PerformSingleSuccessorBreakOff — see that method for the cut-date note.</summary>
    private void PerformMultiPlanBreakOff()
    {
        var saved = GetSavedPatternOrThrow();
        var forecast = _requestForecast();
        var cutDate = forecast.AsOfDate;

        var predecessorPlans = forecast.Book.EarMarkPatternsFor(_financeId);

        // planning/25's Item G — same lookup PerformSingleSuccessorBreakOff
        // already does: matched back to its own full ProposedAllocationPlan
        // by reference, not reconstructed from ChosenPlanShape alone. Null
        // whenever no choice was offered (a genuinely concurrent set) or the
        // default was picked — either way, BreakOff falls back to its own
        // internal Propose call.
        var chosenSuccessorPlan = _planShapeCandidates
            .FirstOrDefault(candidate => candidate.Plan.Plan == ChosenPlanShape)?.Plan;

        var result = BreakOffFactory.BreakOff(new MultiPlanBreakOffRequest
        {
            Predecessor = saved,
            PredecessorPlans = predecessorPlans,
            CutDate = cutDate,
            SuccessorFinanceId = forecast.Book.NextFinanceId(),
            SuccessorAmount = _proposedPattern.Amount,
            SuccessorSchedule = BuildSuccessorSchedule(cutDate),
            CarriedOverJarBalance = JarBalanceOn(cutDate, _financeId),
            AllPatterns = forecast.Book.AllFinancialPatterns(),
            ChosenSuccessorPlan = chosenSuccessorPlan,
        });

        // Same reasoning as PerformSingleSuccessorBreakOff's own note: the
        // consolidated successor is the current segment now.
        _navigationFinanceId = result.Successor.FinanceId;

        _repositories.FinancialPatterns.Save(result.Predecessor, _accountId);
        _repositories.FinancialPatterns.Save(result.Successor, _accountId);

        foreach (var truncatedPlan in result.PredecessorPlans)
        {
            _repositories.EarMarkPatterns.Save(truncatedPlan);
        }

        if (result.SuccessorPlan is { } successorPlan)
        {
            _repositories.EarMarkPatterns.Save(successorPlan);
        }

        if (result.SuccessorStartingEarmark is { } startingEarmark)
        {
            _repositories.ManualEarmarks.Save(startingEarmark);
        }
    }

    /// <summary>[WRITES FILE] Carries out the plan DetermineNarrowingPlanIfApplicable already worked out (before anything was saved): trims the surviving plan's front to match the goal's new ActiveStart via PatternTruncation.StartOn, absorbing whatever it held into StartingAllocation, and deletes any ManualEarmark now before the plan's new Start (its value is already captured in the bump). A no-op whenever _narrowingPlan is null — narrowing didn't apply, wasn't safe yet, or DetermineNarrowingPlanIfApplicable was never called. internal so FinancePatternSaveConfirmationTests can call it directly — Run() can't reach it today, since AskForGuidanceOnImplicitChanges' placeholder always answers UserChooseAlterPast = false.</summary>
    internal void NarrowSurvivingPlanIfNeeded()
    {
        if (_narrowingPlan is not { } plan)
        {
            return;
        }

        foreach (var date in plan.OrphanedManualEarmarkDates)
        {
            _repositories.ManualEarmarks.Delete(_financeId, date);
        }

        var trimmedPlan = PatternTruncation.StartOn(plan.ExistingPlan, _proposedPattern, plan.NewActiveStart, plan.AbsorbedBalance);
        _repositories.EarMarkPatterns.Save(trimmedPlan);

        // EarMarkPatternRepository.Save upserts on (FinanceId, StartDate) —
        // by design, so a break-off/restructure's genuinely NEW segment
        // never overwrites the one it's continuing from. But StartOn moved
        // THIS SAME segment's own Start, not created a new one, so without
        // this the plan's original row (still keyed on its old, now-stale
        // Start) would survive alongside the trimmed one instead of being
        // replaced by it — two rows for one segment, and the stale one
        // fails EarMarkPattern.Create's own ActiveStart check against the
        // now-narrower goal the next time anything reads it back. Found by
        // this class's own test failing with a second, unexpected row.
        _repositories.EarMarkPatterns.Delete(plan.ExistingPlan.FinanceId, plan.ExistingPlan.DatePattern.Start);
    }

    /// <summary>[WRITES FILE] Carries out the plan DetermineConsolidationPlanIfApplicable already worked out (before anything was saved): deletes every surviving EarMarkPattern by its own original (FinanceId, Start) — harmless even if ApplyBackTruncationsIfNeeded already truncated or deleted one first, since PerformSave (and so ApplyBackTruncationsIfNeeded) always runs before this and a delete on an already-deleted row is a no-op — and saves the one EarmarkConsolidation.Consolidate result in their place. A no-op whenever _consolidationPlan is null: consolidation didn't apply, or PerformImplicitEarmarkChanges' own caller decided against it (Item F's still-open "keep them separate" TODO on this path — see that method's own comment). internal so FinancePatternSaveConfirmationTests can call it directly, same reasoning as NarrowSurvivingPlanIfNeeded.</summary>
    internal void ConsolidateSurvivingPlansIfNeeded()
    {
        if (_consolidationPlan is not { } plan)
        {
            return;
        }

        foreach (var survivingPlan in plan.SurvivingPlans)
        {
            _repositories.EarMarkPatterns.Delete(survivingPlan.FinanceId, survivingPlan.DatePattern.Start);
        }

        var result = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = _proposedPattern,
            SurvivingPlans = plan.SurvivingPlans,
            ManualEarmarksForThisGoal = plan.ManualEarmarksForThisGoal,
            AllPatterns = plan.AllPatterns,
            // Read back at DetermineConsolidationPlanIfApplicable time, not
            // here — see ConsolidationPlan.CurrentJar's own field comment
            // for why reading it live at this point (after PerformSave) is
            // the wrong moment.
            CurrentJar = plan.CurrentJar,
        });

        _repositories.EarMarkPatterns.Save(result.ConsolidatedPlan);
    }

    /// <summary>[WRITES FILE] Carries out Item F's "amount changed, no date shift" scaling (planning/25, EarmarkScaling.Scale): every surviving EarMarkPattern's own Amount scales by the same ratio the goal's own Amount just changed by, saved back under each plan's own original (FinanceId, Start) — an in-place update, not a delete-and-recreate, since DetermineConsolidationPlanIfApplicable's own _isAmountOnlyChange gate guarantees no plan's own dates moved. A no-op whenever _consolidationPlan is null, same reasoning as ConsolidateSurvivingPlansIfNeeded. internal so FinancePatternSaveConfirmationTests can call it directly.</summary>
    internal void ScaleSurvivingPlansIfNeeded()
    {
        if (_consolidationPlan is not { } plan)
        {
            return;
        }

        var scaledPlans = EarmarkScaling.Scale(new ScaleRequest
        {
            Goal = _proposedPattern,
            PreviousGoalAmount = plan.PreviousGoalAmount,
            SurvivingPlans = plan.SurvivingPlans,
        });

        foreach (var scaledPlan in scaledPlans)
        {
            _repositories.EarMarkPatterns.Save(scaledPlan);
        }
    }

    /// <summary>[READS FILE] Reads a fund jar's own ExpectedAmount as of a chosen day off the live forecast — the same "read a jar's balance on a chosen day" ManualEarmarkWindow/EarmarkFormPanel already do (BalancesOn/GetLiveJarAmounts), reused here rather than re-derived.</summary>
    /// <param name="date">The day to read the balance as of.</param>
    /// <param name="financeId">Which fund's balance to read.</param>
    /// <returns>The jar's ExpectedAmount on that day, or 0m for a day before the forecast's own timeline starts, or a FinanceId with no jar at all.</returns>
    private decimal JarBalanceOn(DateOnly date, int financeId) => JarOn(date, financeId)?.ExpectedAmount ?? 0m;

    /// <summary>[READS FILE] Reads a fund jar itself (not just its ExpectedAmount) as of a chosen day off the live forecast — JarBalanceOn's own sibling, for callers that also need MilestoneAmount/HasGlut/GlutSurplus, not just the raw balance.</summary>
    /// <param name="date">The day to read the jar as of.</param>
    /// <param name="financeId">Which fund's jar to read.</param>
    /// <returns>The jar as of that day, or null for a day before the forecast's own timeline starts, or a FinanceId with no jar at all.</returns>
    private FundJar? JarOn(DateOnly date, int financeId)
    {
        var entry = _requestForecast().GetTimeline(financeId).LastOrDefault(candidate => candidate.Date <= date);
        return entry?.Snapshot.FundJars.FirstOrDefault(candidate => candidate.FinanceId == financeId);
    }
}

// The three repositories FinancePatternSaveConfirmation needs — everything
// TransactionLogBook itself can't provide, because it's an immutable,
// freshly-recomputed read model with no write capability and no way to
// recover just the user-authored ManualEarmarks (they're folded into a
// BalanceSnapshot's day-by-day EarmarkEvents during the cascade, mixed in
// with system-generated isolated earmarks). One parameter instead of three
// separate ones (author, 2026-08-11) — same "bundle of what a thing needs"
// shape as BreakOffRequest/RestructureRequest, just for repository
// references instead of domain values.
public sealed record FinancePatternRepositories
{
    public required FinancialPatternRepository FinancialPatterns { get; init; }
    public required EarMarkPatternRepository EarMarkPatterns { get; init; }
    public required ManualEarmarkRepository ManualEarmarks { get; init; }
}

// What ConfirmImplicitChanges needs to render itself — everything
// DetermineConditions worked out that a real popup (or a test double) needs
// to know which questions actually apply, plus a plain-language description
// of what changed. Built fresh per Run() call, right before the delegate
// fires — see FinancePatternSaveConfirmation.BuildConfirmationRequest.
public sealed record ImplicitChangeConfirmationRequest
{
    public required bool IsChangeCritical { get; init; }
    public required bool HasMultipleEarmarkPatterns { get; init; }
    public required bool ConsolidationNeeded { get; init; }

    // "" whenever ConsolidationNeeded is false. Shown as a plain
    // announcement (ConsolidationForcedText), not tied to any choice —
    // there's no choice on this row once consolidation is forced, matching
    // ConsolidationForcedReason/DescribeConsolidationForcedReason's own doc
    // comment for why this replaced a static XAML string 2026-08-17.
    public required string ConsolidationForcedReason { get; init; }

    // "" whenever there's nothing to caveat — either "keep separate" isn't
    // being offered at all (ConsolidationNeeded true, or fewer than two
    // plans), or the edit isn't Critical so there's no break-off/alter-past
    // ambiguity for the caveat to be about. A real sentence otherwise,
    // warning ahead of the choice that "break off" always combines plans
    // regardless of what's picked in the Consolidation row — see
    // DescribeConsolidationCaveat's own doc comment for the 2026-08-17 fix
    // this exists to explain.
    public required string ConsolidationCaveat { get; init; }

    public required bool IsAmountOnlyChange { get; init; }

    // planning/25's Item G — empty whenever there's nothing to choose
    // between (most edits, multi-plan goals, or a break-off with no
    // existing plan to draw an alternative shape from). A real popup shows
    // each candidate's own Label and hands back whichever one's own Plan
    // the user picked as ImplicitChangeConfirmationAnswer.ChosenPlanShape.
    public required IReadOnlyList<FinancePatternSaveConfirmation.PlanShapeCandidate> PlanShapeCandidates { get; init; }

    public required string Description { get; init; }

    // "" whenever there's nothing concrete to name (see FinancePatternSaveConfirmation.
    // DescribeAlterPastConsequence's own doc comment for exactly when). A
    // real sentence naming the SPECIFIC savings-plan consequence — absorbed
    // balance, deleted manual earmarks — of choosing "apply it everywhere"
    // in AlterPastSection above, shown only while that option is the one
    // currently selected. Only ever set for a FinancialPattern-editing
    // request — RunForPlan's own EarMarkPattern-editing one never
    // populates it (there's no AlterPastSection there at all).
    public required string AlterPastConsequence { get; init; }

    // "" whenever nothing to warn about — a real sentence whenever a
    // retroactive correction's own new boundary couldn't be applied to the
    // savings plan for a known, specific reason (see FinancePatternSaveConfirmation.
    // NarrowingLimitationWarning's own field comment). Only ever set for a
    // FinancialPattern-editing request — RunForPlan's own EarMarkPattern-
    // editing one never populates it.
    public required string NarrowingLimitationWarning { get; init; }

    // planning/27's own EarMarkPattern-chain questions (Phase 2) — only ever
    // true for a request built by RunForPlan (the EarMarkPattern-editing
    // entry point); always false for a FinancialPattern-editing one. See
    // TouchesChainBoundary/ChangeCanCascade below for that side's own,
    // distinctly-named Phase 1 versions of the same two questions — kept
    // separate rather than shared, even though the two modes never both
    // populate a single request, so each pair's own documented meaning
    // (EarMarkPattern chain vs. FinancialPattern chain) stays unambiguous.
    public required bool PlanTouchesChainBoundary { get; init; }
    public required bool PlanChangeCanCascade { get; init; }

    // planning/27's own Phase 1 — the FinancialPattern-chain mirror of the
    // two above, only ever true for an ordinary FinancialPattern-editing
    // request; always false for RunForPlan's own EarMarkPattern-editing one.
    public required bool TouchesChainBoundary { get; init; }
    public required bool ChangeCanCascade { get; init; }

    // Phase 1's own third question, no EarMarkPattern equivalent at all —
    // see FinancePatternSaveConfirmation.TrivialFieldsCanCascade's own field
    // comment for why Priority/Mandatory/Description/AutoRenew get this
    // treatment while nothing analogous exists for a savings plan.
    public required bool TrivialFieldsCanCascade { get; init; }

    // planning/27's Source row: "warn, don't block." "" whenever nothing to
    // warn about (Source unchanged, or changed on a standalone pattern with
    // no predecessor/successor to disconnect from); a real sentence
    // otherwise. Shown as a plain warning block, not tied to any radio
    // choice — there's no choice on this row, the edit proceeds either way.
    public required string SourceChangeWarning { get; init; }

    // planning/27's own "name the concrete consequence, not a generic
    // notice" requirement — "" whenever the matching trigger (either chain
    // type's own PlanTouchesChainBoundary/TouchesChainBoundary or
    // PlanChangeCanCascade/ChangeCanCascade) is false. Shared by BOTH chain
    // types rather than duplicated per-type, since only one type's own
    // trigger is ever true per request. StayLinkedWarning is "" for a
    // plain, never-destructive nudge and only a real sentence once the edit
    // reaches far enough to absorb a neighbor — the row-based design's own
    // "the warning escalates with the consequence, the default option
    // doesn't change" case. LetItBreakWarning is always a real sentence
    // whenever a TouchesChainBoundary flag is true — breaking the chain
    // always leaves SOME gap or overlap. CascadeDescription is a plain,
    // always-shown description (not a warning tied to one "dangerous"
    // option — neither cascade choice here is destructive), naming the
    // direct edit's own range and how far cascading would reach.
    public required string StayLinkedWarning { get; init; }
    public required string LetItBreakWarning { get; init; }
    public required string CascadeDescription { get; init; }

    // Phase 1's own third question's own description — "" whenever
    // TrivialFieldsCanCascade is false. No EarMarkPattern equivalent, so
    // this is never shared the way the three fields above are.
    public required string TrivialFieldsCascadeDescription { get; init; }

    // The paycheck-association cascade (2026-08-17) — only ever true for a
    // FinancialPattern-editing request whose edit is to an income pattern's
    // own schedule; always false for an EarMarkPattern-editing (RunForPlan)
    // one, since a savings plan itself is never income. A different
    // relationship from every field above (this income's own effect on
    // OTHER patterns' plans, not this pattern's own chain), so it gets its
    // own trigger/description pair rather than sharing one.
    public required bool PacedBillsCanCascade { get; init; }
    public required string PacedBillsCascadeDescription { get; init; }
}

// What the user (or, when nothing is wired up, DefaultConfirmationAnswer)
// decided. Proceed = false cancels the whole save — Run() returns false and
// stops before PerformSave, exactly as if Save had never been clicked. The
// other fields are meaningless when they don't apply (e.g.
// ChooseConsolidation when HasMultipleEarmarkPatterns was false) — Run()
// only ever reads the ones DetermineConditions says are actually in play.
public sealed record ImplicitChangeConfirmationAnswer
{
    public required bool Proceed { get; init; }
    public bool ChooseAlterPast { get; init; }
    public bool ChooseConsolidation { get; init; }
    public bool ChoseScalePatterns { get; init; }

    // planning/25's Item G — one of the EarMarkPatterns offered via this
    // same request's own PlanShapeCandidates, by reference, or null to use
    // Propose's own default (whether because PlanShapeCandidates was empty,
    // or the user was offered a choice and picked the default anyway).
    public EarMarkPattern? ChosenPlanShape { get; init; }

    // planning/27's own chain-boundary/cascade answers — shared by BOTH
    // FinancialPattern-editing (Phase 1) and EarMarkPattern-editing (Phase
    // 2) requests, meaningless unless the matching request field (either
    // PlanTouchesChainBoundary/PlanChangeCanCascade or
    // TouchesChainBoundary/ChangeCanCascade) was true. Both default to the
    // settled safe defaults, so a caller answering only what it cares about
    // still gets the right behavior for the rest.
    public bool ChoseStayLinked { get; init; } = true;
    public bool ChoseCascadeForward { get; init; } = true;

    // Phase 1's own third answer, no EarMarkPattern equivalent — meaningless
    // unless TrivialFieldsCanCascade was true. Defaults to FALSE, unlike
    // ChoseCascadeForward above — SETTLED (round 3 of the small questions):
    // "default stays 'just this segment.'"
    public bool ChoseCascadeTrivialFields { get; init; }

    // The paycheck-association cascade's own answer — meaningless unless
    // PacedBillsCanCascade was true. Defaults to FALSE: "offered as a
    // suggestion (not forced)" means declining is the safe no-op, same
    // reasoning as ChoseCascadeTrivialFields above.
    public bool ChoseToRepaceBills { get; init; }
}
