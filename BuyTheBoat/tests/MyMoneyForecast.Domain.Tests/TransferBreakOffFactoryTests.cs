using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

public class TransferBreakOffFactoryTests
{
    private const int FromAccountId = 1;
    private const int ToAccountId = 2;

    private static (Transfer Transfer, FinancialPattern Withdrawal, FinancialPattern Deposit) MonthlyTransfer(
        decimal amount, DateOnly start, DateOnly until, int transferId = 1, int withdrawalId = 10, int depositId = 11, bool autoRenew = false)
    {
        var schedule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = RecurrenceFrequency.Monthly,
            ByMonthDay = [start.Day],
            DtStart = start,
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
            AutoRenew = autoRenew,
        });

        var deposit = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = depositId,
            Source = "Transfer from Checking",
            Description = "Transfer from Checking",
            DatePattern = schedule,
            Amount = amount,
            Mandatory = false,
            AutoRenew = autoRenew,
        });

        return (transfer, withdrawal, deposit);
    }

    private static RecurrenceRuleOptions MonthlyFrom(DateOnly start, DateOnly until) => new()
    {
        Frequency = RecurrenceFrequency.Monthly,
        ByMonthDay = [start.Day],
        DtStart = start,
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

        result.SuccessorWithdrawal.DatePattern.ActiveStart.ShouldBe(cutDate);
        result.SuccessorDeposit.DatePattern.ActiveStart.ShouldBe(cutDate);
        result.SuccessorTransfer.DatePattern.ActiveStart.ShouldBe(cutDate);
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
        result.SuccessorTransfer.DatePattern.ActiveStart.ShouldBe(pastCutDate);
        result.SuccessorWithdrawalPlan.StartingAllocation.ShouldBe(50m);
    }

    // planning/18 (B12): the lockstep discipline BreakOff already enforces for
    // amount/schedule, extended to the new AutoRenew marker — both legs must
    // always agree, never drift independently.
    [Fact]
    public void BreakOff_rejects_legs_that_disagree_on_auto_renew()
    {
        var (transfer, withdrawal, deposit) = MonthlyTransfer(200m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1), autoRenew: true);
        var driftedDeposit = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = deposit.FinanceId,
            Source = deposit.Source,
            Description = deposit.Description,
            DatePattern = deposit.DatePattern,
            Amount = deposit.Amount,
            Mandatory = false,
            AutoRenew = false, // withdrawal says true — drifted
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
    public void Renew_keeps_both_legs_and_the_transfer_record_moving_together()
    {
        var (transfer, withdrawal, deposit) = MonthlyTransfer(200m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var renewalDate = new DateOnly(2026, 1, 1);

        var result = TransferBreakOffFactory.Renew(new TransferRenewalRequest
        {
            PredecessorTransfer = transfer,
            PredecessorWithdrawal = withdrawal,
            PredecessorWithdrawalPlan = null,
            PredecessorDeposit = deposit,
            RenewalDate = renewalDate,
            SegmentYears = 1,
            SuccessorTransferId = 2,
            SuccessorWithdrawalFinanceId = 12,
            SuccessorDepositFinanceId = 13,
            CarriedOverWithdrawalJarBalance = 0m,
            AllPatterns = [withdrawal, deposit],
        });

        result.SuccessorWithdrawal.DatePattern.ActiveStart.ShouldBe(renewalDate);
        result.SuccessorDeposit.DatePattern.ActiveStart.ShouldBe(renewalDate);
        result.SuccessorTransfer.DatePattern.ActiveStart.ShouldBe(renewalDate);
        result.SuccessorTransfer.DatePattern.Until.ShouldBe(renewalDate.AddYears(1));
        // Unchanged — a renewal is not a change.
        result.SuccessorWithdrawal.Amount.ShouldBe(-200m);
        result.SuccessorDeposit.Amount.ShouldBe(200m);
        result.SuccessorTransfer.Amount.ShouldBe(200m);
    }

    [Fact]
    public void Renew_truncates_the_old_transfer_record_to_match_its_legs()
    {
        var (transfer, withdrawal, deposit) = MonthlyTransfer(200m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var renewalDate = new DateOnly(2026, 1, 1);

        var result = TransferBreakOffFactory.Renew(new TransferRenewalRequest
        {
            PredecessorTransfer = transfer,
            PredecessorWithdrawal = withdrawal,
            PredecessorWithdrawalPlan = null,
            PredecessorDeposit = deposit,
            RenewalDate = renewalDate,
            SegmentYears = 1,
            SuccessorTransferId = 2,
            SuccessorWithdrawalFinanceId = 12,
            SuccessorDepositFinanceId = 13,
            CarriedOverWithdrawalJarBalance = 0m,
            AllPatterns = [withdrawal, deposit],
        });

        result.PredecessorTransfer.DatePattern.Until.ShouldBe(renewalDate.AddDays(-1));
        result.PredecessorWithdrawal.DatePattern.Until.ShouldBe(renewalDate.AddDays(-1));
        result.PredecessorDeposit.DatePattern.Until.ShouldBe(renewalDate.AddDays(-1));
    }

    [Fact]
    public void Renew_carries_the_auto_renew_marker_onto_both_legs_successors()
    {
        // The whole point: a transfer that "keeps going" must still qualify
        // after renewing, on BOTH legs, or the scheduled trigger would stop
        // renewing it after just one cycle.
        var (transfer, withdrawal, deposit) = MonthlyTransfer(200m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1), autoRenew: true);
        var renewalDate = new DateOnly(2026, 1, 1);

        var result = TransferBreakOffFactory.Renew(new TransferRenewalRequest
        {
            PredecessorTransfer = transfer,
            PredecessorWithdrawal = withdrawal,
            PredecessorWithdrawalPlan = null,
            PredecessorDeposit = deposit,
            RenewalDate = renewalDate,
            SegmentYears = 1,
            SuccessorTransferId = 2,
            SuccessorWithdrawalFinanceId = 12,
            SuccessorDepositFinanceId = 13,
            CarriedOverWithdrawalJarBalance = 0m,
            AllPatterns = [withdrawal, deposit],
        });

        result.SuccessorWithdrawal.AutoRenew.ShouldBeTrue();
        result.SuccessorDeposit.AutoRenew.ShouldBeTrue();
    }

    [Fact]
    public void Renew_rejects_a_withdrawal_already_drifted_from_the_transfers_amount()
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

        Should.Throw<ArgumentException>(() => TransferBreakOffFactory.Renew(new TransferRenewalRequest
        {
            PredecessorTransfer = transfer,
            PredecessorWithdrawal = driftedWithdrawal,
            PredecessorWithdrawalPlan = null,
            PredecessorDeposit = deposit,
            RenewalDate = new DateOnly(2026, 1, 1),
            SegmentYears = 1,
            SuccessorTransferId = 2,
            SuccessorWithdrawalFinanceId = 12,
            SuccessorDepositFinanceId = 13,
            CarriedOverWithdrawalJarBalance = 0m,
            AllPatterns = [driftedWithdrawal, deposit],
        }));
    }

    [Fact]
    public void Renew_rejects_legs_that_disagree_on_auto_renew()
    {
        var (transfer, withdrawal, deposit) = MonthlyTransfer(200m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1), autoRenew: true);
        var driftedDeposit = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = deposit.FinanceId,
            Source = deposit.Source,
            Description = deposit.Description,
            DatePattern = deposit.DatePattern,
            Amount = deposit.Amount,
            Mandatory = false,
            AutoRenew = false, // withdrawal says true — drifted
        });

        Should.Throw<ArgumentException>(() => TransferBreakOffFactory.Renew(new TransferRenewalRequest
        {
            PredecessorTransfer = transfer,
            PredecessorWithdrawal = withdrawal,
            PredecessorWithdrawalPlan = null,
            PredecessorDeposit = driftedDeposit,
            RenewalDate = new DateOnly(2026, 1, 1),
            SegmentYears = 1,
            SuccessorTransferId = 2,
            SuccessorWithdrawalFinanceId = 12,
            SuccessorDepositFinanceId = 13,
            CarriedOverWithdrawalJarBalance = 0m,
            AllPatterns = [withdrawal, driftedDeposit],
        }));
    }
}
