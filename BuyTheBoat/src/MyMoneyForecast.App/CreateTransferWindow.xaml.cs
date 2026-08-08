using System.Windows;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// Schedules a transfer between two of the user's accounts. Presented as one
// action; MyMoneyForecast turns it into a Transfer record plus two paired
// withdrawal and deposit patterns — the window just collects the
// from/to/amount/schedule and hands them back.
public partial class CreateTransferWindow : Window
{
    public int FromAccountId { get; private set; }
    public int ToAccountId { get; private set; }
    public decimal Amount { get; private set; }
    public RecurrenceRule? DatePattern { get; private set; }

    /// <summary>[STEP] Assumes at least two accounts exist — the caller checks that before opening, since a transfer needs two different accounts. The preselect arguments back the selected day's "Cover from another account" lever: it opens this already pointed at the short account for exactly the amount it is short, so the fix is one confirmation away.</summary>
    /// <param name="accounts">Every account, to populate the From/To pickers.</param>
    /// <param name="preselectToAccountId">Account to preselect as the destination, if any.</param>
    /// <param name="preselectAmount">Amount to preselect, if any.</param>
    /// <param name="preselectDate">Schedule date to preselect as a one-off, if any.</param>
    public CreateTransferWindow(
        IReadOnlyList<Account> accounts,
        int? preselectToAccountId = null,
        decimal? preselectAmount = null,
        DateOnly? preselectDate = null)
    {
        InitializeComponent();

        FromAccountComboBox.ItemsSource = accounts;
        ToAccountComboBox.ItemsSource = accounts;

        // Default to two different accounts so the form is valid on open.
        var toId = preselectToAccountId ?? (accounts.Count > 1 ? accounts[1].Id : accounts[0].Id);
        ToAccountComboBox.SelectedValue = toId;
        FromAccountComboBox.SelectedValue = accounts.FirstOrDefault(account => account.Id != toId)?.Id ?? accounts[0].Id;

        if (preselectAmount is { } amount)
        {
            AmountTextBox.Text = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // Covering one short day is a one-off move, so start the schedule on
        // that day and stop there. Leaving the form's standing monthly default
        // would quietly commit the user to repeating the transfer forever.
        if (preselectDate is { } date)
        {
            RuleEditor.LoadFrom(RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                Start = date,
                Count = 1,
            }));
        }
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
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

            if (RuleEditor.Result is not { } rule)
            {
                throw new InvalidOperationException("Fix the schedule before continuing.");
            }

            FromAccountId = fromId;
            ToAccountId = toId;
            Amount = amount;
            DatePattern = rule;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
