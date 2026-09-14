namespace MyMoneyForecast.Domain;

public sealed record TransferRequest
{
    public required int TransferId { get; init; }
    public required int WithdrawalFinanceId { get; init; }
    public required int DepositFinanceId { get; init; }
    public required int FromAccountId { get; init; }
    public required int ToAccountId { get; init; }
    public required string FromAccountName { get; init; }
    public required string ToAccountName { get; init; }
    public required decimal Amount { get; init; }
    public required RecurrenceRule DatePattern { get; init; }

    // "It just keeps going" for a transfer — set on BOTH legs (they must always
    // agree, per TransferBreakOffFactory's own check). Marks an ongoing transfer
    // the forecast-time pass extends to the horizon; default false = a plain
    // transfer that ends on its own schedule.
    public bool AutoRenew { get; init; }
}

// The Transfer plus the two ordinary FinancialPatterns it expands into.
// Withdrawal is negative and files under the FROM account; Deposit is
// positive and files under the TO account. The filing itself happens at
// save time (the patterns, like every FinancialPattern, carry no account of
// their own). Both patterns carry the transfer's schedule and are
// non-mandatory: moving your own money is not a bill, so it never
// auto-reserves.
public sealed record TransferResult
{
    public required Transfer Transfer { get; init; }
    public required FinancialPattern Withdrawal { get; init; }
    public required FinancialPattern Deposit { get; init; }
}

// The transfer analogue of OneTimeGoalFactory: one user action becomes a
// linked set of persisted objects. Nothing here is a new documented class —
// the patterns are plain FinancialPatterns.
public static class TransferFactory
{
    /// <summary>[CALC] Builds a transfer's Transfer record plus its withdrawal and deposit patterns, from the accounts, amount, and schedule the user entered.</summary>
    /// <param name="request">The transfer's accounts, amount, and schedule.</param>
    public static TransferResult Create(TransferRequest request)
    {
        var transfer = Transfer.Create(new TransferOptions
        {
            Id = request.TransferId,
            FromAccountId = request.FromAccountId,
            ToAccountId = request.ToAccountId,
            Amount = request.Amount,
            DatePattern = request.DatePattern,
        });

        var withdrawal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = request.WithdrawalFinanceId,
            Source = $"Transfer to {request.ToAccountName}",
            Description = $"Transfer to {request.ToAccountName}",
            DatePattern = request.DatePattern,
            Amount = -request.Amount,
            Mandatory = false,
            AutoRenew = request.AutoRenew,
        });

        var deposit = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = request.DepositFinanceId,
            Source = $"Transfer from {request.FromAccountName}",
            Description = $"Transfer from {request.FromAccountName}",
            DatePattern = request.DatePattern,
            Amount = request.Amount,
            Mandatory = false,
            AutoRenew = request.AutoRenew,
        });

        return new TransferResult { Transfer = transfer, Withdrawal = withdrawal, Deposit = deposit };
    }
}
