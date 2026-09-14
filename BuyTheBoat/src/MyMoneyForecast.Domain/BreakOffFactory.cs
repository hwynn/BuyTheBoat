namespace MyMoneyForecast.Domain;

// Everything "Change starting on a date" needs: the pattern being changed,
// when the change takes effect, the new shape it takes on from there, and —
// for an outflow with a savings plan — the jar balance to carry across.
// CarriedOverJarBalance is supplied by the caller because a pure domain
// factory has no forecast of its own to read a jar's value from; the caller
// reads it off the current forecast at CutDate before calling this (the
// same "read a jar's balance on a chosen day" the manual-earmark dialog
// already does).
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

    // Whether the successor's freshly-proposed plan should spread evenly
    // when there's no clean income to pace against (an ordinary bill or
    // goal) or reserve immediately in full (a transfer's withdrawal — see
    // TransferBreakOffFactory, which sets this false). Irrelevant whenever
    // the successor is genuinely recurring (multi-occurrence), which is
    // unaffected either way.
    public bool SpreadEvenlyWithNoIncome { get; init; } = true;

    // planning/25's Item G: when the caller already asked the user which
    // plan shape they wanted (AllocationPlanProposer.Propose's own default,
    // ProposeSameSchedule, or ProposeSameAmount) and got a real answer, pass
    // it here to use AS-IS instead of computing Propose's own default
    // internally. Null (the ordinary case — no question was asked, or the
    // user picked the default) falls back to the same internal Propose call
    // this class has always made.
    public ProposedAllocationPlan? ChosenSuccessorPlan { get; init; }

    // The affordability ceilings for the successor's FRESHLY-PROPOSED plan —
    // used only on the fallback path (ChosenSuccessorPlan null), since a
    // supplied ChosenSuccessorPlan was already sized by whoever picked it.
    // Both null leaves that fallback uncapped, the default for callers that
    // don't size against a forecast. SuccessorStartingEarmarkCeiling caps the
    // one-off front-load; SuccessorOngoingRateCeiling caps the per-cycle rate.
    public decimal? SuccessorStartingEarmarkCeiling { get; init; }
    public decimal? SuccessorOngoingRateCeiling { get; init; }
}

// The truncated predecessor plus everything the successor needs — the same
// "one action, several linked domain objects" shape as OneTimeGoalFactory,
// TransferFactory, and AllocationPlanProposer. No new persisted "link" type:
// the predecessor is simply bounded and stays in the data as an ordinary
// ended pattern, the same as any other.
public sealed record BreakOffResult
{
    public required FinancialPattern Predecessor { get; init; }
    public required EarMarkPattern? PredecessorPlan { get; init; }
    public required FinancialPattern Successor { get; init; }
    public required EarMarkPattern? SuccessorPlan { get; init; }
    public required ManualEarmark? SuccessorStartingEarmark { get; init; }
}

// The multi-predecessor-plan break-off case (planning/25's Item F — more
// than one EarMarkPattern already shares a finance_id, F27's relaxation of
// 3.11.1.a1 — consolidating them, whether forced or chosen, into one fresh
// successor plan). Otherwise identical to BreakOffRequest; PredecessorPlans
// replaces the single, nullable PredecessorPlan. Still just ONE
// CarriedOverJarBalance, not one per plan — a finance_id has exactly one
// jar regardless of how many EarMarkPatterns feed it, so the caller reads
// it the same single way BreakOffRequest's own field already documents
// (F27/F34 — the jar-balance handoff already sums across every plan sharing
// a finance_id before this request is even built).
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

    // planning/25's Item G, extended here 2026-08-16 to match
    // BreakOffRequest's own field of the same name: when the predecessor's
    // several surviving plans turn out to be a genuine sequential chain
    // (RestructureFactory.FindCurrentPlan finds one, not a concurrent set),
    // the caller can offer the same "keep the same schedule/amount" choice
    // this overload previously never had a way to receive. Null (the
    // ordinary case) falls back to the same internal Propose call this class
    // has always made.
    public ProposedAllocationPlan? ChosenSuccessorPlan { get; init; }

    // Same as BreakOffRequest's own fields of these names — the affordability
    // ceilings for the successor's freshly-proposed plan, applied only on the
    // fallback path (ChosenSuccessorPlan null). Both null leaves it uncapped.
    public decimal? SuccessorStartingEarmarkCeiling { get; init; }
    public decimal? SuccessorOngoingRateCeiling { get; init; }
}

