using BuyTheBoat.Domain;
using BuyTheBoat.Persistence;

namespace BuyTheBoat.App;

/// <summary>
/// Everything between clicking Save on the Earmark form and the savings plan landing in storage: the "stay linked or break" and "cascade forward or not" questions for a chain of plans under one finance id.
/// The plan version of FinancePatternSaveConfirmation, kept separate so neither carries the other's logic; they share only ConfirmationRowBuilder.BuildRows and FinancePatternRepositories.FindOrphanedManualEarmarkDates.
/// </summary>
public sealed class EarmarkPatternSaveConfirmation
{
    private readonly EarMarkPattern _proposedPlan;

    /// <summary>The Start the plan is saved under (or the proposed Start for a new plan), needed to find its saved row, whose key is (FinanceId, StartDate), since the edit can change Start.</summary>
    private readonly DateOnly _earmarkSavedStart;

    private readonly FinancialPattern _goal;
    private readonly Func<ForecastResult> _requestForecast;
    private readonly FinancePatternRepositories _repositories;

    /// <summary>[CALC] Builds the orchestrator for an EarmarkFormPanel save click — the "stay linked or break" and "cascade forward or not" questions for a same-finance_id EarMarkPattern chain. Call Run to actually do the work.</summary>
    /// <param name="proposedPlan">The form's current field values for the savings plan — what would be saved if nothing here needs to ask anything first.</param>
    /// <param name="savedStart">The Start this plan is actually saved under today, or the same as proposedPlan's own Start for a brand-new plan.</param>
    /// <param name="goal">The FinancialPattern this savings plan funds.</param>
    /// <param name="requestForecast">Live-forecast accessor — reaches the rest of the same-finance_id chain.</param>
    /// <param name="repositories">The three repositories needed for whatever this class ends up writing.</param>
    public EarmarkPatternSaveConfirmation(
        EarMarkPattern proposedPlan,
        DateOnly savedStart,
        FinancialPattern goal,
        Func<ForecastResult> requestForecast,
        FinancePatternRepositories repositories)
    {
        _proposedPlan = proposedPlan;
        _earmarkSavedStart = savedStart;
        _goal = goal;
        _requestForecast = requestForecast;
        _repositories = repositories;
    }

    /// <summary>
    /// Shows the confirmation; this class builds the request and applies the answer but never creates a window, so MainWindow shows it.
    /// Null (most tests) uses DefaultOutcome's safe answers, so Run() still completes.
    /// </summary>
    public Func<ImplicitChangeConfirmationRequest, ConfirmationOutcome>? ConfirmImplicitChanges { get; set; }

    /// <summary>Does the plan's date range touch a neighboring segment (a Start change with a predecessor, or an Until change with a successor)? Set by Run.</summary>
    public bool PlanTouchesChainBoundary { get; private set; }

    /// <summary>Could an Amount or shape change carry forward, to a successor plan or across a break-off to the next goal segment's current plan?</summary>
    public bool PlanChangeCanCascade { get; private set; }

    /// <summary>The user's answer to keeping the chain linked by adjusting the neighbor; defaults to true, the non-destructive choice.</summary>
    public bool UserChoseStayLinked { get; private set; } = true;

    /// <summary>The user's answer to carrying an Amount or shape change forward; defaults to true, the popup's pre-selected choice.</summary>
    public bool UserChoseCascadeForward { get; private set; } = true;

