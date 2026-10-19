using System.ComponentModel;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BuyTheBoat.Domain;
using BuyTheBoat.Persistence;

namespace BuyTheBoat.App;

/// <summary>
/// The Bills &amp; Paychecks › Spreadsheet subtab: jot bills and paychecks down as rough draft rows, fix them up, and open each in the Form to save it for real; saved ones are listed underneath, read-only.
/// This panel has no repository: MainWindow stores the drafts through the callbacks below, like the form panels.
/// Avalonia's grid selects whole rows, not single cells: the current cell is the selected row in the current column, and Delete clears that column in every selected draft row.
/// </summary>
/// <remarks>
/// Ideas, not planned: remember fixes so future pastes tidy them automatically (needs its own table); a suggestion that fills two cells at once ("every 2 weeks" → Frequency Weekly and Every 2).
/// </remarks>
public partial class SpreadsheetPanel : UserControl
{
    private readonly SpreadsheetModel _model;
    private readonly Dictionary<DataGridColumn, int> _columnIndexes = [];
    private IReadOnlyList<string> _accountNames = [];
    private bool _isEditing;

    // What the user typed into a selected cell to start editing it; PreparingCellForEdit puts it in the editor's text box.
    private string? _typedToStartEdit;

    // Gives a new draft row its Id.
    public Func<int>? AllocateDraftId { get; set; }

    // Stores a draft row after any change to it.
    public Action<DraftRow>? DraftSaved { get; set; }

    // Removes draft rows from storage, by Id.
    public Action<IReadOnlyList<int>>? DraftsDeleted { get; set; }

    // Opens a draft in the Form subtab: (draft Id, its parsed values). MainWindow runs the unsaved-changes warning first.
    public Action<int, DraftFormValues>? OpenInFormRequested { get; set; }

    public SpreadsheetPanel()
    {
        InitializeComponent();
        _model = new SpreadsheetModel(SpreadsheetColumns.Create(() => _accountNames));
        BuildColumns();
        SheetGrid.ItemsSource = _model.Rows;
        _model.Rows.CollectionChanged += (_, e) =>
        {
            foreach (var row in e.NewItems?.OfType<SpreadsheetRow>() ?? [])
            {
                row.PropertyChanged += OnRowPropertyChanged;
            }

            RefreshToolbar();
        };

        // Tunnelling, so these see keys and typing before the grid and its editors act on them.
        SheetGrid.AddHandler(KeyDownEvent, OnGridPreviewKeyDown, RoutingStrategies.Tunnel);
        SheetGrid.AddHandler(TextInputEvent, OnGridPreviewTextInput, RoutingStrategies.Tunnel);
    }

    /// <summary>[UI] Refills the grid: the drafts, then every saved bill and paycheck with its account's name. accountNames is what the Account column accepts.</summary>
    public void SetContent(
        IReadOnlyList<DraftRow> drafts,
        IReadOnlyList<(FinancialPattern Pattern, string AccountName)> saved,
        IReadOnlyList<string> accountNames)
    {
        _accountNames = accountNames;
        _model.Load(drafts, saved);
        RefreshToolbar();
    }

