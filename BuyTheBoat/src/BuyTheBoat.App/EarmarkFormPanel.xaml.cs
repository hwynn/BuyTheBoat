using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

/// <summary>
/// The Earmark form (Allocations › Form): pick a goal (loads its savings plan if one exists), or switch into a One-off adjustment on the same goal.
/// MainWindow's only way to create or edit savings plans and manual adjustments.
/// </summary>
public partial class EarmarkFormPanel : UserControl
{
    private sealed class GoalOption(FinancialPattern pattern)
    {
        public FinancialPattern Pattern { get; } = pattern;

        public string DisplayText { get; } =
            $"{(string.IsNullOrWhiteSpace(pattern.Description) ? pattern.Source : pattern.Description)} (id {pattern.FinanceId}, {pattern.Amount:C})";
    }

    private IReadOnlyList<FinancialPattern> _goals = [];
    private IReadOnlyDictionary<int, EarMarkPattern> _patternsByFinanceId = new Dictionary<int, EarMarkPattern>();

    /// <summary>Every saved plan, grouped by goal, so Change... can offer a choice when several run at the same time.</summary>
    private ILookup<int, EarMarkPattern> _allPlansByFinanceId = Array.Empty<EarMarkPattern>().ToLookup(plan => plan.FinanceId);
    private IReadOnlyList<ManualEarmark> _existingManualEarmarks = [];
    private IReadOnlySet<int> _transferFinanceIds = new HashSet<int>();
    private ForecastResult? _forecast;
    private ManualEarmark? _oneOffEditTarget;
    private FinancialPattern? _selectedGoal;
    private bool _suppressEvents;
    private bool _isDirty;

    /// <summary>
    /// Delays the one-off live preview until typing pauses (~1.2s), since its forecast-backed jar reading is too heavy for every keystroke.
    /// The "What can I allocate today?" text and the dirty state still update on every change.
    /// </summary>
    private readonly DispatcherTimer _oneOffPreviewTimer = new() { Interval = TimeSpan.FromSeconds(1.2) };

    /// <summary>Light blue for "something already exists here, click to load it", unlike RecurrenceRuleEditor's orange, which means a warning.</summary>
    private static readonly Brush ExistingEntryBrush = new SolidColorBrush(Color.FromRgb(0xC7, 0xDD, 0xF5));

    /// <summary>No control on the form edits this, but it still flows through loading, saving, and the Summary.</summary>
    private decimal _startingAllocation;

    /// <summary>
    /// The plan's starting earmark: a manual earmark dated on the plan's ActiveStart, separate from StartingAllocation, which only a break-off sets.
    /// Edited through StartingEarmarkAmountTextBox when it's shown; deleted on Save if it's zero.
    /// </summary>
    private decimal _startingEarmarkAmount;

    /// <summary>The ActiveStart the starting earmark was loaded against, or null; Save compares it to the current ActiveStart so a moved Start doesn't leave the old earmark behind.</summary>
    private DateOnly? _loadedActiveStart;

    /// <summary>
    /// The plan's Start as saved, or null for a new plan; unlike _loadedActiveStart, this tracks Start itself, which the chain-boundary question can move.
    /// PatternSaved needs this original value to find the right row, since the plan it's handed carries the Start the form now shows.
    /// </summary>
    private DateOnly? _loadedPlanStart;

    /// <summary>The saved plan open in Savings-plan mode, or null for a new one; a goal with several plans running at the same time can have an older one open.</summary>
    private EarMarkPattern? _loadedPlan;

    /// <summary>
    /// False until the constructor finishes, because SavingsPlanRadio's IsChecked="True" in XAML fires Checked during InitializeComponent, before the other controls exist.
    /// Every handler below checks it.
    /// </summary>
    private bool _initialized;

    public EarmarkFormPanel()
    {
        InitializeComponent();

        // Amount is declared in this form's XAML, then moved into RuleEditor's left column once both exist.
        SavingsPlanPanel.Children.Remove(AmountPanel);
        RuleEditor.SetLeadingContent(AmountPanel);

        // Only the Earmark form offers skipped dates: a savings plan's schedule is the user's to arrange, unlike a bill's or paycheck's.
        RuleEditor.ShowExcludedDatesEditor();

        AmountTextBox.TextChanged += (_, _) => { if (!_suppressEvents) { UpdateSummary(); MarkDirtyIfNotSuppressed(); } };
        // Changing the cadence re-sizes the matched amount before anything reads it,
        // so a "Match the goal" plan keeps covering the goal as the schedule changes.
        RuleEditor.ResultChanged += (_, _) => { if (!_suppressEvents) { if (MatchGoalCheckBox.IsChecked == true) { RecomputeMatch(); } UpdateSummary(); MarkDirtyIfNotSuppressed(); } };

        // Runs the one-off preview once typing pauses; stops first so it fires only once.
        _oneOffPreviewTimer.Tick += (_, _) => { _oneOffPreviewTimer.Stop(); UpdateSummary(); UpdateAllocateTodayWarning(); };

        _initialized = true;
    }

    /// <summary>
    /// Saves a plan; MainWindow does the saving, since this panel has no repository. The second argument is the plan's Start as saved (see _loadedPlanStart).
    /// Returns false when the user cancels the confirmation, so SaveSavingsPlan leaves the form as it is.
    /// </summary>
    public Func<EarMarkPattern, DateOnly, bool>? PatternSaved { get; set; }
    public Action<IReadOnlyList<ManualEarmark>, IReadOnlyList<(int FinanceId, DateOnly Date)>>? ManualEarmarksSaved { get; set; }

    /// <summary>Gets the live forecast (computing it if needed), so the user never has to press another button first.</summary>
    public Func<ForecastResult>? RequestForecast { get; set; }

    /// <summary>
    /// Runs a throwaway forecast with one unsaved one-off earmark added, so the live preview reads the goal's real jar.
    /// Null when not wired; the preview then uses the saved reading.
    /// </summary>
    public Func<ManualEarmark, ForecastResult>? RequestForecastWithOneOff { get; set; }

    /// <summary>
    /// Builds an affordability-capped default plan for a goal that has none, without saving it: the same proposal "Save and Plan" would make.
    /// Picking a goal with no plan loads it as unsaved edits; null when not wired, and PopulateSavingsPlanFields falls back to a plain monthly plan.
    /// </summary>
    public Func<FinancialPattern, ProposedAllocationPlan?>? RequestProposedPlan { get; set; }

    /// <summary>
    /// Runs a throwaway forecast with an unsaved plan swapped in (the plan, the ActiveStart of the saved segment it replaces or null if new, and any proposed manual earmarks).
    /// Lets the first-payment warning read real free funds for the plan as typed; null when not wired, and the warning falls back to the set-aside gap.
    /// </summary>
    public Func<EarMarkPattern, DateOnly?, IReadOnlyList<ManualEarmark>, ForecastResult>? RequestForecastWithProposedPlan { get; set; }

    /// <summary>Fires whenever IsDirty or IsPopulated could have changed, so MainWindow can restyle this form's tab header.</summary>
    public event EventHandler? StateChanged;

    public bool IsDirty => _isDirty;

    /// <summary>A selected goal counts as this form being loaded.</summary>
    public bool IsPopulated => _selectedGoal is not null;

    /// <summary>[CALC] Finance id of the goal whose savings plan this form would save over, or null in one-off mode — lets the Allocations list mark that plan's row.</summary>
    public int? LoadedPlanFinanceId => SavingsPlanRadio.IsChecked == true ? _selectedGoal?.FinanceId : null;

    /// <summary>[CALC] The existing manual adjustment open for editing, or null when adding one or in savings-plan mode — lets the Manual adjustments list mark that row.</summary>
    public ManualEarmark? EditingOneOff => OneOffRadio.IsChecked == true ? _oneOffEditTarget : null;

    /// <summary>[CALC] Whether this form's own Advanced mode checkbox is on — a view preference, not part of what gets saved.</summary>
    public bool IsAdvancedMode => AdvancedModeCheckBox.IsChecked == true;

    /// <summary>[UI] Supplies the goals/patterns/manual-earmarks/transfer-ids/forecast this panel reads from. Call before any Load* method, and again after every save so the next load sees current data.</summary>
    /// <param name="goals">Every existing bill/paycheck/goal pattern.</param>
    /// <param name="patterns">Every existing savings plan.</param>
    /// <param name="manualEarmarks">Every existing manual (one-off) earmark.</param>
    /// <param name="transferFinanceIds">Finance ids that are transfer legs — excluded from the goal picker.</param>
    /// <param name="forecast">The live forecast, if one's been computed yet.</param>
    public void SetContext(
        IReadOnlyList<FinancialPattern> goals,
        IReadOnlyList<EarMarkPattern> patterns,
        IReadOnlyList<ManualEarmark> manualEarmarks,
        IReadOnlySet<int> transferFinanceIds,
        ForecastResult? forecast)
    {
        _goals = goals;

        // Keeps each goal's most recently started plan, since a goal can have several.
        _allPlansByFinanceId = patterns.ToLookup(pattern => pattern.FinanceId);
        _patternsByFinanceId = patterns
            .GroupBy(pattern => pattern.FinanceId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(p => p.DatePattern.ActiveStart).First());

        _existingManualEarmarks = manualEarmarks;
        _transferFinanceIds = transferFinanceIds;
        _forecast = forecast;
    }

    /// <summary>[STEP] Blank Savings-plan form, no goal selected yet — the "Add New (advanced)" entry point. Click Change... to pick one.</summary>
    public void LoadForNewPattern()
    {
        _suppressEvents = true;
        ErrorText.Text = string.Empty;
        _oneOffEditTarget = null;
        SavingsPlanRadio.IsChecked = true;

        SetGoal(null);
        AmountTextBox.Text = string.Empty;
        _startingAllocation = 0m;
        _startingEarmarkAmount = 0m;
        StartingEarmarkAmountTextBox.Text = string.Empty;
        _loadedActiveStart = null;
        _loadedPlanStart = null;
        _loadedPlan = null;

        _suppressEvents = false;
        UpdateModeVisibility();
        UpdateSummary();
        ClearDirty();
    }

