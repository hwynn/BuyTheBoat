using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// A minimal disambiguation popup for
// FinancePatternSaveConfirmation.AskWhichEarmarkPatternToOpen.
// Mirrors FinancialPatternPickerWindow's own shape (a plain grid +
// Select/Cancel) rather than inventing a new one — only fires when more
// than one EarMarkPattern genuinely survives for one goal (a
// sequential chain or concurrent earmark patterns), the same rare case that
// window's own doc comment describes for FinancialPattern.
public partial class EarmarkPatternPickerWindow : Window
{
    private sealed class Row(EarMarkPattern pattern)
    {
        public EarMarkPattern Pattern { get; } = pattern;
        public string AmountText => $"{Math.Abs(Pattern.Amount):C}{(Pattern.Amount < 0 ? " / occurrence" : "")}";
        public string DateRangeText => $"{Pattern.DatePattern.ActiveStart:MMM d, yyyy} – {Pattern.DatePattern.Until:MMM d, yyyy}";
        public string StartingAllocationText => Pattern.StartingAllocation.ToString("C");
    }

    // The plan the user picked, or null if they cancelled out.
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
