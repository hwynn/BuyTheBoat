using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MyMoneyForecast.Domain;
using MyMoneyForecast.Persistence;

namespace MyMoneyForecast.App;

// The real startup window — a list of every defined pattern, since this is
// what the finished application will actually open to first. Pattern
// creation itself happens in separate popups so this stays a pure
// "here's what exists, edit/delete it here" view.
public partial class MainWindow : Window
{
    private readonly FinancialPatternRepository _financialPatterns;
    private readonly EarMarkPatternRepository _earMarkPatterns;
    private readonly CurrentBalanceRepository _currentBalance;
    private readonly AccountRepository _accounts;
    private readonly TransferRepository _transfers;
    private readonly ManualEarmarkRepository _manualEarmarks;

    // Backs the spreadsheet export button and the selected-day detail pane —
    // both only make sense once a forecast has actually been computed.
    private ForecastResult? _lastForecast;

    // The non-date inputs the shown forecast was computed from (dates live on
    // _lastForecast itself) — the Forecast button's pending-state compares the
    // fields against these (philosophy §1).
    private decimal? _shownBalance;
    private decimal? _shownCushion;

    // Calendar selection is manual (day cells are Buttons): exactly one cell
    // is selected at a time, tracked here and flagged on the row itself so the
    // cell template's trigger re-renders it — recycling-safe, cross-month.
    private DayCellRow? _selectedDayCell;
    private Dictionary<DateOnly, DayCellRow> _dayCellsByDate = [];

    // Which account the overview is scoped to; null = the household roll-up
    // (planning/11 §C.3). Filtering re-renders the calendar only — the forecast
    // itself is unchanged, so there is nothing to recompute.
    private int? _accountFilter;
    private bool _updatingAccountFilter;

    public MainWindow()
    {
        InitializeComponent();

        // TEMPORARY DIAGNOSTIC (2026-08-12) — remove once the empty-grids-on-
        // launch report is resolved. File-based (not MessageBox) so this can
        // be launched and inspected non-interactively. Logs at every step
        // between opening the database and RefreshGrids populating the
        // grids, to find exactly where a fresh read stops matching what's on
        // disk.
        Log("=== MainWindow constructor start ===");
        Log($"PID: {Environment.ProcessId}, UserName: {Environment.UserName}");
        var diagnosticPath = PatternDatabase.DefaultDatabasePath();
        Log($"Resolved path: {diagnosticPath}, Exists: {File.Exists(diagnosticPath)}, Size: {(File.Exists(diagnosticPath) ? new FileInfo(diagnosticPath).Length : -1)}");

        // In the portable demo, make the labeled data\/logs\/backups\/exports\
        // folders exist from first launch so the tester can see where things
        // live; a no-op on a normal LocalAppData run.
        AppPaths.EnsurePortableFoldersExist();

        var database = new PatternDatabase();
        _financialPatterns = new FinancialPatternRepository(database);
        _earMarkPatterns = new EarMarkPatternRepository(database, _financialPatterns);
        _currentBalance = new CurrentBalanceRepository(database);
        _accounts = new AccountRepository(database);
        _transfers = new TransferRepository(database, _financialPatterns);
        _manualEarmarks = new ManualEarmarkRepository(database, _earMarkPatterns);

        Log($"Immediately after construction — FinancialPatterns.GetAll().Count: {_financialPatterns.GetAll().Count}, EarMarkPatterns.GetAll().Count: {_earMarkPatterns.GetAll().Count}, Accounts.GetAll().Count: {_accounts.GetAll().Count}");

        // Item 6 migration: there is always at least one account. On the first
        // run after accounts landed, the old single balance/cushion becomes the
        // "primary" account, so nothing the user already entered is lost.
        var legacy = _currentBalance.GetCurrent();
        _accounts.EnsureDefaultAccount(legacy?.Balance ?? 0m, legacy?.IdealSafetyCushion ?? 0m);

        Log($"After EnsureDefaultAccount — FinancialPatterns.GetAll().Count: {_financialPatterns.GetAll().Count}, Accounts.GetAll().Count: {_accounts.GetAll().Count}");

        // planning/21 Philosophy 5/7: the permanent Earmark tab persists
        // through these callbacks instead of a ShowDialog() == true check —
        // this panel owns no repository itself.
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

            // Same settled rule as the Savings-plan save path above — one
            // plain save button for the whole Earmark form, one destination,
            // regardless of which mode was active.
            SwitchToTab("Forecast");
        };

        // planning/21 Philosophy 5/7: same standing-tab treatment as Earmark
        // above. Each panel does its own validation (AccountFormPanel's
        // uniqueness check included) — these callbacks are purely "persist
        // what came back, then refresh." Both now recompute the shown forecast
        // after saving (an account's own starting balance feeds it), matching
        // Earmark's save — closing the planning/24 gap that used to sit here.
        AccountForm.AccountSaved = account =>
        {
            _accounts.Save(account);
            // The saved account changed the world; recompute so the Forecast tab
            // we're switching to reflects it, not the pre-save snapshot.
            if (_lastForecast is { } shown)
            {
                RefreshForecast(shown.AsOfDate, shown.HorizonEndDate);
            }

            RefreshAccountsGrid();
            RefreshExpenseFormContext();
            SwitchToTab("Forecast");
        };
        ExpenseForm.RequestForecast = EnsureForecast;
        ExpenseForm.PatternSaved = OnExpensePatternSaved;
        ExpenseForm.PickChainSegment = PickChainSegmentToEdit;

        EarmarkForm.RequestForecast = EnsureForecast;
        EarmarkForm.RequestForecastWithOneOff = ForecastWithOneOff;
        EarmarkForm.RequestForecastWithProposedPlan = ForecastWithProposedPlan;

        // planning/21 Philosophy 5/7: the permanent Transfer tab, replacing the
        // old CreateTransferWindow popup. Create-only for now (see
        // TransferFormPanel) — the panel collects from/to/amount/schedule, this
        // callback does the id assignment, paired-pattern expansion, and save.
        TransferForm.TransferSaved = OnTransferSaved;
        TransferForm.RequestForecast = EnsureForecast;

