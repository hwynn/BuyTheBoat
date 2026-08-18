namespace MyMoneyForecast.App;

// A confirmation row: one question, or one announcement, that the
// save-confirmation popup may raise on a single save. The popup renders a
// list of these, top to bottom, most-vital first, and returns the user's
// choices — it never decides which rows exist, in what order, or that one
// answer might make another question relevant. A wrapper
// (FinancePatternSaveConfirmation today; EarmarkPatternSaveConfirmation once
// split out) owns all of that and hands the popup a finished list.
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