    /// <summary>
    /// [STEP] The single entry point: resolves "stay linked or break" for a Start or Until change and "cascade forward or not" for an Amount or shape change, then saves.
    /// Works like FinancePatternSaveConfirmation.Run (work out what's needed, confirm, then act), for a savings plan instead of the goal.
    /// </summary>
    /// <returns>False if the user cancelled out of the confirmation — nothing was saved. True otherwise, including when nothing needed asking at all.</returns>
    public bool Run()
    {
        var proposedPlan = _proposedPlan;
        var goal = _goal;
        var forecast = _requestForecast();
        var allPlansForGoal = forecast.Book.EarMarkPatternsFor(goal.FinanceId);
        var saved = allPlansForGoal.FirstOrDefault(plan => plan.DatePattern.DtStart == _earmarkSavedStart);

        if (saved is null)
        {
            // A new plan: nothing to compare against, so no chain question.
            _repositories.EarMarkPatterns.Save(proposedPlan);
            return true;
        }

        // Leaves out plans that overlap this one (RestructureFactory.SpansOverlap), which run at the same time rather than in a chain, so PerformEarmarkSave never absorbs or cascades onto one.
        var otherPlans = allPlansForGoal
            .Where(plan => plan.DatePattern.DtStart != saved.DatePattern.DtStart && !RestructureFactory.SpansOverlap(plan, saved))
            .ToList();
        var hasPredecessor = otherPlans.Any(plan => plan.DatePattern.DtStart < saved.DatePattern.DtStart);
        var hasSuccessor = otherPlans.Any(plan => plan.DatePattern.DtStart > saved.DatePattern.DtStart);

        var startChanged = saved.DatePattern.DtStart != proposedPlan.DatePattern.DtStart;
        var untilChanged = saved.DatePattern.Until != proposedPlan.DatePattern.Until;
        var amountOrShapeChanged = saved.Amount != proposedPlan.Amount
            || saved.DatePattern.Frequency != proposedPlan.DatePattern.Frequency
            || saved.DatePattern.Interval != proposedPlan.DatePattern.Interval
            || !saved.DatePattern.ByDay.SequenceEqual(proposedPlan.DatePattern.ByDay)
            || !saved.DatePattern.ByMonthDay.SequenceEqual(proposedPlan.DatePattern.ByMonthDay);

        // Cross-boundary: when this is the last plan in its own chain, does the goal have a break-off successor (BreakOffFactory.FindSuccessor) with a current plan (RestructureFactory.FindCurrentPlan) to cascade onto?
        // Skipped when this plan has its own successor, which the cascade reaches first.
        FinancialPattern? crossBoundaryGoal = null;
        EarMarkPattern? crossBoundaryTarget = null;
        if (!hasSuccessor)
        {
            crossBoundaryGoal = BreakOffFactory.FindSuccessor(goal, forecast.Book.AllFinancialPatterns());
            if (crossBoundaryGoal is { } successorGoal)
            {
                // Null when the far side's plans run at the same time.
                crossBoundaryTarget = RestructureFactory.FindCurrentPlan(forecast.Book.EarMarkPatternsFor(successorGoal.FinanceId));
            }
        }

        PlanTouchesChainBoundary = (startChanged && hasPredecessor) || (untilChanged && hasSuccessor);
        PlanChangeCanCascade = amountOrShapeChanged && (hasSuccessor || crossBoundaryTarget is not null);

        // A lone plan whose rate changed and that started before today: editing in place would re-rate its whole history, changing what's set aside now.
        // So this offers to split it at today instead, keeping the jar's balance and applying the new rate forward, when a successor schedule can be lined up.
        var offerRerateBreakOff = false;
        RecurrenceRuleOptions? rerateSuccessorSchedule = null;
        if (amountOrShapeChanged && !PlanTouchesChainBoundary && !PlanChangeCanCascade
            && saved.DatePattern.ActiveStart < forecast.AsOfDate)
        {
            rerateSuccessorSchedule = AllocationPlanProposer.AlignedSchedule(
                proposedPlan.DatePattern, forecast.AsOfDate, proposedPlan.DatePattern.Until);
            offerRerateBreakOff = rerateSuccessorSchedule is not null;
        }

        var userChoseBreakOff = false;
        if (PlanTouchesChainBoundary || PlanChangeCanCascade || offerRerateBreakOff)
        {
            var request = BuildEarmarkConfirmationRequest(proposedPlan, saved, otherPlans, goal, crossBoundaryTarget, crossBoundaryGoal, offerRerateBreakOff);
            var outcome = ConfirmImplicitChanges?.Invoke(request) ?? DefaultOutcome();
            if (!outcome.Proceed)
            {
                return false;
            }

            UserChoseStayLinked = ChoseStayLinked(outcome);
            UserChoseCascadeForward = ChoseCascadeForward(outcome);
            userChoseBreakOff = ChoseBreakOff(outcome);
        }

        if (offerRerateBreakOff && userChoseBreakOff)
        {
            PerformRerateBreakOff(saved, goal, forecast.AsOfDate, proposedPlan, rerateSuccessorSchedule!);
        }
        else
        {
            PerformEarmarkSave(proposedPlan, saved, otherPlans, goal, crossBoundaryTarget, crossBoundaryGoal);
        }

        return true;
    }

