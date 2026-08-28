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

    // The nested "these kept-separate plans over/underfund the new amount — adjust
    // them?" question's own wording, or "" when there's no funding gap to correct
    // (so the row isn't shown). Rides under the Consolidation row's "keep them
    // separate" option, revealed only while that option is picked.
    public string KeepSeparateFundingQuestion { get; init; } = "";

    public string SourceChangeWarning { get; init; } = "";

    // The goal-health suggestion's own accept/reject wording, or "" when there's
    // no correction to offer (so the row isn't shown). Accepting pre-fills the
    // single plan's form with the correction as an unsaved edit.
    public string GoalHealthSuggestionQuestion { get; init; } = "";

    // The plan's own health heads-up (the former post-save "Worth a look"
    // MessageBox), or "" when there's nothing worth surfacing. A plain
    // announcement — no choice attached.
    public string ConcerningPlanNotice { get; init; } = "";
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

    // One entry per later finance pattern the edited finance pattern's amount
    // change is carried forward onto that's funded by more than one earmark
    // pattern — each becomes its own cross-boundary combine-or-keep-separate
    // ChoiceRow. Empty unless the amount change is actually carried forward onto
    // such a finance pattern.
    public IReadOnlyList<CrossBoundaryConsolidationInput> CrossBoundaryConsolidations { get; init; } = [];

    // Whether a consolidation is on the table this save, so the two consolidate-
    // strategy questions apply — sizing always, spread only when a clear income
    // exists (otherwise both spreads land on the same result).
    public bool ShowConsolidationSizing { get; init; }
    public bool ShowConsolidationSpread { get; init; }
}

