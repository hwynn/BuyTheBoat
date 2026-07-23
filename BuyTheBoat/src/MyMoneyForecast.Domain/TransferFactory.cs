namespace MyMoneyForecast.Domain;

public sealed record TransferRequest
{
    public required int TransferId { get; init; }
    public required int OutLegFinanceId { get; init; }
    public required int InLegFinanceId { get; init; }
    public required int FromAccountId { get; init; }
    public required int ToAccountId { get; init; }
    public required string FromAccountName { get; init; }
    public required string ToAccountName { get; init; }
    public required decimal Amount { get; init; }
    public required RecurrenceRule DatePattern { get; init; }
}

// The Transfer plus the two ordinary FinancialPattern legs it expands into.
// OutLeg is the withdrawal (negative, filed under the FROM account); InLeg is
// the deposit (positive, filed under the TO account). The filing itself happens
// at save time (the legs, like every FinancialPattern, carry no account of
// their own — item 2-A). Both legs carry the transfer's schedule and are
// non-mandatory: moving your own money is not a bill, so it never auto-reserves
// (planning/10 item 3, "Parked").
public sealed record TransferResult
{
    public required Transfer Transfer { get; init; }
    public required FinancialPattern OutLeg { get; init; }
    public required FinancialPattern InLeg { get; init; }
}

// The transfer analogue of OneTimeGoalFactory: one user action becomes a
// linked set of persisted objects. Nothing here is a new documented class —
// the legs are plain FinancialPatterns.
public static class TransferFactory
{
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

        var outLeg = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = request.OutLegFinanceId,
            Source = $"Transfer to {request.ToAccountName}",
            Description = $"Transfer to {request.ToAccountName}",
            DatePattern = request.DatePattern,
            Amount = -request.Amount,
            Mandatory = false,
        });

        var inLeg = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = request.InLegFinanceId,
            Source = $"Transfer from {request.FromAccountName}",
            Description = $"Transfer from {request.FromAccountName}",
            DatePattern = request.DatePattern,
            Amount = request.Amount,
            Mandatory = false,
        });

        return new TransferResult { Transfer = transfer, OutLeg = outLeg, InLeg = inLeg };
    }
}
