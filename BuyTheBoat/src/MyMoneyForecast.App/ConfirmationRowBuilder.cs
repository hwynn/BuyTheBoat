namespace MyMoneyForecast.App;

// Everything BuildRows needs to project the rows — the trigger booleans and
// the pre-computed warning/description strings, gathered by each entry point
// from its own state. An internal build-time intermediate, so the public
// request stays down to what the popup actually reads (Description, Rows,
// PlanShapeCandidates). Fields default to false / "" so each builder sets
// only the ones its own path can raise.
internal sealed record RowInputs
{
    public bool IsChangeCritical { get; init; }
    public bool HasMultipleEarmarkPatterns { get; init; }
    public bool ConsolidationNeeded { get; init; }
    public string ConsolidationForcedReason { get; init; } = "";
    public string ConsolidationCaveat { get; init; } = "";
    public string SourceChangeWarning { get; init; } = "";
    public bool TouchesChainBoundary { get; init; }
    public bool PlanTouchesChainBoundary { get; init; }
    public string StayLinkedWarning { get; init; } = "";
    public string LetItBreakWarning { get; init; } = "";
    public bool ChangeCanCascade { get; init; }
    public bool PlanChangeCanCascade { get; init; }
    public string CascadeDescription { get; init; } = "";
    public bool TrivialFieldsCanCascade { get; init; }
    public string TrivialFieldsCascadeDescription { get; init; } = "";
    public bool PacedBillsCanCascade { get; init; }
    public string PacedBillsCascadeDescription { get; init; } = "";
}