// PredecessorPlans replaces the single, nullable PredecessorPlan — every
// surviving plan, truncated, none dropped. There's still exactly one
// SuccessorPlan: Item F's own ruling is that consolidating N plans always
// means ONE freshly-proposed successor, the same shape as an ordinary
// single-plan break-off, never N successors.
public sealed record MultiPlanBreakOffResult
{
    public required FinancialPattern Predecessor { get; init; }
    public required IReadOnlyList<EarMarkPattern> PredecessorPlans { get; init; }
    public required FinancialPattern Successor { get; init; }
    public required EarMarkPattern? SuccessorPlan { get; init; }
    public required ManualEarmark? SuccessorStartingEarmark { get; init; }
}

// The keep-separate counterpart to MultiPlanBreakOffResult (Item F's own "keep
// separate through a break-off"): the successor keeps ONE plan PER surviving
// predecessor plan (SuccessorPlans, not a single SuccessorPlan), each continuing
// its own rate, rather than folding them all into one. No SuccessorStartingEarmark
// — the carried jar balance rides on the first plan's own StartingAllocation.
public sealed record MultiPlanKeepSeparateResult
{
    public required FinancialPattern Predecessor { get; init; }
    public required IReadOnlyList<EarMarkPattern> PredecessorPlans { get; init; }
    public required FinancialPattern Successor { get; init; }
    public required IReadOnlyList<EarMarkPattern> SuccessorPlans { get; init; }
}

// Everything a periodic RENEWAL of an "ongoing" pattern needs. Deliberately
// narrower than BreakOffRequest: there is no SuccessorAmount or
// SuccessorSchedule-SHAPE field, because renewal changes NOTHING about the
// pattern itself — Renew derives the amount, frequency, interval, and days
// straight from the predecessor. SegmentYears is the one caller-supplied
// number, since how far a renewed segment should reach is a scheduling
// choice, not part of what the pattern IS — deliberately not hardcoded here,
// so a rare, multi-year cadence and a short segment length aren't conflated.
// That is the whole distinction from BreakOff: BreakOff is for when the user
// changes something; Renew is for when nothing did.
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

