using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

/// <summary>
/// The Expense tab, laid out in this project's three form regions.
/// The instance block (top) shows which Expense is loaded, a dirty indicator, Discard and Clear, and "Change..." (FinancialPatternPickerWindow) to pick one to edit.
/// The characterization block (first GroupBox) has Direction, Repeats?, and a "Bill", "Paycheck", or "Custom" label; everything else follows below, depending on Repeats?.
/// </summary>
/// <remarks>
/// The Summary region shows the linked plan from the forecast, the plan Save would propose when there's none yet (AllocationPlanProposer.Propose), or just the goal when several plans fund it (see UpdateSummary).
/// TODO:
/// - The Save button's outline only reflects the plan's saved health; checking whether the typed edit would newly cause a problem isn't built.
/// </remarks>
public partial class ExpenseFormPanel : UserControl
{
    private enum LoadedMode { NewBill, NewPattern, Editing }

    /// <summary>False until the constructor finishes, because several radio buttons have IsChecked="True" in XAML, which fires Checked during InitializeComponent.</summary>
    private bool _initialized;

    /// <summary>Turns off dirty tracking while a Load* method fills the fields, so loading doesn't count as unsaved changes.</summary>
    private bool _suppressEvents;

    private bool _isDirty;

    private int _financeId;
    private bool _isNew;
    private LoadedMode _loadedMode = LoadedMode.NewPattern;
    private FinancialPattern? _loadedExisting;
    private int _loadedAccountId;

    // The Spreadsheet subtab's draft row this form was filled from, so a successful save can remove it. Cleared by every other load.
    private int? _loadedDraftId;

    private IReadOnlyDictionary<int, int> _accountIdByFinanceId = new Dictionary<int, int>();
    private IReadOnlyList<Account> _accounts = [];
    private IReadOnlySet<int> _transferFinanceIds = new HashSet<int>();
    private IReadOnlyDictionary<int, EarMarkPattern> _patternsByFinanceId = new Dictionary<int, EarMarkPattern>();
    private IReadOnlyDictionary<int, int> _earmarkPatternCountByFinanceId = new Dictionary<int, int>();

    /// <summary>Every FinancialPattern, so LoadPattern and ChooseSegmentToLoad can find which segment of a chain to open.</summary>
    private IReadOnlyList<FinancialPattern> _allPatterns = [];
    private IReadOnlyList<ManualEarmark> _manualEarmarks = [];

    /// <summary>Gets the live forecast, computing it if needed (MainWindow.EnsureForecast, which always succeeds).</summary>
    public Func<ForecastResult>? RequestForecast { get; set; }

    /// <summary>
    /// Saves the pattern: (pattern, accountId, isNew, jumpToEarmark), returning whether the save went through.
    /// jumpToEarmark is true for Save and Plan; isNew tells MainWindow to also propose a plan (AutoCreateAllocationPlan); false means the user cancelled, so the form stays as it is.
    /// </summary>
    public Func<FinancialPattern, int, bool, bool, bool>? PatternSaved { get; set; }

    /// <summary>
    /// Asks which segment of a chain to edit (ChainSegmentPickerWindow, via MainWindow), given every same-Source segment.
    /// Only used when the chain has several segments; null or a cancelled pick opens the current segment.
    /// </summary>
    public Func<IReadOnlyList<FinancialPattern>, FinancialPattern?>? PickChainSegment { get; set; }

    /// <summary>[UI] Fires whenever IsDirty or IsPopulated could have changed, so MainWindow can restyle this form's tab header live.</summary>
    public event EventHandler? StateChanged;

    public bool IsDirty => _isDirty;

    /// <summary>[CALC] Whether an existing Expense is currently loaded (not a blank new one) — the instance-information-block's own definition of "populated."</summary>
    public bool IsPopulated => _loadedExisting is not null;

    /// <summary>[CALC] Finance id of the existing bill/paycheck/goal open in this form, or null for a new one — lets the Bills &amp; Paychecks list mark that row.</summary>
    public int? LoadedFinanceId => _loadedExisting?.FinanceId;

