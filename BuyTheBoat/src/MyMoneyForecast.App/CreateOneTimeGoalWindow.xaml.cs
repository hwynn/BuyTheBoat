using System.Windows;
using System.Windows.Controls;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// The simplified path for the common case: a single future expense. Asks
// only what mini_fund_project's "one time goals" sheet asked for — the user
// never sees or picks an "allocation trigger" at all. All the actual
// goal+earmark construction logic lives in OneTimeGoalFactory (Domain),
// where it's unit-tested; this is just form plumbing.
public partial class CreateOneTimeGoalWindow : Window
{
    private readonly IReadOnlyList<FinancialPattern> _existingPatterns;

    public FinancialPattern? CreatedGoal { get; private set; }
    public EarMarkPattern? CreatedEarMarkPattern { get; private set; }

    public CreateOneTimeGoalWindow(IReadOnlyList<FinancialPattern> existingPatterns, IReadOnlyList<Account> accounts)
    {
        InitializeComponent();
        _existingPatterns = existingPatterns;

        AccountComboBox.ItemsSource = accounts;
        AccountComboBox.SelectedValue = accounts.FirstOrDefault()?.Id;
        if (AccountComboBox.SelectedItem is null && accounts.Count > 0)
        {
            AccountComboBox.SelectedIndex = 0;
        }

        DueDatePicker.SelectedDate = DateTime.Today.AddMonths(6);
        StartDatePicker.SelectedDate = DateTime.Today;
    }

    // Which account this goal's savings sit in — always a real selection.
    public int SelectedAccountId => (int)AccountComboBox.SelectedValue;

    private void OnStartTodayChanged(object sender, RoutedEventArgs e)
    {
        StartDatePicker.IsEnabled = StartTodayCheckBox.IsChecked != true;
        if (StartTodayCheckBox.IsChecked == true)
        {
            StartDatePicker.SelectedDate = DateTime.Today;
        }
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(DescriptionTextBox.Text))
            {
                throw new InvalidOperationException("Enter what you're saving for.");
            }

            if (!decimal.TryParse(AmountNeededTextBox.Text, out var amountNeeded) || amountNeeded <= 0)
            {
                throw new InvalidOperationException("Enter how much you need as a positive number.");
            }

            if (DueDatePicker.SelectedDate is not { } dueDateTime)
            {
                throw new InvalidOperationException("Pick the date you need this by.");
            }

            var nextId = _existingPatterns.Count == 0 ? 1 : _existingPatterns.Max(pattern => pattern.FinanceId) + 1;

            var result = OneTimeGoalFactory.Create(new OneTimeGoalRequest
            {
                FinanceId = nextId,
                Description = DescriptionTextBox.Text,
                AmountNeeded = amountNeeded,
                DueDate = DateOnly.FromDateTime(dueDateTime),
                StartSavingDate = ReadStartDate(),
                Priority = int.Parse((string)((ComboBoxItem)PriorityComboBox.SelectedItem).Tag),
                Frequency = ReadFrequency(),
            });

            CreatedGoal = result.Goal;
            CreatedEarMarkPattern = result.SavingsPlan;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ErrorLog.RecordAndDescribe("creating this goal", ex);
        }
    }

    private DateOnly ReadStartDate()
    {
        if (StartTodayCheckBox.IsChecked == true)
        {
            return DateOnly.FromDateTime(DateTime.Today);
        }

        return StartDatePicker.SelectedDate is { } startDateTime
            ? DateOnly.FromDateTime(startDateTime)
            : throw new InvalidOperationException("Pick when you want to start saving, or check \"Start today.\"");
    }

    private SavingsFrequency ReadFrequency()
    {
        var selected = (string)((ComboBoxItem)FrequencyComboBox.SelectedItem).Content;
        return selected switch
        {
            "Weekly" => SavingsFrequency.Weekly,
            "Every other week" => SavingsFrequency.EveryOtherWeek,
            _ => SavingsFrequency.Monthly,
        };
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
