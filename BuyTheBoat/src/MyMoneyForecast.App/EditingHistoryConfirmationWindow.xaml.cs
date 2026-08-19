using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MyMoneyForecast.App;

// planning/28: a dumb renderer over request.Rows. Each row is one question or
// announcement FinancePatternSaveConfirmation decided to raise on a single
// save; this window draws them top-to-bottom and reports the raw selections
// back as a ConfirmationOutcome (one option index per ChoiceRow it drew). It
// never decides which rows exist, what they mean, or that one answer might
// unlock another — and, with the row-keyed outcome, it no longer even knows
// what a selection means: the wrapper reads each index back into a decision.
// Still deliberately the minimal plain-WPF look (the styled mockup is the
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
    // (for the consequence text), and the footer TextBlock under them. Keyed by
    // row Id so ToOutcome can report each selection and the one shared
    // OnOptionChecked handler can refresh the right footer.
    private sealed record ChoiceRowControls(
        IReadOnlyList<RadioButton> Radios, IReadOnlyList<ChoiceOption> Options, TextBlock Footer);

    private readonly Dictionary<string, ChoiceRowControls> _choiceRows = new();

    public EditingHistoryConfirmationWindow(ImplicitChangeConfirmationRequest request)
    {
        InitializeComponent();

        DescriptionText.Text = request.Description;
        foreach (var row in request.Rows)
        {
            RowsPanel.Children.Add(BuildRowControl(row));
        }
    }

    /// <summary>[CALC] The raw selections to hand the wrapper — one option index per ChoiceRow drawn, keyed by row Id. Call after ShowDialog returns, passing its result as proceed. No CandidatePickerRow/CheckboxRiderRow is drawn yet, so ChosenPlanShape and Riders stay empty.</summary>
    /// <param name="proceed">The dialog result — false when the user cancelled.</param>
    public ConfirmationOutcome ToOutcome(bool proceed) => new()
    {
        Proceed = proceed,
        ChosenOptionIndex = _choiceRows.ToDictionary(entry => entry.Key, entry => SelectedIndex(entry.Value.Radios)),
    };

    /// <summary>[CALC] Builds the one control that renders a single row, dispatched on its kind.</summary>
    private UIElement BuildRowControl(ConfirmationRow row) => row switch
    {
        AnnouncementRow announcement => BuildAnnouncement(announcement),
        ChoiceRow choice => BuildChoice(choice),
        // CandidatePickerRow / CheckboxRiderRow aren't produced by
        // FinancePatternSaveConfirmation.BuildRows yet (their choices aren't
        // surfaced today), so they never reach here. Fail loud rather than
        // draw nothing if a later thread emits one without adding its template.
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
        for (var i = 0; i < row.Options.Count; i++)
        {
            var radio = new RadioButton
            {
                Content = row.Options[i].Label,
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
        }

        container.Children.Add(optionsPanel);
        container.Children.Add(footer);

        var controls = new ChoiceRowControls(radios, row.Options, footer);
        _choiceRows[row.Id] = controls;
        UpdateConsequenceFooter(controls); // set the footer for the default selection up front

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