// Cuts a pattern at CutDate: the predecessor's last day is the day before
// (PatternTruncation handles the predecessor and its plan together — the
// "parallel split" is exactly that truncation, not a new primitive); the
// successor starts exactly on CutDate as a genuine new FinanceId.
//
// An outflow's successor gets a FRESHLY PROPOSED plan via
// AllocationPlanProposer — never a copy of the predecessor's, since a
// break-off's whole premise is that the old plan's sizing is now wrong —
// seeded with the carried-over jar balance. The proposer is called with
// asOfDate = CutDate, not today: the successor's plan starts contributing
// exactly when the successor itself starts, deliberately not funding ahead
// of the cut. Income (Amount >= 0) never has a plan at all and skips
// straight to just the cut — no jar machinery runs.
//
// planning/25's Item G (2026-08-14, BreakOffRequest.ChosenSuccessorPlan;
// extended 2026-08-16 to MultiPlanBreakOffRequest's own field of the same
// name, once RestructureFactory.FindCurrentPlan made "which shape" answerable
// even when the predecessor's own plans are a sequential chain rather than
// exactly one row). Either overload's own successor is the caller's chosen
// plan when supplied, still seeded with CarriedOverJarBalance the same way
// either way — a chosen candidate's own StartingAllocation was only ever a
// placeholder for sizing, never meant to survive into what actually gets
// saved.
public static class BreakOffFactory
{
    /// <summary>[CALC] Ends a bill or paycheck on a chosen date and hands it off to a new one that continues from there with its own amount/schedule — "Change starting on a date." For a bill, the old savings plan is wound down and the new one is freshly proposed, carrying over whatever was already saved.</summary>
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
            DatePattern = RecurrenceRule.Create(request.SuccessorSchedule),
            Amount = request.SuccessorAmount,
            Priority = request.Predecessor.Priority,
            Mandatory = request.Predecessor.Mandatory,
            // A break-off is a change to amount/schedule, not to whether
            // the pattern "keeps going" — carried over like every other
            // identity field. Renew (below) depends on this: it is itself a
            // BreakOff, and each renewed segment must keep qualifying for
            // the next one.
            AutoRenew = request.Predecessor.AutoRenew,
        });

        if (request.Predecessor.Amount >= 0m)
        {
            // Income: the cut is the whole job. No jar, ever, so no plan to
            // truncate or propose — PredecessorPlan is ignored here by
            // design (it should always be null for income, but this path
            // doesn't depend on that).
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

    /// <summary>[CALC] The same break-off, when more than one EarMarkPattern already shares the predecessor's finance_id — planning/25's Item F, the consolidating case specifically (forced, or chosen): every surviving plan is truncated, and the successor still gets exactly one freshly-proposed plan, seeded from the one balance the finance_id's single jar actually holds. Keeping multiple plans separate under the new finance_id, instead of consolidating, is a different, not-yet-built mechanism — see FinancePatternSaveConfirmation's own TODO for that case.</summary>
    /// <param name="request">The pattern being changed, every surviving plan, the cut date, the new amount/schedule, and the one jar balance to carry across.</param>
    /// <returns>The now-bounded original pattern plus every one of its plans (each truncated), plus the new pattern that continues from the cut date, with one consolidated savings plan already set up.</returns>
    public static MultiPlanBreakOffResult BreakOff(MultiPlanBreakOffRequest request)
    {
        ValidateCutBoundaries(request.Predecessor, request.CutDate, request.SuccessorFinanceId, request.SuccessorSchedule);

        var predecessorLastDay = request.CutDate.AddDays(-1);
        var truncatedPredecessor = request.Predecessor.WithUntil(predecessorLastDay);

        // Each plan truncated the same way EndOn truncates a single one —
        // reused per plan rather than duplicated. EndOn's own Plan is only
        // ever null when its input plan is null; every entry here is a real
        // plan, so the ! is never actually exercised.
        var truncatedPlans = request.PredecessorPlans
            .Select(plan => PatternTruncation.EndOn(request.Predecessor, plan, predecessorLastDay).Plan!)
            .ToList();

        var successor = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = request.SuccessorFinanceId,
            Source = request.Predecessor.Source,
            Description = request.Predecessor.Description,
            DatePattern = RecurrenceRule.Create(request.SuccessorSchedule),
            Amount = request.SuccessorAmount,
            Priority = request.Predecessor.Priority,
            Mandatory = request.Predecessor.Mandatory,
            AutoRenew = request.Predecessor.AutoRenew,
        });

        if (request.Predecessor.Amount >= 0m)
        {
            // Income: same shortcut as the single-plan overload — no jar,
            // ever, though in practice income never has more than one plan
            // (it never has one at all) to begin with.
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

    /// <summary>[CALC] Item F's "keep separate through a break-off" (planning/25): breaks a chain segment off at the cut like BreakOff does — same truncated predecessor, same new successor pattern — but instead of folding the predecessor's several plans into one, gives the successor one plan PER surviving plan, each continuing its own rate at its own cadence from the cut under the new finance_id. The carried jar balance rides on the first plan's own StartingAllocation, since the new finance_id has one combined jar and where the balance sits doesn't change what that jar reads. The request's ChosenSuccessorPlan/SpreadEvenlyWithNoIncome go unused — no single successor shape is proposed.</summary>
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

    /// <summary>[CALC] Extends an "ongoing" bill or paycheck further out on its renewal date — the amount, frequency, and days never change, only how far out it reaches. Leaves a plain "(renewed ...)" note on the new segment so the renewal is visible to anyone reading the pattern list, without adding a new field to the pattern itself.</summary>
    /// <param name="request">The pattern due for renewal, its renewal date, how many years the new segment should reach, and the jar balance to carry across — deliberately no amount or schedule-shape input, since renewal changes neither.</param>
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
            SuccessorSchedule = new RecurrenceRuleOptions
            {
                Frequency = request.Predecessor.DatePattern.Frequency,
                Interval = request.Predecessor.DatePattern.Interval,
                ByDay = request.Predecessor.DatePattern.ByDay,
                ByMonthDay = request.Predecessor.DatePattern.ByMonthDay,
                DtStart = request.RenewalDate,
                Until = request.RenewalDate.AddYears(request.SegmentYears),
            },
            CarriedOverJarBalance = request.CarriedOverJarBalance,
            AllPatterns = request.AllPatterns,
        });

        // Strip any marker from an earlier renewal before appending a fresh
        // one, so the label always shows the latest renewal only — without
        // this, a pattern renewed for a decade would read "(renewed 2024-...)
        // (renewed 2025-...) (renewed 2026-...)..." indefinitely.
        var label = WithoutPriorRenewalMarker(request.Predecessor.Description ?? request.Predecessor.Source);
        var markedSuccessor = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = result.Successor.FinanceId,
            Source = result.Successor.Source,
            Description = $"{label} (renewed {request.RenewalDate:yyyy-MM-dd})",
            DatePattern = result.Successor.DatePattern,
            Amount = result.Successor.Amount,
            Priority = result.Successor.Priority,
            Mandatory = result.Successor.Mandatory,
            AutoRenew = result.Successor.AutoRenew,
        });

        // A renewal is automatic — the user gives no input for the continuing
        // plan, so it inherits the predecessor plan's standing rather than
        // counting as freshly authored: a dummy that keeps going stays a dummy
        // (EarMarkPattern.ExplicitlyCreated). This is the one thing that sets a
        // renewal's successor plan apart from a user-driven break-off's, whose
        // fresh successor plan IS the user's own. Null when the pattern has no
        // plan (income), where SuccessorPlan is already null.
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
            throw new ArgumentException(
                "The successor needs its own FinanceId — a break-off is a genuinely new identity, not an edit.",
                nameof(successorFinanceId));
        }

        // Checks ActiveStart (Start-or-ActiveFrom), not raw Start, since
        // 2026-08-17: a Weekly successor's own Start may need to land on
        // the reference pattern's own next real occurrence rather than
        // literally on cutDate, to keep its cadence from silently drifting
        // (FinancePatternSaveConfirmation.BuildSuccessorSchedule's own
        // comment has the full reasoning) — ActiveFrom = cutDate in that
        // case is what actually represents "no gap, no overlap" against the
        // truncated predecessor, the same invariant this check has always
        // enforced, just read off the field that's now sometimes doing that
        // job instead of Start itself.
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

    /// <summary>[CALC] Finds whichever segment of a bill or paycheck is CURRENT — the one nothing has since superseded — starting from any segment in its history. Both BreakOff and Renew always reuse a predecessor's Source verbatim on the successor, so the current segment is simply whichever pattern sharing that Source has the latest Start; no dedicated link between segments is needed.</summary>
    /// <param name="pattern">Any segment of the bill or paycheck — the current one, an old superseded one, or anything in between.</param>
    /// <param name="allPatterns">Every pattern to search — normally the same list already passed to BreakOff/Renew.</param>
    /// <returns>The pattern itself, if nothing has since changed or renewed it, or whichever later segment has.</returns>
    public static FinancialPattern FindCurrentSegment(FinancialPattern pattern, IReadOnlyList<FinancialPattern> allPatterns) =>
        allPatterns
            .Where(candidate => candidate.Source == pattern.Source)
            .Append(pattern) // always a candidate, even if the caller's list omits it
            .OrderByDescending(candidate => candidate.DatePattern.ActiveStart)
            .First();

    /// <summary>[CALC] Finds the segment immediately BEFORE this one in a break-off/renewal chain — same Source-reuse lookup FindCurrentSegment uses, one direction: another pattern sharing this one's own Source whose own Until ends exactly the day before this one's own Start. Null when this pattern doesn't continue from an earlier one — either it's the first segment ever, or it was never part of a chain at all. Editing UIs use this to tell a user "this continues an existing pattern" without them having to remember the history themselves.</summary>
    /// <param name="pattern">The segment to find a predecessor for.</param>
    /// <param name="allPatterns">Every pattern to search — normally the same list already passed to BreakOff/Renew/FindCurrentSegment.</param>
    public static FinancialPattern? FindPredecessor(FinancialPattern pattern, IReadOnlyList<FinancialPattern> allPatterns) =>
        allPatterns.FirstOrDefault(candidate =>
            candidate.Source == pattern.Source &&
            candidate.FinanceId != pattern.FinanceId &&
            candidate.DatePattern.ImmediatelyPrecedes(pattern.DatePattern));

    /// <summary>[CALC] Finds the segment immediately AFTER this one in a break-off/renewal chain — the mirror of FindPredecessor: another pattern sharing this one's own Source whose own Start begins exactly the day after this one's own Until. Null when this pattern hasn't since been continued by a later one — it's still the current segment (FindCurrentSegment would return it unchanged).</summary>
    /// <param name="pattern">The segment to find a successor for.</param>
    /// <param name="allPatterns">Every pattern to search — normally the same list already passed to BreakOff/Renew/FindCurrentSegment.</param>
    public static FinancialPattern? FindSuccessor(FinancialPattern pattern, IReadOnlyList<FinancialPattern> allPatterns) =>
        allPatterns.FirstOrDefault(candidate =>
            candidate.Source == pattern.Source &&
            candidate.FinanceId != pattern.FinanceId &&
            pattern.DatePattern.ImmediatelyPrecedes(candidate.DatePattern));

    /// <summary>[CALC] Whether two FinancialPatterns sharing a Source have overlapping active spans — unlike EarMarkPattern's own F27 concurrent earmark pattern shape, nothing in this project designs for two FinancialPatterns sharing a Source and overlapping on purpose (FindPredecessor/FindSuccessor only ever match STRICTLY contiguous dates), but FinancialPattern.Create itself validates no such thing, so a caller building the fuller "every other same-Source pattern" list (not just FindPredecessor/FindSuccessor's own strict match) needs this guard for the same reason RestructureFactory.SpansOverlap exists — an overlapping pattern must never be mistaken for a sequential chain neighbor and absorbed/cascaded onto.</summary>
    /// <returns>True when the two patterns' own active spans (ActiveStart–Until) share any day.</returns>
    public static bool SpansOverlap(FinancialPattern a, FinancialPattern b) =>
        a.DatePattern.ActiveSpansOverlap(b.DatePattern);

    /// <summary>[CALC] Resolves "stay linked in the chain" (planning/27, Phase 1) for a segment's own Until moving, against every other FinancialPattern sharing its Source — the neighbor a growing Until reaches into shrinks or expands to match; one reached far enough to be fully overtaken is absorbed instead (its own FinanceId ceases to exist entirely — see the class-level note on what a caller still owes it), and the walk keeps going in case it reaches even further. Mirrors RestructureFactory.ExtendUntil's own shape exactly, one level up — no goal parameter needed here (a FinancialPattern has no parent to validate against, unlike EarMarkPattern).</summary>
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

        // Walks forward through the chain in date order, absorbing every
        // later segment the new Until reaches all the way through and
        // continuing past it in case the reach goes further still. Stops at
        // the first one it doesn't fully reach — that one gets its own
        // Start moved to newUntil + 1 to stay contiguous, whether that means
        // it shrinks (Until grew into it) or grows (Until shrank away from
        // it); nothing beyond that one is touched at all.
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

    /// <summary>[CALC] Resolves "stay linked in the chain" (planning/27, Phase 1) for a segment's own Start moving, against every other FinancialPattern sharing its Source — the mirror of ExtendUntil, walking backward through earlier segments instead.</summary>
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

        // <= , not < : current.DatePattern.ActiveStart here is already the NEW,
        // proposed Start (every caller passes it as both current and
        // newStart) — a neighbor whose own Start lands EXACTLY on newStart
        // is still a real absorb candidate (the loop's own newStart <=
        // pattern.DatePattern.ActiveStart check below would say so), so filtering
        // it out here with a strict < silently dropped that exact-boundary
        // case entirely. Found 2026-08-17 while writing this method's own
        // app-layer test — RestructureFactory.ExtendStart had the identical,
        // already-latent bug, fixed there too the same pass.
        var earlierPatterns = otherPatterns
            .Where(pattern => pattern.DatePattern.ActiveStart <= current.DatePattern.ActiveStart)
            .OrderByDescending(pattern => pattern.DatePattern.ActiveStart)
            .ToList();

        var absorbed = new List<FinancialPattern>();
        FinancialPattern? neighbor = null;

        // Mirror of ExtendUntil's own walk, going backward through earlier
        // segments instead — same absorb-then-nudge shape, same "stop at the
        // first one not fully reached" rule.
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

    /// <summary>[CALC] Applies a segment's own newly-edited Amount and recurrence shape to every later same-Source segment — planning/27's "cascade forward" choice for a FinancialPattern chain (Phase 1), the default when Amount or shape changes. Each later segment keeps its own Start/Until/Priority/Mandatory/Description/AutoRenew; only Amount and shape (Frequency/Interval/ByDay/ByMonthDay) change. Mirrors RestructureFactory.CascadeForward exactly, one level up.</summary>
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
                AutoRenew = pattern.AutoRenew,
            }))
            .ToList();

    /// <summary>[CALC] Applies a segment's own newly-edited Priority/Mandatory/Description/AutoRenew to every later same-Source segment — planning/27's own reopened "trivial fields" question (round 3 of the fourth relationship's small questions), a FinancialPattern-only mechanism with no EarMarkPattern equivalent (EarMarkPattern has none of these fields). Structurally the same shape as CascadeForward, but for the OTHER field group — Amount/shape/Start/Until all stay each later segment's own.</summary>
    /// <param name="current">The edited segment — its own new Priority/Mandatory/Description/AutoRenew are what gets copied forward.</param>
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
                AutoRenew = current.AutoRenew,
            }))
            .ToList();
}

// What ExtendUntil/ExtendStart (Phase 1) need the caller to actually carry
// out — mirrors RestructureFactory's own ChainBoundaryResult exactly, one
// level up. Deliberately silent on what an absorbed segment's own FinanceId
// takes with it: unlike EarMarkPattern-level absorption, absorbing a whole
// FinancialPattern genuinely orphans everything under its own now-gone
// FinanceId — its own EarMarkPattern chain (if any) and every ManualEarmark
// tied to it — since a pure domain function has no repository access to
// delete any of that itself. A caller with real data (FinancePatternSaveConfirmation)
// decides what to do with each entry in Absorbed once this returns.
public sealed record FinancialChainBoundaryResult
{
    public required FinancialPattern Current { get; init; }
    public required IReadOnlyList<FinancialPattern> Absorbed { get; init; }
    public FinancialPattern? AdjustedNeighbor { get; init; }
}
