using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

// The engine partition (planning/10 item 4): one page per account, plus the
// household roll-up that surfaces a locally-short account even when the
// combined total stays positive ("enough in the right account").
public class MultiAccountForecastTests
{
    private static readonly DateOnly AsOf = new(2026, 7, 23);
    private static readonly DateOnly ExpenseDay = new(2026, 8, 1);

    // A single-occurrence discretionary expense — non-mandatory means no
    // automatic reservation, so the numbers stay simple to reason about.
    private static FinancialPattern OneOffExpense(int id, decimal amount) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = id,
            Source = "One-off",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                Start = ExpenseDay,
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
        TwoAccounts().Household.AsOfFree.ShouldBe(6000m); // 1000 + 5000
    }

    [Fact]
    public void A_short_account_is_flagged_even_though_the_household_total_stays_positive()
    {
        var day = TwoAccounts().Household.Days.Single(entry => entry.Date == ExpenseDay);

        day.Free.ShouldBe(4500m);                        // (1000 - 1500) + 5000 — still positive...
        day.AnyAccountShort.ShouldBeTrue();               // ...yet Checking is short
        day.ShortAccounts.ShouldBe(new[] { "Checking" });
    }

    [Fact]
    public void Only_the_account_that_went_short_reports_a_negative_date()
    {
        var result = TwoAccounts();

        result.Accounts.Single(account => account.Name == "Checking")
            .FirstNegativeFreeBalanceDate.ShouldBe(ExpenseDay);
        result.Accounts.Single(account => account.Name == "Savings")
            .FirstNegativeFreeBalanceDate.ShouldBeNull();
        result.HasNegativeFreeBalance.ShouldBeTrue();
    }
}
