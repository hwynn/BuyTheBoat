using MyMoneyForecast.Domain;
using MyMoneyForecast.Persistence;

namespace MyMoneyForecast.App;

// Orchestrates everything between a Save click on the Earmark form and a
// savings plan (EarMarkPattern) actually landing in storage — the
// "stay linked or break" and "cascade forward or not" questions for a
// chain of same-finance_id plans. The sibling of FinancePatternSaveConfirmation
// (which does the same for the FinancialPattern itself), kept separate so
// neither wrapper carries the other's logic. Both build their own
// confirmation-row list and hand it to the same dumb popup (ConfirmImplicitChanges);
// neither knows the other. The one thing they share is the row projection
// (ConfirmationRowBuilder.BuildRows) and the repositories helper
// (FinancePatternRepositories.FindOrphanedManualEarmarkDates).
public sealed class EarmarkPatternSaveConfirmation
{
    private readonly EarMarkPattern _proposedPlan;

    // The Start this plan is actually saved under today (or _proposedPlan's own Start for a
    // brand-new plan). Distinct from the proposed Start, since Start is one of the fields this
    // mechanism can change — needed to find the saved row, whose key is (FinanceId, StartDate).
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

    // The confirmation popup's callback — this class builds the request and applies the
    // answer but never constructs a Window, so it stays WPF-free (MainWindow shows it). Null
    // (most tests) falls back to DefaultOutcome's safe answers, so Run() still completes
    // without a live UI.
    public Func<ImplicitChangeConfirmationRequest, ConfirmationOutcome>? ConfirmImplicitChanges { get; set; }

    // Whether this plan's date range touches a neighboring segment (a Start
    // change with a predecessor, or an Until change with a successor). Set by
    // Run, read by PerformEarmarkSave/BuildEarmarkConfirmationRequest.
    public bool PlanTouchesChainBoundary { get; private set; }

    // Whether an Amount/shape change on this plan could carry forward — to a
    // same-finance_id successor, or across a FinancialPattern-level break-off to
    // the far side's own current plan.
    public bool PlanChangeCanCascade { get; private set; }

    // The chain-boundary answer — true (keep the chain contiguous by adjusting
    // the neighbor) unless the user explicitly chose to let it break. Defaults
    // true so a save with no popup keeps the safe, non-destructive shape.
    public bool UserChoseStayLinked { get; private set; } = true;

    // The cascade answer — true (carry an Amount/shape change forward) unless
    // the user explicitly chose "only this segment." Defaults true, matching
    // the popup's own pre-selected default.
    public bool UserChoseCascadeForward { get; private set; } = true;

