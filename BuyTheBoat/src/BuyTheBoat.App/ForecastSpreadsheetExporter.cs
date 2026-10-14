using ClosedXML.Excel;
using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

/// <summary>
/// Writes a readable spreadsheet of a computed forecast, for sharing or archiving; it never imports back in, unlike Export Data/Import Data, which copy the raw SQLite file.
/// With several accounts, it opens on an "All accounts" sheet, then has a day-by-day timeline sheet per account, then a Goals list.
/// </summary>
/// <remarks>
/// Target layout: since a spreadsheet can't select days like the app does, lay everything out per day: current and free amounts, every fund jar, and every transaction, with the full amount and due date for every expense type, using colors, clear headers, and grouped columns to show good versus bad.
/// It may one day offer several layouts.
/// </remarks>
public static class ForecastSpreadsheetExporter
{
    private const string MoneyFormat = "$#,##0.00";

    // Excel caps a sheet name at 31 characters.
    private const int MaxSheetNameLength = 31;

    /// <summary>[WRITES FILE] Saves the whole forecast, every account included, as an .xlsx workbook at the given path.</summary>
    public static void Export(ForecastResult forecast, string path)
    {
        using var workbook = new XLWorkbook();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Goals" };

        // A single account's household roll-up would just repeat its own sheet.
        if (forecast.Accounts.Count > 1)
        {
            usedNames.Add("All accounts");
            WriteHouseholdSheet(workbook.Worksheets.Add("All accounts"), forecast);
        }

        foreach (var account in forecast.Accounts)
        {
            WriteTimelineSheet(workbook.Worksheets.Add(UniqueSheetName(account.Name, usedNames)), forecast, account);
        }

        WriteGoalsSheet(workbook.Worksheets.Add("Goals"), forecast);

        workbook.SaveAs(path);
    }

    /// <summary>[CALC] Fills the "All accounts" sheet: each day's free and set-aside money summed across accounts, and which accounts are short.</summary>
    private static void WriteHouseholdSheet(IXLWorksheet sheet, ForecastResult forecast)
    {
        sheet.Cell(1, 1).Value = "Date";
        sheet.Cell(1, 2).Value = "Free Balance";
        sheet.Cell(1, 3).Value = "Set Aside";
        sheet.Cell(1, 4).Value = "Short Accounts";
        sheet.Row(1).Style.Font.Bold = true;

        var row = 2;
        foreach (var day in forecast.Household.Days)
        {
            WriteDate(sheet.Cell(row, 1), day.Date);
            WriteMoney(sheet.Cell(row, 2), day.Free, redWhenNegative: true);
            WriteMoney(sheet.Cell(row, 3), day.SetAside, redWhenNegative: false);
            sheet.Cell(row, 4).Value = string.Join(", ", day.ShortAccounts);
            row++;
        }

        sheet.Columns().AdjustToContents();
    }

    /// <summary>[CALC] Fills one account's sheet with its day-by-day balance, reserved jars, free balance and events.</summary>
    private static void WriteTimelineSheet(IXLWorksheet sheet, ForecastResult forecast, AccountForecast account)
    {
        sheet.Cell(1, 1).Value = "Date";
        sheet.Cell(1, 2).Value = "Expected Balance";
        sheet.Cell(1, 3).Value = "Reserved";
        sheet.Cell(1, 4).Value = "Free Balance";
        sheet.Cell(1, 5).Value = "Events";
        sheet.Row(1).Style.Font.Bold = true;

        var row = 2;
        foreach (var entry in forecast.GetTimeline(account))
        {
            WriteDate(sheet.Cell(row, 1), entry.Date);
            WriteMoney(sheet.Cell(row, 2), entry.Snapshot.ExpectedAmount ?? 0m, redWhenNegative: false);
            sheet.Cell(row, 3).Value = TimelineRow.FormatReserved(entry.Snapshot, forecast.JarLabels);
            WriteMoney(sheet.Cell(row, 4), entry.Snapshot.ExpectedFreeAmount ?? 0m, redWhenNegative: true);
            sheet.Cell(row, 5).Value = TimelineRow.FormatEvents(entry.Snapshot, forecast.JarLabels);
            row++;
        }

        sheet.Columns().AdjustToContents();
    }

