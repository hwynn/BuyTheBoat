namespace BuyTheBoat.App;

/// <summary>
/// Everything BuildRows needs: the conditions and the ready-made warning and description strings, gathered by each confirmation class from its own state.
/// Kept internal so the public request holds only what the popup reads; fields default to false or "", so each class sets only the ones it can raise.
/// </summary>
internal sealed record RowInputs
{
    public bool IsChangeCritical { get; init; }

    /// <summary>The successor's plan shapes, or empty when there's no real choice; becomes a CandidatePickerRow.</summary>
    public IReadOnlyList<FinancePatternSaveConfirmation.PlanShapeCandidate> PlanShapeCandidates { get; init; } = [];

    public bool HasMultipleEarmarkPatterns { get; init; }

    /// <summary>Has the user made at least one plan their own? The combine-or-keep-separate question only shows then.</summary>
    public bool HasExplicitEarmarkPattern { get; init; }
    public bool ConsolidationNeeded { get; init; }
    public string ConsolidationForcedReason { get; init; } = "";

    /// <summary>The "adjust the kept-separate plans to meet the new amount?" wording, shown under "keep them separate" while it's picked; "" hides it.</summary>
    public string KeepSeparateFundingQuestion { get; init; } = "";

    public string SourceChangeWarning { get; init; } = "";

    /// <summary>The goal-health question, or "" to hide it; picking a correction pre-fills the plan's form with it as an unsaved edit.</summary>
    public string GoalHealthSuggestionQuestion { get; init; } = "";

    /// <summary>For an underfunded goal: one option label per correction, in recommendation order, before a final "leave it as is"; currently just "Load the suggested amount".</summary>
    public IReadOnlyList<string> GoalHealthCorrectionLabels { get; init; } = [];

    // For an overfunded goal: the "lower the contribution to X" option (its presence turns this case on), the "keep saving at this rate" option, and the skip question and options nested under it; all empty otherwise.
    public string GoalHealthLowerRateLabel { get; init; } = "";
    public string GoalHealthKeepRateLabel { get; init; } = "";
    public string GoalHealthSkipQuestion { get; init; } = "";
    public IReadOnlyList<string> GoalHealthSkipLabels { get; init; } = [];

    /// <summary>The consequence under "leave it as is": the goal falling short, or money tied up; "" when there's no suggestion.</summary>
    public string GoalHealthRejectWarning { get; init; } = "";

    /// <summary>The plan's health warning, shown as an announcement; "" when there's nothing to say.</summary>
    public string ConcerningPlanNotice { get; init; } = "";

    /// <summary>The "[bill] occurs N more times" announcement when a boundary moved outward and grew a plan; "" otherwise.</summary>
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

    // The paced-bills footers: "update them" says the plans will be re-paced, "leave them" says they stay on the old schedule until edited; both "" when nothing was affected.
    public string PacedBillsCascadeDescription { get; init; } = "";
    public string PacedBillsLeaveDescription { get; init; } = "";

    /// <summary>One entry per later segment the amount change cascades onto that several plans fund; each becomes its own combine-or-keep-separate row.</summary>
    public IReadOnlyList<CrossBoundaryConsolidationInput> CrossBoundaryConsolidations { get; init; } = [];

    // Whether the two consolidation questions apply: sizing whenever plans are combined, spread only when there's one clear income, since otherwise both spreads give the same result.
    public bool ShowConsolidationSizing { get; init; }
    public bool ShowConsolidationSpread { get; init; }

    /// <summary>Earmark form only: offer "split at today or recalculate the whole plan" for a rate change to a lone plan that's already saving; splitting (option 0) is the default.</summary>
    public bool OfferRerateBreakOff { get; init; }
}

/// <summary>One later segment needing a cross-boundary combine-or-keep-separate question: its finance id and a short label.</summary>
internal sealed record CrossBoundaryConsolidationInput(int FinanceId, string Label);

