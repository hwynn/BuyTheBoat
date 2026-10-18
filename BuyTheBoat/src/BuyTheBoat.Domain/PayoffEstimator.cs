namespace BuyTheBoat.Domain;

/// <summary>
/// What the payoff estimate needs: how much is still owed, one regular payment (a positive amount, as the forms use), and the payment schedule.
/// The schedule fields match RecurrenceRuleOptions without an end date, which is what gets worked out.
/// </summary>
public sealed record PayoffRequest
{
    public required decimal TotalOwed { get; init; }
    public required decimal Payment { get; init; }
    public required RecurrenceFrequency Frequency { get; init; }
    public required DateOnly Start { get; init; }
    public int Interval { get; init; } = 1;
    public IReadOnlyList<DayOfWeek> ByDay { get; init; } = [];
    public IReadOnlyList<int> ByMonthDay { get; init; } = [];

    /// <summary>Counts payments from the first one on or after this date, for a loan already partway paid; null counts from Start.</summary>
    public DateOnly? CountFrom { get; init; }
}

/// <summary>
/// How many payments it takes and the date of the last one.
/// Both are minimums, since interest and fees are ignored; the form shows the date as "at least until ...", never as exact.
/// </summary>
public sealed record PayoffEstimate(int PaymentCount, DateOnly PayoffDate);

/// <summary>
/// Answers a bill's "when does this stop?" with "when I've paid it off", from the amount owed and the regular payment.
/// Paying it off means making N payments, which is a Count, so this uses RecurrenceRule's Count-to-Until conversion to find the last payment's date.
/// </summary>
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

        // Rounds up, since a final partial payment still needs its own date ($12,000 at $350 a month is 34.28, so 35 payments).
        var paymentCount = (int)Math.Ceiling(request.TotalOwed / request.Payment);

        // Payments before CountFrom already happened, so they're added to the Count to keep the schedule's own start.
        var earlierPayments = 0;
        if (request.CountFrom is { } countFrom && countFrom > request.Start)
        {
            earlierPayments = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = request.Frequency,
                DtStart = request.Start,
                Interval = request.Interval,
                ByDay = request.ByDay,
                ByMonthDay = request.ByMonthDay,
                Until = countFrom.AddDays(-1),
            }).GetOccurrences().Count;
        }

        // RecurrenceRule.Create turns the Count into the last payment's date.
        var schedule = RecurrenceRule.Create(new RecurrenceRuleOptions
        {
            Frequency = request.Frequency,
            DtStart = request.Start,
            Interval = request.Interval,
            ByDay = request.ByDay,
            ByMonthDay = request.ByMonthDay,
            Count = earlierPayments + paymentCount,
        });

        return new PayoffEstimate(paymentCount, schedule.Until);
    }
}
