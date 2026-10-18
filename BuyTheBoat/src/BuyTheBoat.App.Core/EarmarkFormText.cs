using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

/// <summary>The Earmark form's text that doesn't depend on the form's controls: the "What can I allocate today?" lines and the Summary narrative's cadence phrase and concurrent-plans sentence.</summary>
internal static class EarmarkFormText
{
    /// <summary>
    /// [CALC] The "What can I allocate today?" lines for one day: free funds, what's set aside and how it splits by importance against this goal, and any one-off this goal already has that day.
    /// More or less important means a higher or lower Priority; the safety cushion is listed on its own.
    /// </summary>
    /// <param name="snapshot">The goal's account on the picked day.</param>
    /// <param name="priorityById">Each pattern's Priority, by finance id.</param>
    /// <param name="sameDayOneOff">The amount of a one-off this goal already has that day, other than the one being edited, or null.</param>
    internal static string AllocateTodayText(BalanceSnapshot snapshot, FinancialPattern goal, IReadOnlyDictionary<int, int> priorityById, decimal? sameDayOneOff)
    {
        decimal Held(Func<int, bool> include) => snapshot.FundJars
            .Where(jar => jar.FinanceId is { } id && id != goal.FinanceId && include(priorityById.GetValueOrDefault(id)))
            .Sum(jar => jar.ExpectedAmount);

        var thisGoal = snapshot.FundJars.FirstOrDefault(jar => jar.FinanceId == goal.FinanceId)?.ExpectedAmount ?? 0m;
        var cushion = snapshot.FundJars.Where(jar => jar.FinanceId is null).Sum(jar => jar.ExpectedAmount);
        var totalSetAside = snapshot.FundJars.Sum(jar => jar.ExpectedAmount);

        var lines = new List<string>
        {
            $"Free: {snapshot.ExpectedFreeAmount ?? 0m:N2}",
            $"Set aside: {totalSetAside:N2} — {thisGoal:N2} for this goal, {Held(p => p > goal.Priority):N2} for more important goals, "
                + $"{Held(p => p == goal.Priority):N2} for equally important ones, {Held(p => p < goal.Priority):N2} for less important ones"
                + (cushion > 0m ? $", {cushion:N2} in the safety cushion." : "."),
        };

        if (sameDayOneOff is { } existing)
        {
            lines.Add($"This goal already has a one-off of {existing:N2} on this day; saving adds to it.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>[CALC] The phrase for "We plan to set aside ...", like "a month" or "every 3 months"; "a month" avoids repeating the opening line's "every month".</summary>
    /// <param name="frequency">The plan's contribution frequency.</param>
    /// <param name="interval">The plan's contribution interval.</param>
    internal static string ContributionCadencePhrase(RecurrenceFrequency frequency, int interval)
    {
        var unit = frequency switch
        {
            RecurrenceFrequency.Daily => "day",
            RecurrenceFrequency.Weekly => "week",
            RecurrenceFrequency.Monthly => "month",
            RecurrenceFrequency.Yearly => "year",
            _ => "occurrence",
        };
        return interval <= 1 ? $"a {unit}" : $"every {interval} {unit}s";
    }

    /// <summary>
    /// [CALC] The narrative's sentence naming the other plans running at the same time, like " Another plan also sets aside 50 a month toward it; the chart and the figures beside it count both."
    /// In One-off mode no plan is open, so it names them all; empty when only one plan funds the goal.
    /// </summary>
    /// <param name="concurrentPlans">Every saved plan running at the same time as plan, plan's saved version included.</param>
    /// <param name="plan">The plan the Summary is built around.</param>
    /// <param name="isOneOff">Whether the form is in One-off mode.</param>
    internal static string ConcurrentPlansSentence(IReadOnlyList<EarMarkPattern> concurrentPlans, EarMarkPattern plan, bool isOneOff)
    {
        static string Describe(EarMarkPattern other) =>
            $"{Math.Abs(other.Amount):N0} {ContributionCadencePhrase(other.DatePattern.Frequency, other.DatePattern.Interval)}";

        static string JoinWithAnd(IReadOnlyList<string> parts) =>
            parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}";

        if (isOneOff)
        {
            return concurrentPlans.Count < 2
                ? string.Empty
                : $" {concurrentPlans.Count} plans fund it — {JoinWithAnd(concurrentPlans.Select(Describe).ToList())}; the chart and the figures beside it count them all.";
        }

        // Matched by start, since the open plan's saved version is in the list too.
        var others = concurrentPlans.Where(other => other.DatePattern.ActiveStart != plan.DatePattern.ActiveStart).ToList();
        return others.Count switch
        {
            0 => string.Empty,
            1 => $" Another plan also sets aside {Describe(others[0])} toward it; the chart and the figures beside it count both.",
            _ => $" {others.Count} other plans also set aside {JoinWithAnd(others.Select(Describe).ToList())} toward it; the chart and the figures beside it count them all.",
        };
    }
}
