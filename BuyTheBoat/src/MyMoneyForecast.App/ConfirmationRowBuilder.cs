namespace MyMoneyForecast.App;

// Everything BuildRows needs to project the rows — the trigger booleans and
// the pre-computed warning/description strings, gathered by each entry point
// from its own state. An internal build-time intermediate, so the public
// request stays down to what the popup actually reads (Description, Rows).
// Fields default to false / "" so each builder sets only the ones its own
// path can raise.
internal sealed record RowInputs
{
    public bool IsChangeCritical { get; init; }

    // The break-off successor's alternative savings-plan shapes (planning/25 Item
    // G / Q2), or empty when there's no real choice — most edits, or a break-off
    // with no existing plan to draw an alternative from. More than one entry means
    // a genuine choice; each carries its own proposed plan, so this becomes a
    // CandidatePickerRow rather than a plain ChoiceRow.
    public IReadOnlyList<FinancePatternSaveConfirmation.PlanShapeCandidate> PlanShapeCandidates { get; init; } = [];

    public bool HasMultipleEarmarkPatterns { get; init; }
    // Whether at least one of the plans is one the user made their own — the
    // "combine / keep separate" question is only shown when this is true (a set
    // of untouched dummies has no meaningful combine/keep-separate choice).
    public bool HasExplicitEarmarkPattern { get; init; }
    public bool ConsolidationNeeded { get; init; }
    public string ConsolidationForcedReason { get; init; } = "";

    // The nested "these kept-separate plans over/underfund the new amount — adjust
    // them?" question's own wording, or "" when there's no funding gap to correct
    // (so the row isn't shown). Rides under the Consolidation row's "keep them
    // separate" option, revealed only while that option is picked.
    public string KeepSeparateFundingQuestion { get; init; } = "";

    public string SourceChangeWarning { get; init; } = "";

    // The goal-health suggestion's own question wording, or "" when there's no
    // correction to offer (so the row isn't shown). Picking a correction pre-fills
    // the single plan's form with it as an unsaved edit.
    public string GoalHealthSuggestionQuestion { get; init; } = "";

    // One option label per goal-health correction offered, in recommendation
    // order — the picker renders these, then a trailing "leave it as is". Today
    // exactly one ("Load the suggested amount"); empty when no correction applies.
    // Set for the UNDERfunded case; the overfunded case uses the fields below.
    public IReadOnlyList<string> GoalHealthCorrectionLabels { get; init; } = [];

    // The OVERfunded goal-health nested flow. GoalHealthLowerRateLabel is the
    // "lower the contribution to $X" option (also the presence flag for this
    // shape); GoalHealthKeepRateLabel is the "keep saving at this rate" option the
    // skip sub-question nests under; GoalHealthSkipQuestion + GoalHealthSkipLabels
    // are that sub-question's prompt and its strategy options (a trailing "don't
    // skip any" is appended by the builder). All empty when not overfunded.
    public string GoalHealthLowerRateLabel { get; init; } = "";
    public string GoalHealthKeepRateLabel { get; init; } = "";
    public string GoalHealthSkipQuestion { get; init; } = "";
    public IReadOnlyList<string> GoalHealthSkipLabels { get; init; } = [];

    // The consequence footer shown under the goal-health suggestion's "leave it
    // as is" option — names what rejecting costs (the goal falling short, or
    // money tied up). "" when there's no suggestion. See ChoiceOption.Consequence.
    public string GoalHealthRejectWarning { get; init; } = "";

    // The plan's own health heads-up (the former post-save "Worth a look"
    // MessageBox), or "" when there's nothing worth surfacing. A plain
    // announcement — no choice attached.
    public string ConcerningPlanNotice { get; init; } = "";

    // "[bill] occurs N more times" — the heads-up that moving a boundary outward
    // grew a plan to keep pace. "" when no boundary extended a plan. Announcement.
    public string BoundaryExtensionAnnouncement { get; init; } = "";
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

    // The two paced-bills cascade footers, one per option: "update them" states
    // the plans will be re-paced, "leave them" states they stay on the old
    // schedule until edited. Both "" when nothing was invalidated.
    public string PacedBillsCascadeDescription { get; init; } = "";
    public string PacedBillsLeaveDescription { get; init; } = "";

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