    /// <summary>[CALC] Id of the Spreadsheet subtab draft row this form was filled from, or null when it wasn't — lets a successful save remove that draft.</summary>
    public int? LoadedDraftId => _loadedDraftId;

    /// <summary>[CALC] Whether this form's own Advanced mode checkbox is on — a view preference, not part of what gets saved.</summary>
    public bool IsAdvancedMode => AdvancedModeCheckBox.IsChecked == true;

    public ExpenseFormPanel()
    {
        InitializeComponent();

        // Subscribed once, ever, not per-load — RuleEditor is one long-lived
        // instance, not a fresh control each time.
        RuleEditor.ResultChanged += (_, _) => MarkDirtyIfNotSuppressed();

        _initialized = true;
    }

    /// <summary>
    /// [UI] Supplies what this panel reads: each pattern's account, the transfer legs (for the picker), every plan (for the Summary), and every pattern (for picking a chain segment).
    /// Also keeps the account dropdown filled without changing the pick, so opening the tab directly doesn't leave it empty.
    /// Call before any Load* method, and again after every save.
    /// </summary>
    /// <param name="accountIdByFinanceId">Finance id → the account it's currently filed under.</param>
    /// <param name="accounts">Every account, to populate the account picker.</param>
    /// <param name="transferFinanceIds">Finance ids that are transfer legs — excluded from the instance picker.</param>
    /// <param name="earMarkPatterns">Every savings plan, so the Summary region can find this Expense's own linked plan.</param>
    /// <param name="allPatterns">Every FinancialPattern — LoadPattern's own BreakOffFactory.FindCurrentSegment redirect needs the full chain to search, not just the one pattern being loaded.</param>
    /// <param name="manualEarmarks">Every manual earmark, so the Summary's no-forecast jar reading counts one-off adds and withdrawals.</param>
    public void SetContext(
        IReadOnlyDictionary<int, int> accountIdByFinanceId,
        IReadOnlyList<Account> accounts,
        IReadOnlySet<int> transferFinanceIds,
        IReadOnlyList<EarMarkPattern> earMarkPatterns,
        IReadOnlyList<FinancialPattern> allPatterns,
        IReadOnlyList<ManualEarmark> manualEarmarks)
    {
        _accountIdByFinanceId = accountIdByFinanceId;
        _accounts = accounts;
        _transferFinanceIds = transferFinanceIds;
        _allPatterns = allPatterns;
        _manualEarmarks = manualEarmarks;

        // Keeps each goal's most recently started plan, since a goal can have several (as in EarmarkFormPanel.SetContext).
        _patternsByFinanceId = earMarkPatterns
            .GroupBy(pattern => pattern.FinanceId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(p => p.DatePattern.ActiveStart).First());

        // How many plans each finance id has; with several, UpdateSummary can't predict what a forced combine would produce.
        _earmarkPatternCountByFinanceId = earMarkPatterns
            .GroupBy(pattern => pattern.FinanceId)
            .ToDictionary(group => group.Key, group => group.Count());

        // Refills the account dropdown, since PopulateAccounts only runs on a load; keeps the picked account, since swapping ItemsSource would clear it.
        var pickedAccountId = AccountComboBox.SelectedValue;
        var wasSuppressed = _suppressEvents;
        _suppressEvents = true;
        AccountComboBox.ItemsSource = _accounts;
        AccountComboBox.SelectedValue = pickedAccountId;
        _suppressEvents = wasSuppressed;
    }

    public int SelectedAccountId => (int)AccountComboBox.SelectedValue;

    /// <summary>[STEP] Blank form for the "Create Bill..." shortcut: Direction and Skippable are fixed, and Repeats defaults to Repeating but stays open, since not every bill repeats.</summary>
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

