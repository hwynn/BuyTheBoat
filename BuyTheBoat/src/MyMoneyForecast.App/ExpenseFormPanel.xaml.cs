using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// The Expense tab, built around the general 3-region form pattern this
// project uses:
//   1. Instance-information-block (top Border) — which Expense is loaded, a
//      dirty indicator, Discard/Clear controls, and a "Change..." button
//      (opens FinancialPatternPickerWindow) doubling as this form's edit
//      entry point — the same shape every form's instance-info-block uses
//      except Account's.
//   2. Characterization-field-block (first GroupBox) — Direction, Repeats?,
//      and a computed "Bill"/"Paycheck"/"Custom" label.
//   3. Everything else (second GroupBox + the Due-date/Stops/RuleEditor
//      region below it, contextual on Repeats?).
//
// TODO: the break-off-mode toggle (existing, recurring instances only)
// stays unwired. Save/Save-and-Plan button emphasis only covers half of
// what UpdateStatusIndicator's own doc comment describes — the linked
// plan's CURRENTLY-SAVED health outlines the button, but comparing the
// live-typed fields against what's saved, to catch an edit that would
// NEWLY cause a problem, is a separate, more involved check, still not
// built. "Keeps going" stays disabled (see its own note below). The
// advanced hand-built-single-occurrence escape hatch is not built — a
// one-time Expense only ever gets the plain Due-date field.
//
// Summary region (2026-08-13): the aside ("Fund jar, today") and the
// milestone/actual chart lines are wired for real now — RequestForecast
// for an existing Expense with exactly one saved plan (live pattern math
// as the fallback, same shape as EarmarkFormPanel's own UpdateSummary),
// AllocationPlanProposer.Propose for a rough live PREVIEW when no plan
// exists yet (matching what MainWindow.AutoCreateAllocationPlan would
// actually create on Save, not a guess). More than one existing
// EarMarkPattern shows the goal alone with an explanation instead of a
// chart that might not match what a forced consolidation (planning/25
// Item F) would actually produce — see UpdateSummary's own doc comment.
public partial class ExpenseFormPanel : UserControl
{
    private enum LoadedMode { NewBill, NewPattern, Editing }

    // Guards OnDirectionChanged/OnRepeatsChanged against firing while still
    // under construction — ExpenseRadioButton/RepeatingRadioButton/
    // StopOnDateRadio all have IsChecked="True" in XAML, which raises Checked
    // synchronously during InitializeComponent. Same convention as
    // RecurrenceRuleEditor's own guard.
    private bool _initialized;

    // Suppresses dirty-tracking while a Load* method is programmatically
    // setting fields — otherwise loading an instance would immediately show
    // as "unsaved changes" before the user touched anything.
    private bool _suppressEvents;

    private bool _isDirty;

    private int _financeId;
    private bool _isNew;
    private LoadedMode _loadedMode = LoadedMode.NewPattern;
    private FinancialPattern? _loadedExisting;
    private int _loadedAccountId;

    private IReadOnlyDictionary<int, int> _accountIdByFinanceId = new Dictionary<int, int>();
    private IReadOnlyList<Account> _accounts = [];
    private IReadOnlySet<int> _transferFinanceIds = new HashSet<int>();
    private IReadOnlyDictionary<int, EarMarkPattern> _patternsByFinanceId = new Dictionary<int, EarMarkPattern>();
    private IReadOnlyDictionary<int, int> _earmarkPatternCountByFinanceId = new Dictionary<int, int>();

    // planning/15/16's own "silent redirect" ruling (opening any segment of
    // a break-off/renewal chain for editing always lands on the CURRENT one)
    // — designed but never wired in until now (planning/24's own logged
    // gap). Needed here, not just at each caller, since LoadPattern below is
    // the one place both entry points (the grid's Edit button and this
    // form's own instance picker) funnel through.
    private IReadOnlyList<FinancialPattern> _allPatterns = [];

    // Computes (or returns the already-cached) live forecast on demand —
    // wired to MainWindow.EnsureForecast, which always succeeds rather than
    // requiring the user to have pressed "Forecast" first: everything
    // needed to compute one (the As-Of/Horizon pickers) already has a value
    // at all times, so there's nothing to actually wait on the user for.
    public Func<ForecastResult>? RequestForecast { get; set; }

    // (pattern, accountId, isNew, jumpToEarmark) -> did the save go through.
    // jumpToEarmark distinguishes Save and Plan from Save and Skip planning;
    // isNew tells MainWindow's callback whether to also run
    // AutoCreateAllocationPlan, matching the old split between the create and
    // edit entry points. Returns false when the user cancels the confirmation,
    // so Save can leave the form exactly as it was rather than clearing it.
    public Func<FinancialPattern, int, bool, bool, bool>? PatternSaved { get; set; }

    // Opens a picker for WHICH segment of a break-off chain to edit, shown by
    // MainWindow (ChainSegmentPickerWindow). Given every same-Source segment,
    // it returns the one the user chose. Only consulted when a chain has more
    // than one segment; null (nothing wired) or a cancelled pick falls back to
    // the current segment — the always-land-on-current behavior from before
    // this picker existed.
    public Func<IReadOnlyList<FinancialPattern>, FinancialPattern?>? PickChainSegment { get; set; }

    /// <summary>[UI] Fires whenever IsDirty or IsPopulated could have changed, so MainWindow can restyle this form's tab header live.</summary>
    public event EventHandler? StateChanged;

    public bool IsDirty => _isDirty;

    /// <summary>[CALC] Whether an existing Expense is currently loaded (not a blank new one) — the instance-information-block's own definition of "populated."</summary>
    public bool IsPopulated => _loadedExisting is not null;

    /// <summary>[CALC] Whether this form's own Advanced mode checkbox is on — a view preference, not part of what gets saved.</summary>
    public bool IsAdvancedMode => AdvancedModeCheckBox.IsChecked == true;

