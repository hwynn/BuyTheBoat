using System.Text;

namespace BuyTheBoat.App;

// Turns clipboard text into rows of cells for a range paste into the Spreadsheet subtab.
public static class PasteParser
{
    /// <summary>
    /// [CALC] Splits pasted text into rows of cells, or returns null when it's meant for a single cell.
    /// Tab-separated text (what Excel and Google Sheets copy) always counts as a range.
    /// Several lines without tabs count as comma-separated only when every line has the same number of commas, so a column of "$1,200.00"-style amounts still lands one per row.
    /// Anything else (a single line, commas or not) goes into one cell.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>>? Parse(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // Spreadsheet apps end a copied range with one newline, which isn't an extra empty row.
        if (normalized.EndsWith('\n'))
        {
            normalized = normalized[..^1];
        }

        IReadOnlyList<IReadOnlyList<string>> grid;
        if (normalized.Contains('\t'))
        {
            grid = Split(normalized, '\t');
        }
        else if (normalized.Contains('\n'))
        {
            var lines = normalized.Split('\n');
            var commaCounts = lines.Select(line => line.Count(ch => ch == ',')).Distinct().ToList();
            grid = commaCounts is [> 0]
                ? Split(normalized, ',')
                : lines.Select(line => (IReadOnlyList<string>)[line]).ToList();
        }
        else
        {
            return null;
        }

        return grid.Count == 1 && grid[0].Count == 1 ? null : grid;
    }

    /// <summary>[CALC] Splits delimited text into rows of cells, honoring double-quoted cells (which may hold the delimiter, newlines, and "" for a literal quote).</summary>
    private static List<IReadOnlyList<string>> Split(string text, char delimiter)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    cell.Append(ch);
                }
            }
            else if (ch == '"' && cell.Length == 0)
            {
                inQuotes = true;
            }
            else if (ch == delimiter)
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (ch == '\n')
            {
                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(row);
                row = [];
            }
            else
            {
                cell.Append(ch);
            }
        }

        row.Add(cell.ToString());
        rows.Add(row);
        return rows;
    }
}
