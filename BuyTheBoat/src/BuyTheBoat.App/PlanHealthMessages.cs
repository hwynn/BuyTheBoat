using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

/// <summary>
/// Turns PlanHealthState's numbers into the exact wording shown across the app.
/// Kept apart from any UserControl so the wording is plain, testable logic, and the jar line, Summary, and schedule preview all read from one place.
/// </summary>
public static class PlanHealthMessages
{
    /// <summary>[CALC] The append-only delta for the two "today" states. Null for Healthy/WillMiss/WillBeOverfunded — those either show no delta at all (Healthy) or use SummaryFutureLine's own sentence instead (the future two).</summary>
    /// <param name="state">The plan's current health state.</param>
    public static string? CurrentJarStateDelta(PlanHealthState state) => state.MostImportantHealthState switch
    {
        PlanHealthCategory.AlreadyMissing => $"{state.CurrentShortfallAmount:C0} short",
        PlanHealthCategory.CurrentlyOverfunded => $"{state.CurrentOverfundedAmount:C0} over",
        _ => null,
    };

    /// <summary>
    /// [CALC] The "Fund jar, today" line: "$X saved of $Y milestone", with no gap shown, since the two numbers make it obvious.
    /// Takes raw amounts, so both a saved reading and an unsaved plan's live reading can use it.
    /// </summary>
    /// <param name="expectedAmount">What the jar currently holds.</param>
    /// <param name="milestoneAmount">The milestone it's measured against.</param>
    public static string JarStateLine(decimal expectedAmount, decimal milestoneAmount) =>
        $"{expectedAmount:C0} saved of {milestoneAmount:C0} milestone";

    /// <summary>[CALC] The "Fund jar, today" line without the milestone, just "$X saved", for the Expense form, where the milestone isn't helpful while editing the bill.</summary>
    /// <param name="expectedAmount">What the jar currently holds.</param>
    public static string JarSavedLine(decimal expectedAmount) =>
        $"{expectedAmount:C0} saved";

    /// <summary>[CALC] "$X saved of $Y milestone — $Z short/over", for the save confirmation's "worth a look" row, which spells out the gap; the Summary uses the plain JarStateLine.</summary>
    /// <param name="jar">This savings plan's own FundJar (today's reading) — PlanHealthState alone doesn't carry the raw saved/milestone amounts.</param>
    /// <param name="state">The plan's current health state.</param>
    public static string CurrentJarStateLine(FundJar jar, PlanHealthState state)
    {
        var basic = JarStateLine(jar.ExpectedAmount, jar.MilestoneAmount ?? 0m);
        var delta = CurrentJarStateDelta(state);
        return delta is null ? basic : $"{basic} — {delta}";
    }

    /// <summary>
    /// [CALC] The Summary's "Toward the goal" line for a one-time goal: "on pace for $Y of $Z needed — $W short/over."
    /// Healthy uses today's jar and milestone, phrased as on track; AlreadyMissing and CurrentlyOverfunded return null, since CurrentJarStateLine covers them.
    /// </summary>
    /// <param name="jar">The goal's FundJar, for the Healthy case's today-reading.</param>
    /// <param name="shortfall">The goal's shortfall projection, for the WillMiss/WillBeOverfunded cases.</param>
    /// <param name="category">Which health category the plan is in.</param>
    public static string? SummaryFutureLine(FundJar jar, GoalShortfall shortfall, PlanHealthCategory category) =>
        category switch
        {
            PlanHealthCategory.Healthy =>
                $"{jar.ExpectedAmount:C0} saved, matching the {jar.MilestoneAmount ?? 0m:C0} milestone — on track.",
            PlanHealthCategory.WillMiss =>
                $"{jar.ExpectedAmount:C0} saved, on pace for {shortfall.AmountAllocatedByDueDate:C0} of {shortfall.AmountNeeded:C0} needed — {shortfall.ShortfallAmount:C0} short in the long run",
            PlanHealthCategory.WillBeOverfunded =>
                $"{jar.ExpectedAmount:C0} saved, on pace for {shortfall.AmountAllocatedByDueDate:C0} of {shortfall.AmountNeeded:C0} needed — {shortfall.OverfundedAmount:C0} over",
            _ => null,
        };

