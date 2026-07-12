namespace MyMoneyForecast.Domain;

// The shape Q3 ("am I on track?") takes differs per kind of expense
// (planning/08-forecast-tab-design-philosophy.md §3.III): a one-time optional
// goal cares about the milestone and a relative due date; a regular bill cares
// about "can I pay it in full right now"; a paycheck just gives money. This is
// the domain vocabulary display layers branch on.
public enum ExpenseKind
{
    // Amount > 0 — money in. Nothing to save toward.
    Paycheck,

    // Mandatory — a bill you have to pay (auto-reserved when it has no
    // explicit earmark; the same kind either way).
    Bill,

    // Non-mandatory, single occurrence, with a savings plan — the
    // OneTimeGoalFactory shape ("trip to Japan").
    OneTimeGoal,

    // Non-mandatory, repeating, with a savings plan.
    RepeatingGoal,

    // Non-mandatory with no savings plan — a discretionary expense.
    Discretionary,
}

public static class ExpenseKindClassifier
{
    // `hasEarmark` = an EarMarkPattern exists for this pattern's finance id.
    // The safety cushion has no FinancialPattern at all (finance_id = null),
    // so callers handle it before classifying.
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
