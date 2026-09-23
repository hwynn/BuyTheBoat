using System.Globalization;
using System.Windows;
using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

// Retired — MainWindow no longer opens this popup, having switched to the
// permanent Earmark tab (EarmarkFormPanel). No remaining
// `new CreateEarMarkPatternWindow` call sites. Kept in the tree rather than
// deleted in the same pass that orphaned it, so the change is reviewable on
// its own; safe to delete once that's confirmed.
public partial class CreateEarMarkPatternWindow : Window
{
    public EarMarkPattern? CreatedPattern { get; private set; }

    /// <summary>[STEP] Create mode: pick from any existing goal.</summary>
    /// <param name="goals">Every goal the user can save toward.</param>
    public CreateEarMarkPatternWindow(IReadOnlyList<FinancialPattern> goals)
    {
        InitializeComponent();
        WireLiveUpdates();

        GoalComboBox.ItemsSource = goals.Select(goal => new GoalOption(goal)).ToList();
        GoalComboBox.SelectedIndex = 0;
        UpdateSummary();
    }

    /// <summary>[STEP] Materialize mode: the user pressed "set up a savings plan" on an outflow whose jar has been filling automatically. The goal is fixed, and the already-saved figure is pre-filled with what that jar currently holds — so taking control never moves money, it only changes what governs the jar from here on. Once this pattern exists the outflow stops filling automatically, by the rule that already excludes anything with a savings plan.</summary>
    /// <param name="goal">The outflow this savings plan is being created for.</param>
    /// <param name="alreadySaved">What the jar already holds, pre-filled as the starting allocation.</param>
    public CreateEarMarkPatternWindow(FinancialPattern goal, decimal alreadySaved)
    {
        InitializeComponent();
        WireLiveUpdates();

        Title = "Set Up Savings Plan";

        GoalComboBox.ItemsSource = new[] { new GoalOption(goal) };
        GoalComboBox.SelectedIndex = 0;
        GoalComboBox.IsEnabled = false;

        StartingAllocationTextBox.Text = alreadySaved.ToString(CultureInfo.InvariantCulture);
        UpdateSummary();
    }

    /// <summary>[STEP] Edit mode: the goal it's linked to can't change (FinanceId is the link, and FinanceId is fixed once created) — only amount/timing can.</summary>
    /// <param name="existing">The earmark pattern being edited.</param>
    /// <param name="goal">The goal it's linked to.</param>
    public CreateEarMarkPatternWindow(EarMarkPattern existing, FinancialPattern goal)
    {
        InitializeComponent();
        WireLiveUpdates();

        Title = "Edit Savings Goal";
        CreateButton.Content = "Save";

        GoalComboBox.ItemsSource = new[] { new GoalOption(goal) };
        GoalComboBox.SelectedIndex = 0;
        GoalComboBox.IsEnabled = false;

        AmountTextBox.Text = Math.Abs(existing.Amount).ToString(CultureInfo.InvariantCulture);
        StartingAllocationTextBox.Text = existing.StartingAllocation.ToString(CultureInfo.InvariantCulture);
        RuleEditor.LoadFrom(existing.DatePattern);
        UpdateSummary();
    }

    /// <summary>[UI] Keeps the Summary preview reacting live to every field that feeds it — subscribed once per constructor, right after InitializeComponent, so every field-prefill line below it in each constructor also triggers a refresh for free.</summary>
    private void WireLiveUpdates()
    {
        GoalComboBox.SelectionChanged += (_, _) => UpdateSummary();
        AmountTextBox.TextChanged += (_, _) => UpdateSummary();
        StartingAllocationTextBox.TextChanged += (_, _) => UpdateSummary();
        RuleEditor.ResultChanged += (_, _) => UpdateSummary();
    }

    /// <summary>[UI] TODO: the narrative and chart are computed straight from this window's own raw fields (goal amount/due date, this plan's amount/schedule, starting allocation) — genuinely fine for the chart's "proposed, rough estimate" line, which was never meant to be the expensive real history. The ASIDE is a different story: it needs a live ForecastResult/PlanHealthState (today's actual jar vs. milestone) that this window doesn't have and isn't computing here — shows a plain placeholder instead of fake numbers until that plumbing exists (tracked separately, not deferred silently).</summary>
    private void UpdateSummary()
    {
        if (GoalComboBox.SelectedItem is not GoalOption selectedGoal)
        {
            return; // fires mid-InitializeComponent, or nothing picked yet
        }

        var goal = selectedGoal.Pattern;
        var goalAmount = Math.Abs(goal.Amount);
        var dueDate = goal.DatePattern.Until;
        var label = string.IsNullOrWhiteSpace(goal.Description) ? goal.Source : goal.Description;

        decimal.TryParse(AmountTextBox.Text, out var enteredAmount);
        decimal.TryParse(StartingAllocationTextBox.Text, out var startingAllocation);
        var start = RuleEditor.Result?.ActiveStart ?? DateOnly.FromDateTime(DateTime.Today);

        var narrative = enteredAmount > 0m
            ? $"We need {goalAmount:C0} for {label} by {dueDate:MMM d, yyyy}. We plan to set aside {enteredAmount:C0} per occurrence, starting {start:MMM d, yyyy}."
            : $"We need {goalAmount:C0} for {label} by {dueDate:MMM d, yyyy}.";

        Summary.Load(
            narrative,
            start: start,
            asOfDate: DateOnly.FromDateTime(DateTime.Today),
            dueDate: dueDate,
            startAmount: startingAllocation,
            goalAmount: goalAmount,
            actualTrajectory: [], // no live forecast here (see this method's own header comment) — chart shows just the goal line until that plumbing exists
            jarStateLine: "(fund jar state needs a live forecast — not wired in yet)");
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
            ErrorText.Text = ErrorLog.RecordAndDescribe("creating this savings plan", ex);
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
