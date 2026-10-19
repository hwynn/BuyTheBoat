using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BuyTheBoat.Domain;
using BuyTheBoat.Persistence;

namespace BuyTheBoat.App;

// The app's one main window: the Forecast tab, a list tab for each kind of thing (Accounts, Transfers, Bills & Paychecks, Allocations),
// and a form tab for creating or editing each (Account, Transfer, Expense, Earmark).
// Ported so far: Accounts, Bills & Paychecks (all but its Spreadsheet subtab), and Allocations; the Forecast and Transfers tabs are placeholders.
public partial class MainWindow : Window
{
    private readonly FinancialPatternRepository _financialPatterns;
    private readonly EarMarkPatternRepository _earMarkPatterns;
    private readonly CurrentBalanceRepository _currentBalance;
    private readonly AccountRepository _accounts;
    private readonly TransferRepository _transfers;
    private readonly ManualEarmarkRepository _manualEarmarks;
    private readonly DraftRowRepository _draftRows;

    // The forecast the app is showing; Money flow averages over its window.
    private ForecastResult? _lastForecast;

    // The Forecast tab's As of / Show forecast through dates; held here until that tab and its date pickers are ported.
    private DateOnly _asOfDate = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly? _horizonEndDate;

    // List rows already watching their item for the open-in-form highlight (TrackOpenInFormRow); the grid reuses rows, so each is wired once.
    private readonly HashSet<DataGridRow> _trackedRows = [];

    public MainWindow()
    {
        InitializeComponent();
        FitToScreen();

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

        // The permanent Earmark tab persists through these callbacks instead of a dialog result — this panel owns no repository itself.
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

            // Same settled rule as the Savings-plan save path — one plain save button for the whole Earmark form, one destination, regardless of which mode was active.
            SwitchToTab("Forecast");
        };

        // Each panel does its own validation (AccountFormPanel's uniqueness check included); these callbacks save what came back and refresh.
        // Both recompute the shown forecast after saving, since an account's starting balance feeds it.
        AccountForm.AccountSaved = account =>
        {
            _accounts.Save(account);
            // The saved account changed the world; recompute so the forecast reflects it, not the pre-save snapshot.
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

        // Tab-header styling stays live, not just at save/load: each panel raises StateChanged on every field edit (via MarkDirty/ClearDirty), not only when its own Load*/Save runs, so the header updates while the user is still typing.
        AccountForm.StateChanged += (_, _) => UpdateTabHeaderStyle(AccountTabHeaderText, AccountsTabHeaderText, AccountForm.IsPopulated, AccountForm.IsDirty);
        ExpenseForm.StateChanged += (_, _) => UpdateTabHeaderStyle(ExpenseTabHeaderText, BillsAndPaychecksTabHeaderText, ExpenseForm.IsPopulated, ExpenseForm.IsDirty);
        EarmarkForm.StateChanged += (_, _) => UpdateTabHeaderStyle(EarmarkTabHeaderText, AllocationsTabHeaderText, EarmarkForm.IsPopulated, EarmarkForm.IsDirty);

        // Same live trigger for the list rows: loading a different item moves the highlight; an edit turns it orange.
        AccountForm.StateChanged += (_, _) => MarkRowsOpenInForms();
        ExpenseForm.StateChanged += (_, _) => MarkRowsOpenInForms();
        EarmarkForm.StateChanged += (_, _) => MarkRowsOpenInForms();

        // The balance boxes' keys and focus are caught on the grid, since the boxes are made by the cell template.
        // Keys are caught on the way down (Tunnel), so Enter and Tab reach the box's handler before the grid or focus navigation acts on them.
        AccountsGrid.AddHandler(KeyDownEvent, OnAccountBalanceKeyDown, RoutingStrategies.Tunnel);
        AccountsGrid.AddHandler(GotFocusEvent, OnAccountBalanceGotFocus);
        AccountsGrid.AddHandler(LostFocusEvent, OnAccountBalanceLostFocus);

        RefreshGrids();
        RefreshAccountsGrid();
        LoadSavedBalance();
    }

    /// <summary>[UI] Shrinks the window to fit a screen smaller than its usual size, so its title bar doesn't open above the top edge.</summary>
    private void FitToScreen()
    {
        if (Screens.Primary is not { } screen)
        {
            return;
        }

        // Height counts only the inside of the window, so the title bar's room comes off the screen's.
        const double titleBarRoom = 40;
        MaxWidth = screen.WorkingArea.Width / screen.Scaling;
        MaxHeight = screen.WorkingArea.Height / screen.Scaling - titleBarRoom;
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
    }