    // Earmark path only: offer the "split at today vs recalculate the whole plan"
    // choice for an amount/rate edit to a lone savings plan that already has past
    // contributions. Break off (index 0) is the pre-selected default — it keeps
    // what's already set aside instead of re-rating the jar's whole history.
    public bool OfferRerateBreakOff { get; init; }
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
    /// The plan-shape picker (Q2) is emitted as a CandidatePickerRow right under the break-off announcement, so the successor's shape is a real choice rather than a silent "Recommended."
    ///
    /// An always-shown description that accompanies a choice (the cascade/trivial/paced-bills descriptions) rides as BOTH options' Consequence, so the popup's "footer under the selected option" shows it whichever option is picked. A per-option warning (the chain-boundary case) rides only on the option it belongs to.</summary>
    public static IReadOnlyList<ConfirmationRow> BuildRows(RowInputs r)
    {
        var rows = new List<ConfirmationRow>();

        // A Critical edit always breaks off from today — announced, not asked
        // (forward-only).
        if (r.IsChangeCritical)
        {
            rows.Add(new AnnouncementRow(ConfirmationRowIds.WarnAboutBreakOff,
                "This reaches back to history that's already happened, so it will start a new segment from today — your past records stay exactly as they were."));
        }

        // Item G / Q2 — the new segment's savings plan can be shaped a few ways
        // (Recommended, keep the same schedule, keep the same amount). Only shown
        // when there's a real choice (more than one candidate); the picker returns
        // the chosen candidate's own plan as ChosenPlanShape, so it's its own row
        // kind, not a ChoiceRow. Recommended is index 0, the pre-selected default.
        if (r.PlanShapeCandidates.Count > 0)
        {
            rows.Add(new CandidatePickerRow(ConfirmationRowIds.PlanShape,
                "How should the new segment's savings plan be shaped?",
                r.PlanShapeCandidates,
                DefaultIndex: 0));
        }

        // Item F's consolidation choice — only when there's a real choice to
        // make (more than one plan, the schedule/start date isn't forcing
        // consolidation, and at least one plan is the user's own — folding or
        // splitting a set of untouched dummies alters nothing they explicitly
        // did). Mirrors ConsolidationAskSection. Both answers are now honored on
        // a break-off (keep-separate gives the successor one plan per surviving
        // plan), so this no longer carries a "not supported yet" caveat.
        if (r.HasMultipleEarmarkPatterns && !r.ConsolidationNeeded && r.HasExplicitEarmarkPattern)
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

        // The goal-health suggestion (planning/25): a plan that no longer meets
        // its edited goal, offered one or more corrections to pre-fill its own
        // form with, then a trailing "leave it as is." The first correction is
        // pre-selected (the healthy option); "leave it" carries a consequence
        // footer naming what that costs (the goal falling short, or money tied
        // up), shown only while it's the picked option. Identical corrections are
        // already collapsed upstream (DetermineGoalHealthSuggestionIfApplicable's
        // dedupe), so no two options here ever suggest the same thing.
        if (!string.IsNullOrEmpty(r.GoalHealthSuggestionQuestion) && r.GoalHealthCorrectionLabels.Count > 0)
        {
            // Underfunded: one option per offered correction, then a trailing
            // "leave it as is." The wrapper maps a chosen correction index back to
            // its overrides and treats the trailing option (index == correction
            // count) — and an unanswered row — as "no correction."
            var goalHealthOptions = r.GoalHealthCorrectionLabels
                .Select(label => new ChoiceOption(label, "", ""))
                .Append(new ChoiceOption("Leave it as is", "", r.GoalHealthRejectWarning))
                .ToList();

            rows.Add(new ChoiceRow(ConfirmationRowIds.GoalHealthSuggestion,
                r.GoalHealthSuggestionQuestion,
                goalHealthOptions,
                DefaultIndex: 0,
                Layout: OptionLayout.Stacked));
        }
        else if (!string.IsNullOrEmpty(r.GoalHealthSuggestionQuestion) && !string.IsNullOrEmpty(r.GoalHealthLowerRateLabel))
        {
            // Overfunded: a two-step nested question. Lower the rate to meet the
            // goal (recommended, pre-selected), or keep the rate — which reveals
            // the skip sub-question, when there's a whole contribution's surplus to
            // skip. The skip options list the strategies, then a trailing "don't
            // skip any" (its own default, so keeping the rate changes nothing
            // unless a skip is actively picked).
            ChoiceRow? skipRow = r.GoalHealthSkipLabels.Count > 0
                ? new ChoiceRow(ConfirmationRowIds.GoalHealthSkip,
                    r.GoalHealthSkipQuestion,
                    r.GoalHealthSkipLabels
                        .Select(label => new ChoiceOption(label, "", ""))
                        .Append(new ChoiceOption("Don't skip any", "", ""))
                        .ToList(),
                    DefaultIndex: r.GoalHealthSkipLabels.Count,
                    Layout: OptionLayout.Stacked)
                : null;

            rows.Add(new ChoiceRow(ConfirmationRowIds.GoalHealthSuggestion,
                r.GoalHealthSuggestionQuestion,
                [
                    new ChoiceOption(r.GoalHealthLowerRateLabel, "", ""),
                    new ChoiceOption(r.GoalHealthKeepRateLabel, "", r.GoalHealthRejectWarning)
                    {
                        Children = skipRow is null ? [] : [skipRow],
                    },
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

        // Moving a boundary outward grew a plan to keep pace — otherwise silent,
        // so this names how many more times the goal now occurs. Announcement.
        if (!string.IsNullOrEmpty(r.BoundaryExtensionAnnouncement))
        {
            rows.Add(new AnnouncementRow(ConfirmationRowIds.BoundaryExtension, r.BoundaryExtensionAnnouncement));
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

        // Editing a lone savings plan's amount/rate when it's already been
        // accumulating: split it at today (keep what's set aside) or recalculate
        // the whole plan. Break off is the pre-selected, non-destructive default;
        // re-rating carries the warning that the current set-aside amount changes.
        if (r.OfferRerateBreakOff)
        {
            rows.Add(new ChoiceRow(ConfirmationRowIds.EarmarkRerate,
                "This savings plan has already been setting money aside. How should the new amount apply?",
                [
                    new ChoiceOption(
                        "From today on — keep what's already set aside",
                        "Splits the plan at today: what you've saved so far stays put, and the new amount applies going forward.",
                        ""),
                    new ChoiceOption(
                        "Recalculate the whole plan",
                        "",
                        "The amount set aside so far will be recalculated at the new rate — it won't reflect what was actually put aside."),
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
        // when no one was actually asked. Each option carries its own
        // consequence, shown while selected. Mirrors PacedBillsCascadeSection.
        if (r.PacedBillsCanCascade)
        {
            rows.Add(new ChoiceRow(ConfirmationRowIds.PacedBillsCascade,
                "Your paycheck's schedule changed. Update its associated savings plan(s) too?",
                [
                    new ChoiceOption("Update them to match the new schedule", "", r.PacedBillsCascadeDescription),
                    new ChoiceOption("Leave them as they are", "", r.PacedBillsLeaveDescription),
                ],
                DefaultIndex: 0,
                Layout: OptionLayout.Stacked));
        }

        return rows;
    }
}