    public ExpenseFormPanel()
    {
        InitializeComponent();

        // Subscribed once, ever, not per-load — RuleEditor is one long-lived
        // instance now, not a fresh control each time.
        RuleEditor.ResultChanged += (_, _) => MarkDirtyIfNotSuppressed();

        _initialized = true;
    }

    /// <summary>[UI] Supplies the account/transfer/plan context this panel reads from — which account each existing pattern is currently filed under, which finance ids are transfer legs (needed to open FinancialPatternPickerWindow, alongside RequestForecast), every EarMarkPattern (so the Summary region can find this Expense's own linked plan, if it has one), and every FinancialPattern (so LoadPattern can silently redirect a stale, already-superseded segment to its own current one). Call before any Load* method, and again after every save.</summary>
    /// <param name="accountIdByFinanceId">Finance id → the account it's currently filed under.</param>
    /// <param name="accounts">Every account, to populate the account picker.</param>
    /// <param name="transferFinanceIds">Finance ids that are transfer legs — excluded from the instance picker.</param>
    /// <param name="earMarkPatterns">Every savings plan, so the Summary region can find this Expense's own linked plan.</param>
    /// <param name="allPatterns">Every FinancialPattern — LoadPattern's own BreakOffFactory.FindCurrentSegment redirect needs the full chain to search, not just the one pattern being loaded.</param>
    public void SetContext(
        IReadOnlyDictionary<int, int> accountIdByFinanceId,
        IReadOnlyList<Account> accounts,
        IReadOnlySet<int> transferFinanceIds,
        IReadOnlyList<EarMarkPattern> earMarkPatterns,
        IReadOnlyList<FinancialPattern> allPatterns)
    {
        _accountIdByFinanceId = accountIdByFinanceId;
        _accounts = accounts;
        _transferFinanceIds = transferFinanceIds;
        _allPatterns = allPatterns;

        // Grouped, not keyed straight off FinanceId — same reasoning as
        // EarmarkFormPanel's own SetContext: a goal can have more than one
        // EarMarkPattern (a concurrent second funder, or a break-off/
        // restructure chain), so this keeps each goal's most-recently-
        // started segment.
        _patternsByFinanceId = earMarkPatterns
            .GroupBy(pattern => pattern.FinanceId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(p => p.DatePattern.ActiveStart).First());

        // How MANY plans each finance id has, not just the one above —
        // UpdateSummary's own signal for "editing this could force a
        // consolidation" (planning/25's Item F), which a single-plan preview
        // can't predict the shape of.
        _earmarkPatternCountByFinanceId = earMarkPatterns
            .GroupBy(pattern => pattern.FinanceId)
            .ToDictionary(group => group.Key, group => group.Count());
    }

    public int SelectedAccountId => (int)AccountComboBox.SelectedValue;

    /// <summary>[STEP] Blank form for the "Create Bill..." shortcut — Direction and Skippable are fixed (asking would just be friction for an answer that's never anything else); Repeats defaults to Repeating but stays open, since not every bill repeats.</summary>
    public void LoadForNewBill()
    {
        _isNew = true;
        _financeId = NextFinanceId();
        _loadedMode = LoadedMode.NewBill;
        _loadedExisting = null;
        _suppressEvents = true;

        ResetSharedFields(selectedAccountId: null);

        RepeatingRadioButton.IsChecked = true;
        OneTimeRadioButton.IsChecked = false;
        UpdateRepeatsVisibility();

        ExpenseRadioButton.IsChecked = true;
        IncomeRadioButton.IsChecked = false;
        UnskippableRadioButton.IsChecked = true;
        SkippableRadioButton.IsChecked = false;
        UpdateDirectionDependentUi();
        DirectionPanel.Visibility = Visibility.Collapsed;
        MandatoryPanel.Visibility = Visibility.Collapsed;

        UpdateSelectedInstanceText();
        UpdateSummary();
        UpdateContinuityNote();
        _suppressEvents = false;
        ClearDirty();
    }

    /// <summary>[STEP] Blank form for "Add New (advanced)..." — every field open, no forced answers.</summary>
    public void LoadForNewPattern()
    {
        _isNew = true;
        _financeId = NextFinanceId();
        _loadedMode = LoadedMode.NewPattern;
        _loadedExisting = null;
        _suppressEvents = true;

        ResetSharedFields(selectedAccountId: null);

        RepeatingRadioButton.IsChecked = true;
        OneTimeRadioButton.IsChecked = false;
        UpdateRepeatsVisibility();

        ExpenseRadioButton.IsChecked = true;
        IncomeRadioButton.IsChecked = false;
        UnskippableRadioButton.IsChecked = true;
        SkippableRadioButton.IsChecked = false;
        DirectionPanel.Visibility = Visibility.Visible;
        UpdateDirectionDependentUi();

        UpdateSelectedInstanceText();
        UpdateSummary();
        UpdateContinuityNote();
        _suppressEvents = false;
        ClearDirty();
    }

    /// <summary>[CALC] Works out which segment of a break-off chain to open: the current one, or — when the chain has more than one segment and a picker is wired — whichever the user picked. A standalone pattern, an unwired picker, or a cancelled pick falls back to the current segment.</summary>
    private FinancialPattern ChooseSegmentToLoad(FinancialPattern existing)
    {
        var current = BreakOffFactory.FindCurrentSegment(existing, _allPatterns);

        var segments = _allPatterns
            .Where(pattern => pattern.Source == existing.Source)
            .Append(existing) // always a candidate, even if the caller's list omits it
            .DistinctBy(pattern => pattern.FinanceId)
            .ToList();

        // No per-segment "expired" flag exists yet (05's divergence registry —
        // Expired is permanently false today), so nothing is filtered out here;
        // once real multi-page history/expiry exists, exclude expired segments
        // at this point so they never appear as options.

        if (segments.Count <= 1 || PickChainSegment is null)
        {
            return current;
        }

        return PickChainSegment(segments) ?? current;
    }

