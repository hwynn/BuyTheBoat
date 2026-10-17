using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using BuyTheBoat.Domain;
using BuyTheBoat.Persistence;

namespace BuyTheBoat.App;

// The app's one main window: the Forecast tab, a list tab for each kind of thing (Accounts, Transfers, Bills & Paychecks, Allocations), 
// and a form tab for creating or editing each (Account, Transfer, Expense, Earmark).
public partial class MainWindow : Window
{
    private readonly FinancialPatternRepository _financialPatterns;
    private readonly EarMarkPatternRepository _earMarkPatterns;
    private readonly CurrentBalanceRepository _currentBalance;
    private readonly AccountRepository _accounts;
    private readonly TransferRepository _transfers;
    private readonly ManualEarmarkRepository _manualEarmarks;
    private readonly DraftRowRepository _draftRows;

    // Backs the spreadsheet export button and the selected-day detail pane — both only make sense once a forecast has actually been computed.
    private ForecastResult? _lastForecast;

    // The non-date inputs the shown forecast was computed from (dates live on _lastForecast itself) — the Forecast button's pending-state compares the fields against these.
    private decimal? _shownBalance;
    private decimal? _shownCushion;

    // Calendar selection is manual (day cells are Buttons): 
    //   exactly one cell is selected at a time, tracked here and flagged on the row itself so the cell template's trigger re-renders it — recycling-safe, cross-month.
    private DayCellRow? _selectedDayCell;
    private Dictionary<DateOnly, DayCellRow> _dayCellsByDate = [];

    // Which account the overview is scoped to; null = the household roll-up.
    // Filtering re-renders the calendar only — the forecast itself is unchanged, so there is nothing to recompute.
    private int? _accountFilter;
    private bool _updatingAccountFilter;

    public MainWindow()
    {
        InitializeComponent();

        // In the portable demo, make the labeled data\/logs\/backups\/exports\ folders exist from first launch so the tester can see where things live; a no-op on a normal LocalAppData run.
        AppPaths.EnsurePortableFoldersExist();

        var database = new PatternDatabase();
        _financialPatterns = new FinancialPatternRepository(database);
        _earMarkPatterns = new EarMarkPatternRepository(database, _financialPatterns);
        _currentBalance = new CurrentBalanceRepository(database);
        _accounts = new AccountRepository(database);
        _transfers = new TransferRepository(database, _financialPatterns);
        _manualEarmarks = new ManualEarmarkRepository(database, _earMarkPatterns);
        _draftRows = new DraftRowRepository(database);

        // Migration: there is always at least one account.
        // On the first run after accounts were added, the old single balance/cushion becomes the "primary" account, so nothing the user already entered is lost.
        var legacy = _currentBalance.GetCurrent();
        _accounts.EnsureDefaultAccount(legacy?.Balance ?? 0m, legacy?.IdealSafetyCushion ?? 0m);
        // The permanent Earmark tab persists through these callbacks instead of a ShowDialog() == true check — this panel owns no repository itself.
        EarmarkForm.PatternSaved = OnEarmarkPatternSaved;
        EarmarkForm.ManualEarmarksSaved = (saved, deleted) =>
        {
            foreach (var earmark in saved)
            {
                _manualEarmarks.Save(earmark);
            }

            foreach (var (financeId, date) in deleted)
            {
                _manualEarmarks.Delete(financeId, date);
            }

            RefreshGrids();
            RefreshEarmarkFormContext();
            RefreshShownForecast();

            // Same settled rule as the Savings-plan save path above — one plain save button for the whole Earmark form, one destination, regardless of which mode was active.
            SwitchToTab("Forecast");
        };

        // Same standing-tab treatment as Earmark above.
        // Each panel does its own validation (AccountFormPanel's uniqueness check included); these callbacks save what came back and refresh.
        // Both recompute the shown forecast after saving, since an account's starting balance feeds it.
        AccountForm.AccountSaved = account =>
        {
            _accounts.Save(account);
            // The saved account changed the world; recompute so the Forecast tab reflects it, not the pre-save snapshot.
            if (_lastForecast is { } shown)
            {
                RefreshForecast(shown.AsOfDate, shown.HorizonEndDate);
            }

            RefreshAccountsGrid();
            RefreshExpenseFormContext();

            // Land on the accounts list, so the saved account shows in it.
            AccountsListTab.IsSelected = true;
            SwitchToTab("Accounts");
        };
        ExpenseForm.RequestForecast = EnsureForecast;
        ExpenseForm.RequestForecastWithProposedPattern = ForecastWithProposedPattern;
        ExpenseForm.PatternSaved = OnExpensePatternSaved;
        ExpenseForm.PickChainSegment = PickChainSegmentToEdit;

        // The Spreadsheet subtab owns no repository either: drafts are stored and opened in the Form through these.
        Spreadsheet.AllocateDraftId = _draftRows.NextId;
        Spreadsheet.DraftSaved = _draftRows.Save;
        Spreadsheet.DraftsDeleted = ids =>
        {
            foreach (var id in ids)
            {
                _draftRows.Delete(id);
            }
        };
        Spreadsheet.OpenInFormRequested = OnOpenDraftInForm;

        EarmarkForm.RequestForecast = EnsureForecast;
        EarmarkForm.RequestForecastWithOneOff = ForecastWithOneOff;
        EarmarkForm.RequestForecastWithProposedPlan = ForecastWithProposedPlan;

        // Picking a plan-less goal in the Earmark form proposes a real, affordability-capped plan (the same one "Save and Plan" would create) as unsaved draft values to edit — not a bare monthly skeleton.
        // Paced from the goal's own start when that's still in the future, so the proposed plan fits the goal's span without the proposer stretching the goal (which a draft can't persist).
        EarmarkForm.RequestProposedPlan = goal =>
        {
            var accountId = _financialPatterns.GetAccountId(goal.FinanceId) ?? DefaultAccountId();
            var today = CurrentAsOfDate();
            var asOf = goal.DatePattern.ActiveStart > today ? goal.DatePattern.ActiveStart : today;
            return ProposeAllocationPlanFor(goal, accountId, asOf);
        };

        // The permanent Transfer tab.
        // Create-only for now (see TransferFormPanel) — the panel collects from/to/amount/schedule, this callback does the id assignment, paired-pattern expansion, and save.
        TransferForm.TransferSaved = OnTransferSaved;
        TransferForm.RequestForecast = EnsureForecast;

        // Tab-header styling stays live, not just at save/load: each panel raises StateChanged on every field edit (via MarkDirty/ClearDirty), not only when its own Load*/Save runs, so the header updates while the user is still typing.
        AccountForm.StateChanged += (_, _) => UpdateTabHeaderStyle(AccountTabHeaderText, AccountsTabHeaderText, AccountForm.IsPopulated, AccountForm.IsDirty);
        ExpenseForm.StateChanged += (_, _) => UpdateTabHeaderStyle(ExpenseTabHeaderText, BillsAndPaychecksTabHeaderText, ExpenseForm.IsPopulated, ExpenseForm.IsDirty);
        EarmarkForm.StateChanged += (_, _) => UpdateTabHeaderStyle(EarmarkTabHeaderText, AllocationsTabHeaderText, EarmarkForm.IsPopulated, EarmarkForm.IsDirty);
        TransferForm.StateChanged += (_, _) => UpdateTabHeaderStyle(TransferTabHeaderText, TransfersTabHeaderText, TransferForm.IsPopulated, TransferForm.IsDirty);

        // Same live trigger for the list rows: loading a different item, or switching the Earmark form's mode, moves the highlight; an edit turns it orange.
        AccountForm.StateChanged += (_, _) => MarkRowsOpenInForms();
        ExpenseForm.StateChanged += (_, _) => MarkRowsOpenInForms();
        EarmarkForm.StateChanged += (_, _) => MarkRowsOpenInForms();
        TransferForm.StateChanged += (_, _) => MarkRowsOpenInForms();

        RefreshGrids();
        RefreshAccountsGrid();
        LoadSavedBalance();
    }

    /// <summary>[UI] Snapshots the panel needs before every Load* call — repeated rather than held live.</summary>
    private void RefreshEarmarkFormContext() =>
        EarmarkForm.SetContext(_financialPatterns.GetAll(), _earMarkPatterns.GetAll(), _manualEarmarks.GetAll(), _financialPatterns.GetTransferFinanceIds(), _lastForecast);

    private void RefreshAccountFormContext() => AccountForm.SetContext(_accounts.GetAll());

    private void RefreshTransferFormContext() => TransferForm.SetContext(_accounts.GetAll());

    private void RefreshExpenseFormContext()
    {
        // Same accountId-by-financeId shape RefreshGrids() already builds for the grid's own account-name column — reused here so the instance picker knows which account each existing Expense is filed under.
        var accountIdByFinanceId = _financialPatterns.GetAllByAccount()
            .SelectMany(entry => entry.Value.Select(pattern => (pattern.FinanceId, AccountId: entry.Key)))
            .ToDictionary(pair => pair.FinanceId, pair => pair.AccountId);
        ExpenseForm.SetContext(accountIdByFinanceId, _accounts.GetAll(), _financialPatterns.GetTransferFinanceIds(), _earMarkPatterns.GetAll(), _financialPatterns.GetAll(), _manualEarmarks.GetAll());
    }

