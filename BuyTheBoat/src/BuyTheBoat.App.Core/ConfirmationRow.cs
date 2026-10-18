using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

/// <summary>
/// One question or announcement the save-confirmation popup may show on a save.
/// The popup shows a list of these top to bottom, most important first, and returns the user's choices; it never decides which rows exist, their order, or that one answer makes another question relevant.
/// FinancePatternSaveConfirmation (bills and paychecks) or EarmarkPatternSaveConfirmation (savings plans) decides all that, builds the list with ConfirmationRowBuilder.BuildRows, and reads the choices back from ConfirmationOutcome; EditingHistoryConfirmationWindow shows it.
/// </summary>
public abstract record ConfirmationRow(string Id);

/// <summary>A statement with no choice, such as a forced-combine notice or a Source-change warning; shown only when Text isn't empty.</summary>
public sealed record AnnouncementRow(string Id, string Text) : ConfirmationRow(Id);

/// <summary>
/// A question with a fixed set of options and a visibly pre-selected DefaultIndex; Question shows above the options, and Layout sets how they sit.
/// Every ChoiceRow must have a real default; a question without one is a design gap to resolve, not something to guess at.
/// </summary>
public sealed record ChoiceRow(
    string Id,
    string Question,
    IReadOnlyList<ChoiceOption> Options,
    int DefaultIndex,
    OptionLayout Layout) : ConfirmationRow(Id);

/// <summary>
/// One option in a ChoiceRow: Detail is a fuller explanation, used mostly in the Stacked layout.
/// Consequence is the warning shown under the options while this option is selected; "" when there's no downside, so the default option stays safe.
/// </summary>
public sealed record ChoiceOption(string Label, string Detail, string Consequence)
{
    /// <summary>
    /// Rows shown indented under this option only while it's selected, so a follow-up question only appears when it matters; empty for a leaf option.
    /// The confirmation class builds the tree; the popup shows and hides rows, at most three levels deep.
    /// </summary>
    public IReadOnlyList<ConfirmationRow> Children { get; init; } = [];
}

/// <summary>A choice between labeled plan shapes, each with its own preview, so it's its own row kind; DefaultIndex is the recommended one.</summary>
public sealed record CandidatePickerRow(
    string Id,
    string Question,
    IReadOnlyList<FinancePatternSaveConfirmation.PlanShapeCandidate> Candidates,
    int DefaultIndex) : ConfirmationRow(Id);

/// <summary>
/// How a ChoiceRow's options are arranged: SideBySide (the default) for options that fit in a few words, or Stacked to give each the popup's full width.
/// Stacked suits choices that need explaining, such as a suggestion reshaping a long-term savings plan.
/// </summary>
public enum OptionLayout
{
    SideBySide,
    Stacked,
}

/// <summary>
/// The Id of every row ConfirmationRowBuilder.BuildRows can produce, shared so the builder and the popup reading choices back (EditingHistoryConfirmationWindow.OnSaveClick) always agree.
/// A mismatch would map a choice to the wrong answer without any test noticing, since tests bypass the real popup; announcement ids aren't read back but are listed for completeness.
/// </summary>
public static class ConfirmationRowIds
{
    public const string WarnAboutBreakOff = "warn-about-break-off";

    /// <summary>The successor plan shape (Recommended, same schedule, or same amount); its answer comes back as ConfirmationOutcome.ChosenPlanShape rather than an option index.</summary>
    public const string PlanShape = "plan-shape";
    public const string Consolidation = "consolidation";

    /// <summary>Nested under Consolidation's "keep them separate": offers to re-rate the kept-separate plans to meet the new amount.</summary>
    public const string KeepSeparateFunding = "keep-separate-funding";
    public const string ConsolidationForced = "consolidation-forced";
    public const string SourceChange = "source-change";

    /// <summary>Offers to pre-fill the plan's form with a correction that meets the edited goal.</summary>
    public const string GoalHealthSuggestion = "goal-health-suggestion";

    /// <summary>Nested under the overfunded "keep saving at this rate": skip the next contribution, skip a stretch, or don't skip.</summary>
    public const string GoalHealthSkip = "goal-health-skip";

    /// <summary>The "worth a look" warning about the plan.</summary>
    public const string ConcerningPlan = "concerning-plan";

    /// <summary>The "[bill] occurs N more times" notice when a boundary moved outward and grew a plan.</summary>
    public const string BoundaryExtension = "boundary-extension";
    public const string ChainBoundary = "chain-boundary";
    public const string Cascade = "cascade";

    /// <summary>For a rate change to a lone plan that's already saving: split it at today (option 0, the default, keeping what's set aside) or recalculate the whole plan; Earmark form only.</summary>
    public const string EarmarkRerate = "earmark-rerate";
    public const string TrivialFieldsCascade = "trivial-fields-cascade";
    public const string PacedBillsCascade = "paced-bills-cascade";

    /// <summary>
    /// The start of each cross-boundary combine-or-keep-separate row id: one per later segment the amount change cascades onto that several plans fund, ending in that segment's finance id.
    /// A separate row kind from the break-off's Consolidation: that one folds plans into a new segment, while this folds a later segment's plans in place or scales them.
    /// </summary>
    public const string CrossBoundaryConsolidationPrefix = "cross-boundary-consolidation-";

    /// <summary>[CALC] The row id of the cross-boundary combine-or-keep-separate question for one later segment, by its finance_id.</summary>
    /// <param name="financeId">The later segment the question is about.</param>
    public static string CrossBoundaryConsolidation(int financeId) => CrossBoundaryConsolidationPrefix + financeId;

    // The two consolidation questions, asked once per save and applied to every consolidation it makes: how the combined plan is sized, and how it's spread.
    public const string ConsolidationSizing = "consolidation-sizing";
    public const string ConsolidationSpread = "consolidation-spread";
}

/// <summary>
/// The user's answers as the popup reports them: the option index for each ChoiceRow it showed, keyed by row Id, plus any picked plan shape.
/// The confirmation class reads each index back into a decision; a row the popup didn't show is absent and reads as its safe default, so a bare Proceed gets the default for every question.
/// </summary>
public sealed record ConfirmationOutcome
{
    public required bool Proceed { get; init; }
    public IReadOnlyDictionary<string, int> ChosenOptionIndex { get; init; } = new Dictionary<string, int>();
    public EarMarkPattern? ChosenPlanShape { get; init; }
}
