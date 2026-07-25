using System.Globalization;
using System.Windows;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

public partial class CreateEarMarkPatternWindow : Window
{
    public EarMarkPattern? CreatedPattern { get; private set; }

    // Create mode: pick from any existing goal.
    public CreateEarMarkPatternWindow(IReadOnlyList<FinancialPattern> goals)
    {
        InitializeComponent();

        GoalComboBox.ItemsSource = goals.Select(goal => new GoalOption(goal)).ToList();
        GoalComboBox.SelectedIndex = 0;
    }

    // Materialize mode (planning/14 item D-1): the user pressed "set up a
    // savings plan" on an outflow whose jar has been filling automatically. The
    // goal is fixed, and the already-saved figure is pre-filled with what that
    // jar currently holds — so taking control never MOVES money, it only
    // changes what governs the jar from here on. Once this pattern exists the
    // outflow stops filling automatically, by the rule that already excludes
    // anything with a savings plan.
    public CreateEarMarkPatternWindow(FinancialPattern goal, decimal alreadySaved)
    {
        InitializeComponent();

        Title = "Set Up Savings Plan";

        GoalComboBox.ItemsSource = new[] { new GoalOption(goal) };
        GoalComboBox.SelectedIndex = 0;
        GoalComboBox.IsEnabled = false;

        StartingAllocationTextBox.Text = alreadySaved.ToString(CultureInfo.InvariantCulture);
    }

    // Edit mode: the goal it's linked to can't change (FinanceId is the link,
    // and FinanceId is fixed once created) — only amount/timing can.
    public CreateEarMarkPatternWindow(EarMarkPattern existing, FinancialPattern goal)
    {
        InitializeComponent();

        Title = "Edit Savings Goal";
        CreateButton.Content = "Save";

        GoalComboBox.ItemsSource = new[] { new GoalOption(goal) };
        GoalComboBox.SelectedIndex = 0;
        GoalComboBox.IsEnabled = false;

        AmountTextBox.Text = Math.Abs(existing.Amount).ToString(CultureInfo.InvariantCulture);
        StartingAllocationTextBox.Text = existing.StartingAllocation.ToString(CultureInfo.InvariantCulture);
        RuleEditor.LoadFrom(existing.DatePattern);
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        try
        {
            if (RuleEditor.Result is not { } rule)
            {
                throw new InvalidOperationException("Fix the recurrence rule before continuing.");
            }

            if (GoalComboBox.SelectedItem is not GoalOption selectedGoal)
            {
                throw new InvalidOperationException("Pick a goal to save toward.");
            }

            if (!decimal.TryParse(AmountTextBox.Text, out var enteredAmount))
            {
                throw new InvalidOperationException("Amount must be a number.");
            }

            if (!decimal.TryParse(StartingAllocationTextBox.Text, out var enteredStartingAllocation))
            {
                throw new InvalidOperationException("Already-saved amount must be a number.");
            }

            // Always an allocation — money moving from free balance into the
            // fund jar — so the field is a plain magnitude and the sign is
            // fixed here rather than typed by the user.
            var amount = -Math.Abs(enteredAmount);

            CreatedPattern = EarMarkPattern.Create(
                new EarMarkPatternOptions
                {
                    FinanceId = selectedGoal.Pattern.FinanceId,
                    DatePattern = rule,
                    Amount = amount,
                    StartingAllocation = Math.Abs(enteredStartingAllocation),
                },
                selectedGoal.Pattern);

            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed class GoalOption(FinancialPattern pattern)
    {
        public FinancialPattern Pattern { get; } = pattern;

        public string DisplayText { get; } =
            $"{(string.IsNullOrWhiteSpace(pattern.Description) ? pattern.Source : pattern.Description)} (id {pattern.FinanceId}, {pattern.Amount:C})";
    }
}