    /// <summary>[STEP] Jumps to a main tab (by plain Header like "Forecast", or Tag) or to a Form subtab by its Tag (Account/Transfer/Expense/Earmark), selecting the main tab that holds it too.</summary>
    /// <param name="header">The main tab's Header text or Tag, or a subtab's Tag.</param>
    private void SwitchToTab(string header)
    {
        foreach (TabItem item in MainTabControl.Items)
        {
            if (item.Header as string == header || item.Tag as string == header)
            {
                MainTabControl.SelectedItem = item;
                return;
            }

            if (item.Content is not TabControl subTabs)
            {
                continue;
            }

            foreach (TabItem subItem in subTabs.Items)
            {
                if (subItem.Tag as string == header)
                {
                    subTabs.SelectedItem = subItem;
                    MainTabControl.SelectedItem = item;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// [UI] Refreshes a form's background reference data whenever its Form subtab comes into view — by switching main tabs or subtabs, not only through a save/plan button.
    /// SetContext only ever replaces reference data (available goals, their plans), never a form's own in-progress fields, so this can't discard unsaved edits.
    /// </summary>
    /// <param name="e">
    /// SelectionChanged bubbles up from any Selector inside a tab's own content (every ComboBox and DataGrid included).
    /// e.Source must be the main tab strip or a subtab strip, or picking an item in one of those would also re-fire this.
    /// </param>
    private void OnMainTabControlSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var fromTabStrip = e.Source == MainTabControl
            || (e.Source is TabControl { Parent: TabItem { Parent: var owner } } && owner == MainTabControl);
        if (!fromTabStrip
            || MainTabControl.SelectedItem is not TabItem { Content: TabControl subTabs }
            || subTabs.SelectedItem is not TabItem visibleSubTab)
        {
            return;
        }

        switch (visibleSubTab.Tag as string)
        {
            case "Account":
                RefreshAccountFormContext();
                break;
            case "MoneyFlow":
                RefreshMoneyFlow();
                break;
            case "Transfer":
                RefreshTransferFormContext();
                break;
            case "Expense":
                RefreshExpenseFormContext();
                break;
            case "Earmark":
                RefreshEarmarkFormContext();
                break;
        }
    }

    /// <summary>
    /// [UI] Restyles a form's Form subtab and its main tab alike: bold when the form has an existing instance loaded (IsPopulated), plus an accent color on top when it also has unsaved changes (IsDirty).
    /// ClearValue rather than a hardcoded "normal" color/weight so the not-dirty/not-populated state just inherits whatever the tab strip's own default look is.
    /// </summary>
    /// <param name="hasContent">Whether the form has an existing instance loaded.</param>
    /// <param name="isDirty">Whether the form has unsaved changes.</param>
    private static void UpdateTabHeaderStyle(TextBlock formHeaderText, TextBlock mainHeaderText, bool hasContent, bool isDirty)
    {
        UpdateTabHeaderStyle(formHeaderText, hasContent, isDirty);
        UpdateTabHeaderStyle(mainHeaderText, hasContent, isDirty);
    }

    /// <summary>[UI] Applies the bold/accent "editing" look to one tab header.</summary>
    private static void UpdateTabHeaderStyle(TextBlock headerText, bool hasContent, bool isDirty)
    {
        headerText.FontWeight = hasContent ? FontWeights.Bold : FontWeights.Normal;
        if (isDirty)
        {
            headerText.Foreground = Brushes.DarkOrange;
        }
        else
        {
            headerText.ClearValue(TextBlock.ForegroundProperty);
        }
    }

    /// <summary>[UI] Highlights, in each List subtab, the row whose item is currently open in that tab's Form subtab — and un-highlights the rest.</summary>
    private void MarkRowsOpenInForms()
    {
        MarkRows<AccountRow>(AccountsGrid, AccountForm.IsDirty, row => row.Account.Id == AccountForm.LoadedAccountId);
        MarkRows<TransferRow>(TransfersGrid, TransferForm.IsDirty, row => row.Transfer.Id == TransferForm.EditingTransferId);
        MarkRows<FinancialPatternRow>(FinancialPatternsGrid, ExpenseForm.IsDirty, row => row.FinanceId == ExpenseForm.LoadedFinanceId);
        MarkRows<EarMarkPatternRow>(EarMarkPatternsGrid, EarmarkForm.IsDirty, row => row.Pattern.FinanceId == EarmarkForm.LoadedPlanFinanceId);
        MarkRows<ManualEarmarkRow>(ManualEarmarksGrid, EarmarkForm.IsDirty, row => EarmarkForm.EditingOneOff is { } oneOff
            && row.Earmark.FinanceId == oneOff.FinanceId
            && row.Earmark.Date == oneOff.Date);
    }

    /// <summary>[UI] Sets the open-in-form highlight, and its unsaved-changes color, on each of one grid's rows.</summary>
    private static void MarkRows<TRow>(DataGrid grid, bool formIsDirty, Func<TRow, bool> isOpenInForm) where TRow : FormListRow
    {
        foreach (var row in grid.ItemsSource?.OfType<TRow>() ?? [])
        {
            row.IsOpenInForm = isOpenInForm(row);
            row.IsDirty = row.IsOpenInForm && formIsDirty;
        }
    }

    /// <summary>
    /// [UI] Picking a row other than the one open in the form, while the form has unsaved changes, asks whether to load the picked row (discarding the changes) or go back to the changes.
    /// Without unsaved changes, picking a row only selects it.
    /// </summary>
    private void OnListRowSelected(object sender, SelectionChangedEventArgs e)
    {
        // A rebuilt list clears its selection (nothing added), and putting the selection back on the open row lands here too — neither asks.
        // Clicking into an Accounts balance box selects its row too, but typing a balance loads nothing into the Form, so that doesn't ask either.
        if (sender is not DataGrid grid
            || Keyboard.FocusedElement is TextBox { DataContext: AccountRow }
            || e.OriginalSource != grid
            || e.AddedItems.Count != 1
            || e.AddedItems[0] is not FormListRow picked
            || picked.IsOpenInForm)
        {
            return;
        }

        var (formIsDirty, formName, formTab, loadPicked) = grid.Name switch
        {
            nameof(AccountsGrid) => (AccountForm.IsDirty, "Account", "Account", (Action)(() => OnEditAccountClick(grid, e))),
            nameof(TransfersGrid) => (TransferForm.IsDirty, "Transfer", "Transfer", () => OnEditTransferClick(grid, e)),
            nameof(FinancialPatternsGrid) => (ExpenseForm.IsDirty, "Bills & Paychecks", "Expense", () => OnEditFinancialPatternClick(grid, e)),
            nameof(EarMarkPatternsGrid) => (EarmarkForm.IsDirty, "Allocations", "Earmark", () => OnEditEarMarkPatternClick(grid, e)),
            nameof(ManualEarmarksGrid) => (EarmarkForm.IsDirty, "Allocations", "Earmark", () => OnEditManualEarmarkClick(grid, e)),
            _ => (false, string.Empty, string.Empty, () => { }),
        };
        if (!formIsDirty)
        {
            return;
        }

        // Deferred until the click finishes: a modal popup opened mid-mouse-down leaves the DataGrid holding mouse capture.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (grid.SelectedItem != picked)
            {
                return;
            }

            if (ConfirmLeavingUnsavedChanges(formIsDirty, formName, formTab))
            {
                loadPicked();
                return;
            }

            // Kept the changes (or closed the popup): the selection goes back to the row open in the form, so a later "Edit Selected..." can't discard them by surprise.
            grid.SelectedItem = grid.ItemsSource.OfType<FormListRow>().FirstOrDefault(row => row.IsOpenInForm);
        });
    }

    /// <summary>[CALC] Names the item open in a Form subtab for the unsaved-changes popup: its list name in quotes, or "a new …" when it isn't saved yet.</summary>
    private string DescribeItemInForm(string formTab) => formTab switch
    {
        "Account" => OpenRowName(AccountsGrid) ?? "a new account",
        "Transfer" => OpenRowName(TransfersGrid) ?? "a new transfer",
        "Expense" => OpenRowName(FinancialPatternsGrid) ?? "a new bill or paycheck",
        // In one-off mode the form has no plan picked, so an unmatched item there is a new manual adjustment.
        "Earmark" => OpenRowName(EarMarkPatternsGrid) ?? OpenRowName(ManualEarmarksGrid)
            ?? (EarmarkForm.LoadedPlanFinanceId is null ? "a new manual adjustment" : "a new allocation"),
        _ => "this item",
    };

    /// <summary>[CALC] The quoted list name of a grid's row that's open in the form, or null when none is.</summary>
    private static string? OpenRowName(DataGrid grid) =>
        grid.ItemsSource?.OfType<FormListRow>().FirstOrDefault(row => row.IsOpenInForm) switch
        {
            AccountRow row => $"“{row.Name}”",
            TransferRow row => $"the {row.Amount:C} transfer from {row.From} to {row.To}",
            FinancialPatternRow row => $"“{(row.Description is { Length: > 0 } description ? description : row.Source)}”",
            EarMarkPatternRow row => $"the allocation for “{row.Goal}”",
            ManualEarmarkRow row => $"the {row.Target} adjustment on {row.Date}",
            _ => null,
        };

    /// <summary>
    /// [UI] Before a list row or button replaces a form's unsaved changes, asks whether to continue (true: go ahead and load the new item) or go to the unsaved changes (switches to the form, returns false).
    /// Closing the popup also returns false, staying where the user was.
    /// Returns true without asking when the form has no unsaved changes.
    /// </summary>
    private bool ConfirmLeavingUnsavedChanges(bool formIsDirty, string formName, string formTab)
    {
        if (!formIsDirty)
        {
            return true;
        }

        var popup = new UnsavedChangesWindow(formName, DescribeItemInForm(formTab)) { Owner = this };
        popup.ShowDialog();
        if (popup.ContinueToSelection == false)
        {
            SwitchToTab(formTab);
        }

        return popup.ContinueToSelection == true;
    }

    /// <summary>
    /// [STEP] Fills in what was entered last time, since the Forecast tab's settings carry across launches.
    /// If there's something to show, it forecasts right away so the tab isn't blank after a relaunch.
    /// </summary>
    private void LoadSavedBalance()
    {
        var saved = _currentBalance.GetCurrent();
        if (saved is null)
        {
            AsOfDatePicker.SelectedDate = DateTime.Today;
            HorizonEndDatePicker.SelectedDate = DateTime.Today.AddMonths(3);
            return;
        }

        AsOfDatePicker.SelectedDate = saved.AsOfDate.ToDateTime(TimeOnly.MinValue);
        HorizonEndDatePicker.SelectedDate = saved.HorizonEndDate.ToDateTime(TimeOnly.MinValue);

        // Balance and cushion come from the accounts themselves now (the engine reads them per account), so nothing else to pass here.
        RefreshForecast(saved.AsOfDate, saved.HorizonEndDate);
    }

    /// <summary>[CALC] Fallback account id for the rare case a pattern's own stored account id can't be resolved — picking the first account beats erroring out.</summary>
    private int DefaultAccountId() => _accounts.GetAll().FirstOrDefault()?.Id ?? 1;

    private void RefreshTransfersGrid()
    {
        var namesById = _accounts.GetAll().ToDictionary(account => account.Id, account => account.Name);
        TransfersGrid.ItemsSource = _transfers.GetAll()
            .Select(transfer => new TransferRow(
                transfer,
                namesById.GetValueOrDefault(transfer.FromAccountId, "(unknown)"),
                namesById.GetValueOrDefault(transfer.ToAccountId, "(unknown)")))
            .ToList();
    }

    /// <summary>[STEP] The Transfers list tab's "Schedule Transfer..." button — now opens the permanent Transfer form tab on a blank new transfer instead of a popup.</summary>
    private void OnAddTransferClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureTwoAccountsForTransfer()
            || !ConfirmLeavingUnsavedChanges(TransferForm.IsDirty, "Transfer", "Transfer"))
        {
            return;
        }

