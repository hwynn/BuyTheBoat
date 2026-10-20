using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using BuyTheBoat.Domain;

namespace BuyTheBoat.App;

/// <summary>
/// A "build a recurrence rule" form with a live calendar preview, used wherever something needs a date_pattern (the Expense, Earmark, and Transfer tabs).
/// Exposes the built RecurrenceRule, or null while the form isn't valid.
/// </summary>
public partial class RecurrenceRuleEditor : UserControl
{
    // Guards against recalculating on every individual control's default-value
    // assignment while the control is still being constructed.
    private bool _initialized;

    /// <summary>Does the host form ask "when does this stop?" itself (the bill form)? Then the built-in Ends controls are hidden and SetHostEndDate supplies the end date.</summary>
    private bool _hostControlsEnd;
    private DateOnly? _hostUntil;

    // Occurrences a host form highlights, and the caption and legend shown with them (SetHighlight); empty by default.
    private IReadOnlyCollection<DateOnly> _highlightedDates = [];
    private string? _caption;
    private string? _legendText;

    private static readonly IBrush ShortDateHighlightBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0xA0));

    /// <summary>How many dates the occurrence list shows before the toggle to see the rest.</summary>
    private const int DefaultVisibleOccurrenceCount = 5;

    private IReadOnlyList<DateOnly> _allOccurrences = [];
    private bool _showAllOccurrences;

    /// <summary>Dates the schedule skips (RFC 5545 EXDATE, "the glut case"), kept whether or not ExcludedDatesSection is shown.</summary>
    private List<DateOnly> _excludedDates = [];

    // The span a host has limited the pickers to (LimitSelectableDates), or null for no limit; kept so Until's lower bound can be worked out again on every Start change.
    private DateOnly? _outerEarliest;
    private DateOnly? _outerLatest;

    public RecurrenceRuleEditor()
    {
        InitializeComponent();
        PreviewCalendar.Loaded += (_, _) => ShrinkPreviewCalendar();

        StartDatePicker.SelectedDate = DateTime.Today;
        UntilDatePicker.SelectedDate = DateTime.Today.AddYears(3);

        IntervalTextBox.WhenTextChanged(Recalculate);
        ByMonthDayTextBox.WhenTextChanged(Recalculate);
        CountTextBox.WhenTextChanged(Recalculate);
        StartDatePicker.WhenDateChanged(Recalculate);
        UntilDatePicker.WhenDateChanged(Recalculate);

        // Re-applies the highlight when the user pages months, since the calendar reuses its day buttons.
        PreviewCalendar.DisplayDateChanged += (_, _) => ApplyHighlight();
        Loaded += (_, _) => ApplyHighlight();

        _initialized = true;
        Recalculate();
    }

    /// <summary>
    /// [UI] Shrinks the preview calendar to the WPF app's size (about 180 × 170); the day and header buttons are sized by the styles in RecurrenceRuleEditor.axaml.
    /// Fluent's calendar template sets its header row, weekday row and minimum sizes directly on its parts, which styles can't override, so they're changed here once it has loaded.
    /// </summary>
    private void ShrinkPreviewCalendar()
    {
        const double headerRow = 26;
        const double weekdayRow = 18;
        foreach (var part in PreviewCalendar.GetVisualDescendants().OfType<Control>())
        {
            switch (part)
            {
                case Grid { Name: "PART_MonthView" } monthView:
                    monthView.MinHeight = 0;
                    monthView.RowDefinitions[0].Height = new GridLength(weekdayRow);
                    break;
                case Grid { Name: "PART_YearView" } yearView:
                    yearView.MinHeight = 0;
                    break;
                case Border { Name: "BackgroundLayer" } backgroundLayer:
                    // Shades the area under the weekday names, so it starts where that row ends.
                    backgroundLayer.Margin = new Avalonia.Thickness(0, weekdayRow, 0, 0);
                    break;
                case Grid { MinWidth: > 0 } frame when frame.Children.OfType<Grid>().Any(child => child.Name == "PART_MonthView"):
                    frame.MinWidth = 0;
                    frame.RowDefinitions[0].Height = new GridLength(headerRow);
                    break;
            }
        }
    }

    /// <summary>
    /// [UI] Highlights specific occurrences, with a short caption below the list and a legend explaining what the highlight means, since this control doesn't know.
    /// An empty list, or null or blank text, clears that piece.
    /// </summary>
    /// <param name="dates">The occurrences to highlight.</param>
    /// <param name="caption">A short caption shown below the occurrence list, or null to clear it.</param>
    /// <param name="legendText">Explains what the highlight color means, or null to clear it.</param>
    public void SetHighlight(IReadOnlyCollection<DateOnly> dates, string? caption, string? legendText = null)
    {
        _highlightedDates = dates ?? [];
        _caption = caption;
        _legendText = legendText;
        ApplyHighlight();
        ApplyCaption();
        ApplyLegend();
    }

    public RecurrenceRule? Result { get; private set; }

    public event EventHandler? ResultChanged;

    /// <summary>[STEP] For edit mode — pre-fills the form from an existing rule instead of the today/+6-months defaults.</summary>
    /// <param name="rule">The existing rule to load.</param>
    public void LoadFrom(RecurrenceRule rule)
    {
        _initialized = false;
        _showAllOccurrences = false;
        _excludedDates = rule.ExcludedDates.OrderBy(date => date).ToList();

        foreach (var item in FrequencyComboBox.Items.OfType<ComboBoxItem>())
        {
            if ((string?)item.Tag == rule.Frequency.ToString())
            {
                FrequencyComboBox.SelectedItem = item;
                break;
            }
        }

        IntervalTextBox.Text = rule.Interval.ToString();

        foreach (var checkBox in ByDayPanel.Children.OfType<CheckBox>())
        {
            checkBox.IsChecked = rule.ByDay.Contains(Enum.Parse<DayOfWeek>((string)checkBox.Tag!));
        }

        ByMonthDayTextBox.Text = string.Join(", ", rule.ByMonthDay);
        StartDatePicker.SelectedDate = rule.ToOptions().DtStart.ToDateTime(TimeOnly.MinValue);
        EndsOnDateRadio.IsChecked = true;
        UntilDatePicker.SelectedDate = rule.Until.ToDateTime(TimeOnly.MinValue);

        _initialized = true;
        Recalculate();
    }

    /// <summary>[UI] Hides the built-in "Ends" controls so the host form supplies the end date itself — used by the bill form's "when does this stop?" question.</summary>
    public void LetHostControlEndDate()
    {
        _hostControlsEnd = true;
        foreach (var element in new Control[]
                 { EndsHeader, EndsOnDateRadio, UntilDatePicker, EndsAfterCountRadio, CountTextBox, ResolvedUntilText })
        {
            element.IsVisible = false;
        }

        Recalculate();
    }

    /// <summary>[UI] Undoes LetHostControlEndDate, bringing back the built-in "Ends" controls; needed because ExpenseFormPanel reuses one editor, which would otherwise keep them hidden.</summary>
    public void LetSelfControlEndDate()
    {
        if (!_hostControlsEnd)
        {
            return;
        }

        _hostControlsEnd = false;
        _hostUntil = null;
        foreach (var element in new Control[]
                 { EndsHeader, EndsOnDateRadio, UntilDatePicker, EndsAfterCountRadio, CountTextBox, ResolvedUntilText })
        {
            element.IsVisible = true;
        }

        Recalculate();
    }

    /// <summary>[UI] Shows or hides the raw RRULE text box — hidden by default: it takes up space most editing doesn't need, and is meant to be revealed by a host's own "Advanced mode" checkbox rather than always being on screen.</summary>
    /// <param name="isAdvanced">Whether to show the raw RRULE text box.</param>
    public void SetAdvancedMode(bool isAdvanced)
    {
        RruleLabel.IsVisible = isAdvanced;
        RruleStringTextBox.IsVisible = isAdvanced;
    }

    /// <summary>[UI] Reveals the "Skip specific dates" section — hidden by default so Expense/Transfer, which don't offer this freedom, are unaffected. One-way: no matching "hide it again" exists yet, since nothing currently needs it.</summary>
    public void ShowExcludedDatesEditor() => ExcludedDatesSection.IsVisible = true;

    /// <summary>[UI] Replaces the skipped-dates list and shows the section, for pre-filling a goal-health "skip some events" correction as if the user had picked them; refreshes the preview.</summary>
    /// <param name="excludedDates">The complete set of dates to mark skipped.</param>
    public void SetExcludedDates(IReadOnlyList<DateOnly> excludedDates)
    {
        _excludedDates = excludedDates.OrderBy(date => date).ToList();
        ShowExcludedDatesEditor();
        Recalculate();
    }

    /// <summary>[UI] Puts a host's own fields at the top of this editor's left column, such as the Earmark form's amount, so the preview can use that height; null clears it.</summary>
    /// <param name="content">The element to inject, or null to clear it.</param>
    public void SetLeadingContent(Control? content)
    {
        LeadingContentHost.Content = content;
        LeadingContentHost.IsVisible = content is not null;
    }

    /// <summary>[UI] Sets the end date the host chose through its own stop question, refreshing the preview. Null leaves the rule incomplete until one is picked.</summary>
    /// <param name="until">The end date the host chose, or null if none picked yet.</param>
    public void SetHostEndDate(DateOnly? until)
    {
        // Skips an unchanged date, since the host sets the end in response to ResultChanged and would otherwise loop.
        if (_hostUntil == until)
        {
            return;
        }

        _hostUntil = until;
        Recalculate();
    }

    /// <summary>[UI] Limits the Start and Until pickers to the given span, keeping a savings plan inside its goal's dates; null clears that end, and Until also never goes before Start (ApplyDateBounds).</summary>
    /// <param name="earliest">The earliest pickable day, or null for no lower bound.</param>
    /// <param name="latest">The latest pickable day, or null for no upper bound.</param>
    public void LimitSelectableDates(DateOnly? earliest, DateOnly? latest)
    {
        _outerEarliest = earliest;
        _outerLatest = latest;
        ApplyDateBounds();
    }

    /// <summary>[UI] Sets what the date pickers allow: Start within the host's limit (LimitSelectableDates), and Until no earlier than Start, since that would make an empty schedule; runs when the limit or Start changes.</summary>
    private void ApplyDateBounds()
    {
        var outerStart = _outerEarliest?.ToDateTime(TimeOnly.MinValue);
        var outerEnd = _outerLatest?.ToDateTime(TimeOnly.MinValue);

        StartDatePicker.DisplayDateStart = outerStart;
        StartDatePicker.DisplayDateEnd = outerEnd;

        // Until's floor is the LATER of the outer cap's earliest and the live
        // Start, so tightening one never loosens the other.
        var untilFloor = StartDatePicker.SelectedDate is { } start
            ? (outerStart is { } cap && cap > start ? cap : start)
            : outerStart;
        UntilDatePicker.DisplayDateStart = untilFloor;
        UntilDatePicker.DisplayDateEnd = outerEnd;
    }

    /// <summary>[CALC] The recurrence entered so far, minus its end date — what a loan payoff date is computed from. Throws a friendly error if the schedule fields aren't valid yet.</summary>
    public (RecurrenceFrequency Frequency, DateOnly Start, int Interval, IReadOnlyList<DayOfWeek> ByDay, IReadOnlyList<int> ByMonthDay) ReadScheduleParts()
    {
        var (frequency, start, interval, byDay, byMonthDay) = ReadScheduleFields();
        return (frequency, start, interval, byDay, byMonthDay);
    }

    /// <summary>[UI] Rebuilds the rule and preview after the frequency dropdown changes.</summary>
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => Recalculate();

    /// <summary>[UI] Rebuilds the rule and preview after a checkbox or radio button change.</summary>
    private void OnCheckChanged(object? sender, RoutedEventArgs e) => Recalculate();

    /// <summary>[UI] Builds the rule from the form, refreshes the preview and occurrence list, and tells the host the result changed; shows the problem instead when the form isn't valid.</summary>
    private void Recalculate()
    {
        if (!_initialized)
        {
            return;
        }

        UpdateFormVisibility();
        ErrorText.Text = string.Empty;

        // Keeps Until from going before the new Start; an end date already before it is flagged below.
        ApplyDateBounds();

        // Shown even while another field is invalid mid-edit; a fresh list, so the box notices the change.
        ExcludedDatesListBox.ItemsSource = _excludedDates.ToList();

        try
        {
            var options = ReadOptionsFromForm();
            var rule = RecurrenceRule.Create(options);

            RruleStringTextBox.Text = rule.ToRruleString();
            ResolvedUntilText.Text = EndsAfterCountRadio.IsChecked == true
                ? $"Effective until: {rule.Until:D}"
                : string.Empty;

            var occurrences = rule.GetOccurrences();

            PreviewCalendar.SelectedDates.Clear();
            foreach (var date in occurrences)
            {
                PreviewCalendar.SelectedDates.Add(date.ToDateTime(TimeOnly.MinValue));
            }
            PreviewCalendar.DisplayDate = rule.ToOptions().DtStart.ToDateTime(TimeOnly.MinValue);

            _allOccurrences = occurrences;
            UpdateOccurrencesDisplay();
            UpdateExcludableOccurrences(rule);

            // Flags a valid rule that lands on no dates; not shown with an error, which the catch below already reports.
            EmptyScheduleNote.IsVisible = occurrences.Count == 0;

            Result = rule;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            PreviewCalendar.SelectedDates.Clear();
            _allOccurrences = [];
            UpdateOccurrencesDisplay();
            EmptyScheduleNote.IsVisible = false;
            ExcludableOccurrenceComboBox.ItemsSource = null;
            RruleStringTextBox.Text = string.Empty;
            ResolvedUntilText.Text = string.Empty;
            Result = null;
        }

        ResultChanged?.Invoke(this, EventArgs.Empty);

        // Re-applies the host's highlight, caption, and legend, since the lists were just rebuilt.
        ApplyHighlight();
        ApplyCaption();
        ApplyLegend();
    }

    /// <summary>[UI] Colors the visible calendar days in _highlightedDates and clears the color from ones no longer in it, leaving other days alone so the calendar's own "today" and selection styling stays.</summary>
    private void ApplyHighlight()
    {
        if (!PreviewCalendar.IsLoaded)
        {
            return; // no day buttons made yet — Loaded/DisplayDateChanged call back in
        }

        foreach (var dayButton in PreviewCalendar.GetVisualDescendants().OfType<CalendarDayButton>())
        {
            var isHighlighted = dayButton.DataContext is DateTime day
                                 && _highlightedDates.Contains(DateOnly.FromDateTime(day));
            if (isHighlighted)
            {
                dayButton.Background = ShortDateHighlightBrush;
            }
            else if (ReferenceEquals(dayButton.Background, ShortDateHighlightBrush))
            {
                dayButton.ClearValue(BackgroundProperty);
            }
        }
    }

    /// <summary>[UI] Shows at most DefaultVisibleOccurrenceCount dates unless the user has toggled "Show all" — the toggle button's own label carries the total count regardless of which state it's in, so that's visible even while collapsed.</summary>
    private void UpdateOccurrencesDisplay()
    {
        var visible = _showAllOccurrences
            ? _allOccurrences
            : _allOccurrences.Take(DefaultVisibleOccurrenceCount);

        // Abbreviated weekday name ("ddd"), paired with the list column
        // being wide enough to fit it (see the XAML).
        OccurrencesListBox.ItemsSource = visible
            .Select(date => date.ToString("ddd, MMM d, yyyy"))
            .ToList();

        if (_allOccurrences.Count <= DefaultVisibleOccurrenceCount)
        {
            ToggleOccurrencesButton.IsVisible = false;
            return;
        }

        ToggleOccurrencesButton.IsVisible = true;
        ToggleOccurrencesButton.Content = _showAllOccurrences
            ? "Show fewer"
            : $"Show all {_allOccurrences.Count}";
    }

    /// <summary>[UI] Switches the occurrence list between the first few dates and all of them.</summary>
    private void OnToggleOccurrencesClick(object? sender, RoutedEventArgs e)
    {
        _showAllOccurrences = !_showAllOccurrences;
        UpdateOccurrencesDisplay();
    }

    /// <summary>[UI] Refreshes the dates the "Skip specific dates" picker offers: the schedule's occurrences (read without exclusions) minus the ones already skipped.</summary>
    /// <param name="rule">The just-built rule to read candidate dates from.</param>
    private void UpdateExcludableOccurrences(RecurrenceRule rule)
    {
        var stillExcludable = rule.WithExcludedDates([]).GetOccurrences()
            .Except(_excludedDates)
            .ToList();

        ExcludableOccurrenceComboBox.ItemsSource = stillExcludable;
        if (stillExcludable.Count > 0)
        {
            ExcludableOccurrenceComboBox.SelectedIndex = 0;
        }
    }

    /// <summary>[UI] Moves the picker's currently-selected date from "scheduled" to "skipped" and refreshes the preview — the button beside ExcludableOccurrenceComboBox.</summary>
    private void OnSkipDateClick(object? sender, RoutedEventArgs e)
    {
        if (ExcludableOccurrenceComboBox.SelectedItem is not DateOnly selected)
        {
            return;
        }

        _excludedDates = [.. _excludedDates, selected];
        _excludedDates.Sort();
        Recalculate();
    }

    /// <summary>[UI] Moves one date back from "skipped" to "scheduled" — the Restore button next to each row in ExcludedDatesListBox, reading which date via that row's own Tag.</summary>
    private void OnRestoreExcludedDateClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not DateOnly toRestore)
        {
            return;
        }

        _excludedDates = _excludedDates.Where(date => date != toRestore).ToList();
        Recalculate();
    }

    /// <summary>[UI] Toggles the host-supplied caption's own visibility (e.g. "Projected short") — the occurrence list's height is capped unconditionally instead (see the XAML), so this doesn't need to bound it itself.</summary>
    private void ApplyCaption()
    {
        var hasCaption = !string.IsNullOrWhiteSpace(_caption);
        CaptionText.Text = _caption ?? string.Empty;
        CaptionText.IsVisible = hasCaption;
    }

    /// <summary>[UI] Shows or hides the color legend below the caption; separate from the caption, since a host may give one without the other.</summary>
    private void ApplyLegend()
    {
        var hasLegend = !string.IsNullOrWhiteSpace(_legendText);
        LegendText.Text = _legendText ?? string.Empty;
        LegendPanel.IsVisible = hasLegend;
    }

    /// <summary>[UI] Frequency drives which fields are even meaningful: BYDAY only makes sense for Weekly, BYMONTHDAY only for Monthly/Yearly.</summary>
    private void UpdateFormVisibility()
    {
        var frequency = SelectedFrequency();

        IntervalUnitLabel.Text = frequency switch
        {
            RecurrenceFrequency.Daily => "day(s)",
            RecurrenceFrequency.Weekly => "week(s)",
            RecurrenceFrequency.Monthly => "month(s)",
            RecurrenceFrequency.Yearly => "year(s)",
            _ => string.Empty,
        };

        var showByDay = frequency == RecurrenceFrequency.Weekly;
        ByDayLabel.IsVisible = showByDay;
        ByDayPanel.IsVisible = showByDay;
        ByDayHint.IsVisible = showByDay;

        var showByMonthDay = frequency is RecurrenceFrequency.Monthly or RecurrenceFrequency.Yearly;
        ByMonthDayLabel.IsVisible = showByMonthDay;
        ByMonthDayTextBox.IsVisible = showByMonthDay;

        UntilDatePicker.IsEnabled = EndsOnDateRadio.IsChecked == true;
        CountTextBox.IsEnabled = EndsAfterCountRadio.IsChecked == true;
    }

    /// <summary>[CALC] The repeat frequency picked in the dropdown.</summary>
    private RecurrenceFrequency SelectedFrequency()
    {
        var tag = (string)((ComboBoxItem)FrequencyComboBox.SelectedItem!).Tag!;
        return Enum.Parse<RecurrenceFrequency>(tag);
    }

    /// <summary>[CALC] Reads just the repeat-schedule fields the user has entered (frequency, start, interval, days), without an end date — the shared half of the form ReadOptionsFromForm and the public ReadScheduleParts both build on.</summary>
    private (RecurrenceFrequency Frequency, DateOnly Start, int Interval, List<DayOfWeek> ByDay, List<int> ByMonthDay) ReadScheduleFields()
    {
        if (StartDatePicker.SelectedDate is not { } start)
        {
            throw new InvalidOperationException("Pick a start date.");
        }

        var interval = int.TryParse(IntervalTextBox.Text, out var parsedInterval) && parsedInterval > 0
            ? parsedInterval
            : throw new InvalidOperationException("\"Every\" must be a whole number of 1 or more.");

        var byDay = ByDayPanel.Children
            .OfType<CheckBox>()
            .Where(checkBox => checkBox.IsChecked == true)
            .Select(checkBox => Enum.Parse<DayOfWeek>((string)checkBox.Tag!))
            .ToList();

        var byMonthDay = ParseByMonthDay(ByMonthDayTextBox.Text ?? string.Empty);

        return (SelectedFrequency(), DateOnly.FromDateTime(start), interval, byDay, byMonthDay);
    }

    /// <summary>[CALC] Reads the whole form, end condition included, into the options a RecurrenceRule is built from; throws a friendly message for the first field that isn't valid.</summary>
    private RecurrenceRuleOptions ReadOptionsFromForm()
    {
        var (frequency, start, interval, byDay, byMonthDay) = ReadScheduleFields();

        DateOnly? until = null;
        int? count = null;
        if (_hostControlsEnd)
        {
            until = _hostUntil ?? throw new InvalidOperationException("Choose when this stops.");
        }
        else if (EndsOnDateRadio.IsChecked == true)
        {
            until = UntilDatePicker.SelectedDate is { } untilDate
                ? DateOnly.FromDateTime(untilDate)
                : throw new InvalidOperationException("Pick an end date.");
        }
        else
        {
            count = int.TryParse(CountTextBox.Text, out var parsedCount) && parsedCount > 0
                ? parsedCount
                : throw new InvalidOperationException("Enter a whole number of 1 or more occurrences.");
        }

        return new RecurrenceRuleOptions
        {
            Frequency = frequency,
            DtStart = start,
            Interval = interval,
            ByDay = byDay,
            ByMonthDay = byMonthDay,
            Until = until,
            Count = count,
            ExcludedDates = _excludedDates,
        };
    }

    /// <summary>[CALC] Reads the comma-separated days of the month, like "9, 25"; throws a friendly message for anything outside 1–31.</summary>
    private static List<int> ParseByMonthDay(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var days = new List<int>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var day) || day is < 1 or > 31)
            {
                throw new InvalidOperationException($"\"{part}\" isn't a valid day of the month (1-31).");
            }

            days.Add(day);
        }

        return days;
    }
}
