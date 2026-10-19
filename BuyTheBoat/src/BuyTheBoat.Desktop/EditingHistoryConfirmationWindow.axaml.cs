using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

/// <summary>
/// Shows request.Rows, the questions and announcements FinancePatternSaveConfirmation or EarmarkPatternSaveConfirmation raised for one save, top to bottom, and reports the selections back as a ConfirmationOutcome.
/// It never decides which rows exist or what an answer means; the confirmation class reads each index back into a decision.
/// It's a deliberately plain look for now; a styled version is the eventual goal.
/// Closes with true for Save or Continue, false for Cancel.
/// </summary>
/// <remarks>
/// Built in code-behind rather than with an ItemsControl and DataTemplates, since the rows are immutable records, each ChoiceRow needs its own radio group, and the consequence footer updates live.
/// Those are a few lines here, but would each need a per-row view model and converters in XAML.
/// </remarks>
public partial class EditingHistoryConfirmationWindow : Window
{
    /// <summary>
    /// One drawn ChoiceRow: its radios and options in order, the consequence footer, and each option's child-row panel (null for a leaf), shown only while that option is selected.
    /// Kept by row Id so ToOutcome can report each selection and OnOptionCheckedChanged can refresh the right footer and panels.
    /// </summary>
    private sealed record ChoiceRowControls(
        IReadOnlyList<RadioButton> Radios, IReadOnlyList<ChoiceOption> Options, TextBlock Footer,
        IReadOnlyList<Control?> ChildPanels);

    private readonly Dictionary<string, ChoiceRowControls> _choiceRows = new();

    /// <summary>The plan-shape picker's radios and candidates, so ToOutcome can report the picked plan as ChosenPlanShape; null when no picker was shown, which reads as the default.</summary>
    private (IReadOnlyList<RadioButton> Radios, IReadOnlyList<FinancePatternSaveConfirmation.PlanShapeCandidate> Candidates)? _planShapePicker;

    /// <summary>Only for the XAML loader and previewer; the app uses the constructor that takes the request.</summary>
    public EditingHistoryConfirmationWindow()
    {
        InitializeComponent();
    }

    public EditingHistoryConfirmationWindow(
        ImplicitChangeConfirmationRequest request, int pageNumber = 1, int pageCount = 1)
    {
        InitializeComponent();

        // Limits the height to the screen, so a page too tall to split scrolls inside the window instead of pushing the buttons off-screen.
        if (Screens.Primary is { } screen)
        {
            MaxHeight = screen.WorkingArea.Height / screen.Scaling * 0.92;
        }

        DescriptionText.Text = request.Description;

        // "Continue…" while more pages follow, "Save" on the last — the only thing
        // the popup itself knows about pagination.
        CommitButton.Content = request.IsFinalPage ? "Save" : "Continue…";

        if (pageCount > 1)
        {
            PageIndicator.Text = $"Page {pageNumber} of {pageCount}";
            PageIndicator.IsVisible = true;
        }

        foreach (var row in request.Rows)
        {
            RowsPanel.Children.Add(BuildRowControl(row));
        }
    }

    /// <summary>[CALC] The raw selections to hand the wrapper — one option index per ChoiceRow drawn, keyed by row Id, plus the picked plan shape when a CandidatePickerRow was drawn. Call after the window closes, passing whether it closed with Save as proceed.</summary>
    /// <param name="proceed">Whether the user saved rather than cancelled.</param>
    public ConfirmationOutcome ToOutcome(bool proceed) => new()
    {
        Proceed = proceed,
        ChosenOptionIndex = _choiceRows.ToDictionary(entry => entry.Key, entry => SelectedIndex(entry.Value.Radios)),
        ChosenPlanShape = PickedPlanShape(),
    };

    /// <summary>[CALC] The savings-plan shape the user picked (that candidate's own plan), or null when no picker was drawn or somehow nothing is selected — null reads back as the wrapper's default "Recommended" candidate.</summary>
    private EarMarkPattern? PickedPlanShape()
    {
        if (_planShapePicker is not { } picker)
        {
            return null;
        }

        var selected = SelectedIndex(picker.Radios);
        return selected >= 0 ? picker.Candidates[selected].Plan.Plan : null;
    }

    /// <summary>[CALC] Builds the one control that renders a single row, dispatched on its kind.</summary>
    private Control BuildRowControl(ConfirmationRow row) => row switch
    {
        AnnouncementRow announcement => BuildAnnouncement(announcement),
        ChoiceRow choice => BuildChoice(choice),
        CandidatePickerRow picker => BuildCandidatePicker(picker),
        // Fail loud rather than draw nothing if a new row kind is ever added
        // without a template here.
        _ => throw new NotSupportedException($"No popup template for confirmation row kind {row.GetType().Name}."),
    };