        RefreshTransferFormContext();
        TransferForm.LoadForNew();
        SwitchToTab("Transfer");
    }

    /// <summary>
    /// [STEP] The Transfers list tab's "Edit Selected..." button: loads the chosen transfer into the permanent Transfer form tab so the same form that creates a transfer also edits it.
    /// Saving rewrites that transfer in place.
    /// </summary>
    private void OnEditTransferClick(object sender, RoutedEventArgs e)
    {
        if (TransfersGrid.SelectedItem is not TransferRow row)
        {
            ShowNothingSelected();
            return;
        }

        // "Keeps going" lives on the legs, not the Transfer record, so read it off the withdrawal (the negative leg) to set the form's toggle.
        var withdrawal = _financialPatterns.GetByTransferId(row.Transfer.Id).FirstOrDefault(pattern => pattern.Amount < 0m);
        RefreshTransferFormContext();
        TransferForm.LoadForEdit(row.Transfer, withdrawal?.AutoRenew ?? false);
        SwitchToTab("Transfer");
    }

    /// <summary>
    /// [STEP] The selected day's lever for a short account: opens the Transfer form tab already pointed at that account for what it is short.
    /// The user still confirms — we surface the problem and make the fix easy, we don't move their money for them.
    /// </summary>
    private void OnCoverShortfallClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: System.Windows.Data.CollectionViewGroup group }
            && group.Name is AccountGroupKey key)
        {
            if (!EnsureTwoAccountsForTransfer())
            {
                return;
            }

            RefreshTransferFormContext();
            TransferForm.LoadForShortfall(key.AccountId, key.Shortfall, _selectedDayCell?.Date ?? CurrentAsOfDate());
            SwitchToTab("Transfer");
        }
    }

    /// <summary>[UI] Guards the two transfer entry points: a transfer needs two different accounts, so both surface the same message and back out when only one account exists.</summary>
    private bool EnsureTwoAccountsForTransfer()
    {
        if (_accounts.GetAll().Count >= 2)
        {
            return true;
        }

        MessageBox.Show(this, "You need at least two accounts to transfer between. Add another on the Accounts tab first.", "Not enough accounts", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    /// <summary>
    /// [WRITES FILE] Persists a transfer the Transfer form tab handed back — new or edited: turns from/to/amount/schedule into a Transfer plus its paired patterns, reserves for the withdrawal, saves, and returns the user to the Forecast tab.
    /// An edit rewrites the same transfer's legs in place; a new transfer gets fresh ids.
    /// The panel already validated the inputs and cleared itself.
    /// </summary>
    /// <param name="inputs">The from/to/amount/schedule the Transfer form collected, plus the id of the transfer being edited (null for a new one).</param>
    private void OnTransferSaved(TransferFormInputs inputs)
    {
        var namesById = _accounts.GetAll().ToDictionary(account => account.Id, account => account.Name);

        // Editing rewrites the existing transfer in place — same transfer id and the same withdrawal/deposit finance ids, so the saved patterns and plan are updated rather than a second set minted (the withdrawal is the negative leg, the deposit the positive).
        // A new transfer gets a fresh transfer id and two fresh finance ids, which must not collide with any existing pattern's, transfer legs included.
        int transferId, withdrawalFinanceId, depositFinanceId;
        if (inputs.EditingTransferId is { } editingId)
        {
            var legs = _financialPatterns.GetByTransferId(editingId);
            transferId = editingId;
            withdrawalFinanceId = legs.First(pattern => pattern.Amount < 0m).FinanceId;
            depositFinanceId = legs.First(pattern => pattern.Amount > 0m).FinanceId;
        }
        else
        {
            var maxFinanceId = _financialPatterns.GetAll().Select(pattern => pattern.FinanceId).DefaultIfEmpty(0).Max();
            transferId = _transfers.NextId();
            withdrawalFinanceId = maxFinanceId + 1;
            depositFinanceId = maxFinanceId + 2;
        }

        var result = TransferFactory.Create(new TransferRequest
        {
            TransferId = transferId,
            WithdrawalFinanceId = withdrawalFinanceId,
            DepositFinanceId = depositFinanceId,
            FromAccountId = inputs.FromAccountId,
            ToAccountId = inputs.ToAccountId,
            FromAccountName = namesById[inputs.FromAccountId],
            ToAccountName = namesById[inputs.ToAccountId],
            Amount = inputs.Amount,
            DatePattern = inputs.DatePattern,
            AutoRenew = inputs.AutoRenew,
        });

        // The transfer sets money aside in the account it leaves, through a plan on the withdrawal that reserves the full amount from the as-of date, with no income pacing.
        // spreadEvenlyWithNoIncome: false keeps a transfer plain and immediate, not spread like a one-time goal.
        // Proposes first, since the proposer may move the withdrawal's start back to the as-of date, and that prepared withdrawal is what must be saved for the plan to fit.
        var withdrawalPlan = AllocationPlanProposer.Propose(result.Withdrawal, [], CurrentAsOfDate(), spreadEvenlyWithNoIncome: false);
        _transfers.Save(result with { Withdrawal = withdrawalPlan.Outflow });
        // Clear any existing withdrawal plan before saving the freshly-proposed one.
        // Plans are keyed on (FinanceId, StartDate), so an edit whose new schedule moves the plan's start would otherwise leave the old plan behind as a duplicate reservation (a no-op for a brand-new transfer, whose withdrawal id is fresh; the renewal pass clears it the same way).
        _earMarkPatterns.Delete(withdrawalFinanceId);
        _earMarkPatterns.Save(withdrawalPlan.Plan);

        RefreshGrids();

        // The transfer's patterns change the cascade, so re-run the forecast, then land on the Forecast tab (matching the other form saves) — that view is what actually shows the shortfall the lever was offered for now cleared.
        RefreshShownForecast();
        SwitchToTab("Forecast");
    }

    private void OnDeleteTransferClick(object sender, RoutedEventArgs e)
    {
        if (TransfersGrid.SelectedItem is not TransferRow row)
        {
            ShowNothingSelected();
            return;
        }

        var confirm = MessageBox.Show(this, $"Delete the transfer of {row.Amount:C} from \"{row.From}\" to \"{row.To}\"?", "Delete transfer", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _transfers.Delete(row.Transfer.Id);
        RefreshGrids();
    }

    private (decimal Balance, decimal Cushion) AccountTotals()
    {
        var accounts = _accounts.GetAll();
        return (accounts.Sum(account => account.Balance), accounts.Sum(account => account.IdealSafetyCushion));
    }

    private void RefreshAccountsGrid()
    {
        AccountsGrid.ItemsSource = _accounts.GetAll().Select(account => new AccountRow(account)).ToList();
        RefreshAccountsSummary();
        MarkRowsOpenInForms();
        UpdateForecastButtonState();

        // The Spreadsheet's Account column accepts only current account names.
        RefreshSpreadsheet();
    }

    /// <summary>[UI] Updates the Forecast tab's "Across all accounts" total and cushion line.</summary>
    private void RefreshAccountsSummary()
    {
        var accounts = _accounts.GetAll();
        var total = accounts.Sum(account => account.Balance);
        var cushion = accounts.Sum(account => account.IdealSafetyCushion);
        AccountsSummaryText.Text = accounts.Count == 0
            ? "—"
            : $"{total:C} in {accounts.Count} account{(accounts.Count == 1 ? string.Empty : "s")}"
                + (cushion > 0m ? $" · {cushion:C} cushion" : string.Empty);
    }

    /// <summary>[UI] Fills the Accounts tab's Money flow subtab with each account's average monthly money in and out, from the current saved data.</summary>
    private void RefreshMoneyFlow()
    {
        // Same window as the shown forecast, but re-run so edits made since pressing Forecast show up.
        var shown = EnsureForecast();
        var forecast = TransactionLogBookFactory.CreateForecast(BuildForecastOptions(shown.AsOfDate, shown.HorizonEndDate));

        var rows = forecast.Accounts
            .Select(account => new AccountFlowRow(
                account.Name,
                AccountMoneyFlow.AverageMonthlyInflow(account.Page, forecast.AsOfDate, forecast.HorizonEndDate),
                AccountMoneyFlow.AverageMonthlyOutflow(account.Page, forecast.AsOfDate, forecast.HorizonEndDate)))
            .ToList();
        MoneyFlowGrid.ItemsSource = rows;

        MoneyFlowWindowText.Text = $"Averaged over {forecast.AsOfDate:MMM d, yyyy} – {forecast.HorizonEndDate:MMM d, yyyy}. Change the window with the Forecast tab's dates.";

        // Transfers cancel out across all accounts, so the household net is earnings minus spending.
        var householdNet = rows.Sum(row => row.MonthlyNet);
        MoneyFlowTotalText.Text = $"All accounts together: {householdNet:C} per month "
            + (householdNet < 0m ? "more going out than coming in." : "left over after spending.");
    }

    /// <summary>[UI] Selects the whole balance on entering its box, so typing replaces it.</summary>
    private void OnAccountBalanceGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.SelectAll();
        }
    }

    /// <summary>[WRITES FILE] Saves a balance typed into the Accounts list when the user clicks or tabs away from its box.</summary>
    private void OnAccountBalanceLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            CommitAccountBalance(box);
        }
    }

    /// <summary>[WRITES FILE] Enter/Tab in an Accounts balance box saves it and moves to the next account's box (Shift goes back); Esc puts the saved balance back.</summary>
    private void OnAccountBalanceKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not AccountRow row)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            ResetAccountBalanceBox(box);
            box.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Enter or Key.Tab))
        {
            return;
        }

        // Handled either way, so the DataGrid's own Enter/Tab cell-hopping doesn't also run.
        e.Handled = true;

        // An unreadable entry stays put, flagged, for the user to fix.
        if (!CommitAccountBalance(box))
        {
            return;
        }

        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
        var rows = AccountsGrid.Items;
        var nextIndex = rows.IndexOf(row) + step;
        if (nextIndex < 0 || nextIndex >= rows.Count)
        {
            box.SelectAll();
            return;
        }

        AccountsGrid.ScrollIntoView(rows[nextIndex]);
        AccountsGrid.UpdateLayout();
        if (AccountsGrid.ItemContainerGenerator.ContainerFromIndex(nextIndex) is DataGridRow nextRow
            && FindVisualChild<TextBox>(nextRow) is { } nextBox
            && nextBox.IsEnabled)
        {
            nextBox.Focus();
        }
    }

    /// <summary>
    /// [WRITES FILE] Saves the balance typed into an Accounts list box, then refreshes the forecast and the summary line.
    /// Returns false, flagging the box red, when the text isn't a number.
    /// </summary>
    private bool CommitAccountBalance(TextBox box)
    {
        if (box.DataContext is not AccountRow row)
        {
            return true;
        }

        if (!decimal.TryParse(box.Text, NumberStyles.Currency, CultureInfo.CurrentCulture, out var balance))
        {
            box.BorderBrush = (Brush)FindResource("RedTextBrush");
            box.ToolTip = "Enter an amount like 1234.56 — or press Esc to put the saved balance back.";
            return false;
        }

        box.ClearValue(Control.BorderBrushProperty);
        box.ClearValue(FrameworkElement.ToolTipProperty);

        if (balance == row.Account.Balance)
        {
            // Re-shows "500" as "$500.00".
            ResetAccountBalanceBox(box);
            return true;
        }

        var saved = Account.Create(new AccountOptions
        {
            Id = row.Account.Id,
            Name = row.Account.Name,
            Balance = balance,
            IdealSafetyCushion = row.Account.IdealSafetyCushion,
        });
        _accounts.Save(saved);
        row.ReplaceAccount(saved);

        // The Form still holding the old balance would put it back on its next Save.
        // Its unsaved-changes case can't reach here: that row's box is disabled.
        if (AccountForm.LoadedAccountId == saved.Id && !AccountForm.IsDirty)
        {
            AccountForm.LoadExisting(saved);
        }

        RefreshAccountsSummary();
        RefreshShownForecast();
        UpdateForecastButtonState();
        return true;
    }

    /// <summary>[UI] Puts an Accounts balance box back to showing the saved balance, clearing any red "not a number" flag.</summary>
    private static void ResetAccountBalanceBox(TextBox box)
    {
        box.ClearValue(Control.BorderBrushProperty);
        box.ClearValue(FrameworkElement.ToolTipProperty);
        BindingOperations.GetBindingExpression(box, TextBox.TextProperty)?.UpdateTarget();
    }

    /// <summary>[CALC] The first element of the given type inside a row or other on-screen element, or null.</summary>
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            var found = child as T ?? FindVisualChild<T>(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// [STEP] Opens a blank Accounts › Form for a new account.
    /// Validation (including the name-uniqueness check) lives in AccountFormPanel itself — see its own class comment.
    /// </summary>
    private void OnAddAccountClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmLeavingUnsavedChanges(AccountForm.IsDirty, "Account", "Account"))
        {
            return;
        }

        RefreshAccountFormContext();
        AccountForm.LoadForNew();
        SwitchToTab("Account");
    }

    private void OnEditAccountClick(object sender, RoutedEventArgs e)
    {
        if (AccountsGrid.SelectedItem is not AccountRow row)
        {
            ShowNothingSelected();
            return;
        }

        RefreshAccountFormContext();
        AccountForm.LoadExisting(row.Account);
        SwitchToTab("Account");
    }

    /// <summary>[STEP] Blocked while it's the only account, while anything is still filed under it, or while it's still part of a transfer — we never delete the user's bills out from under them.</summary>
    private void OnDeleteAccountClick(object sender, RoutedEventArgs e)
    {
        if (AccountsGrid.SelectedItem is not AccountRow row)
        {
            ShowNothingSelected();
            return;
        }

        if (_accounts.GetAll().Count <= 1)
        {
            MessageBox.Show(this, "There has to be at least one account.", "Can't delete", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_transfers.IsAccountReferenced(row.Account.Id))
        {
            MessageBox.Show(
                this,
                $"\"{row.Name}\" is still part of a transfer. Delete that transfer first (Transfers tab).",
                "Can't delete yet",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (_financialPatterns.HasPatternsInAccount(row.Account.Id))
        {
            MessageBox.Show(
                this,
                $"\"{row.Name}\" still has bills, paychecks or goals filed under it. Move or delete those first.",
                "Can't delete yet",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(this, $"Delete the account \"{row.Name}\"?", "Delete account", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _accounts.Delete(row.Account.Id);
        RefreshAccountsGrid();
        RefreshAccountFormContext();
        RefreshExpenseFormContext();
    }

    private void OnForecastClick(object sender, RoutedEventArgs e)
    {
        if (AsOfDatePicker.SelectedDate is not { } asOfDateTime)
        {
            MessageBox.Show(this, "Pick a date.", "Invalid date", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (HorizonEndDatePicker.SelectedDate is not { } horizonEndDateTime)
        {
            MessageBox.Show(this, "Pick how far ahead to forecast.", "Invalid date", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var asOfDate = DateOnly.FromDateTime(asOfDateTime);
        var horizonEndDate = DateOnly.FromDateTime(horizonEndDateTime);

        if (horizonEndDate <= asOfDate)
        {
            MessageBox.Show(this, "\"Show forecast through\" must be after \"As of\".", "Invalid date", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Balance and cushion are per account now, validated as each account is saved — so there is nothing to parse here; AccountTotals() is just the household sum the legacy CurrentBalance row below still wants.
        var (balance, idealSafetyCushion) = AccountTotals();

        // CurrentBalance is superseded by the per-account model but still holds the global as-of/horizon dates — kept in step here so the row stays coherent until that settings data moves onto TransactionLogBook itself.
        _currentBalance.Save(balance, asOfDate, horizonEndDate, idealSafetyCushion);
        RefreshForecast(asOfDate, horizonEndDate);
    }

    /// <summary>
    /// [CALC] No upper bound on the horizon by design — years out is a legitimate request (long-term goals, mortgage-length planning), so this is left to whatever the user picks rather than an app-imposed ceiling.
    /// The calendar stays cheap at that scale because the outer ListBox virtualizes months.
    /// One AccountForecastInput per account: its own balance/cushion, and the patterns/earmarks/manuals filed under it (an earmark or manual reaches its account through its finance id).
    /// A transfer's two patterns are patterns filed under an account too, so they ride along and feed its cascade.
    /// </summary>
    private IReadOnlyList<AccountForecastInput> BuildAccountInputs()
    {
        var patternsByAccount = _financialPatterns.GetAllByAccount();
        var allEarmarks = _earMarkPatterns.GetAll();
        var allManuals = _manualEarmarks.GetAll();

        var inputs = new List<AccountForecastInput>();
        foreach (var account in _accounts.GetAll())
        {
            var patterns = patternsByAccount.GetValueOrDefault(account.Id) ?? [];
            var financeIds = patterns.Select(pattern => pattern.FinanceId).ToHashSet();

            inputs.Add(new AccountForecastInput
            {
                AccountId = account.Id,
                Name = account.Name,
                StartingBalance = account.Balance,
                IdealSafetyCushion = account.IdealSafetyCushion,
                FinancialPatterns = patterns,
                EarMarkPatterns = allEarmarks.Where(earmark => financeIds.Contains(earmark.FinanceId)).ToList(),
                ManualEarmarks = allManuals.Where(manual => financeIds.Contains(manual.FinanceId)).ToList(),
            });
        }

        return inputs;
    }

    private void RefreshForecast(DateOnly asOfDate, DateOnly horizonEndDate)
    {
        // "It just keeps going": ongoing bills/paychecks and transfers are stored with a real, bounded end date, so before showing a forecast we extend them forward to reach the horizon.
        // Bills renew as fresh chain segments; transfers just extend their end date in place (they have no distinctive chain identity — see ExtendOngoingTransfersToHorizon).
        // Only the shown forecast does this — the what-if forecasts (ForecastOmitting/WithOneOff/WithProposedPlan) build straight from BuildForecastOptions and stay read-only.
        RenewOngoingPatternsToHorizon(horizonEndDate);
        ExtendOngoingTransfersToHorizon(horizonEndDate);

        var forecast = TransactionLogBookFactory.CreateForecast(BuildForecastOptions(asOfDate, horizonEndDate));

        _lastForecast = forecast;
        var (balance, cushion) = AccountTotals();
        _shownBalance = balance;
        _shownCushion = cushion;

        PopulateAccountFilter(forecast);
        RenderCalendar(forecast, selectDate: forecast.AsOfDate);
        ExportForecastSpreadsheetButton.IsEnabled = true;

        UpdateForecastButtonState();
    }

    /// <summary>
    /// [WRITES FILE] Extends every ongoing (AutoRenew) bill/paycheck forward with fresh renewal segments until its chain reaches the horizon, so "it just keeps going" actually keeps going however far you forecast.
    /// A no-op — and no writes — when every ongoing pattern already reaches the horizon, so re-forecasting the same window is idempotent.
    /// </summary>
    /// <param name="horizon">The forecast horizon the ongoing patterns must reach.</param>
    private void RenewOngoingPatternsToHorizon(DateOnly horizon)
    {
        // Each renewal pushes a chain a cycle-plus past the horizon, so one pass usually settles it; the guarded loop only re-runs if a segment length somehow lands short, and stops rather than spinning.
        for (var guard = 0; guard < 200; guard++)
        {
            var all = _financialPatterns.GetAll();
            // Transfer legs can be AutoRenew too, but they renew as a linked pair through ExtendOngoingTransfersToHorizon — never here, where the single-pattern chain logic would break the pairing.
            var transferFinanceIds = _financialPatterns.GetTransferFinanceIds();
            var dueForRenewal = all
                .Where(pattern => pattern.AutoRenew && !transferFinanceIds.Contains(pattern.FinanceId))
                .Select(pattern => BreakOffFactory.FindCurrentSegment(pattern, all))
                .DistinctBy(segment => segment.FinanceId)
                .Where(segment => segment.DatePattern.Until < horizon)
                .ToList();

            if (dueForRenewal.Count == 0)
            {
                return;
            }

            foreach (var segment in dueForRenewal)
            {
                RenewOngoingSegment(segment, horizon);
            }
        }
    }

    /// <summary>
    /// [WRITES FILE] Appends one renewal segment to an ongoing pattern's chain — a fresh, identical successor (new FinanceId, "(renewed …)" label) reaching a cycle past the horizon, CONTINUING each existing earmark pattern at the same amount and cadence rather than proposing a new one.
    /// A pattern with no savings plan gains none — it just keeps coming out of free money on its due date.
    /// A single bad pattern is logged and skipped rather than breaking the whole forecast.
    /// </summary>
    /// <param name="segment">The ongoing pattern's current segment, due to reach further out.</param>
    /// <param name="horizon">The forecast horizon the successor must clear.</param>
    private void RenewOngoingSegment(FinancialPattern segment, DateOnly horizon)
    {
        try
        {
            // Fetched live (not from the caller's snapshot) so ids stay current across a multi-pattern batch.
            var all = _financialPatterns.GetAll();
            var accountId = _financialPatterns.GetAccountId(segment.FinanceId) ?? DefaultAccountId();
            var renewalDate = segment.DatePattern.Until.AddDays(1);

            // Whole years reaching a cycle-plus past the horizon, so one renewal covers the current view and won't re-fire until the horizon moves out.
            var segmentYears = Math.Max(1, (horizon.DayNumber - renewalDate.DayNumber) / 365 + 2);
            var successorFinanceId = all.Select(pattern => pattern.FinanceId).DefaultIfEmpty(0).Max() + 1;

            // A renewal changes nothing, so the successor is built directly and the pattern simply continues — no break-off-style fresh plan proposal (which would also invent a savings plan for a bill that has none).
            // The predecessor already ends the day before renewalDate, so it needs no change.
            // Mirrors BreakOffFactory.Renew's successor + "(renewed …)" label, minus the proposal.
            var successor = FinancialPattern.Create(new FinancialPatternOptions
            {
                FinanceId = successorFinanceId,
                Source = segment.Source,
                Description = $"{StripRenewalMarker(segment.Description ?? segment.Source)} (renewed {renewalDate:yyyy-MM-dd})",
                Notes = segment.Notes,
                // Active from renewalDate but first occurring on the segment's next real date, so it keeps its days, as the continued plans below do.
                DatePattern = segment.DatePattern.ReanchoredToStartOn(renewalDate).WithUntil(renewalDate.AddYears(segmentYears)),
                Amount = segment.Amount,
                Priority = segment.Priority,
                Mandatory = segment.Mandatory,
                AutoRenew = segment.AutoRenew,
            });
            _financialPatterns.Save(successor, accountId);

            // Continue each earmark pattern unchanged: same amount and cadence, its span re-anchored onto the new segment (ReanchoredToStartOn keeps the contribution days; WithUntil reaches the successor's border).
            // No rows here means the pattern has no savings plan — nothing to carry.
            foreach (var existing in _earMarkPatterns.GetAll().Where(plan => plan.FinanceId == segment.FinanceId))
            {
                var continued = EarMarkPattern.Create(
                    new EarMarkPatternOptions
                    {
                        FinanceId = successor.FinanceId,
                        DatePattern = existing.DatePattern.ReanchoredToStartOn(renewalDate).WithUntil(successor.DatePattern.Until),
                        Amount = existing.Amount,
                        StartingAllocation = 0m,
                        // An automatic renewal takes no user input, so the continuing plan inherits the existing plan's standing — a dummy that keeps going stays a dummy (EarMarkPattern.ExplicitlyCreated).
                        ExplicitlyCreated = existing.ExplicitlyCreated,
                    },
                    successor);
                _earMarkPatterns.Save(continued);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // A single malformed ongoing pattern must not break the whole forecast.
            ErrorLog.Record($"renewing ongoing pattern {segment.FinanceId} ({segment.Source}); skipped it", ex);
        }
    }

    /// <summary>[CALC] Strips a "(renewed yyyy-MM-dd)" suffix from a label so a fresh one can replace it — mirrors BreakOffFactory's own private marker convention, so a pattern renewed year after year reads "(renewed 2031-…)", not a pile-up.</summary>
    /// <param name="label">The label to strip a prior renewal marker from.</param>
    private static string StripRenewalMarker(string label)
    {
        var markerIndex = label.IndexOf(" (renewed ", StringComparison.Ordinal);
        return markerIndex < 0 ? label : label[..markerIndex];
    }

    /// <summary>
    /// [WRITES FILE] Extends every ongoing (AutoRenew) transfer's end date out past the horizon so it keeps going however far you forecast.
    /// Transfers use extend-in-place rather than the bill's chain-of-segments: they have no distinctive identity to chain on (every "Transfer to Savings" leg looks alike), so the transfer and both legs are rebuilt at the same ids with a later end date, and the withdrawal's reservation is re-fitted to the longer span.
    /// A no-op — no writes — when every ongoing transfer already reaches the horizon.
    /// </summary>
    /// <param name="horizon">The forecast horizon the ongoing transfers must reach.</param>
    private void ExtendOngoingTransfersToHorizon(DateOnly horizon)
    {
        var namesById = _accounts.GetAll().ToDictionary(account => account.Id, account => account.Name);

        foreach (var transfer in _transfers.GetAll())
        {
            try
            {
                if (transfer.DatePattern.Until >= horizon)
                {
                    continue;
                }

                var legs = _financialPatterns.GetByTransferId(transfer.Id);
                var withdrawal = legs.FirstOrDefault(leg => leg.Amount < 0m);
                var deposit = legs.FirstOrDefault(leg => leg.Amount > 0m);

                // Ongoing only (the withdrawal leg carries the flag), and only a well-formed pair whose accounts still exist — otherwise leave it be.
                if (withdrawal is not { AutoRenew: true } || deposit is null
                    || !namesById.ContainsKey(transfer.FromAccountId) || !namesById.ContainsKey(transfer.ToAccountId))
                {
                    continue;
                }

                var newUntil = AddOneCycle(horizon, transfer.DatePattern.Frequency, transfer.DatePattern.Interval);
                var result = TransferFactory.Create(new TransferRequest
                {
                    TransferId = transfer.Id,
                    WithdrawalFinanceId = withdrawal.FinanceId,
                    DepositFinanceId = deposit.FinanceId,
                    FromAccountId = transfer.FromAccountId,
                    ToAccountId = transfer.ToAccountId,
                    FromAccountName = namesById[transfer.FromAccountId],
                    ToAccountName = namesById[transfer.ToAccountId],
                    Amount = transfer.Amount,
                    DatePattern = transfer.DatePattern.WithUntil(newUntil),
                    AutoRenew = true,
                });

                // Clear the old withdrawal reservation before re-fitting it to the longer span, so a re-proposed plan on a different key can't leave a stale duplicate behind.
                // A transfer's reservation is mechanical (never a user-designed plan), so re-proposing it — as the create flow does — is fine here.
                var withdrawalPlan = AllocationPlanProposer.Propose(result.Withdrawal, [], CurrentAsOfDate(), spreadEvenlyWithNoIncome: false);
                _earMarkPatterns.Delete(withdrawal.FinanceId);
                _transfers.Save(result with { Withdrawal = withdrawalPlan.Outflow });
                _earMarkPatterns.Save(withdrawalPlan.Plan);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // A single malformed transfer must not break the whole forecast.
                ErrorLog.Record($"extending ongoing transfer {transfer.Id}; skipped it", ex);
            }
        }
    }

    /// <summary>[CALC] One repeat-cycle past the given date — the buffer an ongoing pattern's end date carries past the horizon so its last occurrence isn't clipped at the edge.</summary>
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

    /// <summary>[CALC] Builds the ForecastOptions for the current data over the given window — the shared input both RefreshForecast (the shown forecast) and ForecastOmitting (the affordability re-forecast) run through, so the two can't drift apart.</summary>
    /// <param name="asOfDate">The as-of date.</param>
    /// <param name="horizonEndDate">The horizon end date.</param>
    private ForecastOptions BuildForecastOptions(DateOnly asOfDate, DateOnly horizonEndDate) => new()
    {
        // Ignored when Accounts is set (there is always at least one account), but still required by the record.
        FinancialPatterns = [],
        EarMarkPatterns = [],
        StartingBalance = 0m,
        AsOfDate = asOfDate,
        HorizonEndDate = horizonEndDate,
        Accounts = BuildAccountInputs(),
    };

    /// <summary>
    /// [CALC] Re-runs the forecast with the given goals' savings plans omitted — the "room for these plans" view the save-confirmation's affordability ceiling sizes suggestions against.
    /// Same inputs and window as the shown forecast, just filtered; deliberately does NOT touch _lastForecast (a throwaway calculation, not the shown forecast).
    /// </summary>
    /// <param name="omitFinanceIds">The goals whose EarMarkPatterns to leave out.</param>
    private ForecastResult ForecastOmitting(IReadOnlySet<int> omitFinanceIds)
    {
        var asOfDate = _lastForecast?.AsOfDate ?? CurrentAsOfDate();
        var horizonEnd = _lastForecast?.HorizonEndDate ?? CurrentAsOfDate().AddMonths(3);
        return TransactionLogBookFactory.CreateForecast(
            BuildForecastOptions(asOfDate, horizonEnd).WithoutPlansFor(omitFinanceIds));
    }

    /// <summary>
    /// [CALC] Re-runs the forecast with one not-yet-saved one-off earmark folded in — the live "what if I saved this one-off" preview the Earmark form's summary reads its jar off.
    /// Same inputs and window as the shown forecast, just with the proposed earmark added; a throwaway calculation, never stored on _lastForecast.
    /// </summary>
    /// <param name="oneOff">The proposed one-off earmark to include.</param>
    private ForecastResult ForecastWithOneOff(ManualEarmark oneOff)
    {
        var asOfDate = _lastForecast?.AsOfDate ?? CurrentAsOfDate();
        var horizonEnd = _lastForecast?.HorizonEndDate ?? CurrentAsOfDate().AddMonths(3);
        return TransactionLogBookFactory.CreateForecast(
            BuildForecastOptions(asOfDate, horizonEnd).WithManualEarmark(oneOff));
    }

    /// <summary>
    /// [CALC] Re-runs the forecast with the Expense form's typed bill or paycheck swapped in — the live "what if I saved this edit" reading behind the form's status message and "Save and Plan" outline.
    /// Same inputs and window as the shown forecast; a throwaway calculation, never stored on _lastForecast.
    /// </summary>
    /// <param name="proposed">The typed, not-yet-saved pattern.</param>
    /// <param name="accountId">The account picked on the form.</param>
    private ForecastResult ForecastWithProposedPattern(FinancialPattern proposed, int accountId)
    {
        var asOfDate = _lastForecast?.AsOfDate ?? CurrentAsOfDate();
        var horizonEnd = _lastForecast?.HorizonEndDate ?? CurrentAsOfDate().AddMonths(3);
        return TransactionLogBookFactory.CreateForecast(
            BuildForecastOptions(asOfDate, horizonEnd).WithProposedPattern(proposed, accountId));
    }

    /// <summary>
    /// [CALC] Re-runs the forecast with a not-yet-saved savings plan substituted in (plus any proposed manual earmarks that go with it, e.g. a starting earmark) — the live "what if I saved this plan" preview the Earmark form's first-payment warning reads its free-funds figure off.
    /// Same inputs and window as the shown forecast; a throwaway calculation, never stored on _lastForecast.
    /// </summary>
    /// <param name="proposed">The proposed plan to substitute in.</param>
    /// <param name="replacedActiveStart">The saved segment it replaces, or null for a brand-new plan.</param>
    /// <param name="proposedManualEarmarks">Any proposed manual earmarks to fold in alongside it.</param>
    private ForecastResult ForecastWithProposedPlan(
        EarMarkPattern proposed, DateOnly? replacedActiveStart, IReadOnlyList<ManualEarmark> proposedManualEarmarks)
    {
        var asOfDate = _lastForecast?.AsOfDate ?? CurrentAsOfDate();
        var horizonEnd = _lastForecast?.HorizonEndDate ?? CurrentAsOfDate().AddMonths(3);
        var options = BuildForecastOptions(asOfDate, horizonEnd).WithProposedPlan(proposed, replacedActiveStart);
        foreach (var earmark in proposedManualEarmarks)
        {
            options = options.WithManualEarmark(earmark);
        }

        return TransactionLogBookFactory.CreateForecast(options);
    }

    /// <summary>
    /// [UI] "All accounts" plus one entry per account.
    /// Kept in step with the forecast so a renamed or deleted account can't linger in the filter.
    /// </summary>
    /// <param name="forecast">The just-computed forecast, for its current account list.</param>
    private void PopulateAccountFilter(ForecastResult forecast)
    {
        var options = new List<AccountFilterOption> { new(null, "All accounts") };
        options.AddRange(forecast.Accounts.Select(account => new AccountFilterOption(account.AccountId, account.Name)));

        if (options.All(option => option.AccountId != _accountFilter))
        {
            _accountFilter = null; // the filtered account is gone — fall back to the household
        }

        _updatingAccountFilter = true;
        AccountFilterComboBox.ItemsSource = options;
        AccountFilterComboBox.SelectedItem = options.First(option => option.AccountId == _accountFilter);
        _updatingAccountFilter = false;
    }

    private void OnAccountFilterChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_updatingAccountFilter || AccountFilterComboBox.SelectedItem is not AccountFilterOption option)
        {
            return;
        }

        _accountFilter = option.AccountId;
        if (_lastForecast is { } forecast)
        {
            // Keep the user on the day they were looking at, if it still exists.
            RenderCalendar(forecast, selectDate: _selectedDayCell?.Date ?? forecast.AsOfDate);
        }
    }

    /// <summary>[UI] Rebuilds the calendar from the forecast already in hand (no recompute) and restores the selected day.</summary>
    /// <param name="forecast">The forecast to render.</param>
    /// <param name="selectDate">Which day to select after rendering.</param>
    private void RenderCalendar(ForecastResult forecast, DateOnly selectDate)
    {
        var months = BuildCalendarMonths(forecast, _accountFilter);
        _dayCellsByDate = months
            .SelectMany(month => month.Cells)
            .Where(cell => cell.HasSnapshot)
            .ToDictionary(cell => cell.Date!.Value);
        _selectedDayCell = null;
        TimelineCalendar.ItemsSource = months;

        // Never leave the detail pane blank: fall back to the as-of day if the day that was selected has no cell under the current filter.
        if (_dayCellsByDate.TryGetValue(selectDate, out var cell)
            || _dayCellsByDate.TryGetValue(forecast.AsOfDate, out cell))
        {
            SelectDay(cell);
        }

        // Land the viewport on today's month when today is in range; otherwise start at the top.
        // Deferred to Loaded priority because the virtualizing panel hasn't measured at the moment ItemsSource is set.
        var today = DateOnly.FromDateTime(DateTime.Today);
        var targetMonth = today >= forecast.AsOfDate && today <= forecast.HorizonEndDate
            ? months.FirstOrDefault(month => month.MonthAutomationId == $"Month_{today:yyyy-MM}") ?? months[0]
            : months[0];
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => TimelineCalendar.ScrollIntoView(targetMonth));
    }

    /// <summary>
    /// [CALC] The rich month calendar.
    /// Each active day shows two LABELED numbers (Total + Free), the day's top event by name, an explicit event count, a per-account flow strip, and a ⚠ + words warning when an account is short.
    /// </summary>
    /// <param name="forecast">The forecast to render.</param>
    /// <param name="accountFilter">Re-scopes every number to one account; null means the household roll-up.</param>
    private static List<MonthRow> BuildCalendarMonths(ForecastResult forecast, int? accountFilter)
    {
        var scope = accountFilter is { } id
            ? forecast.Accounts.Where(account => account.AccountId == id).ToList()
            : forecast.Accounts.ToList();

        var flooredDates = forecast.FlooredManualEarmarks.Select(floored => floored.Date).ToHashSet();

        // finance id -> (priority, display name), for picking the day's top event.
        var patternInfo = forecast.Accounts
            .SelectMany(account => account.Page.FinancePatterns)
            .GroupBy(pattern => pattern.FinanceId)
            .ToDictionary(group => group.Key, group => (group.First().Priority, Name: group.First().Description ?? group.First().Source));

        /// <summary>
        /// [CALC] An event the USER would count: a transaction, a scheduled allocation, or a manual adjustment.
        /// System-generated reservation steps (cushion fills, deallocation give-backs, goal releases) are mechanism, not events, so they are not counted.
        /// </summary>
        /// <param name="snapshot">The day's balance snapshot to count events in.</param>
        static int CountEvents(BalanceSnapshot snapshot) =>
            snapshot.ExpectedTransactions.Count(transaction => !transaction.Cancelled)
            + snapshot.EarMarkEvents.Count(earmark =>
                earmark.RepeatedEarmark || (earmark.ExplicitAmount is { } explicitAmount && explicitAmount != 0m));

        var months = new List<MonthRow>();
        var firstOfMonth = new DateOnly(forecast.AsOfDate.Year, forecast.AsOfDate.Month, 1);
        while (firstOfMonth <= forecast.HorizonEndDate)
        {
            var cells = new List<DayCellRow>();
            for (var padding = 0; padding < (int)firstOfMonth.DayOfWeek; padding++)
            {
                cells.Add(DayCellRow.Padding());
            }

            var daysInMonth = DateTime.DaysInMonth(firstOfMonth.Year, firstOfMonth.Month);
            for (var day = 1; day <= daysInMonth; day++)
            {
                var date = new DateOnly(firstOfMonth.Year, firstOfMonth.Month, day);
                var isAsOf = date == forecast.AsOfDate;
                var hasEvents = scope.Any(account => account.Page.BalanceRecord.ContainsKey(date));

                if (!isAsOf && !hasEvents)
                {
                    cells.Add(new DayCellRow(date));
                    continue;
                }

                var total = 0m;
                var free = 0m;
                var eventCount = 0;
                var shortNames = new List<string>();
                var flows = new List<AccountFlowCell>();
                var todaysTransactions = new List<ExpectedTransaction>();
                var deallocated = false;

                foreach (var account in scope)
                {
                    // On the as-of day every account reports its settled seed; otherwise carry forward its latest snapshot.
                    var sampled = isAsOf ? account.Page.InitialSnapshot : SnapshotAsOf(account.Page, date);
                    total += sampled.ExpectedAmount ?? 0m;
                    var accountFree = AccountFreeOn(account, date, forecast.AsOfDate);
                    free += accountFree;
                    if (accountFree < 0m)
                    {
                        shortNames.Add(account.Name);
                    }

                    // Events (and therefore flow) only exist on the account's own event dates — never on the settled as-of day.
                    var onThisDay = isAsOf ? null : account.Page.BalanceRecord.GetValueOrDefault(date);
                    var net = 0m;
                    if (onThisDay is not null)
                    {
                        eventCount += CountEvents(onThisDay);
                        todaysTransactions.AddRange(onThisDay.ExpectedTransactions.Where(transaction => !transaction.Cancelled));
                        net = onThisDay.ExpectedTransactions.Where(transaction => !transaction.Cancelled).Sum(transaction => transaction.ExpectedAmount);
                        deallocated |= onThisDay.IsDeallocationDay;
                    }

                    flows.Add(new AccountFlowCell
                    {
                        Letter = account.Name.Length > 0 ? account.Name[..1].ToUpperInvariant() : "?",
                        Glyph = net > 0m ? "↑" : net < 0m ? "↓" : "•",
                        Kind = net > 0m ? "In" : net < 0m ? "Out" : "None",
                    });
                }

                // The day's highest-priority expected transaction, by name.
                var topEvent = todaysTransactions
                    .Select(transaction => patternInfo.TryGetValue(transaction.FinanceId, out var info)
                        ? (info.Priority, info.Name, Magnitude: Math.Abs(transaction.ExpectedAmount))
                        : (Priority: 0, Name: forecast.JarLabels.GetValueOrDefault(transaction.FinanceId, string.Empty), Magnitude: Math.Abs(transaction.ExpectedAmount)))
                    .OrderByDescending(candidate => candidate.Priority)
                    .ThenByDescending(candidate => candidate.Magnitude)
                    .Select(candidate => candidate.Name)
                    .FirstOrDefault() ?? string.Empty;

                // ⚠ + words, never color alone.
                // "Thin" has no defined threshold yet, so only the definite "short" case is worded.
                var warning = shortNames.Count switch
                {
                    0 => string.Empty,
                    _ when accountFilter is not null => "this account is short",
                    1 => "an account is short",
                    _ => $"{shortNames.Count} accounts are short",
                };

                cells.Add(new DayCellRow(
                    date,
                    total,
                    free,
                    topEvent,
                    eventCount,
                    flows,
                    warning,
                    needsAttention: shortNames.Count > 0 || deallocated || flooredDates.Contains(date)));
            }

            months.Add(new MonthRow
            {
                Title = firstOfMonth.ToString("MMMM yyyy"),
                MonthAutomationId = $"Month_{firstOfMonth:yyyy-MM}",
                Cells = cells,
            });
            firstOfMonth = firstOfMonth.AddMonths(1);
        }

        return months;
    }

    private void OnDayCellClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DayCellRow { HasSnapshot: true } cell })
        {
            SelectDay(cell);
        }
    }

    private void SelectDay(DayCellRow cell)
    {
        if (_selectedDayCell is { } previous)
        {
            previous.IsSelected = false;
        }

        _selectedDayCell = cell;
        cell.IsSelected = true;
        ShowDayDetail(cell.Date!.Value);
    }

    // How thin free funds may get before we warn, as a multiple of the account's safety-cushion target: 1.0 = warn once free drops below one cushion's worth.
    private const decimal ThinnessMultiplier = 1.0m;

    /// <summary>
    /// [UI] The detail pane is the inner layer of the display onion: the cell IS a BalanceSnapshot, and selecting it shows everything the day holds — every event ("what happened today", left) and every fund jar with its per-type health (right), plus the day's free amount.
    /// Account-first (grouped two-pane): both panes group by account so each account's story — its events (left) and its jars (right) — stays together.
    /// The header shows the household free to spend and names any short account, so a positive total never hides a locally-short one.
    /// </summary>
    /// <param name="date">The day to show detail for.</param>
    private void ShowDayDetail(DateOnly date)
    {
        if (_lastForecast is not { } forecast)
        {
            return;
        }

        // The pane shows whatever the calendar's own account filter is showing: one account when filtered, every account otherwise.
        // Scoping here keeps the header, events, and jars about the same accounts as the cell the user clicked — so the free figures can't disagree.
        var scopedAccounts = _accountFilter is { } scopeId
            ? forecast.Accounts.Where(account => account.AccountId == scopeId).ToList()
            : forecast.Accounts.ToList();

        var householdDay = forecast.Household.Days.FirstOrDefault(day => day.Date == date);
        // Free to spend, summed the same way the calendar cell does and over the same accounts, so the header and the cell always agree for the same day.
        var householdFree = scopedAccounts.Sum(account => AccountFreeOn(account, date, forecast.AsOfDate));
        var shortAccounts = householdDay?.ShortAccounts ?? [];
        if (_accountFilter is { } filteredId)
        {
            // Filtered to one account: the pane is about it alone, so the header names it as short only when it is the account that's short.
            var filteredName = forecast.Accounts.FirstOrDefault(account => account.AccountId == filteredId)?.Name;
            shortAccounts = shortAccounts.Where(name => name == filteredName).ToList();
        }
        var anyDeallocation = scopedAccounts.Any(account => account.Page.BalanceRecord.GetValueOrDefault(date)?.IsDeallocationDay == true);

        DeallocationDayChip.Visibility = anyDeallocation ? Visibility.Visible : Visibility.Collapsed;
        DayDetailHeader.Text = shortAccounts.Count > 0
            ? $"Selected day — {date:D}  ·  {string.Join(", ", shortAccounts)} short on cash"
            : $"Selected day — {date:D}";

        FreeToSpendText.Text = householdFree.ToString("C");
        FreeToSpendText.Foreground = householdFree < 0m
            ? (Brush)FindResource("RedTextBrush")
            : Brushes.Black;

        // Each account's free on this day, computed up front so a short account's gap can be classified as coverable-by-transfer (rung 2, name the donor) vs. genuinely short household-wide (rung 3) — the ladder.
        // Deliberately over EVERY account, not just the scoped one: a filtered account that's short can still be told which other account could cover it.
        var freeByAccount = forecast.Accounts.ToDictionary(
            account => account.AccountId,
            account => (account.Page.BalanceRecord.GetValueOrDefault(date) ?? SnapshotAsOf(account.Page, date)).ExpectedFreeAmount ?? 0m);

        var eventRows = new List<DayEventRow>();
        var jarRows = new List<JarDetailRow>();
        var anyEarmarks = false;
        var anyUserSetAside = false;

        foreach (var account in scopedAccounts)
        {
            var page = account.Page;
            var snapshot = page.BalanceRecord.GetValueOrDefault(date) ?? SnapshotAsOf(page, date);
            anyEarmarks |= page.EarmarkPatterns.Count > 0;

            var earmarkedIds = page.EarmarkPatterns.Select(earmark => earmark.FinanceId).ToHashSet();

            // The user has funded a savings plan of their own when a jar tied to one of their earmark patterns holds money — auto-reserved mandatory bills don't count.
            // Its absence (with cash to spare) is the rung-1 nudge's cue.
            anyUserSetAside |= snapshot.FundJars.Any(jar =>
                jar.FinanceId is { } fundedId && jar.ExpectedAmount > 0m && earmarkedIds.Contains(fundedId));

            // How short this account is on this day, and — when short — whether other accounts hold enough free cash to cover it (rung 2, naming a single-account donor) or not (rung 3).
            // Drives the header narrative + lever.
            var accountBalance = snapshot.ExpectedAmount ?? 0m;
            var accountFree = freeByAccount[account.AccountId];
            var shortfall = accountFree < 0m ? -accountFree : 0m;
            var canCoverElsewhere = false;
            string? donorName = null;
            if (shortfall > 0m)
            {
                var donors = forecast.Accounts
                    .Where(other => other.AccountId != account.AccountId && freeByAccount[other.AccountId] > 0m)
                    .Select(other => (other.Name, Free: freeByAccount[other.AccountId]))
                    .ToList();
                canCoverElsewhere = donors.Sum(donor => donor.Free) >= shortfall;
                if (canCoverElsewhere)
                {
                    // Name the donor only when one account alone covers the whole gap; otherwise the cash is spread and the line stays generic.
                    donorName = donors
                        .Where(donor => donor.Free >= shortfall)
                        .OrderByDescending(donor => donor.Free)
                        .Select(donor => donor.Name)
                        .FirstOrDefault();
                }
            }
            // "Funds running low": free funds have fallen toward the reserve but not gone negative (negative is "short", the harsher state on the same axis).
            // Skipped when no cushion is set — nothing to be thin against.
            // This also covers a dipped safety cushion: the cushion only drops below target when a deallocation drains it to avoid going short, which leaves free near zero, so a reserve dip always reads as thin anyway (folded in here rather than shown as its own warning).
            var cushionTarget = page.IdealSafetyCushion;
            var isThin = cushionTarget > 0m && accountFree >= 0m && accountFree < ThinnessMultiplier * cushionTarget;

            var groupKey = new AccountGroupKey(account.AccountId, account.Name, accountBalance, shortfall, canCoverElsewhere, donorName, isThin);

            var context = new DayDetailContext
            {
                Date = date,
                JarLabels = forecast.JarLabels,
                ShortfallsByFinanceId = forecast.GoalShortfalls.ToDictionary(goal => goal.FinanceId),
                PatternsById = page.FinancePatterns.ToDictionary(pattern => pattern.FinanceId),
                EarmarkedIds = earmarkedIds,
                CushionTarget = page.IdealSafetyCushion,
                FlooredFinanceIds = forecast.FlooredManualEarmarks
                    .Where(floored => floored.Date == date)
                    .Select(floored => floored.FinanceId)
                    .ToHashSet(),
            };

            // Events land only on an account's own event dates; jars carry forward, so every account shows its current jars on any selected day.
            if (page.BalanceRecord.ContainsKey(date))
            {
                foreach (var eventRow in DayEventRow.From(snapshot, context))
                {
                    eventRow.Account = groupKey;
                    eventRows.Add(eventRow);
                }
            }

            foreach (var jar in snapshot.FundJars)
            {
                // Omit the safety-cushion row when the account has no cushion set — don't spend the pane's height on a jar the user never opted into.
                if (jar.FinanceId is null && page.IdealSafetyCushion <= 0m)
                {
                    continue;
                }
                var jarRow = JarDetailRow.From(jar, snapshot, context);
                jarRow.Account = groupKey;
                jarRows.Add(jarRow);
            }
        }

        // Rung-1 nudge: spare cash on hand, but the user hasn't funded any goal of their own, so the free figure is really just the balance.
        var showFreeNudge = householdFree > 0m && !anyUserSetAside;
        FreeNudgeText.Text = showFreeNudge
            ? "Set money aside for your goals so this reflects what's really spare."
            : string.Empty;
        FreeNudgeText.Visibility = showFreeNudge ? Visibility.Visible : Visibility.Collapsed;

        AdjustFundsButton.IsEnabled = anyEarmarks;
        DayEventsList.ItemsSource = GroupByAccount(eventRows);
        JarDetailList.ItemsSource = GroupByAccount(jarRows);
    }

    /// <summary>
    /// [CALC] Both selected-day panes group their rows under an account header.
    /// The rows expose an Account property the group description reads.
    /// </summary>
    /// <param name="rows">The event or jar rows to group.</param>
    private static System.ComponentModel.ICollectionView GroupByAccount(System.Collections.IList rows)
    {
        var view = new System.Windows.Data.ListCollectionView(rows);
        view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("Account"));
        return view;
    }

    /// <summary>[CALC] An account's snapshot as of a date: the latest dated snapshot on or before it, else its dateless initial snapshot — so a day that is another account's event date still shows this account's carried-forward jars.</summary>
    /// <param name="page">The account to read a snapshot from.</param>
    /// <param name="date">The date to read the snapshot as of.</param>
    private static BalanceSnapshot SnapshotAsOf(AccountTransactionPage page, DateOnly date)
    {
        var snapshot = page.InitialSnapshot;
        foreach (var (snapshotDate, dated) in page.BalanceRecord)
        {
            if (snapshotDate > date)
            {
                break;
            }
            snapshot = dated;
        }

        return snapshot;
    }

    /// <summary>
    /// [CALC] One account's free-to-spend on a date: its ExpectedFreeAmount — the settled seed on the as-of day, else its carried-forward snapshot.
    /// The single definition the calendar cell and the selected-day header both sum, so the two views can never show different free figures for the same day.
    /// </summary>
    /// <param name="account">The account to read free funds from.</param>
    /// <param name="date">The day to read as of.</param>
    /// <param name="asOfDate">The forecast's as-of date, which reads its settled seed rather than a carried-forward snapshot.</param>
    private static decimal AccountFreeOn(AccountForecast account, DateOnly date, DateOnly asOfDate) =>
        (date == asOfDate ? account.Page.InitialSnapshot : SnapshotAsOf(account.Page, date)).ExpectedFreeAmount ?? 0m;

    /// <summary>
    /// [UI] The Forecast button reads as actionable only while an input (range, balance, or cushion) differs from the forecast on screen.
    /// Wired to every input's change event; unparseable text counts as "differs" so the button stays live and the click handler can explain what's wrong.
    /// </summary>
    private void OnForecastInputChanged(object sender, RoutedEventArgs e) => UpdateForecastButtonState();

    /// <summary>
    /// [UI] When the "As of" field is empty, opens its calendar on the current month rather than wherever it was last left — so returning to today isn't a long scroll back after forecasting years out.
    /// A WPF DatePicker keeps its DisplayDate on the last month shown once its date is cleared; this resets it each time the picker opens with no date.
    /// </summary>
    private void OnAsOfCalendarOpened(object sender, RoutedEventArgs e)
    {
        if (AsOfDatePicker.SelectedDate is null)
        {
            AsOfDatePicker.DisplayDate = DateTime.Today;
        }
    }

    /// <summary>[UI] When "Show forecast through" is empty, opens its calendar a few months past the "As of" date (or past today, if that's empty too) — the usual place a horizon lands, so it isn't a long scroll from wherever it was last left.</summary>
    private void OnHorizonCalendarOpened(object sender, RoutedEventArgs e)
    {
        if (HorizonEndDatePicker.SelectedDate is null)
        {
            var basis = AsOfDatePicker.SelectedDate ?? DateTime.Today;
            HorizonEndDatePicker.DisplayDate = basis.AddMonths(3);
        }
    }

    private void UpdateForecastButtonState()
    {
        if (ForecastButton is null)
        {
            return; // input events can fire while InitializeComponent is mid-parse
        }

        if (_lastForecast is not { } shown)
        {
            ForecastButton.IsEnabled = true;
            return;
        }

        var datesMatch =
            AsOfDatePicker.SelectedDate is { } asOf && DateOnly.FromDateTime(asOf) == shown.AsOfDate
            && HorizonEndDatePicker.SelectedDate is { } horizon && DateOnly.FromDateTime(horizon) == shown.HorizonEndDate;

        // Editing an account changes the totals the shown forecast was built from, so the button lights up exactly as a changed date does.
        var (balance, cushion) = AccountTotals();
        ForecastButton.IsEnabled = !(datesMatch && balance == _shownBalance && cushion == _shownCushion);
    }

    /// <summary>[UI] Re-runs the forecast with the inputs it's already showing — for when data that feeds it (manual earmarks) changed rather than the inputs — keeping the same selected day when it still exists.</summary>
    private void RefreshShownForecast()
    {
        if (_lastForecast is not { } shown)
        {
            return;
        }

        var selectedDate = _selectedDayCell?.Date;
        RefreshForecast(shown.AsOfDate, shown.HorizonEndDate);
        if (selectedDate is { } date && _dayCellsByDate.TryGetValue(date, out var cell))
        {
            SelectDay(cell);
        }
    }

    private void OnAdjustFundsClick(object sender, RoutedEventArgs e) =>
        ShowManualEarmarkForm(initialDate: _selectedDayCell?.Date, editTarget: null);

    private void OnAddManualEarmarkClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmLeavingUnsavedChanges(EarmarkForm.IsDirty, "Allocations", "Earmark"))
        {
            return;
        }

        ShowManualEarmarkForm(initialDate: null, editTarget: null);
    }

    private void OnEditManualEarmarkClick(object sender, RoutedEventArgs e)
    {
        if (ManualEarmarksGrid.SelectedItem is not ManualEarmarkRow row)
        {
            ShowNothingSelected();
            return;
        }

        ShowManualEarmarkForm(initialDate: null, editTarget: row.Earmark);
    }

    private void OnDeleteManualEarmarkClick(object sender, RoutedEventArgs e)
    {
        if (ManualEarmarksGrid.SelectedItem is not ManualEarmarkRow row)
        {
            ShowNothingSelected();
            return;
        }

        if (!ConfirmDelete($"the {row.Target} adjustment on {row.Date}"))
        {
            return;
        }

        _manualEarmarks.Delete(row.Earmark.FinanceId, row.Earmark.Date);
        RefreshGrids();
        RefreshShownForecast();
    }

    /// <summary>
    /// [STEP] Opens Allocations › Form in One-off mode, blank or loaded with the adjustment being edited.
    /// Refuses with a "no funds yet" message when there is no savings plan to adjust.
    /// </summary>
    /// <param name="initialDate">Day to pre-select, if any.</param>
    /// <param name="editTarget">The existing manual earmark being edited, or null to add a new one.</param>
    private void ShowManualEarmarkForm(DateOnly? initialDate, ManualEarmark? editTarget)
    {
        if (_earMarkPatterns.GetAll().Count == 0)
        {
            MessageBox.Show(
                this,
                "Create a savings plan first — manual adjustments live inside a fund's plan (its timeline is the fund's lifetime).",
                "No funds yet", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        RefreshEarmarkFormContext();
        EarmarkForm.LoadOneOff(editTarget, initialDate);
        SwitchToTab("Earmark");
    }

    /// <summary>
    /// [WRITES FILE] Output-only snapshot of whatever forecast is currently on screen — not to be confused with Export Data above, which copies the raw db file for backup/transfer.
    /// See ForecastSpreadsheetExporter.
    /// </summary>
    private void OnExportForecastSpreadsheetClick(object sender, RoutedEventArgs e)
    {
        if (_lastForecast is not { } forecast)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export forecast as spreadsheet",
            FileName = $"buytheboat-{forecast.AsOfDate:yyyy-MM-dd}.xlsx",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*",
        };

        if (AppPaths.DefaultExportFolder is { } exportFolder)
        {
            Directory.CreateDirectory(exportFolder);
            dialog.InitialDirectory = exportFolder;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ForecastSpreadsheetExporter.Export(forecast, dialog.FileName);
        }
        catch (IOException ex)
        {
            ErrorLog.Record("exporting the forecast spreadsheet", ex);
            MessageBox.Show(this, $"Couldn't export: {ex.Message}", "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        MessageBox.Show(this, $"Exported to {dialog.FileName}.", "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RefreshGrids()
    {
        var financialPatterns = _financialPatterns.GetAll();
        var earMarkPatterns = _earMarkPatterns.GetAll();

        // financeId -> the name of the account it's filed under, so the grid can show where each bill/paycheck/goal lives — never a mystery.
        var accountNamesById = _accounts.GetAll().ToDictionary(account => account.Id, account => account.Name);
        var accountNameByFinanceId = _financialPatterns.GetAllByAccount()
            .SelectMany(entry => entry.Value.Select(pattern => (pattern.FinanceId, AccountId: entry.Key)))
            .ToDictionary(pair => pair.FinanceId, pair => accountNamesById.GetValueOrDefault(pair.AccountId, "(unknown)"));

        // A transfer's two patterns are hidden from this list — a transfer shows on its own tab as one thing, not as its two underlying patterns.
        // The engine still reads every pattern when forecasting.
        FinancialPatternsGrid.ItemsSource = _financialPatterns.GetAllExcludingTransferPatterns()
            .Select(pattern => new FinancialPatternRow(pattern, accountNameByFinanceId.GetValueOrDefault(pattern.FinanceId, "(unknown)")))
            .ToList();

        RefreshTransfersGrid();

        // Every expense that reserves has its own Allocation Plan (an EarMarkPattern, per the allocation model), so the grid just shows those; nothing here is a synthesized "automatic" row.
        // A transfer's withdrawal reserves through a plan too, but it's hidden here for the same reason its patterns are: a transfer is shown as one thing on its own tab, not as its underlying reservation machinery.
        var transferWithdrawalIds = _financialPatterns.GetTransferWithdrawalFinanceIds();
        EarMarkPatternsGrid.ItemsSource = earMarkPatterns
            .Where(pattern => !transferWithdrawalIds.Contains(pattern.FinanceId))
            .Select(pattern => new EarMarkPatternRow(
                pattern,
                financialPatterns.FirstOrDefault(goal => goal.FinanceId == pattern.FinanceId)))
            .ToList();

        ManualEarmarksGrid.ItemsSource = _manualEarmarks.GetAll()
            .Select(earmark => new ManualEarmarkRow(
                earmark,
                financialPatterns.FirstOrDefault(pattern => pattern.FinanceId == earmark.FinanceId) is { } owner
                    ? owner.Description ?? owner.Source
                    : $"(finance id {earmark.FinanceId})"))
            .ToList();

        RefreshSpreadsheet();
        MarkRowsOpenInForms();
    }

    /// <summary>[READS FILE] Refills the Bills &amp; Paychecks › Spreadsheet subtab: the stored drafts, then every saved bill and paycheck (transfer patterns hidden, as in the List).</summary>
    private void RefreshSpreadsheet()
    {
        var accounts = _accounts.GetAll();
        var accountNamesById = accounts.ToDictionary(account => account.Id, account => account.Name);
        var accountIdByFinanceId = _financialPatterns.GetAllByAccount()
            .SelectMany(entry => entry.Value.Select(pattern => (pattern.FinanceId, AccountId: entry.Key)))
            .ToDictionary(pair => pair.FinanceId, pair => pair.AccountId);

        var saved = _financialPatterns.GetAllExcludingTransferPatterns()
            .Select(pattern => (pattern, accountIdByFinanceId.TryGetValue(pattern.FinanceId, out var accountId)
                ? accountNamesById.GetValueOrDefault(accountId, "(unknown)")
                : "(unknown)"))
            .ToList();

        Spreadsheet.SetContent(_draftRows.GetAll(), saved, accounts.Select(account => account.Name).ToList());
    }

    /// <summary>[STEP] Opens a Spreadsheet draft row in the Bills &amp; Paychecks › Form as unsaved changes, after the usual unsaved-changes warning if the Form already has some.</summary>
    private void OnOpenDraftInForm(int draftId, DraftFormValues values)
    {
        if (!ConfirmLeavingUnsavedChanges(ExpenseForm.IsDirty, "Bills & Paychecks", "Expense"))
        {
            return;
        }

        RefreshExpenseFormContext();
        ExpenseForm.LoadDraft(values, draftId);
        SwitchToTab("Expense");
    }

    /// <summary>
    /// [WRITES FILE] Exports the data by copying the SQLite file as is.
    /// Export and Import move the raw file rather than another format, since it's the single source of truth and a straight copy can't lose anything.
    /// </summary>
    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export BuyTheBoat data",
            FileName = $"buytheboat-backup-{DateTime.Today:yyyy-MM-dd}.db",
            Filter = "BuyTheBoat database (*.db)|*.db|All files (*.*)|*.*",
        };

        if (AppPaths.DefaultExportFolder is { } exportFolder)
        {
            Directory.CreateDirectory(exportFolder);
            dialog.InitialDirectory = exportFolder;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.Copy(PatternDatabase.DefaultDatabasePath(), dialog.FileName, overwrite: true);
        }
        catch (IOException ex)
        {
            ErrorLog.Record("exporting the database", ex);
            MessageBox.Show(this, $"Couldn't export: {ex.Message}", "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        MessageBox.Show(
            this,
            $"Exported to {dialog.FileName}. Copy this file to another computer and use Import Data there to restore it.",
            "Export complete",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import BuyTheBoat data",
            Filter = "BuyTheBoat database (*.db)|*.db|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!PatternDatabase.LooksLikeValidDatabaseFile(dialog.FileName))
        {
            MessageBox.Show(
                this,
                "That file doesn't look like a BuyTheBoat export — it's missing the expected data.",
                "Import failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        // Deeper than the table-presence check above: confirm the file actually LOADS (migrations run, every row re-validates) before touching the live database, so an incompatible or corrupted export is rejected here rather than crashing the app on the restart below.
        if (PatternDatabase.DescribeLoadFailure(dialog.FileName) is { } loadFailure)
        {
            ErrorLog.Record("validating a database import", new InvalidOperationException(loadFailure));
            MessageBox.Show(
                this,
                "That file couldn't be loaded — it may be from an incompatible version or corrupted. " +
                "Your current data was left unchanged.\n\nDetails: " + loadFailure,
                "Import failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var confirmed = MessageBox.Show(
            this,
            "Importing replaces everything currently in this app with the contents of the selected file. " +
            "Your current data will be backed up first, but this can't be undone from inside the app. Continue?",
            "Confirm import",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

        if (!confirmed)
        {
            return;
        }

        var liveDatabasePath = PatternDatabase.DefaultDatabasePath();
        try
        {
            PatternDatabase.ReleasePooledConnections();

            if (File.Exists(liveDatabasePath))
            {
                // A safety net taken before the overwrite.
                // In the demo this is a timestamped copy in backups\ (older ones are kept); off-demo it's the single "<db>.bak" beside the database.
                File.Copy(liveDatabasePath, AppPaths.NextImportBackupPath(), overwrite: true);
            }

            // Overwrites the live file AND removes its leftover -wal/-shm/-journal scratch files, so no stale journal can be rolled back into the imported data the next time it opens.
            PatternDatabase.ReplaceDatabaseFile(dialog.FileName, liveDatabasePath);
        }
        catch (IOException ex)
        {
            ErrorLog.Record("importing the database", ex);
            MessageBox.Show(this, $"Couldn't import: {ex.Message}", "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        MessageBox.Show(
            this,
            "Import complete. The app will now restart to load the imported data.",
            "Import complete",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        if (Environment.ProcessPath is { } processPath)
        {
            System.Diagnostics.Process.Start(processPath);
        }

        Application.Current.Shutdown();
    }

    /// <summary>
    /// [STEP] Opens a blank Bills & Paychecks › Form for a new bill, paycheck or goal.
    /// Persistence + AutoCreateAllocationPlan happen in ExpenseForm.PatternSaved (wired in the constructor), matching Earmark's shape.
    /// </summary>
    private void OnAddFinancialPatternClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmLeavingUnsavedChanges(ExpenseForm.IsDirty, "Bills & Paychecks", "Expense"))
        {
            return;
        }

        RefreshExpenseFormContext();
        ExpenseForm.LoadForNewPattern();
        SwitchToTab("Expense");
    }

    private void OnCreateBillClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmLeavingUnsavedChanges(ExpenseForm.IsDirty, "Bills & Paychecks", "Expense"))
        {
            return;
        }

        RefreshExpenseFormContext();
        ExpenseForm.LoadForNewBill();
        SwitchToTab("Expense");
    }

    /// <summary>
    /// [STEP] Saves an Expense form pattern (ExpenseForm.PatternSaved) through FinancePatternSaveConfirmation, so a Critical edit against existing history goes through EditingHistoryConfirmationWindow first.
    /// A new expense always gets a proposed plan (AutoCreateAllocationPlan), called from two places below: inside the NavigateToEarmarkForm callback for "Save and Plan", since that fires before Run() returns (re-reading the plan afterward), and here after Run() for "Save and Skip planning".
    /// </summary>
    /// <param name="pattern">The form's current field values — what the user typed, before any implicit break-off/correction Run() might apply.</param>
    /// <param name="accountId">Which account the pattern is filed under.</param>
    /// <param name="isNew">Whether this is a brand-new pattern (the proposer should run) or an edit to an existing one.</param>
    /// <param name="jumpToEarmark">True for "Save and Plan," false for "Save and Skip planning."</param>
    private bool OnExpensePatternSaved(FinancialPattern pattern, int accountId, bool isNew, bool jumpToEarmark)
    {
        // Read now: the form resets itself (clearing this) once the save goes through.
        var draftId = ExpenseForm.LoadedDraftId;

        var confirmation = new FinancePatternSaveConfirmation(
            pattern.FinanceId,
            pattern,
            accountId,
            userSkippedPlanning: !jumpToEarmark,
            EnsureForecast,
            new FinancePatternRepositories
            {
                FinancialPatterns = _financialPatterns,
                EarMarkPatterns = _earMarkPatterns,
                ManualEarmarks = _manualEarmarks,
            },
            ForecastOmitting)
        {
            ConfirmImplicitChanges = ShowEditingHistoryConfirmation,
            PickEarmarkPattern = PickEarmarkPatternToOpen,
            NavigateToEarmarkForm = (plan, suggestedOverrides) =>
            {
                // No plan yet but the user asked to plan: proposes the same affordability-capped plan a new expense gets, for a new pattern or an existing one with no plan.
                // AutoCreateAllocationPlan does nothing for income, so plan stays null and the income branch below handles it; the plan is re-read afterward.
                if (plan is null)
                {
                    // A suggested starting earmark opens in the form as an unsaved edit, so it's saved only if the user saves the plan.
                    if (AutoCreateAllocationPlan(pattern, accountId) is { } suggestedStart)
                    {
                        suggestedOverrides = new Dictionary<string, object?>(suggestedOverrides ?? new Dictionary<string, object?>())
                        {
                            [EarmarkFieldOverrideKeys.StartingEarmarkAmount] = suggestedStart.Amount,
                        };
                    }

                    plan = _earMarkPatterns.GetAll().FirstOrDefault(p => p.FinanceId == pattern.FinanceId);
                }

                // The save (and any AutoCreateAllocationPlan just above) changed the data, but _lastForecast is still the pre-save snapshot the confirmation ran against.
                // Recompute it BEFORE refreshing the form contexts, so the Earmark Summary we're about to land on shows the post-save state rather than a stale reading.
                // Guarded on there being a forecast to re-run at all — with none, the Summary already shows its own no-forecast state.
                if (_lastForecast is { } shown)
                {
                    RefreshForecast(shown.AsOfDate, shown.HorizonEndDate);
                }

                RefreshGrids();
                RefreshExpenseFormContext();
                RefreshEarmarkFormContext();

                if (plan is not null)
                {
                    // Looked up by the PLAN's own FinanceId, not pattern's — after a break-off, plan belongs to the new successor, and pattern is still the original, now-superseded values.
                    var goal = _financialPatterns.GetByFinanceId(plan.FinanceId) ?? pattern;
                    EarmarkForm.LoadPattern(plan, goal, suggestedOverrides);
                    SwitchToTab("Earmark");
                }
                else if (pattern.Amount < 0m)
                {
                    // Defensive fallback: an expense should have a plan by now (AutoCreateAllocationPlan above always saves one for Amount < 0), but if the proposal somehow produced none, open a blank Earmark form so the user can still create one by hand rather than landing nowhere.
                    EarmarkForm.LoadForNewPattern();
                    SwitchToTab("Earmark");
                }
                else
                {
                    // Income never gets a savings plan of its own, so there's nothing to open on the Earmark tab — "Save and Plan" lands back on the Forecast tab, the same as "Save and Skip planning" already does.
                    SwitchToTab("Forecast");
                }
            },
        };

        if (!confirmation.Run())
        {
            return false; // user cancelled — nothing saved; the form keeps the user's edits and the Expense tab stays as-is
        }

        // Filled from a Spreadsheet draft: the saved pattern replaces that draft row.
        if (draftId is { } savedDraftId)
        {
            _draftRows.Delete(savedDraftId);
            RefreshSpreadsheet();
        }

        if (!jumpToEarmark)
        {
            // The NavigateToEarmarkForm callback above never fires for "Save and Skip planning" (Run() only invokes it when planning wasn't skipped) — same AutoCreateAllocationPlan-then-refresh sequence, just without any Earmark-tab navigation at the end.
            // Skipping planning leaves out the suggested starting earmark; the first bill shows as short until the user covers it.
            if (isNew)
            {
                AutoCreateAllocationPlan(pattern, accountId);
            }

            // Recompute the forecast before switching to it, so the Forecast tab reflects the just-saved edit rather than the pre-save snapshot — the same refresh the "Save and Plan" path (NavigateToEarmarkForm) already does, closing a gap for this branch too.
            if (_lastForecast is { } shown)
            {
                RefreshForecast(shown.AsOfDate, shown.HorizonEndDate);
            }

            RefreshGrids();
            RefreshExpenseFormContext();
            RefreshEarmarkFormContext();
            SwitchToTab("Forecast");
        }

        return true;
    }

    /// <summary>
    /// [STEP] Saves a savings plan from the Earmark form (EarmarkForm.PatternSaved) through EarmarkPatternSaveConfirmation.
    /// A Start or Until edit that touches a chain neighbor, or an Amount or schedule edit with later segments, goes through EditingHistoryConfirmationWindow first.
    /// </summary>
    /// <param name="pattern">The form's current field values — what the user typed, before any stay-linked/cascade resolution Run() might apply.</param>
    /// <param name="savedStart">The plan's own Start as it's actually saved today, or the same as pattern's own Start for a brand-new plan (EarmarkFormPanel's own _loadedPlanStart).</param>
    private bool OnEarmarkPatternSaved(EarMarkPattern pattern, DateOnly savedStart)
    {
        var goal = _financialPatterns.GetByFinanceId(pattern.FinanceId)
            ?? throw new InvalidOperationException(
                $"No FinancialPattern found for finance_id {pattern.FinanceId} — a savings plan's own goal should always exist by the time it's saved.");

        var confirmation = new EarmarkPatternSaveConfirmation(
            pattern,
            savedStart,
            goal,
            EnsureForecast,
            new FinancePatternRepositories
            {
                FinancialPatterns = _financialPatterns,
                EarMarkPatterns = _earMarkPatterns,
                ManualEarmarks = _manualEarmarks,
            })
        {
            ConfirmImplicitChanges = ShowEditingHistoryConfirmation,
        };

        if (!confirmation.Run())
        {
            return false; // user cancelled — nothing saved; the form keeps the user's edits and the tab stays as-is
        }

        RefreshGrids();
        RefreshEarmarkFormContext();
        if (_lastForecast is { } shown)
        {
            RefreshForecast(shown.AsOfDate, shown.HorizonEndDate);
        }

        // Earmark's own save "returns to the Forecast tab — no onward hop from there to anywhere else."
        SwitchToTab("Forecast");

        return true;
    }

    /// <summary>
    /// [STEP] Shows the save-confirmation popup and returns the raw selections it reports — shared by both the Expense and Earmark save paths' ConfirmImplicitChanges wiring.
    /// When a save's rows are too tall for one screen it splits them into height-based pages, shown one after another; selections accumulate into one outcome and a cancel on any page aborts the whole save.
    /// A save whose rows fit one page is unchanged.
    /// </summary>
    private ConfirmationOutcome ShowEditingHistoryConfirmation(ImplicitChangeConfirmationRequest request)
    {
        var pages = ConfirmationPaginator.Paginate(
            request.Rows, MaxComfortableConfirmationHeight(), ConfirmationRowHeights.EstimateWorstCase);

        var chosenOptionIndex = new Dictionary<string, int>();
        EarMarkPattern? chosenPlanShape = null;

        // One dialog per page; each row lives on exactly one page, so their ids never collide as their selections accumulate.
        // A cancel on any page returns a non-proceeding outcome, which aborts the save.
        for (var pageNumber = 1; pageNumber <= pages.Count; pageNumber++)
        {
            var isFinalPage = pageNumber == pages.Count;
            var pageRequest = request with { Rows = pages[pageNumber - 1], IsFinalPage = isFinalPage };
            var confirmWindow = new EditingHistoryConfirmationWindow(pageRequest, pageNumber, pages.Count) { Owner = this };
            if (confirmWindow.ShowDialog() != true)
            {
                return new ConfirmationOutcome { Proceed = false };
            }

            var pageOutcome = confirmWindow.ToOutcome(proceed: true);
            foreach (var (rowId, index) in pageOutcome.ChosenOptionIndex)
            {
                chosenOptionIndex[rowId] = index;
            }

            chosenPlanShape ??= pageOutcome.ChosenPlanShape;
        }

        return new ConfirmationOutcome
        {
            Proceed = true,
            ChosenOptionIndex = chosenOptionIndex,
            ChosenPlanShape = chosenPlanShape,
        };
    }

    /// <summary>
    /// [CALC] The comfortable height budget for one confirmation page's rows — the usable screen height less room for the window's title bar, description, page indicator, button row, and margins, floored so a small screen still makes progress.
    /// Feeds ConfirmationPaginator.Paginate.
    /// </summary>
    private static double MaxComfortableConfirmationHeight() =>
        System.Math.Max(300, SystemParameters.WorkArea.Height - 260);

    /// <summary>
    /// [UI] AskWhichEarmarkPatternToOpen's own real disambiguation — shows EarmarkPatternPickerWindow and returns whichever plan the user picked.
    /// Cancelling the picker returns null, which AskWhichEarmarkPatternToOpen reads as "don't navigate" — the save has already committed, so the user simply stays on their current tab rather than being dropped onto a plan they didn't choose.
    /// </summary>
    /// <param name="plans">Every EarMarkPattern surviving for the goal — always more than one; AskWhichEarmarkPatternToOpen's own gate never calls this otherwise.</param>
    private EarMarkPattern? PickEarmarkPatternToOpen(IReadOnlyList<EarMarkPattern> plans)
    {
        var picker = new EarmarkPatternPickerWindow(plans) { Owner = this };
        return picker.ShowDialog() == true ? picker.SelectedPlan : null;
    }

    /// <summary>
    /// [UI] What ExpenseForm.PickChainSegment calls (wired in the constructor) — shows ChainSegmentPickerWindow so the user can choose which segment of a break-off chain to open.
    /// Returns the chosen segment, or null on Cancel (ExpenseFormPanel then keeps the current segment).
    /// </summary>
    /// <param name="segments">Every FinancialPattern in the chain (same Source) — always more than one; ExpenseFormPanel only calls this when there's a real choice.</param>
    private FinancialPattern? PickChainSegmentToEdit(IReadOnlyList<FinancialPattern> segments)
    {
        var picker = new ChainSegmentPickerWindow(segments) { Owner = this };
        return picker.ShowDialog() == true ? picker.SelectedSegment : null;
    }

    /// <summary>
    /// [CALC] Every scheduled expense reserves through its own Allocation Plan (the allocation model), proposed at creation from the current as-of date and the user's income.
    /// Income never gets one.
    /// The plan is persisted like a savings plan and appears in the earmark grid, where it can be edited or removed.
    /// A starting earmark, for a bill due before its first paycheck, is only suggested: it's returned unsaved for the Earmark form to offer, or null when none is needed.
    /// Transfer patterns are excluded from the income scan so a deposit isn't mistaken for a paycheck, and the scan is scoped to this expense's own account — a paycheck filed under a different account never actually funds this one.
    /// </summary>
    /// <param name="pattern">The newly-created expense to propose an allocation plan for.</param>
    /// <param name="accountId">Which account the expense is filed under — scopes the income scan.</param>
    private ManualEarmark? AutoCreateAllocationPlan(FinancialPattern pattern, int accountId)
    {
        if (ProposeAllocationPlanFor(pattern, accountId, CurrentAsOfDate()) is not { } proposal)
        {
            return null;
        }

        // The proposer may stretch the expense's active span back to the as-of date (its ActiveFrom) so its plan fits — persist that prepared expense, not the original, or the plan reads short against a goal whose own Start is later.
        _financialPatterns.Save(proposal.Outflow, accountId);
        _earMarkPatterns.Save(proposal.Plan);
        return proposal.StartingEarmark;
    }

    /// <summary>
    /// [CALC] Builds the affordability-capped default plan for an expense WITHOUT saving anything — shared by AutoCreateAllocationPlan (which persists it) and the Earmark form's own plan-less-goal draft (which loads its values for the user to edit).
    /// Null for income (no plan to propose).
    /// The front-load and ongoing rate are each capped at what the funds can spare (the affordability ceiling), measured on a re-forecast with this expense's own plans omitted so they aren't counted against themselves — the single-date ceiling for the front-load, the range ceiling for the per-cycle rate.
    /// </summary>
    /// <param name="pattern">The expense to propose a plan for.</param>
    /// <param name="accountId">Which account the expense is filed under.</param>
    /// <param name="asOf">The date the proposal is paced from — today for a live expense, or the goal's own future start so the plan fits its span without the proposer having to stretch it.</param>
    private ProposedAllocationPlan? ProposeAllocationPlanFor(FinancialPattern pattern, int accountId, DateOnly asOf)
    {
        if (pattern.Amount >= 0m)
        {
            return null;
        }

        var room = ForecastOmitting(new HashSet<int> { pattern.FinanceId });
        var roomPage = room.Accounts.FirstOrDefault(account => account.AccountId == accountId)?.Page ?? room.PrimaryAccountPage;
        var startingCeiling = AffordabilityCeiling.ForStartingEarmark(roomPage, pattern, asOf, ChangeKind.Implicit);
        var rateCeiling = AffordabilityCeiling.For(roomPage, pattern, asOf, ChangeKind.Implicit);
        return AllocationPlanProposer.Propose(
            pattern, _financialPatterns.GetByAccountExcludingTransferPatterns(accountId), asOf,
            startingEarmarkCeiling: startingCeiling,
            ongoingRateCeiling: rateCeiling);
    }

    private DateOnly CurrentAsOfDate() =>
        AsOfDatePicker.SelectedDate is { } asOf
            ? DateOnly.FromDateTime(asOf)
            : DateOnly.FromDateTime(DateTime.Today);

    /// <summary>
    /// [CALC] What ExpenseForm.RequestForecast calls (wired in the constructor): a feature that needs a forecast to work (FinancialPatternPickerWindow's own data source) should just compute one using whatever's on the As-Of/Horizon pickers right now, not tell the user to go press the Forecast button first.
    /// RefreshForecast always assigns _lastForecast when it returns (or a real computation error propagates, which is the honest outcome, not something to swallow) — so the null-forgiving return below is never actually lying.
    /// </summary>
    private ForecastResult EnsureForecast()
    {
        if (_lastForecast is { } existing)
        {
            return existing;
        }

        var horizonEnd = HorizonEndDatePicker.SelectedDate is { } h
            ? DateOnly.FromDateTime(h)
            : CurrentAsOfDate().AddMonths(3);
        RefreshForecast(CurrentAsOfDate(), horizonEnd);
        return _lastForecast!;
    }

    private void OnEditFinancialPatternClick(object sender, RoutedEventArgs e)
    {
        if (FinancialPatternsGrid.SelectedItem is not FinancialPatternRow row)
        {
            ShowNothingSelected();
            return;
        }

        // The form opens on whichever account the pattern is already filed under, so an unchanged pick preserves the filing and a changed one deliberately moves it.
        var currentAccountId = _financialPatterns.GetAccountId(row.FinanceId) ?? DefaultAccountId();
        RefreshExpenseFormContext();
        ExpenseForm.LoadPattern(row.Pattern, currentAccountId);
        SwitchToTab("Expense");
    }

    private void OnDeleteFinancialPatternClick(object sender, RoutedEventArgs e)
    {
        if (FinancialPatternsGrid.SelectedItem is not FinancialPatternRow row)
        {
            ShowNothingSelected();
            return;
        }

        var label = row.Description is { Length: > 0 } description ? description : row.Source;

        // A savings plan whose goal no longer exists is invalid by 3.10.a3, not merely untidy, so deleting a pattern removes its linked plan too — we still say what is about to happen rather than doing it silently.
        var hasSavingsPlan = _financialPatterns.HasLinkedEarMarkPattern(row.FinanceId);
        if (hasSavingsPlan)
        {
            // Nothing is actually moved by this — a jar was never a real transfer of money (FundJar's own class doc), so once its plan is gone the amount simply reads as free again on the next forecast.
            // Naming it here says what "continue" actually does, rather than leaving the user to notice their free funds went up.
            var freed = CurrentJarAmount(row.FinanceId);
            var freedNote = freed > 0m
                ? $" and free up {freed:C} currently set aside"
                : string.Empty;
            var answer = MessageBox.Show(
                this,
                $"Deleting \"{label}\" will also remove its savings plan{freedNote}.\n\nContinue?",
                "Delete this and its savings plan?",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK)
            {
                return;
            }
        }
        else if (!ConfirmDelete(label))
        {
            return;
        }

        if (hasSavingsPlan)
        {
            _earMarkPatterns.Delete(row.FinanceId);
        }

        _financialPatterns.Delete(row.FinanceId);
        RefreshGrids();
        RefreshExpenseFormContext();
        RefreshEarmarkFormContext();
    }

    /// <summary>
    /// [CALC] What this jar holds as of the forecast's own start date — the amount the delete warning says will be freed up.
    /// Zero when there is no forecast on screen yet, or the jar doesn't exist in it (an expense with no savings plan has no jar).
    /// </summary>
    /// <param name="financeId">Which jar to read.</param>
    private decimal CurrentJarAmount(int financeId)
    {
        if (_lastForecast is not { } forecast)
        {
            return 0m;
        }

        foreach (var account in forecast.Accounts)
        {
            var jar = account.Page.InitialSnapshot.FundJars
                .FirstOrDefault(candidate => candidate.FinanceId == financeId);
            if (jar is not null)
            {
                return jar.ExpectedAmount;
            }
        }

        return 0m;
    }

    private void OnAddEarMarkPatternClick(object sender, RoutedEventArgs e)
    {
        var goals = _financialPatterns.GetAll();
        if (goals.Count == 0)
        {
            MessageBox.Show(
                this,
                "Create a Bill/Paycheck pattern first — an earmark pattern needs an existing goal to save toward.",
                "No goals yet",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!ConfirmLeavingUnsavedChanges(EarmarkForm.IsDirty, "Allocations", "Earmark"))
        {
            return;
        }

        RefreshEarmarkFormContext();
        EarmarkForm.LoadForNewPattern();
        SwitchToTab("Earmark");
    }

    private void OnEditEarMarkPatternClick(object sender, RoutedEventArgs e)
    {
        if (EarMarkPatternsGrid.SelectedItem is AutomaticBillEarmarkRow)
        {
            ShowAutomaticRowIsNotManageable();
            return;
        }

        if (EarMarkPatternsGrid.SelectedItem is not EarMarkPatternRow row)
        {
            ShowNothingSelected();
            return;
        }

        var goal = _financialPatterns.GetByFinanceId(row.Pattern.FinanceId);
        if (goal is null)
        {
            MessageBox.Show(
                this,
                "The goal this was linked to no longer exists — this earmark pattern can only be deleted.",
                "Linked goal missing",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        RefreshEarmarkFormContext();
        EarmarkForm.LoadPattern(row.Pattern, goal);
        SwitchToTab("Earmark");
    }

    private void OnDeleteEarMarkPatternClick(object sender, RoutedEventArgs e)
    {
        if (EarMarkPatternsGrid.SelectedItem is AutomaticBillEarmarkRow)
        {
            ShowAutomaticRowIsNotManageable();
            return;
        }

        if (EarMarkPatternsGrid.SelectedItem is not EarMarkPatternRow row)
        {
            ShowNothingSelected();
            return;
        }

        if (!ConfirmDelete(row.Goal))
        {
            return;
        }

        _earMarkPatterns.Delete(row.Pattern.FinanceId);
        RefreshGrids();
    }

    private void OnCreateOneTimeGoalClick(object sender, RoutedEventArgs e)
    {
        var window = new CreateOneTimeGoalWindow(_financialPatterns.GetAll(), _accounts.GetAll()) { Owner = this };
        if (window.ShowDialog() == true && window.CreatedGoal is { } goal && window.CreatedEarMarkPattern is { } earmark)
        {
            // The goal (a finance pattern) is filed under the chosen account; its earmark reaches the same account through finance_id.
            _financialPatterns.Save(goal, window.SelectedAccountId);
            _earMarkPatterns.Save(earmark);
            RefreshGrids();
        }
    }

    private void ShowNothingSelected() =>
        MessageBox.Show(this, "Select a row first.", "Nothing selected", MessageBoxButton.OK, MessageBoxImage.Information);

    private void ShowAutomaticRowIsNotManageable() => MessageBox.Show(
        this,
        "This is an automatic funding for a mandatory bill, not a real earmark pattern — there's nothing to edit or delete here. " +
        "It disappears on its own if you delete the bill or give it a real earmark, on the Bills & Paychecks tab.",
        "Automatic — nothing to manage",
        MessageBoxButton.OK,
        MessageBoxImage.Information);

    private bool ConfirmDelete(string label) => MessageBox.Show(
        this,
        $"Delete \"{label}\"? This can't be undone.",
        "Confirm delete",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question) == MessageBoxResult.Yes;
}
