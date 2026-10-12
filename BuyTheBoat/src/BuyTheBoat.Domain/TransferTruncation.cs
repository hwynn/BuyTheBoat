namespace BuyTheBoat.Domain;

public sealed record TransferTruncationResult
{
    public required Transfer Transfer { get; init; }
    public required FinancialPattern Withdrawal { get; init; }
    public required EarMarkPattern? WithdrawalPlan { get; init; }
    public required FinancialPattern Deposit { get; init; }
}

/// <summary>
/// Ends a scheduled transfer on a chosen date, with nothing continuing after; the transfer version of PatternTruncation.EndOn.
/// A transfer is three linked things: the Transfer record, the withdrawal leg (and its savings plan, if any), and the deposit leg; all three must end on the same date, or the transfer check flags them as out of step.
/// </summary>
public static class TransferTruncation
{
    /// <summary>
    /// [CALC] Ends a scheduled transfer on a chosen date, with nothing continuing after.
    /// Whatever was set aside for it becomes free funds again from that date on.
    /// </summary>
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
