namespace BuyTheBoat.Domain;

/// <summary>
/// A real movement of money, from a bank feed, an exported statement, or manual entry.
/// Nothing creates one yet, and BalanceSnapshot.ActualTransactions is always empty; it marks where transaction import and pairing will go.
/// </summary>
/// <remarks>
/// ASSUMED-PAIRING(import): every spot tagged ASSUMED-PAIRING acts as if these existed and were paired to their ExpectedTransactions.
/// Search for that tag when import is built; those are the spots to revisit.
/// </remarks>
public sealed record ActualTransaction
{
    /// <summary>"Where the transaction came from and what it is for"; matched against FinancialPattern.Source for automatic pairing.</summary>
    public required string Source { get; init; }

    /// <summary>Can't be in the future, since a transaction can't be recorded before it happens.</summary>
    public required DateOnly OccurredDate { get; init; }

    public required decimal Amount { get; init; }

    /// <summary>The ExpectedTransaction this is paired to; set together with PairedExpectedDate, or both null.</summary>
    public int? PairedFinanceId { get; init; }

    /// <summary>The paired ExpectedTransaction's date; set together with PairedFinanceId, or both null.</summary>
    public DateOnly? PairedExpectedDate { get; init; }

    /// <summary>Whether this came from a batch import rather than being entered by hand.</summary>
    public bool MadeInBulk { get; init; }
}
