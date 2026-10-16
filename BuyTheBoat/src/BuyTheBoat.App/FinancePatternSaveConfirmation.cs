using BuyTheBoat.Domain;
using BuyTheBoat.Persistence;

namespace BuyTheBoat.App;

/// <summary>
/// Everything that happens between clicking Save on the Expense form and the FinancialPattern landing in storage: the confirmations for editing a pattern whose history has already started.
/// Kept in its own class so ExpenseFormPanel.Save and MainWindow's PatternSaved callback stay simple.
/// One instance is created per Save click (either button).
/// </summary>
/// <remarks>
/// The confirmation is EditingHistoryConfirmationWindow, shown through the ConfirmImplicitChanges delegate; when that isn't wired (most tests), DefaultOutcome supplies the safest answers.
/// A Critical edit, one reaching an occurrence that already happened, always breaks off forward from today; there's no "apply to the past" path.
/// PerformImplicitEarmarkChanges carries out that break-off: a newly proposed successor (PerformSingleSuccessorBreakOff), or, when more than one plan funds the goal, one combined successor (PerformMultiPlanBreakOff) or the plans kept separate (PerformMultiPlanKeepSeparateBreakOff), with a shape or start change forcing the combine.
/// After every save, whatever was asked, two checks keep each plan's span inside its goal's (3.11.2.a2): the back truncations for Until and the front truncations for Start.
/// FinancePatternSaveConfirmationTests drives Run() with real repositories and a real forecast.
/// </remarks>
public sealed class FinancePatternSaveConfirmation
{
    private readonly int _financeId;
    private readonly FinancialPattern _proposedPattern;
    private readonly int _accountId;

    /// <summary>
    /// Runs the live forecast, which is how this class reads the book (AllFinancialPatterns, EarMarkPatternsFor) and PlanHealthState.
    /// The repositories are only for writing and for reading a goal's manual earmarks, which the forecast folds into earmark events.
    /// </summary>
    private readonly Func<ForecastResult> _requestForecast;

    /// <summary>
    /// Re-runs the forecast with some goals' plans left out, to measure the room for those plans (AffordabilityCeilingFor).
    /// MainWindow provides it in the app; null in tests, where no affordability cap applies.
    /// </summary>
    private readonly Func<IReadOnlySet<int>, ForecastResult>? _requestForecastOmitting;
    private readonly FinancePatternRepositories _repositories;

    // Which fields changed, for the popup's description, and whether only Amount changed, which triggers the proportional-scaling offer; set in DetermineConditions.
    private bool _startChanged;
    private bool _amountChanged;
    private bool _recurrenceShapeChanged;
    private bool _isAmountOnlyChange;

    /// <summary>
    /// The FinanceId to open the Earmark form under after saving (AskWhichEarmarkPatternToOpen); _financeId unless a break-off happened.
    /// After a break-off, _financeId's plan is the ended predecessor, so PerformSingleSuccessorBreakOff and PerformMultiPlanBreakOff set this to the successor's id.
    /// </summary>
    private int _navigationFinanceId;

    /// <summary>
    /// The plan shapes to offer alongside Propose's default, worked out by DeterminePlanShapeCandidatesIfApplicable before saving.
    /// Empty when there's nothing to choose: most edits, a break-off with no existing plan, or plans that run at the same time; RestructureFactory.FindCurrentPlan tells a chain of plans apart from those.
    /// </summary>
    private IReadOnlyList<PlanShapeCandidate> _planShapeCandidates = [];

    /// <summary>
    /// One plan shape to offer; Label is display text only.
    /// The plan the user picks comes back as ChosenPlanShape and is matched to its full ProposedAllocationPlan (starting earmark included) by reference in PerformSingleSuccessorBreakOff.
    /// </summary>
    public sealed record PlanShapeCandidate(string Label, ProposedAllocationPlan Plan);

    /// <summary>
    /// What ApplyBackTruncationsIfNeeded needs to keep every plan's Until inside the goal's (3.11.2.a2), worked out before saving.
    /// Null when every plan already fits; checked on every save, not just Critical or multi-plan ones.
    /// </summary>
    private BackTruncationPlan? _backTruncations;

    private sealed record BackTruncationPlan(
        IReadOnlyList<EarMarkPattern> PlansExceedingNewUntil,
        IReadOnlyList<DateOnly> OrphanedManualEarmarkDates);

    /// <summary>
    /// The Start-side version of _backTruncations: what ApplyFrontTruncationsIfNeeded needs when the Start moves later in place (a non-Critical edit; a Critical one breaks off instead).
    /// Without it, a plan still starting before the new Start breaks 3.11.2.a2 and the next GetAll() throws.
    /// Null when every plan already starts on or after the new Start.
    /// </summary>
    private FrontTruncationPlan? _frontTruncations;

    private sealed record FrontTruncationPlan(
        IReadOnlyList<EarMarkPattern> PlansStartingBeforeNewStart,
        IReadOnlyList<DateOnly> OrphanedManualEarmarkDates);

    /// <summary>
    /// When the Start or Until moves outward in place, a plan sharing that exact boundary moves out with it, adding occurrences at its own rhythm and amount; worked out before saving, since it needs the old boundary.
    /// Null when no boundary moved outward or no plan shared it.
    /// Only the edited goal's own plans move, not a chain neighbor stretched by ExtendStart/ExtendUntil; it's shown as a "[bill] occurs N more times" row, and checking whether the grown plan is over- or underfunded isn't built yet.
    /// </summary>
    private BoundaryExtensionPlan? _boundaryExtensions;

    /// <param name="AddedGoalOccurrences">How many more times the goal now occurs, for the "[bill] occurs N more times" row.</param>
    private sealed record BoundaryExtensionPlan(
        IReadOnlyList<EarMarkPattern> PlansSharingOldStart,
        IReadOnlyList<EarMarkPattern> PlansSharingOldUntil,
        int AddedGoalOccurrences);

    /// <summary>
    /// Later patterns this Amount change cascades onto that more than one plan funds; each gets its own combine-or-keep-separate question, separate from the break-off's consolidation row.
    /// Empty when the cascade reaches none.
    /// </summary>
    private IReadOnlyList<FinancialPattern> _crossBoundaryConsolidations = [];

    /// <summary>The answer to each of those questions, by successor finance id: true to combine, false (the default) to keep separate; applied by ReconcileCascadedSuccessorSavingsPlans.</summary>
    private IReadOnlyDictionary<int, bool> _successorCombineChoices = new Dictionary<int, bool>();

    // Whether this save consolidates any plans, so the two consolidation questions apply; the spread question only matters when there's one clear income to pace against.
    private bool _consolidationStrategyApplies;
    private bool _consolidationHasIncomeForSpread;

    // The user's two consolidation answers, used for every consolidation this save makes; with no popup, they meet the goal and pace to income.
    private ConsolidationSizing _chosenSizing = ConsolidationSizing.MeetGoal;
    private ConsolidationSpread _chosenSpread = ConsolidationSpread.AcrossPaydays;

    /// <summary>
    /// What keeping this break-off's plans separate would fund the new segment at: a dry run re-rated to meet the goal (EarmarkScaling.ScaleToMeetGoal), worked out before saving.
    /// Null unless a keep-separate break-off is possible; only asked about when the current and needed totals differ (see DetermineKeepSeparateFundingIfApplicable).
    /// </summary>
    private MeetGoalScalingResult? _keepSeparateFunding;

    /// <summary>
    /// The values an accepted goal-health suggestion pre-fills the Earmark form with (EarmarkFieldOverrideKeys to value), so it opens with the correction as unsaved edits.
    /// Null until the user accepts one; single-plan only, since multi-plan cases are handled by automatic changes with no single form to fill.
    /// </summary>
    private IReadOnlyDictionary<string, object?>? _acceptedSuggestionOverrides;

    /// <summary>
    /// The corrections the goal-health suggestion offers, most recommended first: each a plan re-sized to meet the edited goal (EarmarkScaling.ScaleToMeetGoal), with its option label.
    /// Empty unless there's a real correction; only each plan's Amount is used, and duplicates are removed (see DetermineGoalHealthSuggestionIfApplicable).
    /// </summary>
    private IReadOnlyList<GoalHealthCorrection> _goalHealthCorrections = [];

    // Does the plan save too little (the goal falls short) rather than too much? Sets the reject warning's wording; only meaningful when _goalHealthCorrections isn't empty.
    private bool _goalHealthUnderfunds;

    /// <summary>One correction the goal-health picker offers: its option label, and the re-sized plan whose values the form pre-fills if picked.</summary>
    private sealed record GoalHealthCorrection(string Label, EarMarkPattern Plan);

    // An overfunded goal gets a two-step question instead: lower the rate to meet the goal, or keep it and skip some upcoming contributions to use up the surplus.
    // _goalHealthLowerRatePlan is the lower-rate plan, and _goalHealthSkips the skip choices; both are empty unless this save leaves the plan overfunded (see DetermineGoalHealthSuggestionIfApplicable).
    private EarMarkPattern? _goalHealthLowerRatePlan;
    private IReadOnlyList<GoalHealthSkip> _goalHealthSkips = [];

    /// <summary>One skip choice: its option label, and the contribution dates it skips (the ExcludedDates the form pre-fills).</summary>
    private sealed record GoalHealthSkip(string Label, IReadOnlyList<DateOnly> Dates);

    /// <summary>The plan's "Worth a look" health warning, shown as a row in this save's one confirmation; "" when there's nothing to warn about (see DetermineConcerningPlanNoticeIfApplicable).</summary>
    private string _concerningPlanNotice = "";

    /// <summary>
    /// The saved pattern plus every other same-Source pattern, excluding ones that run at the same time (see DetermineChainConditionsIfApplicable).
    /// Captured before saving, since PerformSave overwrites _financeId and PerformChainChangesIfApplicable compares against what was there; null when that never ran or the pattern is new.
    /// </summary>
    private ChainContext? _chainContext;

    private sealed record ChainContext(FinancialPattern Saved, IReadOnlyList<FinancialPattern> OtherPatterns);

    /// <summary>
    /// Does the edited pattern have a later segment in its chain (from DetermineChainConditionsIfApplicable)?
    /// For a Critical edit, an earlier segment is edited in place and cascaded forward, while the current segment breaks off from today.
    /// </summary>
    private bool _chainHasSuccessor;

    /// <summary>
    /// For the paycheck cascade: the income as it was before this edit, and the plans paced against it that no longer line up; captured before saving for PerformPaycheckAssociationCascadeIfApplicable.
    /// Null when this isn't an income edit or no paced plan stops lining up; "paced against" is AllocationPlanProposer.IsPacedAgainst's heuristic.
    /// </summary>
    private PaycheckAssociationContext? _paycheckAssociationContext;

    private sealed record PaycheckAssociationContext(FinancialPattern OldIncome, IReadOnlyList<EarMarkPattern> InvalidatedPlans);

