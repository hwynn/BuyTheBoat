namespace BuyTheBoat.Domain;

/// <summary>
/// Average monthly money in and out of one account, read from its forecast.
/// Nothing calls these yet; they're meant for a future overview of how balanced each account's money flow is.
/// </summary>
/// <remarks>
/// Transfers count on both sides, because each one is an ordinary withdrawal pattern on the source account and a deposit pattern on the destination.
/// One-time goals are left out, since a single large payout would distort the monthly average.
/// </remarks>
public static class AccountMoneyFlow
{
    // Average days per month in the Gregorian calendar.
    private const decimal DaysPerMonth = 365.2425m / 12m;

    /// <summary>[CALC] Works out how much money leaves this account in an average month between two dates (spending plus transfers out, one-time goals excluded), as a positive amount.</summary>
    /// <remarks>TODO: unused — find a good place to show this; it may need a new page or tab.</remarks>
    /// <param name="from">First day of the window, inclusive.</param>
    /// <param name="to">Last day of the window, inclusive.</param>
    public static decimal AverageMonthlyOutflow(AccountTransactionPage page, DateOnly from, DateOnly to) =>
        -CountedAmounts(page, from, to).Where(amount => amount < 0m).Sum() / MonthsBetween(from, to);

    /// <summary>[CALC] Works out how much money comes into this account in an average month between two dates (earnings plus transfers in).</summary>
    /// <remarks>TODO: unused — find a good place to show this; it may need a new page or tab.</remarks>
    /// <param name="from">First day of the window, inclusive.</param>
    /// <param name="to">Last day of the window, inclusive.</param>
    public static decimal AverageMonthlyInflow(AccountTransactionPage page, DateOnly from, DateOnly to) =>
        CountedAmounts(page, from, to).Where(amount => amount > 0m).Sum() / MonthsBetween(from, to);

    /// <summary>[CALC] Lists the signed amounts of this account's planned transactions in the window, skipping cancelled ones and one-time goals.</summary>
    private static IEnumerable<decimal> CountedAmounts(AccountTransactionPage page, DateOnly from, DateOnly to)
    {
        var patternByFinanceId = page.FinancePatterns.ToDictionary(pattern => pattern.FinanceId);
        var earmarkedIds = page.EarmarkPatterns.Select(earmark => earmark.FinanceId).ToHashSet();

        return page.BalanceRecord
            .Where(entry => entry.Key >= from && entry.Key <= to)
            .SelectMany(entry => entry.Value.ExpectedTransactions)
            .Where(expected => !expected.Cancelled)
            .Where(expected => !patternByFinanceId.TryGetValue(expected.FinanceId, out var pattern)
                || ExpenseKindClassifier.Classify(pattern, earmarkedIds.Contains(expected.FinanceId)) != ExpenseKind.OneTimeGoal)
            .Select(expected => expected.ExpectedAmount);
    }

    /// <summary>[CALC] Turns a date window into a month count, so a window that starts or ends mid-month still averages fairly.</summary>
    private static decimal MonthsBetween(DateOnly from, DateOnly to)
    {
        // A backwards window would divide by zero or flip the sign.
        if (to < from)
        {
            throw new ArgumentException("The window must not end before it starts.", nameof(to));
        }

        var days = to.DayNumber - from.DayNumber + 1;
        return days / DaysPerMonth;
    }
}