    /// <summary>
    /// [WRITES FILE] Carries out what Run decided: the chain boundary (stay linked via RestructureFactory.ExtendStart/ExtendUntil, or let it break) and the cascade (RestructureFactory.CascadeForward, within the chain or across to the next goal segment).
    /// Then deletes any manual earmark a shrunken span left uncovered, and saves.
    /// </summary>
    /// <param name="proposedPlan">The form's current field values.</param>
    /// <param name="saved">The plan as it's actually saved today.</param>
    /// <param name="otherPlans">Every other EarMarkPattern sharing the same finance_id.</param>
    /// <param name="goal">The goal this Savings Plan funds.</param>
    /// <param name="crossBoundaryTarget">The "fourth relationship" — the current EarMarkPattern on the far side of a FinancialPattern-level break-off, or null when there's no successor goal, no plan on it yet, or its own plans are a genuinely concurrent set with no single "current" one.</param>
    /// <param name="crossBoundaryGoal">The far side's own goal — required to validate crossBoundaryTarget's cascaded replacement, since it belongs to a different finance_id than goal above.</param>
    private void PerformEarmarkSave(
        EarMarkPattern proposedPlan, EarMarkPattern saved, IReadOnlyList<EarMarkPattern> otherPlans, FinancialPattern goal,
        EarMarkPattern? crossBoundaryTarget, FinancialPattern? crossBoundaryGoal)
    {
        var current = proposedPlan;
        var toDelete = new List<DateOnly>();
        // Keyed by each neighbor's original Start, even when ExtendUntil moves it, so the cascade below can still find it.
        var toSave = new Dictionary<DateOnly, EarMarkPattern>();

        var predecessors = otherPlans.Where(plan => plan.DatePattern.DtStart < saved.DatePattern.DtStart).ToList();
        var successors = otherPlans.Where(plan => plan.DatePattern.DtStart > saved.DatePattern.DtStart).ToList();

        if (PlanTouchesChainBoundary && UserChoseStayLinked)
        {
            if (saved.DatePattern.DtStart != current.DatePattern.DtStart && predecessors.Count > 0)
            {
                var result = RestructureFactory.ExtendStart(current, predecessors, goal, current.DatePattern.DtStart);
                current = result.Current;
                foreach (var absorbed in result.Absorbed)
                {
                    toDelete.Add(absorbed.DatePattern.DtStart);
                }

                if (result.AdjustedNeighbor is { } adjusted)
                {
                    // ExtendStart's own neighbor keeps its own Start — an
                    // in-place update, not a key change.
                    toSave[adjusted.DatePattern.DtStart] = adjusted;
                }
            }

            if (saved.DatePattern.Until != current.DatePattern.Until && successors.Count > 0)
            {
                var result = RestructureFactory.ExtendUntil(current, successors, goal, current.DatePattern.Until);
                current = result.Current;
                foreach (var absorbed in result.Absorbed)
                {
                    toDelete.Add(absorbed.DatePattern.DtStart);
                }

                if (result.AdjustedNeighbor is { } adjusted)
                {
                    // ExtendUntil's neighbor gets a new Start, so it's matched to its original by its unchanged Until.
                    var original = successors.First(plan => plan.DatePattern.Until == adjusted.DatePattern.Until);
                    toDelete.Add(original.DatePattern.DtStart);
                    toSave[original.DatePattern.DtStart] = adjusted;
                }
            }
        }

        // Breaking the chain leaves nothing to cascade onto, so the popup hides this question then; UserChoseStayLinked defaults to true, so a plan with no boundary question still cascades.
        if (PlanChangeCanCascade && UserChoseCascadeForward && UserChoseStayLinked && successors.Count > 0)
        {
            // Cascades onto the successors still standing after the boundary step, including one it just moved; CascadeForward changes only Amount and shape, not dates.
            var stillStanding = successors
                .Where(plan => !toDelete.Contains(plan.DatePattern.DtStart) || toSave.ContainsKey(plan.DatePattern.DtStart))
                .Select(plan => toSave.TryGetValue(plan.DatePattern.DtStart, out var adjusted) ? adjusted : plan)
                .ToList();

            foreach (var cascaded in RestructureFactory.CascadeForward(current.DatePattern, current.Amount, stillStanding, goal))
            {
                // CascadeForward keeps each plan's Start, so the key already exists.
                toSave[cascaded.DatePattern.DtStart] = cascaded;
            }
        }
        else if (PlanChangeCanCascade && UserChoseCascadeForward && UserChoseStayLinked && crossBoundaryTarget is not null && crossBoundaryGoal is not null)
        {
            // Cross-boundary: the far side's plan is under another finance id, so it's saved directly rather than through toSave, which is keyed for this finance id's rows.
            // CascadeForward keeps its Start, so this updates the row in place.
            var cascaded = RestructureFactory.CascadeForward(current.DatePattern, current.Amount, [crossBoundaryTarget], crossBoundaryGoal).Single();
            _repositories.EarMarkPatterns.Save(cascaded);
        }

        // Finds manual earmarks left uncovered when the span shrinks, from "let it break" or a plan with no neighbor; absorbing a segment never leaves any uncovered.
        // Checked against finalOtherPlans, so a neighbor stretched to stay linked counts for what it now covers.
        // Must run before any write, since ManualEarmarkRepository.GetAll() checks every row against what's saved and would throw on the very rows being found.
        var finalOtherPlans = otherPlans
            .Where(plan => !toDelete.Contains(plan.DatePattern.DtStart) || toSave.ContainsKey(plan.DatePattern.DtStart))
            .Select(plan => toSave.TryGetValue(plan.DatePattern.DtStart, out var adjusted) ? adjusted : plan)
            .ToList();
        var finalCoverage = new List<EarMarkPattern> { current };
        finalCoverage.AddRange(finalOtherPlans);

        var orphanedManualEarmarkDates = _repositories.FindOrphanedManualEarmarkDates(finalCoverage, goal.FinanceId);

        // Deletes the old row only when Start actually moved, so an unmoved row isn't dropped.
        if (current.DatePattern.DtStart != saved.DatePattern.DtStart)
        {
            toDelete.Add(saved.DatePattern.DtStart);
        }

        foreach (var date in orphanedManualEarmarkDates)
        {
            _repositories.ManualEarmarks.Delete(goal.FinanceId, date);
        }

        _repositories.EarMarkPatterns.Save(current);
        foreach (var plan in toSave.Values)
        {
            _repositories.EarMarkPatterns.Save(plan);
        }

        foreach (var start in toDelete.Distinct())
        {
            _repositories.EarMarkPatterns.Delete(goal.FinanceId, start);
        }
    }

