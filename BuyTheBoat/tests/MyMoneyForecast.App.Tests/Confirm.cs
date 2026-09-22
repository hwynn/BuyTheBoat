using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App.Tests;

// Builds the ConfirmationOutcome a fake popup hands back, named in terms of the
// choices themselves — so a test double reads as
// Confirm.Proceed().ChoseToRepaceBills() rather than in raw row-id /
// option-index pairs. Each setter fills one ChoiceRow's index the way the real
// popup would; a choice left unset stays at that row's own safe default (the
// wrapper treats a row absent from the outcome as its default), so
// Confirm.Proceed() alone means "accept every default."
internal static class Confirm
{
    public static ConfirmationOutcome Proceed() => new() { Proceed = true };
    public static ConfirmationOutcome Cancel() => new() { Proceed = false };

    // paced-bills cascade — [0] update them (also the popup's own pre-selection),
    // [1] leave them.
    public static ConfirmationOutcome ChoseToRepaceBills(this ConfirmationOutcome o) => o.At(ConfirmationRowIds.PacedBillsCascade, 0);
    public static ConfirmationOutcome ChoseToLeavePacedBills(this ConfirmationOutcome o) => o.At(ConfirmationRowIds.PacedBillsCascade, 1);

    // consolidation — [0] keep separate (default), [1] combine. On a break-off
    // that isn't forcing consolidation, this decides whether the surviving plans
    // fold into one successor or each keep their own.
    public static ConfirmationOutcome ChoseConsolidation(this ConfirmationOutcome o) => o.At(ConfirmationRowIds.Consolidation, 1);

    // keep-separate funding — [0] adjust to meet the goal (the popup's pre-selection),
    // [1] leave as-is. Nested under "keep them separate"; only read on a keep-separate
    // break-off whose plans would over/underfund the new amount.
    public static ConfirmationOutcome ChoseToAdjustKeptSeparatePlans(this ConfirmationOutcome o) => o.At(ConfirmationRowIds.KeepSeparateFunding, 0);
    public static ConfirmationOutcome ChoseToLeaveKeptSeparatePlansAsIs(this ConfirmationOutcome o) => o.At(ConfirmationRowIds.KeepSeparateFunding, 1);

    // goal-health suggestion — [0] load the suggested correction (the popup's
    // pre-selection), [1] leave the plan as-is.
    public static ConfirmationOutcome AcceptedGoalHealthSuggestion(this ConfirmationOutcome o) => o.At(ConfirmationRowIds.GoalHealthSuggestion, 0);
    public static ConfirmationOutcome DeclinedGoalHealthSuggestion(this ConfirmationOutcome o) => o.At(ConfirmationRowIds.GoalHealthSuggestion, 1);

    // chain-boundary — [0] stay linked, [1] let it break.
    public static ConfirmationOutcome ChoseToLetChainBreak(this ConfirmationOutcome o) => o.At(ConfirmationRowIds.ChainBoundary, 1);

    // cascade — [0] apply forward, [1] only this segment.
    public static ConfirmationOutcome ChoseJustThisSegment(this ConfirmationOutcome o) => o.At(ConfirmationRowIds.Cascade, 1);

    // trivial-fields cascade — [0] only this segment, [1] apply forward.
    public static ConfirmationOutcome ChoseCascadeTrivialFields(this ConfirmationOutcome o) => o.At(ConfirmationRowIds.TrivialFieldsCascade, 1);

    // earmark-rerate — [0] break off / split at today (the popup's pre-selection,
    // keeps what's already set aside), [1] recalculate the whole plan (re-rate).
    public static ConfirmationOutcome ChoseToBreakOffRerate(this ConfirmationOutcome o) => o.At(ConfirmationRowIds.EarmarkRerate, 0);
    public static ConfirmationOutcome ChoseToRecalculateWholePlan(this ConfirmationOutcome o) => o.At(ConfirmationRowIds.EarmarkRerate, 1);

    public static ConfirmationOutcome WithPlanShape(this ConfirmationOutcome o, EarMarkPattern plan) => o with { ChosenPlanShape = plan };

    // The plan-shape candidates offered on a request, read out of its one
    // CandidatePickerRow — empty when no picker was shown. The row is the single
    // source; there is no separate candidates field.
    public static IReadOnlyList<FinancePatternSaveConfirmation.PlanShapeCandidate> PlanShapeCandidates(this ImplicitChangeConfirmationRequest request) =>
        request.Rows.OfType<CandidatePickerRow>().SingleOrDefault()?.Candidates ?? [];

    private static ConfirmationOutcome At(this ConfirmationOutcome o, string rowId, int index) =>
        o with { ChosenOptionIndex = new Dictionary<string, int>(o.ChosenOptionIndex) { [rowId] = index } };
}
