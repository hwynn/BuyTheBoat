namespace MyMoneyForecast.Domain;

// Everything "Change my savings plan starting on a date" needs: the plan
// being changed, when the new rate takes effect, and the new amount/schedule
// it takes on from there. Unlike BreakOffRequest, there is no
// SuccessorFinanceId (the successor shares the predecessor's — the goal or
// bill itself never changes here) and no CarriedOverJarBalance — the jar
// never restarts, because the finance_id doesn't change, so the ordinary
// day-to-day cascade carries its balance across the cut for free.
public sealed record RestructureRequest
{
    public required EarMarkPattern Predecessor { get; init; }
    public required FinancialPattern Goal { get; init; }
    public required DateOnly CutDate { get; init; }
    public required decimal SuccessorAmount { get; init; }
    public required RecurrenceRuleOptions SuccessorSchedule { get; init; }
}

// The truncated predecessor plan plus the new one that continues from the cut
// date — sharing the SAME finance_id throughout (the predecessor is
// truncated and kept, not deleted, exactly like BreakOffFactory's own
// predecessor, but it never takes on a new identity, because the goal it
// funds never changed).
public sealed record RestructureResult
{
    public required EarMarkPattern Predecessor { get; init; }
    public required EarMarkPattern Successor { get; init; }
}

// "Restructure the plan" — the earmark-side counterpart to BreakOffFactory's
// "Change starting on a date," scoped one level down: only the savings PLAN
// changes; the goal or bill itself (FinancialPattern) is never touched, so
// the plan's FinanceId never changes either. This rests on more than one
// EarMarkPattern being allowed to share a finance_id, so long as their
// active spans don't overlap (enforced below; the concurrent, overlapping
// case is a separate feature, not built here).
//
// No jar hand-off: because the finance_id is identical throughout, there is
// only ever ONE jar. 3.13.5.3.a1 computes a jar's expected_amount from
// yesterday's value plus today's earmark events FOR THAT FINANCE_ID, with no
// reference to which pattern generated either day's events — so the balance
// carries across the cut automatically, through the ordinary cascade.
// StartingAllocation is left at its default (0m) on the successor.
//
// No re-proposal: the successor's amount/schedule are user-specified
// directly, not run through AllocationPlanProposer — the goal hasn't
// changed, so there's no new fact for a proposer to react to; the user is
// changing their own preference for funding it, like editing any other
// pattern.
//
// StopContributing is a thin wrapper over Restructure — the same
// relationship BreakOffFactory.Renew has to BreakOff — for the specific
// case of stopping contributions rather than changing them to a new rate.
public static class RestructureFactory
{
    /// <summary>[CALC] Changes an existing savings plan's contribution amount or schedule from a chosen date forward, without touching the goal or bill it's funding. What was already saved stays exactly as it was.</summary>
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

        // Checked against the ACTIVE span's start, not the literal occurrence
        // Start — an ordinary rate change has no ActiveFrom, so the two are
        // the same thing there. StopContributing's "stop contributing" shape
        // (below) needs them to differ: its one $0 occurrence sits at the
        // goal's own due date, but its active span — where the jar stays
        // alive and computable — must still begin exactly on the cut date.
        var successorActiveStart = request.SuccessorSchedule.ActiveFrom ?? request.SuccessorSchedule.DtStart;
        if (successorActiveStart != request.CutDate)
        {
            throw new ArgumentException(
                "The successor's active span must start exactly on the cut date.",
                nameof(request));
        }