    // ---- The conditions this class branches on ----

    /// <summary>Does this FinanceId already have more than one plan (relaxing 3.11.1.a1), from a chain or running at the same time? Set in DetermineConditions.</summary>
    private bool HasMultipleEarmarkPatterns { get; set; }

    /// <summary>Has the user made at least one of this goal's plans their own (EarMarkPattern.ExplicitlyCreated)? The combine-or-keep-separate question only means something then. Set in DetermineConditions.</summary>
    private bool HasExplicitEarmarkPattern { get; set; }

    /// <summary>
    /// Do the changed fields make keeping several plans separate impossible (a recurrence-shape change)?
    /// Then combining is announced rather than offered; otherwise the user is asked. Set in DetermineConditions.
    /// </summary>
    private bool ConsolidationNeeded { get; set; }

    /// <summary>Was "Save and Skip planning" clicked rather than "Save and Plan"? Then there's no navigation, so no plan-picking question or suggestion popup. Set at construction.</summary>
    private bool UserSkippedPlanning { get; }

    /// <summary>
    /// The plan shape the user picked from _planShapeCandidates; null means Propose's default, whether there was nothing to choose or the user chose it.
    /// Matched to its full ProposedAllocationPlan by reference in PerformSingleSuccessorBreakOff.
    /// </summary>
    private EarMarkPattern? ChosenPlanShape { get; set; }

    /// <summary>Is the resulting plan worth suggesting a fix for (PlanHealthState.IsWorthWarningAbout)? It triggers the concerning-plan row. Set in DetermineConditions.</summary>
    private bool ChangeWarrantsSuggestions { get; set; }

    // Does this edit touch a chain neighbor's boundary, and can its Amount or shape change cascade forward? Both come from DetermineChainConditionsIfApplicable, and either, both, or neither can hold.
    // EarmarkPatternSaveConfirmation has the plan-chain versions.
    private bool TouchesChainBoundary { get; set; }
    private bool ChangeCanCascade { get; set; }

    /// <summary>
    /// Can a change to Priority, Mandatory, Description, Notes, or AutoRenew cascade forward? Plans have none of these fields, so there's no plan version.
    /// Unlike Amount and shape, the default answer is not to cascade (see UserChoseCascadeTrivialFields).
    /// </summary>
    private bool TrivialFieldsCanCascade { get; set; }

    /// <summary>
    /// The Source-change warning ("warn, don't block"): a sentence naming what the change disconnects, shown with no choice since the edit goes ahead either way.
    /// "" when Source didn't change, or the segment has no predecessor or successor to disconnect from.
    /// </summary>
    private string SourceChangeWarning { get; set; } = "";

    /// <summary>
    /// Was another pattern's active plan paced against this income's old schedule and no longer lines up (DeterminePaycheckAssociationIfApplicable)?
    /// That's other patterns' plans, unlike TouchesChainBoundary and ChangeCanCascade, which are about this pattern's own chain.
    /// </summary>
    private bool PacedBillsCanCascade { get; set; }

    /// <summary>
    /// The user's answer to "stay linked in the chain, or let it break"; only meaningful when TouchesChainBoundary.
    /// Defaults to staying linked, the one choice that's never destructive on its own (EarmarkPatternSaveConfirmation has the plan-chain version).
    /// </summary>
    private bool UserChoseStayLinked { get; set; } = true;

    /// <summary>The user's answer to "cascade forward, or just this segment"; only meaningful when ChangeCanCascade, and defaults to cascading.</summary>
    private bool UserChoseCascadeForward { get; set; } = true;

    /// <summary>The user's answer to the trivial-fields question; only meaningful when TrivialFieldsCanCascade, and defaults to just this segment, unlike UserChoseCascadeForward.</summary>
    private bool UserChoseCascadeTrivialFields { get; set; }

    /// <summary>The user's answer to re-pacing bills after a paycheck change; only meaningful when PacedBillsCanCascade, and defaults to leaving them, since re-pacing visibly changes how money moves.</summary>
    private bool UserChoseToRepaceBills { get; set; }

    /// <summary>The user's answer to "keep them separate or combine into one" on a break-off; only meaningful with several plans not forced to combine, and defaults to keeping them separate, the less destructive choice.</summary>
    private bool UserChoseCombinePlans { get; set; }

    /// <summary>
    /// The user's answer to "adjust the kept-separate plans to meet the new amount?"; only meaningful when that question was shown.
    /// The popup pre-selects "adjust", but with no answer it defaults to leaving each plan's rate.
    /// </summary>
    private bool UserChoseToAdjustKeptSeparatePlans { get; set; }

    /// <summary>
    /// Did a restricted field (start, amount, or recurrence shape) change on a pattern that already has an occurrence on or before today?
    /// Then the edit reaches history, so it breaks off from today rather than saving in place. Set in DetermineConditions.
    /// </summary>
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

    /// <summary>
    /// [READS FILE] The affordability ceiling for `target`, measured on a re-forecast without `omitFinanceId`'s whole chain of plans, so the plans being resized count their own contributions as available.
    /// Returns the starting-earmark ceiling when `startingEarmark` is true, otherwise the ceiling on an ongoing per-cycle contribution; null when no omitting forecast is wired (as in tests).
    /// The three wrappers below pick which chain to omit and which ceiling.
    /// </summary>
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

    /// <summary>
    /// [READS FILE] The most a change to `target`'s plan may set aside per cycle without over-committing, measured without the edited pattern's whole chain of plans.
    /// Used by the goal-health suggestion, the keep-separate funding correction, and the cross-boundary cascade; null (no cap) when no omitting forecast is wired.
    /// </summary>
    /// <param name="target">The goal or bill whose plan is being sized (in the edited pattern's chain).</param>
    /// <param name="changeKind">Whether this is a bold Suggestion or a cautious Implicit change.</param>
    private decimal? AffordabilityCeilingFor(FinancialPattern target, ChangeKind changeKind) =>
        CeilingOmitting(_financeId, target, changeKind, startingEarmark: false);

    /// <summary>
    /// [READS FILE] The most a proposed starting earmark for `target` may set aside on the as-of day, measured without `target`'s own chain of plans.
    /// Keyed on the target rather than the edited pattern, since a paycheck re-pace sizes other bills; null when no omitting forecast is wired.
    /// </summary>
    /// <param name="target">The bill whose starting earmark is being sized.</param>
    /// <param name="changeKind">Whether this is a bold Suggestion or a cautious Implicit change.</param>
    private decimal? StartingEarmarkCeilingFor(FinancialPattern target, ChangeKind changeKind) =>
        CeilingOmitting(target.FinanceId, target, changeKind, startingEarmark: true);

    /// <summary>
    /// [READS FILE] The most a re-proposed plan for `target` may set aside per cycle, measured without `target`'s own chain of plans.
    /// Keyed on the target rather than the edited pattern, since a paycheck re-pace re-proposes other bills; null when no omitting forecast is wired.
    /// </summary>
    /// <param name="target">The bill whose plan is being re-proposed.</param>
    /// <param name="changeKind">Whether this is a bold Suggestion or a cautious Implicit change.</param>
    private decimal? OngoingRateCeilingFor(FinancialPattern target, ChangeKind changeKind) =>
        CeilingOmitting(target.FinanceId, target, changeKind, startingEarmark: false);

    /// <summary>
    /// Opens the Earmark form after saving, set by MainWindow like the other save callbacks; never called when UserSkippedPlanning.
    /// A null plan opens a blank form; the second argument is an accepted suggestion's values (EarmarkFieldOverrideKeys) to pre-fill as unsaved edits, or null for a plain open.
    /// </summary>
    public Action<EarMarkPattern?, IReadOnlyDictionary<string, object?>?>? NavigateToEarmarkForm { get; set; }

    /// <summary>Asks which plan to open when there's more than one (EarmarkPatternPickerWindow); null picks the first match.</summary>
    public Func<IReadOnlyList<EarMarkPattern>, EarMarkPattern?>? PickEarmarkPattern { get; set; }

    /// <summary>
    /// Shows the confirmation: one combined popup covering every question, rather than several stacked.
    /// This class builds the request and applies the answer but never creates a window, so it stays free of WPF; MainWindow shows it.
    /// Null (most tests) uses DefaultOutcome's safest answer to every question, so Run() still completes without a UI.
    /// </summary>
    public Func<ImplicitChangeConfirmationRequest, ConfirmationOutcome>? ConfirmImplicitChanges { get; set; }

    /// <summary>[STEP] The single entry point: works out whether anything needs asking, shows the confirmation if so, saves the FinancialPattern, makes whatever automatic plan and earmark changes were decided, and (on Save and Plan) calls NavigateToEarmarkForm.</summary>
    /// <returns>False if the user cancelled the confirmation, in which case nothing was saved; true otherwise, including when nothing needed asking.</returns>
    public bool Run()
    {
        // Every Determine* call reads the saved data, so all of them must run before PerformSave writes the new pattern.
        DetermineConditions();

        // Independent of whether the edit is Critical: even a Priority change that also renames the Source can touch the chain.
        DetermineChainConditionsIfApplicable();

        // This income's effect on other patterns' plans, separate from its own chain.
        DeterminePaycheckAssociationIfApplicable();

        // The successor plan shapes offered when a Critical edit breaks off.
        DeterminePlanShapeCandidatesIfApplicable();

        // Checked on every save, not just ones that need asking: Until can change without a confirmation, but each plan must still fit inside the shortened Until (3.11.2.a2).
        DetermineBackTruncationsIfApplicable();

        // The same check for a Start moving later in place; a plan still starting before it would make the next GetAll() throw.
        DetermineFrontTruncationsIfApplicable();

        // A boundary moving outward, with a plan that shared it: that plan grows with it.
        DetermineBoundaryExtensionsIfApplicable();

        // A later segment the Amount cascade reaches, funded by more than one plan, gets its own combine-or-keep-separate row, never the break-off's consolidation row.
        DetermineCrossBoundaryConsolidationsIfApplicable();

        // If keeping this segment's plans separate would over- or underfund the new amount, works out the correction to offer; must run after DetermineChainConditionsIfApplicable, which sets _chainHasSuccessor.
        DetermineKeepSeparateFundingIfApplicable();

        // If this edit leaves the goal's single plan out of step with it, works out a corrected plan to offer; accepting it pre-fills that plan's form.
        DetermineGoalHealthSuggestionIfApplicable();

        // The plan's health warning, shown as a row in the confirmation so all of this save's messages are in one place.
        DetermineConcerningPlanNoticeIfApplicable();

        // Design rule: don't bother the user about an automatic change that doesn't alter anything they did.
        // A plan only becomes the user's own once they save its form or choose a full save for it in a popup, so changing only automatic plans stays silent; the three checks below are what make a save worth confirming.

        // The edit changes recorded history, a neighboring chain segment, or the pattern's name: always the user's own work.
        var editTouchesRecordedHistoryOrChain =
            IsChangeCritical || TouchesChainBoundary || ChangeCanCascade
            || TrivialFieldsCanCascade || !string.IsNullOrEmpty(SourceChangeWarning);

        // The save combines, splits, re-paces, or grows a plan the user made their own.
        var changesAnExplicitlyOwnedPlan =
            (HasMultipleEarmarkPatterns && HasExplicitEarmarkPattern)
            || PacedBillsCanCascade
            || (_boundaryExtensions is { AddedGoalOccurrences: > 0 } && BoundaryExtensionTouchesExplicitPlan);

        // There's a suggested fix to offer, or a "worth a look" warning about a plan the user owns.
        var hasPlanHealthAdvice =
            _goalHealthCorrections.Count > 0 || _goalHealthLowerRatePlan is not null || !string.IsNullOrEmpty(_concerningPlanNotice);

        if (editTouchesRecordedHistoryOrChain || changesAnExplicitlyOwnedPlan || hasPlanHealthAdvice)
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
                // Option 1 combines; option 0 keeps separate, the default.
                successor => Chosen(outcome, ConfirmationRowIds.CrossBoundaryConsolidation(successor.FinanceId)) == 1);
            _chosenSizing = ChoseConsolidationSizing(outcome);
            _chosenSpread = ChoseConsolidationSpread(outcome);

