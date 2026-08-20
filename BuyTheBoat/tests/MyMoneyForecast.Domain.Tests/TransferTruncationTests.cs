using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

public class TransferTruncationTests
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

    private static EarMarkPattern MonthlyPlan(FinancialPattern goal, decimal amount, DateOnly start, DateOnly until) =>
        EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                Amount = amount,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    ByMonthDay = [start.Day],
                    DtStart = start,
                    Until = until,
                }),
            },
            goal);

    [Fact]
    public void Ends_the_transfer_record_and_both_legs_on_the_chosen_date()
    {
        var (transfer, withdrawal, deposit) = MonthlyTransfer(200m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var lastDay = new DateOnly(2025, 6, 30);

        var result = TransferTruncation.EndOn(transfer, withdrawal, withdrawalPlan: null, deposit, lastDay);

        result.Transfer.DatePattern.Until.ShouldBe(lastDay);
        result.Withdrawal.DatePattern.Until.ShouldBe(lastDay);
        result.Deposit.DatePattern.Until.ShouldBe(lastDay);
    }

    [Fact]
    public void Truncates_the_withdrawals_savings_plan_too_when_it_has_one()
    {
        var (transfer, withdrawal, deposit) = MonthlyTransfer(200m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var plan = MonthlyPlan(withdrawal, -200m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var lastDay = new DateOnly(2025, 6, 30);

        var result = TransferTruncation.EndOn(transfer, withdrawal, plan, deposit, lastDay);

        result.WithdrawalPlan.ShouldNotBeNull();
        result.WithdrawalPlan!.DatePattern.Until.ShouldBe(lastDay);
    }

    [Fact]
    public void A_withdrawal_plan_that_already_ended_earlier_than_the_new_cutoff_keeps_its_own_earlier_end()
    {
        var (transfer, withdrawal, deposit) = MonthlyTransfer(200m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));
        var plan = MonthlyPlan(withdrawal, -200m, new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 1));

        var result = TransferTruncation.EndOn(transfer, withdrawal, plan, deposit, new DateOnly(2025, 6, 30));

        result.WithdrawalPlan!.DatePattern.Until.ShouldBe(new DateOnly(2025, 4, 1));
    }

    [Fact]
    public void A_withdrawal_with_no_plan_truncates_cleanly_with_a_null_plan_result()
    {
        var (transfer, withdrawal, deposit) = MonthlyTransfer(200m, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1));

        var result = TransferTruncation.EndOn(transfer, withdrawal, withdrawalPlan: null, deposit, new DateOnly(2025, 6, 30));

        result.WithdrawalPlan.ShouldBeNull();
    }

    [Fact]
    public void An_end_date_before_the_transfers_own_start_is_rejected()
    {
        var (transfer, withdrawal, deposit) = MonthlyTransfer(200m, new DateOnly(2025, 6, 1), new DateOnly(2026, 1, 1));

        Should.Throw<ArgumentException>(() =>
            TransferTruncation.EndOn(transfer, withdrawal, withdrawalPlan: null, deposit, new DateOnly(2025, 1, 1)));
    }

    [Fact]
    public void A_withdrawal_already_drifted_from_the_transfers_amount_is_rejected()
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

        Should.Throw<ArgumentException>(() =>
            TransferTruncation.EndOn(transfer, driftedWithdrawal, withdrawalPlan: null, deposit, new DateOnly(2025, 6, 30)));
    }

    [Fact]
    public void A_deposit_already_drifted_from_the_transfers_amount_is_rejected()
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

        Should.Throw<ArgumentException>(() =>
            TransferTruncation.EndOn(transfer, withdrawal, withdrawalPlan: null, driftedDeposit, new DateOnly(2025, 6, 30)));
    }
}
