using System.Globalization;
using System.IO;
using System.Windows;
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

        var database = new PatternDatabase();
        _financialPatterns = new FinancialPatternRepository(database);
        _earMarkPatterns = new EarMarkPatternRepository(database, _financialPatterns);
        _currentBalance = new CurrentBalanceRepository(database);
        _accounts = new AccountRepository(database);
        _transfers = new TransferRepository(database, _financialPatterns);
        _manualEarmarks = new ManualEarmarkRepository(database, _earMarkPatterns);

        // Item 6 migration: there is always at least one account. On the first
        // run after accounts landed, the old single balance/cushion becomes the
        // "primary" account, so nothing the user already entered is lost.
        var legacy = _currentBalance.GetCurrent();
        _accounts.EnsureDefaultAccount(legacy?.Balance ?? 0m, legacy?.IdealSafetyCushion ?? 0m);

        RefreshGrids();
        RefreshAccountsGrid();
        LoadSavedBalance();
    }

    // Pre-fills from whatever was entered last time (Forecast tab's own state
    // is meant to persist across launches, unlike a one-off what-if input) —
    // and, if there's something to show, forecasts immediately so the tab
    // isn't blank on a normal relaunch.
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

    // Until the engine partitions per account (item 4), the forecast still runs
    // on one combined figure — the household total across every account, which
    // is exactly what the old single balance meant.
    // Which account a newly created pattern is filed under. The account picker
    // (item 2-B) replaces this with the user's explicit choice; until then new
    // patterns file under the first account, matching today's behaviour.
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

    private void OnAddTransferClick(object sender, RoutedEventArgs e) => ShowTransferDialog();

    // The selected day's lever for a short account (planning/11 §B): opens the
    // transfer form already pointed at that account for what it is short. The
    // user still confirms — we surface the problem and make the fix easy, we
    // don't move their money for them (philosophy 1).
    private void OnCoverShortfallClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: System.Windows.Data.CollectionViewGroup group }
            && group.Name is AccountGroupKey key)
        {
            ShowTransferDialog(key.AccountId, key.Shortfall, _selectedDayCell?.Date);
        }
    }

    private void ShowTransferDialog(
        int? preselectToAccountId = null,
        decimal? preselectAmount = null,
        DateOnly? preselectDate = null)
    {
        var accounts = _accounts.GetAll();
        if (accounts.Count < 2)
        {
            MessageBox.Show(this, "You need at least two accounts to transfer between. Add another on the Accounts tab first.", "Not enough accounts", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new CreateTransferWindow(accounts, preselectToAccountId, preselectAmount, preselectDate) { Owner = this };
        if (window.ShowDialog() != true || window.DatePattern is not { } schedule)
        {
            return;
        }

        var namesById = accounts.ToDictionary(account => account.Id, account => account.Name);

        // Two fresh finance ids for the patterns (they are real patterns, so they
        // must not collide with any existing pattern's id — patterns included).
        var maxFinanceId = _financialPatterns.GetAll().Select(pattern => pattern.FinanceId).DefaultIfEmpty(0).Max();

        var result = TransferFactory.Create(new TransferRequest
        {
            TransferId = _transfers.NextId(),
            WithdrawalFinanceId = maxFinanceId + 1,
            DepositFinanceId = maxFinanceId + 2,
            FromAccountId = window.FromAccountId,
            ToAccountId = window.ToAccountId,
            FromAccountName = namesById[window.FromAccountId],
            ToAccountName = namesById[window.ToAccountId],
            Amount = window.Amount,
            DatePattern = schedule,
        });

        _transfers.Save(result);

        // Stage-1 revision (planning/14): the transfer reserves in the account it
        // leaves, through a front-loaded Allocation Plan on the withdrawal — no
        // income pacing (a transfer isn't a recurring bill), so the no-income
        // shape reserves the full amount from the as-of date. The withdrawal is
        // already persisted above, so its plan's finance id resolves.
        var withdrawalPlan = AllocationPlanProposer.Propose(result.Withdrawal, [], CurrentAsOfDate());
        _earMarkPatterns.Save(withdrawalPlan.Plan);

        RefreshGrids();

        // The transfer's patterns change the cascade, so re-run the forecast — that
        // is what actually clears the shortfall the lever was offered for.
        RefreshShownForecast();
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

    private void OnAddAccountClick(object sender, RoutedEventArgs e)
    {
        var window = new AccountWindow(_accounts.NextId()) { Owner = this };
        if (window.ShowDialog() != true || window.Result is not { } account)
        {
            return;
        }

        if (_accounts.GetByName(account.Name) is not null)
        {
            MessageBox.Show(this, $"There's already an account called \"{account.Name}\".", "Name already used", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _accounts.Save(account);
        RefreshAccountsGrid();
    }

    private void OnEditAccountClick(object sender, RoutedEventArgs e)
    {
        if (AccountsGrid.SelectedItem is not AccountRow row)
        {
            ShowNothingSelected();
            return;
        }

        var window = new AccountWindow(row.Account.Id, row.Account) { Owner = this };
        if (window.ShowDialog() != true || window.Result is not { } account)
        {
            return;
        }

        // A rename must not collide with a different account's name.
        if (_accounts.GetByName(account.Name) is { } clash && clash.Id != account.Id)
        {
            MessageBox.Show(this, $"There's already an account called \"{account.Name}\".", "Name already used", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _accounts.Save(account);
        RefreshAccountsGrid();
    }

    // Blocked while it's the only account, and blocked while anything is still
    // filed under it — we never delete the user's bills out from under them
    // (philosophy 1). The transfers half of that guard arrives with item 3.
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

        // Balance and cushion are per account now, and each one is validated as
        // its account is saved — so there is nothing to parse here. The forecast
        // runs on the household totals until the engine partitions per account
        // (item 4).
        var (balance, idealSafetyCushion) = AccountTotals();

        // The legacy row still carries the global as-of/horizon; its balance and
        // cushion columns are kept in step only so the row stays coherent, and
        // retire when it becomes the TransactionLogBook settings row (item 6).
        _currentBalance.Save(balance, asOfDate, horizonEndDate, idealSafetyCushion);
        RefreshForecast(asOfDate, horizonEndDate);
    }

    // No upper bound on the horizon by design — years out is a legitimate
    // request (long-term goals, mortgage-length planning), so this is left to
    // whatever the user picks rather than an app-imposed ceiling. The calendar
    // stays cheap at that scale because the outer ListBox virtualizes months.
    // One AccountForecastInput per account: its own balance/cushion, and the
    // patterns/earmarks/manuals filed under it (an earmark or manual reaches its
    // account through its finance id — item 2-A). A transfer's two patterns are patterns
    // filed under an account too, so they ride along and feed its cascade.
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
        var forecast = TransactionLogBookFactory.CreateForecast(new ForecastOptions
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
        });

        _lastForecast = forecast;
        var (balance, cushion) = AccountTotals();
        _shownBalance = balance;
        _shownCushion = cushion;

        PopulateAccountFilter(forecast);
        RenderCalendar(forecast, selectDate: forecast.AsOfDate);
        ExportForecastSpreadsheetButton.IsEnabled = true;

        UpdateForecastButtonState();
    }

    // Every calendar day from the as-of month's first day through the horizon
    // month's last: days with a BalanceSnapshot are live cells; event-less and
    // out-of-range days render faint (every day stays visible — §2.I.d).
    // GetTimeline() already folds the dateless initial snapshot in under the
    // as-of date, so keying by date is collision-free.
    // "All accounts" plus one entry per account. Kept in step with the forecast
    // so a renamed or deleted account can't linger in the filter.
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

    // Rebuilds the calendar from the forecast already in hand (no recompute) and
    // restores the selected day.
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

    // The rich month calendar (planning/11 §B). Each active day shows two
    // LABELED numbers (Total + Free), the day's top event by name, an explicit
    // event count, a per-account flow strip, and a ⚠ + words warning when an
    // account is short. `accountFilter` re-scopes every number to one account
    // (null = the household roll-up).
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

        // An event the USER would count: a transaction, a scheduled allocation,
        // or a manual adjustment. System-generated reservation steps (automatically funded expense
        // accrual, cushion fills, deallocation give-backs) are mechanism, not
        // events, so they are not counted.
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

    // The detail pane is the inner layer of the display onion (§3): the cell
    // IS a BalanceSnapshot, and selecting it shows everything the day holds —
    // every event ("what happened today", left) and every fund jar with its
    // per-type health (right), plus the day's free amount.
    // Account-first (planning/11, grouped two-pane): both panes group by account
    // so each account's story — its events (left) and its jars (right) — stays
    // together. The header shows the household free to spend and names any short
    // account, so a positive total never hides a locally-short one.
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
            ? $"Selected day — {date:D}  ·  {string.Join(", ", shortAccounts)} short"
            : $"Selected day — {date:D}";

        FreeToSpendText.Text = householdFree.ToString("C");
        FreeToSpendText.Foreground = householdFree < 0m
            ? (Brush)FindResource("RedTextBrush")
            : Brushes.Black;

        var eventRows = new List<DayEventRow>();
        var jarRows = new List<JarDetailRow>();
        var anyEarmarks = false;

        foreach (var account in forecast.Accounts)
        {
            var page = account.Page;
            var snapshot = page.BalanceRecord.GetValueOrDefault(date) ?? SnapshotAsOf(page, date);
            anyEarmarks |= page.EarmarkPatterns.Count > 0;

            // How short this account is on this day — drives the group header's
            // "Cover from another account" lever.
            var accountFree = snapshot.ExpectedFreeAmount ?? 0m;
            var groupKey = new AccountGroupKey(account.AccountId, account.Name, accountFree < 0m ? -accountFree : 0m);

            var context = new DayDetailContext
            {
                Date = date,
                JarLabels = forecast.JarLabels,
                ShortfallsByFinanceId = forecast.GoalShortfalls.ToDictionary(shortfall => shortfall.FinanceId),
                PatternsById = page.FinancePatterns.ToDictionary(pattern => pattern.FinanceId),
                EarmarkedIds = page.EarmarkPatterns.Select(earmark => earmark.FinanceId).ToHashSet(),
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
                var jarRow = JarDetailRow.From(jar, snapshot, context);
                jarRow.Account = groupKey;
                jarRows.Add(jarRow);
            }
        }

        AdjustFundsButton.IsEnabled = anyEarmarks;
        DayEventsList.ItemsSource = GroupByAccount(eventRows);
        JarDetailList.ItemsSource = GroupByAccount(jarRows);
    }

    // Both selected-day panes group their rows under an account header. The rows
    // expose an Account property the group description reads.
    private static System.ComponentModel.ICollectionView GroupByAccount(System.Collections.IList rows)
    {
        var view = new System.Windows.Data.ListCollectionView(rows);
        view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("Account"));
        return view;
    }

    // An account's snapshot as of a date: the latest dated snapshot on or before
    // it, else its dateless initial snapshot — so a day that is another account's
    // event date still shows this account's carried-forward jars.
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

    // Philosophy §1: the Forecast button reads as actionable only while an
    // input (range, balance, or cushion) differs from the forecast on screen.
    // Wired to every input's change event; unparseable text counts as "differs"
    // so the button stays live and the click handler can explain what's wrong.
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

    // Re-runs the forecast with the inputs it's already showing — for when
    // data that feeds it (manual earmarks) changed rather than the inputs —
    // keeping the same selected day when it still exists.
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
        ShowManualEarmarkDialog(initialDate: _selectedDayCell?.Date, editTarget: null);

    private void OnAddManualEarmarkClick(object sender, RoutedEventArgs e) =>
        ShowManualEarmarkDialog(initialDate: null, editTarget: null);

    private void OnEditManualEarmarkClick(object sender, RoutedEventArgs e)
    {
        if (ManualEarmarksGrid.SelectedItem is not ManualEarmarkRow row)
        {
            ShowNothingSelected();
            return;
        }

        ShowManualEarmarkDialog(initialDate: null, editTarget: row.Earmark);
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

    private void ShowManualEarmarkDialog(DateOnly? initialDate, ManualEarmark? editTarget)
    {
        var patterns = _earMarkPatterns.GetAll();
        if (patterns.Count == 0)
        {
            MessageBox.Show(
                this,
                "Create a savings plan first — manual adjustments live inside a fund's plan (its timeline is the fund's lifetime).",
                "No funds yet", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var labels = _financialPatterns.GetAll()
            .ToDictionary(pattern => pattern.FinanceId, pattern => pattern.Description ?? pattern.Source);

        var window = new ManualEarmarkWindow(
            patterns, labels, _manualEarmarks.GetAll(), _lastForecast, initialDate, editTarget)
        {
            Owner = this,
        };
        if (window.ShowDialog() != true)
        {
            return;
        }

        foreach (var earmark in window.SavedEarmarks)
        {
            _manualEarmarks.Save(earmark);
        }

        foreach (var (financeId, date) in window.DeletedEarmarks)
        {
            _manualEarmarks.Delete(financeId, date);
        }

        RefreshGrids();
        RefreshShownForecast();
    }

    // Output-only snapshot of whatever forecast is currently on screen — not
    // to be confused with Export Data above, which copies the raw db file for
    // backup/transfer. See ForecastSpreadsheetExporter.
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
            MessageBox.Show(this, $"Couldn't export: {ex.Message}", "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        MessageBox.Show(this, $"Exported to {dialog.FileName}.", "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RefreshGrids()
    {
        var financialPatterns = _financialPatterns.GetAll();
        var earMarkPatterns = _earMarkPatterns.GetAll();

        // financeId -> the name of the account it's filed under, so the grid can
        // show where each bill/paycheck/goal lives (item 2-B: never a mystery).
        var accountNamesById = _accounts.GetAll().ToDictionary(account => account.Id, account => account.Name);
        var accountNameByFinanceId = _financialPatterns.GetAllByAccount()
            .SelectMany(entry => entry.Value.Select(pattern => (pattern.FinanceId, AccountId: entry.Key)))
            .ToDictionary(pair => pair.FinanceId, pair => accountNamesById.GetValueOrDefault(pair.AccountId, "(unknown)"));

        // A transfer's two patterns are hidden from this list — a transfer shows on its own
        // tab as one thing, not as its two underlying patterns (item 3). The
        // engine still reads every pattern (patterns included) when forecasting.
        FinancialPatternsGrid.ItemsSource = _financialPatterns.GetAllExcludingTransferPatterns()
            .Select(pattern => new FinancialPatternRow(pattern, accountNameByFinanceId.GetValueOrDefault(pattern.FinanceId, "(unknown)")))
            .ToList();

        RefreshTransfersGrid();

        // Stage-1 revision (planning/14): the computed A/B ramp is retired, so
        // there are no longer "automatic" rows without a real pattern behind
        // them — every outflow that reserves has its own Allocation Plan
        // (an EarMarkPattern), so the grid just shows those.
        EarMarkPatternsGrid.ItemsSource = earMarkPatterns
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

    // Export/Import move the raw SQLite file rather than any intermediate
    // format (XML, CSV, etc.) — it's already the single source of truth, so
    // copying it byte-for-byte is both the simplest option and the only one
    // that can't lose or misrepresent data in translation.
    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export MyMoneyForecast data",
            FileName = $"mymoneyforecast-backup-{DateTime.Today:yyyy-MM-dd}.db",
            Filter = "MyMoneyForecast database (*.db)|*.db|All files (*.*)|*.*",
        };

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
                File.Copy(liveDatabasePath, liveDatabasePath + ".bak", overwrite: true);
            }

            File.Copy(dialog.FileName, liveDatabasePath, overwrite: true);
        }
        catch (IOException ex)
        {
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

    private void OnAddFinancialPatternClick(object sender, RoutedEventArgs e)
    {
        var window = new CreateFinancialPatternWindow(_financialPatterns.GetAll(), _accounts.GetAll()) { Owner = this };
        if (window.ShowDialog() == true && window.CreatedPattern is { } pattern)
        {
            _financialPatterns.Save(pattern, window.SelectedAccountId);
            AutoCreateAllocationPlan(pattern);
            RefreshGrids();
        }
    }

    private void OnCreateBillClick(object sender, RoutedEventArgs e)
    {
        var window = new CreateFinancialPatternWindow(_financialPatterns.GetAll(), _accounts.GetAll(), forcedMandatory: true) { Owner = this };
        if (window.ShowDialog() == true && window.CreatedPattern is { } pattern)
        {
            _financialPatterns.Save(pattern, window.SelectedAccountId);
            AutoCreateAllocationPlan(pattern);
            RefreshGrids();
        }
    }

    // Stage-1 revision (planning/14): every scheduled outflow reserves through
    // its own Allocation Plan, proposed at creation from the current as-of date
    // and the user's income. Income never gets one (A-1). The plan (and any
    // starting earmark, for a bill due before its first paycheck) is persisted
    // like a savings plan and appears in the earmark grid, where it can be
    // edited or removed. Transfer patterns are excluded from the income scan so
    // a deposit isn't mistaken for a paycheck.
    private void AutoCreateAllocationPlan(FinancialPattern pattern)
    {
        if (pattern.Amount >= 0m)
        {
            return;
        }

        var proposal = AllocationPlanProposer.Propose(
            pattern, _financialPatterns.GetAllExcludingTransferPatterns(), CurrentAsOfDate());
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

    private void OnEditFinancialPatternClick(object sender, RoutedEventArgs e)
    {
        if (FinancialPatternsGrid.SelectedItem is not FinancialPatternRow row)
        {
            ShowNothingSelected();
            return;
        }

        // The picker opens on whichever account the pattern is already filed
        // under, so an unchanged pick preserves the filing and a changed one
        // deliberately moves it.
        var currentAccountId = _financialPatterns.GetAccountId(row.FinanceId) ?? DefaultAccountId();
        var window = new CreateFinancialPatternWindow(row.Pattern, _accounts.GetAll(), currentAccountId) { Owner = this };
        if (window.ShowDialog() == true && window.CreatedPattern is { } updated)
        {
            _financialPatterns.Save(updated, window.SelectedAccountId);
            RefreshGrids();
        }
    }

    private void OnDeleteFinancialPatternClick(object sender, RoutedEventArgs e)
    {
        if (FinancialPatternsGrid.SelectedItem is not FinancialPatternRow row)
        {
            ShowNothingSelected();
            return;
        }

        var label = row.Description is { Length: > 0 } description ? description : row.Source;

        // planning/14 item D-2: this used to be BLOCKED, sending the user to
        // another tab to delete the savings plan first — which made trying out a
        // speculative purchase a two-step chore across two tabs (charter item
        // 13). A savings plan whose goal no longer exists is invalid by 3.10.a3,
        // not merely untidy, so removing both is the more correct outcome. We
        // still say what is about to happen rather than doing it silently.
        var hasSavingsPlan = _financialPatterns.HasLinkedEarMarkPattern(row.FinanceId);
        if (hasSavingsPlan)
        {
            var answer = MessageBox.Show(
                this,
                $"Deleting \"{label}\" will also remove its savings plan.\n\nContinue?",
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
    }

    // planning/14 item D-1. An outflow with no savings plan has its jar filled
    // by the standing automatic rule; this hands that jar over to a plan the
    // user owns. Seeded from what the jar already holds, so pressing it moves
    // no money — it only changes what governs the jar from here on.
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

        var window = new CreateEarMarkPatternWindow(row.Pattern, CurrentJarAmount(row.FinanceId)) { Owner = this };
        if (window.ShowDialog() == true && window.CreatedPattern is { } pattern)
        {
            _earMarkPatterns.Save(pattern);
            RefreshGrids();
            if (_lastForecast is { } shown)
            {
                RefreshForecast(shown.AsOfDate, shown.HorizonEndDate);
            }
        }
    }

    // What this jar holds as of the forecast's own start date, so a new savings
    // plan can pick up exactly where the automatic filling left off. Zero when
    // there is no forecast on screen yet, or the jar doesn't exist in it.
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

        var window = new CreateEarMarkPatternWindow(goals) { Owner = this };
        if (window.ShowDialog() == true && window.CreatedPattern is { } pattern)
        {
            _earMarkPatterns.Save(pattern);
            RefreshGrids();
        }
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

        var window = new CreateEarMarkPatternWindow(row.Pattern, goal) { Owner = this };
        if (window.ShowDialog() == true && window.CreatedPattern is { } updated)
        {
            _earMarkPatterns.Save(updated);
            RefreshGrids();
        }
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
            // The goal (a finance pattern) is filed under the chosen account; its
            // earmark reaches the same account through finance_id (item 2-A).
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
