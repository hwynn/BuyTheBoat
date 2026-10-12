using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

/// <summary>
/// Lets the user pick which segment of a bill or paycheck's break-off chain to open for editing, instead of always the current one.
/// Shown only when a chain has more than one segment; the current segment (per BreakOffFactory.FindCurrentSegment) is shown in bold.
/// </summary>
public partial class ChainSegmentPickerWindow : Window
{
    private sealed class Row(FinancialPattern pattern, bool isCurrent)
    {
        public FinancialPattern Pattern { get; } = pattern;
        public bool IsCurrent { get; } = isCurrent;
        public string StatusText => IsCurrent ? "Current" : "";

        public string DateRangeText => $"{Pattern.DatePattern.ActiveStart:MMM d, yyyy} – {Pattern.DatePattern.Until:MMM d, yyyy}";

        public string AmountText => $"{Math.Abs(Pattern.Amount):C}{(Pattern.Amount < 0 ? " / occurrence" : "")}";
    }

    /// <summary>The segment the user picked, or null if they cancelled.</summary>
    public FinancialPattern? SelectedSegment { get; private set; }

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

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SelectButton.IsEnabled = SegmentsGrid.SelectedItem is not null;

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SegmentsGrid.SelectedItem is Row)
        {
            Confirm();
        }
    }

    private void OnSelectClick(object sender, RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        if (SegmentsGrid.SelectedItem is not Row row)
        {
            return;
        }

        SelectedSegment = row.Pattern;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
