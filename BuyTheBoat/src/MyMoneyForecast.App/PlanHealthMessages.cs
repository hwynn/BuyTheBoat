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

    /// <summary>[CALC] The "Fund jar, today" region's line — "$X saved of $Y milestone", the two amounts and nothing else. That region is only about where the jar stands right now, and the gap is self-evident from the two numbers, so it carries no delta. Takes raw Expected/Milestone amounts so both the saved reading and a proposed, not-yet-saved pattern's live one share it.</summary>
    /// <param name="expectedAmount">What the jar currently holds.</param>
    /// <param name="milestoneAmount">The milestone it's measured against.</param>
    public static string JarStateLine(decimal expectedAmount, decimal milestoneAmount) =>
        $"{expectedAmount:C0} saved of {milestoneAmount:C0} milestone";

    /// <summary>[CALC] The "Fund jar, today" line WITHOUT the milestone comparison — just "$X saved". The finance-pattern form uses this: how much is set aside for the bill is worth showing, but the "of $Y milestone" gap is plan-state detail that isn't helpful while editing the bill itself.</summary>
    /// <param name="expectedAmount">What the jar currently holds.</param>
    public static string JarSavedLine(decimal expectedAmount) =>
        $"{expectedAmount:C0} saved";

    /// <summary>[CALC] "$X saved of $Y milestone[ — $Z short/over]" — the two-amounts line plus the today-delta. Used by the Concerning save-confirmation popup, which wants the shortfall spelled out; the Summary aside uses the plain JarStateLine instead.</summary>
    /// <param name="jar">This savings plan's own FundJar (today's reading) — PlanHealthState alone doesn't carry the raw saved/milestone amounts.</param>
    /// <param name="state">The plan's current health state.</param>
    public static string CurrentJarStateLine(FundJar jar, PlanHealthState state)
    {
        var basic = JarStateLine(jar.ExpectedAmount, jar.MilestoneAmount ?? 0m);
        var delta = CurrentJarStateDelta(state);
        return delta is null ? basic : $"{basic} — {delta}";
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
                $"{jar.ExpectedAmount:C0} saved, on pace for {shortfall.AmountAllocatedByDueDate:C0} of {shortfall.AmountNeeded:C0} needed — {shortfall.ShortfallAmount:C0} short in the long run",
            PlanHealthCategory.WillBeOverfunded =>
                $"{jar.ExpectedAmount:C0} saved, on pace for {shortfall.AmountAllocatedByDueDate:C0} of {shortfall.AmountNeeded:C0} needed — {shortfall.OverfundedAmount:C0} over",
            _ => null,
        };

    /// <summary>[CALC] Flags *that* a repeating goal's shortfall or overfund recurs, gated on IsChronicShortfall/IsChronicOverfund plus the matching already-showing state — a modifier on an existing warning, not an independent announcement. Fills the second line of Summary's aside (AsideSecondaryText); the first line (AsideText) is what CurrentJarStateLine/LiveJarStateLine populate, so "consistently ahead" for a CurrentlyOverfunded plan lives here, not in the RRule preview caption.</summary>
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

    /// <summary>[CALC] The RRule preview's own caption — generic ("Projected short/overfunded"), except each side splits on its own IsChronicShortfall/IsChronicOverfund and reuses SummaryRecurringPhrase's exact words rather than a new synonym. Null for Healthy/AlreadyMissing/CurrentlyOverfunded — this region only ever speaks for the future two: every non-Healthy category has exactly one home, and today's two live in Summary's aside (see SummaryRecurringPhrase), the future two here.</summary>
    /// <param name="state">The plan's current health state.</param>
    public static string? RRulePreviewCaption(PlanHealthState state) => state.MostImportantHealthState switch
    {
        PlanHealthCategory.WillMiss => state.IsChronicShortfall ? "Keeps falling short" : "Projected short",
        PlanHealthCategory.WillBeOverfunded => state.IsChronicOverfund ? "Consistently ahead" : "Projected overfunded",
        _ => null,
    };

    // The legend for the calendar's underfunded-release highlight, supplied from here
    // rather than composed in the form (RecurrenceRuleEditor colors dates without knowing
    // what they mean). Always the same wording — UnderfundedReleaseDates is a per-occurrence
    // forecast fact, independent of the whole-plan verdict (a plan can read WillBeOverfunded
    // overall yet still have one rough release highlighted).
    public const string UnderfundedReleaseHighlightLegend = "Highlighted: this release came up short of what it needed.";

    // Placeholder copy for a not-yet-built mechanism (the form's at-open-vs-live diff).
    // Unused for now; the strings exist so whoever builds that diff needn't invent the copy.
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

    /// <summary>[CALC] The funds-aware first-payment warning, as a two-line block: a plain-language verdict, then a compact facts strip. Scoped to the ONE most-immediate problem — the first pending payment — not the whole plan's long-run health (that's the trajectory/RRule warnings elsewhere). Two shapes: REASSURING when free cash probably covers the not-yet-set-aside gap, deliberately tentative ("probably" — the free figure can't see another same-day claim on that cash) and nudging toward setting funds aside ("up to $X could be allocated"); URGENT when even free cash can't cover it, naming the payment DATE and how far short you'll still be. Takes raw numbers so both the live form preview and the saved reading can call it; a null freeFunds (the no-forecast preview path) falls back to the plain set-aside gap. Returns null when nothing is short, or the first occurrence has already happened.</summary>
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

        // The money's PROBABLY there, just not set aside: free cash covers the
        // gap. Tentative on purpose, and "up to $X could be allocated" invites
        // setting it aside — see this method's own summary.
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

    /// <summary>[CALC] Whether a savings plan is a deliberate holding pattern ("the pause case") rather than an active contribution — nothing will actually land in the jar, either because the plan's own rule produces zero occurrences, or because every occurrence shares the pattern's one Amount at $0. True for either condition alone.</summary>
    /// <param name="amount">The plan's own contribution amount — 0 means every occurrence contributes nothing.</param>
    /// <param name="occurrenceCount">How many occurrences the plan's own rule actually produces.</param>
    public static bool IsPaused(decimal amount, int occurrenceCount) => amount == 0m || occurrenceCount == 0;

    /// <summary>[CALC] The Summary narrative's own replacement sentence for a paused plan (IsPaused) — reads as a deliberate pause instead of "$0" or a silently dropped continuation sentence.</summary>
    public const string PausedFundingSentence = "Funding is currently paused, with nothing scheduled toward it.";

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