/// <summary>
/// Builds the confirmation rows for both FinancePatternSaveConfirmation and EarmarkPatternSaveConfirmation from their RowInputs.
/// Having one place decide which rows appear keeps the two from drifting apart in what the popup shows.
/// </summary>
internal static class ConfirmationRowBuilder
{
    /// <summary>
    /// [CALC] Turns the inputs into the confirmation rows, one per section, most important first; the Earmark form only sets the chain fields, so it gets just those rows.
    /// The plan-shape picker sits right under the break-off announcement.
    /// A description that applies whichever option is picked (cascade, trivial fields, paced bills) is set as both options' Consequence; a warning for one option (the chain boundary) is set only on that option.
    /// </summary>
    public static IReadOnlyList<ConfirmationRow> BuildRows(RowInputs r)
    {
        var rows = new List<ConfirmationRow>();

        // A Critical edit always breaks off from today, so it's announced, not asked.
        if (r.IsChangeCritical)
        {
            rows.Add(new AnnouncementRow(ConfirmationRowIds.WarnAboutBreakOff,
                "This reaches back to history that's already happened, so it will start a new segment from today — your past records stay exactly as they were."));
        }

        // The successor plan's shape (Recommended first, then same schedule or same amount), when there's a real choice; the pick comes back as ChosenPlanShape.
        if (r.PlanShapeCandidates.Count > 0)
        {
            rows.Add(new CandidatePickerRow(ConfirmationRowIds.PlanShape,
                "How should the new segment's savings plan be shaped?",
                r.PlanShapeCandidates,
                DefaultIndex: 0));
        }

        // Combine or keep separate, when there's a real choice: several plans, not forced to combine, and at least one the user made their own.
        if (r.HasMultipleEarmarkPatterns && !r.ConsolidationNeeded && r.HasExplicitEarmarkPattern)
        {
            // Under "keep them separate": offers to re-rate the plans when they'd over- or underfund the new amount.
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

        // The forced-consolidation case — announced, not asked.
        if (r.HasMultipleEarmarkPatterns && r.ConsolidationNeeded && !string.IsNullOrEmpty(r.ConsolidationForcedReason))
        {
            rows.Add(new AnnouncementRow(ConfirmationRowIds.ConsolidationForced, r.ConsolidationForcedReason));
        }

        // The Source row — "warn, don't block."
        if (!string.IsNullOrEmpty(r.SourceChangeWarning))
        {
            rows.Add(new AnnouncementRow(ConfirmationRowIds.SourceChange, r.SourceChangeWarning));
        }

        // The goal-health suggestion for a plan that no longer meets its edited goal.
        if (!string.IsNullOrEmpty(r.GoalHealthSuggestionQuestion) && r.GoalHealthCorrectionLabels.Count > 0)
        {
            // Underfunded: one option per correction, then "leave it as is" with its consequence; the first correction is pre-selected.
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
            // Overfunded: lower the rate (pre-selected), or keep it, which shows the skip question when a whole contribution can be skipped; "don't skip any" is the skip default.
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

        // The plan's own health heads-up — a plain announcement, no choice.
        if (!string.IsNullOrEmpty(r.ConcerningPlanNotice))
        {
            rows.Add(new AnnouncementRow(ConfirmationRowIds.ConcerningPlan, r.ConcerningPlanNotice));
        }

        // A boundary moved outward and grew a plan; names how many more times the goal now occurs.
        if (!string.IsNullOrEmpty(r.BoundaryExtensionAnnouncement))
        {
            rows.Add(new AnnouncementRow(ConfirmationRowIds.BoundaryExtension, r.BoundaryExtensionAnnouncement));
        }

        // "Cascade forward or not", built first so it can nest under the chain question below.
        // The cross-boundary combine-or-keep-separate questions nest under "apply going forward", since they only matter if the change carries forward; each defaults to keeping them separate.
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

            // The two consolidation questions also nest under "apply going forward", for any plans combined downstream.
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

        // "Stay linked or break", for either kind of chain; each option carries its own consequence.
        // The cascade question nests under "keep it linked", since breaking the chain leaves nothing to cascade onto.
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

        // A rate change to a lone plan that's already saving: split at today (the default, keeping what's set aside) or recalculate the whole plan.
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

        // The trivial-fields cascade (Priority, Mandatory, Description, Notes, AutoRenew), defaulting to just this segment, unlike the Amount and shape cascade.
        if (r.TrivialFieldsCanCascade)
        {
            rows.Add(new ChoiceRow(ConfirmationRowIds.TrivialFieldsCascade,
                "This also changes details like priority, mandatory, description, or notes. Update later segments too?",
                [
                    new ChoiceOption("Only this segment", "", r.TrivialFieldsCascadeDescription),
                    new ChoiceOption("Apply it going forward too", "", r.TrivialFieldsCascadeDescription),
                ],
                DefaultIndex: 0,
                Layout: OptionLayout.Stacked));
        }

        // The paycheck cascade: the popup pre-selects "update them", while no popup (DefaultOutcome) leaves the bills alone, so nothing re-paces money without asking.
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
