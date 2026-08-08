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

    // Author, 2026-08-07: the OTHER starting-point source — the isolated
    // earmark (ManualEarmark) dated exactly on the plan's own ActiveStart,
    // not StartingAllocation (which only a break-off ever sets). Populated
    // on Load*, edited live via StartingEarmarkAmountTextBox (visible only
    // when UsersCanEditFundStartPoint), written on Save (SaveSavingsPlan's
    // own delete-if-zero rule, 3.13c.8.a5).
    private decimal _startingEarmarkAmount;

    // The ActiveStart this plan's isolated earmark was actually loaded
    // against — null when there was no saved plan to load one from. Save
    // compares this to the pattern's own (possibly just-edited) ActiveStart:
    // if the Start date moved, whatever was sitting at THIS old date is
    // orphaned and needs deleting too, not just left behind (author's own
    // instruction, 2026-08-07).
    private DateOnly? _loadedActiveStart;

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
        _startingEarmarkAmount = 0m;
        StartingEarmarkAmountTextBox.Text = string.Empty;
        _loadedActiveStart = null;

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
        _startingEarmarkAmount = GetStartingEarmarkAmount(existing);
        StartingEarmarkAmountTextBox.Text = _startingEarmarkAmount == 0m ? string.Empty : _startingEarmarkAmount.ToString(CultureInfo.InvariantCulture);
        _loadedActiveStart = existing.DatePattern.ActiveStart;
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
        // No EarMarkPattern exists yet to have an isolated earmark on —
        // this jar's balance came from automatic reservation, not a plan.
        _startingEarmarkAmount = 0m;
        StartingEarmarkAmountTextBox.Text = string.Empty;
        _loadedActiveStart = null;

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
            // TODO: RecurrenceRuleEditor has no public "reset to defaults"
            // beyond its own constructor — picking a goal with no existing
            // plan leaves whatever schedule was already on screen rather than
            // resetting it. Minor rough edge, not a correctness problem.
        }
    }

    // Shared by every Load* path above — the isolated earmark (if any)
    // dated exactly on this pattern's own ActiveStart, same lookup
    // GetStartingPointTotal already does for display, reused here so
    // _startingEarmarkAmount starts out matching whatever's actually saved.
    private decimal GetStartingEarmarkAmount(EarMarkPattern pattern) =>
        _existingManualEarmarks.FirstOrDefault(m => m.FinanceId == pattern.FinanceId && m.Date == pattern.DatePattern.ActiveStart)?.Amount ?? 0m;

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

        // BUG FOUND AND FIXED (2026-08-08, author's own report: no addition
        // line in One-off mode, even after typing an amount). EarmarkDatePicker
        // has no default SelectedDate in XAML, and this handler — unlike
        // LoadOneOff's own "+ New Earmark" entry point — never gave it one
        // either; it only toggled visibility. Clicking straight into the
        // "One-off adjustment" pill (rather than opening an existing entry
        // or "+ New Earmark" first) left the date genuinely null.
        // GetOneOffLiveDelta's own "SelectedDate is not {} picked" guard
        // then correctly read that as "nothing to compute" — not a math
        // bug, a missing default. Only filled in when it's still blank, so
        // an already-loaded entry's own date is never overwritten.
        //
        // _suppressEvents wraps the assignment on purpose: setting
        // SelectedDate programmatically still raises SelectedDateChanged,
        // and OnInputsChanged's own "landed on a day with an existing
        // entry" switch logic has no business running for a plain default
        // fill-in — the explicit UpdateSummary() call right below already
        // covers the refresh this needs.
        if (OneOffRadio.IsChecked == true && EarmarkDatePicker.SelectedDate is null)
        {
            _suppressEvents = true;
            EarmarkDatePicker.SelectedDate = DateTime.Today;
            _suppressEvents = false;
        }

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

        // Author, 2026-08-07: the starting-earmark field's own save rule.
        // 3.13c.8.a5: a $0 isolated earmark doesn't get to exist, so
        // zero+existing means delete, not save-as-zero (ManualEarmark.Create
        // rejects a zero Amount outright anyway — this is that same rule,
        // applied before Create is even attempted, not caught as an
        // exception from it).
        //
        // Latent, pre-existing inconsistency, NOT introduced here (same
        // ActiveStart GetStartingPointTotal already reads for display):
        // ManualEarmark.Create validates its own Date against
        // DatePattern.Start literally, not ActiveStart — the two only ever
        // coincide today because nothing currently gives an EarMarkPattern
        // its own ActiveFrom lead-in distinct from Start (WithActiveFrom is
        // only ever called on the GOAL side, AllocationPlanProposer). Would
        // need real attention if that ever changes.
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
            // else: zero and nothing there either — the author's own
            // explicit no-op case.
        }
        else
        {
            savedEarmarks.Add(ManualEarmark.Create(
                new ManualEarmarkOptions { FinanceId = selectedGoal.FinanceId, Date = newActiveStart, Amount = _startingEarmarkAmount },
                pattern));
        }

        // Author, 2026-08-07: "if we have a starting earmark event and the
        // start date gets moved, we will have to make sure that old starting
        // earmark gets deleted." _loadedActiveStart is whatever ActiveStart
        // was in effect when this plan was loaded — if the Start date has
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

    // [ShowFundStartPointRegion] Whether the Starting-point region can appear at all. Author, 2026-08-07: true in Savings-plan mode, false in One-off — deliberately simple for now, expected to grow more restrictive later.
    private bool ShowFundStartPointRegion => SavingsPlanRadio.IsChecked == true;

    // [UsersCanEditFundStartPoint] Whether the user can see/edit the starting isolated earmark's own amount — needs ShowFundStartPointRegion, and locks once the pattern's own ActiveStart is in the past.
    // BUG FOUND AND FIXED (2026-08-07, author's own report): read the SAVED
    // pattern's own ActiveStart, so it could never go true for a brand-new
    // plan (nothing saved yet to read) or react to a live edit to the Start
    // date on an existing one — exactly the two cases the author was
    // testing. Reads RuleEditor.Result instead, the same live source
    // UpdateSummary's own narrative already reads, so this reacts to typing
    // the same way everything else in this pass does.
    private bool UsersCanEditFundStartPoint =>
        ShowFundStartPointRegion
        && RuleEditor.Result is { } rule
        && rule.ActiveStart >= DateOnly.FromDateTime(DateTime.Today);

    // planning/23 ("the starting point region"): amount + date only, real
    // data — StartingAllocation (the break-off case, fully unambiguous) plus
    // any ManualEarmark dated exactly on the plan's own ActiveStart (the
    // front-load-or-manual case; which of those two it was is still an open
    // question, not guessed at here — see this region's own XAML comment).
    //
    // REVISED 2026-08-07 (author's own correction): ShowFundStartPointRegion
    // is the ONLY visibility gate now, not a total-is-zero check layered on
    // top of it — the region always renders whenever it's true, in all three
    // content cases below. Was: collapsed entirely whenever the total came
    // out to $0, which silently hid it for almost every ordinary plan
    // (Trip to Japan, Car Insurance Co, Mobile Carrier, ...) — only Car
    // Lease Payment and DMV Registration ever showed it under that rule.
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
        // or a goal picked that has none (author's own call, 2026-08-07):
        // same $0 treatment as an existing zero-total plan, just without a
        // real ActiveStart to caption.
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

    // planning/23 addendum, 2026-08-07 (author's own instruction): reads the
    // CURRENTLY-TYPED Amount/Recurrence fields — "the proposed EarmarkPattern
    // in the form," not the saved plan — since the whole point is catching
    // this before Save, while there's still time to fix it. Builds the same
    // kind of throwaway EarMarkPattern SaveSavingsPlan itself constructs
    // right before persisting, never saved, just fed into the same domain
    // check (TransactionLogBookFactory.FirstOccurrenceShortfall) the saved
    // path also runs for the Summary region's own aside — one computation,
    // two callers, per the author's own "consolidate" instruction.
    private void UpdateStartingShortfallWarning()
    {
        // BUG FOUND AND FIXED (2026-08-07): now shares TryBuildProposedPattern
        // with GetLiveJarAmounts instead of constructing its own copy —
        // was harmless duplication on its own, but kept two independent
        // places that both needed to stay in sync with EarMarkPattern.Create's
        // own validation, exactly the kind of drift risk the author's own
        // "consolidate" instruction is about avoiding.
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

    // BUG FOUND AND FIXED (2026-08-07): UpdateStartingShortfallWarning used
    // to pass _existingManualEarmarks straight through, which meant
    // whatever's live in StartingEarmarkAmountTextBox never actually
    // factored into its own warning — a $500 starting earmark being typed
    // in still read as "$0 accumulated" until Save. Substitutes the live
    // _startingEarmarkAmount in place of whatever's saved at this same
    // (FinanceId, ActiveStart) pair, same "the proposed EarmarkPattern in
    // the form" principle the rest of this check already follows.
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

    // [GetOneOffLiveDelta] How much the currently-typed One-off adjustment
    // would add to today's ExpectedAmount reading, if it were saved right
    // now — so the Summary aside can react before Save is clicked, the
    // same way the Starting-point region's own warning already does.
    //
    // Only applied when the typed date falls strictly after this goal's
    // own most recent release before today (or on/after the plan's
    // ActiveStart, if it hasn't released even once yet), and on or before
    // today. Reason, with a real example already sitting in the seed data:
    // a release does NOT reset the jar's ExpectedAmount to exactly 0 — it
    // only ever subtracts the goal's own fixed Amount
    // (TransactionLogBookFactory.AppendDeallocationOrGoalReleases), so
    // whatever the jar held ABOVE that amount is left behind afterward
    // (or, if it held less, the release floors at 0). Car Insurance Co's
    // own plan shows exactly this: going into its Apr 1, 2026 release the
    // jar held $350 (three $100 monthly contributions plus the +$50
    // manual entry on Mar 1); the release only took the $300 the bill
    // itself cost, leaving $50 behind — which is still sitting in the jar
    // today, and is exactly what the Summary aside's "$50 over" reports.
    //
    // A One-off dated BEFORE that Apr 1 release, if actually saved, would
    // change how much was in the jar going into it — which would change
    // how much was left over afterward, and every day's ExpectedAmount
    // reading since, including today's. This method has no way to replay
    // that release and work out the new leftover live; rather than show a
    // wrong number, it leaves today's reading exactly where it already
    // was for a date that early. GetLiveJarAmounts's own header comment
    // carries the same limit, for the same reason. A future-dated entry
    // still contributes nothing to today's own reading either way,
    // matching what actually saving it would do.
    //
    // TODO (author's own call, 2026-08-07): not urgent, an edge case for
    // later, not blocking anything — leaving the full account here so it
    // can be picked up cold, without re-deriving any of this reasoning.
    //   THE GAP: for a One-off dated on/before this goal's own most recent
    //   release, the line below returns 0m (no live change shown) even
    //   though saving it for real WOULD change today's ExpectedAmount.
    //   Only the live PREVIEW is affected — SaveOneOff -> Merge builds the
    //   real ManualEarmark independently of this method, and the next real
    //   forecast run (TransactionLogBookFactory.CreateForecast) always
    //   computes the correct number regardless.
    //   HOW TO REPRODUCE, to confirm the gap is still real: open Car
    //   Insurance Co's savings plan (seed data, tools/SeedData/Program.cs)
    //   in One-off mode, pick Feb 1, 2026 (before its own Apr 1, 2026
    //   release), type any amount — the "Fund jar, today" aside should NOT
    //   move. If it does move, something already changed this behavior;
    //   re-read this method's current body before trusting the rest of
    //   this note.
    //   WHAT A REAL FIX NEEDS: replaying the actual day-by-day cascade
    //   (TransactionLogBookFactory.CreateForecast's own per-day loop —
    //   floor-at-0, the leftover-after-release carry described above) from
    //   the backdated date forward, not a shortcut computed here. That's
    //   either a new domain function built for this specific purpose, or
    //   accepting the cost of a full RequestForecast() re-run with the
    //   proposed entry inserted — a real design decision, not sketched out
    //   here, and not something to guess at without discussing it first.
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

    // [GetProposedOneOffManualEarmarks] One-off mode's counterpart to
    // GetProposedManualEarmarks above — substitutes the currently-typed
    // OneOffAmountTextBox/EarmarkDatePicker/Action in place of whatever's
    // already saved at that same (FinanceId, Date), mirroring Merge's own
    // "editing replaces, not stacks" rule rather than reinventing it. No
    // date-vs-today filtering here (unlike GetOneOffLiveDelta) — a manual
    // earmark dated between today and the goal's own first occurrence is
    // exactly the "upcoming earmark events" FirstOccurrenceShortfall
    // itself already knows how to count; this just needs to be in the
    // list, not pre-filtered.
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

    // The editable counterpart to AmountTextBox.TextChanged above — same
    // shape, own field. Blank/unparseable reads as 0, same convention
    // AmountTextBox's own OnSaveClick validation uses, not a separate error
    // state here (this field has no "must be filled in" requirement the way
    // Amount does — 0 is a completely valid, common value for it).
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
    // and One-off mode.
    //
    // REVISED same day (author's own follow-up: "I would like the values
    // in the isolated earmark mode to update the summary as well"):
    // One-off mode's aside now folds in whatever's currently typed
    // (OneOffAmountTextBox/EarmarkDatePicker/Action) on top of the SAVED
    // reading, live — see GetOneOffLiveDelta's own header comment for the
    // one deliberate scope limit left (a date dated before this goal's own
    // most recent release, where a release's real leftover-money effect
    // isn't replayed live). The first-payment warning is fully live either
    // way, same as the Starting-point region's own.
    //
    // REVISED again 2026-08-07 (author's own go-ahead, "that third chart
    // line would be incredibly helpful"): the chart itself no longer has
    // this gap — Savings-plan mode's own ProposedTrajectory line reacts to
    // Amount/Schedule edits live, regardless of whether a saved
    // PlanHealthState exists for this goal. What's still true, narrower
    // than before: asideLine/asideSecondaryLine (the TEXT figures, not the
    // chart) still read the SAVED PlanHealthState once one exists, rather
    // than a live recompute of an in-progress edit — full parity there
    // would mean re-running the whole forecast with the typed-but-unsaved
    // rule substituted in, a bigger task than this pass (when no saved
    // PlanHealthState exists yet, the live fallback further below already
    // covers the text too — TryBuildProposedPattern and friends). The
    // narrative sentence and the Starting-point region don't share this
    // gap; both already read straight off the form's own current fields
    // regardless of mode.
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
        var isOneTime = goal.DatePattern.GetOccurrences().Count == 1;

        // This goal's own very first occurrence, ever — the same date
        // TransactionLogBookFactory.FirstOccurrenceShortfall itself checks
        // against (that method's own private FirstOccurrence helper isn't
        // exposed, so this is the same GetOccurrences(Start, Until) call
        // written out again rather than duplicated logic under a new
        // name). Only actually used below when IsFirstOccurrencePending is
        // also true — an established bill like Car Insurance Co, whose
        // first occurrence is long past, never reads this value.
        var firstOccurrenceDate = goal.DatePattern.GetOccurrences(goal.DatePattern.Start, goal.DatePattern.Until).FirstOrDefault();

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

            // Author, 2026-08-07: "I would like the values in the isolated
            // earmark mode to update the summary as well" — today's actual
            // state (planning/22 §6c's own CurrentJarStateLine framing,
            // not the due-date projection) now folds in whatever's
            // currently typed here, same "warnings work dynamically"
            // principle the rest of this form already follows.
            // MilestoneAmount itself needs no change — 3.13.5.4.a1 only
            // ever accumulates repeated, pattern-scheduled contributions,
            // never a manual one (GetOneOffLiveDelta's own header comment
            // has the reasoning for why only ExpectedAmount can move here).
            // delta == 0m (nothing valid typed) keeps the original
            // CurrentJarStateLine wording exactly, including its own
            // health-state-gated delta (planning/22 §6a) — LiveJarStateLine's
            // simpler always-show-a-delta wording only takes over once
            // there's an actual live adjustment to reflect, since
            // CurrentJarStateLine's own gating can't be recomputed live
            // without a full forecast re-run.
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
                // A date the picker's own bounds should already exclude,
                // mid-edit — same defensive shape
                // UpdateStartingShortfallWarning's own catch uses.
                asideLine = "(fund jar state needs a live forecast — not available yet)";
            }

            // Same live substitution for the first-payment warning —
            // reuses FirstOccurrenceShortfall's own existing
            // manualEarmarks support rather than new domain math (it
            // already correctly counts a manual earmark dated between
            // today and the first occurrence as "upcoming" — see
            // FirstOccurrenceShortfall_is_a_partial_amount_when... in
            // TransactionLogBookFactoryTests).
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

            // Author, 2026-08-07: "I do like how we phrased the information
            // in the mockup a little bit better" — Earmark · 5's own
            // continuation, "We plan to set aside $420 a month toward it,"
            // for a repeating goal specifically (the author's own "fine as
            // it is" on the one-time-goal case, item 5, left THAT phrasing
            // — "per occurrence, starting [date]" — untouched). Reads the
            // PLAN's own contribution cadence (RuleEditor.Result), not the
            // goal's — they usually match but aren't the same field (a
            // monthly bill could be funded biweekly). "a {unit}" for
            // Interval<=1, "every N {unit}s" otherwise — matches "a month"
            // reading naturally where "every month... every month" (reusing
            // GoalNarrativeOpening's own phrasing verbatim) would not.
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

            narrative = enteredAmount > 0m ? $"{opening} {continuation}" : opening;

            // Author, 2026-08-07: "that third chart line would be
            // incredibly helpful" — settled-designs.html's own "proposed —
            // rough, live estimate" line, computed regardless of whether a
            // saved PlanHealthState exists (unlike asideLine/
            // asideSecondaryLine below, which still prefer the saved
            // reading when there is one — that split is unchanged). Reuses
            // ComputeMilestoneTrajectory's own new startingAllocation seed
            // (TransactionLogBookFactory) rather than a parallel
            // computation — same "no forecast needed" pattern math
            // GetLiveJarAmounts already relies on, just for the whole shape
            // instead of one "today" point.
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
                // recurring-chronic-shortfall phrase when both would
                // otherwise apply — planning/22's own "real space budget"
                // caps this region's aside at two facts, and a payment
                // about to actually fail is more time-sensitive than an
                // ongoing structural rate problem.
                asideSecondaryLine = PlanHealthMessages.FirstOccurrenceShortfallLine(health.IsFirstOccurrencePending, health.FirstOccurrenceShortfall, isOneTime)
                    ?? PlanHealthMessages.SummaryRecurringPhrase(health);
                highlightDate = health.IsFirstOccurrencePending && health.FirstOccurrenceShortfall > 0m ? firstOccurrenceDate : null;

                // Author, 2026-08-07: "it was supposed to assist highlighting
                // future EarmarkEvent occurrences where the user would fall
                // short of the milestone amount" — UnderfundedReleaseDates is
                // exactly that: this goal's own release dates, from the real
                // forecast's forward walk, where the jar came up short (its
                // own doc comment on PlanHealthState reads as past-tense, but
                // the forward walk that fills it runs from AsOfDate onward,
                // so today-or-later dates are exactly what's in it).
                // SetHighlight and RRulePreviewCaption both already existed;
                // this is the first place either gets called with real data.
                // No live-pattern-math equivalent exists for the
                // no-saved-health branches below (that would need the same
                // day-by-day floor/deallocation walk GetOneOffLiveDelta's own
                // TODO already flags as out of scope for a live preview) —
                // cleared there instead of guessed at.
                RuleEditor.SetHighlight(
                    health.UnderfundedReleaseDates,
                    PlanHealthMessages.RRulePreviewCaption(health),
                    health.UnderfundedReleaseDates.Count > 0 ? PlanHealthMessages.UnderfundedReleaseHighlightLegend : null);
            }
            else if (TryBuildProposedPattern(goal) is { } liveProposed)
            {
                // BUG FOUND AND FIXED (2026-08-07, author's own report):
                // "the summary region in general should be updating based
                // on what we have in the form — only the sentence on top of
                // it is getting updated." This branch only ever runs when
                // there's no saved forecast reading yet (a brand-new plan,
                // or one whose Start just moved past what's been computed)
                // — exactly the case the author was testing. Was a static
                // placeholder the whole time; now computes a real, live
                // reading (GetLiveJarAmounts' own header comment has the
                // full reasoning for how that's possible without a forecast).
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

        // settled-designs.html (the current mockup, superseding planning/22
        // §6c's own older prose-only description of a repeating-only
        // milestone line): a committed-plan milestone line applies to every
        // goal with a savings plan, one-time or repeating — not gated to
        // repeating patterns the way the first cut of this region had it.
        //
        // peakDates: settled-designs.html's own "Earmark · 3" section
        // labels 3 gridlines with their own dates, not every occurrence a
        // frequently-repeating pattern would have — a monthly bill plotted
        // a year-plus out would otherwise crowd a label onto every single
        // one. Empty for a one-time goal: DrawChart falls back to its
        // plain "Due {dueDate}" label for that case, which already names a
        // real, single due date — nothing to pick out of a row of repeats.
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

    // Turns a repeating pattern's own Frequency/Interval into the cadence
    // phrase the Summary narrative's opening line needs ("every 3 months",
    // "every week") — same Daily/Weekly/Monthly/Yearly unit words
    // RecurrenceRuleEditor.UpdateFormVisibility already uses for its own
    // "Every N ___(s)" field label, just written out as a plain phrase
    // instead of that label's "(s)" shorthand.
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

    // The "We plan to set aside" continuation's own cadence phrase ("a
    // month", "every 3 months") — same unit words as CadencePhrase above,
    // different article for the Interval<=1 case ("a month" reads more
    // naturally there than "every month" repeated right after
    // GoalNarrativeOpening's own "every month").
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

    // [GoalNarrativeOpening] The Summary narrative's opening sentence.
    // planning/mockups/settled-designs.html's own "Earmark · 3" section
    // (chronic shortfall, repeating bill) already settled this; it just
    // never made it into this control until now (author's own report,
    // 2026-08-07: the old wording named goal.DatePattern.Until — the
    // pattern's own far-future end, not a real due date at all for a
    // repeating pattern — alongside goalAmount, which is the PER-OCCURRENCE
    // amount; the two numbers didn't describe the same thing).
    //
    // A one-time goal keeps the existing single-transaction wording: there
    // is only one payment, so "by {dueDate}" already says the right thing,
    // and dueDate really is that payment's own due date. A repeating
    // pattern instead names its own cost and cadence and anchors on the
    // NEXT occurrence counting from today, matching Earmark · 3's own
    // "Car insurance costs $300 every 3 months — next due Oct 1, 2026."
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

    // Shared by UpdateStartingShortfallWarning and GetLiveJarAmounts below —
    // the same throwaway, never-saved EarMarkPattern SaveSavingsPlan itself
    // constructs right before persisting. Null whenever the form doesn't
    // have enough to build one yet (no rule, unparseable/non-positive
    // amount), or the rule is momentarily incompatible with the goal —
    // EarMarkPattern.Create enforces that (Start before the goal's own
    // active span, Until past its date range, ...), and the user is very
    // possibly mid-way through fixing exactly that when this runs. Nothing
    // live to show yet in that case, not a crash.
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

    // BUG FOUND AND FIXED (2026-08-07, author's own report against Trip to
    // Japan specifically): every live check so far only ever passed
    // [proposed] — ONE pattern — into TransactionLogBookFactory, silently
    // dropping any OTHER EarMarkPattern also funding this same goal (F27
    // concurrency — Trip to Japan alone has two, tripPlan + tripPlanPartner).
    // The saved-state path (CalculatePlanHealthStates) was never wrong —
    // it already gathers every pattern by FinanceId — only the live,
    // form-driven checks were undercounting. Every EarMarkPattern actually
    // funding this goal, with whichever ONE is currently loaded/being
    // edited in this form (identified the same way _patternsByFinanceId's
    // own F27 stopgap does — matching Start, since that's its own
    // resolution key) replaced by its live, not-yet-saved version; every
    // OTHER concurrent funder passes through unchanged from the saved data.
    private IReadOnlyList<EarMarkPattern> GetPatternsForLiveCheck(FinancialPattern goal, EarMarkPattern proposed)
    {
        var editedStart = _patternsByFinanceId.GetValueOrDefault(goal.FinanceId)?.DatePattern.Start;
        var otherSavedPatterns = _forecast?.Accounts
            .SelectMany(account => account.Page.EarmarkPatterns)
            .Where(p => p.FinanceId == goal.FinanceId && p.DatePattern.Start != editedStart)
            .ToList() ?? [];
        return [.. otherSavedPatterns, proposed];
    }

    // [GetLiveJarAmounts] Author, 2026-08-07: "the summary region in general
    // should be updating based on what we have in the form" — a live
    // (ExpectedAmount, MilestoneAmount) reading for today, computed purely
    // from the proposed pattern's own schedule, no forecast required.
    //
    // The trick: ComputeMilestoneTrajectory already walks "accumulate, reset
    // to 0 on release" starting from 0. Seed that SAME walk with a real
    // starting balance instead of 0, and the two walks are provably
    // identical from the first reset onward — a reset always drives BOTH
    // to exactly 0, erasing whatever the starting balance was worth by
    // then. So: once this goal has released at least once since ActiveStart,
    // live ExpectedAmount == live MilestoneAmount exactly (today's true
    // pace, no reason to reseed); before any release has happened yet, it's
    // just startingTotal + MilestoneAmount (nothing has erased the offset).
    // Means the actual walk never needs re-implementing — this reuses the
    // one that's already built and tested.
    //
    // Known, deliberate gap: like ComputeMilestoneTrajectory itself, this
    // doesn't model manual earmarks beyond the starting point (a real
    // mid-plan top-up on an EXISTING plan, e.g., isn't reflected) — same
    // "pattern math only" scope that function already carries, not a new
    // limit introduced here.
    //
    // BUG FOUND AND FIXED (2026-08-07): takes the full pattern list now,
    // same fix as UpdateStartingShortfallWarning's own — see
    // GetPatternsForLiveCheck's own header comment for why a single
    // proposed pattern isn't enough for an F27-concurrent goal.
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
