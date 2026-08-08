using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// Turns PlanHealthState's numbers into the exact user-facing wording used
// across the app — kept separate from any UserControl so the wording itself
// is plain, testable logic, and so Current jar state / Summary / the RRule
// preview all read from one place instead of composing strings independently.
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

    /// <summary>[CALC] "$X saved of $Y milestone[ — $Z short/over]" — the base fact always shows; the delta only appends for the two "today" states.</summary>
    /// <param name="jar">This savings plan's own FundJar (today's reading) — PlanHealthState alone doesn't carry the raw saved/milestone amounts.</param>
    /// <param name="state">The plan's current health state.</param>
    public static string CurrentJarStateLine(FundJar jar, PlanHealthState state)
    {
        var basic = $"{jar.ExpectedAmount:C0} saved of {jar.MilestoneAmount ?? 0m:C0} milestone";
        var delta = CurrentJarStateDelta(state);
        return delta is null ? basic : $"{basic} — {delta}";
    }

    /// <summary>[CALC] The live counterpart to CurrentJarStateLine — same exact wording, computed straight from raw Expected/Milestone amounts instead of reading them off a saved FundJar/PlanHealthState. Exists because PlanHealthState's other fields (Shortfall, IsChronicShortfall) are whole-span, due-date-anchored figures with no live equivalent for a proposed, not-yet-saved pattern — this sticks to the one pair of numbers that genuinely can be computed live.</summary>
    /// <param name="expectedAmount">What the jar currently holds, live.</param>
    /// <param name="milestoneAmount">The milestone it's being measured against, live.</param>
    public static string LiveJarStateLine(decimal expectedAmount, decimal milestoneAmount)
    {
        var basic = $"{expectedAmount:C0} saved of {milestoneAmount:C0} milestone";
        if (expectedAmount < milestoneAmount)
        {
            return $"{basic} — {milestoneAmount - expectedAmount:C0} short";
        }

        if (expectedAmount > milestoneAmount)
        {
            return $"{basic} — {expectedAmount - milestoneAmount:C0} over";
        }

        return basic;
    }

    /// <summary>[CALC] The Summary aside's future-facing pair, for a one-time goal — "on pace for $Y of $Z needed — $W short/over." Healthy uses today's own jar/milestone (same numbers CurrentJarStateLine would show, just phrased as "on track") — there's nothing to project when today's reading is already fine. AlreadyMissing/CurrentlyOverfunded return null; those two live in CurrentJarStateLine instead.</summary>
    /// <param name="jar">The goal's FundJar, for the Healthy case's today-reading.</param>
    /// <param name="shortfall">The goal's shortfall projection, for the WillMiss/WillBeOverfunded cases.</param>
    /// <param name="category">Which health category the plan is in.</param>
    public static string? SummaryFutureLine(FundJar jar, GoalShortfall shortfall, PlanHealthCategory category) =>
        category switch
        {
            PlanHealthCategory.Healthy =>
                $"{jar.ExpectedAmount:C0} saved, matching the {jar.MilestoneAmount ?? 0m:C0} milestone — on track.",
            PlanHealthCategory.WillMiss =>
                $"{jar.ExpectedAmount:C0} saved, on pace for {shortfall.AmountAllocatedByDueDate:C0} of {shortfall.AmountNeeded:C0} needed — {shortfall.ShortfallAmount:C0} short",
            PlanHealthCategory.WillBeOverfunded =>
                $"{jar.ExpectedAmount:C0} saved, on pace for {shortfall.AmountAllocatedByDueDate:C0} of {shortfall.AmountNeeded:C0} needed — {shortfall.OverfundedAmount:C0} over",
            _ => null,
        };

    /// <summary>[CALC] Flags *that* a repeating goal's shortfall recurs, gated on IsChronicShortfall plus an already-showing shortfall state — a modifier on an existing warning, not an independent announcement. TODO: the overfunded mirror of this was never settled — whether a repeating overfunded plan needs its own Summary-aside phrase (vs. just relying on the RRule preview's "Projected overfunded") is an open question; shipping the shortfall case now and returning null for the overfunded one.</summary>
    /// <param name="state">The plan's current health state.</param>
    // Candidate direction for later, NOT adopted — left as a comment rather than a
    // live branch so it doesn't silently start firing before anyone decides it should:
    //     PlanHealthCategory.CurrentlyOverfunded or PlanHealthCategory.WillBeOverfunded
    //         => "Consistently ahead.",
    public static string? SummaryRecurringPhrase(PlanHealthState state)
    {
        var isShortfallState = state.MostImportantHealthState is PlanHealthCategory.AlreadyMissing or PlanHealthCategory.WillMiss;
        return isShortfallState && state.IsChronicShortfall ? "Keeps falling short." : null;
    }

    /// <summary>[CALC] The RRule preview's own caption — generic ("Projected short/overfunded"), except the shortfall case splits on IsChronicShortfall and reuses SummaryRecurringPhrase's exact words rather than a new synonym. Null for Healthy/AlreadyMissing/CurrentlyOverfunded — this region only ever speaks for the future two.</summary>
    /// <param name="state">The plan's current health state.</param>
    public static string? RRulePreviewCaption(PlanHealthState state) => state.MostImportantHealthState switch
    {
        PlanHealthCategory.WillMiss => state.IsChronicShortfall ? "Keeps falling short" : "Projected short",
        PlanHealthCategory.WillBeOverfunded => "Projected overfunded",
        _ => null,
    };

    // RecurrenceRuleEditor only ever applies a color to whatever dates it's
    // told; it has no idea those dates come from
    // PlanHealthState.UnderfundedReleaseDates, or what that means. This is
    // that explanation, supplied from here rather than composed ad hoc in
    // the form — always the same wording, since the highlight always means
    // the same thing regardless of which caption (or none) goes with it:
    // UnderfundedReleaseDates is a real, per-occurrence forecast fact,
    // computed independently of MostImportantHealthState's own whole-plan
    // verdict (a plan can read WillBeOverfunded overall and still have one
    // rough release highlighted here).
    public const string UnderfundedReleaseHighlightLegend = "Highlighted: this release came up short of what it needed.";

    // Wording settled, mechanism still pending — needs the form/UI layer's
    // at-open-vs-live diff (not built yet). Not called from anywhere yet;
    // the strings exist so whoever builds that diffing mechanism doesn't
    // also have to invent the copy.
    public const string FixedNoLongerShort = "Fixed — no longer short";
    public const string FixedNoLongerOverfunded = "Fixed — no longer overfunded";

    /// <summary>[CALC] The Starting-point region's own warning — takes the raw values rather than a PlanHealthState so both callers can use it: the Earmark form's live check (a proposed, not-yet-saved pattern has no PlanHealthState of its own to read) and the Summary region's aside (which does have a real, saved one). "For the first payment" reads oddly for a one-time goal — there is no "first" of several, this IS the goal, so it says so instead.</summary>
    /// <param name="isFirstOccurrencePending">Whether the first scheduled payment hasn't happened yet.</param>
    /// <param name="firstOccurrenceShortfall">How short that first payment is projected to be.</param>
    /// <param name="isOneTime">Whether this is a one-time goal rather than a repeating one — matches the established GetOccurrences().Count == 1 idiom used elsewhere in the App project.</param>
    public static string? FirstOccurrenceShortfallLine(bool isFirstOccurrencePending, decimal firstOccurrenceShortfall, bool isOneTime) =>
        isFirstOccurrencePending && firstOccurrenceShortfall > 0m
            ? isOneTime
                ? $"{firstOccurrenceShortfall:C0} short for reaching your goal"
                : $"{firstOccurrenceShortfall:C0} short for the first payment"
            : null;

    /// <summary>[CALC] The Expense form's own compact status indicator, next to its save buttons — one of the four states the linked plan is in, or null (nothing shown) for Healthy. A deliberately different word pair (Underfunded/Overfunded, Currently/Projected) from the Earmark form's own "short"/"over" narrative vocabulary — two different registers for the same underlying states, both intentional: this is a compact status label, those are narrative sentences.</summary>
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
