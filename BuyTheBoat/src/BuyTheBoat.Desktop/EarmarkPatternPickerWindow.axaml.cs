using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

/// <summary>
/// A popup for FinancePatternSaveConfirmation.AskWhichEarmarkPatternToOpen, laid out like FinancialPatternPickerWindow.
/// Shown only when more than one EarMarkPattern remains for one goal (a chain of plans one after another, or plans running at once).
/// Closes with true once a plan is picked.
/// </summary>
public partial class EarmarkPatternPickerWindow : Window
{
    private sealed class Row(EarMarkPattern pattern)
    {
        public EarMarkPattern Pattern { get; } = pattern;
        public string AmountText => $"{Math.Abs(Pattern.Amount):N2}{(Pattern.Amount < 0 ? " / occurrence" : "")}";
        public string DateRangeText => $"{Pattern.DatePattern.ActiveStart:MMM d, yyyy} – {Pattern.DatePattern.Until:MMM d, yyyy}";
        public string StartingAllocationText => Pattern.StartingAllocation.ToString("N2");
    }

    /// <summary>The plan the user picked, or null if they cancelled.</summary>
    public EarMarkPattern? SelectedPlan { get; private set; }

    /// <summary>Only for the XAML loader and previewer; the app uses the constructor that takes the plans.</summary>
    public EarmarkPatternPickerWindow()
    {
        InitializeComponent();
    }

    public EarmarkPatternPickerWindow(IReadOnlyList<EarMarkPattern> plans)
    {
        InitializeComponent();

        var rows = plans
            .OrderBy(plan => plan.DatePattern.ActiveStart)
            .Select(plan => new Row(plan))
            .ToList();

        PlansGrid.ItemsSource = rows;
    }

    /// <summary>[UI] Enables Select once a row is picked.</summary>
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        SelectButton.IsEnabled = PlansGrid.SelectedItem is not null;

    /// <summary>[UI] Picks the double-clicked row and closes.</summary>
    private void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (PlansGrid.SelectedItem is Row)
        {
            Confirm();
        }
    }

    /// <summary>[UI] Picks the selected row and closes.</summary>
    private void OnSelectClick(object? sender, RoutedEventArgs e) => Confirm();

    /// <summary>[UI] Records the selected plan and closes with true.</summary>
    private void Confirm()
    {
        if (PlansGrid.SelectedItem is not Row row)
        {
            return;
        }

        SelectedPlan = row.Pattern;
        Close(true);
    }

    /// <summary>[UI] Closes without picking anything.</summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
