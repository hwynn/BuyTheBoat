using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MyMoneyForecast.Domain;

namespace MyMoneyForecast.App;

// A self-contained "build me a recurrence rule" form + live calendar preview,
// meant to be embedded wherever something needs a date_pattern — pattern
// creation popups, eventually. Exposes the computed RecurrenceRule (or null,
// while the form is in an invalid state) rather than owning a window itself.
public partial class RecurrenceRuleEditor : UserControl
{
    // Guards against recalculating on every individual control's default-value
    // assignment while the control is still being constructed.
    private bool _initialized;

    // planning/15 item D: when a host form owns the "when does this stop?"
    // question (the bill form), the built-in Ends controls are hidden and the
    // end date is supplied from outside via SetHostEndDate instead of the radios.
    private bool _hostControlsEnd;
    private DateOnly? _hostUntil;

    // planning/22 §6b, SETTLED 2026-08-05 — a host form can mark specific
    // occurrences with an altered highlight color and show a short caption
    // below the list (e.g. "Projected short," with the short occurrences
    // highlighted). Both default to "nothing to flag," so every existing
    // caller that never calls SetHighlight is unaffected.
    private IReadOnlyCollection<DateOnly> _highlightedDates = [];
    private string? _caption;
    private string? _legendText;

    private static readonly Brush ShortDateHighlightBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0xA0));

    // The author's own call, 2026-08-06: the occurrence list defaults to
    // showing only this many dates, with a toggle to see the rest — kept as
    // a named constant specifically so it's easy to retune later.
    private const int DefaultVisibleOccurrenceCount = 5;

    private IReadOnlyList<DateOnly> _allOccurrences = [];
    private bool _showAllOccurrences;

    public RecurrenceRuleEditor()
    {
        InitializeComponent();

        StartDatePicker.SelectedDate = DateTime.Today;
        UntilDatePicker.SelectedDate = DateTime.Today.AddYears(3);

        // The calendar recycles its CalendarDayButtons as the user pages
        // between months, rather than keeping every date's button alive at
        // once — the highlight has to be re-applied every time the visible
        // days change, not just when the schedule fields do.
        PreviewCalendar.DisplayDateChanged += (_, _) => ApplyHighlight();
        Loaded += (_, _) => ApplyHighlight();

        _initialized = true;
        Recalculate();
    }

    /// <summary>[UI] Marks specific occurrences with an altered highlight color, shows a short caption below the occurrence list (planning/22 §6b), and — since this control has no idea what a highlighted date actually represents, only that it's highlighted — a caller-supplied legend explaining what the highlight color means (author, 2026-08-08). An empty list, and null/blank text for either string, clears that piece back to the plain preview.</summary>
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

    // For edit mode — pre-fills the form from an existing rule instead of the
    // today/+6-months defaults.
    public void LoadFrom(RecurrenceRule rule)
    {
        _initialized = false;
        _showAllOccurrences = false;

        foreach (var item in FrequencyComboBox.Items.OfType<ComboBoxItem>())
        {
            if ((string)item.Tag == rule.Frequency.ToString())
            {
                FrequencyComboBox.SelectedItem = item;
                break;
            }
        }

        IntervalTextBox.Text = rule.Interval.ToString();

        foreach (var checkBox in ByDayPanel.Children.OfType<CheckBox>())
        {
            checkBox.IsChecked = rule.ByDay.Contains(Enum.Parse<DayOfWeek>((string)checkBox.Tag));
        }

        ByMonthDayTextBox.Text = string.Join(", ", rule.ByMonthDay);
        StartDatePicker.SelectedDate = rule.Start.ToDateTime(TimeOnly.MinValue);
        EndsOnDateRadio.IsChecked = true;
        UntilDatePicker.SelectedDate = rule.Until.ToDateTime(TimeOnly.MinValue);

        _initialized = true;
        Recalculate();
    }

    /// <summary>[UI] Hides the built-in "Ends" controls so the host form supplies the end date itself — used by the bill form's "when does this stop?" question.</summary>
    public void LetHostControlEndDate()
    {
        _hostControlsEnd = true;
        foreach (var element in new UIElement[]
                 { EndsHeader, EndsOnDateRadio, UntilDatePicker, EndsAfterCountRadio, CountTextBox, ResolvedUntilText })
        {
            element.Visibility = Visibility.Collapsed;
        }

        Recalculate();
    }

    /// <summary>[UI] Undoes LetHostControlEndDate — restores the built-in "Ends" controls so the editor decides its own end date again. Needed now that a host form (ExpenseFormPanel) is one long-lived instance reused across every open rather than a fresh window each time: without this, switching from the bill form's "when does this stop?" question to any other mode would leave the Ends controls hidden for good.</summary>
    public void LetSelfControlEndDate()
    {
        if (!_hostControlsEnd)
        {
            return;
        }

        _hostControlsEnd = false;
        _hostUntil = null;
        foreach (var element in new UIElement[]
                 { EndsHeader, EndsOnDateRadio, UntilDatePicker, EndsAfterCountRadio, CountTextBox, ResolvedUntilText })
        {
            element.Visibility = Visibility.Visible;
        }

        Recalculate();
    }

    /// <summary>[UI] Shows or hides the raw RRULE text box — hidden by default (the author's own call, 2026-08-06): it takes up space most editing doesn't need, and is meant to be revealed by a host's own "Advanced mode" checkbox rather than always being on screen.</summary>
    public void SetAdvancedMode(bool isAdvanced)
    {
        var visibility = isAdvanced ? Visibility.Visible : Visibility.Collapsed;
        RruleLabel.Visibility = visibility;
        RruleStringTextBox.Visibility = visibility;
    }

    /// <summary>[UI] Lets a host inject its own field(s) at the top of this editor's own left column — e.g. Earmark's "Amount per occurrence," so the right-side preview can use the vertical space that would otherwise sit empty above the recurrence fields (settled-designs.html Earmark·1's Placement C, 2026-08-06). Null clears it back to nothing, same as before this existed — every other current caller (Expense, Transfer) is unaffected unless it calls this too.</summary>
    public void SetLeadingContent(UIElement? content)
    {
        LeadingContentHost.Content = content;
        LeadingContentHost.Visibility = content is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>[UI] Sets the end date the host chose through its own stop question, refreshing the preview. Null leaves the rule incomplete until one is picked.</summary>
    public void SetHostEndDate(DateOnly? until)
    {
        // No-op when unchanged: the host recomputes the end in response to
        // ResultChanged, so without this guard SetHostEndDate → Recalculate →
        // ResultChanged → SetHostEndDate would loop.
        if (_hostUntil == until)
        {
            return;
        }

        _hostUntil = until;
        Recalculate();
    }

    /// <summary>[CALC] The recurrence entered so far, minus its end date — what a loan payoff date is computed from. Throws a friendly error if the schedule fields aren't valid yet.</summary>
    public (RecurrenceFrequency Frequency, DateOnly Start, int Interval, IReadOnlyList<DayOfWeek> ByDay, IReadOnlyList<int> ByMonthDay) ReadScheduleParts()
    {
        var (frequency, start, interval, byDay, byMonthDay) = ReadScheduleFields();
        return (frequency, start, interval, byDay, byMonthDay);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => Recalculate();

    private void OnTextChanged(object sender, TextChangedEventArgs e) => Recalculate();

    private void OnCheckChanged(object sender, RoutedEventArgs e) => Recalculate();

    private void Recalculate()
    {
        if (!_initialized)
        {
            return;
        }

        UpdateFormVisibility();
        ErrorText.Text = string.Empty;

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
            PreviewCalendar.DisplayDate = rule.Start.ToDateTime(TimeOnly.MinValue);

            _allOccurrences = occurrences;
            UpdateOccurrencesDisplay();

            Result = rule;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            PreviewCalendar.SelectedDates.Clear();
            _allOccurrences = [];
            UpdateOccurrencesDisplay();
            RruleStringTextBox.Text = string.Empty;
            ResolvedUntilText.Text = string.Empty;
            Result = null;
        }

        ResultChanged?.Invoke(this, EventArgs.Empty);

        // Schedule edits rebuild SelectedDates/OccurrencesListBox from
        // scratch — reapply whatever highlight/caption/legend the host last
        // set so it survives the user continuing to edit the form afterward.
        ApplyHighlight();
        ApplyCaption();
        ApplyLegend();
    }

    // Colors whichever currently-realized CalendarDayButtons fall in
    // _highlightedDates, and clears the color from any we previously set
    // that are no longer in that set. A button we never touched is left
    // alone, so the calendar's own "today"/selected styling isn't disturbed.
    private void ApplyHighlight()
    {
        if (!PreviewCalendar.IsLoaded)
        {
            return; // no CalendarDayButtons realized yet — Loaded/DisplayDateChanged call back in
        }

        foreach (var dayButton in FindVisualChildren<CalendarDayButton>(PreviewCalendar))
        {
            var isHighlighted = dayButton.DataContext is DateTime day
                                 && _highlightedDates.Contains(DateOnly.FromDateTime(day));
            if (isHighlighted)
            {
                dayButton.Background = ShortDateHighlightBrush;
            }
            else if (ReferenceEquals(dayButton.Background, ShortDateHighlightBrush))
            {
                dayButton.ClearValue(Control.BackgroundProperty);
            }
        }
    }

    // Shows at most DefaultVisibleOccurrenceCount dates unless the user has
    // toggled "Show all" — the toggle button's own label carries the total
    // count regardless of which state it's in, so that's visible even while
    // collapsed.
    private void UpdateOccurrencesDisplay()
    {
        var visible = _showAllOccurrences
            ? _allOccurrences
            : _allOccurrences.Take(DefaultVisibleOccurrenceCount);

        // "dddd, MMMM d, yyyy" (original) -> "MMM d, yyyy" (2026-08-06,
        // width pass — matched settled-designs.html's own "Feb 15, 2026,"
        // no weekday) -> "ddd, MMM d, yyyy" (this pass, same day — the
        // author asked for the weekday back: "It would be helpful if the
        // dates ... said the name of the week they occurred on"). Abbreviated
        // rather than the original's full weekday name, paired with the
        // list column widening back up to fit it (see the XAML).
        OccurrencesListBox.ItemsSource = visible
            .Select(date => date.ToString("ddd, MMM d, yyyy"))
            .ToList();

        if (_allOccurrences.Count <= DefaultVisibleOccurrenceCount)
        {
            ToggleOccurrencesButton.Visibility = Visibility.Collapsed;
            return;
        }

        ToggleOccurrencesButton.Visibility = Visibility.Visible;
        ToggleOccurrencesButton.Content = _showAllOccurrences
            ? "Show fewer"
            : $"Show all {_allOccurrences.Count}";
    }

    private void OnToggleOccurrencesClick(object sender, RoutedEventArgs e)
    {
        _showAllOccurrences = !_showAllOccurrences;
        UpdateOccurrencesDisplay();
    }

    // Now only toggles the host-supplied caption's own visibility
    // (planning/22 §6b's "Projected short," etc.) — the occurrence list's
    // height is capped unconditionally instead (see the XAML), so this no
    // longer needs to bound it itself.
    private void ApplyCaption()
    {
        var hasCaption = !string.IsNullOrWhiteSpace(_caption);
        CaptionText.Text = _caption ?? string.Empty;
        CaptionText.Visibility = hasCaption ? Visibility.Visible : Visibility.Collapsed;
    }

    // Author, 2026-08-08: the color-key row beneath the caption — same
    // on/off shape ApplyCaption already uses, own element since a caption
    // ("Keeps falling short") and a legend ("highlighted = this release
    // came up short") answer different questions and a host might supply
    // one without the other.
    private void ApplyLegend()
    {
        var hasLegend = !string.IsNullOrWhiteSpace(_legendText);
        LegendText.Text = _legendText ?? string.Empty;
        LegendPanel.Visibility = hasLegend ? Visibility.Visible : Visibility.Collapsed;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    // Frequency drives which fields are even meaningful: BYDAY only makes sense
    // for Weekly, BYMONTHDAY only for Monthly/Yearly.
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
        ByDayLabel.Visibility = showByDay ? Visibility.Visible : Visibility.Collapsed;
        ByDayPanel.Visibility = showByDay ? Visibility.Visible : Visibility.Collapsed;
        ByDayHint.Visibility = showByDay ? Visibility.Visible : Visibility.Collapsed;

        var showByMonthDay = frequency is RecurrenceFrequency.Monthly or RecurrenceFrequency.Yearly;
        ByMonthDayLabel.Visibility = showByMonthDay ? Visibility.Visible : Visibility.Collapsed;
        ByMonthDayTextBox.Visibility = showByMonthDay ? Visibility.Visible : Visibility.Collapsed;

        UntilDatePicker.IsEnabled = EndsOnDateRadio.IsChecked == true;
        CountTextBox.IsEnabled = EndsAfterCountRadio.IsChecked == true;
    }

    private RecurrenceFrequency SelectedFrequency()
    {
        var tag = (string)((ComboBoxItem)FrequencyComboBox.SelectedItem).Tag;
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
            .Select(checkBox => Enum.Parse<DayOfWeek>((string)checkBox.Tag))
            .ToList();

        var byMonthDay = ParseByMonthDay(ByMonthDayTextBox.Text);

        return (SelectedFrequency(), DateOnly.FromDateTime(start), interval, byDay, byMonthDay);
    }

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
            Start = start,
            Interval = interval,
            ByDay = byDay,
            ByMonthDay = byMonthDay,
            Until = until,
            Count = count,
        };
    }

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
