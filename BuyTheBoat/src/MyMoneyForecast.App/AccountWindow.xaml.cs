using System.Globalization;
using System.Windows;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// Add or edit a single account (identity, per-account balance, per-account
// cushion).
//
// Retired — MainWindow no longer opens this popup, having switched to the
// permanent Account tab (AccountFormPanel). No remaining `new AccountWindow`
// call sites. Kept in the tree rather than deleted in the same pass that
// orphaned it, so the change is reviewable on its own; safe to delete once
// that's confirmed.
public partial class AccountWindow : Window
{
    private readonly int _id;

    // The account the user confirmed, or null if they cancelled out.
    public Account? Result { get; private set; }

    public AccountWindow(int id, Account? existing = null)
    {
        InitializeComponent();
        _id = id;

        if (existing is null)
        {
            Title = "Add Account";
            BalanceTextBox.Text = "0";
            return;
        }

        Title = "Edit Account";
        NameTextBox.Text = existing.Name;
        BalanceTextBox.Text = existing.Balance.ToString(CultureInfo.InvariantCulture);

        // A cushion of 0 means "none set", so show it blank — otherwise editing
        // implies the user deliberately chose a zero cushion (philosophy 2).
        CushionTextBox.Text = existing.IdealSafetyCushion == 0m
            ? string.Empty
            : existing.IdealSafetyCushion.ToString(CultureInfo.InvariantCulture);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
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

        Result = Account.Create(new AccountOptions
        {
            Id = _id,
            Name = name,
            Balance = balance,
            IdealSafetyCushion = cushion,
        });

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