    /// <summary>
    /// [CALC] Says a repeating goal's shortfall or overfund keeps recurring (IsChronicShortfall or IsChronicOverfund, with the matching state), adding to an existing warning rather than announcing on its own.
    /// Fills the Summary's "Toward the goal" line when SummaryFutureLine returns null.
    /// </summary>
    /// <param name="state">The plan's current health state.</param>
    public static string? SummaryRecurringPhrase(PlanHealthState state)
    {
        var isShortfallState = state.MostImportantHealthState is PlanHealthCategory.AlreadyMissing or PlanHealthCategory.WillMiss;
        if (isShortfallState && state.IsChronicShortfall)
        {
            return "Keeps falling short.";
        }

        var isOverfundState = state.MostImportantHealthState is PlanHealthCategory.CurrentlyOverfunded or PlanHealthCategory.WillBeOverfunded;
        return isOverfundState && state.IsChronicOverfund ? "Consistently ahead." : null;
    }

    /// <summary>
    /// [CALC] The schedule preview's caption, "Projected short" or "Projected overfunded", or SummaryRecurringPhrase's words when it keeps recurring.
    /// Only for the two future states; null otherwise, since today's states are shown in the Summary instead.
    /// </summary>
    /// <param name="state">The plan's current health state.</param>
    public static string? RRulePreviewCaption(PlanHealthState state) => state.MostImportantHealthState switch
    {
        PlanHealthCategory.WillMiss => state.IsChronicShortfall ? "Keeps falling short" : "Projected short",
        PlanHealthCategory.WillBeOverfunded => state.IsChronicOverfund ? "Consistently ahead" : "Projected overfunded",
        _ => null,
    };

    /// <summary>
    /// The legend for the calendar's short-release highlight, since RecurrenceRuleEditor colors dates without knowing what they mean.
    /// Always the same wording, since a single short release can happen even when the plan is overfunded overall.
    /// </summary>
    public const string UnderfundedReleaseHighlightLegend = "Highlighted: this release came up short of what it needed.";

    // Placeholder copy for a not-yet-built mechanism (the form's at-open-vs-live diff).
    // Unused for now; the strings exist so whoever builds that diff needn't invent the copy.
    public const string FixedNoLongerShort = "Fixed — no longer short";
    public const string FixedNoLongerOverfunded = "Fixed — no longer overfunded";

    /// <summary>
    /// [CALC] The Starting-point region's warning, taking raw values so an unsaved plan's live check can use it as well as a saved reading.
    /// A one-time goal names the goal rather than "the first payment", since it's the only one.
    /// </summary>
    /// <param name="isFirstOccurrencePending">Whether the first scheduled payment hasn't happened yet.</param>
    /// <param name="firstOccurrenceShortfall">How short that first payment is projected to be.</param>
    /// <param name="isOneTime">Whether this is a one-time goal rather than a repeating one — matches the established GetOccurrences().Count == 1 idiom used elsewhere in the App project.</param>
    public static string? FirstOccurrenceShortfallLine(bool isFirstOccurrencePending, decimal firstOccurrenceShortfall, bool isOneTime) =>
        isFirstOccurrencePending && firstOccurrenceShortfall > 0m
            ? isOneTime
                ? $"{firstOccurrenceShortfall:C0} short for reaching your goal"
                : $"{firstOccurrenceShortfall:C0} short for the first payment"
            : null;

