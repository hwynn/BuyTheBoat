using System;
using System.Collections.Generic;

namespace MyMoneyForecast.App;

// The height-based pagination the confirmation popup's
// front-end contract calls for. Splitting is about HEIGHT, never logic: the
// dynamic reveal (a ChoiceOption's child rows) decides which questions apply
// WITHIN a page, so a top-level row and its whole subtree always stay together
// on one page. The pre-pass measures each row worst-case (every option's
// children counted as if revealed) so a page can't grow past the budget once the
// user starts expanding things. Pure and WPF-free on purpose, so it stays
// unit-testable in MyMoneyForecast.App.Tests, which can't render the real window.
internal static class ConfirmationPaginator
{
    /// <summary>[CALC] Splits the popup's rows into pages that each fit a height budget, keeping every row (with its dynamic-reveal children) whole on one page — MainWindow's popup driver uses this to decide whether one save needs more than one confirmation page. Returns a single page holding all the rows whenever they fit, so the common one-page save is unchanged; a lone row taller than the budget gets its own page rather than being split.</summary>
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

        // Greedy pack: begin a new page only once the current one holds something
        // AND the next row would push it past the budget — so a single over-tall
        // row still lands alone on its own page instead of being split or dropped.
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

        // The trailing page, and — for a description-only request with no rows —
        // one empty page, matching the single dialog it drew before pagination.
        if (current.Count > 0 || pages.Count == 0)
        {
            pages.Add(current);
        }

        return pages;
    }
}

// A deliberately rough pixel estimate of how tall a confirmation
// row draws in EditingHistoryConfirmationWindow (Width 440, default font), used
// only to decide page breaks. It never has to be exact: the window keeps a scroll
// fallback for a page that still overshoots, and "too tall to read comfortably"
// is itself a soft threshold. WPF-free so it stays testable next to the paginator.
internal static class ConfirmationRowHeights
{
    // Tuned to the popup's own layout: ~408px of text width inside the 440px
    // window's 16px side margins, the default ~12pt font's line box, and the 14px
    // gap every row carries below it.
    private const double ContentWidth = 408;
    private const double AvgCharWidth = 7;
    private const double LineHeight = 18;
    private const double RowBottomMargin = 14;
    private const double OptionLineHeight = 24; // a radio plus its ~4px gap
    private const double QuestionGap = 6;
    private const double FooterGap = 4;
    private const double ChildIndentGap = 8; // the indented child panel's own margins

    /// <summary>[CALC] The worst-case height one confirmation row draws at, in approximate pixels — the value ConfirmationPaginator.Paginate sums to place page breaks. "Worst-case" because a page must still fit once the user reveals a nested question, so every option's children are counted whether or not they'd currently be visible.</summary>
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

        // Each option adds its own line and its full child subtree (worst case =
        // every child revealed); the footer only ever shows one option's
        // consequence at a time, so that part is a max across options, not a sum.
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
