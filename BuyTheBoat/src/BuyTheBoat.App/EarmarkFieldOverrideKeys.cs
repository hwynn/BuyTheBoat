namespace BuyTheBoat.App;

// The keys of the optional override dictionary a suggestion passes into the
// Earmark form's own load (EarmarkFormPanel.LoadPattern). Each key names one
// field whose suggested value should win over what's on the saved plan, so the
// form opens pre-filled with the correction as an UNSAVED edit for the user to
// review. Shared so the side that builds the dictionary (FinancePatternSaveConfirmation)
// and the side that reads it (EarmarkFormPanel) can never drift on a literal —
// the same reason ConfirmationRowIds exists. Only the fields a suggestion can
// actually change today are listed; more get added here as richer suggestions
// are built (each value's runtime type is noted, since the dictionary is untyped).
public static class EarmarkFieldOverrideKeys
{
    // decimal — the plan's own signed Amount (negative = into the jar), same
    // convention EarMarkPattern.Amount uses; the form shows it as its magnitude.
    public const string Amount = "amount";

    // IReadOnlyList<DateOnly> — contribution dates to mark skipped (RFC 5545
    // EXDATE), for a goal-health "skip some events" correction. Pre-fills the
    // recurrence editor's skip list exactly as if the user picked them by hand.
    public const string ExcludedDates = "excludedDates";
}
