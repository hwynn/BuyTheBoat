namespace MyMoneyForecast.Domain;

// A transfer's "Change starting on a date" (planning/16, item 4, extended to
// transfers). A transfer is THREE linked things (planning/10 item 3): a
// withdrawal FinancialPattern, a matching deposit FinancialPattern, and a
// Transfer record that is the canonical definition the other two are
// validated against. BreakOffFactory only knows about FinancialPattern —
// calling it on each leg independently would let the two successors drift
// apart (different cut dates, mismatched amounts) and would leave the OLD
// Transfer record's own schedule pointing past where its legs now end,
// exactly the inconsistency the project's own transfer-validation sweep
// exists to catch. This factory exists to make that structurally impossible:
// one cut date and one mirrored amount, applied to both legs and the Transfer
// record together.
public sealed record TransferBreakOffRequest
{
    public required Transfer PredecessorTransfer { get; init; }
    public required FinancialPattern PredecessorWithdrawal { get; init; }
    public required EarMarkPattern? PredecessorWithdrawalPlan { get; init; }
    public required FinancialPattern PredecessorDeposit { get; init; }
    public required DateOnly CutDate { get; init; }
    public required int SuccessorTransferId { get; init; }
    public required int SuccessorWithdrawalFinanceId { get; init; }
    public required int SuccessorDepositFinanceId { get; init; }

    // A positive magnitude, matching Transfer's own convention — direction
    // comes from From/To, not the sign (mirrored onto the legs below).
    public required decimal SuccessorAmount { get; init; }

    // Shared by both legs — a transfer's withdrawal and deposit always run on
    // the identical schedule.
    public required RecurrenceRuleOptions SuccessorSchedule { get; init; }

    public required decimal CarriedOverWithdrawalJarBalance { get; init; }
    public required IReadOnlyList<FinancialPattern> AllPatterns { get; init; }
}

public sealed record TransferBreakOffResult
{
    public required Transfer PredecessorTransfer { get; init; }
    public required FinancialPattern PredecessorWithdrawal { get; init; }
    public required EarMarkPattern? PredecessorWithdrawalPlan { get; init; }
    public required FinancialPattern PredecessorDeposit { get; init; }
    public required Transfer SuccessorTransfer { get; init; }
    public required FinancialPattern SuccessorWithdrawal { get; init; }
    public required EarMarkPattern SuccessorWithdrawalPlan { get; init; }
    public required ManualEarmark? SuccessorWithdrawalStartingEarmark { get; init; }
    public required FinancialPattern SuccessorDeposit { get; init; }
}

public static class TransferBreakOffFactory
{
    /// <summary>[CALC] Ends a scheduled transfer on a chosen date and hands it off to a new one that continues from there with its own amount — "Change starting on a date," for a transfer. Both legs and the Transfer record itself move together, so the two can never end up on different dates or mismatched amounts.</summary>
    /// <param name="request">The transfer being changed (its Transfer record and both legs), the cut date, the new amount/schedule it takes on, and the withdrawal jar balance to carry across.</param>
    /// <returns>The now-bounded original transfer (record and both legs) plus the new transfer that continues from the cut date, with the withdrawal leg's savings plan already set up.</returns>
    public static TransferBreakOffResult BreakOff(TransferBreakOffRequest request)
    {
        if (request.PredecessorWithdrawal.Amount != -request.PredecessorTransfer.Amount)
        {
            throw new ArgumentException(
                "The withdrawal leg's amount must be the exact negative of the transfer's amount — they have already drifted apart.",
                nameof(request));
        }

        if (request.PredecessorDeposit.Amount != request.PredecessorTransfer.Amount)
        {
            throw new ArgumentException(
                "The deposit leg's amount must exactly match the transfer's amount — they have already drifted apart.",
                nameof(request));
        }

        var withdrawalResult = BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = request.PredecessorWithdrawal,
            PredecessorPlan = request.PredecessorWithdrawalPlan,
            CutDate = request.CutDate,
            SuccessorFinanceId = request.SuccessorWithdrawalFinanceId,
            SuccessorAmount = -request.SuccessorAmount,
            SuccessorSchedule = request.SuccessorSchedule,
            CarriedOverJarBalance = request.CarriedOverWithdrawalJarBalance,
            AllPatterns = request.AllPatterns,
        });

        var depositResult = BreakOffFactory.BreakOff(new BreakOffRequest
        {
            Predecessor = request.PredecessorDeposit,
            PredecessorPlan = null, // income never has one (Stage 1)
            CutDate = request.CutDate,
            SuccessorFinanceId = request.SuccessorDepositFinanceId,
            SuccessorAmount = request.SuccessorAmount,
            SuccessorSchedule = request.SuccessorSchedule,
            CarriedOverJarBalance = 0m, // ignored on the income path
            AllPatterns = request.AllPatterns,
        });

        var truncatedTransfer = request.PredecessorTransfer.WithUntil(request.CutDate.AddDays(-1));

        var successorTransfer = Transfer.Create(new TransferOptions
        {
            Id = request.SuccessorTransferId,
            FromAccountId = request.PredecessorTransfer.FromAccountId,
            ToAccountId = request.PredecessorTransfer.ToAccountId,
            Amount = request.SuccessorAmount,
            // Taken from the withdrawal leg's own resolved schedule rather than
            // rebuilding it a third time — guarantees the Transfer record's
            // DatePattern is exactly what both legs actually ended up with.
            DatePattern = withdrawalResult.Successor.DatePattern,
        });

        return new TransferBreakOffResult
        {
            PredecessorTransfer = truncatedTransfer,
            PredecessorWithdrawal = withdrawalResult.Predecessor,
            PredecessorWithdrawalPlan = withdrawalResult.PredecessorPlan,
            PredecessorDeposit = depositResult.Predecessor,
            SuccessorTransfer = successorTransfer,
            SuccessorWithdrawal = withdrawalResult.Successor,
            // Never null: a withdrawal is an outflow by construction (Transfer's
            // Amount is always positive; the withdrawal leg is always its
            // negative), so BreakOffFactory always takes the outflow path.
            SuccessorWithdrawalPlan = withdrawalResult.SuccessorPlan!,
            SuccessorWithdrawalStartingEarmark = withdrawalResult.SuccessorStartingEarmark,
            SuccessorDeposit = depositResult.Successor,
        };
    }
}
