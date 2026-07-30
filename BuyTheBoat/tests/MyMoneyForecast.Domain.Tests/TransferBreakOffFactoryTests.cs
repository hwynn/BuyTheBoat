using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

public class TransferBreakOffFactoryTests
{
    private const int FromAccountId = 1;
    private const int ToAccountId = 2;

    private static (Transfer Transfer, FinancialPattern Withdrawal, FinancialPattern Deposit) MonthlyTransfer(
        decimal amount, DateOnly start, DateOnly until, int transferId = 1, int withdrawalId = 10, int depositId = 11)
    {
        var schedule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            ByMonthDay = [start.Day],
            Start = start,
            Until = until,
        });

        var transfer = Transfer.Create(new TransferOptions
        {
            Id = transferId,
            FromAccountId = FromAccountId,
            ToAccountId = ToAccountId,
            Amount = amount,
            DatePattern = schedule,
        });

        var withdrawal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = withdrawalId,
            Source = "Transfer to Savings",
            Description = "Transfer to Savings",
            DatePattern = schedule,
            Amount = -amount,
            Mandatory = false,
        });

        var deposit = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = depositId,
            Source = "Transfer from Checking",
            Description = "Transfer from Checking",
            DatePattern = schedule,
            Amount = amount,
            Mandatory = false,
        });

        return (transfer, withdrawal, deposit);
    }

    private static RecurrenceRuleOptions MonthlyFrom(DateOnly start, DateOnly until) => new()
    {
        Frequency = RecurrenceFrequency.Monthly,
        ByMonthDay = [start.Day],
        Start = start,
        Until = until,
    };

    [Fact]
    public void Both_legs_and_the_transfer_record_agree_on_the_cut_date_and_new_amount()
    {
        var (transfer, withdrawal, deposit) = MonthlyTransfer(200m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var cutDate = new DateOnly(2025, 8, 1);

        var result = TransferBreakOffFactory.BreakOff(new TransferBreakOffRequest
        {
            PredecessorTransfer = transfer,
            PredecessorWithdrawal = withdrawal,
            PredecessorWithdrawalPlan = null,
            PredecessorDeposit = deposit,
            CutDate = cutDate,
            SuccessorTransferId = 2,
            SuccessorWithdrawalFinanceId = 12,
            SuccessorDepositFinanceId = 13,
            SuccessorAmount = 300m,
            SuccessorSchedule = MonthlyFrom(cutDate, new DateOnly(2026, 1, 1)),
            CarriedOverWithdrawalJarBalance = 0m,
            AllPatterns = [withdrawal, deposit],
        });

        result.SuccessorWithdrawal.DatePattern.Start.ShouldBe(cutDate);
        result.SuccessorDeposit.DatePattern.Start.ShouldBe(cutDate);
        result.SuccessorTransfer.DatePattern.Start.ShouldBe(cutDate);
        result.SuccessorWithdrawal.Amount.ShouldBe(-300m);
        result.SuccessorDeposit.Amount.ShouldBe(300m);
        result.SuccessorTransfer.Amount.ShouldBe(300m);
    }

    [Fact]
    public void The_old_transfer_record_is_truncated_to_match_its_legs()
    {
        var (transfer, withdrawal, deposit) = MonthlyTransfer(200m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var cutDate = new DateOnly(2025, 8, 1);

        var result = TransferBreakOffFactory.BreakOff(new TransferBreakOffRequest
        {
            PredecessorTransfer = transfer,
            PredecessorWithdrawal = withdrawal,
            PredecessorWithdrawalPlan = null,
            PredecessorDeposit = deposit,
            CutDate = cutDate,
            SuccessorTransferId = 2,
            SuccessorWithdrawalFinanceId = 12,
            SuccessorDepositFinanceId = 13,
            SuccessorAmount = 300m,
            SuccessorSchedule = MonthlyFrom(cutDate, new DateOnly(2026, 1, 1)),
            CarriedOverWithdrawalJarBalance = 0m,
            AllPatterns = [withdrawal, deposit],
        });

        // This is the whole point of the factory: the Transfer record's own
        // schedule must end exactly where its legs now end, or the project's
        // own transfer-consistency sweep would flag it as drifted.
        result.PredecessorTransfer.DatePattern.Until.ShouldBe(new DateOnly(2025, 7, 31));
        result.PredecessorWithdrawal.DatePattern.Until.ShouldBe(new DateOnly(2025, 7, 31));
        result.PredecessorDeposit.DatePattern.Until.ShouldBe(new DateOnly(2025, 7, 31));
    }

    [Fact]
    public void The_new_transfer_record_references_the_two_new_finance_ids()
    {
        var (transfer, withdrawal, deposit) = MonthlyTransfer(200m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var cutDate = new DateOnly(2025, 8, 1);

        var result = TransferBreakOffFactory.BreakOff(new TransferBreakOffRequest
        {
            PredecessorTransfer = transfer,
            PredecessorWithdrawal = withdrawal,
            PredecessorWithdrawalPlan = null,
            PredecessorDeposit = deposit,
            CutDate = cutDate,
            SuccessorTransferId = 2,
            SuccessorWithdrawalFinanceId = 12,
            SuccessorDepositFinanceId = 13,
            SuccessorAmount = 300m,
            SuccessorSchedule = MonthlyFrom(cutDate, new DateOnly(2026, 1, 1)),
            CarriedOverWithdrawalJarBalance = 0m,
            AllPatterns = [withdrawal, deposit],
        });

        result.SuccessorTransfer.Id.ShouldBe(2);
        result.SuccessorWithdrawal.FinanceId.ShouldBe(12);
        result.SuccessorDeposit.FinanceId.ShouldBe(13);
        result.SuccessorTransfer.FromAccountId.ShouldBe(FromAccountId);
        result.SuccessorTransfer.ToAccountId.ShouldBe(ToAccountId);
    }

    [Fact]
    public void Only_the_withdrawal_leg_gets_a_freshly_proposed_plan()
    {
        var (transfer, withdrawal, deposit) = MonthlyTransfer(200m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var cutDate = new DateOnly(2025, 8, 1);

        var result = TransferBreakOffFactory.BreakOff(new TransferBreakOffRequest
        {
            PredecessorTransfer = transfer,
            PredecessorWithdrawal = withdrawal,
            PredecessorWithdrawalPlan = null,
            PredecessorDeposit = deposit,
            CutDate = cutDate,
            SuccessorTransferId = 2,
            SuccessorWithdrawalFinanceId = 12,
            SuccessorDepositFinanceId = 13,
            SuccessorAmount = 300m,
            SuccessorSchedule = MonthlyFrom(cutDate, new DateOnly(2026, 1, 1)),
            CarriedOverWithdrawalJarBalance = 0m,
            AllPatterns = [withdrawal, deposit],
        });

        result.SuccessorWithdrawalPlan.ShouldNotBeNull();
        result.SuccessorWithdrawalPlan.Amount.ShouldBe(-300m); // full amount, front-loaded (no income in AllPatterns)
    }

    [Fact]
    public void The_carried_over_balance_becomes_the_withdrawal_successors_starting_allocation()
    {
        var (transfer, withdrawal, deposit) = MonthlyTransfer(200m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var cutDate = new DateOnly(2025, 8, 1);

        var result = TransferBreakOffFactory.BreakOff(new TransferBreakOffRequest
        {
            PredecessorTransfer = transfer,
            PredecessorWithdrawal = withdrawal,
            PredecessorWithdrawalPlan = null,
            PredecessorDeposit = deposit,
            CutDate = cutDate,
            SuccessorTransferId = 2,
            SuccessorWithdrawalFinanceId = 12,
            SuccessorDepositFinanceId = 13,
            SuccessorAmount = 300m,
            SuccessorSchedule = MonthlyFrom(cutDate, new DateOnly(2026, 1, 1)),
            CarriedOverWithdrawalJarBalance = 75m,
            AllPatterns = [withdrawal, deposit],
        });

        result.SuccessorWithdrawalPlan.StartingAllocation.ShouldBe(75m);
    }

    [Fact]
    public void A_predecessor_withdrawal_already_out_of_sync_with_the_transfer_amount_is_rejected()
    {
        var (transfer, withdrawal, deposit) = MonthlyTransfer(200m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var driftedWithdrawal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = withdrawal.FinanceId,
            Source = withdrawal.Source,
            Description = withdrawal.Description,
            DatePattern = withdrawal.DatePattern,
            Amount = -199m, // should be -200 to match the transfer
            Mandatory = false,
        });
        var cutDate = new DateOnly(2025, 8, 1);

        Should.Throw<ArgumentException>(() => TransferBreakOffFactory.BreakOff(new TransferBreakOffRequest
        {
            PredecessorTransfer = transfer,
            PredecessorWithdrawal = driftedWithdrawal,
            PredecessorWithdrawalPlan = null,
            PredecessorDeposit = deposit,
            CutDate = cutDate,
            SuccessorTransferId = 2,
            SuccessorWithdrawalFinanceId = 12,
            SuccessorDepositFinanceId = 13,
            SuccessorAmount = 300m,
            SuccessorSchedule = MonthlyFrom(cutDate, new DateOnly(2026, 1, 1)),
            CarriedOverWithdrawalJarBalance = 0m,
            AllPatterns = [driftedWithdrawal, deposit],
        }));
    }

    [Fact]
    public void A_predecessor_deposit_already_out_of_sync_with_the_transfer_amount_is_rejected()
    {
        var (transfer, withdrawal, deposit) = MonthlyTransfer(200m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var driftedDeposit = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = deposit.FinanceId,
            Source = deposit.Source,
            Description = deposit.Description,
            DatePattern = deposit.DatePattern,
            Amount = 201m, // should be 200 to match the transfer
            Mandatory = false,
        });
        var cutDate = new DateOnly(2025, 8, 1);

        Should.Throw<ArgumentException>(() => TransferBreakOffFactory.BreakOff(new TransferBreakOffRequest
        {
            PredecessorTransfer = transfer,
            PredecessorWithdrawal = withdrawal,
            PredecessorWithdrawalPlan = null,
            PredecessorDeposit = driftedDeposit,
            CutDate = cutDate,
            SuccessorTransferId = 2,
            SuccessorWithdrawalFinanceId = 12,
            SuccessorDepositFinanceId = 13,
            SuccessorAmount = 300m,
            SuccessorSchedule = MonthlyFrom(cutDate, new DateOnly(2026, 1, 1)),
            CarriedOverWithdrawalJarBalance = 0m,
            AllPatterns = [withdrawal, driftedDeposit],
        }));
    }

    [Fact]
    public void A_cut_date_in_the_past_works_the_same_way_as_one_in_the_future()
    {
        var (transfer, withdrawal, deposit) = MonthlyTransfer(200m, new DateOnly(2024, 1, 1), new DateOnly(2026, 1, 1));
        var pastCutDate = new DateOnly(2024, 8, 1);

        var result = TransferBreakOffFactory.BreakOff(new TransferBreakOffRequest
        {
            PredecessorTransfer = transfer,
            PredecessorWithdrawal = withdrawal,
            PredecessorWithdrawalPlan = null,
            PredecessorDeposit = deposit,
            CutDate = pastCutDate,
            SuccessorTransferId = 2,
            SuccessorWithdrawalFinanceId = 12,
            SuccessorDepositFinanceId = 13,
            SuccessorAmount = 300m,
            SuccessorSchedule = MonthlyFrom(pastCutDate, new DateOnly(2026, 1, 1)),
            CarriedOverWithdrawalJarBalance = 50m,
            AllPatterns = [withdrawal, deposit],
        });

        result.PredecessorTransfer.DatePattern.Until.ShouldBe(new DateOnly(2024, 7, 31));
        result.SuccessorTransfer.DatePattern.Start.ShouldBe(pastCutDate);
        result.SuccessorWithdrawalPlan.StartingAllocation.ShouldBe(50m);
    }
}