    /// <summary>[READS FILE] Gives the Earmark form the saved goals, plans, and manual adjustments it reads from, plus the forecast on screen.</summary>
    private void RefreshEarmarkFormContext() =>
        EarmarkForm.SetContext(_financialPatterns.GetAll(), _earMarkPatterns.GetAll(), _manualEarmarks.GetAll(), _financialPatterns.GetTransferFinanceIds(), _lastForecast);

    private void RefreshAccountFormContext() => AccountForm.SetContext(_accounts.GetAll());

    /// <summary>[READS FILE] Gives the Expense form the saved accounts, plans, and patterns it reads from.</summary>
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
        foreach (var item in MainTabControl.Items.OfType<TabItem>())
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

            foreach (var subItem in subTabs.Items.OfType<TabItem>())
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
    /// SetContext only ever replaces reference data, never a form's own in-progress fields, so this can't discard unsaved edits.
    /// </summary>
    /// <param name="e">
    /// SelectionChanged bubbles up from any selecting control inside a tab's own content (every ComboBox and DataGrid included).
    /// e.Source must be the main tab strip or a subtab strip, or picking an item in one of those would also re-fire this.
    /// </param>
    private void OnMainTabControlSelectionChanged(object? sender, SelectionChangedEventArgs e)
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
    /// ClearValue rather than a hardcoded "normal" color so the not-dirty state just inherits whatever the tab strip's own default look is.
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
        headerText.FontWeight = hasContent ? FontWeight.Bold : FontWeight.Normal;
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
        MarkRows<FinancialPatternRow>(FinancialPatternsGrid, ExpenseForm.IsDirty, row => row.Segments.Any(segment => segment.FinanceId == ExpenseForm.LoadedFinanceId));
        MarkRows<EarMarkPatternRow>(EarMarkPatternsGrid, EarmarkForm.IsDirty, row => row.Plans.Any(plan => plan.FinanceId == EarmarkForm.LoadedPlanFinanceId));
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

    /// <summary>[UI] Gives each list row on screen the open-in-form and unsaved-changes look of the item it shows.</summary>
    private void OnLoadingFormListRow(object? sender, DataGridRowEventArgs e) => TrackOpenInFormRow(e.Row);

    /// <summary>
    /// [UI] Keeps one list row's "openInForm" and "dirty" style classes matching its item's IsOpenInForm and IsDirty, as the item changes and as the grid reuses the row for another item.
    /// Avalonia has no DataTrigger, so the row styles in MainWindow.axaml key off these classes instead.
    /// </summary>
    private void TrackOpenInFormRow(DataGridRow row)
    {
        if (!_trackedRows.Add(row))
        {
            return;
        }

        FormListRow? watched = null;

        void Apply()
        {
            var item = row.DataContext as FormListRow;
            row.Classes.Set("openInForm", item?.IsOpenInForm == true);
            row.Classes.Set("dirty", item?.IsDirty == true);
        }

        void OnItemChanged(object? itemSender, PropertyChangedEventArgs args) => Apply();

        void Rewatch()
        {
            if (watched is not null)
            {
                watched.PropertyChanged -= OnItemChanged;
            }

            watched = row.DataContext as FormListRow;
            if (watched is not null)
            {
                watched.PropertyChanged += OnItemChanged;
            }

            Apply();
        }

        row.DataContextChanged += (_, _) => Rewatch();
        Rewatch();
    }

    /// <summary>
    /// [UI] Picking a row other than the one open in the form, while the form has unsaved changes, asks whether to load the picked row (discarding the changes) or go back to the changes.
    /// Without unsaved changes, picking a row only selects it.
    /// </summary>
    private void OnListRowSelected(object? sender, SelectionChangedEventArgs e)
    {
        // A rebuilt list clears its selection (nothing added), and putting the selection back on the open row lands here too — neither asks.
        // Clicking into an Accounts balance box selects its row too, but typing a balance loads nothing into the Form, so that doesn't ask either.
        if (sender is not DataGrid grid
            || FocusManager?.GetFocusedElement() is TextBox { DataContext: AccountRow }
            || e.Source != grid
            || e.AddedItems.Count != 1
            || e.AddedItems[0] is not FormListRow picked
            || picked.IsOpenInForm)
        {
            return;
        }

        var (formIsDirty, formName, formTab, loadPicked) = grid.Name switch
        {
            nameof(AccountsGrid) => (AccountForm.IsDirty, "Account", "Account", (Action)(() => EditSelectedAccount())),
            nameof(FinancialPatternsGrid) => (ExpenseForm.IsDirty, "Bills & Paychecks", "Expense", () => EditSelectedFinancialPattern()),
            nameof(EarMarkPatternsGrid) => (EarmarkForm.IsDirty, "Allocations", "Earmark", () => EditSelectedEarMarkPattern()),
            nameof(ManualEarmarksGrid) => (EarmarkForm.IsDirty, "Allocations", "Earmark", () => EditSelectedManualEarmark()),
            _ => (false, string.Empty, string.Empty, () => { }),
        };
        if (!formIsDirty)
        {
            return;
        }

        // Deferred until the click finishes, so the popup doesn't open while the grid is still handling the mouse press.
        Dispatcher.UIThread.Post(async () =>
        {
            if (grid.SelectedItem != picked)
            {
                return;
            }

            if (await ConfirmLeavingUnsavedChanges(formIsDirty, formName, formTab))
            {
                loadPicked();
                return;
            }

            // Kept the changes (or closed the popup): the selection goes back to the row open in the form, so a later "Edit Selected..." can't discard them by surprise.
            grid.SelectedItem = grid.ItemsSource?.OfType<FormListRow>().FirstOrDefault(row => row.IsOpenInForm);
        }, DispatcherPriority.Input);
    }

