using ClosedXML.Excel;
using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

/// <summary>
/// Writes a readable spreadsheet of a computed forecast, for sharing or archiving; it never imports back in, unlike Export Data/Import Data, which copy the raw SQLite file.
/// It has two sheets: a day-by-day Timeline and a Goals list.
/// </summary>
/// <remarks>
/// Target layout: since a spreadsheet can't select days like the app does, lay everything out per day: current and free amounts, every fund jar, and every transaction, with the full amount and due date for every expense type, using colors, clear headers, and grouped columns to show good versus bad.
/// It may one day offer several layouts.
/// </remarks>
public static class ForecastSpreadsheetExporter
{
    public static void Export(ForecastResult forecast, string path)
    {
        using var workbook = new XLWorkbook();

        WriteTimelineSheet(workbook.Worksheets.Add("Timeline"), forecast);
        WriteGoalsSheet(workbook.Worksheets.Add("Goals"), forecast);

        workbook.SaveAs(path);
    }

    private static void WriteTimelineSheet(IXLWorksheet sheet, ForecastResult forecast)
    {
        sheet.Cell(1, 1).Value = "Date";
        sheet.Cell(1, 2).Value = "Expected Balance";
        sheet.Cell(1, 3).Value = "Reserved";
        sheet.Cell(1, 4).Value = "Free Balance";
        sheet.Cell(1, 5).Value = "Events";
        sheet.Row(1).Style.Font.Bold = true;

        var row = 2;
        foreach (var entry in forecast.GetTimeline())
        {
            sheet.Cell(row, 1).Value = entry.Date.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 1).Style.DateFormat.Format = "yyyy-mm-dd";
            sheet.Cell(row, 2).Value = entry.Snapshot.ExpectedAmount ?? 0m;
            sheet.Cell(row, 2).Style.NumberFormat.Format = "$#,##0.00";
            sheet.Cell(row, 3).Value = TimelineRow.FormatReserved(entry.Snapshot, forecast.JarLabels);
            sheet.Cell(row, 4).Value = entry.Snapshot.ExpectedFreeAmount ?? 0m;
            sheet.Cell(row, 4).Style.NumberFormat.Format = "$#,##0.00";
            sheet.Cell(row, 5).Value = TimelineRow.FormatEvents(entry.Snapshot, forecast.JarLabels);
            row++;
        }

        sheet.Columns().AdjustToContents();
    }

    private static void WriteGoalsSheet(IXLWorksheet sheet, ForecastResult forecast)
    {
        sheet.Cell(1, 1).Value = "Goal";
        sheet.Cell(1, 2).Value = "Due Date";
        sheet.Cell(1, 3).Value = "Amount Needed";
        sheet.Cell(1, 4).Value = "Allocated by Due Date";
        sheet.Cell(1, 5).Value = "Status";
        sheet.Row(1).Style.Font.Bold = true;

        var row = 2;
        foreach (var status in forecast.GoalShortfalls)
        {
            sheet.Cell(row, 1).Value = status.Label;
            sheet.Cell(row, 2).Value = status.DueDate.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 2).Style.DateFormat.Format = "yyyy-mm-dd";
            sheet.Cell(row, 3).Value = status.AmountNeeded;
            sheet.Cell(row, 3).Style.NumberFormat.Format = "$#,##0.00";
            sheet.Cell(row, 4).Value = status.AmountAllocatedByDueDate;
            sheet.Cell(row, 4).Style.NumberFormat.Format = "$#,##0.00";
            sheet.Cell(row, 5).Value = GoalStatusRow.FormatStatus(status);
            row++;
        }

        sheet.Columns().AdjustToContents();
    }
}