    /// <summary>[CALC] Fills the Goals sheet: every goal with the account it belongs to, what it needs, and whether it's on track.</summary>
    private static void WriteGoalsSheet(IXLWorksheet sheet, ForecastResult forecast)
    {
        sheet.Cell(1, 1).Value = "Goal";
        sheet.Cell(1, 2).Value = "Account";
        sheet.Cell(1, 3).Value = "Due Date";
        sheet.Cell(1, 4).Value = "Amount Needed";
        sheet.Cell(1, 5).Value = "Allocated by Due Date";
        sheet.Cell(1, 6).Value = "Status";
        sheet.Row(1).Style.Font.Bold = true;

        var row = 2;
        foreach (var status in forecast.GoalShortfalls)
        {
            sheet.Cell(row, 1).Value = status.Label;
            sheet.Cell(row, 2).Value = AccountNameFor(forecast, status.FinanceId);
            WriteDate(sheet.Cell(row, 3), status.DueDate);
            WriteMoney(sheet.Cell(row, 4), status.AmountNeeded, redWhenNegative: false);
            WriteMoney(sheet.Cell(row, 5), status.AmountAllocatedByDueDate, redWhenNegative: false);
            sheet.Cell(row, 6).Value = GoalStatusRow.FormatStatus(status);
            row++;
        }

        sheet.Columns().AdjustToContents();
    }

    /// <summary>[CALC] Names the account a goal is filed under, or blank if no account's page holds it.</summary>
    private static string AccountNameFor(ForecastResult forecast, int financeId) =>
        forecast.Accounts.FirstOrDefault(account =>
            account.Page.FinancePatterns.Any(pattern => pattern.FinanceId == financeId))?.Name ?? string.Empty;

    /// <summary>[CALC] Writes a date cell in year-month-day form.</summary>
    private static void WriteDate(IXLCell cell, DateOnly date)
    {
        cell.Value = date.ToDateTime(TimeOnly.MinValue);
        cell.Style.DateFormat.Format = "yyyy-mm-dd";
    }

    /// <summary>[CALC] Writes a dollar cell, in red when asked and the amount is below zero.</summary>
    private static void WriteMoney(IXLCell cell, decimal amount, bool redWhenNegative)
    {
        cell.Value = amount;
        cell.Style.NumberFormat.Format = MoneyFormat;
        if (redWhenNegative && amount < 0m)
        {
            cell.Style.Font.FontColor = XLColor.Red;
        }
    }

    /// <summary>[CALC] Turns an account name into a sheet name Excel accepts, adding " (2)", " (3)"… when two accounts would otherwise share one.</summary>
    /// <param name="usedNames">Sheet names already taken; the returned name is added to it.</param>
    internal static string UniqueSheetName(string accountName, ISet<string> usedNames)
    {
        // Excel rejects these characters in a sheet name, and an apostrophe at either end.
        var cleaned = new string(accountName.Select(ch => ":\\/?*[]".Contains(ch) ? '-' : ch).ToArray()).Trim().Trim('\'');
        if (cleaned.Length == 0)
        {
            cleaned = "Account";
        }

        var name = Truncate(cleaned, MaxSheetNameLength);
        for (var copy = 2; usedNames.Contains(name); copy++)
        {
            var suffix = $" ({copy})";
            name = Truncate(cleaned, MaxSheetNameLength - suffix.Length) + suffix;
        }

        usedNames.Add(name);
        return name;
    }

    /// <summary>[CALC] Cuts text down to the given length.</summary>
    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];
}
