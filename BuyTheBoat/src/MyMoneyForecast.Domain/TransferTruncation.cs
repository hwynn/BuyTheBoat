namespace MyMoneyForecast.Domain;

public sealed record TransferTruncationResult
{
    public required Transfer Transfer { get; init; }
    public required FinancialPattern Withdrawal { get; init; }
    public required EarMarkPattern? WithdrawalPlan { get; init; }
    public required FinancialPattern Deposit { get; init; }
}

// Ends a scheduled transfer on a chosen date, with no continuation — the
// transfer analogue of PatternTruncation.EndOn (planning/16, item 16),
// extended to transfers the same way BreakOffFactory was
// (TransferBreakOffFactory, planning/16 § transfers). A transfer is three
// linked things (planning/10 item 3): the Transfer record itself, the
// withdrawal leg (and its savings plan, if it has one), and the deposit leg
// (income, never has a plan). All three must end on the identical date, or
// the project's own transfer-validation sweep would flag the drift.
// Identified as a gap in planning/18 — ordinary bills get this mechanism for
// free from PatternTruncation.EndOn serving both jobs (standalone truncation
// and break-off's predecessor half); transfers only had the break-off half
// (TransferBreakOffFactory), never the standalone one.
public static class TransferTruncation
{
    /// <summary>[CALC] Ends a scheduled transfer on a chosen date, with no continuation. Whatever was reserved for it becomes ordinary free balance again from that date on, the same as ending any other pattern.</summary>
    /// <param name="transfer">The transfer being ended.</param>
    /// <param name="withdrawal">Its withdrawal leg.</param>
    /// <param name="withdrawalPlan">The withdrawal leg's savings plan, if it has one.</param>
    /// <param name="deposit">Its deposit leg (never has a plan — income never does).</param>
    /// <param name="lastDay">The last day the transfer should still occur.</param>
    /// <returns>The transfer's record and both legs, all ending on <paramref name="lastDay"/>.</returns>
    public static TransferTruncationResult EndOn(
        Transfer transfer,
        FinancialPattern withdrawal,
        EarMarkPattern? withdrawalPlan,
        FinancialPattern deposit,
        DateOnly lastDay)
    {
        if (withdrawal.Amount != -transfer.Amount)
        {
            throw new ArgumentException(
                "The withdrawal leg's amount must be the exact negative of the transfer's amount — they have already drifted apart.",
                nameof(withdrawal));
        }

        if (deposit.Amount != transfer.Amount)
        {
            throw new ArgumentException(
                "The deposit leg's amount must exactly match the transfer's amount — they have already drifted apart.",
                nameof(deposit));
        }

        var withdrawalResult = PatternTruncation.EndOn(withdrawal, withdrawalPlan, lastDay);
        var depositResult = PatternTruncation.EndOn(deposit, null, lastDay);

        return new TransferTruncationResult
        {
            Transfer = transfer.WithUntil(lastDay),
            Withdrawal = withdrawalResult.Pattern,
            WithdrawalPlan = withdrawalResult.Plan,
            Deposit = depositResult.Pattern,
        };
    }
}
