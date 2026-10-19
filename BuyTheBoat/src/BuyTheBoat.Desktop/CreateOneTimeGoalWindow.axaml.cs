using Avalonia.Controls;
using Avalonia.Interactivity;
using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

/// <summary>
/// The simple path for the common case, a single future expense, asking only what mini_fund_project's "one time goals" sheet asked; the user never picks an "allocation trigger".
/// The goal and earmark are built by OneTimeGoalFactory (Domain); this window only gathers the fields.
/// Closes with true once the goal is built.
/// </summary>
public partial class CreateOneTimeGoalWindow : Window
{
    private readonly IReadOnlyList<FinancialPattern> _existingPatterns = [];

    public FinancialPattern? CreatedGoal { get; private set; }
    public EarMarkPattern? CreatedEarMarkPattern { get; private set; }

    /// <summary>Only for the XAML loader and previewer; the app uses the constructor that takes the patterns and accounts.</summary>
    public CreateOneTimeGoalWindow()
    {
        InitializeComponent();
    }

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

        DueDatePicker.WhenDateChanged(OnDateRangeChanged);
        StartDatePicker.WhenDateChanged(OnDateRangeChanged);
        OnDateRangeChanged();
    }

    /// <summary>The account this goal's savings sit in; always set.</summary>
    public int SelectedAccountId => (int)AccountComboBox.SelectedValue!;

    /// <summary>[UI] Keeps the "start saving" date before the "need it by" date by hiding calendar days on the wrong side of the other picker, so Create can't be rejected for it.</summary>
    private void OnDateRangeChanged()
    {
        DueDatePicker.DisplayDateStart = StartDatePicker.SelectedDate?.AddDays(1);
        StartDatePicker.DisplayDateEnd = DueDatePicker.SelectedDate?.AddDays(-1);
    }

    /// <summary>[UI] "Start today" locks the start date to today; unchecking it hands the picker back.</summary>
    private void OnStartTodayChanged(object? sender, RoutedEventArgs e)
    {
        StartDatePicker.IsEnabled = StartTodayCheckBox.IsChecked != true;
        if (StartTodayCheckBox.IsChecked == true)
        {
            StartDatePicker.SelectedDate = DateTime.Today;
        }
    }

    /// <summary>[UI] Builds the goal and its savings plan from the fields and closes, or shows what's wrong below them.</summary>
    private void OnCreateClick(object? sender, RoutedEventArgs e)
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
                Priority = int.Parse((string)((ComboBoxItem)PriorityComboBox.SelectedItem!).Tag!),
                Frequency = ReadFrequency(),
            });

            CreatedGoal = result.Goal;
            CreatedEarMarkPattern = result.SavingsPlan;
            Close(true);
        }
        catch (Exception ex)
        {
            ErrorText.Text = ErrorLog.RecordAndDescribe("creating this goal", ex);
        }
    }

    /// <summary>[CALC] The day saving starts: today when "Start today" is checked, otherwise the picked date; throws a friendly message when neither is set.</summary>
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

    /// <summary>[CALC] The saving cadence picked under Advanced; Monthly unless changed.</summary>
    private SavingsFrequency ReadFrequency()
    {
        var selected = (string?)((ComboBoxItem)FrequencyComboBox.SelectedItem!).Content;
        return selected switch
        {
            "Weekly" => SavingsFrequency.Weekly,
            "Every other week" => SavingsFrequency.EveryOtherWeek,
            _ => SavingsFrequency.Monthly,
        };
    }

    /// <summary>[UI] Closes without creating anything.</summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