    /// <summary>
    /// [WRITES FILE] Splits a lone savings plan at today rather than re-rating its history (RestructureFactory.Restructure): the old rate runs through yesterday, the new rate from today, and the jar's balance carries across.
    /// Recalculating the whole plan goes through PerformEarmarkSave instead.
    /// </summary>
    /// <param name="saved">The plan as it's saved today — becomes the truncated predecessor (its own Start unchanged).</param>
    /// <param name="goal">The goal this savings plan funds.</param>
    /// <param name="cutDate">Today (the forecast's as-of date) — where the old rate ends and the new one begins.</param>
    /// <param name="proposed">The form's current values — supplies the successor's new amount.</param>
    /// <param name="successorSchedule">The proposed recurrence re-anchored to the cut date (AllocationPlanProposer.AlignedSchedule).</param>
    private void PerformRerateBreakOff(
        EarMarkPattern saved, FinancialPattern goal, DateOnly cutDate, EarMarkPattern proposed, RecurrenceRuleOptions successorSchedule)
    {
        var result = RestructureFactory.Restructure(new RestructureRequest
        {
            Predecessor = saved,
            Goal = goal,
            CutDate = cutDate,
            SuccessorAmount = proposed.Amount,
            SuccessorSchedule = successorSchedule,
        });

        // The predecessor keeps its own Start (only its Until shrank), so this is
        // an in-place update; the successor is a new (FinanceId, Start) row.
        _repositories.EarMarkPatterns.Save(result.Predecessor);
        _repositories.EarMarkPatterns.Save(result.Successor);
    }

