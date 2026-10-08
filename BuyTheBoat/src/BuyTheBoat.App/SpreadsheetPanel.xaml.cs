using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using BuyTheBoat.Domain;
using BuyTheBoat.Persistence;

namespace BuyTheBoat.App;

// The Bills & Paychecks › Spreadsheet subtab: a place to jot bills and paychecks down as rough draft rows,
// fix them up, and open each in the Form to save it for real. Saved bills and paychecks are listed underneath, read-only.
// This panel owns no repository: MainWindow stores the drafts through the callbacks below, the same shape as the form panels.
//
// TODO: saved rows become editable here once this grid runs the same checks and confirmations the Form does on save.
// TODO: after fixing one red cell, offer to apply the same fix to every other draft cell in that column holding the same text.
// Ideas, not planned: remember fixes so future pastes tidy them automatically (needs its own table);
// a suggestion that fills two cells at once ("every 2 weeks" → Frequency Weekly and Every 2).
public partial class SpreadsheetPanel : UserControl
{
    private readonly SpreadsheetModel _model;
    private readonly Dictionary<DataGridColumn, int> _columnIndexes = [];
    private IReadOnlyList<string> _accountNames = [];
    private bool _isEditing;

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
            Width = 30,
            CanUserResize = false,
            CellTemplate = ParseTemplate("""
                <CheckBox HorizontalAlignment="Center" VerticalAlignment="Center"
                          IsChecked="{Binding IsChecked, UpdateSourceTrigger=PropertyChanged}">
                    <CheckBox.Style>
                        <Style TargetType="CheckBox">
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding IsDraft}" Value="False">
                                    <Setter Property="Visibility" Value="Collapsed"/>
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </CheckBox.Style>
                </CheckBox>
                """),
        });

        for (var i = 0; i < _model.Columns.Count; i++)
        {
            var column = _model.Columns[i];
            var gridColumn = new DataGridTemplateColumn
            {
                Header = column.HeaderTip is null
                    ? column.Header
                    : new TextBlock { Text = column.Header, ToolTip = column.HeaderTip },
                Width = column.Width,
                CellTemplate = ParseTemplate(DisplayTemplate(i)),
                CellEditingTemplate = ParseTemplate(column.Editor switch
                {
                    CellEditorKind.PickList => PickListTemplate(i),
                    CellEditorKind.DatePicker => DateTemplate(i),
                    _ => TextBoxTemplate(i),
                }),
                ClipboardContentBinding = new Binding($"Cells[{i}].Text"),
            };
            _columnIndexes[gridColumn] = i;
            SheetGrid.Columns.Add(gridColumn);
        }
    }

    /// <summary>[CALC] A cell as it reads when not being edited: its text, in red and underlined with the problem on hover when it's invalid.</summary>
    private static string DisplayTemplate(int i) => $$$"""
        <TextBlock Text="{Binding Cells[{{{i}}}].Text}" Padding="4,2" TextTrimming="CharacterEllipsis"
                   ToolTip="{Binding Cells[{{{i}}}].ProblemTip}">
            <TextBlock.Style>
                <Style TargetType="TextBlock">
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding Cells[{{{i}}}].HasProblem}" Value="True">
                            <Setter Property="Foreground" Value="#FFB00020"/>
                            <Setter Property="TextDecorations" Value="Underline"/>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </TextBlock.Style>
        </TextBlock>
        """;

    /// <summary>[CALC] A plain text box for editing free text and numbers.</summary>
    private static string TextBoxTemplate(int i) => $$$"""
        <TextBox Text="{Binding Cells[{{{i}}}].Text, UpdateSourceTrigger=PropertyChanged}" BorderThickness="0" Padding="2,1"
                 FocusManager.FocusedElement="{Binding RelativeSource={RelativeSource Self}}"/>
        """;

    /// <summary>[CALC] A text box with a dropdown: typing anything is allowed, and the suggested fix sits first in the list, highlighted.</summary>
    private static string PickListTemplate(int i) => $$$"""
        <ComboBox IsEditable="True" IsTextSearchEnabled="True" TextSearch.TextPath="Value" StaysOpenOnEdit="True"
                  Text="{Binding Cells[{{{i}}}].Text, UpdateSourceTrigger=PropertyChanged}"
                  ItemsSource="{Binding Cells[{{{i}}}].Choices}"
                  FocusManager.FocusedElement="{Binding RelativeSource={RelativeSource Self}}">
            <ComboBox.ItemTemplate>
                <DataTemplate>
                    <DockPanel>
                        <TextBlock DockPanel.Dock="Right" Text="suggested" Foreground="Gray" FontSize="10" Margin="10,0,0,0" VerticalAlignment="Center">
                            <TextBlock.Style>
                                <Style TargetType="TextBlock">
                                    <Setter Property="Visibility" Value="Collapsed"/>
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding IsSuggested}" Value="True">
                                            <Setter Property="Visibility" Value="Visible"/>
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </TextBlock.Style>
                        </TextBlock>
                        <TextBlock Text="{Binding Value}"/>
                    </DockPanel>
                </DataTemplate>
            </ComboBox.ItemTemplate>
            <ComboBox.ItemContainerStyle>
                <Style TargetType="ComboBoxItem">
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding IsSuggested}" Value="True">
                            <Setter Property="Background" Value="#FFE3EDFB"/>
                            <Setter Property="FontWeight" Value="SemiBold"/>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </ComboBox.ItemContainerStyle>
        </ComboBox>
        """;

    /// <summary>[CALC] A text box with a calendar dropdown. Typed text is kept even when it isn't a date; picking a day writes it as yyyy-MM-dd.</summary>
    private static string DateTemplate(int i) => $$$"""
        <DockPanel>
            <ToggleButton x:Name="CalendarToggle" DockPanel.Dock="Right" Content="&#x25BE;" Width="18" Padding="0" Focusable="False"
                          ToolTip="Pick a date"/>
            <Popup IsOpen="{Binding IsChecked, ElementName=CalendarToggle}" StaysOpen="False"
                   PlacementTarget="{Binding ElementName=CalendarToggle}" Placement="Bottom">
                <Calendar x:Name="PickerCalendar"/>
            </Popup>
            <TextBox Text="{Binding Cells[{{{i}}}].Text, UpdateSourceTrigger=PropertyChanged}" BorderThickness="0" Padding="2,1"
                     FocusManager.FocusedElement="{Binding RelativeSource={RelativeSource Self}}"/>
        </DockPanel>
        """;

    /// <summary>[CALC] Builds a cell template from XAML text, with the WPF namespaces filled in.</summary>
    private static DataTemplate ParseTemplate(string content) =>
        (DataTemplate)XamlReader.Parse(
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" + content + "</DataTemplate>");

    // Editing ------------------------------------------------------------

    /// <summary>[UI] Saved rows and the checkbox column can't be typed into.</summary>
    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is not SpreadsheetRow { IsDraft: true } || !_columnIndexes.ContainsKey(e.Column))
        {
            e.Cancel = true;
            return;
        }

        _isEditing = true;
    }

    /// <summary>[UI] When a cell starts editing, opens a red pick-list cell's dropdown straight away so the suggested fix is one click away, and wires the date cell's calendar.</summary>
    private void OnPreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.Row.Item is not SpreadsheetRow row || !_columnIndexes.TryGetValue(e.Column, out var index))
        {
            return;
        }

        // Deferred until the editing control has been laid out, so its parts can be found.
        var editingElement = e.EditingElement;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => WireEditor(editingElement, row.Cells[index]));
    }

    /// <summary>[UI] Opens a red pick-list cell's dropdown, and makes picking a day in a date cell's calendar fill the cell.</summary>
    private static void WireEditor(DependencyObject editingElement, SpreadsheetCell cell)
    {
        if (FindChild<ComboBox>(editingElement) is { } comboBox && cell.Problem?.Suggestion is not null)
        {
            comboBox.IsDropDownOpen = true;
        }

        if (FindChild<ToggleButton>(editingElement) is { } toggle && FindChild<System.Windows.Controls.Calendar>(editingElement) is { } calendar)
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

            // A Calendar keeps the mouse captured after a click, which would swallow the next click on the grid.
            calendar.PreviewMouseUp += (_, _) => Mouse.Capture(null);
        }
    }

    /// <summary>[UI] When the user leaves an edited cell: tidy and re-check its row, then store the draft. Deferred until the binding has written the final text.</summary>
    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        _isEditing = false;
        if (e.Row.Item is not SpreadsheetRow { IsDraft: true } row || !_columnIndexes.TryGetValue(e.Column, out var index))
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _model.CommitCell(row, index);
            SaveDraft(row);
        });
    }

    /// <summary>[UI] Starts editing when the user just types into a selected draft cell, the way a spreadsheet does, keeping the typed character.</summary>
    private void OnGridPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_isEditing || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]) || !CurrentCellIsEditableDraft())
        {
            return;
        }

        e.Handled = true;
        var typed = e.Text;
        SheetGrid.BeginEdit();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (Keyboard.FocusedElement is TextBox box)
            {
                box.Text = typed;
                box.CaretIndex = box.Text.Length;
            }
        });
    }

    /// <summary>[UI] Spreadsheet keys: F8 jumps to the next red cell, Enter on a red cell accepts its suggestion, Delete clears the selected draft cells, and Ctrl+V pastes at the current cell.</summary>
    private void OnGridPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F8)
        {
            e.Handled = true;
            JumpToNextInvalidCell();
            return;
        }

        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // While a cell is being edited, only a multi-cell paste is taken over; anything else pastes into the box as usual.
            var text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
            if (text is null || (_isEditing && PasteParser.Parse(text) is null))
            {
                return;
            }

            e.Handled = true;
            if (_isEditing)
            {
                SheetGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
            }

            PasteAtCurrentCell(text);
            return;
        }

        if (_isEditing)
        {
            return;
        }

        if (e.Key == Key.Enter && CurrentCell() is { } current
            && _model.Rows[current.Row].Cells[current.Column].Problem?.Suggestion is not null)
        {
            e.Handled = true;
            var row = _model.Rows[current.Row];
            _model.AcceptSuggestion(row, current.Column);
            SaveDraft(row);
            return;
        }

        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            ClearSelectedCells();
        }
    }

    /// <summary>[UI] Pastes the clipboard text starting at the current cell, then stores every draft it changed.</summary>
    private void PasteAtCurrentCell(string text)
    {
        if (CurrentCell() is not { } current)
        {
            return;
        }

        foreach (var row in _model.Paste(text, current.Column, current.Row))
        {
            SaveDraft(row);
        }
    }

    /// <summary>[UI] Blanks every selected cell that belongs to a draft row, then stores those drafts.</summary>
    private void ClearSelectedCells()
    {
        var touched = new HashSet<SpreadsheetRow>();
        foreach (var selected in SheetGrid.SelectedCells)
        {
            if (selected.Item is SpreadsheetRow { IsDraft: true } row && _columnIndexes.TryGetValue(selected.Column, out var index))
            {
                row.Cells[index].Text = "";
                touched.Add(row);
            }
        }

        foreach (var row in touched)
        {
            _model.Revalidate(row);
            SaveDraft(row);
        }
    }

    /// <summary>[CALC] The current cell as (row, spreadsheet column) indexes, or null when it's the checkbox column or nothing is current.</summary>
    private (int Row, int Column)? CurrentCell()
    {
        var current = SheetGrid.CurrentCell;
        if (current.Item is not SpreadsheetRow row || current.Column is null || !_columnIndexes.TryGetValue(current.Column, out var index))
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
    private void OnAddDraftClick(object sender, RoutedEventArgs e)
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
    private void OnOpenInFormClick(object sender, RoutedEventArgs e)
    {
        if (_model.CheckedDrafts is not [{ HasProblems: false, DraftId: { } id } row])
        {
            return;
        }

        OpenInFormRequested?.Invoke(id, _model.ToFormValues(row));
    }

    /// <summary>[DELETES] Permanently removes the checked draft rows after asking first.</summary>
    private void OnDeleteDraftsClick(object sender, RoutedEventArgs e)
    {
        var drafts = _model.CheckedDrafts;
        if (drafts.Count == 0)
        {
            return;
        }

        var what = drafts.Count == 1 ? $"the draft “{_model.NameOf(drafts[0])}”" : $"{drafts.Count} draft rows";
        if (MessageBox.Show(Window.GetWindow(this), $"Delete {what}? This can't be undone.", "Delete drafts",
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

    private void OnInvalidCounterClick(object sender, MouseButtonEventArgs e) => JumpToNextInvalidCell();

    private void OnNextInvalidClick(object sender, RoutedEventArgs e) => JumpToNextInvalidCell();

    /// <summary>[UI] Moves the cursor to the next red cell after the current one, wrapping to the top.</summary>
    private void JumpToNextInvalidCell()
    {
        var current = CurrentCell() ?? (-1, _model.Columns.Count - 1);
        if (_model.NextInvalidCell(current.Row, current.Column) is { } next)
        {
            FocusCell(next.Row, next.Column);
        }
    }

    /// <summary>[UI] Selects one cell, scrolls it into view, and gives the grid keyboard focus.</summary>
    private void FocusCell(int rowIndex, int columnIndex)
    {
        var row = _model.Rows[rowIndex];
        var column = _columnIndexes.First(pair => pair.Value == columnIndex).Key;
        SheetGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        SheetGrid.SelectedCells.Clear();
        SheetGrid.ScrollIntoView(row, column);
        var cellInfo = new DataGridCellInfo(row, column);
        SheetGrid.CurrentCell = cellInfo;
        SheetGrid.SelectedCells.Add(cellInfo);
        SheetGrid.Focus();
    }

    /// <summary>[WRITES FILE] Saves every row on screen, drafts included, to an .xlsx file the user picks.</summary>
    private void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Download bills and paychecks as a spreadsheet",
            FileName = $"buytheboat-bills-and-paychecks-{DateTime.Today:yyyy-MM-dd}.xlsx",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*",
        };

        if (AppPaths.DefaultExportFolder is { } exportFolder)
        {
            Directory.CreateDirectory(exportFolder);
            dialog.InitialDirectory = exportFolder;
        }

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            BillsSpreadsheetExporter.Export(_model, dialog.FileName);
        }
        catch (IOException ex)
        {
            ErrorLog.Record("downloading the bills and paychecks spreadsheet", ex);
            MessageBox.Show(Window.GetWindow(this), $"Couldn't save: {ex.Message}", "Download failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        MessageBox.Show(Window.GetWindow(this), $"Saved to {dialog.FileName}.", "Download complete", MessageBoxButton.OK, MessageBoxImage.Information);
    }

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
        InvalidCounter.Visibility = invalidCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextInvalidButton.Visibility = InvalidCounter.Visibility;

        RefreshProblemList(checkedDrafts is [{ HasProblems: true } only] ? only : null);
    }

    /// <summary>[UI] Lists what's stopping the checked draft from opening in the Form, each with a button to use its suggested fix. Hidden when there's nothing to list.</summary>
    private void RefreshProblemList(SpreadsheetRow? row)
    {
        ProblemList.Children.Clear();
        if (row is null)
        {
            ProblemStrip.Visibility = Visibility.Collapsed;
            return;
        }

        var red = (Brush)FindResource("InvalidTextBrush");
        ProblemList.Children.Add(new TextBlock
        {
            Text = $"“{_model.NameOf(row)}” can't open in the form until these are fixed:",
            FontWeight = FontWeights.SemiBold,
            Foreground = red,
            Margin = new Thickness(0, 0, 0, 4),
        });

        for (var i = 0; i < _model.Columns.Count; i++)
        {
            if (row.Cells[i].Problem is not { } problem)
            {
                continue;
            }

            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            line.Children.Add(new TextBlock
            {
                Text = $"{_model.Columns[i].Header}: {problem.Message}",
                Foreground = red,
                VerticalAlignment = VerticalAlignment.Center,
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

        ProblemStrip.Visibility = Visibility.Visible;
    }

    /// <summary>[CALC] The first element of the given type inside a cell's editing control, searching depth-first.</summary>
    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match)
        {
            return match;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            if (FindChild<T>(VisualTreeHelper.GetChild(parent, i)) is { } found)
            {
                return found;
            }
        }

        // Before the editing control is laid out it has no visual children yet, only logical ones.
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (FindChild<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
