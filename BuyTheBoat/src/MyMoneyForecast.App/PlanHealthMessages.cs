using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// Turns PlanHealthState's numbers into the exact user-facing wording settled
// in planning/22 §6 — kept separate from any UserControl so the wording
// itself is plain, testable logic, and so Current jar state / Summary / the
// RRule preview all read from one place instead of composing strings
// independently. Every method here corresponds to one settled row in
// planning/22's own content tables; each doc comment cites which.
public static class PlanHealthMessages
{
    // planning/22 §6a, SETTLED: the append-only delta for the two "today"
    // states. Null for Healthy/WillMiss/WillBeOverfunded — those either show
    // no delta at all (Healthy) or use SummaryFutureLine's own sentence
    // instead (the future two).
    public static string? CurrentJarStateDelta(PlanHealthState state) => state.MostImportantHealthState switch
    {
        PlanHealthCategory.AlreadyMissing => $"{state.CurrentShortfallAmount:C0} short",
        PlanHealthCategory.CurrentlyOverfunded => $"{state.CurrentOverfundedAmount:C0} over",
        _ => null,
    };

    // planning/22 §6a, SETTLED, full sentence: "$X saved of $Y milestone[
    // — $Z short/over]" — the base fact always shows; the delta only appends
    // for the two "today" states. jar must be this Savings Plan's own
    // FundJar (today's reading) — PlanHealthState itself only carries the
    // derived deltas, not the raw saved/milestone amounts.
    public static string CurrentJarStateLine(FundJar jar, PlanHealthState state)
    {
        var basic = $"{jar.ExpectedAmount:C0} saved of {jar.MilestoneAmount ?? 0m:C0} milestone";
        var delta = CurrentJarStateDelta(state);
        return delta is null ? basic : $"{basic} — {delta}";
    }

    // Author, 2026-08-07: the live counterpart to CurrentJarStateLine — same
    // exact wording, computed straight from raw Expected/Milestone amounts
    // instead of reading them off a saved FundJar/PlanHealthState. Exists
    // because PlanHealthState's OTHER fields (Shortfall, IsChronicShortfall)
    // are whole-span, due-date-anchored figures with no live equivalent for
    // a proposed, not-yet-saved pattern — this sticks to the one pair of
    // numbers that genuinely can be computed live (see
    // EarmarkFormPanel.GetLiveJarAmounts's own header comment for how).
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

    // planning/22 §6c, SETTLED (Summary A/B aside, one-time goal): the
    // future-facing pair — "on pace for $Y of $Z needed — $W short/over."
    // Healthy uses today's own jar/milestone (same numbers CurrentJarStateLine
    // would show, just phrased as "on track" instead of appending a delta) —
    // there's nothing to project when today's reading is already fine.
    // AlreadyMissing/CurrentlyOverfunded return null; those two live in
    // CurrentJarStateLine instead (planning/22 §6, the per-region intent
    // table, is the map of which region owns which state).
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

    // planning/22 §6c, SETTLED 2026-08-05 (repeating pattern, Summary C
    // aside's second fact slot — replaces "Lifetime contributed"): flags
    // *that* the shortfall recurs, gated on IsChronicShortfall plus an
    // already-showing shortfall state — a modifier on an existing warning,
    // not an independent announcement.
    //
    // TODO (2026-08-05): the overfunded mirror of this was never actually
    // settled — doc 22 only ever confirmed the shortfall phrase, and whether
    // a repeating overfunded plan needs its own Summary-aside phrase (vs.
    // just relying on the RRule preview's "Projected overfunded") is an open
    // question, not resolved here. Shipping the shortfall case now and
    // returning null for the overfunded one (no phrase — same as before this
    // slot existed) rather than block on it. Candidate direction for the next
    // sweep, NOT adopted, deliberately left as a comment rather than a live
    // branch so it doesn't silently start firing before anyone decided it should:
    //     PlanHealthCategory.CurrentlyOverfunded or PlanHealthCategory.WillBeOverfunded
    //         => "Consistently ahead.",
    public static string? SummaryRecurringPhrase(PlanHealthState state)
    {
        var isShortfallState = state.MostImportantHealthState is PlanHealthCategory.AlreadyMissing or PlanHealthCategory.WillMiss;
        return isShortfallState && state.IsChronicShortfall ? "Keeps falling short." : null;
    }

