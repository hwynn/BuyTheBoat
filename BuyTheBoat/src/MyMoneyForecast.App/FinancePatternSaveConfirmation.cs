using MyMoneyForecast.Domain;
using MyMoneyForecast.Persistence;

namespace MyMoneyForecast.App;

// Orchestrates everything that has to happen between a Save button being
// clicked on the Expense form and the FinancialPattern actually landing in
// storage — redesign/MyMoneyForecast/planning/25-editing-patterns-with-history.md's
// Items B through F. Pulled into its own class/file specifically so this
// project's simplest case (ExpenseFormPanel.Save, MainWindow's own
// ExpenseForm.PatternSaved callback) doesn't have to carry the
// confirmation-and-consequence logic this phase adds — see the TODOs left at
// both of those sites.
//
// STATUS (2026-08-12): a real, working confirmation now exists —
// EditingHistoryConfirmationWindow, shown through the ConfirmImplicitChanges
// delegate — but it's deliberately minimal (plain WPF controls, not the
// styled cards in planning/mockups/editing-history-confirmation-mockups.html),
// covers only the Item E/F questions (the separate Concerning/suggestions
// popup, AskForSuggestions, is still its own no-op TODO), and doesn't yet
// name the specific amount/date a retroactive correction would orphan
// (Item E's own "show the consequence" — see BuildDescription's own TODO).
// When ConfirmImplicitChanges isn't wired at all (most tests, and any host
// that hasn't connected a real popup), DefaultConfirmationAnswer supplies
// the same safest, least-destructive answers the old placeholder logic used
// to hardcode.
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
// Still TODO: keeping multiple plans separate on the BREAK-OFF side at all
// (with or without scaling — see PerformImplicitEarmarkChanges' own
// comment), and keeping them separate on the retroactive-correction side for
// a start_date change specifically (each plan would still need its own
// individual narrowing, NarrowSurvivingPlanIfNeeded's own shape, applied
// per-plan rather than to just one — not designed, not built).
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
// from BreakOffFactory), income (no jar at all), and the
// keep-plans-separate TODO's current safe no-op (nothing persisted, not
// something wrong — that test will need updating once that TODO is
// actually built), and — now that ConfirmImplicitChanges is real — the
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
    // comments. Empty means there's nothing to choose between (most edits,
    // or a break-off with no existing plan to draw an alternative shape
    // from) — Item G's own scope is exactly one existing plan; a goal with
    // more than one gets no candidates here at all, deliberately (author,
    // 2026-08-14: the concurrent case needs its own not-yet-built mechanism
    // to continue both plans in unison, and shouldn't also offer a shape
    // choice on top of that complexity).
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

    // Invoked once Run() decides navigation should happen — matches this
    // project's existing PatternSaved/AccountSaved/ManualEarmarksSaved
    // callback idiom (set by MainWindow) rather than giving this class a
    // direct reference to EarmarkFormPanel or the tab control. Null means
    // "open a blank Earmark form" (mirrors MainWindow's own existing
    // fallback when a pattern has no plan yet). Never invoked when
    // UserSkippedPlanning is true.
    public Action<EarMarkPattern?>? NavigateToEarmarkForm { get; set; }

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
        DetermineConditions();

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
        // just above, and mutually exclusive with _consolidationPlan the
        // same way: this needs exactly one surviving plan, that needs more
        // than one.
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
        if (IsChangeCritical || HasMultipleEarmarkPatterns)
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
        }

        PerformSave();
        PerformImplicitEarmarkChanges();

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
        ConsolidationNeeded = HasMultipleEarmarkPatterns && _recurrenceShapeChanged;

        ChangeWarrantsSuggestions = forecast.PlanHealthStates
            .FirstOrDefault(health => health.FinanceId == _financeId)
            ?.IsWorthWarningAbout ?? false;
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
            // TODO: newActiveStart falls inside the plan's own existing
            // lead-in (ActiveFrom < Start) rather than past its literal
            // Start — PatternTruncation.StartOn only handles moving Start
            // itself forward, not narrowing a lead-in alone. Rare (a goal
            // whose plan was already saving ahead of it); left unhandled
            // rather than guessed at.
            return;
        }

        if (newActiveStart < forecast.AsOfDate)
        {
            // TODO: this is arguably the MORE common real-world trigger for
            // Item E, not an edge case — "actually this started earlier
            // than today's date" usually means earlier than today itself,
            // not just earlier than the plan's own current start. But
            // JarBalanceOn (via GetTimeline) can only read balances from
            // AsOfDate forward — nothing before the as-of date is a locked
            // ledger; W4/13a's own divergence (this project computes each
            // day fresh rather than storing history), not something this
            // class invented. Absorbing a balance here today would silently
            // read 0m regardless of what the jar actually held (JarBalanceOn's
            // own documented fallback for a day its timeline doesn't reach)
            // — real money would read as having vanished. Left unhandled
            // rather than absorbing a number known to be wrong; needs either
            // a way to ask for a forecast as of a past date, or a different
            // source for this specific number.
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

    /// <summary>[READS FILE] planning/25's Item G: works out which alternative plan shapes — beyond AllocationPlanProposer.Propose's own default — are genuinely available for this break-off's successor, stored in _planShapeCandidates for BuildConfirmationRequest to show and PerformSingleSuccessorBreakOff to apply whichever gets chosen. Scoped to exactly one existing plan (see _planShapeCandidates' own field comment for why more than one gets nothing here at all). Runs regardless of what UserChooseAlterPast will turn out to be — not known yet when this runs, same "compute eagerly, apply conditionally" shape DetermineConsolidationPlanIfApplicable already uses — and simply goes unused if the retroactive-correction side is chosen instead, where no fresh plan ever gets proposed. internal for the same reason its siblings are — so a test can call this directly ahead of PerformSingleSuccessorBreakOff.</summary>
    internal void DeterminePlanShapeCandidatesIfApplicable()
    {
        if (!IsChangeCritical || HasMultipleEarmarkPatterns)
        {
            return;
        }

        var forecast = _requestForecast();
        var existingPlan = forecast.Book.EarMarkPatternsFor(_financeId).SingleOrDefault();
        if (existingPlan is null)
        {
            return; // nothing to draw an alternative shape from — only the default exists
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

    /// <summary>[CALC] Builds what ConfirmImplicitChanges needs to render the Item B/C/E/F confirmation — everything DetermineConditions just worked out, plus a plain-language description of what changed. TODO: doesn't yet name the specific amount/date that would be orphaned by a retroactive correction (Item E's own "show the consequence, not just a yes/no" — mockups/editing-history-confirmation-mockups.html, Popup 1 · B) — the generic description below is a simpler first cut.</summary>
    private ImplicitChangeConfirmationRequest BuildConfirmationRequest() => new()
    {
        IsChangeCritical = IsChangeCritical,
        HasMultipleEarmarkPatterns = HasMultipleEarmarkPatterns,
        ConsolidationNeeded = ConsolidationNeeded,
        IsAmountOnlyChange = _isAmountOnlyChange,
        PlanShapeCandidates = _planShapeCandidates,
        Description = BuildDescription(),
    };

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

        // TODO: more than one EarMarkPattern survives — this is exactly the
        // existing FinancialPatternPickerWindow-style disambiguation popup's
        // own job, now meant to be incorporated here rather than fired
        // separately (author, this session). Not built — the first match is
        // a placeholder, not a real choice.
        return savingsPlan[0];
    }

    /// <summary>[UI] The Concerning popup — a PlanHealthState-sourced problem summary plus the suggest-a-fix-or-not choice. TODO: no popup exists yet — a no-op, which has the same effect as the user always choosing "I'll handle it myself": nothing gets pre-filled, but the passive PlanHealthState warning on the Earmark form is still there regardless. See mockups/editing-history-confirmation-mockups.html, Popup 2 · A.</summary>
    private void AskForSuggestions()
    {
        // TODO
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
    private void PerformImplicitEarmarkChanges()
    {
        if (IsChangeCritical && !UserChooseAlterPast)
        {
            if (HasMultipleEarmarkPatterns)
            {
                if (ConsolidationNeeded || UserChooseConsolidation)
                {
                    PerformMultiPlanBreakOff();
                    return;
                }

                // TODO: the user (once a real popup can ask) chose to keep
                // the existing plans SEPARATE rather than consolidate them —
                // a genuinely different, more complex mechanism than
                // PerformMultiPlanBreakOff: each existing plan would need
                // its own successor surviving under the SAME new finance_id
                // (F27 already allows more than one EarMarkPattern per
                // finance_id), not one combined fresh plan. Not designed,
                // not built. Nothing is saved for either the FinancialPattern
                // or the EarMarkPattern side in this branch — PerformSave's
                // own guard above already skips the plain, wrong-under-history
                // save, so this persists nothing at all rather than something
                // incorrect. Unreachable today anyway: AskForGuidanceOnImplicitChanges'
                // placeholder always answers UserChooseConsolidation = false,
                // and this branch only differs from PerformMultiPlanBreakOff
                // when ConsolidationNeeded is ALSO false — so today this only
                // fires for a multi-plan, non-recurrence-shape Critical edit.
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

            // Not scaling is a genuine, correct no-op here whenever the
            // change really was amount-only: each surviving plan's own dates
            // and rate are still exactly what they were, still valid against
            // the unchanged-in-that-respect goal — there's nothing to save.
            // TODO: for a start_date change instead, that's not true — each
            // surviving plan would still need its own narrowing (Item E's
            // shape, NarrowSurvivingPlanIfNeeded) applied individually rather
            // than being folded into one. Not designed, not built — the
            // break-off side's own identical TODO (above) explains why this
            // is materially harder than consolidating.
            return;
        }

        // TODO: UserChoseScalePatterns on the BREAK-OFF side (the "keep
        // separate" TODO above, in the IsChangeCritical && !UserChooseAlterPast
        // branch) — scaling alone doesn't help there, since keeping plans
        // separate under a break-off's new finance_id already needs each one
        // reborn as its own successor with its own share of the jar balance;
        // scaling would just be one more field on top of a mechanism that
        // doesn't exist yet.
    }

    /// <summary>[CALC] The successor schedule every break-off path builds the same way: the proposed edit's own recurrence shape and end date, starting exactly on the cut.</summary>
    /// <param name="cutDate">Where the successor's schedule should start.</param>
    private RecurrenceRuleOptions BuildSuccessorSchedule(DateOnly cutDate) => new()
    {
        Frequency = _proposedPattern.DatePattern.Frequency,
        Interval = _proposedPattern.DatePattern.Interval,
        ByDay = _proposedPattern.DatePattern.ByDay,
        ByMonthDay = _proposedPattern.DatePattern.ByMonthDay,
        Start = cutDate,
        Until = _proposedPattern.DatePattern.Until,
    };

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

    /// <summary>[WRITES FILE] The more-than-one-existing-plan case of Item C's break-off, when Item F's own question resolves to consolidating them (forced by ConsolidationNeeded, or chosen via UserChooseConsolidation): every surviving plan is truncated, and the successor still gets exactly one freshly-proposed plan, seeded from the finance_id's one combined jar balance. Otherwise identical to PerformSingleSuccessorBreakOff — see that method for the cut-date note.</summary>
    private void PerformMultiPlanBreakOff()
    {
        var saved = GetSavedPatternOrThrow();
        var forecast = _requestForecast();
        var cutDate = forecast.AsOfDate;

        var predecessorPlans = forecast.Book.EarMarkPatternsFor(_financeId);

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
    public required bool IsAmountOnlyChange { get; init; }

    // planning/25's Item G — empty whenever there's nothing to choose
    // between (most edits, multi-plan goals, or a break-off with no
    // existing plan to draw an alternative shape from). A real popup shows
    // each candidate's own Label and hands back whichever one's own Plan
    // the user picked as ImplicitChangeConfirmationAnswer.ChosenPlanShape.
    public required IReadOnlyList<FinancePatternSaveConfirmation.PlanShapeCandidate> PlanShapeCandidates { get; init; }

    public required string Description { get; init; }
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
}