    /// <summary>[STEP] Loads a pattern for editing — FinanceId is fixed. Also reachable from this form's own instance picker, not just the list tab's "Edit Selected." When the pattern is part of a break-off/renewal chain with more than one segment, a second popup (ChainSegmentPickerWindow, via PickChainSegment) lets the user choose WHICH segment to open — the older "always land on the current segment" redirect (planning/15/16/24) is replaced by an explicit choice, so an earlier segment can be opened on purpose (planning/28's "open the earliest segment you want changed"). A standalone pattern, an unwired picker, or a cancelled pick still loads the current segment.</summary>
    /// <param name="existing">The pattern picked to load for editing — the actual segment opened is whatever the picker returns (or the current one).</param>
    /// <param name="currentAccountId">Which account the PICKED pattern is currently filed under — re-resolved against the chosen segment's own FinanceId, on the (rare) chance a chain crosses accounts.</param>
    public void LoadPattern(FinancialPattern existing, int currentAccountId)
    {
        var current = ChooseSegmentToLoad(existing);
        if (current.FinanceId != existing.FinanceId)
        {
            currentAccountId = _accountIdByFinanceId.GetValueOrDefault(current.FinanceId, currentAccountId);
        }

        _isNew = false;
        _financeId = current.FinanceId;
        _loadedMode = LoadedMode.Editing;
        _loadedExisting = current;
        _loadedAccountId = currentAccountId;
        _suppressEvents = true;

        ResetSharedFields(selectedAccountId: currentAccountId);
        SourceTextBox.Text = current.Source;
        DescriptionTextBox.Text = current.Description;
        PriorityTextBox.Text = current.Priority.ToString();
        AmountTextBox.Text = Math.Abs(current.Amount).ToString(CultureInfo.InvariantCulture);

        DirectionPanel.Visibility = Visibility.Visible;
        ExpenseRadioButton.IsChecked = current.Amount < 0;
        IncomeRadioButton.IsChecked = current.Amount >= 0;
        UnskippableRadioButton.IsChecked = current.Mandatory;
        SkippableRadioButton.IsChecked = !current.Mandatory;

        // Same "how many occurrences" check ExpenseKind already uses to tell
        // a one-time goal from a repeating one.
        var isOneTime = current.DatePattern.GetOccurrences().Count == 1;
        OneTimeRadioButton.IsChecked = isOneTime;
        RepeatingRadioButton.IsChecked = !isOneTime;
        UpdateRepeatsVisibility();

        if (isOneTime)
        {
            DueDatePicker.SelectedDate = current.DatePattern.ActiveStart.ToDateTime(TimeOnly.MinValue);
        }
        else
        {
            RuleEditor.LoadFrom(current.DatePattern);
            StopOnDateRadio.IsChecked = true;
            StopPaidOffRadio.IsChecked = false;
            UpdateStopMode();
            RuleEditor.SetHostEndDate(current.DatePattern.Until);
            StopEndDatePicker.SelectedDate = current.DatePattern.Until.ToDateTime(TimeOnly.MinValue);
        }

        UpdateDirectionDependentUi();
        UpdateSelectedInstanceText();
        UpdateSummary();
        UpdateContinuityNote();
        _suppressEvents = false;
        ClearDirty();
    }

    /// <summary>[UI] Shared reset every Load* starts with.</summary>
    /// <param name="selectedAccountId">Which account to preselect; defaults to the first when null.</param>
    private void ResetSharedFields(int? selectedAccountId)
    {
        ErrorText.Text = string.Empty;
        SourceTextBox.Text = string.Empty;
        DescriptionTextBox.Text = string.Empty;
        AmountTextBox.Text = string.Empty;
        PriorityTextBox.Text = "5";
        DueDatePicker.SelectedDate = null;
        TotalOwedTextBox.Text = string.Empty;
        PayoffReadoutText.Text = string.Empty;
        StopEndDatePicker.SelectedDate = DateTime.Today.AddYears(3);
        PopulateAccounts(selectedAccountId);
    }

    private void PopulateAccounts(int? selectedAccountId)
    {
        AccountComboBox.ItemsSource = _accounts;
        AccountComboBox.SelectedValue = selectedAccountId ?? _accounts.FirstOrDefault()?.Id;
        if (AccountComboBox.SelectedItem is null && _accounts.Count > 0)
        {
            AccountComboBox.SelectedIndex = 0;
        }
    }

    /// <summary>[UI] Description-or-Source fallback matches GoalOption's own convention elsewhere in this app (EarmarkFormPanel) — one consistent rule for "what do we call this pattern out loud" everywhere it's shown.</summary>
    private void UpdateSelectedInstanceText() =>
        SelectedInstanceText.Text = _loadedExisting is { } existing
            ? (string.IsNullOrWhiteSpace(existing.Description) ? existing.Source : existing.Description)
            : "— New Expense —";