    /// <summary>[CALC] Works out which segment of a chain to open: the one the user picks when there are several and a picker is wired, otherwise the current one.</summary>
    private FinancialPattern ChooseSegmentToLoad(FinancialPattern existing)
    {
        var current = BreakOffFactory.FindCurrentSegment(existing, _allPatterns);

        var segments = _allPatterns
            .Where(pattern => pattern.Source == existing.Source)
            .Append(existing) // always a candidate, even if the caller's list omits it
            .DistinctBy(pattern => pattern.FinanceId)
            .ToList();

        // TODO: once pages can expire, leave expired segments out of the choices here; Expired is always false for now.

        if (segments.Count <= 1 || PickChainSegment is null)
        {
            return current;
        }

        return PickChainSegment(segments) ?? current;
    }

    /// <summary>
    /// [STEP] Loads a pattern for editing, from the list's "Edit Selected" or this form's own picker; FinanceId can't change.
    /// For a chain with several segments, PickChainSegment asks which one to open, so an earlier segment can be edited on purpose; otherwise the current segment opens.
    /// </summary>
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
        NotesTextBox.Text = current.Notes;
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
            if (current.AutoRenew)
            {
                // Ongoing: picks "keeps going", and UpdateStopMode sets the hidden end date, which is worked out again on save.
                StopKeepsGoingRadio.IsChecked = true;
                StopOnDateRadio.IsChecked = false;
                StopPaidOffRadio.IsChecked = false;
                UpdateStopMode();
            }
            else
            {
                StopOnDateRadio.IsChecked = true;
                StopPaidOffRadio.IsChecked = false;
                UpdateStopMode();
                RuleEditor.SetHostEndDate(current.DatePattern.Until);
                StopEndDatePicker.SelectedDate = current.DatePattern.Until.ToDateTime(TimeOnly.MinValue);
            }
        }

        UpdateDirectionDependentUi();
        UpdateSelectedInstanceText();
        UpdateSummary();
        UpdateContinuityNote();
        _suppressEvents = false;
        ClearDirty();
    }

    /// <summary>
    /// [STEP] Opens a new bill or paycheck filled in from a Spreadsheet subtab draft row, as unsaved changes, so Save is ready to press.
    /// Blank cells keep the form's usual defaults, except a blank Ends after a filled-in Starts, which answers "When does this stop?" with "It just keeps going".
    /// Frequency "Once" opens it as "One time", with Starts as the due date.
    /// Whether it's skippable has no column, so that's left for the user to answer here.
    /// Saving removes the draft (MainWindow reads LoadedDraftId); Discard or Clear leaves the draft where it was.
    /// </summary>
    /// <param name="values">The draft's cells, already parsed. The spreadsheet only opens a draft with no invalid cells.</param>
    /// <param name="draftId">Which draft row these came from.</param>
    public void LoadDraft(DraftFormValues values, int draftId)
    {
        LoadForNewPattern();
        _suppressEvents = true;

        SourceTextBox.Text = values.Source;
        DescriptionTextBox.Text = values.Description;
        NotesTextBox.Text = values.Notes;
        if (values.Priority is { } priority)
        {
            PriorityTextBox.Text = priority.ToString(CultureInfo.InvariantCulture);
        }

        if (values.AccountName is { } accountName
            && _accounts.FirstOrDefault(account => string.Equals(account.Name, accountName, StringComparison.OrdinalIgnoreCase)) is { } account)
        {
            AccountComboBox.SelectedValue = account.Id;
        }

        // The spreadsheet's Amount is signed — negative for a bill — while this form's Amount box is always a magnitude, with Direction carrying the sign.
        if (values.Amount is { } amount)
        {
            AmountTextBox.Text = Math.Abs(amount).ToString(CultureInfo.InvariantCulture);
            ExpenseRadioButton.IsChecked = amount < 0;
            IncomeRadioButton.IsChecked = amount >= 0;
        }

        if (values.IsOneTime)
        {
            OneTimeRadioButton.IsChecked = true;
            RepeatingRadioButton.IsChecked = false;
            UpdateRepeatsVisibility();
            DueDatePicker.SelectedDate = values.Starts?.ToDateTime(TimeOnly.MinValue);
        }
        else
        {
            LoadDraftSchedule(values);
        }

        UpdateDirectionDependentUi();
        UpdateSummary();
        _suppressEvents = false;
        MarkDirty();
        _loadedDraftId = draftId;
    }

    /// <summary>[UI] Fills the repeat schedule and end date from a draft's cells, using the form's own defaults (Monthly, every 1, starting today) for any that were blank. Leaves the schedule untouched when every schedule cell was blank.</summary>
    private void LoadDraftSchedule(DraftFormValues values)
    {
        var hasSchedule = values.Frequency is not null || values.Interval is not null || values.Starts is not null
            || values.Ends is not null || values.ByDay.Count > 0 || values.ByMonthDay.Count > 0;
        if (!hasSchedule)
        {
            return;
        }

        var start = values.Starts ?? DateOnly.FromDateTime(DateTime.Today);
        var frequency = values.Frequency ?? RecurrenceFrequency.Monthly;
        try
        {
            // The editor only loads a whole rule, so the end date here is a stand-in; the "When does this stop?" answer below supplies the real one.
            RuleEditor.LoadFrom(RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = frequency,
                DtStart = start,
                Interval = values.Interval ?? 1,
                ByDay = frequency == RecurrenceFrequency.Weekly ? values.ByDay : [],
                ByMonthDay = frequency is RecurrenceFrequency.Monthly or RecurrenceFrequency.Yearly ? values.ByMonthDay : [],
                Until = values.Ends ?? start.AddYears(3),
            }));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // The cells were each valid but don't make a schedule together; leave the schedule at its defaults for the user to set here.
            ErrorText.Text = $"Couldn't use the draft's schedule: {ex.Message}";
        }

        if (values.Ends is { } end)
        {
            StopOnDateRadio.IsChecked = true;
            StopEndDatePicker.SelectedDate = end.ToDateTime(TimeOnly.MinValue);
        }
        else if (values.KeepsGoing)
        {
            StopKeepsGoingRadio.IsChecked = true;
            StopOnDateRadio.IsChecked = false;
            StopPaidOffRadio.IsChecked = false;
        }

        UpdateStopMode();
    }

    /// <summary>[UI] Shared reset every Load* starts with.</summary>
    /// <param name="selectedAccountId">Which account to preselect; defaults to the first when null.</param>
    private void ResetSharedFields(int? selectedAccountId)
    {
        _loadedDraftId = null;
        ErrorText.Text = string.Empty;
        SourceTextBox.Text = string.Empty;
        DescriptionTextBox.Text = string.Empty;
        NotesTextBox.Text = string.Empty;
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

    /// <summary>
    /// [UI] Shows this Expense's linked plan in the same Summary region as the Earmark form, healthy or not.
    /// No plan yet shows the plan Save would propose from the typed fields (ShowProposedPreview); income shows none; several plans show just the goal, since editing could force them to combine in a way this can't predict.
    /// One plan shows the real thing from the forecast, or from plain schedule math when there's no forecast reading yet.
    /// </summary>
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
            // Several plans: a chart of one could show a shape Save might not produce, so this shows the goal and Today lines alone.
            Summary.Load(
                $"We need {goalAmount:C0} for {label} by {dueDate:MMM d, yyyy}.",
                start: plan.DatePattern.ActiveStart,
                asOfDate: todayDate,
                dueDate: dueDate,
                startAmount: 0m,
                goalAmount: goalAmount,
                actualTrajectory: [],
                jarStateLine: "Savings plan may need to be consolidated. We don't have a certain preview.");
            return;
        }

        var narrative =
            $"We need {goalAmount:C0} for {label} by {dueDate:MMM d, yyyy}. We plan to set aside {Math.Abs(plan.Amount):C0} per occurrence, starting {plan.DatePattern.ActiveStart:MMM d, yyyy}.";

        var forecast = RequestForecast?.Invoke();
        var health = forecast?.PlanHealthStates.FirstOrDefault(p => p.FinanceId == existing.FinanceId);
        var jar = forecast?.GetTimeline(existing.FinanceId)
            .LastOrDefault(entry => entry.Date <= todayDate)?.Snapshot.FundJars
            .FirstOrDefault(candidate => candidate.FinanceId == existing.FinanceId);

        string jarStateLine;
        string? trajectoryLine = null;
        string? firstPaymentLine = null;
        if (jar is not null && health is not null)
        {
            // "Fund jar, today" and "Toward the goal" lines, with the first-payment warning as a third line.
            var isOneTime = existing.DatePattern.GetOccurrences().Count == 1;
            // Milestone-free here — see JarSavedLine: this form edits the bill, not
            // the plan, so the "of $Y milestone" comparison isn't helpful.
            jarStateLine = PlanHealthMessages.JarSavedLine(jar.ExpectedAmount);
            trajectoryLine = PlanHealthMessages.SummaryFutureLine(jar, health.Shortfall, health.MostImportantHealthState)
                ?? PlanHealthMessages.SummaryRecurringPhrase(health);
            firstPaymentLine = PlanHealthMessages.FirstPaymentCoverageLine(
                health.IsFirstOccurrencePending, Math.Abs(existing.Amount), health.FirstOccurrenceShortfall,
                health.FirstOccurrenceFreeFunds, health.FirstOccurrenceDate, isOneTime);
        }
        else
        {
            var expected = ComputeLiveJarAmount([plan], existing, plan.StartingAllocation, _manualEarmarks, todayDate);
            jarStateLine = PlanHealthMessages.JarSavedLine(expected);
        }

        Summary.Load(
            narrative,
            start: plan.DatePattern.ActiveStart,
            asOfDate: todayDate,
            dueDate: dueDate,
            startAmount: plan.StartingAllocation,
            goalAmount: goalAmount,
            actualTrajectory: GetJarTrajectory(forecast, existing.FinanceId, dueDate),
            jarStateLine: jarStateLine,
            trajectoryLine: trajectoryLine,
            firstPaymentLine: firstPaymentLine);
    }

    /// <summary>
    /// [UI] Shows whether this Expense continues an earlier segment, or has been continued by a later one, since a break-off gives each segment a new FinanceId (BreakOffFactory.FindPredecessor/FindSuccessor).
    /// Hidden for a new pattern and when neither applies.
    /// </summary>
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

    /// <summary>
    /// [UI] UpdateSummary with no plan yet: previews the plan Save would propose from the typed fields (the same AllocationPlanProposer.Propose call as MainWindow.AutoCreateAllocationPlan).
    /// Shows a "not enough typed yet" placeholder when there's nothing valid to propose from.
    /// </summary>
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

        var expected = ComputeLiveJarAmount([proposal.Plan], pattern, proposal.Plan.StartingAllocation, _manualEarmarks, todayDate);

        Summary.Load(
            narrative,
            start: proposal.Plan.DatePattern.ActiveStart,
            asOfDate: todayDate,
            dueDate: dueDate,
            startAmount: proposal.Plan.StartingAllocation,
            goalAmount: goalAmount,
            actualTrajectory: [], // nothing saved yet — no real walked history to show
            jarStateLine: PlanHealthMessages.JarSavedLine(expected));
    }

    /// <summary>
    /// [CALC] Proposes the plan Save would create for an expense with none, the same call as MainWindow.AutoCreateAllocationPlan, scoped to the same account and leaving out transfer legs.
    /// Null when RequestForecast isn't wired or the proposer rejects the pattern.
    /// </summary>
    /// <param name="pattern">The (possibly not-yet-saved) expense to propose a plan for.</param>
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

    /// <summary>
    /// [CALC] Today's jar balance worked out from the plan's schedule and the goal's manual earmarks, with no forecast; the same walk as EarmarkFormPanel.GetLiveJarAmounts.
    /// It pays each release in full and ignores deallocation, so it's a rough reading for when no forecast has the goal's jar today.
    /// </summary>
    /// <param name="patterns">Every EarMarkPattern funding the goal.</param>
    /// <param name="goal">The goal being funded.</param>
    /// <param name="startingTotal">The plans' StartingAllocation; manual earmarks, the starting one included, come from manualEarmarks.</param>
    /// <param name="manualEarmarks">Every manual earmark; only the goal's own count.</param>
    /// <param name="asOfDate">Today's date, to read the jar as of.</param>
    private static decimal ComputeLiveJarAmount(
        IReadOnlyList<EarMarkPattern> patterns, FinancialPattern goal, decimal startingTotal, IReadOnlyList<ManualEarmark> manualEarmarks, DateOnly asOfDate)
    {
        var activeStart = patterns.Count > 0 ? patterns.Min(p => p.DatePattern.ActiveStart) : asOfDate;
        return TransactionLogBookFactory.ComputeExpectedTrajectory(
            patterns, goal, activeStart, asOfDate, startingTotal, manualEarmarks)[^1].ExpectedAmount;
    }

    /// <summary>[CALC] The linked plan's jar day by day from today through `to`, for the chart's "Fund jar (actual)" line; empty when there's no forecast reading.</summary>
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

    /// <summary>
    /// [UI] Shows the linked plan's health state next to the save buttons (PlanHealthMessages.ExpenseStatusLabel), and outlines "Save and Plan" when the plan isn't healthy.
    /// It only reflects the saved plan; checking whether the typed edit would newly cause a problem isn't built.
    /// </summary>
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

    // 1 is the fallback for when RequestForecast isn't wired up yet (RequestForecast
    // always succeeds once the host has wired it, so this only matters pre-wiring).
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

    /// <summary>[UI] Marks the form dirty, unless loading or still being built (PriorityTextBox's Text="5" in XAML fires a change during InitializeComponent).</summary>
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

        // The save buttons are only active with unsaved changes.
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

    /// <summary>[STEP] Opens FinancialPatternPickerWindow to pick a Bill, Paycheck, or Goal to edit, listing every pattern in the forecast's book, even ones with no occurrences in the window.</summary>
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

    /// <summary>
    /// [UI] Shows or hides the advanced schedule fields; a view preference, so it doesn't mark the form dirty.
    /// Undecided: the wording for when Repeats? stops mattering, and whether a single-occurrence rule needs a different preview.
    /// </summary>
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

    /// <summary>[UI] Shows "Bill", "Paycheck", or "Custom" for the chosen fields, matching the shortcut button names; never blank.</summary>
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

            // MainWindow.OnExpensePatternSaved runs the confirmations (FinancePatternSaveConfirmation); this panel just hands over the typed pattern.
            // Clears the form only once the save went through; cancelling leaves every field as typed, and no handler counts as saved.
            var saved = PatternSaved?.Invoke(pattern, accountId, isNew, jumpToEarmark) ?? true;
            if (saved)
            {
                LoadForNewPattern();
            }
        }
        catch (Exception ex)
        {
            ErrorText.Text = ErrorLog.RecordAndDescribe("saving this expense", ex);
        }
    }

    /// <summary>
    /// [CALC] Builds a FinancialPattern from the typed fields, used by both Save and the Summary preview so they always agree.
    /// Throws on anything not yet valid; TryBuildPattern returns null instead.
    /// </summary>
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

        // Same 1–5 scale as the one-time goal window and the Spreadsheet subtab's Priority column.
        if (!int.TryParse(PriorityTextBox.Text, out var priority) || priority is < 1 or > 5)
        {
            throw new InvalidOperationException("Priority must be a whole number from 1 to 5.");
        }

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
            Notes = string.IsNullOrWhiteSpace(NotesTextBox.Text) ? null : NotesTextBox.Text,
            // "It just keeps going", only for a repeating pattern; UpdateKeepsGoingEnd sets its end date, and the renewal pass extends it later.
            AutoRenew = RepeatingRadioButton.IsChecked == true && StopKeepsGoingRadio.IsChecked == true,
        });
    }

    /// <summary>[CALC] BuildPattern for the live preview, returning null instead of throwing while the form is mid-edit.</summary>
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

            // Frequency doesn't matter for a single occurrence; Yearly, as elsewhere in the app.
            return RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = DateOnly.FromDateTime(date),
                Count = 1,
            });
        }

        return RuleEditor.Result ?? throw new InvalidOperationException("Fix the recurrence rule before continuing.");
    }

    // The "when does this stop?" question, shown for every repeating Expense. -------------------

    /// <summary>
    /// [UI] Switches the "Stops…" choice between an end date, paid off, and keeps going.
    /// Checks _initialized, since StopOnDateRadio's IsChecked="True" fires during InitializeComponent, before the stop panels exist.
    /// </summary>
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
        var keepsGoing = StopKeepsGoingRadio.IsChecked == true;
        // "Keeps going" has nothing to configure, so it hides both the date and
        // payoff panels and shows its own explanatory note instead.
        StopOnDatePanel.Visibility = paidOff || keepsGoing ? Visibility.Collapsed : Visibility.Visible;
        StopPaidOffPanel.Visibility = paidOff ? Visibility.Visible : Visibility.Collapsed;
        KeepsGoingNote.Visibility = keepsGoing ? Visibility.Visible : Visibility.Collapsed;
        AmountLabel.Text = paidOff ? "Payment amount" : "Amount owed";
        UpdateStopEnd();
    }

    /// <summary>[UI] Hands the schedule editor the end date the chosen answer implies — the picked date, the computed loan payoff date, or, for "keeps going", the forecast horizon plus a cycle.</summary>
    private void UpdateStopEnd()
    {
        if (StopPaidOffRadio.IsChecked == true)
        {
            UpdatePayoffEnd();
            return;
        }

        if (StopKeepsGoingRadio.IsChecked == true)
        {
            UpdateKeepsGoingEnd();
            return;
        }

        PayoffReadoutText.Text = string.Empty;
        RuleEditor.SetHostEndDate(
            StopEndDatePicker.SelectedDate is { } date ? DateOnly.FromDateTime(date) : null);
    }

    /// <summary>
    /// [UI] For "it just keeps going": sets the hidden end date to the forecast horizon plus one cycle, and at least a year from today, so a near horizon doesn't leave just a few occurrences.
    /// MainWindow.RenewOngoingPatternsToHorizon pushes it further as the horizon moves out.
    /// </summary>
    private void UpdateKeepsGoingEnd()
    {
        PayoffReadoutText.Text = string.Empty;
        var horizon = RequestForecast?.Invoke().HorizonEndDate ?? DateOnly.FromDateTime(DateTime.Today).AddYears(3);
        var oneYearFloor = DateOnly.FromDateTime(DateTime.Today).AddYears(1);
        try
        {
            var schedule = RuleEditor.ReadScheduleParts();
            var end = AddOneCycle(horizon, schedule.Frequency, schedule.Interval);
            RuleEditor.SetHostEndDate(end > oneYearFloor ? end : oneYearFloor);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            // Schedule not fully typed yet — a month past the horizon is a safe
            // over-estimate; UpdateStopEnd re-runs once the schedule is valid.
            var end = horizon.AddMonths(1);
            RuleEditor.SetHostEndDate(end > oneYearFloor ? end : oneYearFloor);
        }
    }

    /// <summary>[CALC] One repeat-cycle past the given date — the "plus a cycle" buffer an ongoing bill's end date carries past the horizon so its last occurrence isn't clipped right at the edge.</summary>
    /// <param name="date">The date to step one cycle past (the forecast horizon).</param>
    /// <param name="frequency">The pattern's repeat frequency.</param>
    /// <param name="interval">The pattern's repeat interval.</param>
    private static DateOnly AddOneCycle(DateOnly date, RecurrenceFrequency frequency, int interval)
    {
        var step = Math.Max(1, interval);
        return frequency switch
        {
            RecurrenceFrequency.Daily => date.AddDays(step),
            RecurrenceFrequency.Weekly => date.AddDays(7 * step),
            RecurrenceFrequency.Monthly => date.AddMonths(step),
            RecurrenceFrequency.Yearly => date.AddYears(step),
            _ => date.AddMonths(1),
        };
    }

    /// <summary>[UI] Works out the loan's payoff date from the owed amount, the payment, and the schedule, shows it as a floor, and hands it to the editor. The owed amount is entry-only — only the resulting date is kept.</summary>
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
