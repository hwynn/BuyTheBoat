using Microsoft.Data.Sqlite;
using MyMoneyForecast.Domain;
using MyMoneyForecast.Persistence;
using Shouldly;

namespace MyMoneyForecast.Scenario.Tests;

public class TransferRepositoryTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"mymoneyforecast-test-{Guid.NewGuid()}.db");
    private readonly FinancialPatternRepository _financialPatterns;
    private readonly TransferRepository _transfers;

    public TransferRepositoryTests()
    {
        var database = new PatternDatabase(_databasePath);
        _financialPatterns = new FinancialPatternRepository(database);
        _transfers = new TransferRepository(database, _financialPatterns);
    }

    private static RecurrenceRule Monthly() => RecurrenceRule.Create(new RecurrenceRuleOptions
    {
        Frequency = RecurrenceFrequency.Monthly,
        ByMonthDay = [1],
        Start = new DateOnly(2025, 1, 1),
        Until = new DateOnly(2027, 1, 1),
    });

    private static TransferResult Transfer(int id = 1, int outId = 100, int inId = 101, int from = 1, int to = 2, decimal amount = 500m) =>
        TransferFactory.Create(new TransferRequest
        {
            TransferId = id,
            WithdrawalFinanceId = outId,
            DepositFinanceId = inId,
            FromAccountId = from,
            ToAccountId = to,
            FromAccountName = "Checking",
            ToAccountName = "Savings",
            Amount = amount,
            DatePattern = Monthly(),
        });

    [Fact]
    public void Saving_a_transfer_stores_the_record_and_both_patterns()
    {
        _transfers.Save(Transfer());

        _transfers.GetAll().Count.ShouldBe(1);
        _financialPatterns.GetAll().Count.ShouldBe(2);                 // the two patterns are real patterns
        _financialPatterns.GetAllExcludingTransferPatterns().ShouldBeEmpty(); // but hidden from the pattern list
    }

    [Fact]
    public void The_withdrawal_and_deposit_file_under_the_from_and_to_accounts()
    {
        _transfers.Save(Transfer(from: 1, to: 2));

        var byAccount = _financialPatterns.GetAllByAccount();
        byAccount[1].Single().Amount.ShouldBe(-500m); // withdrawal in the source account
        byAccount[2].Single().Amount.ShouldBe(500m);  // deposit in the destination account
    }

    [Fact]
    public void A_saved_transfer_round_trips_its_definition()
    {
        _transfers.Save(Transfer(amount: 750m));

        var transfer = _transfers.GetAll().Single();
        transfer.FromAccountId.ShouldBe(1);
        transfer.ToAccountId.ShouldBe(2);
        transfer.Amount.ShouldBe(750m);
    }

    // planning/14 item A-1 / finding F10: the engine needs to know which
    // patterns are a transfer's WITHDRAWAL so the household roll-up can add
    // that reservation back into free. Only the outgoing half counts — the
    // deposit never reserves anything.
    [Fact]
    public void Only_the_withdrawal_half_of_a_transfer_is_reported_as_reserving()
    {
        _transfers.Save(Transfer(from: 1, to: 2, amount: 500m));

        var withdrawalIds = _financialPatterns.GetTransferWithdrawalFinanceIds();

        var withdrawal = _financialPatterns.GetAllByAccount()[1].Single();
        var deposit = _financialPatterns.GetAllByAccount()[2].Single();

        withdrawalIds.ShouldBe(new[] { withdrawal.FinanceId });
        withdrawalIds.ShouldNotContain(deposit.FinanceId);
    }

    [Fact]
    public void An_ordinary_bill_is_never_reported_as_a_transfer_withdrawal()
    {
        // Ordinary patterns carry no TransferId, so a plain bill — negative
        // like a withdrawal — must not be mistaken for one.
        _financialPatterns.Save(
            FinancialPattern.Create(new FinancialPatternOptions
            {
                FinanceId = 90,
                Source = "Rent",
                Amount = -1200m,
                DatePattern = Monthly(),
            }),
            accountId: 1);

        _financialPatterns.GetTransferWithdrawalFinanceIds().ShouldBeEmpty();
    }

    [Fact]
    public void Deleting_a_transfer_removes_both_patterns()
    {
        _transfers.Save(Transfer());

        _transfers.Delete(1);

        _transfers.GetAll().ShouldBeEmpty();
        _financialPatterns.GetAll().ShouldBeEmpty();
    }

    [Fact]
    public void Next_id_advances_past_the_highest_transfer()
    {
        _transfers.Save(Transfer(id: 1));

        _transfers.NextId().ShouldBe(2);
    }

    [Fact]
    public void An_account_a_transfer_touches_is_flagged_referenced()
    {
        _transfers.Save(Transfer(from: 1, to: 2));

        _transfers.IsAccountReferenced(1).ShouldBeTrue();
        _transfers.IsAccountReferenced(2).ShouldBeTrue();
        _transfers.IsAccountReferenced(3).ShouldBeFalse();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
