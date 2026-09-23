using Microsoft.Data.Sqlite;
using BuyTheBoat.Domain;
using BuyTheBoat.Persistence;
using Shouldly;

namespace BuyTheBoat.Scenario.Tests;

public class AccountRepositoryTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"buytheboat-test-{Guid.NewGuid()}.db");
    private readonly AccountRepository _repository;

    public AccountRepositoryTests()
    {
        _repository = new AccountRepository(new PatternDatabase(_databasePath));
    }

    private static Account Acct(int id, string name, decimal balance = 0m, decimal cushion = 0m) =>
        Account.Create(new AccountOptions { Id = id, Name = name, Balance = balance, IdealSafetyCushion = cushion });

    [Fact]
    public void A_fresh_database_has_no_accounts()
    {
        _repository.GetAll().ShouldBeEmpty();
        _repository.NextId().ShouldBe(1);
    }

    [Fact]
    public void An_account_round_trips()
    {
        _repository.Save(Acct(1, "Checking", 2400.50m, 500m));

        var account = _repository.GetById(1);
        account.ShouldNotBeNull();
        account.Name.ShouldBe("Checking");
        account.Balance.ShouldBe(2400.50m);
        account.IdealSafetyCushion.ShouldBe(500m);
    }

    [Fact]
    public void Saving_the_same_id_updates_it_in_place()
    {
        _repository.Save(Acct(1, "Checking", 100m));
        _repository.Save(Acct(1, "Everyday Checking", 250m, 50m));

        _repository.GetAll().Count.ShouldBe(1);
        var account = _repository.GetById(1);
        account!.Name.ShouldBe("Everyday Checking");
        account.Balance.ShouldBe(250m);
        account.IdealSafetyCushion.ShouldBe(50m);
    }

    [Fact]
    public void Next_id_advances_past_the_highest_existing_account()
    {
        _repository.Save(Acct(1, "Checking"));
        _repository.Save(Acct(2, "Savings"));

        _repository.NextId().ShouldBe(3);
    }

    [Fact]
    public void Account_names_must_be_unique()
    {
        _repository.Save(Acct(1, "Checking"));

        Should.Throw<SqliteException>(() => _repository.Save(Acct(2, "Checking")));
    }

    [Fact]
    public void Get_by_name_finds_the_account_or_returns_null()
    {
        _repository.Save(Acct(1, "Checking"));
        _repository.Save(Acct(2, "Savings", 5000m));

        _repository.GetByName("Savings")!.Id.ShouldBe(2);
        _repository.GetByName("Nonexistent").ShouldBeNull();
    }

    [Fact]
    public void A_deleted_account_is_gone()
    {
        _repository.Save(Acct(1, "Checking"));
        _repository.Save(Acct(2, "Savings"));

        _repository.Delete(1);

        _repository.GetAll().Count.ShouldBe(1);
        _repository.GetById(1).ShouldBeNull();
    }

    [Fact]
    public void The_first_run_seeds_a_primary_account_from_the_legacy_balance()
    {
        var seeded = _repository.EnsureDefaultAccount(2400m, 500m);

        seeded.Id.ShouldBe(1);
        seeded.Name.ShouldBe("primary");
        seeded.Balance.ShouldBe(2400m);
        seeded.IdealSafetyCushion.ShouldBe(500m);
        _repository.GetAll().Count.ShouldBe(1);
    }

    [Fact]
    public void Seeding_is_idempotent_and_never_overwrites_a_real_account()
    {
        _repository.Save(Acct(1, "Checking", 100m));

        var result = _repository.EnsureDefaultAccount(9999m, 9999m);

        result.Name.ShouldBe("Checking");
        _repository.GetAll().Count.ShouldBe(1);
        _repository.GetById(1)!.Balance.ShouldBe(100m);
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
