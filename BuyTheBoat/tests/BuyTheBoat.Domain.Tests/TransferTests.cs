using BuyTheBoat.Domain;
using Shouldly;

namespace BuyTheBoat.Domain.Tests;

public class TransferTests
{
    private static RecurrenceRule Monthly() => RecurrenceRule.Create(new RecurrenceRuleOptions
    {
        Frequency = RecurrenceFrequency.Monthly,
        ByMonthDay = [1],
        DtStart = new DateOnly(2025, 1, 1),
        Until = new DateOnly(2027, 1, 1),
    });

    private static TransferOptions ValidOptions() => new()
    {
        Id = 1,
        FromAccountId = 1,
        ToAccountId = 2,
        Amount = 500m,
        DatePattern = Monthly(),
    };

    private static TransferRequest Request() => new()
    {
        TransferId = 1,
        WithdrawalFinanceId = 10,
        DepositFinanceId = 11,
        FromAccountId = 1,
        ToAccountId = 2,
        FromAccountName = "Checking",
        ToAccountName = "Savings",
        Amount = 500m,
        DatePattern = Monthly(),
    };

    [Fact]
    public void A_transfer_must_be_between_two_different_accounts()
    {
        Should.Throw<ArgumentException>(() => Transfer.Create(ValidOptions() with { ToAccountId = 1 }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25)]
    public void A_transfer_amount_must_be_positive(int amount)
    {
        Should.Throw<ArgumentException>(() => Transfer.Create(ValidOptions() with { Amount = amount }));
    }

    [Fact]
    public void Create_produces_a_withdrawal_and_a_matching_deposit()
    {
        var result = TransferFactory.Create(Request());

        result.Transfer.Amount.ShouldBe(500m);
        result.Withdrawal.Amount.ShouldBe(-500m);
        result.Deposit.Amount.ShouldBe(500m);
        result.Withdrawal.Description.ShouldBe("Transfer to Savings");
        result.Deposit.Description.ShouldBe("Transfer from Checking");
    }

    [Fact]
    public void Both_patterns_are_non_mandatory_so_a_transfer_never_auto_reserves()
    {
        var result = TransferFactory.Create(Request());

        result.Withdrawal.Mandatory.ShouldBeFalse();
        result.Deposit.Mandatory.ShouldBeFalse();
    }

    [Fact]
    public void Both_patterns_share_the_transfers_schedule()
    {
        var result = TransferFactory.Create(Request());

        var schedule = result.Transfer.DatePattern.ToRruleString();
        result.Withdrawal.DatePattern.ToRruleString().ShouldBe(schedule);
        result.Deposit.DatePattern.ToRruleString().ShouldBe(schedule);
    }
}
