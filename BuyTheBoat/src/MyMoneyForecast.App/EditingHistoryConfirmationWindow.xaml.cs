using System.Windows;

namespace MyMoneyForecast.App;

// Minimal, plain-WPF version of planning/25's Item B/C/E/F confirmation
// (and, as of this session, planning/27's own "stay linked or break" and
// "cascade forward or not" questions for a savings-plan chain edit) —
// deliberately not the full styled design in
// planning/mockups/editing-history-confirmation-mockups.html, just enough
// content and interaction for FinancePatternSaveConfirmation's
// ConfirmImplicitChanges delegate to have something real to show, so the
// mechanism built this session (break-off, consolidation, narrowing, chain
// boundary resolution, cascade) is actually reachable by using the app, not
// just by its own tests. Content and which sections are visible are driven
// entirely by the ImplicitChangeConfirmationRequest passed to the
// constructor — nothing here re-derives anything DetermineConditions (or
// RunForPlan, for the EarMarkPattern-editing path) already worked out.
//
// TODO: doesn't name the specific amount/date a retroactive correction
// would orphan (Item E's own "show the consequence, not just a yes/no") —
// see BuildDescription's own note. Doesn't cover the proportional-scaling
// offer's own math, just whether the user wants it (ScaleCheckBox);
// PerformImplicitEarmarkChanges doesn't act on ChoseScalePatterns yet
// either. The two new sections' own concrete-consequence wording (which
// segment, its date range, how many manual earmarks) IS built, as of
// 2026-08-17 — StayLinkedWarning/LetItBreakWarning/CascadeDescription,
// computed by FinancePatternSaveConfirmation before this window ever opens.
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

    // planning/27's own EarMarkPattern-chain answers — same read-after-
    // ShowDialog idiom as the three above, meaningless unless the matching
    // request field (PlanTouchesChainBoundary/PlanChangeCanCascade) was true.
    public bool ChoseStayLinked { get; private set; }
    public bool ChoseCascadeForward { get; private set; }

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

        ChainBoundarySection.Visibility = request.PlanTouchesChainBoundary ? Visibility.Visible : Visibility.Collapsed;
        CascadeSection.Visibility = request.PlanChangeCanCascade ? Visibility.Visible : Visibility.Collapsed;

        // Text set before the visibility pass below runs — StayLinkedRadio's
        // own XAML-declared IsChecked="True" fires OnChainBoundaryChoiceChanged
        // synchronously mid-InitializeComponent, before either .Text is set,
        // the same WPF footgun EarmarkFormPanel's own _initialized guard
        // exists for. Calling UpdateChainBoundaryWarningVisibility again here,
        // after both texts are in place, is what makes the INITIAL state
        // correct rather than relying on that early, premature firing.
        StayLinkedWarningText.Text = request.StayLinkedWarning;
        ChainBreakWarningText.Text = request.LetItBreakWarning;
        UpdateChainBoundaryWarningVisibility();

        CascadeDescriptionText.Text = request.CascadeDescription;
        CascadeDescriptionText.Visibility = string.IsNullOrEmpty(request.CascadeDescription) ? Visibility.Collapsed : Visibility.Visible;
    }

    // Keeps each warning's own visibility live as the user picks between the
    // two ChainBoundary radios — both radios share this one handler (Checked
    // fires on whichever one becomes checked, including the one WPF checks
    // automatically when the other is unchecked), rather than computing it
    // only once at OnSaveClick, so the warning is something the user actually
    // sees before committing, not after. A warning only ever shows for
    // whichever option is CURRENTLY selected, and only when there's a real
    // consequence to name — StayLinkedWarningText stays empty (so stays
    // hidden) for a plain, never-destructive nudge, matching the row-based
    // design's own "the default option is never the dangerous one" case.
    private void OnChainBoundaryChoiceChanged(object sender, RoutedEventArgs e) => UpdateChainBoundaryWarningVisibility();

    private void UpdateChainBoundaryWarningVisibility()
    {
        StayLinkedWarningText.Visibility = StayLinkedRadio.IsChecked == true && !string.IsNullOrEmpty(StayLinkedWarningText.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ChainBreakWarningText.Visibility = LetChainBreakRadio.IsChecked == true && !string.IsNullOrEmpty(ChainBreakWarningText.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ChooseAlterPast = AlterPastRadio.IsChecked == true;
        ChooseConsolidation = ConsolidateRadio.IsChecked == true;
        ChoseScalePatterns = ScaleCheckBox.IsChecked == true;
        ChoseStayLinked = StayLinkedRadio.IsChecked == true;
        ChoseCascadeForward = CascadeForwardRadio.IsChecked == true;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