        // Never extend the predecessor — only ever shorten it to at most the
        // day before the cut. A plan that already ended earlier (the user set
        // an earlier end for it) keeps its own, earlier end (mirrors
        // PatternTruncation.EndOn's identical rule).
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
                // No StartingAllocation — same finance_id means the same jar
                // throughout; its balance carries across the cut through the
                // ordinary cascade, with nothing to seed.
            },
            request.Goal);

        return new RestructureResult
        {
            Predecessor = truncatedPredecessor,
            Successor = successor,
        };
    }

    /// <summary>[CALC] Stops new contributions to a savings plan from a chosen date forward, while keeping its jar alive — un-added-to — all the way to the goal's own due date, so the money already saved isn't dropped into free balance early. Resuming contributions later is just calling Restructure again with a real rate.</summary>
    /// <param name="predecessor">The plan whose contributions are stopping.</param>
    /// <param name="goal">The bill or goal it funds — untouched either way.</param>
    /// <param name="cutDate">The date contributions stop.</param>
    /// <returns>The now-bounded original plan plus an empty (zero-rate) continuation, exactly like AllocationPlanProposer.ProposeEmpty's shape.</returns>
    public static RestructureResult StopContributing(EarMarkPattern predecessor, FinancialPattern goal, DateOnly cutDate)
    {
        // "A goal met early — stop contributing, keep the jar alive" is
        // Restructure with the successor being the same empty shape
        // AllocationPlanProposer.ProposeEmpty already builds for a declined
        // plan, not a separate mechanism. Works the same for a one-time goal
        // or a repeating one (a loan): the successor's ACTIVE SPAN reaches
        // from the cut date to the goal's own due date, so the jar stays
        // alive and computable across every remaining occurrence in
        // between — each of which still releases money exactly as today —
        // only the INFLOW stops. Its one $0 OCCURRENCE sits at the due date
        // itself, mirroring ProposeEmpty exactly.
        var dueDate = goal.DatePattern.Until;

        return Restructure(new RestructureRequest
        {
            Predecessor = predecessor,
            Goal = goal,
            CutDate = cutDate,
            SuccessorAmount = 0m,
            SuccessorSchedule = new RecurrenceRuleOptions
            {
                // Frequency is immaterial for a single occurrence — mirrors
                // ProposeEmpty's identical shape.
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = dueDate,
                Count = 1,
                // Only reach back when the occurrence itself doesn't already
                // cover the cut date.
                ActiveFrom = cutDate < dueDate ? cutDate : null,
            },
        });
    }

    /// <summary>[CALC] Finds whichever EarMarkPattern is current among several sharing one finance_id, the way BreakOffFactory.FindCurrentSegment resolves a FinancialPattern chain — feeds a break-off's own alternative plan-shape candidates. Returns null for a genuinely concurrent set, not just an empty one — active spans overlapping means there's no single "current" plan to pick.</summary>
    /// <param name="plans">Every EarMarkPattern sharing one finance_id.</param>
    /// <returns>The plan with the latest Start, or null when the list is empty or any two plans' own active spans overlap.</returns>
    public static EarMarkPattern? FindCurrentPlan(IReadOnlyList<EarMarkPattern> plans)
    {
        if (plans.Count == 0)
        {
            return null;
        }

        // Any two plans whose active spans overlap means this is a genuinely
        // concurrent set (F27), not a sequential chain — nothing here is
        // "the" current one, so bail before picking anything.
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

    /// <summary>[CALC] Whether two EarMarkPatterns sharing one finance_id are genuinely concurrent (F27 — e.g. two household-partner funders both live at once) rather than sequential chain neighbors. The one shared definition of "overlap" for this whole file — FindCurrentPlan's own pairwise check above, extracted so FinancePatternSaveConfirmation.RunForPlan can rule out a concurrent plan before ever treating it as a predecessor/successor, instead of re-deriving the same test a second way.</summary>
    /// <returns>True when the two plans' own active spans (ActiveStart–Until) share any day.</returns>
    public static bool SpansOverlap(EarMarkPattern a, EarMarkPattern b) =>
        a.DatePattern.ActiveSpansOverlap(b.DatePattern);

    /// <summary>[CALC] Resolves "stay linked in the chain" (planning/27) for a plan's own Until moving, against every other EarMarkPattern sharing its finance_id — the neighbor a growing Until reaches into shrinks or expands to match; one reached far enough to be fully overtaken is absorbed instead, and the walk keeps going in case it reaches even further.</summary>
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

        // Walks forward through the chain in date order, absorbing every
        // later plan the new Until reaches all the way through and
        // continuing past it in case the reach goes further still. Stops at
        // the first plan it doesn't fully reach — that one gets its own
        // Start moved to newUntil + 1 to stay contiguous, whether that
        // means it shrinks (Until grew into it) or grows (Until shrank away
        // from it); nothing beyond that one is touched at all.
        foreach (var plan in laterPlans)
        {
            if (newUntil >= plan.DatePattern.Until)
            {
                absorbed.Add(plan);
                // Already-realized money in an absorbed plan's own jar share
                // is never just dropped — same principle break-off's own
                // jar hand-off already embodies. Harmless to add when it's
                // 0m, which is the common case for anything but a chain's
                // very first segment.
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

    /// <summary>[CALC] Resolves "stay linked in the chain" (planning/27) for a plan's own Start moving, against every other EarMarkPattern sharing its finance_id — the mirror of ExtendUntil, walking backward through earlier plans instead of forward through later ones.</summary>
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

        // <= , not < : current.DatePattern.ActiveStart here is already the NEW,
        // proposed Start (every caller passes it as both current and
        // newStart) — a neighbor whose own Start lands EXACTLY on newStart
        // is still a real absorb candidate (the loop's own newStart <=
        // plan.DatePattern.ActiveStart check below would say so), so filtering it
        // out here with a strict < silently dropped that exact-boundary
        // case entirely. Found 2026-08-17 while building Phase 1's own
        // mirror of this method and hitting the case directly; fixed here
        // too since the same bug was already latent in this, the original.
        var earlierPlans = otherPlans
            .Where(plan => plan.DatePattern.ActiveStart <= current.DatePattern.ActiveStart)
            .OrderByDescending(plan => plan.DatePattern.ActiveStart)
            .ToList();

        var absorbed = new List<EarMarkPattern>();
        var absorbedStartingAllocation = 0m;
        EarMarkPattern? neighbor = null;

        // Mirror of ExtendUntil's own walk, going backward through earlier
        // plans instead — same absorb-then-nudge shape, same "stop at the
        // first one not fully reached" rule.
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

    /// <summary>[CALC] Applies a plan's own newly-edited Amount and recurrence shape to every later plan sharing the same finance_id — planning/27's "cascade forward" choice for an EarMarkPattern chain, the default when Amount or shape changes. Each later plan keeps its own Start/Until/ActiveFrom/ExcludedDates/StartingAllocation; only Amount and shape (Frequency/Interval/ByDay/ByMonthDay) change.</summary>
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

// What ExtendUntil/ExtendStart need the caller to actually carry out — the
// plan being saved's own updated shape, plus whatever the rest of the chain
// needs (planning/27's "stay linked in the chain" question). Deliberately
// silent on ManualEarmarks: these are pure domain functions with no
// repository access, same reasoning as PatternTruncation/BreakOffRequest's
// own carried-balance fields — a caller with real data decides what (if
// anything) needs deleting once Absorbed is known.
public sealed record ChainBoundaryResult
{
    public required EarMarkPattern Current { get; init; }
    public required IReadOnlyList<EarMarkPattern> Absorbed { get; init; }
    public EarMarkPattern? AdjustedNeighbor { get; init; }
}