    /// <summary>
    /// [STEP] Loads an existing plan for editing; the goal can't change, only the amount and timing.
    /// A suggestion's values, when passed, land on top as unsaved edits, so the form opens with the correction filled in and Save already lit.
    /// </summary>
    /// <param name="existing">The savings plan to load for editing.</param>
    /// <param name="goal">The goal it's linked to.</param>
    /// <param name="suggestedOverrides">A suggestion's field values to prefer over the saved plan (EarmarkFieldOverrideKeys), or null for a plain load.</param>
    public void LoadPattern(EarMarkPattern existing, FinancialPattern goal, IReadOnlyDictionary<string, object?>? suggestedOverrides = null)
    {
        _suppressEvents = true;
        ErrorText.Text = string.Empty;
        _oneOffEditTarget = null;
        SavingsPlanRadio.IsChecked = true;

        SetGoal(goal);

        AmountTextBox.Text = Math.Abs(existing.Amount).ToString(CultureInfo.InvariantCulture);
        _startingAllocation = existing.StartingAllocation;
        _startingEarmarkAmount = GetStartingEarmarkAmount(existing);
        StartingEarmarkAmountTextBox.Text = _startingEarmarkAmount == 0m ? string.Empty : _startingEarmarkAmount.ToString(CultureInfo.InvariantCulture);
        _loadedActiveStart = existing.DatePattern.ActiveStart;
        _loadedPlanStart = existing.DatePattern.DtStart;
        _loadedPlan = existing;
        RuleEditor.LoadFrom(existing.DatePattern);
        InferMatchToggle(goal, Math.Abs(existing.Amount));

        _suppressEvents = false;
        UpdateModeVisibility();
        UpdateSummary();
        ClearDirty();

        // Applied last with events on, so a changed value marks the form dirty as if typed; a value equal to the saved one leaves it clean.
        ApplySuggestedOverrides(suggestedOverrides);
    }

    /// <summary>
    /// [UI] Fills a suggestion's values over the just-loaded ones, with events on so a real change marks the form dirty.
    /// An unknown key or a value of the wrong type is skipped.
    /// </summary>
    /// <param name="overrides">The suggested field values to prefer, or null for a plain load.</param>
    private void ApplySuggestedOverrides(IReadOnlyDictionary<string, object?>? overrides)
    {
        if (overrides is null)
        {
            return;
        }

        if (overrides.TryGetValue(EarmarkFieldOverrideKeys.Amount, out var amountValue) && amountValue is decimal amount)
        {
            AmountTextBox.Text = Math.Abs(amount).ToString(CultureInfo.InvariantCulture);
        }

        if (overrides.TryGetValue(EarmarkFieldOverrideKeys.ExcludedDates, out var skipValue) && skipValue is IReadOnlyList<DateOnly> excludedDates)
        {
            RuleEditor.SetExcludedDates(excludedDates);
        }

        if (overrides.TryGetValue(EarmarkFieldOverrideKeys.StartingEarmarkAmount, out var startingValue) && startingValue is decimal startingAmount)
        {
            StartingEarmarkAmountTextBox.Text = startingAmount.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>[STEP] One-off adjustment mode, blank (add, with an optional goal preselected) or pre-filled (edit). A withdrawal or move exceeding what the fund holds is blocked; an add exceeding free balance warns but is allowed.</summary>
    /// <param name="editTarget">The existing manual earmark being edited, or null to add a new one.</param>
    /// <param name="initialDate">Day to pre-select for a new entry, if any.</param>
    /// <param name="preselectFinanceId">Goal to pre-select for a new entry, if any.</param>
    public void LoadOneOff(ManualEarmark? editTarget, DateOnly? initialDate = null, int? preselectFinanceId = null)
    {
        _suppressEvents = true;
        ErrorText.Text = string.Empty;
        _oneOffEditTarget = editTarget;
        _loadedPlan = null;
        OneOffRadio.IsChecked = true;

        var goalsWithPlans = _goals.Where(goal => _patternsByFinanceId.ContainsKey(goal.FinanceId)).ToList();
        var targetFinanceId = editTarget?.FinanceId ?? preselectFinanceId ?? goalsWithPlans.FirstOrDefault()?.FinanceId;
        SetGoal(goalsWithPlans.FirstOrDefault(goal => goal.FinanceId == targetFinanceId));

        TargetComboBox.ItemsSource = goalsWithPlans.Select(goal => new GoalOption(goal)).ToList();

        // Limits the calendar to the plan's span, so an out-of-range day can't be picked; must run before SelectedDate is set below.
        UpdateEarmarkDatePickerBounds();

        if (editTarget is not null)
        {
            ActionComboBox.SelectedIndex = editTarget.Amount >= 0m ? 0 : 1;
            ((ComboBoxItem)ActionComboBox.Items[2]).IsEnabled = false; // Move creates pairs, not edits
            EarmarkDatePicker.SelectedDate = editTarget.Date.ToDateTime(TimeOnly.MinValue);
            OneOffAmountTextBox.Text = Math.Abs(editTarget.Amount).ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            ActionComboBox.SelectedIndex = 0;
            ((ComboBoxItem)ActionComboBox.Items[2]).IsEnabled = true;
            EarmarkDatePicker.SelectedDate = (initialDate ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue);
            OneOffAmountTextBox.Text = string.Empty;
        }

        _suppressEvents = false;
        UpdateModeVisibility();
        UpdateOneOffInfo();
        UpdateSummary();
        UpdateAllocateTodayWarning();
        ClearDirty();
    }

    private void SetGoal(FinancialPattern? goal)
    {
        _selectedGoal = goal;
        SelectedGoalText.Text = goal is null
            ? "— No goal selected —"
            : (string.IsNullOrWhiteSpace(goal.Description) ? goal.Source : goal.Description);

        // No goal, nothing to match; with a goal, the Load* methods set the toggle once the schedule is loaded (InferMatchToggle).
        if (goal is null)
        {
            MatchGoalCheckBox.IsEnabled = false;
            MatchGoalCheckBox.IsChecked = false;
        }
        else
        {
            MatchGoalCheckBox.IsEnabled = true;
        }
    }

    /// <summary>
    /// [STEP] Opens FinancialPatternPickerWindow to pick a goal, showing every eligible pattern, not just ones with a plan.
    /// Picking one without a plan in One-off mode shows SaveOneOff's "Pick a goal with a savings plan to adjust" error.
    /// </summary>
    private void OnChangeGoalClick(object sender, RoutedEventArgs e)
    {
        if (_isDirty && !ConfirmDiscard("Switch to a different goal and lose your unsaved changes?"))
        {
            return;
        }

        if (RequestForecast is null)
        {
            return;
        }

        var picker = new FinancialPatternPickerWindow(RequestForecast().Book, _transferFinanceIds, DateOnly.FromDateTime(DateTime.Today))
        {
            Owner = Window.GetWindow(this),
        };

        if (picker.ShowDialog() != true || picker.SelectedPattern is not { } picked)
        {
            return;
        }

        // Several plans across the goal's break-off segments or running at the same time: asks which one to open, then loads it as Allocations › List › Edit Selected does.
        if (SavingsPlanRadio.IsChecked == true && ChainPlans(picked) is { Count: > 1 } chainPlans)
        {
            var planPicker = new EarmarkPatternPickerWindow(chainPlans) { Owner = Window.GetWindow(this) };
            if (planPicker.ShowDialog() == true && planPicker.SelectedPlan is { } plan)
            {
                LoadPattern(plan, _goals.FirstOrDefault(goal => goal.FinanceId == plan.FinanceId) ?? picked);
            }

            return;
        }

        SetGoal(picked);
        MarkDirtyIfNotSuppressed();

        if (SavingsPlanRadio.IsChecked == true)
        {
            PopulateSavingsPlanFields(picked);
            UpdateSummary();
        }
        else
        {
            // An open one-off belongs to its own goal, so picking another starts a new one-off there.
            if (_oneOffEditTarget is not null)
            {
                _oneOffEditTarget = null;
                ((ComboBoxItem)ActionComboBox.Items[2]).IsEnabled = true;
                OneOffAmountTextBox.Text = string.Empty;
            }

            // The form now belongs to this goal, so the Savings-plan fields follow it too.
            PopulateSavingsPlanFields(picked);
            UpdateEarmarkDatePickerBounds();
            UpdateOneOffInfo();
            UpdateSummary();
        }

        UpdateModeVisibility();
    }

    /// <summary>[CALC] The saved plan an edit would replace: the one open in Savings-plan mode, otherwise the goal's most recently started plan; null when the goal has none.</summary>
    private EarMarkPattern? OpenPlanFor(FinancialPattern goal) =>
        SavingsPlanRadio.IsChecked == true && _loadedPlan?.FinanceId == goal.FinanceId
            ? _loadedPlan
            : _patternsByFinanceId.GetValueOrDefault(goal.FinanceId);

    /// <summary>[CALC] The goal's saved plans that run at the same time as its most recently started one, that one included; empty when the goal has no plan.</summary>
    private IReadOnlyList<EarMarkPattern> ConcurrentPlans(FinancialPattern goal) =>
        _patternsByFinanceId.TryGetValue(goal.FinanceId, out var newest)
            ? _allPlansByFinanceId[goal.FinanceId].Where(plan => plan.DatePattern.ActiveSpansOverlap(newest.DatePattern)).ToList()
            : [];

    /// <summary>[CALC] Every saved plan for the goal across all its break-off segments (goals sharing its Source), oldest first.</summary>
    private IReadOnlyList<EarMarkPattern> ChainPlans(FinancialPattern goal) =>
        _goals
            .Where(segment => segment.Source == goal.Source)
            .SelectMany(segment => _allPlansByFinanceId[segment.FinanceId])
            .OrderBy(plan => plan.DatePattern.ActiveStart)
            .ToList();

    /// <summary>[CALC] The days a one-off on this goal can fall on: from the earliest start to the latest end of its plans running at the same time (ConcurrentPlans); null when the goal has no plan.</summary>
    private (DateOnly Start, DateOnly Until)? OneOffSpan(FinancialPattern goal) =>
        ConcurrentPlans(goal) is { Count: > 0 } plans
            ? (plans.Min(plan => plan.DatePattern.ActiveStart), plans.Max(plan => plan.DatePattern.Until))
            : null;

    /// <summary>
    /// [CALC] A plan whose span holds this date, for ManualEarmark.Create's check that a one-off falls inside a plan.
    /// The goal's most recently started plan when none does, so Create gives its usual out-of-span error; null when the goal has no plan.
    /// </summary>
    private EarMarkPattern? PlanCovering(FinancialPattern goal, DateOnly date) =>
        ConcurrentPlans(goal).FirstOrDefault(plan => plan.DatePattern.ActiveSpanContains(date))
            ?? _patternsByFinanceId.GetValueOrDefault(goal.FinanceId);

    /// <summary>[UI] Asks whether to discard unsaved changes; the same helper as ExpenseFormPanel's, since the panels share no base class.</summary>
    /// <param name="message">The confirmation prompt to show.</param>
    private bool ConfirmDiscard(string message) =>
        MessageBox.Show(Window.GetWindow(this), message, "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            == MessageBoxResult.Yes;

    /// <summary>[UI] Limits the one-off date picker to the days the goal's plans cover (OneOffSpan), clearing a picked date that no longer fits after the goal changes.</summary>
    private void UpdateEarmarkDatePickerBounds()
    {
        if (_selectedGoal is not { } goal || OneOffSpan(goal) is not { } span)
        {
            EarmarkDatePicker.DisplayDateStart = null;
            EarmarkDatePicker.DisplayDateEnd = null;
            return;
        }

        EarmarkDatePicker.DisplayDateStart = span.Start.ToDateTime(TimeOnly.MinValue);
        EarmarkDatePicker.DisplayDateEnd = span.Until.ToDateTime(TimeOnly.MinValue);

        if (EarmarkDatePicker.SelectedDate is { } selected
            && (selected < EarmarkDatePicker.DisplayDateStart || selected > EarmarkDatePicker.DisplayDateEnd))
        {
            EarmarkDatePicker.SelectedDate = null;
        }
    }

    private void PopulateSavingsPlanFields(FinancialPattern goal)
    {
        if (_patternsByFinanceId.TryGetValue(goal.FinanceId, out var existing))
        {
            AmountTextBox.Text = Math.Abs(existing.Amount).ToString(CultureInfo.InvariantCulture);
            _startingAllocation = existing.StartingAllocation;
            _startingEarmarkAmount = GetStartingEarmarkAmount(existing);
            StartingEarmarkAmountTextBox.Text = _startingEarmarkAmount == 0m ? string.Empty : _startingEarmarkAmount.ToString(CultureInfo.InvariantCulture);
            _loadedActiveStart = existing.DatePattern.ActiveStart;
            _loadedPlanStart = existing.DatePattern.DtStart;
            _loadedPlan = existing;
            RuleEditor.LoadFrom(existing.DatePattern);
        }
        else if (RequestProposedPlan?.Invoke(goal) is { } proposal)
        {
            // No saved plan: loads the affordability-capped plan "Save and Plan" would create, as unsaved edits.
            // _loadedActiveStart stays null, since there's no saved starting earmark to replace.
            AmountTextBox.Text = Math.Abs(proposal.Plan.Amount).ToString(CultureInfo.InvariantCulture);
            _startingAllocation = proposal.Plan.StartingAllocation;
            _startingEarmarkAmount = proposal.StartingEarmark?.Amount ?? 0m;
            StartingEarmarkAmountTextBox.Text = _startingEarmarkAmount == 0m ? string.Empty : _startingEarmarkAmount.ToString(CultureInfo.InvariantCulture);
            _loadedActiveStart = null;
            _loadedPlanStart = null;
            _loadedPlan = null;
            RuleEditor.LoadFrom(proposal.Plan.DatePattern);
        }
        else
        {
            AmountTextBox.Text = string.Empty;
            _startingAllocation = 0m;
            _startingEarmarkAmount = 0m;
            StartingEarmarkAmountTextBox.Text = string.Empty;
            _loadedActiveStart = null;
            _loadedPlanStart = null;
            _loadedPlan = null;
            // No plan and no proposer wired: resets the schedule to monthly through the goal's due date, rather than leave the last plan on screen.
            var defaultStart = DateOnly.FromDateTime(DateTime.Today);
            var defaultUntil = goal.DatePattern.Until > defaultStart ? goal.DatePattern.Until : defaultStart.AddYears(1);
            RuleEditor.LoadFrom(RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                DtStart = defaultStart,
                Until = defaultUntil,
            }));
        }

        // Turns matching on only when the loaded amount already covers the goal at this schedule, so a paced, capped, or hand-set amount isn't rewritten.
        decimal.TryParse(AmountTextBox.Text, out var loadedAmount);
        InferMatchToggle(goal, loadedAmount);
    }

    /// <summary>[CALC] The amount of the plan's starting earmark, the manual earmark dated on its ActiveStart; 0 if there isn't one.</summary>
    /// <param name="pattern">The savings plan to find the starting earmark for.</param>
    private decimal GetStartingEarmarkAmount(EarMarkPattern pattern) =>
        _existingManualEarmarks.FirstOrDefault(m => m.FinanceId == pattern.FinanceId && m.Date == pattern.DatePattern.ActiveStart)?.Amount ?? 0m;

    /// <summary>[STEP] Clears the form, asking for confirmation first if unsaved edits exist.</summary>
    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (_isDirty && !ConfirmDiscard("Clear the form and lose your unsaved changes?"))
        {
            return;
        }

        LoadForNewPattern();
    }

