using System.Windows;

namespace MyMoneyForecast.App;

// Minimal, plain-WPF version of planning/25's Item B/C/E/F confirmation —
// deliberately not the full styled design in
// planning/mockups/editing-history-confirmation-mockups.html, just enough
// content and interaction for FinancePatternSaveConfirmation's
// ConfirmImplicitChanges delegate to have something real to show, so the
// mechanism built this session (break-off, consolidation, narrowing) is
// actually reachable by using the app, not just by its own tests. Content
// and which sections are visible are driven entirely by the
// ImplicitChangeConfirmationRequest passed to the constructor — nothing
// here re-derives anything DetermineConditions already worked out.
//
// TODO: doesn't name the specific amount/date a retroactive correction
// would orphan (Item E's own "show the consequence, not just a yes/no") —
// see BuildDescription's own note. Doesn't cover the proportional-scaling
// offer's own math, just whether the user wants it (ScaleCheckBox);
// PerformImplicitEarmarkChanges doesn't act on ChoseScalePatterns yet
// either.
public partial class EditingHistoryConfirmationWindow : Window
{
    // Read by the caller (MainWindow's own ConfirmImplicitChanges wiring)
    // after ShowDialog() returns true. Meaningless when the corresponding
    // section was never shown — the caller only reads the ones
    // FinancePatternSaveConfirmation itself says are actually in play,
    // mirroring how ImplicitChangeConfirmationAnswer's own fields work.
    public bool ChooseAlterPast { get; private set; }
    public bool ChooseConsolidation { get; private set; }
    public bool ChoseScalePatterns { get; private set; }

    public EditingHistoryConfirmationWindow(ImplicitChangeConfirmationRequest request)
    {
        InitializeComponent();

        DescriptionText.Text = request.Description;

        AlterPastSection.Visibility = request.IsChangeCritical ? Visibility.Visible : Visibility.Collapsed;

        // Forced consolidation is announced, not asked — Item F's own
        // ruling: there's no real choice once the recurrence shape itself
        // is changing (ConsolidationNeeded), so the ask section and the
        // forced-notice text are mutually exclusive.
        var offersConsolidationChoice = request.HasMultipleEarmarkPatterns && !request.ConsolidationNeeded;
        ConsolidationAskSection.Visibility = offersConsolidationChoice ? Visibility.Visible : Visibility.Collapsed;
        ConsolidationForcedText.Visibility = request.HasMultipleEarmarkPatterns && request.ConsolidationNeeded
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Shown regardless of which Consolidation radio ends up picked —
        // simplest correct behavior for a deliberately minimal popup; the
        // caller only reads ChoseScalePatterns when it's actually relevant.
        ScaleCheckBox.Visibility = offersConsolidationChoice && request.IsAmountOnlyChange
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ChooseAlterPast = AlterPastRadio.IsChecked == true;
        ChooseConsolidation = ConsolidateRadio.IsChecked == true;
        ChoseScalePatterns = ScaleCheckBox.IsChecked == true;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
