using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// The permanent Account tab, replacing the old AccountWindow popup. Fields,
// captions and validation ported verbatim, including the name-uniqueness
// check MainWindow used to run after ShowDialog() — moved in here so a
// clash shows inline instead of a MessageBox, matching every other
// permanent-tab form. AccountRepository's own UNIQUE column is still the
// backstop (case-sensitive — no COLLATE NOCASE on that column — matched
// here with StringComparison.Ordinal), not relied on for the user-facing
// message.
//
// IsPopulated means an existing account is loaded — this form has no
// separate instance-picker, but the same "editing an existing thing" signal
// applies. IsDirty/IsPopulated back MainWindow's tab-header styling and
// this form's own Save button IsEnabled.
//
// TODO: two contextual helper regions this panel doesn't have yet —
// "cushion currently held vs. target" (needs a live forecast read, editing
// only) and "Referenced by: N Expenses, M Transfers" (editing only). Both
// confirmed helpful, neither built; the plain three-field form below still
// functions without them.
public partial class AccountFormPanel : UserControl
{
    private IReadOnlyList<Account> _existingAccounts = [];
    private Account? _loadedExisting;
    private int _id;
    private bool _suppressEvents;
    private bool _isDirty;

    // Guards against TextChanged firing while still under construction — no
    // field currently has an XAML default value that would trigger this, but
    // matching the same defensive convention every other panel uses costs
    // nothing and heads off the exact bug class two of them already hit.
    private bool _initialized;

    public AccountFormPanel()
    {
        InitializeComponent();

        NameTextBox.TextChanged += (_, _) => MarkDirtyIfNotSuppressed();
        BalanceTextBox.TextChanged += (_, _) => MarkDirtyIfNotSuppressed();
        CushionTextBox.TextChanged += (_, _) => MarkDirtyIfNotSuppressed();

        _initialized = true;
    }

    // MainWindow persists whatever comes back through this — this panel owns
    // no repository itself, matching EarmarkFormPanel.
    public Action<Account>? AccountSaved { get; set; }

    /// <summary>[UI] Fires whenever IsDirty or IsPopulated could have changed, so MainWindow can restyle this form's tab header live.</summary>
    public event EventHandler? StateChanged;

    public bool IsDirty => _isDirty;

    /// <summary>[CALC] Whether an existing account is currently loaded — this form's version of "an instance is loaded," since it has no separate instance-picker.</summary>
    public bool IsPopulated => _loadedExisting is not null;

    /// <summary>[CALC] Whether this form's own Advanced mode checkbox is on. Not wired to anything yet — Account has no content that Advanced mode currently reveals.</summary>
    public bool IsAdvancedMode => AdvancedModeCheckBox.IsChecked == true;

    /// <summary>[UI] Supplies the existing accounts this panel reads from. Call before any Load* method, and again after every save so the next new-account id and uniqueness check see current data.</summary>
    /// <param name="existingAccounts">Every account currently saved.</param>
    public void SetContext(IReadOnlyList<Account> existingAccounts) => _existingAccounts = existingAccounts;

    /// <summary>[STEP] Blank form for "Add Account..." — the next id is computed the same way AccountRepository.NextId() does (max existing + 1).</summary>
    public void LoadForNew()
    {
        _suppressEvents = true;
        _loadedExisting = null;
        _id = _existingAccounts.Select(account => account.Id).DefaultIfEmpty(0).Max() + 1;
        NameTextBox.Text = string.Empty;
        BalanceTextBox.Text = "0";
        CushionTextBox.Text = string.Empty;
        ErrorText.Text = string.Empty;
        _suppressEvents = false;
        ClearDirty();
    }

    /// <summary>[STEP] Loads an existing account for editing — Id is fixed, same as the old edit-mode constructor.</summary>
    /// <param name="existing">The account to load for editing.</param>
    public void LoadExisting(Account existing)
    {
        _suppressEvents = true;
        _loadedExisting = existing;
        _id = existing.Id;
        NameTextBox.Text = existing.Name;
        BalanceTextBox.Text = existing.Balance.ToString(CultureInfo.InvariantCulture);

        // A cushion of 0 means "none set", so show it blank — otherwise editing
        // implies the user deliberately chose a zero cushion (philosophy 2).
        CushionTextBox.Text = existing.IdealSafetyCushion == 0m
            ? string.Empty
            : existing.IdealSafetyCushion.ToString(CultureInfo.InvariantCulture);
        ErrorText.Text = string.Empty;
        _suppressEvents = false;
        ClearDirty();
    }

    private void OnClearClick(object sender, RoutedEventArgs e) => LoadForNew();

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var name = NameTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorText.Text = "Give the account a name.";
            return;
        }

        if (!decimal.TryParse(BalanceTextBox.Text, out var balance))
        {
            ErrorText.Text = "Enter a valid current balance.";
            return;
        }

        // Blank means "no cushion", which is different from a typo worth flagging.
        var cushion = 0m;
        if (!string.IsNullOrWhiteSpace(CushionTextBox.Text)
            && !decimal.TryParse(CushionTextBox.Text, out cushion))
        {
            ErrorText.Text = "Enter a valid safety cushion, or leave it blank.";
            return;
        }

        if (cushion < 0m)
        {
            ErrorText.Text = "Safety cushion can't be negative.";
            return;
        }

        // Ported from MainWindow's old post-ShowDialog check — a rename
        // must not collide with a different account's name either.
        if (_existingAccounts.Any(account => account.Id != _id && string.Equals(account.Name, name, StringComparison.Ordinal)))
        {
            ErrorText.Text = $"There's already an account called \"{name}\".";
            return;
        }

        var account = Account.Create(new AccountOptions
        {
            Id = _id,
            Name = name,
            Balance = balance,
            IdealSafetyCushion = cushion,
        });

        // The form clears itself automatically after a successful save,
        // before invoking the callback, unconditional even though the
        // callback's own navigation takes the user elsewhere.
        LoadForNew();
        AccountSaved?.Invoke(account);
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