            // The picked goal-health correction becomes the values the plan's form opens with; no answer, or "leave it", opens the form plain.
            _acceptedSuggestionOverrides = PickGoalHealthOverrides(outcome);
        }

        PerformSave();

        // Must run before PerformImplicitEarmarkChanges: when one save raises both a
        // chain question and a funding question at once, the chain question resolves first.
        PerformChainChangesIfApplicable();
        PerformImplicitEarmarkChanges();

        PerformPaycheckAssociationCascadeIfApplicable();

        if (!UserSkippedPlanning)
        {
            // Cancelling the which-plan picker leaves the user where they are.
            var navigation = AskWhichEarmarkPatternToOpen();
            if (navigation.Navigate)
            {
                NavigateToEarmarkForm?.Invoke(navigation.Target, _acceptedSuggestionOverrides);
            }
        }

        return true;
    }

    /// <summary>
    /// [CALC] The outcome when no ConfirmImplicitChanges is wired (most tests): proceed, with no answers.
    /// Every question then reads as its safe default: stay linked, cascade a rate or schedule change forward, don't cascade trivial fields, leave paced bills alone.
    /// </summary>
    private static ConfirmationOutcome DefaultOutcome() => new() { Proceed = true };

    /// <summary>
    /// [CALC] Returns which option the user picked for one confirmation row, by its position in that row as BuildRows laid it out.
    /// -1 when the row has no answer (it wasn't shown, or no popup asked); callers treat that as the row's safe default.
    /// </summary>
    private static int Chosen(ConfirmationOutcome outcome, string rowId) =>
        outcome.ChosenOptionIndex.TryGetValue(rowId, out var index) ? index : -1;

    /// <summary>[CALC] Whether the user chose to keep the break-off chain linked (option 0) rather than let it break (option 1); keep-linked is the default when the row wasn't answered.</summary>
    private static bool ChoseStayLinked(ConfirmationOutcome outcome) => Chosen(outcome, ConfirmationRowIds.ChainBoundary) != 1;

    /// <summary>[CALC] Whether to apply the change forward to the rest of the chain (option 0) rather than to this segment only (option 1); applying forward is the default when the row wasn't answered.</summary>
    private static bool ChoseCascadeForward(ConfirmationOutcome outcome) => Chosen(outcome, ConfirmationRowIds.Cascade) != 1;

    /// <summary>[CALC] Whether to cascade the trivial-field edits (Priority/Mandatory/Description/Notes/AutoRenew) forward to the rest of the chain (option 1) rather than to this segment only (option 0, the default when the row wasn't answered).</summary>
    private static bool ChoseCascadeTrivialFields(ConfirmationOutcome outcome) => Chosen(outcome, ConfirmationRowIds.TrivialFieldsCascade) == 1;

    /// <summary>[CALC] Whether to re-pace the associated bills onto the new schedule (option 0, the popup's pre-selection) rather than leave them (option 1); an unanswered row reads as leave, so nothing re-paces money when no popup asked.</summary>
    private static bool ChoseToRepaceBills(ConfirmationOutcome outcome) => Chosen(outcome, ConfirmationRowIds.PacedBillsCascade) == 0;

    /// <summary>
    /// [CALC] Whether to combine the plans (option 1) rather than keep them separate (option 0, the default, also when no popup asked).
    /// Only used when the break-off isn't already forced to combine by a shape or start change.
    /// </summary>
    private static bool ChoseCombinePlans(ConfirmationOutcome outcome) => Chosen(outcome, ConfirmationRowIds.Consolidation) == 1;

    /// <summary>[CALC] Whether to adjust the kept-separate plans to meet the goal (option 0, the popup's pre-selection) rather than leave each at its own rate (option 1); an unanswered row reads as leave, so nothing re-rates money when no popup asked.</summary>
    private static bool ChoseToAdjustKeptSeparatePlans(ConfirmationOutcome outcome) => Chosen(outcome, ConfirmationRowIds.KeepSeparateFunding) == 0;

    /// <summary>[CALC] The consolidation sizing the user picked — keep the current rate (option 1) or meet the goal (option 0, the default, also when no popup asked).</summary>
    private static ConsolidationSizing ChoseConsolidationSizing(ConfirmationOutcome outcome) =>
        Chosen(outcome, ConfirmationRowIds.ConsolidationSizing) == 1 ? ConsolidationSizing.KeepCurrentPace : ConsolidationSizing.MeetGoal;

    /// <summary>[CALC] The consolidation spread the user picked — spread evenly (option 1) or across paydays (option 0, the default, also when no popup asked).</summary>
    private static ConsolidationSpread ChoseConsolidationSpread(ConfirmationOutcome outcome) =>
        Chosen(outcome, ConfirmationRowIds.ConsolidationSpread) == 1 ? ConsolidationSpread.Evenly : ConsolidationSpread.AcrossPaydays;

    /// <summary>
    /// [CALC] Works out the conditions above from the saved plan and the proposed edit.
    /// It reads the current forecast rather than simulating the edit, so ChangeWarrantsSuggestions only catches a plan that's already concerning, not one this edit would newly make concerning (the Expense form's status indicator has the same gap).
    /// Internal so FinancePatternSaveConfirmationTests can call it directly.
    /// </summary>
    internal void DetermineConditions()
    {
        var saved = _repositories.FinancialPatterns.GetByFinanceId(_financeId);
        if (saved is null)
        {
            // A brand-new pattern: nothing to change, so every condition stays false.
            return;
        }

        var forecast = _requestForecast();

        _recurrenceShapeChanged =
            saved.DatePattern.Frequency != _proposedPattern.DatePattern.Frequency ||
            saved.DatePattern.Interval != _proposedPattern.DatePattern.Interval ||
            !saved.DatePattern.ByDay.SequenceEqual(_proposedPattern.DatePattern.ByDay) ||
            !saved.DatePattern.ByMonthDay.SequenceEqual(_proposedPattern.DatePattern.ByMonthDay);

        // Counts ActiveFrom as well as Start, since either moves the active span and can strand a plan or manual earmark outside it.
        _startChanged = saved.DatePattern.ActiveStart != _proposedPattern.DatePattern.ActiveStart;

        _amountChanged = saved.Amount != _proposedPattern.Amount;
        var restrictedFieldChanged = _recurrenceShapeChanged || _startChanged || _amountChanged;
        _isAmountOnlyChange = _amountChanged && !_startChanged && !_recurrenceShapeChanged;

        // 1.2.3.10.a5: an occurrence on or before the as-of date counts as "an expected transaction on an expired page in the past" with today's single page.
        // Checked on the saved pattern, since what already happened doesn't depend on the edit.
        var hasPastOccurrence = saved.DatePattern.GetOccurrences(to: forecast.AsOfDate).Count > 0;

        // A pattern that started today or yesterday has no history worth keeping, so the edit replaces it in place instead of breaking off.
        // That skips the "you're changing history" confirmation and avoids BreakOffFactory rejecting a cut on the pattern's own start date.
        var negligibleHistoryToPreserve = forecast.AsOfDate <= saved.DatePattern.ActiveStart.AddDays(1);

        IsChangeCritical = restrictedFieldChanged && hasPastOccurrence && !negligibleHistoryToPreserve;

        var savingsPlan = forecast.Book.EarMarkPatternsFor(_financeId);
        HasMultipleEarmarkPatterns = savingsPlan.Count > 1;

        HasExplicitEarmarkPattern = savingsPlan.Any(plan => plan.ExplicitlyCreated);

        // Plans can only keep their own dates on a break-off when the successor keeps the same shape and start, so a change to either forces them to combine.
        ConsolidationNeeded = HasMultipleEarmarkPatterns && (_recurrenceShapeChanged || _startChanged);

        ChangeWarrantsSuggestions = forecast.PlanHealthStates
            .FirstOrDefault(health => health.FinanceId == _financeId)
            ?.IsWorthWarningAbout ?? false;
    }

    /// <summary>
    /// [READS FILE] Works out the chain questions: does this edit touch a chain boundary, can its Amount or shape cascade forward, can its trivial fields cascade forward, and does a Source change disconnect it from its chain?
    /// Stores _chainContext for PerformChainChangesIfApplicable, since after PerformSave a read would see the new pattern instead of the one to compare against.
    /// Does nothing for a brand-new pattern; internal for the same reason as DetermineConditions.
    /// </summary>
    internal void DetermineChainConditionsIfApplicable()
    {
        var saved = _repositories.FinancialPatterns.GetByFinanceId(_financeId);
        if (saved is null)
        {
            return; // brand-new pattern — nothing to compare against, no chain question
        }

        var forecast = _requestForecast();
        var allPatterns = forecast.Book.AllFinancialPatterns();

        // Leaves out same-Source patterns that overlap this one (BreakOffFactory.SpansOverlap), so PerformChainChangesIfApplicable never absorbs or cascades onto one as if it were a chain neighbor.
        var otherPatterns = allPatterns
            .Where(pattern => pattern.Source == saved.Source && pattern.FinanceId != saved.FinanceId && !BreakOffFactory.SpansOverlap(pattern, saved))
            .ToList();
        _chainContext = new ChainContext(saved, otherPatterns);

        var hasPredecessor = otherPatterns.Any(pattern => pattern.DatePattern.ActiveStart < saved.DatePattern.ActiveStart);
        var hasSuccessor = otherPatterns.Any(pattern => pattern.DatePattern.ActiveStart > saved.DatePattern.ActiveStart);

        var startChanged = saved.DatePattern.ActiveStart != _proposedPattern.DatePattern.ActiveStart;
        var untilChanged = saved.DatePattern.Until != _proposedPattern.DatePattern.Until;
        var trivialFieldsChanged = saved.Priority != _proposedPattern.Priority
            || saved.Mandatory != _proposedPattern.Mandatory
            || saved.Description != _proposedPattern.Description
            || saved.Notes != _proposedPattern.Notes
            || saved.AutoRenew != _proposedPattern.AutoRenew;

        _chainHasSuccessor = hasSuccessor;

        // Editing a segment that has a later segment is saved in place, even when it reaches past history: the user opens the earliest segment to change, and the change flows forward (PerformSave and PerformImplicitEarmarkChanges also check hasSuccessor).
        // Only a Critical edit of the current segment breaks off from today; its typed Start and Until never land on the chain, so the boundary question is skipped for it.
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

    /// <summary>
    /// [READS FILE] Works out whether this edit changes an income's schedule so that another pattern's current plan is no longer paced against it; does nothing unless the saved pattern was already income.
    /// Uses AllocationPlanProposer.IsPacedAgainst twice: against the old income to find plans that were paced, then against the proposed one to drop those that still are.
    /// It isn't limited to non-Critical edits, since a break-off's successor keeps the proposed schedule, so the proposed pattern is the right one to check either way.
    /// </summary>
    internal void DeterminePaycheckAssociationIfApplicable()
    {
        var saved = _repositories.FinancialPatterns.GetByFinanceId(_financeId);
        if (saved is not { Amount: > 0m })
        {
            return; // wasn't income before this edit — nothing could have been paced against it
        }

        var forecast = _requestForecast();

        // Each finance id's current plan only: an older segment isn't worth asking about, and plans running at the same time (FindCurrentPlan returns null) have no single answer, so they're skipped.
        var currentPlans = forecast.Book.AllEarMarkPatterns()
            .GroupBy(plan => plan.FinanceId)
            .Select(group => RestructureFactory.FindCurrentPlan(group.ToList()))
            .OfType<EarMarkPattern>()
            .ToList();

        var invalidatedPlans = AllocationPlanProposer.FindPlansPacedAgainst(saved, currentPlans)
            .Where(plan => !AllocationPlanProposer.IsPacedAgainst(plan, _proposedPattern))
            // Only plans the user made their own are worth asking about (the design rule in Run).
            .Where(plan => plan.ExplicitlyCreated)
            .ToList();

        if (invalidatedPlans.Count == 0)
        {
            return;
        }

        _paycheckAssociationContext = new PaycheckAssociationContext(saved, invalidatedPlans);
        PacedBillsCanCascade = true;
    }

    /// <summary>
    /// [READS FILE] Works out which plan shapes besides AllocationPlanProposer.Propose's default are available for this break-off's successor, stored in _planShapeCandidates.
    /// BuildConfirmationRequest shows them, and PerformSingleSuccessorBreakOff or PerformMultiPlanBreakOff applies the one chosen; internal so a test can call it directly.
    /// </summary>
    internal void DeterminePlanShapeCandidatesIfApplicable()
    {
        // Only for a Critical edit of the current segment: an earlier segment saves in place, and proposing a plan starting today against its past schedule would throw.
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

        // The same successor PerformSingleSuccessorBreakOff builds later: identity fields from the saved pattern, Amount and schedule from the edit.
        // Nothing is saved in between, so NextFinanceId() returns the same id both times.
        var successor = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = forecast.Book.NextFinanceId(),
            Source = saved.Source,
            Description = saved.Description,
            Notes = saved.Notes,
            DatePattern = RecurrenceRule.Create(BuildSuccessorSchedule(cutDate)),
            Amount = _proposedPattern.Amount,
            Priority = saved.Priority,
            Mandatory = saved.Mandatory,
            AutoRenew = saved.AutoRenew,
        });

        // The paychecks every plan choice for the successor may pace against: this account's own patterns, without transfers.
        var patternsToPaceAgainst = PatternsToPaceAgainst(forecast.Book.AllFinancialPatterns(), _accountId);
        var carriedOverJarBalance = JarBalanceOn(cutDate, _financeId);

        // Must be read before PerformSave, while every manual earmark still validates against its unchanged plan.
        var manualEarmarks = _repositories.ManualEarmarks.GetAll()
            .Where(earmark => earmark.FinanceId == _financeId)
            .ToList();

        var candidates = new List<PlanShapeCandidate>
        {
            // Passes carriedOverJarBalance so the preview shows the balance BreakOffFactory.BreakOff will actually save, like the other two candidates.
            // Both ceilings are measured on the edited goal's chain, since the unsaved successor's own chain has nothing to free up; Suggestion tier, since the user picks from this list.
            new("Recommended", AllocationPlanProposer.Propose(successor, patternsToPaceAgainst, cutDate,
                carriedOverJarBalance: carriedOverJarBalance,
                startingEarmarkCeiling: CeilingOmitting(_financeId, successor, ChangeKind.Suggestion, startingEarmark: true),
                ongoingRateCeiling: CeilingOmitting(_financeId, successor, ChangeKind.Suggestion, startingEarmark: false))),
        };

        if (AllocationPlanProposer.ProposeSameSchedule(successor, existingPlan, carriedOverJarBalance, manualEarmarks, patternsToPaceAgainst, cutDate) is { } sameSchedule)
        {
            candidates.Add(new("Keep the same schedule", sameSchedule));
        }

        if (AllocationPlanProposer.ProposeSameAmount(successor, existingPlan, carriedOverJarBalance, manualEarmarks, patternsToPaceAgainst, cutDate) is { } sameAmount)
        {
            candidates.Add(new("Keep the same amount", sameAmount));
        }

        // Both return null when they'd match the default, so more than one entry means a real choice.
        if (candidates.Count > 1)
        {
            _planShapeCandidates = candidates;
        }
    }

    /// <summary>
    /// [READS FILE] Works out whether any plan's Until now runs past the proposed pattern's, and if so what ApplyBackTruncationsIfNeeded needs: the plans to shorten and the manual earmarks now past the end to delete.
    /// Runs on every save, since shortening Until never needs asking but 3.11.2.a2 must always hold.
    /// It must run before PerformSave: reading manual earmarks after the goal shortens would check them against the new Until and throw on the very rows being found.
    /// Internal so a test can call it without Run()'s confirmation.
    /// </summary>
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

        // Must be read before PerformSave, while every manual earmark still validates against its unchanged plan.
        var orphanedDates = _repositories.ManualEarmarks.GetAll()
            .Where(earmark => earmark.FinanceId == _financeId && earmark.Date > newUntil)
            .Select(earmark => earmark.Date)
            .ToList();

        _backTruncations = new BackTruncationPlan(plansExceedingNewUntil, orphanedDates);
    }

    /// <summary>
    /// [READS FILE] The Start-side version of DetermineBackTruncationsIfApplicable: works out whether any plan now starts before the proposed Start, and if so the plans to trim and the manual earmarks now before it to delete.
    /// Runs on every save and before PerformSave, for the same reasons; internal so a test can call it directly.
    /// </summary>
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

        // Must be read before PerformSave, while every manual earmark still validates against its unchanged plan.
        var orphanedDates = _repositories.ManualEarmarks.GetAll()
            .Where(earmark => earmark.FinanceId == _financeId && earmark.Date < newStart)
            .Select(earmark => earmark.Date)
            .ToList();

        _frontTruncations = new FrontTruncationPlan(plansStartingBeforeNewStart, orphanedDates);
    }

    /// <summary>
    /// [READS FILE] The outward version of the two truncation checks: works out whether Start or Until moved outward and which of the goal's own plans shared that boundary and should move with it.
    /// Must run before PerformSave overwrites the old boundary; stored in _boundaryExtensions, and internal so a test can call it directly.
    /// </summary>
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

        // Counts the edited pattern's new occurrences past the old Until and before the old Start.
        var addedPastOldUntil = _proposedPattern.DatePattern.Until > saved.DatePattern.Until
            ? _proposedPattern.DatePattern.GetOccurrences(saved.DatePattern.Until.AddDays(1), _proposedPattern.DatePattern.Until).Count
            : 0;
        var addedBeforeOldStart = _proposedPattern.DatePattern.ActiveStart < saved.DatePattern.ActiveStart
            ? _proposedPattern.DatePattern.GetOccurrences(_proposedPattern.DatePattern.ActiveStart, saved.DatePattern.ActiveStart.AddDays(-1)).Count
            : 0;

        _boundaryExtensions = new BoundaryExtensionPlan(sharingOldStart, sharingOldUntil, addedPastOldUntil + addedBeforeOldStart);
    }

    /// <summary>[CALC] Did the boundary move grow at least one plan the user made their own? Only then is it worth announcing.</summary>
    private bool BoundaryExtensionTouchesExplicitPlan =>
        _boundaryExtensions is { } ext
        && ext.PlansSharingOldStart.Concat(ext.PlansSharingOldUntil).Any(plan => plan.ExplicitlyCreated);

    /// <summary>
    /// [CALC] The "[bill] occurs N more times — its savings plan grows to keep pace" message for a boundary that moved outward and grew a plan.
    /// "" when no plan grew, no occurrence was added, or only automatic plans grew.
    /// </summary>
    private string DescribeBoundaryExtensionAnnouncement()
    {
        if (_boundaryExtensions is not { AddedGoalOccurrences: > 0 } ext || !BoundaryExtensionTouchesExplicitPlan)
        {
            // The extension still happens; there's just nothing the user did to announce (the design rule in Run).
            return "";
        }

        var times = ext.AddedGoalOccurrences == 1 ? "1 more time" : $"{ext.AddedGoalOccurrences} more times";
        return $"{_proposedPattern.Source} now occurs {times} — its savings plan grows to keep pace.";
    }

    /// <summary>
    /// [READS FILE] Works out which later patterns this Amount change cascades onto that more than one plan funds; each gets its own combine-or-keep-separate question in _crossBoundaryConsolidations.
    /// Amount-only successors only: a schedule change moves the dates and forces the plans to combine with no question (ReconcileCascadedSuccessorSavingsPlans).
    /// Does nothing unless the change can cascade; internal so a test can call it directly.
    /// </summary>
    internal void DetermineCrossBoundaryConsolidationsIfApplicable()
    {
        if (!ChangeCanCascade || _chainContext is not { } context)
        {
            return;
        }

        var book = _requestForecast().Book;
        var candidates = new List<FinancialPattern>();
        var anyConsolidating = false;
        var hasIncomeForSpread = false;

        foreach (var successor in context.OtherPatterns.Where(pattern => pattern.DatePattern.ActiveStart > context.Saved.DatePattern.ActiveStart))
        {
            var shapeChanged = !successor.DatePattern.HasSameShapeAs(_proposedPattern.DatePattern);
            var amountChanged = successor.Amount != _proposedPattern.Amount;
            if (!amountChanged && !shapeChanged)
            {
                continue; // the carry-forward doesn't touch this successor
            }

            var plans = book.EarMarkPatternsFor(successor.FinanceId);
            if (plans.Count <= 1)
            {
                continue; // a single earmark pattern is re-rated/re-aligned, never consolidated
            }

            // Several plans: a schedule change forces them to combine, and an amount-only change asks; either way the two consolidation questions apply.
            anyConsolidating = true;
            if (amountChanged && !shapeChanged)
            {
                candidates.Add(successor);
            }

            // The spread question only matters when "Across your paydays" can actually pace: exactly one paycheck paying within the dates the combined plan would cover.
            // Those dates and that count match EarmarkConsolidation.Consolidate and BuildSchedule: from the later of the earliest plan's start and the segment's start, through the segment's end.
            var earliestPlanStart = plans.Min(plan => plan.DatePattern.ActiveStart);
            var start = earliestPlanStart > successor.DatePattern.ActiveStart ? earliestPlanStart : successor.DatePattern.ActiveStart;
            var accountId = _repositories.FinancialPatterns.GetAccountId(successor.FinanceId) ?? _accountId;
            hasIncomeForSpread |= AllocationPlanProposer.ActiveIncomeStreams(
                PatternsToPaceAgainst(book.AllFinancialPatterns(), accountId), start, successor.DatePattern.Until).Count == 1;
        }

        _crossBoundaryConsolidations = candidates;
        _consolidationStrategyApplies = anyConsolidating;
        _consolidationHasIncomeForSpread = hasIncomeForSpread;
    }

    /// <summary>
    /// [READS FILE] Dry-runs keeping this break-off's plans separate and re-rates them to meet the goal, so the confirmation knows whether to ask "adjust them to meet it?".
    /// Only for a current-segment break-off with several plans that aren't forced to combine.
    /// Only the current and needed totals are kept; the real break-off works out the correction again against the real successor.
    /// </summary>
    private void DetermineKeepSeparateFundingIfApplicable()
    {
        // The same conditions PerformImplicitEarmarkChanges uses for a keep-separate break-off.
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

        // A break-off needs the cut after the segment's start (BreakOffFactory.ValidateCutBoundaries); otherwise there's nothing to preview.
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
            AllPatterns = PatternsToPaceAgainst(forecast.Book.AllFinancialPatterns(), _accountId),
        });

        var ceiling = AffordabilityCeilingFor(result.Successor, ChangeKind.Implicit);
        _keepSeparateFunding = EarmarkScaling.ScaleToMeetGoal(result.Successor, result.SuccessorPlans, affordabilityCeiling: ceiling);
    }

    /// <summary>
    /// [CALC] The wording of the keep-separate funding question, naming which way the plans are off so the user sees the consequence.
    /// "" when there's no gap to correct, so the row isn't shown.
    /// </summary>
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

    /// <summary>
    /// [READS FILE] Works out whether the goal's single plan would no longer meet the edited goal, and if so the corrected plan to suggest (EarmarkScaling.ScaleToMeetGoal: same schedule, re-sized amount); accepting pre-fills the plan's form.
    /// Only for a single-plan, amount-only edit that doesn't break off: a break-off already proposes a fitting plan, several plans are handled automatically, and "Save and skip planning" opens no form.
    /// A non-Critical edit means the plan has no past contributions, so re-sizing it is safe.
    /// </summary>
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

        // Offered only when the plan doesn't already meet the edited goal.
        var ceiling = AffordabilityCeilingFor(_proposedPattern, ChangeKind.Suggestion);
        var scaling = EarmarkScaling.ScaleToMeetGoal(_proposedPattern, [plan], affordabilityCeiling: ceiling);
        if (Math.Abs(scaling.CurrentTotal - scaling.NeededTotal) >= 0.01m)
        {
            _goalHealthUnderfunds = scaling.NeededTotal > scaling.CurrentTotal;

            if (_goalHealthUnderfunds)
            {
                // Underfunded: one correction, raising the rate to meet the goal.
                _goalHealthCorrections = DedupeCorrections(
                [
                    new GoalHealthCorrection("Load the suggested amount", scaling.ScaledPlans.Single()),
                ]);
            }
            else
            {
                // Overfunded: lower the rate to meet the goal, or keep it and skip some contributions.
                _goalHealthLowerRatePlan = scaling.ScaledPlans.Single();
                _goalHealthSkips = BuildGoalHealthSkips(plan);
            }
        }
    }

    /// <summary>
    /// [READS FILE] The skip choices for an overfunded plan: "skip the next contribution", and "skip a stretch to clear the surplus" when that drops more than one.
    /// Empty when the surplus isn't a whole contribution, so the skip question doesn't show.
    /// </summary>
    /// <param name="plan">The overfunded plan whose upcoming contributions might be skipped.</param>
    private IReadOnlyList<GoalHealthSkip> BuildGoalHealthSkips(EarMarkPattern plan)
    {
        var skip = EarmarkScaling.SurplusSkip(_proposedPattern, plan, _requestForecast().AsOfDate);
        if (skip is null)
        {
            return [];
        }

        var skips = new List<GoalHealthSkip> { new("Skip the next contribution", skip.SkipNext) };

        // Offers the stretch only when it drops more than the next contribution alone.
        if (skip.SkipStretch.Count > skip.SkipNext.Count)
        {
            skips.Add(new("Skip a stretch to clear the surplus", skip.SkipStretch));
        }

        return skips;
    }

    /// <summary>[CALC] Drops goal-health corrections that match an earlier one (same amount, schedule, and skipped dates), keeping the first, so the picker never shows two identical options.</summary>
    /// <param name="corrections">The corrections in recommendation order.</param>
    private static IReadOnlyList<GoalHealthCorrection> DedupeCorrections(IReadOnlyList<GoalHealthCorrection> corrections) =>
        corrections
            .GroupBy(correction => (
                correction.Plan.Amount,
                correction.Plan.DatePattern.ToRruleString(),
                string.Join(",", correction.Plan.DatePattern.ExcludedDates)))
            .Select(group => group.First())
            .ToList();

    /// <summary>
    /// [CALC] The goal-health question's wording: underfunded names the suggested contribution and asks to load it; overfunded asks how to handle the surplus, with the options giving the details.
    /// "" when there's no correction to offer, so the row isn't shown.
    /// </summary>
    private string DescribeGoalHealthSuggestion()
    {
        if (_goalHealthCorrections.Count > 0)
        {
            return _goalHealthCorrections.Count == 1
                ? $"This change leaves the savings plan out of step with the goal. Load a suggested contribution of {Math.Abs(_goalHealthCorrections[0].Plan.Amount):C} into the plan?"
                : "This change leaves the savings plan out of step with the goal. How do you want to handle it?";
        }

        return _goalHealthLowerRatePlan is not null
            ? "This change leaves the savings plan saving more than the goal needs. How do you want to handle it?"
            : "";
    }

    /// <summary>[CALC] The overfunded goal-health "lower the rate" option's own label, naming the reduced contribution — "" when this save doesn't leave the plan overfunded.</summary>
    private string DescribeGoalHealthLowerRateLabel() =>
        _goalHealthLowerRatePlan is { } lowerRate
            ? $"Lower the contribution to {Math.Abs(lowerRate.Amount):C} to meet the goal"
            : "";

    /// <summary>[CALC] The consequence shown under the "leave it" or "keep the rate" option: the goal falling short, or money tied up; "" when there's no suggestion.</summary>
    private string DescribeGoalHealthRejectWarning()
    {
        if (_goalHealthCorrections.Count == 0 && _goalHealthLowerRatePlan is null)
        {
            return "";
        }

        return _goalHealthUnderfunds
            ? "Left as is, the plan keeps saving less than the goal needs, so it will fall short."
            : "Left as is, the plan keeps saving more than the goal needs, tying up money it won't use.";
    }

    /// <summary>
    /// [CALC] Turns the goal-health answers into the values the plan's form opens with: the chosen correction's amount, the lowered amount, or the chosen skip's excluded dates.
    /// Null for no correction: no popup, "leave it as is", or "keep the rate" with no skip chosen.
    /// </summary>
    /// <param name="outcome">The confirmation answers.</param>
    private IReadOnlyDictionary<string, object?>? PickGoalHealthOverrides(ConfirmationOutcome outcome)
    {
        var choice = Chosen(outcome, ConfirmationRowIds.GoalHealthSuggestion);

        if (_goalHealthUnderfunds)
        {
            // Underfunded: one option per correction, then "leave it as is" (index == Count).
            return choice >= 0 && choice < _goalHealthCorrections.Count
                ? new Dictionary<string, object?> { [EarmarkFieldOverrideKeys.Amount] = _goalHealthCorrections[choice].Plan.Amount }
                : null;
        }

        if (_goalHealthLowerRatePlan is not { } lowerRate)
        {
            return null; // no overfunded advice this save
        }

        // Overfunded: option 0 lowers the rate; option 1 keeps it and asks the skip question.
        if (choice == 0)
        {
            return new Dictionary<string, object?> { [EarmarkFieldOverrideKeys.Amount] = lowerRate.Amount };
        }

        if (choice == 1)
        {
            // One option per skip choice, then "don't skip any" (index == Count).
            var skipChoice = Chosen(outcome, ConfirmationRowIds.GoalHealthSkip);
            if (skipChoice >= 0 && skipChoice < _goalHealthSkips.Count)
            {
                return new Dictionary<string, object?> { [EarmarkFieldOverrideKeys.ExcludedDates] = _goalHealthSkips[skipChoice].Dates };
            }
        }

        return null;
    }

    /// <summary>[CALC] Explains why the plans are being combined without asking, naming whether the schedule, the start date, or both changed; "" when ConsolidationNeeded is false.</summary>
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

    /// <summary>
    /// [READS FILE] Builds what ConfirmImplicitChanges needs to show the confirmation: the rows for everything the Determine* methods worked out, plus a plain description of what changed.
    /// Reads saved data because the chain previews dry-run BreakOffFactory and count the absorbed segments' plans; nothing has been saved yet at this point.
    /// </summary>
    private ImplicitChangeConfirmationRequest BuildConfirmationRequest()
    {
        // A Critical edit of an earlier segment is saved in place and cascades forward, so the break-off, consolidation, and plan-shape rows don't apply; only the chain questions do, and BuildDescription explains the case.
        var editingEarlierSegment = IsChangeCritical && _chainHasSuccessor;

        var inputs = new RowInputs
        {
            IsChangeCritical = IsChangeCritical && !editingEarlierSegment,
            PlanShapeCandidates = editingEarlierSegment ? [] : _planShapeCandidates,
            HasMultipleEarmarkPatterns = HasMultipleEarmarkPatterns && !editingEarlierSegment,
            HasExplicitEarmarkPattern = HasExplicitEarmarkPattern,
            ConsolidationNeeded = ConsolidationNeeded && !editingEarlierSegment,
            ConsolidationForcedReason = editingEarlierSegment ? "" : DescribeConsolidationForcedReason(),
            KeepSeparateFundingQuestion = editingEarlierSegment ? "" : DescribeKeepSeparateFundingQuestion(),
            TouchesChainBoundary = TouchesChainBoundary,
            ChangeCanCascade = ChangeCanCascade,
            TrivialFieldsCanCascade = TrivialFieldsCanCascade,
            SourceChangeWarning = SourceChangeWarning,
            GoalHealthSuggestionQuestion = DescribeGoalHealthSuggestion(),
            GoalHealthCorrectionLabels = _goalHealthCorrections.Select(correction => correction.Label).ToList(),
            GoalHealthLowerRateLabel = DescribeGoalHealthLowerRateLabel(),
            GoalHealthKeepRateLabel = _goalHealthLowerRatePlan is not null ? "Keep saving at this rate" : "",
            GoalHealthSkipQuestion = _goalHealthSkips.Count > 0 ? "You'll build up a surplus. Skip some upcoming contributions to use it up?" : "",
            GoalHealthSkipLabels = _goalHealthSkips.Select(skip => skip.Label).ToList(),
            GoalHealthRejectWarning = DescribeGoalHealthRejectWarning(),
            ConcerningPlanNotice = _concerningPlanNotice,
            BoundaryExtensionAnnouncement = DescribeBoundaryExtensionAnnouncement(),
            StayLinkedWarning = TouchesChainBoundary ? DescribeChainStayLinkedConsequence() : "",
            LetItBreakWarning = TouchesChainBoundary ? DescribeChainLetItBreakConsequence() : "",
            CascadeDescription = ChangeCanCascade ? DescribeChainCascadeConsequence() : "",
            TrivialFieldsCascadeDescription = TrivialFieldsCanCascade ? DescribeChainTrivialFieldsCascadeConsequence() : "",
            PacedBillsCanCascade = PacedBillsCanCascade,
            PacedBillsCascadeDescription = PacedBillsCanCascade ? DescribePacedBillsCascadeConsequence() : "",
            PacedBillsLeaveDescription = PacedBillsCanCascade ? DescribePacedBillsLeaveConsequence() : "",
            // Shown for an earlier-segment edit too, since it does cascade forward onto later segments.
            CrossBoundaryConsolidations = _crossBoundaryConsolidations
                .Select(successor => new CrossBoundaryConsolidationInput(successor.FinanceId, successor.Description ?? successor.Source))
                .ToList(),
            ShowConsolidationSizing = _consolidationStrategyApplies,
            ShowConsolidationSpread = _consolidationHasIncomeForSpread,
        };

        return new ImplicitChangeConfirmationRequest
        {
            Description = BuildDescription(),
            Rows = ConfirmationRowBuilder.BuildRows(inputs),
        };
    }

    /// <summary>
    /// [READS FILE] Previews what "stay linked" would do to the neighboring segments, for the row's warning: "" for a plain nudge, or a sentence naming each segment that would be absorbed.
    /// Absorbing a segment permanently deletes its plans and manual earmarks too, unlike in a chain of plans, so the warning says so.
    /// </summary>
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

    /// <summary>
    /// [CALC] Previews what "let the chain break" leaves behind, for the row's warning: whether a gap or an overlap forms with each neighbor (BreakOffFactory.SpansOverlap).
    /// The neighbor itself is untouched, and this pattern's own plans are handled by the truncation checks, so neither needs mentioning.
    /// </summary>
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
            // Shouldn't happen when TouchesChainBoundary is true; returns "" rather than invent a consequence.
            return "";
        }

        var sentence = string.Join("; ", consequences) + ".";
        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    /// <summary>[CALC] Names the dates this edit covers and how far an Amount or shape cascade would reach across later segments, for the row's description.</summary>
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

    /// <summary>[CALC] Names how many later segments the trivial-fields cascade (Priority, Mandatory, Description, Notes, AutoRenew) would reach; no dates, since these fields don't affect timing.</summary>
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

    /// <summary>[READS FILE] The names of the bills or goals the paycheck cascade would re-pace, for both paced-bills footers below; empty when none.</summary>
    private IReadOnlyList<string> InvalidatedPacedPlanNames()
    {
        if (_paycheckAssociationContext is not { } context)
        {
            return [];
        }

        var billsByFinanceId = _requestForecast().Book.AllFinancialPatterns().ToDictionary(pattern => pattern.FinanceId);
        return context.InvalidatedPlans
            .Select(plan => billsByFinanceId.TryGetValue(plan.FinanceId, out var bill)
                ? (string.IsNullOrWhiteSpace(bill.Description) ? bill.Source : bill.Description)
                : $"finance id {plan.FinanceId}")
            .ToList();
    }

    /// <summary>[READS FILE] The footer under the paced-bills "update them" option, naming the plans that will be re-paced to the paycheck's new schedule; "" when none.</summary>
    private string DescribePacedBillsCascadeConsequence()
    {
        var names = InvalidatedPacedPlanNames();
        return names.Count switch
        {
            0 => "",
            1 => $"\"{names[0]}\"'s savings plan will be re-paced to the paycheck's new schedule.",
            _ => $"{names.Count} savings plans will be re-paced to the paycheck's new schedule: {string.Join(", ", names.Select(name => $"\"{name}\""))}.",
        };
    }

    /// <summary>[READS FILE] The footer under the paced-bills "leave them" option, naming the plans that stay paced to the old schedule until the user edits them; "" when none.</summary>
    private string DescribePacedBillsLeaveConsequence()
    {
        var names = InvalidatedPacedPlanNames();
        return names.Count switch
        {
            0 => "",
            1 => $"\"{names[0]}\"'s savings plan stays paced to the old schedule until you edit it yourself.",
            _ => $"{names.Count} savings plans stay paced to the old schedule until you edit them yourself: {string.Join(", ", names.Select(name => $"\"{name}\""))}.",
        };
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

        // An earlier segment is edited in place, changing its past occurrences, so the message says so.
        if (IsChangeCritical && _chainHasSuccessor)
        {
            return $"You're editing an earlier segment of \"{label}\" — its own past occurrences will change to match.";
        }

        if (IsChangeCritical)
        {
            return $"You're changing the {whatChanged} for \"{label}\", and it already has payments recorded.";
        }

        // A non-Critical edit can reach the confirmation for several reasons, so this only mentions several plans when there are; the rows explain the rest.
        return HasMultipleEarmarkPatterns
            ? $"\"{label}\" already has more than one savings plan."
            : $"You're changing the {whatChanged} for \"{label}\".";
    }

    /// <summary>
    /// Where to go after saving: whether to navigate, and to which plan.
    /// A null Target means there's no plan yet, so the form proposes one; Navigate false means the user cancelled the picker.
    /// </summary>
    private readonly record struct EarmarkNavigation(bool Navigate, EarMarkPattern? Target);

    /// <summary>
    /// [READS FILE] After Save and Plan, decides which plan to open: no plan or one plan needs no popup, and more than one shows the picker (or uses the first plan when no picker is wired).
    /// A cancelled picker returns Navigate false, so the user stays where they are.
    /// Reads the repository rather than the forecast, which MainWindow may have cached before this save; looks under _navigationFinanceId, since after a break-off _financeId's plan is the ended predecessor.
    /// </summary>
    private EarmarkNavigation AskWhichEarmarkPatternToOpen()
    {
        var savingsPlan = _repositories.EarMarkPatterns.GetAll()
            .Where(pattern => pattern.FinanceId == _navigationFinanceId)
            .ToList();
        if (savingsPlan.Count <= 1)
        {
            // One plan or none: go straight there, with a null target when there's no plan yet.
            return new EarmarkNavigation(Navigate: true, Target: savingsPlan.Count == 1 ? savingsPlan[0] : null);
        }

        // No picker wired (most tests): opens the first plan.
        if (PickEarmarkPattern is not { } pick)
        {
            return new EarmarkNavigation(Navigate: true, Target: savingsPlan[0]);
        }

        // A wired picker returning null means the user cancelled — don't navigate.
        return pick(savingsPlan) is { } picked
            ? new EarmarkNavigation(Navigate: true, Target: picked)
            : new EarmarkNavigation(Navigate: false, Target: null);
    }

    /// <summary>
    /// [READS FILE] Works out the plan's "Worth a look" warning (PlanHealthMessages.CurrentJarStateLine, the same wording as the Earmark form's Summary) to show as a row in the confirmation, whether or not the user is heading to the plan form.
    /// Skipped for a current-segment break-off, which replaces the plan with a newly proposed one that meets the goal.
    /// </summary>
    private void DetermineConcerningPlanNoticeIfApplicable()
    {
        if (!ChangeWarrantsSuggestions || (IsChangeCritical && !_chainHasSuccessor))
        {
            return;
        }

        var forecast = _requestForecast();

        // No warning for a plan the user never made their own: they haven't had a chance to touch it, so it's just noise (the design rule in Run).
        if (!forecast.Book.EarMarkPatternsFor(_financeId).Any(plan => plan.ExplicitlyCreated))
        {
            return;
        }

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

    /// <summary>
    /// [WRITES FILE] Saves the edited pattern under its FinanceId, then fixes up its plans' boundaries.
    /// Saves nothing for a Critical edit of the current segment, which breaks off instead; PerformImplicitEarmarkChanges writes its ended original and new successor.
    /// </summary>
    private void PerformSave()
    {
        // A Critical edit of an earlier segment is still saved in place here.
        if (IsChangeCritical && !_chainHasSuccessor)
        {
            return;
        }

        _repositories.FinancialPatterns.Save(_proposedPattern, _accountId);
        ApplyBackTruncationsIfNeeded();
        ApplyFrontTruncationsIfNeeded();
        ApplyBoundaryExtensionsIfNeeded();
    }

    /// <summary>
    /// [WRITES FILE] Carries out what DetermineBackTruncationsIfApplicable worked out: deletes manual earmarks now after the goal's new Until, then ends each plan that runs past it (PatternTruncation.EndOn).
    /// A plan that starts after the new Until is deleted instead, since it would have nothing left.
    /// Does nothing when _backTruncations is null; called from PerformSave right after the pattern is saved.
    /// </summary>
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

    /// <summary>
    /// [WRITES FILE] The Start-side version of ApplyBackTruncationsIfNeeded: deletes manual earmarks now before the goal's new Start, then moves each plan that starts too early to the new Start (PatternTruncation.StartOn, keeping its dates), or deletes it if it ends before then.
    /// absorbedBalance is 0, since this only happens for a non-Critical future edit, so the dropped occurrences were never real money.
    /// StartOn can change the plan's DtStart, which is its saved key, so the old row is deleted first; a plan crossing both boundaries is fixed here on both ends, and ApplyBackTruncationsIfNeeded skips it.
    /// </summary>
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
                // Crosses both boundaries: ends it here too, since splitting the fix across both methods would collide on the moved key.
                clamped = PatternTruncation.EndOn(_proposedPattern, clamped, _proposedPattern.DatePattern.Until).Plan!;
            }

            _repositories.EarMarkPatterns.Delete(plan.FinanceId, plan.DatePattern.DtStart);
            _repositories.EarMarkPatterns.Save(clamped);
        }
    }

    /// <summary>
    /// [WRITES FILE] The outward version of the truncations: grows each plan that shared a boundary that moved outward, adding occurrences at its own rhythm and amount.
    /// Growing Until keeps the plan's key; growing Start (ReanchoredToStartOn) can change its DtStart key, so the old row is deleted first.
    /// Skips a plan a truncation already rewrote this save (one boundary out, the other in), so it keeps its truncated shape.
    /// </summary>
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
                    // Not a user edit, so the plan keeps its existing value.
                    ExplicitlyCreated = plan.ExplicitlyCreated,
                },
                _proposedPattern);

            if (extendsStart)
            {
                _repositories.EarMarkPatterns.Delete(plan.FinanceId, key); // ReanchoredToStartOn can move the key
            }
            _repositories.EarMarkPatterns.Save(extended);
        }
    }

    /// <summary>[CALC] Is one of the truncations already rewriting this plan this save (one boundary out, the other in)? Then ApplyBoundaryExtensionsIfNeeded leaves it alone to avoid a key collision.</summary>
    /// <param name="plan">The plan an extension is considering rewriting.</param>
    private bool IsAlreadyOwnedByATruncation(EarMarkPattern plan)
    {
        var key = plan.DatePattern.DtStart;
        return (_backTruncations is { } back && back.PlansExceedingNewUntil.Any(p => p.DatePattern.DtStart == key))
            || (_frontTruncations is { } front && front.PlansStartingBeforeNewStart.Any(p => p.DatePattern.DtStart == key));
    }

    /// <summary>
    /// [WRITES FILE] Carries out the chain answers: the boundary (stay linked via BreakOffFactory.ExtendStart/ExtendUntil, or let it break), the Amount or shape cascade, and the trivial-fields cascade.
    /// Whenever this runs, PerformSave has already saved the edited pattern in place; a current-segment break-off has no chain questions, so it never gets here.
    /// Absorbing a neighbor deletes its pattern along with all of its plans and manual earmarks, unlike in a chain of plans.
    /// </summary>
    private void PerformChainChangesIfApplicable()
    {
        if (_chainContext is not { } context || (!TouchesChainBoundary && !ChangeCanCascade && !TrivialFieldsCanCascade))
        {
            return;
        }

        var toSave = new Dictionary<int, FinancialPattern>();
        var toDelete = new List<int>();

        // The neighbor stretched to stay linked, if any: ExtendStart moves a predecessor's Until later, and ExtendUntil moves a successor's Start earlier.
        // After saving, its plans are fitted to the new span, dropped occurrences move under a predecessor (the reverse break-off), and identical plans are joined.
        FinancialPattern? extendedPredecessor = null;
        FinancialPattern? extendedSuccessor = null;

        // Each later pattern the cascade reached, before and after, so their plans can be brought in line with the new amount after saving.
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
                    extendedSuccessor = adjusted; // stretched backward to stay contiguous — its plan grows to match
                }
            }
        }

        // Breaking the chain leaves nothing to cascade onto, so the popup hides this question then; UserChoseStayLinked defaults to true, so an amount-only edit still cascades.
        if (ChangeCanCascade && UserChoseCascadeForward && UserChoseStayLinked)
        {
            // Cascades onto the successors still standing after the boundary step, including one it just moved; CascadeForward changes only Amount and shape, not dates.
            var stillStanding = successors
                .Where(pattern => !toDelete.Contains(pattern.FinanceId))
                .Select(pattern => toSave.TryGetValue(pattern.FinanceId, out var adjusted) ? adjusted : pattern)
                .ToList();

            var cascaded = BreakOffFactory.CascadeForward(_proposedPattern.DatePattern, _proposedPattern.Amount, stillStanding);
            for (var i = 0; i < cascaded.Count; i++)
            {
                // CascadeForward keeps stillStanding's order, so index i pairs each successor's before and after.
                toSave[cascaded[i].FinanceId] = cascaded[i];
                cascadeTouchedSuccessors.Add((stillStanding[i], cascaded[i]));
            }
        }

        if (TrivialFieldsCanCascade && UserChoseCascadeTrivialFields)
        {
            // Re-reads stillStanding so it builds on the Amount and shape cascade's results above.
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
            // EarMarkPatternRepository.Delete also deletes the manual earmarks; then the pattern row goes, the same order as FinancialPatternRepository.DeleteByTransferId.
            _repositories.EarMarkPatterns.Delete(financeId);
            _repositories.FinancialPatterns.Delete(financeId);
        }

        if (extendedPredecessor is { } predecessor)
        {
            FitNeighborPlansToItsNewSpan(predecessors.First(p => p.FinanceId == predecessor.FinanceId), predecessor); // grow or clamp the stretched predecessor's own plans
            PreserveDroppedOccurrencesUnderPredecessor(predecessor); // reverse-break-off
            JoinMergeableEarmarkPatterns(predecessor); // fold the stretched plan and the migrated one together when identical
        }

        if (extendedSuccessor is { } successor)
        {
            FitNeighborPlansToItsNewSpan(successors.First(s => s.FinanceId == successor.FinanceId), successor); // grow or clamp the stretched successor's own plans
            JoinMergeableEarmarkPatterns(successor);
        }

        ReconcileCascadedSuccessorSavingsPlans(cascadeTouchedSuccessors);
    }

    /// <summary>
    /// [WRITES FILE] After the cascade, brings each later pattern's plans in line with the new amount or schedule, so none keeps saving toward the old figure; choosing to cascade was the consent, so nothing here asks again.
    /// A schedule change folds the plans into one on the new dates (EarmarkConsolidation.Consolidate), since one combined plan gets the timing right and re-dating several separately doesn't.
    /// An amount-only change re-rates a single plan in place (EarmarkScaling.Scale); with several plans, the user's combine-or-keep-separate answer (_successorCombineChoices) decides.
    /// </summary>
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

            // Amount only: several plans follow the user's answer, combining or (by default) re-rating each; a single plan is just re-rated.
            if (plans.Count > 1 && _successorCombineChoices.TryGetValue(after.FinanceId, out var combine) && combine)
            {
                ConsolidateSuccessorPlans(after, plans);
                continue;
            }

            // The successor is in this chain, so AffordabilityCeilingFor already leaves its plans out of the measurement.
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

    /// <summary>[WRITES FILE] Folds every plan funding one later pattern the cascade reached into a single plan on its new schedule (EarmarkConsolidation.Consolidate), deleting the old rows and saving the replacement.</summary>
    /// <param name="goal">The later finance pattern in its carried-forward (new-schedule) form — what the folded plan is sized and validated against.</param>
    /// <param name="plans">Its existing earmark patterns, to fold into one.</param>
    private void ConsolidateSuccessorPlans(FinancialPattern goal, IReadOnlyList<EarMarkPattern> plans)
    {
        // goal is in this chain, so AffordabilityCeilingFor already leaves its plans out of the measurement.
        var consolidated = EarmarkConsolidation.Consolidate(new ConsolidationRequest
        {
            Goal = goal,
            SurvivingPlans = plans,
            ManualEarmarksForThisGoal = _repositories.ManualEarmarks.GetAll().Where(earmark => earmark.FinanceId == goal.FinanceId).ToList(),
            AllPatterns = PatternsToPaceAgainst(_repositories.FinancialPatterns.GetAll(), _repositories.FinancialPatterns.GetAccountId(goal.FinanceId) ?? _accountId),
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

    /// <summary>
    /// [WRITES FILE] The reverse break-off: when moving Start later drops contributions from this goal's plans and a predecessor has just stretched over those days, the dropped contributions move to a new plan under the predecessor instead of being lost.
    /// Only happens for a future in-place Start move, so no real money has built up and the moved plan starts at 0.
    /// Reads the original plans from _frontTruncations, and must run after the stretched predecessor is saved, since the moved plan is checked against it.
    /// </summary>
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

    /// <summary>
    /// [WRITES FILE] Fits a stretched neighbor's own plans to its new span (3.11.2.a2), as the edited goal's plans are, without asking, since it follows from "keep it linked".
    /// Where the neighbor grew, each plan sharing the moved boundary grows with it; where it shrank, each plan now outside is trimmed (PatternTruncation.EndOn / StartOn) or deleted if nothing is left.
    /// Known limit: a start moved later absorbs no balance, which undercounts a neighbor whose dropped days had already saved money.
    /// </summary>
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

    /// <summary>
    /// [WRITES FILE] The silent join for one goal: folds any two plans that can merge with no visible change (EarMarkPattern.CanJoinWithoutConsequence) into one, until none are left.
    /// Runs after a stretch or reverse break-off, which can leave two identical plans side by side; plans with a different amount or schedule stay separate.
    /// </summary>
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

        // Deletes every original row first so a folded plan can't collide with an existing key, then saves the folded set.
        foreach (var key in originalKeys)
        {
            _repositories.EarMarkPatterns.Delete(goal.FinanceId, key);
        }
        foreach (var plan in plans)
        {
            _repositories.EarMarkPatterns.Save(plan);
        }
    }

    /// <summary>
    /// [WRITES FILE] Carries out the paycheck cascade: re-proposes each plan that's no longer paced against the edited income (AllocationPlanProposer.Propose), carrying its jar balance forward and deleting manual earmarks the new plan no longer covers.
    /// Reads a fresh forecast, since it needs the income schedule as saved, whether the edit landed in place or broke off.
    /// Deletes each old plan by its old (FinanceId, Start) key before saving the new one, since a new Start would otherwise leave the old row behind.
    /// </summary>
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
            var billAccountId = _repositories.FinancialPatterns.GetAccountId(bill.FinanceId) ?? _accountId;
            var proposal = AllocationPlanProposer.Propose(bill, PatternsToPaceAgainst(allPatterns, billAccountId), forecast.AsOfDate,
                carriedOverJarBalance: carriedOverJarBalance,
                startingEarmarkCeiling: StartingEarmarkCeilingFor(bill, ChangeKind.Implicit),
                ongoingRateCeiling: OngoingRateCeilingFor(bill, ChangeKind.Implicit));

            // Must be read before this bill's plan and manual earmarks change below; the income's save doesn't affect them.
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

            // Not a user edit to that plan, so it keeps its existing value.
            _repositories.EarMarkPatterns.Save(proposal.Plan.WithExplicitlyCreated(oldPlan.ExplicitlyCreated));
            if (proposal.StartingEarmark is { } startingEarmark)
            {
                _repositories.ManualEarmarks.Save(startingEarmark);
            }
        }
    }

    /// <summary>
    /// [WRITES FILE] Carries out the break-off for a Critical edit of the current segment: one newly proposed successor (PerformSingleSuccessorBreakOff), or with several plans, one combined successor (PerformMultiPlanBreakOff) or the plans kept separate (PerformMultiPlanKeepSeparateBreakOff).
    /// Does nothing otherwise; a Critical edit of an earlier segment is saved in place and cascaded instead.
    /// </summary>
    private void PerformImplicitEarmarkChanges()
    {
        if (!IsChangeCritical || _chainHasSuccessor)
        {
            return;
        }

        // With several plans, the user's combine-or-keep-separate answer picks the break-off; a shape or start change forces combining.
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

    /// <summary>
    /// [CALC] The successor schedule every break-off uses: the edited pattern's shape and end date, starting at its next real occurrence on or after the cut (AllocationPlanProposer.AlignedSchedule), with ActiveFrom on the cut, so it keeps its days.
    /// </summary>
    /// <remarks>
    /// Starting on the cut itself would move the dates: a weekly pattern with no weekday entered, a monthly or yearly one with no day entered, or a yearly one (which has no month field) takes the cut's day or month, and an Interval > 1 counts its steps from the cut (see AlignedSchedule's remarks).
    /// </remarks>
    /// <param name="cutDate">The date the successor counts as active from.</param>
    private RecurrenceRuleOptions BuildSuccessorSchedule(DateOnly cutDate)
    {
        var reference = _proposedPattern.DatePattern;

        // BreakOffFactory.ValidateCutBoundaries checks ActiveStart, which ActiveFrom keeps on the cut.
        return AllocationPlanProposer.AlignedSchedule(reference, cutDate, reference.Until)
            ?? throw new InvalidOperationException("The pattern has no occurrence left in its own remaining window to break off onto.");
    }

    /// <summary>[READS FILE] Narrows a pattern list to the ones a plan in the given account may pace against: that account's own patterns, without transfers, the same rule MainWindow uses when proposing a plan for a new expense.</summary>
    /// <param name="patterns">The patterns to narrow.</param>
    /// <param name="accountId">The account the plan's expense is filed under.</param>
    private IReadOnlyList<FinancialPattern> PatternsToPaceAgainst(IReadOnlyList<FinancialPattern> patterns, int accountId)
    {
        var allowedFinanceIds = _repositories.FinancialPatterns.GetByAccountExcludingTransferPatterns(accountId)
            .Select(pattern => pattern.FinanceId)
            .ToHashSet();
        return patterns.Where(pattern => allowedFinanceIds.Contains(pattern.FinanceId)).ToList();
    }

    /// <summary>[READS FILE] The saved pattern being edited; every break-off path needs it, and IsChangeCritical can only be true when it exists.</summary>
    private FinancialPattern GetSavedPatternOrThrow() =>
        _repositories.FinancialPatterns.GetByFinanceId(_financeId)
            ?? throw new InvalidOperationException("Nothing to break off or correct — DetermineConditions already requires a saved pattern for IsChangeCritical to be true.");

    /// <summary>
    /// [WRITES FILE] The break-off with no plan or one plan: ends the saved pattern and its plan the day before the cut, and creates a new successor with the edited amount and schedule from the cut on, with a newly proposed plan seeded from the old jar.
    /// PerformMultiPlanBreakOff handles several plans.
    /// </summary>
    private void PerformSingleSuccessorBreakOff()
    {
        var saved = GetSavedPatternOrThrow();
        var forecast = _requestForecast();

        // The cut is today. A pattern that started today would make BreakOffFactory.BreakOff throw, but DetermineConditions treats that as a plain in-place edit, so it never gets here.
        var cutDate = forecast.AsOfDate;

        var predecessorPlan = forecast.Book.EarMarkPatternsFor(_financeId).SingleOrDefault();

        // The picked plan shape, matched to its full proposal by reference; with no pick, the affordability-capped "Recommended" candidate, so the saved plan is the one the picker showed.
        // Null only when no candidates were offered, and then BreakOffFactory proposes its own default.
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
            AllPatterns = PatternsToPaceAgainst(forecast.Book.AllFinancialPatterns(), _accountId),
            ChosenSuccessorPlan = chosenSuccessorPlan,
            // Only used when there's no candidate (no existing plan); sized against the predecessor, which shares the successor's Mandatory, priority, Source, and end date, so the tier and window come out the same.
            SuccessorStartingEarmarkCeiling = chosenSuccessorPlan is null ? CeilingOmitting(_financeId, saved, ChangeKind.Suggestion, startingEarmark: true) : null,
            SuccessorOngoingRateCeiling = chosenSuccessorPlan is null ? CeilingOmitting(_financeId, saved, ChangeKind.Suggestion, startingEarmark: false) : null,
        });

        // The successor is now the current segment, so navigation after saving looks there.
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

    /// <summary>
    /// [WRITES FILE] The break-off with several plans that are combined (forced by ConsolidationNeeded, or chosen): every plan ends the day before the cut, and the successor gets one newly proposed plan (or the chosen shape), seeded from the jar's balance.
    /// Otherwise the same as PerformSingleSuccessorBreakOff.
    /// </summary>
    private void PerformMultiPlanBreakOff()
    {
        var saved = GetSavedPatternOrThrow();
        var forecast = _requestForecast();
        var cutDate = forecast.AsOfDate;

        var predecessorPlans = forecast.Book.EarMarkPatternsFor(_financeId);

        // The same lookup as PerformSingleSuccessorBreakOff; null when no candidates were offered (plans running at the same time).
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
            AllPatterns = PatternsToPaceAgainst(forecast.Book.AllFinancialPatterns(), _accountId),
            ChosenSuccessorPlan = chosenSuccessorPlan,
            // Only used when there's no candidate; sized against the predecessor, as in PerformSingleSuccessorBreakOff.
            SuccessorStartingEarmarkCeiling = chosenSuccessorPlan is null ? CeilingOmitting(_financeId, saved, ChangeKind.Suggestion, startingEarmark: true) : null,
            SuccessorOngoingRateCeiling = chosenSuccessorPlan is null ? CeilingOmitting(_financeId, saved, ChangeKind.Suggestion, startingEarmark: false) : null,
        });

        // The successor is now the current segment, so navigation after saving looks there.
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

    /// <summary>
    /// [WRITES FILE] The break-off with several plans kept separate (the user's choice, when nothing forces combining): every plan ends the day before the cut, and the successor gets one plan per old plan, each continuing its own rate and dates.
    /// The jar's balance goes into the first successor plan; otherwise the same as PerformMultiPlanBreakOff.
    /// </summary>
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
            AllPatterns = PatternsToPaceAgainst(forecast.Book.AllFinancialPatterns(), _accountId),
        });

        // The successor is now the current segment, so navigation after saving looks there.
        _navigationFinanceId = result.Successor.FinanceId;

        // If the user accepted the funding correction, re-rates the successor plans to meet the new amount together, worked out again against the real successor; otherwise each keeps its own rate.
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

    /// <summary>[READS FILE] Reads a jar's ExpectedAmount on a chosen day from the forecast, as EarmarkFormPanel does.</summary>
    /// <param name="date">The day to read the balance as of.</param>
    /// <param name="financeId">Which fund's balance to read.</param>
    /// <returns>The jar's ExpectedAmount on that day, or 0m for a day before the forecast's own timeline starts, or a FinanceId with no jar at all.</returns>
    private decimal JarBalanceOn(DateOnly date, int financeId) => JarOn(date, financeId)?.ExpectedAmount ?? 0m;

    /// <summary>[READS FILE] Reads the whole jar on a chosen day from the forecast, for callers that also need MilestoneAmount or GlutSurplus.</summary>
    /// <param name="date">The day to read the jar as of.</param>
    /// <param name="financeId">Which fund's jar to read.</param>
    /// <returns>The jar as of that day, or null for a day before the forecast's own timeline starts, or a FinanceId with no jar at all.</returns>
    private FundJar? JarOn(DateOnly date, int financeId)
    {
        var entry = _requestForecast().GetTimeline(financeId).LastOrDefault(candidate => candidate.Date <= date);
        return entry?.Snapshot.FundJars.FirstOrDefault(candidate => candidate.FinanceId == financeId);
    }
}

