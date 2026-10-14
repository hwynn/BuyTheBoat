namespace BuyTheBoat.Domain;

/// <summary>
/// Everything "Change starting on a date" needs: the pattern being changed, when the change takes effect, its new shape from then on, and, for an expense with a savings plan, the jar balance to carry across.
/// The caller reads CarriedOverJarBalance off the current forecast at CutDate, since this factory has no forecast of its own.
/// </summary>
public sealed record BreakOffRequest
{
    public required FinancialPattern Predecessor { get; init; }
    public required EarMarkPattern? PredecessorPlan { get; init; }
    public required DateOnly CutDate { get; init; }
    public required int SuccessorFinanceId { get; init; }
    public required decimal SuccessorAmount { get; init; }
    public required RecurrenceRuleOptions SuccessorSchedule { get; init; }
    public required decimal CarriedOverJarBalance { get; init; }
    public required IReadOnlyList<FinancialPattern> AllPatterns { get; init; }

    /// <summary>
    /// With no income to pace against, should the successor's new plan spread its saving evenly (an ordinary bill or goal) rather than set it all aside at once (a transfer's withdrawal; TransferBreakOffFactory sets this false)?
    /// Has no effect when the successor has more than one occurrence.
    /// </summary>
    public bool SpreadEvenlyWithNoIncome { get; init; } = true;

    /// <summary>
    /// The plan shape the user picked when the caller asked (AllocationPlanProposer.Propose's default, ProposeSameSchedule, or ProposeSameAmount), used as is.
    /// Null when no question was asked or the user picked the default; BreakOff then calls Propose itself.
    /// </summary>
    public ProposedAllocationPlan? ChosenSuccessorPlan { get; init; }

    /// <summary>
    /// Caps the one-off starting earmark of the successor's newly proposed plan; uncapped when null.
    /// Only used when ChosenSuccessorPlan is null, since a chosen plan was already sized.
    /// </summary>
    public decimal? SuccessorStartingEarmarkCeiling { get; init; }

    /// <summary>
    /// Caps the per-cycle rate of the successor's newly proposed plan; uncapped when null.
    /// Only used when ChosenSuccessorPlan is null, since a chosen plan was already sized.
    /// </summary>
    public decimal? SuccessorOngoingRateCeiling { get; init; }
}

/// <summary>
/// The predecessor, now ending the day before the cut, plus the new successor pattern, its plan, and its starting earmark.
/// The predecessor stays in the data as an ordinary ended pattern; the two are linked only by sharing a Source (see FindPredecessor).
/// </summary>
public sealed record BreakOffResult
{
    public required FinancialPattern Predecessor { get; init; }
    public required EarMarkPattern? PredecessorPlan { get; init; }
    public required FinancialPattern Successor { get; init; }
    public required EarMarkPattern? SuccessorPlan { get; init; }
    public required ManualEarmark? SuccessorStartingEarmark { get; init; }
}

/// <summary>
/// BreakOffRequest for a predecessor with more than one savings plan (relaxing 3.11.1.a1), used both to consolidate them into one successor plan and to keep them separate (BreakOffKeepingPlansSeparate).
/// PredecessorPlans replaces the single PredecessorPlan.
/// There's still one CarriedOverJarBalance, since a finance id has one jar however many plans feed it.
/// </summary>
public sealed record MultiPlanBreakOffRequest
{
    public required FinancialPattern Predecessor { get; init; }
    public required IReadOnlyList<EarMarkPattern> PredecessorPlans { get; init; }
    public required DateOnly CutDate { get; init; }
    public required int SuccessorFinanceId { get; init; }
    public required decimal SuccessorAmount { get; init; }
    public required RecurrenceRuleOptions SuccessorSchedule { get; init; }
    public required decimal CarriedOverJarBalance { get; init; }
    public required IReadOnlyList<FinancialPattern> AllPatterns { get; init; }
    public bool SpreadEvenlyWithNoIncome { get; init; } = true;

