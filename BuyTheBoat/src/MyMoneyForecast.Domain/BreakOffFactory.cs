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
            ?? AllocationPlanProposer.Propose(successor, request.AllPatterns, request.CutDate, request.SpreadEvenlyWithNoIncome);

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
            ?? AllocationPlanProposer.Propose(successor, request.AllPatterns, request.CutDate, request.SpreadEvenlyWithNoIncome);

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
                Start = request.RenewalDate,
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

        return result with { Successor = markedSuccessor };
    }

    /// <summary>[CALC] The three checks every BreakOff overload shares: a real cut boundary (at least one day of history to preserve), a genuinely new successor identity, and a successor schedule that starts exactly on the cut.</summary>
    /// <param name="predecessor">The pattern being cut.</param>
    /// <param name="cutDate">Where the cut lands.</param>
    /// <param name="successorFinanceId">The id the successor would take on.</param>
    /// <param name="successorSchedule">The successor's proposed schedule.</param>
    private static void ValidateCutBoundaries(FinancialPattern predecessor, DateOnly cutDate, int successorFinanceId, RecurrenceRuleOptions successorSchedule)
    {
        if (cutDate <= predecessor.DatePattern.Start)
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

        if (successorSchedule.Start != cutDate)
        {
            throw new ArgumentException(
                "The successor's schedule must start exactly on the cut date.",
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
            .OrderByDescending(candidate => candidate.DatePattern.Start)
            .First();

    /// <summary>[CALC] Finds the segment immediately BEFORE this one in a break-off/renewal chain — same Source-reuse lookup FindCurrentSegment uses, one direction: another pattern sharing this one's own Source whose own Until ends exactly the day before this one's own Start. Null when this pattern doesn't continue from an earlier one — either it's the first segment ever, or it was never part of a chain at all. Editing UIs use this to tell a user "this continues an existing pattern" without them having to remember the history themselves.</summary>
    /// <param name="pattern">The segment to find a predecessor for.</param>
    /// <param name="allPatterns">Every pattern to search — normally the same list already passed to BreakOff/Renew/FindCurrentSegment.</param>
    public static FinancialPattern? FindPredecessor(FinancialPattern pattern, IReadOnlyList<FinancialPattern> allPatterns) =>
        allPatterns.FirstOrDefault(candidate =>
            candidate.Source == pattern.Source &&
            candidate.FinanceId != pattern.FinanceId &&
            candidate.DatePattern.Until.AddDays(1) == pattern.DatePattern.Start);

    /// <summary>[CALC] Finds the segment immediately AFTER this one in a break-off/renewal chain — the mirror of FindPredecessor: another pattern sharing this one's own Source whose own Start begins exactly the day after this one's own Until. Null when this pattern hasn't since been continued by a later one — it's still the current segment (FindCurrentSegment would return it unchanged).</summary>
    /// <param name="pattern">The segment to find a successor for.</param>
    /// <param name="allPatterns">Every pattern to search — normally the same list already passed to BreakOff/Renew/FindCurrentSegment.</param>
    public static FinancialPattern? FindSuccessor(FinancialPattern pattern, IReadOnlyList<FinancialPattern> allPatterns) =>
        allPatterns.FirstOrDefault(candidate =>
            candidate.Source == pattern.Source &&
            candidate.FinanceId != pattern.FinanceId &&
            candidate.DatePattern.Start == pattern.DatePattern.Until.AddDays(1));
}
