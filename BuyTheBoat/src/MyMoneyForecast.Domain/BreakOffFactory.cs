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
public static class BreakOffFactory
{
    /// <summary>[CALC] Ends a bill or paycheck on a chosen date and hands it off to a new one that continues from there with its own amount/schedule — "Change starting on a date." For a bill, the old savings plan is wound down and the new one is freshly proposed, carrying over whatever was already saved.</summary>
    /// <param name="request">The pattern being changed, the cut date, the new amount/schedule it takes on, and the jar balance to carry across (read by the caller from the current forecast).</param>
    /// <returns>The now-bounded original pattern plus the new one that continues from the cut date, with its savings plan already set up.</returns>
    public static BreakOffResult BreakOff(BreakOffRequest request)
    {
        if (request.CutDate <= request.Predecessor.DatePattern.Start)
        {
            throw new ArgumentException(
                "The cut date must be after the pattern's own start — there has to be at least one day of history to preserve.",
                nameof(request));
        }

        if (request.SuccessorFinanceId == request.Predecessor.FinanceId)
        {
            throw new ArgumentException(
                "The successor needs its own FinanceId — a break-off is a genuinely new identity, not an edit.",
                nameof(request));
        }

        if (request.SuccessorSchedule.Start != request.CutDate)
        {
            throw new ArgumentException(
                "The successor's schedule must start exactly on the cut date.",
                nameof(request));
        }

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

        var proposal = AllocationPlanProposer.Propose(
            successor, request.AllPatterns, request.CutDate, request.SpreadEvenlyWithNoIncome);

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
}
