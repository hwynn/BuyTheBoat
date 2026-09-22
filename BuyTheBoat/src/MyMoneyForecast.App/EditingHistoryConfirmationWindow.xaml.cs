using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// A dumb renderer over request.Rows. Each row is one question or
// announcement FinancePatternSaveConfirmation decided to raise on a single
// save; this window draws them top-to-bottom and reports the raw selections
// back as a ConfirmationOutcome (one option index per ChoiceRow it drew). It
// never decides which rows exist, what they mean, or that one answer might
// unlock another — and, with the row-keyed outcome, it no longer even knows
// what a selection means: the wrapper reads each index back into a decision.
// Still deliberately the minimal plain-WPF look (a styled version is the
// eventual target, not this).
//
// Built in code-behind rather than an ItemsControl + DataTemplates on purpose:
// the ConfirmationRow records are immutable (no mutable SelectedIndex to bind
// two-way against), each ChoiceRow needs its own independent radio group, and
// the selected-option consequence footer has to update live — all a few lines
// here, but each needs its own per-row viewmodel + converters through pure XAML
// binding. Nothing about the domain lives in this file regardless of how it's
// drawn.
public partial class EditingHistoryConfirmationWindow : Window
{
    // One entry per ChoiceRow drawn — its radios (in option order), its options
    // (for the consequence text), the footer TextBlock under them, and the
    // per-option child-row panel (null for a leaf option) shown only while that
    // option is selected (dynamic reveal). Keyed by row Id so ToOutcome can
    // report each selection and the one shared OnOptionChecked handler can
    // refresh the right footer and child panels.
    private sealed record ChoiceRowControls(
        IReadOnlyList<RadioButton> Radios, IReadOnlyList<ChoiceOption> Options, TextBlock Footer,
        IReadOnlyList<FrameworkElement?> ChildPanels);

    private readonly Dictionary<string, ChoiceRowControls> _choiceRows = new();

    // The one plan-shape picker, if this request drew one — its
    // radios (in candidate order) and the candidates themselves, so ToOutcome can
    // report the picked candidate's own plan as ChosenPlanShape. Null when no
    // picker was shown (most saves), which reads back as the default candidate.
    private (IReadOnlyList<RadioButton> Radios, IReadOnlyList<FinancePatternSaveConfirmation.PlanShapeCandidate> Candidates)? _planShapePicker;

    public EditingHistoryConfirmationWindow(
        ImplicitChangeConfirmationRequest request, int pageNumber = 1, int pageCount = 1)
    {
        InitializeComponent();

        // Cap at the working screen so a tall page — or a single row too tall to
        // split — scrolls inside the window instead of pushing the buttons
        // off-screen; the scroll fallback behind height-based pagination.
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
        // today) lays them one per line, matching the old fixed sections.
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

            // An option's own child rows (dynamic reveal) — built recursively and
            // indented under it, shown only while this option is selected. Their
            // own ChoiceRows register themselves in _choiceRows too (via
            // BuildRowControl), so ToOutcome reports their selections like any
            // other, whether or not they happen to be visible.
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

    // The one place a selected option's consequence is written — replaces the
    // hand-copied per-section Update*Visibility handlers the old window had. The
    // sender's Tag names its row; refresh that row's footer to whatever option
    // is now checked.
    private void OnOptionChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string rowId } && _choiceRows.TryGetValue(rowId, out var controls))
        {
            UpdateConsequenceFooter(controls);
            RefreshChildPanels(controls);
        }
    }

    // Show the selected option's child rows and hide the rest — the dynamic
    // reveal. A group's radios fire Checked on the newly-selected one, so
    // refreshing every option's panel here keeps them all in sync.
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
