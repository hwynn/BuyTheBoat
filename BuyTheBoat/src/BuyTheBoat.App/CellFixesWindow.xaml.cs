using System.Windows;
using System.Windows.Controls;

namespace BuyTheBoat.App;

// One red cell as the fix list shows it: where it is, what it says now, why it's red, and the proposed fix (if any).
public sealed class CellFixRow
{
    public required SpreadsheetRow Row { get; init; }
    public required int Column { get; init; }
    public required string DraftName { get; init; }
    public required string ColumnHeader { get; init; }
    public required string CurrentText { get; init; }
    public required string Message { get; init; }
    public string? Suggestion { get; init; }
    public bool HasSuggestion => Suggestion is not null;

    // Fixes with a suggestion start checked; the user unchecks any they don't want.
    public bool IsChecked { get; set; }
}

// The popup behind the Spreadsheet subtab's "N invalid cells" counter: every red draft cell, each with its proposed fix,
// so the user sees exactly what will change before anything does.
public partial class CellFixesWindow : Window
{
    private readonly IReadOnlyList<CellFixRow> _fixes;

    public CellFixesWindow(IReadOnlyList<CellFixRow> fixes)
    {
        InitializeComponent();
        _fixes = fixes;
        FixesGrid.ItemsSource = fixes;
        UpdateApplyButton();
    }

    // The checked fixes, once Apply is clicked. Empty after Cancel.
    public IReadOnlyList<CellFixRow> ChosenFixes { get; private set; } = [];

    // The cell to jump to after Go to cell, or null.
    public CellFixRow? GoToTarget { get; private set; }

    /// <summary>[UI] Keeps the Apply button's count in step with the checkboxes.</summary>
    private void OnCheckChanged(object sender, RoutedEventArgs e) => UpdateApplyButton();

    /// <summary>[UI] Shows how many fixes Apply will make, and disables it when none are checked.</summary>
    private void UpdateApplyButton()
    {
        var count = _fixes.Count(fix => fix.IsChecked && fix.HasSuggestion);
        ApplyButton.Content = count == 1 ? "Apply 1 fix" : $"Apply {count} fixes";
        ApplyButton.IsEnabled = count > 0;
    }

    /// <summary>[STEP] Closes the popup, reporting the checked fixes for the spreadsheet to make.</summary>
    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        ChosenFixes = _fixes.Where(fix => fix.IsChecked && fix.HasSuggestion).ToList();
        DialogResult = true;
    }

    /// <summary>[STEP] Closes the popup without fixing anything, so the spreadsheet can move the cursor to a cell with no suggestion.</summary>
    private void OnGoToCellClick(object sender, RoutedEventArgs e)
    {
        GoToTarget = (sender as FrameworkElement)?.DataContext as CellFixRow;
        DialogResult = false;
    }
}
