using BuyTheBoat.App;
using BuyTheBoat.Domain;
using BuyTheBoat.Persistence;
using Shouldly;

namespace BuyTheBoat.App.Tests;

// The Bills & Paychecks › Spreadsheet subtab's logic: column checks, tidying, suggestions, paste parsing and placement.
public class SpreadsheetTests
{
    private static readonly IReadOnlyList<string> AccountNames = ["Checking", "Savings"];

    private static SpreadsheetModel NewModel() => new(SpreadsheetColumns.Create(() => AccountNames));

    private static int Col(SpreadsheetModel model, string key) =>
        model.Columns.ToList().FindIndex(column => column.Key == key);

    private static SpreadsheetCell Cell(SpreadsheetModel model, SpreadsheetRow row, string key) => row.Cells[Col(model, key)];

    /// <summary>[CALC] A model holding the given drafts (Ids 1, 2, …) followed by one saved monthly water bill.</summary>
    private static SpreadsheetModel ModelWith(params DraftRow[] drafts)
    {
        var model = NewModel();
        var water = FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = 1,
            Source = "CITY WATER",
            Amount = -62m,
            Priority = 5,
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Monthly,
                DtStart = new DateOnly(2026, 1, 5),
                ByMonthDay = [5],
                Until = new DateOnly(2027, 1, 5),
            }),
        });
        model.Load(drafts, [(water, "Checking")]);
        return model;
    }

    /// <summary>[UI] Types text into a draft cell and leaves it, the way the grid commits an edit.</summary>
    private static void Type(SpreadsheetModel model, SpreadsheetRow row, string key, string text)
    {
        Cell(model, row, key).Text = text;
        model.CommitCell(row, Col(model, key));
    }

    // Tidying and checking --------------------------------------------------

    [Theory]
    [InlineData(nameof(DraftRow.Frequency), "monthly", "Monthly")]
    [InlineData(nameof(DraftRow.Frequency), "  WEEKLY ", "Weekly")]
    [InlineData(nameof(DraftRow.Account), "checking", "Checking")]
    [InlineData(nameof(DraftRow.Every), "02", "2")]
    [InlineData(nameof(DraftRow.Starts), "1/15/2026", "2026-01-15")]
    [InlineData(nameof(DraftRow.Priority), "05", "5")]
    public void An_exact_match_is_tidied_into_its_standard_spelling_when_the_cell_is_left(string key, string typed, string tidied)
    {
        var model = ModelWith(new DraftRow { Id = 1 });
        var row = model.Rows[0];

        Type(model, row, key, typed);

        Cell(model, row, key).Text.ShouldBe(tidied);
        Cell(model, row, key).HasProblem.ShouldBeFalse();
    }

    [Theory]
    [InlineData(nameof(DraftRow.Frequency), "bi-weekly", "Weekly")]
    [InlineData(nameof(DraftRow.Frequency), "monthy", "Monthly")]
    [InlineData(nameof(DraftRow.Frequency), "annually", "Yearly")]
    [InlineData(nameof(DraftRow.Account), "chk", "Checking")]
    [InlineData(nameof(DraftRow.Every), "2 weeks", "2")]
    [InlineData(nameof(DraftRow.Every), "every other", "2")]
    [InlineData(nameof(DraftRow.Amount), "about 140", "140")]
    [InlineData(nameof(DraftRow.Amount), "-15.49ish", "-15.49")]
    [InlineData(nameof(DraftRow.Priority), "high, 8", "8")]
    [InlineData(nameof(DraftRow.DaysOfMonth), "~15th?", "15")]
    public void A_near_match_stays_as_typed_and_red_with_a_suggestion(string key, string typed, string suggestion)
    {
        var model = ModelWith(new DraftRow { Id = 1 });
        var row = model.Rows[0];

        Type(model, row, key, typed);

        var cell = Cell(model, row, key);
        cell.Text.ShouldBe(typed);
        cell.Problem.ShouldNotBeNull().Suggestion.ShouldBe(suggestion);
        row.HasProblems.ShouldBeTrue();
    }

    [Fact]
    public void Text_that_matches_nothing_is_red_without_a_suggestion()
    {
        var model = ModelWith(new DraftRow { Id = 1 });
        var row = model.Rows[0];

        Type(model, row, nameof(DraftRow.Frequency), "whenever");

        Cell(model, row, nameof(DraftRow.Frequency)).Problem.ShouldNotBeNull().Suggestion.ShouldBeNull();
    }

    [Theory]
    [InlineData("-62")]
    [InlineData("$1,200.50")]
    [InlineData("(140)")]
    [InlineData("−15.49")]
    public void Common_amount_spellings_are_accepted(string amount)
    {
        CellParsing.ParseMoney(amount).ShouldNotBeNull();
    }

    [Fact]
    public void A_negative_amount_is_a_bill_and_a_positive_one_a_paycheck()
    {
        CellParsing.ParseMoney("(140)").ShouldBe(-140m);
        CellParsing.ParseMoney("−15.49").ShouldBe(-15.49m);
        CellParsing.ParseMoney("$2,150").ShouldBe(2150m);
    }

    [Fact]
    public void Days_of_week_are_red_unless_frequency_is_weekly_and_frequency_changes_recheck_them()
    {
        var model = ModelWith(new DraftRow { Id = 1, Frequency = "Monthly", DaysOfWeek = "1, 5" });
        var row = model.Rows[0];

        Cell(model, row, nameof(DraftRow.DaysOfWeek)).Problem.ShouldNotBeNull().Message.ShouldBe("Only used when Frequency is Weekly.");

        Type(model, row, nameof(DraftRow.Frequency), "weekly");

        Cell(model, row, nameof(DraftRow.DaysOfWeek)).HasProblem.ShouldBeFalse();
    }

    [Fact]
    public void Days_of_month_work_for_monthly_and_yearly_like_the_form()
    {
        var model = ModelWith(new DraftRow { Id = 1, Frequency = "Yearly", DaysOfMonth = "15" });
        Cell(model, model.Rows[0], nameof(DraftRow.DaysOfMonth)).HasProblem.ShouldBeFalse();

        Type(model, model.Rows[0], nameof(DraftRow.Frequency), "Weekly");
        Cell(model, model.Rows[0], nameof(DraftRow.DaysOfMonth)).HasProblem.ShouldBeTrue();
    }

    [Fact]
    public void Weekday_names_in_days_of_week_are_suggested_as_their_numbers()
    {
        var model = ModelWith(new DraftRow { Id = 1, Frequency = "Weekly", DaysOfWeek = "Mon & Fri" });

        Cell(model, model.Rows[0], nameof(DraftRow.DaysOfWeek)).Problem.ShouldNotBeNull().Suggestion.ShouldBe("1, 5");
    }

    [Fact]
    public void Ends_before_starts_is_red()
    {
        var model = ModelWith(new DraftRow { Id = 1, Starts = "2026-06-01", Ends = "2026-01-01" });

        Cell(model, model.Rows[0], nameof(DraftRow.Ends)).Problem.ShouldNotBeNull().Message.ShouldBe("Ends is before Starts.");
    }

    [Fact]
    public void Accepting_a_suggestion_replaces_the_text_and_clears_the_red_mark()
    {
        var model = ModelWith(new DraftRow { Id = 1, Frequency = "bi-weekly" });
        var row = model.Rows[0];

        model.AcceptSuggestion(row, Col(model, nameof(DraftRow.Frequency)));

        Cell(model, row, nameof(DraftRow.Frequency)).Text.ShouldBe("Weekly");
        row.HasProblems.ShouldBeFalse();
    }

    [Fact]
    public void A_red_pick_list_cell_lists_its_suggestion_first_and_marked()
    {
        var model = ModelWith(new DraftRow { Id = 1, Frequency = "yr" });

        var choices = Cell(model, model.Rows[0], nameof(DraftRow.Frequency)).Choices;
        choices[0].ShouldBe(new PickChoice("Yearly", true));
        choices.Count(choice => choice.Value == "Yearly").ShouldBe(1);
    }

    [Fact]
    public void Saved_rows_are_never_checked_and_read_like_drafts()
    {
        var model = ModelWith();
        var saved = model.Rows.Single();

        saved.IsDraft.ShouldBeFalse();
        saved.HasProblems.ShouldBeFalse();
        Cell(model, saved, nameof(DraftRow.Amount)).Text.ShouldBe("-62.00");
        Cell(model, saved, nameof(DraftRow.Frequency)).Text.ShouldBe("Monthly");
        Cell(model, saved, nameof(DraftRow.DaysOfMonth)).Text.ShouldBe("5");
        Cell(model, saved, nameof(DraftRow.Account)).Text.ShouldBe("Checking");
    }

    [Fact]
    public void Drafts_come_before_saved_rows_and_new_drafts_join_the_drafts()
    {
        var model = ModelWith(new DraftRow { Id = 1, Source = "Netflix" });

        model.AddDraft(2);

        model.Rows.Select(row => row.DraftId).ShouldBe([1, 2, null]);
    }

    [Fact]
    public void The_invalid_cell_counter_and_next_invalid_cell_skip_valid_and_saved_cells_and_wrap()
    {
        var model = ModelWith(
            new DraftRow { Id = 1, Amount = "lots" },
            new DraftRow { Id = 2, Frequency = "sometimes" });
        var amount = Col(model, nameof(DraftRow.Amount));
        var frequency = Col(model, nameof(DraftRow.Frequency));

        model.InvalidCellCount.ShouldBe(2);
        model.NextInvalidCell(-1, model.Columns.Count - 1).ShouldBe((0, amount));
        model.NextInvalidCell(0, amount).ShouldBe((1, frequency));
        model.NextInvalidCell(1, frequency).ShouldBe((0, amount));
    }

    // Opening in the form ----------------------------------------------------

    [Fact]
    public void A_draft_parses_into_form_values_with_blanks_left_unset()
    {
        var model = ModelWith(new DraftRow
        {
            Id = 1,
            Description = "Netflix",
            Account = "Savings",
            Amount = "-15.49",
            Frequency = "Weekly",
            Every = "2",
            DaysOfWeek = "1, 7",
            Starts = "2026-03-01",
            Notes = "card ends 4411",
        });

        var values = model.ToFormValues(model.Rows[0]);

        values.Description.ShouldBe("Netflix");
        values.AccountName.ShouldBe("Savings");
        values.Amount.ShouldBe(-15.49m);
        values.Frequency.ShouldBe(RecurrenceFrequency.Weekly);
        values.Interval.ShouldBe(2);
        values.ByDay.ShouldBe([DayOfWeek.Monday, DayOfWeek.Sunday]);
        values.Starts.ShouldBe(new DateOnly(2026, 3, 1));
        values.Ends.ShouldBeNull();
        values.Priority.ShouldBeNull();
        values.Source.ShouldBe("");
        values.Notes.ShouldBe("card ends 4411");
    }

    // Pasting ---------------------------------------------------------------

    [Fact]
    public void Tab_separated_text_from_a_spreadsheet_app_is_a_range()
    {
        PasteParser.Parse("Netflix\t-15.49\r\nGym\t-40\r\n").ShouldBe(
            [["Netflix", "-15.49"], ["Gym", "-40"]]);
    }

    [Fact]
    public void Quoted_cells_keep_their_delimiters_quotes_and_newlines()
    {
        PasteParser.Parse("\"a\tb\"\t\"say \"\"hi\"\"\"\n\"two\nlines\"\tx").ShouldBe(
            [["a\tb", "say \"hi\""], ["two\nlines", "x"]]);
    }

    [Fact]
    public void Lines_with_the_same_number_of_commas_are_comma_separated()
    {
        PasteParser.Parse("Netflix,-15.49\nGym,-40").ShouldBe([["Netflix", "-15.49"], ["Gym", "-40"]]);
    }

    [Fact]
    public void A_column_of_amounts_with_thousands_commas_lands_one_per_row()
    {
        PasteParser.Parse("$1,200.00\n$62.00").ShouldBe([["$1,200.00"], ["$62.00"]]);
    }

    [Theory]
    [InlineData("card ends 4411, renews ~15th?")]
    [InlineData("just one value\n")]
    [InlineData("1, 5")]
    public void Single_line_text_goes_into_one_cell(string text)
    {
        PasteParser.Parse(text).ShouldBeNull();
    }

    [Fact]
    public void A_range_paste_fills_from_the_current_cell_tidies_and_rechecks()
    {
        var model = ModelWith(new DraftRow { Id = 1 }, new DraftRow { Id = 2 });
        var amount = Col(model, nameof(DraftRow.Amount));

        var touched = model.Paste("-15.49\t\tmonthly\n-40\t3\tbi-weekly", amount, 0);

        touched.Select(row => row.DraftId).ShouldBe([1, 2]);
        Cell(model, model.Rows[0], nameof(DraftRow.Amount)).Text.ShouldBe("-15.49");
        Cell(model, model.Rows[0], nameof(DraftRow.Frequency)).Text.ShouldBe("Monthly");
        Cell(model, model.Rows[1], nameof(DraftRow.Priority)).Text.ShouldBe("3");
        Cell(model, model.Rows[1], nameof(DraftRow.Frequency)).HasProblem.ShouldBeTrue();
    }

    [Fact]
    public void A_range_paste_stops_at_the_saved_rows()
    {
        var model = ModelWith(new DraftRow { Id = 1 });

        var touched = model.Paste("Netflix\nGym\nRent", 0, 0);

        touched.Count.ShouldBe(1);
        model.Rows[0].Cells[0].Text.ShouldBe("Netflix");
        model.Rows[1].Cells[0].Text.ShouldBe("CITY WATER");
    }

    [Fact]
    public void A_range_paste_drops_columns_past_the_last_one()
    {
        var model = ModelWith(new DraftRow { Id = 1 });
        var notes = Col(model, nameof(DraftRow.Notes));

        model.Paste("a note\textra\tmore", notes, 0);

        model.Rows[0].Cells[notes].Text.ShouldBe("a note");
    }

    [Fact]
    public void A_single_cell_paste_goes_into_one_draft_cell_and_never_into_a_saved_row()
    {
        var model = ModelWith(new DraftRow { Id = 1 });
        var notes = Col(model, nameof(DraftRow.Notes));

        model.Paste("card ends 4411, renews ~15th?", notes, 0).Count.ShouldBe(1);
        model.Paste("overwrite?", notes, 1).ShouldBeEmpty();

        model.Rows[0].Cells[notes].Text.ShouldBe("card ends 4411, renews ~15th?");
        model.Rows[1].Cells[notes].Text.ShouldBe("");
    }

    [Fact]
    public void A_draft_stores_every_cell_as_its_current_text()
    {
        var model = ModelWith(new DraftRow { Id = 7, Source = "Gym", Amount = "forty" });

        model.ToDraftRow(model.Rows[0]).ShouldBe(new DraftRow { Id = 7, Source = "Gym", Amount = "forty" });
    }
}