    /// <summary>[UI] This Expense's own linked savings plan — the same Summary region Earmark shows, present even in the plain healthy state, not just surfaced through a warning. Four cases: no savings plan exists yet (brand new, or an existing Expense never given one) shows a live PROPOSED preview built from whatever's currently typed, exactly like Save would create it (see ShowProposedPreview); income never gets one; more than one existing EarMarkPattern already funds this Expense shows the goal alone with an explanation, since editing it could force planning/25's Item F consolidation and reshape the plan in a way this preview can't predict; exactly one existing plan shows the real thing, reading live off RequestForecast when a saved PlanHealthState/FundJar reading exists yet, live pattern math otherwise (same fallback shape as EarmarkFormPanel's own UpdateSummary).</summary>
    private void UpdateSummary()
    {
        UpdateStatusIndicator();

        var typed = TryBuildPattern();

        if (_loadedExisting is not { } existing)
        {
            ShowProposedPreview(typed, isNew: true);
            return;
        }

        if (existing.Amount >= 0m)
        {
            Summary.Clear("Income doesn't need a savings plan.");
            return;
        }

        if (!_patternsByFinanceId.TryGetValue(existing.FinanceId, out var plan))
        {
            ShowProposedPreview(typed ?? existing, isNew: false);
            return;
        }

        var goalAmount = Math.Abs(existing.Amount);
        var dueDate = existing.DatePattern.Until;
        var label = string.IsNullOrWhiteSpace(existing.Description) ? existing.Source : existing.Description;
        var todayDate = DateOnly.FromDateTime(DateTime.Today);

        if (_earmarkPatternCountByFinanceId.GetValueOrDefault(existing.FinanceId) > 1)
        {
            // More than one EarMarkPattern already funds this Expense —
            // editing it goes through planning/25's Item F question, which
            // can consolidate them into a single fresh plan. A chart built
            // from just ONE of the existing plans would show a shape Save
            // might not actually produce, so show the goal alone instead of
            // guessing. See SummaryRegion.DrawChart: passing empty
            // trajectories still draws the goal/Today lines, just none of
            // the plan-progress ones.
            Summary.Load(
                $"We need {goalAmount:C0} for {label} by {dueDate:MMM d, yyyy}.",
                start: plan.DatePattern.ActiveStart,
                asOfDate: todayDate,
                dueDate: dueDate,
                startAmount: 0m,
                goalAmount: goalAmount,
                actualTrajectory: [],
                milestoneTrajectory: [],
                asideLine: "Savings plan may need to be consolidated. We don't have a certain preview.");
            return;
        }

        var narrative =
            $"We need {goalAmount:C0} for {label} by {dueDate:MMM d, yyyy}. We plan to set aside {Math.Abs(plan.Amount):C0} per occurrence, starting {plan.DatePattern.ActiveStart:MMM d, yyyy}.";

        var milestoneTrajectory = TransactionLogBookFactory.ComputeMilestoneTrajectory(
            [plan], existing, plan.DatePattern.ActiveStart, dueDate, plan.StartingAllocation);

        var forecast = RequestForecast?.Invoke();
        var health = forecast?.PlanHealthStates.FirstOrDefault(p => p.FinanceId == existing.FinanceId);
        var jar = forecast?.GetTimeline(existing.FinanceId)
            .LastOrDefault(entry => entry.Date <= todayDate)?.Snapshot.FundJars
            .FirstOrDefault(candidate => candidate.FinanceId == existing.FinanceId);

        string asideLine;
        string? asideSecondaryLine = null;
        if (jar is not null && health is not null)
        {
            asideLine = PlanHealthMessages.SummaryFutureLine(jar, health.Shortfall, health.MostImportantHealthState)
                ?? PlanHealthMessages.CurrentJarStateLine(jar, health);

            // Same fallback chain EarmarkFormPanel's own Savings-plan-mode
            // branch uses for the aside's second fact slot — the
            // first-payment warning takes priority when both apply, since a
            // payment about to fail is more time-sensitive than an ongoing
            // rate problem. Only reachable once a real health reading
            // exists — the live (no-forecast-yet) branch below has neither
            // a saved PlanHealthState to read a first-occurrence projection
            // from, nor a due-date-anchored Shortfall to test IsChronicShortfall/
            // IsChronicOverfund against.
            var isOneTime = existing.DatePattern.GetOccurrences().Count == 1;
            asideSecondaryLine = PlanHealthMessages.FirstOccurrenceShortfallLine(
                    health.IsFirstOccurrencePending, health.FirstOccurrenceShortfall, isOneTime)
                ?? PlanHealthMessages.SummaryRecurringPhrase(health);
        }
        else
        {
            var (expected, milestone) = ComputeLiveJarAmounts([plan], existing, plan.StartingAllocation, todayDate);
            asideLine = PlanHealthMessages.LiveJarStateLine(expected, milestone);
        }

        Summary.Load(
            narrative,
            start: plan.DatePattern.ActiveStart,
            asOfDate: todayDate,
            dueDate: dueDate,
            startAmount: plan.StartingAllocation,
            goalAmount: goalAmount,
            actualTrajectory: GetJarTrajectory(forecast, existing.FinanceId, dueDate),
            milestoneTrajectory: milestoneTrajectory,
            asideLine: asideLine,
            asideSecondaryLine: asideSecondaryLine);
    }

