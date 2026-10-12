namespace BuyTheBoat.Domain;

/// <summary>
/// Everything "Change my savings plan starting on a date" needs: the plan being changed, when the new rate takes effect, and its new amount and schedule.
/// Unlike BreakOffRequest, there's no SuccessorFinanceId or CarriedOverJarBalance: the successor keeps the same finance id, so the forecast carries the jar's balance across the cut on its own.
/// </summary>
public sealed record RestructureRequest
{
    public required EarMarkPattern Predecessor { get; init; }
    public required FinancialPattern Goal { get; init; }
    public required DateOnly CutDate { get; init; }
    public required decimal SuccessorAmount { get; init; }
    public required RecurrenceRuleOptions SuccessorSchedule { get; init; }
}

/// <summary>The predecessor plan, now ending the day before the cut, plus the new plan that continues from the cut date, both under the same finance id.</summary>
public sealed record RestructureResult
{
    public required EarMarkPattern Predecessor { get; init; }
    public required EarMarkPattern Successor { get; init; }
}

/// <summary>
/// "Restructure the plan": changes a savings plan from a date forward without touching the bill or goal it funds, so the plan keeps its finance id.
/// Also finds the current plan in a chain and keeps a chain of plans linked when one's dates change; BreakOffFactory does the same for bills and goals.
/// </summary>
/// <remarks>
/// There's no jar hand-off: with the same finance id there's only one jar, and 3.13.5.3.a1 builds each day's jar balance from the day before regardless of which plan made the events, so the balance carries across the cut.
/// There's no re-proposal either: the goal hasn't changed, so the user's new amount and schedule are used directly rather than run through AllocationPlanProposer.
/// StopContributing is a thin wrapper over Restructure for stopping contributions rather than changing their rate.
/// </remarks>
public static class RestructureFactory
{
    /// <summary>
    /// [CALC] Changes an existing savings plan's contribution amount or schedule from a chosen date forward, without touching the goal or bill it's funding.
    /// What was already saved stays exactly as it was.
    /// </summary>
    /// <param name="request">The plan being changed, the date the new rate takes effect, and the new amount/schedule.</param>
    /// <returns>The now-bounded original plan plus the new one that continues from the cut date, at the user's specified rate.</returns>
    public static RestructureResult Restructure(RestructureRequest request)
    {
        if (request.CutDate <= request.Predecessor.DatePattern.ActiveStart)
        {
            throw new ArgumentException(
                "The cut date must be after the plan's own start — there has to be at least one day of the old rate to preserve.",
                nameof(request));
        }

        // Compares the active span's start, not the first occurrence: StopContributing's single $0 occurrence sits at the due date, but its active span must still start on the cut date.
        var successorActiveStart = request.SuccessorSchedule.ActiveFrom ?? request.SuccessorSchedule.DtStart;
        if (successorActiveStart != request.CutDate)
        {
            throw new ArgumentException(
                "The successor's active span must start exactly on the cut date.",
                nameof(request));
        }

        // The predecessor ends the day before the cut, or keeps its own end if that's already earlier (as in PatternTruncation.EndOn).
        var predecessorLastDay = request.CutDate.AddDays(-1);
        var truncatedUntil = request.Predecessor.DatePattern.Until < predecessorLastDay
            ? request.Predecessor.DatePattern.Until
            : predecessorLastDay;

        var truncatedPredecessor = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = request.Predecessor.FinanceId,
                DatePattern = request.Predecessor.DatePattern.WithUntil(truncatedUntil),
                Amount = request.Predecessor.Amount,
                StartingAllocation = request.Predecessor.StartingAllocation,
            },
            request.Goal);

        var successor = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = request.Predecessor.FinanceId,
                DatePattern = RecurrenceRule.Create(request.SuccessorSchedule),
                Amount = request.SuccessorAmount,
                // No StartingAllocation: it's the same jar, so its balance carries across the cut on its own.
            },
            request.Goal);

        return new RestructureResult
        {
            Predecessor = truncatedPredecessor,
            Successor = successor,
        };
    }

    /// <summary>
    /// [CALC] Stops new contributions to a savings plan from a chosen date forward, while keeping its jar open until the goal's due date, so the money already saved isn't released into free funds early.
    /// To resume contributions later, call Restructure again with a real rate.
    /// </summary>
    /// <param name="predecessor">The plan whose contributions are stopping.</param>
    /// <param name="goal">The bill or goal it funds — untouched either way.</param>
    /// <param name="cutDate">The date contributions stop.</param>
    /// <returns>The now-bounded original plan plus an empty (zero-rate) continuation, exactly like AllocationPlanProposer.ProposeEmpty's shape.</returns>
    public static RestructureResult StopContributing(EarMarkPattern predecessor, FinancialPattern goal, DateOnly cutDate)
    {
        // The successor is the same empty plan AllocationPlanProposer.ProposeEmpty builds: one $0 occurrence on the due date, with its active span reaching back to the cut date.
        // The jar stays open across every remaining occurrence, which still pays out as usual; only new contributions stop.
        var dueDate = goal.DatePattern.Until;

        return Restructure(new RestructureRequest
        {
            Predecessor = predecessor,
            Goal = goal,
            CutDate = cutDate,
            SuccessorAmount = 0m,
            SuccessorSchedule = new RecurrenceRuleOptions
            {
                // Frequency doesn't matter for a single occurrence.
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = dueDate,
                Count = 1,
                // Only reach back when the occurrence itself doesn't already
                // cover the cut date.
                ActiveFrom = cutDate < dueDate ? cutDate : null,
            },
        });
    }

    /// <summary>
    /// [CALC] Finds the current plan among several sharing one finance id, the way BreakOffFactory.FindCurrentSegment does for bills and goals; a break-off uses it to offer alternative plan shapes.
    /// Null when the list is empty or the plans run at the same time, since then no single plan is current.
    /// </summary>
    /// <param name="plans">Every EarMarkPattern sharing one finance_id.</param>
    /// <returns>The plan with the latest Start, or null when the list is empty or any two plans' own active spans overlap.</returns>
    public static EarMarkPattern? FindCurrentPlan(IReadOnlyList<EarMarkPattern> plans)
    {
        if (plans.Count == 0)
        {
            return null;
        }

        // Returns null as soon as any two plans overlap, since they run at the same time rather than one after another.
        for (var i = 0; i < plans.Count; i++)
        {
            for (var j = i + 1; j < plans.Count; j++)
            {
                if (SpansOverlap(plans[i], plans[j]))
                {
                    return null;
                }
            }
        }

        return plans.OrderByDescending(plan => plan.DatePattern.ActiveStart).First();
    }

    /// <summary>
    /// [CALC] Whether two plans sharing one finance id run at the same time (e.g. two household partners' plans both active) rather than one after another in a chain.
    /// FindCurrentPlan uses it, and EarmarkPatternSaveConfirmation uses it to keep a concurrent plan from being treated as a predecessor or successor.
    /// </summary>
    /// <returns>True when the two plans' own active spans (ActiveStart–Until) share any day.</returns>
    public static bool SpansOverlap(EarMarkPattern a, EarMarkPattern b) =>
        a.DatePattern.ActiveSpansOverlap(b.DatePattern);

    /// <summary>
    /// [CALC] Keeps a chain of plans linked when one plan's Until moves: the next plan's Start moves to stay right after it, whether that shrinks or grows that plan.
    /// A later plan the new Until reaches all the way through is absorbed instead, its StartingAllocation added to the current plan's, and the walk continues to the next.
    /// </summary>
    /// <param name="current">The plan being saved, with its own Until about to change.</param>
    /// <param name="otherPlans">Every other EarMarkPattern sharing the same finance_id.</param>
    /// <param name="goal">The goal this Savings Plan funds.</param>
    /// <param name="newUntil">The proposed new Until.</param>
    /// <returns>The plan's own updated shape, whichever later plans get absorbed (if any, ordered earliest first), and whichever one plan needs its own Start moved to stay contiguous (if any).</returns>
    public static ChainBoundaryResult ExtendUntil(EarMarkPattern current, IReadOnlyList<EarMarkPattern> otherPlans, FinancialPattern goal, DateOnly newUntil)
    {
        if (newUntil < current.DatePattern.ActiveStart)
        {
            throw new ArgumentException("The new Until can't be before the plan's own Start.", nameof(newUntil));
        }

        var laterPlans = otherPlans
            .Where(plan => plan.DatePattern.ActiveStart > current.DatePattern.ActiveStart)
            .OrderBy(plan => plan.DatePattern.ActiveStart)
            .ToList();

        var absorbed = new List<EarMarkPattern>();
        var absorbedStartingAllocation = 0m;
        EarMarkPattern? neighbor = null;

        // Absorbs each later plan the new Until reaches all the way through, then moves the first one it doesn't reach to start the day after newUntil and stops.
        foreach (var plan in laterPlans)
        {
            if (newUntil >= plan.DatePattern.Until)
            {
                absorbed.Add(plan);
                // Keeps the absorbed plan's StartingAllocation so its money isn't lost.
                absorbedStartingAllocation += plan.StartingAllocation;
                continue;
            }

            neighbor = EarMarkPattern.Create(
                new EarMarkPatternOptions
                {
                    FinanceId = plan.FinanceId,
                    DatePattern = plan.DatePattern.ReanchoredToStartOn(newUntil.AddDays(1)),
                    Amount = plan.Amount,
                    StartingAllocation = plan.StartingAllocation,
                },
                goal);
            break;
        }

        var updatedCurrent = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = current.FinanceId,
                DatePattern = current.DatePattern.WithUntil(newUntil),
                Amount = current.Amount,
                StartingAllocation = current.StartingAllocation + absorbedStartingAllocation,
            },
            goal);

        return new ChainBoundaryResult { Current = updatedCurrent, Absorbed = absorbed, AdjustedNeighbor = neighbor };
    }

    /// <summary>[CALC] Keeps a chain of plans linked when one plan's Start moves; the mirror of ExtendUntil, walking back through earlier plans.</summary>
    /// <param name="current">The plan being saved, with its own Start about to change.</param>
    /// <param name="otherPlans">Every other EarMarkPattern sharing the same finance_id.</param>
    /// <param name="goal">The goal this Savings Plan funds.</param>
    /// <param name="newStart">The proposed new Start.</param>
    /// <returns>The plan's own updated shape, whichever earlier plans get absorbed (if any, ordered latest first), and whichever one plan needs its own Until moved to stay contiguous (if any).</returns>
    public static ChainBoundaryResult ExtendStart(EarMarkPattern current, IReadOnlyList<EarMarkPattern> otherPlans, FinancialPattern goal, DateOnly newStart)
    {
        if (newStart > current.DatePattern.Until)
        {
            throw new ArgumentException("The new Start can't be after the plan's own Until.", nameof(newStart));
        }

        // Uses <=, not <: current already has the proposed Start, so a plan starting exactly on newStart must still be a candidate to absorb.
        var earlierPlans = otherPlans
            .Where(plan => plan.DatePattern.ActiveStart <= current.DatePattern.ActiveStart)
            .OrderByDescending(plan => plan.DatePattern.ActiveStart)
            .ToList();

        var absorbed = new List<EarMarkPattern>();
        var absorbedStartingAllocation = 0m;
        EarMarkPattern? neighbor = null;

        // Absorbs each earlier plan the new Start reaches all the way back through, then moves the first one it doesn't reach to end the day before newStart and stops.
        foreach (var plan in earlierPlans)
        {
            if (newStart <= plan.DatePattern.ActiveStart)
            {
                absorbed.Add(plan);
                absorbedStartingAllocation += plan.StartingAllocation;
                continue;
            }

            neighbor = EarMarkPattern.Create(
                new EarMarkPatternOptions
                {
                    FinanceId = plan.FinanceId,
                    DatePattern = plan.DatePattern.WithUntil(newStart.AddDays(-1)),
                    Amount = plan.Amount,
                    StartingAllocation = plan.StartingAllocation,
                },
                goal);
            break;
        }

        var updatedCurrent = EarMarkPattern.Create(
            new EarMarkPatternOptions
            {
                FinanceId = current.FinanceId,
                DatePattern = current.DatePattern.WithStart(newStart),
                Amount = current.Amount,
                StartingAllocation = current.StartingAllocation + absorbedStartingAllocation,
            },
            goal);

        return new ChainBoundaryResult { Current = updatedCurrent, Absorbed = absorbed, AdjustedNeighbor = neighbor };
    }

    /// <summary>
    /// [CALC] Copies an edited plan's new Amount and recurrence shape (Frequency, Interval, ByDay, ByMonthDay) onto every later plan with the same finance id; the default when either changes.
    /// Each later plan keeps its own Start, Until, ActiveFrom, ExcludedDates, and StartingAllocation.
    /// </summary>
    /// <param name="newShape">The edited plan's own new recurrence shape — only Frequency/Interval/ByDay/ByMonthDay are read from it, not its Start/Until.</param>
    /// <param name="newAmount">The edited plan's own new Amount.</param>
    /// <param name="laterPlans">Every later plan in the same chain (Start after the plan being edited).</param>
    /// <param name="goal">The goal this Savings Plan funds.</param>
    /// <returns>One freshly-built plan per entry in laterPlans, in the same order, each carrying the new Amount and shape forward.</returns>
    public static IReadOnlyList<EarMarkPattern> CascadeForward(RecurrenceRule newShape, decimal newAmount, IReadOnlyList<EarMarkPattern> laterPlans, FinancialPattern goal) =>
        laterPlans
            .Select(plan => EarMarkPattern.Create(
                new EarMarkPatternOptions
                {
                    FinanceId = plan.FinanceId,
                    DatePattern = plan.DatePattern.WithShapeOf(newShape),
                    Amount = newAmount,
                    StartingAllocation = plan.StartingAllocation,
                },
                goal))
            .ToList();
}

/// <summary>
/// What ExtendUntil and ExtendStart need the caller to carry out: the saved plan's updated shape, the plans it absorbed, and the neighbor that moved.
/// Manual earmarks aren't handled here, since this factory has no access to saved data; the caller decides whether any need deleting once Absorbed is known.
/// </summary>
public sealed record ChainBoundaryResult
{
    public required EarMarkPattern Current { get; init; }
    public required IReadOnlyList<EarMarkPattern> Absorbed { get; init; }
    public EarMarkPattern? AdjustedNeighbor { get; init; }
}
