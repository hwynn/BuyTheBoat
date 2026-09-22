using ClosedXML.Excel;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// Output-only: a human-readable snapshot of an already-computed forecast,
// for sharing/archiving outside the app. Distinct from MainWindow's Export
// Data/Import Data (which copy the raw SQLite file byte-for-byte for
// backup/transfer) — this never round-trips back in.
//
// Because the export can't offer dynamic day-selection, it has to lay ALL the detail
// out statically: per day, the current/free amounts, a breakdown of every fund
// jar, and every transaction — with the full amount due + due date shown for
// EVERY expense type (unlike the tailored on-screen views), and colors/clear
// headers/grouped columns doing the work of communicating good-vs-bad at a
// glance. (Today it's a two-sheet mirror of the on-screen grids; a richer
// layout is the target it should grow toward, and one day may offer multiple layouts.)
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