    /// <summary>
    /// Same as BreakOffRequest.ChosenSuccessorPlan.
    /// Only offered when the predecessor's plans form a sequential chain (RestructureFactory.FindCurrentPlan finds one current plan), not a concurrent set.
    /// </summary>
    public ProposedAllocationPlan? ChosenSuccessorPlan { get; init; }

    /// <summary>Same as BreakOffRequest.SuccessorStartingEarmarkCeiling.</summary>
    public decimal? SuccessorStartingEarmarkCeiling { get; init; }

    /// <summary>Same as BreakOffRequest.SuccessorOngoingRateCeiling.</summary>
    public decimal? SuccessorOngoingRateCeiling { get; init; }
}

/// <summary>
/// BreakOffResult for the consolidating multi-plan break-off: every predecessor plan, each ending the day before the cut, plus one new successor plan.
/// Consolidating always produces one successor plan, never one per predecessor plan.
/// </summary>
public sealed record MultiPlanBreakOffResult
{
    public required FinancialPattern Predecessor { get; init; }
    public required IReadOnlyList<EarMarkPattern> PredecessorPlans { get; init; }
    public required FinancialPattern Successor { get; init; }
    public required EarMarkPattern? SuccessorPlan { get; init; }
    public required ManualEarmark? SuccessorStartingEarmark { get; init; }
}

/// <summary>
/// The keep-separate result: the successor gets one plan per predecessor plan, each continuing its own rate, instead of one consolidated plan.
/// There's no SuccessorStartingEarmark; the carried jar balance goes into the first successor plan's StartingAllocation.
/// </summary>
public sealed record MultiPlanKeepSeparateResult
{
    public required FinancialPattern Predecessor { get; init; }
    public required IReadOnlyList<EarMarkPattern> PredecessorPlans { get; init; }
    public required FinancialPattern Successor { get; init; }
    public required IReadOnlyList<EarMarkPattern> SuccessorPlans { get; init; }
}

/// <summary>
/// Everything renewing an "ongoing" pattern needs: BreakOff is for when the user changes something, and Renew is for when nothing changed.
/// There's no amount or schedule-shape input, because Renew copies both from the predecessor.
/// SegmentYears is how far the renewed segment reaches, chosen by the caller; it's separate from the pattern's own cadence, which may itself be several years.
/// </summary>
public sealed record RenewalRequest
{
    public required FinancialPattern Predecessor { get; init; }
    public required EarMarkPattern? PredecessorPlan { get; init; }
    public required DateOnly RenewalDate { get; init; }
    public required int SegmentYears { get; init; }
    public required int SuccessorFinanceId { get; init; }
    public required decimal CarriedOverJarBalance { get; init; }
    public required IReadOnlyList<FinancialPattern> AllPatterns { get; init; }
}