    /// <summary>[UI] Shows whether this Expense continues an earlier segment, or has since been continued by a later one — break-offs/restructures (planning/25's Item C) create a genuinely new FinanceId, so it's easy to forget, looking at just this one row, that it's part of a longer chain. BreakOffFactory.FindPredecessor/FindSuccessor do the actual lookup (the same Source-reuse mechanism FindCurrentSegment already relies on); this just surfaces what they find. Hidden entirely for a brand-new, unsaved pattern (nothing to look up yet) and whenever neither applies — a pattern with no history reads exactly as it does today, no added noise.</summary>
    private void UpdateContinuityNote()
    {
        if (_loadedExisting is not { } existing)
        {
            PredecessorNoteText.Visibility = Visibility.Collapsed;
            SuccessorNoteText.Visibility = Visibility.Collapsed;
            return;
        }

        var allPatterns = RequestForecast?.Invoke()?.Book.AllFinancialPatterns() ?? [];

        if (BreakOffFactory.FindPredecessor(existing, allPatterns) is { } predecessor)
        {
            PredecessorNoteText.Text = $"This pattern continues an earlier one, which ran through {predecessor.DatePattern.Until:MMM d, yyyy}.";
            PredecessorNoteText.Visibility = Visibility.Visible;
        }
        else
        {
            PredecessorNoteText.Visibility = Visibility.Collapsed;
        }

        if (BreakOffFactory.FindSuccessor(existing, allPatterns) is { } successor)
        {
            SuccessorNoteText.Text = $"This pattern is continued by a newer one, starting {successor.DatePattern.ActiveStart:MMM d, yyyy}.";
            SuccessorNoteText.Visibility = Visibility.Visible;
        }
        else
        {
            SuccessorNoteText.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>[UI] The no-existing-plan case of UpdateSummary: proposes a hypothetical Allocation Plan from whatever's currently typed (or, for an already-saved Expense with nothing typed yet, its saved values) via the same AllocationPlanProposer.Propose call MainWindow.AutoCreateAllocationPlan uses at real save time — so this preview can never show a shape Save wouldn't actually produce. Falls back to a plain "not enough typed yet" placeholder whenever there's nothing valid to propose from (blank amount, no due date, income, or the account context isn't available yet).</summary>
    /// <param name="pattern">The pattern to propose a plan for — null when nothing valid is typed yet.</param>
    /// <param name="isNew">Whether this Expense hasn't been saved at all yet (changes only the placeholder's own wording).</param>
    private void ShowProposedPreview(FinancialPattern? pattern, bool isNew)
    {
        if (pattern is null)
        {
            Summary.Clear(isNew
                ? "Fill in an amount and schedule to preview its savings plan."
                : "Fill in an amount to preview its savings plan.");
            return;
        }

        if (pattern.Amount >= 0m)
        {
            Summary.Clear("Income doesn't need a savings plan.");
            return;
        }

        if (TryProposePlan(pattern) is not { } proposal)
        {
            Summary.Clear(isNew
                ? "Fill in an amount and schedule to preview its savings plan."
                : "Fill in an amount to preview its savings plan.");
            return;
        }

        var goalAmount = Math.Abs(pattern.Amount);
        var dueDate = pattern.DatePattern.Until;
        var label = string.IsNullOrWhiteSpace(pattern.Description) ? pattern.Source : pattern.Description;
        var todayDate = DateOnly.FromDateTime(DateTime.Today);
        var narrative =
            $"We'd need {goalAmount:C0} for {label} by {dueDate:MMM d, yyyy}. This is a rough preview of the savings plan Save and Plan would set up — {Math.Abs(proposal.Plan.Amount):C0} per occurrence, starting {proposal.Plan.DatePattern.ActiveStart:MMM d, yyyy}.";

        var milestoneTrajectory = TransactionLogBookFactory.ComputeMilestoneTrajectory(
            [proposal.Plan], pattern, proposal.Plan.DatePattern.ActiveStart, dueDate, proposal.Plan.StartingAllocation);
        var (expected, milestone) = ComputeLiveJarAmounts([proposal.Plan], pattern, proposal.Plan.StartingAllocation, todayDate);

        Summary.Load(
            narrative,
            start: proposal.Plan.DatePattern.ActiveStart,
            asOfDate: todayDate,
            dueDate: dueDate,
            startAmount: proposal.Plan.StartingAllocation,
            goalAmount: goalAmount,
            actualTrajectory: [], // nothing saved yet — no real walked history to show
            milestoneTrajectory: milestoneTrajectory,
            asideLine: PlanHealthMessages.LiveJarStateLine(expected, milestone),
            asideSecondaryLine: "Proposed — not saved yet");
    }

    /// <summary>[CALC] Proposes a hypothetical Allocation Plan for an outflow that doesn't have one yet — the same AllocationPlanProposer.Propose call MainWindow.AutoCreateAllocationPlan makes at real save time, scoped to the same account and excluding transfer legs the same way. Null whenever RequestForecast isn't wired yet, or the proposer itself rejects the pattern (defensive only — an outflow this method's own caller already confirmed has Amount &lt; 0 shouldn't actually reach that throw).</summary>
    /// <param name="pattern">The (possibly not-yet-saved) outflow to propose a plan for.</param>
    private ProposedAllocationPlan? TryProposePlan(FinancialPattern pattern)
    {
        var forecast = RequestForecast?.Invoke();
        if (forecast is null)
        {
            return null;
        }

        var otherPatterns = forecast.Accounts
            .FirstOrDefault(account => account.AccountId == SelectedAccountId)?.Page.FinancePatterns
            .Where(p => !_transferFinanceIds.Contains(p.FinanceId))
            .ToList() ?? [];

        try
        {
            return AllocationPlanProposer.Propose(pattern, otherPatterns, DateOnly.FromDateTime(DateTime.Today));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>[CALC] A live (ExpectedAmount, MilestoneAmount) reading for today, computed purely from a plan's own schedule, no forecast required — the exact mechanism EarmarkFormPanel's own GetLiveJarAmounts uses (see that method's own comment for the "why this is safe" walkthrough); duplicated here rather than shared since the two forms' surrounding context differs enough that a shared signature would need to serve callers with genuinely different data on hand.</summary>
    /// <param name="patterns">Every EarMarkPattern funding the goal.</param>
    /// <param name="goal">The goal being funded.</param>
    /// <param name="startingTotal">What was already in the jar before this plan's own contributions began.</param>
    /// <param name="asOfDate">Today's date, to read the live amounts as of.</param>
    private static (decimal ExpectedAmount, decimal MilestoneAmount) ComputeLiveJarAmounts(
        IReadOnlyList<EarMarkPattern> patterns, FinancialPattern goal, decimal startingTotal, DateOnly asOfDate)
    {
        var activeStart = patterns.Count > 0 ? patterns.Min(p => p.DatePattern.ActiveStart) : asOfDate;
        var milestoneTrajectory = TransactionLogBookFactory.ComputeMilestoneTrajectory(patterns, goal, activeStart, asOfDate);
        var liveMilestone = milestoneTrajectory.Count > 0 ? milestoneTrajectory[^1].MilestoneAmount : 0m;
        var hasReleased = goal.DatePattern.GetOccurrences(activeStart, asOfDate).Count > 0;
        var liveExpected = hasReleased ? liveMilestone : startingTotal + liveMilestone;
        return (liveExpected, liveMilestone);
    }

    /// <summary>[CALC] The linked plan's real FundJar, walked day-by-day from today through `to` — the chart's "Fund jar (actual)" line. Same _forecast/GetTimeline lookup EarmarkFormPanel's own GetJarTrajectory uses; empty when RequestForecast isn't wired or this FinanceId hasn't reached the forecast yet.</summary>
    /// <param name="forecast">The live forecast to read from, or null.</param>
    /// <param name="financeId">Which goal's jar to walk.</param>
    /// <param name="to">The end of the range to walk through.</param>
    private static IReadOnlyList<(DateOnly Date, decimal ExpectedAmount)> GetJarTrajectory(ForecastResult? forecast, int financeId, DateOnly to)
    {
        if (forecast is null)
        {
            return [];
        }

        return forecast.GetTimeline(financeId)
            .Where(entry => entry.Date <= to)
            .Select(entry => (entry.Date, Jar: entry.Snapshot.FundJars.FirstOrDefault(candidate => candidate.FinanceId == financeId)))
            .Where(entry => entry.Jar is not null)
            .Select(entry => (entry.Date, entry.Jar!.ExpectedAmount))
            .ToList();
    }

    /// <summary>[UI] Which of the four plan-health states the linked savings plan is in, shown next to the save buttons (PlanHealthMessages.ExpenseStatusLabel does the actual mapping), plus the same outline "Save and Plan" gets when the pending edit would meaningfully affect the linked plan. Only the "linked plan is currently in a non-Healthy state" half of that trigger is built here — comparing the currently-typed fields against what's saved to catch an edit that would newly cause one of these states is a separate, more involved check, not silently assumed to be covered by this.</summary>
    private void UpdateStatusIndicator()
    {
        var health = _loadedExisting is { } existing
            ? RequestForecast?.Invoke()?.PlanHealthStates.FirstOrDefault(p => p.FinanceId == existing.FinanceId)
            : null;
        var label = health is null ? null : PlanHealthMessages.ExpenseStatusLabel(health.MostImportantHealthState);

        if (label is null)
        {
            StatusText.Visibility = Visibility.Collapsed;
            SaveAndPlanButton.ClearValue(Button.BorderBrushProperty);
            SaveAndPlanButton.ClearValue(Button.BorderThicknessProperty);
        }
        else
        {
            StatusText.Text = label;
            StatusText.Visibility = Visibility.Visible;
            SaveAndPlanButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x4F, 0x08));
            SaveAndPlanButton.BorderThickness = new Thickness(2);
        }
    }

    // Shared with FinancePatternSaveConfirmation via the same TransactionLogBook
    // method — 1 is the fallback for when RequestForecast isn't wired up yet
    // (mirrors OnChangeInstanceClick's own null guard; RequestForecast always
    // succeeds once the host has wired it, so this only matters pre-wiring).
    private int NextFinanceId() => RequestForecast?.Invoke().Book.NextFinanceId() ?? 1;

    // The instance-information-block's controls ------------------------

    private void MarkDirty()
    {
        _isDirty = true;
        UpdateModifiedIndicator();
    }

    private void ClearDirty()
    {
        _isDirty = false;
        UpdateModifiedIndicator();
    }

    /// <summary>[UI] Also gated on _initialized, not just _suppressEvents: a few fields carry XAML default values (PriorityTextBox's Text="5" is the one that actually fires) that raise their change event mid-InitializeComponent, before any Load* call has run to set _suppressEvents at all — without this, the form would read "unsaved changes" the instant it's built.</summary>
    private void MarkDirtyIfNotSuppressed()
    {
        if (_initialized && !_suppressEvents)
        {
            MarkDirty();
        }
    }

    private void UpdateModifiedIndicator()
    {
        ModifiedIndicatorText.Visibility = _isDirty ? Visibility.Visible : Visibility.Collapsed;
        DiscardChangesButton.IsEnabled = _isDirty;

        // A save button looks active only when there are unsaved changes —
        // a blank form or an unchanged loaded instance both read as
        // "nothing to save" the same way.
        SaveAndSkipPlanningButton.IsEnabled = _isDirty;
        SaveAndPlanButton.IsEnabled = _isDirty;

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool ConfirmDiscard(string message) =>
        MessageBox.Show(Window.GetWindow(this), message, "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            == MessageBoxResult.Yes;

    /// <summary>[STEP] Starts a brand new, blank instance. If unsaved edits exist, this asks for confirmation first.</summary>
    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (_isDirty && !ConfirmDiscard("Clear the form and lose your unsaved changes?"))
        {
            return;
        }

        LoadForNewPattern();
    }

    /// <summary>[STEP] No confirmation called for (unlike Clear) — the whole point of the button is discarding, so a second confirmation would be redundant.</summary>
    private void OnDiscardChangesClick(object sender, RoutedEventArgs e) => ReloadCurrentInstance();

    private void ReloadCurrentInstance()
    {
        switch (_loadedMode)
        {
            case LoadedMode.NewBill:
                LoadForNewBill();
                break;
            case LoadedMode.Editing when _loadedExisting is not null:
                LoadPattern(_loadedExisting, _loadedAccountId);
                break;
            default:
                LoadForNewPattern();
                break;
        }
    }

    /// <summary>[STEP] Opens FinancialPatternPickerWindow — the same reusable "pick one of my Bills/Paychecks/Goals" popup every form's instance-info-block uses except Account's. Its own source of truth is the live forecast's TransactionLogBook, not a plain repository read, so it sees a pattern whose occurrences fall entirely outside the forecast's own window. RequestForecast always succeeds, so there's no null/error branch to handle here at all.</summary>
    private void OnChangeInstanceClick(object sender, RoutedEventArgs e)
    {
        if (_isDirty && !ConfirmDiscard("Switch to a different Expense and lose your unsaved changes?"))
        {
            return;
        }

        if (RequestForecast is null)
        {
            return; // not wired up by the host — nothing sensible to do
        }

        var picker = new FinancialPatternPickerWindow(RequestForecast().Book, _transferFinanceIds, DateOnly.FromDateTime(DateTime.Today))
        {
            Owner = Window.GetWindow(this),
        };

        if (picker.ShowDialog() == true && picker.SelectedPattern is { } picked)
        {
            var accountId = _accountIdByFinanceId.GetValueOrDefault(picked.FinanceId, _accounts.FirstOrDefault()?.Id ?? 0);
            LoadPattern(picked, accountId);
        }
    }

    // The characterization-field-block's controls -----------------------

    private void OnDirectionChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        UpdateDirectionDependentUi();
        MarkDirtyIfNotSuppressed();
    }

    private void OnMandatoryChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        UpdateCharacterizationText();
        MarkDirtyIfNotSuppressed();
    }

    private void OnRepeatsChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        UpdateRepeatsVisibility();
        MarkDirtyIfNotSuppressed();
    }

