using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// planning/21 Philosophy 5/7 + the author's own 2026-08-05 description of the
// flow: one permanent Earmark tab, not a popup — pick a goal (loading its
// Savings-plan form if one exists), and a button right there "transforms"
// the same form into a One-off adjustment on that same goal, rather than
// opening a second, separate window (ManualEarmarkWindow) for it. Replaces
// both CreateEarMarkPatternWindow and ManualEarmarkWindow as MainWindow's
// entry points — see MainWindow.xaml.cs's own TODOs at those two files for
// what's left before they can actually be deleted.
//
// Goal selection wired 2026-08-06 to FinancialPatternPickerWindow — the same
// mechanism ExpenseFormPanel uses (the author's own call: "the earmark form
// needs to use the same way to select finance patterns"), not the plain
// ComboBox this used to be. TargetComboBox (One-off mode's "Move to which
// fund?" picker) is deliberately NOT converted in this same pass — smaller,
// secondary selector, scoped out to keep this change focused.
//
// Dirty/has-content tracking also wired 2026-08-06 (mirrors ExpenseFormPanel):
// IsPopulated means a goal is selected (this form's closest thing to an
// instance-information-block right now — it doesn't have the full
// auto-clear/discard/modified-indicator structure Expense's does, that's
// still the separately-tracked, larger "rebuild against the settled mockup"
// task). IsDirty/IsPopulated back MainWindow's tab-header styling and this
// form's own Save button IsEnabled.
//
// TODO (2026-08-05, iterative-build pass): the "What can I allocate today"
// merged region (planning/21 Earmark Step 2, item 15) is also not built here
// — this ports ManualEarmarkWindow's simpler existing BalanceInfoText
// verbatim, not the richer merged version that was designed but never
// implemented in the window it would have replaced.
//
// FIXED 2026-08-06 (planning/23, items A4 and B): Change goal and Clear both
// now confirm before discarding unsaved edits (ConfirmDiscard, matching
// ExpenseFormPanel's own instance picker — this form was missing it on
// both, not just Change goal as this comment used to say); the isolated-
// earmark date picker now refuses out-of-range dates outright
// (UpdateEarmarkDatePickerBounds) instead of only rejecting them on save.
// Still not built: the date-picker-as-selector (marking days that already
// have an entry, clicking one to load it) and the confirm-only-on-save
// suggested-Amount case for the still-unbuilt "will miss" shortcut — both
// genuinely new UI surface, deliberately left for a dedicated pass rather
// than added here.
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

    // planning/23 item B — a light, deliberately different tone from
    // RecurrenceRuleEditor's own shortfall-highlight orange (this isn't a
    // warning, it's "something already exists here you can click into").
    private static readonly Brush ExistingEntryBrush = new SolidColorBrush(Color.FromRgb(0xC7, 0xDD, 0xF5));

    // No longer a visible TextBox (2026-08-06, the author's own call) — "for
    // a shortcut whose location we haven't figured out yet," omitted from
    // the form entirely, even in Advanced mode. The value itself still flows
    // through exactly the same Load*/Save/Summary paths it always did; only
    // the control the user directly edits it through is gone. Whatever
    // reconnects to a control later (the shortcut itself, once its own
    // placement is settled) should read/write this field.
    private decimal _startingAllocation;

    // Same fix RecurrenceRuleEditor already uses elsewhere in this app:
    // SavingsPlanRadio's XAML-declared IsChecked="True" raises Checked
    // synchronously mid-parse, while InitializeComponent is still working
    // through the rest of the tree — SavingsPlanPanel/OneOffPanel etc. don't
    // have field values yet, so UpdateModeVisibility() reads null off them.
    // _suppressEvents doesn't cover this gap (it's only ever set during a
    // Load* call, none of which have run yet at this point), so every
    // XAML-wired handler below also checks this one, which starts true and
    // flips once and for all at the end of the constructor.
    private bool _initialized;

    public EarmarkFormPanel()
    {
        InitializeComponent();

        // Amount moves into RuleEditor's own left column (Placement C,
        // settled-designs.html Earmark·1, 2026-08-06) — declared in this
        // form's own XAML so it's still normal, code-behind-wired content,
        // then handed off here once InitializeComponent has built both trees.
        SavingsPlanPanel.Children.Remove(AmountPanel);
        RuleEditor.SetLeadingContent(AmountPanel);

        AmountTextBox.TextChanged += (_, _) => { if (!_suppressEvents) { UpdateSummary(); MarkDirtyIfNotSuppressed(); } };
        RuleEditor.ResultChanged += (_, _) => { if (!_suppressEvents) { UpdateSummary(); MarkDirtyIfNotSuppressed(); } };

        _initialized = true;
    }

    // MainWindow persists whatever comes back through these — this panel
    // owns no repository itself, matching how CreateEarMarkPatternWindow/
    // ManualEarmarkWindow never did either.
    public Action<EarMarkPattern>? PatternSaved { get; set; }
    public Action<IReadOnlyList<ManualEarmark>, IReadOnlyList<(int FinanceId, DateOnly Date)>>? ManualEarmarksSaved { get; set; }

    // Computes (or returns the already-cached) live forecast on demand — same
    // "just get one, don't tell the user to go press a different button
    // first" reasoning as ExpenseFormPanel's own RequestForecast.
    public Func<ForecastResult>? RequestForecast { get; set; }

    // Fires whenever IsDirty or IsPopulated could have changed, so MainWindow
    // can restyle this form's tab header live.
    public event EventHandler? StateChanged;

    public bool IsDirty => _isDirty;

    // A goal being selected is this form's closest thing to "an instance is
    // loaded" — the author's own call, since this form doesn't have a full
    // instance-information-block (with its own blank/new state) yet.
    public bool IsPopulated => _selectedGoal is not null;

    /// <summary>[CALC] Whether this form's own Advanced mode checkbox is on — a view preference, not part of what gets saved.</summary>
    public bool IsAdvancedMode => AdvancedModeCheckBox.IsChecked == true;

    /// <summary>[UI] Supplies the goals/patterns/manual-earmarks/transfer-ids/forecast this panel reads from. Call before any Load* method, and again after every save so the next load sees current data.</summary>
    public void SetContext(
        IReadOnlyList<FinancialPattern> goals,
        IReadOnlyList<EarMarkPattern> patterns,
        IReadOnlyList<ManualEarmark> manualEarmarks,
        IReadOnlySet<int> transferFinanceIds,
        ForecastResult? forecast)
    {
        _goals = goals;

        // BUG FOUND AND FIXED 2026-08-06: this used to key straight off
        // FinanceId (`patterns.ToDictionary(p => p.FinanceId)`), which throws
        // ("same key already added") the instant a goal has more than one
        // EarMarkPattern — exactly the shape planning/17 (F27) legalized (a
        // second concurrent funder, or a break-off/restructure predecessor +
        // successor). Every goal in the picker would read as having no plan
        // at all, since this method would never finish running. Grouped and
        // resolved to the most-recently-started segment per goal instead —
        // correct for a break-off/restructure chain (the latest Start really
        // is the current one, same reasoning BreakOffFactory.FindCurrentSegment
        // uses for FinancialPattern); an honest, arbitrary-but-deterministic
        // stopgap for true concurrent funders, where neither is more "current"
        // than the other. The real fix is the disambiguation popup planning/21
        // already calls for when a goal has more than one plan — not built yet;
        // this dictionary needs some single answer in the meantime regardless.
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

        _suppressEvents = false;
        UpdateModeVisibility();
        UpdateSummary();
        ClearDirty();
    }

    /// <summary>[STEP] Loads an existing EarMarkPattern for editing — the goal is fixed (FinanceId is the link, and it can't change once created), only amount/timing can.</summary>
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
        RuleEditor.LoadFrom(existing.DatePattern);

        _suppressEvents = false;
        UpdateModeVisibility();
        UpdateSummary();
        ClearDirty();
    }

    /// <summary>[STEP] planning/14 item D-1: turns an automatically-filled jar into a savings plan the user owns. Goal fixed, starting allocation pre-filled from what the jar already holds so pressing Save never moves money — it only changes what governs the jar from here on.</summary>
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

        _suppressEvents = false;
        UpdateModeVisibility();
        UpdateSummary();
        ClearDirty();
    }

    /// <summary>[STEP] One-off adjustment mode, blank (add, optionally a specific goal preselected) or pre-filled (edit) — validation policy unchanged from ManualEarmarkWindow: a withdrawal/move exceeding what the fund holds is blocked, an over-free add warns but is allowed.</summary>
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

        // planning/23 item A4: an isolated earmark's date can never fall
        // outside its own EarMarkPattern's span (3.13.8.a2) — made
        // structurally impossible here, not just rejected on save, matching
        // ManualEarmarkWindow's own retired DisplayDateStart/End before this
        // form replaced it. Called before either branch below sets
        // SelectedDate, so a fresh selection always lands inside the range.
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
        // BUG FOUND AND FIXED (2026-08-07): the XAML move that put Summary
        // outside SavingsPlanPanel only changed where it renders — nothing
        // actually refreshed its content on this path, so One-off mode was
        // showing whatever Summary last had from Savings-plan mode (or
        // nothing at all). UpdateOneOffInfo alone was never enough; it only
        // ever populated BalanceInfoText, a different, older control.
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

    // Opens FinancialPatternPickerWindow — same mechanism ExpenseFormPanel's
    // own "Change..." uses, per the author's own call to keep this
    // consistent everywhere a FinancialPattern gets picked. Deliberately
    // shows every eligible pattern, not just ones with an existing plan —
    // picking one without a plan while in One-off mode surfaces as
    // SaveOneOff's existing "Pick a goal with a savings plan to adjust"
    // error instead of being filtered out of the list up front.
    //
    // Confirm-before-discard added (planning/23, closing the gap this file's
    // own header comment used to flag): ExpenseFormPanel's instance picker
    // already asks before switching away from unsaved work; this form didn't.
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

    // Same helper ExpenseFormPanel already has, copied rather than shared
    // across a base class (matches this project's existing per-panel style —
    // neither panel derives from a common form base today).
    private bool ConfirmDiscard(string message) =>
        MessageBox.Show(Window.GetWindow(this), message, "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            == MessageBoxResult.Yes;

    // planning/23 item A4: keeps an isolated earmark's date inside its own
    // EarMarkPattern's span, structurally — the calendar simply doesn't offer
    // an out-of-range day, rather than accepting one and rejecting it on
    // save. If the goal just changed and the currently-picked date no longer
    // fits, it's cleared rather than left silently invalid; UpdateOneOffInfo
    // already handles a null SelectedDate.
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
            RuleEditor.LoadFrom(existing.DatePattern);
        }
        else
        {
            AmountTextBox.Text = string.Empty;
            _startingAllocation = 0m;
            // TODO: RecurrenceRuleEditor has no public "reset to defaults"
            // beyond its own constructor — picking a goal with no existing
            // plan leaves whatever schedule was already on screen rather than
            // resetting it. Minor rough edge, not a correctness problem.
        }
    }

    // planning/21: "a 'clear the form' control... If unsaved edits exist,
    // this asks for confirmation first" — ExpenseFormPanel's Clear already
    // does this; this form's didn't (a second gap alongside Change goal's,
    // not called out in this file's own header comment, but the same
    // omission).
    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (_isDirty && !ConfirmDiscard("Clear the form and lose your unsaved changes?"))
        {
            return;
        }

        LoadForNewPattern();
    }

    // Switching modes by hand (not via "+ Add manual earmark," which calls
    // LoadOneOff itself) only toggles which panel shows — it doesn't reload
    // data, so an in-progress edit in the panel being hidden isn't lost.
    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _suppressEvents)
        {
            return;
        }

        UpdateModeVisibility();
        // Same gap as LoadOneOff (see its own BUG FOUND AND FIXED comment) —
        // Summary now lives outside the mode-switching Grid, so toggling the
        // radio button needs its own explicit refresh too.
        UpdateSummary();
        MarkDirtyIfNotSuppressed();
    }

    // Advanced mode (the author's own call, 2026-08-06). No _initialized
    // guard needed — this checkbox has no XAML default value to fire early,
    // unlike SavingsPlanRadio above. Scoped narrowly to what's actually
    // settled: planning/21's own Advanced-mode section only ever designed
    // Expense's behavior, never Earmark's — this form gets the same checkbox
    // for consistency, wired to the one piece of content that IS settled
    // (RecurrenceRuleEditor's RRULE box), nothing more invented on top. A
    // view preference, not data: doesn't call MarkDirty.
    private void OnAdvancedModeChanged(object sender, RoutedEventArgs e) =>
        RuleEditor.SetAdvancedMode(AdvancedModeCheckBox.IsChecked == true);

    // Unwired from any control for now (2026-08-06) — the "+ Add manual
    // earmark on this goal" button this used to back is removed from the
    // layout until we settle where it belongs (planning/21 doesn't place it
    // either). Left in place, untouched, as the machinery for whenever it
    // gets a home.
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

        // planning/23 item B, REVISED — the date field never locks (the
        // author's own correction: a user should be able to look around
        // freely without getting stuck editing whatever they clicked out of
        // curiosity). Guarded to EarmarkDatePicker specifically, since this
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

    // Future only (>= today) — matches planning/21 item 10's own "haven't
    // happened yet" restriction on the Savings-plan-mode overview, so
    // "selectable" isn't defined two different ways in the same form.
    private ManualEarmark? FindExistingEntryOn(DateOnly date) =>
        _selectedGoal is { } goal && date >= DateOnly.FromDateTime(DateTime.Today)
            ? _existingManualEarmarks.FirstOrDefault(m => m.FinanceId == goal.FinanceId && m.Date == date)
            : null;

    // planning/23 item B: colors the calendar days that already have an
    // isolated earmark for the selected goal, the moment the dropdown opens
    // — reusing RecurrenceRuleEditor's own CalendarDayButton-walking
    // technique (a DatePicker's popup calendar is the same underlying
    // control). Recomputed fresh every open rather than cached, so it's
    // always current for whichever goal is selected at that moment. Known,
    // accepted rough edge: paging to a different month while the dropdown
    // stays open doesn't re-mark until it's closed and reopened — the
    // common case (today's month) always works.
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

        // planning/21's instance-information-block rule ("the form clears
        // itself automatically after a successful save"), wired 2026-08-05 —
        // before invoking the callback, same reasoning ExpenseFormPanel.Save
        // already uses: unconditional even though the callback's own
        // navigation (Forecast tab, wired 2026-08-06) takes the user elsewhere.
        LoadForNewPattern();
        PatternSaved?.Invoke(pattern);
    }

    // Ported from ManualEarmarkWindow verbatim (Merge/RequireFundsCover/
    // WarnIfOverFree/BalancesOn below) — same validation policy, just reading
    // the source fund from this panel's own goal picker instead of a
    // separate JarComboBox, since the goal is already chosen at the top of
    // this same form.
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

        // BUG FOUND AND FIXED (2026-08-07): was the no-arg GetTimeline(),
        // which only ever looks at PrimaryAccountPage — silently always
        // (0m, 0m) for any goal on a non-Primary account (Savings, Joint
        // Household, ...), not just an occasional miss. The over-commit
        // warning below was reading "nothing free" for those goals no
        // matter what was actually there.
        var entry = _forecast.GetTimeline(financeId).LastOrDefault(candidate => candidate.Date <= date);
        if (entry is null)
        {
            return (0m, 0m);
        }

        var jar = entry.Snapshot.FundJars.FirstOrDefault(candidate => candidate.FinanceId == financeId);
        return (jar?.ExpectedAmount ?? 0m, entry.Snapshot.ExpectedFreeAmount ?? 0m);
    }

    // planning/23 ("the starting point region"): amount + date only, real
    // data — StartingAllocation (the break-off case, fully unambiguous) plus
    // any ManualEarmark dated exactly on the plan's own ActiveStart (the
    // front-load-or-manual case; which of those two it was is still an open
    // question, not guessed at here — see this region's own XAML comment).
    // Hidden entirely when the total is $0 — an ordinary plan with nothing
    // here worth showing, not an error state.
    private void UpdateStartingPointRegion()
    {
        if (_selectedGoal is not { } goal || !_patternsByFinanceId.TryGetValue(goal.FinanceId, out var pattern))
        {
            StartingPointRegion.Visibility = Visibility.Collapsed;
            return;
        }

        var total = GetStartingPointTotal(pattern);
        if (total <= 0m)
        {
            StartingPointRegion.Visibility = Visibility.Collapsed;
            return;
        }

        StartingPointRegion.Visibility = Visibility.Visible;
        StartingAmountText.Text = $"{total:C}";
        StartingDateText.Text = $"as of {pattern.DatePattern.ActiveStart:MMM d, yyyy}";
    }

    // Shared by the Starting-point region and the Summary chart's own
    // actual-line start (UpdateSummary, below) — how much was already in
    // the jar before the plan's own regular occurrences began (planning/23
    // item A2): StartingAllocation (the break-off case) plus any
    // ManualEarmark dated exactly on the plan's own ActiveStart (the
    // front-load-or-manual case).
    private decimal GetStartingPointTotal(EarMarkPattern pattern)
    {
        var activeStart = pattern.DatePattern.ActiveStart;
        var manualAtStart = _existingManualEarmarks.FirstOrDefault(m => m.FinanceId == pattern.FinanceId && m.Date == activeStart);
        return pattern.StartingAllocation + (manualAtStart?.Amount ?? 0m);
    }

    // planning/23 addendum, 2026-08-07: wired to real PlanHealthState/FundJar
    // data (via PlanHealthMessages, so the wording matches planning/22 §6
    // exactly rather than being composed ad hoc here), in both Savings-plan
    // and One-off mode. One known, deliberate gap: this shows the SAVED
    // plan's real health, not a live hypothetical of an in-progress, unsaved
    // Amount/Schedule edit — that would mean re-running the whole forecast
    // with the typed-but-unsaved values substituted in, a bigger task than
    // this pass. Item 23-D's "everything derived reads live" still holds for
    // the narrative sentence and the Starting-point region (both read
    // straight off the form's own current fields); it does not yet hold for
    // the forecast-derived health figures specifically.
    private void UpdateSummary()
    {
        UpdateStartingPointRegion();

        if (_selectedGoal is not { } goal)
        {
            // Was a silent no-op — left the Summary region showing whatever
            // it last had (blank, right after construction), which read as
            // "the Summary region isn't even there" once LoadForNewPattern
            // started leaving the goal unselected (2026-08-06). The
            // GroupBox itself stays visible either way; only its content
            // changes here.
            Summary.Clear("Pick a goal above to see its savings plan summary.");
            return;
        }

        if (!_patternsByFinanceId.TryGetValue(goal.FinanceId, out var plan))
        {
            // No savings plan exists yet for this goal — nothing
            // PlanHealthState can say about a plan that isn't saved. Same
            // "nothing to summarize yet" shape the old placeholder covered,
            // narrowed to the one case it's actually still needed for.
            Summary.Clear("No savings plan yet for this goal — fill in the fields below to create one.");
            return;
        }

        var goalAmount = Math.Abs(goal.Amount);
        var dueDate = goal.DatePattern.Until;
        var label = string.IsNullOrWhiteSpace(goal.Description) ? goal.Source : goal.Description;
        var isOneOff = OneOffRadio.IsChecked == true;

        // _forecast is the same cached-on-SetContext field BalancesOn already
        // reads (set once per tab visit, not recomputed per keystroke) —
        // matches this file's own established convention rather than
        // introducing a second, parallel forecast-access path.
        var health = _forecast?.PlanHealthStates.FirstOrDefault(p => p.FinanceId == goal.FinanceId);

        // BUG FOUND AND FIXED (2026-08-07, author's own report): the chart's
        // milestone line was going in as a single point-in-time value
        // extrapolated backward as one straight segment from $0 — for a plan
        // whose contribution and release land the same day (Mobile Carrier),
        // today's real MilestoneAmount legitimately reads $0, which made that
        // straight line degenerate into the exact same flat segment as the
        // (also $0) actual line — invisible, not merely unhelpful. Replaced
        // with a real walked trajectory (GetJarTrajectory below), so the
        // chart draws the genuine reset-and-climb shape instead of a single
        // extrapolated point.
        var trajectory = GetJarTrajectory(goal.FinanceId, dueDate);
        var jar = trajectory.Count > 0 ? trajectory[0].Jar : null;

        // BUG FOUND AND FIXED (2026-08-07, author's own report): the chart's
        // milestone line only ever covered today onward, same split as the
        // actual line above — but unlike ExpectedAmount, MilestoneAmount
        // doesn't need real transaction history at all (it's pure pattern
        // math: TransactionLogBookFactory.ComputeMilestoneTrajectory's own
        // header comment has the full reasoning), so there was never a real
        // reason to withhold the pre-Today portion. Spans the whole plan,
        // Start through the due date, not just Today onward. All
        // EarMarkPatterns sharing this FinanceId (F27 concurrency — a
        // break-off chain's predecessor + successor both feed the same
        // milestone), gathered from the forecast's own already-loaded
        // accounts rather than a fresh repository read.
        var patternsForMilestone = _forecast?.Accounts
            .SelectMany(account => account.Page.EarmarkPatterns)
            .Where(p => p.FinanceId == goal.FinanceId)
            .ToList() ?? [];
        var milestoneTrajectory = TransactionLogBookFactory.ComputeMilestoneTrajectory(
            patternsForMilestone, goal, plan.DatePattern.ActiveStart, dueDate);

        string narrative;
        string asideLine;
        string? asideSecondaryLine = null;

        if (isOneOff)
        {
            narrative = $"We need {goalAmount:C0} for {label} by {dueDate:MMM d, yyyy}.";
            if (decimal.TryParse(OneOffAmountTextBox.Text, out var proposedAmount) && proposedAmount > 0m)
            {
                var verb = IsMove ? "moving" : IsWithdraw ? "withdrawing" : "adding";
                var dateText = EarmarkDatePicker.SelectedDate is { } picked
                    ? $" on {DateOnly.FromDateTime(picked):MMM d, yyyy}"
                    : string.Empty;
                narrative += $" This one-off adjustment is {verb} {proposedAmount:C0}{dateText}.";
            }

            // planning/22 §6c, One-off mode: today's actual state
            // (CurrentJarStateLine), not the due-date projection —
            // SummaryFutureLine is the Savings-plan-mode framing below.
            asideLine = jar is not null && health is not null
                ? PlanHealthMessages.CurrentJarStateLine(jar, health)
                : "(fund jar state needs a live forecast — not available yet)";
        }
        else
        {
            decimal.TryParse(AmountTextBox.Text, out var enteredAmount);
            var start = RuleEditor.Result?.Start ?? DateOnly.FromDateTime(DateTime.Today);
            narrative = enteredAmount > 0m
                ? $"We need {goalAmount:C0} for {label} by {dueDate:MMM d, yyyy}. We plan to set aside {enteredAmount:C0} per occurrence, starting {start:MMM d, yyyy}."
                : $"We need {goalAmount:C0} for {label} by {dueDate:MMM d, yyyy}.";

            if (jar is not null && health is not null)
            {
                asideLine = PlanHealthMessages.SummaryFutureLine(jar, health.Shortfall, health.MostImportantHealthState)
                    ?? PlanHealthMessages.CurrentJarStateLine(jar, health);
                asideSecondaryLine = PlanHealthMessages.SummaryRecurringPhrase(health);
            }
            else
            {
                asideLine = "(fund jar state needs a live forecast — not available yet)";
            }
        }

        // settled-designs.html (the current mockup, superseding planning/22
        // §6c's own older prose-only description of a repeating-only
        // milestone line): a committed-plan milestone line applies to every
        // goal with a savings plan, one-time or repeating — not gated to
        // repeating patterns the way the first cut of this region had it.
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
            asideSecondaryLine: asideSecondaryLine);
    }

    // The FundJar behind a Savings Plan, walked day-by-day from today through
    // whichever comes first of `to` or the forecast's own HorizonEndDate.
    // Real data, not a hypothetical: TransactionLogBookFactory only ever
    // cascades day-by-day balances forward from AsOfDate — there is no
    // historical BalanceRecord before today (planning/22 §7's own
    // "backward-history gap"), so this can only ever start at today, never
    // at the plan's own original Start (unlike MilestoneAmount, which needs
    // no such split — see ComputeMilestoneTrajectory's own header comment).
    // UpdateSummary's chart accounts for that split explicitly rather than
    // pretending the whole span is real. Same _forecast/GetTimeline lookup
    // BalancesOn already uses, walked across a range instead of a single
    // date.
    private IReadOnlyList<(DateOnly Date, FundJar Jar)> GetJarTrajectory(int financeId, DateOnly to)
    {
        if (_forecast is null)
        {
            return [];
        }

        // BUG FOUND AND FIXED (2026-08-07): was the no-arg GetTimeline() —
        // see BalancesOn's own identical fix, same root cause.
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
