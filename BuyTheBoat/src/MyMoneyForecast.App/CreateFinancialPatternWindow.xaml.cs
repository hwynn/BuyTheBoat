using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// Retired — MainWindow no longer opens this popup, having switched to the
// permanent Expense tab (ExpenseFormPanel) for all three of its old entry
// points ("Create Bill...", "Add New (advanced)...", "Edit Selected...").
// No remaining `new CreateFinancialPatternWindow` call sites. Kept in the
// tree rather than deleted in the same pass that orphaned it, so the
// change is reviewable on its own; safe to delete once that's confirmed.
public partial class CreateFinancialPatternWindow : Window
{
    // Nothing auto-suggests Mandatory from the amount's sign any more, so
    // there is no suggestion to stop overriding — the question is simply
    // hidden when it doesn't apply.

    // Guards OnDirectionChanged against firing while still under construction:
    // ExpenseRadioButton's IsChecked="True" raises Checked synchronously
    // during InitializeComponent(), before later-declared fields (AmountLabel,
    // MandatoryPanel) are assigned. Same convention as RecurrenceRuleEditor's
    // _initialized guard.
    private bool _initialized;

    // True only for the simple "Create Bill" form, which shows the "when
    // does this stop?" question and drives the schedule editor's end date.
    // False for the advanced/pattern and edit forms.
    private bool _simpleBillMode;

    // FinanceId is an internal identifier — never shown or typed by the user
    // (same reasoning as OneTimeGoalFactory's auto-assignment). Create mode
    // computes it once up front; edit mode carries the existing value through
    // unchanged.
    private readonly int _financeId;

    public FinancialPattern? CreatedPattern { get; private set; }

    /// <summary>[STEP] Create mode: FinanceId is auto-assigned (max existing + 1).</summary>
    /// <param name="existingPatterns">Every existing pattern, to compute the next free FinanceId.</param>
    /// <param name="accounts">Every account, to populate the account picker.</param>
    /// <param name="forcedMandatory">When set, hides the Mandatory checkbox and the Expense/Income question entirely and fixes both — used by the "Create Bill..." shortcut, where both answers are always the same and asking is just friction.</param>
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

