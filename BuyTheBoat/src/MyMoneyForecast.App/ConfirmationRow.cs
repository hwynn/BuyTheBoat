using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// A confirmation row: one question, or one announcement, that the
// save-confirmation popup may raise on a single save. The popup renders a
// list of these, top to bottom, most-vital first, and returns the user's
// choices — it never decides which rows exist, in what order, or that one
// answer might make another question relevant. A wrapper
// (FinancePatternSaveConfirmation for a bill/paycheck, EarmarkPatternSaveConfirmation
// for a savings plan) owns all of that and hands the popup a finished list.
//
// See planning/28-refactoring-the-save-confirmation.md for the full design:
// the four row kinds below, the two-layout front-end contract, and the staged
// migration this file is step 1 of. Nothing constructs these yet — the flat
// ImplicitChangeConfirmationRequest/Answer pair is still what runs. This file
// exists so the later steps have a settled vocabulary to build against.
public abstract record ConfirmationRow(string Id);

// A statement with no choice attached — a forced-consolidation notice, a
// source-change warning, a narrowing-limitation notice. The popup shows it
// only when Text is non-empty, matching how the flat DTO's own announcement
// strings ("" = hide) behave today.
public sealed record AnnouncementRow(string Id, string Text) : ConfirmationRow(Id);

// A question with a fixed set of options and a visibly pre-selected default.
// Question is the message shown above the options; DefaultIndex is the option
// that looks already-picked; Layout is how the options sit (see OptionLayout).
// Every ChoiceRow must have a real default — a question reaching the popup
// without one is a design gap to resolve with the author, not a runtime
// state to guess at.
public sealed record ChoiceRow(
    string Id,
    string Question,
    IReadOnlyList<ChoiceOption> Options,
    int DefaultIndex,
    OptionLayout Layout) : ConfirmationRow(Id);

// One option within a ChoiceRow. It can hold information, not just a label:
// Detail is the fuller explanation (used especially in the Stacked layout,
// where an option gets the popup's full width). Consequence is the
// warning/consequence footer shown under the options while THIS option is the
// selected one — "" for an option with no downside, so the footer escalates as
// the user picks a riskier choice while the default option stays safe.
public sealed record ChoiceOption(string Label, string Detail, string Consequence);

// Choose one of several labelled candidate plan shapes (planning/25 Item G) —
// each candidate carries its own preview, so this is its own kind rather than
// a plain ChoiceRow. DefaultIndex is the recommended candidate.
public sealed record CandidatePickerRow(
    string Id,
    string Question,
    IReadOnlyList<FinancePatternSaveConfirmation.PlanShapeCandidate> Candidates,
    int DefaultIndex) : ConfirmationRow(Id);

// A single checkbox that rides nested under a parent ChoiceRow rather than
// standing on its own — today only "also scale each surviving plan's amount,"
// shown under the consolidation question. ParentId is the ChoiceRow it sits
// beneath.
public sealed record CheckboxRiderRow(
    string Id,
    string ParentId,
    string Label,
    bool DefaultChecked) : ConfirmationRow(Id);

// How a ChoiceRow's options are arranged. SideBySide (the default) suits
// options that summarize in a few words; Stacked gives each option the popup's
// full width, for choices that need room to explain themselves — a suggestion
// reshaping a long-term savings plan, say.
public enum OptionLayout
{
    SideBySide,
    Stacked,
}

// The stable Id of every row FinancePatternSaveConfirmation.BuildRows can
// produce. Shared so the builder and the popup that reads choices back
// (EditingHistoryConfirmationWindow.OnSaveClick) can never drift on a literal
// — a mismatch there would map a selection to the wrong answer with no test
// catching it, since the whole suite bypasses the real popup (a delegate
// double stands in). The announcement Ids aren't read back (announcements have
// no answer), but are named here for one complete list.
public static class ConfirmationRowIds
{
    public const string AlterPast = "alter-past";
    public const string Consolidation = "consolidation";
    public const string ConsolidationCaveat = "consolidation-caveat";
    public const string ConsolidationForced = "consolidation-forced";
    public const string SourceChange = "source-change";
    public const string ChainBoundary = "chain-boundary";
    public const string Cascade = "cascade";
    public const string TrivialFieldsCascade = "trivial-fields-cascade";
    public const string PacedBillsCascade = "paced-bills-cascade";

    // One combine-or-keep-separate question per later finance pattern the edited
    // finance pattern's amount change is carried forward onto that's funded by
    // more than one earmark pattern (cross-boundary Q6). The id carries the later
    // finance pattern's finance_id, since there can be several at once —
    // deliberately its own row kind, never the break-off Consolidation above, so
    // the two never share behavior (the break-off folds into a new segment; this
    // folds a later finance pattern's earmark patterns in place, or scales them
    // if kept separate).
    public const string CrossBoundaryConsolidationPrefix = "cross-boundary-consolidation-";

    /// <summary>[CALC] The row id of the cross-boundary combine-or-keep-separate question for one later segment, by its finance_id.</summary>
    /// <param name="financeId">The later segment the question is about.</param>
    public static string CrossBoundaryConsolidation(int financeId) => CrossBoundaryConsolidationPrefix + financeId;
}

// The user's answer, as the raw selections the popup reports — one option
// index per ChoiceRow it drew, keyed by row Id, plus any picked plan shape and
// checkbox riders. The popup doesn't interpret these (it never knew what a row
// meant); the wrapper (FinancePatternSaveConfirmation.Run or
// EarmarkPatternSaveConfirmation.Run) reads each index back into a decision,
// each keying off the same ConfirmationRowIds. A row the popup never drew is simply absent
// from the map — the wrapper treats absent as that row's own safe default, so
// a headless caller (or a test) can return a bare Proceed and still get the
// settled defaults for every question.
public sealed record ConfirmationOutcome
{
    public required bool Proceed { get; init; }
    public IReadOnlyDictionary<string, int> ChosenOptionIndex { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, bool> Riders { get; init; } = new Dictionary<string, bool>();
    public EarMarkPattern? ChosenPlanShape { get; init; }
}