// One later segment that needs a cross-boundary combine-or-keep-separate question,
// identified by its finance_id, with a short label naming which segment it is.
internal sealed record CrossBoundaryConsolidationInput(int FinanceId, string Label);

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
        // consolidation). Mirrors ConsolidationAskSection. Both answers are now
        // honored on a break-off (keep-separate gives the successor one plan per
        // surviving plan), so this no longer carries a "not supported yet" caveat.
        if (r.HasMultipleEarmarkPatterns && !r.ConsolidationNeeded)
        {
            // When keeping them separate would over/underfund the new amount, a
            // nested question offers to re-rate them to meet it — revealed only
            // while "keep them separate" is the pick (combining folds them into
            // one instead, so it never applies there). Its OWN row, never merged
            // into the keep-separate/combine choice above: that one decides
            // whether the plans stay several, this one how much each contributes.
            List<ConfirmationRow> keepSeparateChildren = string.IsNullOrEmpty(r.KeepSeparateFundingQuestion)
                ? []
                :
                [
                    new ChoiceRow(ConfirmationRowIds.KeepSeparateFunding,
                        r.KeepSeparateFundingQuestion,
                        [
                            new ChoiceOption("Adjust them to meet the goal", "", ""),
                            new ChoiceOption("Leave them as they are (may miss the goal)", "", ""),
                        ],
                        DefaultIndex: 0,
                        Layout: OptionLayout.Stacked),
                ];

            rows.Add(new ChoiceRow(ConfirmationRowIds.Consolidation,
                "It has more than one savings plan. What do you want to do?",
                [
                    new ChoiceOption("Keep them separate", "", "") { Children = keepSeparateChildren },
                    new ChoiceOption("Combine them into one", "", ""),
                ],
                DefaultIndex: 0,
                Layout: OptionLayout.Stacked));
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

        // The goal-health suggestion (planning/25): a single plan that no longer
        // meets its edited goal, offered a correction to pre-fill its own form
        // with. Accept is pre-selected (the healthy option); reject carries no
        // consequence yet — reserved for cases where leaving it would actually
        // break something (a paycheck/bill desync and the like).
        // TODO: once more than one correction can be proposed, dedupe identical
        // ones before this renders — never show two options suggesting the same
        // thing (the author called this out as a must-check before rendering).
        if (!string.IsNullOrEmpty(r.GoalHealthSuggestionQuestion))
        {
            rows.Add(new ChoiceRow(ConfirmationRowIds.GoalHealthSuggestion,
                r.GoalHealthSuggestionQuestion,
                [
                    new ChoiceOption("Load the suggested amount", "", ""),
                    new ChoiceOption("Leave it as is", "", ""),
                ],
                DefaultIndex: 0,
                Layout: OptionLayout.Stacked));
        }

        // The plan's own health heads-up — a plain announcement, no choice. Was a
        // separate post-save MessageBox; now it rides the confirmation like every
        // other message this save surfaces.
        if (!string.IsNullOrEmpty(r.ConcerningPlanNotice))
        {
            rows.Add(new AnnouncementRow(ConfirmationRowIds.ConcerningPlan, r.ConcerningPlanNotice));
        }

        // "Cascade forward or not" for an Amount/shape change, built first so it
        // can be nested under the chain question below. The cross-boundary Q6
        // questions (planning/28) nest under its "apply going forward" option —
        // they only matter if the change actually carries forward, so the popup
        // shows them only while that option is selected, instead of as flat rows
        // always visible. One per later finance pattern the amount change reaches
        // that's funded by more than one earmark pattern; each its OWN row kind,
        // never the break-off Consolidation above (the two do different things
        // and never appear together — an edit is a break-off or a carry-forward,
        // not both). Default "keep them separate" (0) — the less-destructive
        // option, matching the break-off row's default.
        ChoiceRow? cascadeRow = null;
        if (r.ChangeCanCascade || r.PlanChangeCanCascade)
        {
            var carryForwardChildren = r.CrossBoundaryConsolidations
                .Select(crossBoundary => (ConfirmationRow)new ChoiceRow(
                    ConfirmationRowIds.CrossBoundaryConsolidation(crossBoundary.FinanceId),
                    $"A later segment ({crossBoundary.Label}) has more than one savings plan. What should happen to them?",
                    [
                        new ChoiceOption("Keep them separate", "", ""),
                        new ChoiceOption("Combine them into one", "", ""),
                    ],
                    DefaultIndex: 0,
                    Layout: OptionLayout.Stacked))
                .ToList();

            // The two consolidate-strategy questions ride alongside the Q6 rows,
            // under "apply going forward" — they matter whenever a consolidation
            // happens downstream (a chosen combine, or a forced shape-change fold).
            // Sizing always; spread only when a clear income makes the two differ.
            if (r.ShowConsolidationSizing)
            {
                carryForwardChildren.Add(new ChoiceRow(ConfirmationRowIds.ConsolidationSizing,
                    "When savings plans get combined, how should the combined plan be sized?",
                    [
                        new ChoiceOption("Adjust it to meet the goal", "", ""),
                        new ChoiceOption("Keep saving at the current rate (may miss the goal)", "", ""),
                    ],
                    DefaultIndex: 0,
                    Layout: OptionLayout.Stacked));
            }

            if (r.ShowConsolidationSpread)
            {
                carryForwardChildren.Add(new ChoiceRow(ConfirmationRowIds.ConsolidationSpread,
                    "And how should the combined plan's contributions be spread?",
                    [
                        new ChoiceOption("Across your paydays", "", ""),
                        new ChoiceOption("Evenly over time", "", ""),
                    ],
                    DefaultIndex: 0,
                    Layout: OptionLayout.Stacked));
            }

            cascadeRow = new ChoiceRow(ConfirmationRowIds.Cascade,
                "This change could also apply to later segments in the chain. What do you want to do?",
                [
                    new ChoiceOption("Apply it going forward too", "", r.CascadeDescription) { Children = carryForwardChildren },
                    new ChoiceOption("Only this segment", "", r.CascadeDescription),
                ],
                DefaultIndex: 0,
                Layout: OptionLayout.Stacked);
        }

        // "Stay linked or break" — shared by both chain types (only one is ever
        // true per request). The cascade question nests under "keep it linked":
        // breaking the chain leaves no forward chain to carry the change onto
        // (planning/28's "break ⇒ no forward chain ⇒ Q4 gone"), so the popup hides
        // it there — and the wrapper gates the cascade on the stay-linked answer
        // to match. Each option carries its own consequence, shown while selected.
        if (r.TouchesChainBoundary || r.PlanTouchesChainBoundary)
        {
            rows.Add(new ChoiceRow(ConfirmationRowIds.ChainBoundary,
                "This plan is part of a chain. Should it stay connected to its neighbor?",
                [
                    new ChoiceOption("Keep it linked — adjust the neighboring segment to match", "", r.StayLinkedWarning)
                    {
                        Children = cascadeRow is null ? [] : [cascadeRow],
                    },
                    new ChoiceOption("Let the chain break", "", r.LetItBreakWarning),
                ],
                DefaultIndex: 0,
                Layout: OptionLayout.Stacked));
        }
        else if (cascadeRow is not null)
        {
            rows.Add(cascadeRow); // no chain boundary in play — the cascade question stands on its own
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
