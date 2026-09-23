using System.Collections.Generic;
using System.Linq;
using MyMoneyForecast.App;
using Shouldly;

namespace MyMoneyForecast.App.Tests;

// The height-based split and its row-height estimate. The
// real popup can't be rendered by this suite, so these cover the pure decision
// logic: which rows land on which page, and that the estimate is worst-case.
public class ConfirmationPaginatorTests
{
    private static AnnouncementRow Row(string id) => new(id, id);

    [Fact]
    public void Rows_that_fit_the_budget_stay_on_one_page()
    {
        var rows = new ConfirmationRow[] { Row("a"), Row("b"), Row("c") };

        var pages = ConfirmationPaginator.Paginate(rows, maxPageHeight: 1000, _ => 100);

        pages.Count.ShouldBe(1);
        pages[0].Select(r => r.Id).ShouldBe(new[] { "a", "b", "c" });
    }

    [Fact]
    public void Rows_over_the_budget_split_onto_further_pages_in_order()
    {
        var rows = new ConfirmationRow[] { Row("a"), Row("b"), Row("c"), Row("d") };

        // 100 + 100 fits 250; a third 100 would overflow, so the page breaks.
        var pages = ConfirmationPaginator.Paginate(rows, maxPageHeight: 250, _ => 100);

        pages.Count.ShouldBe(2);
        pages[0].Select(r => r.Id).ShouldBe(new[] { "a", "b" });
        pages[1].Select(r => r.Id).ShouldBe(new[] { "c", "d" });
    }

    [Fact]
    public void Every_row_appears_exactly_once_across_the_pages_in_original_order()
    {
        var rows = new ConfirmationRow[] { Row("a"), Row("b"), Row("c"), Row("d"), Row("e") };

        var pages = ConfirmationPaginator.Paginate(rows, maxPageHeight: 150, _ => 100);

        pages.SelectMany(page => page).Select(r => r.Id).ShouldBe(new[] { "a", "b", "c", "d", "e" });
    }

    [Fact]
    public void A_single_row_taller_than_the_budget_gets_its_own_page_rather_than_being_split()
    {
        var rows = new ConfirmationRow[] { Row("small"), Row("huge"), Row("small2") };

        var pages = ConfirmationPaginator.Paginate(
            rows, maxPageHeight: 200, row => row.Id == "huge" ? 500 : 100);

        pages.Count.ShouldBe(3);
        pages[0].Single().Id.ShouldBe("small");
        pages[1].Single().Id.ShouldBe("huge");
        pages[2].Single().Id.ShouldBe("small2");
    }

    [Fact]
    public void An_empty_request_still_yields_one_empty_page()
    {
        var pages = ConfirmationPaginator.Paginate(new List<ConfirmationRow>(), maxPageHeight: 500, _ => 100);

        pages.Count.ShouldBe(1);
        pages[0].Count.ShouldBe(0);
    }

    [Fact]
    public void An_empty_announcement_contributes_no_height()
    {
        ConfirmationRowHeights.EstimateWorstCase(new AnnouncementRow("x", "")).ShouldBe(0);
    }

    [Fact]
    public void Worst_case_counts_an_options_children_even_though_only_one_shows_at_a_time()
    {
        var options = new[] { new ChoiceOption("Yes", "", ""), new ChoiceOption("No", "", "") };
        var noChildren = new ChoiceRow("leaf", "Pick one", options, 0, OptionLayout.Stacked);

        var child = new AnnouncementRow("child", "a follow-up question that appears only when Yes is chosen");
        var withChild = new ChoiceRow(
            "parent",
            "Pick one",
            new[]
            {
                new ChoiceOption("Yes", "", "") { Children = new ConfirmationRow[] { child } },
                new ChoiceOption("No", "", ""),
            },
            0,
            OptionLayout.Stacked);

        ConfirmationRowHeights.EstimateWorstCase(withChild)
            .ShouldBeGreaterThan(ConfirmationRowHeights.EstimateWorstCase(noChildren));
    }

    [Fact]
    public void Longer_text_estimates_taller()
    {
        var shortRow = new AnnouncementRow("s", "Short.");
        var longRow = new AnnouncementRow("l", new string('x', 400));

        ConfirmationRowHeights.EstimateWorstCase(longRow)
            .ShouldBeGreaterThan(ConfirmationRowHeights.EstimateWorstCase(shortRow));
    }
}
