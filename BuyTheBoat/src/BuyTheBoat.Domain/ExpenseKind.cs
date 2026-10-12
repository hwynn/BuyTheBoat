namespace BuyTheBoat.Domain;

/// <summary>
/// The kind of expense, which decides how the selected-day region answers "am I on track?".
/// A one-time optional goal cares about its milestone and due date, a regular bill about whether it can be paid in full now, and a paycheck just brings money in.
/// </summary>
public enum ExpenseKind
{
    /// <summary>Amount above 0: money in, nothing to save toward.</summary>
    Paycheck,

    /// <summary>Mandatory: a bill you have to pay, whether or not it has a savings plan.</summary>
    Bill,

    /// <summary>Non-mandatory, single occurrence, with a savings plan, the OneTimeGoalFactory shape ("trip to Japan").</summary>
    OneTimeGoal,

    /// <summary>Non-mandatory, repeating, with a savings plan.</summary>
    RepeatingGoal,

    /// <summary>Non-mandatory with no savings plan.</summary>
    Discretionary,
}

public static class ExpenseKindClassifier
{
    /// <summary>[CALC] Classifies a pattern as a Paycheck, Bill, OneTimeGoal, RepeatingGoal, or Discretionary expense, which decides how the forecast display answers "am I on track?".</summary>
    /// <param name="pattern">The pattern to classify.</param>
    /// <param name="hasEarmark">Whether an EarMarkPattern exists for this pattern's finance id. The safety cushion has no FinancialPattern, so callers handle it before classifying.</param>
    public static ExpenseKind Classify(FinancialPattern pattern, bool hasEarmark)
    {
        if (pattern.Amount > 0m)
        {
            return ExpenseKind.Paycheck;
        }

        if (pattern.Mandatory)
        {
            return ExpenseKind.Bill;
        }

        if (!hasEarmark)
        {
            return ExpenseKind.Discretionary;
        }

        return pattern.DatePattern.GetOccurrences().Count == 1
            ? ExpenseKind.OneTimeGoal
            : ExpenseKind.RepeatingGoal;
    }
}
