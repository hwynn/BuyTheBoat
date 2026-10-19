using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

/// <summary>
/// Lets the user pick which segment of a bill or paycheck's break-off chain to open for editing, instead of always the current one.
/// Shown only when a chain has more than one segment; the current segment (per BreakOffFactory.FindCurrentSegment) is shown in bold.
/// Closes with true once a segment is picked.
/// </summary>
public partial class ChainSegmentPickerWindow : Window
{
    private sealed class Row(FinancialPattern pattern, bool isCurrent)
    {
        public FinancialPattern Pattern { get; } = pattern;
        public bool IsCurrent { get; } = isCurrent;
        public string StatusText => IsCurrent ? "Current" : "";

        public string DateRangeText => $"{Pattern.DatePattern.ActiveStart:MMM d, yyyy} – {Pattern.DatePattern.Until:MMM d, yyyy}";

        public string AmountText => $"{Math.Abs(Pattern.Amount):N2}{(Pattern.Amount < 0 ? " / occurrence" : "")}";
    }

    /// <summary>The segment the user picked, or null if they cancelled.</summary>
    public FinancialPattern? SelectedSegment { get; private set; }

    /// <summary>Only for the XAML loader and previewer; the app uses the constructor that takes the segments.</summary>
    public ChainSegmentPickerWindow()
    {
        InitializeComponent();
    }

    public ChainSegmentPickerWindow(IReadOnlyList<FinancialPattern> segments)
    {
        InitializeComponent();

        var current = BreakOffFactory.FindCurrentSegment(segments[0], segments);

        var rows = segments
            .OrderBy(segment => segment.DatePattern.ActiveStart) // oldest first
            .Select(segment => new Row(segment, segment.FinanceId == current.FinanceId))
            .ToList();

        SegmentsGrid.ItemsSource = rows;
        SegmentsGrid.SelectedItem = rows.FirstOrDefault(row => row.IsCurrent); // pre-select the current one
    }

    /// <summary>[UI] Shows the current segment's row in bold; the grid reuses rows, so every other row is set back to normal.</summary>
    private void OnLoadingRow(object? sender, DataGridRowEventArgs e) =>
        e.Row.FontWeight = e.Row.DataContext is Row { IsCurrent: true } ? FontWeight.Bold : FontWeight.Normal;

    /// <summary>[UI] Enables Select once a row is picked.</summary>
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        SelectButton.IsEnabled = SegmentsGrid.SelectedItem is not null;

    /// <summary>[UI] Picks the double-clicked row and closes.</summary>
    private void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SegmentsGrid.SelectedItem is Row)
        {
            Confirm();
        }
    }

    /// <summary>[UI] Picks the selected row and closes.</summary>
    private void OnSelectClick(object? sender, RoutedEventArgs e) => Confirm();

    /// <summary>[UI] Records the selected segment and closes with true.</summary>
    private void Confirm()
    {
        if (SegmentsGrid.SelectedItem is not Row row)
        {
            return;
        }

        SelectedSegment = row.Pattern;
        Close(true);
    }

    /// <summary>[UI] Closes without picking anything.</summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
