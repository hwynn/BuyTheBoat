using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using BuyTheBoat.Domain;
using BuyTheBoat.Persistence;

namespace BuyTheBoat.App;

// One entry in a PickList cell's dropdown. The suggested fix for a red cell is listed first and highlighted.
public sealed record PickChoice(string Value, bool IsSuggested);

// One cell of the Spreadsheet subtab: its text exactly as typed, and what's wrong with it, if anything.
public sealed class SpreadsheetCell : INotifyPropertyChanged
{
    private string _text = "";
    private CellProblem? _problem;
    private IReadOnlyList<PickChoice> _choices = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Text
    {
        get => _text;
        set
        {
            if (value == _text)
            {
                return;
            }

            _text = value;
            Raise(nameof(Text));
        }
    }

    public CellProblem? Problem
    {
        get => _problem;
        set
        {
            if (value == _problem)
            {
                return;
            }

            _problem = value;
            Raise(nameof(Problem));
            Raise(nameof(HasProblem));
            Raise(nameof(ProblemTip));
        }
    }

    public bool HasProblem => _problem is not null;
    public string? ProblemTip => _problem?.Tip;

    public IReadOnlyList<PickChoice> Choices
    {
        get => _choices;
        set
        {
            _choices = value;
            Raise(nameof(Choices));
        }
    }

    /// <summary>[UI] Tells the grid this cell's text, problem or dropdown changed.</summary>
    private void Raise(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

// One row of the Spreadsheet subtab: a draft (editable, checkable) or a saved bill/paycheck (read-only, no checkbox).
public sealed class SpreadsheetRow : INotifyPropertyChanged
{
    private bool _isChecked;
    private bool _hasProblems;

    public SpreadsheetRow(int? draftId, IEnumerable<string> texts)
    {
        DraftId = draftId;
        Cells = texts.Select(text => new SpreadsheetCell { Text = text }).ToList();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Null for a saved row.
    public int? DraftId { get; }
    public bool IsDraft => DraftId is not null;
    public IReadOnlyList<SpreadsheetCell> Cells { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (value == _isChecked)
            {
                return;
            }

            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public bool HasProblems
    {
        get => _hasProblems;
        set
        {
            if (value == _hasProblems)
            {
                return;
            }

            _hasProblems = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasProblems)));
        }
    }
}

// What a draft row puts into the Form when opened: each cell already parsed, null (or empty) where the cell was blank.
public sealed record DraftFormValues
{
    public string Source { get; init; } = "";
    public string Description { get; init; } = "";
    public string? AccountName { get; init; }
    public decimal? Amount { get; init; }
    public int? Priority { get; init; }
    public RecurrenceFrequency? Frequency { get; init; }
    public int? Interval { get; init; }
    public IReadOnlyList<DayOfWeek> ByDay { get; init; } = [];
    public IReadOnlyList<int> ByMonthDay { get; init; } = [];
    public DateOnly? Starts { get; init; }
    public DateOnly? Ends { get; init; }

    // True when it has no end date: answers the Form's "When does this stop?" with "It just keeps going".
    public bool KeepsGoing { get; init; }

    // True when Frequency is Once: the Form opens as "One time", with Starts as the due date.
    public bool IsOneTime { get; init; }
    public string Notes { get; init; } = "";
}

// Everything the Spreadsheet subtab knows, minus the UI: its columns, its rows (drafts first, then saved),
// checking and tidying cells, and pasting. SpreadsheetPanel draws it; MainWindow stores the drafts.
public sealed class SpreadsheetModel(IReadOnlyList<ISpreadsheetColumn> columns)
{
    public IReadOnlyList<ISpreadsheetColumn> Columns { get; } = columns;
    public ObservableCollection<SpreadsheetRow> Rows { get; } = [];

    /// <summary>[UI] Replaces every row: drafts first so they don't get forgotten, then the saved bills and paychecks. Draft checkmarks survive by Id.</summary>
    /// <param name="saved">Each saved bill/paycheck with the name of the account it's filed under.</param>
    public void Load(IReadOnlyList<DraftRow> drafts, IReadOnlyList<(FinancialPattern Pattern, string AccountName)> saved)
    {
        var checkedIds = Rows.Where(row => row.IsChecked).Select(row => row.DraftId).ToHashSet();
        Rows.Clear();

        foreach (var draft in drafts)
        {
            var row = new SpreadsheetRow(draft.Id, Columns.Select(column => TextOf(draft, column.Key)))
            {
                IsChecked = checkedIds.Contains(draft.Id),
            };
            Revalidate(row);
            Rows.Add(row);
        }

        foreach (var (pattern, accountName) in saved)
        {
            Rows.Add(new SpreadsheetRow(null, Columns.Select(column => SavedText(pattern, accountName, column.Key))));
        }
    }

