namespace BuyTheBoat.App;

/// <summary>
/// The keys for the values a suggestion passes to EarmarkFormPanel.LoadPattern, each a field whose suggested value replaces the saved one, so the form opens with the correction as an unsaved edit.
/// Shared so FinancePatternSaveConfirmation and EarmarkFormPanel always agree, as with ConfirmationRowIds; each key notes its value's type, since the dictionary is untyped.
/// </summary>
public static class EarmarkFieldOverrideKeys
{
    /// <summary>decimal: the plan's signed Amount (negative = into the jar), as in EarMarkPattern.Amount; the form shows it without the sign.</summary>
    public const string Amount = "amount";

    /// <summary>IReadOnlyList&lt;DateOnly&gt;: contribution dates to skip (RFC 5545 EXDATE), for a "skip some events" correction, filled into the skip list as if picked by hand.</summary>
    public const string ExcludedDates = "excludedDates";
}