    /// <summary>
    /// [READS FILE] Builds what ConfirmImplicitChanges needs: the "stay linked or break", "cascade forward or not", and re-rate questions, each with its consequence spelled out.
    /// Only called when at least one question applies; reads saved data because the "let it break" preview counts uncovered manual earmarks.
    /// </summary>
    /// <param name="current">The plan as the user is currently proposing to save it.</param>
    /// <param name="saved">The plan as it's actually saved today.</param>
    /// <param name="otherPlans">Every other EarMarkPattern sharing the same finance_id (concurrent plans already excluded — see Run's own note).</param>
    /// <param name="goal">The goal this Savings Plan funds.</param>
    /// <param name="crossBoundaryTarget">The "fourth relationship" target, or null — see PerformEarmarkSave's own param doc for the full explanation.</param>
    /// <param name="crossBoundaryGoal">The far side's own goal, paired with crossBoundaryTarget.</param>
    private ImplicitChangeConfirmationRequest BuildEarmarkConfirmationRequest(
        EarMarkPattern current, EarMarkPattern saved, IReadOnlyList<EarMarkPattern> otherPlans, FinancialPattern goal,
        EarMarkPattern? crossBoundaryTarget, FinancialPattern? crossBoundaryGoal, bool offerRerateBreakOff)
    {
        var predecessors = otherPlans.Where(plan => plan.DatePattern.DtStart < saved.DatePattern.DtStart).ToList();
        var successors = otherPlans.Where(plan => plan.DatePattern.DtStart > saved.DatePattern.DtStart).ToList();

        // The pattern-chain, consolidation, and paycheck questions never apply to a plan edit, so those inputs stay at their defaults.
        var inputs = new RowInputs
        {
            PlanTouchesChainBoundary = PlanTouchesChainBoundary,
            PlanChangeCanCascade = PlanChangeCanCascade,
            StayLinkedWarning = PlanTouchesChainBoundary ? DescribeStayLinkedConsequence(current, saved, predecessors, successors, goal) : "",
            LetItBreakWarning = PlanTouchesChainBoundary ? DescribeLetItBreakConsequence(current, saved, predecessors, successors, otherPlans, goal) : "",
            CascadeDescription = PlanChangeCanCascade ? DescribeCascadeConsequence(current, successors, crossBoundaryTarget, crossBoundaryGoal) : "",
            OfferRerateBreakOff = offerRerateBreakOff,
        };

        return new ImplicitChangeConfirmationRequest
        {
            // offerRerateBreakOff is only ever set for a lone plan (both chain
            // flags false), so its own wording takes precedence over the chain one.
            Description = offerRerateBreakOff
                ? "This savings plan has already been setting money aside."
                : (PlanTouchesChainBoundary, PlanChangeCanCascade) switch
                {
                    (true, true) => "This plan is part of a chain. Its date range touches a neighboring segment, and its amount or schedule change could carry forward too.",
                    (true, false) => "This plan is part of a chain. Its date range touches a neighboring segment.",
                    _ => "This plan is part of a chain. Later segments could pick up this same amount or schedule change.",
                },
            Rows = ConfirmationRowBuilder.BuildRows(inputs),
        };
    }

