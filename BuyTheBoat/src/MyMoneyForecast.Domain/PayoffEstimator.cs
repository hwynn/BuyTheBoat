namespace MyMoneyForecast.Domain;

// What the payoff estimate needs: how much is still owed, the size of one
// regular payment (a positive magnitude, like the create forms use), and the
// schedule those payments follow. The schedule fields mirror
// RecurrenceRuleOptions minus the end bound — which is exactly what we compute.
public sealed record PayoffRequest
{
    public required decimal TotalOwed { get; init; }
    public required decimal Payment { get; init; }
    public required RecurrenceFrequency Frequency { get; init; }
    public required DateOnly Start { get; init; }
    public int Interval { get; init; } = 1;
    public IReadOnlyList<DayOfWeek> ByDay { get; init; } = [];
    public IReadOnlyList<int> ByMonthDay { get; init; } = [];
}

// How many payments it takes and the date of the last one. Both are lower
// bounds: interest and fees are ignored, so a real loan takes at least this
// many payments and runs at least this long. The form states the date to
// the user as a floor ("at least until ..."), never as an exact date.
public sealed record PayoffEstimate(int PaymentCount, DateOnly PayoffDate);

// Answers a bill's "when does this stop?" with "when I've paid it off", from
// just the amount owed and the regular payment. Deliberately ignores
// interest and fees — the figure it returns is the soonest a loan could be
// clear, presented as a floor, not an exact date.
//
// It is the mirror of RecurrenceRule's own Count->Until resolution: "pay it
// off" means "make N payments", N payments is a Count, and a Count resolves to
// the date of the Nth occurrence. So this reuses that tested path rather than
// re-deriving occurrence dates by hand.
public static class PayoffEstimator
{
    /// <summary>[CALC] Works out the fewest payments and earliest date a loan could be paid off in, from the amount owed and the regular payment — a floor, since it ignores interest and fees.</summary>
    /// <param name="request">The amount owed, the regular payment, and the payment schedule.</param>
    public static PayoffEstimate Estimate(PayoffRequest request)
    {
        if (request.TotalOwed <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "TotalOwed must be greater than zero — there is nothing to pay off otherwise.");
        }

        if (request.Payment <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Payment must be greater than zero — a loan is never paid off at zero per payment.");
        }

        // Round up: a final partial payment is still a whole payment date. $12,000
        // owed at $350 a month is 34.28 payments — i.e. a 35th, smaller payment.
        var paymentCount = (int)Math.Ceiling(request.TotalOwed / request.Payment);

        // N payments IS a Count, so its end date is exactly the Count->Until
        // resolution RecurrenceRule.Create already performs and tests.
        var schedule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = request.Frequency,
            Start = request.Start,
            Interval = request.Interval,
            ByDay = request.ByDay,
            ByMonthDay = request.ByMonthDay,
            Count = paymentCount,
        });

        return new PayoffEstimate(paymentCount, schedule.Until);
    }
}
