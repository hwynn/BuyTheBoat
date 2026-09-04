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

    [Fact]
    public void FirstPaymentCoverageLine_is_null_when_the_plan_already_covers_the_first_payment()
    {
        PlanHealthMessages.FirstPaymentCoverageLine(
            isFirstOccurrencePending: true, firstPaymentAmount: 400m, setAsideShortfall: 0m,
            freeFunds: 900m, paymentDate: new DateOnly(2026, 3, 8), isOneTime: false)
            .ShouldBeNull();
    }

    [Fact]
    public void FirstPaymentCoverageLine_is_null_once_the_first_occurrence_has_happened()
    {
        PlanHealthMessages.FirstPaymentCoverageLine(
            isFirstOccurrencePending: false, firstPaymentAmount: 400m, setAsideShortfall: 300m,
            freeFunds: 50m, paymentDate: new DateOnly(2026, 3, 8), isOneTime: false)
            .ShouldBeNull();
    }

    [Fact]
    public void FirstPaymentCoverageLine_is_tentative_and_invites_allocating_when_free_cash_probably_covers_the_gap()
    {
        // $300 of the $400 payment isn't set aside, but $900 free cash covers it — so it hedges
        // ("probably") and nudges toward setting it aside ("up to $X could be allocated").
        var line = PlanHealthMessages.FirstPaymentCoverageLine(
            isFirstOccurrencePending: true, firstPaymentAmount: 400m, setAsideShortfall: 300m,
            freeFunds: 900m, paymentDate: new DateOnly(2026, 3, 8), isOneTime: false);

        line.ShouldNotBeNull();
        line.ShouldContain("probably");                          // tentative, not a promise
        line.ShouldContain("\n");                                 // two-line block: verdict + facts
        line.ShouldContain("$100 of $400 set aside");             // set aside so far: $400 − $300
        line.ShouldContain("up to $900 could be allocated");      // the nudge, not "$900 free of …"
    }

    [Fact]
    public void FirstPaymentCoverageLine_is_firm_and_dated_when_even_free_cash_falls_short()
    {
        // $300 unearmarked, only $50 free — still $250 short even after free cash: the urgent case,
        // stamped with the payment date.
        var line = PlanHealthMessages.FirstPaymentCoverageLine(
            isFirstOccurrencePending: true, firstPaymentAmount: 400m, setAsideShortfall: 300m,
            freeFunds: 50m, paymentDate: new DateOnly(2026, 3, 8), isOneTime: false);

        line.ShouldNotBeNull();
        line.ShouldContain("Mar 8");                              // the urgency: when
        line.ShouldContain("$250 short");                         // how far short after free cash
        line.ShouldContain("even after free cash");
        line.ShouldContain("only $50 free");
        line.ShouldNotContain("probably");                        // firm, not hedged
    }

    [Fact]
    public void FirstPaymentCoverageLine_falls_back_to_the_plain_gap_when_free_funds_are_unknown()
    {
        // The no-forecast preview path: with free funds unknown, one plain line, no facts strip.
        var line = PlanHealthMessages.FirstPaymentCoverageLine(
            isFirstOccurrencePending: true, firstPaymentAmount: 400m, setAsideShortfall: 300m,
            freeFunds: null, paymentDate: null, isOneTime: false);

        line.ShouldBe("$300 short for the first payment");
    }
}