    /// <summary>[UI] No _initialized guard needed — unlike the radios above, this checkbox has no XAML default value to fire early. Two things remain undecided beyond what's built here: exact wording for "Repeats? stops doing anything," and whether the recurrence preview should be replaced for a single-occurrence RRule. A view preference, not data: doesn't call MarkDirty.</summary>
    private void OnAdvancedModeChanged(object sender, RoutedEventArgs e) =>
        RuleEditor.SetAdvancedMode(AdvancedModeCheckBox.IsChecked == true);

    private void UpdateDirectionDependentUi()
    {
        AmountLabel.Text = ExpenseRadioButton.IsChecked == true ? "Amount owed" : "Amount received";
        UpdateSkippableVisibility();
        UpdateCharacterizationText();
    }

    /// <summary>[UI] The skippable question only means something for money going OUT, so it disappears for income entirely.</summary>
    private void UpdateSkippableVisibility() =>
        MandatoryPanel.Visibility = ExpenseRadioButton.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>[UI] Reuses names already used elsewhere in the UI's own shortcut buttons. "Create Bill..." is the only named shortcut Expense has today, so "Bill" is the only non-Custom shape recognized on the expense side; "Paycheck" is offered on the income side even though no shortcut button is named that yet, matching the domain's own bill-vs-paycheck framing. Always shows something, never blank.</summary>
    private void UpdateCharacterizationText()
    {
        CharacterizationText.Text = ExpenseRadioButton.IsChecked == true
            ? (UnskippableRadioButton.IsChecked == true ? "Bill" : "Custom")
            : "Paycheck";
    }

