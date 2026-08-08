using System.Windows;
using System.Windows.Controls;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// Create/edit manual earmarks. Validation policy: withdrawals/moves
// exceeding what the fund holds on the chosen day are BLOCKED (jars never
// go below 0 — no money from nothing); adds exceeding that day's free
// balance WARN but are allowed (the plan is over-committed and
// deallocation may pull funds back from lower-priority jars — the user may
// know money is coming that the forecast doesn't). Day balances come from
// the currently-shown forecast; the domain type enforces the
// pattern-span-is-jar-lifetime rule on top.
//
// Retired — MainWindow no longer opens this popup, having switched to the
// permanent Earmark tab's One-off adjustment mode (EarmarkFormPanel, which
// ports this exact validation policy verbatim — see its own
// SaveOneOff/Merge/RequireFundsCover/WarnIfOverFree). No remaining
// `new ManualEarmarkWindow` call sites. Kept in the tree rather than
// deleted in the same pass that orphaned it, so the change is reviewable
// on its own; safe to delete once that's confirmed.
public partial class ManualEarmarkWindow : Window
{
    private sealed record JarChoice(EarMarkPattern Pattern, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly IReadOnlyList<ManualEarmark> _existing;
    private readonly ForecastResult? _forecast;
    private readonly ManualEarmark? _editTarget;

    // What the caller persists on DialogResult == true: upserts, then deletes
    // (a merge that nets to exactly zero removes that day's manual earmark).
    public IReadOnlyList<ManualEarmark> SavedEarmarks { get; private set; } = [];
    public IReadOnlyList<(int FinanceId, DateOnly Date)> DeletedEarmarks { get; private set; } = [];

    public ManualEarmarkWindow(
        IReadOnlyList<EarMarkPattern> patterns,
        IReadOnlyDictionary<int, string> labels,
        IReadOnlyList<ManualEarmark> existing,
        ForecastResult? forecast,
        DateOnly? initialDate = null,
        ManualEarmark? editTarget = null)
    {
        InitializeComponent();
        _existing = existing;
        _forecast = forecast;
        _editTarget = editTarget;

        var choices = patterns
            .Select(pattern => new JarChoice(
                pattern,
                labels.GetValueOrDefault(pattern.FinanceId, $"(finance id {pattern.FinanceId})")))
            .ToList();
        JarComboBox.ItemsSource = choices;
        TargetComboBox.ItemsSource = choices;
        if (choices.Count > 0)
        {
            JarComboBox.SelectedIndex = 0;
        }

        if (editTarget is { } edit)
        {
            // Edit mode: the fund and day identify the earmark — change those
            // by deleting and recreating. Amount/direction are what's edited.
            Title = "Edit Manual Adjustment";
            JarComboBox.SelectedItem = choices.FirstOrDefault(choice => choice.Pattern.FinanceId == edit.FinanceId);
            JarComboBox.IsEnabled = false;
            EarmarkDatePicker.SelectedDate = edit.Date.ToDateTime(TimeOnly.MinValue);
            EarmarkDatePicker.IsEnabled = false;
            ActionComboBox.SelectedIndex = edit.Amount >= 0m ? 0 : 1;
            ((ComboBoxItem)ActionComboBox.Items[2]).IsEnabled = false; // Move creates pairs, not edits
            AmountTextBox.Text = Math.Abs(edit.Amount).ToString();
        }
        else
        {
            EarmarkDatePicker.SelectedDate = (initialDate ?? DateOnly.FromDateTime(DateTime.Today))
                .ToDateTime(TimeOnly.MinValue);
        }

        UpdateLiveInfo();
    }

    private bool IsMove => ActionComboBox.SelectedIndex == 2;
    private bool IsWithdraw => ActionComboBox.SelectedIndex == 1;

    private void OnInputsChanged(object sender, RoutedEventArgs e)
    {
        if (TargetRow is null)
        {
            return; // fires while InitializeComponent is mid-parse
        }

        TargetRow.Visibility = IsMove ? Visibility.Visible : Visibility.Collapsed;
        JarLabelText.Text = IsMove ? "Move from which fund?" : "Which fund?";

        // Constrain the picker to the fund's lifetime (its pattern's span).
        if (JarComboBox.SelectedItem is JarChoice { Pattern: var pattern })
        {
            EarmarkDatePicker.DisplayDateStart = pattern.DatePattern.Start.ToDateTime(TimeOnly.MinValue);
            EarmarkDatePicker.DisplayDateEnd = pattern.DatePattern.Until.ToDateTime(TimeOnly.MinValue);
        }

        UpdateLiveInfo();
    }

    private void UpdateLiveInfo()
    {
        if (BalanceInfoText is null)
        {
            return;
        }

        if (_forecast is null || SelectedDate() is not { } date || JarComboBox.SelectedItem is not JarChoice choice)
        {
            BalanceInfoText.Text = string.Empty;
            return;
        }

        var (jarBalance, free) = BalancesOn(date, choice.Pattern.FinanceId);
        BalanceInfoText.Text =
            $"On {date:MMMM d, yyyy}: {choice.Label} holds {jarBalance:C} · free balance {free:C}. " +
            $"The fund's plan runs {choice.Pattern.DatePattern.Start:MMM d, yyyy} – {choice.Pattern.DatePattern.Until:MMM d, yyyy}.";
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        try
        {
            if (JarComboBox.SelectedItem is not JarChoice source)
            {
                throw new InvalidOperationException("Pick a fund.");
            }

            if (SelectedDate() is not { } date)
            {
                throw new InvalidOperationException("Pick a day.");
            }

            if (!decimal.TryParse(AmountTextBox.Text, out var amount) || amount <= 0m)
            {
                throw new InvalidOperationException("Enter the amount as a positive number.");
            }

            var saved = new List<ManualEarmark>();
            var deleted = new List<(int, DateOnly)>();

            if (IsMove)
            {
                if (TargetComboBox.SelectedItem is not JarChoice target || target.Pattern.FinanceId == source.Pattern.FinanceId)
                {
                    throw new InvalidOperationException("Pick a different fund to move to.");
                }

                RequireFundsCover(source, date, withdrawal: amount);
                Merge(saved, deleted, source.Pattern, date, -amount);
                Merge(saved, deleted, target.Pattern, date, amount);
            }
            else if (IsWithdraw)
            {
                RequireFundsCover(source, date, withdrawal: amount);
                Merge(saved, deleted, source.Pattern, date, -amount);
            }
            else
            {
                WarnIfOverFree(date, addition: amount);
                Merge(saved, deleted, source.Pattern, date, amount);
            }

            SavedEarmarks = saved;
            DeletedEarmarks = deleted;
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            // The user backed out of the over-commit warning — stay open.
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    /// <summary>[CALC] The documented merge rule: a second manual amount on an occupied day adds onto the existing one (edit mode replaces instead). A result of exactly zero removes the day's manual earmark.</summary>
    /// <param name="saved">Accumulates earmarks to upsert.</param>
    /// <param name="deleted">Accumulates (financeId, date) pairs to delete.</param>
    /// <param name="pattern">The earmark pattern the amount is filed under.</param>
    /// <param name="date">The day being adjusted.</param>
    /// <param name="delta">The signed amount to merge in.</param>
    private void Merge(List<ManualEarmark> saved, List<(int, DateOnly)> deleted, EarMarkPattern pattern, DateOnly date, decimal delta)
    {
        var existing = _editTarget is not null && _editTarget.FinanceId == pattern.FinanceId && _editTarget.Date == date
            ? 0m // editing replaces the day's amount, not stacks onto itself
            : _existing.FirstOrDefault(m => m.FinanceId == pattern.FinanceId && m.Date == date)?.Amount ?? 0m;

        var final = existing + delta;
        if (final == 0m)
        {
            deleted.Add((pattern.FinanceId, date));
            return;
        }

        saved.Add(ManualEarmark.Create(
            new ManualEarmarkOptions { FinanceId = pattern.FinanceId, Date = date, Amount = final },
            pattern));
    }

    /// <summary>[CALC] Ruling: jars never go below 0 — a withdrawal (or move-source) larger than what the fund holds that day is blocked outright.</summary>
    /// <param name="source">The jar the withdrawal comes from.</param>
    /// <param name="date">The day being adjusted.</param>
    /// <param name="withdrawal">The amount being withdrawn.</param>
    private void RequireFundsCover(JarChoice source, DateOnly date, decimal withdrawal)
    {
        if (_forecast is null)
        {
            return; // no forecast on screen — the cascade floor is the backstop
        }

        var (jarBalance, _) = BalancesOn(date, source.Pattern.FinanceId);
        var existing = ExistingAmountFor(source.Pattern.FinanceId, date);
        if (withdrawal - Math.Min(existing, 0m) > jarBalance)
        {
            throw new InvalidOperationException(
                $"{source.Label} only holds {jarBalance:C} on that day — a fund can't go below zero.");
        }
    }

    /// <summary>[UI] Ruling: over-adds warn but are allowed.</summary>
    /// <param name="date">The day being adjusted.</param>
    /// <param name="addition">The amount being added.</param>
    private void WarnIfOverFree(DateOnly date, decimal addition)
    {
        if (_forecast is null || JarComboBox.SelectedItem is not JarChoice choice)
        {
            return;
        }

        var (_, free) = BalancesOn(date, choice.Pattern.FinanceId);
        if (addition <= free)
        {
            return;
        }

        var proceed = MessageBox.Show(
            this,
            $"Free balance on that day is only {free:C}. Setting aside {addition:C} over-commits the plan — " +
            "the forecast will show a negative free balance, and money may be pulled back out of " +
            "lower-priority funds to cover it. Add it anyway?",
            "More than the free balance",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (proceed != MessageBoxResult.Yes)
        {
            throw new OperationCanceledException();
        }
    }

    private decimal ExistingAmountFor(int financeId, DateOnly date) =>
        _existing.FirstOrDefault(m => m.FinanceId == financeId && m.Date == date)?.Amount ?? 0m;

    /// <summary>[CALC] The fund's and free balance on the chosen day, read from the shown forecast: the timeline entry on that date, or the nearest one before it.</summary>
    /// <param name="date">The day to read balances as of.</param>
    /// <param name="financeId">Which fund's balance to read.</param>
    private (decimal JarBalance, decimal Free) BalancesOn(DateOnly date, int financeId)
    {
        var entry = _forecast!.GetTimeline().LastOrDefault(candidate => candidate.Date <= date);
        if (entry is null)
        {
            return (0m, 0m);
        }

        var jar = entry.Snapshot.FundJars.FirstOrDefault(candidate => candidate.FinanceId == financeId);
        return (jar?.ExpectedAmount ?? 0m, entry.Snapshot.ExpectedFreeAmount ?? 0m);
    }

    private DateOnly? SelectedDate() =>
        EarmarkDatePicker.SelectedDate is { } picked ? DateOnly.FromDateTime(picked) : null;

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