/// <summary>
/// Cuts a bill, paycheck, or goal at CutDate: the predecessor ends the day before, and a successor with a new FinanceId starts exactly on CutDate.
/// Also renews ongoing patterns, finds segments in a chain, and keeps a chain linked when a segment's dates change.
/// </summary>
/// <remarks>
/// PatternTruncation ends the predecessor and its plan together.
/// An expense's successor gets a newly proposed plan from AllocationPlanProposer, never a copy of the predecessor's, since a break-off means the old plan's sizing is now wrong.
/// The proposer runs as of CutDate, not today, so the new plan doesn't start saving before the successor starts.
/// When the caller supplies ChosenSuccessorPlan, that plan is used instead.
/// Either way, the plan's StartingAllocation is set to CarriedOverJarBalance; a chosen plan's own StartingAllocation was only a placeholder for sizing.
/// Income never has a plan, so for income the cut is the whole job.
/// </remarks>
public static class BreakOffFactory
{
    /// <summary>
    /// [CALC] "Change starting on a date": ends a bill or paycheck on a chosen date and hands it off to a new one with its own amount and schedule.
    /// For a bill, the old savings plan is wound down and a new one is proposed, carrying over whatever was already saved.
    /// </summary>
    /// <param name="request">The pattern being changed, the cut date, the new amount/schedule it takes on, and the jar balance to carry across (read by the caller from the current forecast).</param>
    /// <returns>The now-bounded original pattern plus the new one that continues from the cut date, with its savings plan already set up.</returns>
    public static BreakOffResult BreakOff(BreakOffRequest request)
    {
        ValidateCutBoundaries(request.Predecessor, request.CutDate, request.SuccessorFinanceId, request.SuccessorSchedule);

        var truncated = PatternTruncation.EndOn(request.Predecessor, request.PredecessorPlan, request.CutDate.AddDays(-1));

        var successor = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = request.SuccessorFinanceId,
            Source = request.Predecessor.Source,
            Description = request.Predecessor.Description,
            Notes = request.Predecessor.Notes,
            DatePattern = RecurrenceRule.Create(request.SuccessorSchedule),
            Amount = request.SuccessorAmount,
            Priority = request.Predecessor.Priority,
            Mandatory = request.Predecessor.Mandatory,
            // Carried over like the other identity fields, since a break-off changes amount and schedule, not whether the pattern keeps going.
            // Renew depends on this: each renewed segment must stay eligible for the next renewal.
            AutoRenew = request.Predecessor.AutoRenew,
        });

        if (request.Predecessor.Amount >= 0m)
        {
            // Income: the cut is the whole job, since income never has a jar or plan.
            return new BreakOffResult
            {
                Predecessor = truncated.Pattern,
                PredecessorPlan = null,
                Successor = successor,
                SuccessorPlan = null,
                SuccessorStartingEarmark = null,
            };
        }

        var proposal = request.ChosenSuccessorPlan
            ?? AllocationPlanProposer.Propose(successor, request.AllPatterns, request.CutDate, request.SpreadEvenlyWithNoIncome,
                startingEarmarkCeiling: request.SuccessorStartingEarmarkCeiling,
                ongoingRateCeiling: request.SuccessorOngoingRateCeiling);

        var successorPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = proposal.Plan.FinanceId,
                DatePattern = proposal.Plan.DatePattern,
                Amount = proposal.Plan.Amount,
                StartingAllocation = request.CarriedOverJarBalance,
            },
            proposal.Outflow);

        return new BreakOffResult
        {
            Predecessor = truncated.Pattern,
            PredecessorPlan = truncated.Plan,
            Successor = proposal.Outflow,
            SuccessorPlan = successorPlan,
            SuccessorStartingEarmark = proposal.StartingEarmark,
        };
    }

    /// <summary>
    /// [CALC] The same break-off when more than one savings plan shares the predecessor's finance id, consolidating them.
    /// Every plan ends the day before the cut, and the successor gets one newly proposed plan, seeded from the jar's single balance.
    /// To keep the plans separate instead, use BreakOffKeepingPlansSeparate.
    /// </summary>
    /// <param name="request">The pattern being changed, every surviving plan, the cut date, the new amount/schedule, and the one jar balance to carry across.</param>
    /// <returns>The now-bounded original pattern plus every one of its plans (each truncated), plus the new pattern that continues from the cut date, with one consolidated savings plan already set up.</returns>
    public static MultiPlanBreakOffResult BreakOff(MultiPlanBreakOffRequest request)
    {
        ValidateCutBoundaries(request.Predecessor, request.CutDate, request.SuccessorFinanceId, request.SuccessorSchedule);

        var predecessorLastDay = request.CutDate.AddDays(-1);
        var truncatedPredecessor = request.Predecessor.WithUntil(predecessorLastDay);

        // Each predecessor plan, ended the day before the cut.
        // EndOn only returns a null Plan for a null input, so the ! is safe.
        var truncatedPlans = request.PredecessorPlans
            .Select(plan => PatternTruncation.EndOn(request.Predecessor, plan, predecessorLastDay).Plan!)
            .ToList();

        var successor = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = request.SuccessorFinanceId,
            Source = request.Predecessor.Source,
            Description = request.Predecessor.Description,
            Notes = request.Predecessor.Notes,
            DatePattern = RecurrenceRule.Create(request.SuccessorSchedule),
            Amount = request.SuccessorAmount,
            Priority = request.Predecessor.Priority,
            Mandatory = request.Predecessor.Mandatory,
            AutoRenew = request.Predecessor.AutoRenew,
        });

        if (request.Predecessor.Amount >= 0m)
        {
            // Income: the cut is the whole job, as in the single-plan overload.
            return new MultiPlanBreakOffResult
            {
                Predecessor = truncatedPredecessor,
                PredecessorPlans = truncatedPlans,
                Successor = successor,
                SuccessorPlan = null,
                SuccessorStartingEarmark = null,
            };
        }

        var proposal = request.ChosenSuccessorPlan
            ?? AllocationPlanProposer.Propose(successor, request.AllPatterns, request.CutDate, request.SpreadEvenlyWithNoIncome,
                startingEarmarkCeiling: request.SuccessorStartingEarmarkCeiling,
                ongoingRateCeiling: request.SuccessorOngoingRateCeiling);

        var successorPlan = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = proposal.Plan.FinanceId,
                DatePattern = proposal.Plan.DatePattern,
                Amount = proposal.Plan.Amount,
                StartingAllocation = request.CarriedOverJarBalance,
            },
            proposal.Outflow);

        return new MultiPlanBreakOffResult
        {
            Predecessor = truncatedPredecessor,
            PredecessorPlans = truncatedPlans,
            Successor = proposal.Outflow,
            SuccessorPlan = successorPlan,
            SuccessorStartingEarmark = proposal.StartingEarmark,
        };
    }

    /// <summary>
    /// [CALC] Breaks off like BreakOff, but gives the successor one plan per predecessor plan, each continuing its own rate and cadence from the cut, instead of one consolidated plan.
    /// The carried jar balance goes into the first plan's StartingAllocation; the new finance id has one jar, so which plan holds it doesn't matter.
    /// The request's ChosenSuccessorPlan and SpreadEvenlyWithNoIncome are unused, since no new plan is proposed.
    /// </summary>
    /// <param name="request">The same break-off request the consolidating overload takes.</param>
    public static MultiPlanKeepSeparateResult BreakOffKeepingPlansSeparate(MultiPlanBreakOffRequest request)
    {
        ValidateCutBoundaries(request.Predecessor, request.CutDate, request.SuccessorFinanceId, request.SuccessorSchedule);

        var predecessorLastDay = request.CutDate.AddDays(-1);
        var truncatedPredecessor = request.Predecessor.WithUntil(predecessorLastDay);
        var truncatedPlans = request.PredecessorPlans
            .Select(plan => PatternTruncation.EndOn(request.Predecessor, plan, predecessorLastDay).Plan!)
            .ToList();

        var successor = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = request.SuccessorFinanceId,
            Source = request.Predecessor.Source,
            Description = request.Predecessor.Description,
            Notes = request.Predecessor.Notes,
            DatePattern = RecurrenceRule.Create(request.SuccessorSchedule),
            Amount = request.SuccessorAmount,
            Priority = request.Predecessor.Priority,
            Mandatory = request.Predecessor.Mandatory,
            AutoRenew = request.Predecessor.AutoRenew,
        });

        var successorPlans = request.PredecessorPlans
            .Select((plan, index) => EarMarkPattern.Create(
                new EarMarkPatternOptions
                {
                    FinanceId = request.SuccessorFinanceId,
                    // Re-anchored to the successor's own start so it stays inside
                    // the successor's span (3.11.2.a2), keeping its own cadence.
                    DatePattern = plan.DatePattern
                        .ReanchoredToStartOn(successor.DatePattern.ActiveStart)
                        .WithUntil(successor.DatePattern.Until),
                    Amount = plan.Amount,
                    StartingAllocation = index == 0 ? Math.Max(0m, request.CarriedOverJarBalance) : 0m,
                },
                successor))
            .ToList();

        return new MultiPlanKeepSeparateResult
        {
            Predecessor = truncatedPredecessor,
            PredecessorPlans = truncatedPlans,
            Successor = successor,
            SuccessorPlans = successorPlans,
        };
    }

    /// <summary>
    /// [CALC] Extends an "ongoing" bill or paycheck further out on its renewal date; the amount, frequency, and days stay the same.
    /// The new segment's description gets a "(renewed yyyy-MM-dd)" note, so the renewal shows in the pattern list without adding a field to the pattern.
    /// </summary>
    /// <param name="request">The pattern due for renewal, its renewal date, how many years the new segment should reach, and the jar balance to carry across.</param>
    /// <returns>The now-bounded prior segment plus the new one that continues from the renewal date, with its savings plan already set up.</returns>
    public static BreakOffResult Renew(RenewalRequest request)
    {
        if (request.SegmentYears < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "SegmentYears must be at least 1.");
        }

        var result = BreakOff(new BreakOffRequest
        {
            Predecessor = request.Predecessor,
            PredecessorPlan = request.PredecessorPlan,
            CutDate = request.RenewalDate,
            SuccessorFinanceId = request.SuccessorFinanceId,
            // Unchanged from the predecessor — a renewal is not a change.
            SuccessorAmount = request.Predecessor.Amount,
            // Active from RenewalDate but first occurring on the predecessor's next real date, so it keeps its days.
            SuccessorSchedule = request.Predecessor.DatePattern
                .ReanchoredToStartOn(request.RenewalDate)
                .WithUntil(request.RenewalDate.AddYears(request.SegmentYears))
                .ToOptions(),
            CarriedOverJarBalance = request.CarriedOverJarBalance,
            AllPatterns = request.AllPatterns,
        });

        // Removes any earlier renewal marker before adding the new one, so the description shows only the latest renewal.
        var label = WithoutPriorRenewalMarker(request.Predecessor.Description ?? request.Predecessor.Source);
        var markedSuccessor = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = result.Successor.FinanceId,
            Source = result.Successor.Source,
            Description = $"{label} (renewed {request.RenewalDate:yyyy-MM-dd})",
            Notes = result.Successor.Notes,
            DatePattern = result.Successor.DatePattern,
            Amount = result.Successor.Amount,
            Priority = result.Successor.Priority,
            Mandatory = result.Successor.Mandatory,
            AutoRenew = result.Successor.AutoRenew,
        });

        // The successor plan keeps the predecessor plan's ExplicitlyCreated, since a renewal is automatic: a dummy plan that keeps going stays a dummy.
        // That's the one difference from a user-driven break-off, whose new plan is the user's own.
        // Null for income, which has no plan.
        var continuingPlan = result.SuccessorPlan?.WithExplicitlyCreated(
            request.PredecessorPlan?.ExplicitlyCreated ?? false);

        return result with { Successor = markedSuccessor, SuccessorPlan = continuingPlan };
    }

    /// <summary>[CALC] The three checks every BreakOff overload shares: a real cut boundary (at least one day of history to preserve), a genuinely new successor identity, and a successor schedule that starts exactly on the cut.</summary>
    /// <param name="predecessor">The pattern being cut.</param>
    /// <param name="cutDate">Where the cut lands.</param>
    /// <param name="successorFinanceId">The id the successor would take on.</param>
    /// <param name="successorSchedule">The successor's proposed schedule.</param>
    private static void ValidateCutBoundaries(FinancialPattern predecessor, DateOnly cutDate, int successorFinanceId, RecurrenceRuleOptions successorSchedule)
    {
        if (cutDate <= predecessor.DatePattern.ActiveStart)
        {
            throw new ArgumentException(
                "The cut date must be after the pattern's own start — there has to be at least one day of history to preserve.",
                nameof(cutDate));
        }

        if (successorFinanceId == predecessor.FinanceId)
        {
            throw new DuplicateFinanceIdException(
                "The successor needs its own FinanceId — a break-off is a genuinely new identity, not an edit.",
                nameof(successorFinanceId));
        }

        // Compares ActiveStart (ActiveFrom, else Start), because a successor's Start may land on its next real occurrence rather than on cutDate, to keep its cadence (see FinancePatternSaveConfirmation.BuildSuccessorSchedule).
        // ActiveFrom = cutDate is then what keeps "no gap, no overlap" with the ended predecessor.
        var successorActiveStart = successorSchedule.ActiveFrom ?? successorSchedule.DtStart;
        if (successorActiveStart != cutDate)
        {
            throw new ArgumentException(
                "The successor's own active span must start exactly on the cut date.",
                nameof(successorSchedule));
        }
    }

    /// <summary>[CALC] Strips a "(renewed yyyy-MM-dd)" marker from a label, if it has one — so a fresh marker can be appended without stacking up prior ones.</summary>
    /// <param name="label">The label to strip a marker from.</param>
    private static string WithoutPriorRenewalMarker(string label)
    {
        var markerIndex = label.IndexOf(" (renewed ", StringComparison.Ordinal);
        return markerIndex < 0 ? label : label[..markerIndex];
    }

    /// <summary>
    /// [CALC] Finds the current segment of a bill or paycheck, the one nothing has replaced since, starting from any segment in its history.
    /// BreakOff and Renew always copy the predecessor's Source to the successor, so the current segment is the one sharing that Source with the latest start.
    /// </summary>
    /// <param name="pattern">Any segment of the bill or paycheck — the current one, an old superseded one, or anything in between.</param>
    /// <param name="allPatterns">Every pattern to search — normally the same list already passed to BreakOff/Renew.</param>
    /// <returns>The pattern itself, if nothing has since changed or renewed it, or whichever later segment has.</returns>
    public static FinancialPattern FindCurrentSegment(FinancialPattern pattern, IReadOnlyList<FinancialPattern> allPatterns) =>
        allPatterns
            .Where(candidate => candidate.Source == pattern.Source)
            .Append(pattern) // always a candidate, even if the caller's list omits it
            .OrderByDescending(candidate => candidate.DatePattern.ActiveStart)
            .First();

    /// <summary>
    /// [CALC] Finds the segment just before this one in a break-off or renewal chain: another pattern with the same Source that ends the day before this one starts.
    /// Null when this is the first segment, or was never part of a chain.
    /// Editing screens use this to tell the user "this continues an existing pattern".
    /// </summary>
    /// <param name="pattern">The segment to find a predecessor for.</param>
    /// <param name="allPatterns">Every pattern to search — normally the same list already passed to BreakOff/Renew/FindCurrentSegment.</param>
    public static FinancialPattern? FindPredecessor(FinancialPattern pattern, IReadOnlyList<FinancialPattern> allPatterns) =>
        allPatterns.FirstOrDefault(candidate =>
            candidate.Source == pattern.Source &&
            candidate.FinanceId != pattern.FinanceId &&
            candidate.DatePattern.ImmediatelyPrecedes(pattern.DatePattern));

    /// <summary>
    /// [CALC] Finds the segment just after this one in a break-off or renewal chain: another pattern with the same Source that starts the day after this one ends.
    /// Null when nothing has continued this pattern yet, so it's still the current segment.
    /// </summary>
    /// <param name="pattern">The segment to find a successor for.</param>
    /// <param name="allPatterns">Every pattern to search — normally the same list already passed to BreakOff/Renew/FindCurrentSegment.</param>
    public static FinancialPattern? FindSuccessor(FinancialPattern pattern, IReadOnlyList<FinancialPattern> allPatterns) =>
        allPatterns.FirstOrDefault(candidate =>
            candidate.Source == pattern.Source &&
            candidate.FinanceId != pattern.FinanceId &&
            pattern.DatePattern.ImmediatelyPrecedes(candidate.DatePattern));

    /// <summary>
    /// [CALC] Whether two patterns sharing a Source have overlapping active spans.
    /// Nothing is meant to create overlapping same-Source patterns, but FinancialPattern.Create doesn't prevent it, so callers building the full same-Source list use this to keep an overlapping pattern from being treated as a chain neighbor and absorbed or cascaded onto.
    /// RestructureFactory.SpansOverlap does the same for savings plans.
    /// </summary>
    /// <returns>True when the two patterns' own active spans (ActiveStart–Until) share any day.</returns>
    public static bool SpansOverlap(FinancialPattern a, FinancialPattern b) =>
        a.DatePattern.ActiveSpansOverlap(b.DatePattern);

    /// <summary>
    /// [CALC] Keeps a chain linked when a segment's Until moves: the next segment's Start moves to stay right after it, whether that shrinks or grows that segment.
    /// A later segment the new Until reaches all the way through is absorbed instead; its FinanceId ceases to exist (see FinancialChainBoundaryResult for what the caller still owes it), and the walk continues to the next.
    /// RestructureFactory.ExtendUntil does the same for savings plans.
    /// </summary>
    /// <param name="current">The segment being saved, with its own Until about to change.</param>
    /// <param name="otherPatterns">Every other FinancialPattern sharing the same Source.</param>
    /// <param name="newUntil">The proposed new Until.</param>
    /// <returns>The segment's own updated shape, whichever later segments get absorbed (if any, ordered earliest first), and whichever one segment needs its own Start moved to stay contiguous (if any).</returns>
    public static FinancialChainBoundaryResult ExtendUntil(FinancialPattern current, IReadOnlyList<FinancialPattern> otherPatterns, DateOnly newUntil)
    {
        if (newUntil < current.DatePattern.ActiveStart)
        {
            throw new ArgumentException("The new Until can't be before the pattern's own Start.", nameof(newUntil));
        }

        var laterPatterns = otherPatterns
            .Where(pattern => pattern.DatePattern.ActiveStart > current.DatePattern.ActiveStart)
            .OrderBy(pattern => pattern.DatePattern.ActiveStart)
            .ToList();

        var absorbed = new List<FinancialPattern>();
        FinancialPattern? neighbor = null;

        // Absorbs each later segment the new Until reaches all the way through, then moves the first one it doesn't reach to start the day after newUntil and stops.
        foreach (var pattern in laterPatterns)
        {
            if (newUntil >= pattern.DatePattern.Until)
            {
                absorbed.Add(pattern);
                continue;
            }

            neighbor = pattern.ReanchoredToStartOn(newUntil.AddDays(1));
            break;
        }

        return new FinancialChainBoundaryResult { Current = current.WithUntil(newUntil), Absorbed = absorbed, AdjustedNeighbor = neighbor };
    }

    /// <summary>[CALC] Resolves "stay linked in the chain" for a segment's own Start moving, against every other FinancialPattern sharing its Source — the mirror of ExtendUntil, walking backward through earlier segments instead.</summary>
    /// <param name="current">The segment being saved, with its own Start about to change.</param>
    /// <param name="otherPatterns">Every other FinancialPattern sharing the same Source.</param>
    /// <param name="newStart">The proposed new Start.</param>
    /// <returns>The segment's own updated shape, whichever earlier segments get absorbed (if any, ordered latest first), and whichever one segment needs its own Until moved to stay contiguous (if any).</returns>
    public static FinancialChainBoundaryResult ExtendStart(FinancialPattern current, IReadOnlyList<FinancialPattern> otherPatterns, DateOnly newStart)
    {
        if (newStart > current.DatePattern.Until)
        {
            throw new ArgumentException("The new Start can't be after the pattern's own Until.", nameof(newStart));
        }

        // Uses <=, not <: current already has the proposed Start, so a segment starting exactly on newStart must still be a candidate to absorb.
        // RestructureFactory.ExtendStart uses the same inclusive boundary.
        var earlierPatterns = otherPatterns
            .Where(pattern => pattern.DatePattern.ActiveStart <= current.DatePattern.ActiveStart)
            .OrderByDescending(pattern => pattern.DatePattern.ActiveStart)
            .ToList();

        var absorbed = new List<FinancialPattern>();
        FinancialPattern? neighbor = null;

        // Absorbs each earlier segment the new Start reaches all the way back through, then moves the first one it doesn't reach to end the day before newStart and stops.
        foreach (var pattern in earlierPatterns)
        {
            if (newStart <= pattern.DatePattern.ActiveStart)
            {
                absorbed.Add(pattern);
                continue;
            }

            neighbor = pattern.WithUntil(newStart.AddDays(-1));
            break;
        }

        return new FinancialChainBoundaryResult { Current = current.WithStart(newStart), Absorbed = absorbed, AdjustedNeighbor = neighbor };
    }

    /// <summary>
    /// [CALC] Copies an edited segment's new Amount and recurrence shape (Frequency, Interval, ByDay, ByMonthDay) onto every later segment with the same Source; the default when either changes.
    /// Each later segment keeps its own Start, Until, Priority, Mandatory, Description, Notes, and AutoRenew.
    /// RestructureFactory.CascadeForward does the same for savings plans.
    /// </summary>
    /// <param name="newShape">The edited segment's own new recurrence shape — only Frequency/Interval/ByDay/ByMonthDay are read from it, not its Start/Until.</param>
    /// <param name="newAmount">The edited segment's own new Amount.</param>
    /// <param name="laterPatterns">Every later segment in the same chain (Start after the segment being edited).</param>
    /// <returns>One freshly-built segment per entry in laterPatterns, in the same order, each carrying the new Amount and shape forward.</returns>
    public static IReadOnlyList<FinancialPattern> CascadeForward(RecurrenceRule newShape, decimal newAmount, IReadOnlyList<FinancialPattern> laterPatterns) =>
        laterPatterns
            .Select(pattern => FinancialPattern.Create(new FinancialPatternOptions
            {
                FinanceId = pattern.FinanceId,
                Source = pattern.Source,
                DatePattern = pattern.DatePattern.WithShapeOf(newShape),
                Amount = newAmount,
                Priority = pattern.Priority,
                Mandatory = pattern.Mandatory,
                Description = pattern.Description,
                Notes = pattern.Notes,
                AutoRenew = pattern.AutoRenew,
            }))
            .ToList();

    /// <summary>
    /// [CALC] Copies an edited segment's new Priority, Mandatory, Description, Notes, and AutoRenew onto every later segment with the same Source.
    /// Each later segment keeps its own Amount, shape, Start, and Until.
    /// Savings plans have none of these fields, so there's no savings-plan version.
    /// </summary>
    /// <param name="current">The edited segment — its own new Priority/Mandatory/Description/Notes/AutoRenew are what gets copied forward.</param>
    /// <param name="laterPatterns">Every later segment in the same chain.</param>
    /// <returns>One freshly-built segment per entry in laterPatterns, in the same order, each carrying the new trivial-field values forward.</returns>
    public static IReadOnlyList<FinancialPattern> CascadeTrivialFieldsForward(FinancialPattern current, IReadOnlyList<FinancialPattern> laterPatterns) =>
        laterPatterns
            .Select(pattern => FinancialPattern.Create(new FinancialPatternOptions
            {
                FinanceId = pattern.FinanceId,
                Source = pattern.Source,
                DatePattern = pattern.DatePattern,
                Amount = pattern.Amount,
                Priority = current.Priority,
                Mandatory = current.Mandatory,
                Description = current.Description,
                Notes = current.Notes,
                AutoRenew = current.AutoRenew,
            }))
            .ToList();
}

/// <summary>
/// What ExtendUntil and ExtendStart need the caller to carry out; RestructureFactory.ChainBoundaryResult is the savings-plan version.
/// An absorbed segment's FinanceId is gone, which orphans its savings plans and manual earmarks.
/// This factory can't delete them, so the caller (FinancePatternSaveConfirmation) decides what to do with each entry in Absorbed.
/// </summary>
public sealed record FinancialChainBoundaryResult
{
    public required FinancialPattern Current { get; init; }
    public required IReadOnlyList<FinancialPattern> Absorbed { get; init; }
    public FinancialPattern? AdjustedNeighbor { get; init; }
}
