using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// planning/21 "Form layout, top to bottom" (the general 3-region pattern) +
// "Expense" content inventory, built 2026-08-05. Rebuilt from an earlier pass
// that only ported the old CreateFinancialPatternWindow popup's fields
// without the surrounding architecture — that pass shipped a form with no
// instance-information-block, no characterization-field-block, one save
// button instead of the settled two, and no feedback after a save (the
// settled "clears itself automatically" rule was simply missing). This
// version builds all three regions for real:
//   1. Instance-information-block (top Border) — which Expense is loaded, a
//      dirty indicator, Discard/Clear controls, and a "Change..." button
//      (opens FinancialPatternPickerWindow, wired 2026-08-06) doubling as
//      this form's edit entry point — the same shape every form's
//      instance-info-block uses except Account's (the author's own call).
//   2. Characterization-field-block (first GroupBox) — Direction, Repeats?,
//      and a computed "Bill"/"Paycheck"/"Custom" label.
//   3. Everything else (second GroupBox + the Due-date/Stops/RuleEditor
//      region below it, contextual on Repeats?).
//
// TODO (2026-08-05): the break-off-mode toggle (existing, recurring
// instances only) stays unwired,
// matching its pre-existing "not wired into the app" status from before this
// panel existed — also the author's call. Save/Save-and-Plan button
// emphasis (muted-until-dirty, extra outline when the plan is meaningfully
// affected, driven by the four plan-health states) is not built — ties to the
// same "needs a live forecast" gap EarmarkFormPanel's Summary aside already
// carries as its own TODO. "Keeps going" stays disabled (see its own note
// below). The advanced hand-built-single-occurrence escape hatch planning/21
// flags as "genuinely open, not resolved" is not built — a one-time Expense
// only ever gets the plain Due-date field.
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

    private IReadOnlyList<FinancialPattern> _existingPatterns = [];
    private IReadOnlyDictionary<int, int> _accountIdByFinanceId = new Dictionary<int, int>();
    private IReadOnlyList<Account> _accounts = [];
    private IReadOnlySet<int> _transferFinanceIds = new HashSet<int>();
    private IReadOnlyDictionary<int, EarMarkPattern> _patternsByFinanceId = new Dictionary<int, EarMarkPattern>();

    // Computes (or returns the already-cached) live forecast on demand —
    // wired to MainWindow.EnsureForecast, which always succeeds rather than
    // requiring the user to have pressed "Forecast" first (the author's own
    // call: everything needed to compute one — the As-Of/Horizon pickers —
    // already has a value at all times, so there's nothing to actually wait
    // on the user for).
    public Func<ForecastResult>? RequestForecast { get; set; }

    // (pattern, accountId, isNew, jumpToEarmark) — jumpToEarmark distinguishes
    // Save and Plan from Save and Skip planning; isNew tells MainWindow's
    // callback whether to also run AutoCreateAllocationPlan, matching the old
    // split between the create and edit entry points.
    public Action<FinancialPattern, int, bool, bool>? PatternSaved { get; set; }

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

    /// <summary>[UI] Supplies the existing patterns/accounts this panel reads from, which account each pattern is currently filed under, which finance ids are transfer legs (needed to open FinancialPatternPickerWindow, alongside RequestForecast), and every EarMarkPattern (so the Summary region can find this Expense's own linked plan, if it has one). Call before any Load* method, and again after every save.</summary>
    public void SetContext(
        IReadOnlyList<FinancialPattern> existingPatterns,
        IReadOnlyDictionary<int, int> accountIdByFinanceId,
        IReadOnlyList<Account> accounts,
        IReadOnlySet<int> transferFinanceIds,
        IReadOnlyList<EarMarkPattern> earMarkPatterns)
    {
        _existingPatterns = existingPatterns;
        _accountIdByFinanceId = accountIdByFinanceId;
        _accounts = accounts;
        _transferFinanceIds = transferFinanceIds;

        // BUG FOUND AND FIXED 2026-08-06 — same defect as EarmarkFormPanel's
        // own SetContext (see its comment): keying straight off FinanceId
        // throws the moment a goal has more than one EarMarkPattern (F27,
        // planning/17 — a concurrent second funder, or a break-off/
        // restructure chain), which would silently break this form's
        // "Currently saved toward this" context for every Expense, not just
        // the one with multiple plans, since SetContext would never finish.
        // Same stopgap resolution: most-recently-started segment per goal.
        _patternsByFinanceId = earMarkPatterns
            .GroupBy(pattern => pattern.FinanceId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(p => p.DatePattern.Start).First());
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
        _suppressEvents = false;
        ClearDirty();
    }

    /// <summary>[STEP] Loads an existing pattern for editing — FinanceId is fixed. Also reachable by picking it from this form's own instance picker, not just the list tab's "Edit Selected."</summary>
    public void LoadPattern(FinancialPattern existing, int currentAccountId)
    {
        _isNew = false;
        _financeId = existing.FinanceId;
        _loadedMode = LoadedMode.Editing;
        _loadedExisting = existing;
        _loadedAccountId = currentAccountId;
        _suppressEvents = true;

        ResetSharedFields(selectedAccountId: currentAccountId);
        SourceTextBox.Text = existing.Source;
        DescriptionTextBox.Text = existing.Description;
        PriorityTextBox.Text = existing.Priority.ToString();
        AmountTextBox.Text = Math.Abs(existing.Amount).ToString(CultureInfo.InvariantCulture);

        DirectionPanel.Visibility = Visibility.Visible;
        ExpenseRadioButton.IsChecked = existing.Amount < 0;
        IncomeRadioButton.IsChecked = existing.Amount >= 0;
        UnskippableRadioButton.IsChecked = existing.Mandatory;
        SkippableRadioButton.IsChecked = !existing.Mandatory;

        // Same "how many occurrences" check ExpenseKind already uses to tell
        // a one-time goal from a repeating one.
        var isOneTime = existing.DatePattern.GetOccurrences().Count == 1;
        OneTimeRadioButton.IsChecked = isOneTime;
        RepeatingRadioButton.IsChecked = !isOneTime;
        UpdateRepeatsVisibility();

        if (isOneTime)
        {
            DueDatePicker.SelectedDate = existing.DatePattern.Start.ToDateTime(TimeOnly.MinValue);
        }
        else
        {
            RuleEditor.LoadFrom(existing.DatePattern);
            StopOnDateRadio.IsChecked = true;
            StopPaidOffRadio.IsChecked = false;
            UpdateStopMode();
            RuleEditor.SetHostEndDate(existing.DatePattern.Until);
            StopEndDatePicker.SelectedDate = existing.DatePattern.Until.ToDateTime(TimeOnly.MinValue);
        }

        UpdateDirectionDependentUi();
        UpdateSelectedInstanceText();
        UpdateSummary();
        _suppressEvents = false;
        ClearDirty();
    }

    // Shared reset every Load* starts with.
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

    // Description-or-Source fallback matches GoalOption's own convention
    // elsewhere in this app (EarmarkFormPanel) — one consistent rule for
    // "what do we call this pattern out loud" everywhere it's shown.
    private void UpdateSelectedInstanceText() =>
        SelectedInstanceText.Text = _loadedExisting is { } existing
            ? (string.IsNullOrWhiteSpace(existing.Description) ? existing.Source : existing.Description)
            : "— New Expense —";

    // This Expense's own linked savings plan, shown read-only — the same
    // Summary region Earmark shows, present even in the plain healthy state
    // (the author's own call, 2026-08-06), not just surfaced through a
    // warning. Reflects the loaded instance, not live field edits — the plan
    // itself is only proposed at save time and edited on the Earmark tab, so
    // there's nothing meaningfully live to react to here yet.
    private void UpdateSummary()
    {
        UpdateStatusIndicator();

        if (_loadedExisting is not { } existing)
        {
            Summary.Clear("Save this Expense to set up its savings plan.");
            return;
        }

        if (existing.Amount >= 0m)
        {
            Summary.Clear("Income doesn't need a savings plan.");
            return;
        }

        if (!_patternsByFinanceId.TryGetValue(existing.FinanceId, out var plan))
        {
            Summary.Clear("No savings plan set up for this yet.");
            return;
        }

        var goalAmount = Math.Abs(existing.Amount);
        var dueDate = existing.DatePattern.Until;
        var label = string.IsNullOrWhiteSpace(existing.Description) ? existing.Source : existing.Description;
        var narrative =
            $"We need {goalAmount:C0} for {label} by {dueDate:MMM d, yyyy}. We plan to set aside {Math.Abs(plan.Amount):C0} per occurrence, starting {plan.DatePattern.Start:MMM d, yyyy}.";

        // TODO: same as EarmarkFormPanel's own note — the aside and the
        // actual-balance line both stay placeholders until a live
        // ForecastResult reaches this panel too. The milestone line COULD
        // be wired already (TransactionLogBookFactory.ComputeMilestoneTrajectory
        // needs no forecast, just the saved patterns this panel already has)
        // — left for that same follow-up rather than done piecemeal here.
        Summary.Load(
            narrative,
            start: plan.DatePattern.Start,
            asOfDate: DateOnly.FromDateTime(DateTime.Today),
            dueDate: dueDate,
            startAmount: plan.StartingAllocation,
            goalAmount: goalAmount,
            actualTrajectory: [],
            milestoneTrajectory: [],
            asideLine: "(fund jar state needs a live forecast — not wired in yet)");
    }

    // planning/21's own settled status indicator (author, 2026-08-07: "one
    // of the first things we made that health state class to handle"):
    // which of the four plan-health states the linked savings plan is in,
    // shown next to the save buttons — the label alone (PlanHealthMessages.
    // ExpenseStatusLabel does the actual mapping), plus the same outline
    // "Save and Plan" gets when the pending edit would meaningfully affect
    // the linked plan (planning/21, "Save and Plan... gets an extra outline
    // when the pending change is one that would meaningfully affect the
    // linked plan"). Only the "linked plan is currently in a non-Healthy
    // state" half of that trigger is built here — comparing the
    // currently-typed fields against what's saved to catch an edit that
    // would newly cause one of these states is a separate, more involved
    // check (which fields even count is not settled anywhere), not
    // silently assumed to be covered by this.
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

    private int NextFinanceId() =>
        _existingPatterns.Select(pattern => pattern.FinanceId).DefaultIfEmpty(0).Max() + 1;

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

    // Also gated on _initialized, not just _suppressEvents: a few fields
    // carry XAML default values (PriorityTextBox's Text="5" is the one that
    // actually fires) that raise their change event mid-InitializeComponent,
    // before any Load* call has run to set _suppressEvents at all — without
    // this, the form would read "unsaved changes" the instant it's built.
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

        // The author's own rule: a save button looks active only when there
        // are unsaved changes — a blank form or an unchanged loaded instance
        // both read as "nothing to save" the same way.
        SaveAndSkipPlanningButton.IsEnabled = _isDirty;
        SaveAndPlanButton.IsEnabled = _isDirty;

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool ConfirmDiscard(string message) =>
        MessageBox.Show(Window.GetWindow(this), message, "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            == MessageBoxResult.Yes;

    // planning/21: "a 'clear the form' control — starts a brand new, blank
    // instance. If unsaved edits exist, this asks for confirmation first."
    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (_isDirty && !ConfirmDiscard("Clear the form and lose your unsaved changes?"))
        {
            return;
        }

        LoadForNewPattern();
    }

    // planning/21: "a 'discard unsaved edits' control must exist" — no
    // confirmation called for (unlike Clear); the whole point of the button
    // is discarding, so a second confirmation would be redundant.
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

    // Opens FinancialPatternPickerWindow — the same reusable "pick one of my
    // Bills/Paychecks/Goals" popup every form's instance-info-block uses
    // except Account's (the author's own call, 2026-08-06). Its own source
    // of truth is the live forecast's TransactionLogBook, not a plain
    // repository read, so it sees a pattern whose occurrences fall entirely
    // outside the forecast's own window. RequestForecast always succeeds
    // (the author's own call, 2026-08-06 — computing one on demand beats
    // telling the user to go press a different button first), so there's no
    // null/error branch to handle here at all.
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

    // Advanced mode (the author's own call, 2026-08-06). No _initialized
    // guard needed — unlike the radios above, this checkbox has no XAML
    // default value to fire early. Scoped narrowly to what's actually
    // settled: planning/21's Advanced-mode section only ever designed
    // Expense's own behavior, and even there left two things undecided
    // ("Repeats? stops doing anything," exact wording TBD; the recurrence
    // preview possibly getting replaced for a single-occurrence RRule) —
    // neither of those is built here. This is a view preference, not data:
    // it doesn't call MarkDirty.
    private void OnAdvancedModeChanged(object sender, RoutedEventArgs e) =>
        RuleEditor.SetAdvancedMode(AdvancedModeCheckBox.IsChecked == true);

    private void UpdateDirectionDependentUi()
    {
        AmountLabel.Text = ExpenseRadioButton.IsChecked == true ? "Amount owed" : "Amount received";
        UpdateSkippableVisibility();
        UpdateCharacterizationText();
    }

    // planning/14 item B-4: the skippable question only means something for
    // money going OUT, so it disappears for income entirely.
    private void UpdateSkippableVisibility() =>
        MandatoryPanel.Visibility = ExpenseRadioButton.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;

    // planning/21 §2: reuses names already used elsewhere in the UI's own
    // shortcut buttons. "Create Bill..." is the only named shortcut Expense
    // has today, so "Bill" is the only non-Custom shape recognized on the
    // expense side; "Paycheck" is offered on the income side even though no
    // shortcut button is named that yet, matching the domain's own
    // bill-vs-paycheck framing. Always shows something, never blank.
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

            var pattern = FinancialPattern.Create(new FinancialPatternOptions
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

            var accountId = SelectedAccountId;
            var isNew = _isNew;

            // planning/21: "the form clears itself automatically after a
            // successful save." Done before invoking the callback, so it
            // happens unconditionally even though the callback's own
            // navigation (Forecast vs. Earmark) takes the user elsewhere —
            // this Expense tab should read blank whichever way they arrived
            // back at it next.
            LoadForNewPattern();

            PatternSaved?.Invoke(pattern, accountId, isNew, jumpToEarmark);
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
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
                Start = DateOnly.FromDateTime(date),
                Count = 1,
            });
        }

        return RuleEditor.Result ?? throw new InvalidOperationException("Fix the recurrence rule before continuing.");
    }

    // planning/15 item D — the "when does this stop?" question, now shown
    // for every repeating Expense (planning/21 Step 2), not only the old
    // "Create Bill" shortcut. -------------------------------------------

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
