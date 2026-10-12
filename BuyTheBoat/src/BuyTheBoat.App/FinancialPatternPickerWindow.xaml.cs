using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

/// <summary>
/// A popup to pick one of the user's Bills, Paychecks, or Goals, for linking something to it or switching which one a form shows; creating and editing stay on their tabs.
/// Lists TransactionLogBook.AllFinancialPatterns(), the forecast's record of everything the user has.
/// Always leaves out ended patterns and transfer legs.
/// </summary>
public partial class FinancialPatternPickerWindow : Window
{
    private sealed class Row(FinancialPattern pattern)
    {
        public FinancialPattern Pattern { get; } = pattern;
        public string Description => string.IsNullOrWhiteSpace(Pattern.Description) ? "—" : Pattern.Description;
        public string Source => Pattern.Source;
        public string DateRangeText => $"{Pattern.DatePattern.ActiveStart:MMM d, yyyy} – {Pattern.DatePattern.Until:MMM d, yyyy}";
    }

    /// <summary>The pattern the user picked, or null if they cancelled.</summary>
    public FinancialPattern? SelectedPattern { get; private set; }

    public FinancialPatternPickerWindow(TransactionLogBook book, IReadOnlySet<int> transferFinanceIds, DateOnly today)
    {
        InitializeComponent();

        var rows = book.AllFinancialPatterns()
            .Where(pattern => pattern.DatePattern.Until >= today)
            .Where(pattern => !transferFinanceIds.Contains(pattern.FinanceId))
            .OrderBy(pattern => pattern.Source)
            .ThenBy(pattern => pattern.DatePattern.ActiveStart)
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
