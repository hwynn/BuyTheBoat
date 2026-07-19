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

    public MainWindow()
    {
        InitializeComponent();

        var database = new PatternDatabase();
        _financialPatterns = new FinancialPatternRepository(database);
        _earMarkPatterns = new EarMarkPatternRepository(database, _financialPatterns);
        _currentBalance = new CurrentBalanceRepository(database);
        _manualEarmarks = new ManualEarmarkRepository(database, _earMarkPatterns);

        RefreshGrids();
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

        CurrentBalanceTextBox.Text = saved.Balance.ToString(CultureInfo.InvariantCulture);
        AsOfDatePicker.SelectedDate = saved.AsOfDate.ToDateTime(TimeOnly.MinValue);
        HorizonEndDatePicker.SelectedDate = saved.HorizonEndDate.ToDateTime(TimeOnly.MinValue);
        SafetyCushionTextBox.Text = saved.IdealSafetyCushion.ToString(CultureInfo.InvariantCulture);
        RefreshForecast(saved.Balance, saved.AsOfDate, saved.HorizonEndDate, saved.IdealSafetyCushion);
    }

    private void OnForecastClick(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(CurrentBalanceTextBox.Text, out var balance))
        {
            MessageBox.Show(this, "Enter a valid balance.", "Invalid balance", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

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

        // Blank cushion = 0 (off); a non-empty, unparseable, or negative value
        // is a mistake worth flagging rather than silently zeroing.
        var idealSafetyCushion = 0m;
        if (!string.IsNullOrWhiteSpace(SafetyCushionTextBox.Text)
            && !decimal.TryParse(SafetyCushionTextBox.Text, out idealSafetyCushion))
        {
            MessageBox.Show(this, "Enter a valid safety cushion, or leave it blank.", "Invalid cushion", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (idealSafetyCushion < 0m)
        {
            MessageBox.Show(this, "Safety cushion can't be negative.", "Invalid cushion", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _currentBalance.Save(balance, asOfDate, horizonEndDate, idealSafetyCushion);
        RefreshForecast(balance, asOfDate, horizonEndDate, idealSafetyCushion);
    }

    // No upper bound on the horizon by design — years out is a legitimate
    // request (long-term goals, mortgage-length planning), so this is left to
    // whatever the user picks rather than an app-imposed ceiling. The calendar
    // stays cheap at that scale because the outer ListBox virtualizes months.
    private void RefreshForecast(decimal balance, DateOnly asOfDate, DateOnly horizonEndDate, decimal idealSafetyCushion)
    {
        var forecast = TransactionLogBookFactory.CreateForecast(new ForecastOptions
        {
            FinancialPatterns = _financialPatterns.GetAll(),
            EarMarkPatterns = _earMarkPatterns.GetAll(),
            ManualEarmarks = _manualEarmarks.GetAll(),
            StartingBalance = balance,
            AsOfDate = asOfDate,
            HorizonEndDate = horizonEndDate,
            IdealSafetyCushion = idealSafetyCushion,
        });

        _lastForecast = forecast;
        _shownBalance = balance;
        _shownCushion = idealSafetyCushion;

        var months = BuildCalendarMonths(forecast);
        _dayCellsByDate = months
            .SelectMany(month => month.Cells)
            .Where(cell => cell.HasSnapshot)
            .ToDictionary(cell => cell.Date!.Value);
        _selectedDayCell = null;
        TimelineCalendar.ItemsSource = months;
        ExportForecastSpreadsheetButton.IsEnabled = true;

        // Pre-select the as-of cell so the detail pane is never blank after a
        // forecast — it's the "what's my situation right now" view.
        if (_dayCellsByDate.TryGetValue(forecast.AsOfDate, out var asOfCell))
        {
            SelectDay(asOfCell);
        }

        // Land the viewport on today's month when today is in range (§2.g);
        // otherwise start at the top. Deferred to Loaded priority because the
        // virtualizing panel hasn't measured at the moment ItemsSource is set.
        var today = DateOnly.FromDateTime(DateTime.Today);
        var targetMonth = today >= forecast.AsOfDate && today <= forecast.HorizonEndDate
            ? months.FirstOrDefault(month => month.MonthAutomationId == $"Month_{today:yyyy-MM}") ?? months[0]
            : months[0];
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => TimelineCalendar.ScrollIntoView(targetMonth));

        UpdateForecastButtonState();
    }

    // Every calendar day from the as-of month's first day through the horizon
    // month's last: days with a BalanceSnapshot are live cells; event-less and
    // out-of-range days render faint (every day stays visible — §2.I.d).
    // GetTimeline() already folds the dateless initial snapshot in under the
    // as-of date, so keying by date is collision-free.
    private static List<MonthRow> BuildCalendarMonths(ForecastResult forecast)
    {
        var snapshotsByDate = forecast.GetTimeline()
            .ToDictionary(entry => entry.Date, entry => entry.Snapshot);
        var flooredDates = forecast.FlooredManualEarmarks
            .Select(floored => floored.Date)
            .ToHashSet();

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
                cells.Add(new DayCellRow(date, snapshotsByDate.GetValueOrDefault(date), flooredDates.Contains(date)));
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
        ShowDayDetail(cell);
    }

    // The detail pane is the inner layer of the display onion (§3): the cell
    // IS a BalanceSnapshot, and selecting it shows everything the day holds —
    // every event ("what happened today", left) and every fund jar with its
    // per-type health (right), plus the day's free amount.
    private void ShowDayDetail(DayCellRow cell)
    {
        if (_lastForecast is not { } forecast || cell.Snapshot is not { } snapshot || cell.Date is not { } date)
        {
            return;
        }

        var page = forecast.PrimaryAccountPage;
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

        AdjustFundsButton.IsEnabled = page.EarmarkPatterns.Count > 0;
        DayDetailHeader.Text = $"Selected day — {date:D}";
        DeallocationDayChip.Visibility = snapshot.IsDeallocationDay ? Visibility.Visible : Visibility.Collapsed;

        var free = snapshot.ExpectedFreeAmount ?? 0m;
        FreeToSpendText.Text = free.ToString("C");
        FreeToSpendText.Foreground = free < 0m
            ? (Brush)FindResource("RedTextBrush")
            : Brushes.Black;

        DayEventsList.ItemsSource = DayEventRow.From(snapshot, context);
        JarDetailList.ItemsSource = snapshot.FundJars
            .Select(jar => JarDetailRow.From(jar, snapshot, context))
            .ToList();
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

        var balanceMatches = decimal.TryParse(CurrentBalanceTextBox.Text, out var balance)
            && balance == _shownBalance;

        var cushionText = SafetyCushionTextBox.Text;
        var cushionMatches = string.IsNullOrWhiteSpace(cushionText)
            ? _shownCushion == 0m
            : decimal.TryParse(cushionText, out var cushion) && cushion == _shownCushion;

        ForecastButton.IsEnabled = !(datesMatch && balanceMatches && cushionMatches);
    }

    // Re-runs the forecast with the inputs it's already showing — for when
    // data that feeds it (manual earmarks) changed rather than the inputs —
    // keeping the same selected day when it still exists.
    private void RefreshShownForecast()
    {
        if (_lastForecast is not { } shown || _shownBalance is not { } balance || _shownCushion is not { } cushion)
        {
            return;
        }

        var selectedDate = _selectedDayCell?.Date;
        RefreshForecast(balance, shown.AsOfDate, shown.HorizonEndDate, cushion);
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

        FinancialPatternsGrid.ItemsSource = financialPatterns
            .Select(pattern => new FinancialPatternRow(pattern))
            .ToList();

        var explicitRows = earMarkPatterns
            .Select(pattern => new EarMarkPatternRow(
                pattern,
                financialPatterns.FirstOrDefault(goal => goal.FinanceId == pattern.FinanceId)))
            .Cast<object>();

        // Every mandatory bill without an explicit earmark already gets an
        // automatic reservation baked into the forecast's numbers (see
        // TransactionLogBookFactory) — surfaced here too, read-only, so that
        // mechanism isn't invisible. Appended after the real rows.
        var automaticRows = TransactionLogBookFactory.GetAutomaticallyEarmarkedBills(financialPatterns, earMarkPatterns)
            .Select(bill => new AutomaticBillEarmarkRow(bill))
            .Cast<object>();

        EarMarkPatternsGrid.ItemsSource = explicitRows.Concat(automaticRows).ToList();

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
        var window = new CreateFinancialPatternWindow(_financialPatterns.GetAll()) { Owner = this };
        if (window.ShowDialog() == true && window.CreatedPattern is { } pattern)
        {
            _financialPatterns.Save(pattern);
            RefreshGrids();
        }
    }

    private void OnCreateBillClick(object sender, RoutedEventArgs e)
    {
        var window = new CreateFinancialPatternWindow(_financialPatterns.GetAll(), forcedMandatory: true) { Owner = this };
        if (window.ShowDialog() == true && window.CreatedPattern is { } pattern)
        {
            _financialPatterns.Save(pattern);
            RefreshGrids();
        }
    }

    private void OnEditFinancialPatternClick(object sender, RoutedEventArgs e)
    {
        if (FinancialPatternsGrid.SelectedItem is not FinancialPatternRow row)
        {
            ShowNothingSelected();
            return;
        }

        var window = new CreateFinancialPatternWindow(row.Pattern) { Owner = this };
        if (window.ShowDialog() == true && window.CreatedPattern is { } updated)
        {
            _financialPatterns.Save(updated);
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

        if (_financialPatterns.HasLinkedEarMarkPattern(row.FinanceId))
        {
            MessageBox.Show(
                this,
                "This has a linked savings goal (earmark pattern). Delete that first, on the Savings Goals tab.",
                "Can't delete yet",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!ConfirmDelete(row.Description is { Length: > 0 } description ? description : row.Source))
        {
            return;
        }

        _financialPatterns.Delete(row.FinanceId);
        RefreshGrids();
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
        var window = new CreateOneTimeGoalWindow(_financialPatterns.GetAll()) { Owner = this };
        if (window.ShowDialog() == true && window.CreatedGoal is { } goal && window.CreatedEarMarkPattern is { } earmark)
        {
            _financialPatterns.Save(goal);
            _earMarkPatterns.Save(earmark);
            RefreshGrids();
        }
    }

    private void ShowNothingSelected() =>
        MessageBox.Show(this, "Select a row first.", "Nothing selected", MessageBoxButton.OK, MessageBoxImage.Information);

    private void ShowAutomaticRowIsNotManageable() => MessageBox.Show(
        this,
        "This is an automatic reservation for a mandatory bill, not a real earmark pattern — there's nothing to edit or delete here. " +
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
