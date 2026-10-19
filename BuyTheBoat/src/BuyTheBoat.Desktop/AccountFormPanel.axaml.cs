using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

/// <summary>
/// The Account tab, with validation, including the unique-name check, done here so a clash shows inline like the other forms.
/// AccountRepository's UNIQUE column is the backstop; it's case-sensitive, matched here with StringComparison.Ordinal.
/// IsPopulated means an existing account is loaded; IsDirty and IsPopulated drive MainWindow's tab-header styling and this form's Save button.
/// </summary>
public partial class AccountFormPanel : UserControl
{
    private IReadOnlyList<Account> _existingAccounts = [];
    private Account? _loadedExisting;
    private int _id;
    private bool _suppressEvents;
    private bool _isDirty;

    /// <summary>False until the constructor finishes, so TextChanged during construction is ignored, as in the other panels.</summary>
    private bool _initialized;

    public AccountFormPanel()
    {
        InitializeComponent();

        foreach (var box in new[] { NameTextBox, BalanceTextBox, CushionTextBox })
        {
            box.WhenTextChanged(MarkDirtyIfNotSuppressed);
        }

        _initialized = true;
    }

    /// <summary>Receives the saved account; MainWindow stores it, since this panel has no repository.</summary>
    public Action<Account>? AccountSaved { get; set; }

    /// <summary>[UI] Fires whenever IsDirty or IsPopulated could have changed, so MainWindow can restyle this form's tab header live.</summary>
    public event EventHandler? StateChanged;

    public bool IsDirty => _isDirty;

    /// <summary>[CALC] Whether an existing account is currently loaded — this form's version of "an instance is loaded," since it has no separate instance-picker.</summary>
    public bool IsPopulated => _loadedExisting is not null;

    /// <summary>[CALC] Id of the existing account open in this form, or null for a new one — lets the Accounts list mark that row.</summary>
    public int? LoadedAccountId => _loadedExisting?.Id;

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

    /// <summary>[STEP] Loads an existing account for editing; its Id can't change.</summary>
    /// <param name="existing">The account to load for editing.</param>
    public void LoadExisting(Account existing)
    {
        _suppressEvents = true;
        _loadedExisting = existing;
        _id = existing.Id;
        NameTextBox.Text = existing.Name;
        BalanceTextBox.Text = existing.Balance.ToString(CultureInfo.InvariantCulture);

        // A cushion of 0 means none was set, so it shows blank rather than as a chosen zero.
        CushionTextBox.Text = existing.IdealSafetyCushion == 0m
            ? string.Empty
            : existing.IdealSafetyCushion.ToString(CultureInfo.InvariantCulture);
        ErrorText.Text = string.Empty;
        _suppressEvents = false;
        ClearDirty();
    }

    /// <summary>[UI] Empties the form for a new account.</summary>
    private void OnClearClick(object? sender, RoutedEventArgs e) => LoadForNew();

    /// <summary>[UI] Checks the typed account and hands it to MainWindow to save, or shows what's wrong below the form.</summary>
    private void OnSaveClick(object? sender, RoutedEventArgs e)
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

        // A rename must not collide with a different account's name either.
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

        // Clears the form before the callback, which may navigate elsewhere.
        LoadForNew();
        AccountSaved?.Invoke(account);
    }

    /// <summary>[UI] Marks the form as having unsaved changes and enables Save.</summary>
    private void MarkDirty()
    {
        _isDirty = true;
        SaveButton.IsEnabled = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>[UI] Marks the form as matching what's saved and disables Save.</summary>
    private void ClearDirty()
    {
        _isDirty = false;
        SaveButton.IsEnabled = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>[UI] Marks the form dirty for a user's edit, but not for the form filling itself in.</summary>
    private void MarkDirtyIfNotSuppressed()
    {
        if (_initialized && !_suppressEvents)
        {
            MarkDirty();
        }
    }
}
