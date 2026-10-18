using System;
using System.Collections.Generic;

namespace BuyTheBoat.App;

/// <summary>
/// Splits the confirmation popup's rows into pages by height only; a row and its nested rows always stay on one page.
/// Each row is measured as if every nested question were shown, so a page can't outgrow its budget as the user expands things.
/// Free of WPF so BuyTheBoat.App.Tests can test it without the real window.
/// </summary>
internal static class ConfirmationPaginator
{
    /// <summary>
    /// [CALC] Splits the rows into pages that each fit a height budget, keeping every row whole with its nested rows; MainWindow uses this to decide whether a save needs several pages.
    /// Everything fits on one page when it can; a row taller than the budget gets a page of its own.
    /// </summary>
    /// <param name="rows">The full row set the wrapper built for this save, in display order.</param>
    /// <param name="maxPageHeight">The comfortable height budget for one page's rows, in the same approximate pixels estimateHeight returns.</param>
    /// <param name="estimateHeight">Worst-case height of one row with its children expanded — normally ConfirmationRowHeights.EstimateWorstCase; taken as a parameter so the split can be tested against exact heights.</param>
    public static IReadOnlyList<IReadOnlyList<ConfirmationRow>> Paginate(
        IReadOnlyList<ConfirmationRow> rows,
        double maxPageHeight,
        Func<ConfirmationRow, double> estimateHeight)
    {
        var pages = new List<IReadOnlyList<ConfirmationRow>>();
        var current = new List<ConfirmationRow>();
        var currentHeight = 0d;

        // Adds each row to the current page, starting a new page when it isn't empty and the row would push it past the budget, so an over-tall row gets a page of its own.
        foreach (var row in rows)
        {
            var rowHeight = estimateHeight(row);
            if (current.Count > 0 && currentHeight + rowHeight > maxPageHeight)
            {
                pages.Add(current);
                current = new List<ConfirmationRow>();
                currentHeight = 0d;
            }

            current.Add(row);
            currentHeight += rowHeight;
        }

        // Adds the last page, or one empty page when there are no rows, so a description-only request still shows one dialog.
        if (current.Count > 0 || pages.Count == 0)
        {
            pages.Add(current);
        }

        return pages;
    }
}

/// <summary>
/// A rough pixel estimate of how tall a row draws in EditingHistoryConfirmationWindow, used only for page breaks.
/// It needn't be exact, since the window scrolls a page that still runs over; free of WPF so it's testable.
/// </summary>
internal static class ConfirmationRowHeights
{
    // Matched to the popup's layout: ~408px of text inside the 440px window's margins, the default font's line height, and the 14px gap under each row.
    private const double ContentWidth = 408;
    private const double AvgCharWidth = 7;
    private const double LineHeight = 18;
    private const double RowBottomMargin = 14;
    private const double OptionLineHeight = 24; // a radio plus its ~4px gap
    private const double QuestionGap = 6;
    private const double FooterGap = 4;
    private const double ChildIndentGap = 8; // the indented child panel's own margins

    /// <summary>[CALC] A row's height in approximate pixels with every nested question shown, which Paginate adds up to place page breaks.</summary>
    /// <param name="row">The row to estimate.</param>
    public static double EstimateWorstCase(ConfirmationRow row) => row switch
    {
        AnnouncementRow announcement => AnnouncementHeight(announcement),
        ChoiceRow choice => ChoiceHeight(choice),
        CandidatePickerRow picker => PickerHeight(picker),
        _ => LineHeight + RowBottomMargin,
    };

    /// <summary>[CALC] An announcement's height — its wrapped text plus the row gap, or nothing at all for an empty announcement (which the popup doesn't draw).</summary>
    private static double AnnouncementHeight(AnnouncementRow row) =>
        string.IsNullOrEmpty(row.Text) ? 0 : WrappedTextHeight(row.Text) + RowBottomMargin;

    /// <summary>[CALC] A choice row's worst-case height: its question, every option's line, and all options' child rows counted as if revealed — plus the tallest single consequence footer, since only one option's footer ever shows at once.</summary>
    /// <param name="row">The choice row to estimate.</param>
    private static double ChoiceHeight(ChoiceRow row)
    {
        var height = WrappedTextHeight(row.Question) + QuestionGap + RowBottomMargin;
        var tallestConsequence = 0d;

        // Adds each option's line and all its nested rows; the footer shows one option's consequence at a time, so it counts the tallest one.
        foreach (var option in row.Options)
        {
            height += OptionLineHeight + WrappedExtraLines(option.Label);
            foreach (var child in option.Children)
            {
                height += EstimateWorstCase(child) + ChildIndentGap;
            }

            if (!string.IsNullOrEmpty(option.Consequence))
            {
                tallestConsequence = Math.Max(tallestConsequence, WrappedTextHeight(option.Consequence) + FooterGap);
            }
        }

        return height + tallestConsequence;
    }

    /// <summary>[CALC] A candidate picker's height — its question plus one line per candidate shape.</summary>
    private static double PickerHeight(CandidatePickerRow row) =>
        WrappedTextHeight(row.Question) + QuestionGap + (row.Candidates.Count * OptionLineHeight) + RowBottomMargin;

    /// <summary>[CALC] Approximate rendered height of a wrapped run of text at the popup's width — always at least one line.</summary>
    private static double WrappedTextHeight(string text) => Math.Max(1, LineCount(text)) * LineHeight;

    /// <summary>[CALC] The extra height a long option label adds beyond its single radio line once it wraps.</summary>
    private static double WrappedExtraLines(string label) => Math.Max(0, LineCount(label) - 1) * LineHeight;

    /// <summary>[CALC] Roughly how many wrapped lines a run of text takes at the popup's width.</summary>
    private static int LineCount(string text) => (int)Math.Ceiling(text.Length * AvgCharWidth / ContentWidth);
}
