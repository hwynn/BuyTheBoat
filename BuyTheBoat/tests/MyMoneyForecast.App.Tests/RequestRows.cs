using System.Collections.Generic;
using System.Linq;

namespace MyMoneyForecast.App.Tests;

// Reads a confirmation request's rows the way a test used to read its flat
// trigger/warning fields — so a test can still ask "was the paced-bills
// question raised?" or "what warning sits under the let-it-break option?"
// now that those live inside the rows the popup would draw, not as their own
// request fields. An absent row (the question wasn't raised) reads as "" /
// false, exactly as the old empty-string / false field did.
internal static class RequestRows
{
    // True when the request drew a row with this Id at all — the row-model
    // stand-in for an old "…CanCascade" / "IsChangeCritical" trigger flag.
    public static bool HasRow(this ImplicitChangeConfirmationRequest request, string rowId) =>
        request.Rows.Flattened().Any(row => row.Id == rowId);

    // The text of the announcement row with this Id, or "" if none — the
    // stand-in for an old announcement/warning string field.
    public static string AnnouncementText(this ImplicitChangeConfirmationRequest request, string rowId) =>
        request.Rows.Flattened().OfType<AnnouncementRow>().FirstOrDefault(row => row.Id == rowId)?.Text ?? "";

    // The consequence text shown under one option of the choice row with this
    // Id, or "" if there's no such row — the stand-in for an old per-option
    // warning/description field (StayLinkedWarning = option 0, LetItBreakWarning
    // = option 1, CascadeDescription = either, and so on).
    public static string OptionConsequence(this ImplicitChangeConfirmationRequest request, string rowId, int optionIndex) =>
        request.Rows.Flattened().OfType<ChoiceRow>().FirstOrDefault(row => row.Id == rowId) is { } choice
            ? choice.Options[optionIndex].Consequence
            : "";

    // Every row, top-level and nested — a ChoiceOption can carry child rows
    // (dynamic reveal), so a question nested under an option is still found by
    // Id here, even though the popup shows it only under its parent option.
    private static IEnumerable<ConfirmationRow> Flattened(this IEnumerable<ConfirmationRow> rows)
    {
        foreach (var row in rows)
        {
            yield return row;
            if (row is ChoiceRow choice)
            {
                foreach (var nested in choice.Options.SelectMany(option => option.Children).Flattened())
                {
                    yield return nested;
                }
            }
        }
    }
}
