namespace BuyTheBoat.Domain;

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

    /// <summary>
    /// "It just keeps going" for a transfer, set on both legs, which must always agree (TransferBreakOffFactory checks this).
    /// The forecast extends an ongoing transfer to the horizon; false means it ends on its own schedule.
    /// </summary>
    public bool AutoRenew { get; init; }
}

/// <summary>
/// The Transfer plus the two ordinary FinancialPatterns it becomes: a negative Withdrawal filed under the from account, and a positive Deposit filed under the to account.
/// The filing happens at save time, since patterns carry no account of their own.
/// Both follow the transfer's schedule and are non-mandatory, since moving your own money isn't a bill; the withdrawal still sets money aside through a plain, immediate savings plan made when the transfer is saved.
/// </summary>
public sealed record TransferResult
{
    public required Transfer Transfer { get; init; }
    public required FinancialPattern Withdrawal { get; init; }
    public required FinancialPattern Deposit { get; init; }
}

/// <summary>Turns one transfer the user enters into a Transfer record and two plain FinancialPatterns, as OneTimeGoalFactory does for goals.</summary>
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
