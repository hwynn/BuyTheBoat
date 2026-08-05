using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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
// TODO (2026-08-05, iterative-build pass): the instance-picker here is a
// plain ComboBox, not the real searchable popup planning/21 still calls "not
// yet designed" for either form — fine for a household's realistic list
// size today, revisit once that popup exists. The "What can I allocate
// today" merged region (planning/21 Earmark Step 2, item 15) is also not
// built here — this ports ManualEarmarkWindow's simpler existing
// BalanceInfoText verbatim, not the richer merged version that was
// designed but never implemented in the window it would have replaced.
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
    private ForecastResult? _forecast;
    private ManualEarmark? _oneOffEditTarget;
    private bool _suppressEvents;

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

        AmountTextBox.TextChanged += (_, _) => { if (!_suppressEvents) UpdateSummary(); };
        StartingAllocationTextBox.TextChanged += (_, _) => { if (!_suppressEvents) UpdateSummary(); };
        RuleEditor.ResultChanged += (_, _) => { if (!_suppressEvents) UpdateSummary(); };

        _initialized = true;
    }

    // MainWindow persists whatever comes back through these — this panel
    // owns no repository itself, matching how CreateEarMarkPatternWindow/
    // ManualEarmarkWindow never did either.
    public Action<EarMarkPattern>? PatternSaved { get; set; }
    public Action<IReadOnlyList<ManualEarmark>, IReadOnlyList<(int FinanceId, DateOnly Date)>>? ManualEarmarksSaved { get; set; }

    /// <summary>[UI] Supplies the goals/patterns/manual-earmarks/forecast this panel reads from. Call before any Load* method, and again after every save so the next load sees current data.</summary>
    public void SetContext(
        IReadOnlyList<FinancialPattern> goals,
        IReadOnlyList<EarMarkPattern> patterns,
        IReadOnlyList<ManualEarmark> manualEarmarks,
        ForecastResult? forecast)
    {
        _goals = goals;
        _patternsByFinanceId = patterns.ToDictionary(pattern => pattern.FinanceId);
        _existingManualEarmarks = manualEarmarks;
        _forecast = forecast;
    }

    /// <summary>[STEP] Blank Savings-plan form, goal picker open to any goal — the "Add New (advanced)" entry point.</summary>
    public void LoadForNewPattern()
    {
        _suppressEvents = true;
        ErrorText.Text = string.Empty;
        _oneOffEditTarget = null;
        SavingsPlanRadio.IsChecked = true;

        var options = _goals.Select(goal => new GoalOption(goal)).ToList();
        GoalComboBox.ItemsSource = options;
        GoalComboBox.IsEnabled = true;
        GoalComboBox.SelectedIndex = options.Count > 0 ? 0 : -1;

        if (options.Count > 0)
        {
            PopulateSavingsPlanFields(options[0].Pattern);
        }

        _suppressEvents = false;
        UpdateModeVisibility();
        UpdateSummary();
    }

    /// <summary>[STEP] Loads an existing EarMarkPattern for editing — the goal is fixed (FinanceId is the link, and it can't change once created), only amount/timing can.</summary>
    public void LoadPattern(EarMarkPattern existing, FinancialPattern goal)
    {
        _suppressEvents = true;
        ErrorText.Text = string.Empty;
        _oneOffEditTarget = null;
        SavingsPlanRadio.IsChecked = true;

        GoalComboBox.ItemsSource = new[] { new GoalOption(goal) };
        GoalComboBox.SelectedIndex = 0;
        GoalComboBox.IsEnabled = false;

        AmountTextBox.Text = Math.Abs(existing.Amount).ToString(CultureInfo.InvariantCulture);
        StartingAllocationTextBox.Text = existing.StartingAllocation.ToString(CultureInfo.InvariantCulture);
        RuleEditor.LoadFrom(existing.DatePattern);

        _suppressEvents = false;
        UpdateModeVisibility();
        UpdateSummary();
    }

    /// <summary>[STEP] planning/14 item D-1: turns an automatically-filled jar into a savings plan the user owns. Goal fixed, starting allocation pre-filled from what the jar already holds so pressing Save never moves money — it only changes what governs the jar from here on.</summary>
    public void LoadForMaterialize(FinancialPattern goal, decimal alreadySaved)
    {
        _suppressEvents = true;
        ErrorText.Text = string.Empty;
        _oneOffEditTarget = null;
        SavingsPlanRadio.IsChecked = true;

        GoalComboBox.ItemsSource = new[] { new GoalOption(goal) };
        GoalComboBox.SelectedIndex = 0;
        GoalComboBox.IsEnabled = false;

        AmountTextBox.Text = string.Empty;
        StartingAllocationTextBox.Text = alreadySaved.ToString(CultureInfo.InvariantCulture);

        _suppressEvents = false;
        UpdateModeVisibility();
        UpdateSummary();
    }

    /// <summary>[STEP] One-off adjustment mode, blank (add, optionally a specific goal preselected) or pre-filled (edit) — validation policy unchanged from ManualEarmarkWindow: a withdrawal/move exceeding what the fund holds is blocked, an over-free add warns but is allowed.</summary>
    public void LoadOneOff(ManualEarmark? editTarget, DateOnly? initialDate = null, int? preselectFinanceId = null)
    {
        _suppressEvents = true;
        ErrorText.Text = string.Empty;
        _oneOffEditTarget = editTarget;
        OneOffRadio.IsChecked = true;

        var goalsWithPlans = _goals.Where(goal => _patternsByFinanceId.ContainsKey(goal.FinanceId)).ToList();
        var options = goalsWithPlans.Select(goal => new GoalOption(goal)).ToList();
        GoalComboBox.ItemsSource = options;
        GoalComboBox.IsEnabled = editTarget is null;

        var targetFinanceId = editTarget?.FinanceId ?? preselectFinanceId ?? goalsWithPlans.FirstOrDefault()?.FinanceId;
        GoalComboBox.SelectedItem = options.FirstOrDefault(option => option.Pattern.FinanceId == targetFinanceId);

        TargetComboBox.ItemsSource = options;

        if (editTarget is not null)
        {
            ActionComboBox.SelectedIndex = editTarget.Amount >= 0m ? 0 : 1;
            ((ComboBoxItem)ActionComboBox.Items[2]).IsEnabled = false; // Move creates pairs, not edits
            EarmarkDatePicker.SelectedDate = editTarget.Date.ToDateTime(TimeOnly.MinValue);
            EarmarkDatePicker.IsEnabled = false;
            OneOffAmountTextBox.Text = Math.Abs(editTarget.Amount).ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            ActionComboBox.SelectedIndex = 0;
            ((ComboBoxItem)ActionComboBox.Items[2]).IsEnabled = true;
            EarmarkDatePicker.SelectedDate = (initialDate ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue);
            EarmarkDatePicker.IsEnabled = true;
            OneOffAmountTextBox.Text = string.Empty;
        }

        _suppressEvents = false;
        UpdateModeVisibility();
        UpdateOneOffInfo();
    }

    private void PopulateSavingsPlanFields(FinancialPattern goal)
    {
        if (_patternsByFinanceId.TryGetValue(goal.FinanceId, out var existing))
        {
            AmountTextBox.Text = Math.Abs(existing.Amount).ToString(CultureInfo.InvariantCulture);
            StartingAllocationTextBox.Text = existing.StartingAllocation.ToString(CultureInfo.InvariantCulture);
            RuleEditor.LoadFrom(existing.DatePattern);
        }
        else
        {
            AmountTextBox.Text = string.Empty;
            StartingAllocationTextBox.Text = "0";
            // TODO: RecurrenceRuleEditor has no public "reset to defaults"
            // beyond its own constructor — picking a goal with no existing
            // plan leaves whatever schedule was already on screen rather than
            // resetting it. Minor rough edge, not a correctness problem.
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e) => LoadForNewPattern();

    private void OnGoalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _suppressEvents || GoalComboBox.SelectedItem is not GoalOption { Pattern: var goal })
        {
            return;
        }

        if (SavingsPlanRadio.IsChecked == true)
        {
            PopulateSavingsPlanFields(goal);
            UpdateSummary();
        }
        else
        {
            UpdateOneOffInfo();
        }

        UpdateModeVisibility();
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
    }

    private void OnTransformToOneOffClick(object sender, RoutedEventArgs e)
    {
        if (GoalComboBox.SelectedItem is not GoalOption { Pattern: var goal } || !_patternsByFinanceId.ContainsKey(goal.FinanceId))
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

        var selectedGoalHasPlan = GoalComboBox.SelectedItem is GoalOption { Pattern: var goal } && _patternsByFinanceId.ContainsKey(goal.FinanceId);
        AddManualFromPlanButton.IsEnabled = selectedGoalHasPlan;
        NoPatternNote.Visibility = !isOneOff && GoalComboBox.SelectedItem is not null && !selectedGoalHasPlan
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

        TargetRow.Visibility = IsMove ? Visibility.Visible : Visibility.Collapsed;
        UpdateOneOffInfo();
    }

    private void UpdateOneOffInfo()
    {
        if (BalanceInfoText is null)
        {
            return;
        }

        if (_forecast is null
            || GoalComboBox.SelectedItem is not GoalOption { Pattern: var goal }
            || EarmarkDatePicker.SelectedDate is not { } selectedDate)
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

        if (GoalComboBox.SelectedItem is not GoalOption selectedGoal)
        {
            throw new InvalidOperationException("Pick a goal to save toward.");
        }

        if (!decimal.TryParse(AmountTextBox.Text, out var enteredAmount))
        {
            throw new InvalidOperationException("Amount must be a number.");
        }

        if (!decimal.TryParse(StartingAllocationTextBox.Text, out var enteredStartingAllocation))
        {
            throw new InvalidOperationException("Already-saved amount must be a number.");
        }

        // Always an allocation — money moving from free balance into the fund
        // jar — so the field is a plain magnitude and the sign is fixed here
        // rather than typed by the user.
        var amount = -Math.Abs(enteredAmount);

        var pattern = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = selectedGoal.Pattern.FinanceId,
                DatePattern = rule,
                Amount = amount,
                StartingAllocation = Math.Abs(enteredStartingAllocation),
            },
            selectedGoal.Pattern);

        PatternSaved?.Invoke(pattern);
    }

    // Ported from ManualEarmarkWindow verbatim (Merge/RequireFundsCover/
    // WarnIfOverFree/BalancesOn below) — same validation policy, just reading
    // the source fund from this panel's own goal picker instead of a
    // separate JarComboBox, since the goal is already chosen at the top of
    // this same form.
    private void SaveOneOff()
    {
        if (GoalComboBox.SelectedItem is not GoalOption { Pattern: var goal }
            || !_patternsByFinanceId.TryGetValue(goal.FinanceId, out var sourcePattern))
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

        var entry = _forecast.GetTimeline().LastOrDefault(candidate => candidate.Date <= date);
        if (entry is null)
        {
            return (0m, 0m);
        }

        var jar = entry.Snapshot.FundJars.FirstOrDefault(candidate => candidate.FinanceId == financeId);
        return (jar?.ExpectedAmount ?? 0m, entry.Snapshot.ExpectedFreeAmount ?? 0m);
    }

    private void UpdateSummary()
    {
        if (GoalComboBox.SelectedItem is not GoalOption { Pattern: var goal })
        {
            return;
        }

        var goalAmount = Math.Abs(goal.Amount);
        var dueDate = goal.DatePattern.Until;
        var label = string.IsNullOrWhiteSpace(goal.Description) ? goal.Source : goal.Description;

        decimal.TryParse(AmountTextBox.Text, out var enteredAmount);
        decimal.TryParse(StartingAllocationTextBox.Text, out var startingAllocation);
        var start = RuleEditor.Result?.Start ?? DateOnly.FromDateTime(DateTime.Today);

        var narrative = enteredAmount > 0m
            ? $"We need {goalAmount:C0} for {label} by {dueDate:MMM d, yyyy}. We plan to set aside {enteredAmount:C0} per occurrence, starting {start:MMM d, yyyy}."
            : $"We need {goalAmount:C0} for {label} by {dueDate:MMM d, yyyy}.";

        // TODO: same as CreateEarMarkPatternWindow's own note — this preview
        // is computed from the form's own raw fields, not a live forecast, so
        // the aside stays a placeholder until PlanHealthState is threaded in.
        Summary.Load(
            narrative,
            start: start,
            asOfDate: DateOnly.FromDateTime(DateTime.Today),
            dueDate: dueDate,
            startAmount: startingAllocation,
            todayAmount: startingAllocation,
            goalAmount: goalAmount,
            asideLine: "(fund jar state needs a live forecast — not wired in yet)");
    }
}