// The one row-projection both save-confirmation wrappers share
// (FinancePatternSaveConfirmation and EarmarkPatternSaveConfirmation). Each
// wrapper works out its own conditions, fills a RowInputs, and calls BuildRows;
// neither wrapper knows the other, and there's a single place that decides which
// rows a given set of inputs produces — so the two paths can never drift apart
// in what the popup shows.
internal static class ConfirmationRowBuilder
{
    /// <summary>[CALC] Projects the computed inputs into the confirmation-row list (planning/28) — one row per section, in most-vital-first order, under the same visibility conditions the hand-built popup used. Serves both entry points: the earmark path only sets the chain fields, so it naturally yields just those rows.
    ///
    /// Deliberately NOT emitted, because today's popup surfaces neither, so turning them into rows would offer a choice that isn't offered now — a behavior change left to a later thread: the plan-shape picker (PlanShapeCandidates → a CandidatePickerRow, still carried on the request) and the amount-scale rider (a CheckboxRiderRow).
    ///
    /// An always-shown description that accompanies a choice (the cascade/trivial/paced-bills descriptions) rides as BOTH options' Consequence, so the popup's "footer under the selected option" shows it whichever option is picked. A per-option warning (the chain-boundary case) rides only on the option it belongs to.</summary>
    public static IReadOnlyList<ConfirmationRow> BuildRows(RowInputs r)
    {
        var rows = new List<ConfirmationRow>();

        // A Critical edit always breaks off from today — announced, not asked
        // (forward-only). Mirrors AlterPastSection.
        if (r.IsChangeCritical)
        {
            rows.Add(new AnnouncementRow(ConfirmationRowIds.AlterPast,
                "This reaches back to history that's already happened, so it will start a new segment from today — your past records stay exactly as they were."));
        }

        // Item F's consolidation choice — only when there's a real choice to
        // make (more than one plan, and the schedule/start date isn't forcing
        // consolidation). Mirrors ConsolidationAskSection.
        if (r.HasMultipleEarmarkPatterns && !r.ConsolidationNeeded)
        {
            rows.Add(new ChoiceRow(ConfirmationRowIds.Consolidation,
                "It has more than one savings plan. What do you want to do?",
                [
                    new ChoiceOption("Keep them separate", "", ""),
                    new ChoiceOption("Combine them into one", "", ""),
                ],
                DefaultIndex: 0,
                Layout: OptionLayout.Stacked));

            // The always-shown caveat that a break-off combines plans
            // regardless of this choice — its own line, not tied to a radio
            // (mirrors ConsolidationCaveatText).
            if (!string.IsNullOrEmpty(r.ConsolidationCaveat))
            {
                rows.Add(new AnnouncementRow(ConfirmationRowIds.ConsolidationCaveat, r.ConsolidationCaveat));
            }
        }

        // Item F's forced case — announced, not asked. Mirrors
        // ConsolidationForcedText.
        if (r.HasMultipleEarmarkPatterns && r.ConsolidationNeeded && !string.IsNullOrEmpty(r.ConsolidationForcedReason))
        {
            rows.Add(new AnnouncementRow(ConfirmationRowIds.ConsolidationForced, r.ConsolidationForcedReason));
        }

        // planning/27's Source row — "warn, don't block." Mirrors
        // SourceChangeWarningText.
        if (!string.IsNullOrEmpty(r.SourceChangeWarning))
        {
            rows.Add(new AnnouncementRow(ConfirmationRowIds.SourceChange, r.SourceChangeWarning));
        }

        // "Stay linked or break" — shared by both chain types (only one is ever
        // true per request). Each option carries its own consequence, shown
        // while it's the selected one. Mirrors ChainBoundarySection.
        if (r.TouchesChainBoundary || r.PlanTouchesChainBoundary)
        {
            rows.Add(new ChoiceRow(ConfirmationRowIds.ChainBoundary,
                "This plan is part of a chain. Should it stay connected to its neighbor?",
                [
                    new ChoiceOption("Keep it linked — adjust the neighboring segment to match", "", r.StayLinkedWarning),
                    new ChoiceOption("Let the chain break", "", r.LetItBreakWarning),
                ],
                DefaultIndex: 0,
                Layout: OptionLayout.Stacked));
        }

        // "Cascade forward or not" for an Amount/shape change. The always-shown
        // description rides as both options' consequence. Mirrors
        // CascadeSection.
        if (r.ChangeCanCascade || r.PlanChangeCanCascade)
        {
            rows.Add(new ChoiceRow(ConfirmationRowIds.Cascade,
                "This change could also apply to later segments in the chain. What do you want to do?",
                [
                    new ChoiceOption("Apply it going forward too", "", r.CascadeDescription),
                    new ChoiceOption("Only this segment", "", r.CascadeDescription),
                ],
                DefaultIndex: 0,
                Layout: OptionLayout.Stacked));
        }

        // Phase 1's trivial-fields cascade (Priority/Mandatory/Description/
        // AutoRenew) — no EarMarkPattern equivalent. Defaults to "just this
        // segment" (index 0), the one place this doesn't mirror Amount/shape's
        // own default. Mirrors TrivialFieldsCascadeSection.
        if (r.TrivialFieldsCanCascade)
        {
            rows.Add(new ChoiceRow(ConfirmationRowIds.TrivialFieldsCascade,
                "This also changes details like priority, mandatory, or description. Update later segments too?",
                [
                    new ChoiceOption("Only this segment", "", r.TrivialFieldsCascadeDescription),
                    new ChoiceOption("Apply it going forward too", "", r.TrivialFieldsCascadeDescription),
                ],
                DefaultIndex: 0,
                Layout: OptionLayout.Stacked));
        }

        // The paycheck-association cascade. The popup pre-selects "update them"
        // (index 0) — the author's chosen default for what a real user sees, so
        // matching the bills to the new schedule is one Save away (changed from
        // "leave them" 2026-08-19, on the author's call). The headless fallback
        // (DefaultOutcome) still declines, so nothing re-paces money
        // when no one was actually asked. Mirrors PacedBillsCascadeSection.
        //
        // TODO (content, not mechanism): both options below carry the same
        // description, so the footer reads the same whichever the user picks.
        // The popup CAN show a different consequence per option — the
        // chain-boundary row already does — so give "leave them" its own line
        // (e.g. what stays stale until it's edited) if a per-option message
        // here is ever wanted.
        if (r.PacedBillsCanCascade)
        {
            rows.Add(new ChoiceRow(ConfirmationRowIds.PacedBillsCascade,
                "Your paycheck's schedule changed. Update its associated savings plan(s) too?",
                [
                    new ChoiceOption("Update them to match the new schedule", "", r.PacedBillsCascadeDescription),
                    new ChoiceOption("Leave them as they are", "", r.PacedBillsCascadeDescription),
                ],
                DefaultIndex: 0,
                Layout: OptionLayout.Stacked));
        }

        return rows;
    }
}