            // The simple "Create Bill" form (a mandatory expense) gets the
            // "when does this stop?" question; the advanced/pattern form
            // keeps the editor's raw end controls.
            if (mandatory)
            {
                EnableStopQuestion();
            }
        }

        _initialized = true;
    }

    /// <summary>[STEP] Edit mode: FinanceId is fixed — it's an internal identity, not a field the user should be able to change once other data (e.g. a linked EarMarkPattern) may already reference it. The existing Mandatory value is treated as an explicit choice, same as if the user just typed it — opening the edit window and adjusting the amount shouldn't silently flip it back to the direction-based suggestion.</summary>
    /// <param name="existing">The pattern being edited.</param>
    /// <param name="accounts">Every account, to populate the account picker.</param>
    /// <param name="selectedAccountId">Which account the pattern is currently filed under.</param>
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

    /// <summary>[UI] Always populated and defaulted, never a blank/silent default.</summary>
    /// <param name="accounts">Every account to populate the picker with.</param>
    /// <param name="selectedAccountId">Which account to preselect; defaults to the first when null.</param>
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

    /// <summary>[UI] The skippable question only means something for money going OUT, so it disappears for income entirely — better hidden than silently answered.</summary>
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

    // The "when does this stop?" question ------------------------------

    /// <summary>[UI] Reveals the "when does this stop?" question and hands the schedule editor its end date, so the editor's own Ends controls step aside (ruling D-1).</summary>
    private void EnableStopQuestion()
    {
        _simpleBillMode = true;
        StopQuestionBox.Visibility = Visibility.Visible;
        RuleEditor.LetHostControlEndDate();
        RuleEditor.ResultChanged += OnRuleEditorResultChanged;

        // Match the editor's previous default end so the common "ends on a
        // date" case is unchanged from before this question existed.
        StopEndDatePicker.SelectedDate = DateTime.Today.AddYears(3);
        UpdateStopMode();
    }

    // These forward each relevant change to the stop-date recalculation. The
    // payoff answer depends on the owed amount, the payment (the amount field
    // above), and the schedule (owned by the editor), so all three feed in.

    /// <summary>[UI] Switches the form between the two stop-question inputs — a date picker, or the loan's payoff fields — when the user picks a different answer.</summary>
    private void OnStopModeChanged(object sender, RoutedEventArgs e)
    {
        if (_simpleBillMode)
        {
            UpdateStopMode();
        }
    }

    /// <summary>[UI] Passes a newly picked end date through to the schedule editor.</summary>
    private void OnStopEndDatePicked(object sender, SelectionChangedEventArgs e)
    {
        if (_simpleBillMode)
        {
            UpdateStopEnd();
        }
    }

    /// <summary>[UI] Recomputes the payoff readout as the amount still owed is typed.</summary>
    private void OnTotalOwedChanged(object sender, TextChangedEventArgs e)
    {
        if (_simpleBillMode)
        {
            UpdateStopEnd();
        }
    }

    /// <summary>[UI] Recomputes the payoff readout when the payment amount changes, while the "paid off" answer is selected.</summary>
    private void OnAmountChanged(object sender, TextChangedEventArgs e)
    {
        if (_simpleBillMode && StopPaidOffRadio.IsChecked == true)
        {
            UpdateStopEnd();
        }
    }

    /// <summary>[UI] Recomputes the payoff readout when the bill's schedule changes, while the "paid off" answer is selected.</summary>
    private void OnRuleEditorResultChanged(object? sender, EventArgs e)
    {
        if (_simpleBillMode && StopPaidOffRadio.IsChecked == true)
        {
            UpdateStopEnd();
        }
    }

    /// <summary>[UI] Swaps in the inputs for the chosen answer — a date picker, or the loan's owed amount — and relabels the amount field so a loan's per-payment amount can't be mistaken for its balance.</summary>
    private void UpdateStopMode()
    {
        var paidOff = StopPaidOffRadio.IsChecked == true;
        StopOnDatePanel.Visibility = paidOff ? Visibility.Collapsed : Visibility.Visible;
        StopPaidOffPanel.Visibility = paidOff ? Visibility.Visible : Visibility.Collapsed;
        AmountLabel.Text = paidOff ? "Payment amount" : "Amount owed";
        UpdateStopEnd();
    }

    /// <summary>[UI] Hands the schedule editor the end date the chosen answer implies — the picked date, or the computed loan payoff date.</summary>
    private void UpdateStopEnd()
    {
        if (StopPaidOffRadio.IsChecked == true)
        {
            UpdatePayoffEnd();
            return;
        }

        PayoffReadoutText.Text = string.Empty;
        RuleEditor.SetHostEndDate(
            StopEndDatePicker.SelectedDate is { } date ? DateOnly.FromDateTime(date) : null);
    }

    /// <summary>[UI] Works out the loan's payoff date from the owed amount, the payment, and the schedule, shows it as a floor, and hands it to the editor. The owed amount is entry-only — only the resulting date is kept (W3).</summary>
    private void UpdatePayoffEnd()
    {
        try
        {
            if (!decimal.TryParse(TotalOwedTextBox.Text, out var owed) || owed <= 0)
            {
                throw new InvalidOperationException("Enter the total still owed (a positive amount).");
            }

            if (!decimal.TryParse(AmountTextBox.Text, out var payment) || Math.Abs(payment) <= 0)
            {
                throw new InvalidOperationException("Enter the regular payment amount above.");
            }

            var schedule = RuleEditor.ReadScheduleParts();
            var estimate = PayoffEstimator.Estimate(new PayoffRequest
            {
                TotalOwed = owed,
                Payment = Math.Abs(payment),
                Frequency = schedule.Frequency,
                Start = schedule.Start,
                Interval = schedule.Interval,
                ByDay = schedule.ByDay,
                ByMonthDay = schedule.ByMonthDay,
            });

            PayoffReadoutText.Text =
                $"Paid off at least by {estimate.PayoffDate:D} — {estimate.PaymentCount} payments.";
            RuleEditor.SetHostEndDate(estimate.PayoffDate);
        }
        catch (Exception ex)
        {
            PayoffReadoutText.Text = ex.Message;
            RuleEditor.SetHostEndDate(null);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