    /// <summary>[CALC] A plain, wrapped statement with no choice attached — the forced-consolidation notice, the source-change warning, the break-off notice.</summary>
    private static TextBlock BuildAnnouncement(AnnouncementRow row) => new()
    {
        Text = row.Text,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 14),
    };

    /// <summary>[CALC] A question: its bold prompt, its options as one independent radio group (laid out per OptionLayout), the default pre-selected, and a footer under them showing the selected option's consequence (hidden when that option has none).</summary>
    private Control BuildChoice(ChoiceRow row)
    {
        var container = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

        container.Children.Add(new TextBlock
        {
            Text = row.Question,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        });

        // SideBySide lays the radios in a row; Stacked (what every row uses
        // today) lays them one per line.
        var sideBySide = row.Layout == OptionLayout.SideBySide;
        var optionsPanel = new StackPanel
        {
            Orientation = sideBySide ? Orientation.Horizontal : Orientation.Vertical,
        };

        var footer = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            FontStyle = FontStyle.Italic,
            Margin = new Thickness(20, 4, 0, 0),
            IsVisible = false,
        };

        var radios = new List<RadioButton>(row.Options.Count);
        var childPanels = new List<Control?>(row.Options.Count);
        for (var i = 0; i < row.Options.Count; i++)
        {
            var option = row.Options[i];
            var radio = new RadioButton
            {
                Content = new TextBlock { Text = option.Label, TextWrapping = TextWrapping.Wrap },
                // Unique GroupName per row keeps each question's radios
                // mutually exclusive without bleeding into the next question's.
                GroupName = row.Id,
                IsChecked = i == row.DefaultIndex,
                Margin = sideBySide ? new Thickness(0, 0, 12, 0) : new Thickness(0, 0, 0, 4),
                Tag = row.Id,
            };
            radio.IsCheckedChanged += OnOptionCheckedChanged;
            radios.Add(radio);
            optionsPanel.Children.Add(radio);

            // Builds the option's child rows, indented and shown only while it's selected; they register in _choiceRows too, so ToOutcome reports them whether or not they're visible.
            if (option.Children.Count > 0)
            {
                var childPanel = new StackPanel { Margin = new Thickness(22, 2, 0, 6) };
                foreach (var child in option.Children)
                {
                    childPanel.Children.Add(BuildRowControl(child));
                }

                childPanel.IsVisible = i == row.DefaultIndex;
                childPanels.Add(childPanel);
                optionsPanel.Children.Add(childPanel);
            }
            else
            {
                childPanels.Add(null);
            }
        }

        container.Children.Add(optionsPanel);
        container.Children.Add(footer);

        var controls = new ChoiceRowControls(radios, row.Options, footer, childPanels);
        _choiceRows[row.Id] = controls;
        UpdateConsequenceFooter(controls); // set the footer for the default selection up front

        return container;
    }

    /// <summary>[CALC] The plan-shape picker: its bold prompt and one radio per candidate shape, the recommended one pre-selected. Each candidate's own plan is remembered so ToOutcome can report the picked one as ChosenPlanShape.</summary>
    private Control BuildCandidatePicker(CandidatePickerRow row)
    {
        var container = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

        container.Children.Add(new TextBlock
        {
            Text = row.Question,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        });

        // One radio per candidate, its own Label as the text — no consequence
        // footer or child rows, unlike a ChoiceRow: a candidate is just a shape.
        var radios = new List<RadioButton>(row.Candidates.Count);
        for (var i = 0; i < row.Candidates.Count; i++)
        {
            var radio = new RadioButton
            {
                Content = new TextBlock { Text = row.Candidates[i].Label, TextWrapping = TextWrapping.Wrap },
                GroupName = row.Id,
                IsChecked = i == row.DefaultIndex,
                Margin = new Thickness(0, 0, 0, 4),
            };
            radios.Add(radio);
            container.Children.Add(radio);
        }

        _planShapePicker = (radios, row.Candidates);
        return container;
    }

    /// <summary>[UI] Updates a row's consequence footer and child rows once an option becomes checked; the sender's Tag names the row.</summary>
    private void OnOptionCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true, Tag: string rowId } && _choiceRows.TryGetValue(rowId, out var controls))
        {
            UpdateConsequenceFooter(controls);
            RefreshChildPanels(controls);
        }
    }

    /// <summary>[UI] Shows the selected option's child rows and hides the others'.</summary>
    private static void RefreshChildPanels(ChoiceRowControls controls)
    {
        for (var i = 0; i < controls.Radios.Count; i++)
        {
            if (controls.ChildPanels[i] is { } panel)
            {
                panel.IsVisible = controls.Radios[i].IsChecked == true;
            }
        }
    }

    /// <summary>[UI] Shows the selected option's consequence under the row's options, or hides the footer when it has none.</summary>
    private static void UpdateConsequenceFooter(ChoiceRowControls controls)
    {
        var selected = SelectedIndex(controls.Radios);
        var consequence = selected >= 0 ? controls.Options[selected].Consequence : "";
        controls.Footer.Text = consequence;
        controls.Footer.IsVisible = !string.IsNullOrEmpty(consequence);
    }

    /// <summary>[CALC] Which of a row's radios is checked, or -1 when none is.</summary>
    private static int SelectedIndex(IReadOnlyList<RadioButton> radios)
    {
        for (var i = 0; i < radios.Count; i++)
        {
            if (radios[i].IsChecked == true)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>[UI] Closes with the choices accepted.</summary>
    private void OnSaveClick(object? sender, RoutedEventArgs e) => Close(true);

    /// <summary>[UI] Closes without saving.</summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
