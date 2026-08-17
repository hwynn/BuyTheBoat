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
// Naming the specific amount/date a retroactive correction would orphan
// (Item E's own "show the consequence, not just a yes/no") is now built too,
// 2026-08-17 — AlterPastConsequenceText, computed by FinancePatternSaveConfirmation.
// DescribeAlterPastConsequence and shown only while AlterPastRadio is
// selected, the same "concrete consequence under the currently-selected
// option" pattern StayLinkedWarning/LetItBreakWarning/CascadeDescription
// already established. TODO still open: doesn't cover the proportional-
// scaling offer's own math, just whether the user wants it (ScaleCheckBox) —
// ScaleSurvivingPlansIfNeeded itself has acted on ChoseScalePatterns since
// 2026-08-14, this window just doesn't show what the new amounts would be
// ahead of the choice.
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

    // planning/27's own chain-boundary/cascade answers — shared by both
    // chain types (see ChainBoundarySection/CascadeSection's own XAML
    // comment), meaningless unless the matching request field of the same
    // name — either PlanTouchesChainBoundary/PlanChangeCanCascade or
    // TouchesChainBoundary/ChangeCanCascade — was true.
    public bool ChoseStayLinked { get; private set; }
    public bool ChoseCascadeForward { get; private set; }

    // Phase 1's own third answer, no EarMarkPattern equivalent — meaningless
    // unless TrivialFieldsCanCascade was true.
    public bool ChoseCascadeTrivialFields { get; private set; }

    // The paycheck-association cascade's own answer — meaningless unless
    // PacedBillsCanCascade was true.
    public bool ChoseToRepaceBills { get; private set; }

    public EditingHistoryConfirmationWindow(ImplicitChangeConfirmationRequest request)
    {
        InitializeComponent();

        DescriptionText.Text = request.Description;

        AlterPastSection.Visibility = request.IsChangeCritical ? Visibility.Visible : Visibility.Collapsed;

        // Text set before the visibility pass below runs — BreakOffRadio's
        // own XAML-declared IsChecked="True" fires OnAlterPastChoiceChanged
        // synchronously mid-InitializeComponent, before this .Text is set,
        // the same WPF footgun ChainBoundarySection's own comment already
        // documents. Calling UpdateAlterPastConsequenceVisibility again
        // here, after the text is in place, is what makes the INITIAL
        // state correct rather than relying on that early, premature firing.
        AlterPastConsequenceText.Text = request.AlterPastConsequence;
        UpdateAlterPastConsequenceVisibility();

        NarrowingLimitationWarningText.Text = request.NarrowingLimitationWarning;
        NarrowingLimitationWarningText.Visibility = string.IsNullOrEmpty(request.NarrowingLimitationWarning) ? Visibility.Collapsed : Visibility.Visible;

        // Forced consolidation is announced, not asked — Item F's own
        // ruling: there's no real choice once the recurrence shape itself
        // is changing (ConsolidationNeeded), so the ask section and the
        // forced-notice text are mutually exclusive.
        var offersConsolidationChoice = request.HasMultipleEarmarkPatterns && !request.ConsolidationNeeded;
        ConsolidationAskSection.Visibility = offersConsolidationChoice ? Visibility.Visible : Visibility.Collapsed;
        ConsolidationForcedText.Text = request.ConsolidationForcedReason;
        ConsolidationForcedText.Visibility = request.HasMultipleEarmarkPatterns && request.ConsolidationNeeded
            ? Visibility.Visible
            : Visibility.Collapsed;
        ConsolidationCaveatText.Text = request.ConsolidationCaveat;
        ConsolidationCaveatText.Visibility = string.IsNullOrEmpty(request.ConsolidationCaveat) ? Visibility.Collapsed : Visibility.Visible;

        // Shown regardless of which Consolidation radio ends up picked —
        // simplest correct behavior for a deliberately minimal popup; the
        // caller only reads ChoseScalePatterns when it's actually relevant.
        ScaleCheckBox.Visibility = offersConsolidationChoice && request.IsAmountOnlyChange
            ? Visibility.Visible
            : Visibility.Collapsed;

        SourceChangeWarningText.Text = request.SourceChangeWarning;
        SourceChangeWarningText.Visibility = string.IsNullOrEmpty(request.SourceChangeWarning) ? Visibility.Collapsed : Visibility.Visible;

        // Either chain type's own trigger shows the same row — the two
        // never both apply to one request (RunForPlan/Run() never both run
        // on one instance), so this is never ambiguous about which one lit
        // it up.
        ChainBoundarySection.Visibility = request.PlanTouchesChainBoundary || request.TouchesChainBoundary ? Visibility.Visible : Visibility.Collapsed;
        CascadeSection.Visibility = request.PlanChangeCanCascade || request.ChangeCanCascade ? Visibility.Visible : Visibility.Collapsed;
        TrivialFieldsCascadeSection.Visibility = request.TrivialFieldsCanCascade ? Visibility.Visible : Visibility.Collapsed;

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

        TrivialFieldsCascadeDescriptionText.Text = request.TrivialFieldsCascadeDescription;
        TrivialFieldsCascadeDescriptionText.Visibility = string.IsNullOrEmpty(request.TrivialFieldsCascadeDescription) ? Visibility.Collapsed : Visibility.Visible;

        PacedBillsCascadeSection.Visibility = request.PacedBillsCanCascade ? Visibility.Visible : Visibility.Collapsed;
        PacedBillsCascadeDescriptionText.Text = request.PacedBillsCascadeDescription;
        PacedBillsCascadeDescriptionText.Visibility = string.IsNullOrEmpty(request.PacedBillsCascadeDescription) ? Visibility.Collapsed : Visibility.Visible;
    }

    // Same "warning follows the currently-selected option" reasoning as
    // OnChainBoundaryChoiceChanged just below, for Item E's own two radios
    // instead — AlterPastConsequenceText only shows while AlterPastRadio is
    // the one currently checked, since BreakOffRadio (the default) never
    // orphans anything on its own.
    private void OnAlterPastChoiceChanged(object sender, RoutedEventArgs e) => UpdateAlterPastConsequenceVisibility();

    private void UpdateAlterPastConsequenceVisibility()
    {
        AlterPastConsequenceText.Visibility = AlterPastRadio.IsChecked == true && !string.IsNullOrEmpty(AlterPastConsequenceText.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
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
        ChoseCascadeTrivialFields = CascadeTrivialFieldsRadio.IsChecked == true;
        ChoseToRepaceBills = RepaceBillsRadio.IsChecked == true;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