    /// <summary>
    /// [CALC] The first-payment warning as two lines, a plain verdict and then the facts, about the first pending payment only; the plan's long-run health is shown elsewhere.
    /// Reassuring when free cash probably covers what isn't set aside yet, worded tentatively since another same-day claim could take that cash, and suggesting "up to $X could be allocated".
    /// Urgent when even free cash can't cover it, naming the payment date and how far short it will be.
    /// A null freeFunds (no forecast) falls back to the set-aside gap; returns null when nothing is short or the first payment has happened.
    /// </summary>
    /// <param name="isFirstOccurrencePending">Whether the first scheduled payment hasn't happened yet.</param>
    /// <param name="firstPaymentAmount">What that first payment (or, for a one-time goal, the goal) costs.</param>
    /// <param name="setAsideShortfall">How much of that first payment isn't set aside yet — 0 when the plan already covers it.</param>
    /// <param name="freeFunds">Free-to-spend cash on hand entering the payment day, or null when it isn't known.</param>
    /// <param name="paymentDate">The day that payment lands, for the urgent line's date stamp; may be null (the urgent line then omits the date).</param>
    /// <param name="isOneTime">Whether this is a one-time goal (no "first" of several — it IS the goal).</param>
    public static string? FirstPaymentCoverageLine(
        bool isFirstOccurrencePending, decimal firstPaymentAmount, decimal setAsideShortfall,
        decimal? freeFunds, DateOnly? paymentDate, bool isOneTime)
    {
        if (!isFirstOccurrencePending || setAsideShortfall <= 0m)
        {
            return null;
        }

        var setAside = firstPaymentAmount - setAsideShortfall;

        // Free amount unknown (the no-forecast preview path): no forecast to
        // read a strip from, so fall back to the plain set-aside gap.
        if (freeFunds is not decimal free)
        {
            return isOneTime
                ? $"{setAsideShortfall:C0} short for reaching your goal"
                : $"{setAsideShortfall:C0} short for the first payment";
        }

        // The money's probably there, just not set aside: free cash covers the gap.
        if (free >= setAsideShortfall)
        {
            return "You'll probably have the free cash — it just isn't set aside." +
                $"\n{setAside:C0} of {firstPaymentAmount:C0} set aside · up to {free:C0} could be allocated";
        }

        // Genuinely short: even free cash can't cover the gap. Firm, and dated —
        // this is the urgent, most-immediate problem.
        var stillShort = setAsideShortfall - free;
        var when = paymentDate is { } date ? $"{date:MMM d} " : string.Empty;
        var verdict = isOneTime
            ? (paymentDate is { } d ? $"By {d:MMM d} you'll be {stillShort:C0} short of your goal, even after free cash."
                                    : $"You'll be {stillShort:C0} short of your goal, even after free cash.")
            : $"The {when}payment falls {stillShort:C0} short, even after free cash.";
        return $"{verdict}\n{setAside:C0} of {firstPaymentAmount:C0} set aside · only {free:C0} free";
    }

    /// <summary>[CALC] Is the plan deliberately paused ("the pause case"), so nothing lands in the jar? True when its schedule has no occurrences or its amount is $0.</summary>
    /// <param name="amount">The plan's own contribution amount — 0 means every occurrence contributes nothing.</param>
    /// <param name="occurrenceCount">How many occurrences the plan's own rule actually produces.</param>
    public static bool IsPaused(decimal amount, int occurrenceCount) => amount == 0m || occurrenceCount == 0;

    /// <summary>[CALC] The Summary narrative's own replacement sentence for a paused plan (IsPaused) — reads as a deliberate pause instead of "$0" or a silently dropped continuation sentence.</summary>
    public const string PausedFundingSentence = "Funding is currently paused, with nothing scheduled toward it.";

    /// <summary>
    /// [CALC] The Expense form's compact status label next to its save buttons, for the linked plan's state; null for Healthy.
    /// It says Underfunded/Overfunded and Currently/Projected on purpose, unlike the Earmark form's "short"/"over" sentences, since this is a short label.
    /// </summary>
    /// <param name="category">Which health category the linked plan is in.</param>
    public static string? ExpenseStatusLabel(PlanHealthCategory category) => category switch
    {
        PlanHealthCategory.AlreadyMissing => "Currently Underfunded",
        PlanHealthCategory.WillMiss => "Underfunding Projected",
        PlanHealthCategory.CurrentlyOverfunded => "Currently Overfunded",
        PlanHealthCategory.WillBeOverfunded => "Overfunding Projected",
        _ => null,
    };
}
