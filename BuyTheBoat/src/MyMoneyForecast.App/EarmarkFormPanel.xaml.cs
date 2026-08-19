using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
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
        // (redesign/planning/26, "EarmarkPatterns aren't 'real' the way
        // FinancialPatterns are"); Expense's bill/paycheck schedule doesn't
        // offer it.
        RuleEditor.ShowExcludedDatesEditor();

        AmountTextBox.TextChanged += (_, _) => { if (!_suppressEvents) { UpdateSummary(); MarkDirtyIfNotSuppressed(); } };
        RuleEditor.ResultChanged += (_, _) => { if (!_suppressEvents) { UpdateSummary(); MarkDirtyIfNotSuppressed(); } };

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

        // A goal can have more than one EarMarkPattern (concurrent funders,
        // or a break-off/restructure chain), so this groups and keeps each
        // goal's most-recently-started one rather than a plain ToDictionary,
        // which throws on the duplicate key. A disambiguation picker for the
        // true-concurrent case isn't built yet — this is the single answer
        // used until then.
        _patternsByFinanceId = patterns
            .GroupBy(pattern => pattern.FinanceId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(p => p.DatePattern.Start).First());

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

    /// <summary>[STEP] Loads an existing EarMarkPattern for editing — the goal is fixed (FinanceId is the link, and it can't change once created), only amount/timing can.</summary>
    /// <param name="existing">The savings plan to load for editing.</param>
    /// <param name="goal">The goal it's linked to.</param>
    public void LoadPattern(EarMarkPattern existing, FinancialPattern goal)
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
        _loadedPlanStart = existing.DatePattern.Start;
        RuleEditor.LoadFrom(existing.DatePattern);

        _suppressEvents = false;
        UpdateModeVisibility();
        UpdateSummary();
        ClearDirty();
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

        EarmarkDatePicker.DisplayDateStart = pattern.DatePattern.Start.ToDateTime(TimeOnly.MinValue);
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
        else
        {
            AmountTextBox.Text = string.Empty;
            _startingAllocation = 0m;
            _startingEarmarkAmount = 0m;
            StartingEarmarkAmountTextBox.Text = string.Empty;
            _loadedActiveStart = null;
            // TODO: picking a goal with no existing plan leaves whatever
            // schedule was already on screen — RecurrenceRuleEditor has no
            // public reset beyond its own constructor.
        }
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

    /// <summary>[STEP] Unwired from any control for now — the "+ Add manual earmark on this goal" button this used to back is removed from the layout until it has a settled home. Left in place, untouched, as the machinery for whenever it gets one.</summary>
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
        // Same gap as LoadOneOff/OnModeChanged — action/date/amount edits
        // need to reach Summary too, not just BalanceInfoText, now that it's
        // visible in One-off mode.
        UpdateSummary();
        MarkDirtyIfNotSuppressed();
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
            : $" The fund's plan runs {pattern.DatePattern.Start:MMM d, yyyy} – {pattern.DatePattern.Until:MMM d, yyyy}.";
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
            ErrorText.Text = ex.Message;
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
        var savedStart = _loadedPlanStart ?? pattern.DatePattern.Start;

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
            var isPending = TransactionLogBookFactory.IsFirstOccurrencePending(goal, asOfDate);
            var shortfall = TransactionLogBookFactory.FirstOccurrenceShortfall(
                GetPatternsForLiveCheck(goal, proposed), goal, GetProposedManualEarmarks(goal, proposed), asOfDate);
            var isOneTime = goal.DatePattern.GetOccurrences().Count == 1;
            line = PlanHealthMessages.FirstOccurrenceShortfallLine(isPending, shortfall, isOneTime);
        }
        catch (ArgumentException)
        {
            // GetProposedManualEarmarks' own ManualEarmark.Create call can
            // still throw even though proposed itself built fine — same
            // Start-vs-ActiveStart latent inconsistency SaveSavingsPlan's
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

    /// <summary>[CALC] How much the currently-typed One-off adjustment would add to today's ExpectedAmount reading, if saved right now — lets the Summary aside react before Save is clicked. Known gap (planning/24): a date backdated to before this goal's most recent release doesn't replay the real day-by-day cascade, so it shows no live change even though saving it for real would shift today's balance — a real forecast run always gets the right number regardless.</summary>
    /// <param name="goal">The goal the one-off adjustment is against.</param>
    /// <param name="activeStart">The savings plan's own ActiveStart.</param>
    /// <param name="asOfDate">Today's date — the live preview only applies on or before this.</param>
    private decimal GetOneOffLiveDelta(FinancialPattern goal, DateOnly activeStart, DateOnly asOfDate)
    {
        if (!decimal.TryParse(OneOffAmountTextBox.Text, out var typedAmount) || typedAmount <= 0m
            || EarmarkDatePicker.SelectedDate is not { } picked)
        {
            return 0m;
        }

        var date = DateOnly.FromDateTime(picked);
        if (date > asOfDate)
        {
            return 0m;
        }

        var releases = goal.DatePattern.GetOccurrences(activeStart, asOfDate);
        var mostRecentReleaseDate = releases.Count > 0 ? releases[^1] : activeStart;
        if (date <= mostRecentReleaseDate)
        {
            return 0m;
        }

        var newAmount = IsWithdraw || IsMove ? -typedAmount : typedAmount;
        var oldAmount = _oneOffEditTarget?.Amount ?? 0m; // Merge's own "editing replaces, not stacks" rule (SaveOneOff, above)
        return newAmount - oldAmount;
    }

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

    /// <summary>[UI] Rebuilds the Summary region (narrative sentence, chart, and the two aside lines) from PlanHealthState/FundJar data, in both Savings-plan and One-off mode. In One-off mode the aside folds in whatever's currently typed on top of the saved reading, live. Known gap: asideLine/asideSecondaryLine (the text figures, not the chart) read the saved PlanHealthState once one exists, rather than a live recompute of an in-progress edit — full parity would mean re-running the whole forecast on every keystroke. When no saved PlanHealthState exists yet, the live fallback further below already covers the text too.</summary>
    private void UpdateSummary()
    {
        UpdateStartingPointRegion();

        if (_selectedGoal is not { } goal)
        {
            Summary.Clear("Pick a goal above to see its savings plan summary.");
            PredecessorNoteText.Visibility = Visibility.Collapsed;
            SuccessorNoteText.Visibility = Visibility.Collapsed;
            return;
        }

        if (!_patternsByFinanceId.TryGetValue(goal.FinanceId, out var plan))
        {
            Summary.Clear("No savings plan yet for this goal — fill in the fields below to create one.");
            PredecessorNoteText.Visibility = Visibility.Collapsed;
            SuccessorNoteText.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateContinuityNote(goal, plan);

        var goalAmount = Math.Abs(goal.Amount);
        var dueDate = goal.DatePattern.Until;
        var label = string.IsNullOrWhiteSpace(goal.Description) ? goal.Source : goal.Description;
        var isOneOff = OneOffRadio.IsChecked == true;
        var isOneTime = goal.DatePattern.GetOccurrences().Count == 1;

        // This goal's own very first occurrence, ever. Only used below when
        // IsFirstOccurrencePending is also true.
        var firstOccurrenceDate = goal.DatePattern.GetOccurrences(goal.DatePattern.Start, goal.DatePattern.Until).FirstOrDefault();

        var health = _forecast?.PlanHealthStates.FirstOrDefault(p => p.FinanceId == goal.FinanceId);

        // The chart's actual-amount line: a real walked trajectory rather
        // than a single point extrapolated backward (which degenerates to
        // an invisible flat line whenever today's balance is genuinely $0).
        var trajectory = GetJarTrajectory(goal.FinanceId, dueDate);
        var jar = trajectory.Count > 0 ? trajectory[0].Jar : null;

        // The chart's milestone line spans the whole plan (Start through
        // the due date), not just today onward — MilestoneAmount is pure
        // pattern math and needs no real transaction history. Gathers every
        // EarMarkPattern sharing this FinanceId (a goal can have more than
        // one — concurrent funders, or a break-off chain).
        var patternsForMilestone = _forecast?.Accounts
            .SelectMany(account => account.Page.EarmarkPatterns)
            .Where(p => p.FinanceId == goal.FinanceId)
            .ToList() ?? [];
        var milestoneTrajectory = TransactionLogBookFactory.ComputeMilestoneTrajectory(
            patternsForMilestone, goal, plan.DatePattern.ActiveStart, dueDate);

        string narrative;
        string asideLine;
        string? asideSecondaryLine = null;
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

            // Today's actual-state line folds in whatever's currently
            // typed. MilestoneAmount itself never moves here — it only ever
            // accumulates scheduled contributions, never a manual one.
            // delta == 0m (nothing valid typed) keeps the original,
            // saved-health-gated wording; the simpler always-show-a-delta
            // wording only takes over once there's a live adjustment to
            // reflect.
            try
            {
                var delta = GetOneOffLiveDelta(goal, plan.DatePattern.ActiveStart, todayDate);
                additionAmount = delta == 0m ? null : delta; // null, not 0 — a $0 line would just retrace Actual for no reason
                if (jar is not null && health is not null)
                {
                    asideLine = delta == 0m
                        ? PlanHealthMessages.CurrentJarStateLine(jar, health)
                        : PlanHealthMessages.LiveJarStateLine(jar.ExpectedAmount + delta, jar.MilestoneAmount ?? 0m);
                }
                else
                {
                    var live = GetLiveJarAmounts(
                        GetPatternsForLiveCheck(goal, plan), plan, goal, GetStartingPointTotal(plan), todayDate);
                    asideLine = PlanHealthMessages.LiveJarStateLine(live.ExpectedAmount + delta, live.MilestoneAmount);
                }
            }
            catch (ArgumentException)
            {
                // A date the picker's bounds should already exclude, mid-edit.
                asideLine = "(fund jar state needs a live forecast — not available yet)";
            }

            // Same live substitution for the first-payment warning.
            try
            {
                var isPending = TransactionLogBookFactory.IsFirstOccurrencePending(goal, todayDate);
                var shortfall = TransactionLogBookFactory.FirstOccurrenceShortfall(
                    GetPatternsForLiveCheck(goal, plan), goal, GetProposedOneOffManualEarmarks(goal, plan), todayDate);
                asideSecondaryLine = PlanHealthMessages.FirstOccurrenceShortfallLine(isPending, shortfall, isOneTime);
                highlightDate = isPending && shortfall > 0m ? firstOccurrenceDate : null;
            }
            catch (ArgumentException)
            {
                asideSecondaryLine = null;
            }
        }
        else
        {
            decimal.TryParse(AmountTextBox.Text, out var enteredAmount);
            var start = RuleEditor.Result?.Start ?? DateOnly.FromDateTime(DateTime.Today);
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
            // regardless of whether a saved PlanHealthState exists.
            if (TryBuildProposedPattern(goal) is { } proposedForChart)
            {
                var proposedStartingTotal = Math.Abs(_startingAllocation) + _startingEarmarkAmount;
                proposedTrajectory = TransactionLogBookFactory.ComputeMilestoneTrajectory(
                        GetPatternsForLiveCheck(goal, proposedForChart), goal, plan.DatePattern.ActiveStart, dueDate, proposedStartingTotal)
                    .Select(p => (p.Date, p.MilestoneAmount))
                    .ToList();
            }

            if (jar is not null && health is not null)
            {
                asideLine = PlanHealthMessages.SummaryFutureLine(jar, health.Shortfall, health.MostImportantHealthState)
                    ?? PlanHealthMessages.CurrentJarStateLine(jar, health);

                // The first-payment warning takes priority over the
                // recurring-chronic-shortfall phrase when both apply — this
                // region's aside is capped at two facts, and a payment about
                // to fail is more time-sensitive than an ongoing rate problem.
                asideSecondaryLine = PlanHealthMessages.FirstOccurrenceShortfallLine(health.IsFirstOccurrencePending, health.FirstOccurrenceShortfall, isOneTime)
                    ?? PlanHealthMessages.SummaryRecurringPhrase(health);
                highlightDate = health.IsFirstOccurrencePending && health.FirstOccurrenceShortfall > 0m ? firstOccurrenceDate : null;

                // Highlights this goal's release dates where the jar came
                // up short, from the real forecast's forward walk. No
                // live-pattern-math equivalent exists for the no-saved-
                // health branches below, so they clear the highlight
                // instead of guessing at one.
                RuleEditor.SetHighlight(
                    health.UnderfundedReleaseDates,
                    PlanHealthMessages.RRulePreviewCaption(health),
                    health.UnderfundedReleaseDates.Count > 0 ? PlanHealthMessages.UnderfundedReleaseHighlightLegend : null);
            }
            else if (TryBuildProposedPattern(goal) is { } liveProposed)
            {
                // No saved forecast reading yet (a brand-new plan, or one
                // whose Start just moved past what's been computed) — a
                // live reading computed straight from the form's own
                // fields, no forecast needed.
                var startingTotal = Math.Abs(_startingAllocation) + _startingEarmarkAmount;
                var live = GetLiveJarAmounts(GetPatternsForLiveCheck(goal, liveProposed), liveProposed, goal, startingTotal, DateOnly.FromDateTime(DateTime.Today));
                asideLine = PlanHealthMessages.LiveJarStateLine(live.ExpectedAmount, live.MilestoneAmount);
                RuleEditor.SetHighlight([], null);
            }
            else
            {
                asideLine = "(fund jar state needs a live forecast — not available yet)";
                RuleEditor.SetHighlight([], null);
            }
        }

        // TODO(2026-08-13): the narrative above, and the chart's own
        // "actual"/"proposed" lines, only ever describe THIS ONE
        // EarMarkPattern (plan) — but the aside (asideLine/
        // asideSecondaryLine) and the health figures behind it
        // (IsChronicShortfall/IsChronicOverfund, GoalShortfall) are summed
        // across every plan sharing this finance_id, concurrent funders
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
            other.DatePattern.Start != plan.DatePattern.Start && // a different row, not this same plan read back
            // Overlaps this plan's own active span — F27's "concurrent
            // funder" shape, as opposed to a break-off/restructure chain's
            // sequential segments, which never overlap by construction (a
            // predecessor's own Until always ends the day before its
            // successor's own Start — "connected at the start/end," not
            // concurrent).
            other.DatePattern.ActiveStart <= plan.DatePattern.Until &&
            plan.DatePattern.ActiveStart <= other.DatePattern.Until);
        if (hasConcurrentPlan)
        {
            narrative += " Another earmark pattern is allocating funds alongside this one.";
        }

        // A committed-plan milestone line applies to every goal with a
        // savings plan, one-time or repeating.
        //
        // peakDates labels up to 3 gridlines with their own dates, rather
        // than every occurrence a frequently-repeating pattern would have.
        // Empty for a one-time goal — DrawChart falls back to a plain
        // "Due {dueDate}" label there.
        var peakDates = isOneTime
            ? []
            : goal.DatePattern.GetOccurrences(plan.DatePattern.ActiveStart, dueDate).Take(3).ToList();
        Summary.Load(
            narrative,
            start: plan.DatePattern.ActiveStart,
            asOfDate: DateOnly.FromDateTime(DateTime.Today),
            dueDate: dueDate,
            startAmount: GetStartingPointTotal(plan),
            goalAmount: goalAmount,
            actualTrajectory: trajectory.Select(p => (p.Date, p.Jar.ExpectedAmount)).ToList(),
            milestoneTrajectory: milestoneTrajectory,
            asideLine: asideLine,
            asideSecondaryLine: asideSecondaryLine,
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

        if (allPlans.FirstOrDefault(candidate => SharesGoalSource(candidate) && candidate.DatePattern.Until.AddDays(1) == plan.DatePattern.Start) is { } predecessor)
        {
            PredecessorNoteText.Text = $"This plan continues an earlier one, which ran through {predecessor.DatePattern.Until:MMM d, yyyy}.";
            PredecessorNoteText.Visibility = Visibility.Visible;
        }
        else
        {
            PredecessorNoteText.Visibility = Visibility.Collapsed;
        }

        if (allPlans.FirstOrDefault(candidate => SharesGoalSource(candidate) && candidate.DatePattern.Start == plan.DatePattern.Until.AddDays(1)) is { } successor)
        {
            SuccessorNoteText.Text = $"This plan is continued by a newer one, starting {successor.DatePattern.Start:MMM d, yyyy}.";
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

    /// <summary>[CALC] A goal can have more than one EarMarkPattern funding it concurrently (e.g. two household partners each contributing) — this gathers every one actually funding the goal, with whichever ONE is currently loaded/being edited in this form (matched by Start, the same resolution key _patternsByFinanceId uses) replaced by its live, not-yet-saved version; every other concurrent funder passes through unchanged from the saved data. Live checks need this explicitly — the saved-state path already gathers every pattern by FinanceId.</summary>
    /// <param name="goal">The goal whose funding patterns to gather.</param>
    /// <param name="proposed">The not-yet-saved version of the pattern currently being edited.</param>
    private IReadOnlyList<EarMarkPattern> GetPatternsForLiveCheck(FinancialPattern goal, EarMarkPattern proposed)
    {
        var editedStart = _patternsByFinanceId.GetValueOrDefault(goal.FinanceId)?.DatePattern.Start;
        var otherSavedPatterns = _forecast?.Accounts
            .SelectMany(account => account.Page.EarmarkPatterns)
            .Where(p => p.FinanceId == goal.FinanceId && p.DatePattern.Start != editedStart)
            .ToList() ?? [];
        return [.. otherSavedPatterns, proposed];
    }

    /// <summary>[CALC] A live (ExpectedAmount, MilestoneAmount) reading for today, computed purely from the proposed pattern's own schedule, no forecast required. The trick: ComputeMilestoneTrajectory already walks "accumulate, reset to 0 on release" starting from 0. Seed that same walk with a real starting balance instead of 0, and the two walks are provably identical from the first reset onward — a reset always drives both to exactly 0, erasing whatever the starting balance was worth by then. So: once this goal has released at least once since ActiveStart, live ExpectedAmount equals live MilestoneAmount exactly (today's true pace, no reason to reseed); before any release has happened yet, it's just startingTotal + MilestoneAmount (nothing has erased the offset). Known, deliberate gap: like ComputeMilestoneTrajectory itself, this doesn't model manual earmarks beyond the starting point (a real mid-plan top-up on an existing plan isn't reflected).</summary>
    /// <param name="patterns">Every pattern funding the goal (see GetPatternsForLiveCheck), so a goal with more than one concurrent funder is still computed correctly.</param>
    /// <param name="proposed">The not-yet-saved version of the pattern currently being edited.</param>
    /// <param name="goal">The goal being funded.</param>
    /// <param name="startingTotal">What was already in the jar before this plan's own contributions began.</param>
    /// <param name="asOfDate">Today's date, to read the live amounts as of.</param>
    private (decimal ExpectedAmount, decimal MilestoneAmount) GetLiveJarAmounts(
        IReadOnlyList<EarMarkPattern> patterns, EarMarkPattern proposed, FinancialPattern goal, decimal startingTotal, DateOnly asOfDate)
    {
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
