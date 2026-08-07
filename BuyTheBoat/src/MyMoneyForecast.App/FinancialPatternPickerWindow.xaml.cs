using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// A reusable "pick one of my Bills/Paychecks/Goals" popup — planning/21
// Philosophy 5/7 keeps every CREATE/EDIT form on its own permanent tab, but
// SELECTING an existing FinancialPattern (to link something else to it, or
// to switch which one another form is showing) is a different, smaller
// action — a popup is the right shape for it, not a tab of its own.
//
// Source of truth is TransactionLogBook.AllFinancialPatterns() (the author's
// own call): a plain repository read would also see every pattern with no
// page-boundary limitation, but the forecast's own Book is meant to be the
// canonical "everything the user has" source for this popup, not the
// repository underneath it.
//
// Always excludes expired patterns (DatePattern.Until already passed) and
// transfer-leg patterns — both built into this window, not something a
// caller can turn off, since no second use case needing either has come up
// yet (the author's own call, made explicitly rather than assumed).
//
// TODO (2026-08-05): not wired into any form yet — built and tested as a
// standalone, reusable piece first, per the "small, verify, then move
// outward" approach. EarmarkFormPanel's own instance picker (still a plain
// ComboBox) is the obvious next caller, but that wiring is a separate step.
public partial class FinancialPatternPickerWindow : Window
{
    private sealed class Row(FinancialPattern pattern)
    {
        public FinancialPattern Pattern { get; } = pattern;
        public string Description => string.IsNullOrWhiteSpace(Pattern.Description) ? "—" : Pattern.Description;
        public string Source => Pattern.Source;
        public string DateRangeText => $"{Pattern.DatePattern.Start:MMM d, yyyy} – {Pattern.DatePattern.Until:MMM d, yyyy}";
    }

    // The pattern the user picked, or null if they cancelled out.
    public FinancialPattern? SelectedPattern { get; private set; }

    public FinancialPatternPickerWindow(TransactionLogBook book, IReadOnlySet<int> transferFinanceIds, DateOnly today)
    {
        InitializeComponent();

        var rows = book.AllFinancialPatterns()
            .Where(pattern => pattern.DatePattern.Until >= today)
            .Where(pattern => !transferFinanceIds.Contains(pattern.FinanceId))
            .OrderBy(pattern => pattern.Source)
            .ThenBy(pattern => pattern.DatePattern.Start)
            .Select(pattern => new Row(pattern))
            .ToList();

        PatternsGrid.ItemsSource = rows;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SelectButton.IsEnabled = PatternsGrid.SelectedItem is not null;

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PatternsGrid.SelectedItem is Row)
        {
            Confirm();
        }
    }

    private void OnSelectClick(object sender, RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        if (PatternsGrid.SelectedItem is not Row row)
        {
            return;
        }

        SelectedPattern = row.Pattern;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
