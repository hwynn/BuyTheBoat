namespace BuyTheBoat.Domain;

/// <summary>
/// One expected money event on a specific date: one occurrence of a FinancialPattern, such as a bill's due date or a payday.
/// "An expected transaction is a singular, discrete event in a transaction log page. It is normally created after the current date... If it has a finance_id, this is a repeated expected transaction (even if it only happens once)." (class documentation.ods)
/// The forecast works from these, and they're what real bank data will be paired against.
/// </summary>
public sealed record ExpectedTransaction
{
    /// <summary>
    /// The FinancialPattern that generated this; even a one-off goal has one.
    /// "So if a user manually creates a new expected transaction, a new financial pattern will have to be created first by the system."
    /// </summary>
    public required int FinanceId { get; init; }

    public required DateOnly ExpectedDate { get; init; }

    public required decimal ExpectedAmount { get; init; }

    /// <summary>
    /// "If true, we ignore this expected transaction."
    /// Nothing sets this yet; it's here for when editing individual occurrences becomes a feature.
    /// </summary>
    public bool Cancelled { get; init; }

    /// <summary>
    /// ASSUMED-PAIRING(fulfillment): with no actual transactions to pair against, every expected transaction is treated as paid exactly as written, so this stays null.
    /// When transaction import exists, pairing belongs here and on AccountTransactionPage (pair_actual_event, unpair_actual_event, ScanForMatchs).
    /// </summary>
    public decimal? PairedAmount { get; init; }

    /// <summary>Stays null until pairing exists; see PairedAmount.</summary>
    public DateOnly? PairedActualDate { get; init; }

    /// <summary>How far below and above the expected amount a real transaction may be and still match; [0, 0] by default, and only used once pairing exists.</summary>
    public (decimal Below, decimal Above) AmountTolerance { get; init; } = (0m, 0m);

    /// <summary>How far before and after the expected date a real transaction may land and still match; [0, 0] by default, and only used once pairing exists.</summary>
    public (decimal Below, decimal Above) DateTolerance { get; init; } = (0m, 0m);
}
