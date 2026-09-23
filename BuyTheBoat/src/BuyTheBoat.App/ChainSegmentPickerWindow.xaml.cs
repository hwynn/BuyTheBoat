using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

// Lets the user pick WHICH segment of a bill/paycheck's break-off chain to
// open for editing, rather than always being sent to the current one.
// Shown only when a chain actually has more than one segment; a standalone
// pattern skips it. The current segment — same Source, latest Start, per
// BreakOffFactory.FindCurrentSegment's own rule — is set apart in bold.
public partial class ChainSegmentPickerWindow : Window
{
    private sealed class Row(FinancialPattern pattern, bool isCurrent)
    {
        public FinancialPattern Pattern { get; } = pattern;
        public bool IsCurrent { get; } = isCurrent;
        public string StatusText => IsCurrent ? "Current" : "";

        // Both borders of the range in full — year, month, and day.
        public string DateRangeText => $"{Pattern.DatePattern.ActiveStart:MMM d, yyyy} – {Pattern.DatePattern.Until:MMM d, yyyy}";

        public string AmountText => $"{Math.Abs(Pattern.Amount):C}{(Pattern.Amount < 0 ? " / occurrence" : "")}";
    }

    // The segment the user picked, or null if they cancelled out.
    public FinancialPattern? SelectedSegment { get; private set; }

    public ChainSegmentPickerWindow(IReadOnlyList<FinancialPattern> segments)
    {
        InitializeComponent();

        var current = BreakOffFactory.FindCurrentSegment(segments[0], segments);

        var rows = segments
            .OrderBy(segment => segment.DatePattern.ActiveStart) // oldest first — history reads top to bottom
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
