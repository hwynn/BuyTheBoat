using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

/// <summary>
/// The values a Transfer save needs, handed to MainWindow, which assigns ids, builds the two patterns (TransferFactory) and the withdrawal's plan, and saves; the same split as the other forms.
/// EditingTransferId is null for a new transfer, or the transfer's id when editing, so MainWindow rewrites its legs in place.
/// </summary>
public sealed record TransferFormInputs(int? EditingTransferId, int FromAccountId, int ToAccountId, decimal Amount, RecurrenceRule DatePattern, bool AutoRenew);

/// <summary>
/// The Transfer tab, for creating a new transfer or editing one picked on the Transfers list ("Edit Selected...", via LoadForEdit).
/// The selected-day "cover from another account" button opens it pre-filled (LoadForShortfall).
/// There's no instance picker, so the tab header shows the bold "editing" weight only while a transfer is loaded (IsPopulated).
/// </summary>
public partial class TransferFormPanel : UserControl
{
    private IReadOnlyList<Account> _accounts = [];
    private bool _suppressEvents;
    private bool _isDirty;
    private bool _initialized;

    // The transfer currently being edited, or null when the form is on a new
    // transfer. Drives IsPopulated and tells MainWindow which transfer to rewrite.
    private int? _editingTransferId;

    public TransferFormPanel()
    {
        InitializeComponent();

        FromAccountComboBox.SelectionChanged += (_, _) => OnFieldChanged();
        ToAccountComboBox.SelectionChanged += (_, _) => OnFieldChanged();
        AmountTextBox.TextChanged += (_, _) => OnFieldChanged();
        RuleEditor.ResultChanged += (_, _) => OnFieldChanged();

        _initialized = true;
    }

    // MainWindow persists whatever comes back through this — this panel owns no
    // repository, matching every other form panel.
    public Action<TransferFormInputs>? TransferSaved { get; set; }

    // Wired to MainWindow.EnsureForecast — only used to read the current horizon,
    // so a "keeps going" transfer's end date can be set to horizon+cycle.
    public Func<ForecastResult>? RequestForecast { get; set; }

    /// <summary>[UI] Fires whenever IsDirty could have changed, so MainWindow can restyle this form's tab header live.</summary>
    public event EventHandler? StateChanged;

    public bool IsDirty => _isDirty;

    /// <summary>[CALC] True while an existing transfer is loaded for editing — lets MainWindow style this tab header with the bold "editing" weight, the same UpdateTabHeaderStyle call the other forms use.</summary>
    public bool IsPopulated => _editingTransferId is not null;

    /// <summary>[CALC] Id of the existing transfer open in this form, or null for a new one — lets the Transfers list mark that row.</summary>
    public int? EditingTransferId => _editingTransferId;

    /// <summary>[UI] Supplies the accounts for the From and To pickers, keeping the current picks, since it runs on every tab switch and must never discard a transfer in progress.</summary>
    /// <param name="accounts">Every account currently saved.</param>
    public void SetContext(IReadOnlyList<Account> accounts)
    {
        _accounts = accounts;

        _suppressEvents = true;
        var previousFrom = FromAccountComboBox.SelectedValue;
        var previousTo = ToAccountComboBox.SelectedValue;
        FromAccountComboBox.ItemsSource = accounts;
        ToAccountComboBox.ItemsSource = accounts;
        FromAccountComboBox.SelectedValue = previousFrom;
        ToAccountComboBox.SelectedValue = previousTo;
        _suppressEvents = false;

        UpdateAvailability();
        UpdateSummary();
    }

    /// <summary>[STEP] Blank form for a new transfer, with two different accounts and a plain monthly schedule so it's valid on open; Amount starts blank, so Save stays disabled until one is entered.</summary>
    public void LoadForNew()
    {
        _suppressEvents = true;
        _editingTransferId = null;
        SelectTwoDifferentAccounts(preselectToAccountId: null);
        AmountTextBox.Text = string.Empty;
        KeepsGoingCheckBox.IsChecked = false;
        RuleEditor.LetSelfControlEndDate();
        KeepsGoingNote.Visibility = Visibility.Collapsed;
        RuleEditor.LoadFrom(DefaultMonthlySchedule());
        ErrorText.Text = string.Empty;
        _suppressEvents = false;

        ClearDirty();
        UpdateAvailability();
        UpdateSummary();
    }

