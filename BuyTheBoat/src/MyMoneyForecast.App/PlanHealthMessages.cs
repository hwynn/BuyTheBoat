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

    /// <summary>[CALC] Flags *that* a repeating goal's shortfall or overfund recurs, gated on IsChronicShortfall/IsChronicOverfund plus the matching already-showing state — a modifier on an existing warning, not an independent announcement. Settled 2026-08-13: the overfunded mirror this TODO used to leave open is exactly what IsChronicOverfund now answers (found in the field via Storage Unit Rental — two concurrent plans permanently outpacing the goal read identically to a one-off surplus without it). This is CurrentlyOverfunded's own correct home, not a second one: "Current jar state" (planning/22 §6a) no longer exists as its own control — SummaryRegion.xaml.cs's own header comment records it being folded into Summary's aside, whose first line (AsideText) is exactly what CurrentJarStateLine/LiveJarStateLine populate. This method fills the SAME aside's second line (AsideSecondaryText) — one UI element, two fact slots — not a competing home the way briefly adding this category to RRulePreviewCaption (a genuinely different UI element, the RRule editor's own caption) actually was.</summary>
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

    /// <summary>[CALC] The RRule preview's own caption — generic ("Projected short/overfunded"), except each side splits on its own IsChronicShortfall/IsChronicOverfund and reuses SummaryRecurringPhrase's exact words rather than a new synonym. Null for Healthy/AlreadyMissing/CurrentlyOverfunded — this region only ever speaks for the future two (planning/22 §6b's own settled rule: "every one of the four non-Healthy categories has exactly one home... today's two in Current jar state[/Summary's own aside — the same UI element, see SummaryRecurringPhrase's own comment], the future two here"). Reverted 2026-08-13: briefly grew a CurrentlyOverfunded branch, which put that category in two homes at once — the actual bug (CurrentlyOverfunded's own aside-block wiring, via SummaryRecurringPhrase, was simply incomplete in one of the two forms) lived elsewhere; see that method's own comment for where "Consistently ahead" actually belongs.</summary>
    /// <param name="state">The plan's current health state.</param>
    public static string? RRulePreviewCaption(PlanHealthState state) => state.MostImportantHealthState switch
    {
        PlanHealthCategory.WillMiss => state.IsChronicShortfall ? "Keeps falling short" : "Projected short",
        PlanHealthCategory.WillBeOverfunded => state.IsChronicOverfund ? "Consistently ahead" : "Projected overfunded",
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

    /// <summary>[CALC] The funds-aware first-payment warning, as a two-line block: a plain-language verdict, then a compact "$X due · $Y set aside · $Z free" facts strip that carries more detail the more serious the case is. Deliberately TENTATIVE when reassuring ("you'll probably have the free cash") and FIRM when warning — the free-funds figure is naive, unable to see another unallocated expense landing the same day and eyeing the same cash, so it can overstate the reassuring case (hence "probably," itself a small nudge that setting funds aside is what removes the doubt) while any shortfall it does find only understates (competing expenses make it worse, never better). Takes raw numbers so both the live form preview and the saved Summary aside can call it; a null freeFunds (the no-forecast preview path) falls back to the plain set-aside gap with no strip. Returns null when nothing is short, or the first occurrence has already happened.</summary>
    /// <param name="isFirstOccurrencePending">Whether the first scheduled payment hasn't happened yet.</param>
    /// <param name="firstPaymentAmount">What that first payment (or, for a one-time goal, the goal) costs.</param>
    /// <param name="setAsideShortfall">How much of that first payment isn't set aside yet — 0 when the plan already covers it.</param>
    /// <param name="freeFunds">Free-to-spend cash on hand entering the payment day, or null when it isn't known.</param>
    /// <param name="balance">Total money expected in the account that day — frames free as "$900 free of $1,355"; may be null.</param>
    /// <param name="isOneTime">Whether this is a one-time goal (no "first" of several — it IS the goal).</param>
    public static string? FirstPaymentCoverageLine(
        bool isFirstOccurrencePending, decimal firstPaymentAmount, decimal setAsideShortfall,
        decimal? freeFunds, decimal? balance, bool isOneTime)
    {
        if (!isFirstOccurrencePending || setAsideShortfall <= 0m)
        {
            return null;
        }

        var setAside = firstPaymentAmount - setAsideShortfall;
        var amountLabel = isOneTime ? "needed" : "due";

        // Free amount unknown (the no-forecast preview path): no forecast to
        // read a strip from, so fall back to the plain set-aside gap.
        if (freeFunds is not decimal free)
        {
            return isOneTime
                ? $"{setAsideShortfall:C0} short for reaching your goal"
                : $"{setAsideShortfall:C0} short for the first payment";
        }

        // The money's PROBABLY there, just not earmarked: free cash covers the
        // gap. Tentative on purpose — see this method's own summary.
        if (free >= setAsideShortfall)
        {
            var verdict = "You'll probably have the free cash — it just isn't set aside.";
            var facts = $"{firstPaymentAmount:C0} {amountLabel} · {setAside:C0} set aside · {free:C0} free";
            return $"{verdict}\n{facts}";
        }

        // Genuinely short: even free cash can't cover the gap. Firm — the safe
        // direction. "free of total" makes plain that money exists but is
        // locked in other goals.
        var stillShort = setAsideShortfall - free;
        var freeOfBalance = balance is decimal have ? $"{free:C0} free of {have:C0}" : $"{free:C0} free";
        var shortVerdict = isOneTime ? "You'll be short even after free cash." : "Short even after free cash.";
        var shortFacts = $"{firstPaymentAmount:C0} {amountLabel} · {setAside:C0} set aside · {freeOfBalance} · {stillShort:C0} short";
        return $"{shortVerdict}\n{shortFacts}";
    }

    /// <summary>[CALC] Whether a savings plan is a deliberate holding pattern (redesign/planning/26-editing-an-earmark-pattern.md, "the pause case") rather than an active contribution — nothing will actually land in the jar, either because the plan's own rule produces zero occurrences, or because every occurrence shares the pattern's one Amount at $0. True for either condition alone.</summary>
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
