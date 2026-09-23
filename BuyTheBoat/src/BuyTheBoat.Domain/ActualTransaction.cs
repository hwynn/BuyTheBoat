namespace BuyTheBoat.Domain;

// A real, observed movement of money — from a bank feed, exported statement,
// or manual entry. A pure stub in the current scope: nothing constructs one,
// and BalanceSnapshot.ActualTransactions is always empty. Exists to mark
// where actual-transaction import will land and what pairing will key on.
//
// ASSUMED-PAIRING(import): every site tagged ASSUMED-PAIRING elsewhere in
// this project is a place that currently pretends instances of this class
// exist and are paired to their ExpectedTransactions. Grep for that tag when
// import becomes real — those are the sites to revisit.
public sealed record ActualTransaction
{
    // "Where the transaction came from and what it is for" — the string
    // matched against FinancialPattern.Source for automatic pairing.
    public required string Source { get; init; }

    // Cannot be in the future — a user can't record a transaction in advance.
    public required DateOnly OccurredDate { get; init; }

    public required decimal Amount { get; init; }

    // Pairing to an ExpectedTransaction: both set, or both null.
    public int? PairedFinanceId { get; init; }
    public DateOnly? PairedExpectedDate { get; init; }

    // Whether this arrived via a batch import rather than hand entry.
    public bool MadeInBulk { get; init; }
}