    /// <summary>
    /// [STEP] Pre-fills the form to cover one short account on one day, from the selected-day "Cover from another account" button: To is the short account, for the amount it's short, once on that day.
    /// Left dirty so it's one click to Save; the user still confirms, so no money moves on its own.
    /// </summary>
    /// <param name="toAccountId">The short account to deposit into.</param>
    /// <param name="amount">The amount it's short.</param>
    /// <param name="date">The day to schedule the one-off transfer on.</param>
    public void LoadForShortfall(int toAccountId, decimal amount, DateOnly date)
    {
        _suppressEvents = true;
        _editingTransferId = null;
        SelectTwoDifferentAccounts(preselectToAccountId: toAccountId);
        AmountTextBox.Text = amount.ToString(CultureInfo.InvariantCulture);
        // Covering a short day is a one-off move, never an ongoing transfer.
        KeepsGoingCheckBox.IsChecked = false;
        RuleEditor.LetSelfControlEndDate();
        KeepsGoingNote.Visibility = Visibility.Collapsed;
        RuleEditor.LoadFrom(RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Yearly,
            DtStart = date,
            Count = 1,
        }));
        ErrorText.Text = string.Empty;
        _suppressEvents = false;

        // Pre-filled and ready to submit, unlike a blank new form — enable Save.
        MarkDirty();
        UpdateAvailability();
        UpdateSummary();
    }

    /// <summary>
    /// [STEP] Loads a saved transfer to edit, remembering its id so saving rewrites it in place rather than creating another.
    /// Opens clean, so Save stays disabled until something changes.
    /// </summary>
    /// <param name="transfer">The transfer to edit.</param>
    /// <param name="autoRenew">Whether this transfer "keeps going" — read off its withdrawal leg, since the Transfer record itself doesn't carry it.</param>
    public void LoadForEdit(Transfer transfer, bool autoRenew)
    {
        _suppressEvents = true;
        _editingTransferId = transfer.Id;
        FromAccountComboBox.SelectedValue = transfer.FromAccountId;
        ToAccountComboBox.SelectedValue = transfer.ToAccountId;
        AmountTextBox.Text = transfer.Amount.ToString(CultureInfo.InvariantCulture);
        KeepsGoingCheckBox.IsChecked = autoRenew;
        RuleEditor.LoadFrom(transfer.DatePattern);
        // Reconciles the "Ends" controls to the toggle: a keeps-going transfer's
        // end date is host-driven (horizon + a cycle), a plain one owns its own.
        ApplyKeepsGoingMode();
        ErrorText.Text = string.Empty;
        _suppressEvents = false;

        ClearDirty();
        UpdateAvailability();
        UpdateSummary();
    }

    private void OnClearClick(object sender, RoutedEventArgs e) => LoadForNew();

    private void OnKeepsGoingChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _suppressEvents)
        {
            return;
        }

        ApplyKeepsGoingMode();
        MarkDirty();
        UpdateSummary();
    }

    /// <summary>[UI] Reflects the "keeps going" toggle: when on, the schedule's own "Ends" controls step aside and the end date becomes the forecast horizon plus a cycle; when off, the schedule controls its own end date again.</summary>
    private void ApplyKeepsGoingMode()
    {
        var keepsGoing = KeepsGoingCheckBox.IsChecked == true;
        KeepsGoingNote.Visibility = keepsGoing ? Visibility.Visible : Visibility.Collapsed;
        if (!keepsGoing)
        {
            RuleEditor.LetSelfControlEndDate();
            return;
        }

        RuleEditor.LetHostControlEndDate();
        var horizon = RequestForecast?.Invoke().HorizonEndDate ?? DateOnly.FromDateTime(DateTime.Today).AddYears(3);
        try
        {
            var schedule = RuleEditor.ReadScheduleParts();
            RuleEditor.SetHostEndDate(AddOneCycle(horizon, schedule.Frequency, schedule.Interval));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            // Schedule not fully typed yet — a month past the horizon is a safe
            // over-estimate; this re-runs when the schedule changes.
            RuleEditor.SetHostEndDate(horizon.AddMonths(1));
        }
    }

    /// <summary>[CALC] One repeat-cycle past the given date — the "plus a cycle" buffer an ongoing transfer's end date carries past the horizon so its last occurrence isn't clipped at the edge.</summary>
    /// <param name="date">The date to step one cycle past (the forecast horizon).</param>
    /// <param name="frequency">The transfer's repeat frequency.</param>
    /// <param name="interval">The transfer's repeat interval.</param>
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

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        try
        {
            if (FromAccountComboBox.SelectedValue is not int fromId || ToAccountComboBox.SelectedValue is not int toId)
            {
                throw new InvalidOperationException("Pick both a From and a To account.");
            }

            if (fromId == toId)
            {
                throw new InvalidOperationException("A transfer has to move money between two different accounts.");
            }

            if (!decimal.TryParse(AmountTextBox.Text, out var amount) || amount <= 0m)
            {
                throw new InvalidOperationException("Enter the amount to move as a positive number.");
            }

            if (RuleEditor.Result is not { } schedule)
            {
                throw new InvalidOperationException("Fix the schedule before continuing.");
            }

            // With "keeps going" on, the schedule already ends at the horizon plus a cycle (ApplyKeepsGoingMode); this just sets AutoRenew for both legs.
            var inputs = new TransferFormInputs(_editingTransferId, fromId, toId, amount, schedule, KeepsGoingCheckBox.IsChecked == true);

            // Clears the form before the callback navigates away, as the other forms do.
            LoadForNew();
            TransferSaved?.Invoke(inputs);
        }
        catch (Exception ex)
        {
            ErrorText.Text = ErrorLog.RecordAndDescribe("saving this transfer", ex);
        }
    }

    /// <summary>[UI] Points To at a preselected account (or the second account by default) and From at any other, so the form opens on two different, valid accounts.</summary>
    /// <param name="preselectToAccountId">Account to preselect as the destination, or null for the default second account.</param>
    private void SelectTwoDifferentAccounts(int? preselectToAccountId)
    {
        if (_accounts.Count == 0)
        {
            return;
        }

        var toId = preselectToAccountId ?? (_accounts.Count > 1 ? _accounts[1].Id : _accounts[0].Id);
        ToAccountComboBox.SelectedValue = toId;
        FromAccountComboBox.SelectedValue = _accounts.FirstOrDefault(account => account.Id != toId)?.Id ?? _accounts[0].Id;
    }

    /// <summary>[CALC] The plain monthly-through-three-years schedule a new transfer opens on — mirrors RecurrenceRuleEditor's own construction default so a cleared form matches a freshly-opened one.</summary>
    private static RecurrenceRule DefaultMonthlySchedule()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            DtStart = today,
            Until = today.AddYears(3),
        });
    }

    /// <summary>[UI] Disables the fields and Save (and shows a note) while fewer than two accounts exist — a transfer needs two, and the form is a permanent tab that can't just refuse to open the way the old popup did.</summary>
    private void UpdateAvailability()
    {
        var hasTwoAccounts = _accounts.Count >= 2;
        NeedsTwoAccountsNote.Visibility = hasTwoAccounts ? Visibility.Collapsed : Visibility.Visible;
        FromAccountComboBox.IsEnabled = hasTwoAccounts;
        ToAccountComboBox.IsEnabled = hasTwoAccounts;
        AmountTextBox.IsEnabled = hasTwoAccounts;
        RuleEditor.IsEnabled = hasTwoAccounts;
        if (!hasTwoAccounts)
        {
            SaveButton.IsEnabled = false;
        }
    }

    /// <summary>[UI] Rebuilds the "Summary" helper line from whatever's currently entered — a plain confirmation of the move (amount and direction), since a reversed From/To is an easy mistake. No forecast needed.</summary>
    private void UpdateSummary()
    {
        if (_accounts.Count < 2)
        {
            SummaryText.Text = string.Empty;
            return;
        }

        var from = SelectedAccount(FromAccountComboBox);
        var to = SelectedAccount(ToAccountComboBox);
        if (from is null || to is null || from.Id == to.Id)
        {
            SummaryText.Text = "Pick two different accounts to move money between.";
            return;
        }

        if (!decimal.TryParse(AmountTextBox.Text, out var amount) || amount <= 0m)
        {
            SummaryText.Text = $"Moves money from {from.Name} to {to.Name} — enter an amount above.";
            return;
        }

        SummaryText.Text = $"Moves {amount:C0} from {from.Name} to {to.Name}, on the schedule above.";
    }

    /// <summary>[CALC] The Account currently chosen in one of the pickers, or null if none.</summary>
    /// <param name="comboBox">The From or To picker to read.</param>
    private Account? SelectedAccount(ComboBox comboBox) =>
        comboBox.SelectedValue is int id ? _accounts.FirstOrDefault(account => account.Id == id) : null;

    private void OnFieldChanged()
    {
        if (!_initialized || _suppressEvents)
        {
            return;
        }

        MarkDirty();
        UpdateSummary();
    }

    private void MarkDirty()
    {
        _isDirty = true;
        SaveButton.IsEnabled = _accounts.Count >= 2;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearDirty()
    {
        _isDirty = false;
        SaveButton.IsEnabled = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
