using System.IO;
using BuyTheBoat.Domain;
using ClosedXML.Excel;
using Shouldly;

namespace BuyTheBoat.App.Tests;

// Covers the forecast spreadsheet export reading every account, not just the primary one.
public class ForecastSpreadsheetExporterTests
{
    private static readonly DateOnly AsOf = new(2026, 7, 23);
    private static readonly DateOnly ExpenseDay = new(2026, 8, 1);

    /// <summary>[CALC] Builds a single-occurrence expense with no savings plan, so it only lowers free funds on its day.</summary>
    private static FinancialPattern OneOffExpense(int id, decimal amount) =>
        FinancialPattern.Create(new FinancialPatternOptions
        {
            FinanceId = id,
            Source = "One-off",
            DatePattern = RecurrenceRule.Create(new RecurrenceRuleOptions
            {
                Frequency = RecurrenceFrequency.Yearly,
                DtStart = ExpenseDay,
                Count = 1,
            }),
            Amount = amount,
            Mandatory = false,
        });

    /// <summary>[CALC] Builds an account input holding the given balance and patterns.</summary>
    private static AccountForecastInput Account(int id, string name, decimal balance, params FinancialPattern[] patterns) =>
        new()
        {
            AccountId = id,
            Name = name,
            StartingBalance = balance,
            FinancialPatterns = patterns,
            EarMarkPatterns = [],
        };

    /// <summary>[CALC] Runs a forecast over the given accounts.</summary>
    private static ForecastResult Forecast(params AccountForecastInput[] accounts) =>
        TransactionLogBookFactory.CreateForecast(new ForecastOptions
        {
            FinancialPatterns = [],
            EarMarkPatterns = [],
            StartingBalance = 0m,
            AsOfDate = AsOf,
            HorizonEndDate = new DateOnly(2026, 12, 1),
            Accounts = accounts,
        });

    /// <summary>[WRITES FILE] Exports the forecast to a temp file and opens it back up for checking.</summary>
    private static XLWorkbook ExportAndOpen(ForecastResult forecast)
    {
        var path = Path.Combine(Path.GetTempPath(), $"forecast-export-test-{Guid.NewGuid():N}.xlsx");
        ForecastSpreadsheetExporter.Export(forecast, path);
        var bytes = File.ReadAllBytes(path);
        File.Delete(path);
        return new XLWorkbook(new MemoryStream(bytes));
    }

    [Fact]
    public void Several_accounts_get_a_household_sheet_then_one_sheet_each()
    {
        using var workbook = ExportAndOpen(Forecast(
            Account(1, "Checking", 1000m, OneOffExpense(10, -1500m)),
            Account(2, "Savings", 5000m)));

        workbook.Worksheets.Select(sheet => sheet.Name).ShouldBe(["All accounts", "Checking", "Savings", "Goals"]);
    }

    [Fact]
    public void Each_account_sheet_shows_that_accounts_own_balance()
    {
        using var workbook = ExportAndOpen(Forecast(
            Account(1, "Checking", 1000m),
            Account(2, "Savings", 5000m)));

        workbook.Worksheet("Checking").Cell(2, 2).GetValue<decimal>().ShouldBe(1000m);
        workbook.Worksheet("Savings").Cell(2, 2).GetValue<decimal>().ShouldBe(5000m);
    }

    [Fact]
    public void The_household_sheet_names_the_short_account_on_its_short_day()
    {
        using var workbook = ExportAndOpen(Forecast(
            Account(1, "Checking", 1000m, OneOffExpense(10, -1500m)),
            Account(2, "Savings", 5000m)));

        var sheet = workbook.Worksheet("All accounts");
        var shortRow = sheet.RowsUsed().Skip(1)
            .Single(row => DateOnly.FromDateTime(row.Cell(1).GetDateTime()) == ExpenseDay);
        shortRow.Cell(4).GetString().ShouldBe("Checking");
    }

    [Fact]
    public void A_single_account_has_no_household_sheet()
    {
        using var workbook = ExportAndOpen(Forecast(Account(1, "Primary", 1000m)));

        workbook.Worksheets.Select(sheet => sheet.Name).ShouldBe(["Primary", "Goals"]);
    }

    [Fact]
    public void Sheet_names_drop_characters_excel_rejects_and_stay_unique()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Goals" };

        ForecastSpreadsheetExporter.UniqueSheetName("Bills/Rent [joint]", used).ShouldBe("Bills-Rent -joint-");
        ForecastSpreadsheetExporter.UniqueSheetName("goals", used).ShouldBe("goals (2)");
        ForecastSpreadsheetExporter.UniqueSheetName(new string('x', 40), used).Length.ShouldBe(31);
        ForecastSpreadsheetExporter.UniqueSheetName(new string('x', 40), used).ShouldBe(new string('x', 27) + " (2)");
    }
}