    /// <summary>
    /// [CALC] Previews what "stay linked" would do to the neighbors, for the row's warning: "" for a plain nudge, or a sentence naming each segment that would be absorbed and deleted.
    /// Dry-runs the same RestructureFactory calls PerformEarmarkSave makes.
    /// </summary>
    private static string DescribeStayLinkedConsequence(
        EarMarkPattern current, EarMarkPattern saved, IReadOnlyList<EarMarkPattern> predecessors, IReadOnlyList<EarMarkPattern> successors, FinancialPattern goal)
    {
        var absorbed = new List<EarMarkPattern>();

        if (saved.DatePattern.DtStart != current.DatePattern.DtStart && predecessors.Count > 0)
        {
            absorbed.AddRange(RestructureFactory.ExtendStart(current, predecessors, goal, current.DatePattern.DtStart).Absorbed);
        }

        if (saved.DatePattern.Until != current.DatePattern.Until && successors.Count > 0)
        {
            absorbed.AddRange(RestructureFactory.ExtendUntil(current, successors, goal, current.DatePattern.Until).Absorbed);
        }

        if (absorbed.Count == 0)
        {
            return ""; // a plain nudge — never destructive, nothing to warn about
        }

        var ordered = absorbed.OrderBy(plan => plan.DatePattern.DtStart).ToList();
        var ranges = string.Join("; ", ordered.Select(plan => $"{plan.DatePattern.DtStart:MMM d, yyyy} – {plan.DatePattern.Until:MMM d, yyyy}"));
        return ordered.Count == 1
            ? $"This will delete the segment covering {ranges} entirely — its own amount and schedule won't be kept."
            : $"This will delete {ordered.Count} segments entirely ({ranges}) — their own amounts and schedules won't be kept.";
    }

