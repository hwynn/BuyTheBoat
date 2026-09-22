using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// The Earmark tab: pick a goal (loads its savings plan if one exists), or
// switch into a One-off adjustment on the same goal instead of opening a
// separate window. Replaces the old CreateEarMarkPatternWindow and
// ManualEarmarkWindow popups as MainWindow's entry points.
//
// TODO: the merged "what can I allocate today" region isn't built yet —
// this still uses the older, simpler BalanceInfoText. The date-picker-as-
// selector (click a day that already has an entry to load it) isn't built
// either.
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
    private IReadOnlyList<ManualEarmark> _existingManualEarmarks = [];
    private IReadOnlySet<int> _transferFinanceIds = new HashSet<int>();
    private ForecastResult? _forecast;
    private ManualEarmark? _oneOffEditTarget;
    private FinancialPattern? _selectedGoal;
    private bool _suppressEvents;
    private bool _isDirty;

    // Debounce for the one-off live preview: its forecast-backed jar reading is too heavy to run on every
    // keystroke, so a one-off field change restarts this timer and the preview only recomputes once the user
    // pauses (~1.2s). Cheap feedback (BalanceInfoText, dirty state) still updates on every change.
    private readonly DispatcherTimer _oneOffPreviewTimer = new() { Interval = TimeSpan.FromSeconds(1.2) };

    // Light blue for "something already exists here, click to load it" —
    // deliberately different from RecurrenceRuleEditor's own orange, which
    // means a warning.
    private static readonly Brush ExistingEntryBrush = new SolidColorBrush(Color.FromRgb(0xC7, 0xDD, 0xF5));

    // Not currently editable from any control on the form, but still flows
    // through the normal Load/Save/Summary paths.
    private decimal _startingAllocation;

    // The other starting-point source: a manual earmark dated exactly on
    // the plan's own ActiveStart (distinct from StartingAllocation, which
    // only a break-off sets). Edited live via StartingEarmarkAmountTextBox
    // when visible; deleted on Save if it's zero.
    private decimal _startingEarmarkAmount;

    // The ActiveStart this plan's starting earmark was loaded against, or
    // null if there wasn't one. Save compares this to the current
    // ActiveStart so a moved Start date doesn't leave the old entry orphaned.
    private DateOnly? _loadedActiveStart;

    // The plan's own literal Start as it's actually saved today, or null for
    // a brand-new plan (LoadForNewPattern/LoadForMaterialize — no existing
    // row to speak of yet). Distinct from _loadedActiveStart above: that one
    // tracks ActiveStart, for the isolated-starting-earmark's own key; this
    // one tracks Start itself, since Start is one of the two fields
    // FinancePatternSaveConfirmation's chain-boundary question can move, and
    // PatternSaved needs the ORIGINAL value to find the right row — the
    // proposed EarMarkPattern handed to PatternSaved only ever carries
    // whatever Start the form currently shows, which is the NEW value once
    // the user has changed it.
    private DateOnly? _loadedPlanStart;

    // Guards against SavingsPlanRadio's XAML-declared IsChecked="True"
    // firing Checked synchronously mid-InitializeComponent, before this
    // form's other controls exist yet. Starts true and flips once, at the
    // end of the constructor; every handler below checks it.
    private bool _initialized;

    public EarmarkFormPanel()
    {
        InitializeComponent();

        // Amount is declared in this form's own XAML (normal code-behind
        // wiring), then handed to RuleEditor's left column once both trees
        // exist.
        SavingsPlanPanel.Children.Remove(AmountPanel);
        RuleEditor.SetLeadingContent(AmountPanel);

        // Only the Earmark form gets this — a savings plan's own recurrence
        // is an organizational construct the user has real freedom over
        // (EarmarkPatterns aren't 'real' the way
        // FinancialPatterns are); Expense's bill/paycheck schedule doesn't
        // offer it.
        RuleEditor.ShowExcludedDatesEditor();

        AmountTextBox.TextChanged += (_, _) => { if (!_suppressEvents) { UpdateSummary(); MarkDirtyIfNotSuppressed(); } };
        // Changing the cadence re-sizes the matched amount before anything reads it,
        // so a "Match the goal" plan keeps covering the goal as the schedule changes.
        RuleEditor.ResultChanged += (_, _) => { if (!_suppressEvents) { if (MatchGoalCheckBox.IsChecked == true) { RecomputeMatch(); } UpdateSummary(); MarkDirtyIfNotSuppressed(); } };

        // Once typing pauses, run the (now forecast-backed) one-off preview. One-shot: Stop first, so a
        // fresh keystroke that restarted the timer isn't pre-empted by this tick.
        _oneOffPreviewTimer.Tick += (_, _) => { _oneOffPreviewTimer.Stop(); UpdateSummary(); };

        _initialized = true;
    }

    // MainWindow persists whatever comes back through these — this panel
    // owns no repository itself. PatternSaved's second parameter is the
    // plan's own Start as it's actually saved today (or the same as the
    // proposed pattern's own Start for a brand-new plan) — see
    // _loadedPlanStart's own field comment for why this can't just be read
    // off the pattern parameter itself. It returns whether the save went
    // through (false when the user cancels the confirmation), so SaveSavingsPlan
    // can leave the form as-is rather than clearing it.
    public Func<EarMarkPattern, DateOnly, bool>? PatternSaved { get; set; }
    public Action<IReadOnlyList<ManualEarmark>, IReadOnlyList<(int FinanceId, DateOnly Date)>>? ManualEarmarksSaved { get; set; }

    // Computes (or returns the cached) live forecast on demand, so this form
    // never has to send the user to press a different button first.
    public Func<ForecastResult>? RequestForecast { get; set; }

    // Runs a throwaway forecast with one not-yet-saved one-off earmark folded in — the live one-off preview
    // reads the goal's REAL jar off it (full day-by-day cascade), instead of approximating. Null when this
    // hook isn't wired up; the preview then falls back to the plain saved reading.
    public Func<ManualEarmark, ForecastResult>? RequestForecastWithOneOff { get; set; }

    // Builds an affordability-capped default plan for a goal that has none yet,
    // WITHOUT saving it — the same proposal "Save and Plan" would create. Picking
    // a plan-less goal loads its values as unsaved draft edits, so the user lands
    // on a real proposal to tweak instead of a bare monthly skeleton. Null when
    // this hook isn't wired up; PopulateSavingsPlanFields then falls back to that skeleton.
    public Func<FinancialPattern, ProposedAllocationPlan?>? RequestProposedPlan { get; set; }

    // Runs a throwaway forecast with a not-yet-saved savings PLAN substituted in (args: proposed plan, the
    // active-start of the saved segment it replaces or null if brand new, and any proposed manual earmarks
    // to fold in). Lets the live first-payment warning read real free funds for the plan as typed, instead
    // of the no-forecast set-aside gap. Null when this hook isn't wired up; the warning then falls back to that gap.
    public Func<EarMarkPattern, DateOnly?, IReadOnlyList<ManualEarmark>, ForecastResult>? RequestForecastWithProposedPlan { get; set; }

    // Fires whenever IsDirty or IsPopulated could have changed, so MainWindow
    // can restyle this form's tab header live.
    public event EventHandler? StateChanged;

    public bool IsDirty => _isDirty;

    // A goal being selected is this form's closest thing to "an instance is
    // loaded" — it doesn't have a full instance-information-block yet.
    public bool IsPopulated => _selectedGoal is not null;

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

        // A goal can have more than one EarMarkPattern (concurrent earmark patterns,
        // or a break-off/restructure chain), so this groups and keeps each
        // goal's most-recently-started one rather than a plain ToDictionary,
        // which throws on the duplicate key. A disambiguation picker for the
        // true-concurrent case isn't built yet — this is the single answer
        // used until then.
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
        ChangeGoalButton.IsEnabled = true;
        AmountTextBox.Text = string.Empty;
        _startingAllocation = 0m;
        _startingEarmarkAmount = 0m;
        StartingEarmarkAmountTextBox.Text = string.Empty;
        _loadedActiveStart = null;
        _loadedPlanStart = null;

        _suppressEvents = false;
        UpdateModeVisibility();
        UpdateSummary();
        ClearDirty();
    }

    /// <summary>[STEP] Loads an existing EarMarkPattern for editing — the goal is fixed (FinanceId is the link, and it can't change once created), only amount/timing can. When a suggestion's overrides are passed, its values land on top of the saved ones as UNSAVED edits, so the form opens with the correction pre-filled and its Save button/tab already lit.</summary>
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
        ChangeGoalButton.IsEnabled = false;

        AmountTextBox.Text = Math.Abs(existing.Amount).ToString(CultureInfo.InvariantCulture);
        _startingAllocation = existing.StartingAllocation;
        _startingEarmarkAmount = GetStartingEarmarkAmount(existing);
        StartingEarmarkAmountTextBox.Text = _startingEarmarkAmount == 0m ? string.Empty : _startingEarmarkAmount.ToString(CultureInfo.InvariantCulture);
        _loadedActiveStart = existing.DatePattern.ActiveStart;
        _loadedPlanStart = existing.DatePattern.DtStart;
        RuleEditor.LoadFrom(existing.DatePattern);
        InferMatchToggle(goal, Math.Abs(existing.Amount));

        _suppressEvents = false;
        UpdateModeVisibility();
        UpdateSummary();
        ClearDirty();

        // A suggestion's values land LAST, over the saved ones, with events live
        // — so setting a field to something different from what was just loaded
        // fires the same dirty-tracking a keystroke would, lighting up Save and
        // the tab exactly as if the user had typed the correction. An override
        // equal to the saved value changes no text, so the form stays clean.
        ApplySuggestedOverrides(suggestedOverrides);
    }

    /// <summary>[UI] Lays a suggestion's field values over the just-loaded saved ones, with events live so a real change marks the form dirty through the normal path. A no-op when there are no overrides; an unknown key or a value of the wrong runtime type is skipped rather than throwing.</summary>
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
    }

    /// <summary>[STEP] Turns an automatically-filled jar into a savings plan the user owns. Goal fixed, starting allocation pre-filled from what the jar already holds so pressing Save never moves money — it only changes what governs the jar from here on.</summary>
    /// <param name="goal">The outflow this savings plan is being created for.</param>
    /// <param name="alreadySaved">What the jar already holds, pre-filled as the starting allocation.</param>
    public void LoadForMaterialize(FinancialPattern goal, decimal alreadySaved)
    {
        _suppressEvents = true;
        ErrorText.Text = string.Empty;
        _oneOffEditTarget = null;
        SavingsPlanRadio.IsChecked = true;

        SetGoal(goal);
        ChangeGoalButton.IsEnabled = false;

        AmountTextBox.Text = string.Empty;
        _startingAllocation = alreadySaved;
        // No EarMarkPattern exists yet to have an isolated earmark on —
        // this jar's balance came from automatic reservation, not a plan.
        _startingEarmarkAmount = 0m;
        StartingEarmarkAmountTextBox.Text = string.Empty;
        _loadedActiveStart = null;
        _loadedPlanStart = null;
        // Materializing a jar carries no schedule of its own to size against, so the
        // user picks the amount; matching starts off.
        MatchGoalCheckBox.IsChecked = false;
        ApplyMatchState();

        _suppressEvents = false;
        UpdateModeVisibility();
        UpdateSummary();
        ClearDirty();
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
        OneOffRadio.IsChecked = true;

        var goalsWithPlans = _goals.Where(goal => _patternsByFinanceId.ContainsKey(goal.FinanceId)).ToList();
        var targetFinanceId = editTarget?.FinanceId ?? preselectFinanceId ?? goalsWithPlans.FirstOrDefault()?.FinanceId;
        SetGoal(goalsWithPlans.FirstOrDefault(goal => goal.FinanceId == targetFinanceId));
        ChangeGoalButton.IsEnabled = editTarget is null;

        TargetComboBox.ItemsSource = goalsWithPlans.Select(goal => new GoalOption(goal)).ToList();

        // An isolated earmark's date can never fall outside its own
        // EarMarkPattern's span, so the calendar simply doesn't offer an
        // out-of-range day rather than rejecting one on save. Called before
        // either branch below sets SelectedDate, so a fresh selection
        // always lands inside range.
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
        // UpdateOneOffInfo only populates the older BalanceInfoText control —
        // Summary needs its own explicit refresh too.
        UpdateSummary();
        ClearDirty();
    }

    private void SetGoal(FinancialPattern? goal)
    {
        _selectedGoal = goal;
        SelectedGoalText.Text = goal is null
            ? "— No goal selected —"
            : (string.IsNullOrWhiteSpace(goal.Description) ? goal.Source : goal.Description);

        // Nothing to match against without a goal — the Load* paths that DO have
        // one set the toggle themselves (InferMatchToggle), on after the schedule
        // is in place.
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

    /// <summary>[STEP] Opens FinancialPatternPickerWindow, the same picker ExpenseFormPanel uses everywhere a FinancialPattern gets chosen. Shows every eligible pattern, not just ones with an existing plan — picking one without a plan while in One-off mode surfaces as SaveOneOff's own "Pick a goal with a savings plan to adjust" error rather than being filtered out.</summary>
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

        SetGoal(picked);
        MarkDirtyIfNotSuppressed();

        if (SavingsPlanRadio.IsChecked == true)
        {
            PopulateSavingsPlanFields(picked);
            UpdateSummary();
        }
        else
        {
            UpdateEarmarkDatePickerBounds();
            UpdateOneOffInfo();
            UpdateSummary();
        }

        UpdateModeVisibility();
    }

    /// <summary>[UI] Same helper ExpenseFormPanel has — copied rather than shared, since neither panel derives from a common form base.</summary>
    /// <param name="message">The confirmation prompt to show.</param>
    private bool ConfirmDiscard(string message) =>
        MessageBox.Show(Window.GetWindow(this), message, "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            == MessageBoxResult.Yes;

    /// <summary>[UI] Keeps an isolated earmark's date inside its own EarMarkPattern's span. If the goal just changed and the currently-picked date no longer fits, it's cleared rather than left silently invalid; UpdateOneOffInfo already handles a null SelectedDate.</summary>
    private void UpdateEarmarkDatePickerBounds()
    {
        if (_selectedGoal is not { } goal || !_patternsByFinanceId.TryGetValue(goal.FinanceId, out var pattern))
        {
            EarmarkDatePicker.DisplayDateStart = null;
            EarmarkDatePicker.DisplayDateEnd = null;
            return;
        }

        EarmarkDatePicker.DisplayDateStart = pattern.DatePattern.ActiveStart.ToDateTime(TimeOnly.MinValue);
        EarmarkDatePicker.DisplayDateEnd = pattern.DatePattern.Until.ToDateTime(TimeOnly.MinValue);

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
            RuleEditor.LoadFrom(existing.DatePattern);
        }
        else if (RequestProposedPlan?.Invoke(goal) is { } proposal)
        {
            // No saved plan, but we can propose a real one: the same
            // affordability-capped plan "Save and Plan" would create, loaded as
            // unsaved draft values the user can edit before saving. _loadedActiveStart
            // stays null — nothing is saved yet, so there's no existing starting
            // earmark to reconcile against on Save (the draft's own is written fresh).
            AmountTextBox.Text = Math.Abs(proposal.Plan.Amount).ToString(CultureInfo.InvariantCulture);
            _startingAllocation = proposal.Plan.StartingAllocation;
            _startingEarmarkAmount = proposal.StartingEarmark?.Amount ?? 0m;
            StartingEarmarkAmountTextBox.Text = _startingEarmarkAmount == 0m ? string.Empty : _startingEarmarkAmount.ToString(CultureInfo.InvariantCulture);
            _loadedActiveStart = null;
            RuleEditor.LoadFrom(proposal.Plan.DatePattern);
        }
        else
        {
            AmountTextBox.Text = string.Empty;
            _startingAllocation = 0m;
            _startingEarmarkAmount = 0m;
            StartingEarmarkAmountTextBox.Text = string.Empty;
            _loadedActiveStart = null;
            // No existing plan and no proposer wired: reset the schedule
            // to a sensible default (monthly, through the goal's own due date) rather
            // than leaving whatever plan was last on screen — LoadFrom does the full
            // field reset for us.
            var defaultStart = DateOnly.FromDateTime(DateTime.Today);
            var defaultUntil = goal.DatePattern.Until > defaultStart ? goal.DatePattern.Until : defaultStart.AddYears(1);
            RuleEditor.LoadFrom(RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                DtStart = defaultStart,
                Until = defaultUntil,
            }));
        }

        // Turn matching on only when the amount just loaded already covers the goal
        // at this cadence — true for a plain default plan (full amount on the bill's
        // own cycle), false for an income-paced or affordability-capped proposal, or
        // a hand-set amount like the user's own — so nothing meaningful gets rewritten.
        decimal.TryParse(AmountTextBox.Text, out var loadedAmount);
        InferMatchToggle(goal, loadedAmount);
    }

    /// <summary>[CALC] Shared by every Load* path above — the isolated earmark (if any) dated exactly on this pattern's own ActiveStart.</summary>
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

    /// <summary>[UI] Switching modes by hand (not via "+ Add manual earmark," which calls LoadOneOff itself) only toggles which panel shows — it doesn't reload data, so an in-progress edit in the panel being hidden isn't lost.</summary>
    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _suppressEvents)
        {
            return;
        }

        UpdateModeVisibility();

        // Clicking straight into the One-off pill leaves EarmarkDatePicker
        // with no default date, so only fill one in when it's still blank —
        // an already-loaded entry's date is never overwritten. Wrapped in
        // _suppressEvents since setting SelectedDate programmatically still
        // raises SelectedDateChanged, and OnInputsChanged's "landed on a day
        // with an existing entry" switch logic shouldn't run for a plain
        // default fill-in.
        if (OneOffRadio.IsChecked == true && EarmarkDatePicker.SelectedDate is null)
        {
            _suppressEvents = true;
            EarmarkDatePicker.SelectedDate = DateTime.Today;
            _suppressEvents = false;
        }

        // Summary lives outside the mode-switching Grid, so toggling the
        // radio button needs its own explicit refresh too.
        UpdateSummary();
        MarkDirtyIfNotSuppressed();
    }

    /// <summary>[UI] No _initialized guard needed — this checkbox has no XAML default value to fire early, unlike SavingsPlanRadio above. Wired to the one piece of content that's settled for Earmark (RecurrenceRuleEditor's RRULE box). A view preference, not data: doesn't call MarkDirty.</summary>
    private void OnAdvancedModeChanged(object sender, RoutedEventArgs e) =>
        RuleEditor.SetAdvancedMode(AdvancedModeCheckBox.IsChecked == true);

    /// <summary>[STEP] Unwired from any control for now — the "+ Add manual earmark on this goal" button it would back isn't in the layout until it has a settled home. Kept as the machinery for whenever it gets one.</summary>
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

    /// <summary>[UI] Caps the schedule editor's date pickers to the selected goal's span and captions that span above the fields, so the user can't build (or even pick) a plan reaching outside its goal — the case EarMarkPattern.Create would otherwise reject at Save. Savings-plan mode only, and only with a goal picked; one-off mode manages its own EarmarkDatePicker bounds elsewhere.</summary>
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

        // The date field never locks — a user should be able to look around
        // freely without getting stuck editing whatever they clicked out of
        // curiosity. Guarded to EarmarkDatePicker specifically, since this
        // one handler also backs ActionComboBox and OneOffAmountTextBox.
        //
        // "Editing the date of the loaded entry" was never actually an
        // option in the first place — (FinanceId, Date) is that row's own
        // key — so there was nothing a locked control was protecting
        // against. Every date change already resolves unambiguously to one
        // of two things: land on a different day that already has an entry
        // (load it), or land anywhere else (that's a fresh, unrelated
        // entry now — never a rename of whatever was loaded before).
        //
        // isSwitch is false — no reload, no confirm, just a plain field
        // change — for the common case of nudging the date while building a
        // brand-new entry (blank before, blank after: nothing to lose).
        // It's only ever true when we're actually landing somewhere
        // different from what's currently loaded.
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
                    // A blank start on whichever date was actually clicked —
                    // same shape "+ New Earmark" already builds, just landing
                    // here instead of always defaulting to today. Goal must
                    // be passed explicitly: LoadOneOff(null) with no
                    // preselect falls back to the first goal with a plan,
                    // which would silently swap goals out from under the user.
                    LoadOneOff(editTarget: null, initialDate: date, preselectFinanceId: _selectedGoal?.FinanceId);
                }

                return;
            }
        }

        TargetRow.Visibility = IsMove ? Visibility.Visible : Visibility.Collapsed;
        UpdateOneOffInfo();
        // Same gap as LoadOneOff/OnModeChanged — action/date/amount edits need to reach Summary too, not
        // just BalanceInfoText, now that it's visible in One-off mode. Debounced: the Summary recompute now
        // runs a real forecast (the one-off preview), too heavy to do on every keystroke.
        ScheduleOneOffPreview();
        MarkDirtyIfNotSuppressed();
    }

    /// <summary>[UI] Restarts the one-off preview debounce — the forecast-backed Summary recompute runs once ~1.2s pass without another keystroke, not on every one. Cheap per-change feedback (BalanceInfoText, dirty state) is left to run immediately by the caller; only the heavy recompute waits.</summary>
    private void ScheduleOneOffPreview()
    {
        _oneOffPreviewTimer.Stop();
        _oneOffPreviewTimer.Start();
    }

    /// <summary>[CALC] Future only (>= today) — matches the Savings-plan-mode overview's own "haven't happened yet" restriction, so "selectable" isn't defined two different ways in the same form.</summary>
    /// <param name="date">The date to check for an existing entry.</param>
    private ManualEarmark? FindExistingEntryOn(DateOnly date) =>
        _selectedGoal is { } goal && date >= DateOnly.FromDateTime(DateTime.Today)
            ? _existingManualEarmarks.FirstOrDefault(m => m.FinanceId == goal.FinanceId && m.Date == date)
            : null;

    /// <summary>[UI] Colors the calendar days that already have an isolated earmark for the selected goal, the moment the dropdown opens — reusing RecurrenceRuleEditor's own CalendarDayButton-walking technique (a DatePicker's popup calendar is the same underlying control). Recomputed fresh every open rather than cached, so it's always current for whichever goal is selected at that moment. Known, accepted rough edge: paging to a different month while the dropdown stays open doesn't re-mark until it's closed and reopened — the common case (today's month) always works.</summary>
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

    private void UpdateOneOffInfo()
    {
        if (BalanceInfoText is null)
        {
            return;
        }

        if (_forecast is null || _selectedGoal is not { } goal || EarmarkDatePicker.SelectedDate is not { } selectedDate)
        {
            BalanceInfoText.Text = string.Empty;
            return;
        }

        var date = DateOnly.FromDateTime(selectedDate);
        var (jarBalance, free) = BalancesOn(date, goal.FinanceId);
        var pattern = _patternsByFinanceId.GetValueOrDefault(goal.FinanceId);
        var span = pattern is null
            ? string.Empty
            : $" The fund's plan runs {pattern.DatePattern.ActiveStart:MMM d, yyyy} – {pattern.DatePattern.Until:MMM d, yyyy}.";
        BalanceInfoText.Text = $"On {date:MMMM d, yyyy}: this fund holds {jarBalance:C} · free balance {free:C}.{span}";
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

        // Always an allocation — money moving from free balance into the fund
        // jar — so the field is a plain magnitude and the sign is fixed here
        // rather than typed by the user.
        var amount = -Math.Abs(enteredAmount);

        // Backstop for the goal-span cap: the date pickers already refuse
        // out-of-range days from the calendar (UpdateGoalSpanConstraint), but a
        // date typed straight into the box can still slip past those display
        // bounds — so catch it here with the goal's own dates named, rather than
        // letting EarMarkPattern.Create's raw ArgumentException surface.
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

        // The starting-earmark field's own save rule. 3.13c.8.a5: a $0
        // isolated earmark doesn't get to exist, so zero+existing means
        // delete, not save-as-zero.
        //
        // Latent inconsistency, worth knowing: ManualEarmark.Create
        // validates its own Date against DatePattern.Start literally, not
        // ActiveStart — the two only ever coincide today because nothing
        // currently gives an EarMarkPattern its own ActiveFrom lead-in
        // distinct from Start. Would need real attention if that ever changes.
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

        // If a starting earmark exists and the Start date gets moved, the
        // old one must be deleted too. _loadedActiveStart is whatever
        // ActiveStart was in effect when this plan was loaded — if the
        // Start date has
        // since moved, whatever was sitting at that OLD date no longer
        // means anything (it isn't "at the start" of this schedule anymore)
        // and would otherwise sit there forever, orphaned. Skipped when the
        // two dates match — already handled by the block above in that case.
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

        // savedStart has to be read before LoadForNewPattern below clears
        // _loadedPlanStart — falls back to the proposed pattern's own Start
        // when there was nothing loaded (a brand-new plan), matching
        // FinancePatternSaveConfirmation's other constructor's own
        // documented contract for that parameter.
        var savedStart = _loadedPlanStart ?? pattern.DatePattern.ActiveStart;

        // Only clear once the save has actually gone through — cancelling the
        // confirmation returns false and leaves the form exactly as typed, so
        // the user resumes as if Save was never clicked (same rule as
        // ExpenseFormPanel.Save). A null handler counts as "went through,"
        // keeping the old always-clear behavior for that case. Clearing after
        // the callback's own navigation is fine — the panel just reads blank
        // next time the user lands back on this tab.
        if (PatternSaved?.Invoke(pattern, savedStart) ?? true)
        {
            LoadForNewPattern();
        }
    }

    /// <summary>[CALC] Ported from ManualEarmarkWindow verbatim (Merge/RequireFundsCover/WarnIfOverFree/BalancesOn below) — same validation policy, just reading the source fund from this panel's own goal picker instead of a separate JarComboBox, since the goal is already chosen at the top of this same form.</summary>
    private void SaveOneOff()
    {
        if (_selectedGoal is not { } goal || !_patternsByFinanceId.TryGetValue(goal.FinanceId, out var sourcePattern))
        {
            throw new InvalidOperationException("Pick a goal with a savings plan to adjust.");
        }

        if (EarmarkDatePicker.SelectedDate is not { } selectedDate)
        {
            throw new InvalidOperationException("Pick a day.");
        }
        var date = DateOnly.FromDateTime(selectedDate);

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
            if (!_patternsByFinanceId.TryGetValue(targetGoal.Pattern.FinanceId, out var targetPattern))
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

        // Same auto-clear rule as SaveSavingsPlan above — one-off adjustments
        // are still a save on this same form.
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

        // Uses the FinanceId-scoped overload, not the no-arg GetTimeline()
        // — the no-arg one only looks at PrimaryAccountPage, which would
        // silently read (0m, 0m) for any goal on a non-Primary account.
        var entry = _forecast.GetTimeline(financeId).LastOrDefault(candidate => candidate.Date <= date);
        if (entry is null)
        {
            return (0m, 0m);
        }

        var jar = entry.Snapshot.FundJars.FirstOrDefault(candidate => candidate.FinanceId == financeId);
        return (jar?.ExpectedAmount ?? 0m, entry.Snapshot.ExpectedFreeAmount ?? 0m);
    }

    // Whether the Starting-point region can appear at all: true in
    // Savings-plan mode, false in One-off — deliberately simple for now,
    // expected to grow more restrictive later.
    private bool ShowFundStartPointRegion => SavingsPlanRadio.IsChecked == true;

    // Whether the user can see/edit the starting isolated earmark's own
    // amount — needs ShowFundStartPointRegion, and locks once the pattern's
    // own ActiveStart is in the past. Reads RuleEditor.Result (the live,
    // currently-typed rule) rather than the saved pattern, so this reacts
    // correctly for a brand-new plan and to a live edit of an existing Start
    // date, the same as everything else in this form.
    private bool UsersCanEditFundStartPoint =>
        ShowFundStartPointRegion
        && RuleEditor.Result is { } rule
        && rule.ActiveStart >= DateOnly.FromDateTime(DateTime.Today);

    /// <summary>[UI] The starting-point region: amount + date only, real data — StartingAllocation (the break-off case, fully unambiguous) plus any ManualEarmark dated exactly on the plan's own ActiveStart (the front-load-or-manual case; which of those two it was is still an open question, not guessed at here — see this region's own XAML comment). ShowFundStartPointRegion is the only visibility gate — the region always renders whenever it's true, in all three content cases below.</summary>
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

        // No saved EarMarkPattern to read from at all — no goal picked yet,
        // or a goal picked that has none: same $0 treatment as an existing
        // zero-total plan, just without a real ActiveStart to caption.
        if (_selectedGoal is not { } goal || !_patternsByFinanceId.TryGetValue(goal.FinanceId, out var pattern))
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

        // Runs for both branches above — brand-new (no saved plan yet) is
        // exactly when this warning matters most, not a case to skip it in.
        UpdateStartingShortfallWarning();
    }

    /// <summary>[UI] Reads the CURRENTLY-TYPED Amount/Recurrence fields, not the saved plan, since the whole point is catching this before Save. Builds the same kind of throwaway EarMarkPattern SaveSavingsPlan itself constructs right before persisting, never saved, just fed into the same domain check (TransactionLogBookFactory.FirstOccurrenceShortfall) the saved path also runs for the Summary region's own aside — one computation, two callers. Shares TryBuildProposedPattern with GetLiveJarAmounts rather than building its own copy, so there's one place that has to stay in sync with EarMarkPattern.Create's validation.</summary>
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

            // Prefer a real forecast with this plan substituted in — it carries the free-funds figure the
            // funds-aware message needs. Falls back to the plain set-aside gap (no free funds) when the hook isn't wired,
            // or the goal has no health row in the result.
            var editedStart = _patternsByFinanceId.GetValueOrDefault(goal.FinanceId)?.DatePattern.ActiveStart;
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
            // GetProposedManualEarmarks' own ManualEarmark.Create call can still throw even though
            // proposed itself built fine — same Start-vs-ActiveStart latent inconsistency SaveSavingsPlan's
            // own comment already flags, not a new risk introduced here.
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

    /// <summary>[CALC] Substitutes the live _startingEarmarkAmount in place of whatever's saved at this same (FinanceId, ActiveStart) pair, so a starting earmark being typed but not yet saved still factors into the warning — the same "proposed, not saved" principle the rest of this check already follows.</summary>
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

    /// <summary>[CALC] The single ManualEarmark the currently-typed one-off would save — signed for the chosen action (add is positive; withdraw and move are negative on the source fund) — or null when there's nothing valid typed to build one from, or the date momentarily falls outside the plan's span (the user is very possibly mid-edit). Feeds the live one-off preview's what-if forecast.</summary>
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
            return ManualEarmark.Create(
                new ManualEarmarkOptions { FinanceId = goal.FinanceId, Date = DateOnly.FromDateTime(picked), Amount = signedAmount },
                plan);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>[CALC] The goal's FundJar as of the forecast's own as-of date (today) — the first day its day-by-day walk produces — or null when the goal has no jar in that forecast yet.</summary>
    /// <param name="forecast">The forecast to read from.</param>
    /// <param name="financeId">Which goal's jar to read.</param>
    private static FundJar? JarAsOf(ForecastResult forecast, int financeId) =>
        forecast.GetTimeline(financeId)
            .Select(entry => entry.Snapshot.FundJars.FirstOrDefault(jar => jar.FinanceId == financeId))
            .FirstOrDefault(jar => jar is not null);

    /// <summary>[CALC] One-off mode's counterpart to GetProposedManualEarmarks above — substitutes the currently-typed amount/date/action in place of whatever's already saved at that same (FinanceId, Date).</summary>
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
            plan));
        return withoutThisEntry;
    }

    /// <summary>[UI] The editable counterpart to AmountTextBox.TextChanged above. Blank or unparseable reads as 0, which is a valid value here (unlike Amount, this field has no "must be filled in" requirement).</summary>
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

    /// <summary>[CALC] How much was already in the jar before the plan's own regular contributions began: StartingAllocation (the break-off case) plus any manual earmark dated exactly on the plan's ActiveStart. Feeds the Starting-point region and the Summary chart's actual-line start.</summary>
    /// <param name="pattern">The savings plan to compute the starting total for.</param>
    private decimal GetStartingPointTotal(EarMarkPattern pattern)
    {
        var activeStart = pattern.DatePattern.ActiveStart;
        var manualAtStart = _existingManualEarmarks.FirstOrDefault(m => m.FinanceId == pattern.FinanceId && m.Date == activeStart);
        return pattern.StartingAllocation + (manualAtStart?.Amount ?? 0m);
    }

    /// <summary>[UI] Rebuilds the Summary region (narrative sentence, chart, and the two aside lines) from PlanHealthState/FundJar data, in both Savings-plan and One-off mode. Both modes fold whatever's currently typed into a throwaway what-if forecast and read the goal's real health + jar off THAT, so the aside text updates live — not just the chart. Falls back to the saved reading, then pure pattern math, when no what-if forecast source is wired.</summary>
    private void UpdateSummary()
    {
        // _forecast is the post-save world: MainWindow's NavigateToEarmarkForm
        // recomputes the forecast (RefreshForecast) before handing it in via
        // SetContext, so landing here right after a save — even an earlier-segment
        // edit that cascades forward — reads current numbers, not a pre-save
        // snapshot. The chart's forward line reflects the typed-but-unsaved fields
        // (the live proposedTrajectory here on the earmark form); the finance-pattern
        // form draws no milestone line at all, since a finance-pattern edit only
        // changes the plan implicitly, behind the save-confirmation popup.
        UpdateStartingPointRegion();

        if (_selectedGoal is not { } goal)
        {
            Summary.Clear("Pick a goal above to see its savings plan summary.");
            PredecessorNoteText.Visibility = Visibility.Collapsed;
            SuccessorNoteText.Visibility = Visibility.Collapsed;
            return;
        }

        var isOneOff = OneOffRadio.IsChecked == true;

        // A goal with no saved EarMarkPattern still previews in Savings-plan mode:
        // anchor the Summary on the live proposed pattern built from the typed
        // (or draft-proposed) fields, so a brand-new plan shows its chart and
        // narrative exactly like an edit to an existing one — the whole point of
        // "see what the plan will look like before saving." One-off mode has
        // nothing to adjust without a saved plan, so it keeps the placeholder.
        // savedPlan (may be null) is kept separately from plan for the few places
        // that must distinguish "the segment being replaced on Save" from "the
        // pattern the chart is drawn from."
        var savedPlan = _patternsByFinanceId.GetValueOrDefault(goal.FinanceId);
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

        // How far out the CHART reaches. A far-off repeating goal (a 13-year
        // mortgage, say) compresses its early, meaningful activity into a sliver
        // when drawn all the way to its own Until — so past ~5 years we preview it
        // like an ongoing bill: a fixed 5-year window, the plan's lines just running
        // on to the edge rather than shrunk to show a distant finish. Only the CHART
        // is capped — the narrative below still names the real due date. A one-time
        // goal keeps its full span (its single due date IS the point).
        var chartEnd = !isOneTime && dueDate > DateOnly.FromDateTime(DateTime.Today).AddYears(5)
            ? DateOnly.FromDateTime(DateTime.Today).AddYears(5)
            : dueDate;

        // This goal's own very first occurrence, ever. Only used below when
        // IsFirstOccurrencePending is also true.
        var firstOccurrenceDate = goal.DatePattern.GetOccurrences(goal.DatePattern.ActiveStart, goal.DatePattern.Until).FirstOrDefault();

        var health = _forecast?.PlanHealthStates.FirstOrDefault(p => p.FinanceId == goal.FinanceId);

        // The chart's actual-amount line: a real walked trajectory rather
        // than a single point extrapolated backward (which degenerates to
        // an invisible flat line whenever today's balance is genuinely $0).
        var trajectory = GetJarTrajectory(goal.FinanceId, chartEnd);
        var jar = trajectory.Count > 0 ? trajectory[0].Jar : null;

        // Every EarMarkPattern sharing this FinanceId (a goal can have more than
        // one — concurrent earmark patterns, or a break-off chain). Only used below
        // to detect whether a concurrent plan exists; the chart's forward line is
        // the LIVE proposed line built from the typed fields (proposedTrajectory,
        // below), so there's no longer a separate frozen "committed plan" milestone
        // line drawn from these saved patterns — the one milestone line always
        // reflects what the user has currently typed.
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

            // Today's actual-state line folds in whatever's currently typed by folding the proposed one-off
            // into a throwaway what-if forecast and reading the goal's REAL jar off it — a full day-by-day
            // walk, so a backdated one-off whose excess survives a release shows up correctly (the old
            // pattern-math shortcut couldn't see that). Falls back to the plain saved reading when nothing
            // valid is typed, no what-if source is wired, or the goal has no jar in the forecast
            // yet. MilestoneAmount never moves either way — it only accumulates scheduled contributions.
            try
            {
                var savedExpected = jar?.ExpectedAmount
                    ?? GetLiveJarAmounts(GetPatternsForLiveCheck(goal, plan), plan, goal, GetStartingPointTotal(plan), todayDate).ExpectedAmount;

                if (TryBuildProposedOneOff(goal, plan) is { } proposedOneOff
                    && RequestForecastWithOneOff is { } requestWithOneOff
                    && JarAsOf(requestWithOneOff(proposedOneOff), goal.FinanceId) is { } proposedJar)
                {
                    var addition = proposedJar.ExpectedAmount - savedExpected;
                    additionAmount = addition == 0m ? null : addition; // null, not 0 — a $0 line would just retrace Actual
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

            // Same live substitution for the first-payment warning — funds-aware, read off the same
            // proposed-one-off forecast the jar reading above uses (or the saved health when nothing's
            // typed), falling back to the plain set-aside gap only when neither is available.
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

            // For a repeating goal, names the plan's own contribution
            // cadence (RuleEditor.Result) — not the goal's own cadence,
            // since a monthly bill could be funded biweekly.
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

            // The chart's "proposed — rough, live estimate" line, computed
            // regardless of whether a saved PlanHealthState exists. This is the
            // projected jar ExpectedAmount, NOT the milestone: a release
            // subtracts the goal's payout and floors at 0 rather than resetting,
            // so the starting amount (and any structural glut) carries forward
            // and visibly lifts the line, instead of washing out at the first
            // release the way a milestone walk does.
            if (TryBuildProposedPattern(goal) is { } proposedForChart)
            {
                var proposedStartingTotal = Math.Abs(_startingAllocation) + _startingEarmarkAmount;
                proposedTrajectory = TransactionLogBookFactory.ComputeExpectedTrajectory(
                        GetPatternsForLiveCheck(goal, proposedForChart), goal, plan.DatePattern.ActiveStart, chartEnd, proposedStartingTotal)
                    .Select(p => (p.Date, p.ExpectedAmount))
                    .ToList();
            }

            // Fold the typed-but-unsaved plan into a throwaway what-if forecast and
            // read the goal's REAL health + jar off it, so the aside's TEXT updates
            // live as you type — not just the chart. This matters most for a
            // MANDATORY bill (a mortgage, say): it already carries an auto-earmark
            // health, so the aside has to track the what-if reading rather than
            // sitting frozen on that while you design the plan. Falls back to the
            // saved health, then to pure pattern math, when no what-if source is
            // wired. (The starting-point warning runs its own copy of this same
            // forecast; sharing one pass between them is a possible later tidy-up.)
            var liveHealth = health;
            var liveJar = jar;
            try
            {
                if (TryBuildProposedPattern(goal) is { } proposedForAside && RequestForecastWithProposedPlan is { } requestAside)
                {
                    // The segment being replaced is the SAVED one's start, or null
                    // when there's no saved plan yet (brand-new) — not plan's own
                    // start, which for a brand-new plan is the proposed pattern's.
                    var liveForecast = requestAside(proposedForAside, savedPlan?.DatePattern.ActiveStart, GetProposedManualEarmarks(goal, proposedForAside));
                    liveHealth = liveForecast.PlanHealthStates.FirstOrDefault(state => state.FinanceId == goal.FinanceId) ?? health;
                    liveJar = JarAsOf(liveForecast, goal.FinanceId) ?? jar;
                }
            }
            catch (ArgumentException)
            {
                // A latent Start-vs-ActiveStart inconsistency mid-edit (the same one
                // UpdateStartingShortfallWarning guards) — fall back to the saved reading.
                liveHealth = health;
                liveJar = jar;
            }

            if (liveJar is not null && liveHealth is not null)
            {
                // Two labeled regions now: Fund jar, today (where it stands) and
                // Toward the goal (the long-run picture, or the chronic-shortfall
                // phrase). The first-payment warning is its own third line below.
                jarStateLine = PlanHealthMessages.JarStateLine(liveJar.ExpectedAmount, liveJar.MilestoneAmount ?? 0m);
                trajectoryLine = PlanHealthMessages.SummaryFutureLine(liveJar, liveHealth.Shortfall, liveHealth.MostImportantHealthState)
                    ?? PlanHealthMessages.SummaryRecurringPhrase(liveHealth);
                // The first-payment warning is NOT shown in the aside in Savings-plan
                // mode: the STARTING POINT column's own live warning
                // (StartingShortfallWarningText) is its sole home here,
                // so leaving firstPaymentLine null avoids saying the same
                // sentence twice. One-off mode, where that column is hidden, still
                // routes it into the aside below. The chart highlight stays — it ties
                // to the column warning by its shared amber color.
                highlightDate = liveHealth.IsFirstOccurrencePending && liveHealth.FirstOccurrenceShortfall > 0m ? firstOccurrenceDate : null;

                // Highlights this goal's release dates where the jar came up short,
                // from the forecast's forward walk (the live one when we have it). No
                // live-pattern-math equivalent exists for the fallback branches
                // below, so they clear the highlight instead of guessing at one.
                RuleEditor.SetHighlight(
                    liveHealth.UnderfundedReleaseDates,
                    PlanHealthMessages.RRulePreviewCaption(liveHealth),
                    liveHealth.UnderfundedReleaseDates.Count > 0 ? PlanHealthMessages.UnderfundedReleaseHighlightLegend : null);
            }
            else if (TryBuildProposedPattern(goal) is { } liveProposed)
            {
                // No forecast reading available at all (no forecast hook wired) — a live reading
                // computed straight from the form's own fields, no forecast needed.
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

        // TODO: the narrative above, and the chart's own
        // "actual"/"proposed" lines, only ever describe THIS ONE
        // EarMarkPattern (plan) — but the aside (jarStateLine/
        // firstPaymentLine) and the health figures behind it
        // (IsChronicShortfall/IsChronicOverfund, GoalShortfall) are summed
        // across every plan sharing this finance_id, concurrent earmark patterns
        // included (patternsForMilestone, right above). Found via Storage
        // Unit Rental in the field: two concurrent plans ($35 + $25) against
        // a $50 bill — this one plan's own $35 narrative sat right next to a
        // health verdict ("Consistently ahead") that only makes sense once
        // you know a SECOND plan exists, which nothing here ever mentioned.
        // Long-term handling undecided — showing every plan somehow, a
        // combined chart, a plan picker, something else entirely — not
        // scoped or designed yet. Short-term mitigation only, below: flag
        // that another plan exists at all, without trying to describe or
        // total what it's doing.
        var hasConcurrentPlan = patternsForMilestone.Any(other =>
            other.DatePattern.ActiveStart != plan.DatePattern.ActiveStart && // a different row, not this same plan read back
            // Overlaps this plan's own active span — the "concurrent
            // earmark pattern" shape, as opposed to a break-off/restructure chain's
            // sequential segments, which never overlap by construction (a
            // predecessor's own Until always ends the day before its
            // successor's own Start — "connected at the start/end," not
            // concurrent).
            other.DatePattern.ActiveSpansOverlap(plan.DatePattern));
        if (hasConcurrentPlan)
        {
            narrative += " Another earmark pattern is allocating funds alongside this one.";
        }

        // peakDates labels up to 3 gridlines with their own dates, rather
        // than every occurrence a frequently-repeating pattern would have.
        // Empty for a one-time goal — DrawChart falls back to a plain
        // "Due {dueDate}" label there.
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

    /// <summary>[UI] Shows whether this plan continues an earlier one, or has since been continued by a later one — a break-off's successor GOAL always gets a brand-new FinanceId, so its own freshly-proposed PLAN does too (BreakOffFactory.BreakOff never reuses a finance_id), which means the same finance_id scoping _patternsByFinanceId uses everywhere else in this file can't find a predecessor/successor plan — only the goal's own Source survives the cut (same reasoning as ExpenseFormPanel's own UpdateContinuityNote, one level down — a plan's chain identity rides on its goal's). Called from inside UpdateSummary once goal/plan are already resolved — its own two early-return branches clear both texts directly instead, since there's no plan (or no goal at all) to search a chain from. The XAML elements this sets live inside SavingsPlanPanel only (the "recurrence fields" area this was asked to squeeze into) — running this regardless of mode is harmless in One-off mode, since the note simply isn't visible while its own parent panel is collapsed.</summary>
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

    /// <summary>[CALC] Turns a repeating pattern's own Frequency/Interval into the cadence phrase the Summary narrative's opening line needs ("every 3 months", "every week") — same Daily/Weekly/Monthly/Yearly unit words RecurrenceRuleEditor.UpdateFormVisibility already uses for its own "Every N ___(s)" field label, just written out as a plain phrase instead of that label's "(s)" shorthand.</summary>
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

    /// <summary>[CALC] The "We plan to set aside" continuation's own cadence phrase ("a month", "every 3 months") — same unit words as CadencePhrase above, different article for an interval of 1 or less ("a month" reads more naturally there than "every month" repeated right after GoalNarrativeOpening's own "every month").</summary>
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

    /// <summary>[CALC] The Summary narrative's opening sentence. A one-time goal keeps the single-transaction wording: there is only one payment, so "by {dueDate}" already says the right thing, and dueDate really is that payment's own due date. A repeating pattern instead names its own cost and cadence and anchors on the NEXT occurrence counting from today ("Car insurance costs $300 every 3 months — next due Oct 1, 2026") — the old wording named the pattern's far-future Until alongside the per-occurrence amount, which didn't describe the same thing.</summary>
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

    /// <summary>[CALC] Shared by UpdateStartingShortfallWarning and GetLiveJarAmounts below — the same throwaway, never-saved EarMarkPattern SaveSavingsPlan itself constructs right before persisting. Null whenever the form doesn't have enough to build one yet (no rule, unparseable/non-positive amount), or the rule is momentarily incompatible with the goal — EarMarkPattern.Create enforces that (Start before the goal's own active span, Until past its date range, ...), and the user is very possibly mid-way through fixing exactly that when this runs. Nothing live to show yet in that case, not a crash.</summary>
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

    /// <summary>[CALC] The per-occurrence amount that makes the plan's total contributions equal the goal's total consumption over the plan's own window — the "Match the goal" figure. Deliberately a plain steady-state rate: no catch-up for an occurrence the plan starts too late to fund, and no netting of the starting balance (the user tops those up by hand). Null when the schedule or the goal has no occurrences in the window to divide by.</summary>
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

        // Consumption over the same window the deposits span, counted from the first
        // deposit onward — occurrences before it are the ones the user front-loads by
        // hand, not something the ongoing rate stretches to cover.
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

    /// <summary>[CALC] A goal can have more than one EarMarkPattern funding it concurrently (e.g. two household partners each contributing) — this gathers every one actually funding the goal, with whichever ONE is currently loaded/being edited in this form (matched by Start, the same resolution key _patternsByFinanceId uses) replaced by its live, not-yet-saved version; every other concurrent earmark pattern passes through unchanged from the saved data. Live checks need this explicitly — the saved-state path already gathers every pattern by FinanceId.</summary>
    /// <param name="goal">The goal whose funding patterns to gather.</param>
    /// <param name="proposed">The not-yet-saved version of the pattern currently being edited.</param>
    private IReadOnlyList<EarMarkPattern> GetPatternsForLiveCheck(FinancialPattern goal, EarMarkPattern proposed)
    {
        var editedStart = _patternsByFinanceId.GetValueOrDefault(goal.FinanceId)?.DatePattern.ActiveStart;
        var otherSavedPatterns = _forecast?.Accounts
            .SelectMany(account => account.Page.EarmarkPatterns)
            .Where(p => p.FinanceId == goal.FinanceId && p.DatePattern.ActiveStart != editedStart)
            .ToList() ?? [];
        return [.. otherSavedPatterns, proposed];
    }

    /// <summary>[CALC] A live (ExpectedAmount, MilestoneAmount) reading for today, computed purely from the proposed pattern's own schedule, no forecast required. The trick: ComputeMilestoneTrajectory already walks "accumulate, reset to 0 on release" starting from 0. Seed that same walk with a real starting balance instead of 0, and the two walks are provably identical from the first reset onward — a reset always drives both to exactly 0, erasing whatever the starting balance was worth by then. So: once this goal has released at least once since ActiveStart, live ExpectedAmount equals live MilestoneAmount exactly (today's true pace, no reason to reseed); before any release has happened yet, it's just startingTotal + MilestoneAmount (nothing has erased the offset). Known, deliberate gap: like ComputeMilestoneTrajectory itself, this doesn't model manual earmarks beyond the starting point (a real mid-plan top-up on an existing plan isn't reflected).</summary>
    /// <param name="patterns">Every pattern funding the goal (see GetPatternsForLiveCheck), so a goal with more than one concurrent earmark pattern is still computed correctly.</param>
    /// <param name="proposed">The not-yet-saved version of the pattern currently being edited.</param>
    /// <param name="goal">The goal being funded.</param>
    /// <param name="startingTotal">What was already in the jar before this plan's own contributions began.</param>
    /// <param name="asOfDate">Today's date, to read the live amounts as of.</param>
    private (decimal ExpectedAmount, decimal MilestoneAmount) GetLiveJarAmounts(
        IReadOnlyList<EarMarkPattern> patterns, EarMarkPattern proposed, FinancialPattern goal, decimal startingTotal, DateOnly asOfDate)
    {
        // TODO: this live preview ignores a mid-plan ManualEarmark that the real forecast counts,
        // so the Summary can show a misleading jar balance — revisit the preview's honesty.
        var activeStart = proposed.DatePattern.ActiveStart;
        var milestoneTrajectory = TransactionLogBookFactory.ComputeMilestoneTrajectory(patterns, goal, activeStart, asOfDate);
        var liveMilestone = milestoneTrajectory.Count > 0 ? milestoneTrajectory[^1].MilestoneAmount : 0m;
        var hasReleased = goal.DatePattern.GetOccurrences(activeStart, asOfDate).Count > 0;
        var liveExpected = hasReleased ? liveMilestone : startingTotal + liveMilestone;
        return (liveExpected, liveMilestone);
    }

    /// <summary>[CALC] The FundJar behind a Savings Plan, walked day-by-day from today through whichever comes first of `to` or the forecast's own HorizonEndDate. Real data, not a hypothetical: TransactionLogBookFactory only ever cascades day-by-day balances forward from AsOfDate — there is no historical BalanceRecord before today, so this can only ever start at today, never at the plan's own original Start (unlike MilestoneAmount, which needs no such split). UpdateSummary's chart accounts for that split explicitly rather than pretending the whole span is real. Same _forecast/GetTimeline lookup BalancesOn already uses, walked across a range instead of a single date.</summary>
    /// <param name="financeId">Which goal's jar to walk.</param>
    /// <param name="to">The end of the range to walk through.</param>
    private IReadOnlyList<(DateOnly Date, FundJar Jar)> GetJarTrajectory(int financeId, DateOnly to)
    {
        if (_forecast is null)
        {
            return [];
        }

        // Uses the FinanceId-scoped overload, same reasoning as BalancesOn.
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
