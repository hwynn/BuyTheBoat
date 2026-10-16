using BuyTheBoat.Domain;
using Shouldly;

namespace BuyTheBoat.App.Tests;

// Covers FinancePatternSaveConfirmation.WillBreakOff, which the Expense form uses to decide whether its status can preview an edit in place.
public class WillBreakOffTests
{
    private static readonly DateOnly AsOf = new(2026, 6, 15);

    /// <summary>[CALC] Builds a monthly rent pattern running from the given start through year-end.</summary>
    private static FinancialPattern Rent(int financeId, DateOnly start, decimal amount = -1000m, DateOnly? until = null, string? description = null) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = financeId,
            Source = "Rent",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                DtStart = start,
                Until = until ?? new DateOnly(2026, 12, 31),
            }),
            Amount = amount,
            Description = description,
        });

    [Fact]
    public void An_amount_change_on_a_bill_that_has_not_come_due_yet_saves_in_place()
    {
        var saved = Rent(1, new DateOnly(2026, 7, 1));

        FinancePatternSaveConfirmation.WillBreakOff(saved, Rent(1, new DateOnly(2026, 7, 1), -1100m), [saved], AsOf).ShouldBeFalse();
    }

    [Fact]
    public void An_amount_change_on_a_bill_that_already_came_due_breaks_off()
    {
        var saved = Rent(1, new DateOnly(2026, 1, 1));

        FinancePatternSaveConfirmation.WillBreakOff(saved, Rent(1, new DateOnly(2026, 1, 1), -1100m), [saved], AsOf).ShouldBeTrue();
    }

    [Fact]
    public void An_amount_change_on_an_earlier_segment_with_a_later_one_saves_in_place()
    {
        var earlier = Rent(1, new DateOnly(2026, 1, 1), until: new DateOnly(2026, 8, 31));
        var later = Rent(2, new DateOnly(2026, 9, 1));
        var typed = Rent(1, new DateOnly(2026, 1, 1), -1100m, until: new DateOnly(2026, 8, 31));

        FinancePatternSaveConfirmation.WillBreakOff(earlier, typed, [earlier, later], AsOf).ShouldBeFalse();
    }

    [Fact]
    public void A_description_change_on_a_bill_that_already_came_due_saves_in_place()
    {
        var saved = Rent(1, new DateOnly(2026, 1, 1));
        var typed = Rent(1, new DateOnly(2026, 1, 1), description: "Apartment");

        FinancePatternSaveConfirmation.WillBreakOff(saved, typed, [saved], AsOf).ShouldBeFalse();
    }
}