    /// <summary>[UI] Switching modes by hand only changes which panel shows, without reloading, so an edit in progress in the hidden panel isn't lost.</summary>
    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _suppressEvents)
        {
            return;
        }

        UpdateModeVisibility();

        // Fills in today's date when the one-off date is blank, never overwriting a loaded entry's date.
        // Events are suppressed so OnInputsChanged doesn't treat the fill-in as picking a day.
        if (OneOffRadio.IsChecked == true && EarmarkDatePicker.SelectedDate is null)
        {
            _suppressEvents = true;
            EarmarkDatePicker.SelectedDate = DateTime.Today;
            _suppressEvents = false;
        }

        // Summary sits outside the mode panels, so it needs its own refresh.
        UpdateSummary();
        MarkDirtyIfNotSuppressed();
    }

    /// <summary>[UI] Shows or hides RecurrenceRuleEditor's RRULE box; a view preference, so it doesn't mark the form dirty.</summary>
    private void OnAdvancedModeChanged(object sender, RoutedEventArgs e) =>
        RuleEditor.SetAdvancedMode(AdvancedModeCheckBox.IsChecked == true);

    /// <summary>[STEP] Switches to adding a one-off adjustment on the selected goal; not wired to any control, since the "+ Add manual earmark on this goal" button isn't in the layout yet.</summary>
    private void OnTransformToOneOffClick(object sender, RoutedEventArgs e)
    {
        if (_selectedGoal is not { } goal || !_patternsByFinanceId.ContainsKey(goal.FinanceId))
        {
            return;
        }

        LoadOneOff(editTarget: null, initialDate: DateOnly.FromDateTime(DateTime.Today), preselectFinanceId: goal.FinanceId);
    }

    private void UpdateModeVisibility()
    {
        var isOneOff = OneOffRadio.IsChecked == true;
        SavingsPlanPanel.Visibility = isOneOff ? Visibility.Collapsed : Visibility.Visible;
        OneOffPanel.Visibility = isOneOff ? Visibility.Visible : Visibility.Collapsed;

        var selectedGoalHasPlan = _selectedGoal is { } goal && _patternsByFinanceId.ContainsKey(goal.FinanceId);
        NoPatternNote.Visibility = !isOneOff && _selectedGoal is not null && !selectedGoalHasPlan
            ? Visibility.Visible
            : Visibility.Collapsed;

        UpdateGoalSpanConstraint();
    }

    /// <summary>
    /// [UI] Limits the schedule's date pickers to the selected goal's span and shows that span above the fields, so the user can't pick a plan reaching outside its goal.
    /// Savings-plan mode with a goal picked only; one-off mode limits its own date picker.
    /// </summary>
    private void UpdateGoalSpanConstraint()
    {
        if (SavingsPlanRadio.IsChecked == true && _selectedGoal is { } goal)
        {
            var activeStart = goal.DatePattern.ActiveStart;
            var until = goal.DatePattern.Until;
            RuleEditor.LimitSelectableDates(activeStart, until);
            GoalSpanText.Text = $"The plan must stay within the goal's dates: {activeStart:MMM d, yyyy} – {until:MMM d, yyyy}.";
            GoalSpanText.Visibility = Visibility.Visible;
        }
        else
        {
            RuleEditor.LimitSelectableDates(null, null);
            GoalSpanText.Visibility = Visibility.Collapsed;
        }
    }

    private bool IsMove => ActionComboBox.SelectedIndex == 2;
    private bool IsWithdraw => ActionComboBox.SelectedIndex == 1;

    private void OnInputsChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _suppressEvents || TargetRow is null)
        {
            return; // fires while InitializeComponent is mid-parse
        }

        // A date change (this handler also serves the action and amount fields) never edits the loaded entry's date, since (FinanceId, Date) is its key.
        // Picking a day with an entry loads it; picking another day starts a new entry; isSwitch is false when just moving a new entry's date.
        if (ReferenceEquals(sender, EarmarkDatePicker) && EarmarkDatePicker.SelectedDate is { } picked)
        {
            var date = DateOnly.FromDateTime(picked);
            var existing = FindExistingEntryOn(date);
            var isSwitch = existing?.Date != _oneOffEditTarget?.Date;

            if (isSwitch)
            {
                var prompt = existing is not null
                    ? "Switch to that day's existing entry and lose your unsaved changes?"
                    : "Start a new entry on this day and lose your unsaved changes?";
                if (_isDirty && !ConfirmDiscard(prompt))
                {
                    return; // leave the picker showing the clicked date; nothing else changes
                }

                if (existing is not null)
                {
                    LoadOneOff(existing);
                }
                else
                {
                    // A blank entry on the clicked date; the goal is passed so LoadOneOff doesn't fall back to the first goal with a plan.
                    LoadOneOff(editTarget: null, initialDate: date, preselectFinanceId: _selectedGoal?.FinanceId);
                }

                return;
            }
        }

        TargetRow.Visibility = IsMove ? Visibility.Visible : Visibility.Collapsed;
        UpdateOneOffInfo();
        // Summary needs refreshing too, but it runs a forecast, so it waits for typing to pause.
        ScheduleOneOffPreview();
        MarkDirtyIfNotSuppressed();
    }

    /// <summary>[UI] Restarts the one-off preview timer, so the Summary recompute runs once ~1.2s pass without another keystroke.</summary>
    private void ScheduleOneOffPreview()
    {
        _oneOffPreviewTimer.Stop();
        _oneOffPreviewTimer.Start();
    }

    /// <summary>[CALC] The selected goal's manual earmark on a date, today or later only, matching what Savings-plan mode treats as selectable.</summary>
    /// <param name="date">The date to check for an existing entry.</param>
    private ManualEarmark? FindExistingEntryOn(DateOnly date) =>
        _selectedGoal is { } goal && date >= DateOnly.FromDateTime(DateTime.Today)
            ? _existingManualEarmarks.FirstOrDefault(m => m.FinanceId == goal.FinanceId && m.Date == date)
            : null;

    /// <summary>
    /// [UI] When the date dropdown opens, colors the days that already have a manual earmark for the selected goal (the same CalendarDayButton approach as RecurrenceRuleEditor).
    /// Known rough edge: paging to another month while the dropdown is open doesn't re-mark until it's reopened.
    /// </summary>
    private void OnEarmarkDatePickerCalendarOpened(object sender, RoutedEventArgs e)
    {
        var marked = _selectedGoal is { } goal
            ? _existingManualEarmarks
                .Where(m => m.FinanceId == goal.FinanceId && m.Date >= DateOnly.FromDateTime(DateTime.Today))
                .Select(m => m.Date)
                .ToHashSet()
            : [];

        foreach (var dayButton in FindVisualChildren<CalendarDayButton>(EarmarkDatePicker))
        {
            var isMarked = dayButton.DataContext is DateTime day && marked.Contains(DateOnly.FromDateTime(day));
            if (isMarked)
            {
                dayButton.Background = ExistingEntryBrush;
                dayButton.FontWeight = FontWeights.Bold;
            }
            else if (ReferenceEquals(dayButton.Background, ExistingEntryBrush))
            {
                dayButton.ClearValue(Control.BackgroundProperty);
                dayButton.ClearValue(Control.FontWeightProperty);
            }
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>[UI] Fills the "What can I allocate today?" region for the picked goal and day from the current forecast; hidden until both are picked.</summary>
    private void UpdateOneOffInfo()
    {
        if (AllocateTodayRegion is null)
        {
            return;
        }

        if (_forecast is null || _selectedGoal is not { } goal || EarmarkDatePicker.SelectedDate is not { } selectedDate
            || _forecast.GetTimeline(goal.FinanceId) is not { Count: > 0 } timeline)
        {
            AllocateTodayRegion.Visibility = Visibility.Collapsed;
            return;
        }

        // The forecast starts today, so a one-off dated earlier reads today's figures.
        var date = DateOnly.FromDateTime(selectedDate);
        var beforeForecast = date < _forecast.AsOfDate;
        var entry = beforeForecast ? timeline[0] : timeline.Last(candidate => candidate.Date <= date);
        AllocateTodayHeader.Text = beforeForecast || date == DateOnly.FromDateTime(DateTime.Today)
            ? "What can I allocate today?"
            : $"What can I allocate on {date:MMM d, yyyy}?";

        var priorityById = _goals.ToDictionary(pattern => pattern.FinanceId, pattern => pattern.Priority);
        var sameDayEntry = _existingManualEarmarks.FirstOrDefault(m => m.FinanceId == goal.FinanceId && m.Date == date && m != _oneOffEditTarget);
        var text = AllocateTodayText(entry.Snapshot, goal, priorityById, sameDayEntry?.Amount);
        if (beforeForecast)
        {
            text = $"This one-off is dated {date:MMM d, yyyy}, before the forecast starts, so these are today's figures.{Environment.NewLine}{text}";
        }
        if (OneOffSpan(goal) is { } days)
        {
            text += $"{Environment.NewLine}This goal's {(ConcurrentPlans(goal).Count > 1 ? "plans run" : "plan runs")} {days.Start:MMM d, yyyy} – {days.Until:MMM d, yyyy}.";
        }

        AllocateTodayBody.Text = text;
        AllocateTodayRegion.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// [CALC] The "What can I allocate today?" lines for one day: free funds, what's set aside and how it splits by importance against this goal, and any one-off this goal already has that day.
    /// More or less important means a higher or lower Priority; the safety cushion is listed on its own.
    /// </summary>
    /// <param name="snapshot">The goal's account on the picked day.</param>
    /// <param name="priorityById">Each pattern's Priority, by finance id.</param>
    /// <param name="sameDayOneOff">The amount of a one-off this goal already has that day, other than the one being edited, or null.</param>
    internal static string AllocateTodayText(BalanceSnapshot snapshot, FinancialPattern goal, IReadOnlyDictionary<int, int> priorityById, decimal? sameDayOneOff)
    {
        decimal Held(Func<int, bool> include) => snapshot.FundJars
            .Where(jar => jar.FinanceId is { } id && id != goal.FinanceId && include(priorityById.GetValueOrDefault(id)))
            .Sum(jar => jar.ExpectedAmount);

        var thisGoal = snapshot.FundJars.FirstOrDefault(jar => jar.FinanceId == goal.FinanceId)?.ExpectedAmount ?? 0m;
        var cushion = snapshot.FundJars.Where(jar => jar.FinanceId is null).Sum(jar => jar.ExpectedAmount);
        var totalSetAside = snapshot.FundJars.Sum(jar => jar.ExpectedAmount);

        var lines = new List<string>
        {
            $"Free: {snapshot.ExpectedFreeAmount ?? 0m:C}",
            $"Set aside: {totalSetAside:C} — {thisGoal:C} for this goal, {Held(p => p > goal.Priority):C} for more important goals, "
                + $"{Held(p => p == goal.Priority):C} for equally important ones, {Held(p => p < goal.Priority):C} for less important ones"
                + (cushion > 0m ? $", {cushion:C} in the safety cushion." : "."),
        };

        if (sameDayOneOff is { } existing)
        {
            lines.Add($"This goal already has a one-off of {existing:C} on this day; saving adds to it.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// [UI] Warns, once typing pauses, when the typed add would leave free funds short so the forecast takes money back out of other savings (a deallocation day).
    /// Reads a what-if forecast with the one-off folded in, and only flags a shortfall the current forecast doesn't already have.
    /// </summary>
    private void UpdateAllocateTodayWarning()
    {
        AllocateTodayWarningText.Visibility = Visibility.Collapsed;
        if (OneOffRadio.IsChecked != true || IsWithdraw || IsMove || _forecast is null || _selectedGoal is not { } goal
            || OpenPlanFor(goal) is not { } plan
            || TryBuildProposedOneOff(goal, plan) is not { } proposed
            || RequestForecastWithOneOff is not { } requestWithOneOff)
        {
            return;
        }

        ForecastResult whatIf;
        try
        {
            whatIf = requestWithOneOff(proposed);
        }
        catch (ArgumentException)
        {
            return;
        }

        var alreadyShort = _forecast.GetTimeline(goal.FinanceId)
            .Where(entry => entry.Snapshot.IsDeallocationDay)
            .Select(entry => entry.Date)
            .ToHashSet();
        var newShortDay = whatIf.GetTimeline(goal.FinanceId)
            .FirstOrDefault(entry => entry.Date >= proposed.Date && entry.Snapshot.IsDeallocationDay && !alreadyShort.Contains(entry.Date));
        if (newShortDay is null)
        {
            return;
        }

        AllocateTodayWarningText.Text =
            $"This is more than free funds can cover: on {newShortDay.Date:MMM d, yyyy} the forecast would take money back out of other savings to make up the difference — the safety cushion first, then goals not marked Mandatory, least important first.";
        AllocateTodayWarningText.Visibility = Visibility.Visible;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        try
        {
            if (SavingsPlanRadio.IsChecked == true)
            {
                SaveSavingsPlan();
            }
            else
            {
                SaveOneOff();
            }
        }
        catch (OperationCanceledException)
        {
            // The user backed out of the over-commit warning — stay open.
        }
        catch (Exception ex)
        {
            ErrorText.Text = ErrorLog.RecordAndDescribe("saving this savings plan", ex);
        }
    }

    private void SaveSavingsPlan()
    {
        if (RuleEditor.Result is not { } rule)
        {
            throw new InvalidOperationException("Fix the recurrence rule before continuing.");
        }

        if (_selectedGoal is not { } selectedGoal)
        {
            throw new InvalidOperationException("Pick a goal to save toward.");
        }

        if (!decimal.TryParse(AmountTextBox.Text, out var enteredAmount))
        {
            throw new InvalidOperationException("Amount must be a number.");
        }

        // Always money into the jar, so the user types a positive amount and the sign is set here.
        var amount = -Math.Abs(enteredAmount);

        // A date typed into the box can get past the picker limits, so this catches it with a message naming the goal's dates.
        if (rule.ActiveStart < selectedGoal.DatePattern.ActiveStart || rule.Until > selectedGoal.DatePattern.Until)
        {
            throw new InvalidOperationException(
                "The plan must stay within the goal's dates: " +
                $"{selectedGoal.DatePattern.ActiveStart:MMM d, yyyy} – {selectedGoal.DatePattern.Until:MMM d, yyyy}.");
        }

        var pattern = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = selectedGoal.FinanceId,
                DatePattern = rule,
                Amount = amount,
                StartingAllocation = Math.Abs(_startingAllocation),
            },
            selectedGoal);

        // Saves the starting earmark, or deletes it when it's 0, since a $0 isolated earmark can't exist (3.13c.8.a5).
        var newActiveStart = pattern.DatePattern.ActiveStart;
        var savedEarmarks = new List<ManualEarmark>();
        var deletedEarmarks = new List<(int FinanceId, DateOnly Date)>();

        var existingAtNewStart = _existingManualEarmarks.FirstOrDefault(
            m => m.FinanceId == selectedGoal.FinanceId && m.Date == newActiveStart);
        if (_startingEarmarkAmount == 0m)
        {
            if (existingAtNewStart is not null)
            {
                deletedEarmarks.Add((selectedGoal.FinanceId, newActiveStart));
            }
            // else: zero and nothing there either — a no-op.
        }
        else
        {
            savedEarmarks.Add(ManualEarmark.Create(
                new ManualEarmarkOptions { FinanceId = selectedGoal.FinanceId, Date = newActiveStart, Amount = _startingEarmarkAmount },
                pattern));
        }

        // If the start moved, deletes the starting earmark at the old date, which would otherwise be left behind.
        if (_loadedActiveStart is { } oldActiveStart && oldActiveStart != newActiveStart)
        {
            var existingAtOldStart = _existingManualEarmarks.FirstOrDefault(
                m => m.FinanceId == selectedGoal.FinanceId && m.Date == oldActiveStart);
            if (existingAtOldStart is not null)
            {
                deletedEarmarks.Add((selectedGoal.FinanceId, oldActiveStart));
            }
        }

        if (savedEarmarks.Count > 0 || deletedEarmarks.Count > 0)
        {
            ManualEarmarksSaved?.Invoke(savedEarmarks, deletedEarmarks);
        }

        // Read before LoadForNewPattern clears _loadedPlanStart; a new plan uses its own start.
        var savedStart = _loadedPlanStart ?? pattern.DatePattern.ActiveStart;

        // Clears the form only once the save went through; cancelling leaves it as typed (as in ExpenseFormPanel.Save), and no handler counts as saved.
        if (PatternSaved?.Invoke(pattern, savedStart) ?? true)
        {
            LoadForNewPattern();
        }
    }

    /// <summary>
    /// [CALC] Checks and builds a one-off adjustment (add, withdraw, or move to another fund) for the goal picked at the top of this form, then hands it back to be saved.
    /// A withdrawal or move larger than what the fund holds is blocked (RequireFundsCover); an add larger than the free balance only warns (WarnIfOverFree).
    /// </summary>
    private void SaveOneOff()
    {
        if (_selectedGoal is not { } goal || !_patternsByFinanceId.ContainsKey(goal.FinanceId))
        {
            throw new InvalidOperationException("Pick a goal with a savings plan to adjust.");
        }

        if (EarmarkDatePicker.SelectedDate is not { } selectedDate)
        {
            throw new InvalidOperationException("Pick a day.");
        }
        var date = DateOnly.FromDateTime(selectedDate);
        var sourcePattern = PlanCovering(goal, date)!;

        if (!decimal.TryParse(OneOffAmountTextBox.Text, out var amount) || amount <= 0m)
        {
            throw new InvalidOperationException("Enter the amount as a positive number.");
        }

        var saved = new List<ManualEarmark>();
        var deleted = new List<(int, DateOnly)>();

        if (IsMove)
        {
            if (TargetComboBox.SelectedItem is not GoalOption targetGoal || targetGoal.Pattern.FinanceId == goal.FinanceId)
            {
                throw new InvalidOperationException("Pick a different fund to move to.");
            }
            if (PlanCovering(targetGoal.Pattern, date) is not { } targetPattern)
            {
                throw new InvalidOperationException("That goal has no savings plan to move funds into.");
            }

            RequireFundsCover(goal.FinanceId, date, withdrawal: amount);
            Merge(saved, deleted, sourcePattern, date, -amount);
            Merge(saved, deleted, targetPattern, date, amount);
        }
        else if (IsWithdraw)
        {
            RequireFundsCover(goal.FinanceId, date, withdrawal: amount);
            Merge(saved, deleted, sourcePattern, date, -amount);
        }
        else
        {
            WarnIfOverFree(goal.FinanceId, date, addition: amount);
            Merge(saved, deleted, sourcePattern, date, amount);
        }

        // Clears the form after saving, as SaveSavingsPlan does.
        LoadForNewPattern();
        ManualEarmarksSaved?.Invoke(saved, deleted);
    }

    private void Merge(List<ManualEarmark> saved, List<(int, DateOnly)> deleted, EarMarkPattern pattern, DateOnly date, decimal delta)
    {
        var existing = _oneOffEditTarget is not null && _oneOffEditTarget.FinanceId == pattern.FinanceId && _oneOffEditTarget.Date == date
            ? 0m // editing replaces the day's amount, not stacks onto itself
            : _existingManualEarmarks.FirstOrDefault(m => m.FinanceId == pattern.FinanceId && m.Date == date)?.Amount ?? 0m;

        var final = existing + delta;
        if (final == 0m)
        {
            deleted.Add((pattern.FinanceId, date));
            return;
        }

        saved.Add(ManualEarmark.Create(
            new ManualEarmarkOptions { FinanceId = pattern.FinanceId, Date = date, Amount = final },
            pattern));
    }

    private void RequireFundsCover(int financeId, DateOnly date, decimal withdrawal)
    {
        if (_forecast is null)
        {
            return; // no forecast on screen — the cascade floor is the backstop
        }

        var (jarBalance, _) = BalancesOn(date, financeId);
        var existing = _existingManualEarmarks.FirstOrDefault(m => m.FinanceId == financeId && m.Date == date)?.Amount ?? 0m;
        if (withdrawal - Math.Min(existing, 0m) > jarBalance)
        {
            throw new InvalidOperationException($"This fund only holds {jarBalance:C} on that day — a fund can't go below zero.");
        }
    }

    private void WarnIfOverFree(int financeId, DateOnly date, decimal addition)
    {
        if (_forecast is null)
        {
            return;
        }

        var (_, free) = BalancesOn(date, financeId);
        if (addition <= free)
        {
            return;
        }

        var proceed = MessageBox.Show(
            Window.GetWindow(this),
            $"Free balance on that day is only {free:C}. Setting aside {addition:C} over-commits the plan — " +
            "the forecast will show a negative free balance, and money may be pulled back out of " +
            "lower-priority funds to cover it. Add it anyway?",
            "More than the free balance",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (proceed != MessageBoxResult.Yes)
        {
            throw new OperationCanceledException();
        }
    }

    private (decimal JarBalance, decimal Free) BalancesOn(DateOnly date, int financeId)
    {
        if (_forecast is null)
        {
            return (0m, 0m);
        }

        // Uses the FinanceId overload, since the no-argument GetTimeline() only reads the Primary account.
        var entry = _forecast.GetTimeline(financeId).LastOrDefault(candidate => candidate.Date <= date);
        if (entry is null)
        {
            return (0m, 0m);
        }

        var jar = entry.Snapshot.FundJars.FirstOrDefault(candidate => candidate.FinanceId == financeId);
        return (jar?.ExpectedAmount ?? 0m, entry.Snapshot.ExpectedFreeAmount ?? 0m);
    }

    /// <summary>Can the Starting-point region appear? Only in Savings-plan mode, for now.</summary>
    private bool ShowFundStartPointRegion => SavingsPlanRadio.IsChecked == true;

    /// <summary>
    /// Can the user edit the starting earmark's amount? Only when the region shows and the plan's start isn't in the past.
    /// Reads the typed schedule (RuleEditor.Result), so it reacts to a new plan or a Start being edited.
    /// </summary>
    private bool UsersCanEditFundStartPoint =>
        ShowFundStartPointRegion
        && RuleEditor.Result is { } rule
        && rule.ActiveStart >= DateOnly.FromDateTime(DateTime.Today);

    /// <summary>
    /// [UI] Fills the Starting-point region with the amount and date: StartingAllocation (from a break-off) plus any manual earmark dated on the plan's ActiveStart.
    /// Whether that earmark was a starting earmark or entered by hand isn't known, so it isn't guessed (see the region's XAML comment).
    /// </summary>
    private void UpdateStartingPointRegion()
    {
        if (!ShowFundStartPointRegion)
        {
            StartingPointRegion.Visibility = Visibility.Collapsed;
            StartingShortfallWarningText.Visibility = Visibility.Collapsed;
            return;
        }

        StartingPointRegion.Visibility = Visibility.Visible;
        StartingEarmarkEditPanel.Visibility = UsersCanEditFundStartPoint ? Visibility.Visible : Visibility.Collapsed;

        // No goal or no saved plan: shows $0 with no date.
        if (_selectedGoal is not { } goal || OpenPlanFor(goal) is not { } pattern)
        {
            StartingAmountText.Text = $"{0m:C}";
            StartingDateText.Text = "no savings plan saved yet";
        }
        else
        {
            var total = GetStartingPointTotal(pattern);
            if (total <= 0m)
            {
                StartingAmountText.Text = $"{0m:C}";
                StartingDateText.Text = "savings plan started from 0";
            }
            else
            {
                StartingAmountText.Text = $"{total:C}";
                StartingDateText.Text = $"as of {pattern.DatePattern.ActiveStart:MMM d, yyyy}";
            }
        }

        // Runs for a new plan too, when the warning matters most.
        UpdateStartingShortfallWarning();
    }

    /// <summary>
    /// [UI] Shows whether the typed plan would cover the goal's first payment, using the typed fields rather than the saved plan so it's caught before Save.
    /// Builds an unsaved plan with TryBuildProposedPattern (shared with GetLiveJarAmounts) and runs the same check as the Summary's aside (TransactionLogBookFactory.FirstOccurrenceShortfall).
    /// </summary>
    private void UpdateStartingShortfallWarning()
    {
        if (_selectedGoal is not { } goal || TryBuildProposedPattern(goal) is not { } proposed)
        {
            StartingShortfallWarningText.Visibility = Visibility.Collapsed;
            return;
        }

        string? line;
        try
        {
            var asOfDate = DateOnly.FromDateTime(DateTime.Today);
            var isOneTime = goal.DatePattern.GetOccurrences().Count == 1;
            var proposedManuals = GetProposedManualEarmarks(goal, proposed);
            var payment = Math.Abs(goal.Amount);

            // Prefers a forecast with this plan swapped in, which gives the free-funds figure; otherwise falls back to the set-aside gap alone.
            var editedStart = OpenPlanFor(goal)?.DatePattern.ActiveStart;
            var health = RequestForecastWithProposedPlan is { } request
                ? request(proposed, editedStart, proposedManuals).PlanHealthStates.FirstOrDefault(state => state.FinanceId == goal.FinanceId)
                : null;

            if (health is not null)
            {
                line = PlanHealthMessages.FirstPaymentCoverageLine(
                    health.IsFirstOccurrencePending, payment, health.FirstOccurrenceShortfall,
                    health.FirstOccurrenceFreeFunds, health.FirstOccurrenceDate, isOneTime);
            }
            else
            {
                var isPending = TransactionLogBookFactory.IsFirstOccurrencePending(goal, asOfDate);
                var shortfall = TransactionLogBookFactory.FirstOccurrenceShortfall(
                    GetPatternsForLiveCheck(goal, proposed), goal, proposedManuals, asOfDate);
                line = PlanHealthMessages.FirstPaymentCoverageLine(isPending, payment, shortfall, freeFunds: null, paymentDate: null, isOneTime);
            }
        }
        catch (ArgumentException)
        {
            // ManualEarmark.Create in GetProposedManualEarmarks can still reject the typed starting earmark; the warning is then hidden.
            line = null;
        }

        if (line is null)
        {
            StartingShortfallWarningText.Visibility = Visibility.Collapsed;
            return;
        }

        StartingShortfallWarningText.Text = line;
        StartingShortfallWarningText.Visibility = Visibility.Visible;
    }

    /// <summary>[CALC] Every manual earmark, with the typed starting earmark in place of the saved one at the plan's ActiveStart, so an unsaved starting earmark counts toward the warning.</summary>
    /// <param name="goal">The goal the starting earmark is filed under.</param>
    /// <param name="proposed">The not-yet-saved savings plan being checked.</param>
    private IReadOnlyList<ManualEarmark> GetProposedManualEarmarks(FinancialPattern goal, EarMarkPattern proposed)
    {
        var activeStart = proposed.DatePattern.ActiveStart;
        var withoutOldStartingEntry = _existingManualEarmarks
            .Where(m => !(m.FinanceId == goal.FinanceId && m.Date == activeStart))
            .ToList();

        if (_startingEarmarkAmount == 0m)
        {
            return withoutOldStartingEntry;
        }

        withoutOldStartingEntry.Add(ManualEarmark.Create(
            new ManualEarmarkOptions { FinanceId = goal.FinanceId, Date = activeStart, Amount = _startingEarmarkAmount },
            proposed));
        return withoutOldStartingEntry;
    }

    /// <summary>
    /// [CALC] The manual earmark the typed one-off would save, for the live preview's forecast: positive for an add, negative on the source fund for a withdraw or move.
    /// Null when nothing valid is typed, or the date is outside the plan's span, which can happen mid-edit.
    /// </summary>
    /// <param name="goal">The goal the one-off adjustment is against.</param>
    /// <param name="plan">The goal's savings plan.</param>
    private ManualEarmark? TryBuildProposedOneOff(FinancialPattern goal, EarMarkPattern plan)
    {
        if (!decimal.TryParse(OneOffAmountTextBox.Text, out var typedAmount) || typedAmount <= 0m
            || EarmarkDatePicker.SelectedDate is not { } picked)
        {
            return null;
        }

        var signedAmount = IsWithdraw || IsMove ? -typedAmount : typedAmount;
        try
        {
            var date = DateOnly.FromDateTime(picked);
            return ManualEarmark.Create(
                new ManualEarmarkOptions { FinanceId = goal.FinanceId, Date = date, Amount = signedAmount },
                PlanCovering(goal, date) ?? plan);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>[CALC] The goal's jar on the forecast's first day (today), or null when the goal has no jar in that forecast.</summary>
    /// <param name="forecast">The forecast to read from.</param>
    /// <param name="financeId">Which goal's jar to read.</param>
    private static FundJar? JarAsOf(ForecastResult forecast, int financeId) =>
        forecast.GetTimeline(financeId)
            .Select(entry => entry.Snapshot.FundJars.FirstOrDefault(jar => jar.FinanceId == financeId))
            .FirstOrDefault(jar => jar is not null);

    /// <summary>[CALC] The One-off version of GetProposedManualEarmarks: every manual earmark, with the typed one in place of the saved one on that date.</summary>
    /// <param name="goal">The goal the one-off adjustment is against.</param>
    /// <param name="plan">The goal's savings plan.</param>
    private IReadOnlyList<ManualEarmark> GetProposedOneOffManualEarmarks(FinancialPattern goal, EarMarkPattern plan)
    {
        if (!decimal.TryParse(OneOffAmountTextBox.Text, out var typedAmount) || typedAmount <= 0m
            || EarmarkDatePicker.SelectedDate is not { } picked)
        {
            return _existingManualEarmarks;
        }

        var date = DateOnly.FromDateTime(picked);
        var withoutThisEntry = _existingManualEarmarks
            .Where(m => !(m.FinanceId == goal.FinanceId && m.Date == date))
            .ToList();

        var signedAmount = IsWithdraw || IsMove ? -typedAmount : typedAmount;
        withoutThisEntry.Add(ManualEarmark.Create(
            new ManualEarmarkOptions { FinanceId = goal.FinanceId, Date = date, Amount = signedAmount },
            PlanCovering(goal, date) ?? plan));
        return withoutThisEntry;
    }

    /// <summary>[UI] Tracks the typed starting earmark; blank or unreadable counts as 0, which is allowed here.</summary>
    private void OnStartingEarmarkAmountChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized || _suppressEvents)
        {
            return;
        }

        decimal.TryParse(StartingEarmarkAmountTextBox.Text, out var amount);
        _startingEarmarkAmount = amount;
        UpdateSummary();
        MarkDirtyIfNotSuppressed();
    }

    /// <summary>[CALC] How much was in the jar before the plan's contributions began: StartingAllocation plus any manual earmark on the plan's ActiveStart; shown in the Starting-point region and the Summary chart.</summary>
    /// <param name="pattern">The savings plan to compute the starting total for.</param>
    private decimal GetStartingPointTotal(EarMarkPattern pattern)
    {
        var activeStart = pattern.DatePattern.ActiveStart;
        var manualAtStart = _existingManualEarmarks.FirstOrDefault(m => m.FinanceId == pattern.FinanceId && m.Date == activeStart);
        return pattern.StartingAllocation + (manualAtStart?.Amount ?? 0m);
    }

    /// <summary>
    /// [UI] Rebuilds the Summary region (narrative, chart, and the two aside lines) in either mode, from a throwaway forecast with whatever's typed folded in, so the text updates live along with the chart.
    /// Falls back to the saved reading, then to plain schedule math, when no such forecast is wired.
    /// </summary>
    private void UpdateSummary()
    {
        // _forecast is already current after a save, since MainWindow recomputes it before calling SetContext; the chart's forward line follows the typed fields.
        UpdateStartingPointRegion();

        if (_selectedGoal is not { } goal)
        {
            Summary.Clear("Pick a goal above to see its savings plan summary.");
            PredecessorNoteText.Visibility = Visibility.Collapsed;
            SuccessorNoteText.Visibility = Visibility.Collapsed;
            return;
        }

        var isOneOff = OneOffRadio.IsChecked == true;

        // A goal with no saved plan still previews in Savings-plan mode, from the plan built from the typed fields; One-off mode needs a saved plan, so it shows a placeholder.
        // savedPlan (the segment Save would replace) is kept apart from plan (what the chart draws).
        var savedPlan = OpenPlanFor(goal);
        var plan = savedPlan ?? (isOneOff ? null : TryBuildProposedPattern(goal));
        if (plan is null)
        {
            Summary.Clear(isOneOff
                ? "No savings plan yet for this goal — create one in Savings-plan mode first."
                : "No savings plan yet for this goal — fill in the fields below to create one.");
            PredecessorNoteText.Visibility = Visibility.Collapsed;
            SuccessorNoteText.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateContinuityNote(goal, plan);

        var goalAmount = Math.Abs(goal.Amount);
        var dueDate = goal.DatePattern.Until;
        var label = string.IsNullOrWhiteSpace(goal.Description) ? goal.Source : goal.Description;
        var isOneTime = goal.DatePattern.GetOccurrences().Count == 1;

        // Caps the chart at 5 years for a repeating goal, so a long one like a mortgage doesn't squeeze its early activity into a sliver; the narrative still names the real due date.
        // A one-time goal's chart runs to its due date.
        var chartEnd = !isOneTime && dueDate > DateOnly.FromDateTime(DateTime.Today).AddYears(5)
            ? DateOnly.FromDateTime(DateTime.Today).AddYears(5)
            : dueDate;

        // The goal's first occurrence ever; only used when IsFirstOccurrencePending.
        var firstOccurrenceDate = goal.DatePattern.GetOccurrences(goal.DatePattern.ActiveStart, goal.DatePattern.Until).FirstOrDefault();

        var health = _forecast?.PlanHealthStates.FirstOrDefault(p => p.FinanceId == goal.FinanceId);

        // The chart's actual-amount line, walked day by day from the forecast.
        var trajectory = GetJarTrajectory(goal.FinanceId, chartEnd);
        var jar = trajectory.Count > 0 ? trajectory[0].Jar : null;

        // Every saved plan for this goal, only used to detect a plan running at the same time; the chart's forward line comes from the typed fields.
        var patternsForMilestone = _forecast?.Accounts
            .SelectMany(account => account.Page.EarmarkPatterns)
            .Where(p => p.FinanceId == goal.FinanceId)
            .ToList() ?? [];

        string narrative;
        string jarStateLine;
        string? trajectoryLine = null;
        string? firstPaymentLine = null;
        DateOnly? highlightDate = null;
        decimal? additionAmount = null;
        IReadOnlyList<(DateOnly Date, decimal Amount)> proposedTrajectory = [];

        if (isOneOff)
        {
            narrative = GoalNarrativeOpening(goal, goalAmount, label, dueDate, isOneTime, DateOnly.FromDateTime(DateTime.Today));
            if (decimal.TryParse(OneOffAmountTextBox.Text, out var proposedAmount) && proposedAmount > 0m)
            {
                var verb = IsMove ? "moving" : IsWithdraw ? "withdrawing" : "adding";
                var dateText = EarmarkDatePicker.SelectedDate is { } picked
                    ? $" on {DateOnly.FromDateTime(picked):MMM d, yyyy}"
                    : string.Empty;
                narrative += $" This one-off adjustment is {verb} {proposedAmount:C0}{dateText}.";
            }

            var todayDate = DateOnly.FromDateTime(DateTime.Today);

            // Reads today's jar from a forecast with the typed one-off added, so a backdated one-off that outlasts a release shows correctly.
            // Falls back to the saved reading when nothing valid is typed or no such forecast is wired; MilestoneAmount never changes, since it only counts scheduled contributions.
            try
            {
                var savedExpected = jar?.ExpectedAmount
                    ?? GetLiveJarAmounts(GetPatternsForLiveCheck(goal, plan), plan, goal, GetStartingPointTotal(plan), todayDate).ExpectedAmount;

                if (TryBuildProposedOneOff(goal, plan) is { } proposedOneOff
                    && RequestForecastWithOneOff is { } requestWithOneOff
                    && JarAsOf(requestWithOneOff(proposedOneOff), goal.FinanceId) is { } proposedJar)
                {
                    var addition = proposedJar.ExpectedAmount - savedExpected;
                    additionAmount = addition == 0m ? null : addition; // null rather than 0, since a $0 line would just retrace Actual
                    jarStateLine = PlanHealthMessages.JarStateLine(proposedJar.ExpectedAmount, proposedJar.MilestoneAmount ?? 0m);
                }
                else if (jar is not null && health is not null)
                {
                    jarStateLine = PlanHealthMessages.JarStateLine(jar.ExpectedAmount, jar.MilestoneAmount ?? 0m);
                }
                else
                {
                    var live = GetLiveJarAmounts(GetPatternsForLiveCheck(goal, plan), plan, goal, GetStartingPointTotal(plan), todayDate);
                    jarStateLine = PlanHealthMessages.JarStateLine(live.ExpectedAmount, live.MilestoneAmount);
                }
            }
            catch (ArgumentException)
            {
                // A date the picker's bounds should already exclude, mid-edit.
                jarStateLine = "(fund jar state needs a live forecast — not available yet)";
            }

            // The first-payment warning reads the same forecast (or the saved health), falling back to the set-aside gap alone.
            try
            {
                var payment = Math.Abs(goal.Amount);
                var warningHealth = TryBuildProposedOneOff(goal, plan) is { } proposedOneOff && RequestForecastWithOneOff is { } requestOneOff
                    ? requestOneOff(proposedOneOff).PlanHealthStates.FirstOrDefault(state => state.FinanceId == goal.FinanceId)
                    : health;

                if (warningHealth is { } wh)
                {
                    firstPaymentLine = PlanHealthMessages.FirstPaymentCoverageLine(
                        wh.IsFirstOccurrencePending, payment, wh.FirstOccurrenceShortfall,
                        wh.FirstOccurrenceFreeFunds, wh.FirstOccurrenceDate, isOneTime);
                    highlightDate = wh.IsFirstOccurrencePending && wh.FirstOccurrenceShortfall > 0m ? firstOccurrenceDate : null;
                }
                else
                {
                    var isPending = TransactionLogBookFactory.IsFirstOccurrencePending(goal, todayDate);
                    var shortfall = TransactionLogBookFactory.FirstOccurrenceShortfall(
                        GetPatternsForLiveCheck(goal, plan), goal, GetProposedOneOffManualEarmarks(goal, plan), todayDate);
                    firstPaymentLine = PlanHealthMessages.FirstPaymentCoverageLine(isPending, payment, shortfall, freeFunds: null, paymentDate: null, isOneTime);
                    highlightDate = isPending && shortfall > 0m ? firstOccurrenceDate : null;
                }
            }
            catch (ArgumentException)
            {
                firstPaymentLine = null;
            }
        }
        else
        {
            decimal.TryParse(AmountTextBox.Text, out var enteredAmount);
            var start = RuleEditor.Result?.ActiveStart ?? DateOnly.FromDateTime(DateTime.Today);
            var opening = GoalNarrativeOpening(goal, goalAmount, label, dueDate, isOneTime, DateOnly.FromDateTime(DateTime.Today));

            // For a repeating goal, names the plan's schedule rather than the goal's, since a monthly bill can be funded biweekly.
            string continuation;
            if (!isOneTime && RuleEditor.Result is { } contributionRule)
            {
                var contributionCadence = ContributionCadencePhrase(contributionRule.Frequency, contributionRule.Interval);
                continuation = $"We plan to set aside {enteredAmount:C0} {contributionCadence} toward it.";
            }
            else
            {
                continuation = $"We plan to set aside {enteredAmount:C0} per occurrence, starting {start:MMM d, yyyy}.";
            }

            var contributionOccurrenceCount = RuleEditor.Result?.GetOccurrences().Count ?? 1;
            narrative = PlanHealthMessages.IsPaused(enteredAmount, contributionOccurrenceCount)
                ? $"{opening} {PlanHealthMessages.PausedFundingSentence}"
                : enteredAmount > 0m ? $"{opening} {continuation}" : opening;

            // The chart's "proposed" line: the projected jar balance rather than the milestone, so a starting amount or glut carries past each release instead of resetting.
            if (TryBuildProposedPattern(goal) is { } proposedForChart)
            {
                var proposedStartingTotal = Math.Abs(_startingAllocation) + _startingEarmarkAmount;

                // The saved starting earmark sits on the plan's start and is already in proposedStartingTotal as the typed amount.
                var laterManualEarmarks = _existingManualEarmarks
                    .Where(manual => manual.Date != plan.DatePattern.ActiveStart)
                    .ToList();
                proposedTrajectory = TransactionLogBookFactory.ComputeExpectedTrajectory(
                        GetPatternsForLiveCheck(goal, proposedForChart), goal, plan.DatePattern.ActiveStart, chartEnd, proposedStartingTotal, laterManualEarmarks)
                    .Select(p => (p.Date, p.ExpectedAmount))
                    .ToList();
            }

            // Reads the goal's health and jar from a forecast with the typed plan swapped in, so the aside text updates as you type; this matters most for a mandatory bill, which already has a health reading.
            // Falls back to the saved health, then to plain schedule math.
            var liveHealth = health;
            var liveJar = jar;
            try
            {
                if (TryBuildProposedPattern(goal) is { } proposedForAside && RequestForecastWithProposedPlan is { } requestAside)
                {
                    // The segment being replaced is the saved plan's, or null for a new plan.
                    var liveForecast = requestAside(proposedForAside, savedPlan?.DatePattern.ActiveStart, GetProposedManualEarmarks(goal, proposedForAside));
                    liveHealth = liveForecast.PlanHealthStates.FirstOrDefault(state => state.FinanceId == goal.FinanceId) ?? health;
                    liveJar = JarAsOf(liveForecast, goal.FinanceId) ?? jar;
                }
            }
            catch (ArgumentException)
            {
                // The typed values can't be built mid-edit, so this falls back to the saved reading.
                liveHealth = health;
                liveJar = jar;
            }

            if (liveJar is not null && liveHealth is not null)
            {
                // "Fund jar, today" and "Toward the goal" lines.
                jarStateLine = PlanHealthMessages.JarStateLine(liveJar.ExpectedAmount, liveJar.MilestoneAmount ?? 0m);
                trajectoryLine = PlanHealthMessages.SummaryFutureLine(liveJar, liveHealth.Shortfall, liveHealth.MostImportantHealthState)
                    ?? PlanHealthMessages.SummaryRecurringPhrase(liveHealth);
                // In Savings-plan mode the first-payment warning shows only in the Starting-point column (StartingShortfallWarningText), so firstPaymentLine stays null to avoid saying it twice.
                // The chart highlight stays, matching that warning's amber color.
                highlightDate = liveHealth.IsFirstOccurrencePending && liveHealth.FirstOccurrenceShortfall > 0m ? firstOccurrenceDate : null;

                // Highlights the release dates where the jar came up short; the fallback branches below can't work these out, so they clear the highlight.
                RuleEditor.SetHighlight(
                    liveHealth.UnderfundedReleaseDates,
                    PlanHealthMessages.RRulePreviewCaption(liveHealth),
                    liveHealth.UnderfundedReleaseDates.Count > 0 ? PlanHealthMessages.UnderfundedReleaseHighlightLegend : null);
            }
            else if (TryBuildProposedPattern(goal) is { } liveProposed)
            {
                // No forecast wired: works the reading out from the form's fields.
                var startingTotal = Math.Abs(_startingAllocation) + _startingEarmarkAmount;
                var live = GetLiveJarAmounts(GetPatternsForLiveCheck(goal, liveProposed), liveProposed, goal, startingTotal, DateOnly.FromDateTime(DateTime.Today));
                jarStateLine = PlanHealthMessages.JarStateLine(live.ExpectedAmount, live.MilestoneAmount);
                RuleEditor.SetHighlight([], null);
            }
            else
            {
                jarStateLine = "(fund jar state needs a live forecast — not available yet)";
                RuleEditor.SetHighlight([], null);
            }
        }

        // The chart and the aside add up every plan running at the same time (overlapping spans; chain segments never overlap), so the narrative names them too.
        var concurrentPlans = patternsForMilestone
            .Where(other => other.DatePattern.ActiveSpansOverlap(plan.DatePattern))
            .ToList();
        narrative += ConcurrentPlansSentence(concurrentPlans, plan, isOneOff);

        // Dates for up to 3 chart gridlines; empty for a one-time goal, which gets a plain "Due" label.
        var peakDates = isOneTime
            ? []
            : goal.DatePattern.GetOccurrences(plan.DatePattern.ActiveStart, chartEnd).Take(3).ToList();
        Summary.Load(
            narrative,
            start: plan.DatePattern.ActiveStart,
            asOfDate: DateOnly.FromDateTime(DateTime.Today),
            dueDate: chartEnd,
            startAmount: GetStartingPointTotal(plan),
            goalAmount: goalAmount,
            actualTrajectory: trajectory.Select(p => (p.Date, p.Jar.ExpectedAmount)).ToList(),
            jarStateLine: jarStateLine,
            trajectoryLine: trajectoryLine,
            firstPaymentLine: firstPaymentLine,
            peakDates: peakDates,
            highlightDate: highlightDate,
            proposedTrajectory: proposedTrajectory,
            additionAmount: additionAmount);
    }

    /// <summary>
    /// [UI] Shows whether this plan continues an earlier one, or has been continued by a later one.
    /// A break-off gives the successor a new finance id, so this finds neighbors through the goal's Source instead (as ExpenseFormPanel.UpdateContinuityNote does).
    /// </summary>
    /// <param name="goal">The currently-selected goal.</param>
    /// <param name="plan">The goal's own currently-loaded savings plan.</param>
    private void UpdateContinuityNote(FinancialPattern goal, EarMarkPattern plan)
    {
        var goalsByFinanceId = (_forecast?.Book.AllFinancialPatterns() ?? []).ToDictionary(candidate => candidate.FinanceId);
        var allPlans = _forecast?.Book.AllEarMarkPatterns() ?? [];

        bool SharesGoalSource(EarMarkPattern candidate) =>
            candidate.FinanceId != plan.FinanceId &&
            goalsByFinanceId.TryGetValue(candidate.FinanceId, out var candidateGoal) &&
            candidateGoal.Source == goal.Source;

        if (allPlans.FirstOrDefault(candidate => SharesGoalSource(candidate) && candidate.DatePattern.ImmediatelyPrecedes(plan.DatePattern)) is { } predecessor)
        {
            PredecessorNoteText.Text = $"This plan continues an earlier one, which ran through {predecessor.DatePattern.Until:MMM d, yyyy}.";
            PredecessorNoteText.Visibility = Visibility.Visible;
        }
        else
        {
            PredecessorNoteText.Visibility = Visibility.Collapsed;
        }

        if (allPlans.FirstOrDefault(candidate => SharesGoalSource(candidate) && plan.DatePattern.ImmediatelyPrecedes(candidate.DatePattern)) is { } successor)
        {
            SuccessorNoteText.Text = $"This plan is continued by a newer one, starting {successor.DatePattern.ActiveStart:MMM d, yyyy}.";
            SuccessorNoteText.Visibility = Visibility.Visible;
        }
        else
        {
            SuccessorNoteText.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>[CALC] Turns a schedule's frequency and interval into the phrase for the Summary's opening line, like "every 3 months" or "every week".</summary>
    /// <param name="frequency">The pattern's repeat frequency.</param>
    /// <param name="interval">The pattern's repeat interval.</param>
    private static string CadencePhrase(RecurrenceFrequency frequency, int interval)
    {
        var unit = frequency switch
        {
            RecurrenceFrequency.Daily => "day",
            RecurrenceFrequency.Weekly => "week",
            RecurrenceFrequency.Monthly => "month",
            RecurrenceFrequency.Yearly => "year",
            _ => "occurrence",
        };
        return interval <= 1 ? $"every {unit}" : $"every {interval} {unit}s";
    }

    /// <summary>[CALC] The phrase for "We plan to set aside ...", like "a month" or "every 3 months"; "a month" avoids repeating the opening line's "every month".</summary>
    /// <param name="frequency">The plan's contribution frequency.</param>
    /// <param name="interval">The plan's contribution interval.</param>
    private static string ContributionCadencePhrase(RecurrenceFrequency frequency, int interval)
    {
        var unit = frequency switch
        {
            RecurrenceFrequency.Daily => "day",
            RecurrenceFrequency.Weekly => "week",
            RecurrenceFrequency.Monthly => "month",
            RecurrenceFrequency.Yearly => "year",
            _ => "occurrence",
        };
        return interval <= 1 ? $"a {unit}" : $"every {interval} {unit}s";
    }

    /// <summary>
    /// [CALC] The narrative's sentence naming the other plans running at the same time, like " Another plan also sets aside $50 a month toward it; the chart and the figures beside it count both."
    /// In One-off mode no plan is open, so it names them all; empty when only one plan funds the goal.
    /// </summary>
    /// <param name="concurrentPlans">Every saved plan running at the same time as plan, plan's saved version included.</param>
    /// <param name="plan">The plan the Summary is built around.</param>
    /// <param name="isOneOff">Whether the form is in One-off mode.</param>
    internal static string ConcurrentPlansSentence(IReadOnlyList<EarMarkPattern> concurrentPlans, EarMarkPattern plan, bool isOneOff)
    {
        static string Describe(EarMarkPattern other) =>
            $"{Math.Abs(other.Amount):C0} {ContributionCadencePhrase(other.DatePattern.Frequency, other.DatePattern.Interval)}";

        static string JoinWithAnd(IReadOnlyList<string> parts) =>
            parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}";

        if (isOneOff)
        {
            return concurrentPlans.Count < 2
                ? string.Empty
                : $" {concurrentPlans.Count} plans fund it — {JoinWithAnd(concurrentPlans.Select(Describe).ToList())}; the chart and the figures beside it count them all.";
        }

        // Matched by start, since the open plan's saved version is in the list too.
        var others = concurrentPlans.Where(other => other.DatePattern.ActiveStart != plan.DatePattern.ActiveStart).ToList();
        return others.Count switch
        {
            0 => string.Empty,
            1 => $" Another plan also sets aside {Describe(others[0])} toward it; the chart and the figures beside it count both.",
            _ => $" {others.Count} other plans also set aside {JoinWithAnd(others.Select(Describe).ToList())} toward it; the chart and the figures beside it count them all.",
        };
    }

    /// <summary>
    /// [CALC] The Summary's opening sentence: "We need $X for [goal] by [date]" for a one-time goal.
    /// A repeating one names its cost, schedule, and next due date instead, like "Car insurance costs $300 every 3 months — next due Oct 1, 2026".
    /// </summary>
    /// <param name="goal">The goal to narrate.</param>
    /// <param name="goalAmount">The full amount needed.</param>
    /// <param name="label">The goal's display label.</param>
    /// <param name="dueDate">The goal's due date.</param>
    /// <param name="isOneTime">Whether this is a one-time goal rather than a repeating one.</param>
    /// <param name="asOfDate">Today's date, for finding the next occurrence.</param>
    private static string GoalNarrativeOpening(
        FinancialPattern goal, decimal goalAmount, string label, DateOnly dueDate, bool isOneTime, DateOnly asOfDate)
    {
        if (isOneTime)
        {
            return $"We need {goalAmount:C0} for {label} by {dueDate:MMM d, yyyy}.";
        }

        var cadence = CadencePhrase(goal.DatePattern.Frequency, goal.DatePattern.Interval);
        var upcoming = goal.DatePattern.GetOccurrences(asOfDate, dueDate);
        var nextDueText = upcoming.Count > 0 ? upcoming[0].ToString("MMM d, yyyy") : "no date left in range";
        return $"{label} costs {goalAmount:C0} {cadence} — next due {nextDueText}.";
    }

    /// <summary>
    /// [CALC] Builds an unsaved plan from the typed fields, as SaveSavingsPlan would, for the live warnings and Summary.
    /// Null when there isn't enough typed yet, or the plan doesn't fit the goal yet, which can happen mid-edit.
    /// </summary>
    /// <param name="goal">The goal to propose a savings plan for.</param>
    private EarMarkPattern? TryBuildProposedPattern(FinancialPattern goal)
    {
        if (RuleEditor.Result is not { } rule || !decimal.TryParse(AmountTextBox.Text, out var enteredAmount) || enteredAmount <= 0m)
        {
            return null;
        }

        try
        {
            return EarMarkPattern.Create(
                new EarMarkPatternOptions
                {
                    FinanceId = goal.FinanceId,
                    DatePattern = rule,
                    Amount = -enteredAmount,
                    StartingAllocation = Math.Abs(_startingAllocation),
                },
                goal);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>[UI] Reacts to the "Match the goal" checkbox: locks the amount box and fills in the covering figure when on, hands the box back to the user when off. Also called by the Load* paths after they set the toggle.</summary>
    private void OnMatchGoalToggled(object sender, RoutedEventArgs e)
    {
        ApplyMatchState();
        if (!_suppressEvents)
        {
            UpdateSummary();
            MarkDirtyIfNotSuppressed();
        }
    }

    /// <summary>[UI] Puts the amount box into the state the "Match the goal" toggle calls for — read-only and auto-filled when on, plain and editable when off — without itself touching the Summary or dirty flag.</summary>
    private void ApplyMatchState()
    {
        var on = MatchGoalCheckBox.IsChecked == true;
        AmountTextBox.IsReadOnly = on;
        if (on)
        {
            RecomputeMatch();
        }
        else if (_selectedGoal is { } goal)
        {
            UpdateMatchCoverageCaption(goal, matched: null);
        }
    }

    /// <summary>[UI] Fills the amount box with the per-occurrence figure that covers the goal at the current cadence, and shows what it covers underneath. A no-op unless matching is on with a goal and a valid schedule.</summary>
    private void RecomputeMatch()
    {
        if (MatchGoalCheckBox.IsChecked != true || _selectedGoal is not { } goal)
        {
            return;
        }

        var matched = ComputeMatchedAmount(goal);
        var wasSuppressed = _suppressEvents;
        _suppressEvents = true;
        AmountTextBox.Text = matched is { } value ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;
        _suppressEvents = wasSuppressed;
        UpdateMatchCoverageCaption(goal, matched);
    }

    /// <summary>
    /// [CALC] The "Match the goal" amount: per contribution, so the plan's total equals what the goal pays out over the plan's window.
    /// A plain steady rate, with no catch-up for an occurrence the plan starts too late for and no starting balance subtracted; the user handles those by hand.
    /// Null when the schedule or the goal has no occurrences in the window.
    /// </summary>
    /// <param name="goal">The goal being funded, whose amount and cadence set what must be covered.</param>
    private decimal? ComputeMatchedAmount(FinancialPattern goal)
    {
        if (RuleEditor.Result is not { } plan)
        {
            return null;
        }

        var deposits = plan.GetOccurrences();
        if (deposits.Count == 0)
        {
            return null;
        }

        // Counts the goal's occurrences from the first contribution on; earlier ones are covered by hand.
        var releaseCount = goal.DatePattern.GetOccurrences(deposits[0], plan.Until).Count;
        if (releaseCount == 0)
        {
            return null;
        }

        return Math.Round(Math.Abs(goal.Amount) * releaseCount / deposits.Count, 2);
    }

    /// <summary>[UI] Shows the little "covers $456.00 / month" line under the amount box while matching is on, hidden otherwise.</summary>
    /// <param name="goal">The goal whose own amount and cadence the line names.</param>
    /// <param name="matched">The computed match, or null when there's nothing to cover — hides the line.</param>
    private void UpdateMatchCoverageCaption(FinancialPattern goal, decimal? matched)
    {
        if (MatchGoalCheckBox.IsChecked != true || matched is null)
        {
            MatchCoverageText.Visibility = Visibility.Collapsed;
            return;
        }

        var cadence = DescribeGoalCadence(goal);
        MatchCoverageText.Text = cadence is null
            ? $"covers {Math.Abs(goal.Amount):C} in total"
            : $"covers {Math.Abs(goal.Amount):C} / {cadence}";
        MatchCoverageText.Visibility = Visibility.Visible;
    }

    /// <summary>[CALC] A short name for how often the goal comes due — "month", "2 weeks", etc. — or null for a one-time goal (nothing recurring to name).</summary>
    /// <param name="goal">The goal whose cadence to describe.</param>
    private static string? DescribeGoalCadence(FinancialPattern goal)
    {
        if (goal.DatePattern.GetOccurrences().Count <= 1)
        {
            return null;
        }

        var unit = goal.DatePattern.Frequency switch
        {
            RecurrenceFrequency.Daily => "day",
            RecurrenceFrequency.Weekly => "week",
            RecurrenceFrequency.Monthly => "month",
            RecurrenceFrequency.Yearly => "year",
            _ => "period",
        };
        var interval = goal.DatePattern.Interval;
        return interval <= 1 ? unit : $"{interval} {unit}s";
    }

    /// <summary>[UI] Sets the "Match the goal" toggle to match the amount just loaded — on only when that amount already covers the goal at its cadence, so a paced, capped, or hand-set plan opens with matching off and nothing gets silently rewritten.</summary>
    /// <param name="goal">The goal being funded.</param>
    /// <param name="loadedAmount">The plan amount just placed in the box, to compare against the computed match.</param>
    private void InferMatchToggle(FinancialPattern goal, decimal loadedAmount)
    {
        MatchGoalCheckBox.IsChecked = ComputeMatchedAmount(goal) is { } matched && matched == loadedAmount;
        ApplyMatchState();
    }

    /// <summary>[CALC] Every plan funding the goal (several can run at once, like two partners each contributing), with the one being edited replaced by its unsaved version, matched by its start.</summary>
    /// <param name="goal">The goal whose funding patterns to gather.</param>
    /// <param name="proposed">The not-yet-saved version of the pattern currently being edited.</param>
    private IReadOnlyList<EarMarkPattern> GetPatternsForLiveCheck(FinancialPattern goal, EarMarkPattern proposed)
    {
        var editedStart = OpenPlanFor(goal)?.DatePattern.ActiveStart;
        var otherSavedPatterns = _forecast?.Accounts
            .SelectMany(account => account.Page.EarmarkPatterns)
            .Where(p => p.FinanceId == goal.FinanceId && p.DatePattern.ActiveStart != editedStart)
            .ToList() ?? [];
        return [.. otherSavedPatterns, proposed];
    }

    /// <summary>
    /// [CALC] Today's jar balance and milestone, worked out from the plan's schedule and the goal's manual earmarks, with no forecast.
    /// Like the chart's proposed line, it pays each release in full and ignores deallocation, so it's a rough reading for when no forecast has the goal's jar today.
    /// </summary>
    /// <param name="patterns">Every pattern funding the goal (see GetPatternsForLiveCheck), so a goal with more than one concurrent earmark pattern is still computed correctly.</param>
    /// <param name="proposed">The not-yet-saved version of the pattern currently being edited.</param>
    /// <param name="goal">The goal being funded.</param>
    /// <param name="startingTotal">What was already in the jar before this plan's own contributions began, including the starting earmark.</param>
    /// <param name="asOfDate">Today's date, to read the live amounts as of.</param>
    private (decimal ExpectedAmount, decimal MilestoneAmount) GetLiveJarAmounts(
        IReadOnlyList<EarMarkPattern> patterns, EarMarkPattern proposed, FinancialPattern goal, decimal startingTotal, DateOnly asOfDate)
    {
        var activeStart = proposed.DatePattern.ActiveStart;
        var milestoneTrajectory = TransactionLogBookFactory.ComputeMilestoneTrajectory(patterns, goal, activeStart, asOfDate);
        var liveMilestone = milestoneTrajectory.Count > 0 ? milestoneTrajectory[^1].MilestoneAmount : 0m;

        // The starting earmark sits on the plan's start and is already in startingTotal.
        var laterManualEarmarks = _existingManualEarmarks.Where(manual => manual.Date != activeStart).ToList();
        var liveExpected = TransactionLogBookFactory.ComputeExpectedTrajectory(
            patterns, goal, activeStart, asOfDate, startingTotal, laterManualEarmarks)[^1].ExpectedAmount;
        return (liveExpected, liveMilestone);
    }

    /// <summary>
    /// [CALC] The goal's jar day by day from today through `to` or the forecast's end, whichever comes first.
    /// It always starts today, since the forecast has no balances before then; UpdateSummary's chart accounts for that.
    /// </summary>
    /// <param name="financeId">Which goal's jar to walk.</param>
    /// <param name="to">The end of the range to walk through.</param>
    private IReadOnlyList<(DateOnly Date, FundJar Jar)> GetJarTrajectory(int financeId, DateOnly to)
    {
        if (_forecast is null)
        {
            return [];
        }

        // Uses the FinanceId overload, as BalancesOn does.
        return _forecast.GetTimeline(financeId)
            .Where(entry => entry.Date <= to)
            .Select(entry => (entry.Date, Jar: entry.Snapshot.FundJars.FirstOrDefault(candidate => candidate.FinanceId == financeId)))
            .Where(entry => entry.Jar is not null)
            .Select(entry => (entry.Date, Jar: entry.Jar!))
            .ToList();
    }

    private void MarkDirty()
    {
        _isDirty = true;
        SaveButton.IsEnabled = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearDirty()
    {
        _isDirty = false;
        SaveButton.IsEnabled = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MarkDirtyIfNotSuppressed()
    {
        if (_initialized && !_suppressEvents)
        {
            MarkDirty();
        }
    }
}