    // planning/22 §6b, SETTLED 2026-08-05: the RRule preview's own caption —
    // generic ("Projected short/overfunded"), except the shortfall case
    // splits on IsChronicShortfall and reuses SummaryRecurringPhrase's exact
    // words rather than a new synonym. Null for Healthy/AlreadyMissing/
    // CurrentlyOverfunded — this region only ever speaks for the future two.
    public static string? RRulePreviewCaption(PlanHealthState state) => state.MostImportantHealthState switch
    {
        PlanHealthCategory.WillMiss => state.IsChronicShortfall ? "Keeps falling short" : "Projected short",
        PlanHealthCategory.WillBeOverfunded => "Projected overfunded",
        _ => null,
    };

    // Author, 2026-08-08: "a legend explaining the color should appear...
    // The function running the rrule preview is ignorant of that, so the
    // caller will have to include that in the optional message" —
    // RecurrenceRuleEditor only ever applies a color to whatever dates
    // it's told; it has no idea those dates come from
    // PlanHealthState.UnderfundedReleaseDates, or what that means. This is
    // that explanation, supplied from here rather than composed ad hoc in
    // the form — always the same wording, since the highlight always
    // means the same thing regardless of which caption (or none) goes
    // with it: UnderfundedReleaseDates is a real, per-occurrence forecast
    // fact, computed independently of MostImportantHealthState's own
    // whole-plan verdict (a plan can read WillBeOverfunded overall and
    // still have one rough release highlighted here — Car Insurance Co in
    // the seed data is exactly that case).
    public const string UnderfundedReleaseHighlightLegend = "Highlighted: this release came up short of what it needed.";

    // planning/22 §6b: wording SETTLED, mechanism still pending — needs the
    // form/UI layer's at-open-vs-live diff (not built; that layer doesn't
    // exist yet). Not called from anywhere yet; the strings exist so whoever
    // builds that diffing mechanism doesn't also have to invent the copy.
    public const string FixedNoLongerShort = "Fixed — no longer short";
    public const string FixedNoLongerOverfunded = "Fixed — no longer overfunded";

    // Author, 2026-08-07: the Starting-point region's own new warning —
    // takes the raw values rather than a PlanHealthState so both callers can
    // use it: the Earmark form's live check (a proposed, not-yet-saved
    // pattern has no PlanHealthState of its own to read) and, per the
    // author's own "doesn't hurt to show this in a couple of places," the
    // Summary region's aside (which does have a real, saved one).
    // REVISED same day (author's own correction): "for the first payment"
    // reads oddly for a one-time goal — there is no "first" of several, this
    // IS the goal, so it says so instead. isOneTime matches the established
    // GetOccurrences().Count == 1 idiom (ExpenseFormPanel, ExpenseKind) —
    // not a new concept, just this method's first use of it.
    public static string? FirstOccurrenceShortfallLine(bool isFirstOccurrencePending, decimal firstOccurrenceShortfall, bool isOneTime) =>
        isFirstOccurrencePending && firstOccurrenceShortfall > 0m
            ? isOneTime
                ? $"{firstOccurrenceShortfall:C0} short for reaching your goal"
                : $"{firstOccurrenceShortfall:C0} short for the first payment"
            : null;

    // planning/21 "Four plan-health states, SETTLED 2026-08-02" / "Labels,
    // SETTLED 2026-08-05": the Expense form's own compact status indicator,
    // next to its save buttons — one of the four states the linked plan is
    // in, or null (nothing shown) for Healthy. A deliberately different
    // word pair (Underfunded/Overfunded, Currently/Projected) from the
    // Earmark form's own "short"/"over" narrative vocabulary — planning/21's
    // own note: "two different registers for the same underlying states,
    // both intentional: this is a compact status label, those are
    // narrative sentences." Author, 2026-08-07: "that warning text is one
    // of the first things we made that health state class to handle" —
    // this is that: the label is a pure function of MostImportantHealthState,
    // nothing composed ad hoc in the form itself.
    public static string? ExpenseStatusLabel(PlanHealthCategory category) => category switch
    {
        PlanHealthCategory.AlreadyMissing => "Currently Underfunded",
        PlanHealthCategory.WillMiss => "Underfunding Projected",
        PlanHealthCategory.CurrentlyOverfunded => "Currently Overfunded",
        PlanHealthCategory.WillBeOverfunded => "Overfunding Projected",
        _ => null,
    };
}
