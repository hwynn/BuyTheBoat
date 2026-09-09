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
// missing any of the Item B-G questions/warnings themselves: the plan's health
// heads-up (DetermineConcerningPlanNoticeIfApplicable — now an announcement row,
// formerly a post-save MessageBox), the plan-disambiguation picker
// (AskWhichEarmarkPatternToOpen/PickEarmarkPattern), and Item E's own named
// consequence (AlterPastConsequence) all went from TODO/no-op to real,
// tested mechanisms 2026-08-17. See this class's own bottom-of-header "TODO
// (2026-08-17)" section for what's STILL genuinely open, added the same day
// on direct author request to make sure nothing found got lost to low
// context before it could be written down. When ConfirmImplicitChanges
// isn't wired at all (most tests, and any host that hasn't connected a real
// popup), DefaultOutcome supplies the same safest,
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
// STATUS notes (planning/28 is the live design for the refactor below):
//
// 1. RESOLVED 2026-08-18 by the forward-only ruling: the old narrowing
//    crash risk (a retroactive correction reaching before the as-of date
//    leaving the plan stale against the goal) is gone — there is no
//    retroactive "apply everywhere" path anymore, so nothing ever narrows a
//    plan before the as-of date. The narrowing machinery and its background
//    task (task_ad0129c6) both retire with it.
//
// 2. DESIGN GOAL, not yet built: the author has confirmed "we eventually
//    want to let the user keep unconsolidated earmark patterns" — the
//    fallback that always consolidates on the break-off side is a SAFETY
//    measure, not the intended final behavior. Still wanted, still not
//    designed. See planning/28 and redesign/memory/project_next_phase.md.
//
// 3. ARCHITECTURE, in progress (planning/28): this class's own
//    author-acknowledged assessment — "the save confirmation system is
//    probably a mess." Nine-plus independent trigger/answer/description
//    fields (IsChangeCritical, ConsolidationNeeded, TouchesChainBoundary,
//    ChangeCanCascade, TrivialFieldsCanCascade, PacedBillsCanCascade, and
//    more) each wired through their own Determine*/Build*/Describe* trio and
//    their own slice of ImplicitChangeConfirmationRequest/Answer. Thread 2
//    of planning/28 replaces this with the "confirmation row" model; the
//    foundational ConfirmationRow.cs types exist, the DTO/popup reshape does
//    not yet.
//
// 4. SMALLER gaps: the goal-health suggestion (DetermineGoalHealthSuggestionIfApplicable)
//    is a v1 — a single amount-only correction offered accept/reject, not yet
//    the full multi-option strategy-picker planning/25/28 describe (its reject
//    warning is unbuilt, and identical options aren't deduped yet);
//    PickEarmarkPatternToOpen falls back to the first plan on cancel rather
//    than aborting navigation.
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

    // Re-runs the forecast with a set of goals' savings plans left out, for the "room for these plans"
    // affordability basis (AffordabilityCeilingFor below). Null in tests/callers that don't size against a
    // ceiling — the affordability caps then simply don't apply. In the running app it's provided by
    // MainWindow, which is the one untestable link: it builds the same ForecastOptions RefreshForecast does,
    // then WithoutPlansFor + CreateForecast.
    private readonly Func<IReadOnlySet<int>, ForecastResult>? _requestForecastOmitting;
    private readonly FinancePatternRepositories _repositories;

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

    // What ApplyBackTruncationsIfNeeded needs to fix 3.11.2.a2's back (Until)
    // boundary, worked out by DetermineBackTruncationsIfApplicable before
    // anything is saved — null means every existing EarMarkPattern already
    // fits inside the proposed pattern's own Until (most edits; always true
    // for a brand-new pattern, since DetermineConditions's own early return
    // leaves nothing here to find). NOT gated on IsChangeCritical or
    // HasMultipleEarmarkPatterns — see this class's own "Back-boundary
    // invariant" header note for why. The Start (front) boundary has its own
    // mirror, _frontTruncations, just below.
    private BackTruncationPlan? _backTruncations;

    private sealed record BackTruncationPlan(
        IReadOnlyList<EarMarkPattern> PlansExceedingNewUntil,
        IReadOnlyList<DateOnly> OrphanedManualEarmarkDates);

    // The front-boundary mirror of _backTruncations (M2): what
    // ApplyFrontTruncationsIfNeeded needs to keep 3.11.2.a2 holding when the
    // proposed pattern's own Start moves FORWARD in place — a non-Critical
    // future edit; a Critical one breaks off instead and never saves in place,
    // so this only ever fixes a future-dated pattern's own plans. Without it a
    // plan left starting before the new Start violates the earmark-span-within-
    // goal-span invariant, and the next EarMarkPatternRepository.GetAll()
    // re-validation throws — the Start-side twin of the crash _backTruncations
    // already fixes on the Until side. Worked out before anything is saved,
    // same "read before write" timing; null when every plan already starts on
    // or after the new Start.
    private FrontTruncationPlan? _frontTruncations;

    private sealed record FrontTruncationPlan(
        IReadOnlyList<EarMarkPattern> PlansStartingBeforeNewStart,
        IReadOnlyList<DateOnly> OrphanedManualEarmarkDates);

    // The OUTWARD counterpart to the two truncations (M2 piece 3): when the
    // proposed pattern's own Start or Until moves OUTWARD in place (Until later,
    // or a future Start earlier), a plan that shared that exact boundary tracks
    // it out too — the same "a shared boundary means the plan follows the goal"
    // rule the truncations enforce on the way in, now on the way out, adding
    // occurrences at the plan's own rhythm and amount. Worked out before anything
    // is saved (it needs the goal's OLD boundary, gone once PerformSave writes the
    // new one); null when no boundary moved outward, or no plan shared the one
    // that did. SCOPE (piece 3): only the directly-edited goal's OWN plans, not a
    // chain neighbor stretched by ExtendStart/ExtendUntil — that case overlaps the
    // reverse-break-off and waits on the silent join (M1) to settle the overlap.
    // The "[bill] occurs N more times" announcement now surfaces this (a
    // BoundaryExtension row, 2026-08-27) — no longer silent; the over/underfund
    // health check on the grown plan is still deferred.
    private BoundaryExtensionPlan? _boundaryExtensions;

    private sealed record BoundaryExtensionPlan(
        IReadOnlyList<EarMarkPattern> PlansSharingOldStart,
        IReadOnlyList<EarMarkPattern> PlansSharingOldUntil,
        // How many more times the goal itself now occurs because the boundary
        // moved out — what the "[bill] occurs N more times" announcement reports.
        int AddedGoalOccurrences);

    // The later finance patterns this finance pattern's own Amount change will be
    // carried forward onto that are funded by MORE THAN ONE earmark pattern
    // (cross-boundary Q6) — each gets its own combine-or-keep-separate question,
    // kept distinct from the break-off Consolidation row. Worked out before the
    // confirmation; empty when no such later finance pattern is reached.
    private IReadOnlyList<FinancialPattern> _crossBoundaryConsolidations = [];

    // The answer to each cross-boundary consolidation question, by successor
    // finance_id — true = combine, false (the default) = keep separate. Read from
    // the outcome in Run, applied by ReconcileCascadedSuccessorSavingsPlans.
    private IReadOnlyDictionary<int, bool> _successorCombineChoices = new Dictionary<int, bool>();

    // Whether a consolidation is on the table this save (a multi-plan successor the
    // cascade touches, forced by a schedule change or offered as a combine), so the
    // two consolidate-strategy questions apply. The spread one only differs when a
    // single clear income exists to pace against.
    private bool _consolidationStrategyApplies;
    private bool _consolidationHasIncomeForSpread;

    // The user's two consolidate-strategy answers, applied to every consolidation
    // this save makes. Default to the long-standing behavior (meet the goal, pace to
    // income) so a headless caller consolidates exactly as before.
    private ConsolidationSizing _chosenSizing = ConsolidationSizing.MeetGoal;
    private ConsolidationSpread _chosenSpread = ConsolidationSpread.AcrossPaydays;

    // What keeping this break-off's plans separate would leave the new segment
    // funded at — a dry-run of the keep-separate break-off, re-rated to meet the
    // goal (EarmarkScaling.ScaleToMeetGoal), computed before anything is saved.
    // Null unless a keep-separate break-off is actually on the table; carries the
    // proportionally-corrected plans and the current-vs-needed totals. Only offered
    // as a question when the two totals differ (the plans over/underfund the new
    // amount) — see DetermineKeepSeparateFundingIfApplicable.
    private MeetGoalScalingResult? _keepSeparateFunding;

    // The field overrides an accepted goal-health suggestion should pre-fill the
    // Earmark form with (EarmarkFieldOverrideKeys → suggested value), passed into
    // NavigateToEarmarkForm so the form opens with the correction as unsaved edits.
    // Null until the user accepts a suggestion in the confirmation; a plain open
    // otherwise. Single-plan only — the multi-plan cases resolve via implicit
    // changes, where there's no single form to pre-fill.
    private IReadOnlyDictionary<string, object?>? _acceptedSuggestionOverrides;

    // The correction the goal-health suggestion offers — the single plan re-sized
    // (via EarmarkScaling.ScaleToMeetGoal) to meet the EDITED goal, when it no
    // longer does after this save. Null unless there's a real correction to offer;
    // only its Amount is used today (the override the form pre-fills), though the
    // whole proposed plan is kept so richer overrides can be added later. See
    // DetermineGoalHealthSuggestionIfApplicable.
    private EarMarkPattern? _goalHealthSuggestedPlan;

    // Whether that correction is needed because the plan currently saves too
    // LITTLE (true — the goal would fall short) or too much (false — it ties up
    // money the goal won't use). Drives the reject warning's wording; only
    // meaningful when _goalHealthSuggestedPlan is set.
    private bool _goalHealthUnderfunds;

    // The plan's own health heads-up, when it's worth warning about — the plain
    // "Worth a look" sentence that used to be a separate post-save MessageBox,
    // now shown as an announcement row so ALL of this save's messaging lives in
    // the one confirmation this class drives (the author's own intent). "" when
    // there's nothing to surface. See DetermineConcerningPlanNoticeIfApplicable.
    private string _concerningPlanNotice = "";

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

    // Whether the edited FinancialPattern has a later segment in its own
    // break-off chain (a same-Source pattern with a later Start). Computed by
    // DetermineChainConditionsIfApplicable. This is what tells an EARLIER-segment
    // edit (edit in place + cascade forward) apart from a CURRENT-segment edit
    // (break off from today) once the edit is Critical — see PerformSave and
    // PerformImplicitEarmarkChanges.
    private bool _chainHasSuccessor;

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

    // Whether this FinanceId's Savings Plan already has more than one
    // EarMarkPattern (F27's relaxation of 3.11.1.a1 — sequential from an
    // earlier Restructure/break-off, or concurrent, item 9's shape).
    // Computed in DetermineConditions.
    private bool HasMultipleEarmarkPatterns { get; set; }

    // Item F's feasibility test result: true when the changed field(s) make
    // it impossible to keep multiple EarMarkPatterns separate (the
    // recurrence-shape case, planning/25 Item F's table) — consolidation
    // isn't offered as a choice there, it's announced. False means keeping
    // them separate is workable, and the user gets asked instead.
    // Computed in DetermineConditions.
    private bool ConsolidationNeeded { get; set; }

    // Whether "Save and Skip planning" was clicked rather than "Save and
    // Plan" — no navigation happens either way, so no plan-picking question
    // and no suggestion popup fire regardless of anything else here. Set at
    // construction, not computed.
    private bool UserSkippedPlanning { get; }

    // planning/25's Item G: which of _planShapeCandidates the user picked,
    // when there was a choice to make at all — null means "use Propose's
    // own default," both when _planShapeCandidates was empty (nothing to
    // choose between) and when the user was offered a choice and picked the
    // default anyway. Matched back to its own full ProposedAllocationPlan by
    // reference in PerformSingleSuccessorBreakOff.
    private EarMarkPattern? ChosenPlanShape { get; set; }

    // Whether the resulting plan (after whatever above has been resolved) is
    // worth suggesting a fix for — the concerning-plan notice row's own
    // trigger, sourced from PlanHealthState.IsWorthWarningAbout. Computed in
    // DetermineConditions.
    private bool ChangeWarrantsSuggestions { get; set; }

    // planning/27's own Phase 1 — the FinancialPattern break-off chain's "does
    // this edit touch a neighbor's boundary" and "can this Amount/shape change
    // cascade forward" conditions, computed by DetermineChainConditionsIfApplicable.
    // Set together: a save can touch a boundary, be cascade-eligible, both, or
    // neither. (The EarMarkPattern-chain equivalents live on the sibling
    // EarmarkPatternSaveConfirmation now, not here.)
    private bool TouchesChainBoundary { get; set; }
    private bool ChangeCanCascade { get; set; }

    // Phase 1's own third question, with no EarMarkPattern equivalent at
    // all (Priority/Mandatory/Description/AutoRenew have no analog there) —
    // round 3 of the "fourth relationship" small questions reopened these
    // trivial fields to ask too, once the row-based page made asking nearly
    // free. Unlike Amount/shape, defaults to NOT cascading (see
    // UserChoseCascadeTrivialFields below) — the one place Phase 1 doesn't
    // mirror Amount/shape's own default.
    private bool TrivialFieldsCanCascade { get; set; }

    // planning/27's own Source row: "warn, don't block." "" whenever Source
    // didn't change, or changed on a segment with no predecessor/successor
    // to disconnect from (a standalone pattern — nothing to warn about). A
    // real sentence otherwise, naming what disconnects — shown as a plain
    // warning block, not tied to any radio choice, since there's no choice
    // to make here; the edit proceeds either way.
    private string SourceChangeWarning { get; set; } = "";

    // The paycheck-association cascade's own trigger — true when at least
    // one OTHER FinancialPattern's currently-active savings plan was paced
    // against this income's OLD schedule and no longer is, per
    // DeterminePaycheckAssociationIfApplicable. A completely different
    // relationship from TouchesChainBoundary/ChangeCanCascade above (those
    // are about _financeId's own predecessor/successor chain; this is about
    // OTHER, unrelated FinancialPatterns' own plans), and only ever relevant
    // to a FinancialPattern edit — a savings plan itself is never income.
    private bool PacedBillsCanCascade { get; set; }

    // The user's answer to "stay linked in the chain, or let it break" —
    // meaningless unless TouchesChainBoundary is true. No explicit default
    // was ever settled the way cascading forward's was — true (stay linked)
    // is this class's own reasoned choice, matching the one option that's
    // never destructive on its own, not something stated outright. (The
    // EarMarkPattern chain's own answer lives on EarmarkPatternSaveConfirmation.)
    private bool UserChoseStayLinked { get; set; } = true;

    // The user's answer to "cascade forward, or just this segment" —
    // meaningless unless ChangeCanCascade is true. Defaults to true — SETTLED,
    // cascading forward is the system default for an Amount/shape change.
    private bool UserChoseCascadeForward { get; set; } = true;

    // The user's answer to Phase 1's own trivial-fields question —
    // meaningless unless TrivialFieldsCanCascade is true. Defaults to
    // FALSE, unlike UserChoseCascadeForward above — SETTLED (round 3):
    // "default stays 'just this segment' — nothing about today's actual
    // behavior changes for anyone who accepts the row's own default."
    // Trivial fields have no savings-plan counterpart.
    private bool UserChoseCascadeTrivialFields { get; set; }

    // The user's answer to the paycheck-association cascade — meaningless
    // unless PacedBillsCanCascade is true. Defaults to FALSE — "offered as a
    // suggestion (not forced)" (the settled language this mechanism is
    // named for) means declining is the safe no-op, same reasoning as
    // UserChoseCascadeTrivialFields above, and more so here: re-pacing a
    // bill's plan is a real, visible change to its own money movement, not
    // just a trivial-field copy.
    private bool UserChoseToRepaceBills { get; set; }

    // The user's answer to Item F's "keep them separate / combine them into
    // one" question on a break-off — meaningless unless HasMultipleEarmarkPatterns
    // is true and ConsolidationNeeded is false (a shape/start change forces
    // consolidation regardless). Defaults to FALSE (keep separate): that's the
    // Consolidation row's own default, and the less-destructive option — the
    // break-off keeps one successor plan per surviving plan rather than folding
    // them into one.
    private bool UserChoseCombinePlans { get; set; }

    // The user's answer to the nested "these kept-separate plans over/underfund
    // the new amount — adjust them to meet it?" question — meaningless unless the
    // keep-separate funding question was actually shown (_keepSeparateFunding is
    // set and its totals differ). Defaults to FALSE: keeping each plan's own rate
    // untouched is the safe no-op, and nothing re-rates money when no one was
    // asked (headless). The popup pre-selects "adjust," mirroring the consolidate
    // sizing question, but declining stays the default a bare Proceed reads back.
    private bool UserChoseToAdjustKeptSeparatePlans { get; set; }

    // The overall Trivial/Critical/Concerning categorization's own top-level
    // flag: true when a restricted field (start/amount/recurrence shape) changed
    // AND the saved pattern already has an occurrence on or before today — i.e.
    // the edit reaches history, so it breaks off from today rather than saving
    // in place. Computed in DetermineConditions.
    private bool IsChangeCritical { get; set; }

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
        FinancePatternRepositories repositories,
        Func<IReadOnlySet<int>, ForecastResult>? requestForecastOmitting = null)
    {
        _financeId = financeId;
        _navigationFinanceId = financeId;
        _proposedPattern = proposedPattern;
        _accountId = accountId;
        UserSkippedPlanning = userSkippedPlanning;
        _requestForecast = requestForecast;
        _repositories = repositories;
        _requestForecastOmitting = requestForecastOmitting;
    }

    /// <summary>[READS FILE] The affordability ceiling for `target`, measured on a re-forecast with `omitFinanceId`'s whole chain of savings plans left out — the "room for these plans" basis, so the plans being resized count their own current contributions as available rather than already-spent. Returns the single-date front-load ceiling when `startingEarmark` is true, otherwise the range ceiling that bounds an ongoing per-cycle contribution. Null when no omitting-forecast source is wired (headless tests). The three wrappers below fix its two knobs per caller: which chain to omit (the edited pattern's or the target's own) and which of the two ceilings.</summary>
    /// <param name="omitFinanceId">Whose chain of plans to leave out of the re-forecast — the edited pattern's (_financeId) when the target belongs to that same chain, or the target's own when it doesn't.</param>
    /// <param name="target">The goal or bill whose plan is being sized.</param>
    /// <param name="changeKind">Whether this is a bold Suggestion or a cautious Implicit change.</param>
    /// <param name="startingEarmark">True for the single-date front-load ceiling; false for the range (ongoing-rate) ceiling.</param>
    private decimal? CeilingOmitting(int omitFinanceId, FinancialPattern target, ChangeKind changeKind, bool startingEarmark)
    {
        if (_requestForecastOmitting is null)
        {
            return null;
        }

        var omitted = _requestForecastOmitting(_requestForecast().Book.ChainFinanceIds(omitFinanceId));
        var page = omitted.Accounts.FirstOrDefault(account => account.AccountId == _accountId)?.Page
            ?? omitted.PrimaryAccountPage;
        return startingEarmark
            ? AffordabilityCeiling.ForStartingEarmark(page, target, omitted.AsOfDate, changeKind)
            : AffordabilityCeiling.For(page, target, omitted.AsOfDate, changeKind);
    }

    /// <summary>[READS FILE] The most a change to `target`'s savings plan may set aside per cycle without over-committing — the range ceiling, measured with the EDITED pattern's whole chain of plans omitted. Feeds the goal-health suggestion, the keep-separate funding correction, and the cross-boundary cascade (whose successors share the edited pattern's chain). Null when no omitting-forecast source is wired, which leaves those scalings uncapped.</summary>
    /// <param name="target">The goal or bill whose plan is being sized (in the edited pattern's chain).</param>
    /// <param name="changeKind">Whether this is a bold Suggestion or a cautious Implicit change.</param>
    private decimal? AffordabilityCeilingFor(FinancialPattern target, ChangeKind changeKind) =>
        CeilingOmitting(_financeId, target, changeKind, startingEarmark: false);

    /// <summary>[READS FILE] The most a proposed starting (front-load) earmark for `target` may set aside on the as-of day — the single-date ceiling, measured with `target`'s OWN chain of plans omitted (its front-load shouldn't count against itself). Keyed on the target rather than the edited pattern, since a paycheck re-pace front-loads OTHER bills. Null when no omitting-forecast source is wired.</summary>
    /// <param name="target">The bill whose starting earmark is being sized.</param>
    /// <param name="changeKind">Whether this is a bold Suggestion or a cautious Implicit change.</param>
    private decimal? StartingEarmarkCeilingFor(FinancialPattern target, ChangeKind changeKind) =>
        CeilingOmitting(target.FinanceId, target, changeKind, startingEarmark: true);

    /// <summary>[READS FILE] The most a re-proposed plan for `target` may reserve per cycle — the range ceiling, measured with `target`'s OWN chain of plans omitted. Keyed on the target rather than the edited pattern, since a paycheck re-pace re-proposes OTHER bills, which don't share the edited paycheck's chain. Null when no omitting-forecast source is wired.</summary>
    /// <param name="target">The bill whose plan is being re-proposed.</param>
    /// <param name="changeKind">Whether this is a bold Suggestion or a cautious Implicit change.</param>
    private decimal? OngoingRateCeilingFor(FinancialPattern target, ChangeKind changeKind) =>
        CeilingOmitting(target.FinanceId, target, changeKind, startingEarmark: false);

    // Invoked once Run() decides navigation should happen — matches this
    // project's existing PatternSaved/AccountSaved/ManualEarmarksSaved
    // callback idiom (set by MainWindow) rather than giving this class a
    // direct reference to EarmarkFormPanel or the tab control. Null (the first
    // argument) means "open a blank Earmark form" (mirrors MainWindow's own
    // existing fallback when a pattern has no plan yet). Never invoked when
    // UserSkippedPlanning is true. The second argument is an accepted
    // suggestion's field overrides (EarmarkFieldOverrideKeys) to pre-fill the
    // form with as unsaved edits, or null for a plain open.
    public Action<EarMarkPattern?, IReadOnlyDictionary<string, object?>?>? NavigateToEarmarkForm { get; set; }

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
    // wired up — most tests) falls back to DefaultOutcome's own
    // safest, least-destructive answer to every question at once, so Run()
    // still completes end to end without a live UI.
    public Func<ImplicitChangeConfirmationRequest, ConfirmationOutcome>? ConfirmImplicitChanges { get; set; }

    /// <summary>[STEP] The single entry point: works out whether anything needs asking, shows the confirmation if so, saves the FinancialPattern, performs whatever implicit EarMarkPattern/EarMarkEvent changes were decided, and (on Save and Plan) calls NavigateToEarmarkForm. See the class's own STATUS note for exactly which of PerformImplicitEarmarkChanges' branches are real versus still TODO.</summary>
    /// <returns>False if the user cancelled out of the confirmation — nothing was saved, exactly as if Save had never been clicked. True otherwise, including every case where nothing needed asking at all.</returns>
    public bool Run()
    {
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

        // planning/25's Item G — same "read before anything is saved" timing
        // as every other Determine* call. Populates the successor plan-shape
        // choices (Q2) shown whenever a Critical edit breaks off; empty for a
        // non-break-off edit or a genuinely concurrent multi-plan set.
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

        // The front-boundary twin (M2): the same invariant, and the same
        // "read before write" timing, for a Start moving forward in place —
        // otherwise a plan left starting before the new Start crashes the next
        // GetAll() exactly as an over-long Until used to.
        DetermineFrontTruncationsIfApplicable();

        // The outward twin of the two truncations (M2 piece 3): a boundary of the
        // goal moving OUT, with a plan that shared it, means that plan grows to
        // keep tracking it. Same "read the old boundary before PerformSave writes
        // the new one" timing as its inward siblings.
        DetermineBoundaryExtensionsIfApplicable();

        // Cross-boundary Q6 (planning/28): a later segment the Amount cascade will
        // land on, funded by more than one plan, gets its own combine-or-keep-
        // separate question — worked out here so the confirmation can ask it. Its
        // own row, deliberately never the break-off Consolidation one.
        DetermineCrossBoundaryConsolidationsIfApplicable();

        // The funding side of the break-off's own keep-separate choice: if keeping
        // this segment's several plans separate would over/underfund the new
        // amount, work out the proportional correction now so the confirmation can
        // offer it — its own nested question, computed after the chain conditions
        // it depends on (_chainHasSuccessor). Runs before anything is saved, same
        // as its sibling Determine* calls.
        DetermineKeepSeparateFundingIfApplicable();

        // The goal-health suggestion (planning/25's deferred picker): if editing
        // this goal leaves its single savings plan out of step with it, work out
        // a corrected plan to offer — accepting it pre-fills that plan's form with
        // the fix as an unsaved edit. Its own accept/reject row.
        DetermineGoalHealthSuggestionIfApplicable();

        // The plan's own health heads-up (formerly a post-save MessageBox) — worked
        // out here so it can ride the confirmation as an announcement row instead,
        // keeping all of this save's messaging in the one place this class drives.
        DetermineConcerningPlanNoticeIfApplicable();

        // SETTLED 2026-08-11 (author): Item F's own question applies
        // regardless of which Item E path gets chosen — it is NOT
        // break-off-only. What "consolidate" means differs by path: for
        // break-off, it's Item C's existing "always fresh-propose one
        // successor" shape; for a retroactive correction (same finance_id,
        // no successor), it means collapsing the existing plans in place
        // under that same finance_id instead — a genuinely new mechanism,
        // not yet designed or built (see PerformImplicitEarmarkChanges's own
        // TODOs). One combined pass either way, per the note above.
        if (IsChangeCritical || HasMultipleEarmarkPatterns || TouchesChainBoundary || ChangeCanCascade || TrivialFieldsCanCascade || !string.IsNullOrEmpty(SourceChangeWarning) || PacedBillsCanCascade || _goalHealthSuggestedPlan is not null || !string.IsNullOrEmpty(_concerningPlanNotice) || _boundaryExtensions is { AddedGoalOccurrences: > 0 })
        {
            var outcome = ConfirmImplicitChanges?.Invoke(BuildConfirmationRequest()) ?? DefaultOutcome();
            if (!outcome.Proceed)
            {
                return false;
            }

            ChosenPlanShape = outcome.ChosenPlanShape;
            UserChoseStayLinked = ChoseStayLinked(outcome);
            UserChoseCascadeForward = ChoseCascadeForward(outcome);
            UserChoseCascadeTrivialFields = ChoseCascadeTrivialFields(outcome);
            UserChoseToRepaceBills = ChoseToRepaceBills(outcome);
            UserChoseCombinePlans = ChoseCombinePlans(outcome);
            UserChoseToAdjustKeptSeparatePlans = ChoseToAdjustKeptSeparatePlans(outcome);
            _successorCombineChoices = _crossBoundaryConsolidations.ToDictionary(
                successor => successor.FinanceId,
                // [1] combine, [0] keep separate (the default, also for a headless caller).
                successor => Chosen(outcome, ConfirmationRowIds.CrossBoundaryConsolidation(successor.FinanceId)) == 1);
            _chosenSizing = ChoseConsolidationSizing(outcome);
            _chosenSpread = ChoseConsolidationSpread(outcome);

            // An accepted goal-health suggestion becomes the overrides the plan's
            // form opens pre-filled with. Only its amount today; the whole proposed
            // plan is on hand for richer overrides later.
            if (_goalHealthSuggestedPlan is { } suggested && ChoseAcceptGoalHealthSuggestion(outcome))
            {
                _acceptedSuggestionOverrides = new Dictionary<string, object?>
                {
                    [EarmarkFieldOverrideKeys.Amount] = suggested.Amount,
                };
            }
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
            NavigateToEarmarkForm?.Invoke(target, _acceptedSuggestionOverrides);
        }

        return true;
    }

    /// <summary>[CALC] The default outcome when no ConfirmImplicitChanges delegate is wired up (most tests, and any host that hasn't connected a real popup) — a bare Proceed with no selections. Every question then reads back as its own safe default, since the interpreters below treat a row absent from the map as its default: stay linked, cascade a rate/schedule change forward, don't cascade trivial fields, leave paced bills alone. Always proceeds — there's no one here to cancel on.</summary>
    private static ConfirmationOutcome DefaultOutcome() => new() { Proceed = true };

    // The interpreters that turn the popup's raw per-row selection back into a
    // decision — the semantics the popup itself no longer knows. A row absent
    // from the outcome (the popup never drew it, or a headless caller returned
    // a bare Proceed) reads as index -1, which each rule below resolves to the
    // settled safe default. The option indices mirror, exactly, the order
    // BuildRows lays each ChoiceRow out in.
    private static int Chosen(ConfirmationOutcome outcome, string rowId) =>
        outcome.ChosenOptionIndex.TryGetValue(rowId, out var index) ? index : -1;

    // chain-boundary: [0] keep linked (default), [1] let it break.
    private static bool ChoseStayLinked(ConfirmationOutcome outcome) => Chosen(outcome, ConfirmationRowIds.ChainBoundary) != 1;

    // cascade: [0] apply forward (default), [1] only this segment.
    private static bool ChoseCascadeForward(ConfirmationOutcome outcome) => Chosen(outcome, ConfirmationRowIds.Cascade) != 1;

    // trivial-fields cascade: [0] only this segment (default), [1] apply forward.
    private static bool ChoseCascadeTrivialFields(ConfirmationOutcome outcome) => Chosen(outcome, ConfirmationRowIds.TrivialFieldsCascade) == 1;

    // paced-bills cascade: [0] update them (the popup's own pre-selection), [1]
    // leave them. Absent (headless) reads as leave — nothing re-paces money
    // when no one was actually asked.
    private static bool ChoseToRepaceBills(ConfirmationOutcome outcome) => Chosen(outcome, ConfirmationRowIds.PacedBillsCascade) == 0;

    // consolidation: [0] keep separate (default, also headless), [1] combine.
    // Only consulted for a break-off that ISN'T forcing consolidation (a
    // shape/start change forces it regardless of this answer).
    private static bool ChoseCombinePlans(ConfirmationOutcome outcome) => Chosen(outcome, ConfirmationRowIds.Consolidation) == 1;

    // keep-separate funding: [0] adjust to meet the goal (the popup's own
    // pre-selection), [1] leave them. Absent (headless) reads as leave — each
    // plan keeps its own rate, nothing re-rates money when no one was asked.
    private static bool ChoseToAdjustKeptSeparatePlans(ConfirmationOutcome outcome) => Chosen(outcome, ConfirmationRowIds.KeepSeparateFunding) == 0;

    // goal-health suggestion: [0] load the suggested contribution (the popup's own
    // pre-selection), [1] leave it. Absent (headless) reads as leave — the form
    // opens plain, no correction pre-filled, when no one accepted one.
    private static bool ChoseAcceptGoalHealthSuggestion(ConfirmationOutcome outcome) => Chosen(outcome, ConfirmationRowIds.GoalHealthSuggestion) == 0;

    // consolidation sizing: [0] meet the goal (default, also headless), [1] keep the current rate.
    private static ConsolidationSizing ChoseConsolidationSizing(ConfirmationOutcome outcome) =>
        Chosen(outcome, ConfirmationRowIds.ConsolidationSizing) == 1 ? ConsolidationSizing.KeepCurrentPace : ConsolidationSizing.MeetGoal;

    // consolidation spread: [0] across paydays (default, also headless), [1] evenly.
    private static ConsolidationSpread ChoseConsolidationSpread(ConfirmationOutcome outcome) =>
        Chosen(outcome, ConfirmationRowIds.ConsolidationSpread) == 1 ? ConsolidationSpread.Evenly : ConsolidationSpread.AcrossPaydays;

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
        _startChanged = saved.DatePattern.ActiveStart != _proposedPattern.DatePattern.ActiveStart;

        _amountChanged = saved.Amount != _proposedPattern.Amount;
        var restrictedFieldChanged = _recurrenceShapeChanged || _startChanged || _amountChanged;
        _isAmountOnlyChange = _amountChanged && !_startChanged && !_recurrenceShapeChanged;

        // 1.2.3.10.a5's own trigger — an occurrence on or before the as-of
        // date is exactly "an expected transaction on an expired page in the
        // past" under today's single-page model (the assumptions glossary
        // Ch.20's own formal-vs-practical note). Checked against the SAVED pattern's own
        // occurrences, not the proposed one — what already happened is fixed
        // regardless of what's being typed now.
        var hasPastOccurrence = saved.DatePattern.GetOccurrences(to: forecast.AsOfDate).Count > 0;

        // A Critical edit normally breaks off at today, preserving the pre-cut
        // occurrences as a truncated predecessor. But if that cut would leave no
        // more than one day of the saved pattern before it — a pattern that only
        // started today or yesterday — there is nothing worth preserving: the whole
        // "history" is forecast, not settled. Such an edit is a plain in-place
        // replace (the negligible old segment is simply overwritten), NOT a break-off
        // — so no "you're changing history" confirmation, and, crucially, no
        // BreakOffFactory rejection ("the cut date must be after the pattern's own
        // start"). Author, 2026-09-05: this resolves the sharp edge
        // PerformSingleSuccessorBreakOff's own comment used to defer — a real bill
        // (a mortgage) edited on its own start date hit it in the portable demo.
        var negligibleHistoryToPreserve = forecast.AsOfDate <= saved.DatePattern.ActiveStart.AddDays(1);

        IsChangeCritical = restrictedFieldChanged && hasPastOccurrence && !negligibleHistoryToPreserve;

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

        var hasPredecessor = otherPatterns.Any(pattern => pattern.DatePattern.ActiveStart < saved.DatePattern.ActiveStart);
        var hasSuccessor = otherPatterns.Any(pattern => pattern.DatePattern.ActiveStart > saved.DatePattern.ActiveStart);

        // Start's own literal move — deliberately narrower than _startChanged
        // above, which also counts ActiveFrom (Item E's own narrowing
        // trigger, unrelated to chain contiguity: a predecessor's own Until
        // connects to THIS pattern's own Start, never its ActiveFrom lead-in).
        var startChanged = saved.DatePattern.ActiveStart != _proposedPattern.DatePattern.ActiveStart;
        var untilChanged = saved.DatePattern.Until != _proposedPattern.DatePattern.Until;
        var trivialFieldsChanged = saved.Priority != _proposedPattern.Priority
            || saved.Mandatory != _proposedPattern.Mandatory
            || saved.Description != _proposedPattern.Description
            || saved.AutoRenew != _proposedPattern.AutoRenew;

        _chainHasSuccessor = hasSuccessor;

        // Editing a segment that has a LATER segment in its chain is NOT a
        // break-off, even when it reaches already-occurred history — it's
        // forward-only's own "open the earliest segment you want changed; the
        // change flows forward from there" (planning/28, reconciling planning/27's
        // own earlier scope note with planning/16 F24's "editing and break-off
        // are separate, deliberately-chosen actions"). Such a segment is saved
        // in place and its own occurrences change (PerformSave and
        // PerformImplicitEarmarkChanges route on the same hasSuccessor), and
        // these chain questions apply to it. Only a Critical edit of the CURRENT
        // segment (no successor) still breaks off from today — there the typed
        // Start/Until never lands on the existing chain, so the boundary question
        // stays suppressed for that one case (the cascade/trivial rows already
        // require hasSuccessor, so they suppress themselves there).
        TouchesChainBoundary = (!IsChangeCritical || hasSuccessor) && ((startChanged && hasPredecessor) || (untilChanged && hasSuccessor));
        ChangeCanCascade = (_amountChanged || _recurrenceShapeChanged) && hasSuccessor;
        TrivialFieldsCanCascade = trivialFieldsChanged && hasSuccessor;

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

    /// <summary>[READS FILE] planning/25's Item G: works out which alternative plan shapes — beyond AllocationPlanProposer.Propose's own default — are genuinely available for this break-off's successor, stored in _planShapeCandidates for BuildConfirmationRequest to show and PerformSingleSuccessorBreakOff/PerformMultiPlanBreakOff to apply whichever gets chosen. Runs regardless of what UserChooseAlterPast will turn out to be — not known yet when this runs, same "compute eagerly, apply conditionally" shape DetermineConsolidationPlanIfApplicable already uses — and simply goes unused if the retroactive-correction side is chosen instead, where no fresh plan ever gets proposed. internal for the same reason its siblings are — so a test can call this directly ahead of PerformSingleSuccessorBreakOff.</summary>
    internal void DeterminePlanShapeCandidatesIfApplicable()
    {
        // Only for a Critical edit of the CURRENT segment (a real break-off) —
        // the candidates are shapes for the break-off's freshly-proposed
        // successor plan. An EARLIER-segment edit (Critical but with a later
        // segment) is saved in place and never breaks off, so there's no
        // successor to propose a plan for; running this there would propose a
        // plan starting today against the earlier segment's own past-starting
        // schedule and throw ("can't begin allocating before its goal's span
        // starts").
        if (!IsChangeCritical || _chainHasSuccessor)
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
            // Held to what the funds can afford, like the new-expense default — both ceilings measured against
            // the EDITED goal's chain, not the successor's own: the successor isn't saved yet, and though it
            // shares that Source its own chain lookup would find nothing to free up. Suggestion tier since the
            // user reviews and picks this from the candidate list.
            new("Recommended", AllocationPlanProposer.Propose(successor, allPatterns, cutDate,
                carriedOverJarBalance: carriedOverJarBalance,
                startingEarmarkCeiling: CeilingOmitting(_financeId, successor, ChangeKind.Suggestion, startingEarmark: true),
                ongoingRateCeiling: CeilingOmitting(_financeId, successor, ChangeKind.Suggestion, startingEarmark: false))),
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

    /// <summary>[READS FILE] The front-boundary mirror of DetermineBackTruncationsIfApplicable (M2): works out whether any existing EarMarkPattern now starts BEFORE the proposed pattern's own Start — 3.11.2.a2 has to keep holding after ANY save — and if so, everything ApplyFrontTruncationsIfNeeded needs: which plans start too early, and which ManualEarmarks now fall before it and need deleting. Stored in _frontTruncations, same "read before PerformSave writes anything" reasoning as the back side. Runs unconditionally, not gated on IsChangeCritical — a Start moving forward in place (only ever a non-Critical future edit; a Critical one breaks off instead) is exempt from ASKING but never from keeping its plans valid. internal for the same reason DetermineBackTruncationsIfApplicable is — so a test can call it directly ahead of ApplyFrontTruncationsIfNeeded.</summary>
    internal void DetermineFrontTruncationsIfApplicable()
    {
        var forecast = _requestForecast();
        var newStart = _proposedPattern.DatePattern.ActiveStart;

        var plansStartingBeforeNewStart = forecast.Book.EarMarkPatternsFor(_financeId)
            .Where(plan => plan.DatePattern.ActiveStart < newStart)
            .ToList();

        if (plansStartingBeforeNewStart.Count == 0)
        {
            return; // every existing plan already starts on or after the new Start — nothing to fix
        }

        // Safe here — nothing has been saved yet this Run(), so every
        // ManualEarmark still validates against its own (unchanged) plan.
        var orphanedDates = _repositories.ManualEarmarks.GetAll()
            .Where(earmark => earmark.FinanceId == _financeId && earmark.Date < newStart)
            .Select(earmark => earmark.Date)
            .ToList();

        _frontTruncations = new FrontTruncationPlan(plansStartingBeforeNewStart, orphanedDates);
    }

    /// <summary>[READS FILE] The OUTWARD counterpart to the two Determine…Truncations (M2 piece 3): works out whether the proposed pattern's own Start or Until has moved OUTWARD from what's saved (Until later, or a future Start earlier) and, if so, which of its plans shared that exact boundary and should therefore track it out — captured before PerformSave overwrites the goal's old boundary. Stored in _boundaryExtensions; null when nothing moved outward, or no plan shared the moved boundary. Scoped to the directly-edited goal's own plans only (see the field's own note). internal so a test can call it directly ahead of ApplyBoundaryExtensionsIfNeeded.</summary>
    internal void DetermineBoundaryExtensionsIfApplicable()
    {
        var saved = _repositories.FinancialPatterns.GetByFinanceId(_financeId);
        if (saved is null)
        {
            return; // brand-new pattern — no old boundary to have moved outward from
        }

        var plans = _requestForecast().Book.EarMarkPatternsFor(_financeId);

        var sharingOldUntil = _proposedPattern.DatePattern.Until > saved.DatePattern.Until
            ? plans.Where(plan => plan.DatePattern.Until == saved.DatePattern.Until).ToList()
            : [];
        var sharingOldStart = _proposedPattern.DatePattern.ActiveStart < saved.DatePattern.ActiveStart
            ? plans.Where(plan => plan.DatePattern.ActiveStart == saved.DatePattern.ActiveStart).ToList()
            : [];

        if (sharingOldStart.Count == 0 && sharingOldUntil.Count == 0)
        {
            return; // nothing moved outward, or no plan shared the boundary that did
        }

        // How many more times the goal now occurs in the range the move opened up
        // — the proposed pattern's own occurrences past the old Until, plus any
        // before the old Start. Counted off the proposed (post-edit) shape, since
        // that's what will actually occur going forward.
        var addedPastOldUntil = _proposedPattern.DatePattern.Until > saved.DatePattern.Until
            ? _proposedPattern.DatePattern.GetOccurrences(saved.DatePattern.Until.AddDays(1), _proposedPattern.DatePattern.Until).Count
            : 0;
        var addedBeforeOldStart = _proposedPattern.DatePattern.ActiveStart < saved.DatePattern.ActiveStart
            ? _proposedPattern.DatePattern.GetOccurrences(_proposedPattern.DatePattern.ActiveStart, saved.DatePattern.ActiveStart.AddDays(-1)).Count
            : 0;

        _boundaryExtensions = new BoundaryExtensionPlan(sharingOldStart, sharingOldUntil, addedPastOldUntil + addedBeforeOldStart);
    }

    /// <summary>[CALC] The "[bill] occurs N more times" heads-up for a boundary that moved outward and grew a plan to track it — "" when no boundary extended a plan, or the move added no new occurrence. Names the goal's own added occurrences, and that its savings plan grows to keep pace (the money consequence of the otherwise-silent extend).</summary>
    private string DescribeBoundaryExtensionAnnouncement()
    {
        if (_boundaryExtensions is not { AddedGoalOccurrences: > 0 } ext)
        {
            return "";
        }

        var times = ext.AddedGoalOccurrences == 1 ? "1 more time" : $"{ext.AddedGoalOccurrences} more times";
        return $"{_proposedPattern.Source} now occurs {times} — its savings plan grows to keep pace.";
    }

    /// <summary>[READS FILE] Works out which later finance patterns this finance pattern's own Amount change will be carried forward onto that are funded by MORE THAN ONE earmark pattern (planning/28's cross-boundary Q6) — each needs its own combine-or-keep-separate question, since folding several earmark patterns into one is a real choice, not a forced one. Amount-only successors only: a schedule change moves the dates and forces them to consolidate with no question (ReconcileCascadedSuccessorSavingsPlans handles that directly). Stored in _crossBoundaryConsolidations for the confirmation to ask about; a no-op unless this change can actually be carried forward. Same "read before anything is saved" timing as its sibling Determine* calls. internal so a test can drive it directly.</summary>
    internal void DetermineCrossBoundaryConsolidationsIfApplicable()
    {
        if (!ChangeCanCascade || _chainContext is not { } context)
        {
            return;
        }

        var book = _requestForecast().Book;
        var candidates = new List<FinancialPattern>();
        var anyConsolidating = false;

        foreach (var successor in context.OtherPatterns.Where(pattern => pattern.DatePattern.ActiveStart > context.Saved.DatePattern.ActiveStart))
        {
            var shapeChanged = !successor.DatePattern.HasSameShapeAs(_proposedPattern.DatePattern);
            var amountChanged = successor.Amount != _proposedPattern.Amount;
            if (!amountChanged && !shapeChanged)
            {
                continue; // the carry-forward doesn't touch this successor
            }

            if (book.EarMarkPatternsFor(successor.FinanceId).Count <= 1)
            {
                continue; // a single earmark pattern is re-rated/re-aligned, never consolidated
            }

            // Multi-earmark-pattern: a schedule change forces a fold; an amount-only
            // change offers the combine-or-keep-separate question. Either way a
            // consolidation is possible, so the two strategy questions apply.
            anyConsolidating = true;
            if (amountChanged && !shapeChanged)
            {
                candidates.Add(successor);
            }
        }

        _crossBoundaryConsolidations = candidates;
        _consolidationStrategyApplies = anyConsolidating;
        // The spread question only differs when Consolidate's own AcrossPaydays would
        // actually pace — i.e. exactly one income stream to pace against. "Still
        // paying on or after today" (not a bare Amount > 0) so a broken-off income's
        // truncated predecessor doesn't count as a phantom second stream and suppress
        // the question (same trap as AllocationPlanProposer.ActiveIncomeStreams).
        var asOfToday = _requestForecast().AsOfDate;
        _consolidationHasIncomeForSpread = anyConsolidating
            && book.AllFinancialPatterns().Count(pattern => pattern.Amount > 0m && pattern.DatePattern.Until >= asOfToday) == 1;
    }

    /// <summary>[READS FILE] Dry-runs the break-off's own keep-separate outcome and re-rates it to meet the goal, so the confirmation knows whether keeping this segment's several plans separate would leave the new amount over/underfunded — the trigger for the nested "adjust them to meet it?" question. Only a current-segment break-off whose plans could actually stay separate (more than one, no forced consolidation); a no-op otherwise. Reads the current forecast before anything is saved, same as its sibling Determine* calls; the dry-run's own new finance_id is throwaway — only the current-vs-needed totals are kept, so the real break-off recomputes the correction against the real successor at execution time.</summary>
    private void DetermineKeepSeparateFundingIfApplicable()
    {
        // The same gate PerformImplicitEarmarkChanges routes a keep-separate
        // break-off under: a Critical edit of the CURRENT segment (not an
        // earlier one that cascades in place), more than one plan, and nothing
        // forcing them to consolidate.
        if (!IsChangeCritical || _chainHasSuccessor || !HasMultipleEarmarkPatterns || ConsolidationNeeded)
        {
            return;
        }

        var saved = _repositories.FinancialPatterns.GetByFinanceId(_financeId);
        if (saved is null)
        {
            return;
        }

        var forecast = _requestForecast();
        var cutDate = forecast.AsOfDate;

        // The break-off needs the cut strictly after the segment's own start
        // (BreakOffFactory.ValidateCutBoundaries) — PerformSingleSuccessorBreakOff's
        // own documented sharp edge. When it doesn't hold there's no break-off to
        // preview a funding gap for.
        if (cutDate <= saved.DatePattern.ActiveStart)
        {
            return;
        }

        var result = BreakOffFactory.BreakOffKeepingPlansSeparate(new MultiPlanBreakOffRequest
        {
            Predecessor = saved,
            PredecessorPlans = forecast.Book.EarMarkPatternsFor(_financeId),
            CutDate = cutDate,
            SuccessorFinanceId = forecast.Book.NextFinanceId(),
            SuccessorAmount = _proposedPattern.Amount,
            SuccessorSchedule = BuildSuccessorSchedule(cutDate),
            CarriedOverJarBalance = JarBalanceOn(cutDate, _financeId),
            AllPatterns = forecast.Book.AllFinancialPatterns(),
        });

        var ceiling = AffordabilityCeilingFor(result.Successor, ChangeKind.Implicit);
        _keepSeparateFunding = EarmarkScaling.ScaleToMeetGoal(result.Successor, result.SuccessorPlans, affordabilityCeiling: ceiling);
    }

    /// <summary>[CALC] The nested keep-separate funding question's own wording — "" whenever there's no gap to correct (the plans already fund the new amount, or no keep-separate break-off is on the table), so the row isn't shown at all. Names which way it's off (short of, or more than) so the ask isn't a bare yes/no, matching the "show the consequence" standard.</summary>
    private string DescribeKeepSeparateFundingQuestion()
    {
        if (_keepSeparateFunding is not { } funding || Math.Abs(funding.CurrentTotal - funding.NeededTotal) < 0.01m)
        {
            return "";
        }

        var problem = funding.CurrentTotal < funding.NeededTotal
            ? "won't fully cover"
            : "would set aside more than";
        return $"Kept separate, these savings plans {problem} the new amount. Adjust their contributions to meet it?";
    }

    /// <summary>[READS FILE] Works out whether the single savings plan under the edited goal would no longer meet it after this save — and if so, what corrected plan to suggest (EarmarkScaling.ScaleToMeetGoal: the plan's own schedule kept, the amount re-sized to the edited goal). Stored for the confirmation to offer as an accept/reject question, where accepting pre-fills the plan's form with the correction. Scoped to a single-plan, amount-only, non-break-off edit: the plan's span is unchanged (so it still fits the edited goal), a break-off already fresh-proposes a plan that meets the goal, several plans resolve via implicit changes (no single form to pre-fill), and "Save and skip planning" opens no form. A non-Critical edit also means the plan has no past occurrence, so scaling it is well-formed — no realized past contributions to mis-size against. Reads before anything is saved, like its sibling Determine* calls.</summary>
    private void DetermineGoalHealthSuggestionIfApplicable()
    {
        if (UserSkippedPlanning
            || IsChangeCritical
            || HasMultipleEarmarkPatterns
            || !_isAmountOnlyChange)
        {
            return;
        }

        var plan = _requestForecast().Book.EarMarkPatternsFor(_financeId).SingleOrDefault();
        if (plan is null)
        {
            return;
        }

        // The plan is future here (a non-Critical edit has no past occurrence),
        // so scaling it to the edited goal is well-formed. Same machinery the
        // keep-separate funding uses; offered only when the plan doesn't already
        // meet the edited goal (its current and needed totals differ).
        var ceiling = AffordabilityCeilingFor(_proposedPattern, ChangeKind.Suggestion);
        var scaling = EarmarkScaling.ScaleToMeetGoal(_proposedPattern, [plan], affordabilityCeiling: ceiling);
        if (Math.Abs(scaling.CurrentTotal - scaling.NeededTotal) >= 0.01m)
        {
            _goalHealthSuggestedPlan = scaling.ScaledPlans.Single();
            _goalHealthUnderfunds = scaling.NeededTotal > scaling.CurrentTotal;
        }
    }

    /// <summary>[CALC] The goal-health suggestion question's own wording — "" when there's no correction to offer (so the row isn't shown). Names the suggested contribution so the choice isn't a bare yes/no.</summary>
    private string DescribeGoalHealthSuggestion()
    {
        if (_goalHealthSuggestedPlan is not { } suggested)
        {
            return "";
        }

        return $"This change leaves the savings plan out of step with the goal. Load a suggested contribution of {Math.Abs(suggested.Amount):C} into the plan?";
    }

    /// <summary>[CALC] The consequence shown under the goal-health suggestion's "leave it as is" option — names what rejecting costs (the goal falling short, or money tied up). "" when there's no suggestion, so the reject option carries no footer.</summary>
    private string DescribeGoalHealthRejectWarning()
    {
        if (_goalHealthSuggestedPlan is null)
        {
            return "";
        }

        return _goalHealthUnderfunds
            ? "Left as is, the plan keeps saving less than the goal needs, so it will fall short."
            : "Left as is, the plan keeps saving more than the goal needs, tying up money it won't use.";
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

    /// <summary>[READS FILE] Builds what ConfirmImplicitChanges needs to render the Item B/C/E/F confirmation, plus planning/27's own Phase 1 questions (chain boundary, Amount/shape cascade, trivial-fields cascade, Source-change warning) — everything DetermineConditions/DetermineChainConditionsIfApplicable already worked out, plus a plain-language description of what changed. [READS FILE] because the chain-boundary/cascade previews below dry-run BreakOffFactory calls and read EarMarkPatternsFor for the absorb warning's own plan count — safe here, same as everywhere else in this class, since nothing has been saved yet this Run(). TODO: doesn't yet name the specific amount/date that would be orphaned by a retroactive correction (Item E's own "show the consequence, not just a yes/no" — mockups/editing-history-confirmation-mockups.html, Popup 1 · B) — the generic description below is a simpler first cut.</summary>
    private ImplicitChangeConfirmationRequest BuildConfirmationRequest()
    {
        // An earlier-segment edit (Critical, but the segment has a LATER one in
        // its chain) is NOT a break-off — it's saved in place and cascades
        // forward. So the break-off / consolidation / plan-shape machinery
        // doesn't apply to it; only the chain questions do. The break-off
        // announcement, the consolidation question, and the plan-shape picker
        // are all suppressed for it here; BuildDescription names the case
        // instead.
        var editingEarlierSegment = IsChangeCritical && _chainHasSuccessor;

        var inputs = new RowInputs
        {
            IsChangeCritical = IsChangeCritical && !editingEarlierSegment,
            HasMultipleEarmarkPatterns = HasMultipleEarmarkPatterns && !editingEarlierSegment,
            ConsolidationNeeded = ConsolidationNeeded && !editingEarlierSegment,
            ConsolidationForcedReason = editingEarlierSegment ? "" : DescribeConsolidationForcedReason(),
            KeepSeparateFundingQuestion = editingEarlierSegment ? "" : DescribeKeepSeparateFundingQuestion(),
            TouchesChainBoundary = TouchesChainBoundary,
            ChangeCanCascade = ChangeCanCascade,
            TrivialFieldsCanCascade = TrivialFieldsCanCascade,
            SourceChangeWarning = SourceChangeWarning,
            GoalHealthSuggestionQuestion = DescribeGoalHealthSuggestion(),
            GoalHealthRejectWarning = DescribeGoalHealthRejectWarning(),
            ConcerningPlanNotice = _concerningPlanNotice,
            BoundaryExtensionAnnouncement = DescribeBoundaryExtensionAnnouncement(),
            StayLinkedWarning = TouchesChainBoundary ? DescribeChainStayLinkedConsequence() : "",
            LetItBreakWarning = TouchesChainBoundary ? DescribeChainLetItBreakConsequence() : "",
            CascadeDescription = ChangeCanCascade ? DescribeChainCascadeConsequence() : "",
            TrivialFieldsCascadeDescription = TrivialFieldsCanCascade ? DescribeChainTrivialFieldsCascadeConsequence() : "",
            PacedBillsCanCascade = PacedBillsCanCascade,
            PacedBillsCascadeDescription = PacedBillsCanCascade ? DescribePacedBillsCascadeConsequence() : "",
            // Not gated on editingEarlierSegment: an earlier-segment edit is saved
            // in place and DOES cascade forward, so its later multi-plan segments
            // still get this question, unlike the break-off-only rows above.
            CrossBoundaryConsolidations = _crossBoundaryConsolidations
                .Select(successor => new CrossBoundaryConsolidationInput(successor.FinanceId, successor.Description))
                .ToList(),
            ShowConsolidationSizing = _consolidationStrategyApplies,
            ShowConsolidationSpread = _consolidationHasIncomeForSpread,
        };

        return new ImplicitChangeConfirmationRequest
        {
            Description = BuildDescription(),
            PlanShapeCandidates = editingEarlierSegment ? [] : _planShapeCandidates,
            Rows = ConfirmationRowBuilder.BuildRows(inputs),
        };
    }

    /// <summary>[READS FILE] Previews what "stay linked" would actually do to _proposedPattern's own neighbors, for the confirmation row's own warning slot — "" for a plain, contiguous nudge (never destructive), or a real sentence naming which segment(s) would be absorbed. Unlike the EarMarkPattern-chain case, absorbing a whole FinancialPattern genuinely deletes everything under its own now-gone FinanceId — its own EarMarkPattern chain (if any) and every ManualEarmark tied to it, not just the row itself (this document's own explicit distinction) — named here, not glossed over, matching the "show the consequence" standard. [READS FILE] to count each absorbed segment's own EarMarkPatterns.</summary>
    private string DescribeChainStayLinkedConsequence()
    {
        if (_chainContext is not { } context)
        {
            return "";
        }

        var predecessors = context.OtherPatterns.Where(pattern => pattern.DatePattern.ActiveStart < context.Saved.DatePattern.ActiveStart).ToList();
        var successors = context.OtherPatterns.Where(pattern => pattern.DatePattern.ActiveStart > context.Saved.DatePattern.ActiveStart).ToList();
        var absorbed = new List<FinancialPattern>();

        if (context.Saved.DatePattern.ActiveStart != _proposedPattern.DatePattern.ActiveStart && predecessors.Count > 0)
        {
            absorbed.AddRange(BreakOffFactory.ExtendStart(_proposedPattern, predecessors, _proposedPattern.DatePattern.ActiveStart).Absorbed);
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
        var ordered = absorbed.OrderBy(pattern => pattern.DatePattern.ActiveStart).ToList();
        var descriptions = ordered.Select(pattern =>
        {
            var planCount = forecast.Book.EarMarkPatternsFor(pattern.FinanceId).Count;
            var label = string.IsNullOrWhiteSpace(pattern.Description) ? pattern.Source : pattern.Description;
            var range = $"{pattern.DatePattern.ActiveStart:MMM d, yyyy} – {pattern.DatePattern.Until:MMM d, yyyy}";
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

        var predecessors = context.OtherPatterns.Where(pattern => pattern.DatePattern.ActiveStart < context.Saved.DatePattern.ActiveStart).ToList();
        var successors = context.OtherPatterns.Where(pattern => pattern.DatePattern.ActiveStart > context.Saved.DatePattern.ActiveStart).ToList();
        var consequences = new List<string>();

        if (context.Saved.DatePattern.ActiveStart != _proposedPattern.DatePattern.ActiveStart && predecessors.Count > 0)
        {
            var predecessor = predecessors.OrderByDescending(pattern => pattern.DatePattern.ActiveStart).First();
            consequences.Add(BreakOffFactory.SpansOverlap(_proposedPattern, predecessor)
                ? $"it will overlap with the segment before it ({predecessor.DatePattern.ActiveStart:MMM d, yyyy} – {predecessor.DatePattern.Until:MMM d, yyyy})"
                : $"a gap will open before it, from {predecessor.DatePattern.Until.AddDays(1):MMM d, yyyy} to {_proposedPattern.DatePattern.ActiveStart.AddDays(-1):MMM d, yyyy}");
        }

        if (context.Saved.DatePattern.Until != _proposedPattern.DatePattern.Until && successors.Count > 0)
        {
            var successor = successors.OrderBy(pattern => pattern.DatePattern.ActiveStart).First();
            consequences.Add(BreakOffFactory.SpansOverlap(_proposedPattern, successor)
                ? $"it will overlap with the segment after it ({successor.DatePattern.ActiveStart:MMM d, yyyy} – {successor.DatePattern.Until:MMM d, yyyy})"
                : $"a gap will open after it, from {_proposedPattern.DatePattern.Until.AddDays(1):MMM d, yyyy} to {successor.DatePattern.ActiveStart.AddDays(-1):MMM d, yyyy}");
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

        var successors = context.OtherPatterns.Where(pattern => pattern.DatePattern.ActiveStart > context.Saved.DatePattern.ActiveStart).ToList();
        if (successors.Count == 0)
        {
            return "";
        }

        var furthest = successors.Max(pattern => pattern.DatePattern.Until);
        return successors.Count == 1
            ? $"This edit covers {_proposedPattern.DatePattern.ActiveStart:MMM d, yyyy} – {_proposedPattern.DatePattern.Until:MMM d, yyyy}. Cascading forward would also update the segment running through {furthest:MMM d, yyyy}."
            : $"This edit covers {_proposedPattern.DatePattern.ActiveStart:MMM d, yyyy} – {_proposedPattern.DatePattern.Until:MMM d, yyyy}. Cascading forward would also update {successors.Count} later segments, through {furthest:MMM d, yyyy}.";
    }

    /// <summary>[CALC] Names how many later segments the trivial-fields cascade (Priority/Mandatory/Description/AutoRenew) would reach — Phase 1's own third question, with no EarMarkPattern equivalent. Simpler than DescribeChainCascadeConsequence's own text since there's no "date range" concept for fields that don't affect timing at all.</summary>
    private string DescribeChainTrivialFieldsCascadeConsequence()
    {
        if (_chainContext is not { } context)
        {
            return "";
        }

        var successors = context.OtherPatterns.Where(pattern => pattern.DatePattern.ActiveStart > context.Saved.DatePattern.ActiveStart).ToList();
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

        // An earlier segment (one with a later segment in its chain) is edited in
        // place — its own occurrences change — never broken off, so its message
        // says so plainly (planning/25 Item B: a change touching already-occurred
        // history is never silent).
        if (IsChangeCritical && _chainHasSuccessor)
        {
            return $"You're editing an earlier segment of \"{label}\" — its own past occurrences will change to match.";
        }

        return IsChangeCritical
            ? $"You're changing the {whatChanged} for \"{label}\", and it already has payments recorded."
            : $"\"{label}\" already has more than one savings plan.";
    }

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
        // ConfirmImplicitChanges/NavigateToEarmarkForm rather than fired
        // separately, so this class stays WPF-free. Falls back to the
        // first match — the original placeholder — whenever nothing's
        // wired up (most tests, and any host that hasn't connected one),
        // same "safest default when nothing's connected" reasoning
        // DefaultOutcome already applies elsewhere in this class.
        return PickEarmarkPattern?.Invoke(savingsPlan) ?? savingsPlan[0];
    }

    /// <summary>[READS FILE] Works out the plan's own health heads-up — the "Worth a look" sentence (PlanHealthMessages.CurrentJarStateLine, the same wording the Earmark form's Summary aside uses) — for the confirmation to show as an announcement row, replacing the old separate post-save MessageBox. Only when the plan is worth warning about and it isn't a current-segment break-off (which replaces the plan with a freshly-proposed one that already meets the goal, so its old concern is moot). Surfaced whether or not the user is heading to the plan form — a plan-health warning is worth seeing either way (author's call, 2026-08-27, when this moved into the confirmation). Reads _financeId — pre-save, before any break-off could move the current segment — so it needs no fresh forecast; "" whenever there's nothing to surface. Same read-before-save timing as its sibling Determine* calls.</summary>
    private void DetermineConcerningPlanNoticeIfApplicable()
    {
        if (!ChangeWarrantsSuggestions || (IsChangeCritical && !_chainHasSuccessor))
        {
            return;
        }

        var forecast = _requestForecast();
        if (forecast.PlanHealthStates.FirstOrDefault(health => health.FinanceId == _financeId) is not { } state)
        {
            return;
        }

        if (JarOn(forecast.AsOfDate, _financeId) is not { } jar)
        {
            return;
        }

        _concerningPlanNotice = PlanHealthMessages.CurrentJarStateLine(jar, state);
    }

    /// <summary>[WRITES FILE] Persists the FinancialPattern side of the edit — the proposed pattern saved under the same FinanceId for a plain (non-Critical) edit, or nothing at all when a Critical edit is about to break off instead. Forward-only (planning/28): a Critical edit always breaks off, so its FinancialPattern-side save is two rows under two different FinanceIds (the truncated original plus a brand-new successor), written by PerformImplicitEarmarkChanges — never _proposedPattern saved as-is under _financeId.</summary>
    private void PerformSave()
    {
        // A Critical edit of the CURRENT segment breaks off — its FinancialPattern
        // rows (a truncated original plus a new successor) are written by
        // PerformImplicitEarmarkChanges instead, so nothing is saved here. But a
        // Critical edit of an EARLIER segment (one with a later segment) is saved
        // in place, its own occurrences changing, so it goes through the normal
        // save.
        if (IsChangeCritical && !_chainHasSuccessor)
        {
            return;
        }

        _repositories.FinancialPatterns.Save(_proposedPattern, _accountId);
        ApplyBackTruncationsIfNeeded();
        ApplyFrontTruncationsIfNeeded();
        ApplyBoundaryExtensionsIfNeeded();
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
            if (_frontTruncations is { } front && front.PlansStartingBeforeNewStart.Any(p => p.DatePattern.DtStart == plan.DatePattern.DtStart))
            {
                continue; // also starts before the new Start — ApplyFrontTruncationsIfNeeded brings both its boundaries in line
            }

            if (plan.DatePattern.ActiveStart > _proposedPattern.DatePattern.Until)
            {
                _repositories.EarMarkPatterns.Delete(plan.FinanceId, plan.DatePattern.DtStart);
                continue;
            }

            var truncated = PatternTruncation.EndOn(_proposedPattern, plan, _proposedPattern.DatePattern.Until).Plan!;
            _repositories.EarMarkPatterns.Save(truncated);
        }
    }

    /// <summary>[WRITES FILE] The front-boundary mirror of ApplyBackTruncationsIfNeeded (M2): carries out the fix DetermineFrontTruncationsIfApplicable worked out before anything was saved — deletes any ManualEarmark now dated before the goal's own new Start, then brings every EarMarkPattern that still starts before it back in line — its own Start clamped forward via PatternTruncation.StartOn (which keeps it on the same cadence) when active span survives inside the new range, or deleted outright when its own Until is already before the new Start. absorbedBalance is 0m: a Start moving forward in place is only ever a non-Critical FUTURE edit (a Critical one breaks off), so the dropped occurrences are forecasted, not real accumulated money — there's nothing yet to carry over beyond whatever StartingAllocation the plan already held. Unlike EndOn, StartOn can move the plan's DtStart (its persistence key), so this deletes the pre-clamp row before saving the re-started one. A plan straddling BOTH boundaries is brought in line here (Start then Until), and ApplyBackTruncationsIfNeeded skips it to avoid re-creating its old-key row. A no-op whenever _frontTruncations is null. Called from inside PerformSave, right after the FinancialPattern save.</summary>
    private void ApplyFrontTruncationsIfNeeded()
    {
        if (_frontTruncations is not { } fix)
        {
            return;
        }

        var newStart = _proposedPattern.DatePattern.ActiveStart;

        foreach (var date in fix.OrphanedManualEarmarkDates)
        {
            _repositories.ManualEarmarks.Delete(_financeId, date);
        }

        foreach (var plan in fix.PlansStartingBeforeNewStart)
        {
            if (plan.DatePattern.Until < newStart)
            {
                _repositories.EarMarkPatterns.Delete(plan.FinanceId, plan.DatePattern.DtStart);
                continue;
            }

            var clamped = PatternTruncation.StartOn(plan, _proposedPattern, newStart, absorbedBalance: 0m);
            if (clamped.DatePattern.Until > _proposedPattern.DatePattern.Until)
            {
                // Straddles both boundaries — clamp the far end too, so this one
                // plan is fully brought in line here rather than split across the two
                // appliers (whose keys would otherwise collide once StartOn moves
                // this plan's own DtStart).
                clamped = PatternTruncation.EndOn(_proposedPattern, clamped, _proposedPattern.DatePattern.Until).Plan!;
            }

            _repositories.EarMarkPatterns.Delete(plan.FinanceId, plan.DatePattern.DtStart);
            _repositories.EarMarkPatterns.Save(clamped);
        }
    }

    /// <summary>[WRITES FILE] The OUTWARD counterpart to the two Apply…Truncations (M2 piece 3): carries out the extend DetermineBoundaryExtensionsIfApplicable worked out — each plan that shared the goal's own now-outward-moved boundary grows to match it, adding occurrences on its own cadence and amount. Until-outward keeps the plan's DtStart (WithUntil), so its persistence key is stable and Save just replaces it; Start-outward moves the plan's Start earlier while keeping it on the same cadence (ReanchoredToStartOn — the same primitive StartOn uses) and can move DtStart, so that path deletes the old-key row first. A plan sharing BOTH outward-moved boundaries is grown on both in one rewrite. Skips a plan a truncation already owns this Run() (the rare edit moving one boundary out and the other in on the same plan) to avoid colliding on its key — that plan keeps its truncated shape, not yet extended. A no-op whenever _boundaryExtensions is null. Called from inside PerformSave, right after the two ApplyTruncations.</summary>
    private void ApplyBoundaryExtensionsIfNeeded()
    {
        if (_boundaryExtensions is not { } ext)
        {
            return;
        }

        var newStart = _proposedPattern.DatePattern.ActiveStart;
        var newUntil = _proposedPattern.DatePattern.Until;

        foreach (var plan in ext.PlansSharingOldStart.Concat(ext.PlansSharingOldUntil).DistinctBy(plan => plan.DatePattern.DtStart))
        {
            if (IsAlreadyOwnedByATruncation(plan))
            {
                continue;
            }

            var key = plan.DatePattern.DtStart;
            var extendsUntil = ext.PlansSharingOldUntil.Any(p => p.DatePattern.DtStart == key);
            var extendsStart = ext.PlansSharingOldStart.Any(p => p.DatePattern.DtStart == key);

            var grownPattern = plan.DatePattern;
            if (extendsUntil)
            {
                grownPattern = grownPattern.WithUntil(newUntil);
            }
            if (extendsStart)
            {
                grownPattern = grownPattern.ReanchoredToStartOn(newStart); // preserves the Until just set above
            }

            var extended = EarMarkPattern.Create(
                new EarMarkPatternOptions
                {
                    FinanceId = plan.FinanceId,
                    DatePattern = grownPattern,
                    Amount = plan.Amount,
                    StartingAllocation = plan.StartingAllocation,
                },
                _proposedPattern);

            if (extendsStart)
            {
                _repositories.EarMarkPatterns.Delete(plan.FinanceId, key); // ReanchoredToStartOn can move the key
            }
            _repositories.EarMarkPatterns.Save(extended);
        }
    }

    /// <summary>[CALC] Whether a plan is already being rewritten this Run() by one of the two truncations — so ApplyBoundaryExtensionsIfNeeded leaves it alone rather than colliding on its persistence key. True only in the rare edit that moves one of a plan's boundaries outward while the other moves inward.</summary>
    /// <param name="plan">The plan an extension is considering rewriting.</param>
    private bool IsAlreadyOwnedByATruncation(EarMarkPattern plan)
    {
        var key = plan.DatePattern.DtStart;
        return (_backTruncations is { } back && back.PlansExceedingNewUntil.Any(p => p.DatePattern.DtStart == key))
            || (_frontTruncations is { } front && front.PlansStartingBeforeNewStart.Any(p => p.DatePattern.DtStart == key));
    }

    /// <summary>[WRITES FILE] Carries out whatever Item C/D/E/F (planning/25 — the mechanism, the gap-folding rule, the retroactive-correction ruling, and the multiple-EarMarkPatterns ruling, respectively) decided. Real today for: a break-off with 0 or 1 existing EarMarkPattern (PerformSingleSuccessorBreakOff); a break-off that consolidates more than one (PerformMultiPlanBreakOff, forced or chosen); a single-plan retroactive correction's own narrowing (NarrowSurvivingPlanIfNeeded); a multi-plan retroactive correction's own in-place consolidation, forced or chosen (ConsolidateSurvivingPlansIfNeeded, backed by EarmarkConsolidation.Consolidate); and — new 2026-08-14 — that same retroactive-correction side's own amount-only scaling, when the plans are kept separate rather than consolidated (ScaleSurvivingPlansIfNeeded, backed by the new EarmarkScaling.Scale). Still TODO: keeping multiple plans separate on the BREAK-OFF side at all (with or without scaling), and keeping them separate on the retroactive-correction side for a start_date change specifically (only the amount-only case is resolved).</summary>
    /// <summary>[WRITES FILE] Carries out whatever DetermineChainConditionsIfApplicable/the confirmation decided for planning/27's own Phase 1 — resolves the chain boundary (stay linked, via BreakOffFactory.ExtendStart/ExtendUntil, or left broken), the Amount/shape cascade, and the trivial-fields cascade against the rest of the same-Source chain. A no-op whenever _chainContext is null (brand-new pattern) or none of TouchesChainBoundary/ChangeCanCascade/TrivialFieldsCanCascade are true — and whenever it DOES run, _proposedPattern was saved verbatim under _financeId by the PerformSave call just before it (either a plain non-Critical edit, or an EARLIER-segment edit — Critical but with a later segment — both of which PerformSave writes in place); only a CURRENT-segment break-off skips that save, and its chain conditions are all suppressed there (no successor to reach), so this method never runs for it. Absorbing a neighbor here means deleting its WHOLE FinancialPattern row, plus every EarMarkPattern and ManualEarmark under its own FinanceId (EarMarkPatternRepository.Delete(financeId)'s own existing two-table cascade) — the FinancialPattern-level absorb this document's own text describes as "genuinely orphaning everything," unlike the EarMarkPattern-chain case where nothing is orphaned.</summary>
    private void PerformChainChangesIfApplicable()
    {
        if (_chainContext is not { } context || (!TouchesChainBoundary && !ChangeCanCascade && !TrivialFieldsCanCascade))
        {
            return;
        }

        var toSave = new Dictionary<int, FinancialPattern>();
        var toDelete = new List<int>();

        // A chain neighbor stretched by keeping the boundary linked, if any:
        // ExtendStart grows a predecessor's Until forward, ExtendUntil grows a
        // successor's Start backward. Once every finance-row change here is saved,
        // the neighbor's own savings plan is grown to match the stretch (#5), the
        // reverse-break-off migrates any dropped occurrences under a predecessor
        // (piece 2), and any earmark patterns left mergeable under one finance_id
        // are silently joined (M1).
        FinancialPattern? extendedPredecessor = null;
        FinancialPattern? extendedSuccessor = null;

        // The later finance patterns this finance pattern's own Amount/schedule
        // change was carried forward onto, each as (pre-cascade, carried-forward)
        // — their own savings plans get brought back in line with the new figure
        // once the finance rows are saved (cross-boundary Q6), so a later finance
        // pattern isn't left saving toward the old one.
        var cascadeTouchedSuccessors = new List<(FinancialPattern Before, FinancialPattern After)>();

        var predecessors = context.OtherPatterns.Where(pattern => pattern.DatePattern.ActiveStart < context.Saved.DatePattern.ActiveStart).ToList();
        var successors = context.OtherPatterns.Where(pattern => pattern.DatePattern.ActiveStart > context.Saved.DatePattern.ActiveStart).ToList();

        if (TouchesChainBoundary && UserChoseStayLinked)
        {
            if (context.Saved.DatePattern.ActiveStart != _proposedPattern.DatePattern.ActiveStart && predecessors.Count > 0)
            {
                var result = BreakOffFactory.ExtendStart(_proposedPattern, predecessors, _proposedPattern.DatePattern.ActiveStart);
                foreach (var absorbed in result.Absorbed)
                {
                    toDelete.Add(absorbed.FinanceId);
                }

                if (result.AdjustedNeighbor is { } adjusted)
                {
                    toSave[adjusted.FinanceId] = adjusted;
                    extendedPredecessor = adjusted; // stretched over the days this segment no longer reaches — the reverse-break-off's home
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
                    extendedSuccessor = adjusted; // stretched backward to stay contiguous — its plan grows to match (#5)
                }
            }
        }

        // Gated on UserChoseStayLinked too: breaking the chain leaves no forward
        // chain to carry the change onto (planning/28's "break ⇒ no forward chain
        // ⇒ Q4 gone"), matching the popup hiding this question under "let the
        // chain break." UserChoseStayLinked defaults true, so an amount-only edit
        // with no chain-boundary question still cascades as before.
        if (ChangeCanCascade && UserChoseCascadeForward && UserChoseStayLinked)
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

            var cascaded = BreakOffFactory.CascadeForward(_proposedPattern.DatePattern, _proposedPattern.Amount, stillStanding);
            for (var i = 0; i < cascaded.Count; i++)
            {
                // CascadeForward preserves stillStanding's order, so index i pairs
                // each successor's pre-cascade shape with its cascaded one.
                toSave[cascaded[i].FinanceId] = cascaded[i];
                cascadeTouchedSuccessors.Add((stillStanding[i], cascaded[i]));
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

        if (extendedPredecessor is { } predecessor)
        {
            FitNeighborPlansToItsNewSpan(predecessors.First(p => p.FinanceId == predecessor.FinanceId), predecessor); // grow or clamp the stretched predecessor's own plans
            PreserveDroppedOccurrencesUnderPredecessor(predecessor); // reverse-break-off (piece 2)
            JoinMergeableEarmarkPatterns(predecessor); // M1 — fold the stretched plan and the migrated one together when identical
        }

        if (extendedSuccessor is { } successor)
        {
            FitNeighborPlansToItsNewSpan(successors.First(s => s.FinanceId == successor.FinanceId), successor); // grow or clamp the stretched successor's own plans
            JoinMergeableEarmarkPatterns(successor); // M1
        }

        ReconcileCascadedSuccessorSavingsPlans(cascadeTouchedSuccessors);
    }

    /// <summary>[WRITES FILE] After the edited finance pattern's Amount/schedule change is carried forward onto the LATER finance patterns in its chain (the Q4 cascade), brings each of those later finance patterns' own savings plans back in line with the new figure — so a later finance pattern isn't left with earmark patterns still saving toward the old one (planning/28's cross-boundary Q6). Choosing to apply the change going forward is itself the consent, so nothing here asks again. A schedule change makes the later earmark patterns fold into one moved onto the new dates (EarmarkConsolidation.Consolidate) regardless of how many there are — the settled rule, since the dates move and each earmark pattern's own timing has to be worked out afresh, which one folded plan does correctly and re-dating several individually doesn't. An amount-only change on a single earmark pattern re-rates it proportionally in place (EarmarkScaling.Scale). An amount-only change on a later finance pattern funded by MORE THAN ONE earmark pattern is the cross-boundary Q6: the user's per-finance-pattern combine-or-keep-separate answer (_successorCombineChoices) picks between folding them into one and re-rating each proportionally.</summary>
    /// <param name="touched">Each later finance pattern the cascade reached, as (its pre-cascade form, its carried-forward form), in no particular order.</param>
    private void ReconcileCascadedSuccessorSavingsPlans(IReadOnlyList<(FinancialPattern Before, FinancialPattern After)> touched)
    {
        if (touched.Count == 0)
        {
            return;
        }

        var book = _requestForecast().Book;

        foreach (var (before, after) in touched)
        {
            var plans = book.EarMarkPatternsFor(after.FinanceId);
            if (plans.Count == 0)
            {
                continue; // no savings plan on this later finance pattern to bring in line
            }

            if (!before.DatePattern.HasSameShapeAs(after.DatePattern))
            {
                ConsolidateSuccessorPlans(after, plans); // the schedule moved the dates — the earmark patterns must fold into one, no keep-separate choice
                continue;
            }

            if (before.Amount == after.Amount)
            {
                continue; // dates and amount both unchanged for this later finance pattern
            }

            // Amount-only. A single earmark pattern is re-rated with no extra
            // question — choosing to apply the change going forward is the consent.
            // More than one is a real choice (cross-boundary Q6): "combine" folds
            // them into one; "keep separate" (the default) re-rates each
            // proportionally, the same Scale a single earmark pattern takes.
            if (plans.Count > 1 && _successorCombineChoices.TryGetValue(after.FinanceId, out var combine) && combine)
            {
                ConsolidateSuccessorPlans(after, plans);
                continue;
            }

            // The successor shares this chain's Source, so AffordabilityCeilingFor already omits its plans —
            // the re-rate is held to the room the whole chain's contributions free up.
            foreach (var scaled in EarmarkScaling.Scale(new ScaleRequest
            {
                Goal = after,
                PreviousGoalAmount = before.Amount,
                SurvivingPlans = plans,
            }, affordabilityCeiling: AffordabilityCeilingFor(after, ChangeKind.Implicit)))
            {
                _repositories.EarMarkPatterns.Save(scaled); // Scale keeps each plan's own (FinanceId, Start) — an in-place update
            }
        }
    }

    /// <summary>[WRITES FILE] Folds every earmark pattern funding one later finance pattern (reached by the carry-forward) into a single plan moved onto its new schedule (EarmarkConsolidation.Consolidate), deleting the old rows and saving the one replacement. Used when the carried-forward change alters the recurrence schedule, where keeping the earmark patterns separate isn't workable — each would need its own timing worked out afresh against the moved dates.</summary>
    /// <param name="goal">The later finance pattern in its carried-forward (new-schedule) form — what the folded plan is sized and validated against.</param>
    /// <param name="plans">Its existing earmark patterns, to fold into one.</param>
    private void ConsolidateSuccessorPlans(FinancialPattern goal, IReadOnlyList<EarMarkPattern> plans)
    {
        // goal shares this chain's Source, so AffordabilityCeilingFor already omits its plans — the folded
        // plan's ongoing contribution is held to the room the chain's contributions free up.
        var consolidated = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = plans,
            ManualEarmarksForThisGoal = _repositories.ManualEarmarks.GetAll().Where(earmark => earmark.FinanceId == goal.FinanceId).ToList(),
            AllPatterns = _repositories.FinancialPatterns.GetAll(),
            CurrentJar = JarOn(_requestForecast().AsOfDate, goal.FinanceId),
            Sizing = _chosenSizing,
            Spread = _chosenSpread,
        }, affordabilityCeiling: AffordabilityCeilingFor(goal, ChangeKind.Implicit)).ConsolidatedPlan;

        foreach (var plan in plans)
        {
            _repositories.EarMarkPatterns.Delete(plan.FinanceId, plan.DatePattern.DtStart);
        }
        _repositories.EarMarkPatterns.Save(consolidated);
    }

    /// <summary>[WRITES FILE] M2's cut-occurrence preservation (reverse break-off): when a Start move forward drops repeated occurrences from this goal's own earmark patterns AND a predecessor segment has just stretched to cover the days this segment no longer reaches, those occurrences survive as a new earmark pattern under that predecessor rather than being let go — the user's own conscious contribution schedule for those dates. Only ever fires for a FUTURE in-place Start move (a Critical, past-touching one breaks off instead), so the earmark patterns hold no accumulated real money yet: the migrated one starts at 0m and simply re-generates the dropped occurrences under the predecessor's finance_id. A dropped stretch with no occurrence is let go (nothing to preserve). ASSUMPTION (M2 piece 2, to compose with later pieces): the predecessor's OWN earmark pattern is not also stretched over those days (piece 3 — extend-outward), so the migrated one sits contiguously after it with no overlap to merge (M1); once those land this joins them. Reads the original (pre-clamp) earmark patterns from _frontTruncations, captured before any save, and runs last so the predecessor's own stretched row is already persisted (the migrated one must validate against it).</summary>
    /// <param name="predecessor">The predecessor segment, freshly stretched to cover the days this segment no longer reaches.</param>
    private void PreserveDroppedOccurrencesUnderPredecessor(FinancialPattern predecessor)
    {
        if (_frontTruncations is not { } front)
        {
            return;
        }

        var dayBeforeNewStart = _proposedPattern.DatePattern.ActiveStart.AddDays(-1);

        foreach (var droppedPlan in front.PlansStartingBeforeNewStart)
        {
            var droppedOccurrences = droppedPlan.DatePattern.GetOccurrences(droppedPlan.DatePattern.ActiveStart, dayBeforeNewStart);
            if (droppedOccurrences.Count == 0)
            {
                continue; // the dropped span held no occurrence — nothing to preserve
            }

            var migrated = EarMarkPattern.Create(
                new EarMarkPatternOptions
                {
                    FinanceId = predecessor.FinanceId,
                    DatePattern = droppedPlan.DatePattern.WithUntil(dayBeforeNewStart), // keep its Start and cadence, ending the day before this segment's new start
                    Amount = droppedPlan.Amount,
                    StartingAllocation = 0m,
                },
                predecessor);
            _repositories.EarMarkPatterns.Save(migrated);
        }
    }

    /// <summary>[WRITES FILE] Brings a stretched chain neighbor's OWN earmark patterns in line with its new span, so keeping the boundary linked leaves the neighbor's savings valid and covering the right range (3.11.2.a2) — the chain-side-effect twin of the directly-edited goal's own boundary work. If the neighbor GREW (a predecessor's end forward, or a successor's start backward), each earmark pattern that shared the moved boundary grows to match (WithUntil / ReanchoredToStartOn). If it SHRANK (an end pulled back, or a start pushed forward), each earmark pattern now falling outside is clamped to the new span (PatternTruncation.EndOn / StartOn), or deleted outright when it's now entirely outside. Silent — a required consequence of the user's own "keep it linked" choice, exactly as the directly-edited goal's own boundary is kept valid. A clamped-forward start absorbs no balance (0m, matching the directly-edited front-truncation), which under-counts a non-future neighbor whose dropped days had already accrued — a known limit of that shared shape, not a crash.</summary>
    /// <param name="oldNeighbor">The neighbor before the boundary move.</param>
    /// <param name="newNeighbor">The neighbor after — what the adjusted earmark patterns are sized and validated against.</param>
    private void FitNeighborPlansToItsNewSpan(FinancialPattern oldNeighbor, FinancialPattern newNeighbor)
    {
        var plans = _requestForecast().Book.EarMarkPatternsFor(newNeighbor.FinanceId);
        var newSpan = newNeighbor.DatePattern;

        // The neighbor's end moved.
        if (newSpan.Until > oldNeighbor.DatePattern.Until)
        {
            // Grew forward — grow each plan that shared the old end.
            foreach (var plan in plans.Where(plan => plan.DatePattern.Until == oldNeighbor.DatePattern.Until))
            {
                _repositories.EarMarkPatterns.Save(EarMarkPattern.Create(
                    new EarMarkPatternOptions
                    {
                        FinanceId = plan.FinanceId,
                        DatePattern = plan.DatePattern.WithUntil(newSpan.Until),
                        Amount = plan.Amount,
                        StartingAllocation = plan.StartingAllocation,
                    },
                    newNeighbor));
            }
        }
        else if (newSpan.Until < oldNeighbor.DatePattern.Until)
        {
            // Pulled back — clamp each plan that now ends past it, or delete one that's now entirely past it.
            foreach (var plan in plans.Where(plan => plan.DatePattern.Until > newSpan.Until))
            {
                if (plan.DatePattern.ActiveStart > newSpan.Until)
                {
                    _repositories.EarMarkPatterns.Delete(plan.FinanceId, plan.DatePattern.DtStart);
                    continue;
                }

                _repositories.EarMarkPatterns.Save(PatternTruncation.EndOn(newNeighbor, plan, newSpan.Until).Plan!);
            }
        }

        // The neighbor's start moved.
        if (newSpan.ActiveStart < oldNeighbor.DatePattern.ActiveStart)
        {
            // Grew backward — grow each plan that shared the old start.
            foreach (var plan in plans.Where(plan => plan.DatePattern.ActiveStart == oldNeighbor.DatePattern.ActiveStart))
            {
                var grown = EarMarkPattern.Create(
                    new EarMarkPatternOptions
                    {
                        FinanceId = plan.FinanceId,
                        DatePattern = plan.DatePattern.ReanchoredToStartOn(newSpan.ActiveStart),
                        Amount = plan.Amount,
                        StartingAllocation = plan.StartingAllocation,
                    },
                    newNeighbor);
                _repositories.EarMarkPatterns.Delete(plan.FinanceId, plan.DatePattern.DtStart); // ReanchoredToStartOn can move the key
                _repositories.EarMarkPatterns.Save(grown);
            }
        }
        else if (newSpan.ActiveStart > oldNeighbor.DatePattern.ActiveStart)
        {
            // Pushed forward — clamp each plan that now begins before it, or delete one that's now entirely before it.
            foreach (var plan in plans.Where(plan => plan.DatePattern.ActiveStart < newSpan.ActiveStart))
            {
                if (plan.DatePattern.Until < newSpan.ActiveStart)
                {
                    _repositories.EarMarkPatterns.Delete(plan.FinanceId, plan.DatePattern.DtStart);
                    continue;
                }

                var clamped = PatternTruncation.StartOn(plan, newNeighbor, newSpan.ActiveStart, absorbedBalance: 0m);
                _repositories.EarMarkPatterns.Delete(plan.FinanceId, plan.DatePattern.DtStart); // StartOn can move the key
                _repositories.EarMarkPatterns.Save(clamped);
            }
        }
    }

    /// <summary>[WRITES FILE] M1's silent join (#6), applied to one goal: folds any two earmark patterns under it that can merge with no visible change (EarMarkPattern.CanJoinWithoutConsequence — same amount, same cadence, their occurrences a clean union) into one, repeating until none remain. Runs after a stretch has grown or re-homed plans under this goal (#5 + the reverse-break-off), where the neighbor's own grown plan and a migrated one can end up identical and covering the same span. A genuine difference (different amount or cadence) is left as two separate plans — a valid concurrent earmark patterns shape, not folded behind the user's back.</summary>
    /// <param name="goal">The goal whose earmark patterns to fold.</param>
    private void JoinMergeableEarmarkPatterns(FinancialPattern goal)
    {
        var plans = _repositories.EarMarkPatterns.GetAll().Where(plan => plan.FinanceId == goal.FinanceId).ToList();
        var originalKeys = plans.Select(plan => plan.DatePattern.DtStart).ToList();

        bool folded;
        do
        {
            folded = false;
            for (var i = 0; i < plans.Count && !folded; i++)
            {
                for (var j = i + 1; j < plans.Count; j++)
                {
                    if (EarMarkPattern.CanJoinWithoutConsequence(plans[i], plans[j]))
                    {
                        var joined = plans[i].JoinedWith(plans[j], goal);
                        plans.RemoveAt(j);
                        plans.RemoveAt(i);
                        plans.Add(joined);
                        folded = true;
                        break;
                    }
                }
            }
        }
        while (folded);

        if (plans.Count == originalKeys.Count)
        {
            return; // nothing merged — leave storage untouched
        }

        // Delete every original row first (so a fold landing on an existing key
        // can't collide), then save the folded set.
        foreach (var key in originalKeys)
        {
            _repositories.EarMarkPatterns.Delete(goal.FinanceId, key);
        }
        foreach (var plan in plans)
        {
            _repositories.EarMarkPatterns.Save(plan);
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
            var proposal = AllocationPlanProposer.Propose(bill, allPatterns, forecast.AsOfDate,
                carriedOverJarBalance: carriedOverJarBalance,
                startingEarmarkCeiling: StartingEarmarkCeilingFor(bill, ChangeKind.Implicit),
                ongoingRateCeiling: OngoingRateCeilingFor(bill, ChangeKind.Implicit));

            // Same "read before write" reasoning FindOrphanedManualEarmarkDates'
            // own call sites elsewhere already follow — safe here because
            // nothing has touched this bill's own plan or ManualEarmarks yet,
            // even though the income's own save has already happened.
            var otherExistingPlans = forecast.Book.EarMarkPatternsFor(oldPlan.FinanceId)
                .Where(plan => plan.DatePattern.DtStart != oldPlan.DatePattern.DtStart)
                .ToList();
            var finalCoverage = otherExistingPlans.Append(proposal.Plan).ToList();
            var orphanedDates = _repositories.FindOrphanedManualEarmarkDates(finalCoverage, oldPlan.FinanceId);

            _repositories.EarMarkPatterns.Delete(oldPlan.FinanceId, oldPlan.DatePattern.DtStart);
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

    /// <summary>[WRITES FILE] Carries out whatever Item C/D/E/F (planning/25 — the mechanism, the gap-folding rule, the retroactive-correction ruling, and the multiple-EarMarkPatterns ruling. Forward-only (planning/28): carries out the break-off a Critical edit triggers — a single freshly-proposed successor (PerformSingleSuccessorBreakOff), or one combined successor when more than one EarMarkPattern already funds the goal (PerformMultiPlanBreakOff). A non-Critical edit does nothing here. Keeping multiple plans separate through a break-off is still unbuilt (header TODO item 2).</summary>
    private void PerformImplicitEarmarkChanges()
    {
        // Break off only for a Critical edit of the CURRENT segment. A Critical
        // edit of an EARLIER segment (one with a later segment) is saved in place
        // by PerformSave and cascaded forward by PerformChainChangesIfApplicable,
        // never broken off — forward-only's "open the earliest segment, change
        // flows forward from there."
        if (!IsChangeCritical || _chainHasSuccessor)
        {
            return;
        }

        // Forward-only (planning/28, 2026-08-18): a Critical edit — one that
        // reaches an already-occurred occurrence — always breaks off from
        // today. The retroactive "correct it everywhere" path was removed,
        // along with its narrowing (NarrowSurvivingPlanIfNeeded), in-place
        // consolidation (ConsolidateSurvivingPlansIfNeeded) and amount-only
        // scaling (ScaleSurvivingPlansIfNeeded).
        //
        // Item F's own question, honored at last (2026-08-27): with more than
        // one existing plan, the user's "keep them separate / combine them into
        // one" pick decides which break-off runs. Combining folds every plan
        // into one freshly-proposed successor; keeping them separate gives the
        // successor one plan per surviving plan, each continuing its own rate.
        // A shape/start change (ConsolidationNeeded) forces the combine, since
        // the plans can't keep their own occurrence dates onto a differently
        // shaped successor — same forced case the popup announces rather than asks.
        if (HasMultipleEarmarkPatterns)
        {
            if (ConsolidationNeeded || UserChoseCombinePlans)
            {
                PerformMultiPlanBreakOff();
            }
            else
            {
                PerformMultiPlanKeepSeparateBreakOff();
            }

            return;
        }

        PerformSingleSuccessorBreakOff();
    }

    /// <summary>[CALC] The successor schedule every break-off path builds the same way: the proposed edit's own recurrence shape and end date, starting exactly on the cut — or, for a Weekly pattern, re-anchored to the nearest date on or after the cut that actually preserves its own cadence, with ActiveFrom carrying "active from the cut" separately when that lands later. See this method's own body comment for why Weekly needs the second path at all.</summary>
    /// <param name="cutDate">Where the successor's schedule should start (or count as active from, for the Weekly case below).</param>
    private RecurrenceRuleOptions BuildSuccessorSchedule(DateOnly cutDate)
    {
        var reference = _proposedPattern.DatePattern;

        // Monthly/Yearly always carry an explicit ByMonthDay — RecurrenceRuleEditor
        // never shows a ByDay picker for those — so ical.net always finds
        // the right day regardless of where Start itself falls. Nothing to
        // preserve here; DtStart = cutDate is already correct, and every
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
                DtStart = cutDate,
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
        // planning/25's own text had floated as an alternate reading. The sharp
        // edge this comment used to defer — the saved pattern's own Start IS
        // today, so CutDate == Start and BreakOffFactory.BreakOff throws — is now
        // handled upstream (2026-09-05): DetermineConditions treats an edit with
        // no more than one day of history to preserve as non-Critical, a plain
        // in-place replace, so this break-off path is never reached for it. The
        // CutDate > ActiveStart invariant therefore always holds by the time we
        // get here; BreakOffFactory still enforces it as a genuine domain guard.
        var cutDate = forecast.AsOfDate;

        var predecessorPlan = forecast.Book.EarMarkPatternsFor(_financeId).SingleOrDefault();

        // planning/25's Item G — matched back to its own full
        // ProposedAllocationPlan (StartingEarmark included) by reference. When
        // the user takes the default rather than picking explicitly (or sets a
        // stray value that matches nothing), fall back to the "Recommended"
        // candidate this Run() built — which is affordability-capped — rather
        // than to BreakOffFactory's own uncapped re-derivation, so the plan
        // saved is the one the picker actually showed. Null only when no
        // candidate was offered at all (no existing plan to draw one from), in
        // which case BreakOffFactory computes its own default.
        var chosenSuccessorPlan = (_planShapeCandidates
            .FirstOrDefault(candidate => candidate.Plan.Plan == ChosenPlanShape)
            ?? _planShapeCandidates.FirstOrDefault(candidate => candidate.Label == "Recommended"))?.Plan;

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
            // Only the fallback (no candidate to fall back to — no existing plan) path uses these. Size the
            // successor's fresh plan against the predecessor as a proxy target: it shares the successor's
            // mandatory-ness, priority, Source and end date, so the tier and window come out the same.
            SuccessorStartingEarmarkCeiling = chosenSuccessorPlan is null ? CeilingOmitting(_financeId, saved, ChangeKind.Suggestion, startingEarmark: true) : null,
            SuccessorOngoingRateCeiling = chosenSuccessorPlan is null ? CeilingOmitting(_financeId, saved, ChangeKind.Suggestion, startingEarmark: false) : null,
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
        // already does, and the same fall-back to the affordability-capped
        // "Recommended" candidate when the user takes the default rather than
        // picking explicitly, so the saved plan matches what the picker showed.
        // Null only when no candidate was offered at all (a genuinely
        // concurrent set), in which case BreakOff computes its own default.
        var chosenSuccessorPlan = (_planShapeCandidates
            .FirstOrDefault(candidate => candidate.Plan.Plan == ChosenPlanShape)
            ?? _planShapeCandidates.FirstOrDefault(candidate => candidate.Label == "Recommended"))?.Plan;

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
            // Only the fallback (a genuinely concurrent set — no candidate) path uses these; same predecessor-
            // as-proxy-target reasoning as PerformSingleSuccessorBreakOff.
            SuccessorStartingEarmarkCeiling = chosenSuccessorPlan is null ? CeilingOmitting(_financeId, saved, ChangeKind.Suggestion, startingEarmark: true) : null,
            SuccessorOngoingRateCeiling = chosenSuccessorPlan is null ? CeilingOmitting(_financeId, saved, ChangeKind.Suggestion, startingEarmark: false) : null,
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

    /// <summary>[WRITES FILE] The more-than-one-existing-plan case of Item C's break-off when Item F's question resolves to keeping them separate (the user's pick, only reachable when the schedule/start isn't forcing consolidation): every surviving plan is truncated the day before the cut, and the successor gets ONE plan per surviving plan — each continuing its own rate at its own cadence from the cut forward, rather than folding into one. The finance_id's one combined jar balance carries on the first successor plan. Otherwise identical to PerformMultiPlanBreakOff — see that method for the cut-date note.</summary>
    private void PerformMultiPlanKeepSeparateBreakOff()
    {
        var saved = GetSavedPatternOrThrow();
        var forecast = _requestForecast();
        var cutDate = forecast.AsOfDate;

        var predecessorPlans = forecast.Book.EarMarkPatternsFor(_financeId);

        var result = BreakOffFactory.BreakOffKeepingPlansSeparate(new MultiPlanBreakOffRequest
        {
            Predecessor = saved,
            PredecessorPlans = predecessorPlans,
            CutDate = cutDate,
            SuccessorFinanceId = forecast.Book.NextFinanceId(),
            SuccessorAmount = _proposedPattern.Amount,
            SuccessorSchedule = BuildSuccessorSchedule(cutDate),
            CarriedOverJarBalance = JarBalanceOn(cutDate, _financeId),
            AllPatterns = forecast.Book.AllFinancialPatterns(),
        });

        // Same reasoning as PerformSingleSuccessorBreakOff's own note: the new
        // successor segment is the current one now, wherever the plans landed.
        _navigationFinanceId = result.Successor.FinanceId;

        // If the user took the offer to correct the funding (the nested
        // keep-separate question), re-rate every successor plan proportionally so
        // they together meet the new amount; otherwise each keeps its own raw
        // rate, over/underfunding and all. Recomputed here against the real
        // successor rather than reusing the dry-run's throwaway one.
        var successorPlans = result.SuccessorPlans;
        if (UserChoseToAdjustKeptSeparatePlans)
        {
            var ceiling = AffordabilityCeilingFor(result.Successor, ChangeKind.Implicit);
            successorPlans = EarmarkScaling.ScaleToMeetGoal(result.Successor, result.SuccessorPlans, affordabilityCeiling: ceiling).ScaledPlans;
        }

        _repositories.FinancialPatterns.Save(result.Predecessor, _accountId);
        _repositories.FinancialPatterns.Save(result.Successor, _accountId);

        foreach (var truncatedPlan in result.PredecessorPlans)
        {
            _repositories.EarMarkPatterns.Save(truncatedPlan);
        }

        foreach (var successorPlan in successorPlans)
        {
            _repositories.EarMarkPatterns.Save(successorPlan);
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

    /// <summary>[READS FILE] Every ManualEarmark under a finance_id that no plan in the given final coverage set still covers (RecurrenceRule.ActiveSpanContains — the same check ManualEarmark.Create/ManualEarmarkRepository.GetAll already use to validate one). Shared by both save-confirmation wrappers: the real, about-to-be-deleted list (against the actual post-save set) and a preview count (against a hypothetical set, nothing deleted).</summary>
    /// <param name="finalCoverage">Every EarMarkPattern expected to exist under financeId once this save (real or hypothetical) completes.</param>
    /// <param name="financeId">Which goal's own ManualEarmarks to check.</param>
    public IReadOnlyList<DateOnly> FindOrphanedManualEarmarkDates(IReadOnlyList<EarMarkPattern> finalCoverage, int financeId) =>
        ManualEarmarks.GetAll()
            .Where(earmark => earmark.FinanceId == financeId)
            .Where(earmark => !finalCoverage.Any(plan => plan.DatePattern.ActiveSpanContains(earmark.Date)))
            .Select(earmark => earmark.Date)
            .ToList();
}

// What the confirmation popup needs to render itself — the plain-language
// description and the list of rows (each a question or announcement) it draws,
// plus the plan-shape candidates that don't have a row of their own yet. Built
// fresh per Run() call, right before the delegate fires — see
// FinancePatternSaveConfirmation.BuildConfirmationRequest. The trigger booleans
// and warning strings that decide which rows exist live on the internal
// RowInputs now (ConfirmationRowBuilder.cs), not here.
public sealed record ImplicitChangeConfirmationRequest
{
    // One row per question/announcement the popup shows, in the order it shows
    // them (most-vital first), built by ConfirmationRowBuilder.BuildRows.
    // The popup renders exactly this — it never re-derives which questions apply.
    public IReadOnlyList<ConfirmationRow> Rows { get; init; } = [];

    // planning/25's Item G — empty whenever there's nothing to choose
    // between (most edits, multi-plan goals, or a break-off with no
    // existing plan to draw an alternative shape from). A real popup shows
    // each candidate's own Label and hands back whichever one's own Plan
    // the user picked as ConfirmationOutcome.ChosenPlanShape.
    public required IReadOnlyList<FinancePatternSaveConfirmation.PlanShapeCandidate> PlanShapeCandidates { get; init; }

    public required string Description { get; init; }
}