    private void UpdateRepeatsVisibility()
    {
        var isRepeating = RepeatingRadioButton.IsChecked == true;
        DueDatePanel.Visibility = isRepeating ? Visibility.Collapsed : Visibility.Visible;
        StopQuestionBox.Visibility = isRepeating ? Visibility.Visible : Visibility.Collapsed;
        RuleEditor.Visibility = isRepeating ? Visibility.Visible : Visibility.Collapsed;

        if (isRepeating)
        {
            RuleEditor.LetHostControlEndDate();
            UpdateStopMode();
        }
        else
        {
            RuleEditor.LetSelfControlEndDate();
        }
    }

    // Everything-else fields ---------------------------------------------

    private void OnFieldChanged(object sender, TextChangedEventArgs e) => MarkDirtyIfNotSuppressed();
    private void OnFieldChanged(object sender, SelectionChangedEventArgs e) => MarkDirtyIfNotSuppressed();

    private void OnAmountChanged(object sender, TextChangedEventArgs e)
    {
        MarkDirtyIfNotSuppressed();
        if (RepeatingRadioButton.IsChecked == true && StopPaidOffRadio.IsChecked == true)
        {
            UpdateStopEnd();
        }
    }

    // The two settled save buttons ----------------------------------------

    private void OnSaveAndSkipPlanningClick(object sender, RoutedEventArgs e) => Save(jumpToEarmark: false);

    private void OnSaveAndPlanClick(object sender, RoutedEventArgs e) => Save(jumpToEarmark: true);

    private void Save(bool jumpToEarmark)
    {
        ErrorText.Text = string.Empty;
        try
        {
            var pattern = BuildPattern();
            var accountId = SelectedAccountId;
            var isNew = _isNew;

            // The confirmation-and-consequence flow for planning/25's Items
            // B/E/F runs inside PatternSaved's own handler (MainWindow.
            // OnExpensePatternSaved constructs and runs a
            // FinancePatternSaveConfirmation) — this panel hands off the raw,
            // just-typed pattern and stays WPF/persistence-free itself.
            //
            // Only clear once the save has actually gone through. Cancelling
            // the confirmation returns false and leaves every field exactly as
            // typed, so the user resumes as if Save was never clicked. A null
            // handler (nothing wired) counts as "went through," keeping the old
            // always-clear behavior for that case. The clear happens after the
            // callback's own navigation (Forecast vs. Earmark) — clearing a
            // now-off-screen panel is fine; it just reads blank next time the
            // user lands back on this tab.
            var saved = PatternSaved?.Invoke(pattern, accountId, isNew, jumpToEarmark) ?? true;
            if (saved)
            {
                LoadForNewPattern();
            }
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    /// <summary>[CALC] Builds a FinancialPattern from the currently-typed fields — the single source both Save and the live Summary preview build from, so the preview can never show something Save wouldn't actually produce. Throws InvalidOperationException/ArgumentException on anything not yet valid; see TryBuildPattern for the non-throwing counterpart.</summary>
    private FinancialPattern BuildPattern()
    {
        var rule = BuildRecurrenceRule();

        if (!decimal.TryParse(AmountTextBox.Text, out var enteredAmount))
        {
            throw new InvalidOperationException("Amount must be a number.");
        }

        // The field is always a magnitude — Math.Abs guards against a
        // stray "-" typed out of habit turning into a double-negative.
        var magnitude = Math.Abs(enteredAmount);
        var amount = ExpenseRadioButton.IsChecked == true ? -magnitude : magnitude;

        var priority = int.TryParse(PriorityTextBox.Text, out var parsedPriority) ? parsedPriority : 0;

        return FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = _financeId,
            Source = SourceTextBox.Text,
            DatePattern = rule,
            Amount = amount,
            Priority = priority,
            // Income is never "unskippable" — the question is hidden for
            // it, so don't let a stale radio state leak into the saved pattern.
            Mandatory = ExpenseRadioButton.IsChecked == true && UnskippableRadioButton.IsChecked == true,
            Description = string.IsNullOrWhiteSpace(DescriptionTextBox.Text) ? null : DescriptionTextBox.Text,
        });
    }

