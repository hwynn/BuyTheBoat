using BuyTheBoat.Domain;
using Shouldly;

namespace BuyTheBoat.Domain.Tests;

public class AccountTests
{
    private static AccountOptions Valid() => new()
    {
        Id = 1,
        Name = "Checking",
        Balance = 2400m,
        IdealSafetyCushion = 500m,
    };

    [Fact]
    public void Create_keeps_the_values_it_was_given()
    {
        var account = Account.Create(Valid());

        account.Id.ShouldBe(1);
        account.Name.ShouldBe("Checking");
        account.Balance.ShouldBe(2400m);
        account.IdealSafetyCushion.ShouldBe(500m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_cannot_be_empty(string name)
    {
        Should.Throw<ArgumentException>(() => Account.Create(Valid() with { Name = name }));
    }

    [Fact]
    public void Balance_and_cushion_default_to_zero()
    {
        var account = Account.Create(new AccountOptions { Id = 2, Name = "Savings" });

        account.Balance.ShouldBe(0m);
        account.IdealSafetyCushion.ShouldBe(0m);
    }

    [Fact]
    public void Cushion_cannot_be_negative()
    {
        Should.Throw<ArgumentException>(() => Account.Create(Valid() with { IdealSafetyCushion = -1m }));
    }

    [Fact]
    public void Balance_may_be_negative_for_an_overdrawn_account()
    {
        var account = Account.Create(Valid() with { Balance = -50m });

        account.Balance.ShouldBe(-50m);
    }
}
