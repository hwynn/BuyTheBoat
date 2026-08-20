namespace MyMoneyForecast.Domain;

public enum SavingsFrequency
{
    Weekly,
    EveryOtherWeek,
    Monthly,
}

public sealed record OneTimeGoalRequest
{
    public required int FinanceId { get; init; }
    public required string Description { get; init; }
    public required decimal AmountNeeded { get; init; }
    public required DateOnly DueDate { get; init; }
    public required DateOnly StartSavingDate { get; init; }

    // Per FinancialPattern's documented convention: 1 = lowest priority,
    // higher numbers = higher priority. This is what actually decides which
    // goals get funded first when money is short — not the timing below,
    // which is provisional at best once deallocation-driven reallocation
    // exists.
    public int Priority { get; init; } = 3;

    // Only meant to be user-controlled behind an "advanced" opt-in. The
    // default case shouldn't ask about this at all: exactly when allocation
    // happens is expected to be reorganized dynamically by priority once
    // deallocation logic exists, so picking a cadence here is provisional,
    // not a real schedule the user needs to get right.
    public SavingsFrequency Frequency { get; init; } = SavingsFrequency.Monthly;
}

public sealed record OneTimeGoal(FinancialPattern Goal, EarMarkPattern SavingsPlan);

// Implicitly creates the goal + savings-plan pair for the simplified
// one-time-goal flow — mirrors mini_fund_project's "one time goals" sheet,
// which asked for exactly these things (description, amount, due date,
// optional start date) and nothing about how or when money actually arrives.
// There is no separate "Goal" type in the documented domain model — a
// one-time goal is a FinancialPattern with a single occurrence, exactly as
// class documentation.ods's own "boat" example models it.
public static class OneTimeGoalFactory
{
    /// <summary>[CALC] Builds a one-time goal's FinancialPattern plus its savings plan (EarMarkPattern), splitting the amount needed evenly across installments from the start date to the due date.</summary>
    /// <param name="request">The goal's description, amount, due date, and savings schedule.</param>
    public static OneTimeGoal Create(OneTimeGoalRequest request)
    {
        if (request.StartSavingDate >= request.DueDate)
        {
            throw new ArgumentException("The start date must be before the due date.", nameof(request));
        }

        var goal = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = request.FinanceId,
            Source = request.Description,
            Description = request.Description,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = request.DueDate,
                Count = 1,
                // The goal's only occurrence is its due date, but saving starts
                // earlier — ActiveFrom stretches the goal's active span back to the
                // save-start day so its savings plan (and jar) legitimately begin
                // before the due date.
                ActiveFrom = request.StartSavingDate,
            }),
            Amount = -request.AmountNeeded,
            Priority = request.Priority,
            Mandatory = false, // discretionary even though the amount is negative
        });

        // BuildSavingsDatePattern always derives ByDay/ByMonthDay from
        // StartSavingDate itself, so StartSavingDate is always a matching
        // occurrence — combined with the check above (Start < Due), there's
        // always at least one installment. No "zero occurrences" case exists
        // to guard against here.
        var savingsPattern = BuildSavingsDatePattern(request.StartSavingDate, request.DueDate, request.Frequency);
        var occurrenceCount = savingsPattern.GetOccurrences().Count;
        var installmentAmount = Math.Round(-request.AmountNeeded / occurrenceCount, 2);

        var savingsPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                DatePattern = savingsPattern,
                Amount = installmentAmount,
            },
            goal);

        return new OneTimeGoal(goal, savingsPlan);
    }

    /// <summary>[CALC] Builds the recurrence rule for a savings plan's own contribution schedule (weekly, every other week, or monthly).</summary>
    /// <param name="start">When contributions begin.</param>
    /// <param name="until">When contributions stop (the goal's due date).</param>
    /// <param name="frequency">How often to contribute.</param>
    private static RecurrenceRule BuildSavingsDatePattern(DateOnly start, DateOnly until, SavingsFrequency frequency) =>
        frequency switch
        {
            SavingsFrequency.Weekly => RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Weekly,
                DtStart = start,
                Until = until,
            }),
            SavingsFrequency.EveryOtherWeek => RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Weekly,
                Interval = 2,
                DtStart = start,
                Until = until,
            }),
            SavingsFrequency.Monthly => RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                ByMonthDay = [start.Day],
                DtStart = start,
                Until = until,
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(frequency)),
        };
}
