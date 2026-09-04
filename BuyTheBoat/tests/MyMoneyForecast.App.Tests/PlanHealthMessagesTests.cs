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
            freeFunds: 900m, balance: 1000m, isOneTime: false)
            .ShouldBeNull();
    }

    [Fact]
    public void FirstPaymentCoverageLine_is_null_once_the_first_occurrence_has_happened()
    {
        PlanHealthMessages.FirstPaymentCoverageLine(
            isFirstOccurrencePending: false, firstPaymentAmount: 400m, setAsideShortfall: 300m,
            freeFunds: 100m, balance: 1000m, isOneTime: false)
            .ShouldBeNull();
    }

    [Fact]
    public void FirstPaymentCoverageLine_is_tentative_when_free_cash_probably_covers_the_unearmarked_gap()
    {
        // $300 of the $400 payment isn't set aside, but $900 free cash covers it — so it hedges
        // ("probably"), since the naive free figure can't see another same-day claim on that cash.
        var line = PlanHealthMessages.FirstPaymentCoverageLine(
            isFirstOccurrencePending: true, firstPaymentAmount: 400m, setAsideShortfall: 300m,
            freeFunds: 900m, balance: 1000m, isOneTime: false);

        line.ShouldNotBeNull();
        line.ShouldContain("probably");                 // tentative, not a promise
        line.ShouldContain("\n");                        // two-line block: verdict + facts strip
        line.ShouldContain("$100 set aside");            // set aside so far: $400 − $300
        line.ShouldContain("$900 free");
    }

    [Fact]
    public void FirstPaymentCoverageLine_is_firm_and_shows_free_of_total_when_even_free_cash_falls_short()
    {
        // $300 unearmarked, only $100 free — still $200 short: a firm warning, and "free of total"
        // shows the money exists but is locked in other goals.
        var line = PlanHealthMessages.FirstPaymentCoverageLine(
            isFirstOccurrencePending: true, firstPaymentAmount: 400m, setAsideShortfall: 300m,
            freeFunds: 100m, balance: 1000m, isOneTime: false);

        line.ShouldNotBeNull();
        line.ShouldContain("Short even after free cash");
        line.ShouldContain("\n");
        line.ShouldContain("$100 free of $1,000");       // free framed against total
        line.ShouldContain("$200 short");
        line.ShouldNotContain("probably");               // firm, not hedged
    }

    [Fact]
    public void FirstPaymentCoverageLine_falls_back_to_the_plain_gap_when_free_funds_are_unknown()
    {
        // The no-forecast preview path: with free funds unknown, one plain line, no facts strip.
        var line = PlanHealthMessages.FirstPaymentCoverageLine(
            isFirstOccurrencePending: true, firstPaymentAmount: 400m, setAsideShortfall: 300m,
            freeFunds: null, balance: null, isOneTime: false);

        line.ShouldBe("$300 short for the first payment");
    }
}
