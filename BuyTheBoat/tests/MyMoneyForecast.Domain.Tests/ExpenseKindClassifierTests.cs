using MyMoneyForecast.Domain;
using Shouldly;

namespace MyMoneyForecast.Domain.Tests;

public class ExpenseKindClassifierTests
{
    private static FinancialPattern Pattern(decimal amount, bool? mandatory, int occurrences) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "Test",
            Amount = amount,
            Mandatory = mandatory,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [1],
                Start = new DateOnly(2025, 1, 1),
                Count = occurrences,
            }),
        });

    [Fact]
    public void Positive_amounts_are_paychecks_regardless_of_earmarks()
    {
        ExpenseKindClassifier.Classify(Pattern(1700m, null, 26), hasEarmark: false)
            .ShouldBe(ExpenseKind.Paycheck);
        ExpenseKindClassifier.Classify(Pattern(1700m, null, 26), hasEarmark: true)
            .ShouldBe(ExpenseKind.Paycheck);
    }

    [Fact]
    public void Mandatory_expenses_are_bills_with_or_without_an_explicit_earmark()
    {
        // Mandatory defaults true for negative amounts (FinancialPattern.Create).
        ExpenseKindClassifier.Classify(Pattern(-1200m, null, 12), hasEarmark: false)
            .ShouldBe(ExpenseKind.Bill);
        ExpenseKindClassifier.Classify(Pattern(-1200m, true, 12), hasEarmark: true)
            .ShouldBe(ExpenseKind.Bill);
    }

    [Fact]
    public void A_non_mandatory_single_occurrence_with_a_savings_plan_is_a_one_time_goal()
    {
        // The OneTimeGoalFactory shape: discretionary, one occurrence, earmarked.
        ExpenseKindClassifier.Classify(Pattern(-3000m, false, 1), hasEarmark: true)
            .ShouldBe(ExpenseKind.OneTimeGoal);
    }

    [Fact]
    public void A_non_mandatory_repeating_expense_with_a_savings_plan_is_a_repeating_goal()
    {
        ExpenseKindClassifier.Classify(Pattern(-100m, false, 12), hasEarmark: true)
            .ShouldBe(ExpenseKind.RepeatingGoal);
    }

    [Fact]
    public void A_non_mandatory_expense_without_a_savings_plan_is_discretionary()
    {
        ExpenseKindClassifier.Classify(Pattern(-400m, false, 1), hasEarmark: false)
            .ShouldBe(ExpenseKind.Discretionary);
    }
}
