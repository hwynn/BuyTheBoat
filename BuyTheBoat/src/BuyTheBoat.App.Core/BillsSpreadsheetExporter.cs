using ClosedXML.Excel;

namespace BuyTheBoat.App;

// The Spreadsheet subtab's "Download as spreadsheet": every row as shown, drafts included, with a Status column saying which is which.
// Output-only, like ForecastSpreadsheetExporter — nothing reads this file back in.
public static class BillsSpreadsheetExporter
{
    /// <summary>[WRITES FILE] Saves the Spreadsheet subtab's rows to an .xlsx file: a Status column (Draft or Saved), then the same columns as on screen.</summary>
    public static void Export(SpreadsheetModel model, string path)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Bills & Paychecks");

        sheet.Cell(1, 1).Value = "Status";
        for (var c = 0; c < model.Columns.Count; c++)
        {
            sheet.Cell(1, c + 2).Value = model.Columns[c].Header;
        }

        sheet.Row(1).Style.Font.Bold = true;

        var rowNumber = 2;
        foreach (var row in model.Rows)
        {
            sheet.Cell(rowNumber, 1).Value = row.IsDraft ? "Draft" : "Saved";
            for (var c = 0; c < model.Columns.Count; c++)
            {
                // Written as text, not parsed: a draft cell may hold anything, and the file should match what's on screen.
                sheet.Cell(rowNumber, c + 2).Value = row.Cells[c].Text;
            }

            if (row.IsDraft)
            {
                sheet.Row(rowNumber).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF2FD");
            }

            rowNumber++;
        }

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }
}
