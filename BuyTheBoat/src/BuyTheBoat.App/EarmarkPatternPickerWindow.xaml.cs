using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

/// <summary>
/// A popup for FinancePatternSaveConfirmation.AskWhichEarmarkPatternToOpen, laid out like FinancialPatternPickerWindow.
/// Shown only when more than one EarMarkPattern remains for one goal (a chain of plans one after another, or plans running at once).
/// </summary>
public partial class EarmarkPatternPickerWindow : Window
{
    private sealed class Row(EarMarkPattern pattern)
    {
        public EarMarkPattern Pattern { get; } = pattern;
        public string AmountText => $"{Math.Abs(Pattern.Amount):C}{(Pattern.Amount < 0 ? " / occurrence" : "")}";
        public string DateRangeText => $"{Pattern.DatePattern.ActiveStart:MMM d, yyyy} – {Pattern.DatePattern.Until:MMM d, yyyy}";
        public string StartingAllocationText => Pattern.StartingAllocation.ToString("C");
    }

    /// <summary>The plan the user picked, or null if they cancelled.</summary>
    public EarMarkPattern? SelectedPlan { get; private set; }

    public EarmarkPatternPickerWindow(IReadOnlyList<EarMarkPattern> plans)
    {
        InitializeComponent();

        var rows = plans
            .OrderBy(plan => plan.DatePattern.ActiveStart)
            .Select(plan => new Row(plan))
            .ToList();

        PlansGrid.ItemsSource = rows;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SelectButton.IsEnabled = PlansGrid.SelectedItem is not null;

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlansGrid.SelectedItem is Row)
        {
            Confirm();
        }
    }

    private void OnSelectClick(object sender, RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        if (PlansGrid.SelectedItem is not Row row)
        {
            return;
        }

        SelectedPlan = row.Pattern;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
