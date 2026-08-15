using Shouldly;

namespace MyMoneyForecast.App.Tests;

public class PlanHealthMessagesTests
{
    [Fact]
    public void IsPaused_is_true_when_the_amount_is_zero_even_with_occurrences_scheduled()
    {
        PlanHealthMessages.IsPaused(amount: 0m, occurrenceCount: 12).ShouldBeTrue();
    }

    [Fact]
    public void IsPaused_is_true_when_there_are_no_occurrences_even_with_a_nonzero_amount()
    {
        PlanHealthMessages.IsPaused(amount: 50m, occurrenceCount: 0).ShouldBeTrue();
    }

    [Fact]
    public void IsPaused_is_false_for_an_ordinary_active_plan()
    {
        PlanHealthMessages.IsPaused(amount: 50m, occurrenceCount: 12).ShouldBeFalse();
    }
}
