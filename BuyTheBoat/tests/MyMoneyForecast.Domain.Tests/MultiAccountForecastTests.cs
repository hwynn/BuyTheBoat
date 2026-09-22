using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

// The engine partition (the multi-account design): one page per account, plus the
// household roll-up that surfaces a locally-short account even when the
// combined total stays positive ("enough in the right account").
public class MultiAccountForecastTests
{
    private static readonly DateOnly AsOf = new(2026, 7, 23);
    private static readonly DateOnly ExpenseDay = new(2026, 8, 1);

    // A single-occurrence discretionary expense with no Allocation Plan. It
    // doesn't reserve ahead — it just reduces free funds on its
    // due date. (In the app an outflow is given a plan at creation; a test that
    // wants a reservation adds one explicitly, as the transfer test below does.)
    private static FinancialPattern OneOffExpense(int id, decimal amount) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = id,
            Source = "One-off",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = ExpenseDay,
                Count = 1,
            }),
            Amount = amount,
            Mandatory = false,
        });

    // Checking holds $1,000 and takes a $1,500 hit (goes short); Savings holds
    // $5,000 and is untouched.
    private static ForecastResult TwoAccounts() =>
        TransactionLogBookFactory.CreateForecast(new ForecastOptions
        {
            FinancialPatterns = [],  // ignored when Accounts is provided
            EarMarkPatterns = [],
            StartingBalance = 0m,
            AsOfDate = AsOf,
            HorizonEndDate = new DateOnly(2026, 12, 1),
            Accounts =
            [
                new AccountForecastInput
                {
                    AccountId = 1,
                    Name = "Checking",
                    StartingBalance = 1000m,
                    FinancialPatterns = [OneOffExpense(10, -1500m)],
                    EarMarkPatterns = [],
                },
                new AccountForecastInput
                {
                    AccountId = 2,
                    Name = "Savings",
                    StartingBalance = 5000m,
                    FinancialPatterns = [],
                    EarMarkPatterns = [],
                },
            ],
        });

    [Fact]
    public void Each_account_gets_its_own_page()
    {
        var result = TwoAccounts();

        result.Book.LogPages[0].AccountPages.Keys.OrderBy(key => key).ToArray()
            .ShouldBe(new[] { "Checking", "Savings" });
        result.Accounts.Select(account => account.Name).ToArray()
            .ShouldBe(new[] { "Checking", "Savings" });
    }

    [Fact]
    public void The_household_as_of_free_is_the_sum_of_the_accounts()
    {
        // 1000 (Checking — the August expense has no Allocation Plan, so it is
        // not reserved ahead) + 5000 (Savings). The expense only presses on free
        // funds on its due date.
        TwoAccounts().Household.AsOfFree.ShouldBe(6000m);
    }

    [Fact]
    public void A_short_account_is_flagged_even_though_the_household_total_stays_positive()
    {
        var day = TwoAccounts().Household.Days.Single(entry => entry.Date == ExpenseDay);

        day.Free.ShouldBe(4500m);                        // (1000 - 1500) + 5000 — still positive...
        day.AnyAccountShort.ShouldBeTrue();               // ...yet Checking is short
        day.ShortAccounts.ShouldBe(new[] { "Checking" });
    }

    // A transfer reserves in the account it leaves, and that reservation lowers
    // free the same as any other outflow's — in the account's own free and in
    // the combined free across accounts. Free means money not set aside for
    // anything, transfers included.
    [Fact]
    public void A_transfer_reservation_lowers_free_in_its_account_and_across_accounts()
    {
        // A transfer withdrawal reserves through its own Allocation Plan (what
        // TransferFactory creates at transfer time) — here a single up-front
        // contribution of the full amount, the front-loaded shape for a one-off
        // with no income.
        var withdrawal = OneOffExpense(20, -300m).WithActiveFrom(AsOf);
        var withdrawalPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = 20,
                DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
                {
                    Frequency = RecurrenceFrequency.Yearly,
                    DtStart = AsOf,
                    Count = 1,
                }),
                Amount = -300m,
            },
            withdrawal);

        var result = TransactionLogBookFactory.CreateForecast(new ForecastOptions
        {
            FinancialPatterns = [],
            EarMarkPatterns = [],
            StartingBalance = 0m,
            AsOfDate = AsOf,
            HorizonEndDate = new DateOnly(2026, 12, 1),
            Accounts =
            [
                new AccountForecastInput
                {
                    AccountId = 1,
                    Name = "Checking",
                    StartingBalance = 1000m,
                    FinancialPatterns = [withdrawal],
                    EarMarkPatterns = [withdrawalPlan],
                },
                new AccountForecastInput
                {
                    AccountId = 2,
                    Name = "Savings",
                    StartingBalance = 5000m,
                    FinancialPatterns = [OneOffExpense(21, 300m)],  // the matching deposit
                    EarMarkPatterns = [],
                },
            ],
        });

        // Checking's OWN free is down by the money committed to leaving.
        var checking = result.Accounts.Single(account => account.Name == "Checking");
        checking.Page.InitialSnapshot.ExpectedFreeAmount.ShouldBe(700m);

        // Combined free reflects that reservation too: 700 (Checking) + 5000
        // (Savings), not the full 6000 — the reserved 300 is not free.
        result.Household.AsOfFree.ShouldBe(5700m);
    }

    // The middle warning state — not over-committed, but
    // the buffer took the hit.
    [Fact]
    public void An_account_whose_cushion_is_not_whole_is_flagged_separately_from_being_short()
    {
        var result = TransactionLogBookFactory.CreateForecast(new ForecastOptions
        {
            FinancialPatterns = [],
            EarMarkPatterns = [],
            StartingBalance = 0m,
            AsOfDate = AsOf,
            HorizonEndDate = new DateOnly(2026, 12, 1),
            Accounts =
            [
                new AccountForecastInput
                {
                    AccountId = 1,
                    Name = "Checking",
                    StartingBalance = 1000m,
                    IdealSafetyCushion = 200m,
                    FinancialPatterns = [OneOffExpense(30, -900m)],
                    EarMarkPatterns = [],
                },
            ],
        });

        var day = result.Household.Days.Single(entry => entry.Date == ExpenseDay);

        day.ShortAccounts.ShouldBeEmpty();                              // solvent...
        day.CushionDippedAccounts.ShouldBe(new[] { "Checking" });       // ...but the buffer gave way
        day.AnyCushionDipped.ShouldBeTrue();
    }

    [Fact]
    public void A_cushion_of_zero_can_never_report_as_dipped()
    {
        // The state is inert for anyone who hasn't set a cushion:
        // 0 cannot sit below 0, so the middle warning never fires.
        foreach (var day in TwoAccounts().Household.Days)
        {
            day.CushionDippedAccounts.ShouldBeEmpty();
        }
    }

    [Fact]
    public void Only_the_account_that_went_short_reports_a_negative_date()
    {
        var result = TwoAccounts();

        // The expense day, not the as-of date: the August expense has no
        // Allocation Plan, so it isn't reserved ahead — Checking only goes
        // negative when it lands.
        result.Accounts.Single(account => account.Name == "Checking")
            .FirstNegativeFreeBalanceDate.ShouldBe(ExpenseDay);
        result.Accounts.Single(account => account.Name == "Savings")
            .FirstNegativeFreeBalanceDate.ShouldBeNull();
        result.HasNegativeFreeBalance.ShouldBeTrue();
    }
}