    /// <summary>[UI] Adds a blank draft row at the bottom of the drafts section and returns it.</summary>
    public SpreadsheetRow AddDraft(int id)
    {
        var row = new SpreadsheetRow(id, Columns.Select(_ => ""));
        Revalidate(row);
        Rows.Insert(Rows.Count(existing => existing.IsDraft), row);
        return row;
    }

    /// <summary>[UI] Takes a draft row out of the grid.</summary>
    public void Remove(SpreadsheetRow row) => Rows.Remove(row);

    /// <summary>[UI] Runs when the user leaves an edited cell: tidies an exact match into its standard spelling, then re-checks the whole row (Frequency changes what the day columns accept).</summary>
    public void CommitCell(SpreadsheetRow row, int column)
    {
        if (!row.IsDraft)
        {
            return;
        }

        var cell = row.Cells[column];
        cell.Text = Columns[column].Tidy(cell.Text);
        Revalidate(row);
    }

    /// <summary>[UI] Replaces a draft cell's text with the given fix, then tidies and re-checks the row as if the user had typed it.</summary>
    public void ReplaceText(SpreadsheetRow row, int column, string text)
    {
        row.Cells[column].Text = text;
        CommitCell(row, column);
    }

    /// <summary>[CALC] Every red draft cell, reading left to right then top to bottom.</summary>
    public IReadOnlyList<(SpreadsheetRow Row, int Column)> InvalidCells() =>
        Rows.Where(row => row.IsDraft)
            .SelectMany(row => row.Cells.Select((cell, column) => (Row: row, Column: column, cell.HasProblem)))
            .Where(entry => entry.HasProblem)
            .Select(entry => (entry.Row, entry.Column))
            .ToList();

    /// <summary>[UI] Replaces a red cell's text with its suggested fix. Does nothing when the cell has no suggestion.</summary>
    public void AcceptSuggestion(SpreadsheetRow row, int column)
    {
        if (row.Cells[column].Problem?.Suggestion is not { } suggestion)
        {
            return;
        }

        row.Cells[column].Text = suggestion;
        CommitCell(row, column);
    }

    /// <summary>[UI] Re-checks every cell in a draft row, refreshing its red marks, hover text, and dropdown order. Saved rows are never checked.</summary>
    public void Revalidate(SpreadsheetRow row)
    {
        if (!row.IsDraft)
        {
            return;
        }

        string OtherCell(string key) => Columns.Select((column, index) => (column, index))
            .Where(pair => pair.column.Key == key)
            .Select(pair => row.Cells[pair.index].Text)
            .FirstOrDefault() ?? "";

        for (var i = 0; i < Columns.Count; i++)
        {
            var cell = row.Cells[i];
            cell.Problem = Columns[i].Check(cell.Text, OtherCell);

            if (Columns[i].Editor == CellEditorKind.PickList)
            {
                var suggestion = cell.Problem?.Suggestion;
                var rest = Columns[i].Choices.Where(choice => choice != suggestion).Select(choice => new PickChoice(choice, false));
                cell.Choices = suggestion is null ? rest.ToList() : [new PickChoice(suggestion, true), .. rest];
            }
        }

        row.HasProblems = row.Cells.Any(cell => cell.HasProblem);
    }

    /// <summary>[UI] Re-checks every draft — after the account list changes, an account name that used to be valid may not be anymore.</summary>
    public void RevalidateAll()
    {
        foreach (var row in Rows.Where(row => row.IsDraft))
        {
            Revalidate(row);
        }
    }

    /// <summary>
    /// [UI] The paste entry point: decides whether the clipboard text is a range or a single cell (PasteParser), and pastes it starting at the given cell.
    /// For a range, first adds however many blank drafts the pasted rows need beyond the drafts already below the start, then fills the range.
    /// Starting on a saved row pastes into the new drafts instead. Columns past the last one are dropped.
    /// Returns the draft rows that changed or were added, so they can be saved.
    /// </summary>
    /// <param name="allocateFirstDraftId">Gives the Id for the first added draft; any further ones take the Ids after it.</param>
    public IReadOnlyList<SpreadsheetRow> Paste(string text, int column, int row, Func<int> allocateFirstDraftId)
    {
        if (PasteParser.Parse(text) is not { } grid)
        {
            return PasteIntoCell(text, column, row);
        }

        var startRow = row >= 0 && row < Rows.Count && Rows[row].IsDraft ? row : DraftCount;
        var added = AddBlankDrafts(grid.Count - DraftRowsFrom(startRow), allocateFirstDraftId);
        return PasteRange(grid, column, startRow).Union(added).ToList();
    }

    /// <summary>[CALC] How many draft rows there are.</summary>
    private int DraftCount => Rows.Count(existing => existing.IsDraft);

