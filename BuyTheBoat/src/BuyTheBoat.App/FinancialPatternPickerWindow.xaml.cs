using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

/// <summary>
/// A popup to pick one of the user's Bills, Paychecks, or Goals, for linking something to it or switching which one a form shows; creating and editing stay on their tabs.
/// Lists TransactionLogBook.AllFinancialPatterns(), the forecast's record of everything the user has, once per break-off chain (segments sharing a Source).
/// Always leaves out ended chains and transfers.
/// </summary>
public partial class FinancialPatternPickerWindow : Window
{
    private sealed class Row(FinancialPattern pattern, DateOnly chainStart, DateOnly chainUntil)
    {
        public FinancialPattern Pattern { get; } = pattern;
        public string Description => string.IsNullOrWhiteSpace(Pattern.Description) ? "—" : Pattern.Description;
        public string Source => Pattern.Source;
        public string DateRangeText => $"{chainStart:MMM d, yyyy} – {chainUntil:MMM d, yyyy}";
    }

    /// <summary>The picked chain's segment running today, or its next one when none is, or null if the user cancelled.</summary>
    public FinancialPattern? SelectedPattern { get; private set; }

    public FinancialPatternPickerWindow(TransactionLogBook book, IReadOnlySet<int> transferFinanceIds, DateOnly today)
    {
        InitializeComponent();

        var rows = book.AllFinancialPatterns()
            .Where(pattern => !transferFinanceIds.Contains(pattern.FinanceId))
            .GroupBy(pattern => pattern.Source)
            .Select(chain => chain.OrderBy(pattern => pattern.DatePattern.ActiveStart).ToList())
            .Where(segments => segments[^1].DatePattern.Until >= today)
            .OrderBy(segments => segments[0].Source)
            .Select(segments => new Row(
                segments.FirstOrDefault(segment => segment.DatePattern.ActiveSpanContains(today))
                    ?? segments.First(segment => segment.DatePattern.ActiveStart > today || segment == segments[^1]),
                segments[0].DatePattern.ActiveStart,
                segments[^1].DatePattern.Until))
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
