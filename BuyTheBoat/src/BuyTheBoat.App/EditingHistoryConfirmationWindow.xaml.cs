using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

/// <summary>
/// Shows request.Rows, the questions and announcements FinancePatternSaveConfirmation or EarmarkPatternSaveConfirmation raised for one save, top to bottom, and reports the selections back as a ConfirmationOutcome.
/// It never decides which rows exist or what an answer means; the confirmation class reads each index back into a decision.
/// It's a deliberately plain WPF look for now; a styled version is the eventual goal.
/// </summary>
/// <remarks>
/// Built in code-behind rather than with an ItemsControl and DataTemplates, since the rows are immutable records, each ChoiceRow needs its own radio group, and the consequence footer updates live.
/// Those are a few lines here, but would each need a per-row view model and converters in XAML.
/// </remarks>
public partial class EditingHistoryConfirmationWindow : Window
{
    /// <summary>
    /// One drawn ChoiceRow: its radios and options in order, the consequence footer, and each option's child-row panel (null for a leaf), shown only while that option is selected.
    /// Kept by row Id so ToOutcome can report each selection and OnOptionChecked can refresh the right footer and panels.
    /// </summary>
    private sealed record ChoiceRowControls(
        IReadOnlyList<RadioButton> Radios, IReadOnlyList<ChoiceOption> Options, TextBlock Footer,
        IReadOnlyList<FrameworkElement?> ChildPanels);

    private readonly Dictionary<string, ChoiceRowControls> _choiceRows = new();

    /// <summary>The plan-shape picker's radios and candidates, so ToOutcome can report the picked plan as ChosenPlanShape; null when no picker was shown, which reads as the default.</summary>
    private (IReadOnlyList<RadioButton> Radios, IReadOnlyList<FinancePatternSaveConfirmation.PlanShapeCandidate> Candidates)? _planShapePicker;

    public EditingHistoryConfirmationWindow(
        ImplicitChangeConfirmationRequest request, int pageNumber = 1, int pageCount = 1)
    {
        InitializeComponent();

        // Limits the height to the screen, so a page too tall to split scrolls inside the window instead of pushing the buttons off-screen.
        MaxHeight = SystemParameters.WorkArea.Height * 0.92;

        DescriptionText.Text = request.Description;

        // "Continue…" while more pages follow, "Save" on the last — the only thing
        // the popup itself knows about pagination.
        CommitButton.Content = request.IsFinalPage ? "Save" : "Continue…";

        if (pageCount > 1)
        {
            PageIndicator.Text = $"Page {pageNumber} of {pageCount}";
            PageIndicator.Visibility = Visibility.Visible;
        }

        foreach (var row in request.Rows)
        {
            RowsPanel.Children.Add(BuildRowControl(row));
        }
    }

    /// <summary>[CALC] The raw selections to hand the wrapper — one option index per ChoiceRow drawn, keyed by row Id, plus the picked plan shape when a CandidatePickerRow was drawn. Call after ShowDialog returns, passing its result as proceed.</summary>
    /// <param name="proceed">The dialog result — false when the user cancelled.</param>
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
    private UIElement BuildRowControl(ConfirmationRow row) => row switch
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
    private FrameworkElement BuildChoice(ChoiceRow row)
    {
        var container = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

        container.Children.Add(new TextBlock
        {
            Text = row.Question,
            FontWeight = FontWeights.Bold,
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
            FontStyle = FontStyles.Italic,
            Margin = new Thickness(20, 4, 0, 0),
            Visibility = Visibility.Collapsed,
        };

        var radios = new List<RadioButton>(row.Options.Count);
        var childPanels = new List<FrameworkElement?>(row.Options.Count);
        for (var i = 0; i < row.Options.Count; i++)
        {
            var option = row.Options[i];
            var radio = new RadioButton
            {
                Content = option.Label,
                // Unique GroupName per row keeps each question's radios
                // mutually exclusive without bleeding into the next question's.
                GroupName = row.Id,
                IsChecked = i == row.DefaultIndex,
                Margin = sideBySide ? new Thickness(0, 0, 12, 0) : new Thickness(0, 0, 0, 4),
                Tag = row.Id,
            };
            radio.Checked += OnOptionChecked;
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

                childPanel.Visibility = i == row.DefaultIndex ? Visibility.Visible : Visibility.Collapsed;
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
    private FrameworkElement BuildCandidatePicker(CandidatePickerRow row)
    {
        var container = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

        container.Children.Add(new TextBlock
        {
            Text = row.Question,
            FontWeight = FontWeights.Bold,
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
                Content = row.Candidates[i].Label,
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

    /// <summary>[UI] Updates a row's consequence footer and child rows to the option now checked; the sender's Tag names the row.</summary>
    private void OnOptionChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string rowId } && _choiceRows.TryGetValue(rowId, out var controls))
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
                panel.Visibility = controls.Radios[i].IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private static void UpdateConsequenceFooter(ChoiceRowControls controls)
    {
        var selected = SelectedIndex(controls.Radios);
        var consequence = selected >= 0 ? controls.Options[selected].Consequence : "";
        controls.Footer.Text = consequence;
        controls.Footer.Visibility = string.IsNullOrEmpty(consequence) ? Visibility.Collapsed : Visibility.Visible;
    }

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

    private void OnSaveClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
