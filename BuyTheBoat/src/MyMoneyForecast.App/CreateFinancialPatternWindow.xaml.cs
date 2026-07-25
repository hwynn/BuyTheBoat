using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

public partial class CreateFinancialPatternWindow : Window
{
    // (The old _mandatoryIsExplicit / _updatingMandatoryProgrammatically pair
    // is gone with planning/14 item B-4: nothing auto-suggests an answer from
    // the amount's sign any more, so there is no suggestion to stop overriding.
    // The question is simply hidden when it doesn't apply.)

    // Guards OnDirectionChanged against firing while still under construction:
    // ExpenseRadioButton's IsChecked="True" raises Checked synchronously
    // during InitializeComponent(), before later-declared fields (AmountLabel,
    // MandatoryPanel) are assigned. Same convention as RecurrenceRuleEditor's
    // _initialized guard.
    private bool _initialized;

    // FinanceId is an internal identifier — never shown or typed by the user
    // (same reasoning as OneTimeGoalFactory's auto-assignment). Create mode
    // computes it once up front; edit mode carries the existing value through
    // unchanged.
    private readonly int _financeId;

    public FinancialPattern? CreatedPattern { get; private set; }

    // Create mode: FinanceId is auto-assigned (max existing + 1).
    // forcedMandatory, when set, hides the Mandatory checkbox and the
    // Expense/Income question entirely and fixes both — used by the "Create
    // Bill..." shortcut, where both answers are always the same and asking is
    // just friction.
    public CreateFinancialPatternWindow(IReadOnlyList<FinancialPattern> existingPatterns, IReadOnlyList<Account> accounts, bool? forcedMandatory = null)
    {
        InitializeComponent();

        _financeId = existingPatterns.Count == 0 ? 1 : existingPatterns.Max(pattern => pattern.FinanceId) + 1;
        PopulateAccounts(accounts, null);

        if (forcedMandatory is { } mandatory)
        {
            Title = mandatory ? "Create Bill" : "Create Pattern";
            MandatoryPanel.Visibility = Visibility.Collapsed;
            UnskippableRadioButton.IsChecked = mandatory;
            SkippableRadioButton.IsChecked = !mandatory;

            // A bill is unambiguously an expense — asking would just be
            // friction for an answer that's never anything else.
            DirectionPanel.Visibility = Visibility.Collapsed;
            AmountLabel.Text = "Amount owed";
        }

        _initialized = true;
    }

    // Edit mode: FinanceId is fixed — it's an internal identity, not a field
    // the user should be able to change once other data (e.g. a linked
    // EarMarkPattern) may already reference it. The existing Mandatory value
    // is treated as an explicit choice, same as if the user just typed it —
    // opening the edit window and adjusting the amount shouldn't silently
    // flip it back to the direction-based suggestion.
    public CreateFinancialPatternWindow(FinancialPattern existing, IReadOnlyList<Account> accounts, int selectedAccountId)
    {
        InitializeComponent();
        PopulateAccounts(accounts, selectedAccountId);

        Title = "Edit Bill / Paycheck";
        CreateButton.Content = "Save";

        _financeId = existing.FinanceId;
        SourceTextBox.Text = existing.Source;
        DescriptionTextBox.Text = existing.Description;
        ExpenseRadioButton.IsChecked = existing.Amount < 0;
        IncomeRadioButton.IsChecked = existing.Amount >= 0;
        AmountLabel.Text = existing.Amount < 0 ? "Amount owed" : "Amount received";
        AmountTextBox.Text = Math.Abs(existing.Amount).ToString(CultureInfo.InvariantCulture);
        PriorityTextBox.Text = existing.Priority.ToString();
        UnskippableRadioButton.IsChecked = existing.Mandatory;
        SkippableRadioButton.IsChecked = !existing.Mandatory;
        UpdateSkippableVisibility();
        RuleEditor.LoadFrom(existing.DatePattern);

        _initialized = true;
    }

    // Always populated and defaulted (never a blank/silent default) — item 2-B.
    private void PopulateAccounts(IReadOnlyList<Account> accounts, int? selectedAccountId)
    {
        AccountComboBox.ItemsSource = accounts;
        AccountComboBox.SelectedValue = selectedAccountId ?? accounts.FirstOrDefault()?.Id;
        if (AccountComboBox.SelectedItem is null && accounts.Count > 0)
        {
            AccountComboBox.SelectedIndex = 0;
        }
    }

    // Which account the pattern is filed under — always a real selection.
    public int SelectedAccountId => (int)AccountComboBox.SelectedValue;

    private void OnDirectionChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        AmountLabel.Text = ExpenseRadioButton.IsChecked == true ? "Amount owed" : "Amount received";
        UpdateSkippableVisibility();
    }

    // planning/14 item B-4: the skippable question only means something for
    // money going OUT, so it disappears for income entirely. This replaces the
    // old behaviour where a Mandatory checkbox re-ticked itself as the
    // direction changed — a question that doesn't apply is better hidden than
    // silently answered.
    private void UpdateSkippableVisibility() =>
        MandatoryPanel.Visibility = ExpenseRadioButton.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        try
        {
            if (RuleEditor.Result is not { } rule)
            {
                throw new InvalidOperationException("Fix the recurrence rule before continuing.");
            }

            if (!decimal.TryParse(AmountTextBox.Text, out var enteredAmount))
            {
                throw new InvalidOperationException("Amount must be a number.");
            }

            // The field is always a magnitude — Math.Abs guards against a
            // stray "-" typed out of habit turning into a double-negative.
            // ExpenseRadioButton defaults to checked even when the direction
            // question is hidden (Create Bill mode), so no special-casing is
            // needed there.
            var magnitude = Math.Abs(enteredAmount);
            var amount = ExpenseRadioButton.IsChecked == true ? -magnitude : magnitude;

            var priority = int.TryParse(PriorityTextBox.Text, out var parsedPriority) ? parsedPriority : 0;

            CreatedPattern = FinancialPattern.Create(new FinancialPatternOptions
            {
                FinanceId = _financeId,
                Source = SourceTextBox.Text,
                DatePattern = rule,
                Amount = amount,
                Priority = priority,
                // Income is never "unskippable" — the question is hidden for it,
                // so don't let a stale radio state leak into the saved pattern.
                Mandatory = ExpenseRadioButton.IsChecked == true && UnskippableRadioButton.IsChecked == true,
                Description = string.IsNullOrWhiteSpace(DescriptionTextBox.Text) ? null : DescriptionTextBox.Text,
            });

            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