    /// <summary>
    /// [READS FILE] Previews what "let the chain break" leaves behind, for the row's warning: whether a gap or overlap forms with each neighbor (RestructureFactory.SpansOverlap).
    /// Also counts the manual earmarks a gap would leave uncovered, checked against the new span with every other plan unchanged.
    /// </summary>
    private string DescribeLetItBreakConsequence(
        EarMarkPattern current, EarMarkPattern saved, IReadOnlyList<EarMarkPattern> predecessors, IReadOnlyList<EarMarkPattern> successors,
        IReadOnlyList<EarMarkPattern> otherPlans, FinancialPattern goal)
    {
        var consequences = new List<string>();

        if (saved.DatePattern.DtStart != current.DatePattern.DtStart && predecessors.Count > 0)
        {
            var predecessor = predecessors.OrderByDescending(plan => plan.DatePattern.DtStart).First();
            consequences.Add(RestructureFactory.SpansOverlap(current, predecessor)
                ? $"it will overlap with the segment before it ({predecessor.DatePattern.DtStart:MMM d, yyyy} – {predecessor.DatePattern.Until:MMM d, yyyy})"
                : $"a gap will open before it, from {predecessor.DatePattern.Until.AddDays(1):MMM d, yyyy} to {current.DatePattern.DtStart.AddDays(-1):MMM d, yyyy}");
        }

        if (saved.DatePattern.Until != current.DatePattern.Until && successors.Count > 0)
        {
            var successor = successors.OrderBy(plan => plan.DatePattern.DtStart).First();
            consequences.Add(RestructureFactory.SpansOverlap(current, successor)
                ? $"it will overlap with the segment after it ({successor.DatePattern.DtStart:MMM d, yyyy} – {successor.DatePattern.Until:MMM d, yyyy})"
                : $"a gap will open after it, from {current.DatePattern.Until.AddDays(1):MMM d, yyyy} to {successor.DatePattern.DtStart.AddDays(-1):MMM d, yyyy}");
        }

        var hypotheticalCoverage = new List<EarMarkPattern> { current };
        hypotheticalCoverage.AddRange(otherPlans);
        var orphaned = _repositories.FindOrphanedManualEarmarkDates(hypotheticalCoverage, goal.FinanceId);
        if (orphaned.Count > 0)
        {
            consequences.Add(orphaned.Count == 1
                ? $"1 manual earmark dated {orphaned[0]:MMM d, yyyy} will be deleted"
                : $"{orphaned.Count} manual earmarks will be deleted");
        }

        if (consequences.Count == 0)
        {
            // Shouldn't happen when PlanTouchesChainBoundary is true; returns "" rather than invent a consequence.
            return "";
        }

        var sentence = string.Join("; ", consequences) + ".";
        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    /// <summary>
    /// [CALC] Names the dates this edit covers and how far a cascade would reach (which segments, through what date), shown under the row whichever option is picked.
    /// Only one of successors and crossBoundaryTarget is ever set.
    /// </summary>
    /// <param name="current">The plan as the user is currently proposing to save it.</param>
    /// <param name="successors">Later plans sharing the same finance_id — empty whenever the cascade is cross-boundary instead.</param>
    /// <param name="crossBoundaryTarget">The "fourth relationship" target, or null when there isn't one.</param>
    /// <param name="crossBoundaryGoal">The far side's own goal, paired with crossBoundaryTarget — named in the sentence so the user knows this reaches beyond the bill they're currently looking at.</param>
    private static string DescribeCascadeConsequence(
        EarMarkPattern current, IReadOnlyList<EarMarkPattern> successors, EarMarkPattern? crossBoundaryTarget, FinancialPattern? crossBoundaryGoal)
    {
        var ownRange = $"This edit covers {current.DatePattern.DtStart:MMM d, yyyy} – {current.DatePattern.Until:MMM d, yyyy}.";

        if (successors.Count > 0)
        {
            var furthest = successors.Max(plan => plan.DatePattern.Until);
            return successors.Count == 1
                ? $"{ownRange} Cascading forward would also update the segment running through {furthest:MMM d, yyyy}."
                : $"{ownRange} Cascading forward would also update {successors.Count} later segments, through {furthest:MMM d, yyyy}.";
        }

        if (crossBoundaryTarget is not null && crossBoundaryGoal is not null)
        {
            var label = string.IsNullOrWhiteSpace(crossBoundaryGoal.Description) ? crossBoundaryGoal.Source : crossBoundaryGoal.Description;
            return $"{ownRange} This bill has since moved to a newer segment (\"{label}\") — cascading forward would also update its own savings plan, running through {crossBoundaryTarget.DatePattern.Until:MMM d, yyyy}.";
        }

        return "";
    }

    /// <summary>
    /// [CALC] Returns which option the user picked for one row, or -1 when unanswered, which callers treat as the safe default.
    /// Each confirmation class reads only the rows it raised, so this is repeated rather than shared; only the row ids (ConfirmationRowIds) are shared.
    /// </summary>
    private static int Chosen(ConfirmationOutcome outcome, string rowId) =>
        outcome.ChosenOptionIndex.TryGetValue(rowId, out var index) ? index : -1;

    // chain-boundary: [0] keep linked (default), [1] let it break.
    private static bool ChoseStayLinked(ConfirmationOutcome outcome) => Chosen(outcome, ConfirmationRowIds.ChainBoundary) != 1;

    // cascade: [0] apply forward (default), [1] only this segment.
    private static bool ChoseCascadeForward(ConfirmationOutcome outcome) => Chosen(outcome, ConfirmationRowIds.Cascade) != 1;

    /// <summary>
    /// [CALC] Whether to split the plan at today (option 0) rather than recalculate the whole plan (option 1).
    /// Unanswered reads as recalculate, so a save with no popup re-rates in place instead of splitting without asking; the popup pre-selects the split.
    /// </summary>
    private static bool ChoseBreakOff(ConfirmationOutcome outcome) => Chosen(outcome, ConfirmationRowIds.EarmarkRerate) == 0;

    /// <summary>[CALC] The outcome when no ConfirmImplicitChanges is wired (most tests): proceed with no answers, so every question reads as its safe default.</summary>
    private static ConfirmationOutcome DefaultOutcome() => new() { Proceed = true };
}
