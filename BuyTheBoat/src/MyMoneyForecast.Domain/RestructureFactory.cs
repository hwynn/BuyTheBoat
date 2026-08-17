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
        if (request.CutDate <= request.Predecessor.DatePattern.Start)
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
        var successorActiveStart = request.SuccessorSchedule.ActiveFrom ?? request.SuccessorSchedule.Start;
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
                Start = dueDate,
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
                if (plans[i].DatePattern.ActiveStart <= plans[j].DatePattern.Until &&
                    plans[j].DatePattern.ActiveStart <= plans[i].DatePattern.Until)
                {
                    return null;
                }
            }
        }

        return plans.OrderByDescending(plan => plan.DatePattern.Start).First();
    }
}