    /// <summary>[STEP] The single entry point — resolves "stay linked or break" for a Start/Until change and "cascade forward or not" for an Amount/shape change against the rest of the same-finance_id chain, then saves. Mirrors FinancePatternSaveConfirmation.Run's own overall shape (work out what's needed, confirm, then act) for a savings plan instead of the goal itself.</summary>
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
            // A brand-new plan — nothing to compare against, so no chain
            // question to ask, the same way DetermineConditions treats a
            // brand-new FinancialPattern.
            _repositories.EarMarkPatterns.Save(proposedPlan);
            return true;
        }

        // Two shapes are allowed for more than one EarMarkPattern under one finance_id: a
        // genuine sequential chain (RestructureFactory), or concurrent, overlapping earmark
        // patterns — a different, still only partially built case that must NOT get
        // chain-boundary/cascade treatment. Filtering to !SpansOverlap(plan, saved) here —
        // before hasPredecessor/hasSuccessor are computed — keeps a concurrent plan from being
        // mistaken for a sequential neighbor and absorbed/cascaded-onto by PerformEarmarkSave
        // below, which trusts this same filtered list.
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

        // The "fourth relationship" — only relevant once this plan is the LAST in its OWN
        // finance_id's chain (!hasSuccessor): does the GOAL ITSELF have a break-off successor
        // under a different finance_id, and does THAT segment have a real "current" plan to
        // cascade onto? Composes two lookups (BreakOffFactory.FindSuccessor, then
        // RestructureFactory.FindCurrentPlan on the far side). Never computed when hasSuccessor
        // is true — a same-finance_id successor takes priority (forward-only cascades through
        // this plan's OWN chain first; the cross-boundary reach only matters once that chain
        // has nowhere further to go).
        FinancialPattern? crossBoundaryGoal = null;
        EarMarkPattern? crossBoundaryTarget = null;
        if (!hasSuccessor)
        {
            crossBoundaryGoal = BreakOffFactory.FindSuccessor(goal, forecast.Book.AllFinancialPatterns());
            if (crossBoundaryGoal is { } successorGoal)
            {
                // FindCurrentPlan itself returns null for a genuinely
                // concurrent set on the far side — no different than any other
                // "which plan is current" lookup, no special leniency for the
                // cross-boundary case.
                crossBoundaryTarget = RestructureFactory.FindCurrentPlan(forecast.Book.EarMarkPatternsFor(successorGoal.FinanceId));
            }
        }

        PlanTouchesChainBoundary = (startChanged && hasPredecessor) || (untilChanged && hasSuccessor);
        PlanChangeCanCascade = amountOrShapeChanged && (hasSuccessor || crossBoundaryTarget is not null);

        // A lone savings plan (no chain neighbour, no cross-boundary successor) whose
        // amount/rate changed and that has already been accumulating (active span began before
        // today): editing in place would re-rate its whole history, quietly changing what's set
        // aside now. Offer to split it at today instead (the same-finance_id break-off), keeping
        // the jar's balance and applying the new rate forward. Only when an aligned successor exists.
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

    /// <summary>[WRITES FILE] Carries out whatever Run decided — resolves the chain boundary (stay linked, via RestructureFactory.ExtendStart/ExtendUntil, or left broken) and the cascade (same-finance_id via RestructureFactory.CascadeForward, cross-boundary via the same function pointed at the far side's own goal, or just this plan), deletes any ManualEarmark a shrinking span just orphaned, then saves.</summary>
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
        // Keyed by each surviving neighbor's own ORIGINAL Start — stable
        // even for ExtendUntil's own neighbor, whose Start itself moves, so
        // the cascade step below can still find and update it by where it
        // used to be.
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
                    // ExtendUntil's own neighbor gets a NEW Start — a real
                    // key change, found by whichever original still has the
                    // adjusted one's own (unmoved) Until.
                    var original = successors.First(plan => plan.DatePattern.Until == adjusted.DatePattern.Until);
                    toDelete.Add(original.DatePattern.DtStart);
                    toSave[original.DatePattern.DtStart] = adjusted;
                }
            }
        }

        // Gated on UserChoseStayLinked too (both branches): breaking the chain
        // leaves no forward chain to carry the change onto (break
        // ⇒ no forward chain), matching the popup hiding this question under
        // "let the chain break." UserChoseStayLinked defaults true, so a plan
        // with no chain-boundary question still cascades as before.
        if (PlanChangeCanCascade && UserChoseCascadeForward && UserChoseStayLinked && successors.Count > 0)
        {
            // Cascades onto whatever successors are still standing after
            // boundary resolution above — including one just date-adjusted
            // there, whose own dates CascadeForward leaves untouched, only
            // its Amount/shape change.
            var stillStanding = successors
                .Where(plan => !toDelete.Contains(plan.DatePattern.DtStart) || toSave.ContainsKey(plan.DatePattern.DtStart))
                .Select(plan => toSave.TryGetValue(plan.DatePattern.DtStart, out var adjusted) ? adjusted : plan)
                .ToList();

            foreach (var cascaded in RestructureFactory.CascadeForward(current.DatePattern, current.Amount, stillStanding, goal))
            {
                // CascadeForward's own output always keeps its input's
                // Start, so this is guaranteed to be a real key already in
                // toSave or among the originals — never a fresh one.
                toSave[cascaded.DatePattern.DtStart] = cascaded;
            }
        }
        else if (PlanChangeCanCascade && UserChoseCascadeForward && UserChoseStayLinked && crossBoundaryTarget is not null && crossBoundaryGoal is not null)
        {
            // The "fourth relationship" — the far side belongs to a DIFFERENT finance_id than
            // everything else here, so it can't go through toSave/toDelete (both keyed for THIS
            // finance_id's rows, where a key collision against an unrelated goal's Start is a
            // real if unlikely risk) — saved directly instead. CascadeForward keeps its input's
            // Start, so this is always an in-place update, never a key change (hence no delete).
            var cascaded = RestructureFactory.CascadeForward(current.DatePattern, current.Amount, [crossBoundaryTarget], crossBoundaryGoal).Single();
            _repositories.EarMarkPatterns.Save(cascaded);
        }

        // A "let it break" choice — or a standalone plan with no neighbor, so
        // PlanTouchesChainBoundary never fired — can shrink current's span away from where it
        // used to reach. Checked generally here, not gated on PlanTouchesChainBoundary, so a
        // standalone plan's Start/Until edit gets the same protection a chained one does. This
        // is the only way an EarMarkPattern-level edit can orphan a ManualEarmark — absorption
        // never does (the absorbing segment covers the union of both old spans). Computed
        // against finalOtherPlans (post-boundary-resolution, not the stale originals), so a
        // neighbor that stretched to stay contiguous still counts as covering what it now
        // reaches. Must run before any write: ManualEarmarkRepository.GetAll() re-validates
        // every row against what's saved, so reading here — before this save touches anything —
        // keeps it from throwing on the very rows it's identifying (same reasoning as
        // DetermineBackTruncationsIfApplicable's header note for the FinancialPattern-level check).
        var finalOtherPlans = otherPlans
            .Where(plan => !toDelete.Contains(plan.DatePattern.DtStart) || toSave.ContainsKey(plan.DatePattern.DtStart))
            .Select(plan => toSave.TryGetValue(plan.DatePattern.DtStart, out var adjusted) ? adjusted : plan)
            .ToList();
        var finalCoverage = new List<EarMarkPattern> { current };
        finalCoverage.AddRange(finalOtherPlans);

        var orphanedManualEarmarkDates = _repositories.FindOrphanedManualEarmarkDates(finalCoverage, goal.FinanceId);

        // The saved row's own key only actually changed if Start moved —
        // checked here, after boundary resolution, not assumed up front:
        // saving current under its final key and then deleting the OLD key
        // (only when they differ) is what makes this an update rather than
        // an accidental drop of a row that never actually moved.
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

    /// <summary>[WRITES FILE] Splits a lone savings plan at today instead of re-rating its whole history (RestructureFactory.Restructure) — the old rate stays on record through yesterday, the new rate takes over from today, and the jar's current balance carries across untouched (one jar, same finance_id). Chosen by the user for a plan that's already been accumulating; recalculating the whole plan goes through PerformEarmarkSave instead.</summary>
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

    /// <summary>[READS FILE] Builds what ConfirmImplicitChanges needs for the EarMarkPattern-editing case — the "stay linked or break" and "cascade forward or not" questions, now with the concrete-consequence wording the settled design calls for (StayLinkedWarning/LetItBreakWarning/CascadeDescription). Only ever called when at least one of PlanTouchesChainBoundary/PlanChangeCanCascade/offerRerateBreakOff is true (Run's own gate), so Description always names at least one. [READS FILE] because the "let it break" preview dry-runs FindOrphanedManualEarmarkDates, which reads ManualEarmarks — safe here, since nothing has been saved yet this Run().</summary>
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

        // The FinancialPattern-chain, consolidation, and
        // paycheck-association questions never apply to an EarMarkPattern-editing
        // request, so those inputs stay at their false / "" defaults.
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

    /// <summary>[CALC] Previews what "stay linked" would actually do to current's own neighbors, for the confirmation row's own warning slot — "" for a plain, contiguous nudge (never destructive, matching the settled "the default option is never the dangerous one" reasoning, so nothing to warn about), or a real sentence naming which segment(s) would be absorbed (deleted outright, their own values overwritten) once the edit reaches that far. Dry-runs the exact same RestructureFactory calls PerformEarmarkSave itself will make if this branch is actually chosen; the result here is discarded after formatting, not stored, since only one of "stay linked"/"let it break" ever actually runs and re-deriving it is cheap (pure functions over short lists).</summary>
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

    /// <summary>[READS FILE] Previews what "let the chain break" would actually leave behind, for the confirmation row's own warning slot — always a real sentence when PlanTouchesChainBoundary is true, since breaking the chain always leaves SOME gap or overlap. Names whether a gap or overlap forms with the predecessor/successor (RestructureFactory.SpansOverlap decides which) and how many manual earmarks a gap would strand, via a dry run of FindOrphanedManualEarmarkDates against the hypothetical "break" outcome — current's own new span plus every other plan left exactly as it stands today, since "let it break" never touches a neighbor.</summary>
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
            // Shouldn't happen when PlanTouchesChainBoundary is true (some
            // gap/overlap always results from breaking) — never claim a
            // consequence isn't real just to force non-empty text.
            return "";
        }

        var sentence = string.Join("; ", consequences) + ".";
        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    /// <summary>[CALC] Names the date range a cascade would actually reach, for the confirmation row's own always-shown description — the settled rule is that whatever confirms an Amount/shape cascade "must show, plainly, the date range the direct edit itself covers and how far the cascade reaches into the future — which segments, through what date." Unlike StayLinkedWarning/LetItBreakWarning above, this isn't a warning tied to one "dangerous" option — neither Cascade choice is destructive, so it's shown under the row regardless of which one is currently selected. successors and crossBoundaryTarget are mutually exclusive by construction (Run only ever computes the cross-boundary target when this same-finance_id chain has no successor of its own), so exactly one branch below ever has anything to describe.</summary>
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

    // The interpreters that turn the popup's raw per-row selection back into a
    // decision. Deliberately duplicated from FinancePatternSaveConfirmation
    // rather than shared: the front-end contract puts answer-semantics on the
    // wrapper, and each wrapper only reads the rows it itself raised (this one
    // raises just ChainBoundary and Cascade). The shared piece is the row-id
    // vocabulary they both key off (ConfirmationRowIds), not the reading of it.
    // A row absent from the outcome reads as index -1 → the settled safe default.
    private static int Chosen(ConfirmationOutcome outcome, string rowId) =>
        outcome.ChosenOptionIndex.TryGetValue(rowId, out var index) ? index : -1;

    // chain-boundary: [0] keep linked (default), [1] let it break.
    private static bool ChoseStayLinked(ConfirmationOutcome outcome) => Chosen(outcome, ConfirmationRowIds.ChainBoundary) != 1;

    // cascade: [0] apply forward (default), [1] only this segment.
    private static bool ChoseCascadeForward(ConfirmationOutcome outcome) => Chosen(outcome, ConfirmationRowIds.Cascade) != 1;

    // earmark-rerate: [0] break off / split at today (jar preserved), [1] recalculate the whole plan.
    // Unlike the sibling readers, absent → false (recalculate): the split is a user-facing choice only
    // the real popup surfaces, so a save with no popup keeps the historical plain in-place re-rate rather
    // than silently restructuring. The popup always reports index 0 for its pre-selected default, so a
    // real user who just clicks Save still gets the safe break-off.
    private static bool ChoseBreakOff(ConfirmationOutcome outcome) => Chosen(outcome, ConfirmationRowIds.EarmarkRerate) == 0;

    /// <summary>[CALC] The default outcome when no ConfirmImplicitChanges delegate is wired up (most tests, and any host that hasn't connected a real popup) — a bare Proceed with no selections, so every question reads back as its own safe default (stay linked, cascade a rate/schedule change forward). Always proceeds — there's no one here to cancel on.</summary>
    private static ConfirmationOutcome DefaultOutcome() => new() { Proceed = true };
}