    /// <summary>[CALC] How many draft rows a paste starting at this row can fill before reaching the saved rows.</summary>
    private int DraftRowsFrom(int row) => Math.Max(0, DraftCount - row);

    /// <summary>[UI] Adds the given number of blank drafts at the bottom of the drafts section, with consecutive Ids. Adds nothing for zero or less.</summary>
    private IReadOnlyList<SpreadsheetRow> AddBlankDrafts(int count, Func<int> allocateFirstDraftId)
    {
        if (count <= 0)
        {
            return [];
        }

        var firstId = allocateFirstDraftId();
        return Enumerable.Range(0, count).Select(offset => AddDraft(firstId + offset)).ToList();
    }

    /// <summary>[UI] Puts the whole text into one draft cell. A saved row's cell takes nothing.</summary>
    private IReadOnlyList<SpreadsheetRow> PasteIntoCell(string text, int column, int row)
    {
        if (row < 0 || row >= Rows.Count || !Rows[row].IsDraft || column < 0 || column >= Columns.Count)
        {
            return [];
        }

        Rows[row].Cells[column].Text = text.TrimEnd('\r', '\n');
        CommitCell(Rows[row], column);
        return [Rows[row]];
    }

    /// <summary>[UI] Lays a grid of pasted cells over the drafts, top-left at the given cell, then tidies and re-checks every cell it touched. Paste adds any drafts the grid needs before calling this.</summary>
    private IReadOnlyList<SpreadsheetRow> PasteRange(IReadOnlyList<IReadOnlyList<string>> grid, int column, int row)
    {
        var touched = new List<SpreadsheetRow>();
        for (var r = 0; r < grid.Count; r++)
        {
            var rowIndex = row + r;

            // Load-bearing: a range paste never writes into a saved row — saved rows only change through the Form.
            if (rowIndex >= Rows.Count || !Rows[rowIndex].IsDraft)
            {
                break;
            }

            var target = Rows[rowIndex];
            for (var c = 0; c < grid[r].Count && column + c < Columns.Count; c++)
            {
                target.Cells[column + c].Text = Columns[column + c].Tidy(grid[r][c]);
            }

            Revalidate(target);
            touched.Add(target);
        }

        return touched;
    }

    /// <summary>[CALC] How many draft cells are currently red — the toolbar counter.</summary>
    public int InvalidCellCount => Rows.Where(row => row.IsDraft).Sum(row => row.Cells.Count(cell => cell.HasProblem));

    /// <summary>[CALC] The next red cell after the given one, reading left to right then top to bottom and wrapping around to the top. Null when nothing is red.</summary>
    public (int Row, int Column)? NextInvalidCell(int afterRow, int afterColumn)
    {
        var total = Rows.Count * Columns.Count;
        var start = Math.Max(-1, afterRow * Columns.Count + afterColumn);
        for (var step = 1; step <= total; step++)
        {
            var position = (start + step) % total;
            var (rowIndex, columnIndex) = (position / Columns.Count, position % Columns.Count);
            if (Rows[rowIndex].IsDraft && Rows[rowIndex].Cells[columnIndex].HasProblem)
            {
                return (rowIndex, columnIndex);
            }
        }

        return null;
    }

    /// <summary>[CALC] The checked draft rows.</summary>
    public IReadOnlyList<SpreadsheetRow> CheckedDrafts => Rows.Where(row => row.IsDraft && row.IsChecked).ToList();

    /// <summary>[CALC] The draft row as it's stored: every cell's text exactly as it reads now.</summary>
    public DraftRow ToDraftRow(SpreadsheetRow row)
    {
        string Text(string key) => row.Cells[Columns.ToList().FindIndex(column => column.Key == key)].Text;
        return new DraftRow
        {
            Id = row.DraftId ?? throw new InvalidOperationException("A saved row isn't a draft."),
            Source = Text(nameof(DraftRow.Source)),
            Description = Text(nameof(DraftRow.Description)),
            Account = Text(nameof(DraftRow.Account)),
            Amount = Text(nameof(DraftRow.Amount)),
            Priority = Text(nameof(DraftRow.Priority)),
            Frequency = Text(nameof(DraftRow.Frequency)),
            Every = Text(nameof(DraftRow.Every)),
            DaysOfWeek = Text(nameof(DraftRow.DaysOfWeek)),
            DaysOfMonth = Text(nameof(DraftRow.DaysOfMonth)),
            Starts = Text(nameof(DraftRow.Starts)),
            Ends = Text(nameof(DraftRow.Ends)),
            Notes = Text(nameof(DraftRow.Notes)),
        };
    }