    /// <summary>[UI] Adds one column per spreadsheet column, after a checkbox column that only draft rows show.</summary>
    private void BuildColumns()
    {
        SheetGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "",
            Width = new DataGridLength(30),
            CanUserResize = false,
            CellTemplate = (IDataTemplate)this.FindResource("CheckCell")!,
        });

        for (var i = 0; i < _model.Columns.Count; i++)
        {
            var column = _model.Columns[i];
            object header = column.Header;
            if (column.HeaderTip is { } tip)
            {
                var headerText = new TextBlock { Text = column.Header };
                ToolTip.SetTip(headerText, tip);
                header = headerText;
            }

            var gridColumn = new DataGridTemplateColumn
            {
                Header = header,
                Width = new DataGridLength(column.Width),
                CellTemplate = CellTemplate("CellText", i),
                CellEditingTemplate = CellTemplate(column.Editor switch
                {
                    CellEditorKind.PickList => "CellPickListEditor",
                    CellEditorKind.DatePicker => "CellDateEditor",
                    _ => "CellTextEditor",
                }, i),
                ClipboardContentBinding = new ReflectionBinding($"Cells[{i}].Text"),
            };
            _columnIndexes[gridColumn] = i;
            SheetGrid.Columns.Add(gridColumn);
        }
    }

    /// <summary>[CALC] One of the cell templates in SpreadsheetPanel.axaml, showing the row's cell in the given column.</summary>
    private IDataTemplate CellTemplate(string resourceKey, int column)
    {
        var template = (IDataTemplate)this.FindResource(resourceKey)!;
        return new FuncDataTemplate<SpreadsheetRow>((_, _) =>
        {
            var control = template.Build(null)!;
            control.Bind(DataContextProperty, new ReflectionBinding($"Cells[{column}]"));
            return control;
        });
    }

    /// <summary>[UI] Tints draft rows. Rows are reused as the grid scrolls, so every row is set either way.</summary>
    private void OnLoadingRow(object? sender, DataGridRowEventArgs e) =>
        e.Row.Classes.Set("draft", e.Row.DataContext is SpreadsheetRow { IsDraft: true });

    // Editing ------------------------------------------------------------

    /// <summary>[UI] Saved rows and the checkbox column can't be typed into.</summary>
    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.DataContext is not SpreadsheetRow { IsDraft: true } || !_columnIndexes.ContainsKey(e.Column))
        {
            e.Cancel = true;
            _typedToStartEdit = null;
            return;
        }

        _isEditing = true;
    }

    /// <summary>[UI] When a cell starts editing: puts the cursor in its text box (with whatever was typed to start it), opens a red pick-list cell's dropdown so the suggested fix is one click away, and wires the date cell's calendar.</summary>
    private void OnPreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.Row.DataContext is not SpreadsheetRow row || !_columnIndexes.TryGetValue(e.Column, out var index))
        {
            return;
        }

        var typed = _typedToStartEdit;
        _typedToStartEdit = null;

        // Deferred until the editing control has been laid out, so its parts can be found.
        var editingElement = e.EditingElement;
        Dispatcher.UIThread.Post(() => WireEditor(editingElement, row.Cells[index], typed), DispatcherPriority.Loaded);
    }

    /// <summary>[UI] Focuses the editor's text box, opens a red pick-list cell's dropdown, and makes picking a day in a date cell's calendar fill the cell.</summary>
    private static void WireEditor(Control editingElement, SpreadsheetCell cell, string? typed)
    {
        // Plain, pick-list and date cells all have a text box inside; the pick-list's is part of the ComboBox's template.
        if (editingElement.GetSelfAndVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } box)
        {
            box.Focus();
            if (typed is not null)
            {
                box.Text = typed;
            }

            box.CaretIndex = box.Text?.Length ?? 0;
        }

        if (editingElement is ComboBox comboBox && typed is null && cell.Problem?.Suggestion is not null)
        {
            comboBox.IsDropDownOpen = true;
        }

        // A Popup's content isn't drawn until it opens, so the calendar is found through the logical tree.
        if (editingElement.GetLogicalDescendants().OfType<ToggleButton>().FirstOrDefault() is { } toggle
            && editingElement.GetLogicalDescendants().OfType<Avalonia.Controls.Calendar>().FirstOrDefault() is { } calendar)
        {
            if (CellParsing.ParseDate(cell.Text) is { } current)
            {
                calendar.SelectedDate = current.ToDateTime(TimeOnly.MinValue);
                calendar.DisplayDate = calendar.SelectedDate.Value;
            }

            calendar.SelectedDatesChanged += (_, _) =>
            {
                if (calendar.SelectedDate is { } picked)
                {
                    cell.Text = DateOnly.FromDateTime(picked).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    toggle.IsChecked = false;
                }
            };
        }
    }

    /// <summary>[UI] When the user leaves an edited cell: tidy and re-check its row, then store the draft. Deferred until the editor has written the final text.</summary>
    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        _isEditing = false;
        if (e.Row.DataContext is not SpreadsheetRow { IsDraft: true } row || !_columnIndexes.TryGetValue(e.Column, out var index))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _model.CommitCell(row, index);
            SaveDraft(row);
        }, DispatcherPriority.Background);
    }

    /// <summary>[UI] Starts editing when the user just types into a selected draft cell, the way a spreadsheet does, keeping the typed character.</summary>
    private void OnGridPreviewTextInput(object? sender, TextInputEventArgs e)
    {
        if (_isEditing || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]) || !CurrentCellIsEditableDraft())
        {
            return;
        }

        e.Handled = true;
        StartEditingWith(e.Text);
    }

    /// <summary>[UI] Starts editing the current cell with the typed text in its box, cursor at the end.</summary>
    private void StartEditingWith(string typed)
    {
        _typedToStartEdit = typed;
        if (!SheetGrid.BeginEdit())
        {
            _typedToStartEdit = null;
        }
    }

    /// <summary>[UI] Spreadsheet keys: F8 jumps to the next red cell, Backspace starts editing the current draft cell with it cleared, Delete clears the selected draft cells, and Ctrl+V pastes at the current cell.</summary>
    private void OnGridPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F8)
        {
            e.Handled = true;
            JumpToNextInvalidCell();
            return;
        }

        if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control)
        {
            // The clipboard can only be read asynchronously, so the key is always taken here; a paste that turns out to be one cell's worth goes into the editing box as usual.
            e.Handled = true;
            PasteFromClipboard(e.Source as TextBox);
            return;
        }

        if (_isEditing)
        {
            return;
        }

        if (e.Key == Key.Back && CurrentCellIsEditableDraft())
        {
            e.Handled = true;
            StartEditingWith("");
            return;
        }

        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            ClearSelectedCells();
        }
    }

    /// <summary>[UI] Pastes the clipboard text: into the box being edited when it's a single value, otherwise across the grid starting at the current cell.</summary>
    private async void PasteFromClipboard(TextBox? editingBox)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard || await clipboard.TryGetTextAsync() is not { } text)
        {
            return;
        }

        // While a cell is being edited, only a multi-cell paste is taken over.
        if (_isEditing && PasteParser.Parse(text) is null)
        {
            editingBox?.Paste();
            return;
        }

        if (_isEditing)
        {
            SheetGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        }

        PasteAtCurrentCell(text);
    }

    /// <summary>[UI] Pastes the clipboard text starting at the current cell, adding draft rows when the paste has more rows than there are drafts below it, then stores every draft it changed or added.</summary>
    private void PasteAtCurrentCell(string text)
    {
        if (CurrentCell() is not { } current || AllocateDraftId is null)
        {
            return;
        }

        foreach (var row in _model.Paste(text, current.Column, current.Row, AllocateDraftId))
        {
            SaveDraft(row);
        }
    }

    /// <summary>[UI] Blanks the current column's cell in every selected draft row, then stores those drafts.</summary>
    private void ClearSelectedCells()
    {
        if (SheetGrid.CurrentColumn is not { } column || !_columnIndexes.TryGetValue(column, out var index))
        {
            return;
        }

        foreach (var row in SheetGrid.SelectedItems.OfType<SpreadsheetRow>().Where(row => row.IsDraft).ToList())
        {
            row.Cells[index].Text = "";
            _model.Revalidate(row);
            SaveDraft(row);
        }
    }

    /// <summary>[CALC] The current cell as (row, spreadsheet column) indexes, or null when it's the checkbox column or nothing is current.</summary>
    private (int Row, int Column)? CurrentCell()
    {
        if (SheetGrid.SelectedItem is not SpreadsheetRow row || SheetGrid.CurrentColumn is not { } column || !_columnIndexes.TryGetValue(column, out var index))
        {
            return null;
        }

        return (_model.Rows.IndexOf(row), index);
    }

    /// <summary>[CALC] Whether the current cell is a draft cell that can be typed into.</summary>
    private bool CurrentCellIsEditableDraft() =>
        CurrentCell() is { } current && _model.Rows[current.Row].IsDraft;

    // Toolbar ------------------------------------------------------------

    /// <summary>[UI] Adds a blank draft row, stores it, and puts the cursor in its first cell.</summary>
    private void OnAddDraftClick(object? sender, RoutedEventArgs e)
    {
        if (AllocateDraftId is null)
        {
            return;
        }

        var row = _model.AddDraft(AllocateDraftId());
        SaveDraft(row);
        FocusCell(_model.Rows.IndexOf(row), 0);
    }

    /// <summary>[UI] Hands the one checked draft to MainWindow to open in the Form. The button is only enabled when exactly one draft is checked and none of its cells are red.</summary>
    private async void OnOpenInFormClick(object? sender, RoutedEventArgs e)
    {
        if (_model.CheckedDrafts is not [{ HasProblems: false, DraftId: { } id } row])
        {
            return;
        }

        DraftFormValues values;
        try
        {
            values = _model.ToFormValues(row);
        }
        catch (InvalidOperationException ex)
        {
            // Only reachable if a cell went bad without its row being re-checked; the message names the cell.
            _model.Revalidate(row);
            RefreshToolbar();
            await MessageBox.Show(OwnerWindow(), $"This draft can't open in the form yet. {ex.Message}", "Fix the draft first",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        OpenInFormRequested?.Invoke(id, values);
    }

    /// <summary>[DELETES] Permanently removes the checked draft rows after asking first.</summary>
    private async void OnDeleteDraftsClick(object? sender, RoutedEventArgs e)
    {
        var drafts = _model.CheckedDrafts;
        if (drafts.Count == 0)
        {
            return;
        }

        var what = drafts.Count == 1 ? $"the draft “{_model.NameOf(drafts[0])}”" : $"{drafts.Count} draft rows";
        if (await MessageBox.Show(OwnerWindow(), $"Delete {what}? This can't be undone.", "Delete drafts",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var row in drafts)
        {
            _model.Remove(row);
        }

        DraftsDeleted?.Invoke(drafts.Select(row => row.DraftId!.Value).ToList());
    }

    /// <summary>[STEP] Opens the fix list from the red "N invalid cells" counter.</summary>
    private void OnInvalidCounterClick(object? sender, RoutedEventArgs e) => ShowFixes();

    /// <summary>[STEP] Opens the fix list for every red cell. Makes the fixes the user left checked, or moves the cursor to the cell they chose to fix by hand.</summary>
    private async void ShowFixes()
    {
        SheetGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        var fixes = _model.InvalidCells()
            .Select(entry =>
            {
                var cell = entry.Row.Cells[entry.Column];
                return new CellFixRow
                {
                    Row = entry.Row,
                    Column = entry.Column,
                    DraftName = _model.NameOf(entry.Row),
                    ColumnHeader = _model.Columns[entry.Column].Header,
                    CurrentText = cell.Text,
                    Message = cell.Problem!.Message,
                    Suggestion = cell.Problem.Suggestion,
                    IsChecked = cell.Problem.Suggestion is not null,
                };
            })
            .ToList();
        if (fixes.Count == 0)
        {
            return;
        }

        var window = new CellFixesWindow(fixes);
        if (await window.ShowDialog<bool>(OwnerWindow()))
        {
            // Applied as shown, in order: each fix writes the exact text the user saw, even if an earlier fix changed what this cell would now suggest.
            foreach (var fix in window.ChosenFixes)
            {
                _model.ReplaceText(fix.Row, fix.Column, fix.Suggestion!);
            }

            foreach (var row in window.ChosenFixes.Select(fix => fix.Row).Distinct())
            {
                SaveDraft(row);
            }
        }
        else if (window.GoToTarget is { } target)
        {
            FocusCell(_model.Rows.IndexOf(target.Row), target.Column);
        }
    }

    /// <summary>[UI] Moves the cursor to the next red cell, from the Next (F8) button.</summary>
    private void OnNextInvalidClick(object? sender, RoutedEventArgs e) => JumpToNextInvalidCell();

    /// <summary>[UI] Moves the cursor to the next red cell after the current one, wrapping to the top.</summary>
    private void JumpToNextInvalidCell()
    {
        var current = CurrentCell() ?? (-1, _model.Columns.Count - 1);
        if (_model.NextInvalidCell(current.Row, current.Column) is { } next)
        {
            FocusCell(next.Row, next.Column);
        }
    }

    /// <summary>[UI] Selects one cell's row, makes its column current, scrolls it into view, and gives the grid keyboard focus.</summary>
    private void FocusCell(int rowIndex, int columnIndex)
    {
        var row = _model.Rows[rowIndex];
        var column = _columnIndexes.First(pair => pair.Value == columnIndex).Key;
        SheetGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        SheetGrid.SelectedItem = row;
        SheetGrid.CurrentColumn = column;
        SheetGrid.ScrollIntoView(row, column);
        SheetGrid.Focus();
    }

    /// <summary>[WRITES FILE] Saves every row on screen, drafts included, to an .xlsx file the user picks.</summary>
    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        var owner = OwnerWindow();
        IStorageFolder? startFolder = null;
        if (AppPaths.DefaultExportFolder is { } exportFolder)
        {
            Directory.CreateDirectory(exportFolder);
            startFolder = await owner.StorageProvider.TryGetFolderFromPathAsync(exportFolder);
        }

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Download bills and paychecks as a spreadsheet",
            SuggestedFileName = $"buytheboat-bills-and-paychecks-{DateTime.Today:yyyy-MM-dd}.xlsx",
            DefaultExtension = "xlsx",
            FileTypeChoices = [new FilePickerFileType("Excel Workbook") { Patterns = ["*.xlsx"] }, FilePickerFileTypes.All],
            SuggestedStartLocation = startFolder,
        });

        // The exporter writes to a path; a picked location with none (not a real folder on this machine) can't be written to.
        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            BillsSpreadsheetExporter.Export(_model, path);
        }
        catch (IOException ex)
        {
            ErrorLog.Record("downloading the bills and paychecks spreadsheet", ex);
            await MessageBox.Show(owner, $"Couldn't save: {ex.Message}", "Download failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        await MessageBox.Show(owner, $"Saved to {path}.", "Download complete", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>[CALC] The window this panel sits in, for owning its popups.</summary>
    private Window OwnerWindow() => (Window)TopLevel.GetTopLevel(this)!;

    // Keeping the toolbar and problem list current --------------------------------

    /// <summary>[UI] A checkbox or a row's red marks changed, so the buttons and problem list may need to change too.</summary>
    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SpreadsheetRow.IsChecked) or nameof(SpreadsheetRow.HasProblems))
        {
            RefreshToolbar();
        }
    }

    /// <summary>[WRITES FILE] Stores a draft row as it reads now, then refreshes the toolbar for its new red marks.</summary>
    private void SaveDraft(SpreadsheetRow row)
    {
        DraftSaved?.Invoke(_model.ToDraftRow(row));
        RefreshToolbar();
    }

    /// <summary>[UI] Updates which buttons are enabled, the red-cell counter, and the problem list for the checked draft.</summary>
    private void RefreshToolbar()
    {
        var checkedDrafts = _model.CheckedDrafts;
        OpenInFormButton.IsEnabled = checkedDrafts is [{ HasProblems: false }];
        DeleteDraftsButton.IsEnabled = checkedDrafts.Count > 0;

        var invalidCount = _model.InvalidCellCount;
        InvalidCounterText.Text = invalidCount == 1 ? "1 invalid cell" : $"{invalidCount} invalid cells";
        InvalidCounter.IsVisible = invalidCount > 0;
        NextInvalidButton.IsVisible = InvalidCounter.IsVisible;

        RefreshProblemList(checkedDrafts is [{ HasProblems: true } only] ? only : null);
    }

    /// <summary>[UI] Lists what's stopping the checked draft from opening in the Form, each with a button to use its suggested fix. Hidden when there's nothing to list.</summary>
    private void RefreshProblemList(SpreadsheetRow? row)
    {
        ProblemList.Children.Clear();
        if (row is null)
        {
            ProblemStrip.IsVisible = false;
            return;
        }

        var red = (IBrush)this.FindResource("InvalidTextBrush")!;
        ProblemList.Children.Add(new TextBlock
        {
            Text = $"“{_model.NameOf(row)}” can't open in the form until these are fixed:",
            FontWeight = FontWeight.SemiBold,
            Foreground = red,
            Margin = new Thickness(0, 0, 0, 4),
        });

        for (var i = 0; i < _model.Columns.Count; i++)
        {
            if (row.Cells[i].Problem is not { } problem)
            {
                continue;
            }

            var line = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            line.Children.Add(new TextBlock
            {
                Text = $"{_model.Columns[i].Header}: {problem.Message}",
                Foreground = red,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });

            if (problem.Suggestion is { } suggestion)
            {
                var columnIndex = i;
                var useButton = new Button { Content = $"Use {suggestion}", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(8, 0, 0, 0) };
                useButton.Click += (_, _) =>
                {
                    _model.AcceptSuggestion(row, columnIndex);
                    SaveDraft(row);
                };
                line.Children.Add(useButton);
            }

            ProblemList.Children.Add(line);
        }

        ProblemStrip.IsVisible = true;
    }
}
