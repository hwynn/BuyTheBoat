using BuyTheBoat.Domain;
using Shouldly;

namespace BuyTheBoat.Domain.Tests;

public class PayoffEstimatorTests
{
    [Fact]
    public void Rounds_the_payment_count_up_when_the_last_payment_is_partial()
    {
        // $12,000 owed at $350/month is 34.28 payments — the 35th (smaller)
        // payment still needs its own date, so the count rounds up to 35 and
        // the payoff date is the 35th monthly occurrence (Jan 2025 + 34 months).
        var estimate = PayoffEstimator.Estimate(new PayoffRequest
        {
            TotalOwed = 12_000m,
            Payment = 350m,
            Frequency = RecurrenceFrequency.Monthly,
            Start = new DateOnly(2025, 1, 1),
        });

        estimate.PaymentCount.ShouldBe(35);
        estimate.PayoffDate.ShouldBe(new DateOnly(2027, 11, 1));
    }

    [Fact]
    public void Uses_an_exact_count_when_the_payment_divides_the_balance_evenly()
    {
        // $12,000 at exactly $1,000/month clears in 12 payments; the last is the
        // 12th monthly occurrence, Dec 2025.
        var estimate = PayoffEstimator.Estimate(new PayoffRequest
        {
            TotalOwed = 12_000m,
            Payment = 1_000m,
            Frequency = RecurrenceFrequency.Monthly,
            Start = new DateOnly(2025, 1, 1),
        });

        estimate.PaymentCount.ShouldBe(12);
        estimate.PayoffDate.ShouldBe(new DateOnly(2025, 12, 1));
    }

    [Fact]
    public void A_single_payment_clears_a_balance_smaller_than_the_payment()
    {
        // Owe less than one payment and it is paid off on the very first payment
        // date, i.e. the schedule's start.
        var estimate = PayoffEstimator.Estimate(new PayoffRequest
        {
            TotalOwed = 200m,
            Payment = 350m,
            Frequency = RecurrenceFrequency.Monthly,
            Start = new DateOnly(2025, 1, 1),
        });

        estimate.PaymentCount.ShouldBe(1);
        estimate.PayoffDate.ShouldBe(new DateOnly(2025, 1, 1));
    }

    [Fact]
    public void Follows_the_payment_schedule_for_non_monthly_loans()
    {
        // $1,000 at $100 paid weekly from a Monday is 10 payments; the 10th
        // Monday is nine weeks after the start.
        var estimate = PayoffEstimator.Estimate(new PayoffRequest
        {
            TotalOwed = 1_000m,
            Payment = 100m,
            Frequency = RecurrenceFrequency.Weekly,
            Start = new DateOnly(2025, 1, 6),
        });

        estimate.PaymentCount.ShouldBe(10);
        estimate.PayoffDate.ShouldBe(new DateOnly(2025, 3, 10));
    }

    [Fact]
    public void A_zero_or_negative_payment_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => PayoffEstimator.Estimate(new PayoffRequest
        {
            TotalOwed = 5_000m,
            Payment = 0m,
            Frequency = RecurrenceFrequency.Monthly,
            Start = new DateOnly(2025, 1, 1),
        }));
    }

    [Fact]
    public void A_zero_or_negative_balance_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => PayoffEstimator.Estimate(new PayoffRequest
        {
            TotalOwed = 0m,
            Payment = 350m,
            Frequency = RecurrenceFrequency.Monthly,
            Start = new DateOnly(2025, 1, 1),
        }));
    }
}