    /// <summary>
    /// [CALC] Turns a draft row into the values the Form loads from it.
    /// Throws InvalidOperationException naming the first invalid cell, re-checked here rather than trusting the row's red marks, so the Form never gets a value it shouldn't have.
    /// A blank Ends with a filled-in Starts means the bill keeps going (KeepsGoing); Frequency "Once" means a one-time item (IsOneTime).
    /// </summary>
    public DraftFormValues ToFormValues(SpreadsheetRow row)
    {
        var draft = ToDraftRow(row);
        string OtherCell(string key) => TextOf(draft, key);
        foreach (var column in Columns)
        {
            if (column.Check(TextOf(draft, column.Key), OtherCell) is { } problem)
            {
                throw new InvalidOperationException($"{column.Header}: {problem.Message}");
            }
        }

        return new DraftFormValues
        {
            Source = draft.Source.Trim(),
            Description = draft.Description.Trim(),
            AccountName = string.IsNullOrWhiteSpace(draft.Account) ? null : draft.Account.Trim(),
            Amount = CellParsing.ParseMoney(draft.Amount),
            Priority = CellParsing.ParsePositiveWholeNumber(draft.Priority),
            Frequency = CellParsing.ParseFrequency(draft.Frequency),
            Interval = CellParsing.ParsePositiveWholeNumber(draft.Every),
            ByDay = (CellParsing.ParseNumberList(draft.DaysOfWeek, 1, 7) ?? []).Select(CellParsing.ToDayOfWeek).ToList(),
            ByMonthDay = CellParsing.ParseNumberList(draft.DaysOfMonth, 1, 31) ?? [],
            Starts = CellParsing.ParseDate(draft.Starts),
            Ends = CellParsing.ParseDate(draft.Ends),

            // Filling in when it starts but not when it ends reads as a deliberate "no end date" — except for a one-time item, which never has an Ends.
            IsOneTime = CellParsing.IsOnce(draft.Frequency),
            KeepsGoing = !CellParsing.IsOnce(draft.Frequency) && string.IsNullOrWhiteSpace(draft.Ends) && !string.IsNullOrWhiteSpace(draft.Starts),
            Notes = draft.Notes,
        };
    }

    /// <summary>[CALC] A short name for a row in messages: its Description, else its Source, else "this draft".</summary>
    public string NameOf(SpreadsheetRow row)
    {
        var draft = row.IsDraft ? ToDraftRow(row) : null;
        var name = draft is null ? row.Cells[1].Text : draft.Description;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = row.Cells[0].Text;
        }

        return string.IsNullOrWhiteSpace(name) ? "this draft" : name.Trim();
    }

    /// <summary>[CALC] A stored draft's text for one column.</summary>
    private static string TextOf(DraftRow draft, string key) => key switch
    {
        nameof(DraftRow.Source) => draft.Source,
        nameof(DraftRow.Description) => draft.Description,
        nameof(DraftRow.Account) => draft.Account,
        nameof(DraftRow.Amount) => draft.Amount,
        nameof(DraftRow.Priority) => draft.Priority,
        nameof(DraftRow.Frequency) => draft.Frequency,
        nameof(DraftRow.Every) => draft.Every,
        nameof(DraftRow.DaysOfWeek) => draft.DaysOfWeek,
        nameof(DraftRow.DaysOfMonth) => draft.DaysOfMonth,
        nameof(DraftRow.Starts) => draft.Starts,
        nameof(DraftRow.Ends) => draft.Ends,
        nameof(DraftRow.Notes) => draft.Notes,
        _ => "",
    };

    /// <summary>[CALC] A saved bill or paycheck's text for one column, in the same spellings a draft uses. A one-time item reads "Once" under Frequency; an ongoing one has a blank Ends, the same way a draft says it.</summary>
    private static string SavedText(FinancialPattern pattern, string accountName, string key)
    {
        var rule = pattern.DatePattern;
        var isOneTime = rule.GetOccurrences().Count == 1;
        return key switch
        {
            nameof(DraftRow.Source) => pattern.Source,
            nameof(DraftRow.Description) => pattern.Description ?? "",
            nameof(DraftRow.Account) => accountName,
            nameof(DraftRow.Amount) => pattern.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            nameof(DraftRow.Priority) => pattern.Priority.ToString(CultureInfo.InvariantCulture),
            nameof(DraftRow.Frequency) => isOneTime ? "Once" : rule.Frequency.ToString(),
            nameof(DraftRow.Every) => isOneTime ? "" : rule.Interval.ToString(CultureInfo.InvariantCulture),
            nameof(DraftRow.DaysOfWeek) => string.Join(", ", rule.ByDay.Select(CellParsing.FromDayOfWeek)),
            nameof(DraftRow.DaysOfMonth) => string.Join(", ", rule.ByMonthDay),
            nameof(DraftRow.Starts) => rule.ActiveStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            nameof(DraftRow.Ends) => pattern.AutoRenew ? "" : rule.Until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            nameof(DraftRow.Notes) => pattern.Notes ?? "",
            _ => "",
        };
    }
}
