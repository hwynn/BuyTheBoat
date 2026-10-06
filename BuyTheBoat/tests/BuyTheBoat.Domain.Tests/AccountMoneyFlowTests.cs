using BuyTheBoat.Domain;
using Shouldly;

namespace BuyTheBoat.Domain.Tests;

// Covers the average-monthly money in/out calculations over an account's forecast.
public class AccountMoneyFlowTests
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly DateOnly Dec31 = new(2026, 12, 31);

    /// <summary>[CALC] Builds a monthly pattern of the given amount; negative is money out, positive is money in.</summary>
    private static FinancialPattern Monthly(int financeId, decimal amount) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = financeId,
            Source = $"pattern-{financeId}",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                DtStart = Jan1,
                Until = Dec31,
            }),
            Amount = amount,
        });

    /// <summary>[CALC] Builds a page whose snapshots hold one expected transaction per occurrence of each pattern.</summary>
    private static AccountTransactionPage Page(
        IReadOnlyList<FinancialPattern> patterns,
        IReadOnlyList<EarMarkPattern>? earmarks = null,
        IReadOnlyList<ExpectedTransaction>? extra = null)
    {
        var expected = patterns
            .SelectMany(pattern => pattern.DatePattern.GetOccurrences().Select(date => new ExpectedTransaction
            {
                FinanceId = pattern.FinanceId,
                ExpectedDate = date,
                ExpectedAmount = pattern.Amount,
            }))
            .Concat(extra ?? []);

        var record = new SortedDictionary<DateOnly, BalanceSnapshot>(expected
            .GroupBy(transaction => transaction.ExpectedDate)
            .ToDictionary(group => group.Key, group => Snapshot(group.Key, group.ToList())));

        return new AccountTransactionPage
        {
            Account = "checking",
            StartDate = Jan1,
            EndDate = Dec31,
            FinancePatterns = patterns,
            EarmarkPatterns = earmarks ?? [],
            InitialSnapshot = Snapshot(null, []),
            BalanceRecord = record,
        };
    }

    /// <summary>[CALC] Builds a snapshot holding just the given expected transactions.</summary>
    private static BalanceSnapshot Snapshot(DateOnly? date, IReadOnlyList<ExpectedTransaction> expected) => new()
    {
        SnapshotDate = date,
        FullAmount = null,
        ExpectedAmount = 0m,
        ExpectedFreeAmount = 0m,
        FundJars = [],
        ActualTransactions = [],
        ExpectedTransactions = expected,
        EarMarkEvents = [],
    };

    [Fact]
    public void A_full_year_of_monthly_patterns_averages_to_one_occurrence_each_way()
    {
        var page = Page([Monthly(1, 2000m), Monthly(2, -500m), Monthly(3, -300m)]);

        AccountMoneyFlow.AverageMonthlyInflow(page, Jan1, Dec31).ShouldBe(2000m, tolerance: 5m);
        AccountMoneyFlow.AverageMonthlyOutflow(page, Jan1, Dec31).ShouldBe(800m, tolerance: 5m);
    }

    [Fact]
    public void Transactions_outside_the_window_are_not_counted()
    {
        var page = Page([Monthly(1, -100m)]);

        // Jan 1 – Jun 30 holds six of the twelve payments over roughly six months.
        AccountMoneyFlow.AverageMonthlyOutflow(page, Jan1, new DateOnly(2026, 6, 30)).ShouldBe(100m, tolerance: 1m);
    }

    [Fact]
    public void One_time_goal_payouts_are_left_out()
    {
        var goal = OneTimeGoalFactory.Create(new OneTimeGoalRequest
        {
            FinanceId = 9,
            Description = "trip to Japan",
            AmountNeeded = 6000m,
            DueDate = new DateOnly(2026, 8, 1),
            StartSavingDate = Jan1,
        });
        var page = Page([Monthly(1, -100m), goal.Goal], [goal.SavingsPlan]);

        AccountMoneyFlow.AverageMonthlyOutflow(page, Jan1, Dec31).ShouldBe(100m, tolerance: 1m);
    }

    [Fact]
    public void Cancelled_transactions_are_left_out()
    {
        var cancelled = new ExpectedTransaction
        {
            FinanceId = 1,
            ExpectedDate = new DateOnly(2026, 3, 15),
            ExpectedAmount = -1200m,
            Cancelled = true,
        };
        var page = Page([Monthly(1, -100m)], extra: [cancelled]);

        AccountMoneyFlow.AverageMonthlyOutflow(page, Jan1, Dec31).ShouldBe(100m, tolerance: 1m);
    }

    [Fact]
    public void A_window_ending_before_it_starts_is_rejected()
    {
        var page = Page([Monthly(1, -100m)]);

        Should.Throw<ArgumentException>(() => AccountMoneyFlow.AverageMonthlyOutflow(page, Dec31, Jan1));
    }
}