    /// <summary>[CALC] The non-throwing counterpart to BuildPattern, for UpdateSummary's live preview — which recomputes on every keystroke and shouldn't surface an error for a form that's simply mid-edit (no due date picked yet, an unparsable amount, ...). Null means exactly that: not enough typed yet to know what's being proposed, not a real problem.</summary>
    private FinancialPattern? TryBuildPattern()
    {
        try
        {
            return BuildPattern();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private RecurrenceRule BuildRecurrenceRule()
    {
        if (OneTimeRadioButton.IsChecked == true)
        {
            if (DueDatePicker.SelectedDate is not { } date)
            {
                throw new InvalidOperationException("Pick a due date.");
            }

            // Same one-occurrence convention used everywhere else in this app
            // (CreateTransferWindow, OneTimeGoalFactory, AllocationPlanProposer):
            // Frequency is immaterial for Count = 1, Yearly is the standing choice.
            return RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = DateOnly.FromDateTime(date),
                Count = 1,
            });
        }

        return RuleEditor.Result ?? throw new InvalidOperationException("Fix the recurrence rule before continuing.");
    }

    // The "when does this stop?" question, shown for every repeating
    // Expense, not only the old "Create Bill" shortcut. -------------------

    // _initialized guards all three of these against StopOnDateRadio's own
    // IsChecked="True" firing Checked synchronously mid-parse: by that point
    // RepeatingRadioButton.IsChecked already reads true (its own IsChecked=
    // "True" was processed earlier in the same document), so checking that
    // property alone isn't a safe guard here — StopOnDatePanel/StopPaidOffPanel
    // are declared later in the same StackPanel and don't exist yet.
    private void OnStopModeChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        MarkDirtyIfNotSuppressed();
        if (RepeatingRadioButton.IsChecked == true)
        {
            UpdateStopMode();
        }
    }

    private void OnStopEndDatePicked(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        MarkDirtyIfNotSuppressed();
        if (RepeatingRadioButton.IsChecked == true)
        {
            UpdateStopEnd();
        }
    }

    private void OnTotalOwedChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        MarkDirtyIfNotSuppressed();
        if (RepeatingRadioButton.IsChecked == true)
        {
            UpdateStopEnd();
        }
    }

    /// <summary>[UI] Swaps in the inputs for the chosen answer — a date picker, or the loan's owed amount — and relabels the amount field so a loan's per-payment amount can't be mistaken for its balance.</summary>
    private void UpdateStopMode()
    {
        var paidOff = StopPaidOffRadio.IsChecked == true;
        StopOnDatePanel.Visibility = paidOff ? Visibility.Collapsed : Visibility.Visible;
        StopPaidOffPanel.Visibility = paidOff ? Visibility.Visible : Visibility.Collapsed;
        AmountLabel.Text = paidOff ? "Payment amount" : "Amount owed";
        UpdateStopEnd();
    }

    /// <summary>[UI] Hands the schedule editor the end date the chosen answer implies — the picked date, or the computed loan payoff date.</summary>
    private void UpdateStopEnd()
    {
        if (StopPaidOffRadio.IsChecked == true)
        {
            UpdatePayoffEnd();
            return;
        }

        PayoffReadoutText.Text = string.Empty;
        RuleEditor.SetHostEndDate(
            StopEndDatePicker.SelectedDate is { } date ? DateOnly.FromDateTime(date) : null);
    }

    /// <summary>[UI] Works out the loan's payoff date from the owed amount, the payment, and the schedule, shows it as a floor, and hands it to the editor. The owed amount is entry-only — only the resulting date is kept (W3).</summary>
    private void UpdatePayoffEnd()
    {
        try
        {
            if (!decimal.TryParse(TotalOwedTextBox.Text, out var owed) || owed <= 0)
            {
                throw new InvalidOperationException("Enter the total still owed (a positive amount).");
            }

            if (!decimal.TryParse(AmountTextBox.Text, out var payment) || Math.Abs(payment) <= 0)
            {
                throw new InvalidOperationException("Enter the regular payment amount above.");
            }

            var schedule = RuleEditor.ReadScheduleParts();
            var estimate = PayoffEstimator.Estimate(new PayoffRequest
            {
                TotalOwed = owed,
                Payment = Math.Abs(payment),
                Frequency = schedule.Frequency,
                Start = schedule.Start,
                Interval = schedule.Interval,
                ByDay = schedule.ByDay,
                ByMonthDay = schedule.ByMonthDay,
            });

            PayoffReadoutText.Text =
                $"Paid off at least by {estimate.PayoffDate:D} — {estimate.PaymentCount} payments.";
            RuleEditor.SetHostEndDate(estimate.PayoffDate);
        }
        catch (Exception ex)
        {
            PayoffReadoutText.Text = ex.Message;
            RuleEditor.SetHostEndDate(null);
        }
    }
}
