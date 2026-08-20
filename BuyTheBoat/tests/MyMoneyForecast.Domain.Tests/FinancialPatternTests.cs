using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

public class FinancialPatternTests
{
    private static RecurrenceRule Biweekly() => RecurrenceRule.Create(new RecurrenceRuleOptions
    {
        Frequency = RecurrenceFrequency.Daily,
        DtStart = new DateOnly(2025, 1, 24),
        Interval = 14,
        Until = new DateOnly(2027, 12, 25),
    });

    [Fact]
    public void Positive_amount_defaults_to_not_mandatory_a_paycheck()
    {
        var paycheck = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Employer Inc",
            DatePattern = Biweekly(),
            Amount = 2073.40m,
        });

        paycheck.Mandatory.ShouldBeFalse();
    }

    [Fact]
    public void Negative_amount_defaults_to_mandatory_a_bill()
    {
        var bill = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 2,
            Source = "City Power",
            DatePattern = Biweekly(),
            Amount = -70m,
        });

        bill.Mandatory.ShouldBeTrue();
    }

    [Fact]
    public void Explicit_mandatory_overrides_the_amount_based_default()
    {
        var optionalBigExpense = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 3,
            Source = "Game Shack",
            DatePattern = Biweekly(),
            Amount = -560m,
            Mandatory = false,
        });

        optionalBigExpense.Mandatory.ShouldBeFalse();
    }

    [Fact]
    public void Empty_source_is_rejected()
    {
        Should.Throw<ArgumentException>(() => FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 4,
            Source = "   ",
            DatePattern = Biweekly(),
            Amount = 100m,
        }));
    }
}