/// <summary>
/// The repositories the save confirmations need, bundled into one parameter.
/// The forecast's TransactionLogBook can't write, and can't give back the user's manual earmarks on their own, since it mixes them into each day's earmark events.
/// </summary>
public sealed record FinancePatternRepositories
{
    public required FinancialPatternRepository FinancialPatterns { get; init; }
    public required EarMarkPatternRepository EarMarkPatterns { get; init; }
    public required ManualEarmarkRepository ManualEarmarks { get; init; }

    /// <summary>
    /// [READS FILE] Every manual earmark under a finance id that none of the given plans covers (RecurrenceRule.ActiveSpanContains, the same check ManualEarmark.Create uses).
    /// Used both for the real list to delete after a save and for a preview count against plans that aren't saved.
    /// </summary>
    /// <param name="finalCoverage">Every EarMarkPattern expected to exist under financeId once this save (real or hypothetical) completes.</param>
    /// <param name="financeId">Which goal's own ManualEarmarks to check.</param>
    public IReadOnlyList<DateOnly> FindOrphanedManualEarmarkDates(IReadOnlyList<EarMarkPattern> finalCoverage, int financeId) =>
        ManualEarmarks.GetAll()
            .Where(earmark => earmark.FinanceId == financeId)
            .Where(earmark => !finalCoverage.Any(plan => plan.DatePattern.ActiveSpanContains(earmark.Date)))
            .Select(earmark => earmark.Date)
            .ToList();
}

/// <summary>
/// What the confirmation popup shows: a plain description and its rows, each a question or announcement.
/// Built by BuildConfirmationRequest right before the popup opens; which rows exist is decided by RowInputs (ConfirmationRowBuilder.cs).
/// </summary>
public sealed record ImplicitChangeConfirmationRequest
{
    /// <summary>
    /// The popup's rows, most important first, built by ConfirmationRowBuilder.BuildRows; the popup shows exactly these.
    /// The plan-shape choice is one of them, a CandidatePickerRow.
    /// </summary>
    public IReadOnlyList<ConfirmationRow> Rows { get; init; } = [];

    public required string Description { get; init; }

    /// <summary>
    /// False on every page but the last, so the commit button reads "Continue…" rather than "Save".
    /// Requests are built with this true; MainWindow sets it per page when the rows span several pages.
    /// </summary>
    public bool IsFinalPage { get; init; } = true;
}