    /// <summary>[CALC] Names the item open in a Form subtab for the unsaved-changes popup: its list name in quotes, or "a new …" when it isn't saved yet.</summary>
    private string DescribeItemInForm(string formTab) => formTab switch
    {
        "Account" => OpenRowName(AccountsGrid) ?? "a new account",
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
    private async Task<bool> ConfirmLeavingUnsavedChanges(bool formIsDirty, string formName, string formTab)
    {
        if (!formIsDirty)
        {
            return true;
        }

        var popup = new UnsavedChangesWindow(formName, DescribeItemInForm(formTab));
        await popup.ShowDialog(this);
        if (popup.ContinueToSelection == false)
        {
            SwitchToTab(formTab);
        }

        return popup.ContinueToSelection == true;
    }

    /// <summary>
    /// [STEP] Picks up the forecast dates entered last time, since the Forecast tab's settings carry across launches.
    /// If there's something to show, it forecasts right away, as the Forecast tab does on launch.
    /// </summary>
    private void LoadSavedBalance()
    {
        var saved = _currentBalance.GetCurrent();
        if (saved is null)
        {
            _asOfDate = DateOnly.FromDateTime(DateTime.Today);
            _horizonEndDate = _asOfDate.AddMonths(3);
            return;
        }

        _asOfDate = saved.AsOfDate;
        _horizonEndDate = saved.HorizonEndDate;
        RefreshForecast(saved.AsOfDate, saved.HorizonEndDate);
    }

    /// <summary>[CALC] Fallback account id for the rare case a pattern's own stored account id can't be resolved — picking the first account beats erroring out.</summary>
    private int DefaultAccountId() => _accounts.GetAll().FirstOrDefault()?.Id ?? 1;

    /// <summary>[READS FILE] Fills the Accounts list from the saved accounts and re-marks the row open in the Form.</summary>
    private void RefreshAccountsGrid()
    {
        AccountsGrid.ItemsSource = _accounts.GetAll().Select(account => new AccountRow(account)).ToList();
        MarkRowsOpenInForms();
    }

    /// <summary>[UI] Fills the Accounts tab's Money flow subtab with each account's average monthly money in and out, from the current saved data.</summary>
    private void RefreshMoneyFlow()
    {
        // Same window as the shown forecast, but re-run so edits made since it was computed show up.
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
        MoneyFlowTotalText.Text = $"All accounts together: {householdNet:N2} per month "
            + (householdNet < 0m ? "more going out than coming in." : "left over after spending.");
    }

    /// <summary>[UI] Selects the whole balance on entering its box, so typing replaces it.</summary>
    private void OnAccountBalanceGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is TextBox { DataContext: AccountRow } box)
        {
            box.SelectAll();
        }
    }

    /// <summary>[WRITES FILE] Saves a balance typed into the Accounts list when the user clicks or tabs away from its box.</summary>
    private void OnAccountBalanceLostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is TextBox { DataContext: AccountRow } box)
        {
            CommitAccountBalance(box);
        }
    }

    /// <summary>[WRITES FILE] Enter/Tab in an Accounts balance box saves it and moves to the next account's box (Shift goes back); Esc puts the saved balance back.</summary>
    private void OnAccountBalanceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not TextBox { DataContext: AccountRow row } box)
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

        // Handled either way, so the DataGrid's own Enter/Tab cell-hopping and Tab's focus move don't also run.
        e.Handled = true;

        // An unreadable entry stays put, flagged, for the user to fix.
        if (!CommitAccountBalance(box))
        {
            return;
        }

        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
        var rows = AccountsGrid.ItemsSource?.OfType<AccountRow>().ToList() ?? [];
        var nextIndex = rows.IndexOf(row) + step;
        if (nextIndex < 0 || nextIndex >= rows.Count)
        {
            box.SelectAll();
            return;
        }

        var nextRow = rows[nextIndex];
        AccountsGrid.ScrollIntoView(nextRow, null);
        AccountsGrid.UpdateLayout();
        if (AccountsGrid.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(candidate => candidate.DataContext == nextRow) is { IsEnabled: true } nextBox)
        {
            nextBox.Focus();
        }
    }

    /// <summary>
    /// [WRITES FILE] Saves the balance typed into an Accounts list box, then refreshes the forecast.
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
            box.Classes.Set("invalid", true);
            ToolTip.SetTip(box, "Enter an amount like 1234.56 — or press Esc to put the saved balance back.");
            return false;
        }

        box.Classes.Set("invalid", false);
        box.ClearValue(ToolTip.TipProperty);

        if (balance == row.Account.Balance)
        {
            // Re-shows "500" as "500.00".
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

        RefreshShownForecast();
        return true;
    }

    /// <summary>[UI] Puts an Accounts balance box back to showing the saved balance, clearing any red "not a number" flag.</summary>
    private static void ResetAccountBalanceBox(TextBox box)
    {
        box.Classes.Set("invalid", false);
        box.ClearValue(ToolTip.TipProperty);

        // SetCurrentValue keeps the box's binding to the row, so a later save still updates it.
        if (box.DataContext is AccountRow row)
        {
            box.SetCurrentValue(TextBox.TextProperty, row.BalanceText);
        }
    }

    /// <summary>
    /// [STEP] Opens a blank Accounts › Form for a new account.
    /// Validation (including the name-uniqueness check) lives in AccountFormPanel itself — see its own class comment.
    /// </summary>
    private async void OnAddAccountClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmLeavingUnsavedChanges(AccountForm.IsDirty, "Account", "Account"))
        {
            return;
        }

        RefreshAccountFormContext();
        AccountForm.LoadForNew();
        SwitchToTab("Account");
    }

    /// <summary>[STEP] Opens the selected account in Accounts › Form.</summary>
    private void OnEditAccountClick(object? sender, RoutedEventArgs e) => EditSelectedAccount();

    /// <summary>[STEP] Loads the account selected in the list into Accounts › Form and shows it, or says nothing is selected.</summary>
    private void EditSelectedAccount()
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

    /// <summary>[DELETES] Deletes the selected account after asking; blocked while it's the only account, while anything is still filed under it, or while it's still part of a transfer — we never delete the user's bills out from under them.</summary>
    private async void OnDeleteAccountClick(object? sender, RoutedEventArgs e)
    {
        if (AccountsGrid.SelectedItem is not AccountRow row)
        {
            ShowNothingSelected();
            return;
        }

        if (_accounts.GetAll().Count <= 1)
        {
            await MessageBox.Show(this, "There has to be at least one account.", "Can't delete", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_transfers.IsAccountReferenced(row.Account.Id))
        {
            await MessageBox.Show(
                this,
                $"\"{row.Name}\" is still part of a transfer. Delete that transfer first (Transfers tab).",
                "Can't delete yet",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (_financialPatterns.HasPatternsInAccount(row.Account.Id))
        {
            await MessageBox.Show(
                this,
                $"\"{row.Name}\" still has bills, paychecks or goals filed under it. Move or delete those first.",
                "Can't delete yet",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var confirm = await MessageBox.Show(this, $"Delete the account \"{row.Name}\"?", "Delete account", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _accounts.Delete(row.Account.Id);
        RefreshAccountsGrid();
        RefreshAccountFormContext();
        RefreshExpenseFormContext();
    }

    /// <summary>[READS FILE] Fills the Bills &amp; Paychecks list from the saved patterns and re-marks the row open in the Form.</summary>
    private void RefreshGrids()
    {
        // financeId -> the name of the account it's filed under, so the grid can show where each bill/paycheck/goal lives — never a mystery.
        var accountNamesById = _accounts.GetAll().ToDictionary(account => account.Id, account => account.Name);
        var accountNameByFinanceId = _financialPatterns.GetAllByAccount()
            .SelectMany(entry => entry.Value.Select(pattern => (pattern.FinanceId, AccountId: entry.Key)))
            .ToDictionary(pair => pair.FinanceId, pair => accountNamesById.GetValueOrDefault(pair.AccountId, "(unknown)"));

        // A transfer's two patterns are hidden from this list — a transfer shows on its own tab as one thing, not as its two underlying patterns.
        // The engine still reads every pattern when forecasting.
        // A break-off chain shows as one row (its segments share a Source, as BreakOffFactory.FindCurrentSegment reads them); opening it asks which segment.
        FinancialPatternsGrid.ItemsSource = _financialPatterns.GetAllExcludingTransferPatterns()
            .GroupBy(pattern => pattern.Source)
            .Select(chain =>
            {
                var segments = chain.OrderBy(pattern => pattern.DatePattern.ActiveStart).ToList();
                var latest = segments[^1];
                return new FinancialPatternRow(latest, accountNameByFinanceId.GetValueOrDefault(latest.FinanceId, "(unknown)"), segments);
            })
            .ToList();

        // Every expense that reserves has its own Allocation Plan (an EarMarkPattern, per the allocation model), so the grid just shows those; nothing here is a synthesized "automatic" row.
        // A transfer's withdrawal reserves through a plan too, but it's hidden here for the same reason its patterns are: a transfer is shown as one thing on its own tab, not as its underlying reservation machinery.
        // One row per goal, across its break-off segments (shared Source) and any plans running at the same time; opening it asks which plan.
        // A plan whose goal is missing gets a row of its own.
        var financialPatterns = _financialPatterns.GetAll();
        var transferWithdrawalIds = _financialPatterns.GetTransferWithdrawalFinanceIds();
        var goalsByFinanceId = financialPatterns.ToDictionary(goal => goal.FinanceId);
        EarMarkPatternsGrid.ItemsSource = _earMarkPatterns.GetAll()
            .Where(pattern => !transferWithdrawalIds.Contains(pattern.FinanceId))
            .GroupBy(pattern => goalsByFinanceId.TryGetValue(pattern.FinanceId, out var goal) ? goal.Source : $"(finance id {pattern.FinanceId})")
            .Select(group =>
            {
                var plans = group.OrderBy(plan => plan.DatePattern.ActiveStart).ToList();
                var latest = plans[^1];
                return new EarMarkPatternRow(latest, goalsByFinanceId.GetValueOrDefault(latest.FinanceId), plans);
            })
            .ToList();

        ManualEarmarksGrid.ItemsSource = _manualEarmarks.GetAll()
            .Select(earmark => new ManualEarmarkRow(
                earmark,
                financialPatterns.FirstOrDefault(pattern => pattern.FinanceId == earmark.FinanceId) is { } owner
                    ? owner.Description ?? owner.Source
                    : $"(finance id {earmark.FinanceId})"))
            .ToList();

        MarkRowsOpenInForms();
    }

    /// <summary>
    /// [STEP] Opens a blank Bills &amp; Paychecks › Form for a new bill, paycheck or goal.
    /// Persistence + AutoCreateAllocationPlan happen in ExpenseForm.PatternSaved (wired in the constructor), matching Earmark's shape.
    /// </summary>
    private async void OnAddFinancialPatternClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmLeavingUnsavedChanges(ExpenseForm.IsDirty, "Bills & Paychecks", "Expense"))
        {
            return;
        }

        RefreshExpenseFormContext();
        ExpenseForm.LoadForNewPattern();
        SwitchToTab("Expense");
    }

    /// <summary>[STEP] Opens a blank Bills &amp; Paychecks › Form set up for a bill: an expense you have to pay.</summary>
    private async void OnCreateBillClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmLeavingUnsavedChanges(ExpenseForm.IsDirty, "Bills & Paychecks", "Expense"))
        {
            return;
        }

        RefreshExpenseFormContext();
        ExpenseForm.LoadForNewBill();
        SwitchToTab("Expense");
    }

    /// <summary>[STEP] Opens the selected bill or paycheck in Bills &amp; Paychecks › Form.</summary>
    private void OnEditFinancialPatternClick(object? sender, RoutedEventArgs e) => EditSelectedFinancialPattern();

    /// <summary>[STEP] Loads the bill or paycheck selected in the list into the Form and shows it, or says nothing is selected.</summary>
    private void EditSelectedFinancialPattern()
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

    /// <summary>[DELETES] Deletes the selected bill or paycheck (asking which segment for a chain), and its savings plan with it, after saying what will happen.</summary>
    private async void OnDeleteFinancialPatternClick(object? sender, RoutedEventArgs e)
    {
        if (FinancialPatternsGrid.SelectedItem is not FinancialPatternRow row)
        {
            ShowNothingSelected();
            return;
        }

        // A row standing for a break-off chain asks which segment to delete.
        var segment = row.Segments.Count > 1 ? PickChainSegmentToEdit(row.Segments) : row.Pattern;
        if (segment is null)
        {
            return;
        }

        var label = segment.Description is { Length: > 0 } description ? description : segment.Source;

        // A savings plan whose goal no longer exists is invalid by 3.10.a3, not merely untidy, so deleting a pattern removes its linked plan too — we still say what is about to happen rather than doing it silently.
        var hasSavingsPlan = _financialPatterns.HasLinkedEarMarkPattern(segment.FinanceId);
        if (hasSavingsPlan)
        {
            // Nothing is actually moved by this — a jar was never a real transfer of money (FundJar's own class doc), so once its plan is gone the amount simply reads as free again on the next forecast.
            // Naming it here says what "continue" actually does, rather than leaving the user to notice their free funds went up.
            var freed = CurrentJarAmount(segment.FinanceId);
            var freedNote = freed > 0m
                ? $" and free up {freed:N2} currently set aside"
                : string.Empty;
            var answer = await MessageBox.Show(
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
        else if (!await ConfirmDelete(label))
        {
            return;
        }

        if (hasSavingsPlan)
        {
            _earMarkPatterns.Delete(segment.FinanceId);
        }

        _financialPatterns.Delete(segment.FinanceId);
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
        }

        if (!jumpToEarmark)
        {
            // The NavigateToEarmarkForm callback above never fires for "Save and Skip planning" (Run() only invokes it when planning wasn't skipped) — same AutoCreateAllocationPlan-then-refresh sequence, just without any Earmark-tab navigation at the end.
            // Skipping planning leaves out the suggested starting earmark; the first bill shows as short until the user covers it.
            if (isNew)
            {
                AutoCreateAllocationPlan(pattern, accountId);
            }

            // Recompute the forecast before switching to it, so the Forecast tab reflects the just-saved edit rather than the pre-save snapshot.
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
    /// [STEP] Opens a blank Allocations › Form for a new savings plan; the user picks its goal there with Change....
    /// Says so instead when there's no bill, paycheck, or goal to save toward yet.
    /// </summary>
    private async void OnAddEarMarkPatternClick(object? sender, RoutedEventArgs e)
    {
        var goals = _financialPatterns.GetAll();
        if (goals.Count == 0)
        {
            await MessageBox.Show(
                this,
                "Create a Bill/Paycheck pattern first — an earmark pattern needs an existing goal to save toward.",
                "No goals yet",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!await ConfirmLeavingUnsavedChanges(EarmarkForm.IsDirty, "Allocations", "Earmark"))
        {
            return;
        }

        RefreshEarmarkFormContext();
        EarmarkForm.LoadForNewPattern();
        SwitchToTab("Earmark");
    }

    /// <summary>[STEP] Opens the selected savings plan in Allocations › Form.</summary>
    private void OnEditEarMarkPatternClick(object? sender, RoutedEventArgs e) => EditSelectedEarMarkPattern();

    /// <summary>[STEP] Loads the savings plan selected in the list (asking which one when the row stands for several) into the Form and shows it.</summary>
    private async void EditSelectedEarMarkPattern()
    {
        if (EarMarkPatternsGrid.SelectedItem is AutomaticBillEarmarkRow)
        {
            await ShowAutomaticRowIsNotManageable();
            return;
        }

        if (EarMarkPatternsGrid.SelectedItem is not EarMarkPatternRow row)
        {
            ShowNothingSelected();
            return;
        }

        if (await PickPlanFromRow(row) is not { } plan)
        {
            return;
        }

        var goal = _financialPatterns.GetByFinanceId(plan.FinanceId);
        if (goal is null)
        {
            await MessageBox.Show(
                this,
                "The goal this was linked to no longer exists — this earmark pattern can only be deleted.",
                "Linked goal missing",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        RefreshEarmarkFormContext();
        EarmarkForm.LoadPattern(plan, goal);
        SwitchToTab("Earmark");
    }

    /// <summary>[UI] The plan an Allocations row should act on: its only plan, or the one picked in EarmarkPatternPickerWindow when it stands for several; null on Cancel.</summary>
    private async Task<EarMarkPattern?> PickPlanFromRow(EarMarkPatternRow row)
    {
        if (row.Plans.Count == 1)
        {
            return row.Pattern;
        }

        var picker = new EarmarkPatternPickerWindow(row.Plans);
        return await picker.ShowDialog<bool>(this) ? picker.SelectedPlan : null;
    }

    /// <summary>[DELETES] Deletes the selected savings plan after asking; deleting a goal's last plan removes its manual adjustments too, since they'd have no jar left.</summary>
    private async void OnDeleteEarMarkPatternClick(object? sender, RoutedEventArgs e)
    {
        if (EarMarkPatternsGrid.SelectedItem is AutomaticBillEarmarkRow)
        {
            await ShowAutomaticRowIsNotManageable();
            return;
        }

        if (EarMarkPatternsGrid.SelectedItem is not EarMarkPatternRow row)
        {
            ShowNothingSelected();
            return;
        }

        if (await PickPlanFromRow(row) is not { } plan || !await ConfirmDelete(row.Goal))
        {
            return;
        }

        // Deletes just the picked plan; when it's the goal's last one, its manual earmarks go too, since they'd point at a jar that no longer exists.
        var otherPlans = _earMarkPatterns.GetAll()
            .Where(other => other.FinanceId == plan.FinanceId && other.DatePattern.DtStart != plan.DatePattern.DtStart)
            .ToList();
        if (otherPlans.Count == 0)
        {
            _earMarkPatterns.Delete(plan.FinanceId);
        }
        else
        {
            _earMarkPatterns.Delete(plan.FinanceId, plan.DatePattern.DtStart);
            var repositories = new FinancePatternRepositories
            {
                FinancialPatterns = _financialPatterns,
                EarMarkPatterns = _earMarkPatterns,
                ManualEarmarks = _manualEarmarks,
            };
            foreach (var date in repositories.FindOrphanedManualEarmarkDates(otherPlans, plan.FinanceId))
            {
                _manualEarmarks.Delete(plan.FinanceId, date);
            }
        }

        RefreshGrids();
    }

    /// <summary>[WRITES FILE] Opens the one-time goal popup and saves the goal and its savings plan it builds.</summary>
    private async void OnCreateOneTimeGoalClick(object? sender, RoutedEventArgs e)
    {
        var window = new CreateOneTimeGoalWindow(_financialPatterns.GetAll(), _accounts.GetAll());
        if (await window.ShowDialog<bool>(this) && window.CreatedGoal is { } goal && window.CreatedEarMarkPattern is { } earmark)
        {
            // The goal (a finance pattern) is filed under the chosen account; its earmark reaches the same account through finance_id.
            _financialPatterns.Save(goal, window.SelectedAccountId);
            _earMarkPatterns.Save(earmark);
            RefreshGrids();
        }
    }

    /// <summary>[STEP] Opens a blank one-off adjustment in Allocations › Form.</summary>
    private async void OnAddManualEarmarkClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmLeavingUnsavedChanges(EarmarkForm.IsDirty, "Allocations", "Earmark"))
        {
            return;
        }

        await ShowManualEarmarkForm(initialDate: null, editTarget: null);
    }

    /// <summary>[STEP] Opens the selected manual adjustment in Allocations › Form.</summary>
    private void OnEditManualEarmarkClick(object? sender, RoutedEventArgs e) => EditSelectedManualEarmark();

    /// <summary>[STEP] Loads the manual adjustment selected in the list into the Form and shows it, or says nothing is selected.</summary>
    private async void EditSelectedManualEarmark()
    {
        if (ManualEarmarksGrid.SelectedItem is not ManualEarmarkRow row)
        {
            ShowNothingSelected();
            return;
        }

        await ShowManualEarmarkForm(initialDate: null, editTarget: row.Earmark);
    }

    /// <summary>[DELETES] Deletes the selected manual adjustment after asking.</summary>
    private async void OnDeleteManualEarmarkClick(object? sender, RoutedEventArgs e)
    {
        if (ManualEarmarksGrid.SelectedItem is not ManualEarmarkRow row)
        {
            ShowNothingSelected();
            return;
        }

        if (!await ConfirmDelete($"the {row.Target} adjustment on {row.Date}"))
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
    private async Task ShowManualEarmarkForm(DateOnly? initialDate, ManualEarmark? editTarget)
    {
        if (_earMarkPatterns.GetAll().Count == 0)
        {
            await MessageBox.Show(
                this,
                "Create a savings plan first — manual adjustments live inside a fund's plan (its timeline is the fund's lifetime).",
                "No funds yet", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        RefreshEarmarkFormContext();
        EarmarkForm.LoadOneOff(editTarget, initialDate);
        SwitchToTab("Earmark");
    }

    /// <summary>[UI] Explains that an automatic bill funding row has nothing to edit or delete.</summary>
    private Task<MessageBoxResult> ShowAutomaticRowIsNotManageable() => MessageBox.Show(
        this,
        "This is an automatic funding for a mandatory bill, not a real earmark pattern — there's nothing to edit or delete here. " +
        "It disappears on its own if you delete the bill or give it a real earmark, on the Bills & Paychecks tab.",
        "Automatic — nothing to manage",
        MessageBoxButton.OK,
        MessageBoxImage.Information);

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
            var confirmWindow = new EditingHistoryConfirmationWindow(pageRequest, pageNumber, pages.Count);
            if (!confirmWindow.ShowDialogAndWait<bool>(this))
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
    private double MaxComfortableConfirmationHeight() =>
        Math.Max(300, (Screens.Primary is { } screen ? screen.WorkingArea.Height / screen.Scaling : 768) - 260);

    /// <summary>
    /// [UI] AskWhichEarmarkPatternToOpen's own real disambiguation — shows EarmarkPatternPickerWindow and returns whichever plan the user picked.
    /// Cancelling the picker returns null, which AskWhichEarmarkPatternToOpen reads as "don't navigate" — the save has already committed, so the user simply stays on their current tab rather than being dropped onto a plan they didn't choose.
    /// </summary>
    /// <param name="plans">Every EarMarkPattern surviving for the goal — always more than one; AskWhichEarmarkPatternToOpen's own gate never calls this otherwise.</param>
    private EarMarkPattern? PickEarmarkPatternToOpen(IReadOnlyList<EarMarkPattern> plans)
    {
        var picker = new EarmarkPatternPickerWindow(plans);
        return picker.ShowDialogAndWait<bool>(this) ? picker.SelectedPlan : null;
    }

    /// <summary>
    /// [UI] What ExpenseForm.PickChainSegment calls (wired in the constructor) — shows ChainSegmentPickerWindow so the user can choose which segment of a break-off chain to open.
    /// Returns the chosen segment, or null on Cancel (ExpenseFormPanel then keeps the current segment).
    /// </summary>
    /// <param name="segments">Every FinancialPattern in the chain (same Source) — always more than one; ExpenseFormPanel only calls this when there's a real choice.</param>
    private FinancialPattern? PickChainSegmentToEdit(IReadOnlyList<FinancialPattern> segments)
    {
        var picker = new ChainSegmentPickerWindow(segments);
        return picker.ShowDialogAndWait<bool>(this) ? picker.SelectedSegment : null;
    }

    /// <summary>
    /// [WRITES FILE] Every scheduled expense reserves through its own Allocation Plan (the allocation model), proposed at creation from the current as-of date and the user's income.
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

    /// <summary>[UI] Asks "Delete X? This can't be undone."; true for Yes.</summary>
    private async Task<bool> ConfirmDelete(string label) => await MessageBox.Show(
        this,
        $"Delete \"{label}\"? This can't be undone.",
        "Confirm delete",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <summary>
    /// [CALC] No upper bound on the horizon by design — years out is a legitimate request (long-term goals, mortgage-length planning), so this is left to whatever the user picks rather than an app-imposed ceiling.
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

    /// <summary>[WRITES FILE] Extends ongoing bills and transfers to the horizon, then computes the forecast for the window and keeps it as the one being shown.</summary>
    private void RefreshForecast(DateOnly asOfDate, DateOnly horizonEndDate)
    {
        // "It just keeps going": ongoing bills/paychecks and transfers are stored with a real, bounded end date, so before showing a forecast we extend them forward to reach the horizon.
        // Bills renew as fresh chain segments; transfers just extend their end date in place (they have no distinctive chain identity — see ExtendOngoingTransfersToHorizon).
        RenewOngoingPatternsToHorizon(horizonEndDate);
        ExtendOngoingTransfersToHorizon(horizonEndDate);

        _lastForecast = TransactionLogBookFactory.CreateForecast(BuildForecastOptions(asOfDate, horizonEndDate));
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

    /// <summary>[CALC] Builds the ForecastOptions for the current data over the given window — the shared input every forecast runs through, so they can't drift apart.</summary>
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

    /// <summary>[WRITES FILE] Re-runs the forecast over the window it's already showing — for when data that feeds it changed rather than the window.</summary>
    private void RefreshShownForecast()
    {
        if (_lastForecast is { } shown)
        {
            RefreshForecast(shown.AsOfDate, shown.HorizonEndDate);
        }
    }

    /// <summary>[CALC] The forecast's As of date.</summary>
    private DateOnly CurrentAsOfDate() => _asOfDate;

    /// <summary>
    /// [WRITES FILE] The forecast being shown, computing one over the current As of / horizon dates when there isn't one yet, rather than telling the user to go press Forecast first.
    /// RefreshForecast always assigns _lastForecast when it returns (or a real computation error propagates), so the null-forgiving return below is never actually lying.
    /// </summary>
    private ForecastResult EnsureForecast()
    {
        if (_lastForecast is { } existing)
        {
            return existing;
        }

        RefreshForecast(CurrentAsOfDate(), _horizonEndDate ?? CurrentAsOfDate().AddMonths(3));
        return _lastForecast!;
    }

    /// <summary>[UI] Tells the user to pick a row before using an Edit or Delete button.</summary>
    private void ShowNothingSelected() =>
        _ = MessageBox.Show(this, "Select a row first.", "Nothing selected", MessageBoxButton.OK, MessageBoxImage.Information);
}