        // Tab-header styling stays live, not just at save/load: each panel
        // raises StateChanged on every field edit (via MarkDirty/ClearDirty),
        // not only when its own Load*/Save runs, so the header updates while
        // the user is still typing.
        AccountForm.StateChanged += (_, _) => UpdateTabHeaderStyle(AccountTabHeaderText, AccountForm.IsPopulated, AccountForm.IsDirty);
        ExpenseForm.StateChanged += (_, _) => UpdateTabHeaderStyle(ExpenseTabHeaderText, ExpenseForm.IsPopulated, ExpenseForm.IsDirty);
        EarmarkForm.StateChanged += (_, _) => UpdateTabHeaderStyle(EarmarkTabHeaderText, EarmarkForm.IsPopulated, EarmarkForm.IsDirty);
        TransferForm.StateChanged += (_, _) => UpdateTabHeaderStyle(TransferTabHeaderText, TransferForm.IsPopulated, TransferForm.IsDirty);

        RefreshGrids();
        RefreshAccountsGrid();
        LoadSavedBalance();
        Log("=== MainWindow constructor end ===");
    }

    // TEMPORARY DIAGNOSTIC (2026-08-12) — remove once the empty-grids-on-
    // launch report is resolved.
    private static readonly string DiagnosticLogPath = Path.Combine(Path.GetTempPath(), "mmf-diagnostic.log");

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(DiagnosticLogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch
        {
            // Diagnostic logging itself must never be why the app fails.
        }
    }

    /// <summary>[UI] Snapshots the panel needs before every Load* call — repeated rather than held live, matching how CreateEarMarkPatternWindow/ManualEarmarkWindow always took a fresh snapshot at construction too.</summary>
    private void RefreshEarmarkFormContext() =>
        EarmarkForm.SetContext(_financialPatterns.GetAll(), _earMarkPatterns.GetAll(), _manualEarmarks.GetAll(), _financialPatterns.GetTransferFinanceIds(), _lastForecast);

    private void RefreshAccountFormContext() => AccountForm.SetContext(_accounts.GetAll());

    private void RefreshTransferFormContext() => TransferForm.SetContext(_accounts.GetAll());

    private void RefreshExpenseFormContext()
    {
        // Same accountId-by-financeId shape RefreshGrids() already builds for
        // the grid's own account-name column — reused here so the instance
        // picker knows which account each existing Expense is filed under.
        var accountIdByFinanceId = _financialPatterns.GetAllByAccount()
            .SelectMany(entry => entry.Value.Select(pattern => (pattern.FinanceId, AccountId: entry.Key)))
            .ToDictionary(pair => pair.FinanceId, pair => pair.AccountId);
        ExpenseForm.SetContext(accountIdByFinanceId, _accounts.GetAll(), _financialPatterns.GetTransferFinanceIds(), _earMarkPatterns.GetAll(), _financialPatterns.GetAll());
    }

    /// <summary>[STEP] Matches either a plain string Header (Forecast) or Tag (Account/Expense/Earmark, whose Header is a styled TextBlock — see MainWindow.xaml's own comment on why Tag carries the stable name).</summary>
    /// <param name="header">The tab's Header text or Tag to switch to.</param>
    private void SwitchToTab(string header)
    {
        foreach (System.Windows.Controls.TabItem item in MainTabControl.Items)
        {
            if (item.Header as string == header || item.Tag as string == header)
            {
                MainTabControl.SelectedItem = item;
                return;
            }
        }
    }

    /// <summary>[UI] Refreshes each permanent tab's background reference data on every selection, not only when reached through a save/plan button — SetContext only ever replaces reference data (available goals, their plans), never a form's own in-progress fields, so this can't discard unsaved edits.</summary>
    /// <param name="e">SelectionChanged bubbles up from any Selector inside a tab's own content (every ComboBox in Expense/Earmark's forms included) — e.Source must be checked, or picking an item in one of those would also re-fire this.</param>
    private void OnMainTabControlSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.Source != MainTabControl || MainTabControl.SelectedItem is not System.Windows.Controls.TabItem selected)
        {
            return;
        }

        switch (selected.Tag as string)
        {
            case "Account":
                RefreshAccountFormContext();
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

    /// <summary>[UI] Bold when the form has an existing instance loaded (IsPopulated), plus an accent color on top when it also has unsaved changes (IsDirty). ClearValue rather than a hardcoded "normal" color/weight so the not-dirty/not-populated state just inherits whatever the tab strip's own default look is.</summary>
    /// <param name="headerText">The tab header's TextBlock to restyle.</param>
    /// <param name="hasContent">Whether the form has an existing instance loaded.</param>
    /// <param name="isDirty">Whether the form has unsaved changes.</param>
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

    /// <summary>[STEP] Pre-fills from whatever was entered last time (Forecast tab's own state is meant to persist across launches, unlike a one-off what-if input) — and, if there's something to show, forecasts immediately so the tab isn't blank on a normal relaunch.</summary>
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

        // Balance and cushion come from the accounts themselves now (the engine
        // reads them per account), so nothing else to pass here.
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
        if (!EnsureTwoAccountsForTransfer())
        {
            return;
        }

        RefreshTransferFormContext();
        TransferForm.LoadForNew();
        SwitchToTab("Transfer");
    }

    /// <summary>[STEP] The selected day's lever for a short account (planning/11 §B): opens the Transfer form tab already pointed at that account for what it is short. The user still confirms — we surface the problem and make the fix easy, we don't move their money for them (philosophy 1).</summary>
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

    /// <summary>[WRITES FILE] Persists a transfer the Transfer form tab handed back: turns from/to/amount/schedule into a Transfer plus its paired patterns, reserves for the withdrawal, saves, and returns the user to the Forecast tab. The panel already validated the inputs and cleared itself.</summary>
    /// <param name="inputs">The from/to/amount/schedule the Transfer form collected.</param>
    private void OnTransferSaved(TransferFormInputs inputs)
    {
        var namesById = _accounts.GetAll().ToDictionary(account => account.Id, account => account.Name);

        // Two fresh finance ids for the patterns (they are real patterns, so they
        // must not collide with any existing pattern's id — patterns included).
        var maxFinanceId = _financialPatterns.GetAll().Select(pattern => pattern.FinanceId).DefaultIfEmpty(0).Max();

        var result = TransferFactory.Create(new TransferRequest
        {
            TransferId = _transfers.NextId(),
            WithdrawalFinanceId = maxFinanceId + 1,
            DepositFinanceId = maxFinanceId + 2,
            FromAccountId = inputs.FromAccountId,
            ToAccountId = inputs.ToAccountId,
            FromAccountName = namesById[inputs.FromAccountId],
            ToAccountName = namesById[inputs.ToAccountId],
            Amount = inputs.Amount,
            DatePattern = inputs.DatePattern,
            AutoRenew = inputs.AutoRenew,
        });

        // The transfer reserves in the account it leaves, through a
        // front-loaded Allocation Plan on the withdrawal (Stage 1's
        // allocation model — planning/14) — no income pacing (a transfer
        // isn't a recurring bill), so the no-income shape reserves the full
        // amount from the as-of date. spreadEvenlyWithNoIncome: false — a
        // transfer stays plain and immediate, not spread like a one-time
        // goal (planning/13, C1); a deliberate ruling against adaptive
        // behavior for transfers. Propose first: the proposer may stretch
        // the withdrawal's active span back to the as-of date (planning/15,
        // ActiveFrom) so its plan fits, and that prepared withdrawal is what
        // must be persisted (via the transfer) for the plan to resolve.
        var withdrawalPlan = AllocationPlanProposer.Propose(result.Withdrawal, [], CurrentAsOfDate(), spreadEvenlyWithNoIncome: false);
        _transfers.Save(result with { Withdrawal = withdrawalPlan.Outflow });
        _earMarkPatterns.Save(withdrawalPlan.Plan);

        RefreshGrids();

        // The transfer's patterns change the cascade, so re-run the forecast, then
        // land on the Forecast tab (matching the other form saves) — that view is
        // what actually shows the shortfall the lever was offered for now cleared.
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
        var accounts = _accounts.GetAll();
        AccountsGrid.ItemsSource = accounts.Select(account => new AccountRow(account)).ToList();

        var total = accounts.Sum(account => account.Balance);
        var cushion = accounts.Sum(account => account.IdealSafetyCushion);
        AccountsSummaryText.Text = accounts.Count == 0
            ? "—"
            : $"{total:C} in {accounts.Count} account{(accounts.Count == 1 ? string.Empty : "s")}"
                + (cushion > 0m ? $" · {cushion:C} cushion" : string.Empty);

        UpdateForecastButtonState();
    }

    /// <summary>[STEP] planning/21 Philosophy 5/7: populates and switches to the permanent Account tab instead of opening AccountWindow. Validation (including the name-uniqueness check) lives in AccountFormPanel itself — see its own class comment.</summary>
    private void OnAddAccountClick(object sender, RoutedEventArgs e)
    {
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

    /// <summary>[STEP] Blocked while it's the only account, while anything is still filed under it, or while it's still part of a transfer — we never delete the user's bills out from under them (philosophy 1).</summary>
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

        // Balance and cushion are per account now, validated as each account is
        // saved — so there is nothing to parse here; AccountTotals() is just the
        // household sum the legacy CurrentBalance row below still wants.
        var (balance, idealSafetyCushion) = AccountTotals();

        // CurrentBalance is superseded by the per-account model but still holds
        // the global as-of/horizon dates — kept in step here so the row stays
        // coherent until that settings data moves onto TransactionLogBook itself.
        _currentBalance.Save(balance, asOfDate, horizonEndDate, idealSafetyCushion);
        RefreshForecast(asOfDate, horizonEndDate);
    }

    /// <summary>[CALC] No upper bound on the horizon by design — years out is a legitimate request (long-term goals, mortgage-length planning), so this is left to whatever the user picks rather than an app-imposed ceiling. The calendar stays cheap at that scale because the outer ListBox virtualizes months. One AccountForecastInput per account: its own balance/cushion, and the patterns/earmarks/manuals filed under it (an earmark or manual reaches its account through its finance id). A transfer's two patterns are patterns filed under an account too, so they ride along and feed its cascade.</summary>
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
        // "It just keeps going" (planning/15): ongoing bills/paychecks and transfers
        // are stored with a real, bounded end date, so before showing a forecast we
        // extend them forward to reach the horizon. Bills renew as fresh chain
        // segments; transfers just extend their end date in place (they have no
        // distinctive chain identity — see ExtendOngoingTransfersToHorizon). Only the
        // shown forecast does this — the what-if forecasts (ForecastOmitting/
        // WithOneOff/WithProposedPlan) build straight from BuildForecastOptions and
        // stay read-only.
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

    /// <summary>[WRITES FILE] Extends every ongoing (AutoRenew) bill/paycheck forward with fresh renewal segments until its chain reaches the horizon, so "it just keeps going" actually keeps going however far you forecast (planning/15). A no-op — and no writes — when every ongoing pattern already reaches the horizon, so re-forecasting the same window is idempotent.</summary>
    /// <param name="horizon">The forecast horizon the ongoing patterns must reach.</param>
    private void RenewOngoingPatternsToHorizon(DateOnly horizon)
    {
        // Each renewal pushes a chain a cycle-plus past the horizon, so one pass
        // usually settles it; the guarded loop only re-runs if a segment length
        // somehow lands short, and stops rather than spinning.
        for (var guard = 0; guard < 200; guard++)
        {
            var all = _financialPatterns.GetAll();
            // Transfer legs can be AutoRenew too, but they renew as a linked pair
            // through ExtendOngoingTransfersToHorizon — never here, where the
            // single-pattern chain logic would break the pairing.
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

    /// <summary>[WRITES FILE] Appends one renewal segment to an ongoing pattern's chain — a fresh, identical successor (new FinanceId, "(renewed …)" label) reaching a cycle past the horizon, CONTINUING each existing earmark pattern at the same amount and cadence rather than proposing a new one. A self-funding bill (no explicit earmark) gains none — its automatic reservation just carries on. A single bad pattern is logged and skipped rather than breaking the whole forecast.</summary>
    /// <param name="segment">The ongoing pattern's current segment, due to reach further out.</param>
    /// <param name="horizon">The forecast horizon the successor must clear.</param>
    private void RenewOngoingSegment(FinancialPattern segment, DateOnly horizon)
    {
        try
        {
            // Fetched live (not from the caller's snapshot) so ids stay current
            // across a multi-pattern batch.
            var all = _financialPatterns.GetAll();
            var accountId = _financialPatterns.GetAccountId(segment.FinanceId) ?? DefaultAccountId();
            var renewalDate = segment.DatePattern.Until.AddDays(1);

            // Whole years reaching a cycle-plus past the horizon, so one renewal
            // covers the current view and won't re-fire until the horizon moves out.
            var segmentYears = Math.Max(1, (horizon.DayNumber - renewalDate.DayNumber) / 365 + 2);
            var successorFinanceId = all.Select(pattern => pattern.FinanceId).DefaultIfEmpty(0).Max() + 1;

            // A renewal changes nothing, so the successor is built directly and the
            // pattern simply continues — no break-off-style fresh plan proposal (which
            // would also invent an earmark for a bill that funds itself automatically).
            // The predecessor already ends the day before renewalDate, so it needs no
            // change. Mirrors BreakOffFactory.Renew's successor + "(renewed …)" label,
            // minus the proposal.
            var successor = FinancialPattern.Create(new FinancialPatternOptions
            {
                FinanceId = successorFinanceId,
                Source = segment.Source,
                Description = $"{StripRenewalMarker(segment.Description ?? segment.Source)} (renewed {renewalDate:yyyy-MM-dd})",
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = segment.DatePattern.Frequency,
                    Interval = segment.DatePattern.Interval,
                    ByDay = segment.DatePattern.ByDay,
                    ByMonthDay = segment.DatePattern.ByMonthDay,
                    DtStart = renewalDate,
                    Until = renewalDate.AddYears(segmentYears),
                }),
                Amount = segment.Amount,
                Priority = segment.Priority,
                Mandatory = segment.Mandatory,
                AutoRenew = segment.AutoRenew,
            });
            _financialPatterns.Save(successor, accountId);

            // Continue each explicit earmark pattern unchanged: same amount and cadence,
            // its span re-anchored onto the new segment (ReanchoredToStartOn keeps the
            // contribution days; WithUntil reaches the successor's border). No rows here
            // means a self-funding bill — nothing to carry, the auto-reservation covers it.
            foreach (var existing in _earMarkPatterns.GetAll().Where(plan => plan.FinanceId == segment.FinanceId))
            {
                var continued = EarMarkPattern.Create(
                    new EarMarkPatternOptions
                    {
                        FinanceId = successor.FinanceId,
                        DatePattern = existing.DatePattern.ReanchoredToStartOn(renewalDate).WithUntil(successor.DatePattern.Until),
                        Amount = existing.Amount,
                        StartingAllocation = 0m,
                    },
                    successor);
                _earMarkPatterns.Save(continued);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // A single malformed ongoing pattern must not break the whole forecast.
            Log($"Skipped renewing ongoing pattern {segment.FinanceId} ({segment.Source}): {ex.Message}");
        }
    }

    /// <summary>[CALC] Strips a "(renewed yyyy-MM-dd)" suffix from a label so a fresh one can replace it — mirrors BreakOffFactory's own private marker convention, so a pattern renewed year after year reads "(renewed 2031-…)", not a pile-up.</summary>
    /// <param name="label">The label to strip a prior renewal marker from.</param>
    private static string StripRenewalMarker(string label)
    {
        var markerIndex = label.IndexOf(" (renewed ", StringComparison.Ordinal);
        return markerIndex < 0 ? label : label[..markerIndex];
    }

    /// <summary>[WRITES FILE] Extends every ongoing (AutoRenew) transfer's end date out past the horizon so it keeps going however far you forecast. Transfers use extend-in-place rather than the bill's chain-of-segments: they have no distinctive identity to chain on (every "Transfer to Savings" leg looks alike), so the transfer and both legs are rebuilt at the same ids with a later end date, and the withdrawal's reservation is re-fitted to the longer span. A no-op — no writes — when every ongoing transfer already reaches the horizon.</summary>
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

                // Ongoing only (the withdrawal leg carries the flag), and only a
                // well-formed pair whose accounts still exist — otherwise leave it be.
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

                // Clear the old withdrawal reservation before re-fitting it to the
                // longer span, so a re-proposed plan on a different key can't leave a
                // stale duplicate behind. A transfer's reservation is mechanical (never
                // a user-designed plan), so re-proposing it — as the create flow does —
                // is fine here.
                var withdrawalPlan = AllocationPlanProposer.Propose(result.Withdrawal, [], CurrentAsOfDate(), spreadEvenlyWithNoIncome: false);
                _earMarkPatterns.Delete(withdrawal.FinanceId);
                _transfers.Save(result with { Withdrawal = withdrawalPlan.Outflow });
                _earMarkPatterns.Save(withdrawalPlan.Plan);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // A single malformed transfer must not break the whole forecast.
                Log($"Skipped extending ongoing transfer {transfer.Id}: {ex.Message}");
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
        // Ignored when Accounts is set (there is always at least one account),
        // but still required by the record.
        FinancialPatterns = [],
        EarMarkPatterns = [],
        StartingBalance = 0m,
        AsOfDate = asOfDate,
        HorizonEndDate = horizonEndDate,
        Accounts = BuildAccountInputs(),
        // planning/14 item A-1: lets the household roll-up add a transfer's
        // reservation back into free, so moving your own money never reads
        // as household spending.
        TransferWithdrawalFinanceIds = _financialPatterns.GetTransferWithdrawalFinanceIds(),
    };

    /// <summary>[CALC] Re-runs the forecast with the given goals' savings plans omitted — the "room for these plans" view the save-confirmation's affordability ceiling sizes suggestions against. Same inputs and window as the shown forecast, just filtered; deliberately does NOT touch _lastForecast (a throwaway calculation, not the shown forecast).</summary>
    /// <param name="omitFinanceIds">The goals whose EarMarkPatterns to leave out.</param>
    private ForecastResult ForecastOmitting(IReadOnlySet<int> omitFinanceIds)
    {
        var asOfDate = _lastForecast?.AsOfDate ?? CurrentAsOfDate();
        var horizonEnd = _lastForecast?.HorizonEndDate ?? CurrentAsOfDate().AddMonths(3);
        return TransactionLogBookFactory.CreateForecast(
            BuildForecastOptions(asOfDate, horizonEnd).WithoutPlansFor(omitFinanceIds));
    }

    /// <summary>[CALC] Re-runs the forecast with one not-yet-saved one-off earmark folded in — the live "what if I saved this one-off" preview the Earmark form's summary reads its jar off. Same inputs and window as the shown forecast, just with the proposed earmark added; a throwaway calculation, never stored on _lastForecast.</summary>
    /// <param name="oneOff">The proposed one-off earmark to include.</param>
    private ForecastResult ForecastWithOneOff(ManualEarmark oneOff)
    {
        var asOfDate = _lastForecast?.AsOfDate ?? CurrentAsOfDate();
        var horizonEnd = _lastForecast?.HorizonEndDate ?? CurrentAsOfDate().AddMonths(3);
        return TransactionLogBookFactory.CreateForecast(
            BuildForecastOptions(asOfDate, horizonEnd).WithManualEarmark(oneOff));
    }

    /// <summary>[CALC] Re-runs the forecast with a not-yet-saved savings plan substituted in (plus any proposed manual earmarks that go with it, e.g. a starting earmark) — the live "what if I saved this plan" preview the Earmark form's first-payment warning reads its free-funds figure off. Same inputs and window as the shown forecast; a throwaway calculation, never stored on _lastForecast.</summary>
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

    /// <summary>[UI] "All accounts" plus one entry per account. Kept in step with the forecast so a renamed or deleted account can't linger in the filter.</summary>
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

        // Never leave the detail pane blank: fall back to the as-of day if the
        // day that was selected has no cell under the current filter.
        if (_dayCellsByDate.TryGetValue(selectDate, out var cell)
            || _dayCellsByDate.TryGetValue(forecast.AsOfDate, out cell))
        {
            SelectDay(cell);
        }

        // Land the viewport on today's month when today is in range (§2.g);
        // otherwise start at the top. Deferred to Loaded priority because the
        // virtualizing panel hasn't measured at the moment ItemsSource is set.
        var today = DateOnly.FromDateTime(DateTime.Today);
        var targetMonth = today >= forecast.AsOfDate && today <= forecast.HorizonEndDate
            ? months.FirstOrDefault(month => month.MonthAutomationId == $"Month_{today:yyyy-MM}") ?? months[0]
            : months[0];
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => TimelineCalendar.ScrollIntoView(targetMonth));
    }

    /// <summary>[CALC] The rich month calendar (planning/11 §B). Each active day shows two LABELED numbers (Total + Free), the day's top event by name, an explicit event count, a per-account flow strip, and a ⚠ + words warning when an account is short.</summary>
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

        /// <summary>[CALC] An event the USER would count: a transaction, a scheduled allocation, or a manual adjustment. System-generated reservation steps (automatically funded expense accrual, cushion fills, deallocation give-backs) are mechanism, not events, so they are not counted.</summary>
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
                    // On the as-of day every account reports its settled seed;
                    // otherwise carry forward its latest snapshot.
                    var sampled = isAsOf ? account.Page.InitialSnapshot : SnapshotAsOf(account.Page, date);
                    total += sampled.ExpectedAmount ?? 0m;
                    var accountFree = sampled.ExpectedFreeAmount ?? 0m;
                    free += accountFree;
                    if (accountFree < 0m)
                    {
                        shortNames.Add(account.Name);
                    }

                    // Events (and therefore flow) only exist on the account's own
                    // event dates — never on the settled as-of day.
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

                // The day's highest-priority expected transaction, by name (§C.5).
                var topEvent = todaysTransactions
                    .Select(transaction => patternInfo.TryGetValue(transaction.FinanceId, out var info)
                        ? (info.Priority, info.Name, Magnitude: Math.Abs(transaction.ExpectedAmount))
                        : (Priority: 0, Name: forecast.JarLabels.GetValueOrDefault(transaction.FinanceId, string.Empty), Magnitude: Math.Abs(transaction.ExpectedAmount)))
                    .OrderByDescending(candidate => candidate.Priority)
                    .ThenByDescending(candidate => candidate.Magnitude)
                    .Select(candidate => candidate.Name)
                    .FirstOrDefault() ?? string.Empty;

                // ⚠ + words, never color alone (§C.1). "Thin" has no defined
                // threshold yet, so only the definite "short" case is worded.
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

    /// <summary>[UI] The detail pane is the inner layer of the display onion (§3): the cell IS a BalanceSnapshot, and selecting it shows everything the day holds — every event ("what happened today", left) and every fund jar with its per-type health (right), plus the day's free amount. Account-first (planning/11, grouped two-pane): both panes group by account so each account's story — its events (left) and its jars (right) — stays together. The header shows the household free to spend and names any short account, so a positive total never hides a locally-short one.</summary>
    /// <param name="date">The day to show detail for.</param>
    private void ShowDayDetail(DateOnly date)
    {
        if (_lastForecast is not { } forecast)
        {
            return;
        }

        var householdDay = forecast.Household.Days.FirstOrDefault(day => day.Date == date);
        var householdFree = householdDay?.Free
            ?? (date == forecast.AsOfDate
                ? forecast.Household.AsOfFree
                : forecast.Accounts.Sum(account => SnapshotAsOf(account.Page, date).ExpectedFreeAmount ?? 0m));
        var shortAccounts = householdDay?.ShortAccounts ?? [];
        var anyDeallocation = forecast.Accounts.Any(account => account.Page.BalanceRecord.GetValueOrDefault(date)?.IsDeallocationDay == true);

        DeallocationDayChip.Visibility = anyDeallocation ? Visibility.Visible : Visibility.Collapsed;
        DayDetailHeader.Text = shortAccounts.Count > 0
            ? $"Selected day — {date:D}  ·  {string.Join(", ", shortAccounts)} short on cash"
            : $"Selected day — {date:D}";

        FreeToSpendText.Text = householdFree.ToString("C");
        FreeToSpendText.Foreground = householdFree < 0m
            ? (Brush)FindResource("RedTextBrush")
            : Brushes.Black;

        // Each account's free on this day, computed up front so a short account's
        // gap can be classified as coverable-by-transfer (rung 2, name the donor)
        // vs. genuinely short household-wide (rung 3) — the ladder, planning/22.
        var freeByAccount = forecast.Accounts.ToDictionary(
            account => account.AccountId,
            account => (account.Page.BalanceRecord.GetValueOrDefault(date) ?? SnapshotAsOf(account.Page, date)).ExpectedFreeAmount ?? 0m);

        var eventRows = new List<DayEventRow>();
        var jarRows = new List<JarDetailRow>();
        var anyEarmarks = false;
        var anyUserSetAside = false;

        foreach (var account in forecast.Accounts)
        {
            var page = account.Page;
            var snapshot = page.BalanceRecord.GetValueOrDefault(date) ?? SnapshotAsOf(page, date);
            anyEarmarks |= page.EarmarkPatterns.Count > 0;

            var earmarkedIds = page.EarmarkPatterns.Select(earmark => earmark.FinanceId).ToHashSet();

            // The user has funded a savings plan of their own when a jar tied to one
            // of their earmark patterns holds money — auto-reserved mandatory bills
            // don't count. Its absence (with cash to spare) is the rung-1 nudge's cue.
            anyUserSetAside |= snapshot.FundJars.Any(jar =>
                jar.FinanceId is { } fundedId && jar.ExpectedAmount > 0m && earmarkedIds.Contains(fundedId));

            // How short this account is on this day, and — when short — whether other
            // accounts hold enough free cash to cover it (rung 2, naming a single-
            // account donor) or not (rung 3). Drives the header narrative + lever.
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
                    // Name the donor only when one account alone covers the whole gap;
                    // otherwise the cash is spread and the line stays generic.
                    donorName = donors
                        .Where(donor => donor.Free >= shortfall)
                        .OrderByDescending(donor => donor.Free)
                        .Select(donor => donor.Name)
                        .FirstOrDefault();
                }
            }
            var groupKey = new AccountGroupKey(account.AccountId, account.Name, accountBalance, shortfall, canCoverElsewhere, donorName);

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

            // Events land only on an account's own event dates; jars carry
            // forward, so every account shows its current jars on any selected day.
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
                // Omit the safety-cushion row when the account has no cushion set —
                // don't spend the pane's height on a jar the user never opted into.
                if (jar.FinanceId is null && page.IdealSafetyCushion <= 0m)
                {
                    continue;
                }
                var jarRow = JarDetailRow.From(jar, snapshot, context);
                jarRow.Account = groupKey;
                jarRows.Add(jarRow);
            }
        }

        // Rung-1 nudge: spare cash on hand, but the user hasn't funded any goal of
        // their own, so the free figure is really just the balance (planning/22).
        var showFreeNudge = householdFree > 0m && !anyUserSetAside;
        FreeNudgeText.Text = showFreeNudge
            ? "Set money aside for your goals so this reflects what's really spare."
            : string.Empty;
        FreeNudgeText.Visibility = showFreeNudge ? Visibility.Visible : Visibility.Collapsed;

        AdjustFundsButton.IsEnabled = anyEarmarks;
        DayEventsList.ItemsSource = GroupByAccount(eventRows);
        JarDetailList.ItemsSource = GroupByAccount(jarRows);
    }

    /// <summary>[CALC] Both selected-day panes group their rows under an account header. The rows expose an Account property the group description reads.</summary>
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

    /// <summary>[UI] Philosophy §1: the Forecast button reads as actionable only while an input (range, balance, or cushion) differs from the forecast on screen. Wired to every input's change event; unparseable text counts as "differs" so the button stays live and the click handler can explain what's wrong.</summary>
    private void OnForecastInputChanged(object sender, RoutedEventArgs e) => UpdateForecastButtonState();

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

        // Editing an account changes the totals the shown forecast was built
        // from, so the button lights up exactly as a changed date does.
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

    private void OnAddManualEarmarkClick(object sender, RoutedEventArgs e) =>
        ShowManualEarmarkForm(initialDate: null, editTarget: null);

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

    /// <summary>[STEP] planning/21 Philosophy 5/7: the permanent Earmark tab replaces ManualEarmarkWindow's popup — same "no funds yet" guard as before, then populates the tab's One-off mode instead of opening a dialog.</summary>
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

    /// <summary>[WRITES FILE] Output-only snapshot of whatever forecast is currently on screen — not to be confused with Export Data above, which copies the raw db file for backup/transfer. See ForecastSpreadsheetExporter.</summary>
    private void OnExportForecastSpreadsheetClick(object sender, RoutedEventArgs e)
    {
        if (_lastForecast is not { } forecast)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export forecast as spreadsheet",
            FileName = $"mymoneyforecast-{forecast.AsOfDate:yyyy-MM-dd}.xlsx",
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

        // TEMPORARY DIAGNOSTIC (2026-08-12) — remove once the empty-grids-on-
        // launch report is resolved.
        Log($"RefreshGrids — financialPatterns.Count: {financialPatterns.Count}, earMarkPatterns.Count: {earMarkPatterns.Count}");

        // financeId -> the name of the account it's filed under, so the grid
        // can show where each bill/paycheck/goal lives — never a mystery.
        var accountNamesById = _accounts.GetAll().ToDictionary(account => account.Id, account => account.Name);
        var accountNameByFinanceId = _financialPatterns.GetAllByAccount()
            .SelectMany(entry => entry.Value.Select(pattern => (pattern.FinanceId, AccountId: entry.Key)))
            .ToDictionary(pair => pair.FinanceId, pair => accountNamesById.GetValueOrDefault(pair.AccountId, "(unknown)"));

        // A transfer's two patterns are hidden from this list — a transfer
        // shows on its own tab as one thing, not as its two underlying
        // patterns. The engine still reads every pattern when forecasting.
        FinancialPatternsGrid.ItemsSource = _financialPatterns.GetAllExcludingTransferPatterns()
            .Select(pattern => new FinancialPatternRow(pattern, accountNameByFinanceId.GetValueOrDefault(pattern.FinanceId, "(unknown)")))
            .ToList();

        RefreshTransfersGrid();

        // Every outflow that reserves has its own Allocation Plan (an
        // EarMarkPattern, per Stage 1's allocation model — planning/14), so
        // the grid just shows those; nothing here is a synthesized "automatic"
        // row. A transfer's withdrawal reserves through a plan too, but it's
        // hidden here for the same reason its patterns are: a transfer is
        // shown as one thing on its own tab, not as its underlying
        // reservation machinery.
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
    }

    /// <summary>[WRITES FILE] Export/Import move the raw SQLite file rather than any intermediate format (XML, CSV, etc.) — it's already the single source of truth, so copying it byte-for-byte is both the simplest option and the only one that can't lose or misrepresent data in translation.</summary>
    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export MyMoneyForecast data",
            FileName = $"mymoneyforecast-backup-{DateTime.Today:yyyy-MM-dd}.db",
            Filter = "MyMoneyForecast database (*.db)|*.db|All files (*.*)|*.*",
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
            Title = "Import MyMoneyForecast data",
            Filter = "MyMoneyForecast database (*.db)|*.db|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!PatternDatabase.LooksLikeValidDatabaseFile(dialog.FileName))
        {
            MessageBox.Show(
                this,
                "That file doesn't look like a MyMoneyForecast export — it's missing the expected data.",
                "Import failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        // Deeper than the table-presence check above: confirm the file actually LOADS (migrations run,
        // every row re-validates) before touching the live database, so an incompatible or corrupted
        // export is rejected here rather than crashing the app on the restart below.
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
                // A safety net taken before the overwrite. In the demo this is a
                // timestamped copy in backups\ (older ones are kept); off-demo it's
                // the single "<db>.bak" beside the database, exactly as before.
                File.Copy(liveDatabasePath, AppPaths.NextImportBackupPath(), overwrite: true);
            }

            // Overwrites the live file AND removes its leftover -wal/-shm/-journal
            // scratch files, so no stale journal can be rolled back into the
            // imported data the next time it opens.
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

    /// <summary>[STEP] planning/21 Philosophy 5/7: populates and switches to the permanent Expense tab instead of opening CreateFinancialPatternWindow. Persistence + AutoCreateAllocationPlan now happen in ExpenseForm.PatternSaved (wired in the constructor), matching Earmark's shape.</summary>
    private void OnAddFinancialPatternClick(object sender, RoutedEventArgs e)
    {
        RefreshExpenseFormContext();
        ExpenseForm.LoadForNewPattern();
        SwitchToTab("Expense");
    }

    private void OnCreateBillClick(object sender, RoutedEventArgs e)
    {
        RefreshExpenseFormContext();
        ExpenseForm.LoadForNewBill();
        SwitchToTab("Expense");
    }

    /// <summary>[STEP] What ExpenseForm.PatternSaved calls (wired in the constructor) — planning/25's own migration target, now resolved: routes every Expense save through FinancePatternSaveConfirmation instead of a plain repository save, so a Critical edit against existing history goes through EditingHistoryConfirmationWindow first. AutoCreateAllocationPlan (Stage 1's own, older concern — a brand-new outflow always gets a proposed plan, regardless of anything planning/25 added) has to be called from two places below rather than once: Run()'s own NavigateToEarmarkForm callback fires before this method regains control, so for a new pattern going straight to Earmark, the plan has to be created inside that callback (and the plan this method was handed — computed before the plan existed — re-read afterward); for "Save and Skip planning," it happens here instead, after Run() returns.</summary>
    /// <param name="pattern">The form's current field values — what the user typed, before any implicit break-off/correction Run() might apply.</param>
    /// <param name="accountId">Which account the pattern is filed under.</param>
    /// <param name="isNew">Whether this is a brand-new pattern (Stage 1's proposer should run) or an edit to an existing one.</param>
    /// <param name="jumpToEarmark">True for "Save and Plan," false for "Save and Skip planning."</param>
    private bool OnExpensePatternSaved(FinancialPattern pattern, int accountId, bool isNew, bool jumpToEarmark)
    {
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
                if (isNew)
                {
                    // Run()'s own AskWhichEarmarkPatternToOpen already ran
                    // (before this callback did) and found nothing, since
                    // the plan doesn't exist until AutoCreateAllocationPlan
                    // creates it right here — re-read rather than trusting
                    // the now-stale (null) plan parameter above.
                    AutoCreateAllocationPlan(pattern, accountId);
                    plan = _earMarkPatterns.GetAll().FirstOrDefault(p => p.FinanceId == pattern.FinanceId);
                }

                // The save (and any AutoCreateAllocationPlan just above) changed
                // the data, but _lastForecast is still the pre-save snapshot the
                // confirmation ran against. Recompute it BEFORE refreshing the form
                // contexts, so the Earmark Summary we're about to land on shows the
                // post-save state rather than a stale reading (EarmarkFormPanel.UpdateSummary's
                // own TODO). Guarded on there being a forecast to re-run at all —
                // with none, the Summary already shows its own no-forecast state.
                if (_lastForecast is { } shown)
                {
                    RefreshForecast(shown.AsOfDate, shown.HorizonEndDate);
                }

                RefreshGrids();
                RefreshExpenseFormContext();
                RefreshEarmarkFormContext();

                if (plan is not null)
                {
                    // Looked up by the PLAN's own FinanceId, not pattern's —
                    // after a break-off, plan belongs to the new successor,
                    // and pattern is still the original, now-superseded values.
                    var goal = _financialPatterns.GetByFinanceId(plan.FinanceId) ?? pattern;
                    EarmarkForm.LoadPattern(plan, goal, suggestedOverrides);
                    SwitchToTab("Earmark");
                }
                else if (pattern.Amount < 0m)
                {
                    // A brand-new outflow with no plan yet opens a blank Earmark
                    // form so the user can create one.
                    EarmarkForm.LoadForNewPattern();
                    SwitchToTab("Earmark");
                }
                else
                {
                    // Income never gets a savings plan of its own, so there's
                    // nothing to open on the Earmark tab — "Save and Plan" lands
                    // back on the Forecast tab, the same as "Save and Skip
                    // planning" already does.
                    SwitchToTab("Forecast");
                }
            },
        };

        if (!confirmation.Run())
        {
            return false; // user cancelled — nothing saved; the form keeps the user's edits and the Expense tab stays as-is
        }

        if (!jumpToEarmark)
        {
            // The NavigateToEarmarkForm callback above never fires for "Save
            // and Skip planning" (Run() only invokes it when planning wasn't
            // skipped) — same AutoCreateAllocationPlan-then-refresh sequence,
            // just without any Earmark-tab navigation at the end.
            if (isNew)
            {
                AutoCreateAllocationPlan(pattern, accountId);
            }

            // Recompute the forecast before switching to it, so the Forecast tab
            // reflects the just-saved edit rather than the pre-save snapshot — the
            // same refresh the "Save and Plan" path (NavigateToEarmarkForm) already
            // does, closing the planning/24 gap for this branch too.
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

    /// <summary>[STEP] What EarmarkForm.PatternSaved calls (wired in the constructor) — planning/27's own migration target, now resolved: routes every Savings-plan save through EarmarkPatternSaveConfirmation instead of a plain repository save, so a Start/Until edit that touches a chain neighbor, or an Amount/schedule edit with later segments to carry it to, goes through EditingHistoryConfirmationWindow first.</summary>
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

        // planning/21: Earmark's own save "returns to the Forecast tab —
        // no onward hop from there to anywhere else."
        SwitchToTab("Forecast");

        return true;
    }

    /// <summary>[STEP] Shows EditingHistoryConfirmationWindow and returns the raw selections it reports — shared by both the Expense and Earmark save paths' own ConfirmImplicitChanges wiring, since the window is the same either way; each request's own rows decide what it actually shows, and the wrapper reads each selection back into a decision.</summary>
    private ConfirmationOutcome ShowEditingHistoryConfirmation(ImplicitChangeConfirmationRequest request)
    {
        var confirmWindow = new EditingHistoryConfirmationWindow(request) { Owner = this };
        var proceed = confirmWindow.ShowDialog() == true;
        return confirmWindow.ToOutcome(proceed);
    }

    /// <summary>[UI] AskWhichEarmarkPatternToOpen's own real disambiguation, built 2026-08-17 — shows EarmarkPatternPickerWindow and returns whichever plan the user picked. Cancelling the picker (SelectedPlan stays null) falls back to the first plan in the list rather than opening nothing at all — Save has already committed by the time this runs, so there's always a real plan to land on somewhere, and refusing to pick one would only strand the user on whatever tab they started from.</summary>
    /// <param name="plans">Every EarMarkPattern surviving for the goal — always more than one; AskWhichEarmarkPatternToOpen's own gate never calls this otherwise.</param>
    private EarMarkPattern? PickEarmarkPatternToOpen(IReadOnlyList<EarMarkPattern> plans)
    {
        var picker = new EarmarkPatternPickerWindow(plans) { Owner = this };
        return picker.ShowDialog() == true ? picker.SelectedPlan : plans[0];
    }

    /// <summary>[UI] What ExpenseForm.PickChainSegment calls (wired in the constructor) — shows ChainSegmentPickerWindow so the user can choose which segment of a break-off chain to open. Returns the chosen segment, or null on Cancel (ExpenseFormPanel then keeps the current segment).</summary>
    /// <param name="segments">Every FinancialPattern in the chain (same Source) — always more than one; ExpenseFormPanel only calls this when there's a real choice.</param>
    private FinancialPattern? PickChainSegmentToEdit(IReadOnlyList<FinancialPattern> segments)
    {
        var picker = new ChainSegmentPickerWindow(segments) { Owner = this };
        return picker.ShowDialog() == true ? picker.SelectedSegment : null;
    }

    /// <summary>[CALC] Every scheduled outflow reserves through its own Allocation Plan (Stage 1's allocation model — planning/14), proposed at creation from the current as-of date and the user's income. Income never gets one (A-1). The plan (and any starting earmark, for a bill due before its first paycheck) is persisted like a savings plan and appears in the earmark grid, where it can be edited or removed. Transfer patterns are excluded from the income scan so a deposit isn't mistaken for a paycheck, and the scan is scoped to this outflow's own account (planning/17, F33) — a paycheck filed under a different account never actually funds this one.</summary>
    /// <param name="pattern">The newly-created outflow to propose an allocation plan for.</param>
    /// <param name="accountId">Which account the outflow is filed under — scopes the income scan.</param>
    private void AutoCreateAllocationPlan(FinancialPattern pattern, int accountId)
    {
        if (pattern.Amount >= 0m)
        {
            return;
        }

        // Cap the front-load and the ongoing rate at what the funds can spare (the affordability ceiling),
        // both measured on a re-forecast with this bill's own plans omitted so they aren't counted against
        // themselves — the single-date ceiling for the front-load, the range ceiling for the per-cycle rate.
        var room = ForecastOmitting(new HashSet<int> { pattern.FinanceId });
        var roomPage = room.Accounts.FirstOrDefault(account => account.AccountId == accountId)?.Page ?? room.PrimaryAccountPage;
        var startingCeiling = AffordabilityCeiling.ForStartingEarmark(roomPage, pattern, CurrentAsOfDate(), ChangeKind.Implicit);
        var rateCeiling = AffordabilityCeiling.For(roomPage, pattern, CurrentAsOfDate(), ChangeKind.Implicit);
        var proposal = AllocationPlanProposer.Propose(
            pattern, _financialPatterns.GetByAccountExcludingTransferPatterns(accountId), CurrentAsOfDate(),
            startingEarmarkCeiling: startingCeiling,
            ongoingRateCeiling: rateCeiling);
        // The proposer may stretch the outflow's active span back to the as-of
        // date (planning/15, ActiveFrom) so its plan fits — persist that prepared
        // outflow, not the original, or the plan reads short against a goal whose
        // own Start is later.
        _financialPatterns.Save(proposal.Outflow, accountId);
        _earMarkPatterns.Save(proposal.Plan);
        if (proposal.StartingEarmark is { } starting)
        {
            _manualEarmarks.Save(starting);
        }
    }

    private DateOnly CurrentAsOfDate() =>
        AsOfDatePicker.SelectedDate is { } asOf
            ? DateOnly.FromDateTime(asOf)
            : DateOnly.FromDateTime(DateTime.Today);

    /// <summary>[CALC] What ExpenseForm.RequestForecast calls (wired in the constructor): a feature that needs a forecast to work (FinancialPatternPickerWindow's own data source) should just compute one using whatever's on the As-Of/Horizon pickers right now, not tell the user to go press the Forecast button first. RefreshForecast always assigns _lastForecast when it returns (or a real computation error propagates, which is the honest outcome, not something to swallow) — so the null-forgiving return below is never actually lying.</summary>
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

        // The form opens on whichever account the pattern is already filed
        // under, so an unchanged pick preserves the filing and a changed one
        // deliberately moves it.
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

        // A savings plan whose goal no longer exists is invalid by 3.10.a3,
        // not merely untidy, so deleting a pattern removes its linked plan
        // too — we still say what is about to happen rather than doing it
        // silently.
        var hasSavingsPlan = _financialPatterns.HasLinkedEarMarkPattern(row.FinanceId);
        if (hasSavingsPlan)
        {
            // planning/16 item 18: nothing is actually moved by this — a jar was
            // never a real transfer of money (FundJar's own class doc), so once
            // its plan is gone the amount simply reads as free again on the next
            // forecast. Naming it here says what "continue" actually does,
            // rather than leaving the user to notice their free funds went up.
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

    /// <summary>[STEP] planning/14 item D-1. An outflow with no savings plan has its jar filled by the standing automatic rule; this hands that jar over to a plan the user owns. Seeded from what the jar already holds, so pressing it moves no money — it only changes what governs the jar from here on.</summary>
    private void OnSetUpSavingsPlanClick(object sender, RoutedEventArgs e)
    {
        if (FinancialPatternsGrid.SelectedItem is not FinancialPatternRow row)
        {
            ShowNothingSelected();
            return;
        }

        if (row.Amount >= 0m)
        {
            MessageBox.Show(
                this,
                "Money coming in doesn't need a savings plan — there's nothing to set aside for it.",
                "Nothing to save toward",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_financialPatterns.HasLinkedEarMarkPattern(row.FinanceId))
        {
            MessageBox.Show(
                this,
                "This already has a savings plan. Edit it on the Allocations tab.",
                "Already has a plan",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        RefreshEarmarkFormContext();
        EarmarkForm.LoadForMaterialize(row.Pattern, CurrentJarAmount(row.FinanceId));
        SwitchToTab("Earmark");
    }

    /// <summary>[CALC] What this jar holds as of the forecast's own start date, so a new savings plan can pick up exactly where the automatic filling left off. Zero when there is no forecast on screen yet, or the jar doesn't exist in it.</summary>
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
            // The goal (a finance pattern) is filed under the chosen account;
            // its earmark reaches the same account through finance_id.
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
