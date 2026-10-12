namespace BuyTheBoat.Domain;

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

    /// <summary>
    /// 1 is the lowest priority; higher numbers are more important.
    /// This decides which goals get funded first when money is short, since deallocation drains the least important jars first whatever the schedule.
    /// </summary>
    public int Priority { get; init; } = 3;

    /// <summary>
    /// Only meant to be set by the user behind an "advanced" option.
    /// When money runs short, deallocation drains jars by priority whatever the schedule, so the user doesn't need to get this exactly right.
    /// </summary>
    public SavingsFrequency Frequency { get; init; } = SavingsFrequency.Monthly;
}

public sealed record OneTimeGoal(FinancialPattern Goal, EarMarkPattern SavingsPlan);

/// <summary>
/// Creates the goal and savings plan together for the simplified one-time-goal flow, asking only what mini_fund_project's "one time goals" sheet did: description, amount, due date, and optional start date.
/// A one-time goal is just a FinancialPattern with a single occurrence, as in class documentation.ods's "boat" example; there's no separate Goal type.
/// </summary>
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
                // The goal's only occurrence is its due date; ActiveFrom stretches its active span back to the save-start day so its plan and jar can begin earlier.
                ActiveFrom = request.StartSavingDate,
            }),
            Amount = -request.AmountNeeded,
            Priority = request.Priority,
            Mandatory = false, // discretionary even though the amount is negative
        });

        // StartSavingDate is always an occurrence and comes before DueDate (checked above), so there's at least one installment.
        var savingsPattern = BuildSavingsDatePattern(request.StartSavingDate, request.DueDate, request.Frequency);
        var occurrenceCount = savingsPattern.GetOccurrences().Count;
        var installmentAmount = Math.Round(-request.AmountNeeded / occurrenceCount, 2);

        var savingsPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = goal.FinanceId,
                DatePattern = savingsPattern,
                Amount = installmentAmount,
                // Created automatically, so not the user's own until they edit it (see EarMarkPattern.ExplicitlyCreated).
                ExplicitlyCreated = false,
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
